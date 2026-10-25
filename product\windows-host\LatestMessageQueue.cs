using System.Text.Json;

namespace ControllerBridge.Host;

public sealed record QueuedMessage(string Line, DateTimeOffset ReceivedAt);

// Configuration/lifecycle messages are ordering barriers. Liveness pings are
// sideband, so they cannot keep an obsolete snapshot alive in a separate batch.
public sealed class LatestMessageQueue(Func<DateTimeOffset>? clock = null) : IDisposable
{
    private sealed class Batch
    {
        public QueuedMessage? Control;
        public Dictionary<string, (long Sequence, QueuedMessage Message)> States { get; } = [];
        public List<QueuedMessage> Pings { get; } = [];
        public QueuedMessage[] Messages => Control != null ? [Control] : States.Values.Select(x => x.Message).Concat(Pings).ToArray();
    }
    private readonly object _gate = new();
    private readonly Queue<Batch> _batches = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly SemaphoreSlim _capacity = new(32);
    private Batch? _tailStates;
    private bool _complete;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    public int PendingBatchCount { get { lock (_gate) return _batches.Count; } }

    public async Task EnqueueAsync(string line, CancellationToken cancellationToken)
    {
        var message = new QueuedMessage(line, _clock());
        var isState = TryState(line, out var id, out var sequence);
        var isPing = !isState && IsPing(line);
        lock (_gate)
        {
            if (_complete) return;
            if ((isState || isPing) && _tailStates != null)
            {
                if (isState) AddState(_tailStates, id, sequence, message);
                else AddPing(_tailStates, message);
                return;
            }
        }
        await _capacity.WaitAsync(cancellationToken);
        lock (_gate)
        {
            if (_complete) { _capacity.Release(); return; }
            var batch = new Batch();
            if (isState || isPing)
            {
                if (isState) AddState(batch, id, sequence, message); else AddPing(batch, message);
                _tailStates = batch;
            }
            else { batch.Control = message; _tailStates = null; }
            _batches.Enqueue(batch);
            _available.Release();
        }
    }
    private static void AddState(Batch batch, string id, long sequence, QueuedMessage message)
    {
        if (batch.States.TryGetValue(id, out var old) && sequence <= old.Sequence) return;
        if (!batch.States.ContainsKey(id) && batch.States.Count >= 4) throw new InvalidDataException("Mais de quatro controles em um lote de estados.");
        batch.States[id] = (sequence, message);
    }
    private static void AddPing(Batch batch, QueuedMessage message)
    {
        if (batch.Pings.Count >= 32) throw new InvalidDataException("Mais de 32 pings pendentes em um lote.");
        batch.Pings.Add(message);
    }
    public async Task<QueuedMessage[]?> DequeueAsync(CancellationToken cancellationToken)
    {
        await _available.WaitAsync(cancellationToken);
        lock (_gate)
        {
            if (_batches.Count == 0) return null;
            var batch = _batches.Dequeue();
            if (ReferenceEquals(batch, _tailStates)) _tailStates = null;
            _capacity.Release();
            return batch.Messages;
        }
    }
    public void Complete(bool discardPending = false)
    {
        lock (_gate)
        {
            if (_complete) return;
            _complete = true;
            if (discardPending)
            {
                while (_batches.Count > 0) { _batches.Dequeue(); _capacity.Release(); }
                _tailStates = null;
            }
            _available.Release();
        }
    }
    private static bool TryState(string line, out string id, out long sequence)
    {
        id = ""; sequence = -1;
        try
        {
            using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "state") return false;
            if (!root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1) return false;
            id = root.RequiredString("controller_id", 128);
            sequence = root.RequiredSequence();
            _ = GamepadState.Parse(root); // Invalid snapshots remain ordering barriers and are rejected by the session.
            return true;
        }
        catch (Exception error) when (error is JsonException or ProtocolException) { return false; }
    }
    private static bool IsPing(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "ping") return false;
            if (!root.TryGetProperty("v", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1) return false;
            _ = root.RequiredSequence();
            return true;
        }
        catch (Exception error) when (error is JsonException or ProtocolException) { return false; }
    }
    public void Dispose() { _available.Dispose(); _capacity.Dispose(); }
}
