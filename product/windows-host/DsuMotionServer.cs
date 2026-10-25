using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace ControllerBridge.Host;

/// <summary>
/// Loopback DSU/Cemuhook v1001 server that exposes motion from eligible virtual DS4 profiles.
/// It deliberately reports neutral buttons and sticks; Controller Bridge remains the input path.
/// </summary>
public sealed class DsuMotionServer : IDisposable
{
    public const int DefaultPort = 26760;
    private const ushort ProtocolVersion = 1001;
    private const uint VersionMessage = 0x00100000;
    private const uint ControllerInfoMessage = 0x00100001;
    private const uint ControllerDataMessage = 0x00100002;
    private static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(5);

    private readonly Socket _socket;
    private readonly object _gate = new();
    private readonly Dictionary<EndPointKey, Client> _clients = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _receiveTask;
    private readonly uint _serverId = unchecked((uint)Random.Shared.NextInt64(1, (long)uint.MaxValue + 1));
    private ControllerSnapshot[] _snapshots = [];
    private int _packetCounter;
    private string? _lastError;
    private bool _disposed;

    private sealed class Client(DateTimeOffset lastSeen)
    {
        public DateTimeOffset LastSeen { get; set; } = lastSeen;
        public bool AllSlots { get; set; }
        public HashSet<byte> Slots { get; } = [];
        public HashSet<string> MacAddresses { get; } = new(StringComparer.Ordinal);
        public bool Subscribed => AllSlots || Slots.Count != 0 || MacAddresses.Count != 0;
    }

    private readonly record struct EndPointKey(IPAddress Address, int Port)
    {
        public static EndPointKey From(EndPoint endpoint)
        {
            var ip = (IPEndPoint)endpoint;
            return new(ip.Address, ip.Port);
        }
        public EndPoint ToEndPoint() => new IPEndPoint(Address, Port);
    }

