package com.controllerbridge.app;

import android.os.Handler;
import android.os.Looper;
import org.json.JSONException;
import org.json.JSONObject;
import java.io.*;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

final class BridgeClient implements AutoCloseable {
    interface Listener {
        void onConnected(BridgeClient source);
        void onMessage(BridgeClient source, JSONObject message);
        void onDisconnected(BridgeClient source, String reason);
    }
    private final Listener listener;
    private final BridgeLifecycle lifecycle = new BridgeLifecycle();
    private final Handler main = new Handler(Looper.getMainLooper());
    private final ExecutorService readerWorker = Executors.newSingleThreadExecutor(r -> daemon(r, "controller-bridge-reader"));
    private final ExecutorService writerWorker = Executors.newSingleThreadExecutor(r -> daemon(r, "controller-bridge-writer"));
    private final Object queueLock = new Object();
    private final ArrayDeque<JSONObject> controls = new ArrayDeque<>();
    private final LinkedHashMap<String, JSONObject> latestStates = new LinkedHashMap<>();
    private volatile Socket socket;
    private volatile BufferedWriter writer;
    private volatile boolean closed;
    private boolean writerLoopStarted;
    private final AtomicBoolean disconnectNotified = new AtomicBoolean();

    private static Thread daemon(Runnable r, String name) { Thread t = new Thread(r, name); t.setDaemon(true); return t; }
    BridgeClient(Listener listener) { this.listener = listener; }

    void connect(String host, int port, String installId) {
        if (!lifecycle.claimConnect()) return;
        try { readerWorker.execute(() -> {
            String reason = "Conexão encerrada.";
            try {
                Socket connected = new Socket(); connected.setTcpNoDelay(true);
                synchronized(queueLock) {
                    if (closed || !lifecycle.canContinueSetup()) { connected.close(); return; }
                    socket = connected;
                }
                connected.connect(new InetSocketAddress(host, port), 2500); connected.setSoTimeout(0);
                synchronized(queueLock) {
                    if (closed || !lifecycle.canContinueSetup()) throw new IOException("Conexão cancelada.");
                    writer = new BufferedWriter(new OutputStreamWriter(connected.getOutputStream(), StandardCharsets.UTF_8));
                    if (!writerLoopStarted) { writerLoopStarted = true; writerWorker.execute(this::writerLoop); }
                }
                enqueueControl(new JSONObject().put("v",1).put("type","hello").put("client_id",installId).put("app_version","0.1.0"));
                BufferedReader reader = new BufferedReader(new InputStreamReader(connected.getInputStream(), StandardCharsets.UTF_8));
                String line; boolean welcomed = false;
                while (!closed && (line = reader.readLine()) != null) {
                    JSONObject msg = new JSONObject(line); String type = msg.optString("type", "");
                    if (!welcomed) {
                        if (!"welcome".equals(type) || msg.optInt("v", -1) != 1) throw new IOException("O host não aceitou o protocolo Controller Bridge v1.");
                        welcomed = true; main.post(() -> listener.onConnected(this));
                    } else main.post(() -> listener.onMessage(this, msg));
                }
                if (!closed && !welcomed) reason = "O PC encerrou antes de confirmar a conexão.";
            } catch (IOException | JSONException | RejectedExecutionException error) { reason = error.getMessage() == null ? error.getClass().getSimpleName() : error.getMessage(); }
            finally { closeSocket(); writerWorker.shutdownNow(); readerWorker.shutdown(); if (!closed) notifyDisconnected(reason); }
        }); } catch (RejectedExecutionException rejected) { lifecycle.cancel(); closed=true; notifyDisconnected("Conexão cancelada."); }
    }

