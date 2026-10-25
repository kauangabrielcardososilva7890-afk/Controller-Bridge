using System.Reflection;

namespace ControllerBridge.Host;

public interface IControllerOutput : IDisposable
{
    void Apply(GamepadState state);
    void Neutralize();
}

public interface IOutputFactory : IDisposable
{
    IControllerOutput Create(string id, OutputKind kind);
}

public sealed class OutputPool(IOutputFactory factory, IXInputSlotProbe? slotProbe = null) : IDisposable
{
    private readonly object _gate = new();
    private int _xboxCount;
    public IControllerOutput Create(string id, OutputKind kind)
    {
        lock (_gate)
        {
            if (kind == OutputKind.Xbox360 && _xboxCount >= 4)
                throw new ProtocolException("output_limit", "O limite de quatro saídas Xbox foi atingido.");
            uint previousSlots = 0;
            if (kind == OutputKind.Xbox360 && slotProbe != null)
            {
                try { previousSlots = slotProbe.ConnectedMask(); }
                catch (Exception error) { throw new ProtocolException("output_unavailable", $"Não foi possível consultar as posições XInput: {error.Message}"); }
                if ((previousSlots & 0xFu) == 0xFu)
                    throw new ProtocolException("output_limit", "As quatro posições XInput do Windows já estão ocupadas. Desative um controle Xbox ou escolha saída DS4.");
            }
            IControllerOutput output;
            try { output = factory.Create(id, kind); }
            catch (Exception error) { throw new ProtocolException("output_unavailable", $"Não foi possível criar a saída {kind}: {error.GetBaseException().Message}"); }
            if (kind == OutputKind.Xbox360 && slotProbe != null)
            {
                try
                {
                    if (!slotProbe.WaitForNewSlot(previousSlots, TimeSpan.FromMilliseconds(750)))
                        throw new ProtocolException("output_unavailable", "O controle virtual Xbox não apareceu em uma posição XInput do Windows. Escolha DS4 ou libere uma posição Xbox.");
                }
                catch (Exception error)
                {
                    try { output.Dispose(); } catch { }
                    if (error is ProtocolException) throw;
                    throw new ProtocolException("output_unavailable", $"Não foi possível confirmar a posição XInput da saída: {error.GetBaseException().Message}");
                }
            }
            if (kind == OutputKind.Xbox360) _xboxCount++;
            return new PooledOutput(output, () => { lock (_gate) { if (kind == OutputKind.Xbox360) _xboxCount--; } });
        }
    }
    public void Dispose() => factory.Dispose();
    private sealed class PooledOutput(IControllerOutput inner, Action release) : IControllerOutput
    {
        private bool _disposed;
        public void Apply(GamepadState state) => inner.Apply(state);
        public void Neutralize() => inner.Neutralize();
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { inner.Dispose(); } finally { release(); }
        }
    }
}

public sealed class DiagnosticOutputFactory(Action<string> log) : IOutputFactory
{
    public IControllerOutput Create(string id, OutputKind kind) => new DiagnosticOutput(id, kind, log);
    public void Dispose() { }
    private sealed class DiagnosticOutput(string id, OutputKind kind, Action<string> log) : IControllerOutput
    {
        private string _last = "";
        public void Apply(GamepadState state)
        {
            var value = $"[{id}/{kind}] botões={string.Join(',', state.Buttons.Where(x => x.Value).Select(x => x.Key))} eixos={string.Join(' ', state.Axes.Select(x => $"{x.Key}:{x.Value:F2}"))}";
            if (value != _last) { log(value); _last = value; }
        }
        public void Neutralize() => Apply(GamepadState.Neutral());
        public void Dispose() => Neutralize();
    }
}

public static class AxisProjection
{
    public static short XboxStick(float value) => (short)Math.Round(Math.Clamp(value, -1, 1) * (value < 0 ? 32768 : 32767));
    public static byte Ds4Stick(float value) => (byte)Math.Clamp((int)Math.Round((Math.Clamp(value, -1, 1) + 1) * 127.5), 0, 255);
    public static byte Trigger(float value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    public static float TriggerValue(GamepadState state, string axis, string button) => state.Axis(axis) > 0 ? state.Axis(axis) : state.Button(button) ? 1 : 0;
    public static string Dpad(GamepadState state)
    {
        var x = (state.Button("dpad_right") ? 1 : 0) - (state.Button("dpad_left") ? 1 : 0);
        var y = (state.Button("dpad_down") ? 1 : 0) - (state.Button("dpad_up") ? 1 : 0);
        return (x, y) switch { (0,-1) => "North", (1,-1) => "Northeast", (1,0) => "East", (1,1) => "Southeast", (0,1) => "South", (-1,1) => "Southwest", (-1,0) => "West", (-1,-1) => "Northwest", _ => "None" };
    }
}

// Loads the user's separately installed ViGEm client library.
public sealed class ViGEmOutputFactory : IOutputFactory
{
    private readonly Assembly _assembly;
    private readonly object _client;
    public ViGEmOutputFactory(string assemblyPath)
    {
        _assembly = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        _client = Activator.CreateInstance(_assembly.GetType("Nefarius.ViGEm.Client.ViGEmClient", true)!)!;
    }
    public IControllerOutput Create(string id, OutputKind kind)
    {
        var method = kind == OutputKind.Xbox360 ? "CreateXbox360Controller" : "CreateDualShock4Controller";
        var controller = _client.GetType().GetMethod(method, Type.EmptyTypes)!.Invoke(_client, null)!;
        try { return new ViGEmOutput(_assembly, controller, kind); }
        catch { (controller as IDisposable)?.Dispose(); throw; }
    }
    public void Dispose() => (_client as IDisposable)?.Dispose();

