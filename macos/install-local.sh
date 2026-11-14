#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
staged="$root/.verification/macos-build/RELYR Mac Trial.app"
case "${1:-}" in
    "") installed="/Applications/RELYR Mac Trial.app" ;;
    --verification) installed="$root/.verification/macos/RELYR Mac Trial.app" ;;
    *) echo "Usage: bash macos/install-local.sh [--verification]" >&2; exit 2 ;;
esac
[[ -f "$root/.verification/macos-build/validated-signature.txt" ]] || { echo "Run bash macos/build.sh successfully first." >&2; exit 1; }
actual=$(codesign -d -vvv "$staged" 2>&1 | sed -n 's/^CDHash=//p')
[[ "$actual" == "$(cat "$root/.verification/macos-build/validated-signature.txt")" ]] || { echo "Build changed after validation." >&2; exit 1; }
codesign --verify --deep --strict "$staged"
python3 - "$staged" "$installed" "$root" "$actual" <<'PYINSTALL'
from pathlib import Path
import ctypes
import fcntl
import os
import subprocess
import sys
import tempfile

source, destination, root = map(Path, sys.argv[1:4])
expected_hash = sys.argv[4]
build = root / ".verification/macos-build"
quit_tool = build / "QuitForUpdate"
codesign = "/usr/bin/codesign"

def validate(bundle):
    subprocess.run([codesign, "--verify", "--deep", "--strict", bundle], check=True)
    signature = subprocess.check_output([codesign, "-d", "-vvv", bundle], stderr=subprocess.STDOUT)
    if f"CDHash={expected_hash}".encode() not in signature.splitlines():
        raise RuntimeError("App signature changed after validation.")

rename = ctypes.CDLL(None, use_errno=True).renamex_np
rename.argtypes = [ctypes.c_char_p, ctypes.c_char_p, ctypes.c_uint]
rename.restype = ctypes.c_int

def swap(first, second):
    rename_swap = 0x00000002  # RENAME_SWAP from <sys/stdio.h>
    if rename(os.fsencode(first), os.fsencode(second), rename_swap):
        error = ctypes.get_errno()
        raise OSError(error, os.strerror(error), first, None, second)

with (build / "install-local.lock").open("a") as lock:
    fcntl.flock(lock, fcntl.LOCK_EX)
    if destination.is_symlink() or (destination.exists() and not destination.is_dir()):
        raise RuntimeError(f"Unexpected app bundle path: {destination}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix=".relyr-update-", dir=destination.parent) as temp:
        pending = Path(temp) / destination.name
        subprocess.run(["/usr/bin/ditto", source, pending], check=True)
        validate(pending)
        subprocess.run(["/usr/bin/xcrun", "swiftc", root / "macos/Tools/QuitForUpdate.swift",
                        "-module-cache-path", build / "module-cache", "-o", quit_tool,
                        "-framework", "AppKit"], check=True)
        subprocess.run([quit_tool, destination], check=True)
        if destination.exists():
            # Keep the enclosing bundle and its Finder/Dock bookmark in place.
            current_contents = destination / "Contents"
            pending_contents = pending / "Contents"
            if current_contents.is_symlink() or not current_contents.is_dir():
                raise RuntimeError(f"Unexpected app contents: {current_contents}")
            swap(current_contents, pending_contents)
            try:
                validate(destination)
            except BaseException:
                swap(current_contents, pending_contents)
                raise
        else:
            pending.rename(destination)
PYINSTALL
# Refresh only this application's registration after replacing its bundle.
# Do not reset the global icon database or restart Finder/Dock.
registrar="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
if [[ "${1:-}" != --verification && -x "$registrar" ]]; then
    "$registrar" -f "$installed" || echo "Icon registration refresh failed; app update succeeded." >&2
fi
echo "Updated: $installed"
