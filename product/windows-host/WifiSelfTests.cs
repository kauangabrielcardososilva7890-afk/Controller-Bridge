using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControllerBridge.Host;

public static class WifiSelfTests
{
    private static int _checks;
    private static readonly byte[] Key = Enumerable.Range(0, 16).Select(x => (byte)x).ToArray();
    private static readonly string ServerNonce = string.Concat(Enumerable.Range(0, 32).Select(x => x.ToString("x2")));
    private static readonly string ClientNonce = string.Concat(Enumerable.Range(32, 32).Select(x => x.ToString("x2")));
    public static async Task<int> RunAsync(string directory)
    {
        _checks = 0;
        Directory.CreateDirectory(directory);
        TestVectorsAndEnvelopes();
        var keyFile = Path.Combine(directory, "pairing.key");
        var persisted = WifiPairing.LoadOrCreate(keyFile);
        Check(persisted.Length == 16 && persisted.SequenceEqual(WifiPairing.LoadOrCreate(keyFile)), "Código de pareamento é aleatório e persistente.");
        Check(WifiPairing.DecodeCode("0001-0203 0405060708090a0b0c0d0e0f").SequenceEqual(Key), "Código aceita agrupamento visual.");
        await TestAuthenticatedConnectionAsync(Path.Combine(directory, "profiles"));
        await TestRejectedAuthenticationAsync(false);
        await TestRejectedAuthenticationAsync(true);
        return _checks;
    }
    private static void TestVectorsAndEnvelopes()
    {
        var transcript = WifiAuthentication.Transcript(ServerNonce, ClientNonce, "test-client");
        Check(WifiAuthentication.Proof(Key, "client", transcript) == "61934e5da6e95ece338c9c8d6500ae0d342e29024f167ad48e4011fa0a15335b", "Prova HMAC cliente corresponde ao vetor compartilhado Android.");
        Check(WifiAuthentication.Proof(Key, "server", transcript) == "af8ae9b94dbe14a58e5e81da336d9baf2f0d854c966fa1de0e57e4a84a42e30a", "Prova HMAC servidor corresponde ao vetor compartilhado Android.");
        using var client = new WifiSessionCrypto(Key, transcript, false, "test-client");
        using var server = new WifiSessionCrypto(Key, transcript, true, "test-client");
        var encrypted = client.Encrypt("{\"v\":1,\"type\":\"hello\"}");
        using (var envelope = JsonDocument.Parse(encrypted)) Check(envelope.RootElement.GetProperty("data").GetString() == "zDZeqvNQeItaBPpqXGmVgX9XDtPj7gvnSbyN6bWFIvLV6YwTTzo=", "AES-GCM corresponde ao vetor de bytes compartilhado.");
        Check(server.Decrypt(encrypted) == "{\"v\":1,\"type\":\"hello\"}", "Envelope cliente→servidor descriptografado.");
        Reject(() => server.Decrypt(encrypted), "Replay de contador é recusado.");
        var response = server.Encrypt("{\"v\":1,\"type\":\"welcome\"}");
        Check(client.Decrypt(response).Contains("welcome"), "Chave servidor→cliente funciona separadamente.");
        using var tamperServer = new WifiSessionCrypto(Key, transcript, true, "test-client");
        using var envelopeDoc = JsonDocument.Parse(encrypted);
        var corrupted = Convert.FromBase64String(envelopeDoc.RootElement.GetProperty("data").GetString()!); corrupted[0] ^= 1;
        Reject(() => tamperServer.Decrypt(JsonSerializer.Serialize(new { v = 1, type = "secure", counter = 1, data = Convert.ToBase64String(corrupted) })), "Alteração de ciphertext invalida a tag.");
        using var gapClient = new WifiSessionCrypto(Key, transcript, false, "test-client");
        using var gapServer = new WifiSessionCrypto(Key, transcript, true, "test-client");
        _ = gapClient.Encrypt("first"); var second = gapClient.Encrypt("second");
        Reject(() => gapServer.Decrypt(second), "Saltos no contador são recusados.");
        using var reflectionClient = new WifiSessionCrypto(Key, transcript, false, "test-client");
        Reject(() => reflectionClient.Decrypt(encrypted), "Mensagem refletida usa chave de direção errada.");
        Reject(() => gapServer.Decrypt("{\"v\":1,\"type\":\"state\"}"), "Mensagem de aplicação em texto puro é recusada no canal Wi-Fi.");
        var otherTranscript = WifiAuthentication.Transcript(new string('f', 64), ClientNonce, "test-client");
        Check(WifiAuthentication.Proof(Key, "client", otherTranscript) != WifiAuthentication.Proof(Key, "client", transcript), "Nonce novo impede reutilizar prova antiga.");
    }
    private static async Task TestAuthenticatedConnectionAsync(string directory)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var factory = new RecordingFactory(); using var pool = new OutputPool(factory);
        var host = new HostServer(pool, new ProfileStore(directory), _ => { });
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient();
        var accept = listener.AcceptTcpClientAsync(cancellation.Token);
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellation.Token);
        var accepted = await accept; listener.Stop();
        var serverTask = host.ServeWifiClientAsync(accepted, Key, cancellation.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" };
        using var challenge = JsonDocument.Parse((await reader.ReadLineAsync(cancellation.Token))!);
        Check(challenge.RootElement.GetProperty("type").GetString() == "auth_challenge" && factory.Outputs.Count == 0, "Wi-Fi exige autenticação antes de criar saída.");
        var nonce = challenge.RootElement.GetProperty("server_nonce").GetString()!;
        var transcript = WifiAuthentication.Transcript(nonce, ClientNonce, "test-client");
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { v = 1, type = "auth_response", client_id = "test-client", client_nonce = ClientNonce, proof = WifiAuthentication.Proof(Key, "client", transcript) }).AsMemory(), cancellation.Token);
        using var ok = JsonDocument.Parse((await reader.ReadLineAsync(cancellation.Token))!);
        Check(ok.RootElement.GetProperty("type").GetString() == "auth_ok" && ok.RootElement.GetProperty("proof").GetString() == WifiAuthentication.Proof(Key, "server", transcript), "Servidor confirma autenticação mutuamente.");
        using var crypto = new WifiSessionCrypto(Key, transcript, false, "test-client");
        await writer.WriteLineAsync(crypto.Encrypt("{\"v\":1,\"type\":\"hello\",\"client_id\":\"different-client\",\"app_version\":\"0.1.0\"}").AsMemory(), cancellation.Token);
        using (var mismatch = JsonDocument.Parse(crypto.Decrypt((await reader.ReadLineAsync(cancellation.Token))!)))
            Check(mismatch.RootElement.GetProperty("code").GetString() == "client_id_mismatch" && factory.Outputs.Count == 0, "Hello precisa corresponder à identidade autenticada.");
        await writer.WriteLineAsync(crypto.Encrypt("{\"v\":1,\"type\":\"hello\",\"client_id\":\"test-client\",\"app_version\":\"0.1.0\"}").AsMemory(), cancellation.Token);
        Check(MessageType(crypto.Decrypt((await reader.ReadLineAsync(cancellation.Token))!)) == "welcome", "Hello/welcome criptografados mantêm o protocolo normal.");
        await writer.WriteLineAsync(crypto.Encrypt("{\"v\":1,\"type\":\"attach\",\"controller_id\":\"1\",\"profile_id\":\"wifi-test\",\"descriptor\":\"test\",\"name\":\"Wi-Fi test\"}").AsMemory(), cancellation.Token);
        Check(MessageType(crypto.Decrypt((await reader.ReadLineAsync(cancellation.Token))!)) == "profile", "Controle autenticado recebe perfil.");
        var state = GamepadState.Neutral(); var buttons = state.Buttons.ToDictionary(); buttons["south"] = true;
        await writer.WriteLineAsync(crypto.Encrypt(JsonSerializer.Serialize(new { v = 1, type = "state", controller_id = "1", seq = 1, buttons, axes = state.Axes })).AsMemory(), cancellation.Token);
        await writer.WriteLineAsync(crypto.Encrypt("{\"v\":1,\"type\":\"ping\",\"seq\":1}").AsMemory(), cancellation.Token);
        Check(MessageType(crypto.Decrypt((await reader.ReadLineAsync(cancellation.Token))!)) == "pong" && factory.Outputs.Single().Last.Button("south"), "Estado/ping criptografados chegam ao adaptador.");
        await writer.WriteLineAsync("{\"v\":1,\"type\":\"ping\",\"seq\":99}".AsMemory(), cancellation.Token);
        await serverTask.WaitAsync(cancellation.Token);
        Check(factory.Outputs.Single().Disposed && factory.Outputs.Single().Last.IsNeutral, "Injeção em texto puro fecha conexão e libera comando pressionado.");
    }
    private static async Task TestRejectedAuthenticationAsync(bool timeout)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(14));
        var factory = new RecordingFactory(); using var pool = new OutputPool(factory);
        var host = new HostServer(pool, new ProfileStore(Path.GetTempPath()), _ => { });
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient(); var accept = listener.AcceptTcpClientAsync(cancellation.Token);
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, cancellation.Token);
        var accepted = await accept; listener.Stop(); var task = host.ServeWifiClientAsync(accepted, Key, cancellation.Token);
        using var stream = client.GetStream(); using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true, NewLine = "\n" };
        _ = await reader.ReadLineAsync(cancellation.Token);
        if (!timeout) await writer.WriteLineAsync(JsonSerializer.Serialize(new { v = 1, type = "auth_response", client_id = "wrong-key", client_nonce = ClientNonce, proof = new string('0', 64) }).AsMemory(), cancellation.Token);
        var line = await reader.ReadLineAsync(cancellation.Token);
        using var error = JsonDocument.Parse(line!);
        Check(error.RootElement.GetProperty("code").GetString() == "auth_failed", timeout ? "Autenticação sem resposta expira após prazo." : "Código/prova incorretos recebem erro genérico.");
        await task.WaitAsync(cancellation.Token);
        Check(factory.Outputs.Count == 0, "Falha de autenticação não aloca controles.");
    }
    private static string? MessageType(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.GetProperty("type").GetString(); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); _checks++; }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception error) when (error is CryptographicException or ProtocolException or JsonException or InvalidDataException) { _checks++; return; }
        throw new Exception(message);
    }
    private sealed class RecordingFactory : IOutputFactory
    {
        public List<RecordingOutput> Outputs { get; } = [];
        public IControllerOutput Create(string id, OutputKind kind) { var output = new RecordingOutput(); Outputs.Add(output); return output; }
        public void Dispose() { }
    }
    private sealed class RecordingOutput : IControllerOutput
    {
        public GamepadState Last { get; private set; } = GamepadState.Neutral();
        public bool Disposed { get; private set; }
        public void Apply(GamepadState state) => Last = state;
        public void Neutralize() => Last = GamepadState.Neutral();
        public void Dispose() { Neutralize(); Disposed = true; }
    }
}

