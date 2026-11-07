#!/usr/bin/env python3
"""Rewrite Reel.apk with the REELS MODS libmain wrapper.

  original lib/arm64-v8a/libmain.so  -> lib/arm64-v8a/libmain_real.so (SONAME blanked)
  native/libmain.so (our wrapper)    -> lib/arm64-v8a/libmain.so

Every other entry keeps its ORIGINAL compression method: resources.arsc (and
anything the game shipped STORED) must stay uncompressed for targetSdk >= 30,
otherwise PackageManager rejects the APK with -124.

Usage:  python repack_zip.py [GameApk] [OurLib] [OutApk]
Then run repack.ps1 for zipalign + apksigner, or use this as the zip stage.
"""
import os
import shutil
import struct
import sys
import zipfile
from pathlib import Path

BASE = Path(__file__).resolve().parent
GAME_APK = Path(sys.argv[1]) if len(sys.argv) > 1 else BASE / ".." / "tools" / "game" / "Reel.apk"
OUR_LIB = Path(sys.argv[2]) if len(sys.argv) > 2 else BASE / "libmain.so"
OUT_APK = Path(sys.argv[3]) if len(sys.argv) > 3 else BASE / ".." / "tools" / "game" / "Reel-modded.apk"
WORK_DIR = Path(os.environ.get("TEMP", ".")) / "reels-repack"

LIB_ORIG = "lib/arm64-v8a/libmain.so"
LIB_REAL = "lib/arm64-v8a/libmain_real.so"


# ---------------------------------------------------------------- ELF helper
def patch_soname(data: bytearray) -> int:
    """Blank DT_SONAME of the original libmain.so (point it at the first NUL
    in .dynstr) so dlopen("libmain_real.so") cannot collide with our
    soname "libmain.so". Returns the old d_val for logging."""
    if bytes(data[:4]) != b"\x7fELF":
        raise SystemExit("not an ELF")
    e_phoff = struct.unpack_from("<Q", data, 0x20)[0]
    e_phentsize = struct.unpack_from("<H", data, 0x36)[0]
    e_phnum = struct.unpack_from("<H", data, 0x38)[0]

    def phdr(i):
        return e_phoff + i * e_phentsize

    dyn_off = dyn_size = None
    loads = []
    for i in range(e_phnum):
        p = phdr(i)
        p_type = struct.unpack_from("<I", data, p)[0]
        if p_type == 2:  # PT_DYNAMIC
            dyn_off = struct.unpack_from("<Q", data, p + 8)[0]
            dyn_size = struct.unpack_from("<Q", data, p + 40)[0]
        elif p_type == 1:  # PT_LOAD
            loads.append((
                struct.unpack_from("<Q", data, p + 16)[0],  # p_vaddr
                struct.unpack_from("<Q", data, p + 32)[0],  # p_filesz
                struct.unpack_from("<Q", data, p + 8)[0],   # p_offset
            ))
    if dyn_off is None:
        raise SystemExit("no PT_DYNAMIC")

    strtab_vaddr = strsz = 0
    soname_off = None
    off = dyn_off
    while off < dyn_off + dyn_size:
        tag = struct.unpack_from("<q", data, off)[0]
        if tag == 0:
            break
        if tag == 5:
            strtab_vaddr = struct.unpack_from("<Q", data, off + 8)[0]
        elif tag == 10:
            strsz = struct.unpack_from("<Q", data, off + 8)[0]
        elif tag == 14:
            soname_off = off + 8
        off += 16
    if soname_off is None:
        raise SystemExit("no DT_SONAME")
    if not strtab_vaddr or not strsz:
        raise SystemExit("no DT_STRTAB/DT_STRSZ")

    strtab_file = None
    for vaddr, filesz, foff in loads:
        if vaddr <= strtab_vaddr < vaddr + filesz:
            strtab_file = foff + (strtab_vaddr - vaddr)
            break
    if strtab_file is None:
        raise SystemExit("strtab not in any PT_LOAD")

    # first NUL byte inside .dynstr -> soname becomes ""
    zero_rel = -1
    for i in range(int(strsz)):
        if data[strtab_file + i] == 0:
            zero_rel = i
            break
    if zero_rel < 0:
        raise SystemExit("no NUL in dynstr")

    old = struct.unpack_from("<Q", data, soname_off)[0]
    struct.pack_into("<Q", data, soname_off, zero_rel)
    return old


# ---------------------------------------------------------------- rewrite apk
def main() -> None:
    WORK_DIR.mkdir(parents=True, exist_ok=True)
    out_tmp = OUT_APK.with_suffix(".apk.tmp")

    print(f"reading {GAME_APK}")
    with zipfile.ZipFile(GAME_APK) as zin:
        # 1) original libmain.so -> libmain_real.so with blanked SONAME
        orig = bytearray(zin.read(LIB_ORIG))
        old = patch_soname(orig)
        print(f"DT_SONAME d_val: {old} -> 0 (blanked soname)")

        our_bytes = OUR_LIB.read_bytes()

        # 2) full rewrite, preserving each entry's compression method
        stored_before = sum(1 for i in zin.infolist() if i.compress_type == zipfile.ZIP_STORED)
        if out_tmp.exists():
            out_tmp.unlink()
        with zipfile.ZipFile(out_tmp, "w", allowZip64=True) as zout:
            for info in zin.infolist():
                if info.filename == LIB_ORIG:
                    ni = zipfile.ZipInfo(LIB_REAL, date_time=info.date_time)
                    ni.compress_type = zipfile.ZIP_DEFLATED
                    ni.external_attr = info.external_attr
                    ni.create_system = info.create_system
                    zout.writestr(ni, bytes(orig))
                    continue
                ni = zipfile.ZipInfo(info.filename, date_time=info.date_time)
                ni.compress_type = info.compress_type  # STORED stays STORED
                ni.external_attr = info.external_attr
                ni.create_system = info.create_system
                ni.comment = info.comment
                with zin.open(info) as src, zout.open(ni, "w") as dst:
                    shutil.copyfileobj(src, dst, 1024 * 1024)

            # 3) our wrapper under the original name
            ni = zipfile.ZipInfo(LIB_ORIG)
            ni.compress_type = zipfile.ZIP_DEFLATED
            ni.external_attr = 0o100755 << 16  # regular file, rwxr-xr-x
            ni.create_system = 3  # unix
            zout.writestr(ni, our_bytes)

        stored_after = sum(
            1 for i in zipfile.ZipFile(out_tmp).infolist()
            if i.compress_type == zipfile.ZIP_STORED
        )
        print(f"stored entries: {stored_before} before -> {stored_after} after")

    out_tmp.replace(OUT_APK)
    print(f"rewrote {OUT_APK.stat().st_size:,} bytes -> {OUT_APK}")


if __name__ == "__main__":
    main()
