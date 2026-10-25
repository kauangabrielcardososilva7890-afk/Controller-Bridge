namespace ControllerBridge.Host;

public static class StartupMode
{
    public static bool UseWifi(string[] args, bool english, bool packaged)
    {
        var wifi = args.Contains("--wifi", StringComparer.Ordinal);
        var usb = args.Contains("--usb", StringComparer.Ordinal);
        if (wifi && usb) throw new ArgumentException("Choose either --usb or --wifi, not both.");
        if (wifi) return true;
        if (usb) return false;
        if (args.Contains("--diagnostic", StringComparer.Ordinal)) return false;
        if (!packaged || args.Contains("--prepared", StringComparer.Ordinal) || args.Contains("--probe", StringComparer.Ordinal)) return true;
        Console.WriteLine(english
            ? "Choose the phone connection: [1] USB cable (default)  [2] Wi-Fi"
            : "Escolha a conexão do celular: [1] cabo USB (padrão)  [2] Wi-Fi");
        while (true)
        {
            Console.Write(english ? "Connection [1/2]: " : "Conexão [1/2]: ");
            var choice = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(choice) || choice == "1" || choice.Equals("usb", StringComparison.OrdinalIgnoreCase)) return false;
            if (choice == "2" || choice.Equals("wifi", StringComparison.OrdinalIgnoreCase) || choice.Equals("wi-fi", StringComparison.OrdinalIgnoreCase)) return true;
            Console.WriteLine(english ? "Enter 1 for USB or 2 for Wi-Fi." : "Digite 1 para USB ou 2 para Wi-Fi.");
        }
    }
}
