# Controller Bridge — Android

> **100% AI-created project.**

Android client that forwards Bluetooth controllers recognized by the phone to the Windows host. Input capture runs while the app is open, and the app keeps the screen awake on its main screen.

## Install and connect

Two APK variants are provided. **PT-BR** installs as `com.controllerbridge.app`; **EN** installs as `com.controllerbridge.app.en`, so both can be installed together. Each starts and stays in its named language.

1. Pair the controller with Android in the system Bluetooth settings.
2. Install/start the Controller Bridge Windows host and its required virtual controller driver on the PC. The new Windows dashboard displays the PC IP, port **47653**, and pairing code.
3. In the phone app, choose **USB** or **Wi-Fi**:
   - **USB:** connect the phone to the PC, unlock it, allow USB debugging, then run `adb reverse tcp:47652 tcp:47652` on the PC for that cable connection. Keep `127.0.0.1` as the host address.
   - **Wi-Fi:** the Windows host starts Wi-Fi by default. Connect phone and PC to the same LAN, enter the PC's LAN IP, port **47653**, and the 32-character pairing code shown on the dashboard. The app pairs each PC once and saves the pairing key encrypted with Android Keystore. Wi-Fi uses authenticated AES-GCM messages; USB stays on local port 47652. The host does not add firewall rules automatically.
4. Tap **Connect to PC** and keep the app screen open while playing. Choose Xbox 360 or DualShock 4 output in either the phone app or Windows dashboard. On Windows, click a button on the controller diagram to choose it for remapping, then press the replacement physical button on the controller connected to the phone.
5. The two app variants keep their own fixed language.

Tap a controller row to choose which controller to configure; all attached controllers keep forwarding input. Each row shows its own output, and a Windows output/gyro change updates the matching phone profile without changing the controller currently selected for editing. If both sides edit the same revision, the host returns its accepted profile and the phone adopts it.

## DS4 motion and light

- On Android 12 or later, the **DS4 gyro** switch appears when a DS4 controller's own public input-device sensors can be paired unambiguously. It is available only for DualShock 4 output.
- A physical light-color control appears only when Android reports a controllable RGB light on the connected DS4. Color settings are saved separately for each phone controller profile.
- The exact gyro/light behavior still needs testing with a physical DS4 on the target phone. If Android does not expose the RGB light, the app hides the color picker and reports that it is unavailable.
- Motion uses only the connected controller's `InputDevice.getSensorManager()` (available since API 31), with both `TYPE_GYROSCOPE` and `TYPE_ACCELEROMETER` present and an unambiguous controller association. The app never substitutes the phone's internal sensors. Turning motion off or selecting Xbox output unregisters the sensor listener; the option is also synchronized when changed on Windows.
- Android 12 alone does not guarantee that a phone's Bluetooth/input driver exposes the DS4 sensors. If the sensors are absent or cannot be associated safely, enabling motion is unavailable; an already-enabled preference can still be turned off. This implementation does not read raw Bluetooth HID reports. Alternatives require a compatible Android driver/firmware exposing those input-device sensors, or a separate raw-HID implementation with transport access appropriate to that device (for example, a privileged/system integration). Neither alternative is implemented or guaranteed here. See the official [input-device sensor API](https://developer.android.com/reference/android/view/InputDevice#getSensorManager()).

## Local build

With Android SDK, Android Studio JBR Java 21, Gradle 8.2.1 and Android Gradle Plugin 8.2.1 already available locally:

```powershell
.\Build.ps1
```

The script builds both variants offline, runs focused mapping and Wi-Fi security checks, and writes `dist/ControllerBridge-Android-PT-BR-v0.5.0.apk` and `dist/ControllerBridge-Android-EN-v0.5.0.apk`.

## Current limits

- Capture works only while the app is open; background/locked-screen capture is not enabled.
- Physical controller input, motion, color-light availability, screen-awake behavior, and gameplay forwarding still need validation on the target phone and controller.
- Some third-party controllers may not expose every button/axis to Android; those inputs can be remapped when Android reports them.
- Wi-Fi starts automatically when the PC host opens. Enter the PC LAN address, port, and pairing code displayed by the host. The app does not create router or firewall rules.

---

# Controller Bridge — Android

> **Projeto criado 100% com IA.**

Cliente Android que encaminha ao host Windows os controles Bluetooth reconhecidos pelo celular. A captura funciona enquanto o app está aberto, e o aplicativo mantém a tela ligada na tela principal.

## Instalar e conectar

Há duas versões do APK. **PT-BR** instala como `com.controllerbridge.app`; **EN** instala como `com.controllerbridge.app.en`, então as duas podem ficar instaladas juntas. Cada versão abre e permanece no idioma indicado no nome.

