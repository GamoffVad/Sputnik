#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1:-0.2.2}"
output="${2:-$root/artifacts/macos}"
build_number="${GITHUB_RUN_NUMBER:-1}"
minimum_version="13.0"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || ! "$build_number" =~ ^[1-9][0-9]*$ ]]; then
  echo "Expected a release version such as 0.1.0 and a positive build number." >&2
  exit 2
fi
if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Build Sputnik for Mac on macOS with Xcode installed." >&2
  exit 1
fi
for tool in xcrun python3; do
  if ! command -v "$tool" >/dev/null; then
    echo "Required build tool is missing: $tool" >&2
    exit 1
  fi
done

mkdir -p "$output"
output="$(cd "$output" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

volume="$work/volume"
app="$volume/Sputnik.app"
resources="$app/Contents/Resources"
executable="$app/Contents/MacOS/Sputnik"
mkdir -p "$resources" "$(dirname "$executable")"

sources=()
while IFS= read -r -d '' path; do
  sources+=("$path")
done < <(find "$root/macos/Sources" -type f -name '*.swift' -print0)
if [[ ! ${sources[0]+present} ]]; then
  echo "Swift sources are missing from macos/Sources." >&2
  exit 1
fi

sdk="$(xcrun --sdk macosx --show-sdk-path)"
for architecture in arm64 x86_64; do
  xcrun swiftc -O -whole-module-optimization -parse-as-library -module-name SputnikMac -sdk "$sdk" \
    -target "$architecture-apple-macosx$minimum_version" \
    "${sources[@]}" -o "$work/Sputnik-$architecture"
done
xcrun lipo -create "$work/Sputnik-arm64" "$work/Sputnik-x86_64" -output "$executable"
xcrun lipo "$executable" -verify_arch arm64 x86_64
chmod 755 "$executable"

ditto "$root/wwwroot" "$resources/wwwroot"
python3 "$root/macos/prepare_assets.py" "$resources/wwwroot/assets" "$root/pets/knight-watchman.pet"
iconset="$work/Sputnik.iconset"
mkdir "$iconset"
# Reuse the approved app icon. The Windows icon contains a 256px PNG layer.
python3 - "$root/Sputnik.ico" "$work/icon.png" <<'PY'
from pathlib import Path
from struct import unpack_from
import sys

source = Path(sys.argv[1]).read_bytes()
count = unpack_from("<H", source, 4)[0]
for index in range(count):
    width, height, _, _, _, _, size, offset = unpack_from("<BBBBHHII", source, 6 + index * 16)
    data = source[offset:offset + size]
    if width == height == 0 and data.startswith(b"\x89PNG\r\n\x1a\n"):
        Path(sys.argv[2]).write_bytes(data)
        break
else:
    raise SystemExit("Sputnik.ico is missing its 256px PNG layer")
PY
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$work/icon.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  sips -z "$((size * 2))" "$((size * 2))" "$work/icon.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$resources/Sputnik.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDisplayName</key><string>Спутник</string>
  <key>CFBundleName</key><string>Sputnik</string>
  <key>CFBundleIdentifier</key><string>com.gamoffvad.sputnik</string>
  <key>CFBundleExecutable</key><string>Sputnik</string>
  <key>CFBundleIconFile</key><string>Sputnik.icns</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$build_number</string>
  <key>LSMinimumSystemVersion</key><string>$minimum_version</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSAppTransportSecurity</key>
  <dict>
    <key>NSAllowsLocalNetworking</key><true/>
    <key>NSExceptionDomains</key>
    <dict>
      <key>127.0.0.1</key>
      <dict>
        <key>NSExceptionAllowsInsecureHTTPLoads</key><true/>
      </dict>
    </dict>
  </dict>
</dict>
</plist>
PLIST
plutil -lint "$app/Contents/Info.plist"

sign_identity="${SPUTNIK_SIGN_IDENTITY:-}"
if [[ -n "$sign_identity" ]]; then
  codesign --force --sign "$sign_identity" --options runtime --timestamp "$app"
else
  codesign --force --sign - "$app"
fi
codesign --verify --strict --verbose=2 "$app"
if [[ "${SPUTNIK_SMOKE_TEST:-0}" == 1 ]]; then
  "$executable" --smoke-test
fi

ln -s /Applications "$volume/Applications"
filename="Sputnik-$version-macOS-Universal.dmg"
hdiutil create -quiet -fs HFS+ -format UDZO -volname "Sputnik $version" \
  -srcfolder "$volume" "$work/$filename"
if [[ -n "$sign_identity" ]]; then
  codesign --sign "$sign_identity" --timestamp "$work/$filename"
  codesign --verify --strict --verbose=2 "$work/$filename"
fi
hdiutil verify -quiet "$work/$filename"
mv -f "$work/$filename" "$output/$filename"
(
  cd "$output"
  shasum -a 256 "$filename" > "$filename.sha256"
)
printf '%s\n' "$output/$filename"
