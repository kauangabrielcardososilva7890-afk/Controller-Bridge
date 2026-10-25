# Controller Bridge — Windows 0.3.3

> **100% AI-created project.** Forward Android-connected Bluetooth game controllers to separate virtual controllers on Windows 11.

## English

### Start in two main steps

1. Download `ControllerBridge-Windows-EN-v0.3.3.zip` (or the PT-BR variant) and extract the **whole ZIP**.
2. Open the single launcher, **ControllerBridge.exe**, and choose Wi-Fi (recommended) or USB cable from its menu. For USB, connect the phone with a data cable, enable USB debugging, and approve this PC on the phone. For Wi-Fi, connect the PC and phone to the same local network.
3. Keep the host window open. It shows `STATUS: ACTIVE`; Wi-Fi also displays the PC IP address, port, and pairing code for the Android app.

The ZIP already includes a private .NET 8.0.32 runtime and the official client library. You do not need to install .NET or copy DLLs. Keep all extracted files and folders together.

If the virtual controller driver is missing, setup asks whether to open the **bundled official ViGEmBus 1.22.0 installer**. It verifies the pinned SHA-256 and vendor signature first; Windows displays UAC and the installer stays visible. You approve the installation and its license in the normal installer. Cancelling stops host startup. No driver is installed silently, and this project's build/test does not run that installer.

Setup locates ADB in Android Studio/SDK, PATH or a saved location, selects an authorized phone and runs `adb -s <phone> reverse tcp:47652 tcp:47652`. An `unauthorized` phone stops setup with instructions to unlock it and approve USB debugging; restart the launcher afterward. If several phones are connected, choose the intended phone. Two gamepads connected to that phone still work as independent controllers.

**Only if ADB is missing:** setup offers to open Google's [official Platform Tools page](https://developer.android.com/tools/releases/platform-tools). Accept Google's terms, download/extract it, then select or paste the path to `adb.exe`. Android SDK files are not redistributed or downloaded silently. You normally do this once.

### Optional Wi-Fi

Open **ControllerBridge.exe** and choose Wi-Fi, connect PC and phone to the same LAN, then choose Wi-Fi in Android and enter the displayed PC IPv4, port **47653** and pairing code. You can also find IPv4 with `ipconfig`. If Windows Firewall asks, allow Controller Bridge **only on your private network**. No firewall rule is created automatically.

Pairing uses mutual authentication and authenticated encryption. The host remembers its key in `%LOCALAPPDATA%\ControllerBridge\pairing.key`. To reset all pairings, stop the host and delete that key file, then restart Wi-Fi and pair again. Advanced options: `--wifi`, `--wifi-port N`, `--wifi-bind IP`, `--pairing-file path`. USB uses loopback port47652 and stays available with Wi-Fi enabled.

### Controls and language

- Each phone-connected controller has a separate profile and Xbox 360 or DS4 output; native PC controllers remain native. Xbox outputs share Windows' four XInput positions.
- Button remapping, axis calibration, DS4 gyro and exposed RGB lights are managed in Android. Sensor/light support depends on what Android exposes for that controller.
- PT-BR and EN packages have their own initial language. `--lang pt` or `--lang en` overrides it. `--version` prints the version; `--help` lists options.
- `--diagnostic` receives input without creating virtual outputs. `--prepared` skips package preparation for advanced/manual setups.
- Profiles are saved under `%LOCALAPPDATA%\ControllerBridge\profiles`.

### Included dependencies and source builds

