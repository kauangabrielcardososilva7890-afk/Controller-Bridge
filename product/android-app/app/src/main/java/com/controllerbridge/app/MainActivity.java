package com.controllerbridge.app;

import android.app.Activity;
import android.content.Context;
import android.hardware.input.InputManager;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.hardware.lights.Light;
import android.hardware.lights.LightsManager;
import android.hardware.lights.LightsRequest;
import android.hardware.lights.LightState;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.Gravity;
import android.view.WindowManager;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;
import android.widget.Switch;
import android.widget.SeekBar;
import android.app.AlertDialog;
import android.text.InputType;

import org.json.JSONException;
import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;

public final class MainActivity extends Activity implements InputManager.InputDeviceListener, BridgeClient.Listener {
    private static final int PORT = 47652;
    private static final int WIFI_PORT = 47653;
    private static final long SNAPSHOT_MS = 250;

    private final Handler main = new Handler(Looper.getMainLooper());
    private final Map<Integer, Controller> controllers = new LinkedHashMap<>();
    private final Map<String, String> friendlyButtons = new HashMap<>();
    private InputManager inputManager;
    private PhoneProfileStore profiles;
    private WifiPairingStore wifiPairings;
    private BridgeClient bridge;
    private boolean sessionReady;
    private boolean refreshingUi;
    private boolean resumed;
    private boolean inputFocused = true;
    private boolean english;
    private boolean wifiMode;
    private boolean connectedOverWifi;
    private String pendingPairingHost;
    private byte[] pendingPairingKey;
    private boolean pendingPairingWasStored;
    private String selectedControllerId;
    private String pendingButton;
    private String pendingAxis;
    private final Map<Integer, Float> axisCaptureBaseline = new HashMap<>();
    private long pingSequence;
    private long snapshotTicks;
    private final Map<Long, Long> pingTimes = new HashMap<>();
    private String installId;

    private EditText hostAddress;
    private EditText pairingCodeInput;
    private Spinner transportPicker;
    private Button connectionButton;
    private TextView connectionStatus;
    private TextView usbNote;
    private TextView wifiNote;
    private TextView helpText;
    private TextView liveInput;
    private TextView profileSummary;
    private TextView gyroStatus;
    private TextView lightbarStatus;
    private Switch gyroToggle;
    private Button lightbarButton;
    private Spinner outputPicker;
    private LinearLayout mappingRows;
    private LinearLayout controllerList;
    private ArrayAdapter<String> outputAdapter;
    private final List<View> workspacePages = new ArrayList<>();
    private final List<Button> workspaceTabs = new ArrayList<>();
    private final Set<String> expandedMappingGroups = new HashSet<>();
    private ControllerPreview controllerPreview;
    private TextView controllerCount;
    private TextView liveControllerName;
    private Button resetMappingButton;
    private Button calibrateButton;
    private Button cancelCaptureButton;
    private TextView hostLabel;
    private TextView pairingLabel;
    private int activeWorkspace;

    private static final int BG = 0xff141819;
    private static final int SURFACE = 0xff1d2224;
    private static final int INSET = 0xff171c1e;
    private static final int BORDER = 0xff353d40;
    private static final int ACCENT = 0xff52d8e8;
    private static final int INK = 0xffedf2f3;
    private static final int MUTED = 0xffa1adb2;

    private final Runnable periodicSnapshots = new Runnable() {
        @Override public void run() {
            if (bridge != null && sessionReady) {
                for (Controller controller : controllers.values()) if (controller.attached && controller.profileReady) sendState(controller);
                if (++snapshotTicks % 4 == 0) sendPing();
            }
            if (resumed) main.postDelayed(this, SNAPSHOT_MS);
        }
    };

    private static final class Controller {
        final int deviceId;
        final String descriptor;
        final String profileId;
        InputDevice device;
        MappingModel mapping = MappingModel.standard();
        final Set<Integer> pressedKeys = new HashSet<>();
        final Map<Integer, Float> rawAxes = new HashMap<>();
        String output = "xbox360";
        int revision = 1;
        long sequence;
        String lastSentProfileJson;
        String pendingProfileJson;
        int pendingRevision;
        JSONObject deferredHostProfile;
        float hatX;
        float hatY;
        boolean attached;
        boolean profileReady;
        boolean pendingProfileSave;
        boolean gyroEnabled;
        InputDevice motionDevice;
        SensorManager motionManager;
        Sensor gyroSensor;
        Sensor accelSensor;
        MotionCapture motionCapture;
        boolean motionRegistered;
        final float[] gyroRadS = new float[3];
        final float[] accelMS2 = {0f,9.80665f,0f};
        boolean gyroSampleReceived;
        long motionTimestampNs;
        LightsManager lightsManager;
        Light rgbLight;
        LightsManager.LightsSession lightSession;
        boolean lightProbeComplete;
        int lightbarColor = Integer.MIN_VALUE;
        Controller(InputDevice device, String profileId) {
            this.device = device;
            this.deviceId = device.getId();
            this.descriptor = PhoneProfileStore.descriptor(device);
            this.profileId = profileId;
        }
        String id() { return Integer.toString(deviceId); }
        String displayName() { return device.getName(); }
    }

    private static final class MotionPair {
        final InputDevice device; final SensorManager manager; final Sensor gyro; final Sensor accel;
        MotionPair(InputDevice device, SensorManager manager, Sensor gyro, Sensor accel) {
            this.device=device; this.manager=manager; this.gyro=gyro; this.accel=accel;
        }
    }

    private final class MotionCapture implements SensorEventListener {
        final Controller controller;
        MotionCapture(Controller controller) { this.controller=controller; }
        @Override public void onSensorChanged(SensorEvent event) {
            if(!controller.motionRegistered || !resumed || !inputFocused) return;
            float[] target;
            if(event.sensor.getType()==Sensor.TYPE_GYROSCOPE) {
                target=controller.gyroRadS; controller.motionTimestampNs=event.timestamp; controller.gyroSampleReceived=true;
            } else if(event.sensor.getType()==Sensor.TYPE_ACCELEROMETER) target=controller.accelMS2;
            else return;
            int length=Math.min(3,event.values.length);
            for(int i=0;i<3;i++) target[i]=i<length && Float.isFinite(event.values[i])?event.values[i]:0f;
            if(event.sensor.getType()==Sensor.TYPE_GYROSCOPE && controller.output.equals("ds4")
                    && controller.gyroEnabled && inputFocused) sendState(controller);
        }
        @Override public void onAccuracyChanged(Sensor sensor,int accuracy) { }
    }

