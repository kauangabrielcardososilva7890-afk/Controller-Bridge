param([string]$OutputPath = (Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tooling'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$cacheRoot = Join-Path $PSScriptRoot '.bootstrap-cache'
$env:NUGET_PACKAGES = Join-Path $cacheRoot 'nuget'
New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$dependencies = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Bootstrap\Dependencies.json') | ConvertFrom-Json
function Fetch-Pinned([string]$Url,[string]$Path,[string]$Sha256) {
    if (!(Test-Path -LiteralPath $Path)) { Invoke-WebRequest -Uri $Url -OutFile $Path }
    if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ine $Sha256) { throw ('Dependency checksum failed: ' + [IO.Path]::GetFileName($Path)) }
}
function Assert-PackagedDashboard([string]$Executable) {
    $inputFile = [IO.Path]::GetTempFileName()
    $outputFile = [IO.Path]::GetTempFileName()
    $errorFile = [IO.Path]::GetTempFileName()
    $process = $null
    try {
        $process = Start-Process -FilePath $Executable -ArgumentList @('--prepared','--diagnostic','--usb','--port','47654') -RedirectStandardInput $inputFile -RedirectStandardOutput $outputFile -RedirectStandardError $errorFile -PassThru
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
            if ($process.HasExited) {
                $details = ((Get-Content -Raw -LiteralPath $outputFile) + (Get-Content -Raw -LiteralPath $errorFile)).Trim()
                throw "Packaged dashboard exited before opening (exit $($process.ExitCode)): $details"
            }
        } while ($process.MainWindowTitle -notlike '*Controller Bridge*' -and [DateTime]::UtcNow -lt $deadline)
        if ($process.MainWindowTitle -notlike '*Controller Bridge*') { throw 'Packaged dashboard did not create its main window within 15 seconds.' }
    }
    finally {
        if ($process -and !$process.HasExited) {
            $null = $process.CloseMainWindow()
            if (!$process.WaitForExit(3000)) { Stop-Process -Id $process.Id -Force }
        }
        foreach ($file in @($inputFile,$outputFile,$errorFile)) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force } }
    }
}
$clientPackage = Join-Path $cacheRoot 'nefarius.vigem.client.1.21.256.nupkg'
Fetch-Pinned $dependencies.client.url $clientPackage $dependencies.client.packageSha256
$driverInstaller = Join-Path $cacheRoot 'ViGEmBus_1.22.0_x64_x86_arm64.exe'
Fetch-Pinned $dependencies.driver.url $driverInstaller $dependencies.driver.sha256
. (Join-Path $PSScriptRoot 'Bootstrap\Prepare.ps1') -LibraryOnly
Assert-Installer $driverInstaller $dependencies.driver
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (!(Test-Path -LiteralPath $windowsPowerShell)) { throw 'Windows PowerShell 5.1 is required to verify and run the packaged bootstrap scripts.' }
& $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Bootstrap\Tests.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Bootstrap verification failed.' }
$projectFile = Join-Path $PSScriptRoot 'ControllerBridge.Host.csproj'
[xml]$project = Get-Content -Raw -LiteralPath $projectFile
$version = [string]$project.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The project must declare a stable semantic version.' }
$outputRoot = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$stagingRoot = Join-Path $outputRoot ('.staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stagingRoot | Out-Null
$packages = @()
try {
    foreach ($variant in @(@{ Name = 'PT-BR'; Language = 'pt-BR'; Override = 'en'; Marker = 'Padrão: USB'; Opposite = 'Default: USB' }, @{ Name = 'EN'; Language = 'en'; Override = 'pt'; Marker = 'Default: USB'; Opposite = 'Padrão: USB' })) {
        $stage = Join-Path $stagingRoot $variant.Name
        dotnet publish $projectFile -c Release -r win-x64 --self-contained true --ignore-failed-sources "-p:RuntimeFrameworkVersion=$($dependencies.runtimeVersion)" '-p:PublishTrimmed=false' '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' "-p:DefaultLanguage=$($variant.Language)" '-p:DebugType=None' -o $stage
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($variant.Name)." }
        $executable = Join-Path $stage 'ControllerBridge.exe'
        if (!(Test-Path -LiteralPath $executable) -or (Get-Item -LiteralPath $executable).Length -lt 50MB) { throw 'Expected self-contained single-file executable was not published.' }
        foreach ($runtimeFile in @('hostfxr.dll','coreclr.dll','System.Private.CoreLib.dll','ControllerBridge.dll','ControllerBridge.deps.json','ControllerBridge.runtimeconfig.json')) { if (Test-Path -LiteralPath (Join-Path $stage $runtimeFile)) { throw ('Unexpected loose runtime file in single-file publish: ' + $runtimeFile) } }
        $archivePackage=[IO.Compression.ZipFile]::OpenRead($clientPackage)
        try { [IO.Compression.ZipFileExtensions]::ExtractToFile($archivePackage.GetEntry('lib/netstandard2.0/Nefarius.ViGEm.Client.dll'), (Join-Path $stage 'Nefarius.ViGEm.Client.dll'),$true) } finally { $archivePackage.Dispose() }
        if ((Get-FileHash -LiteralPath (Join-Path $stage 'Nefarius.ViGEm.Client.dll') -Algorithm SHA256).Hash -ine $dependencies.client.dllSha256) { throw 'Bundled client DLL hash failed.' }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Bootstrap') -Destination (Join-Path $stage 'Bootstrap') -Recurse
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ThirdParty') -Destination (Join-Path $stage 'ThirdParty') -Recurse
        $runtimeNotices = Join-Path $env:NUGET_PACKAGES ("microsoft.netcore.app.runtime.win-x64\" + $dependencies.runtimeVersion)
        Copy-Item -LiteralPath (Join-Path $runtimeNotices 'LICENSE.TXT') -Destination (Join-Path $stage 'ThirdParty\dotNET-Runtime-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $runtimeNotices 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $stage 'ThirdParty\dotNET-Runtime-ThirdPartyNotices.txt')
        New-Item -ItemType Directory -Path (Join-Path $stage 'Installers') | Out-Null
        Copy-Item -LiteralPath $driverInstaller -Destination (Join-Path $stage 'Installers\ViGEmBus_1.22.0_x64_x86_arm64.exe')
        Assert-PackagedDashboard $executable
        $reportedVersion = & $executable --version
        if ($LASTEXITCODE -ne 0 -or $reportedVersion -ne "Controller Bridge $version") { throw "Unexpected executable version for $($variant.Name)." }
        $help = (& $executable --help) -join "`n"
        if ($LASTEXITCODE -ne 0 -or !$help.Contains($variant.Marker)) { throw "Default language check failed for $($variant.Name)." }
        $overrideHelp = (& $executable --help --lang $variant.Override) -join "`n"
        if ($LASTEXITCODE -ne 0 -or !$overrideHelp.Contains($variant.Opposite)) { throw "Language override check failed for $($variant.Name)." }
        & $executable --self-test
        if ($LASTEXITCODE -ne 0) { throw "Self-test failed for $($variant.Name)." }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $stage 'README.md')
        $startText = if ($variant.Name -eq 'PT-BR') {
            "Controller Bridge $version — Português do Brasil`r`nProjeto criado 100% com IA.`r`n`r`n1. Extraia o ZIP inteiro e abra ControllerBridge.exe. Escolha 1 ou pressione Enter para USB; escolha 2 para Wi-Fi. Selecione o mesmo modo no app Android.`r`n2. Para USB, o host abre e aguarda mesmo sem celular. Conecte o cabo, desbloqueie o aparelho e autorize a depuração USB; o ADB reverse será configurado automaticamente, inclusive após reconectar. Para Wi-Fi, informe no celular o IP, a porta 47653 e o código de pareamento exibidos no painel.`r`n3. Minimize o painel para manter o host em segundo plano. Para encerrar, digite stop ou parar no console ou feche o painel. No painel, selecione um controle, escolha a saída Xbox 360 ou DualShock 4 e clique em um botão do desenho para remapeá-lo.`r`nO .NET e a biblioteca cliente já estão incluídos. Se faltar o driver, você escolherá abrir o instalador oficial incluído; o Windows mostrará UAC e o instalador visível. Cancelar interrompe a abertura do host.`r`nSe faltar ADB no modo USB, a preparação mostrará a página oficial da Google para você aceitar os termos, baixar e informar o adb.exe. O Android SDK não é redistribuído. Não é preciso reiniciar após conectar ou reconectar o cabo.`r`nIdioma: --lang en ou --lang pt. Modos diretos: --usb ou --wifi. Requer Windows 11 x64. Não execute arquivos diretamente de dentro do ZIP; mantenha todas as pastas extraídas juntas.`r`n"
        } else {
            "Controller Bridge $version — English`r`n100% AI-created project.`r`n`r`n1. Extract the full ZIP and open ControllerBridge.exe. Choose 1 or press Enter for USB; choose 2 for Wi-Fi. Select the same mode in the Android app.`r`n2. For USB, the host opens and waits even before the phone is connected. Connect the cable, unlock the phone and authorize USB debugging; ADB reverse is configured automatically, including after reconnecting. For Wi-Fi, enter the PC IP, port 47653 and pairing code shown in the dashboard on the phone.`r`n3. Minimize the dashboard to keep the host running. Type stop or parar in the console, or close the dashboard, to stop it. In the dashboard, select a controller, choose Xbox 360 or DualShock 4 output and click a diagram button to remap it.`r`n.NET and the client library are included. If the driver is missing, you choose whether to open the bundled official installer; Windows shows UAC and the visible installer. Cancelling stops the host startup.`r`nIf ADB is missing in USB mode, setup shows Google's official page so you can accept the terms, download and select adb.exe. Android SDK files are not redistributed. No restart is needed after connecting or reconnecting the cable.`r`nLanguage: --lang en or --lang pt. Direct modes: --usb or --wifi. Requires Windows 11 x64. Do not run files inside the ZIP; keep all extracted files/folders together.`r`n"
        }
        Set-Content -LiteralPath (Join-Path $stage 'START-HERE.txt') -Encoding utf8 -Value $startText
        Set-Content -LiteralPath (Join-Path $stage 'VERSION.txt') -Encoding utf8 -Value "ControllerBridge $version`r`nDefaultLanguage=$($variant.Language)"
        $archive = Join-Path $outputRoot "ControllerBridge-Windows-$($variant.Name)-v$version.zip"
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
        $packages += Get-Item -LiteralPath $archive
    }
    $checksums = foreach ($package in $packages) { (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $package.Name }
    Set-Content -LiteralPath (Join-Path $outputRoot 'SHA256SUMS.txt') -Encoding ascii -Value $checksums
    $packages | Select-Object Name, Length, FullName
} finally {
    $resolvedStage = [IO.Path]::GetFullPath($stagingRoot)
    $rootPrefix = $outputRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedStage.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolvedStage -Leaf).StartsWith('.staging-')) { throw 'Unsafe package cleanup path.' }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
