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
        getWindow().setStatusBarColor(0xff11161d);
        getWindow().setNavigationBarColor(0xff0c1016);
        getWindow().getDecorView().setSystemUiVisibility(0);
        inputManager = (InputManager)getSystemService(Context.INPUT_SERVICE);
        profiles = new PhoneProfileStore(this);
        wifiPairings = new WifiPairingStore(this);
        String savedLanguage=getSharedPreferences("controller-bridge",MODE_PRIVATE).getString("language",null);
        if(savedLanguage==null) {
            english=!"pt".equals(BuildConfig.DEFAULT_LANGUAGE);
            getSharedPreferences("controller-bridge",MODE_PRIVATE).edit().putString("language",english?"en":"pt").apply();
        } else english=!"pt".equals(savedLanguage);
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
        ScrollView scroll = new ScrollView(this);
        scroll.setFillViewport(true);
        scroll.setBackgroundColor(0xff11161d);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(16), dp(14), dp(16), dp(24));
        root.setBackgroundColor(0xff11161d);
        scroll.addView(root);

        LinearLayout languageRow=new LinearLayout(this); languageRow.setOrientation(LinearLayout.HORIZONTAL); languageRow.setGravity(Gravity.CENTER_VERTICAL);
        TextView languageLabel=text(tr("IDIOMA","LANGUAGE"),10,true); languageLabel.setTextColor(0xff8292a4);
        languageRow.addView(languageLabel,new LinearLayout.LayoutParams(0,LinearLayout.LayoutParams.WRAP_CONTENT,1f));
        Button ptButton=button("PT"); Button enButton=button("EN");
        ptButton.setTextColor(english?0xffa9b7c5:Color.WHITE); enButton.setTextColor(english?Color.WHITE:0xffa9b7c5);
        ptButton.setBackground(rounded(english?0xff202833:0xff397cf6,english?0xff303c49:0xff397cf6,7));
        enButton.setBackground(rounded(english?0xff397cf6:0xff202833,english?0xff397cf6:0xff303c49,7));
        languageRow.addView(ptButton,new LinearLayout.LayoutParams(dp(48),dp(36)));
        LinearLayout.LayoutParams enParams=new LinearLayout.LayoutParams(dp(48),dp(36)); enParams.leftMargin=dp(6); languageRow.addView(enButton,enParams);
        root.addView(languageRow,fullWidth());
        View.OnClickListener chooseLanguage=view->{boolean next=view==enButton; if(next==english)return; english=next;
            String addressDraft=hostAddress==null?null:hostAddress.getText().toString();
            String pairingDraft=pairingCodeInput==null?null:pairingCodeInput.getText().toString();
            getSharedPreferences("controller-bridge",MODE_PRIVATE).edit().putString("language",english?"en":"pt").apply();
            setupLabels(); buildUi();
            if(addressDraft!=null)hostAddress.setText(addressDraft);
            if(pairingDraft!=null)pairingCodeInput.setText(pairingDraft);
            updateTransportUi(); renderControllerList(); renderControllerDetails(); };
        ptButton.setOnClickListener(chooseLanguage); enButton.setOnClickListener(chooseLanguage);

        TextView title = text("CONTROLLER BRIDGE", 20, true);
        title.setTextColor(0xfff2f6fb);
        title.setLetterSpacing(.04f);
        root.addView(title);
        TextView subtitle=text(tr("CONTROLE BLUETOOTH  ·  ANDROID → WINDOWS","BLUETOOTH CONTROLLER  ·  ANDROID → WINDOWS"), 11, true);
        subtitle.setTextColor(0xff8292a4);
        root.addView(subtitle);

        LinearLayout connectionCard = card(root, tr("CONEXÃO COM O PC","PC CONNECTION"));
        connectionStatus = text(tr("Desconectado","Disconnected"), 15, true);
        connectionStatus.setTextColor(0xffffc66d);
        connectionCard.addView(connectionStatus);
        transportPicker=new Spinner(this);
        ArrayAdapter<String> transportAdapter=new ArrayAdapter<>(this,android.R.layout.simple_spinner_item,
                new String[]{tr("USB · ADB Reverse","USB · ADB Reverse"),tr("Wi-Fi · mesma rede","Wi-Fi · same network")});
        transportAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        transportPicker.setAdapter(transportAdapter); transportPicker.setSelection(wifiMode?1:0,false);
        connectionCard.addView(transportPicker,fullWidth());
        hostAddress = new EditText(this);
        hostAddress.setSingleLine(true);
        hostAddress.setHint(tr("IP local do PC","PC local IP address"));
        hostAddress.setHintTextColor(0xff7f8d9d);
        hostAddress.setTextColor(0xffe8edf3);
        hostAddress.setTextSize(14);
        hostAddress.setPadding(dp(12), dp(7), dp(12), dp(7));
        hostAddress.setBackground(rounded(0xff18212b, 0xff334252, 8));
        hostAddress.setText(getSharedPreferences("controller-bridge", MODE_PRIVATE).getString(wifiMode?"wifi_host":"host", wifiMode?"":"127.0.0.1"));
        connectionCard.addView(hostAddress, fullWidth());
        pairingCodeInput=new EditText(this); pairingCodeInput.setSingleLine(true);
        pairingCodeInput.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_PASSWORD);
        pairingCodeInput.setHint(tr("Código de pareamento exibido pelo host (32 caracteres)","Pairing code shown by the host (32 characters)"));
        pairingCodeInput.setHintTextColor(0xff7f8d9d); pairingCodeInput.setTextColor(0xffe8edf3); pairingCodeInput.setTextSize(14);
        pairingCodeInput.setPadding(dp(12),dp(7),dp(12),dp(7)); pairingCodeInput.setBackground(rounded(0xff18212b,0xff334252,8));
        connectionCard.addView(pairingCodeInput,fullWidth());
        transportPicker.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(android.widget.AdapterView<?> parent,View view,int position,long id) {
                boolean next=position==1; if(next==wifiMode)return;
                if(bridge!=null) { transportPicker.setSelection(wifiMode?1:0); return; }
                String current=hostAddress.getText().toString().trim();
                getSharedPreferences("controller-bridge",MODE_PRIVATE).edit().putString(wifiMode?"wifi_host":"host",current).apply();
                wifiMode=next; getSharedPreferences("controller-bridge",MODE_PRIVATE).edit().putBoolean("wifi_mode",wifiMode).apply();
                hostAddress.setText(getSharedPreferences("controller-bridge",MODE_PRIVATE).getString(wifiMode?"wifi_host":"host",wifiMode?"":"127.0.0.1"));
                updateTransportUi();
            }
            @Override public void onNothingSelected(android.widget.AdapterView<?> parent) { }
        });
        connectionButton = button(tr("Conectar ao PC","Connect to PC"));
        connectionButton.setBackground(rounded(0xff397cf6, 0xff397cf6, 10));
        connectionButton.setTextColor(Color.WHITE);
        connectionCard.addView(connectionButton, fullWidth());
        connectionButton.setOnClickListener(v -> toggleConnection());
        usbNote=text(tr("USB · Autorize a depuração e configure `adb reverse tcp:47652 tcp:47652` no PC.","USB · Allow debugging and run `adb reverse tcp:47652 tcp:47652` on the PC."), 12, false);
        usbNote.setTextColor(0xffb4c0cd); connectionCard.addView(usbNote);
        wifiNote=text(tr("Wi-Fi · Inicie o host com `--wifi`, use a mesma rede, informe o IP e o código mostrado no PC.","Wi-Fi · Start the host with `--wifi`, join the same network, and enter the PC IP and displayed code."), 11, true);
        wifiNote.setTextColor(0xffb4c0cd); connectionCard.addView(wifiNote);
        updateTransportUi();

        LinearLayout controllerCard = card(root, tr("CONTROLES CONECTADOS","CONNECTED CONTROLLERS"));
        controllerList = new LinearLayout(this);
        controllerList.setOrientation(LinearLayout.VERTICAL);
        controllerCard.addView(controllerList, fullWidth());
        TextView outputLabel=text(tr("SAÍDA DO PC","PC OUTPUT"),10,true); outputLabel.setTextColor(0xff8494a6);
        outputLabel.setPadding(0,dp(12),0,0); controllerCard.addView(outputLabel);
        outputPicker = new Spinner(this);
        outputAdapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, new String[]{"Xbox 360 (XInput)", "DualShock 4"});
        outputAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        outputPicker.setAdapter(outputAdapter);
        controllerCard.addView(outputPicker, fullWidth());
        gyroToggle = new Switch(this);
        gyroToggle.setText(tr("Giroscópio do DS4","DS4 gyro")); gyroToggle.setTextSize(14); gyroToggle.setTextColor(0xffe5ebf2);
        gyroToggle.setPadding(0,dp(8),0,dp(2));
        controllerCard.addView(gyroToggle,fullWidth());
        gyroStatus=text(tr("Disponível somente na saída DualShock 4.","Available only with DualShock 4 output."),11,false); gyroStatus.setTextColor(0xff8f9eae);
        controllerCard.addView(gyroStatus,fullWidth());
        lightbarStatus=text(tr("Luz RGB física indisponível neste controle.","Physical RGB light unavailable on this controller."),11,false);
        lightbarStatus.setTextColor(0xff8f9eae); controllerCard.addView(lightbarStatus,fullWidth());
        lightbarButton=button(tr("Escolher cor da luz","Choose light color"));
        lightbarButton.setOnClickListener(v->{Controller current=selected(); if(current!=null) showLightbarPicker(current);});
        controllerCard.addView(lightbarButton,fullWidth());
        lightbarButton.setVisibility(View.GONE);
        gyroToggle.setOnCheckedChangeListener((button,checked)->{
            if(refreshingUi)return;
            Controller current=selected(); if(current==null)return;
            if(!current.output.equals("ds4") || findMotionPair(current)==null) { renderControllerDetails(); return; }
            current.gyroEnabled=checked;
            refreshMotionSubscriptions(); persistAndSendProfile(current); renderControllerDetails();
        });
        outputPicker.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(android.widget.AdapterView<?> parent, View view, int position, long id) {
                if (refreshingUi) return;
                Controller current = selected();
                if (current == null) return;
                String output = position == 1 ? "ds4" : "xbox360";
                if (!current.output.equals(output)) {
                    current.output = output;
                    if (!output.equals("ds4")) current.gyroEnabled=false;
                    refreshMotionSubscriptions(); persistAndSendProfile(current); renderControllerDetails();
                }
            }
            @Override public void onNothingSelected(android.widget.AdapterView<?> parent) { }
        });
        profileSummary = text(tr("Nenhum controle detectado.","No controller detected."), 13, false);
        profileSummary.setTextColor(0xff8f9eae);
        controllerCard.addView(profileSummary);

        LinearLayout liveCard = card(root, tr("ENTRADA AO VIVO","LIVE INPUT"));
        liveInput = text(tr("Conecte um controle Bluetooth ao celular.","Connect a Bluetooth controller to your phone."), 14, false);
        liveInput.setPadding(dp(12), dp(10), dp(12), dp(10));
        liveInput.setTypeface(android.graphics.Typeface.MONOSPACE);
        liveInput.setBackground(rounded(0xff18212b, 0xff2b3744, 8));
        liveCard.addView(liveInput, fullWidth());

        LinearLayout mappingCard = card(root, tr("MAPEAMENTO","MAPPING"));
        helpText = text(tr("O app usa o layout padrão Android. Toque em Mapear e pressione o novo botão no controle.","The app starts with Android's standard layout. Tap Map and press the replacement controller button."), 13, false);
        helpText.setTextColor(0xffa7b5c3);
        mappingCard.addView(helpText);
        Button reset = button(tr("Restaurar padrão","Restore defaults"));
        mappingCard.addView(reset, fullWidth());
        reset.setOnClickListener(v -> resetSelectedMapping());
        Button calibrate = button(tr("Aplicar calibração do controle","Apply controller calibration"));
        mappingCard.addView(calibrate, fullWidth());
        calibrate.setOnClickListener(v -> calibrateSelectedAxes());
        mappingRows = new LinearLayout(this);
        mappingRows.setOrientation(LinearLayout.VERTICAL);
        mappingCard.addView(mappingRows, fullWidth());
        LinearLayout noteCard=card(root,tr("DICA","TIP"));
        noteCard.addView(text(tr("Mantenha esta tela aberta durante o jogo. Se uma entrada não aparecer no teste ao vivo, ela não será enviada ao PC.","Keep this screen open while playing. Inputs that do not appear in the live test will not be sent to the PC."),12,false));
        noteCard.addView(text(tr("O sensor de movimento pode ser usado na saída DS4. O Wi-Fi usa pareamento com código; a cor da luz só aparece quando Android expõe RGB controlável.","Motion sensors can be forwarded to DS4 output. Wi-Fi uses code-based pairing; light color appears only when Android exposes controllable RGB."),11,false));

        setContentView(scroll);
        updateConnectionUi();
        updatePendingInstruction();
        renderControllerList();
        renderControllerDetails();
    }

    private void updateConnectionUi() {
        if(connectionButton==null || connectionStatus==null) return;
        updateTransportUi();
        if(bridge==null) {
            connectionButton.setText(tr("Conectar ao PC","Connect to PC"));
            connectionStatus.setText(tr("Desconectado","Disconnected"));
        } else if(sessionReady) {
            connectionButton.setText(tr("Desconectar","Disconnect"));
            connectionStatus.setText(connectedOverWifi?tr("Wi-Fi seguro conectado","Secure Wi-Fi connected"):tr("Conectado ao host do PC por USB","Connected to PC host over USB"));
        } else {
            connectionButton.setText(tr("Cancelar conexão","Cancel connection"));
            String host=hostAddress.getText().toString().trim();
            connectionStatus.setText(tr("Conectando a ","Connecting to ")+host+":"+(wifiMode?WIFI_PORT:PORT)+"…");
        }
    }

    private void updateTransportUi() {
        if(hostAddress==null)return;
        boolean editable=bridge==null && wifiMode;
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
        if(pendingButton!=null && controller!=null)
            helpText.setText(tr("Agora pressione o botão físico que deve corresponder a ","Press the physical button that should map to ")+friendlyButtons.get(pendingButton)+".");
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
        outer.setPadding(dp(14),dp(10),dp(14),dp(12)); outer.setBackground(rounded(0xff202833,0xff303c49,13));
        LinearLayout.LayoutParams params=fullWidth(); params.topMargin=dp(12); parent.addView(outer,params);
        TextView title=text(heading,11,true); title.setTextColor(0xff6fa5ff); title.setLetterSpacing(.06f);
        outer.addView(title); return outer;
    }
    private TextView text(String value, int sp, boolean bold) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(sp);
        view.setTextColor(0xffe5ebf2);
        if (bold) view.setTypeface(null, android.graphics.Typeface.BOLD);
        view.setPadding(0, 6, 0, 6);
        return view;
    }
    private Button button(String value) {
        Button view = new Button(this);
        view.setText(value);
        view.setAllCaps(false);
        view.setTextSize(13);
        view.setTextColor(0xffe8eef5);
        view.setMinHeight(dp(42)); view.setMinWidth(0);
        view.setPadding(dp(10),dp(4),dp(10),dp(4));
        view.setBackground(rounded(0xff2b3744,0xff3a4a5a,9));
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
        if (controller.id().equals(selectedControllerId)) selectedControllerId = controllers.isEmpty() ? null : controllers.values().iterator().next().id();
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
        int initial = controller.lightbarColor == Integer.MIN_VALUE ? 0xff397cf6 : controller.lightbarColor;
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
        if(!shouldListen || controller.motionRegistered) return;
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
        InputDevice.MotionRange bestRange = null;
        float bestMagnitude = .35f;
        for (InputDevice.MotionRange range : controller.device.getMotionRanges()) {
            if ((range.getSource() & InputDevice.SOURCE_CLASS_JOYSTICK) == 0) continue;
            float raw = event.getAxisValue(range.getAxis());
            if (Math.abs(raw) <= bestMagnitude) continue;
            bestMagnitude = Math.abs(raw);
            bestRange = range;
        }
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
            profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
            persistAndSendProfile(controller);
            helpText.setText(tr("Eixo ","Axis ") + axis + tr(" remapeado para "," mapped to ") + axisName(range.getAxis()) + ".");
            renderControllerDetails();
            return;
        }
        helpText.setText(tr("Ainda não detectei movimento. Mova apenas o eixo que deseja mapear.","No axis movement detected. Move only the axis you want to map."));
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
        connectionButton.setText(tr("Cancelar conexão","Cancel connection"));
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
        if ("profile".equals(type) && controller != null) {
            String returnedId = message.optString("profile_id", "");
            if (!controller.profileId.equals(returnedId)) { connectionStatus.setText(tr("O PC retornou um perfil incompatível.","The PC returned an incompatible profile.")); return; }
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
            renderControllerDetails();
            sendState(controller);
        } else if ("profile_saved".equals(type) && controller != null) {
            if (!controller.profileId.equals(message.optString("profile_id", ""))) return;
            controller.revision = Math.max(controller.revision, message.optInt("revision", controller.revision));
            int ackRevision = message.optInt("revision", controller.revision);
            controller.revision = Math.max(controller.revision, ackRevision);
            controller.pendingProfileSave = false;
            String currentJson = controller.output + ":" + controller.gyroEnabled + ":" + encodeMapping(controller.mapping);
            if (controller.pendingProfileJson != null && !controller.pendingProfileJson.equals(currentJson)) {
                controller.pendingProfileJson = null;
                profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
                persistAndSendProfile(controller);
            } else {
                controller.pendingProfileJson = null;
                profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
            }
            connectionStatus.setText(tr("Perfil salvo no PC · revisão ","Profile saved on PC · revision ") + controller.revision);
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
            if (controller != null) { reconcilePendingProfile(controller);
                if (message.optString("code", "").equals("output_unavailable")) controller.profileReady = false; }
        }
    }

    @Override public void onDisconnected(BridgeClient source, String reason) {
        if (bridge != source) return;
        if(pendingPairingWasStored && pendingPairingHost!=null && "Falha de autenticação Wi-Fi.".equals(reason)) wifiPairings.remove(pendingPairingHost);
        clearPendingPairing();
        reconcilePendingProfiles();
        bridge = null;
        sessionReady = false;
        connectedOverWifi=false;
        for (Controller controller : controllers.values()) { controller.attached = false; controller.profileReady = false; clearInput(controller, false); }
        refreshMotionSubscriptions();
        connectionStatus.setText(tr("Desconectado: ","Disconnected: ") + displayReason(reason));
        connectionButton.setText(tr("Conectar ao PC","Connect to PC"));
        updateTransportUi();
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
        if (controller.pendingProfileSave) {
            controller.revision = Math.max(controller.revision, controller.pendingRevision);
            profiles.put(controller.profileId, controller.revision, controller.output, controller.gyroEnabled, encodeMapping(controller.mapping));
        }
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
        connectionButton.setText(tr("Conectar ao PC","Connect to PC"));
        old.closeAfterFlush();
        updateTransportUi();
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
        if (all.isEmpty()) {
            TextView empty=text(tr("Nenhum controle encontrado. Conecte um controle Bluetooth ao celular.","No controllers found. Connect a Bluetooth controller to your phone."),13,false);
            empty.setTextColor(0xff9aa9b8); empty.setPadding(dp(10),dp(12),dp(10),dp(12));
            empty.setBackground(rounded(0xff18212b,0xff2d3946,9)); controllerList.addView(empty,fullWidth());
            return;
        }
        for (int i=0;i<all.size();i++) {
            Controller controller=all.get(i);
            boolean selected=controller.id().equals(selectedControllerId);
            LinearLayout item=new LinearLayout(this); item.setOrientation(LinearLayout.HORIZONTAL);
            item.setGravity(Gravity.CENTER_VERTICAL); item.setPadding(dp(10),dp(8),dp(10),dp(8));
            item.setBackground(rounded(selected?0xff243650:0xff18212b,selected?0xff4b8dff:0xff2d3946,10));
            item.setMinimumHeight(dp(66)); item.setClickable(true); item.setFocusable(true);
            LinearLayout.LayoutParams itemParams=fullWidth(); itemParams.bottomMargin=dp(7);
            TextView index=new TextView(this); index.setText(Integer.toString(i+1)); index.setTextSize(14); index.setGravity(Gravity.CENTER);
            index.setTextColor(selected?Color.WHITE:0xffafbdcb); index.setBackground(rounded(selected?0xff397cf6:0xff2b3744,selected?0xff397cf6:0xff3a4a5a,24));
            LinearLayout.LayoutParams badgeParams=new LinearLayout.LayoutParams(dp(38),dp(38)); badgeParams.setMargins(0,0,dp(10),0); item.addView(index,badgeParams);
            LinearLayout labels=new LinearLayout(this); labels.setOrientation(LinearLayout.VERTICAL);
            TextView name=text(controller.displayName(),15,true); name.setSingleLine(true); name.setEllipsize(android.text.TextUtils.TruncateAt.END);
            TextView state=text(controller.attached?tr("Conectado ao PC","Connected to PC"):tr("Conectado ao celular · aguardando PC","Connected to phone · waiting for PC"),11,false);
            state.setTextColor(controller.attached?0xff7bd69c:0xff93a3b4);
            labels.addView(name); labels.addView(state);
            item.addView(labels,new LinearLayout.LayoutParams(0,LinearLayout.LayoutParams.WRAP_CONTENT,1f));
            TextView selectedTag=text(selected?tr("EDITANDO","EDITING"):"›",selected?9:22,true);
            selectedTag.setTextColor(selected?0xff8bb7ff:0xff8292a4); selectedTag.setGravity(Gravity.CENTER);
            item.addView(selectedTag,new LinearLayout.LayoutParams(selected?dp(68):dp(26),LinearLayout.LayoutParams.WRAP_CONTENT));
            item.setContentDescription(controller.displayName()+ (selected?tr(", selecionado para configurar",", selected for setup"):""));
            item.setOnClickListener(v->{selectedControllerId=controller.id();renderControllerList();renderControllerDetails();});
            controllerList.addView(item,itemParams);
        }
    }

    private void renderControllerDetails() {
        Controller controller = selected();
        refreshingUi = true;
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
                lightbarButton.setBackground(rounded(controller.lightbarColor==Integer.MIN_VALUE?0xff2b3744:controller.lightbarColor,0xff3a4a5a,9));
            }
        }
        MotionPair motionPair=controller==null?null:findMotionPair(controller);
        if(controller!=null) updateMotionSubscription(controller,motionPair);
        if(gyroToggle!=null) {
            boolean ds4=controller!=null && controller.output.equals("ds4");
            boolean available=ds4 && motionPair!=null;
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
            else profileSummary.setText(tr("Perfil: ","Profile: ") + controller.device.getName() + " · "
                    + (controller.output.equals("ds4") ? "DualShock 4" : "Xbox 360") + " · "
                    + (controller.attached ? tr("vinculado ao PC","linked to PC") : tr("aguardando conexão","waiting for connection")));
        }
        if (mappingRows != null) renderMappingRows(controller);
        refreshingUi = false;
        renderLiveState();
    }

    private void renderMappingRows(Controller controller) {
        mappingRows.removeAllViews();
        if (controller == null) {
            mappingRows.addView(text(tr("Conecte um controle Bluetooth ao Android para editar o mapeamento.","Connect a Bluetooth controller to edit its mapping."), 13, false));
            return;
        }
        for (String logical : MappingModel.BUTTONS) {
            Integer key = controller.mapping.buttonSources().get(logical);
            String current = key == null ? "—" : KeyEvent.keyCodeToString(key);
            mappingRows.addView(bindingRow(friendlyButtons.get(logical), current, () -> {
                pendingButton = logical; pendingAxis = null;
                helpText.setText(tr("Agora pressione o botão físico que deve corresponder a ","Press the physical button that should map to ") + friendlyButtons.get(logical) + ".");
            }, null, false));
        }
        String[] axisLabels = {trAxisLabel("lx"),trAxisLabel("ly"),trAxisLabel("rx"),trAxisLabel("ry"),trAxisLabel("lt"),trAxisLabel("rt")};
        for (int i = 0; i < MappingModel.AXES.size(); i++) {
            String logical = MappingModel.AXES.get(i);
            MappingModel.AxisBinding axis = controller.mapping.axisBindings().get(logical);
            String current = axis == null ? "—" : axisName(axis.source) + (axis.invert ? " · " + tr("invertido","inverted") : "");
            int axisIndex = i;
            Button invert = button(tr("Inverter: ","Invert: ") + (axis != null && axis.invert ? tr("sim","on") : tr("não","off")));
            invert.setOnClickListener(v -> {
                MappingModel.AxisBinding before = controller.mapping.axisBindings().get(logical);
                if (before == null) return;
                controller.mapping = controller.mapping.withInverted(logical, !before.invert);
                persistAndSendProfile(controller);
            });
            mappingRows.addView(bindingRow(axisLabels[axisIndex], current, () -> {
                pendingAxis = logical; pendingButton = null;
                helpText.setText(tr("Mova somente o eixo que deseja usar para ","Move only the axis you want to use for ") + axisLabels[axisIndex] + ".");
            }, invert, true));
        }
    }

    private View bindingRow(String label, String current, Runnable remap, Button extra, boolean compact) {
        LinearLayout row = new LinearLayout(this);
        row.setOrientation(LinearLayout.HORIZONTAL);
        row.setGravity(android.view.Gravity.CENTER_VERTICAL);
        row.setPadding(dp(8), dp(5), dp(6), dp(5));
        row.setBackground(rounded(0xff18212b,0xff2d3946,8));
        LinearLayout.LayoutParams rowParams=fullWidth(); rowParams.bottomMargin=dp(5);
        LinearLayout info = new LinearLayout(this);
        info.setOrientation(LinearLayout.VERTICAL);
        TextView title = text(label, compact ? 13 : 14, true);
        TextView source = text(current, 11, false);
        source.setTextColor(0xff8797a8); source.setSingleLine(true); source.setEllipsize(android.text.TextUtils.TruncateAt.END);
        info.addView(title); info.addView(source);
        row.addView(info, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1f));
        if (extra != null) {
            extra.setTextSize(10); extra.setSingleLine(true);
            LinearLayout.LayoutParams extraParams=new LinearLayout.LayoutParams(dp(88),dp(38));
            extraParams.setMargins(dp(3),0,dp(3),0); row.addView(extra,extraParams);
        }
        Button change = button(tr("Mapear","Map"));
        change.setTextSize(11); change.setSingleLine(true);
        change.setOnClickListener(v -> remap.run());
        row.addView(change, new LinearLayout.LayoutParams(dp(78),dp(38)));
        row.setLayoutParams(rowParams);
        return row;
    }

    private void renderLiveState() {
        if (liveInput == null) return;
        Controller controller = selected();
        if (controller == null) { liveInput.setText(tr("Conecte um controle Bluetooth ao celular.","Connect a Bluetooth controller to your phone.")); return; }
        Map<String, Boolean> buttons = controller.mapping.mapButtons(controller.pressedKeys, controller.hatX, controller.hatY);
        List<String> active = new ArrayList<>();
        for (Map.Entry<String, Boolean> entry : buttons.entrySet()) if (entry.getValue()) active.add(friendlyButtons.get(entry.getKey()));
        Map<String, Float> axes = controller.mapping.mapAxes(axis -> readCurrentAxis(controller, axis));
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

