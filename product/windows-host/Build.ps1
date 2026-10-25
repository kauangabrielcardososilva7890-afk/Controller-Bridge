param([string]$Configuration = 'Release', [string]$OutputPath = '')
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tooling'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$projectFile = Join-Path $PSScriptRoot 'ControllerBridge.Host.csproj'
$buildArgs = @('build', $projectFile, '-c', $Configuration, '--ignore-failed-sources')
if ($OutputPath) { $buildArgs += @('-o', $OutputPath) }
dotnet @buildArgs
exit $LASTEXITCODE
