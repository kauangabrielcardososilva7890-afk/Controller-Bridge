using System.Diagnostics;
using System.Text.Json;

namespace ControllerBridge.Host;

public sealed class UsbReverseMonitor(int port, bool english, Action<string> log)
{
    private readonly Dictionary<string, bool> _lastResults = new(StringComparer.Ordinal);
    private readonly string? _preferredSerial = LoadPreferredSerial();
    private string? _sessionSerial;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var adb = FindAdb();
                if (adb != null)
                {
                    var devices = await RunAdbAsync(adb, ["devices", "-l"], cancellationToken);
                    if (devices.ExitCode == 0)
                    {
                        var serials = ParseAuthorizedDevices(devices.Output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
                        foreach (var removed in _lastResults.Keys.Except(serials, StringComparer.Ordinal).ToArray()) _lastResults.Remove(removed);
                        _sessionSerial = RefreshSessionDevice(_sessionSerial, serials, _preferredSerial);
                        var selectedDevices = _sessionSerial != null && serials.Contains(_sessionSerial, StringComparer.Ordinal) ? new[] { _sessionSerial } : Array.Empty<string>();
                        foreach (var serial in selectedDevices)
                        {
                            var reverse = await RunAdbAsync(adb, ["-s", serial, "reverse", $"tcp:{port}", $"tcp:{port}"], cancellationToken);
                            var succeeded = reverse.ExitCode == 0;
                            if (!_lastResults.TryGetValue(serial, out var previous) || previous != succeeded)
                            {
                                log(succeeded
                                    ? english ? $"USB/ADB ready for phone {serial}." : $"USB/ADB pronto para o celular {serial}."
                                    : english ? $"Could not prepare USB/ADB for {serial}: {reverse.Error.Trim()}" : $"Falha ao preparar USB/ADB para {serial}: {reverse.Error.Trim()}");
                            }
                            _lastResults[serial] = succeeded;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                log((english ? "USB/ADB monitor error: " : "Erro no monitor USB/ADB: ") + error.GetBaseException().Message);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
        }
    }

    public static IReadOnlyList<string> ParseAuthorizedDevices(IEnumerable<string> lines)
    {
        var serials = new List<string>();
        foreach (var line in lines)
        {
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && fields[1] == "device" && fields[0] != "List") serials.Add(fields[0]);
        }
        return serials;
    }

    public static string? SelectSessionDevice(IReadOnlyList<string> serials, string? preferredSerial) =>
        preferredSerial != null && serials.Contains(preferredSerial, StringComparer.Ordinal) ? preferredSerial : serials.FirstOrDefault();

    public static string? RefreshSessionDevice(string? currentSerial, IReadOnlyList<string> serials, string? preferredSerial) =>
        currentSerial != null && serials.Contains(currentSerial, StringComparer.Ordinal) ? currentSerial : SelectSessionDevice(serials, preferredSerial);

    private static string? FindAdb()
    {
        var candidates = new List<string>();
        var pathAdb = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, "adb.exe")) ?? [];
        candidates.AddRange(pathAdb);
        foreach (var sdk in new[] { Environment.GetEnvironmentVariable("ANDROID_HOME"), Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk") })
            if (!string.IsNullOrWhiteSpace(sdk)) candidates.Add(Path.Combine(sdk, "platform-tools", "adb.exe"));
        candidates.Add(Path.Combine(ApplicationPaths.BaseDirectory, "platform-tools", "adb.exe"));

        var setupPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerBridge", "setup.json");
        if (File.Exists(setupPath))
        {
            try
            {
                using var setup = JsonDocument.Parse(File.ReadAllText(setupPath));
                if (setup.RootElement.TryGetProperty("adbPath", out var saved) && saved.ValueKind == JsonValueKind.String)
                    candidates.Insert(0, saved.GetString()!);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }

        return candidates.FirstOrDefault(candidate => File.Exists(candidate));
    }

    private static string? LoadPreferredSerial()
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerBridge", "setup.json");
        if (!File.Exists(settingsPath)) return null;
        try
        {
            using var setup = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return setup.RootElement.TryGetProperty("deviceSerial", out var serial) && serial.ValueKind == JsonValueKind.String
                ? serial.GetString()
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAdbAsync(string adb, string[] arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = adb,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!process.Start()) throw new InvalidOperationException("ADB failed to start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        return (process.ExitCode, await output, await error);
    }
}
