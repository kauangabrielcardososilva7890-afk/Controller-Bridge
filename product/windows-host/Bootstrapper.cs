using System.Diagnostics;

namespace ControllerBridge.Host;

public static class Bootstrapper
{
    public static async Task<bool> PrepareAsync(bool wifi, bool diagnostic, bool english)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Bootstrap", "Prepare.ps1");
        if (!File.Exists(script)) return true; // Source/developer builds keep their existing startup.
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell) { UseShellExecute = false };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Mode", wifi ? "WiFi" : "USB", "-Language", english ? "en" : "pt" }) start.ArgumentList.Add(argument);
        if (diagnostic) start.ArgumentList.Add("-Diagnostic");
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start setup.");
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }
}