    private sealed class ViGEmOutput : IControllerOutput
    {
        private readonly Assembly _assembly;
        private readonly object _controller;
        private readonly Type _common;
        private readonly Type _typed;
        private readonly string _prefix;
        private readonly OutputKind _kind;
        private bool _disposed;
        private byte _ds4PacketCounter;
        public ViGEmOutput(Assembly assembly, object controller, OutputKind kind)
        {
            _assembly = assembly; _controller = controller; _kind = kind;
            _common = assembly.GetType("Nefarius.ViGEm.Client.IVirtualGamepad", true)!;
            _typed = assembly.GetType(kind == OutputKind.Xbox360 ? "Nefarius.ViGEm.Client.Targets.IXbox360Controller" : "Nefarius.ViGEm.Client.Targets.IDualShock4Controller", true)!;
            _prefix = kind == OutputKind.Xbox360 ? "Nefarius.ViGEm.Client.Targets.Xbox360.Xbox360" : "Nefarius.ViGEm.Client.Targets.DualShock4.DualShock4";
            _common.GetProperty("AutoSubmitReport")!.SetValue(controller, false);
            CallCommon("Connect");
            try
            {
                Neutralize();
            }
            catch { try { CallCommon("Disconnect"); } catch { } throw; }
        }
        private void CallCommon(string name) => _common.GetMethod(name)!.Invoke(_controller, null);
        private object Constant(string group, string name) => _assembly.GetType(_prefix + group, true)!.GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        private void Set(string method, string group, string name, object value)
        {
            var constant = Constant(group, name);
            _typed.GetMethod(method, [constant.GetType().BaseType!, value.GetType()])!.Invoke(_controller, [constant, value]);
        }
        public void Apply(GamepadState state)
        {
            if (_kind == OutputKind.Ds4)
            {
                var reportTimestamp = (long)(System.Diagnostics.Stopwatch.GetTimestamp() * (1_000_000_000d / System.Diagnostics.Stopwatch.Frequency));
                _typed.GetMethod("SubmitRawReport")!.Invoke(_controller, [Ds4ReportEncoder.Encode(state, _ds4PacketCounter++, reportTimestamp)]);
                return;
            }
            CallCommon("ResetReport");
            if (_kind == OutputKind.Xbox360)
            {
                foreach (var (logical, target) in new (string,string)[] { ("south","A"),("east","B"),("west","X"),("north","Y"),("l1","LeftShoulder"),("r1","RightShoulder"),("share","Back"),("options","Start"),("l3","LeftThumb"),("r3","RightThumb"),("ps","Guide"),("dpad_up","Up"),("dpad_down","Down"),("dpad_left","Left"),("dpad_right","Right") })
                    Set("SetButtonState", "Button", target, state.Button(logical));
                foreach (var (logical,target,invert) in new (string,string,bool)[] { ("lx","LeftThumbX",false),("ly","LeftThumbY",true),("rx","RightThumbX",false),("ry","RightThumbY",true) })
                    Set("SetAxisValue", "Axis", target, AxisProjection.XboxStick(state.Axis(logical) * (invert ? -1 : 1)));
            }
            Set("SetSliderValue", "Slider", "LeftTrigger", AxisProjection.Trigger(AxisProjection.TriggerValue(state, "lt", "l2")));
            Set("SetSliderValue", "Slider", "RightTrigger", AxisProjection.Trigger(AxisProjection.TriggerValue(state, "rt", "r2")));
            CallCommon("SubmitReport");
        }
        public void Neutralize() => Apply(GamepadState.Neutral());
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Neutralize(); } finally { try { CallCommon("Disconnect"); } finally { (_controller as IDisposable)?.Dispose(); } }
        }
    }
}
