# Rebuild Reel.apk with REELS MODS libmain wrapper.
#   original lib/arm64-v8a/libmain.so  -> lib/arm64-v8a/libmain_real.so (SONAME blanked)
#   native/libmain.so (our wrapper)    -> lib/arm64-v8a/libmain.so
#
# The zip rewrite (extract + SONAME patch + repack preserving each entry's
# compression method) lives in repack_zip.py: .NET Framework's ZipArchive
# cannot emit STORED entries, and resources.arsc MUST stay stored for
# targetSdk >= 30 (otherwise install fails with -124).
param(
    [string]$GameApk   = "$PSScriptRoot\..\tools\game\Reel.apk",
    [string]$OurLib    = "$PSScriptRoot\libmain.so",
    [string]$OutApk    = "$PSScriptRoot\..\tools\game\Reel-modded.apk"
)

$ErrorActionPreference = 'Stop'

$buildTools = "C:\Program Files\Unity\Hub\Editor\2022.3.2f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools\32.0.0"
$javaHome   = "C:\Program Files\Unity\Hub\Editor\2022.3.2f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK"
$zipalign   = "$buildTools\zipalign.exe"
$apksigner  = "$buildTools\apksigner.bat"
$keytool    = "$javaHome\bin\keytool.exe"
$keystore   = "$PSScriptRoot\reels.keystore"

# ---------------------------------------------------------------- rewrite apk
Write-Host "rewriting APK (python)..."
& python "$PSScriptRoot\repack_zip.py" $GameApk $OurLib $OutApk
if ($LASTEXITCODE -ne 0) { throw "repack_zip.py failed" }
Write-Host ("rewrote {0:N0} bytes" -f (Get-Item $OutApk).Length)

# ---------------------------------------------------------------- align + sign
& $zipalign -f -p 4 $OutApk "$OutApk.aligned"
if ($LASTEXITCODE -ne 0) { throw "zipalign failed" }
Move-Item "$OutApk.aligned" $OutApk -Force

if (-not (Test-Path $keystore)) {
    Write-Host "creating keystore..."
    & $keytool -genkeypair -v -keystore $keystore -alias reelsmods `
        -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass reelsmods -keypass reelsmods `
        -dname "CN=REELS MODS, O=REELS" 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "keytool failed" }
}

$env:JAVA_HOME = $javaHome
$env:PATH = "$javaHome\bin;$env:PATH"
& $apksigner sign --ks $keystore --ks-pass pass:reelsmods --key-pass pass:reelsmods $OutApk
if ($LASTEXITCODE -ne 0) { throw "apksigner failed" }
& $apksigner verify --print-certs $OutApk | Select-Object -First 4
Write-Host "DONE: $OutApk"
