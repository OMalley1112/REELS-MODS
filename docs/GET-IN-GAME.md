# How to get REELS MODS into the game — step by step

Two different things, so don't mix them up:

| | What it is | Status |
|---|---|---|
| **A. The menu app** | REELS MODS as its own Quest app | ✅ works, downloaded from Releases |
| **B. In-game mod** | The toggles changing Reel's coins | ❌ needs your headset plugged in |

This guide covers both. Part B is the one you actually want.

---

## A. Install the menu app (2 minutes)

1. Download `Reel-Mods-0.2.2.apk` from the Releases page.
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
   adb install -r "C:\path\to\Reel-Mods-0.2.2.apk"
   ```
7. In the Quest: **Library → filter icon → Unknown Sources → Reel Mods**.
   (The in-panel title still reads **R E E L S   M O D S**.)
8. Press **X** or **Y** on the left controller (or **A** / **B** on the right) to open the panel.

---

## B. Get it into Reel (the real mod)

This is the part that needs the game's own files. Reel is an IL2CPP Quest
game, so the menu has to be injected as a native library. Nothing here can be
faked or skipped.

### Step 1 — Connect the headset
Same as A2–A5. The only thing that matters:
```
adb devices
```
must print a device. **Nothing below works until this line is true.**

### Step 2 — Find Reel's package name
```
adb shell pm list packages | findstr /i reel
```
It will look something like `com.mostsanestudios.reel`. Copy the exact value.

### Step 3 — Pull the APK
```
mkdir tools\game
adb shell pm path <package_from_step_2>
```
That prints `package:/data/app/.../base.apk`. Then:
```
adb pull <that path> tools\game\Reel.apk
```
Reel is roughly 1–2 GB. Let it finish.

### Step 4 — Unpack it
```
cd tools\game
tar -xf Reel.apk
```
Then check which engine it is:
```
dir lib\arm64-v8a
```
- `libil2cpp.so` present → **IL2CPP** (expected, continue to step 5)
- only `libmonosgen-2.0.so` + `Assembly-CSharp.dll` → **Mono** (much easier,
  skip to step 6)

### Step 5 — Dump the IL2CPP classes
1. Get **Il2CppDumper** (github.com/Perfare/Il2CppDumper), release zip.
2. Put `lib\arm64-v8a\libil2cpp.so` and
   `assets\bin\Data\Managed\Metadata\global-metadata.dat` in its folder.
3. Run `Il2CppDumper.exe libil2cpp.so global-metadata.dat output`
4. Open `output\dump.cs`.

### Step 6 — Find the real coin code
In `dump.cs`, search:
```
coin
currency
bundle
shop
purchase
unlock
rod
wallet
playerprefs
```
Write down the **exact** class name and field name you find.

> The old stub files in your Downloads (`ReelsMoneyMod.cs`) guessed
> `GameEconomy._currentCoins` and `TelemetryManager._isAntiCheatActive`.
> Those were invented. Trust only `dump.cs`.

### Step 7 — Tell me the class + field
Paste the lines from `dump.cs` here (or in the repo issue). That is the only
missing input. With them the menu row becomes a real write.

### Step 8 — Build the injected menu
`unity\src\BuildScript.cs` already builds the panel. The mod part is a native
`.so` that:
- attaches to Reel's process
- finds the coin field from step 6
- writes it when the **Give Coins** row is clicked

### Step 9 — Install into Reel
Two routes, chosen by what step 4 said:

| Engine | How it goes in |
|---|---|
| Mono | drop the managed DLL into `Managed/` + a small loader `.so`, repackage, `adb install -r` |
| IL2CPP | native `.so` next to `libil2cpp.so` + loader entry, repackage, `adb install -r` |

### Step 10 — Test in the game
1. Launch **Reel**.
2. Press **X** / **Y** (left) or **A** / **B** (right).
3. REELS tab → **Give Coins** → ON.
4. Look at the coin counter.
5. **Restart Reel** and check again — that tells you whether the value is
   local (sticks until restart), on disk (sticks forever), or server-side
   (gets rolled back and only client-side mods like Auto Catch will work).

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `adb devices` prints nothing | cable is charge-only → try another USB-C cable / port; Developer Mode not actually ON |
| `unauthorized` | put the headset on and accept **Allow USB debugging** |
| `INSTALL_FAILED_OLDER_SDK` | wrong build; re-download the release APK |
| App opens but no panel | press **X** or **Y**, not the trigger |
| Panel visible but trigger does nothing | the laser needs a controller pose; make sure the controllers are awake (grab them) |
| Menu opens inside Reel but coins don't change | step 6/7 not done — we still don't know the real field name |
