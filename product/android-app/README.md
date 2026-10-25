# Controller Bridge — Android

> **100% AI-created project.**

Android client that forwards Bluetooth controllers recognized by the phone to the Windows host. Input capture runs while the app is open, and the app keeps the screen awake on its main screen.

## Install and use

1. Pair the controller with Android in the system Bluetooth settings.
2. Install/start the Controller Bridge Windows host and its required virtual controller driver on the PC.
3. Connect the phone to the PC over USB, unlock it, and accept **Allow USB debugging**. Select “Always allow from this computer” if desired.
4. On the PC, run once for each USB connection:

   ```powershell
   adb reverse tcp:47652 tcp:47652
   ```

5. Open Controller Bridge on the phone, keep the address at `127.0.0.1`, and tap **Connect to PC**. Keep the app screen open while playing.
6. Choose Xbox 360 or DualShock 4 output. The app starts with Android's standard layout. Tap **Map** to change one button, or invert/remap/calibrate an axis.
7. For a supported DS4 motion sensor on Android 12 or later, choose DualShock 4 output and enable **DS4 gyro**. The sensor is paired by controller descriptor, or only when there is exactly one DS4 controller and one DS4 sensor.

Wi-Fi is shown as unavailable until authenticated Wi-Fi pairing is implemented by the host. The PT/EN selector switches the app language without disconnecting the session.

## Local build

With Android SDK, Android Studio JBR Java 21, Gradle 8.2.1 and Android Gradle Plugin 8.2.1 already available locally:

```powershell
.\Build.ps1
```

The script builds offline, runs focused mapping/device checks, and copies the installer to `dist/ControllerBridge-android.apk`.

## Current limits

- Capture works only while the app is open; background/locked-screen capture is not enabled.
- Physical controller input, gyro axes, and screen-awake behavior still need validation on the target phone and controller. Local checks do not replace that test.
- Some third-party controllers may not expose every button/axis to Android; those inputs can be remapped when Android reports them.
- Physical DS4 lightbar control is not available in this build.
- Wi-Fi mode is unavailable; the host currently accepts the USB/ADB local connection only.

---

# Controller Bridge — Android

> **Projeto criado 100% com IA.**

Cliente Android que encaminha ao host Windows os controles Bluetooth reconhecidos pelo celular. A captura funciona enquanto o app está aberto, e o aplicativo mantém a tela ligada na tela principal.

## Instalação e uso

1. Pareie o controle com o Android nas configurações Bluetooth do sistema.
2. Instale/inicie no PC Windows o host Controller Bridge e o driver de controle virtual exigido por ele.
3. Conecte o celular ao PC por USB, desbloqueie-o e aceite **Permitir depuração USB**. Se quiser, marque “Sempre permitir deste computador”.
4. No PC, execute uma vez a cada conexão USB:

   ```powershell
   adb reverse tcp:47652 tcp:47652
   ```

5. Abra o Controller Bridge no celular, mantenha o endereço `127.0.0.1` e toque em **Conectar ao PC**. Deixe a tela do app aberta durante o jogo.
6. Escolha a saída Xbox 360 ou DualShock 4. O app começa com o layout padrão do Android. Toque em **Mapear** para mudar um botão, ou inverta/remapeie/calibre um eixo.
7. Para um sensor de movimento DS4 compatível no Android 12 ou superior, escolha a saída DualShock 4 e ative **Giroscópio do DS4**. O sensor é associado pelo descritor do controle, ou somente quando há exatamente um controle DS4 e um sensor DS4.

O Wi-Fi aparece como indisponível até o host implementar pareamento Wi-Fi autenticado. O seletor PT/EN troca o idioma do app sem desconectar a sessão.

## Build local

Com Android SDK, Java 21 do Android Studio JBR, Gradle 8.2.1 e Android Gradle Plugin 8.2.1 já disponíveis localmente:

```powershell
.\Build.ps1
```

O script compila offline, executa verificações focadas de mapeamento/controles e copia o instalador para `dist/ControllerBridge-android.apk`.

## Limites atuais

- A captura funciona somente enquanto o app está aberto; não há captura em segundo plano ou com a tela bloqueada.
- Entrada física, eixos do giroscópio e tela sempre ligada ainda precisam ser verificados no celular e controle reais. As verificações locais não substituem esse teste.
- Alguns controles paralelos podem não expor todos os botões/eixos ao Android; essas entradas podem ser remapeadas quando o Android as reconhece.
- O controle físico da luz do DS4 não está disponível nesta versão.
- O modo Wi-Fi ainda está indisponível; o host aceita somente a conexão local por USB/ADB.
