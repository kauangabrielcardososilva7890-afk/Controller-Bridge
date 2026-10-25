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
$script:reverseCalled=$false
$script:reverseArgs=@()
$script:adbLines=@('phone1 unauthorized')
$script:adbAvailable=$false
$script:adbDeviceExitCode=0
$script:adbReverseExitCode=0
function Find-Adb { if($script:adbAvailable){return 'C:\test\adb.exe'}; return $null }
function Invoke-Adb { param($AdbPath,$AdbArguments) if ($AdbArguments[0] -eq 'devices') { return [pscustomobject]@{Code=$script:adbDeviceExitCode;Lines=$script:adbLines} }; $script:reverseCalled=$true; $script:reverseArgs=$AdbArguments; return [pscustomobject]@{Code=$script:adbReverseExitCode;Lines=@('simulated reverse result')} }
function Save-SelectedUsbDevice { param($AdbPath,$Serial) $script:savedSerial=$Serial }
function Read-Host { param($Prompt) return '' }
Prepare-Usb
Check (!$script:reverseCalled) 'USB setup must allow the host to start when ADB is not installed yet.'
$script:adbAvailable=$true
Prepare-Usb
Check (!$script:reverseCalled) 'USB setup must let the host start while waiting for the user to authorize a phone.'
$script:adbLines=@('List of devices attached','emulator-5554 offline')
Prepare-Usb
Check (!$script:reverseCalled) 'USB setup must let the host start while no phone is connected.'
$script:adbLines=@('phone2 device product:x model:POCO')
Prepare-Usb
Check ($script:reverseCalled -and $script:reverseArgs[1] -eq 'phone2' -and $script:savedSerial -eq 'phone2') 'An authorized USB phone must still receive adb reverse and its selection must be remembered.'
$script:reverseCalled=$false
$script:adbDeviceExitCode=1
Prepare-Usb
Check (!$script:reverseCalled) 'A transient ADB failure must not prevent the host from opening.'
$script:adbDeviceExitCode=0
$script:adbReverseExitCode=1
Prepare-Usb
Check ($script:reverseCalled) 'A transient reverse failure must not prevent the host from opening.'
$script:installerStarted=$false
function Test-DriverReady { return $false }
function Read-Host { param($Prompt) return 'n' }
function Stop-Setup { param($pt,$en) throw [OperationCanceledException]::new('Expected test cancellation') }
function Start-Process { $script:installerStarted=$true; throw 'Installer must not run in a test.' }
try { Ensure-Driver $manifest.driver; throw 'Cancellation did not stop setup.' } catch [OperationCanceledException] { }
Check (!$script:installerStarted) 'Declining driver installation must not start any installer.'
Write-Host "PASS: $checks bootstrap checks (no downloads or installations)."
