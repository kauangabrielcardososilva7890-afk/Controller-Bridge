using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

[assembly: AssemblyTitle("Controller Bridge")]
[assembly: AssemblyDescription("Controller Bridge Windows host and controller dashboard")]
[assembly: AssemblyCompany("Controller Bridge")]
[assembly: AssemblyProduct("Controller Bridge")]
[assembly: AssemblyVersion("0.5.0.0")]
[assembly: AssemblyFileVersion("0.5.0.0")]

internal static class ControllerBridgeLauncher
{
    private static int Main(string[] args)
    {
        try
        {
            var root = AppDomain.CurrentDomain.BaseDirectory;
            var runtimeRoot = Path.Combine(root, "runtime");
            var runtimeHost = Path.Combine(runtimeRoot, "dotnet.exe");
            var app = Path.Combine(root, "ControllerBridge.dll");
            if (!File.Exists(runtimeHost) || !File.Exists(app))
            {
                Console.Error.WriteLine("Controller Bridge runtime files are missing. Extract the complete ZIP and try again.");
                return 1;
            }

            var arguments = new StringBuilder(Quote(app));
            foreach (var argument in args) arguments.Append(' ').Append(Quote(argument));
            var start = new ProcessStartInfo(runtimeHost, arguments.ToString())
            {
                UseShellExecute = false,
                WorkingDirectory = root,
                CreateNoWindow = false
            };
            start.EnvironmentVariables["DOTNET_ROOT"] = runtimeRoot;
            start.EnvironmentVariables["DOTNET_ROOT_X64"] = runtimeRoot;
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Could not start the bundled .NET runtime.");
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetBaseException().Message);
            return 1;
        }
    }

    private static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return value;
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"')
            {
                result.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes).Append(character);
            slashes = 0;
        }
        result.Append('\\', slashes * 2).Append('"');
        return result.ToString();
    }
}
