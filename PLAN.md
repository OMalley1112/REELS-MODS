# REELS MODS - Quest mod menu for "Reel" (Meta Quest)

Game: **Reel** - Most Sane Studios, free fishing game on the Meta store (Quest 2/3/3S/Pro).
Goal: an in-game mod menu with a **REELS** tab (Give Coins, Give Bundles, unlocks, ...) like a normal mod menu.

## Menu mockup

```
        R E E L S   M O D S
   ==============================
   [ MAIN ] [ REELS ] [ PAGES ]

   Give Coins ................ [ON]
   Give Bundles .............. [ON]
   Unlock Every Rod .......... [ON]
   Instant Reel .............. [OFF]
   Auto Catch ................ [OFF]
   Monster Lure .............. [ON]

   ------ / \ ------  01 / 02
```

## Why this is not a RIFTIX build

RIFTIX is a PC/BepInEx (Mono) mod for Gorilla Tag. Reel is a Quest game = Android/ARM64.
Two possible engines, both need the game files first:

| Engine | How the menu gets in |
|---|---|
| Mono | drop a managed DLL next to the game assemblies + small loader .so |
| IL2CPP | dump `libil2cpp.so` + `global-metadata.dat`, build a native ARM64 .so menu |

## Steps

1. [x] Quest connected, pulled `Reel.apk` v0.1.41 (1.12 GB, arm64, IL2CPP).
2. [x] Extracted `libil2cpp.so` + `global-metadata.dat`.
3. [x] Dumped classes with Il2CppDumper v6.7.46 (built from source) -> `dump.cs`.
4. [x] Economy mapped: `PlayerProfile` (coins/xp/crowns/candy/medals), server
       mirror `GameProfileSnapshot`, sync via `GameEconomySession`.
5. [x] Native injector written (`native/injector.cpp`): libmain wrapper +
       `VRFishGame.Update` inline hook + world-space TMP menu.
6. [x] Repacked + signed `Reel-modded.apk` (see `docs/INJECTION.md`).
7. [ ] On-device test: install, press X, give coins, check what the server
       keeps. Then add Give Bundles + persist-check.

## Files

- `native/`  - in-game injector source (`injector.cpp`, `repack.ps1`, `repack_zip.py`)
- `unity/`   - standalone Reel Mods Quest app source
- `docs/`    - ENGINE / MENU / CONTROLS / GET-IN-GAME / INJECTION
- `tools/`   - dumper + pulled game files (git-ignored, derived)
