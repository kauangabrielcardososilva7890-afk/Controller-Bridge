package com.controllerbridge.app;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.Mac;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;

/** Client-side primitives for the authenticated Wi-Fi v1 framing contract. */
final class WifiSecureChannel {
    private static final String PREFIX = "ControllerBridge/WiFi/v1|";
    private WifiSecureChannel() { }

    static byte[] parsePairingCode(String input) {
        if (input == null) throw new IllegalArgumentException("Pairing code is required.");
        String clean = input.replace(" ", "").replace("-", "").toLowerCase(java.util.Locale.ROOT);
        if (clean.length() != 32) throw new IllegalArgumentException("Pairing code must contain 32 hex characters.");
        return fromHex(clean);
    }

    static Session createClient(byte[] pairingKey, String serverNonceHex, byte[] clientNonce, String clientId)
            throws GeneralSecurityException {
        return createSession(pairingKey, serverNonceHex, clientNonce, clientId, true);
    }

    static Session createServerForChecks(byte[] pairingKey, String serverNonceHex, byte[] clientNonce, String clientId)
            throws GeneralSecurityException {
        return createSession(pairingKey, serverNonceHex, clientNonce, clientId, false);
    }

    private static Session createSession(byte[] pairingKey, String serverNonceHex, byte[] clientNonce, String clientId, boolean client)
            throws GeneralSecurityException {
        validateInputs(pairingKey, serverNonceHex, clientNonce, clientId);
        String clientNonceHex = toHex(clientNonce);
        String transcript = PREFIX + serverNonceHex + "|" + clientNonceHex + "|" + clientId;
        byte[] proof = hmac(pairingKey, "client|" + transcript);
        byte[] serverProof = hmac(pairingKey, "server|" + transcript);
        byte[] c2s = hmac(pairingKey, "c2s|" + transcript);
        byte[] s2c = hmac(pairingKey, "s2c|" + transcript);
        return new Session(proof, serverProof, c2s, s2c, client);
    }

    private static void validateInputs(byte[] pairingKey, String serverNonceHex, byte[] clientNonce, String clientId) {
        if (pairingKey == null || pairingKey.length != 16) throw new IllegalArgumentException("Pairing key must be 128 bits.");
        if (serverNonceHex == null || serverNonceHex.length() != 64 || !serverNonceHex.equals(serverNonceHex.toLowerCase(java.util.Locale.ROOT)))
            throw new IllegalArgumentException("Server nonce must be 32 lowercase-hex bytes.");
        fromHex(serverNonceHex);
        if (clientNonce == null || clientNonce.length != 32) throw new IllegalArgumentException("Client nonce must be 32 bytes.");
        if (clientId == null || clientId.isEmpty() || clientId.length() > 128) throw new IllegalArgumentException("Invalid client ID.");
    }

    private static byte[] hmac(byte[] key, String text) throws GeneralSecurityException {
        Mac mac = Mac.getInstance("HmacSHA256");
        mac.init(new SecretKeySpec(key, "HmacSHA256"));
        return mac.doFinal(text.getBytes(StandardCharsets.UTF_8));
    }

    static byte[] fromHex(String text) {
        if (text == null || (text.length() & 1) != 0) throw new IllegalArgumentException("Invalid hex.");
        byte[] result = new byte[text.length() / 2];
        for (int i = 0; i < result.length; i++) {
            int high = Character.digit(text.charAt(i * 2), 16), low = Character.digit(text.charAt(i * 2 + 1), 16);
            if (high < 0 || low < 0) throw new IllegalArgumentException("Invalid hex.");
            result[i] = (byte)((high << 4) | low);
        }
        return result;
    }

    static String toHex(byte[] bytes) {
        char[] chars = new char[bytes.length * 2];
        char[] digits = "0123456789abcdef".toCharArray();
        for (int i = 0; i < bytes.length; i++) {
            int value = bytes[i] & 0xff;
            chars[i * 2] = digits[value >>> 4]; chars[i * 2 + 1] = digits[value & 15];
        }
        return new String(chars);
    }