    void send(JSONObject message) {
        if (closed) return;
        String type = message.optString("type", "");
        if ("state".equals(type)) {
            String id = message.optString("controller_id", "");
            synchronized (queueLock) { if (closed) return; latestStates.put(id, message); queueLock.notifyAll(); }
        } else enqueueControl(message);
    }
    private void enqueueControl(JSONObject message) {
        synchronized (queueLock) { if (closed) return; controls.addLast(message); queueLock.notifyAll(); }
    }
    private void writerLoop() {
        try {
            while (true) {
                JSONObject next;
                synchronized (queueLock) {
                    while (!closed && controls.isEmpty() && latestStates.isEmpty()) queueLock.wait();
                    if (closed && controls.isEmpty() && latestStates.isEmpty()) return;
                    next = controls.pollFirst();
                    if (next == null) { Iterator<Map.Entry<String,JSONObject>> it=latestStates.entrySet().iterator(); next=it.next().getValue(); it.remove(); }
                }
                if ("detach".equals(next.optString("type"))) {
                    JSONObject neutral;
                    synchronized (queueLock) { neutral=latestStates.remove(next.optString("controller_id", "")); }
                    if (neutral != null && isNeutral(neutral)) writeNow(neutral);
                }
                String type=next.optString("type", "");
                if ("__close".equals(type)) { closeSocket(); return; }
                if (BridgeLifecycle.isWireMessage(type)) writeNow(next);
                if ("detach".equals(type)) synchronized (queueLock) { latestStates.remove(next.optString("controller_id", "")); }
            }
        } catch (InterruptedException interrupted) { Thread.currentThread().interrupt(); }
        catch (IOException error) { closed=true; closeSocket(); readerWorker.shutdownNow(); notifyDisconnected(error.getMessage()==null?"Falha ao enviar dados.":error.getMessage()); }
    }
    private void writeNow(JSONObject value) throws IOException { BufferedWriter out=writer; if(out==null)throw new IOException("O socket ainda não está pronto."); out.write(value.toString());out.write('\n');out.flush(); }
    private static boolean isNeutral(JSONObject state) {
        JSONObject buttons=state.optJSONObject("buttons"), axes=state.optJSONObject("axes");
        if(buttons==null||axes==null)return false;
        for(java.util.Iterator<String> it=buttons.keys();it.hasNext();) if(buttons.optBoolean(it.next(),false))return false;
        for(java.util.Iterator<String> it=axes.keys();it.hasNext();) if(Math.abs(axes.optDouble(it.next(),1d))>.0001)return false;
        return true;
    }
    private void closeSocket() { Socket current=socket; socket=null; writer=null; if(current!=null) try{current.close();}catch(IOException ignored){} }
    private void notifyDisconnected(String reason) { if(disconnectNotified.compareAndSet(false,true)) main.post(()->listener.onDisconnected(this,reason)); }

    void closeAfterFlush() {
        if (closed) return;
        boolean hadWriter;
        synchronized(queueLock) {
            if(closed)return;
            closed=true; lifecycle.cancel();
            hadWriter=writerLoopStarted;
            if(hadWriter) {
                latestStates.entrySet().removeIf(e -> !isNeutral(e.getValue()));
                try { controls.addLast(new JSONObject().put("type","__close")); } catch(JSONException impossible) { closeSocket(); }
            }
            queueLock.notifyAll();
        }
        if (!hadWriter) { closeSocket(); readerWorker.shutdownNow(); writerWorker.shutdownNow(); return; }
        // A neutral/detach queue is intentionally bounded by the caller's brief flush window.
        try { writerWorker.shutdown(); if(!writerWorker.awaitTermination(200,TimeUnit.MILLISECONDS)){ closeSocket(); writerWorker.shutdownNow(); } }
        catch(InterruptedException e){Thread.currentThread().interrupt();closeSocket();writerWorker.shutdownNow();}
        readerWorker.shutdownNow();
    }
    @Override public void close() { synchronized(queueLock){closed=true;lifecycle.cancel();controls.clear();latestStates.clear();queueLock.notifyAll();} closeSocket(); readerWorker.shutdownNow(); writerWorker.shutdownNow(); }
}
