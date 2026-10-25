package com.controllerbridge.app;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.function.IntToDoubleFunction;

/** Android key/axis identifiers mapped into the transport's logical controller layout. */
public final class MappingModel {
    public static final List<String> BUTTONS = List.of(
            "south", "east", "west", "north", "l1", "r1", "l2", "r2", "share", "options",
            "l3", "r3", "ps", "dpad_up", "dpad_down", "dpad_left", "dpad_right");
    public static final List<String> AXES = List.of("lx", "ly", "rx", "ry", "lt", "rt");

    public static Map<String, Boolean> neutralButtons() {
        Map<String, Boolean> result = new LinkedHashMap<>();
        for (String name : BUTTONS) result.put(name, false);
        return result;
    }

    public static Map<String, Float> neutralAxes() {
        Map<String, Float> result = new LinkedHashMap<>();
        for (String name : AXES) result.put(name, 0f);
        return result;
    }

    public static final class AxisBinding {
        public final int source;
        public final boolean invert;
        public final float min;
        public final float center;
        public final float max;
        public final float deadZone;

        public AxisBinding(int source, boolean invert, float min, float center, float max, float deadZone) {
            if (!Float.isFinite(min) || !Float.isFinite(center) || !Float.isFinite(max) || min >= max || center < min || center > max)
                throw new IllegalArgumentException("Limites de eixo inválidos.");
            if (!Float.isFinite(deadZone) || deadZone < 0 || deadZone >= 1) throw new IllegalArgumentException("Zona morta inválida.");
            this.source = source;
            this.invert = invert;
            this.min = min;
            this.center = center;
            this.max = max;
            this.deadZone = deadZone;
        }

        public float normalize(float raw, boolean trigger) {
            if (!Float.isFinite(raw)) return 0f;
            float value;
            if (trigger) {
                value = clamp((raw - min) / (max - min), 0f, 1f);
                value = value <= deadZone ? 0f : (value - deadZone) / (1f - deadZone);
                if (invert) value = 1f - value;
            } else {
                raw = clamp(raw, min, max);
                value = raw < center
                        ? (raw - center) / Math.max(0.0001f, center - min)
                        : (raw - center) / Math.max(0.0001f, max - center);
                if (invert) value = -value;
                float magnitude = Math.abs(value);
                if (magnitude <= deadZone) return 0f;
                value = Math.copySign((magnitude - deadZone) / (1f - deadZone), value);
            }
            return clamp(value, trigger ? 0f : -1f, 1f);
        }
    }

    private final Map<String, Integer> buttonSources;
    private final Map<String, AxisBinding> axisBindings;
    private final int hatXSource;
    private final int hatYSource;
    private final float hatThreshold;

    public MappingModel(Map<String, Integer> buttonSources, Map<String, AxisBinding> axisBindings,
                        int hatXSource, int hatYSource, float hatThreshold) {
        this.buttonSources = new LinkedHashMap<>();
        for (Map.Entry<String, Integer> entry : buttonSources.entrySet()) {
            if (BUTTONS.contains(entry.getKey()) && entry.getValue() != null && entry.getValue() >= 0)
                this.buttonSources.put(entry.getKey(), entry.getValue());
        }
        this.axisBindings = new LinkedHashMap<>();
        for (Map.Entry<String, AxisBinding> entry : axisBindings.entrySet()) {
            if (AXES.contains(entry.getKey()) && entry.getValue() != null) this.axisBindings.put(entry.getKey(), entry.getValue());
        }
        if (!Float.isFinite(hatThreshold) || hatThreshold <= 0 || hatThreshold > 1) throw new IllegalArgumentException("Limite da cruz digital inválido.");
        this.hatXSource = hatXSource;
        this.hatYSource = hatYSource;
        this.hatThreshold = hatThreshold;
    }

