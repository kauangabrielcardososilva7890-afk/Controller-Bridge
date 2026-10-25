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
        if (!packaged || Console.IsInputRedirected) return true;

        Console.WriteLine(english ? "Choose connection mode:" : "Escolha o modo de conexão:");
        Console.WriteLine(english ? "  1. Wi-Fi (recommended)" : "  1. Wi-Fi (recomendado)");
        Console.WriteLine(english ? "  2. USB cable" : "  2. Cabo USB");
        Console.Write(english ? "Type 1 or 2 [1]: " : "Digite 1 ou 2 [1]: ");
        var choice = Console.ReadLine()?.Trim();
        if (choice is "" or "1" or null) return true;
        if (choice == "2") return false;
        throw new ArgumentException(english ? "Invalid choice. Run ControllerBridge.exe again and select 1 or 2." : "Opção inválida. Abra ControllerBridge.exe novamente e escolha 1 ou 2.");
    }
}
