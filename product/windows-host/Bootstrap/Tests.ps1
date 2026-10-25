$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Prepare.ps1') -LibraryOnly
$checks=0
function Check([bool]$condition,[string]$message) { if (!$condition) { throw $message }; $script:checks++ }
$sample=@(Parse-AdbDevices @('List of devices attached','phone1 unauthorized usb:1-1','phone2 device product:x model:POCO','emulator-5554 offline','* daemon started successfully'))
Check ($sample.Count -eq 3) 'Only actual ADB device rows must be parsed.'
Check ($sample[0].State -eq 'unauthorized') 'Unauthorized phone must stay blocked.'
Check (@($sample | Where-Object State -eq 'device').Count -eq 1) 'Only authorized device is eligible.'
Check ($sample[1].Serial -eq 'phone2') 'Selected serial must be retained for adb -s.'
Check (@(Parse-AdbDevices @('List of devices attached','')).Count -eq 0) 'No device must not produce a false positive.'
$manifest=Get-Content -Raw (Join-Path $PSScriptRoot 'Dependencies.json') | ConvertFrom-Json
Check ($manifest.driver.url.StartsWith('https://github.com/nefarius/ViGEmBus/releases/download/')) 'Driver must use the official vendor release.'
Check ($manifest.driver.sha256 -match '^[0-9a-f]{64}$' -and $manifest.client.dllSha256 -match '^[0-9a-f]{64}$') 'Pinned SHA256 hashes required.'
Check ($manifest.driver.publisher -eq 'Nefarius Software Solutions e.U.') 'Publisher validation required.'
$script:installerStarted=$false
function Test-DriverReady { return $false }
function Read-Host { param($Prompt) return 'n' }
function Stop-Setup { param($pt,$en) throw [OperationCanceledException]::new('Expected test cancellation') }
function Start-Process { $script:installerStarted=$true; throw 'Installer must not run in a test.' }
try { Ensure-Driver $manifest.driver; throw 'Cancellation did not stop setup.' } catch [OperationCanceledException] { }
Check (!$script:installerStarted) 'Declining driver installation must not start any installer.'
$script:reverseCalled=$false
function Find-Adb { return 'C:\test\adb.exe' }
function Invoke-Adb { param($AdbPath,$AdbArguments) if ($AdbArguments[0] -eq 'devices') { return [pscustomobject]@{Code=0;Lines=@('phone1 unauthorized','emulator-5554 device')} }; $script:reverseCalled=$true; return [pscustomobject]@{Code=0;Lines=@()} }
try { Prepare-Usb; throw 'Unauthorized phone did not stop USB setup.' } catch [OperationCanceledException] { }
Check (!$script:reverseCalled) 'Unauthorized phone must stop before reverse, even with another authorized device.'
Write-Host "PASS: $checks bootstrap checks (no downloads or installations)."
