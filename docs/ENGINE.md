# Reel engine notes

Everything here is provisional until the APK is pulled.

## Why this matters

The whole build path forks on one question: **IL2CPP or Mono?**

Check after unpacking the APK:

```
unzip -l Reel.apk | findstr /i "libil2cpp Assembly-CSharp"
```

| Found | Engine | Menu path |
|---|---|---|
| `lib/arm64-v8a/libil2cpp.so` | IL2CPP | dump metadata, native ARM64 `.so` |
| `lib/arm64-v8a/libmonosgen-2.0.so` + `Assembly-CSharp.dll` | Mono | managed DLL + small loader `.so` |

Quest games are IL2CPP the large majority of the time.

## IL2CPP workflow

1. Extract `lib/arm64-v8a/libil2cpp.so` and `assets/bin/Data/Managed/Metadata/global-metadata.dat`
2. Run Il2CppDumper against the pair -> `dump.cs`, `il2cpp.h`, `script.json`
3. Grep `dump.cs` for the economy:

```
findstr /i "coin currency bundle shop purchase unlock rod" dump.cs
```

4. Note the exact class + field names and their offset in `script.json`
5. Menu reads/writes those offsets directly from native code

## What the earlier stub files got wrong

`Downloads\ReelsMoneyMod.cs` referenced `GameEconomy._currentCoins` and
`TelemetryManager._isAntiCheatActive`. Those were guesses from an AI with no
access to the real assembly — treat them as wrong until `dump.cs` proves
otherwise. Do not build on them.

## Detection surface

Quest titles vary wildly: some use plain Unity PlayerPrefs, some use a
server-authoritative wallet, some run a BattlEye/EAC-style guard.

The dump will show which of these is true for Reel:

- wallet lives in a field we can write -> trivial, and it resets on restart
- wallet is PlayerPrefs/JSON on disk -> write the file, reload
- wallet is server-authoritative -> local edits are cosmetic and will be
  rolled back; only client-side feel mods (auto catch, instant reel) stick

Rule: find out before promising anything. Report what actually survives a
restart and a reconnect.
