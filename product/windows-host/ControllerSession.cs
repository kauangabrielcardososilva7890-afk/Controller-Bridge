using System.Text.Json;
using System.Reflection;

namespace ControllerBridge.Host;

public sealed class ControllerSession(OutputPool outputs, ProfileStore profiles, Action<string> log, Func<DateTimeOffset>? clock = null) : IDisposable
{
    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, Entry> _controllers = new();
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private bool _hello;
    private sealed class Entry(string profileId, string descriptor, string name, ControllerProfile profile, IControllerOutput output, DateTimeOffset now)
    {
        public string ProfileId { get; } = profileId;
        public string Descriptor { get; } = descriptor;
        public string Name { get; } = name;
        public ControllerProfile Profile { get; set; } = profile;
        public IControllerOutput Output { get; set; } = output;
        public long LastSequence { get; set; } = -1;
        public long LastMotionTimestamp { get; set; } = -1;
        public DateTimeOffset LastState { get; set; } = now;
        public bool TimedOut { get; set; }
    }

    public object? ProcessLine(string line, DateTimeOffset? receivedAt = null)
    {
        lock (_gate)
        {
            string? controllerId = null;
            try
            {
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new ProtocolException("invalid_message", "A mensagem precisa ser um objeto JSON.");
                if (root.TryGetProperty("controller_id", out var id) && id.ValueKind == JsonValueKind.String) controllerId = id.GetString();
                if (!root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1)
                    throw new ProtocolException("unsupported_version", "Versão de protocolo não suportada.");
                var type = root.RequiredString("type", 40);
                if (type == "hello")
                {
                    if (_hello) throw new ProtocolException("invalid_message", "A sessão já foi iniciada.");
                    _ = root.RequiredString("client_id", 128);
                    _ = root.RequiredString("app_version", 64);
                    _hello = true;
                    return new { v = 1, type = "welcome", session_id = SessionId, max_controllers = 4, outputs = new[] { "xbox360", "ds4" } };
                }
                if (!_hello) throw new ProtocolException("hello_required", "Inicie a sessão com hello.");
                if (type == "ping") return new { v = 1, type = "pong", seq = root.RequiredSequence() };
                controllerId = root.RequiredString("controller_id", 128);
                return type switch
                {
                    "attach" => Attach(controllerId, root),
                    "state" => ApplyState(controllerId, root, receivedAt ?? _clock()),
                    "profile_update" => UpdateProfile(controllerId, root),
                    "detach" => Detach(controllerId),
                    _ => throw new ProtocolException("unknown_message", "Tipo de mensagem desconhecido.")
                };
            }
            catch (Exception error) when (error is JsonException or ProtocolException)
            {
                if (controllerId != null && _controllers.TryGetValue(controllerId, out var entry)) SafeNeutralize(entry);
                else NeutralizeAll();
                var code = error is ProtocolException protocol ? protocol.Code : "invalid_json";
                return new { v = 1, type = "error", controller_id = controllerId, code, message = error.Message };
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or TargetInvocationException)
            {
                NeutralizeAll();
                log($"Falha na sessão: {error.GetBaseException().Message}");
                return new { v = 1, type = "error", controller_id = controllerId, code = "host_error", message = error.GetBaseException().Message };
            }
        }
    }

    private object Attach(string id, JsonElement root)
    {
        if (_controllers.ContainsKey(id)) throw new ProtocolException("already_attached", "Este controle já está conectado nesta sessão.");
        if (_controllers.Count >= 4) throw new ProtocolException("controller_limit", "Esta sessão aceita até quatro controles.");
        var descriptor = root.RequiredString("descriptor", 1024);
        var profileId = root.RequiredString("profile_id", 128);
        var name = root.RequiredString("name", 256);
        var profile = profiles.Load(profileId);
        var output = outputs.Create(SessionId + "/" + id, profile.Kind);
        _controllers.Add(id, new(profileId, descriptor, name, profile, output, _clock()));
        log($"Controle conectado: {name} ({id}), saída {profile.Output}.");
        return ProfileMessage(id, profileId, profile);
    }

