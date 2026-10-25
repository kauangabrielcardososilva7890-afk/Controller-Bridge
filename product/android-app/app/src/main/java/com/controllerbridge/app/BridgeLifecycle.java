package com.controllerbridge.app;

import java.util.concurrent.atomic.AtomicBoolean;

/** Small cancellation gate shared by connection setup and close paths. */
final class BridgeLifecycle {
    private final AtomicBoolean cancelled = new AtomicBoolean();
    private final AtomicBoolean connectClaimed = new AtomicBoolean();

    boolean claimConnect() { return !cancelled.get() && connectClaimed.compareAndSet(false, true); }
    boolean canContinueSetup() { return !cancelled.get(); }
    void cancel() { cancelled.set(true); }
    static boolean isWireMessage(String type) { return type != null && !"__close".equals(type); }
}
