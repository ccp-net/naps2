using System.ComponentModel;
using System.Runtime.InteropServices;
using NAPS2.Platform.Windows;

namespace NAPS2.Unmanaged;

internal class NativeLibrary
{
    private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;
    private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    public static string FindLibraryPath(string libraryName, string? baseFolder = null) =>
        FindPath(libraryName, baseFolder, PlatformCompat.System.LibrarySearchPaths);

    public static string FindExePath(string exeName, string? baseFolder = null) =>
        FindPath(exeName, baseFolder, PlatformCompat.System.ExeSearchPaths);

    private static string FindPath(string libraryName, string? baseFolder, string[] systemSearchPaths)
    {
        var baseFolders = !string.IsNullOrWhiteSpace(baseFolder)
            ? new[] { baseFolder, Path.Combine(baseFolder, "lib") }
            : new[] { AssemblyHelper.LibFolder, AssemblyHelper.EntryFolder };
        foreach (var actualBaseFolder in baseFolders)
        {
            foreach (var searchPath in systemSearchPaths)
            {
                var path = Path.Combine(actualBaseFolder, searchPath, libraryName);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        if (baseFolder != null)
        {
            throw new Exception($"Could not find '{libraryName}' in '{baseFolder}'");
        }
        return libraryName;
    }

    private readonly Dictionary<Type, object> _funcCache = new();
    private readonly Lazy<IntPtr> _libraryHandle;

    public NativeLibrary(string libraryPath, string[]? depPaths = null)
    {
        LibraryPath = libraryPath;
        _libraryHandle = new Lazy<IntPtr>(() =>
        {
            if (depPaths != null)
            {
                foreach (var depPath in depPaths)
                {
                    DoLoadLibrary(depPath);
                }
            }
            return DoLoadLibrary(libraryPath);
        });
    }

    private static IntPtr DoLoadLibrary(string path)
    {
        if (PlatformCompat.System.CanUseWin32)
        {
            // Pdfium and several scanner libraries have native dependencies. LoadLibraryEx with
            // LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR makes Windows search the folder containing the target DLL for its
            // sibling dependencies, while DEFAULT_DIRS still includes trusted system locations such as System32.
            // This is more reliable than changing the process-wide DLL directory and then calling LoadLibrary.
            if (Path.IsPathRooted(path))
            {
                var handle = LoadLibraryExW(path, IntPtr.Zero,
                    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
                if (handle != IntPtr.Zero)
                {
                    return handle;
                }

                var firstError = Marshal.GetLastWin32Error();

                // Fallback for native libraries built with older dependency-loading assumptions.
                handle = LoadLibraryExW(path, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
                if (handle != IntPtr.Zero)
                {
                    return handle;
                }

                var secondError = Marshal.GetLastWin32Error();
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Win32.SetDllDirectory(directory);
                }

                handle = LoadLibraryW(path);
                if (handle != IntPtr.Zero)
                {
                    return handle;
                }

                var finalError = Marshal.GetLastWin32Error();
                var error = finalError != 0 ? finalError : secondError != 0 ? secondError : firstError;
                var message = error != 0 ? new Win32Exception(error).Message : "Unknown Windows loader error";
                throw new Exception($"Could not load library: \"{path}\". Win32 error {error}: {message}");
            }

            var winHandle = LoadLibraryW(path);
            if (winHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                var message = error != 0 ? new Win32Exception(error).Message : "Unknown Windows loader error";
                throw new Exception($"Could not load library: \"{path}\". Win32 error {error}: {message}");
            }
            return winHandle;
        }

        var handleFallback = PlatformCompat.System.LoadLibrary(path);
        if (handleFallback == IntPtr.Zero)
        {
            var error = PlatformCompat.System.GetLoadError();
            throw new Exception($"Could not load library: \"{path}\". Error: {error}");
        }
        return handleFallback;
    }

    public string LibraryPath { get; }

    public IntPtr LibraryHandle => _libraryHandle.Value;

    public T Load<T>()
    {
        return (T) _funcCache.Get(typeof(T), () => Marshal.GetDelegateForFunctionPointer<T>(LoadFunc<T>())!);
    }

    private IntPtr LoadFunc<T>()
    {
        var symbol = typeof(T).Name.Replace("_delegate", "");
        var ptr = PlatformCompat.System.LoadSymbol(LibraryHandle, symbol);
        if (ptr == IntPtr.Zero)
        {
            var error = PlatformCompat.System.GetLoadError();
            throw new InvalidOperationException($"Could not load symbol: \"{symbol}\". Error: {error}");
        }
        return ptr;
    }
}
