package com.controllerbridge.app;

import org.json.JSONException;
import org.json.JSONObject;

import java.util.LinkedHashMap;
import java.util.Iterator;
import java.util.Map;

public final class MappingCodec {
    private MappingCodec() { }

    public static JSONObject encode(MappingModel mapping) throws JSONException {
        JSONObject root = new JSONObject();
        root.put("version", 1);
        JSONObject buttons = new JSONObject();
        for (Map.Entry<String, Integer> entry : mapping.buttonSources().entrySet()) buttons.put(entry.getKey(), entry.getValue());
        root.put("buttons", buttons);
        JSONObject axes = new JSONObject();
        for (Map.Entry<String, MappingModel.AxisBinding> entry : mapping.axisBindings().entrySet()) {
            MappingModel.AxisBinding b = entry.getValue();
            JSONObject value = new JSONObject();
            value.put("source", b.source); value.put("invert", b.invert);
            value.put("min", b.min); value.put("center", b.center); value.put("max", b.max); value.put("dead_zone", b.deadZone);
            axes.put(entry.getKey(), value);
        }
        root.put("axes", axes);
        root.put("hat", new JSONObject().put("x_source", mapping.hatXSource()).put("y_source", mapping.hatYSource()).put("threshold", mapping.hatThreshold()));
        return root;
    }

    public static MappingModel decode(JSONObject root) throws JSONException {
        if (root == null || root.optInt("version", 1) != 1) throw new JSONException("Versão do mapeamento não suportada.");
        MappingModel defaults = MappingModel.standard();
        Map<String, Integer> buttons = new LinkedHashMap<>(defaults.buttonSources());
        JSONObject buttonObject = root.optJSONObject("buttons");
        if (buttonObject != null) {
            for (Iterator<String> keys = buttonObject.keys(); keys.hasNext();) {
                String logical = keys.next();
                Object value = buttonObject.get(logical);
                if (value instanceof Number number && MappingModel.BUTTONS.contains(logical)) buttons.put(logical, number.intValue());
            }
        }
        Map<String, MappingModel.AxisBinding> axes = new LinkedHashMap<>(defaults.axisBindings());
        JSONObject axisObject = root.optJSONObject("axes");
        if (axisObject != null) {
            for (Iterator<String> keys = axisObject.keys(); keys.hasNext();) {
                String logical = keys.next();
                JSONObject value = axisObject.optJSONObject(logical);
                if (value == null || !MappingModel.AXES.contains(logical)) continue;
                MappingModel.AxisBinding standard = axes.get(logical);
                axes.put(logical, new MappingModel.AxisBinding(
                        value.optInt("source", standard.source), value.optBoolean("invert", standard.invert),
                        (float)value.optDouble("min", standard.min), (float)value.optDouble("center", standard.center),
                        (float)value.optDouble("max", standard.max), (float)value.optDouble("dead_zone", standard.deadZone)));
            }
        }
        JSONObject hat = root.optJSONObject("hat");
        return new MappingModel(buttons, axes,
                hat == null ? defaults.hatXSource() : hat.optInt("x_source", defaults.hatXSource()),
                hat == null ? defaults.hatYSource() : hat.optInt("y_source", defaults.hatYSource()),
                hat == null ? defaults.hatThreshold() : (float)hat.optDouble("threshold", defaults.hatThreshold()));
    }
}