    @Override public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().setStatusBarColor(BG);
        getWindow().setNavigationBarColor(BG);
        getWindow().getDecorView().setSystemUiVisibility(0);
        inputManager = (InputManager)getSystemService(Context.INPUT_SERVICE);
        profiles = new PhoneProfileStore(this);
        wifiPairings = new WifiPairingStore(this);
        english="en".equals(BuildConfig.DEFAULT_LANGUAGE);
        wifiMode=getSharedPreferences("controller-bridge",MODE_PRIVATE).getBoolean("wifi_mode",false);
        installId = getSharedPreferences("controller-bridge", MODE_PRIVATE).getString("install_id", null);
        if (installId == null) {
            installId = UUID.randomUUID().toString();
            getSharedPreferences("controller-bridge", MODE_PRIVATE).edit().putString("install_id", installId).apply();
        }
        setupLabels();
        buildUi();
    }

    private void setupLabels() {
        String[] labels = {tr("Sul / X","South / X"), tr("Leste / Bolinha","East / Circle"), tr("Oeste / Quadrado","West / Square"), tr("Norte / Triângulo","North / Triangle"), "L1", "R1", "L2", "R2", tr("Compartilhar","Share"), tr("Opções","Options"), "L3", "R3", tr("PS / Guia","PS / Guide"), tr("Direcional ↑","D-pad ↑"), tr("Direcional ↓","D-pad ↓"), tr("Direcional ←","D-pad ←"), tr("Direcional →","D-pad →")};
        for (int i = 0; i < MappingModel.BUTTONS.size(); i++) friendlyButtons.put(MappingModel.BUTTONS.get(i), labels[i]);
    }

    private String tr(String portuguese,String englishText) { return english?englishText:portuguese; }
    private String displayButtonLabel(String logical) {
        Controller controller = selected();
        boolean ds4 = controller != null && controller.output.equals("ds4");
        switch (logical) {
            case "south": return ds4 ? tr("Cruz  ×", "Cross  ×") : "A";
            case "east": return ds4 ? tr("Círculo  ○", "Circle  ○") : "B";
            case "west": return ds4 ? tr("Quadrado  □", "Square  □") : "X";
            case "north": return ds4 ? tr("Triângulo  △", "Triangle  △") : "Y";
            case "l1": return ds4 ? "L1" : "LB";
            case "r1": return ds4 ? "R1" : "RB";
            case "l2": return ds4 ? "L2" : "LT";
            case "r2": return ds4 ? "R2" : "RT";
            case "share": return ds4 ? "Share" : "View";
            case "options": return ds4 ? "Options" : "Menu";
            case "ps": return ds4 ? "PS" : tr("Guia", "Guide");
            default: return friendlyButtons.get(logical);
        }
    }
    private String trAxisLabel(String axis) {
        switch(axis) {
            case "lx": return tr("Analógico esquerdo X","Left stick X");
            case "ly": return tr("Analógico esquerdo Y","Left stick Y");
            case "rx": return tr("Analógico direito X","Right stick X");
            case "ry": return tr("Analógico direito Y","Right stick Y");
            case "lt": return tr("Gatilho esquerdo","Left trigger");
            case "rt": return tr("Gatilho direito","Right trigger");
            default: return axis;
        }
    }

    private String axisName(int source) {
        String name=MotionEvent.axisToString(source).replace("AXIS_", "");
        return tr("Eixo ","Axis ") + name;
    }

    private String displayReason(String reason) {
        if (reason == null || reason.isEmpty()) return tr("erro inesperado", "unexpected error");
        if (!english) return reason;
        switch (reason) {
            case "Conexão encerrada.": return "Connection closed.";
            case "Conexão cancelada.": return "Connection canceled.";
            case "O host não aceitou o protocolo Controller Bridge v1.": return "The host did not accept Controller Bridge protocol v1.";
            case "O PC encerrou antes de confirmar a conexão.": return "The PC closed the connection before confirming it.";
            case "O socket ainda não está pronto.": return "The socket is not ready yet.";
            case "Falha ao enviar dados.": return "Failed to send data.";
            case "Falha de autenticação Wi-Fi.": return "Wi-Fi authentication failed. Check the PC code and try again.";
            case "Envelope Wi-Fi inválido.": return "Invalid secure Wi-Fi message.";
            case "Linha TCP acima do limite.": return "TCP message exceeds the allowed size.";
            case "Falha ao proteger mensagem Wi-Fi.": return "Could not secure a Wi-Fi message.";
            default: return reason;
        }
    }

    private String displayHostError(JSONObject message) {
        String code=message.optString("code","");
        if("auth_failed".equals(code))return tr("Pareamento Wi-Fi recusado. Confira o código mostrado no PC.","Wi-Fi pairing was rejected. Check the code shown on the PC.");
        if("output_unavailable".equals(code))return tr("O host não conseguiu criar ou atualizar o controle virtual.","The host could not create or update the virtual controller.");
        return message.optString("message",tr("Erro de protocolo.","Protocol error."));
    }

    private void buildUi() {
        LinearLayout shell = new LinearLayout(this);
        shell.setOrientation(LinearLayout.VERTICAL);
        shell.setBackgroundColor(BG);
        LinearLayout header = new LinearLayout(this);
        header.setGravity(Gravity.CENTER_VERTICAL);
        header.setPadding(dp(20), dp(18), dp(20), dp(14));
        TextView brand = text("CB", 17, true);
        brand.setGravity(Gravity.CENTER);
        brand.setTextColor(ACCENT);
        brand.setBackground(rounded(0xff213238, 0xff39565d, 13));
        header.addView(brand, new LinearLayout.LayoutParams(dp(46), dp(46)));
        LinearLayout wordmark = new LinearLayout(this);
        wordmark.setOrientation(LinearLayout.VERTICAL);
        wordmark.setPadding(dp(12), 0, 0, 0);
        TextView title = text("Controller Bridge", 20, true);
        title.setPadding(0, 0, 0, dp(2));
        wordmark.addView(title);
        TextView subtitle = text(tr("SEU CONTROLE. NO PC.", "YOUR CONTROLLER. ON PC."), 10, true);
        subtitle.setLetterSpacing(.12f); subtitle.setTextColor(MUTED);
        subtitle.setPadding(0, 0, 0, 0); wordmark.addView(subtitle);
        header.addView(wordmark, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        shell.addView(header, fullWidth());

        LinearLayout tabs = new LinearLayout(this);
        tabs.setPadding(dp(16), 0, dp(16), dp(12));
        String[] tabLabels = {tr("Conexão", "Connection"), tr("Controles", "Controllers"), tr("Ao vivo", "Live input")};
        for (int i = 0; i < tabLabels.length; i++) {
            final int page = i;
            Button tab = button(tabLabels[i]);
            tab.setTextSize(12); tab.setSingleLine(true);
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(48), 1);
            if (i > 0) params.leftMargin = dp(6);
            tabs.addView(tab, params); workspaceTabs.add(tab);
            tab.setOnClickListener(v -> showWorkspace(page));
        }
        shell.addView(tabs, fullWidth());
        View divider = new View(this); divider.setBackgroundColor(BORDER);
        shell.addView(divider, new LinearLayout.LayoutParams(-1, dp(1)));
        android.widget.FrameLayout workspace = new android.widget.FrameLayout(this);
        shell.addView(workspace, new LinearLayout.LayoutParams(-1, 0, 1));
        LinearLayout connectionRoot = workspacePage(workspace);
        LinearLayout controlsRoot = workspacePage(workspace);
        LinearLayout liveRoot = workspacePage(workspace);

        pageIntro(connectionRoot, tr("Conecte ao seu PC", "Connect to your PC"),
                tr("Escolha como o celular vai transmitir seus controles.", "Choose how your phone sends your controllers to Windows."));
        LinearLayout connectionCard = card(connectionRoot, tr("CONEXÃO COM O PC", "PC CONNECTION"));
        connectionStatus = text(tr("Desconectado", "Disconnected"), 16, true);
        connectionStatus.setTextColor(INK); connectionStatus.setMinHeight(dp(48));
        connectionStatus.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        connectionCard.addView(connectionStatus, fullWidth());
        fieldLabel(connectionCard, tr("Transporte", "Transport"));
        transportPicker = new Spinner(this);
        transportPicker.setAdapter(pickerAdapter(new String[]{tr("USB · cabo", "USB · cable"), tr("Wi-Fi · mesma rede", "Wi-Fi · same network")}));
        stylePicker(transportPicker); transportPicker.setSelection(wifiMode ? 1 : 0, false);
        connectionCard.addView(transportPicker, spaced());
        hostLabel = fieldLabel(connectionCard, tr("Endereço do PC", "PC address"));
        hostAddress = input(tr("IP local do PC", "PC local IP address"));
        hostAddress.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_URI);
        hostAddress.setText(getSharedPreferences("controller-bridge", MODE_PRIVATE).getString(wifiMode ? "wifi_host" : "host", wifiMode ? "" : "127.0.0.1"));
        connectionCard.addView(hostAddress, spaced());
        pairingLabel = fieldLabel(connectionCard, tr("Código de pareamento", "Pairing code"));
        pairingCodeInput = input(tr("Código exibido no PC", "Code shown on your PC"));
        pairingCodeInput.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        connectionCard.addView(pairingCodeInput, spaced());
        usbNote = text(tr("1. Conecte o cabo USB e autorize a depuração.\n2. Abra o app do PC e escolha USB.", "1. Connect your USB cable and allow debugging.\n2. Open the PC app and choose USB."), 13, false);
        wifiNote = text(tr("1. Coloque o celular e o PC na mesma rede.\n2. Abra o app do PC, escolha Wi-Fi e copie o código.", "1. Put your phone and PC on the same network.\n2. Open the PC app, choose Wi-Fi and copy its code."), 13, false);
        usbNote.setTextColor(MUTED); wifiNote.setTextColor(MUTED);
        usbNote.setLineSpacing(dp(5), 1); wifiNote.setLineSpacing(dp(5), 1);
        connectionCard.addView(usbNote, spaced()); connectionCard.addView(wifiNote, spaced());
        connectionButton = button(tr("Conectar", "Connect"));
        primary(connectionButton); connectionCard.addView(connectionButton, spaced());
        connectionButton.setOnClickListener(v -> toggleConnection());
        transportPicker.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(android.widget.AdapterView<?> parent, View view, int position, long id) {
                boolean next = position == 1; if (next == wifiMode) return;
                if (bridge != null) { transportPicker.setSelection(wifiMode ? 1 : 0); return; }
                String current = hostAddress.getText().toString().trim();
                getSharedPreferences("controller-bridge", MODE_PRIVATE).edit().putString(wifiMode ? "wifi_host" : "host", current).apply();
                wifiMode = next; getSharedPreferences("controller-bridge", MODE_PRIVATE).edit().putBoolean("wifi_mode", wifiMode).apply();
                hostAddress.setText(getSharedPreferences("controller-bridge", MODE_PRIVATE).getString(wifiMode ? "wifi_host" : "host", wifiMode ? "" : "127.0.0.1"));
                updateTransportUi();
            }
            @Override public void onNothingSelected(android.widget.AdapterView<?> parent) { }
        });
        LinearLayout connectionHint = card(connectionRoot, tr("PRONTO PARA JOGAR", "READY TO PLAY"));
        TextView hint = text(tr("Pareie o controle nas configurações Bluetooth do Android. Cada controle tem seu próprio perfil e sua própria saída no PC.", "Pair your controller in Android Bluetooth settings. Each controller has its own profile and PC output."), 14, false);
        hint.setTextColor(MUTED); hint.setLineSpacing(dp(3), 1); connectionHint.addView(hint);

        pageIntro(controlsRoot, tr("Seus controles", "Your controllers"),
                tr("Escolha um jogador e ajuste seu perfil.", "Choose a player and fine-tune their profile."));
        controllerCount = fieldLabel(controlsRoot, tr("CONTROLES CONECTADOS", "CONNECTED CONTROLLERS"));
        android.widget.HorizontalScrollView players = new android.widget.HorizontalScrollView(this);
        players.setHorizontalScrollBarEnabled(false); players.setClipToPadding(false);
        controllerList = new LinearLayout(this); controllerList.setOrientation(LinearLayout.HORIZONTAL);
        players.addView(controllerList); controlsRoot.addView(players, spaced());
        LinearLayout profileCard = card(controlsRoot, tr("PERFIL DO JOGADOR", "PLAYER PROFILE"));
        profileSummary = text(tr("Selecione um controle para configurar.", "Select a controller to configure."), 17, true);
        profileCard.addView(profileSummary, fullWidth());
        TextView outputLabel = fieldLabel(profileCard, tr("Saída virtual no PC", "Virtual output on PC"));
        outputLabel.setPadding(0, dp(14), 0, dp(6));
        outputPicker = new Spinner(this);
        outputAdapter = pickerAdapter(new String[]{"Xbox 360 · XInput", "DualShock 4"});
        outputPicker.setAdapter(outputAdapter); stylePicker(outputPicker);
        profileCard.addView(outputPicker, spaced());
        TextView savedNote = text(tr("Alterações salvas automaticamente para este controle.", "Changes are saved automatically for this controller."), 12, false);
        savedNote.setTextColor(MUTED); profileCard.addView(savedNote);
        outputPicker.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(android.widget.AdapterView<?> parent, View view, int position, long id) {
                if (refreshingUi) return;
                Controller current = selected(); if (current == null) return;
                String output = position == 1 ? "ds4" : "xbox360";
                if (!current.output.equals(output)) {
                    current.output = output;
                    if (!output.equals("ds4")) current.gyroEnabled = false;
                    refreshMotionSubscriptions(); persistAndSendProfile(current); renderControllerDetails();
                }
            }
            @Override public void onNothingSelected(android.widget.AdapterView<?> parent) { }
        });

        LinearLayout mappingCard = card(controlsRoot, tr("MAPEAMENTO", "MAPPING"));
        helpText = text(tr("Abra um grupo, toque em Mapear e pressione o botão no controle.", "Open a group, tap Map, then press the button on your controller."), 13, false);
        helpText.setTextColor(MUTED); helpText.setLineSpacing(dp(3), 1);
        helpText.setAccessibilityLiveRegion(View.ACCESSIBILITY_LIVE_REGION_POLITE);
        mappingCard.addView(helpText, fullWidth());
        cancelCaptureButton = button(tr("Cancelar captura", "Cancel capture"));
        cancelCaptureButton.setVisibility(View.GONE);
        cancelCaptureButton.setOnClickListener(v -> {
            pendingButton = null; pendingAxis = null; axisCaptureBaseline.clear();
            helpText.setText(tr("Captura cancelada. Escolha um mapeamento para editar.", "Capture canceled. Choose a mapping to edit."));
            renderMappingRows(selected()); updatePendingInstruction();
        });
        mappingCard.addView(cancelCaptureButton, spaced());
        mappingRows = new LinearLayout(this); mappingRows.setOrientation(LinearLayout.VERTICAL);
        mappingCard.addView(mappingRows, spaced());
        calibrateButton = button(tr("Aplicar calibração do controle", "Apply controller calibration"));
        calibrateButton.setOnClickListener(v -> calibrateSelectedAxes()); mappingCard.addView(calibrateButton, spaced());
        resetMappingButton = button(tr("Restaurar mapeamento padrão", "Restore default mapping"));
        resetMappingButton.setOnClickListener(v -> resetSelectedMapping()); mappingCard.addView(resetMappingButton, spaced());

        LinearLayout sensorCard = card(controlsRoot, tr("MOVIMENTO E LUZ", "MOTION & LIGHT"));
        gyroToggle = new Switch(this);
        gyroToggle.setText(tr("Giroscópio do DS4", "DS4 gyroscope")); gyroToggle.setTextSize(15); gyroToggle.setTextColor(INK);
        gyroToggle.setMinHeight(dp(56));
        android.content.res.ColorStateList toggleTint = new android.content.res.ColorStateList(
                new int[][]{new int[]{android.R.attr.state_checked}, new int[]{}}, new int[]{ACCENT, MUTED});
        gyroToggle.setThumbTintList(toggleTint);
        sensorCard.addView(gyroToggle, fullWidth());
        gyroStatus = text("", 13, false); gyroStatus.setTextColor(MUTED); sensorCard.addView(gyroStatus, fullWidth());
        View sensorDivider = new View(this); sensorDivider.setBackgroundColor(BORDER);
        LinearLayout.LayoutParams dividerParams = fullWidth(); dividerParams.height = dp(1); dividerParams.setMargins(0, dp(14), 0, dp(10));
        sensorCard.addView(sensorDivider, dividerParams);
        fieldLabel(sensorCard, tr("Luz do controle", "Controller light"));
        lightbarStatus = text("", 13, false); lightbarStatus.setTextColor(MUTED); sensorCard.addView(lightbarStatus, fullWidth());
        lightbarButton = button(tr("Escolher cor da luz", "Choose light color"));
        lightbarButton.setOnClickListener(v -> { Controller current = selected(); if (current != null) showLightbarPicker(current); });
        sensorCard.addView(lightbarButton, spaced()); lightbarButton.setVisibility(View.GONE);
        gyroToggle.setOnCheckedChangeListener((button, checked) -> {
            if (refreshingUi) return;
            Controller current = selected(); if (current == null) return;
            if (!current.output.equals("ds4") || (checked && findMotionPair(current) == null)) { renderControllerDetails(); return; }
            current.gyroEnabled = checked;
            refreshMotionSubscriptions(); persistAndSendProfile(current); renderControllerDetails();
        });

        pageIntro(liveRoot, tr("Cada movimento, ao vivo", "Every move, live"),
                tr("Teste a entrada do jogador selecionado na aba Controles.", "Test input from the player selected in Controllers."));
        LinearLayout liveCard = card(liveRoot, tr("MONITOR DO CONTROLE", "CONTROLLER MONITOR"));
        liveControllerName = text("", 17, true); liveCard.addView(liveControllerName, fullWidth());
        controllerPreview = new ControllerPreview(this); liveCard.addView(controllerPreview, new LinearLayout.LayoutParams(-1, dp(238)));
        liveInput = text("", 13, false); liveInput.setTextColor(MUTED);
        liveInput.setPadding(dp(14), dp(14), dp(14), dp(14)); liveInput.setLineSpacing(dp(7), 1);
        liveInput.setTypeface(android.graphics.Typeface.MONOSPACE);
        liveInput.setBackground(rounded(INSET, BORDER, 12)); liveCard.addView(liveInput, spaced());
        TextView liveHint = text(tr("O destaque ciano mostra a entrada recebida pelo Android. A conexão com o PC é exibida na aba Conexão.", "Cyan highlights show input received by Android. PC connection status is shown in Connection."), 13, false);
        liveHint.setTextColor(MUTED); liveRoot.addView(liveHint, spaced());
        setContentView(shell); showWorkspace(activeWorkspace);
        updateConnectionUi(); updatePendingInstruction(); renderControllerList(); renderControllerDetails();
    }

    private LinearLayout workspacePage(android.widget.FrameLayout workspace) {
        ScrollView scroll = new ScrollView(this); scroll.setFillViewport(true); scroll.setClipToPadding(false);
        scroll.setPadding(dp(16), dp(16), dp(16), dp(24)); scroll.setVerticalScrollBarEnabled(false);
        android.widget.FrameLayout centered = new android.widget.FrameLayout(this);
        LinearLayout content = new LinearLayout(this); content.setOrientation(LinearLayout.VERTICAL);
        int width = Math.min(getResources().getDisplayMetrics().widthPixels - dp(32), dp(720));
        android.widget.FrameLayout.LayoutParams params = new android.widget.FrameLayout.LayoutParams(width, -2, Gravity.TOP | Gravity.CENTER_HORIZONTAL);
        centered.addView(content, params); scroll.addView(centered);
        workspace.addView(scroll, new android.widget.FrameLayout.LayoutParams(-1, -1)); workspacePages.add(scroll);
        // The page width follows the actual window, including rotation and split-screen resizing.
        scroll.addOnLayoutChangeListener((v, l, t, r, b, oldL, oldT, oldR, oldB) -> {
            int available = Math.max(dp(200), Math.min(r - l - dp(32), dp(720)));
            if (content.getLayoutParams().width != available) { content.getLayoutParams().width = available; content.requestLayout(); }
        });
        return content;
    }

    private void showWorkspace(int selectedPage) {
        activeWorkspace = selectedPage;
        for (int i = 0; i < workspacePages.size(); i++) {
            boolean selected = i == selectedPage;
            workspacePages.get(i).setVisibility(selected ? View.VISIBLE : View.GONE);
            Button tab = workspaceTabs.get(i); tab.setSelected(selected);
            tab.setTextColor(selected ? ACCENT : MUTED);
            tab.setBackground(rounded(selected ? 0xff24343a : BG, selected ? 0xff46616a : BORDER, 10));
            tab.setContentDescription(tab.getText() + (selected ? tr(", selecionado", ", selected") : ""));
        }
    }

    private void pageIntro(LinearLayout parent, String heading, String description) {
        TextView title = text(heading, 25, true); title.setPadding(dp(2), dp(3), 0, dp(5)); parent.addView(title);
        TextView subtitle = text(description, 14, false); subtitle.setTextColor(MUTED); subtitle.setLineSpacing(dp(3), 1);
        subtitle.setPadding(dp(2), 0, 0, dp(12)); parent.addView(subtitle);
    }

    private TextView fieldLabel(LinearLayout parent, String label) {
        TextView view = text(label, 12, true); view.setTextColor(MUTED); view.setPadding(0, dp(8), 0, dp(6));
        parent.addView(view, fullWidth()); return view;
    }

    private EditText input(String hint) {
        EditText view = new EditText(this); view.setSingleLine(true); view.setTextSize(15);
        view.setTextColor(INK); view.setHintTextColor(MUTED); view.setHint(hint);
        view.setPadding(dp(14), dp(12), dp(14), dp(12)); view.setMinHeight(dp(52));
        view.setBackground(rounded(INSET, BORDER, 10)); return view;
    }

    private ArrayAdapter<String> pickerAdapter(String[] items) {
        ArrayAdapter<String> adapter = new ArrayAdapter<String>(this, android.R.layout.simple_spinner_item, items) {
            @Override public View getView(int position, View convertView, android.view.ViewGroup parent) {
                TextView view = (TextView) super.getView(position, convertView, parent);
                view.setText(items[position] + "   ▾");
                view.setTextColor(INK); view.setTextSize(15); view.setPadding(dp(14), dp(12), dp(14), dp(12)); return view;
            }
            @Override public View getDropDownView(int position, View convertView, android.view.ViewGroup parent) {
                TextView view = (TextView) super.getDropDownView(position, convertView, parent);
                view.setTextColor(INK); view.setBackgroundColor(SURFACE); view.setMinHeight(dp(52)); return view;
            }
        };
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item); return adapter;
    }

    private void stylePicker(Spinner picker) {
        picker.setMinimumHeight(dp(52)); picker.setBackground(rounded(INSET, BORDER, 10));
        picker.setPopupBackgroundDrawable(rounded(SURFACE, BORDER, 10));
        picker.setPadding(0, 0, dp(12), 0);
    }

    private LinearLayout.LayoutParams spaced() {
        LinearLayout.LayoutParams params = fullWidth(); params.topMargin = dp(7); return params;
    }

    private void primary(Button button) {
        button.setTextColor(0xff102125); button.setTypeface(null, android.graphics.Typeface.BOLD);
        button.setBackground(rounded(ACCENT, ACCENT, 11));
    }

    private void updateConnectionUi() {
        if(connectionButton==null || connectionStatus==null) return;
        updateTransportUi();
        if(bridge==null) {
            connectionButton.setText(tr("Conectar","Connect"));
            connectionStatus.setText(tr("Desconectado","Disconnected"));
        } else if(sessionReady) {
            connectionButton.setText(tr("Desconectar","Disconnect"));
            connectionStatus.setText(connectedOverWifi?tr("Wi-Fi seguro conectado","Secure Wi-Fi connected"):tr("Conectado ao host do PC por USB","Connected to PC host over USB"));
        } else {
            connectionButton.setText(tr("Cancelar","Cancel"));
            String host=hostAddress.getText().toString().trim();
            connectionStatus.setText(tr("Conectando a ","Connecting to ")+host+":"+(wifiMode?WIFI_PORT:PORT)+"…");
        }
    }

    private void updateTransportUi() {
        if(hostAddress==null)return;
        boolean editable=bridge==null && wifiMode;
        hostAddress.setVisibility(wifiMode ? View.VISIBLE : View.GONE);
        if (hostLabel != null) hostLabel.setVisibility(wifiMode ? View.VISIBLE : View.GONE);
        if (pairingLabel != null) pairingLabel.setVisibility(wifiMode ? View.VISIBLE : View.GONE);
        hostAddress.setEnabled(editable); hostAddress.setFocusable(editable); hostAddress.setFocusableInTouchMode(editable);
        hostAddress.setHint(wifiMode?tr("IP local do PC","PC local IP address"):tr("USB · 127.0.0.1","USB · 127.0.0.1"));
        if(!wifiMode)hostAddress.setText("127.0.0.1");
        if(pairingCodeInput!=null) {
            pairingCodeInput.setVisibility(wifiMode?View.VISIBLE:View.GONE);
            pairingCodeInput.setEnabled(editable); pairingCodeInput.setFocusable(editable); pairingCodeInput.setFocusableInTouchMode(editable);
            String host=hostAddress.getText().toString().trim(); byte[] saved=host.isEmpty()?null:wifiPairings.get(host);
            pairingCodeInput.setHint(saved==null?tr("Código de 32 caracteres exibido pelo host","32-character code shown by the host")
                    :tr("Pareado · deixe vazio para reutilizar","Paired · leave blank to reuse"));
            if(saved!=null)java.util.Arrays.fill(saved,(byte)0);
        }
        if(transportPicker!=null)transportPicker.setEnabled(bridge==null);
        if(usbNote!=null)usbNote.setVisibility(wifiMode?View.GONE:View.VISIBLE);
        if(wifiNote!=null)wifiNote.setVisibility(wifiMode?View.VISIBLE:View.GONE);
    }

    private void updatePendingInstruction() {
        if(helpText==null) return;
        Controller controller=selected();
        if (cancelCaptureButton != null) cancelCaptureButton.setVisibility(pendingButton != null || pendingAxis != null ? View.VISIBLE : View.GONE);
        if(pendingButton!=null && controller!=null)
            helpText.setText(tr("Agora pressione o botão físico que deve corresponder a ","Press the physical button that should map to ")+displayButtonLabel(pendingButton)+".");
        else if(pendingAxis!=null && controller!=null) {
            String label=trAxisLabel(pendingAxis);
            helpText.setText(tr("Mova somente o eixo que deseja usar para ","Move only the axis you want to use for ")+label+".");
        }
    }

    private static LinearLayout.LayoutParams fullWidth() {
        return new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
    }
    private int dp(float value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private GradientDrawable rounded(int color, int stroke, int radiusDp) {
        GradientDrawable shape=new GradientDrawable(); shape.setColor(color); shape.setCornerRadius(dp(radiusDp));
        if(stroke!=color) shape.setStroke(dp(1),stroke); return shape;
    }
    private LinearLayout card(LinearLayout parent,String heading) {
        LinearLayout outer=new LinearLayout(this); outer.setOrientation(LinearLayout.VERTICAL);
        outer.setPadding(dp(16),dp(14),dp(16),dp(16)); outer.setBackground(rounded(SURFACE,BORDER,16));
        LinearLayout.LayoutParams params=fullWidth(); params.topMargin=dp(12); parent.addView(outer,params);
        TextView title=text(heading,10,true); title.setTextColor(ACCENT); title.setLetterSpacing(.12f); title.setPadding(0,0,0,dp(9));
        outer.addView(title); return outer;
    }
    private TextView text(String value, int sp, boolean bold) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(sp);
        view.setTextColor(new android.content.res.ColorStateList(
                new int[][]{new int[]{-android.R.attr.state_enabled}, new int[]{}}, new int[]{0xff647075, INK}));
        if (bold) view.setTypeface(null, android.graphics.Typeface.BOLD);
        view.setPadding(0, dp(3), 0, dp(3));
        return view;
    }
    private Button button(String value) {
        Button view = new Button(this);
        view.setText(value);
        view.setAllCaps(false);
        view.setTextSize(13);
        view.setTextColor(new android.content.res.ColorStateList(
                new int[][]{new int[]{-android.R.attr.state_enabled}, new int[]{}}, new int[]{0xff647075, INK}));
        view.setMinHeight(dp(48)); view.setMinimumHeight(dp(48)); view.setMinWidth(0); view.setMinimumWidth(0);
        view.setPadding(dp(10),dp(4),dp(10),dp(4));
        android.content.res.ColorStateList rippleColor = android.content.res.ColorStateList.valueOf(0x3352d8e8);
        view.setBackground(new android.graphics.drawable.RippleDrawable(rippleColor, rounded(0xff293033,BORDER,10), null));
        return view;
    }
    @Override protected void onResume() {
        super.onResume();
        resumed = true;
        inputFocused = hasWindowFocus();
        reconcilePendingProfiles();
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        if (inputManager != null) inputManager.registerInputDeviceListener(this, main);
        refreshDevices();
        refreshMotionSubscriptions();
        renderControllerDetails();
        main.removeCallbacks(periodicSnapshots);
        main.postDelayed(periodicSnapshots, SNAPSHOT_MS);
    }

    @Override protected void onPause() {
        resumed = false;
        inputFocused = false;
        main.removeCallbacks(periodicSnapshots);
        if (inputManager != null) inputManager.unregisterInputDeviceListener(this);
        stopAllMotionSubscriptions();
        for (Controller controller : controllers.values()) resetLightbarProbe(controller);
        disconnectGracefully(tr("App pausado; controles liberados.","App paused; controllers released."));
        getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        super.onPause();
    }

    @Override public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        inputFocused = hasFocus && resumed;
        if (!inputFocused) for (Controller controller : controllers.values()) {
            clearInput(controller, false);
            sendNeutralState(controller);
        }
        refreshMotionSubscriptions();
    }

    @Override public void onInputDeviceAdded(int deviceId) { addDevice(deviceId); refreshMotionSubscriptions(); renderControllerDetails(); }
    @Override public void onInputDeviceChanged(int deviceId) {
        InputDevice fresh = InputDevice.getDevice(deviceId);
        Controller current = controllers.get(deviceId);
        if (fresh == null) { removeDevice(deviceId); refreshMotionSubscriptions(); return; }
        if (current != null) { current.device = fresh; resetLightbarProbe(current); }
        else addDevice(deviceId);
        refreshMotionSubscriptions();
        renderControllerDetails();
        renderControllerList();
    }
    @Override public void onInputDeviceRemoved(int deviceId) { removeDevice(deviceId); refreshMotionSubscriptions(); renderControllerDetails(); }

    private void refreshDevices() {
        for (int id : InputDevice.getDeviceIds()) addDevice(id);
        for (int id : new ArrayList<>(controllers.keySet())) if (InputDevice.getDevice(id) == null) removeDevice(id);
        renderControllerList();
    }
    private void addDevice(int id) {
        InputDevice device = InputDevice.getDevice(id);
        if (device == null || !isController(device) || controllers.containsKey(id)) return;
        Set<String> active = new HashSet<>();
        for (Controller c : controllers.values()) active.add(c.profileId);
        String profileId = profiles.profileId(device, active);
        Controller controller = new Controller(device, profileId);
        PhoneProfileStore.CachedProfile cached = profiles.get(profileId);
        controller.lightbarColor = profiles.getLightbarColor(profileId, Integer.MIN_VALUE);
        if (cached != null) {
            controller.mapping = decodeMapping(cached.mapping);
            controller.output = cached.output;
            controller.gyroEnabled = cached.gyroEnabled && controller.output.equals("ds4");
            controller.revision = cached.revision;
        } else controller.mapping = applyDeviceRanges(device, MappingModel.standard());
        controllers.put(id, controller);
        if (selectedControllerId == null) selectedControllerId = controller.id();
        if (bridge != null && sessionReady) attach(controller);
        refreshMotionSubscriptions();
        renderControllerList();
        renderControllerDetails();
    }
    private void removeDevice(int id) {
        Controller controller = controllers.remove(id);
        if (controller == null) return;
        sendNeutralAndDetach(controller);
        stopMotionSubscription(controller);
        closeLightbarSession(controller);
        if (controller.id().equals(selectedControllerId)) {
            selectedControllerId = controllers.isEmpty() ? null : controllers.values().iterator().next().id();
            pendingButton = null; pendingAxis = null; axisCaptureBaseline.clear();
            if (helpText != null) helpText.setText(tr("Selecione um controle e toque em Mapear para configurar.","Select a controller and tap Map to configure it."));
        }
        renderControllerList();
        renderControllerDetails();
    }

    private static boolean isDs4Device(InputDevice device) {
        if(device==null || device.getVendorId()!=0x054c) return false;
        int product=device.getProductId();
        return product==0x05c4 || product==0x09cc;
    }

    private boolean isDs4Controller(Controller controller) { return isDs4Device(controller.device); }

    private void inspectLightbar(Controller controller) {
        if (controller == null || controller.lightProbeComplete) return;
        controller.lightProbeComplete = true;
        controller.lightsManager = null; controller.rgbLight = null;
        if (Build.VERSION.SDK_INT < 31 || !isDs4Controller(controller)) return;
        try {
            LightsManager manager = controller.device.getLightsManager();
            for (Light light : manager.getLights()) {
                if (light.hasRgbControl() && light.getType() == Light.LIGHT_TYPE_INPUT) {
                    controller.lightsManager = manager; controller.rgbLight = light; break;
                }
            }
            if (controller.rgbLight != null && controller.lightbarColor != Integer.MIN_VALUE)
                applyLightbarColor(controller, controller.lightbarColor, false);
        } catch (RuntimeException ignored) {
            closeLightbarSession(controller);
            controller.lightsManager = null; controller.rgbLight = null;
        }
    }

    private boolean applyLightbarColor(Controller controller, int color, boolean persist) {
        if (controller == null || controller.lightsManager == null || controller.rgbLight == null || Build.VERSION.SDK_INT < 31) return false;
        try {
            if (controller.lightSession == null) controller.lightSession = controller.lightsManager.openSession();
            LightState state = new LightState.Builder().setColor(Color.rgb(Color.red(color), Color.green(color), Color.blue(color))).build();
            LightsRequest request = new LightsRequest.Builder().addLight(controller.rgbLight, state).build();
            controller.lightSession.requestLights(request);
            controller.lightbarColor = color | 0xff000000;
            if (persist) profiles.putLightbarColor(controller.profileId, controller.lightbarColor);
            return true;
        } catch (RuntimeException deniedOrUnsupported) {
            closeLightbarSession(controller);
            controller.lightsManager = null; controller.rgbLight = null;
            if (lightbarStatus != null) lightbarStatus.setText(tr("Android não permitiu alterar a luz RGB.","Android did not allow changing the RGB light."));
            if (lightbarButton != null) lightbarButton.setVisibility(View.GONE);
            return false;
        }
    }

    private void closeLightbarSession(Controller controller) {
        if (controller.lightSession != null) {
            try { controller.lightSession.close(); } catch (RuntimeException ignored) { }
            controller.lightSession = null;
        }
    }

    private void resetLightbarProbe(Controller controller) {
        closeLightbarSession(controller);
        controller.lightProbeComplete=false; controller.lightsManager=null; controller.rgbLight=null;
    }

    private void showLightbarPicker(Controller controller) {
        if (controller.rgbLight == null) return;
        int initial = controller.lightbarColor == Integer.MIN_VALUE ? ACCENT : controller.lightbarColor;
        LinearLayout content = new LinearLayout(this); content.setOrientation(LinearLayout.VERTICAL); content.setPadding(dp(22),dp(8),dp(22),dp(4));
        View preview = new View(this); preview.setBackground(rounded(initial,initial,10));
        LinearLayout.LayoutParams previewParams=fullWidth(); previewParams.height=dp(42); previewParams.bottomMargin=dp(12); content.addView(preview,previewParams);
        SeekBar[] channels = new SeekBar[3];
        int[] values={Color.red(initial),Color.green(initial),Color.blue(initial)};
        String[] names={tr("Vermelho","Red"),tr("Verde","Green"),tr("Azul","Blue")};
        for(int i=0;i<3;i++) {
            TextView label=text(names[i],12,true); content.addView(label);
            SeekBar slider=new SeekBar(this); slider.setMax(255); slider.setProgress(values[i]); channels[i]=slider;
            content.addView(slider,fullWidth());
        }
        Runnable updatePreview=()->preview.setBackground(rounded(Color.rgb(channels[0].getProgress(),channels[1].getProgress(),channels[2].getProgress()),0xff394655,10));
        for(SeekBar slider:channels) slider.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
            @Override public void onProgressChanged(SeekBar seekBar,int progress,boolean fromUser) { updatePreview.run(); }
            @Override public void onStartTrackingTouch(SeekBar seekBar) { }
            @Override public void onStopTrackingTouch(SeekBar seekBar) { }
        });
        AlertDialog dialog=new AlertDialog.Builder(this).setTitle(tr("Cor da luz do controle","Controller light color"))
                .setView(content).setNegativeButton(tr("Cancelar","Cancel"),null)
                .setPositiveButton(tr("Aplicar","Apply"),null).create();
        dialog.setOnShowListener(v->dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(button->{
            int color=Color.rgb(channels[0].getProgress(),channels[1].getProgress(),channels[2].getProgress());
            if(applyLightbarColor(controller,color,true)) { renderControllerDetails(); dialog.dismiss(); }
        }));
        dialog.show();
    }

    private MotionPair findMotionPair(Controller controller) {
        if(Build.VERSION.SDK_INT<31 || controller==null || !isDs4Controller(controller)) return null;
        ArrayList<MotionPair> candidates=new ArrayList<>();
        for(int id:InputDevice.getDeviceIds()) {
            InputDevice device=InputDevice.getDevice(id);
            if(device==null || (device.getSources()&InputDevice.SOURCE_SENSOR)!=InputDevice.SOURCE_SENSOR) continue;
            try {
                SensorManager manager=device.getSensorManager();
                Sensor gyro=manager.getDefaultSensor(Sensor.TYPE_GYROSCOPE);
                Sensor accel=manager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
                if(gyro!=null && accel!=null) candidates.add(new MotionPair(device,manager,gyro,accel));
            } catch(RuntimeException ignored) { }
        }
        String wanted=controller.device.getDescriptor();
        int matchingControllers=0;
        if(wanted!=null && !wanted.isBlank())
            for(Controller item:controllers.values()) if(isDs4Controller(item) && wanted.equals(item.device.getDescriptor())) matchingControllers++;
        int ds4Count=0; for(Controller item:controllers.values()) if(isDs4Controller(item)) ds4Count++;
        int exactCount=(wanted==null || wanted.isBlank())?0:matchingControllers;
        List<String> descriptors=new ArrayList<>(); List<Boolean> isDs4Sensors=new ArrayList<>();
        for(MotionPair pair:candidates) {
            descriptors.add(pair.device.getDescriptor());
            isDs4Sensors.add(isDs4Device(pair.device));
        }
        int index=MotionDeviceMatcher.choose(wanted,exactCount,ds4Count,descriptors,isDs4Sensors);
        return index>=0?candidates.get(index):null;
    }

    private void refreshMotionSubscriptions() {
        for(Controller controller:controllers.values()) updateMotionSubscription(controller,findMotionPair(controller));
    }

    private void updateMotionSubscription(Controller controller,MotionPair pair) {
        boolean changed=controller.motionDevice==null ? pair!=null : pair==null || controller.motionDevice.getId()!=pair.device.getId();
        if(changed) stopMotionSubscription(controller);
        if(pair==null) {
            controller.motionDevice=null; controller.motionManager=null; controller.gyroSensor=null; controller.accelSensor=null;
        } else {
            controller.motionDevice=pair.device; controller.motionManager=pair.manager;
            controller.gyroSensor=pair.gyro; controller.accelSensor=pair.accel;
        }
        boolean shouldListen=resumed && inputFocused && bridge!=null && sessionReady && controller.attached
                && controller.profileReady && controller.output.equals("ds4") && controller.gyroEnabled && pair!=null;
        if(!shouldListen) { stopMotionSubscription(controller); return; }
        if(controller.motionRegistered) return;
        if(controller.motionCapture==null) controller.motionCapture=new MotionCapture(controller);
        boolean gyroOk=false, accelOk=false;
        try {
            gyroOk=controller.motionManager.registerListener(controller.motionCapture,controller.gyroSensor,SensorManager.SENSOR_DELAY_GAME,main);
            accelOk=gyroOk && controller.motionManager.registerListener(controller.motionCapture,controller.accelSensor,SensorManager.SENSOR_DELAY_GAME,main);
        } catch(RuntimeException ignored) { }
        if(!gyroOk || !accelOk) {
            controller.motionManager.unregisterListener(controller.motionCapture);
            controller.motionRegistered=false;
            return;
        }
        controller.gyroSampleReceived=false; controller.motionTimestampNs=0;
        controller.gyroRadS[0]=controller.gyroRadS[1]=controller.gyroRadS[2]=0f;
        controller.accelMS2[0]=0f; controller.accelMS2[1]=9.80665f; controller.accelMS2[2]=0f;
        controller.motionRegistered=true;
    }

    private void stopMotionSubscription(Controller controller) {
        if(controller.motionRegistered && controller.motionManager!=null && controller.motionCapture!=null)
            controller.motionManager.unregisterListener(controller.motionCapture);
        controller.motionRegistered=false;
        controller.gyroSampleReceived=false;
        controller.motionTimestampNs=0;
    }

    private void stopAllMotionSubscriptions() { for(Controller controller:controllers.values()) stopMotionSubscription(controller); }

    private static boolean isController(InputDevice device) {
        if (device == null) return false;
        int sources = device.getSources();
        boolean gamepad = (sources & InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD;
        boolean joystick = (sources & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK;
        boolean dpad = (sources & InputDevice.SOURCE_DPAD) == InputDevice.SOURCE_DPAD;
        return ControllerInputFilter.accepts(device.isVirtual(), gamepad, joystick, dpad);
    }

    @Override public boolean dispatchKeyEvent(KeyEvent event) {
        InputDevice device = InputDevice.getDevice(event.getDeviceId());
        Controller controller = controllers.get(event.getDeviceId());
        if (controller != null && device != null && isController(device)) {
            if (event.getAction() == KeyEvent.ACTION_DOWN) controller.pressedKeys.add(event.getKeyCode());
            else if (event.getAction() == KeyEvent.ACTION_UP) controller.pressedKeys.remove(event.getKeyCode());
            if (event.getAction() == KeyEvent.ACTION_DOWN && event.getRepeatCount() == 0 && pendingButton != null && controller.id().equals(selectedControllerId)) {
            controller.mapping = controller.mapping.withButton(pendingButton, event.getKeyCode());
                pendingButton = null;
                profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
                persistAndSendProfile(controller);
                helpText.setText(tr("Botão atualizado. Os outros mapeamentos foram mantidos.","Button updated. Other mappings were kept."));
            }
            refreshState(controller, null);
            return true;
        }
        return super.dispatchKeyEvent(event);
    }

    @Override public boolean dispatchGenericMotionEvent(MotionEvent event) {
        Controller controller = controllers.get(event.getDeviceId());
        InputDevice device = InputDevice.getDevice(event.getDeviceId());
        if (controller != null && device != null && (event.getSource() & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK) {
            controller.device = device;
            for (InputDevice.MotionRange range : device.getMotionRanges()) {
                if ((range.getSource() & InputDevice.SOURCE_CLASS_JOYSTICK) != 0) {
                    controller.rawAxes.put(range.getAxis(), event.getAxisValue(range.getAxis()));
                }
            }
            if (pendingAxis != null && controller.id().equals(selectedControllerId)) captureAxis(controller, event);
            refreshState(controller, event);
            return true;
        }
        return super.dispatchGenericMotionEvent(event);
    }

    private void captureAxis(Controller controller, MotionEvent event) {
        List<AxisCaptureSelector.Sample> samples = new ArrayList<>();
        for (InputDevice.MotionRange range : controller.device.getMotionRanges()) {
            if ((range.getSource() & InputDevice.SOURCE_CLASS_JOYSTICK) == 0) continue;
            float raw = event.getAxisValue(range.getAxis());
            Float baseline = axisCaptureBaseline.get(range.getAxis());
            if (baseline == null) {
                axisCaptureBaseline.put(range.getAxis(), raw);
                continue;
            }
            samples.add(new AxisCaptureSelector.Sample(range.getAxis(), baseline, raw, range.getMin(), range.getMax()));
        }
        Integer source = AxisCaptureSelector.select(samples);
        InputDevice.MotionRange bestRange = source == null ? null : controller.device.getMotionRange(source, InputDevice.SOURCE_JOYSTICK);
        if (bestRange != null) {
            InputDevice.MotionRange range = bestRange;
            boolean trigger = pendingAxis.equals("lt") || pendingAxis.equals("rt");
            float min = range.getMin(), max = range.getMax();
            float center = trigger ? min : Math.max(min, Math.min(max, 0f));
            float span = Math.max(.0001f, max - min);
            float dead = trigger ? .02f : Math.max(.02f, Math.min(.25f, range.getFlat() * 2f / span));
            MappingModel.AxisBinding binding = new MappingModel.AxisBinding(range.getAxis(), false, min, center, max, dead);
            controller.mapping = controller.mapping.withAxis(pendingAxis, binding);
            String axis = pendingAxis;
            pendingAxis = null;
            axisCaptureBaseline.clear();
            profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
            persistAndSendProfile(controller);
            helpText.setText(tr("Eixo ","Axis ") + axis + tr(" remapeado para "," mapped to ") + axisName(range.getAxis()) + ".");
            renderControllerDetails();
            return;
        }
        helpText.setText(tr("Ainda não detectei movimento. Mova apenas o eixo que deseja mapear.","No axis movement detected. Move only the axis you want to map."));
    }

    private void startAxisCapture(Controller controller, String logical) {
        pendingAxis = logical; pendingButton = null;
        axisCaptureBaseline.clear(); axisCaptureBaseline.putAll(controller.rawAxes);
        updatePendingInstruction(); renderMappingRows(controller);
    }

    private void refreshState(Controller controller, MotionEvent event) {
        if (event != null) {
            controller.hatX = event.getAxisValue(controller.mapping.hatXSource());
            controller.hatY = event.getAxisValue(controller.mapping.hatYSource());
        }
        renderLiveState();
        if (bridge != null && sessionReady && controller.attached && controller.profileReady) sendState(controller);
    }

    private void toggleConnection() {
        if (bridge != null) { disconnectGracefully(tr("Desconectado pelo usuário.","Disconnected by user.")); return; }
        reconcilePendingProfiles();
        String host = wifiMode?hostAddress.getText().toString().trim():"127.0.0.1";
        if (host.isEmpty()) { hostAddress.setError(tr("Informe o IP local do PC.","Enter the PC local IP address.")); return; }
        int port=wifiMode?WIFI_PORT:PORT;
        getSharedPreferences("controller-bridge", MODE_PRIVATE).edit().putString(wifiMode?"wifi_host":"host", host).apply();
        byte[] pairingKey=null;
        if(wifiMode) {
            String typed=pairingCodeInput.getText().toString().trim();
            pendingPairingWasStored=typed.isEmpty();
            try { pairingKey=pendingPairingWasStored?wifiPairings.get(host):WifiSecureChannel.parsePairingCode(typed); }
            catch(IllegalArgumentException invalid) { pairingCodeInput.setError(tr("Código inválido. Use 32 caracteres hexadecimais.","Invalid code. Enter 32 hexadecimal characters.")); return; }
            if(pairingKey==null) { pairingCodeInput.setError(tr("Informe o código exibido pelo host.","Enter the code shown by the host.")); return; }
            pendingPairingHost=host; pendingPairingKey=pairingKey.clone();
        } else clearPendingPairing();
        connectedOverWifi=wifiMode;
        connectionStatus.setText(wifiMode?tr("Autenticando Wi-Fi com ","Authenticating Wi-Fi with ")+host+":"+port+"…"
                :tr("Conectando por USB a ","Connecting over USB to ")+host+":"+port+"…");
        connectionButton.setText(tr("Cancelar","Cancel"));
        bridge = new BridgeClient(this);
        sessionReady = false;
        updateTransportUi();
        bridge.connect(host, port, installId, pairingKey);
    }

    private void clearPendingPairing() {
        if(pendingPairingKey!=null)java.util.Arrays.fill(pendingPairingKey,(byte)0);
        pendingPairingKey=null; pendingPairingHost=null; pendingPairingWasStored=false;
    }

    @Override public void onConnected(BridgeClient source) {
        if (bridge != source) return;
        sessionReady = true;
        boolean pairStorageFailed=false;
        if(connectedOverWifi && pendingPairingHost!=null && pendingPairingKey!=null) {
            try { wifiPairings.put(pendingPairingHost,pendingPairingKey); }
            catch(Exception storageError) { pairStorageFailed=true; }
        }
        if(pairingCodeInput!=null)pairingCodeInput.setText("");
        clearPendingPairing();
        connectionStatus.setText(pairStorageFailed?tr("Wi-Fi autenticado; não foi possível salvar o pareamento.","Wi-Fi authenticated; pairing could not be saved.")
                :connectedOverWifi?tr("Wi-Fi seguro conectado","Secure Wi-Fi connected"):tr("Conectado ao host do PC por USB","Connected to PC host over USB"));
        connectionButton.setText(tr("Desconectar","Disconnect"));
        updateTransportUi();
        for (Controller controller : controllers.values()) attach(controller);
    }

    @Override public void onMessage(BridgeClient source, JSONObject message) {
        if (bridge != source) return;
        String type = message.optString("type", "");
        String id = message.optString("controller_id", "");
        Controller controller = findById(id);
        if ("mapping_capture".equals(type)) {
            String logicalButton = message.optString("button", "");
            String requestedProfileId = message.optString("profile_id", "");
            if (controller == null || !controller.attached || !controller.profileReady
                    || (!requestedProfileId.isEmpty() && !controller.profileId.equals(requestedProfileId))
                    || !MappingModel.BUTTONS.contains(logicalButton)) return;
            selectedControllerId = controller.id();
            pendingButton = logicalButton;
            pendingAxis = null;
            axisCaptureBaseline.clear();
            showWorkspace(1);
            renderControllerList();
            renderControllerDetails();
            updatePendingInstruction();
        } else if ("profile".equals(type) && controller != null) {
            String returnedId = message.optString("profile_id", "");
            if (!controller.profileId.equals(returnedId)) { connectionStatus.setText(tr("O PC retornou um perfil incompatível.","The PC returned an incompatible profile.")); return; }
            if (controller.profileReady) {
                receiveHostProfileUpdate(controller, message);
                return;
            }
            int hostRevision = message.optInt("revision", 1);
            String hostOutput = message.optString("output", "xbox360");
            JSONObject hostMapping = message.optJSONObject("mapping");
            PhoneProfileStore.CachedProfile cached = profiles.get(controller.profileId);
            boolean hostIsEmpty = hostMapping == null || hostMapping.length() == 0;
            if (cached != null && (hostIsEmpty || cached.revision >= hostRevision)) {
                controller.mapping = decodeMapping(cached.mapping);
                controller.output = cached.output;
                controller.gyroEnabled = cached.gyroEnabled && controller.output.equals("ds4");
                controller.revision = hostRevision;
                controller.profileReady = true;
                controller.attached = true;
                persistAndSendProfile(controller);
            } else {
                controller.mapping = hostIsEmpty ? applyDeviceRanges(controller.device, MappingModel.standard()) : decodeMapping(hostMapping);
                controller.output = "ds4".equalsIgnoreCase(hostOutput) ? "ds4" : "xbox360";
                controller.gyroEnabled = controller.output.equals("ds4") && message.optBoolean("gyro_enabled", false);
                controller.revision = hostRevision;
                controller.profileReady = true;
                controller.attached = true;
                profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
                if (hostIsEmpty) persistAndSendProfile(controller);
            }
            refreshMotionSubscriptions();
            renderControllerList();
            renderControllerDetails();
            sendState(controller);
        } else if ("profile_saved".equals(type) && controller != null) {
            if (!controller.profileId.equals(message.optString("profile_id", ""))) return;
            int ackRevision = message.optInt("revision", -1);
            if (!controller.pendingProfileSave || ackRevision != controller.pendingRevision) return;
            controller.revision = Math.max(controller.revision, ackRevision);
            controller.pendingProfileSave = false;
            String currentJson = controller.output + ":" + controller.gyroEnabled + ":" + encodeMapping(controller.mapping);
            boolean editedAgain = controller.pendingProfileJson != null && !controller.pendingProfileJson.equals(currentJson);
            clearPendingProfile(controller);
            // Resolve desktop changes before sending another queued phone edit at the same revision.
            if (!applyDeferredHostProfile(controller, ackRevision)) {
                profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
                if (editedAgain) persistAndSendProfile(controller);
            }
            connectionStatus.setText(tr("Perfil salvo no PC · revisão ","Profile saved on PC · revision ") + controller.revision);
            renderControllerList();
            renderControllerDetails();
        } else if ("pong".equals(type)) {
            long sequence = message.optLong("seq", -1);
            Long sentAt = pingTimes.remove(sequence);
            if (sentAt != null) {
                long rtt = android.os.SystemClock.elapsedRealtime() - sentAt;
                connectionStatus.setText(tr("Conectado ao host do PC · RTT local ","Connected to PC host · local RTT ") + rtt + " ms");
            }
        } else if ("error".equals(type)) {
            connectionStatus.setText(displayHostError(message));
            if (controller != null) {
                JSONObject accepted = message.optJSONObject("profile");
                if ("stale_revision".equals(message.optString("code", "")) && accepted != null
                        && controller.profileId.equals(accepted.optString("profile_id", ""))
                        && controller.id().equals(accepted.optString("controller_id", ""))
                        && accepted.optInt("revision", -1) > controller.revision) {
                    JSONObject deferred = controller.deferredHostProfile;
                    if (deferred != null && deferred.optInt("revision", -1) > accepted.optInt("revision", -1)) accepted = deferred;
                    clearPendingProfile(controller);
                    controller.deferredHostProfile = null;
                    applyHostProfile(controller, accepted, accepted.optInt("revision", -1));
                } else {
                    clearPendingProfile(controller);
                    applyDeferredHostProfile(controller, controller.revision);
                    if (message.optString("code", "").equals("output_unavailable")) controller.profileReady = false;
                }
                refreshMotionSubscriptions();
                renderControllerList();
                renderControllerDetails();
            }
        }
    }

    /** Apply a newer profile pushed by the desktop UI without echoing it back as a phone edit. */
    private void receiveHostProfileUpdate(Controller controller, JSONObject message) {
        if (!controller.profileId.equals(message.optString("profile_id", ""))) return;
        int hostRevision = message.optInt("revision", -1);
        if (hostRevision < 1) return;

        // Keep the newest desktop snapshot until the outstanding phone edit is accepted or rejected.
        if (controller.pendingProfileSave) {
            JSONObject deferred = controller.deferredHostProfile;
            if (deferred == null || hostRevision > deferred.optInt("revision", -1))
                controller.deferredHostProfile = message;
            return;
        }
        applyHostProfile(controller, message, hostRevision);
    }

    private boolean applyDeferredHostProfile(Controller controller, int acknowledgedRevision) {
        if (controller.pendingProfileSave) return false;
        JSONObject deferred = controller.deferredHostProfile;
        controller.deferredHostProfile = null;
        if (deferred == null) return false;
        int deferredRevision = deferred.optInt("revision", -1);
        if (deferredRevision <= Math.max(acknowledgedRevision, controller.revision)) return false;
        applyHostProfile(controller, deferred, deferredRevision);
        return true;
    }

    private void applyHostProfile(Controller controller, JSONObject message, int hostRevision) {
        if (!controller.profileId.equals(message.optString("profile_id", "")) || hostRevision <= controller.revision) return;
        String hostOutput = message.optString("output", controller.output);
        if (!"ds4".equalsIgnoreCase(hostOutput) && !"xbox360".equalsIgnoreCase(hostOutput)) return;

        JSONObject hostMapping = message.optJSONObject("mapping");
        if (hostMapping != null && hostMapping.length() > 0)
            controller.mapping = decodeMapping(hostMapping);
        controller.output = "ds4".equalsIgnoreCase(hostOutput) ? "ds4" : "xbox360";
        controller.gyroEnabled = controller.output.equals("ds4") && message.optBoolean("gyro_enabled", controller.gyroEnabled);
        controller.revision = hostRevision;
        controller.lastSentProfileJson = null;
        profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
        refreshMotionSubscriptions();
        renderControllerList();
        if (controller.id().equals(selectedControllerId)) renderControllerDetails();
        if (bridge != null && sessionReady && controller.attached && controller.profileReady) sendState(controller);
    }

    @Override public void onDisconnected(BridgeClient source, String reason) {
        if (bridge != source) return;
        if(pendingPairingWasStored && pendingPairingHost!=null && "Falha de autenticação Wi-Fi.".equals(reason)) wifiPairings.remove(pendingPairingHost);
        clearPendingPairing();
        bridge = null;
        sessionReady = false;
        reconcilePendingProfiles();
        connectedOverWifi=false;
        for (Controller controller : controllers.values()) { controller.attached = false; controller.profileReady = false; clearInput(controller, false); }
        refreshMotionSubscriptions();
        connectionStatus.setText(tr("Desconectado: ","Disconnected: ") + displayReason(reason));
        connectionButton.setText(tr("Conectar","Connect"));
        updateTransportUi();
        renderControllerList();
        renderControllerDetails();
    }

    private void attach(Controller controller) {
        if (bridge == null || !sessionReady || controller.attached) return;
        try {
            JSONObject message = new JSONObject().put("v", 1).put("type", "attach")
                    .put("controller_id", controller.id()).put("profile_id", controller.profileId)
                    .put("descriptor", controller.descriptor).put("name", controller.device.getName());
            bridge.send(message);
            controller.attached = true;
        } catch (JSONException impossible) { connectionStatus.setText(tr("Falha ao montar mensagem attach.","Could not build attach message.")); }
    }

    private void sendState(Controller controller) {
        if (bridge == null || !controller.attached || !controller.profileReady) return;
        if (!inputFocused) { sendNeutralState(controller); return; }
        try {
            Map<String, Boolean> buttons = controller.mapping.mapButtons(controller.pressedKeys, controller.hatX, controller.hatY);
            Map<String, Float> axes = controller.mapping.mapAxes(axis -> readCurrentAxis(controller, axis));
            // Digital trigger presses remain represented even if a device has no analog trigger axis.
            if (buttons.getOrDefault("l2", false) && axes.getOrDefault("lt", 0f) == 0f) axes.put("lt", 1f);
            if (buttons.getOrDefault("r2", false) && axes.getOrDefault("rt", 0f) == 0f) axes.put("rt", 1f);
            JSONObject buttonJson = new JSONObject();
            for (String name : MappingModel.BUTTONS) buttonJson.put(name, buttons.getOrDefault(name, false));
            JSONObject axisJson = new JSONObject();
            for (String name : MappingModel.AXES) axisJson.put(name, axes.getOrDefault(name, 0f));
            JSONObject state = new JSONObject().put("v", 1).put("type", "state").put("controller_id", controller.id())
                    .put("seq", ++controller.sequence).put("buttons", buttonJson).put("axes", axisJson).put("battery", JSONObject.NULL);
            if(controller.output.equals("ds4") && controller.gyroEnabled && controller.motionRegistered && controller.gyroSampleReceived)
                state.put("motion",encodeMotion(controller));
            bridge.send(state);
        } catch (JSONException error) { connectionStatus.setText(tr("Falha ao montar estado do controle.","Could not build controller state.")); }
    }

    private JSONObject encodeMotion(Controller controller) throws JSONException {
        JSONArray gyro=new JSONArray(), accel=new JSONArray();
        for(int i=0;i<3;i++) { gyro.put(finite(controller.gyroRadS[i])); accel.put(finite(controller.accelMS2[i])); }
        return new JSONObject().put("gyro_rad_s",gyro).put("accel_m_s2",accel).put("timestamp_ns",controller.motionTimestampNs);
    }
    private static double finite(float value) { return Float.isFinite(value)?value:0d; }

    private void sendNeutralState(Controller controller) {
        if (bridge == null || !controller.attached || !controller.profileReady) return;
        try {
            JSONObject buttonJson = new JSONObject();
            for (String name : MappingModel.BUTTONS) buttonJson.put(name, false);
            JSONObject axisJson = new JSONObject();
            for (String name : MappingModel.AXES) axisJson.put(name, 0f);
            bridge.send(new JSONObject().put("v",1).put("type","state").put("controller_id",controller.id())
                    .put("seq",++controller.sequence).put("buttons",buttonJson).put("axes",axisJson).put("battery",JSONObject.NULL));
        } catch (JSONException error) { connectionStatus.setText(tr("Falha ao enviar estado neutro.","Could not send neutral state.")); }
    }

    private float readCurrentAxis(Controller controller, int axis) {
        // Input events carry the latest complete device axes. Querying the device itself is not public API;
        // cache the most recent MotionEvent values in the controller state.
        return controller.rawAxes.getOrDefault(axis, 0f);
    }

    private void sendPing() {
        if (bridge == null || !sessionReady) return;
        try {
            long sequence = ++pingSequence;
            pingTimes.put(sequence, android.os.SystemClock.elapsedRealtime());
            bridge.send(new JSONObject().put("v", 1).put("type", "ping").put("seq", sequence));
            while (pingTimes.size() > 8) pingTimes.remove(pingTimes.keySet().iterator().next());
        }
        catch (JSONException ignored) { }
    }

    private void persistAndSendProfile(Controller controller) {
        JSONObject mapping = encodeMapping(controller.mapping);
        profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, mapping);
        renderControllerList();
        if (bridge == null || !sessionReady || !controller.attached || !controller.profileReady || controller.pendingProfileSave) {
            renderControllerDetails();
            return;
        }
        try {
            int nextRevision = controller.revision + 1;
            JSONObject update = new JSONObject().put("v", 1).put("type", "profile_update")
                    .put("controller_id", controller.id()).put("profile_id", controller.profileId)
                    .put("revision", nextRevision).put("output", controller.output).put("mapping", mapping);
            update.put("gyro_enabled", controller.output.equals("ds4") && controller.gyroEnabled);
            controller.lastSentProfileJson = controller.output + ":" + controller.gyroEnabled + ":" + mapping;
            controller.pendingProfileSave = true;
            controller.pendingRevision = nextRevision;
            controller.pendingProfileJson = controller.lastSentProfileJson;
            bridge.send(update);
        } catch (JSONException error) { connectionStatus.setText(tr("Falha ao enviar perfil.","Could not send profile.")); }
        renderControllerDetails();
    }

    private void sendNeutralAndDetach(Controller controller) {
        clearInput(controller, false);
        if (bridge == null || !controller.attached) return;
        if (controller.profileReady) sendNeutralState(controller);
        try { bridge.send(new JSONObject().put("v", 1).put("type", "detach").put("controller_id", controller.id())); }
        catch (JSONException ignored) { }
        controller.attached = false; controller.profileReady = false;
    }

    private void clearInput(Controller controller, boolean sendNow) {
        controller.pressedKeys.clear(); controller.hatX=0; controller.hatY=0;
        controller.rawAxes.clear();
        if(sendNow && controller.attached && controller.profileReady) sendNeutralState(controller);
    }

    private void reconcilePendingProfiles() {
        for (Controller controller : controllers.values()) reconcilePendingProfile(controller);
    }

    private void reconcilePendingProfile(Controller controller) {
        JSONObject deferred = controller.deferredHostProfile;
        if (deferred != null && deferred.optInt("revision", -1) > controller.revision) {
            clearPendingProfile(controller);
            applyDeferredHostProfile(controller, controller.revision);
            return;
        }
        if (controller.pendingProfileSave) {
            controller.revision = Math.max(controller.revision, controller.pendingRevision);
            profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
        }
        clearPendingProfile(controller);
        controller.deferredHostProfile = null;
    }

    private void clearPendingProfile(Controller controller) {
        controller.pendingProfileSave = false;
        controller.pendingProfileJson = null;
        controller.pendingRevision = 0;
    }

    private void disconnectGracefully(String status) {
        BridgeClient old = bridge;
        if (old == null) return;
        for (Controller controller : controllers.values()) sendNeutralAndDetach(controller);
        refreshMotionSubscriptions();
        reconcilePendingProfiles();
        bridge = null;
        sessionReady = false;
        clearPendingPairing();
        connectedOverWifi=false;
        connectionStatus.setText(status);
        connectionButton.setText(tr("Conectar","Connect"));
        old.closeAfterFlush();
        updateTransportUi();
        renderControllerList();
        renderControllerDetails();
    }

    private void resetSelectedMapping() {
        Controller controller = selected();
        if (controller == null) return;
        controller.mapping = applyDeviceRanges(controller.device, MappingModel.standard());
        helpText.setText(tr("Mapeamento padrão Android restaurado.","Android default mapping restored."));
        persistAndSendProfile(controller);
    }

    private void calibrateSelectedAxes() {
        Controller controller = selected();
        if (controller == null) return;
        controller.mapping = applyDeviceRanges(controller.device, controller.mapping);
        helpText.setText(tr("Limites e zona morta informados pelo controle aplicados aos eixos mapeados.","Controller-reported axis ranges and dead zones applied."));
        persistAndSendProfile(controller);
    }

    private MappingModel applyDeviceRanges(InputDevice device, MappingModel mapping) {
        if (device == null) return mapping;
        MappingModel updated = mapping;
        for (Map.Entry<String, MappingModel.AxisBinding> entry : mapping.axisBindings().entrySet()) {
            MappingModel.AxisBinding old = entry.getValue();
            InputDevice.MotionRange range = device.getMotionRange(old.source, InputDevice.SOURCE_JOYSTICK);
            if (range == null) continue;
            boolean trigger = entry.getKey().equals("lt") || entry.getKey().equals("rt");
            float min = range.getMin(), max = range.getMax();
            float center = trigger ? min : Math.max(min, Math.min(max, 0f));
            float span = Math.max(.0001f, max - min);
            float flatDeadZone = Math.max(0, range.getFlat() * 2f / span);
            float deadZone = flatDeadZone > 0 ? Math.min(.25f, flatDeadZone) : old.deadZone;
            try { updated = updated.withCalibration(entry.getKey(), min, center, max, deadZone); }
            catch (IllegalArgumentException ignored) { }
        }
        return updated;
    }

    private void renderControllerList() {
        if (controllerList == null) return;
        controllerList.removeAllViews();
        List<Controller> all = new ArrayList<>(controllers.values());
        controllerCount.setText(tr("CONTROLES CONECTADOS", "CONNECTED CONTROLLERS") + "  ·  " + all.size());
        if (all.isEmpty()) {
            TextView empty = text(tr("Nenhum controle conectado\nPareie um controle nas configurações Bluetooth do Android.", "No controllers connected\nPair a controller in Android Bluetooth settings."), 14, false);
            empty.setTextColor(MUTED); empty.setPadding(dp(16), dp(18), dp(16), dp(18));
            empty.setLineSpacing(dp(5), 1); empty.setBackground(rounded(INSET, BORDER, 12));
            controllerList.addView(empty, new LinearLayout.LayoutParams(dp(268), -2)); return;
        }
        for (int i = 0; i < all.size(); i++) {
            Controller controller = all.get(i);
            boolean isSelected = controller.id().equals(selectedControllerId);
            LinearLayout item = new LinearLayout(this); item.setOrientation(LinearLayout.VERTICAL);
            item.setPadding(dp(14), dp(12), dp(14), dp(12)); item.setMinimumHeight(dp(100));
            item.setBackground(rounded(isSelected ? 0xff24363c : SURFACE, isSelected ? ACCENT : BORDER, 13));
            item.setClickable(true); item.setFocusable(true); item.setSelected(isSelected);
            TextView player = text(tr("JOGADOR ", "PLAYER ") + (i + 1) + (isSelected ? "  ✓" : ""), 10, true);
            player.setLetterSpacing(.08f); player.setTextColor(isSelected ? ACCENT : MUTED); item.addView(player);
            TextView name = text(controller.displayName(), 14, true); name.setSingleLine(true);
            name.setEllipsize(android.text.TextUtils.TruncateAt.END); item.addView(name);
            String output = controller.output.equals("ds4") ? "DualShock 4" : "Xbox 360";
            TextView status = text((controller.attached && controller.profileReady ? "● " : "○ ") + output, 12, false);
            status.setTextColor(controller.attached && controller.profileReady ? 0xff8dd5ad : MUTED); item.addView(status);
            LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(dp(184), -2); params.rightMargin = dp(8);
            item.setContentDescription(tr("Jogador ", "Player ") + (i + 1) + ", " + controller.displayName() + ", " + output + (isSelected ? tr(", selecionado", ", selected") : ""));
            item.setOnClickListener(v -> {
                if (!controller.id().equals(selectedControllerId)) {
                    pendingButton = null; pendingAxis = null; axisCaptureBaseline.clear();
                    helpText.setText(tr("Abra um grupo para configurar este controle.", "Open a group to configure this controller."));
                }
                selectedControllerId = controller.id(); renderControllerList(); renderControllerDetails(); updatePendingInstruction();
            });
            controllerList.addView(item, params);
        }
    }

    private void renderControllerDetails() {
        Controller controller = selected();
        refreshingUi = true;
        if (resetMappingButton != null) resetMappingButton.setEnabled(controller != null);
        if (calibrateButton != null) calibrateButton.setEnabled(controller != null);
        if (liveControllerName != null) liveControllerName.setText(controller == null ? tr("Aguardando controle", "Waiting for a controller") : controller.displayName());
        if (outputPicker != null) { outputPicker.setEnabled(controller != null); outputPicker.setSelection(controller != null && controller.output.equals("ds4") ? 1 : 0); }
        inspectLightbar(controller);
        if(lightbarStatus!=null && lightbarButton!=null) {
            if(controller==null) {
                lightbarStatus.setText(tr("Conecte um controle DS4 para verificar a luz RGB.","Connect a DS4 controller to check for an RGB light."));
                lightbarButton.setVisibility(View.GONE);
            } else if(Build.VERSION.SDK_INT<31) {
                lightbarStatus.setText(tr("Luz RGB requer Android 12 ou superior.","RGB lights require Android 12 or later."));
                lightbarButton.setVisibility(View.GONE);
            } else if(!isDs4Controller(controller)) {
                lightbarStatus.setText(tr("A luz RGB só é configurável no controle DS4.","RGB light settings are only available for a DS4 controller."));
                lightbarButton.setVisibility(View.GONE);
            } else if(controller.rgbLight==null) {
                lightbarStatus.setText(tr("Android não expõe uma luz RGB controlável neste DS4.","Android does not expose a controllable RGB light on this DS4."));
                lightbarButton.setVisibility(View.GONE);
            } else {
                lightbarStatus.setText(controller.lightbarColor==Integer.MIN_VALUE
                        ?tr("Luz RGB controlável detectada.","Controllable RGB light detected.")
                        :tr("Cor salva: ","Saved color: ")+String.format(Locale.ROOT,"#%06X",controller.lightbarColor&0xffffff));
                lightbarButton.setVisibility(View.VISIBLE);
                lightbarButton.setText(controller.lightbarColor==Integer.MIN_VALUE
                        ?tr("Escolher cor da luz","Choose light color")
                        :tr("Alterar cor da luz","Change light color"));
                int lightColor = controller.lightbarColor == Integer.MIN_VALUE ? 0xff293033 : controller.lightbarColor;
                lightbarButton.setBackground(rounded(lightColor, BORDER, 10));
                lightbarButton.setTextColor(Color.luminance(lightColor) > .45 ? 0xff102125 : INK);
            }
        }
        MotionPair motionPair=controller==null?null:findMotionPair(controller);
        if(controller!=null) updateMotionSubscription(controller,motionPair);
        if(gyroToggle!=null) {
            boolean ds4=controller!=null && controller.output.equals("ds4");
            boolean available=ds4 && (motionPair!=null || controller.gyroEnabled);
            gyroToggle.setEnabled(available);
            gyroToggle.setChecked(ds4 && controller.gyroEnabled);
        }
        if(gyroStatus!=null) {
            if(controller==null) gyroStatus.setText(tr("Conecte um controle DS4 com sensores de movimento.","Connect a DS4 controller with motion sensors."));
            else if(!controller.output.equals("ds4")) gyroStatus.setText(tr("Disponível somente na saída DualShock 4.","Available only with DualShock 4 output."));
            else if(Build.VERSION.SDK_INT<31) gyroStatus.setText(tr("Giroscópio requer Android 12 ou superior.","Gyro requires Android 12 or later."));
            else if(motionPair==null) gyroStatus.setText(tr("Sensor DS4 não encontrado ou vínculo ambíguo.","DS4 sensor not found or pairing is ambiguous."));
            else gyroStatus.setText(controller.gyroEnabled?tr("Giroscópio e acelerômetro ativos.","Gyroscope and accelerometer active."):tr("Sensores DS4 encontrados · desligado.","DS4 sensors found · off."));
        }
        if (profileSummary != null) {
            if (controller == null) profileSummary.setText(tr("Nenhum controle detectado.","No controller selected."));
            else profileSummary.setText(controller.device.getName() + "\n" +
                    (controller.attached && controller.profileReady ? tr("● Perfil ativo no PC", "● Profile active on PC") : tr("○ Perfil salvo no celular", "○ Profile saved on phone")));
        }
        if (mappingRows != null) renderMappingRows(controller);
        refreshingUi = false;
        renderLiveState();
    }

    private void renderMappingRows(Controller controller) {
        mappingRows.removeAllViews();
        if (controller == null) {
            TextView empty = text(tr("Conecte um controle Bluetooth para editar o mapeamento.", "Connect a Bluetooth controller to edit its mapping."), 14, false);
            empty.setTextColor(MUTED); mappingRows.addView(empty); return;
        }
        mappingGroup(controller, "action", tr("Botões de ação", "Action buttons"), new String[]{"south", "east", "west", "north"}, false);
        mappingGroup(controller, "left-shoulder", tr("Lado esquerdo · gatilho e ombro", "Left side · trigger & shoulder"), new String[]{"l2", "l1"}, false);
        mappingGroup(controller, "right-shoulder", tr("Lado direito · gatilho e ombro", "Right side · trigger & shoulder"), new String[]{"r2", "r1"}, false);
        mappingGroup(controller, "center", tr("Botões centrais e sticks", "Center & stick buttons"), new String[]{"share", "options", "l3", "r3", "ps"}, false);
        mappingGroup(controller, "dpad", tr("Direcional", "D-pad"), new String[]{"dpad_up", "dpad_down", "dpad_left", "dpad_right"}, false);
        mappingGroup(controller, "axes", tr("Eixos e analógicos", "Axes & sticks"), MappingModel.AXES.toArray(new String[0]), true);
        updatePendingInstruction();
    }

    private void mappingGroup(Controller controller, String id, String title, String[] logicals, boolean axes) {
        LinearLayout group = new LinearLayout(this); group.setOrientation(LinearLayout.VERTICAL);
        group.setBackground(rounded(INSET, BORDER, 11));
        boolean capturingHere = java.util.Arrays.asList(logicals).contains(axes ? pendingAxis : pendingButton);
        boolean expanded = expandedMappingGroups.contains(id) || capturingHere;
        Button heading = button(title + "   " + (expanded ? "−" : "+"));
        heading.setGravity(Gravity.START | Gravity.CENTER_VERTICAL); heading.setTextSize(14);
        heading.setPadding(dp(12), dp(10), dp(12), dp(10));
        heading.setTextColor(expanded ? ACCENT : INK);
        heading.setContentDescription(title + (expanded ? tr(", recolher", ", collapse") : tr(", expandir", ", expand")));
        group.addView(heading, fullWidth());
        heading.setOnClickListener(v -> {
            if (!expandedMappingGroups.add(id)) expandedMappingGroups.remove(id);
            renderMappingRows(controller);
        });
        if (expanded) {
            for (String logical : logicals) {
                if (!axes && !MappingModel.BUTTONS.contains(logical)) continue;
                if (axes) {
                    MappingModel.AxisBinding axis = controller.mapping.axisBindings().get(logical);
                    String current = axis == null ? "—" : axisName(axis.source);
                    Button invert = button(tr("Inverter: ", "Invert: ") + (axis != null && axis.invert ? tr("sim", "on") : tr("não", "off")));
                    invert.setEnabled(axis != null);
                    invert.setOnClickListener(v -> {
                        MappingModel.AxisBinding before = controller.mapping.axisBindings().get(logical);
                        if (before == null) return;
                        controller.mapping = controller.mapping.withInverted(logical, !before.invert); persistAndSendProfile(controller);
                    });
                    group.addView(bindingRow(trAxisLabel(logical), current, () -> {
                        startAxisCapture(controller, logical);
                    }, invert, logical.equals(pendingAxis)), fullWidth());
                } else {
                    Integer key = controller.mapping.buttonSources().get(logical);
                    String current = key == null ? "—" : KeyEvent.keyCodeToString(key).replace("KEYCODE_", "").replace("BUTTON_", "");
                    group.addView(bindingRow(displayButtonLabel(logical), current, () -> {
                        pendingButton = logical; pendingAxis = null; axisCaptureBaseline.clear(); updatePendingInstruction(); renderMappingRows(controller);
                    }, null, logical.equals(pendingButton)), fullWidth());
                }
            }
        }
        mappingRows.addView(group, spaced());
    }

    private View bindingRow(String label, String current, Runnable remap, Button extra, boolean capturing) {
        LinearLayout row = new LinearLayout(this); row.setOrientation(LinearLayout.VERTICAL);
        row.setPadding(dp(12), dp(12), dp(12), dp(12));
        if (capturing) row.setBackground(rounded(0xff24383d, ACCENT, 9));
        LinearLayout info = new LinearLayout(this); info.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = text(label, 14, true);
        info.addView(title, new LinearLayout.LayoutParams(0, -2, 1));
        TextView source = text(current, 12, false); source.setTextColor(ACCENT);
        source.setGravity(Gravity.END); source.setMaxLines(2);
        info.addView(source, new LinearLayout.LayoutParams(dp(100), -2));
        row.addView(info, fullWidth());
        LinearLayout actions = new LinearLayout(this); actions.setPadding(0, dp(8), 0, 0);
        if (extra != null) {
            extra.setTextSize(12); LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(0, dp(48), 1);
            params.rightMargin = dp(8); actions.addView(extra, params);
        }
        Button change = button(capturing ? tr("Aguardando entrada…", "Waiting for input…") : tr("Mapear", "Map"));
        change.setTextColor(ACCENT); change.setOnClickListener(v -> remap.run());
        change.setContentDescription(tr("Mapear ", "Map ") + label);
        actions.addView(change, new LinearLayout.LayoutParams(0, dp(48), 1)); row.addView(actions, fullWidth());
        return row;
    }

    private void renderLiveState() {
        if (liveInput == null) return;
        Controller controller = selected();
        if (controller == null) {
            liveInput.setText(tr("Conecte um controle Bluetooth ao celular.", "Connect a Bluetooth controller to your phone."));
            if (controllerPreview != null) controllerPreview.setInput(null, null); return;
        }
        Map<String, Boolean> buttons = controller.mapping.mapButtons(controller.pressedKeys, controller.hatX, controller.hatY);
        List<String> active = new ArrayList<>();
        for (Map.Entry<String, Boolean> entry : buttons.entrySet()) if (entry.getValue()) active.add(displayButtonLabel(entry.getKey()));
        Map<String, Float> axes = controller.mapping.mapAxes(axis -> readCurrentAxis(controller, axis));
        if (controllerPreview != null) { controllerPreview.setOutput(controller.output.equals("ds4")); controllerPreview.setInput(buttons, axes); }
        liveInput.setText(tr("Pressionados: ","Pressed: ") + (active.isEmpty() ? tr("nenhum","none") : String.join(", ", active))
                + "\nLX " + f(axes.get("lx")) + "  LY " + f(axes.get("ly"))
                + "\nRX " + f(axes.get("rx")) + "  RY " + f(axes.get("ry"))
                + "\nL2 " + f(axes.get("lt")) + "  R2 " + f(axes.get("rt")));
    }
    private static String f(Float value) { return String.format(Locale.US, "%.2f", value == null ? 0f : value); }

    private Controller selected() {
        if (selectedControllerId == null) return null;
        for (Controller controller : controllers.values()) if (controller.id().equals(selectedControllerId)) return controller;
        return null;
    }
    private Controller findById(String id) {
        for (Controller controller : controllers.values()) if (controller.id().equals(id)) return controller;
        return null;
    }
    private JSONObject encodeMapping(MappingModel mapping) {
        try { return MappingCodec.encode(mapping); } catch (JSONException error) { return new JSONObject(); }
    }
    private MappingModel decodeMapping(JSONObject json) {
        try { return json == null ? MappingModel.standard() : MappingCodec.decode(json); }
        catch (JSONException | IllegalArgumentException error) { return MappingModel.standard(); }
    }

}
