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

1. [ ] Quest connected: Developer Mode ON, USB-C to PC, "Allow USB debugging" accepted.
2. [ ] Pull the game: `adb shell pm list packages` -> find Reel -> `adb pull` the APK.
3. [ ] Unpack APK: check `lib/arm64-v8a/` for `libil2cpp.so` (IL2CPP) or Mono (Assembly-CSharp.dll).
4. [ ] Dump classes -> find the coin / bundle / rod-unlock code and where the values live.
5. [ ] Build the menu (world-space canvas rows, same look as RIFTIX: gradient, pop, notifications).
6. [ ] Wire REELS tab: Give Coins, Give Bundles, unlock rows.
7. [ ] Repack + install, test in-game with the X button, report which mods actually stick.

## Files

- `tools/`  - dumpers, apk unpackers (added as needed)
- `src/`    - menu + mod source
- `build/`  - output .so / patched apk
