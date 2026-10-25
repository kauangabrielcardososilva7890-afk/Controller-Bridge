using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControllerBridge.Host;

public sealed class ProfileStore(string directory)
{
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private string PathFor(string profileId) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId))) + ".json");

    public ControllerProfile Load(string profileId)
    {
        lock (_gate)
        {
            var path = PathFor(profileId);
            if (!File.Exists(path)) return ControllerProfile.Default();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (root.RequiredString("profileId", 128) != profileId) throw new InvalidDataException();
                var profile = new ControllerProfile(root.GetProperty("revision").GetInt32(), root.RequiredString("output"), root.GetProperty("mapping").Clone(), root.OptionalBoolean("gyroEnabled"));
                _ = profile.Kind;
                if (profile.Revision < 1 || profile.Mapping.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
                return profile;
            }
            catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or InvalidDataException or ProtocolException or FormatException)
            {
                throw new ProtocolException("profile_corrupt", "O perfil salvo está inválido. Faça uma cópia de segurança e restaure o perfil.");
            }
        }
    }

    public void Save(string profileId, string descriptor, ControllerProfile profile)
    {
        lock (_gate)
        {
            if (profile.Revision <= Load(profileId).Revision)
                throw new ProtocolException("stale_revision", "Outro controle ou sessão já salvou uma revisão mais recente deste perfil.");
            Directory.CreateDirectory(directory);
            var path = PathFor(profileId);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { profileId, descriptor, profile.Revision, profile.Output, profile.Mapping, profile.GyroEnabled }, JsonOptions), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
    }
}