    private DsuMotionServer(IPAddress address, int port)
    {
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            _socket.Bind(new IPEndPoint(address, port));
            _socket.ReceiveTimeout = 500;
            BoundEndPoint = (IPEndPoint)_socket.LocalEndPoint!;
            _receiveTask = Task.Run(ReceiveLoop);
        }
        catch
        {
            _socket.Dispose();
            throw;
        }
    }

    public IPEndPoint BoundEndPoint { get; }
    public bool IsRunning => !_disposed && _socket.IsBound;
    public string? LastError { get { lock (_gate) return _lastError; } }
    public string Status => IsRunning ? (LastError is null ? $"Listening on {BoundEndPoint}" : $"Listening on {BoundEndPoint}; {LastError}") : "Stopped";

    public static DsuMotionServer Start(IPAddress? address = null, int port = DefaultPort)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        return new DsuMotionServer(address ?? IPAddress.Loopback, port);
    }

    public static bool TryStart(out DsuMotionServer? server, out string? error,
        IPAddress? address = null, int port = DefaultPort)
    {
        try
        {
            server = Start(address, port);
            error = null;
            return true;
        }
        catch (SocketException exception)
        {
            server = null;
            error = exception.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"DSU UDP port {port} is already in use."
                : $"Could not start the DSU UDP server: {exception.Message}";
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or ObjectDisposedException)
        {
            server = null;
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Updates the eligible DS4 motion sources and sends fresh reports to active subscribers.</summary>
    public void Publish(IEnumerable<ControllerSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var eligible = snapshots
            .Where(snapshot => snapshot.Connected && snapshot.Output == "ds4" && snapshot.GyroEnabled && snapshot.State.Motion is not null)
            .OrderBy(snapshot => snapshot.SessionId, StringComparer.Ordinal)
            .ThenBy(snapshot => snapshot.ControllerId, StringComparer.Ordinal)
            .Take(4)
            .ToArray();

        List<(EndPointKey Endpoint, byte Slot, ControllerSnapshot Snapshot)> deliveries = [];
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _snapshots = eligible;
            ExpireClients(DateTimeOffset.UtcNow);
            foreach (var pair in _clients)
            {
                if (!pair.Value.Subscribed) continue;
                for (byte slot = 0; slot < _snapshots.Length; slot++)
                {
                    if (IsRequested(pair.Value, slot, _snapshots[slot]))
                        deliveries.Add((pair.Key, slot, _snapshots[slot]));
                }
            }
        }

        foreach (var delivery in deliveries)
            TrySend(BuildMotionPacket(delivery.Slot, delivery.Snapshot), delivery.Endpoint.ToEndPoint());
    }

    private static bool IsRequested(Client client, byte slot, ControllerSnapshot snapshot) =>
        client.AllSlots || client.Slots.Contains(slot) || client.MacAddresses.Contains(MacForSlot(slot));

    private void ReceiveLoop()
    {
        var buffer = new byte[2048];
        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var length = _socket.ReceiveFrom(buffer, SocketFlags.None, ref remote);
                if (length > 0) HandlePacket(buffer.AsSpan(0, length), EndPointKey.From(remote));
            }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut) { }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (_stop.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                SetError($"DSU receive error: {exception.Message}");
            }
            lock (_gate) ExpireClients(DateTimeOffset.UtcNow);
        }
    }

    private void HandlePacket(ReadOnlySpan<byte> packet, EndPointKey endpoint)
    {
        if (!IsValidClientPacket(packet)) return;
        var message = BinaryPrimitives.ReadUInt32LittleEndian(packet[16..20]);
        var now = DateTimeOffset.UtcNow;
        switch (message)
        {
            case VersionMessage when packet.Length == 20:
                TrySend(BuildPacket(VersionMessage, [0xE9, 0x03]), endpoint.ToEndPoint());
                break;
            case ControllerInfoMessage:
                ReplyControllerInfo(packet, endpoint);
                break;
            case ControllerDataMessage:
                RegisterSubscription(packet, endpoint, now);
                break;
        }
    }

    private void ReplyControllerInfo(ReadOnlySpan<byte> packet, EndPointKey endpoint)
    {
        if (packet.Length < 28) return;
        var count = BinaryPrimitives.ReadInt32LittleEndian(packet[20..24]);
        if (count is < 1 or > 4 || packet.Length != 24 + count) return;
        ControllerSnapshot[] snapshots;
        lock (_gate) snapshots = _snapshots;
        for (var index = 0; index < count; index++)
        {
            var slot = packet[24 + index];
            if (slot > 3) return;
            var payload = new byte[12];
            if (slot < snapshots.Length) WriteSharedInfo(payload, slot, connected: true);
            // The last byte in the 12-byte port record is reserved and remains zero.
            TrySend(BuildPacket(ControllerInfoMessage, payload), endpoint.ToEndPoint());
        }
    }

    private void RegisterSubscription(ReadOnlySpan<byte> packet, EndPointKey endpoint, DateTimeOffset now)
    {
        if (packet.Length != 28) return;
        var flags = packet[20];
        if ((flags & ~0x03) != 0) return;
        List<(byte Slot, ControllerSnapshot Snapshot)> immediate = [];
        lock (_gate)
        {
            if (!_clients.TryGetValue(endpoint, out var client)) _clients[endpoint] = client = new Client(now);
            client.LastSeen = now;
            if (flags == 0)
            {
                client.AllSlots = true;
                client.Slots.Clear();
                client.MacAddresses.Clear();
            }
            if ((flags & 0x01) != 0)
            {
                if (packet[21] > 3) return;
                client.Slots.Add(packet[21]);
            }
            if ((flags & 0x02) != 0)
                client.MacAddresses.Add(Convert.ToHexString(packet.Slice(22, 6)));
            for (byte slot = 0; slot < _snapshots.Length; slot++)
                if (IsRequested(client, slot, _snapshots[slot])) immediate.Add((slot, _snapshots[slot]));
        }

        foreach (var (slot, snapshot) in immediate)
            TrySend(BuildMotionPacket(slot, snapshot), endpoint.ToEndPoint());
    }

    private static bool IsValidClientPacket(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 20 || !packet[..4].SequenceEqual("DSUC"u8)) return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(packet[4..6]) != ProtocolVersion) return false;
        if (BinaryPrimitives.ReadUInt16LittleEndian(packet[6..8]) != packet.Length - 16) return false;
        var expected = BinaryPrimitives.ReadUInt32LittleEndian(packet[8..12]);
        return expected == ComputeCrc32(packet, zeroCrcField: true);
    }

    private byte[] BuildMotionPacket(byte slot, ControllerSnapshot snapshot)
    {
        var packet = new byte[100];
        var payload = packet.AsSpan(20);
        WriteSharedInfo(payload, slot, connected: true);
        payload[11] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload[12..16], unchecked((uint)Interlocked.Increment(ref _packetCounter)));
        // DSU's button and stick fields are intentionally neutral: this endpoint serves motion only.
        payload[20..24].Fill(128);
        var motion = snapshot.State.Motion!;
        var timestampMicros = (ulong)Math.Max(0, motion.TimestampNanoseconds / 1000);
        BinaryPrimitives.WriteUInt64LittleEndian(payload[48..56], timestampMicros);
        WriteFloat(payload[56..60], (float)(motion.AccelerationMetersPerSecondSquared.X / 9.80665));
        WriteFloat(payload[60..64], (float)(motion.AccelerationMetersPerSecondSquared.Y / 9.80665));
        WriteFloat(payload[64..68], (float)(motion.AccelerationMetersPerSecondSquared.Z / 9.80665));
        const double radiansToDegrees = 180d / Math.PI;
        WriteFloat(payload[68..72], (float)(motion.GyroRadiansPerSecond.X * radiansToDegrees));
        WriteFloat(payload[72..76], (float)(motion.GyroRadiansPerSecond.Y * radiansToDegrees));
        WriteFloat(payload[76..80], (float)(motion.GyroRadiansPerSecond.Z * radiansToDegrees));
        return FinalizePacket(packet, ControllerDataMessage);
    }

    private static void WriteSharedInfo(Span<byte> payload, byte slot, bool connected)
    {
        payload[0] = slot;
        payload[1] = connected ? (byte)2 : (byte)0;
        payload[2] = connected ? (byte)2 : (byte)0; // DS4 model with full motion sensors.
        payload[3] = connected ? (byte)2 : (byte)0; // Bluetooth source on the phone.
        Convert.FromHexString(MacForSlot(slot)).CopyTo(payload[4..10]);
        payload[10] = 0; // Battery level is unknown for a motion-only source.
    }

    private static string MacForSlot(byte slot) => $"0000000000{slot + 1:X2}";

    private byte[] BuildPacket(uint message, ReadOnlySpan<byte> payload)
    {
        var packet = new byte[20 + payload.Length];
        return FinalizePacket(packet, message, payload);
    }

    private byte[] FinalizePacket(byte[] packet, uint message, ReadOnlySpan<byte> payload = default)
    {
        "DSUS"u8.CopyTo(packet);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), ProtocolVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), checked((ushort)(packet.Length - 16)));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), _serverId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16, 4), message);
        if (!payload.IsEmpty) payload.CopyTo(packet.AsSpan(20));
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), ComputeCrc32(packet, zeroCrcField: false));
        return packet;
    }

    private static void WriteFloat(Span<byte> target, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(target, BitConverter.SingleToInt32Bits(float.IsFinite(value) ? value : 0f));

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes, bool zeroCrcField)
    {
        uint crc = 0xFFFFFFFF;
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = zeroCrcField && index is >= 8 and < 12 ? (byte)0 : bytes[index];
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }
        return ~crc;
    }

    private void TrySend(byte[] packet, EndPoint endpoint)
    {
        try { _socket.SendTo(packet, SocketFlags.None, endpoint); }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            if (!_stop.IsCancellationRequested) SetError($"DSU send error: {exception.Message}");
        }
    }

    private void ExpireClients(DateTimeOffset now)
    {
        foreach (var endpoint in _clients.Where(pair => now - pair.Value.LastSeen > ClientTimeout).Select(pair => pair.Key).ToArray())
            _clients.Remove(endpoint);
    }

    private void SetError(string error) { lock (_gate) _lastError = error; }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _clients.Clear();
        }
        _stop.Cancel();
        _socket.Dispose();
        try { _receiveTask.Wait(TimeSpan.FromSeconds(1)); } catch (AggregateException) { }
        _stop.Dispose();
    }

}
