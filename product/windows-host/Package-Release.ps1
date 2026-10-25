param([string]$OutputPath = (Join-Path $PSScriptRoot 'artifacts'))
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tooling'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
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
    foreach ($variant in @(@{ Name = 'PT-BR'; Language = 'pt-BR'; Override = 'en'; Marker = 'Wi-Fi fica desligado'; Opposite = 'Wi-Fi is off' }, @{ Name = 'EN'; Language = 'en'; Override = 'pt'; Marker = 'Wi-Fi is off'; Opposite = 'Wi-Fi fica desligado' })) {
        $stage = Join-Path $stagingRoot $variant.Name
        dotnet publish $projectFile -c Release --self-contained false --ignore-failed-sources "-p:DefaultLanguage=$($variant.Language)" '-p:DebugType=None' -o $stage
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $($variant.Name)." }
        $executable = Join-Path $stage 'ControllerBridge.exe'
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
            "Controller Bridge $version — Português do Brasil`r`nProjeto criado 100% com IA.`r`n`r`nExtraia todos os arquivos deste ZIP na mesma pasta. Execute ControllerBridge.exe.`r`nEste pacote inicia em português. Use --lang en para mudar para inglês.`r`nRequer Windows 11 x64, .NET 8 Runtime e dependências de controle virtual descritas no README.`r`nPara Wi-Fi, execute ControllerBridge.exe --wifi. Para USB, siga os passos ADB do README.`r`n"
        } else {
            "Controller Bridge $version — English`r`n100% AI-created project.`r`n`r`nExtract all ZIP files into the same folder. Run ControllerBridge.exe.`r`nThis package starts in English. Use --lang pt to switch to Portuguese.`r`nRequires Windows 11 x64, .NET 8 Runtime, and virtual controller dependencies described in README.`r`nFor Wi-Fi, run ControllerBridge.exe --wifi. For USB, follow the README ADB steps.`r`n"
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

