package com.controllerbridge.app;

import java.util.List;

/** Returns a sensor candidate only when its link to a DS4 controller is unambiguous. */
final class MotionDeviceMatcher {
    private MotionDeviceMatcher() { }

    static int choose(String selectedDescriptor, int matchingDs4Controllers, int totalDs4Controllers,
                      List<String> sensorDescriptors, List<Boolean> isDs4Sensor) {
        if (selectedDescriptor != null && !selectedDescriptor.isBlank() && matchingDs4Controllers == 1) {
            int exact = -1;
            for (int i = 0; i < sensorDescriptors.size(); i++) {
                if (selectedDescriptor.equals(sensorDescriptors.get(i))) {
                    if (exact >= 0) return -1;
                    exact = i;
                }
            }
            if (exact >= 0) return exact;
        }
        if (totalDs4Controllers != 1) return -1;
        int only = -1;
        for (int i = 0; i < isDs4Sensor.size(); i++) if (isDs4Sensor.get(i)) {
            if (only >= 0) return -1;
            only = i;
        }
        return only;
    }
}
