# How to get REELS MODS into the game — step by step

Two different things, so don't mix them up:

| | What it is | Status |
|---|---|---|
| **A. The menu app** | REELS MODS as its own Quest app (OPEN REEL button) | ✅ works, v0.2.5.0 on Releases |
| **B. In-game mod** | REELS MODS inside Reel, opened with **X** | ✅ built — install the modded Reel (below) |

This guide covers both. Part B is the one you actually want.

---

## A. Install the menu app (2 minutes)

1. Download `Reel-Mods-0.2.5.0.apk` from the Releases page.
2. Put the Quest on and enable **Developer Mode**:
   - Phone → **Meta Horizon app** → menu → **Settings** → **Devices** → your Quest → **Developer Mode** → ON
3. Plug the Quest into the PC with the USB-C cable.
4. In the headset, a popup says **Allow USB debugging** → click **Allow**.
5. On the PC open a terminal in `quest-tools\platform-tools\` and run:
   ```
   adb devices
   ```
   You must see a line ending in `device`, not `unauthorized` or nothing.
6. Install:
   ```
   adb install -r "C:\path\to\Reel-Mods-0.2.5.0.apk"
   ```
7. In the Quest: **Library → filter icon → Unknown Sources → Reel Mods**.
   (The in-panel title still reads **R E E L S   M O D S**.)
8. Press **X** or **Y** on the left controller (or **A** / **B** on the right) to open the panel.
9. Point the laser at **▶ OPEN REEL** and pull the trigger — Reel boots.

> v0.2.4 fixed the click bug: tabs, the `<` `>` page arrows and OPEN REEL
> were dead in VR because the laser only recognized row components.

---

## B. Get the menu inside Reel (the real mod)

The in-game menu is a native library (`libmain.so`) wrapped around Reel's own
`libmain.so` — see `docs/INJECTION.md` for how it works. The modded game APK
is **not** uploaded here (redistribution of the full game), so you rebuild it
once from your own pulled copy:

### One-time rebuild (needs your pulled `tools\game\Reel.apk`)

```powershell
cd native
# 1. compile the wrapper (NDK from the Unity install)
clang++.exe -target aarch64-linux-android31 -shared -fPIC -O2 -std=c++17 `
  -o libmain.so injector.cpp -llog -ldl -static-libstdc++ `
  "-Wl,-soname,libmain.so" "-Wl,--no-undefined"

# 2. repack (needs Python 3 on PATH) -> ..\tools\game\Reel-modded.apk
powershell -ExecutionPolicy Bypass -File repack.ps1
```

### Install it (every time the game updates)

```powershell
# official and modded Reel have different signatures, so remove first:
adb uninstall com.mostsanestudios.reel
adb install -r ..\tools\game\Reel-modded.apk
```

### Test in the game

1. Launch **Reel** (from Reel Mods press **▶ OPEN REEL**, or from Unknown Sources).
2. Press **X** (or Y / A / B) — the **REELS MODS** panel spawns in front of you.
3. REELS rows → **GIVE COINS** etc. — the wallet line in the panel refreshes.
4. Watch the log if it misbehaves:
   ```
   adb logcat -s REELS_MODS
   ```
   Expected: `wrapper loaded` → `hook installed` → `menu built` → `menu OPEN`.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `adb devices` prints nothing | cable is charge-only → try another USB-C cable / port; Developer Mode not actually ON |
| `unauthorized` | put the headset on and accept **Allow USB debugging** |
| `INSTALL_FAILED_OLDER_SDK` | wrong build; re-download the release APK |
| `INSTALL_FAILED_UPDATE_INCOMPATIBLE` | signatures differ → `adb uninstall com.mostsanestudios.reel` first, then install |
| App opens but no panel | press **X** or **Y**, not the trigger |
| Panel visible but trigger does nothing | the laser needs a controller pose; make sure the controllers are awake (grab them) |
| Tabs / page arrows / OPEN REEL don't click | update to **v0.2.5.0** — earlier builds only recognized row components |
| No menu inside Reel | Reel isn't the modded build — check `adb logcat -s REELS_MODS` prints `wrapper loaded` |
| Menu opens inside Reel but coins don't change | economy is server-authoritative — see `docs/INJECTION.md` § persistence |
