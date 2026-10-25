# Controller Bridge

> **100% AI-created project.** / **Projeto criado 100% com IA.**

Connect Bluetooth game controllers paired with an Android phone to virtual Xbox 360 or DualShock 4 controllers on Windows 11. The Android app is the profile editor and can forward more than one controller at a time.

Conecte controles Bluetooth pareados com um celular Android a controles virtuais Xbox 360 ou DualShock 4 no Windows 11. O app Android edita os perfis e pode encaminhar mais de um controle ao mesmo tempo.

---

## English

### Download

- **Android app (APK):** [Download Controller Bridge for Android](https://github.com/kauangabrielcardososilva7890-afk/Controller-Bridge/releases/latest/download/ControllerBridge-Android.apk)
- **Windows host (ZIP with EXE):** [Download Controller Bridge for Windows 11 x64](https://github.com/kauangabrielcardososilva7890-afk/Controller-Bridge/releases/latest/download/ControllerBridge-Windows-x64.zip)

The app includes English and Portuguese. Choose **EN** or **PT** at the top of the app. The Windows host follows Windows language by default; pass `--lang en` or `--lang pt` to choose explicitly.

### Requirements

- Android 8.0 or later with Bluetooth gamepad support.
- Windows 11 x64 and the .NET 8 runtime.
- The ViGEmBus virtual gamepad driver and its client library `Nefarius.ViGEm.Client.dll`, installed separately.
- A USB cable and Android USB debugging for the supported phone-to-PC connection.

### Install and connect

1. Download the APK above, open it on Android and allow installation when asked.
2. Pair the PS4, Xbox, or other compatible controller with the phone using Android Bluetooth settings.
3. Download and extract the Windows ZIP. Install the ViGEmBus driver, then place `Nefarius.ViGEm.Client.dll` beside `ControllerBridge.exe`.
4. On Android, enable **Developer options** and **USB debugging**. Connect the unlocked phone to the PC and approve the computer's debugging prompt.
5. Open PowerShell on Windows and run `adb reverse tcp:47652 tcp:47652`. If `adb` is not recognized, run it using its full path inside Android SDK `platform-tools`.
6. Start `ControllerBridge.exe` and keep its window open.
7. Open the Android app, choose a controller from the list, select **Xbox 360** or **DualShock 4** output, and tap **Connect to PC**. Keep the app in the foreground while playing.

### Features

- Multiple phone-connected controllers, each with its own profile and output selection.
- Automatic standard mapping plus individual button remapping, axis reassignment, inversion and calibration.
- Xbox 360 or DualShock 4 virtual output, selected separately for each controller.
- DS4 motion sensor forwarding can be switched on or off per profile when Android exposes the controller sensors.
- USB connection through ADB Reverse; the host only listens on the local PC.
- English/Portuguese app and host text. The README presents English first and Portuguese below.

### Current limits

- Wi-Fi connection and physical controller lightbar color control are not included in this release.
- The phone app must remain open in the foreground; it keeps the screen awake while visible.
- The Windows host runs in a terminal window.
- Xbox virtual devices use Windows XInput slots, shared with physical Xbox-compatible devices.
- Battery, rumble, PS5 output, OTG and hiding physical PC controllers are not supported.

### Build from source

- Android: install Android Studio with the Android SDK and Java 21, then run `product/android-app/Build.ps1`.
- Windows host: install the .NET 8 SDK and run `product/windows-host/Build.ps1`.
- The Windows virtual gamepad driver/client library is a separate system dependency and is not bundled.

---

## Português

### Download

- **App Android (APK):** [Baixar Controller Bridge para Android](https://github.com/kauangabrielcardososilva7890-afk/Controller-Bridge/releases/latest/download/ControllerBridge-Android.apk)
- **Host Windows (ZIP com EXE):** [Baixar Controller Bridge para Windows 11 x64](https://github.com/kauangabrielcardososilva7890-afk/Controller-Bridge/releases/latest/download/ControllerBridge-Windows-x64.zip)

O app inclui português e inglês. Escolha **PT** ou **EN** no topo da tela. O host do Windows acompanha o idioma do sistema por padrão; use `--lang pt` ou `--lang en` para escolher manualmente.

### Requisitos

- Android 8.0 ou superior com suporte a controles Bluetooth.
- Windows 11 x64 e runtime .NET 8.
- Driver de controle virtual ViGEmBus e a biblioteca cliente `Nefarius.ViGEm.Client.dll`, instalados separadamente.
- Cabo USB e depuração USB do Android para a conexão celular-PC disponível nesta versão.

### Instalar e conectar

1. Baixe o APK acima, abra-o no Android e permita a instalação quando solicitado.
2. Pareie o controle de PS4, Xbox ou outro controle compatível com o celular pelas configurações Bluetooth do Android.
3. Baixe e extraia o ZIP do Windows. Instale o driver ViGEmBus e coloque `Nefarius.ViGEm.Client.dll` ao lado de `ControllerBridge.exe`.
4. No Android, ative **Opções do desenvolvedor** e **Depuração USB**. Conecte o celular desbloqueado ao PC e autorize a solicitação de depuração.
5. Abra o PowerShell no Windows e execute `adb reverse tcp:47652 tcp:47652`. Se o comando `adb` não for reconhecido, execute-o pelo caminho completo dentro de `platform-tools` do Android SDK.
6. Inicie `ControllerBridge.exe` e mantenha a janela aberta.
7. Abra o app Android, escolha um controle na lista, selecione a saída **Xbox 360** ou **DualShock 4** e toque em **Conectar ao PC**. Mantenha o app em primeiro plano durante o jogo.

### Recursos

- Vários controles conectados ao celular, cada um com perfil e saída independentes.
- Mapeamento padrão automático, remapeamento individual de botões, troca de eixos, inversão e calibração.
- Saída virtual Xbox 360 ou DualShock 4, escolhida separadamente para cada controle.
- O encaminhamento dos sensores de movimento do DS4 pode ser ligado ou desligado por perfil quando o Android expõe esses sensores.
- Conexão USB por ADB Reverse; o host aceita conexões somente do próprio PC.
- Textos do app e do host em português/inglês. Este README apresenta inglês primeiro e português depois.

### Limites atuais

- A conexão Wi-Fi e o controle da cor da luz física do controle não estão incluídos nesta versão.
- O app do celular precisa permanecer aberto em primeiro plano; ele mantém a tela ligada enquanto está visível.
- O host do Windows funciona em uma janela de terminal.
- Controles virtuais Xbox usam as posições XInput do Windows, compartilhadas com controles físicos compatíveis.
- Bateria, vibração, saída PS5, OTG e ocultação de controles físicos do PC não são suportados.

### Compilar do código-fonte

- Android: instale o Android Studio com Android SDK e Java 21 e execute `product/android-app/Build.ps1`.
- Host Windows: instale o .NET 8 SDK e execute `product/windows-host/Build.ps1`.
- O driver virtual do Windows e sua biblioteca cliente são dependências separadas e não vêm incluídos.
