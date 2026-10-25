package com.controllerbridge.app;

import java.util.Set;
import java.util.UUID;
import java.util.function.Supplier;

final class ProfileIdAllocator {
    private ProfileIdAllocator() { }
    static String allocateUnique(Supplier<UUID> ids, Set<String> active) {
        String candidate;
        do { candidate = ids.get().toString(); } while (active.contains(candidate));
        return candidate;
    }
}
