using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ControllerBridge.Host;

public sealed class HostServer(OutputPool outputs, ProfileStore profiles, Action<string> log)
{
    public async Task RunAsync(int port, CancellationToken cancellationToken)
    {
        // Loopback-only until authenticated Wi-Fi pairing is implemented.
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        log($"Ouvindo USB/ADB em 127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}.");
        var sessions = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                sessions.RemoveAll(task => task.IsCompleted);
                sessions.Add(ServeClientAsync(client, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            listener.Stop();
            await Task.WhenAll(sessions);
        }
    }

    public async Task ServeClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        using (var session = new ControllerSession(outputs, profiles, log))
        using (var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 8192, true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" };
            using var queue = new LatestMessageQueue();
            var processor = Task.Run(async () =>
            {
                try
                {
                    while (await queue.DequeueAsync(timerCancellation.Token) is { } batch)
                        foreach (var message in batch)
                        {
                            var reply = session.ProcessLine(message.Line, message.ReceivedAt);
                            if (reply != null) await writer.WriteLineAsync(JsonSerializer.Serialize(reply).AsMemory(), timerCancellation.Token);
                        }
                }
                catch { timerCancellation.Cancel(); throw; }
            }, CancellationToken.None);
            var timer = Task.Run(async () =>
            {
                try
                {
                    using var watchdog = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                    while (await watchdog.WaitForNextTickAsync(timerCancellation.Token)) session.NeutralizeStale();
                }
                catch (OperationCanceledException) { }
            }, CancellationToken.None);
            log("Celular conectado ao receptor.");
            try
            {
                var boundedReader = new BoundedLineReader(reader, 65536);
                while (await boundedReader.ReadLineAsync(timerCancellation.Token) is { } line)
                    await queue.EnqueueAsync(line, timerCancellation.Token);
            }
            catch (Exception error) when (error is IOException or SocketException or DecoderFallbackException or InvalidDataException)
            { log($"Conexão encerrada: {error.Message}"); }
            catch (OperationCanceledException) when (timerCancellation.IsCancellationRequested) { }
            finally
            {
                queue.Complete(discardPending: true);
                timerCancellation.Cancel();
                try { await processor; }
                catch (Exception error) when (error is OperationCanceledException or IOException or SocketException) { }
                await timer;
            }
            log("Celular desconectado; controles liberados.");
        }
    }
}

public sealed class BoundedLineReader(StreamReader reader, int maxLength)
{
    private readonly char[] _buffer = new char[4096];
    private int _position;
    private int _length;
    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        while (true)
        {
            if (_position == _length)
            {
                _length = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                _position = 0;
                if (_length == 0)
                {
                    if (line.Length == 0) return null;
                    throw new InvalidDataException("Mensagem sem terminador de linha.");
                }
            }
            var character = _buffer[_position++];
            if (character == '\n') return line.ToString().TrimEnd('\r');
            line.Append(character);
            if (line.Length > maxLength) throw new InvalidDataException("Mensagem excedeu o limite de 64 KiB.");
        }
    }
}
