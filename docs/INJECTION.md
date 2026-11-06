# In-game injection — REELS MODS inside Reel

Status: **built and signed, waiting for on-device test.**
Target: Reel v0.1.41 (`com.mostsanestudios.reel`), arm64 only.

## What it is

A drop-in replacement for the game's `libmain.so`. The original file is kept
inside the APK as `libmain_real.so`; our wrapper forwards `JNI_OnLoad` to it,
so the game boots normally — then a worker thread waits for the IL2CPP runtime,
hooks `VRFishGame.Update`, and draws a menu in front of the camera every frame.

```
lib/arm64-v8a/libmain.so        <- REELS MODS wrapper (ours)
lib/arm64-v8a/libmain_real.so   <- original game bootstrap (untouched)
```

## Controls (in game)

| input        | action                          |
|--------------|---------------------------------|
| **X**        | open / close the menu           |
| **Y or A**   | use the selected row            |
| **up/down**  | move selection                  |

Rows: Give Coins (+1M) · Give Crowns (+1k) · Give Candy (+10k) ·
Give Medals (+10k) · Give XP (+50k) · Max Everything.

The footer shows `key:<n>` — the raw code of the last button you pressed.
If pressing buttons doesn't move the menu, read that number off the screen
(or `adb logcat -s REELS_MODS`) and we remap instantly.

## How it works

1. `JNI_OnLoad` (ours) starts a worker, then calls the original bootstrap.
2. Worker waits for `libil2cpp.so` with `dlopen(RTLD_NOLOAD)`, resolves the
   exported `il2cpp_*` C API by `dlsym`.
3. Resolves `VRFish.Game.VRFishGame` + `VRFish.Gameplay.PlayerProfile` etc.
   by name across every loaded assembly image.
4. Inline-hooks `VRFishGame.Update` (RVA 0x6F7BCD4): first 4 instructions are
   copied to an RX page with the ADRP relocated, original bytes replaced by a
   `ldr x16 / br x16` veneer. No BTI in this binary, so mid-function branches
   are safe.
5. Menu tick (main thread): `Input.GetKeyDown` polling, world-space
   `TextMeshProUGUI` panel parented to `Camera.main`, currency writes to
   `PlayerProfile` fields, then invokes the progression `Changed` delegate so
   the HUD refreshes.

## Known risks (be honest with yourself)

- The economy is **server-authoritative** (`GameEconomySession`, profile
  `revision`). Local writes show up immediately but the server may overwrite
  them on the next sync — that's what the on-device test decides.
- Installing requires uninstalling the store build (signature change).
  Server-held progress should restore on login; on-headset-only saves won't.

## Rebuild

```powershell
# 1. compile the wrapper (NDK from the Unity install)
clang++.exe -target aarch64-linux-android31 -shared -fPIC -O2 -std=c++17 `
  -o libmain.so injector.cpp -llog -ldl -static-libstdc++ `
  "-Wl,-soname,libmain.so" "-Wl,--no-undefined"

# 2. repack the game APK (rename original, patch its SONAME, add ours, align, sign)
powershell -ExecutionPolicy Bypass -File repack.ps1
#    -> ..\tools\game\Reel-modded.apk
```

`repack.ps1` blanks the original lib's `DT_SONAME` so `dlopen("libmain_real.so")`
can't collide with our wrapper's soname.

## Install

```powershell
adb uninstall com.mostsanestudios.reel
adb install -r Reel-modded.apk
```

Watch it boot:

```powershell
adb logcat -s REELS_MODS
```

Expected lines: `libmain wrapper loaded` → `original JNI_OnLoad returned ...`
→ `il2cpp domain ready` → `core resolved OK` → `hook installed` →
`REELS MODS ready - press X in game`.
