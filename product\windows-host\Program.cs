namespace ControllerBridge.Host;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Contains("--self-test")) return await SelfTests.RunAsync();
            var languageArg = Argument(args, "--lang");
            if (languageArg is not null && languageArg is not ("en" or "pt"))
                throw new ArgumentException("--lang accepts only en or pt.");
            var english = languageArg is "en" || (languageArg is null && !System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase));
            if (args.Contains("--help")) { PrintHelp(english); return 0; }
            string Tr(string pt, string en) => english ? en : pt;
            var diagnostic = args.Contains("--diagnostic");
            var assemblyPath = Argument(args, "--vigem-client") ?? Path.Combine(AppContext.BaseDirectory, "Nefarius.ViGEm.Client.dll");
            var profileDirectory = Argument(args, "--profiles") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerBridge", "profiles");
            var port = int.Parse(Argument(args, "--port") ?? "47652");
            if (port is < 1 or > 65535) throw new ArgumentException(Tr("A porta precisa estar entre 1 e 65535.", "Port must be between 1 and 65535."));
            if (!diagnostic && !File.Exists(assemblyPath))
                throw new FileNotFoundException(Tr("Coloque Nefarius.ViGEm.Client.dll ao lado do executável, informe --vigem-client <path> ou use --diagnostic.", "Place Nefarius.ViGEm.Client.dll beside the executable, pass --vigem-client <path>, or use --diagnostic."), assemblyPath);
            IOutputFactory factory = diagnostic ? new DiagnosticOutputFactory(Console.WriteLine) : new ViGEmOutputFactory(assemblyPath);
            using var outputs = new OutputPool(factory, diagnostic ? null : new WindowsXInputSlotProbe());
            if (args.Contains("--probe"))
            {
                var failed = false;
                foreach (var kind in new[] { OutputKind.Xbox360, OutputKind.Ds4 })
                {
                    try
                    {
                        using var output = outputs.Create("probe", kind);
                        output.Neutralize();
                        Console.WriteLine(Tr($"Saída {kind} criada, neutralizada e liberada.", $"{kind} output created, neutralized and released."));
                    }
                    catch (Exception error) { failed = true; Console.Error.WriteLine(Tr($"Saída {kind} indisponível: {error.GetBaseException().Message}", $"{kind} output unavailable: {error.GetBaseException().Message}")); }
                }
                return failed ? 1 : 0;
            }
            Console.WriteLine(diagnostic ? Tr("Controller Bridge — diagnóstico (sem saída virtual).", "Controller Bridge — diagnostics (no virtual output).") : Tr("Controller Bridge — saída virtual ativa.", "Controller Bridge — virtual output active."));
            Console.WriteLine(Tr("Mantenha o app Android aberto. Pressione Ctrl+C para encerrar.", "Keep the Android app open. Press Ctrl+C to exit."));
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            await new HostServer(outputs, new ProfileStore(profileDirectory), message => Console.WriteLine(LocalizeLog(message, english))).RunAsync(port, cancellation.Token);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetBaseException().Message);
            return 1;
        }
    }
    private static string? Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--")) throw new ArgumentException($"Falta o valor de {name}.");
        return args[index + 1];
    }
    private static string LocalizeLog(string message, bool english)
    {
        if (!english) return message;
        if (message.StartsWith("Ouvindo USB/ADB em 127.0.0.1:", StringComparison.Ordinal)) return message.Replace("Ouvindo USB/ADB em", "Listening for USB/ADB on");
        if (message == "Celular conectado ao receptor.") return "Phone connected to the host.";
        if (message.StartsWith("Conexão encerrada:", StringComparison.Ordinal)) return message.Replace("Conexão encerrada:", "Connection closed:");
        if (message == "Celular desconectado; controles liberados.") return "Phone disconnected; controllers released.";
        return message;
    }
    private static void PrintHelp(bool english)
    {
        Console.WriteLine(english
            ? "ControllerBridge.exe [--lang en|pt] [--diagnostic] [--vigem-client path] [--profiles folder] [--port 47652]\n  --probe      Create and release Xbox 360 and DS4 outputs.\n  --self-test  Run protocol and lifecycle checks.\nThis release accepts local USB/ADB connections only."
            : "ControllerBridge.exe [--lang en|pt] [--diagnostic] [--vigem-client caminho] [--profiles pasta] [--port 47652]\n  --probe      Cria e libera saídas Xbox 360 e DS4.\n  --self-test  Executa verificações de protocolo e ciclo de vida.\nEsta versão aceita apenas conexões locais por USB/ADB.");
    }
}
