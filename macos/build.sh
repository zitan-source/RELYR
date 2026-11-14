#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
if [[ "$(uname -s)" != Darwin ]]; then
    echo "This script requires macOS and Xcode Command Line Tools." >&2
    exit 1
fi
xcrun --find swiftc >/dev/null
out="$root/.verification/macos-build"
# Build and validate away from the installed app and its privacy grant.
[[ -f "$root/.local-signing/relyr.keychain-db" ]] || { echo "Run python3 macos/sign-local.py --initialize once before building." >&2; exit 1; }
app="$out/RELYR Mac Trial.app"
version=$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$root/macos/Info.plist")
mkdir -p "$out" "$app/Contents/MacOS" "$app/Contents/Resources"
rm -f "$out/validated-signature.txt"
sdk="$(xcrun --sdk macosx --show-sdk-path)"
common=(-I "$root/macos/CArchive" -swift-version 5 -warnings-as-errors -sdk "$sdk" -module-cache-path "$out/module-cache")
xcrun swiftc "${common[@]}" "$root/macos/Sources/TrackpadRecognizer.swift" "$root/macos/Tests/TrackpadTests.swift" -o "$out/TrackpadTests"
"$out/TrackpadTests"
xcrun swiftc "${common[@]}" "$root/macos/Sources/MacRelease.swift" "$root/macos/Tests/MacReleaseTests.swift" -o "$out/MacReleaseTests"
"$out/MacReleaseTests"
xcrun swiftc "${common[@]}" \
    "$root/macos/Sources/WindowGeometry.swift" "$root/macos/Sources/Configuration.swift" "$root/macos/Sources/LayerMachine.swift" \
    "$root/macos/Tests/CoreTests.swift" -o "$out/CoreTests"
"$out/CoreTests" "$out"
xcrun swiftc "${common[@]}" "$root/macos/Sources/MouseDeviceTracker.swift" "$root/macos/Tests/MouseDeviceTrackerTests.swift" -o "$out/MouseDeviceTrackerTests" -framework IOKit
"$out/MouseDeviceTrackerTests"
xcrun swiftc "${common[@]}" "$root/macos/Sources/NativeEvents.swift" "$root/macos/Sources/TextInputRouting.swift" "$root/macos/Sources/InstanceLock.swift" \
    "$root/macos/Tests/NativeEventTests.swift" -o "$out/NativeEventTests" -framework CoreGraphics -framework Carbon
"$out/NativeEventTests" "$out"
xcrun swiftc "${common[@]}" "$root/macos/Sources/WindowGeometry.swift" "$root/macos/Sources/WindowActions.swift" "$root/macos/Sources/WindowSpaces.swift" \
    "$root/macos/Tests/WindowActionTests.swift" -o "$out/WindowActionTests" -framework AppKit -framework ApplicationServices
"$out/WindowActionTests"
xcrun swiftc "${common[@]}" "$root/macos/Sources/WindowGeometry.swift" "$root/macos/Sources/Configuration.swift" \
    "$root/macos/Sources/AudioActions.swift" "$root/macos/Tests/AudioActionTests.swift" -o "$out/AudioActionTests" -framework CoreAudio
"$out/AudioActionTests"
python3 "$root/macos/Tests/archive-fixtures.py" "$out/archive-tests"
xcrun swiftc "${common[@]}" "$root/macos/Sources/WindowGeometry.swift" "$root/macos/Sources/Configuration.swift" "$root/macos/Sources/ArchiveAutomation.swift" "$root/macos/Tests/ArchiveTests.swift" -o "$out/ArchiveTests" -framework AppKit
"$out/ArchiveTests" "$out/archive-tests"
xcrun swiftc "${common[@]}" "$root/macos/Sources/InstanceLock.swift" "$root/macos/Sources/UninstallPlan.swift" "$root/macos/Tests/UninstallTests.swift" -o "$out/UninstallTests"
"$out/UninstallTests" "$out"
xcrun swiftc "${common[@]}" -O -target arm64-apple-macos13.0 \
    "$root"/macos/Sources/*.swift -o "$app/Contents/MacOS/RELYR" \
    -framework AppKit -framework ApplicationServices -framework Carbon
cp "$root/macos/Info.plist" "$app/Contents/Info.plist"
# Keep the Windows product artwork as the single source of truth.
iconset="$out/RELYR.iconset"
mkdir -p "$iconset"
sips -s format png "$root/RELYR/Assets/RELYR.ico" --out "$out/RELYR-icon-master.png" >/dev/null
for size in 16 32 128 256 512; do
    sips -z "$size" "$size" "$out/RELYR-icon-master.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
    double=$((size * 2))
    sips -z "$double" "$double" "$out/RELYR-icon-master.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/RELYR.icns"
cp "$root/LICENSE" "$app/Contents/Resources/LICENSE.txt"
cp "$root/macos/README.ja.md" "$app/Contents/Resources/README.ja.md"
plutil -lint "$app/Contents/Info.plist"
python3 - "$app/Contents/Info.plist" <<'PYTEST'
import plistlib,sys
with open(sys.argv[1], 'rb') as f: info = plistlib.load(f)
assert not info.get('LSMultipleInstancesProhibited',False), 'Other login sessions must not be blocked by Launch Services'
print('PASS: Launch Services permits separate user sessions; per-user lock remains responsible for duplicates')
PYTEST
python3 "$root/macos/sign-local.py" "$app"
codesign --verify --deep --strict --verbose=2 "$app"
lipo "$app/Contents/MacOS/RELYR" -verify_arch arm64
if [[ "$(uname -m)" == arm64 ]]; then
    if "$app/Contents/MacOS/RELYR" --ui-smoke-test; then
        echo "Unsafe UI-test arguments were accepted." >&2
        exit 1
    else
        [[ $? -eq 2 ]]
    fi
    "$app/Contents/MacOS/RELYR" --ui-smoke-test "$out/RELYR-Mac-Trial-preview.png"
fi
python3 "$root/macos/Tests/verify-signing.py" "$app" "$out/signing-test"
ditto -c -k --sequesterRsrc --keepParent "$app" "$out/RELYR-Mac-Trial-$version-arm64.zip"
(cd "$out" && shasum -a 256 "RELYR-Mac-Trial-$version-arm64.zip" > "RELYR-Mac-Trial-$version-arm64.zip.sha256")
codesign -d -vvv "$app" 2>&1 | sed -n 's/^CDHash=//p' > "$out/validated-signature.txt"
echo "Built: $app"
echo "Install verified build: bash macos/install-local.sh"
