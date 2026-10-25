package com.controllerbridge.app;

import android.content.Context;
import android.content.SharedPreferences;
import android.view.InputDevice;

import org.json.JSONException;
import org.json.JSONObject;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HashSet;
import java.util.Set;
import java.util.UUID;

final class PhoneProfileStore {
    static final class CachedProfile {
        final int revision;
        final String output;
        final boolean gyroEnabled;
        final JSONObject mapping;
        CachedProfile(int revision, String output, boolean gyroEnabled, JSONObject mapping) {
            this.revision = revision; this.output = output; this.gyroEnabled = gyroEnabled; this.mapping = mapping;
        }
    }

    private final SharedPreferences preferences;
    PhoneProfileStore(Context context) {
        preferences = context.getSharedPreferences("controller-bridge-profiles", Context.MODE_PRIVATE);
    }

    synchronized String profileId(InputDevice device, Set<String> activeProfileIds) {
        String descriptor = descriptor(device);
        String instanceKey = "instance:" + digest(descriptor) + ":" + device.getId();
        String known = preferences.getString(instanceKey, null);
        if (known != null && !activeProfileIds.contains(known)) return known;

        String baseKey = "base:" + digest(descriptor);
        String base = preferences.getString(baseKey, null);
        if (base != null && !activeProfileIds.contains(base)) {
            preferences.edit().putString(instanceKey, base).apply();
            return base;
        }

        String prefix = "instance:" + digest(descriptor) + ":";
        for (String key : preferences.getAll().keySet()) {
            if (key.startsWith(prefix)) {
                String candidate = preferences.getString(key, null);
                if (candidate != null && !activeProfileIds.contains(candidate)) {
                    preferences.edit().putString(instanceKey, candidate).apply();
                    return candidate;
                }
            }
        }

        String created = ProfileIdAllocator.allocateUnique(UUID::randomUUID, activeProfileIds);
        SharedPreferences.Editor edit = preferences.edit().putString(instanceKey, created);
        if (base == null) edit.putString(baseKey, created);
        edit.apply();
        return created;
    }

    synchronized CachedProfile get(String profileId) {
        String raw = preferences.getString("profile:" + profileId, null);
        if (raw == null) return null;
        try {
            JSONObject value = new JSONObject(raw);
            return new CachedProfile(value.optInt("revision", 1), value.optString("output", "xbox360"),
                    value.optBoolean("gyro_enabled", false), value.optJSONObject("mapping"));
        } catch (JSONException invalid) { return null; }
    }

    synchronized void put(String profileId, int revision, String output, JSONObject mapping) {
        put(profileId, revision, output, false, mapping);
    }

    synchronized void put(String profileId, int revision, String output, boolean gyroEnabled, JSONObject mapping) {
        try {
            JSONObject value = new JSONObject().put("revision", revision).put("output", output)
                    .put("gyro_enabled", gyroEnabled).put("mapping", mapping);
            preferences.edit().putString("profile:" + profileId, value.toString()).apply();
        } catch (JSONException impossible) { throw new IllegalStateException(impossible); }
    }

    synchronized int getLightbarColor(String profileId, int fallback) {
        return preferences.getInt("lightbar:" + profileId, fallback);
    }

    synchronized void putLightbarColor(String profileId, int color) {
        preferences.edit().putInt("lightbar:" + profileId, color).apply();
    }

    static String descriptor(InputDevice device) {
        String descriptor = device.getDescriptor();
        if (descriptor == null || descriptor.isBlank())
            descriptor = device.getName() + ":" + device.getVendorId() + ":" + device.getProductId();
        return descriptor;
    }

    private static String digest(String text) {
        try {
            byte[] bytes = MessageDigest.getInstance("SHA-256").digest(text.getBytes(StandardCharsets.UTF_8));
            StringBuilder out = new StringBuilder(bytes.length * 2);
            for (byte value : bytes) out.append(String.format(java.util.Locale.ROOT, "%02x", value));
            return out.toString();
        } catch (NoSuchAlgorithmException impossible) { throw new IllegalStateException(impossible); }
    }
}

