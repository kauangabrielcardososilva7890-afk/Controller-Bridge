package com.controllerbridge.app;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Arrays;
import java.util.Base64;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Stores each host pairing key encrypted with an app-private Android Keystore key. */
final class WifiPairingStore {
    private static final String ALIAS = "controller-bridge-wifi-pairing-v1";
    private final SharedPreferences preferences;

    WifiPairingStore(Context context) {
        preferences = context.getSharedPreferences("controller-bridge-wifi-pairings", Context.MODE_PRIVATE);
    }

    synchronized void put(String host, byte[] pairingKey) throws Exception {
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, key());
        cipher.updateAAD(host.getBytes(StandardCharsets.UTF_8));
        byte[] encrypted = cipher.doFinal(pairingKey);
        byte[] packed = new byte[cipher.getIV().length + encrypted.length];
        System.arraycopy(cipher.getIV(), 0, packed, 0, cipher.getIV().length);
        System.arraycopy(encrypted, 0, packed, cipher.getIV().length, encrypted.length);
        preferences.edit().putString(keyName(host), Base64.getEncoder().encodeToString(packed)).apply();
        Arrays.fill(encrypted, (byte)0);
    }

    synchronized byte[] get(String host) {
        String encoded = preferences.getString(keyName(host), null);
        if (encoded == null) return null;
        try {
            byte[] packed = Base64.getDecoder().decode(encoded);
            if (packed.length < 12 + 16 + 16) throw new IllegalArgumentException("Corrupt pairing key.");
            byte[] iv = Arrays.copyOfRange(packed, 0, 12);
            byte[] encrypted = Arrays.copyOfRange(packed, 12, packed.length);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, key(), new GCMParameterSpec(128, iv));
            cipher.updateAAD(host.getBytes(StandardCharsets.UTF_8));
            return cipher.doFinal(encrypted);
        } catch (Exception invalidOrLostKey) {
            preferences.edit().remove(keyName(host)).apply();
            return null;
        }
    }

    synchronized void remove(String host) { preferences.edit().remove(keyName(host)).apply(); }

    private SecretKey key() throws Exception {
        KeyStore store = KeyStore.getInstance("AndroidKeyStore");
        store.load(null);
        java.security.Key existing = store.getKey(ALIAS, null);
        if (existing instanceof SecretKey) return (SecretKey)existing;
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore");
        generator.init(new KeyGenParameterSpec.Builder(ALIAS,
                KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setRandomizedEncryptionRequired(true)
                .build());
        return generator.generateKey();
    }

    private static String keyName(String host) { return "host:" + host; }
}

