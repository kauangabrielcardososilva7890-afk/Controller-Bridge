using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace ControllerBridge.Host;

public sealed class HostServer(OutputPool outputs, ProfileStore profiles, Action<string> log)
{
    private readonly SemaphoreSlim _clientSlots = new(16);
    public async Task RunAsync(int port, CancellationToken cancellationToken, WifiListenerOptions? wifi = null)
    {
        using var stopAll = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        async Task RunOne(IPAddress address, int listenerPort, byte[]? key)
        {
            try { await RunListenerAsync(address, listenerPort, key, stopAll.Token); }
            catch { stopAll.Cancel(); throw; }
        }
        var listeners = new List<Task> { RunOne(IPAddress.Loopback, port, null) };
        if (wifi != null) listeners.Add(RunOne(wifi.BindAddress, wifi.Port, wifi.PairingKey));
        await Task.WhenAll(listeners);
    }

    private async Task RunListenerAsync(IPAddress address, int port, byte[]? wifiKey, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(address, port);
        listener.Start();
        log(wifiKey == null ? $"Ouvindo USB/ADB em 127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}." : $"Ouvindo Wi-Fi autenticado em {listener.LocalEndpoint}.");
        var sessions = new List<Task>();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken);
                if (!_clientSlots.Wait(0)) { client.Dispose(); continue; }
                sessions.RemoveAll(task => task.IsCompleted);
                sessions.Add(ServeLimitedAsync(client, wifiKey, cancellationToken));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            listener.Stop();
            await Task.WhenAll(sessions);
        }
    }

    private async Task ServeLimitedAsync(TcpClient client, byte[]? wifiKey, CancellationToken cancellationToken)
    {
        try { await ServeCoreAsync(client, wifiKey, cancellationToken); }
        catch (Exception error) { log($"Conexão encerrada: {error.GetBaseException().Message}"); }
        finally { _clientSlots.Release(); }
    }
    public Task ServeClientAsync(TcpClient client, CancellationToken cancellationToken) => ServeCoreAsync(client, null, cancellationToken);
    public Task ServeWifiClientAsync(TcpClient client, byte[] pairingKey, CancellationToken cancellationToken) => ServeCoreAsync(client, pairingKey, cancellationToken);

    private async Task ServeCoreAsync(TcpClient client, byte[]? wifiKey, CancellationToken cancellationToken)
    {
        using (client)
        using (var timerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            client.NoDelay = true;
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 8192, true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" };
            var boundedReader = new BoundedLineReader(reader, wifiKey == null ? 65536 : 131072);
            WifiSessionCrypto? secure = null;
            if (wifiKey != null)
            {
                try { secure = await WifiAuthentication.AcceptAsync(wifiKey, boundedReader, writer, cancellationToken); }
                catch (Exception error) when (error is CryptographicException or JsonException or ProtocolException or InvalidDataException or IOException or OperationCanceledException or FormatException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        try { await writer.WriteLineAsync(JsonSerializer.Serialize(new { v = 1, type = "error", code = "auth_failed", message = "Authentication failed." }).AsMemory(), failureTimeout.Token); } catch { }
                    }
                    log("Autenticação Wi-Fi recusada; conexão fechada.");
                    return;
                }
                log("Autenticação Wi-Fi concluída.");
            }
            using var crypto = secure;
            using var session = new ControllerSession(outputs, profiles, log, expectedClientId: crypto?.ClientId);
            using var queue = new LatestMessageQueue();
            var processor = Task.Run(async () =>
            {
                try
                {
                    while (await queue.DequeueAsync(timerCancellation.Token) is { } batch)
                        foreach (var message in batch)
                        {
                            var reply = session.ProcessLine(message.Line, message.ReceivedAt);
                            if (reply != null)
                            {
                                var response = JsonSerializer.Serialize(reply);
                                await writer.WriteLineAsync((crypto?.Encrypt(response) ?? response).AsMemory(), timerCancellation.Token);
                            }
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
                while (await boundedReader.ReadLineAsync(timerCancellation.Token) is { } line)
                    await queue.EnqueueAsync(crypto?.Decrypt(line) ?? line, timerCancellation.Token);
            }
            catch (Exception error) when (error is IOException or SocketException or DecoderFallbackException or InvalidDataException or CryptographicException or JsonException or ProtocolException or OverflowException)
            { log($"Conexão encerrada: {error.Message}"); }
            catch (OperationCanceledException) when (timerCancellation.IsCancellationRequested) { }
            finally
            {
                queue.Complete(discardPending: true);
                timerCancellation.Cancel();
                try { await processor; }
                catch (Exception error) when (error is OperationCanceledException or IOException or SocketException or CryptographicException) { }
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
            if (line.Length > maxLength) throw new InvalidDataException($"Mensagem excedeu o limite de {maxLength} caracteres.");
        }
    }
}

