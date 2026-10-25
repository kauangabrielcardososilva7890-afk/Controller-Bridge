namespace ControllerBridge.Host;

public static class ApplicationPaths
{
    public static string BaseDirectory => ResolveBaseDirectory(Environment.ProcessPath, AppContext.BaseDirectory);

    public static string ResolveBaseDirectory(string? processPath, string fallbackDirectory)
    {
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var executableDirectory = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(executableDirectory)) return executableDirectory;
        }

        return fallbackDirectory;
    }
}
