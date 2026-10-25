package com.controllerbridge.app;

/** Selects deliberate movement, so an idle trigger resting at -1 cannot steal a capture. */
public final class AxisCaptureSelector {
    private AxisCaptureSelector() { }

    public record Sample(int source, float baseline, float current, float minimum, float maximum) { }

    public static Integer select(Iterable<Sample> samples) {
        float strongest = .18f;
        Integer selected = null;
        for (Sample sample : samples) {
            float span = sample.maximum() - sample.minimum();
            if (!Float.isFinite(span) || span <= 0 || !Float.isFinite(sample.baseline()) || !Float.isFinite(sample.current())) continue;
            float movement = Math.abs(sample.current() - sample.baseline()) / span;
            if (movement > strongest) {
                strongest = movement;
                selected = sample.source();
            }
        }
        return selected;
    }
}
