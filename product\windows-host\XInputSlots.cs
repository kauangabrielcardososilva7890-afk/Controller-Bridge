using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ControllerBridge.Host;

public interface IXInputSlotProbe
{
    uint ConnectedMask();
    bool WaitForNewSlot(uint previousMask, TimeSpan timeout);
}

public sealed class WindowsXInputSlotProbe : IXInputSlotProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint Packet;
        public ushort Buttons;
        public byte LeftTrigger, RightTrigger;
        public short LeftX, LeftY, RightX, RightY;
    }
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint GetState(uint index, out XInputState state);
    public uint ConnectedMask()
    {
        uint mask = 0;
        for (uint index = 0; index < 4; index++) if (GetState(index, out _) == 0) mask |= 1u << (int)index;
        return mask;
    }
    public bool WaitForNewSlot(uint previousMask, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            if ((ConnectedMask() & ~previousMask & 0xFu) != 0) return true;
            Thread.Sleep(10);
        } while (stopwatch.Elapsed < timeout);
        return false;
    }
}