The official client is MIT-licensed, ViGEmBus is BSD-3-Clause, and .NET includes its MIT license and third-party notices. Required notices and upstream attribution are in **ThirdParty/**. The signed driver installer is unmodified; the upstream ViGEm project is archived. The client package and driver installer are pinned by hash in `Bootstrap/Dependencies.json` and checked while packaging; driver signature is checked again before opening it.

`Build.ps1` builds a developer version using the installed .NET 8 SDK. `Package-Release.ps1` publishes self-contained Windows x64 PT-BR/EN packages, verifies language/version and tests, bundles the verified dependencies, then writes ZIPs and `SHA256SUMS.txt` under `artifacts/`. It downloads from official sources when its ignored build cache is missing. It never installs the driver or publishes a release.

---

## Português

> **Projeto criado 100% com IA.** Encaminha controles Bluetooth conectados ao Android para controles virtuais separados no Windows 11.

### Iniciar em duas etapas principais

1. Baixe `ControllerBridge-Windows-PT-BR-v0.3.3.zip` (ou a variante EN) e extraia o **ZIP inteiro**.
2. Abra o único iniciador, **ControllerBridge.exe**, e escolha Wi-Fi (recomendado) ou cabo USB no menu. Para USB, conecte o celular com um cabo de dados, ative a depuração USB e autorize este PC no celular. Para Wi-Fi, conecte PC e celular à mesma rede local.
3. Mantenha a janela do host aberta. Ela mostra `STATUS: ATIVO`; no Wi-Fi, também exibe o IP do PC, a porta e o código para informar no app Android.

O ZIP já contém um runtime privado do .NET 8.0.32 e a biblioteca cliente oficial. Você não precisa instalar .NET nem copiar DLLs. Mantenha todos os arquivos e pastas extraídos juntos.

Se faltar o driver de controle virtual, a preparação pergunta se você deseja abrir o **instalador oficial ViGEmBus 1.22.0 incluído**. Antes, ela verifica o SHA-256 fixado e a assinatura do fornecedor; o Windows mostra o UAC e o instalador fica visível. Você autoriza a instalação e a licença no instalador normal. Cancelar interrompe a abertura do host. Nenhum driver é instalado silenciosamente, e o processo de compilação/teste deste projeto não executa esse instalador.

A preparação localiza ADB no Android Studio/SDK, no PATH ou no local salvo, seleciona um celular autorizado e executa `adb -s <celular> reverse tcp:47652 tcp:47652`. Um celular `unauthorized` interrompe a preparação e mostra como desbloquear e autorizar a depuração USB; depois, inicie novamente. Se houver vários celulares, escolha o desejado. Dois controles conectados ao mesmo celular continuam independentes.

**Somente se faltar ADB:** a preparação oferece abrir a [página oficial do Platform Tools da Google](https://developer.android.com/tools/releases/platform-tools). Aceite os termos da Google, baixe/extraia e selecione ou cole o caminho de `adb.exe`. Os arquivos do Android SDK não são redistribuídos nem baixados silenciosamente. Normalmente isso só precisa ser feito uma vez.

### Wi-Fi opcional

Abra **ControllerBridge.exe** e escolha Wi-Fi, conecte PC e celular à mesma rede e escolha Wi-Fi no Android. Informe o IPv4 do PC exibido, a porta **47653** e o código de pareamento. Também é possível localizar o IPv4 com `ipconfig`. Se o Firewall do Windows perguntar, permita o Controller Bridge **somente na rede privada**. Nenhuma regra de firewall é criada automaticamente.

O pareamento usa autenticação mútua e criptografia autenticada. O host guarda a chave em `%LOCALAPPDATA%\ControllerBridge\pairing.key`. Para refazer todos os pareamentos, feche o host, apague esse arquivo, reinicie Wi-Fi e pareie novamente. Opções avançadas: `--wifi`, `--wifi-port N`, `--wifi-bind IP`, `--pairing-file caminho`. USB usa a porta local47652 e continua disponível com Wi-Fi ativado.

### Controles e idioma

- Cada controle conectado ao celular tem perfil separado e saída Xbox 360 ou DS4; controles nativos do PC continuam nativos. Saídas Xbox compartilham as quatro posições XInput do Windows.
- Remapeamento, calibração de eixos, giroscópio DS4 e luz RGB exposta são gerenciados no Android. Sensores/luzes dependem do que o Android disponibiliza para aquele controle.
- Os pacotes PT-BR e EN têm seu próprio idioma inicial. `--lang pt` ou `--lang en` altera essa escolha. `--version` mostra a versão; `--help` lista opções.
- `--diagnostic` recebe comandos sem criar controles virtuais. `--prepared` pula a preparação do pacote para configurações avançadas/manuais.
- Os perfis ficam em `%LOCALAPPDATA%\ControllerBridge\profiles`.

### Dependências incluídas e compilação

O cliente oficial tem licença MIT, ViGEmBus tem licença BSD-3-Clause e o .NET inclui sua licença MIT e avisos de terceiros. Os avisos e créditos exigidos estão em **ThirdParty/**. O instalador assinado do driver está sem modificações; o projeto original ViGEm está arquivado. O pacote cliente e o instalador são fixados por hash em `Bootstrap/Dependencies.json` e conferidos no empacotamento; a assinatura do driver é conferida novamente antes de abri-lo.

`Build.ps1` compila uma versão de desenvolvimento usando o SDK .NET 8 instalado. `Package-Release.ps1` publica pacotes Windows x64 autocontidos PT-BR/EN, verifica versão/idioma e testes, inclui as dependências verificadas e gera ZIPs com `SHA256SUMS.txt` em `artifacts/`. Ele baixa de fontes oficiais quando o cache de build ignorado não existe. Nunca instala o driver nem publica uma release.
