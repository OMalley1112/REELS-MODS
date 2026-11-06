# Rebuild Reel.apk with REELS MODS libmain wrapper.
#   original lib/arm64-v8a/libmain.so  -> lib/arm64-v8a/libmain_real.so (SONAME blanked)
#   native/libmain.so (our wrapper)    -> lib/arm64-v8a/libmain.so
param(
    [string]$GameApk   = "$PSScriptRoot\..\tools\game\Reel.apk",
    [string]$OurLib    = "$PSScriptRoot\libmain.so",
    [string]$OutApk    = "$PSScriptRoot\..\tools\game\Reel-modded.apk",
    [string]$WorkDir   = "$env:TEMP\reels-repack"
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$buildTools = "C:\Program Files\Unity\Hub\Editor\2022.3.2f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\build-tools\32.0.0"
$javaHome   = "C:\Program Files\Unity\Hub\Editor\2022.3.2f1\Editor\Data\PlaybackEngines\AndroidPlayer\OpenJDK"
$zipalign   = "$buildTools\zipalign.exe"
$apksigner  = "$buildTools\apksigner.bat"
$keytool    = "$javaHome\bin\keytool.exe"
$keystore   = "$PSScriptRoot\reels.keystore"

# ---------------------------------------------------------------- ELF helper
function Get-ElfSonamePatch {
    param([byte[]]$Bytes)
    # Returns a hashtable: @{ Offset = <file offset of d_un of DT_SONAME>; NewValue = <strtab-relative offset of a NUL byte> }
    function U32([int]$o) { [BitConverter]::ToUInt32($Bytes, $o) }
    function U64([int]$o) { [BitConverter]::ToUInt64($Bytes, $o) }
    function U16([int]$o) { [BitConverter]::ToUInt16($Bytes, $o) }
    if ($Bytes[0] -ne 0x7F -or $Bytes[1] -ne 0x45) { throw "not an ELF" }
    $phoff   = [int](U64 0x20)
    $phentsize = [int](U16 0x36)
    $phnum   = [int](U16 0x38)

    $dynOff = -1; $dynSize = 0
    for ($i = 0; $i -lt $phnum; $i++) {
        $p = $phoff + $i * $phentsize
        $type = [BitConverter]::ToUInt32($Bytes, $p)
        if ($type -eq 2) { # PT_DYNAMIC
            $dynOff  = [int](U64 ($p + 8))
            $dynSize = [int](U64 ($p + 40))
        }
    }
    if ($dynOff -lt 0) { throw "no PT_DYNAMIC" }

    $strtabVaddr = [uint64]0; $strsz = [uint64]0; $sonameOff = -1
    $off = $dynOff
    while ($off -lt ($dynOff + $dynSize)) {
        $tag = [BitConverter]::ToInt64($Bytes, $off)
        if ($tag -eq 0) { break }
        if ($tag -eq 5)  { $strtabVaddr = [BitConverter]::ToUInt64($Bytes, $off + 8) }
        if ($tag -eq 10) { $strsz       = [BitConverter]::ToUInt64($Bytes, $off + 8) }
        if ($tag -eq 14) { $sonameOff   = $off + 8 }
        $off += 16
    }
    if ($sonameOff -lt 0) { throw "no DT_SONAME" }
    if ($strtabVaddr -eq 0 -or $strsz -eq 0) { throw "no DT_STRTAB/DT_STRSZ" }

    # vaddr -> file offset via program headers
    $strtabFile = -1
    for ($i = 0; $i -lt $phnum; $i++) {
        $p = $phoff + $i * $phentsize
        $type = [BitConverter]::ToUInt32($Bytes, $p)
        if ($type -ne 1) { continue } # PT_LOAD
        $vaddr  = [BitConverter]::ToUInt64($Bytes, $p + 16)
        $filesz = [BitConverter]::ToUInt64($Bytes, $p + 32)
        $foff   = [BitConverter]::ToUInt64($Bytes, $p + 8)
        if ($strtabVaddr -ge $vaddr -and $strtabVaddr -lt ($vaddr + $filesz)) {
            $strtabFile = [int]($foff + ($strtabVaddr - $vaddr))
        }
    }
    if ($strtabFile -lt 0) { throw "strtab not in any PT_LOAD" }

    # first NUL byte inside dynstr -> soname becomes ""
    $zeroRel = -1
    for ($i = 0; $i -lt [int]$strsz; $i++) {
        if ($Bytes[$strtabFile + $i] -eq 0) { $zeroRel = $i; break }
    }
    if ($zeroRel -lt 0) { throw "no NUL in dynstr" }

    return @{ Offset = $sonameOff; NewValue = [uint64]$zeroRel; StrtabFile = $strtabFile }
}

# ---------------------------------------------------------------- extract
if (-not (Test-Path $WorkDir)) { New-Item -ItemType Directory -Path $WorkDir | Out-Null }
$origLibPath = "$WorkDir\libmain_orig.so"
if (-not (Test-Path $origLibPath)) {
    Write-Host "extracting original libmain.so from APK..."
    $zip = [System.IO.Compression.ZipFile]::OpenRead($GameApk)
    try {
        $e = $zip.GetEntry("lib/arm64-v8a/libmain.so")
        if (-not $e) { throw "libmain.so not in APK" }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $origLibPath, $true)
    } finally { $zip.Dispose() }
}

# ---------------------------------------------------------------- patch soname
$realLibPath = "$WorkDir\libmain_real.so"
$bytes = [System.IO.File]::ReadAllBytes($origLibPath)
$patch = Get-ElfSonamePatch -Bytes $bytes
$old = [BitConverter]::ToUInt64($bytes, $patch.Offset)
Write-Host ("DT_SONAME d_val: {0} -> {1} (blanked soname)" -f $old, $patch.NewValue)
[System.BitConverter]::GetBytes([uint64]$patch.NewValue).CopyTo($bytes, $patch.Offset)
[System.IO.File]::WriteAllBytes($realLibPath, $bytes)

# ---------------------------------------------------------------- rewrite apk
if (Test-Path $OutApk) { Remove-Item $OutApk -Force }
Write-Host "rewriting APK..."
$in  = [System.IO.Compression.ZipFile]::OpenRead($GameApk)
$out = [System.IO.Compression.ZipFile]::Open($OutApk, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $ourBytes = [System.IO.File]::ReadAllBytes($OurLib)
    foreach ($entry in $in.Entries) {
        $name = $entry.FullName
        if ($name -eq "lib/arm64-v8a/libmain.so") {
            # original becomes libmain_real.so
            $ne = $out.CreateEntry("lib/arm64-v8a/libmain_real.so", [System.IO.Compression.CompressionLevel]::Optimal)
            $src = [System.IO.File]::OpenRead($realLibPath)
            try {
                $dst = $ne.Open()
                try { $src.CopyTo($dst) } finally { $dst.Dispose() }
            } finally { $src.Dispose() }
            continue
        }
        $ne2 = $out.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
        $s = $entry.Open()
        try {
            $d = $ne2.Open()
            try { $s.CopyTo($d) } finally { $d.Dispose() }
        } finally { $s.Dispose() }
    }
    # our wrapper
    $we = $out.CreateEntry("lib/arm64-v8a/libmain.so", [System.IO.Compression.CompressionLevel]::Optimal)
    $ws = $we.Open()
    try { $ws.Write($ourBytes, 0, $ourBytes.Length) } finally { $ws.Dispose() }
} finally { $in.Dispose(); $out.Dispose() }
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
