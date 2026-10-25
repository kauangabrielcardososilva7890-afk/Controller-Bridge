using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControllerBridge.Host;

public sealed record WifiListenerOptions(System.Net.IPAddress BindAddress, int Port, byte[] PairingKey);

public static class WifiPairing
{
    public static byte[] DecodeCode(string code)
    {
        var normalized = code.Replace("-", "").Replace(" ", "").Trim();
        if (normalized.Length != 32) throw new FormatException("Pairing code must contain 32 hexadecimal characters.");
        return Convert.FromHexString(normalized);
    }
    public static byte[] LoadOrCreate(string path)
    {
        if (File.Exists(path)) return DecodeCode(File.ReadAllText(path));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var key = RandomNumberGenerator.GetBytes(16);
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var bytes = Encoding.ASCII.GetBytes(Convert.ToHexString(key).ToLowerInvariant());
            file.Write(bytes);
            file.Flush(true);
            return key;
        }
        catch (IOException) when (File.Exists(path)) { return DecodeCode(File.ReadAllText(path)); }
    }
}

public static class WifiAuthentication
{
    public const string Domain = "ControllerBridge/WiFi/v1";
    public static string Transcript(string serverNonce, string clientNonce, string clientId) => $"{Domain}|{serverNonce}|{clientNonce}|{clientId}";
    public static byte[] Derive(byte[] key, string label, string transcript) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(label + "|" + transcript));
    public static string Proof(byte[] key, string label, string transcript) => Convert.ToHexString(Derive(key, label, transcript)).ToLowerInvariant();
    public static string Nonce(JsonElement element, string name)
    {
        var text = element.RequiredString(name, 64);
        if (text.Length != 64 || text != text.ToLowerInvariant()) throw new CryptographicException("Authentication failed.");
        try { _ = Convert.FromHexString(text); } catch (FormatException) { throw new CryptographicException("Authentication failed."); }
        return text;
    }
    public static async Task<WifiSessionCrypto> AcceptAsync(byte[] key, BoundedLineReader reader, StreamWriter writer, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var serverNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { v = 1, type = "auth_challenge", server_nonce = serverNonce, algorithm = "hmac-sha256+aes-256-gcm" }).AsMemory(), timeout.Token);
        var line = await reader.ReadLineAsync(timeout.Token);
        if (line == null || line.Length > 4096) throw new CryptographicException("Authentication failed.");
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != 1 || root.RequiredString("type", 32) != "auth_response")
            throw new CryptographicException("Authentication failed.");
        var clientId = root.RequiredString("client_id", 128);
        var clientNonce = Nonce(root, "client_nonce");
        var transcript = Transcript(serverNonce, clientNonce, clientId);
        var proof = Nonce(root, "proof");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(proof), Derive(key, "client", transcript)))
            throw new CryptographicException("Authentication failed.");
        await writer.WriteLineAsync(JsonSerializer.Serialize(new { v = 1, type = "auth_ok", proof = Proof(key, "server", transcript) }).AsMemory(), timeout.Token);
        return new WifiSessionCrypto(key, transcript, true, clientId);
    }
}

public sealed class WifiSessionCrypto : IDisposable
{
    private readonly AesGcm _outgoing;
    private readonly AesGcm _incoming;
    private readonly string _sendDirection;
    private readonly string _receiveDirection;
    private long _sendCounter;
    private long _receiveCounter;
    public string ClientId { get; }
    public WifiSessionCrypto(byte[] pairingKey, string transcript, bool isServer, string clientId)
    {
        ClientId = clientId;
        _sendDirection = isServer ? "s2c" : "c2s";
        _receiveDirection = isServer ? "c2s" : "s2c";
        var sendKey = WifiAuthentication.Derive(pairingKey, _sendDirection, transcript);
        var receiveKey = WifiAuthentication.Derive(pairingKey, _receiveDirection, transcript);
        try { _outgoing = new AesGcm(sendKey, 16); _incoming = new AesGcm(receiveKey, 16); }
        finally { CryptographicOperations.ZeroMemory(sendKey); CryptographicOperations.ZeroMemory(receiveKey); }
    }
    private static byte[] Nonce(long counter)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), (ulong)counter);
        return nonce;
    }
    private static byte[] Aad(string direction, long counter) => Encoding.UTF8.GetBytes(WifiAuthentication.Domain + "|" + direction + "|" + counter.ToString(CultureInfo.InvariantCulture));
    public string Encrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        if (bytes.Length > 65536) throw new InvalidDataException("Application message exceeds 64 KiB.");
        var counter = checked(++_sendCounter);
        var encrypted = new byte[bytes.Length + 16];
        _outgoing.Encrypt(Nonce(counter), bytes, encrypted.AsSpan(0, bytes.Length), encrypted.AsSpan(bytes.Length, 16), Aad(_sendDirection, counter));
        return JsonSerializer.Serialize(new { v = 1, type = "secure", counter, data = Convert.ToBase64String(encrypted) });
    }
    public string Decrypt(string line)
    {
        using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("v", out var v) || v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version != 1 || root.RequiredString("type", 16) != "secure" || !root.TryGetProperty("counter", out var c) || c.ValueKind != JsonValueKind.Number || !c.TryGetInt64(out var counter) || counter < 1 || counter != checked(_receiveCounter + 1))
            throw new CryptographicException("Invalid encrypted envelope or replay.");
        var data = root.RequiredString("data", 90000);
        byte[] encrypted;
        try { encrypted = Convert.FromBase64String(data); } catch (FormatException) { throw new CryptographicException("Invalid encrypted envelope."); }
        if (encrypted.Length is < 16 or > 65552) throw new CryptographicException("Invalid encrypted message length.");
        var plaintext = new byte[encrypted.Length - 16];
        _incoming.Decrypt(Nonce(counter), encrypted.AsSpan(0, plaintext.Length), encrypted.AsSpan(plaintext.Length, 16), plaintext, Aad(_receiveDirection, counter));
        var result = new UTF8Encoding(false, true).GetString(plaintext);
        _receiveCounter = counter;
        return result;
    }
    public void Dispose() { _outgoing.Dispose(); _incoming.Dispose(); }
}

