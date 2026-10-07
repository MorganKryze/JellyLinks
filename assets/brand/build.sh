#!/usr/bin/env bash
# Rebuilds the JellyLinks images from their sources: compose the SVGs, then render them.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
assets="$here/.."
python3 "$here/brand.py"
rsvg-convert "$here/catalog.svg" -o "$assets/icon.png"
rsvg-convert "$here/banner.svg" -o "$assets/banner.png"
rsvg-convert "$here/social.svg" -o "$assets/social-preview.png"
rsvg-convert -w 512 -h 512 "$here/mark.svg" -o "$assets/mark.png"
cp "$here/mark.svg" "$assets/mark.svg"
