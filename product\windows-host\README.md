# Controller Bridge — Windows Host

> **100% AI-created project.** Controller Bridge connects Bluetooth game controllers paired with an Android phone to virtual controllers on Windows 11.

## English

### Download and requirements

- Download the latest `ControllerBridge-android.apk` for Android and `ControllerBridge.exe` for Windows from the GitHub Releases page.
- Windows 11 x64 and the .NET 8 Desktop Runtime are required.
- Install the ViGEmBus virtual gamepad driver and place its client library `Nefarius.ViGEm.Client.dll` beside `ControllerBridge.exe`. Use `--lang en` or `--lang pt` to choose the terminal language; otherwise it follows Windows language.
- On Android, pair the PS4, Xbox, or other supported Bluetooth gamepad in system Bluetooth settings.

### Connect over USB

1. Enable Developer options and USB debugging on Android, connect the phone by USB, unlock it and approve the computer.
2. In PowerShell, run `adb reverse tcp:47652 tcp:47652`. If `adb` is not on PATH, use the Android SDK `platform-tools\adb.exe` path.
3. Start `ControllerBridge.exe` and keep its window open.
4. Open the Android app, choose the controller and virtual output (Xbox 360 or DualShock 4), then tap **Connect to PC**. Keep the app in the foreground during play.

Use **Diagnostic mode** (`ControllerBridge.exe --diagnostic`) to view incoming input without creating virtual controllers. Profiles and mappings are managed in the Android app and stored on the PC under `%LOCALAPPDATA%\ControllerBridge\profiles`.

### Features and current limits

- Multiple paired controllers with separate profiles and Xbox 360 / DualShock 4 output selection.
- Automatic standard mapping with per-button remapping and axis calibration.
- DS4 gyro pass-through can be enabled or disabled per profile when the controller exposes compatible motion sensors.
- USB via ADB Reverse is supported. Wi-Fi mode is not available in this release.
- The Windows host currently runs in a terminal. It listens only on the local PC connection and does not hide or modify controllers plugged directly into Windows. Physical controller lightbar color is not available in this release.
- Xbox virtual outputs share Windows' four XInput slots with native controllers.
- This project does not redistribute the virtual bus driver or its client library; install them separately.

### Build from source

Install the .NET 8 SDK and run `product/windows-host/Build.ps1`. The output is `ControllerBridge.exe`.

---

## Português

> **Projeto criado 100% com IA.** O Controller Bridge conecta controles Bluetooth pareados com um celular Android a controles virtuais no Windows 11.

### Download e requisitos

- Baixe o `ControllerBridge-android.apk` para Android e o `ControllerBridge.exe` para Windows mais recentes na página Releases do GitHub.
- É necessário Windows 11 x64 e .NET 8 Desktop Runtime.
- Instale o driver de controle virtual ViGEmBus e coloque a biblioteca cliente `Nefarius.ViGEm.Client.dll` ao lado de `ControllerBridge.exe`. Use `--lang pt` ou `--lang en` para escolher o idioma do terminal; sem essa opção, ele acompanha o idioma do Windows.
- No Android, pareie o controle de PS4, Xbox ou outro controle Bluetooth compatível nas configurações do sistema.

### Conectar por USB

1. Ative as Opções do desenvolvedor e a depuração USB no Android, conecte o celular ao PC, desbloqueie e autorize o computador.
2. No PowerShell, execute `adb reverse tcp:47652 tcp:47652`. Se `adb` não estiver no PATH, use o caminho do `platform-tools\adb.exe` do Android SDK.
3. Inicie `ControllerBridge.exe` e mantenha a janela aberta.
4. Abra o app Android, escolha o controle e a saída virtual (Xbox 360 ou DualShock 4) e toque em **Conectar ao PC**. Mantenha o app em primeiro plano durante o jogo.

Use o **Modo de diagnóstico** (`ControllerBridge.exe --diagnostic`) para visualizar os comandos recebidos sem criar controles virtuais. Os perfis e mapeamentos são gerenciados no app Android e salvos no PC em `%LOCALAPPDATA%\ControllerBridge\profiles`.

### Recursos e limites atuais

- Vários controles pareados, com perfis separados e escolha de saída Xbox 360 / DualShock 4.
- Mapeamento padrão automático, remapeamento individual de botões e calibração de eixos.
- O giroscópio do DS4 pode ser ativado ou desativado por perfil quando o controle expõe sensores de movimento compatíveis. A cor da luz física do controle não está disponível nesta versão.
- Conexão USB por ADB Reverse disponível. O modo Wi-Fi ainda não está disponível.
- O host Windows funciona pelo terminal. Ele escuta apenas a conexão local do PC e não oculta nem altera controles conectados diretamente ao Windows.
- Saídas virtuais Xbox compartilham as quatro posições XInput do Windows com controles nativos.
- Este projeto não redistribui o driver de barramento virtual nem sua biblioteca cliente; instale-os separadamente.

### Compilar do código-fonte

Instale o .NET 8 SDK e execute `product/windows-host/Build.ps1`. O resultado é `ControllerBridge.exe`.
