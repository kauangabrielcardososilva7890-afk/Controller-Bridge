using System.Buffers.Binary;

namespace ControllerBridge.Host;

public static class Ds4ReportEncoder
{
    // ViGEm DS4_REPORT_EX is 63 bytes without report ID. Layout:
    // https://github.com/nefarius/ViGEmClient/blob/master/include/ViGEm/Common.h
    // The virtual device advertises fixed calibration feature 0x02:
    // https://github.com/nefarius/ViGEmBus/blob/master/sys/Ds4Pdo.cpp
    public static byte[] Encode(GamepadState state, byte packetCounter = 0, long? reportTimestampNanoseconds = null)
    {
        var report = new byte[63];
        report[0] = AxisProjection.Ds4Stick(state.Axis("lx"));
        report[1] = AxisProjection.Ds4Stick(state.Axis("ly"));
        report[2] = AxisProjection.Ds4Stick(state.Axis("rx"));
        report[3] = AxisProjection.Ds4Stick(state.Axis("ry"));
        ushort buttons = AxisProjection.Dpad(state) switch { "North" => 0, "Northeast" => 1, "East" => 2, "Southeast" => 3, "South" => 4, "Southwest" => 5, "West" => 6, "Northwest" => 7, _ => 8 };
        foreach (var (logical, bit) in new (string, ushort)[] { ("west",0x10),("south",0x20),("east",0x40),("north",0x80),("l1",0x100),("r1",0x200),("share",0x1000),("options",0x2000),("l3",0x4000),("r3",0x8000) })
            if (state.Button(logical)) buttons |= bit;
        if (state.Button("l2") || state.Axis("lt") > .5f) buttons |= 0x400;
        if (state.Button("r2") || state.Axis("rt") > .5f) buttons |= 0x800;
        BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(4, 2), buttons);
        report[6] = (byte)(((packetCounter & 63) << 2) | (state.Button("ps") ? 1 : 0));
        report[7] = AxisProjection.Trigger(AxisProjection.TriggerValue(state, "lt", "l2"));
        report[8] = AxisProjection.Trigger(AxisProjection.TriggerValue(state, "rt", "r2"));
        report[11] = 255;
        report[29] = 0x1A; // Wired, full battery.
        report[32] = 1;
        foreach (var touchOffset in new[] { 33, 42, 51 }) { report[touchOffset + 1] = 0x80; report[touchOffset + 5] = 0x80; }
        // DS4/SDL uses controller-local Y-up. Missing/disabled motion means rest,
        // not free fall: gyro=0, acceleration=(0,+g,0). Controller axis order/sign
        // is preserved, never rotated into phone-screen coordinates.
        var motion = state.Motion ?? new MotionState(new(0, 0, 0), new(0, 9.80665, 0), 0);
        {
            var ns = reportTimestampNanoseconds ?? motion.TimestampNanoseconds;
            // One DS4 timestamp tick is 16/3 microseconds; split division avoids Int64 overflow.
            var ticks = (ns / 16000 * 3) + (ns % 16000 * 3 / 16000);
            BinaryPrimitives.WriteUInt16LittleEndian(report.AsSpan(9, 2), unchecked((ushort)ticks));
            var gyro = motion.GyroRadiansPerSecond;
            var accel = motion.AccelerationMetersPerSecondSquared;
            // Invert the fixed feature-report calibration, so calibrated consumers see SI input.
            Write(report, 12, Gyro(gyro.X, 17676, 1));
            Write(report, 14, Gyro(gyro.Y, 17771, 0));
            Write(report, 16, Gyro(gyro.Z, 17786, 0));
            Write(report, 18, Accel(accel.X, 16209, -297.5));
            Write(report, 20, Accel(accel.Y, 16148, -42));
            Write(report, 22, Accel(accel.Z, 15988, -512));
        }
        return report;
    }
    private static double Gyro(double radiansPerSecond, double range, double bias) => radiansPerSecond * (180 / Math.PI) * 16 * range / 17280 + bias;
    private static double Accel(double metersPerSecondSquared, double range, double bias) => metersPerSecondSquared / 9.80665 * 8192 * range / 16384 + bias;
    private static void Write(byte[] report, int offset, double value) => BinaryPrimitives.WriteInt16LittleEndian(report.AsSpan(offset, 2), (short)Math.Round(Math.Clamp(value, short.MinValue, short.MaxValue)));
}
