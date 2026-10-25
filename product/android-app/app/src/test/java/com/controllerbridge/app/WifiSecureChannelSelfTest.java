package com.controllerbridge.app;

public final class WifiSecureChannelSelfTest {
    private static int checks;
    public static void main(String[] args) throws Exception {
        byte[] key = WifiSecureChannel.fromHex("000102030405060708090a0b0c0d0e0f");
        byte[] clientNonce = WifiSecureChannel.fromHex("202122232425262728292a2b2c2d2e2f303132333435363738393a3b3c3d3e3f");
        WifiSecureChannel.Session client = WifiSecureChannel.createClient(key,
                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", clientNonce, "test-client");
        WifiSecureChannel.Session server = WifiSecureChannel.createServerForChecks(key,
                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", clientNonce, "test-client");
        check(WifiSecureChannel.toHex(client.clientProof).equals("61934e5da6e95ece338c9c8d6500ae0d342e29024f167ad48e4011fa0a15335b"), "client proof matches contract fixture");
        check(WifiSecureChannel.toHex(client.expectedServerProof).equals("af8ae9b94dbe14a58e5e81da336d9baf2f0d854c966fa1de0e57e4a84a42e30a"), "server proof matches contract fixture");
        check(client.verifyServerProof("af8ae9b94dbe14a58e5e81da336d9baf2f0d854c966fa1de0e57e4a84a42e30a"), "server proof validates");
        check(!client.verifyServerProof("00".repeat(32)), "invalid server proof is rejected");
        check(WifiSecureChannel.parsePairingCode("0001-0203 0405060708090a0b0c0d0e0f").length == 16, "pairing code accepts grouping spaces and hyphens");
        WifiSecureChannel.Encrypted outbound = client.encrypt("{\"v\":1,\"type\":\"hello\"}");
        check(outbound.data.equals("zDZeqvNQeItaBPpqXGmVgX9XDtPj7gvnSbyN6bWFIvLV6YwTTzo="), "AES-GCM envelope matches .NET host fixture");
        check(outbound.counter == 1 && server.decrypt(outbound.counter, outbound.data).equals("{\"v\":1,\"type\":\"hello\"}"), "client envelope round trips with independent c2s key");
        WifiSecureChannel.Encrypted reply = server.encrypt("{\"v\":1,\"type\":\"welcome\"}");
        check(reply.counter == 1 && client.decrypt(reply.counter, reply.data).equals("{\"v\":1,\"type\":\"welcome\"}"), "server envelope round trips with independent s2c key");
        boolean replayRejected = false;
        try { client.decrypt(reply.counter, reply.data); } catch (Exception expected) { replayRejected = true; }
        check(replayRejected, "replayed counter is rejected");
        WifiSecureChannel.Session freshClient = WifiSecureChannel.createClient(key,
                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", clientNonce, "test-client");
        WifiSecureChannel.Session freshServer = WifiSecureChannel.createServerForChecks(key,
                "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", clientNonce, "test-client");
        WifiSecureChannel.Encrypted message = freshClient.encrypt("secure");
        byte[] tampered = java.util.Base64.getDecoder().decode(message.data); tampered[0] ^= 1;
        boolean tamperRejected = false;
        try { freshServer.decrypt(message.counter, java.util.Base64.getEncoder().encodeToString(tampered)); } catch (Exception expected) { tamperRejected = true; }
        check(tamperRejected, "modified ciphertext is rejected");
        boolean gapRejected = false;
        try { freshServer.decrypt(2, message.data); } catch (Exception expected) { gapRejected = true; }
        check(gapRejected, "counter gap is rejected");
        boolean invalidCodeRejected = false;
        try { WifiSecureChannel.parsePairingCode("not-a-key"); } catch (IllegalArgumentException expected) { invalidCodeRejected = true; }
        check(invalidCodeRejected, "malformed pairing code is rejected");
        boolean uppercaseNonceRejected = false;
        try { WifiSecureChannel.createClient(key, "A00102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f", clientNonce, "test-client"); }
        catch (IllegalArgumentException expected) { uppercaseNonceRejected = true; }
        check(uppercaseNonceRejected, "noncanonical uppercase server nonce is rejected");
        System.out.println("PASS: " + checks + " Wi-Fi auth/envelope checks.");
    }
    private static void check(boolean condition, String name) {
        if (!condition) throw new AssertionError(name);
        checks++;
    }
}