    private object? ApplyState(string id, JsonElement root, DateTimeOffset receivedAt)
    {
        var entry = Find(id);
        var sequence = root.RequiredSequence();
        if (sequence <= entry.LastSequence) throw new ProtocolException("stale_sequence", "Estado duplicado ou fora de ordem.");
        var state = GamepadState.Parse(root);
        if (_clock() - receivedAt >= TimeSpan.FromSeconds(1))
        {
            entry.LastSequence = sequence;
            NeutralizeStale();
            return null;
        }
        if (!entry.Profile.GyroEnabled || entry.Profile.Kind != OutputKind.Ds4) state = state with { Motion = null };
        if (state.Motion != null && state.Motion.TimestampNanoseconds < entry.LastMotionTimestamp)
            throw new ProtocolException("invalid_motion", "Timestamp do sensor fora de ordem.");
        try { entry.Output.Apply(state); }
        catch (Exception error) { throw new ProtocolException("output_unavailable", $"Não foi possível atualizar a saída: {error.GetBaseException().Message}"); }
        entry.LastSequence = sequence;
        if (state.Motion != null) entry.LastMotionTimestamp = state.Motion.TimestampNanoseconds;
        entry.LastState = receivedAt;
        entry.TimedOut = false;
        return null;
    }

    private object UpdateProfile(string id, JsonElement root)
    {
        var entry = Find(id);
        if (root.RequiredString("profile_id", 128) != entry.ProfileId)
            throw new ProtocolException("profile_id_mismatch", "O perfil não corresponde ao controle anunciado nesta sessão.");
        if (!root.TryGetProperty("revision", out var revisionField) || revisionField.ValueKind != JsonValueKind.Number || !revisionField.TryGetInt32(out var revision) || revision <= entry.Profile.Revision)
            throw new ProtocolException("stale_revision", "A revisão precisa ser mais nova que o perfil salvo.");
        if (!root.TryGetProperty("mapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object)
            throw new ProtocolException("invalid_profile", "O mapeamento precisa ser um objeto JSON.");
        var profile = new ControllerProfile(revision, root.RequiredString("output", 16), mapping.Clone(), root.OptionalBoolean("gyro_enabled"));
        _ = profile.Kind;
        try { entry.Output.Neutralize(); }
        catch (Exception error) { throw new ProtocolException("output_unavailable", $"Não foi possível liberar a saída anterior para trocar o perfil: {error.GetBaseException().Message}"); }
        // A kind change is Xbox->DS4 or DS4->Xbox, so allocating first does not consume an extra Xbox slot.
        IControllerOutput? replacement = profile.Kind != entry.Profile.Kind ? outputs.Create(SessionId + "/" + id, profile.Kind) : null;
        try { profiles.Save(entry.ProfileId, entry.Descriptor, profile); }
        catch { replacement?.Dispose(); throw; }
        var former = entry.Output;
        if (replacement != null) entry.Output = replacement;
        entry.Profile = profile;
        entry.LastMotionTimestamp = -1;
        SafeNeutralize(entry);
        if (replacement != null)
            try { former.Dispose(); } catch (Exception error) { log($"Falha ao liberar saída anterior: {error.GetBaseException().Message}"); }
        return new { v = 1, type = "profile_saved", controller_id = id, profile_id = entry.ProfileId, revision = profile.Revision, gyro_enabled = profile.GyroEnabled };
    }

    private Entry Find(string id) => _controllers.TryGetValue(id, out var entry) ? entry : throw new ProtocolException("unknown_controller", "Anuncie o controle com attach antes de enviar comandos.");
    private object? Detach(string id)
    {
        var entry = Find(id);
        _controllers.Remove(id);
        try { entry.Output.Dispose(); } catch (Exception error) { log($"Falha ao liberar {id}: {error.GetBaseException().Message}"); }
        log($"Controle desconectado: {entry.Name} ({id}).");
        return null;
    }
    private static object ProfileMessage(string id, string profileId, ControllerProfile profile) => new { v = 1, type = "profile", controller_id = id, profile_id = profileId, revision = profile.Revision, output = profile.Output, gyro_enabled = profile.GyroEnabled, mapping = profile.Mapping };
    private void SafeNeutralize(Entry entry)
    {
        try { entry.Output.Neutralize(); } catch (Exception error) { log($"Falha ao neutralizar controle: {error.GetBaseException().Message}"); }
    }
    private void NeutralizeAll() { foreach (var entry in _controllers.Values) SafeNeutralize(entry); }
    public void NeutralizeStale()
    {
        lock (_gate)
        {
            foreach (var entry in _controllers.Values)
                if (!entry.TimedOut && _clock() - entry.LastState >= TimeSpan.FromSeconds(1))
                {
                    SafeNeutralize(entry);
                    entry.TimedOut = true;
                    log($"Sem estado recente: {entry.Name}; comandos liberados.");
                }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _controllers.Values)
                try { entry.Output.Dispose(); } catch (Exception error) { log(error.GetBaseException().Message); }
            _controllers.Clear();
        }
    }
}
