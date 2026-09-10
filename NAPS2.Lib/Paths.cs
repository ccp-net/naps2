namespace NAPS2;

public static class Paths
{
    private static readonly string ExecutablePath = AssemblyHelper.EntryFolder;
    private static readonly string AppDataPath;
    private static readonly string TempPath;
    private static readonly string TempSubfolderPath;
    private static readonly string RecoveryPath;
    private static readonly string ComponentsPath;

    static Paths()
    {
#if ZIP
        AppDataPath = Path.Combine(ExecutablePath, "..", "Data");
#else
        var userAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
#if NET6_0_OR_GREATER
        // CCP Scan is a distinct Windows product and must not inherit profiles/language/settings from a separately
        // installed NAPS2 instance. Using a dedicated app-data directory also makes deployment defaults predictable.
        var subfolder = OperatingSystem.IsWindows() ? "CCP Scan Ho So Dang Vien" : "naps2";
        if (string.IsNullOrEmpty(userAppData) && OperatingSystem.IsMacOS())
        {
            userAppData = Environment.ExpandEnvironmentVariables("/Users/%USER%/.config");
        }
        else if (OperatingSystem.IsMacOS())
        {
            userAppData = Environment.ExpandEnvironmentVariables("%HOME%/.config");
        }
#else
        var subfolder = "CCP Scan Ho So Dang Vien";
#endif
        AppDataPath = Path.Combine(userAppData, subfolder);
#endif
        var dataPathFromEnv = Environment.GetEnvironmentVariable("NAPS2_TEST_DATA");
        if (!string.IsNullOrEmpty(dataPathFromEnv))
        {
            AppDataPath = dataPathFromEnv;
            IsTestAppDataPath = true;
        }
        var args = Environment.GetCommandLineArgs();
        var flagIndex = Array.IndexOf(args, "/Naps2TestData");
        if (flagIndex >= 0 && flagIndex < args.Length - 1)
        {
            AppDataPath = args[flagIndex + 1];
            IsTestAppDataPath = true;
        }

        TempPath = Path.Combine(AppDataPath, "temp");
        TempSubfolderPath = Path.Combine(TempPath, Path.GetRandomFileName());
        RecoveryPath = Path.Combine(AppDataPath, "recovery");
        ComponentsPath = Path.Combine(AppDataPath, "components");
    }

    public static readonly bool IsTestAppDataPath;

    public static string AppData => EnsureFolderExists(AppDataPath);

    public static string Executable => EnsureFolderExists(ExecutablePath);

    public static string Temp => EnsureFolderExists(TempPath);

    public static string TempSubfolder => EnsureFolderExists(TempSubfolderPath);

    public static string Recovery => EnsureFolderExists(RecoveryPath);

    public static string Components => EnsureFolderExists(ComponentsPath);

    public static void ClearTemp()
    {
        try
        {
            if (!Directory.Exists(TempPath)) return;
            var otherNaps2Processes = Process.GetProcesses().Where(x =>
                x.ProcessName.IndexOf("NAPS2", StringComparison.OrdinalIgnoreCase) >= 0 &&
                x.Id != Process.GetCurrentProcess().Id);
            if (!otherNaps2Processes.Any())
            {
                Directory.Delete(TempPath, true);
                Directory.CreateDirectory(TempPath);
            }
        }
        catch (Exception)
        {
        }
    }

    public static void DeleteTempSubfolder()
    {
        try
        {
            if (!Directory.Exists(TempSubfolderPath)) return;
            Directory.Delete(TempSubfolderPath, true);
        }
        catch (Exception)
        {
        }
    }

    private static string EnsureFolderExists(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        return folderPath;
    }
}
