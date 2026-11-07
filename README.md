# Reel Mods — REELS MODS menu

An in-game-style mod menu for **Reel** — Most Sane Studios, free fishing game
on Meta Quest (Quest 2 / 3 / 3S / Pro).

In the headset it installs as **`Reel Mods`** (Library → Unknown Sources), and
the panel you open is **`R E E L S   M O D S`**.

Same look and feel as RIFTIX: world-space panel, gradient title, button pop,
notifications, X/Y to open.

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

## Install

Download the APK from **[Releases](https://github.com/OMalley1112/REELS-MODS/releases)**
and put it on the headset:

```
adb install -r Reel-Mods-0.2.4.apk
```

> **0.2.2 would not open** (Unity injects `UnityPlayerActivity` with no
> `MAIN`/`LAUNCHER` intent-filter, so the APK had no launchable activity).
> Fixed in 0.2.3 — download the latest release.

Then **Library → filter icon → Unknown Sources → Reel Mods**.

**Controls:** X or Y (left hand) / A or B (right hand) opens the panel ·
trigger points and clicks · `<` `>` change page · tabs MAIN / REELS / PAGES.

**▶ OPEN REEL** — the button under the row list finds Reel on the headset and
launches it. Turn on **Auto Open Reel** (MAIN tab) and the app will boot
straight into the game after1.5s.

> Opening Reel is *not* the same as modding it. The launcher only starts the
> game; the REELS toggles still need Reel's files before they write anything.
> See `docs/GET-IN-GAME.md`.

## Status

| Step | State |
|---|---|
| Repo + plan + docs | done |
| Standalone menu APK, installs and runs on Quest | **done — v0.2.4** |
| One-tap launch of Reel from inside the menu | **done — v0.2.4** |
| In-game injection (wrapped `libmain.so`, X opens REELS MODS) | **done — source + docs, device test pending** |
| Quest connected / Developer Mode | done |
| Pull Reel APK | done (`tools/game/Reel.apk`, git-ignored) |
| Detect IL2CPP vs Mono | done — IL2CPP |
| Dump coin / bundle / rod code | done (`tools/dumper/output/dump.cs`, git-ignored) |
| Wire the REELS toggles to the real game | Give Coins/XPMedals/Candy write `PlayerProfile` fields; server rollback untested |
| Install inside Reel | **done — repack signs clean; 0.2.3-era `resources.arsc` fix included** |

The REELS rows switch state and toast correctly today. They are **not yet
writing Reel's wallet** — that needs the game APK pulled and its IL2CPP
classes dumped. See `docs/GET-IN-GAME.md` for the step-by-step.

## Repo layout

```
reels-mods/
  README.md      this file
  PLAN.md        full build plan + menu mockup
  docs/
    GET-IN-GAME.md   step-by-step: how to get it into Reel
    ENGINE.md        IL2CPP vs Mono decision + dump workflow
    MENU.md          panel, tabs, rows, notifications (RIFTIX look)
    CONTROLS.md      buttons and what each toggle does
  unity/src/     the menu source (single source of truth)
  unity/REELSMODS/   Unity 2022.3.2f1 project (Android / ARM64)
  tools/         apk unpackers, il2cpp dumpers
  build/         output .apk (git-ignored, attached to releases)
```

## Rebuilding the APK

```
"C:\Program Files\Unity\Hub\Editor\2022.3.2f1\Editor\Unity.exe" ^
  -batchmode -quit ^
  -projectPath reels-mods\unity\REELSMODS ^
  -buildTarget Android ^
  -executeMethod BuildScript.BuildFromCli ^
  -logFile build.log
```

Output: `unity/REELSMODS/build/Reel-Mods-<version>.apk`
(ARM64, IL2CPP, min SDK 29, Oculus XR loader).

## Legal

Private use on your own headset. Don't ship it, don't use it on other
people's accounts, don't use it to grief anyone.
