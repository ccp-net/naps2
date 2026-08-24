using System.Runtime.InteropServices;
using NAPS2.Platform.Windows;

namespace NAPS2.Unmanaged;

internal class NativeLibrary
{
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
        // On Windows, native libraries such as Pdfium may depend on DLLs placed beside the main DLL. When an absolute
        // library path is supplied, LoadLibrary does not always resolve those sibling dependencies from that directory.
        // Point the Windows DLL search directory at the native library folder before loading it.
        if (PlatformCompat.System.CanUseWin32 && Path.IsPathRooted(path))
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Win32.SetDllDirectory(directory);
            }
        }

        var handle = PlatformCompat.System.LoadLibrary(path);
        if (handle == IntPtr.Zero)
        {
            var error = PlatformCompat.System.GetLoadError();
            throw new Exception($"Could not load library: \"{path}\". Error: {error}");
        }
        return handle;
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
