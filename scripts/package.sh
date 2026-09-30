#!/usr/bin/env bash
# Builds the plugin ZIP (DLL + meta.json + icon) for a release.
# Usage: scripts/package.sh 0.2.0 [targetAbi]   → dist/Jellyfin.Plugin.JellyLinks.zip
set -euo pipefail
version="${1:?usage: scripts/package.sh <version, e.g. 0.2.0> [targetAbi]}"
abi="${2:-10.11.10.0}"
full="${version}.0"
root="$(cd "$(dirname "$0")/.." && pwd)"
dist="$root/dist"
rm -rf "$dist"
mkdir -p "$dist/stage"

# Everything but the final version=/checksum= lines goes to stderr: the CI appends stdout to $GITHUB_OUTPUT.
dotnet build "$root/Jellyfin.Plugin.JellyLinks/JellyLinks.csproj" --configuration Release --no-incremental --nologo -v q \
  -p:Version="$full" -p:AssemblyVersion="$full" -p:FileVersion="$full" >&2
cp "$root/Jellyfin.Plugin.JellyLinks/bin/Release/net9.0/Jellyfin.Plugin.JellyLinks.dll" "$dist/stage/" >&2
cp "$root/assets/icon.png" "$dist/stage/" >&2

# Jellyfin reads these keys case-sensitively (camelCase): PascalCase ones are silently ignored.
jq -n --arg ver "$full" --arg abi "$abi" --arg ts "$(date -u +%Y-%m-%dT%H:%M:%S)" '{
  guid: "5b0f6e0c-2d4b-4f5e-9a57-3c1d8e7a4b21",
  name: "JellyLinks",
  version: $ver,
  timestamp: $ts,
  targetAbi: $abi,
  changelog: "",
  autoUpdate: true,
  imagePath: "icon.png",
  assemblies: ["Jellyfin.Plugin.JellyLinks.dll"]
}' > "$dist/stage/meta.json"

(cd "$dist/stage" && zip -q -X "$dist/Jellyfin.Plugin.JellyLinks.zip" Jellyfin.Plugin.JellyLinks.dll meta.json icon.png) >&2
checksum="$( (md5sum "$dist/Jellyfin.Plugin.JellyLinks.zip" 2>/dev/null || md5 -r "$dist/Jellyfin.Plugin.JellyLinks.zip") | awk '{print toupper($1)}')"
echo "version=$full"
echo "checksum=$checksum"
