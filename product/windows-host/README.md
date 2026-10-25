# Controller Bridge — Windows Host

> **100% AI-created project.** Controller Bridge connects Bluetooth game controllers paired with an Android phone to virtual controllers on Windows 11.

## English

### Download and requirements

- Download the Windows ZIP for your preferred language: `ControllerBridge-Windows-PT-BR-v0.3.0.zip` or `ControllerBridge-Windows-EN-v0.3.0.zip`. Download the Android package from the same release. Extract the whole Windows ZIP before running `ControllerBridge.exe`; keep its DLL and runtime configuration files together.
- Windows 11 x64 and the .NET 8 Runtime are required.
- Install the [official ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases). Download the [Nefarius.ViGEm.Client NuGet package](https://www.nuget.org/packages/Nefarius.ViGEm.Client/1.21.256), open the `.nupkg` as a ZIP, and copy `lib/netstandard2.0/Nefarius.ViGEm.Client.dll` beside `ControllerBridge.exe`. The PT-BR package starts in Portuguese and the EN package starts in English; `--lang en` or `--lang pt` overrides that choice. Source builds with `DefaultLanguage=auto` follow Windows language.
- On Android, pair the PS4, Xbox, or other supported Bluetooth gamepad in system Bluetooth settings.

### Connect over USB

1. Enable Developer options and USB debugging on Android, connect the phone by USB, unlock it and approve the computer.
2. In PowerShell, run `adb reverse tcp:47652 tcp:47652`. If `adb` is not on PATH, use the Android SDK `platform-tools\adb.exe` path.
3. Start `ControllerBridge.exe` and keep its window open.
4. Open the Android app, choose the controller and virtual output (Xbox 360 or DualShock 4), then tap **Connect to PC**. Keep the app in the foreground during play.

Use **Diagnostic mode** (`ControllerBridge.exe --diagnostic`) to view incoming input without creating virtual controllers. Profiles and mappings are managed in the Android app and stored on the PC under `%LOCALAPPDATA%\ControllerBridge\profiles`.

### Connect over Wi-Fi

1. Connect the PC and phone to the same local network. Start `ControllerBridge.exe --wifi` (or add `--diagnostic` for a receiver-only test).
2. The Windows window displays the PC's active LAN IPv4 addresses, port `47653`, and a 32-character pairing code. You can also run `ipconfig` in PowerShell and find **IPv4 Address** for the active Wi-Fi/Ethernet adapter. In the Android app choose **Wi-Fi**, enter that PC IP and port `47653`, paste the displayed pairing code, then connect.
3. If Windows Firewall asks, allow Controller Bridge on your **private network only**. If the phone cannot connect, check that the PC/phone share a network and that the private-network firewall permission is enabled. The host does not create firewall rules automatically.

Pairing authenticates both devices and encrypts/authenticates each input and profile message. The code is saved under `%LOCALAPPDATA%\ControllerBridge\pairing.key`; Android remembers it for that host. Keep the code private. To reset pairing, stop the host, delete that key file, restart with `--wifi`, and enter the new code on Android. Existing pairings then stop working. `--pairing-file path` selects another key file, `--wifi-port N` changes the LAN port, and `--wifi-bind IP` restricts the listener to one PC address. Wi-Fi is off by default; the USB/ADB loopback listener continues alongside it when enabled.

### Features and current limits

- Multiple paired controllers with separate profiles and Xbox 360 / DualShock 4 output selection.
- Automatic standard mapping with per-button remapping and axis calibration.
- DS4 gyro pass-through can be enabled or disabled per profile when the controller exposes compatible motion sensors.
- USB via ADB Reverse and opt-in authenticated, encrypted Wi-Fi are supported.
- The Windows host currently runs in a terminal. USB uses loopback only; `--wifi` enables the separate LAN listener. It does not hide or modify controllers plugged directly into Windows. The Android app manages DS4 light color only if Android exposes an RGB light for that controller.
- Xbox virtual outputs share Windows' four XInput slots with native controllers.
- This project does not redistribute the virtual bus driver or its client library; install them separately.
- ViGEmBus is an external dependency whose upstream repository is archived; check its official release page before installing.

### Build from source

Install the .NET 8 SDK and run `product/windows-host/Build.ps1`. The output is `ControllerBridge.exe`.

Run `product/windows-host/Package-Release.ps1` to generate both language ZIPs and `SHA256SUMS.txt` under `product/windows-host/artifacts`. The script builds version 0.3.0, checks each default language and its override, and runs self-tests before packaging. It does not publish anything. `Build.ps1 -DefaultLanguage pt-BR` or `-DefaultLanguage en` builds one specific variant; the default is `auto`. Use `ControllerBridge.exe --version` to inspect the application version.

---

## Português

> **Projeto criado 100% com IA.** O Controller Bridge conecta controles Bluetooth pareados com um celular Android a controles virtuais no Windows 11.

### Download e requisitos

- Baixe o ZIP Windows do idioma desejado: `ControllerBridge-Windows-PT-BR-v0.3.0.zip` ou `ControllerBridge-Windows-EN-v0.3.0.zip`. Baixe o pacote Android da mesma versão. Extraia o ZIP Windows inteiro antes de executar `ControllerBridge.exe`; mantenha as DLLs e os arquivos de configuração do runtime juntos.
- É necessário Windows 11 x64 e o Runtime do .NET 8.
- Instale o [driver oficial ViGEmBus](https://github.com/nefarius/ViGEmBus/releases). Baixe o [pacote NuGet Nefarius.ViGEm.Client](https://www.nuget.org/packages/Nefarius.ViGEm.Client/1.21.256), abra o `.nupkg` como ZIP e copie `lib/netstandard2.0/Nefarius.ViGEm.Client.dll` para junto de `ControllerBridge.exe`. O pacote PT-BR inicia em português e o pacote EN inicia em inglês; use `--lang pt` ou `--lang en` para alterar essa escolha. Builds do código-fonte com `DefaultLanguage=auto` acompanham o idioma do Windows.
- No Android, pareie o controle de PS4, Xbox ou outro controle Bluetooth compatível nas configurações do sistema.

### Conectar por USB

1. Ative as Opções do desenvolvedor e a depuração USB no Android, conecte o celular ao PC, desbloqueie e autorize o computador.
2. No PowerShell, execute `adb reverse tcp:47652 tcp:47652`. Se `adb` não estiver no PATH, use o caminho do `platform-tools\adb.exe` do Android SDK.
3. Inicie `ControllerBridge.exe` e mantenha a janela aberta.
4. Abra o app Android, escolha o controle e a saída virtual (Xbox 360 ou DualShock 4) e toque em **Conectar ao PC**. Mantenha o app em primeiro plano durante o jogo.

Use o **Modo de diagnóstico** (`ControllerBridge.exe --diagnostic`) para visualizar os comandos recebidos sem criar controles virtuais. Os perfis e mapeamentos são gerenciados no app Android e salvos no PC em `%LOCALAPPDATA%\ControllerBridge\profiles`.

### Conectar por Wi-Fi

1. Conecte PC e celular à mesma rede local. Inicie `ControllerBridge.exe --wifi` (adicione `--diagnostic` para testar somente a recepção).
2. A janela do Windows mostra os endereços IPv4 locais ativos do PC, a porta `47653` e um código de pareamento com 32 caracteres. Você também pode executar `ipconfig` no PowerShell e localizar **Endereço IPv4** do adaptador Wi-Fi/Ethernet ativo. No app Android escolha **Wi-Fi**, informe esse IP do PC e a porta `47653`, cole o código exibido e conecte.
3. Se o Firewall do Windows perguntar, permita o Controller Bridge **somente na rede privada**. Se o celular não conectar, confira a rede compartilhada e a permissão do firewall para rede privada. O host não cria regras de firewall automaticamente.

O pareamento autentica os dois dispositivos e criptografa/autentica cada mensagem de comando e perfil. O código fica salvo em `%LOCALAPPDATA%\ControllerBridge\pairing.key`; o Android o memoriza para esse host. Mantenha o código privado. Para refazer o pareamento, feche o host, apague esse arquivo de chave, inicie novamente com `--wifi` e informe o código novo no Android. Os pareamentos anteriores deixam de funcionar. `--pairing-file caminho` escolhe outro arquivo de chave, `--wifi-port N` muda a porta de rede e `--wifi-bind IP` limita a escuta a um endereço do PC. Wi-Fi fica desligado por padrão; a conexão USB/ADB local continua disponível ao mesmo tempo quando ele é ativado.

### Recursos e limites atuais

- Vários controles pareados, com perfis separados e escolha de saída Xbox 360 / DualShock 4.
- Mapeamento padrão automático, remapeamento individual de botões e calibração de eixos.
- O giroscópio do DS4 pode ser ativado ou desativado por perfil quando o controle expõe sensores de movimento compatíveis. A cor da luz física do controle não está disponível nesta versão.
- Conexão USB por ADB Reverse e Wi-Fi opcional autenticado e criptografado disponíveis.
- O host Windows funciona pelo terminal. USB usa somente a conexão local; `--wifi` ativa a escuta separada na rede. Ele não oculta nem altera controles conectados diretamente ao Windows. O app Android controla a cor da luz do DS4 somente quando Android expõe uma luz RGB para esse controle.
- Saídas virtuais Xbox compartilham as quatro posições XInput do Windows com controles nativos.
- Este projeto não redistribui o driver de barramento virtual nem sua biblioteca cliente; instale-os separadamente.
- ViGEmBus é uma dependência externa e o repositório original está arquivado; confira a página oficial de versões antes de instalar.

### Compilar do código-fonte

Instale o .NET 8 SDK e execute `product/windows-host/Build.ps1`. O resultado é `ControllerBridge.exe`.

Execute `product/windows-host/Package-Release.ps1` para gerar os dois ZIPs de idioma e `SHA256SUMS.txt` em `product/windows-host/artifacts`. O script compila a versão 0.3.0, confere o idioma inicial e a troca manual de cada variante e executa os testes antes de empacotar. Ele não publica nada. `Build.ps1 -DefaultLanguage pt-BR` ou `-DefaultLanguage en` compila uma variante específica; o padrão é `auto`. Use `ControllerBridge.exe --version` para conferir a versão do aplicativo.

