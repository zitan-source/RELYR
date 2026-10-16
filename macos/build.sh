#!/bin/bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
if [[ "$(uname -s)" != Darwin ]]; then
    echo "This script requires macOS and Xcode Command Line Tools." >&2
    exit 1
fi
xcrun --find swiftc >/dev/null
out="$root/.verification/macos"
app="$out/RELYR Mac Trial.app"
version=$(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$root/macos/Info.plist")
mkdir -p "$out" "$app/Contents/MacOS" "$app/Contents/Resources"
sdk="$(xcrun --sdk macosx --show-sdk-path)"
common=(-swift-version 5 -warnings-as-errors -sdk "$sdk")
xcrun swiftc "${common[@]}" \
    "$root/macos/Sources/Configuration.swift" "$root/macos/Sources/LayerMachine.swift" \
    "$root/macos/Tests/CoreTests.swift" -o "$out/CoreTests"
"$out/CoreTests" "$out"
xcrun swiftc "${common[@]}" "$root/macos/Sources/NativeEvents.swift" "$root/macos/Sources/InstanceLock.swift" \
    "$root/macos/Tests/NativeEventTests.swift" -o "$out/NativeEventTests" -framework CoreGraphics
"$out/NativeEventTests" "$out"
xcrun swiftc "${common[@]}" -O -target arm64-apple-macos13.0 \
    "$root"/macos/Sources/*.swift -o "$app/Contents/MacOS/RELYR" \
    -framework AppKit -framework ApplicationServices -framework Carbon
cp "$root/macos/Info.plist" "$app/Contents/Info.plist"
cp "$root/LICENSE" "$app/Contents/Resources/LICENSE.txt"
cp "$root/macos/README.ja.md" "$app/Contents/Resources/README.ja.md"
plutil -lint "$app/Contents/Info.plist"
codesign --force --sign - "$app"
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
ditto -c -k --sequesterRsrc --keepParent "$app" "$out/RELYR-Mac-Trial-$version-arm64.zip"
(cd "$out" && shasum -a 256 "RELYR-Mac-Trial-$version-arm64.zip" > "RELYR-Mac-Trial-$version-arm64.zip.sha256")
echo "Built: $app"
