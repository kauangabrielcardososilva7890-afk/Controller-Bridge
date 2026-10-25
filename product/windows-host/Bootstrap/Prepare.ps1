param([ValidateSet('USB','WiFi')][string]$Mode='USB', [ValidateSet('pt','en')][string]$Language='pt', [switch]$Diagnostic, [switch]$LibraryOnly)
$ErrorActionPreference='Stop'
function Text([string]$pt,[string]$en) { if ($Language -eq 'en') { return $en }; return $pt }
function Stop-Setup([string]$pt,[string]$en) { Write-Host (Text $pt $en) -ForegroundColor Yellow; Read-Host (Text 'Pressione Enter para fechar' 'Press Enter to close') | Out-Null; exit 1 }
function Parse-AdbDevices([string[]]$Lines) {
    foreach ($line in $Lines) { if ($line -match '^(\S+)\s+(device|unauthorized|offline|recovery|sideload)\b(.*)$') { [pscustomobject]@{ Serial=$matches[1]; State=$matches[2]; Details=$matches[3].Trim() } } }
}
function Invoke-Adb([string]$AdbPath,[string[]]$AdbArguments) {
    $previous=$ErrorActionPreference; $ErrorActionPreference='Continue'
    try { $lines=@(& $AdbPath @AdbArguments 2>&1 | ForEach-Object { $_.ToString() }); $code=$LASTEXITCODE; return [pscustomobject]@{ Lines=$lines; Code=$code } } finally { $ErrorActionPreference=$previous }
}
function Find-Adb {
    $candidates=@()
    $command=Get-Command adb.exe -ErrorAction SilentlyContinue; if ($command) { $candidates += $command.Source }
    foreach ($sdk in @($env:ANDROID_HOME,$env:ANDROID_SDK_ROOT,(Join-Path $env:LOCALAPPDATA 'Android\Sdk'))) { if ($sdk) { $candidates += Join-Path $sdk 'platform-tools\adb.exe' } }
    $candidates += Join-Path $script:AppRoot 'platform-tools\adb.exe'
    $settings=Join-Path $script:DataRoot 'setup.json'
    if (Test-Path -LiteralPath $settings) { try { $saved=Get-Content -Raw -LiteralPath $settings | ConvertFrom-Json; if ($saved.adbPath) { $candidates += $saved.adbPath } } catch { } }
    foreach ($candidate in $candidates) { if (Test-Path -LiteralPath $candidate -PathType Leaf) { return [IO.Path]::GetFullPath($candidate) } }
    Write-Host (Text 'ADB nao encontrado. Ele faz parte do Android SDK Platform Tools da Google.' 'ADB was not found. It is part of Google Android SDK Platform Tools.')
    Write-Host 'https://developer.android.com/tools/releases/platform-tools'
    $answer=Read-Host (Text 'Abrir a pagina oficial para voce aceitar os termos, baixar e extrair? [S/N]' 'Open the official page so you can accept its terms, download and extract? [Y/N]')
    if ($answer -match '^(s|y)$') { Start-Process 'https://developer.android.com/tools/releases/platform-tools' }
    $chosen=(Read-Host (Text 'Cole o caminho completo do adb.exe extraido, ou Enter para cancelar' 'Paste the full path to the extracted adb.exe, or Enter to cancel')).Trim().Trim('"')
    if (!$chosen -or !(Test-Path -LiteralPath $chosen -PathType Leaf) -or [IO.Path]::GetFileName($chosen) -ine 'adb.exe') { Stop-Setup 'Instale/extraia o Platform Tools e inicie novamente. Nada foi instalado automaticamente.' 'Download/extract Platform Tools and start again. Nothing was installed automatically.' }
    New-Item -ItemType Directory -Force -Path $script:DataRoot | Out-Null
    @{adbPath=[IO.Path]::GetFullPath($chosen)} | ConvertTo-Json | Set-Content -Encoding utf8 -LiteralPath $settings
    return [IO.Path]::GetFullPath($chosen)
}
function Prepare-Usb {
    $adb=Find-Adb
    $result=Invoke-Adb $adb @('devices','-l')
    if ($result.Code -ne 0) { Write-Host ($result.Lines -join "`n"); Stop-Setup 'Nao foi possivel iniciar o ADB. Confira a instalacao do Android SDK.' 'ADB could not start. Check the Android SDK installation.' }
    $devices=@(Parse-AdbDevices $result.Lines); $ready=@($devices | Where-Object State -eq 'device')
    if (@($devices | Where-Object State -eq 'unauthorized').Count) { Stop-Setup 'Existe um celular nao autorizado. Desbloqueie e permita a depuracao USB, ou desconecte o aparelho que nao sera usado. Depois inicie novamente.' 'A phone is unauthorized. Unlock it and allow USB debugging, or disconnect the unused phone. Then start again.' }
    if (!$ready.Count) {
        Stop-Setup 'Nenhum celular autorizado. Conecte por cabo de dados, ative a depuracao USB e desbloqueie o celular. Se estiver offline, reconecte o cabo.' 'No authorized phone. Connect a data cable, enable USB debugging and unlock the phone. If offline, reconnect the cable.'
    }
    $selected=$ready[0]
    if ($ready.Count -gt 1) {
        Write-Host (Text 'Escolha o celular para este host:' 'Choose the phone for this host:')
        for ($i=0; $i -lt $ready.Count; $i++) { Write-Host ("{0}: {1} {2}" -f ($i+1),$ready[$i].Serial,$ready[$i].Details) }
        $number=0; $choice=Read-Host (Text 'Numero' 'Number')
        if (![int]::TryParse($choice,[ref]$number) -or $number -lt 1 -or $number -gt $ready.Count) { Stop-Setup 'Escolha invalida. Inicie novamente.' 'Invalid choice. Start again.' }
        $selected=$ready[$number-1]
    }
    $reverse=Invoke-Adb $adb @('-s',$selected.Serial,'reverse','tcp:47652','tcp:47652')
    if ($reverse.Code -ne 0) { Write-Host ($reverse.Lines -join "`n"); Stop-Setup 'Nao foi possivel preparar a conexao USB. Reconecte e autorize o celular.' 'Could not prepare the USB connection. Reconnect and authorize the phone.' }
    Write-Host (Text 'USB preparado. Mantenha o app Android aberto.' 'USB is ready. Keep the Android app open.')
}
function Test-DriverReady {
    $previous=$ErrorActionPreference; $ErrorActionPreference='Continue'
    try { $result=@(& "$env:SystemRoot\System32\sc.exe" query ViGEmBus 2>&1); return $LASTEXITCODE -eq 0 -and (($result -join "`n") -match '\b4\s+RUNNING\b') } finally { $ErrorActionPreference=$previous }
}
function Assert-Installer([string]$Path,$Dependency) {
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Dependency.sha256) { throw 'Driver checksum verification failed.' }
    $signature=Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or !$signature.SignerCertificate -or $signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false) -ne $Dependency.publisher) { throw 'Driver publisher signature verification failed.' }
}
function Ensure-Driver($Dependency) {
    if (Test-DriverReady) { return }
    Write-Host (Text 'O driver de controle virtual ViGEmBus esta ausente ou nao esta ativo.' 'The ViGEmBus virtual controller driver is missing or inactive.')
    Write-Host (Text 'O instalador oficial incluido sera verificado e aberto com interface visivel. O Windows pedira sua permissao de administrador (UAC).' 'The bundled official installer will be verified and opened visibly. Windows will ask for administrator permission (UAC).')
    Write-Host $Dependency.url
    $answer=Read-Host (Text 'Abrir o instalador oficial agora? [S/N]' 'Open the official installer now? [Y/N]')
    if ($answer -notmatch '^(s|y)$') { Stop-Setup 'Instalacao cancelada. Nenhuma alteracao foi feita no driver.' 'Installation cancelled. No driver changes were made.' }
    $installer=Join-Path $script:AppRoot 'Installers\ViGEmBus_1.22.0_x64_x86_arm64.exe'
    if (!(Test-Path -LiteralPath $installer)) {
        Write-Host (Text 'O instalador incluido nao foi encontrado. Sera baixada a mesma versao oficial verificada.' 'The bundled installer was not found. The same verified official version will be downloaded.')
        $cache=Join-Path $script:DataRoot 'Downloads'; New-Item -ItemType Directory -Force -Path $cache | Out-Null
        $installer=Join-Path $cache 'ViGEmBus_1.22.0_x64_x86_arm64.exe'
    }
    if (!(Test-Path -LiteralPath $installer)) {
        $partial=$installer+'.partial'
        try { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -UseBasicParsing -Uri $Dependency.url -OutFile $partial; Assert-Installer $partial $Dependency; Move-Item -LiteralPath $partial -Destination $installer -Force } finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force } }
    }
    Assert-Installer $installer $Dependency
    $installation=Start-Process -FilePath $installer -Verb RunAs -Wait -PassThru
    if ($installation.ExitCode -in @(3010,1641)) { Stop-Setup 'Reinicie o Windows para concluir o driver e inicie novamente.' 'Restart Windows to finish the driver installation, then start again.' }
    if ($installation.ExitCode -ne 0 -or !(Test-DriverReady)) { Stop-Setup 'O driver nao ficou ativo. Conclua o instalador ou reinicie o Windows, depois inicie novamente.' 'The driver is not active. Finish the installer or restart Windows, then start again.' }
}
if ($LibraryOnly) { return }
try {
    $script:AppRoot=Split-Path $PSScriptRoot -Parent; $script:DataRoot=Join-Path $env:LOCALAPPDATA 'ControllerBridge'
    $dependencies=Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Dependencies.json') | ConvertFrom-Json
    $client=Join-Path $script:AppRoot 'Nefarius.ViGEm.Client.dll'
    if (!$Diagnostic -and (!(Test-Path -LiteralPath $client) -or (Get-FileHash -LiteralPath $client -Algorithm SHA256).Hash -ine $dependencies.client.dllSha256)) { Stop-Setup 'A biblioteca do pacote esta ausente ou foi alterada. Extraia novamente o ZIP oficial inteiro.' 'The bundled library is missing or changed. Extract the entire official ZIP again.' }
    if ($Mode -eq 'USB') { Prepare-Usb }
    if (!$Diagnostic) { Ensure-Driver $dependencies.driver }
    exit 0
} catch { Write-Host $_.Exception.Message -ForegroundColor Red; Stop-Setup 'A preparacao parou. Nenhum comando foi enviado ao host. Confira a mensagem acima e tente novamente.' 'Setup stopped before starting the host. Check the message above and try again.' }
