# Controller Bridge — Android

> **100% AI-created project.**

Android client that forwards Bluetooth controllers recognized by the phone to the Windows host. Input capture runs while the app is open, and the app keeps the screen awake on its main screen.

## Install and connect

Two APK variants are provided. **PT-BR** installs as `com.controllerbridge.app`; **EN** installs as `com.controllerbridge.app.en`, so both can be installed together. Each starts and stays in its named language.

1. Pair the controller with Android in the system Bluetooth settings.
2. Install/start the Controller Bridge Windows host and its required virtual controller driver on the PC.
3. In the phone app, choose **USB** or **Wi-Fi**:
   - **USB:** connect the phone to the PC, unlock it, allow USB debugging, then run `adb reverse tcp:47652 tcp:47652` on the PC for that cable connection. Keep `127.0.0.1` as the host address.
   - **Wi-Fi:** start the Windows host with `--wifi`, connect phone and PC to the same LAN, enter the PC's LAN IP and the 32-character pairing code shown by the host. The app pairs each PC once and saves the pairing key encrypted with Android Keystore. Wi-Fi uses authenticated AES-GCM messages on port 47653; USB stays on local port 47652. The host does not add firewall rules automatically.
4. Tap **Connect to PC** and keep the app screen open while playing. Select Xbox 360 or DualShock 4 output. The app starts with Android's standard layout. Tap **Map** to change one button, or invert/remap/calibrate an axis.
5. The two app variants keep their own fixed language.

## DS4 motion and light

- On Android 12 or later, the **DS4 gyro** switch appears when a DS4 controller's own public input-device sensors can be paired unambiguously. It is available only for DualShock 4 output.
- A physical light-color control appears only when Android reports a controllable RGB light on the connected DS4. Color settings are saved separately for each phone controller profile.
- The exact gyro/light behavior still needs testing with a physical DS4 on the target phone. If Android does not expose the RGB light, the app hides the color picker and reports that it is unavailable.

## Local build

With Android SDK, Android Studio JBR Java 21, Gradle 8.2.1 and Android Gradle Plugin 8.2.1 already available locally:

```powershell
.\Build.ps1
```

The script builds both variants offline, runs focused mapping and Wi-Fi security checks, and writes `dist/ControllerBridge-Android-PT-BR-v0.3.0.apk` and `dist/ControllerBridge-Android-EN-v0.3.0.apk`.

## Current limits

- Capture works only while the app is open; background/locked-screen capture is not enabled.
- Physical controller input, motion, color-light availability, screen-awake behavior, and gameplay forwarding still need validation on the target phone and controller.
- Some third-party controllers may not expose every button/axis to Android; those inputs can be remapped when Android reports them.
- Wi-Fi requires the PC host to be started explicitly with `--wifi`, a LAN address, and the pairing code displayed by that host. The app does not create router or firewall rules.

---

# Controller Bridge — Android

> **Projeto criado 100% com IA.**

Cliente Android que encaminha ao host Windows os controles Bluetooth reconhecidos pelo celular. A captura funciona enquanto o app está aberto, e o aplicativo mantém a tela ligada na tela principal.

## Instalar e conectar

Há duas versões do APK. **PT-BR** instala como `com.controllerbridge.app`; **EN** instala como `com.controllerbridge.app.en`, então as duas podem ficar instaladas juntas. Cada versão abre e permanece no idioma indicado no nome.

1. Pareie o controle com o Android nas configurações Bluetooth do sistema.
2. Instale/inicie no PC Windows o host Controller Bridge e o driver de controle virtual exigido por ele.
3. No app do celular, escolha **USB** ou **Wi-Fi**:
   - **USB:** conecte o celular ao PC, desbloqueie-o e autorize a depuração USB. A cada conexão do cabo, execute `adb reverse tcp:47652 tcp:47652` no PC. Mantenha o endereço do host como `127.0.0.1`.
   - **Wi-Fi:** inicie o host Windows com `--wifi`, conecte celular e PC à mesma rede local e informe o IP local do PC e o código de pareamento de 32 caracteres mostrado pelo host. O pareamento é salvo por PC com a chave cifrada pelo Android Keystore. Wi-Fi usa mensagens autenticadas AES-GCM na porta 47653; USB continua na porta local 47652. O host não configura regras de firewall automaticamente.
4. Toque em **Conectar ao PC** e deixe a tela do app aberta durante o jogo. Escolha a saída Xbox 360 ou DualShock 4. O app começa com o layout padrão do Android. Toque em **Mapear** para mudar um botão, ou inverta/remapeie/calibre um eixo.
5. Cada variante mantém seu idioma fixo.

## Movimento e luz do DS4

- No Android 12 ou superior, a opção **Giroscópio do DS4** aparece quando os sensores públicos do próprio controle DS4 podem ser associados sem ambiguidade. Ela só fica disponível para a saída DualShock 4.
- O seletor de cor física aparece somente quando Android informa uma luz RGB controlável no DS4 conectado. A cor fica salva separadamente em cada perfil de controle do celular.
- O funcionamento real do giroscópio e da luz ainda precisa ser verificado com um DS4 físico no celular de destino. Se Android não expuser a luz RGB, o app esconde o seletor e informa que o recurso não está disponível.

## Build local

Com Android SDK, Java 21 do Android Studio JBR, Gradle 8.2.1 e Android Gradle Plugin 8.2.1 já disponíveis localmente:

```powershell
.\Build.ps1
```

O script compila as duas versões offline, executa verificações focadas de mapeamento e segurança Wi-Fi, e gera `dist/ControllerBridge-Android-PT-BR-v0.3.0.apk` e `dist/ControllerBridge-Android-EN-v0.3.0.apk`.

## Limites atuais

- A captura funciona somente enquanto o app está aberto; não há captura em segundo plano ou com a tela bloqueada.
- Entrada física, giroscópio, disponibilidade da luz RGB, tela sempre ligada e encaminhamento para jogos ainda precisam ser verificados no celular e controle reais.
- Alguns controles paralelos podem não expor todos os botões/eixos ao Android; essas entradas podem ser remapeadas quando o Android as reconhece.
- Wi-Fi exige iniciar o host do PC com `--wifi`, usar o IP local e informar o código exibido pelo host. O app não cria regras de roteador ou firewall.




