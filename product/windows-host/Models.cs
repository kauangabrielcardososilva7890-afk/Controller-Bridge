using System.Text.Json;

namespace ControllerBridge.Host;

public enum OutputKind { Xbox360, Ds4 }

public sealed record MotionVector(double X, double Y, double Z);
public sealed record MotionState(MotionVector GyroRadiansPerSecond, MotionVector AccelerationMetersPerSecondSquared, long TimestampNanoseconds)
{
    public static MotionState? Parse(JsonElement message)
    {
        if (!message.TryGetProperty("motion", out var motion) || motion.ValueKind == JsonValueKind.Null) return null;
        if (motion.ValueKind != JsonValueKind.Object) throw new ProtocolException("invalid_motion", "Movimento precisa ser um objeto JSON.");
        if (!motion.TryGetProperty("timestamp_ns", out var timestamp) || timestamp.ValueKind != JsonValueKind.Number || !timestamp.TryGetInt64(out var nanoseconds) || nanoseconds < 0)
            throw new ProtocolException("invalid_motion", "Timestamp do sensor inválido.");
        return new(ParseVector(motion, "gyro_rad_s"), ParseVector(motion, "accel_m_s2"), nanoseconds);
    }
    private static MotionVector ParseVector(JsonElement motion, string name)
    {
        if (!motion.TryGetProperty(name, out var vector) || vector.ValueKind != JsonValueKind.Array || vector.GetArrayLength() != 3)
            throw new ProtocolException("invalid_motion", $"{name} precisa ter três valores.");
        var values = vector.EnumerateArray().ToArray();
        var numbers = new double[3];
        for (var index = 0; index < 3; index++)
            if (values[index].ValueKind != JsonValueKind.Number || !values[index].TryGetDouble(out numbers[index]) || !double.IsFinite(numbers[index]))
                throw new ProtocolException("invalid_motion", $"{name} contém um valor inválido.");
        return new(numbers[0], numbers[1], numbers[2]);
    }
}

public sealed record GamepadState(IReadOnlyDictionary<string, bool> Buttons, IReadOnlyDictionary<string, float> Axes, MotionState? Motion = null)
{
    public static readonly string[] ButtonNames = ["south", "east", "west", "north", "l1", "r1", "l2", "r2", "share", "options", "l3", "r3", "ps", "dpad_up", "dpad_down", "dpad_left", "dpad_right"];
    public static readonly string[] AxisNames = ["lx", "ly", "rx", "ry", "lt", "rt"];
    public static GamepadState Neutral() => new(ButtonNames.ToDictionary(x => x, _ => false), AxisNames.ToDictionary(x => x, _ => 0f));
    public bool Button(string name) => Buttons.TryGetValue(name, out var value) && value;
    public float Axis(string name) => Axes.TryGetValue(name, out var value) ? value : 0;
    public bool IsNeutral => Buttons.Values.All(x => !x) && Axes.Values.All(x => x == 0);

    public static GamepadState Parse(JsonElement message)
    {
        if (!message.TryGetProperty("buttons", out var buttons) || buttons.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("axes", out var axes) || axes.ValueKind != JsonValueKind.Object)
            throw new ProtocolException("invalid_state", "O estado precisa conter botões e eixos completos.");
        var parsedButtons = new Dictionary<string, bool>();
        foreach (var name in ButtonNames)
        {
            if (!buttons.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ProtocolException("invalid_state", $"Botão inválido ou ausente: {name}.");
            parsedButtons[name] = value.GetBoolean();
        }
        var parsedAxes = new Dictionary<string, float>();
        foreach (var name in AxisNames)
        {
            if (!axes.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetSingle(out var number) || !float.IsFinite(number))
                throw new ProtocolException("invalid_state", $"Eixo inválido ou ausente: {name}.");
            parsedAxes[name] = Math.Clamp(number, name is "lt" or "rt" ? 0f : -1f, 1f);
        }
        return new(parsedButtons, parsedAxes, MotionState.Parse(message));
    }
}

public sealed record ControllerProfile(int Revision, string Output, JsonElement Mapping, bool GyroEnabled = false)
{
    public static ControllerProfile Default() => new(1, "xbox360", JsonSerializer.SerializeToElement(new { }));
    public OutputKind Kind => Output switch
    {
        "xbox360" => OutputKind.Xbox360,
        "ds4" => OutputKind.Ds4,
        _ => throw new ProtocolException("invalid_output", "Escolha xbox360 ou ds4 como saída.")
    };
}

public sealed record ControllerSnapshot(string SessionId, string ControllerId, string Name, string Output, bool GyroEnabled, int Revision, bool Connected, GamepadState State, JsonElement? Mapping = null);

public sealed class ProtocolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class JsonFields
{
    public static bool OptionalBoolean(this JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var field)) return false;
        if (field.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ProtocolException("invalid_message", $"Campo booleano inválido: {name}.");
        return field.GetBoolean();
    }
    public static string RequiredString(this JsonElement element, string name, int maxLength = 512)
    {
        if (!element.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(field.GetString()) || field.GetString()!.Length > maxLength)
            throw new ProtocolException("invalid_message", $"Campo inválido: {name}.");
        return field.GetString()!;
    }
    public static long RequiredSequence(this JsonElement element)
    {
        if (!element.TryGetProperty("seq", out var seq) || seq.ValueKind != JsonValueKind.Number || !seq.TryGetInt64(out var value) || value < 0)
            throw new ProtocolException("invalid_state", "Número de sequência inválido.");
        return value;
    }
}
