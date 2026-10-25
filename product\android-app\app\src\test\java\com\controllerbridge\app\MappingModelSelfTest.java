package com.controllerbridge.app;

import java.util.Map;
import java.util.List;
import java.util.Set;

public final class MappingModelSelfTest {
    private static int checks;
    public static void main(String[] args) {
        MappingModel base = MappingModel.standard();
        check(base.buttonSources().get("south") == 96, "standard south key");
        check(base.axisBindings().get("lx").source == 0, "standard left X axis");
        check(base.axisBindings().get("rt").source == 18, "standard right trigger axis");

        MappingModel edited = base.withButton("south", 188);
        check(edited.buttonSources().get("south") == 188, "single button remap applied");
        check(edited.buttonSources().get("east").equals(base.buttonSources().get("east")), "other buttons preserved");
        check(edited.axisBindings().get("lx").source == base.axisBindings().get("lx").source, "axes preserved on button edit");

        MappingModel inverted = base.withInverted("ly", true);
        check(inverted.axisBindings().get("ly").invert, "axis inversion applied");
        check(!inverted.axisBindings().get("lx").invert, "other axis inversion preserved");
        check(base.axisBindings().get("ly").normalize(-1, false) == -1f, "Y positive-up raw normalized before inversion");
        check(inverted.axisBindings().get("ly").normalize(-1, false) == 1f, "axis inversion flips direction");

        MappingModel.AxisBinding calibrated = new MappingModel.AxisBinding(0, false, -2f, 0f, 2f, .1f);
        check(calibrated.normalize(0, false) == 0f, "center remains neutral");
        check(calibrated.normalize(.1f, false) == 0f, "dead zone filters noise");
        check(Math.abs(calibrated.normalize(2f, false) - 1f) < .0001f, "calibrated maximum clamps to one");
        MappingModel.AxisBinding trigger = new MappingModel.AxisBinding(17, false, 0f, 0f, 1f, .05f);
        check(trigger.normalize(0f, true) == 0f, "trigger released is zero");
        check(Math.abs(trigger.normalize(1f, true) - 1f) < .0001f, "trigger pressed is one");
        MappingModel.AxisBinding invertedTrigger = new MappingModel.AxisBinding(17, true, 0f, 0f, 1f, .05f);
        check(invertedTrigger.normalize(0f, true) == 1f, "inverted trigger release maps to one");
        check(invertedTrigger.normalize(1f, true) == 0f, "inverted trigger press maps to zero");
        check(Math.abs(invertedTrigger.normalize(.5f, true) - .5263158f) < .001f, "inverted trigger midpoint reverses after dead zone");
        Map<String, Float> neutral = MappingModel.neutralAxes();
        check(neutral.size() == MappingModel.AXES.size() && neutral.values().stream().allMatch(value -> value == 0f),
                "focus-loss neutral snapshot keeps inverted triggers at logical zero");

        String same = "00000000-0000-0000-0000-000000000001";
        java.util.ArrayDeque<java.util.UUID> ids = new java.util.ArrayDeque<>();
        ids.add(java.util.UUID.fromString(same)); ids.add(java.util.UUID.fromString("00000000-0000-0000-0000-000000000002"));
        String allocated = ProfileIdAllocator.allocateUnique(ids::removeFirst, Set.of(same));
        check(allocated.endsWith("0002"), "active profile UUID is skipped for another live controller");

        BridgeLifecycle canceled = new BridgeLifecycle();
        canceled.cancel();
        check(!canceled.claimConnect() && !canceled.canContinueSetup(), "cancel before connect setup prevents socket and writer start");
        check(!BridgeLifecycle.isWireMessage("__close") && BridgeLifecycle.isWireMessage("detach"), "internal close marker is never a wire message");
        boolean cancellationRacesSafe = true;
        for (int i = 0; i < 100; i++) {
            BridgeLifecycle gate = new BridgeLifecycle();
            java.util.concurrent.CountDownLatch start = new java.util.concurrent.CountDownLatch(1);
            Thread claim = new Thread(() -> { try { start.await(); gate.claimConnect(); } catch (InterruptedException e) { Thread.currentThread().interrupt(); } });
            Thread cancel = new Thread(() -> { try { start.await(); gate.cancel(); } catch (InterruptedException e) { Thread.currentThread().interrupt(); } });
            claim.start(); cancel.start(); start.countDown();
            try { claim.join(); cancel.join(); } catch (InterruptedException e) { throw new AssertionError(e); }
            cancellationRacesSafe &= !gate.canContinueSetup() && !gate.claimConnect();
        }
        check(cancellationRacesSafe, "concurrent connect/cancel always closes the setup gate");

        check(!ControllerInputFilter.accepts(true, false, false, true), "virtual keyboard/DPAD is rejected as controller");
        check(ControllerInputFilter.accepts(false, false, false, true), "physical D-pad controller remains supported");
        check(ControllerInputFilter.accepts(false, true, false, false)
                        && ControllerInputFilter.accepts(false, false, true, false),
                "physical gamepad and joystick remain supported");
        check(MotionDeviceMatcher.choose("ds4-a",1,2,List.of("ds4-b","ds4-a"),List.of(true,true))==1,
                "gyro follows exact descriptor when multiple DS4 profiles exist");
        check(MotionDeviceMatcher.choose("ds4-a",1,1,List.of("other"),List.of(true))==0,
                "gyro fallback pairs only one DS4 with one sensor");
        check(MotionDeviceMatcher.choose("ds4-a",1,1,List.of("one","two"),List.of(true,true))==-1,
                "gyro fallback refuses multiple sensors");
        check(MotionDeviceMatcher.choose("ds4-a",1,2,List.of("other"),List.of(true))==-1,
                "gyro fallback refuses multiple DS4 controllers");

        Map<String, Boolean> hat = base.mapButtons(Set.of(), -1f, 1f);
        check(hat.get("dpad_left") && hat.get("dpad_down"), "hat axis maps diagonal D-pad");
        Map<String, Float> axes = base.mapAxes(id -> id == 0 ? .6 : 0);
        check(Math.abs(axes.get("lx") - .5652174f) < .001f, "stick dead-zone output is rescaled");
        System.out.println("PASS: " + checks + " verificações de mapeamento/calibração.");
    }
    private static void check(boolean condition, String name) {
        checks++;
        if (!condition) throw new AssertionError(name);
    }
}
