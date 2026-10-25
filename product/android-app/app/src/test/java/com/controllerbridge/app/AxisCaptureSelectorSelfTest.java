package com.controllerbridge.app;

import java.util.List;

public final class AxisCaptureSelectorSelfTest {
    public static void main(String[] args) {
        expect(0, List.of(sample(17, -1, -1, -1, 1), sample(0, 0, .8f, -1, 1)), "idle negative trigger must not steal stick capture");
        expect(18, List.of(sample(17, -1, -1, -1, 1), sample(18, -1, .6f, -1, 1)), "right trigger must win independently of left trigger");
        expect(17, List.of(sample(17, 0, .8f, 0, 1), sample(18, 0, 0, 0, 1)), "positive left trigger range");
        expect(null, List.of(sample(17, -1, -1, -1, 1), sample(0, 0, .05f, -1, 1)), "idle and small noise must not capture");
        expect(null, List.of(sample(0, 0, Float.NaN, -1, 1), sample(1, 0, 1, 0, 0)), "invalid ranges and values must not capture");
        expect(11, List.of(sample(0, 0, .7f, -1, 1), sample(11, 0, .9f, -1, 1)), "strongest normalized movement wins");
        System.out.println("PASS: 6 axis capture regression checks.");
    }
    private static AxisCaptureSelector.Sample sample(int axis, float before, float after, float min, float max) {
        return new AxisCaptureSelector.Sample(axis, before, after, min, max);
    }
    private static void expect(Integer expected, List<AxisCaptureSelector.Sample> samples, String reason) {
        if (!java.util.Objects.equals(expected, AxisCaptureSelector.select(samples))) throw new AssertionError(reason);
    }
}
