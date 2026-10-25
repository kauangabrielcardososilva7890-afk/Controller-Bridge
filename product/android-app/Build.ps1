$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$androidSdk = if ($env:CONTROLLER_BRIDGE_ANDROID_SDK) { [IO.Path]::GetFullPath($env:CONTROLLER_BRIDGE_ANDROID_SDK) } else { Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
$aapt2 = Join-Path $androidSdk 'build-tools\34.0.0\aapt2.exe'
$androidUserHome = Join-Path $projectRoot '.android-user'
$androidJava = 'C:\Program Files\Android\Android Studio\jbr'
$gradleUserHome = if ($env:CONTROLLER_BRIDGE_GRADLE_USER_HOME) { [IO.Path]::GetFullPath($env:CONTROLLER_BRIDGE_GRADLE_USER_HOME) } else { Join-Path $env:USERPROFILE '.gradle' }
$gradleCache = Join-Path $gradleUserHome 'wrapper\dists\gradle-8.2.1-all'
$gradleBat = Get-ChildItem -LiteralPath $gradleCache -Recurse -Filter 'gradle.bat' -ErrorAction Stop | Select-Object -First 1 -ExpandProperty FullName
$javaExe = Join-Path $androidJava 'bin\java.exe'
$javacExe = Join-Path $androidJava 'bin\javac.exe'
$keytoolExe = Join-Path $androidJava 'bin\keytool.exe'

if (-not (Test-Path -LiteralPath $androidSdk)) { throw "Android SDK não encontrado em $androidSdk" }
if (-not (Test-Path -LiteralPath $aapt2)) { throw "AAPT2 não encontrado em $aapt2" }
if (-not (Test-Path -LiteralPath $javaExe)) { throw "Java 21 do Android Studio não encontrado em $androidJava" }
if (-not (Test-Path -LiteralPath $keytoolExe)) { throw "keytool não encontrado em $keytoolExe" }
if (-not $gradleBat) { throw 'Gradle 8.2.1 não está disponível no cache local.' }

$env:JAVA_HOME = $androidJava
$env:ANDROID_SDK_ROOT = $androidSdk
$env:ANDROID_USER_HOME = $androidUserHome
$env:ANDROID_SDK_HOME = $null
$env:ANDROID_PREFS_ROOT = $null
$env:GRADLE_USER_HOME = $gradleUserHome
New-Item -ItemType Directory -Force -Path $androidUserHome | Out-Null
 $debugKeystore = Join-Path $androidUserHome 'controller-bridge-debug.keystore'
if (-not (Test-Path -LiteralPath $debugKeystore)) {
    & $keytoolExe -genkeypair -noprompt -storetype PKCS12 -keystore $debugKeystore -storepass android -alias androiddebugkey -keypass android -dname 'CN=Controller Bridge Debug,O=Controller Bridge,C=BR' -keyalg RSA -keysize 2048 -validity 10000
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao criar a chave de debug local.' }
}
Push-Location $projectRoot
try {
    & $gradleBat --offline --no-daemon "-Pandroid.aapt2FromMavenOverride=$aapt2" :app:assemblePtBrDebug :app:assembleEnDebug
    if ($LASTEXITCODE -ne 0) { throw "Gradle terminou com código $LASTEXITCODE" }

    $testOutput = Join-Path $projectRoot 'build\mapping-checks'
    New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
    $mappingSource = Join-Path $projectRoot 'app\src\main\java\com\controllerbridge\app\MappingModel.java'
    $allocatorSource = Join-Path $projectRoot 'app\src\main\java\com\controllerbridge\app\ProfileIdAllocator.java'
    $lifecycleSource = Join-Path $projectRoot 'app\src\main\java\com\controllerbridge\app\BridgeLifecycle.java'
    $inputFilterSource = Join-Path $projectRoot 'app\src\main\java\com\controllerbridge\app\ControllerInputFilter.java'
    $motionMatcherSource = Join-Path $projectRoot 'app\src\main\java\com\controllerbridge\app\MotionDeviceMatcher.java'
    $wifiSecureSource = Join-Path $projectRoot 'app\src\main\java\com\controllerbridge\app\WifiSecureChannel.java'
    $testSource = Join-Path $projectRoot 'app\src\test\java\com\controllerbridge\app\MappingModelSelfTest.java'
    $wifiTestSource = Join-Path $projectRoot 'app\src\test\java\com\controllerbridge\app\WifiSecureChannelSelfTest.java'
    & $javacExe -encoding UTF-8 -d $testOutput $mappingSource $allocatorSource $lifecycleSource $inputFilterSource $motionMatcherSource $wifiSecureSource $testSource $wifiTestSource
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar as verificações de mapeamento.' }
    & $javaExe -cp $testOutput com.controllerbridge.app.MappingModelSelfTest
    if ($LASTEXITCODE -ne 0) { throw 'As verificações de mapeamento falharam.' }
    & $javaExe -cp $testOutput com.controllerbridge.app.WifiSecureChannelSelfTest
    if ($LASTEXITCODE -ne 0) { throw 'As verificações do canal Wi-Fi falharam.' }

    $apkRoot = Join-Path $projectRoot 'app\build\outputs\apk'
    $builtApks = @(Get-ChildItem -LiteralPath $apkRoot -Recurse -Filter '*.apk' -ErrorAction Stop)
    $ptApk = @($builtApks | Where-Object { $_.FullName -match '(?i)[\\/]ptbr[\\/]debug[\\/]' -or $_.Name -match '(?i)pt.?br' })
    $enApk = @($builtApks | Where-Object { $_.FullName -match '(?i)[\\/]en[\\/]debug[\\/]' -or $_.Name -match '(?i)(^|[-_])en([-_]|$)' })
    if ($ptApk.Count -ne 1) { throw "Esperado exatamente um APK PT-BR; encontrados $($ptApk.Count)." }
    if ($enApk.Count -ne 1) { throw "Esperado exatamente um APK EN; encontrados $($enApk.Count)." }
    $dist = Join-Path $projectRoot 'dist'
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    $ptDeliverable = Join-Path $dist 'ControllerBridge-Android-PT-BR-v0.5.0.apk'
    $enDeliverable = Join-Path $dist 'ControllerBridge-Android-EN-v0.5.0.apk'
    Copy-Item -LiteralPath $ptApk[0].FullName -Destination $ptDeliverable -Force
    Copy-Item -LiteralPath $enApk[0].FullName -Destination $enDeliverable -Force
    $oldGeneric = Join-Path $dist 'ControllerBridge-android.apk'
    if (Test-Path -LiteralPath $oldGeneric) { Remove-Item -LiteralPath $oldGeneric -Force }
    Write-Output "APK PT-BR gerado: $ptDeliverable"
    Write-Output "APK EN gerado: $enDeliverable"
} finally {
    Pop-Location
}