    static final class Session implements AutoCloseable {
        final byte[] clientProof;
        final byte[] expectedServerProof;
        private final byte[] txKey;
        private final byte[] rxKey;
        private final String txDirection;
        private final String rxDirection;
        private long txCounter;
        private long rxCounter;
        private boolean destroyed;

        private Session(byte[] clientProof, byte[] serverProof, byte[] c2s, byte[] s2c, boolean client) {
            this.clientProof = clientProof; expectedServerProof = serverProof;
            txKey = client ? c2s : s2c; rxKey = client ? s2c : c2s;
            txDirection = client ? "c2s" : "s2c"; rxDirection = client ? "s2c" : "c2s";
        }

        boolean verifyServerProof(String receivedHex) {
            if (destroyed) return false;
            try { return MessageDigest.isEqual(expectedServerProof, fromHex(receivedHex)); }
            catch (IllegalArgumentException invalid) { return false; }
        }

        synchronized Encrypted encrypt(String plaintextJson) throws GeneralSecurityException {
            ensureOpen();
            if (txCounter == Long.MAX_VALUE) throw new GeneralSecurityException("Secure message counter exhausted.");
            long counter = ++txCounter;
            byte[] plain = plaintextJson.getBytes(StandardCharsets.UTF_8);
            if (plain.length > 65536) throw new GeneralSecurityException("Secure message is too large.");
            Cipher cipher = cipher(Cipher.ENCRYPT_MODE, txKey, txDirection, counter);
            return new Encrypted(counter, Base64.getEncoder().encodeToString(cipher.doFinal(plain)));
        }

        synchronized String decrypt(long counter, String encoded) throws GeneralSecurityException {
            ensureOpen();
            if (rxCounter == Long.MAX_VALUE || counter != rxCounter + 1 || counter < 1)
                throw new GeneralSecurityException("Unexpected secure message counter.");
            if (encoded == null || encoded.length() > 131072) throw new GeneralSecurityException("Secure envelope is too large.");
            byte[] cipherText;
            try { cipherText = Base64.getDecoder().decode(encoded); }
            catch (IllegalArgumentException invalid) { throw new GeneralSecurityException("Invalid secure envelope encoding.", invalid); }
            if (cipherText.length < 16) throw new GeneralSecurityException("Invalid secure envelope tag.");
            Cipher cipher = cipher(Cipher.DECRYPT_MODE, rxKey, rxDirection, counter);
            byte[] plain = cipher.doFinal(cipherText);
            if (plain.length > 65536) throw new GeneralSecurityException("Secure message is too large.");
            rxCounter = counter;
            return new String(plain, StandardCharsets.UTF_8);
        }

        private static Cipher cipher(int mode, byte[] key, String direction, long counter) throws GeneralSecurityException {
            byte[] nonce = ByteBuffer.allocate(12).order(ByteOrder.BIG_ENDIAN).putInt(0).putLong(counter).array();
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(mode, new SecretKeySpec(key, "AES"), new GCMParameterSpec(128, nonce));
            cipher.updateAAD((PREFIX + direction + "|" + counter).getBytes(StandardCharsets.UTF_8));
            return cipher;
        }

        private void ensureOpen() throws GeneralSecurityException {
            if (destroyed) throw new GeneralSecurityException("Secure channel is closed.");
        }

        @Override public synchronized void close() {
            if (destroyed) return;
            destroyed = true;
            java.util.Arrays.fill(clientProof, (byte)0);
            java.util.Arrays.fill(expectedServerProof, (byte)0);
            java.util.Arrays.fill(txKey, (byte)0);
            java.util.Arrays.fill(rxKey, (byte)0);
        }
    }

    static final class Encrypted {
        final long counter;
        final String data;
        Encrypted(long counter, String data) { this.counter = counter; this.data = data; }
    }
}

