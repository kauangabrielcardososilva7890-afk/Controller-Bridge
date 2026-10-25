package com.controllerbridge.app;

/** Source classifier separated from Android so virtual keyboard/DPAD cases can be checked locally. */
final class ControllerInputFilter {
    private ControllerInputFilter() { }
    static boolean accepts(boolean virtual, boolean gamepad, boolean joystick, boolean dpad) {
        if (virtual) return false;
        return gamepad || joystick || dpad;
    }
}
