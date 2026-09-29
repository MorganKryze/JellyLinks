#!/usr/bin/env bash
# End-to-end check: create a batch as a user, download it the way JDownloader does
# (HEAD, parallel ranges, a cut, a resume), then read the verdict in the plugin database.
# Usage: JF=http://localhost:8098 JUSER=ami JPASS=… CFG=docker/v10-config bash tests/integration/jdownloader-sim.sh
# On Docker Desktop for Mac the host reaches the container as 192.168.65.1: the IP-limit step needs 192.168.65.0/24 in Known proxies besides 172.16.0.0/12.
# Jellyfin's API speaks PascalCase JSON: fields are read as .Links[0].Url, .Id, .Size.
set -euo pipefail
: "${JF:?}" "${JUSER:?}" "${JPASS:?}" "${CFG:?}"
AUTH='MediaBrowser Client="sim", Device="sim", DeviceId="sim", Version="1"'
work=$(mktemp -d); trap 'rm -rf "$work"' EXIT

login=$(curl -sf -H "Authorization: $AUTH" -H 'Content-Type: application/json' \
  -d "{\"Username\":\"$JUSER\",\"Pw\":\"$JPASS\"}" "$JF/Users/AuthenticateByName")
TOKEN=$(jq -r .AccessToken <<<"$login"); UID_=$(jq -r .User.Id <<<"$login")
H=(-H "Authorization: $AUTH, Token=\"$TOKEN\"" -H 'Content-Type: application/json')

movie=$(curl -sf "${H[@]}" "$JF/Items?userId=$UID_&includeItemTypes=Movie&recursive=true&limit=1" | jq -r '.Items[0].Id')
batch=$(curl -sf "${H[@]}" -d "{\"RootItemIds\":[\"$movie\"],\"AllVersions\":false,\"IncludeSubtitles\":true}" "$JF/JellyLinks/batches")
url=$(jq -r '.Links[0].Url' <<<"$batch"); size=$(jq -r '.Links[0].Size' <<<"$batch"); id=$(jq -r .Id <<<"$batch")
echo "batch $id · $size bytes"

# HEAD: counted nowhere
curl -sfI "$url" | grep -iE '^(HTTP|content-length|accept-ranges)'

# 4 parallel chunks, the 4th cut after ~1 MB
q=$(( size / 4 ))
pids=()
for i in 0 1 2; do
  s=$(( i * q )); e=$(( (i + 1) * q - 1 ))
  curl -sf -r "$s-$e" -o "$work/p$i" "$url" &
  pids+=($!)
done
s=$(( 3 * q ))
curl -s -r "$s-$(( size - 1 ))" --max-filesize 999999999 --limit-rate 2M --max-time 1 -o "$work/p3" "$url" || true
for pid in "${pids[@]}"; do wait "$pid"; done
# resume the 4th chunk where it stopped
got=$(stat -f%z "$work/p3" 2>/dev/null || stat -c%s "$work/p3")
curl -sf -r "$(( s + got ))-$(( size - 1 ))" "$url" >> "$work/p3"

cat "$work"/p0 "$work"/p1 "$work"/p2 "$work"/p3 > "$work/all"
total=$(stat -f%z "$work/all" 2>/dev/null || stat -c%s "$work/all")
echo "reassembled $total / $size"
[ "$total" -eq "$size" ] || { echo "FAIL: reassembled size differs" >&2; exit 1; }

# verdict from the plugin database (a COPY, the server keeps running)
db=$(find "$CFG" -name jellylinks.db | head -1)
cp "$db" "$work/db"; cp "$db-wal" "$work/db-wal" 2>/dev/null || true
IFS='|' read -r n bad bytes < <(sqlite3 "file:$work/db?mode=ro" \
  "SELECT COUNT(*), COALESCE(SUM(status <> 'complete'),0), COALESCE(SUM(bytes_sent),0) FROM sessions s JOIN links l ON l.id=s.link_id WHERE l.batch_id=$id;")
echo "sessions=$n not_complete=$bad bytes_sent=$bytes"
[ "$n" -eq 1 ] && [ "$bad" -eq 0 ] && [ "$bytes" -ge "$size" ] || { echo "FAIL: wrong verdict" >&2; exit 1; }
echo OK
