using System.Globalization;
using System.Reflection;

namespace ControllerBridge.Host;

public static class HostLanguage
{
    public static string Resolve(string? requested = null) => Resolve(requested,
        typeof(HostLanguage).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(attribute => attribute.Key == "DefaultLanguage")?.Value,
        CultureInfo.CurrentUICulture);

    public static string Resolve(string? requested, string? packagedDefault, CultureInfo windowsCulture)
    {
        if (requested != null)
        {
            if (requested is not ("en" or "pt")) throw new ArgumentException("--lang accepts only en or pt.");
            return requested;
        }
        if (packagedDefault is "pt-BR" or "pt") return "pt";
        if (packagedDefault == "en") return "en";
        return windowsCulture.TwoLetterISOLanguageName.Equals("pt", StringComparison.OrdinalIgnoreCase) ? "pt" : "en";
    }
}