    public static MappingModel standard() {
        Map<String, Integer> buttons = new LinkedHashMap<>();
        int[] keys = {96,97,99,100,102,103,104,105,109,108,106,107,110,19,20,21,22};
        for (int i = 0; i < BUTTONS.size(); i++) buttons.put(BUTTONS.get(i), keys[i]);
        Map<String, AxisBinding> axes = new LinkedHashMap<>();
        axes.put("lx", new AxisBinding(0, false, -1, 0, 1, .08f));
        axes.put("ly", new AxisBinding(1, false, -1, 0, 1, .08f));
        axes.put("rx", new AxisBinding(11, false, -1, 0, 1, .08f));
        axes.put("ry", new AxisBinding(14, false, -1, 0, 1, .08f));
        axes.put("lt", new AxisBinding(17, false, 0, 0, 1, .02f));
        axes.put("rt", new AxisBinding(18, false, 0, 0, 1, .02f));
        return new MappingModel(buttons, axes, 15, 16, .5f);
    }

    public MappingModel withButton(String logical, int keyCode) {
        requireButton(logical);
        Map<String, Integer> updated = new LinkedHashMap<>(buttonSources);
        updated.put(logical, keyCode);
        return new MappingModel(updated, axisBindings, hatXSource, hatYSource, hatThreshold);
    }

    public MappingModel withAxis(String logical, AxisBinding binding) {
        if (!AXES.contains(logical)) throw new IllegalArgumentException("Eixo desconhecido: " + logical);
        Map<String, AxisBinding> updated = new LinkedHashMap<>(axisBindings);
        updated.put(logical, binding);
        return new MappingModel(buttonSources, updated, hatXSource, hatYSource, hatThreshold);
    }

    public MappingModel withInverted(String logical, boolean invert) {
        AxisBinding old = axisBindings.get(logical);
        if (old == null) throw new IllegalArgumentException("Eixo sem mapeamento: " + logical);
        return withAxis(logical, new AxisBinding(old.source, invert, old.min, old.center, old.max, old.deadZone));
    }

    public MappingModel withCalibration(String logical, float min, float center, float max, float deadZone) {
        AxisBinding old = axisBindings.get(logical);
        if (old == null) throw new IllegalArgumentException("Eixo sem mapeamento: " + logical);
        return withAxis(logical, new AxisBinding(old.source, old.invert, min, center, max, deadZone));
    }

    public Map<String, Boolean> mapButtons(Set<Integer> pressed, float hatX, float hatY) {
        Map<String, Boolean> result = new LinkedHashMap<>();
        for (String logical : BUTTONS) {
            Integer code = buttonSources.get(logical);
            result.put(logical, code != null && pressed.contains(code));
        }
        if (hatX <= -hatThreshold) result.put("dpad_left", true);
        if (hatX >= hatThreshold) result.put("dpad_right", true);
        if (hatY <= -hatThreshold) result.put("dpad_up", true);
        if (hatY >= hatThreshold) result.put("dpad_down", true);
        return result;
    }

    public Map<String, Float> mapAxes(IntToDoubleFunction axisValue) {
        Map<String, Float> result = new LinkedHashMap<>();
        for (String name : AXES) {
            AxisBinding binding = axisBindings.get(name);
            float value = binding == null ? 0f : (float) axisValue.applyAsDouble(binding.source);
            result.put(name, binding == null ? 0f : binding.normalize(value, name.equals("lt") || name.equals("rt")));
        }
        return result;
    }

    public Map<String, Integer> buttonSources() { return Collections.unmodifiableMap(buttonSources); }
    public Map<String, AxisBinding> axisBindings() { return Collections.unmodifiableMap(axisBindings); }
    public int hatXSource() { return hatXSource; }
    public int hatYSource() { return hatYSource; }
    public float hatThreshold() { return hatThreshold; }

    private static void requireButton(String logical) {
        if (!BUTTONS.contains(logical)) throw new IllegalArgumentException("Botão desconhecido: " + logical);
    }
    private static float clamp(float value, float min, float max) { return Math.max(min, Math.min(max, value)); }
}
