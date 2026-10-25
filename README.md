# Controller Bridge

> **100% AI-created project.**

Connect Bluetooth game controllers paired with an Android phone to virtual Xbox 360 or DualShock 4 controllers on Windows 11. The Android app edits profiles and can forward several controllers at once.

## English

### Downloads

- **Android app (APK):** [Download Controller Bridge for Android](https://github.com/kauangabrielcardososilva7890-afk/Controller-Bridge/releases/latest/download/ControllerBridge-Android.apk)
- **Windows host (ZIP with EXE):** [Download Controller Bridge for Windows 11 x64](https://github.com/kauangabrielcardososilva7890-afk/Controller-Bridge/releases/latest/download/ControllerBridge-Windows-x64.zip)

The Android app includes English and Portuguese; choose **EN** or **PT** in the app. The Windows host follows the system language or accepts `--lang en` / `--lang pt`.

### Requirements

- Android 8.0 or later with Bluetooth gamepad support.
- Windows 11 x64 and .NET 8 Runtime.
- ViGEmBus virtual gamepad driver and `Nefarius.ViGEm.Client.dll`, installed separately.
- For USB: a USB cable, Android USB debugging and Android SDK Platform Tools (`adb`). Wi-Fi uses the local network instead.

### Install

1. Download and install the Android APK.
2. Pair the PS4, Xbox or another compatible Bluetooth controller with the phone in Android Bluetooth settings.
3. Download and extract the Windows ZIP. Install the [official ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases). Download the [Nefarius.ViGEm.Client NuGet package](https://www.nuget.org/packages/Nefarius.ViGEm.Client/1.21.256), open the `.nupkg` as a ZIP archive, and copy `lib/netstandard2.0/Nefarius.ViGEm.Client.dll` beside `ControllerBridge.exe`.
4. Choose one of the connection methods below, then open the Android app, select a controller and its **Xbox 360** or **DualShock 4** PC output, and tap **Connect to PC**.

### Connect by USB

1. Enable Android **Developer options** and **USB debugging**. Connect the unlocked phone to the PC and approve the debugging prompt.
2. In PowerShell, run `adb reverse tcp:47652 tcp:47652`. If `adb` is not recognized, run `platform-tools\adb.exe` from the Android SDK by its full path.
3. Start `ControllerBridge.exe` and keep its window open. In the app, choose **USB · ADB Reverse** and connect. ADB Reverse must be set up again when the cable connection changes.

### Connect by Wi-Fi

1. Connect the phone and PC to the same local network. Start the Windows host with `ControllerBridge.exe --wifi`.
2. The host window shows the PC's active IPv4 addresses and a 32-character pairing code. In the Android app, choose **Wi-Fi**, enter the PC's local IP and the code, then connect. The host listens on port `47653`; USB remains available on `47652`.
3. If Windows Firewall asks, allow the app on **private networks only**. The host does not add firewall rules automatically. Keep the pairing code private; Android saves it encrypted in Android Keystore for that PC.

Wi-Fi is convenient but can have more variable latency than USB, depending on the network. The Wi-Fi messages are mutually authenticated and encrypted. The host only opens its Wi-Fi listener when `--wifi` is supplied. To reset pairing, close the host and delete `%LOCALAPPDATA%\ControllerBridge\pairing.key`, then start it again with `--wifi` and enter the new code in Android.

### Features

- Multiple controllers connected to the phone at the same time, each with an independent profile and output selection.
- Automatic standard mapping, plus individual button remapping, axis reassignment, inversion and calibration.
- Xbox 360 or DualShock 4 virtual output selected per controller.
- Per-profile DS4 motion forwarding toggle when Android exposes compatible controller sensors.
- USB/ADB and opt-in authenticated Wi-Fi transports.
- Conditional DS4 RGB light control: the color picker appears only if Android exposes a controllable RGB light on that DS4. Android 12 or later is required for this public lights API.
- Bilingual Android app and Windows host. This README has a complete English section followed by Portuguese.

### Current limits

- Keep the Android app open in the foreground while playing; it keeps the screen awake while visible. Background or locked-screen capture is not enabled.
- The Android/DS4 light capability and complete gameplay path still need confirmation on the target phone and controller; unsupported lights are reported and the picker stays hidden.
- The Windows host runs in a terminal window. It does not hide or modify physical controllers connected directly to Windows.
- Xbox virtual devices use Windows XInput slots, shared with physical Xbox-compatible devices.
- Battery reporting, rumble, PS5 output and OTG are not supported.
- ViGEmBus is an external dependency whose upstream repository is archived; review its official releases before installing.

### Build from source

- Android: install Android Studio with the Android SDK and Java 21, then run `product/android-app/Build.ps1`.
- Windows host: install the .NET 8 SDK and run `product/windows-host/Build.ps1`.
- The Windows virtual gamepad driver and client library are separate system dependencies and are not bundled.

---

## Português

> **Projeto criado 100% com IA.**

Conecte controles Bluetooth pareados com um celular Android a controles virtuais Xbox 360 ou DualShock 4 no Windows 11. O app Android edita perfis e pode encaminhar vários controles ao mesmo tempo.

### Downloads

- **App Android (APK):** [Baixar Controller Bridge para Android](https://github.com/kauangabrielcardososilva7890-afk/Controller-Bridge/releases/latest/download/ControllerBridge-Android.apk)
- **Host Windows (ZIP com EXE):** [Baixar Controller Bridge para Windows 11 x64](https://github.com/kauangabrielcardososilva7890-afk/Controller-Bridge/releases/latest/download/ControllerBridge-Windows-x64.zip)

O app Android inclui português e inglês; escolha **PT** ou **EN** no app. O host Windows acompanha o idioma do sistema ou aceita `--lang pt` / `--lang en`.

### Requisitos

- Android 8.0 ou superior com suporte a controles Bluetooth.
- Windows 11 x64 e .NET 8 Runtime.
- Driver de controle virtual ViGEmBus e `Nefarius.ViGEm.Client.dll`, instalados separadamente.
- Para USB: cabo, depuração USB do Android e Android SDK Platform Tools (`adb`). No Wi-Fi, usa-se a rede local.

### Instalar

1. Baixe e instale o APK Android.
2. Pareie o controle de PS4, Xbox ou outro controle Bluetooth compatível com o celular pelas configurações Bluetooth do Android.
3. Baixe e extraia o ZIP do Windows. Instale o [driver oficial ViGEmBus](https://github.com/nefarius/ViGEmBus/releases). Baixe o [pacote NuGet Nefarius.ViGEm.Client](https://www.nuget.org/packages/Nefarius.ViGEm.Client/1.21.256), abra o `.nupkg` como ZIP e copie `lib/netstandard2.0/Nefarius.ViGEm.Client.dll` para junto de `ControllerBridge.exe`.
4. Escolha um dos métodos de conexão abaixo. Depois abra o app Android, selecione um controle, escolha a saída **Xbox 360** ou **DualShock 4** para o PC e toque em **Conectar ao PC**.

### Conectar por USB

1. Ative **Opções do desenvolvedor** e **Depuração USB** no Android. Conecte o celular desbloqueado ao PC e autorize a solicitação de depuração.
2. No PowerShell, execute `adb reverse tcp:47652 tcp:47652`. Se `adb` não for reconhecido, execute `platform-tools\adb.exe` pelo caminho completo do Android SDK.
3. Inicie `ControllerBridge.exe` e mantenha a janela aberta. No app, escolha **USB · ADB Reverse** e conecte. Configure o ADB Reverse novamente quando a conexão por cabo mudar.

### Conectar por Wi-Fi

1. Conecte o celular e o PC à mesma rede local. Inicie o host Windows com `ControllerBridge.exe --wifi`.
2. A janela do host mostra os endereços IPv4 ativos do PC e um código de pareamento de 32 caracteres. No app Android, escolha **Wi-Fi**, informe o IP local do PC e o código, e conecte. O host usa a porta `47653`; USB continua disponível na porta `47652`.
3. Se o Firewall do Windows perguntar, permita o programa **somente em redes privadas**. O host não cria regras de firewall automaticamente. Mantenha o código privado; o Android o salva cifrado no Android Keystore para esse PC.

O Wi-Fi é prático, mas pode ter latência mais variável que o USB, dependendo da rede. As mensagens Wi-Fi são autenticadas nos dois sentidos e criptografadas. O host só abre a conexão Wi-Fi quando iniciado com `--wifi`. Para refazer o pareamento, feche o host, apague `%LOCALAPPDATA%\ControllerBridge\pairing.key`, inicie-o novamente com `--wifi` e informe o novo código no Android.

### Recursos

- Vários controles conectados ao celular ao mesmo tempo, cada um com perfil e saída independentes.
- Mapeamento padrão automático, remapeamento individual de botões, troca de eixos, inversão e calibração.
- Saída virtual Xbox 360 ou DualShock 4 selecionada por controle.
- Opção por perfil para encaminhar o movimento do DS4 quando Android expõe sensores compatíveis.
- Conexões USB/ADB e Wi-Fi autenticado opcional.
- Controle condicional da luz RGB do DS4: o seletor de cor aparece somente se Android expuser uma luz RGB controlável nesse DS4. Essa API pública de luzes exige Android 12 ou superior.
- App Android e host Windows em português e inglês. Este README tem uma seção completa em inglês e outra em português.

### Limites atuais

- Mantenha o app Android aberto em primeiro plano durante o jogo; ele mantém a tela ligada enquanto está visível. Captura em segundo plano ou com a tela bloqueada não está ativada.
- A disponibilidade da luz no Android/DS4 e o fluxo completo de jogo ainda precisam ser confirmados no celular e controle de destino; quando a luz não é compatível, o app informa e esconde o seletor.
- O host Windows funciona em uma janela de terminal. Ele não oculta nem altera controles físicos conectados diretamente ao Windows.
- Controles virtuais Xbox usam posições XInput do Windows, compartilhadas com controles Xbox físicos compatíveis.
- Bateria, vibração, saída PS5 e OTG não são suportados.
- ViGEmBus é uma dependência externa e o repositório original está arquivado; confira as versões oficiais antes de instalar.

### Compilar do código-fonte

- Android: instale Android Studio com Android SDK e Java 21 e execute `product/android-app/Build.ps1`.
- Host Windows: instale o .NET 8 SDK e execute `product/windows-host/Build.ps1`.
- O driver virtual do Windows e sua biblioteca cliente são dependências separadas e não vêm incluídos.

