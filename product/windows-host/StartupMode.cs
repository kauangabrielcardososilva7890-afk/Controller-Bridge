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
        _ = packaged;
        _ = english;
        return true;
    }
}
