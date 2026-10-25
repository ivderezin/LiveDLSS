#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
VERSION="$(cat "$ROOT/VERSION")"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Invalid release version: $VERSION" >&2
  exit 1
fi
OUT="$ROOT/dist/LiveDLSS-Windows-x64"
ARCHIVE="$ROOT/dist/LiveDLSS-$VERSION-Windows-x64.zip"
WIN="$ROOT/apps/windows"

if ! ls "$WIN"/runtimes/ffmpeg/avcodec-*.dll >/dev/null 2>&1; then
  echo "FFmpeg not found in apps/windows/runtimes/ffmpeg" >&2
  exit 1
fi

rm -rf "$OUT"
rm -f "$ARCHIVE"
mkdir -p "$OUT"

(cd "$WIN" && "$DOTNET" publish src/App/LiveDLSS.Server -c Release -r win-x64 --self-contained \
  -p:DebugType=none -o "$OUT/LiveDLSSServer")

# NVIDIA model files are supplied by the user, not distributed with the release.
find "$OUT/LiveDLSSServer" -type f -iname 'nvngx_dlssnr.dll' -delete

cp "$ROOT/README.md" "$OUT/README.md"
cp "$ROOT/VERSION" "$OUT/VERSION"

mkdir -p "$OUT/dlss-bridge"
cp -R "$WIN/native/LiveDLSS.Dlss/." "$OUT/dlss-bridge/"
mkdir -p "$OUT/neural-rendering-bridge"
cp -R "$WIN/native/LiveDLSS.NeuralRendering/." "$OUT/neural-rendering-bridge/"

for f in Run-LiveDLSS.bat Allow-Firewall.ps1 Build-DlssBridge.ps1 Build-NeuralRendering.ps1; do
  { [[ "$f" == *.ps1 ]] && printf '\xEF\xBB\xBF'; perl -pe 's/\r?\n/\r\n/' "$ROOT/tools/windows-package/$f"; } > "$OUT/$f"
done

(cd "$ROOT/dist" && zip -qr -9 "$ARCHIVE" LiveDLSS-Windows-x64)
echo "→ $ARCHIVE ($(du -h "$ARCHIVE" | cut -f1))"