1. Pareie o controle com o Android nas configurações Bluetooth do sistema.
2. Instale/inicie no PC Windows o host Controller Bridge e o driver de controle virtual exigido por ele. O novo painel Windows mostra o IP do PC, a porta **47653** e o código de pareamento.
3. No app do celular, escolha **USB** ou **Wi-Fi**:
   - **USB:** conecte o celular ao PC, desbloqueie-o e autorize a depuração USB. A cada conexão do cabo, execute `adb reverse tcp:47652 tcp:47652` no PC. Mantenha o endereço do host como `127.0.0.1`.
   - **Wi-Fi:** o host Windows inicia em Wi-Fi por padrão. Conecte celular e PC à mesma rede local e informe o IP do PC, a porta **47653** e o código de pareamento de 32 caracteres do painel. O pareamento é salvo por PC com a chave cifrada pelo Android Keystore. Wi-Fi usa mensagens autenticadas AES-GCM; USB continua na porta local 47652. O host não configura regras de firewall automaticamente.
4. Toque em **Conectar ao PC** e deixe a tela do app aberta durante o jogo. Escolha a saída Xbox 360 ou DualShock 4 no celular ou no painel Windows. No Windows, clique em um botão do desenho e pressione o botão físico que deve substituí-lo no controle conectado ao celular.
5. Cada variante mantém seu idioma fixo.

Toque na linha de um controle para escolher qual configurar; todos os controles vinculados continuam enviando entrada. Cada linha mostra sua própria saída, e uma mudança de saída/giroscópio no Windows atualiza o perfil correspondente no celular sem alterar o controle selecionado para edição. Se os dois lados editarem a mesma revisão, o host devolve o perfil aceito e o celular o aplica.

## Movimento e luz do DS4

- No Android 12 ou superior, a opção **Giroscópio do DS4** aparece quando os sensores públicos do próprio controle DS4 podem ser associados sem ambiguidade. Ela só fica disponível para a saída DualShock 4.
- O seletor de cor física aparece somente quando Android informa uma luz RGB controlável no DS4 conectado. A cor fica salva separadamente em cada perfil de controle do celular.
- O funcionamento real do giroscópio e da luz ainda precisa ser verificado com um DS4 físico no celular de destino. Se Android não expuser a luz RGB, o app esconde o seletor e informa que o recurso não está disponível. Quando ativado com a saída DS4, o host Windows também serve os dados localmente por DSU/Cemuhook UDP em `127.0.0.1:26760` para provedores de emulador compatíveis.
- O movimento usa somente `InputDevice.getSensorManager()` do controle conectado (disponível desde a API 31), com `TYPE_GYROSCOPE` e `TYPE_ACCELEROMETER` presentes e associação inequívoca ao controle. O app nunca usa os sensores internos do celular como substitutos. Desativar o movimento ou escolher a saída Xbox remove o listener dos sensores; a opção também é sincronizada quando alterada no Windows.
- Ter Android 12 não garante que o driver Bluetooth/entrada do celular exponha os sensores do DS4. Se os sensores não estiverem disponíveis ou a associação for ambígua, não é possível ativar o movimento; uma preferência já ativada ainda pode ser desligada. Esta implementação não lê relatórios Bluetooth HID brutos. As alternativas exigem um driver/firmware Android compatível que exponha esses sensores de entrada ou uma implementação separada de HID bruto com acesso ao transporte adequado ao dispositivo (por exemplo, integração privilegiada/de sistema). Nenhuma dessas alternativas está implementada ou garantida aqui. Consulte a [API oficial de sensores do dispositivo de entrada](https://developer.android.com/reference/android/view/InputDevice#getSensorManager()).

## Build local

Com Android SDK, Java 21 do Android Studio JBR, Gradle 8.2.1 e Android Gradle Plugin 8.2.1 já disponíveis localmente:

```powershell
.\Build.ps1
```

O script compila as duas versões offline, executa verificações focadas de mapeamento e segurança Wi-Fi, e gera `dist/ControllerBridge-Android-PT-BR-v0.5.0.apk` e `dist/ControllerBridge-Android-EN-v0.5.0.apk`.

## Limites atuais

- A captura funciona somente enquanto o app está aberto; não há captura em segundo plano ou com a tela bloqueada.
- Entrada física, giroscópio, disponibilidade da luz RGB, tela sempre ligada e encaminhamento para jogos ainda precisam ser verificados no celular e controle reais.
- Alguns controles paralelos podem não expor todos os botões/eixos ao Android; essas entradas podem ser remapeadas quando o Android as reconhece.
- O Wi-Fi inicia automaticamente quando o host do PC é aberto. Informe o IP local, a porta e o código de pareamento exibidos pelo host. O app não cria regras de roteador ou firewall.




