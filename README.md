# REELS MODS

An in-game mod menu for **Reel** — Most Sane Studios, free fishing game on Meta Quest (Quest 2 / 3 / 3S / Pro).

Same look and feel as RIFTIX: world-space panel, gradient title, button pop, fancy notifications, X/Y to open.

```
          R E E L S   M O D S
     ==============================
     [ MAIN ] [ REELS ] [ PAGES ]

     Give Coins ............... [ON]
     Give Bundles ............. [ON]
     Unlock Every Rod ......... [ON]
     Instant Reel ............. [OFF]
     Auto Catch ............... [OFF]
     Monster Lure ............. [ON]

     ------ / \ ------   01 / 02
```

## Status

| Step | State |
|---|---|
| Repo + plan | done |
| Quest connected / Developer Mode | waiting on you |
| Pull Reel APK | blocked (needs headset) |
| Detect IL2CPP vs Mono | blocked |
| Dump coin / bundle / rod code | blocked |
| Build menu | not started |
| Install + test in game | not started |

## Repo layout

```
reels-mods/
  README.md      this file
  PLAN.md        full build plan + menu mockup
  docs/          engine notes, dump workflow
  src/           menu + mod source (once the dump lands)
  tools/         apk unpackers, il2cpp dumpers
  build/         output .so / patched apk
```

## Getting the game files

Reel is a Quest (Android/ARM64) game, so nothing here can be built until the APK is on disk.

1. Phone -> Meta Horizon app -> Settings -> Developer Mode -> **ON**
2. USB-C from Quest to PC
3. In the headset, accept **Allow USB debugging**
4. `quest-tools\platform-tools\adb.exe devices` shows `device`
5. `adb shell pm list packages | findstr -i reel` finds the package
6. `adb pull` the APK -> `tools/`

## Build target

Reel is almost certainly **IL2CPP** (Unity + Quest), which means:

- `lib/arm64-v8a/libil2cpp.so` + `assets/bin/Data/Managed/Metadata/global-metadata.dat`
- dump classes with Il2CppDumper -> `dump.cs`
- find the real coin / bundle / rod-unlock fields there (the class names guessed in older stub files were wrong)
- menu ships as a native ARM64 `.so` loaded through a loader, or an injected overlay

If the APK turns out to be Mono, it is much easier: a managed DLL + a small loader `.so`.

## Legal

Private use on your own headset. Don't ship it, don't use it on other people's accounts, don't use it to grief anyone.
