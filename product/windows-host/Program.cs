using System.Windows.Forms;

namespace ControllerBridge.Host;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--self-test")) return SelfTests.RunAsync().GetAwaiter().GetResult();
            var languageArg = Argument(args, "--lang");
            var english = HostLanguage.Resolve(languageArg) == "en";
            if (args.Contains("--version"))
            {
                Console.WriteLine("Controller Bridge " + typeof(Program).Assembly.GetName().Version!.ToString(3));
                return 0;
            }
            if (args.Contains("--help")) { PrintHelp(english); return 0; }
            string Tr(string pt, string en) => english ? en : pt;
            var diagnostic = args.Contains("--diagnostic");
            var packaged = File.Exists(Path.Combine(AppContext.BaseDirectory, "Bootstrap", "Prepare.ps1"));
            var wifiMode = StartupMode.UseWifi(args, english, packaged);
            // Keep the entry-point thread on its original STA until Application.Run starts.
            // Awaiting setup here (before a WinForms synchronization context/message loop exists)
            // resumes Main on a pool MTA thread, which breaks Clipboard and other OLE APIs.
            if (!args.Contains("--probe") && !args.Contains("--prepared") && !Bootstrapper.PrepareAsync(wifiMode, diagnostic, english).GetAwaiter().GetResult()) return 1;
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
            WifiListenerOptions? wifi = null;
            string[] localIps = [];
            if (wifiMode)
            {
                var wifiPort = int.Parse(Argument(args, "--wifi-port") ?? "47653");
                if (wifiPort is < 1 or > 65535 || wifiPort == port) throw new ArgumentException(Tr("A porta Wi-Fi precisa ser válida e diferente da porta USB.", "Wi-Fi port must be valid and differ from the USB port."));
                if (!System.Net.IPAddress.TryParse(Argument(args, "--wifi-bind") ?? "0.0.0.0", out var bindAddress)) throw new ArgumentException(Tr("Endereço Wi-Fi inválido.", "Invalid Wi-Fi bind address."));
                var pairingPath = Argument(args, "--pairing-file") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerBridge", "pairing.key");
                var pairingKey = WifiPairing.LoadOrCreate(pairingPath);
                wifi = new(bindAddress, wifiPort, pairingKey);
                Console.WriteLine(Tr($"Código de pareamento Wi-Fi: {Convert.ToHexString(pairingKey).ToLowerInvariant()}", $"Wi-Fi pairing code: {Convert.ToHexString(pairingKey).ToLowerInvariant()}"));
                Console.WriteLine(Tr($"No Android, informe o IP deste PC, porta {wifiPort} e o código acima. Mantenha esse código privado.", $"On Android, enter this PC's LAN IP, port {wifiPort}, and the code above. Keep this code private."));
                try
                {
                    var addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                        .Where(adapter => adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                        .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses
                            .Where(address => address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(address.Address))
                            .Select(address => new { adapter.Name, adapter.NetworkInterfaceType, Address = address.Address.ToString() }))
                        .DistinctBy(item => item.Address)
                        .OrderBy(item => item.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ? 0 : item.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet ? 1 : 2)
                        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                    localIps = addresses.Select(item => item.Address).ToArray();
                    foreach (var address in addresses) Console.WriteLine(Tr($"IP para conectar no celular ({address.Name}): {address.Address}", $"IP to enter on the phone ({address.Name}): {address.Address}"));
                    if (addresses.Length == 0) Console.WriteLine(Tr("Nenhum IP local ativo encontrado. Conecte o PC à mesma rede do celular.", "No active LAN IPv4 found. Connect this PC to the same network as the phone."));
                }
                catch (System.Net.NetworkInformation.NetworkInformationException)
                { Console.WriteLine(Tr("Consulte o endereço IPv4 deste PC nas configurações de rede do Windows.", "Find this PC's IPv4 address in Windows network settings.")); }
                Console.WriteLine(Tr($"USE NO CELULAR: modo Wi-Fi, um dos IPs acima, porta {wifiPort} e o código de pareamento.", $"ON THE PHONE: choose Wi-Fi, enter one of the IPs above, port {wifiPort}, and the pairing code."));
            }
            else Console.WriteLine(Tr("Conexão USB/ADB ativa em 127.0.0.1:47652; IP não é necessário no celular.", "USB/ADB is active on 127.0.0.1:47652; no IP is needed on the phone."));
            Console.WriteLine(diagnostic ? Tr("STATUS: ATIVO — aguardando o celular (diagnóstico, sem saída virtual).", "STATUS: ACTIVE — waiting for the phone (diagnostics, no virtual output).") : Tr("STATUS: ATIVO — aguardando o app do celular.", "STATUS: ACTIVE — waiting for the phone app."));
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.WriteLine(Tr("O painel Controller Bridge será aberto. Digite stop ou parar aqui para encerrar.", "The Controller Bridge dashboard will open. Type stop or parar here to stop."));
            Console.Write("> ");
            using var host = new HostServer(outputs, new ProfileStore(profileDirectory), message => Console.WriteLine(LocalizeLog(message, english)));
            var serverTask = host.RunAsync(port, cancellation.Token, wifi);
            ApplicationConfiguration.Initialize();
            using var dashboard = new DashboardForm(english, host, localIps, wifiMode, wifi?.Port ?? 47653,
                wifi == null ? "—" : Convert.ToHexString(wifi.PairingKey).ToLowerInvariant(), cancellation.Cancel);
            var commandReader = Task.Run(() =>
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var command = Console.ReadLine();
                    if (command == null) return;
                    if (command.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase) || command.Trim().Equals("parar", StringComparison.OrdinalIgnoreCase))
                    {
                        cancellation.Cancel();
                        if (dashboard.IsHandleCreated && !dashboard.IsDisposed) dashboard.BeginInvoke(new Action(dashboard.Close));
                        return;
                    }
                    Console.WriteLine(Tr("Digite stop ou parar para encerrar.", "Type stop or parar to stop."));
                    Console.Write("> ");
                }
            });
            _ = serverTask.ContinueWith(task =>
            {
                if (task.Exception != null)
                {
                    Console.Error.WriteLine(task.Exception.GetBaseException().Message);
                    if (dashboard.IsHandleCreated && !dashboard.IsDisposed) dashboard.BeginInvoke(new Action(dashboard.Close));
                }
            }, TaskScheduler.Default);
            Application.Run(dashboard);
            cancellation.Cancel();
            try { serverTask.GetAwaiter().GetResult(); }
            finally { _ = commandReader; }
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
        if (message.StartsWith("Ouvindo Wi-Fi autenticado em ", StringComparison.Ordinal)) return message.Replace("Ouvindo Wi-Fi autenticado em ", "Listening for authenticated Wi-Fi on ");
        if (message == "Autenticação Wi-Fi recusada; conexão fechada.") return "Wi-Fi authentication rejected; connection closed.";
        if (message == "Autenticação Wi-Fi concluída.") return "Wi-Fi authentication completed.";
        if (message.StartsWith("Servidor local de movimento ativo em ", StringComparison.Ordinal)) return message.Replace("Servidor local de movimento ativo em ", "Local motion server listening on ");
        if (message.StartsWith("Servidor local de movimento indisponível: ", StringComparison.Ordinal)) return message.Replace("Servidor local de movimento indisponível: ", "Local motion server unavailable: ");
        return message;
    }
    private static void PrintHelp(bool english)
    {
        Console.WriteLine(english
            ? "ControllerBridge.exe [--usb|--wifi] [--lang en|pt] [--diagnostic] [--vigem-client path] [--profiles folder] [--port 47652]\n  Default: USB/ADB on loopback port 47652; press Enter to choose USB or type 2 for Wi-Fi.\n  Wi-Fi uses port 47653 and displays the PC IP and pairing code in the dashboard.\n  --usb / --wifi   Select a connection directly without the startup prompt.\n  Type stop or parar in the console, or close the dashboard, to stop the host.\n  --probe      Create and release Xbox 360 and DS4 outputs.\n  --self-test  Run protocol, authentication and lifecycle checks.\n  --version    Show the application version."
            : "ControllerBridge.exe [--usb|--wifi] [--lang en|pt] [--diagnostic] [--vigem-client caminho] [--profiles pasta] [--port 47652]\n  Padrão: USB/ADB em 127.0.0.1:47652; pressione Enter para USB ou digite 2 para Wi-Fi.\n  Wi-Fi usa a porta 47653 e mostra o IP do PC e o código no painel.\n  --usb / --wifi   Escolhe a conexão diretamente, sem a pergunta inicial.\n  Digite stop ou parar no console, ou feche o painel, para encerrar.\n  --probe      Cria e libera saídas Xbox 360 e DS4.\n  --self-test  Executa verificações de protocolo, autenticação e ciclo de vida.\n  --version    Mostra a versão do aplicativo.");
    }
}
