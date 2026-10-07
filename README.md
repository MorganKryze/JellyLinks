# JellyLinks — a Jellyfin plugin

![Jellyfin](https://img.shields.io/badge/Jellyfin-10.11.10%20%7C%2012.0-00a4dc?logo=jellyfin&logoColor=white) ![Version](https://img.shields.io/github/v/release/MorganKryze/JellyLinks?logo=semantic-release&logoColor=white&label=version&color=AA5CC3) ![License](https://img.shields.io/github/license/MorganKryze/JellyLinks?logoColor=white)

Give the people you share your library with direct download links to the original files — a movie, a
season, a whole series — that they paste into JDownloader or any download manager. You keep the keys:
links expire, can be revoked, are capped per address and per quota, and every download is on record.

JellyLinks never downloads anything itself and never transcodes: it serves your files as they are.

![The links dialog](./assets/screenshots/links-dialog.png)

## Features

The interface is in English by default and follows the language set in Jellyfin. French is included; another language is one more object in `Jellyfin.Plugin.JellyLinks/Client/strings.json`, with the same keys.

- **Links button** ("Download links", link icon) on movie, series, season and episode pages, in the "…" menus and in multi-selection.
- **Pick what you need**: default version or all versions, external subtitles, whole seasons or single
  episodes, "unwatched only". Totals update as you tick.
- **Copy the links or download a `.txt`**, one URL per line — straight into your download manager.
- **My links** (« Mes liens »): each batch as a card with its progress; copy again, revoke, or regenerate an expired one.
- **Signed, expiring links** (7 days by default). A renamed file keeps its link working; a deleted one answers `410`.
- **Resumable downloads**: byte ranges, `ETag` and `If-Range`, parallel chunks.
- **Sharing guard**: a batch used from more distinct addresses than allowed (3 by default) is blocked
  until you release it.
- **Quotas** (off by default): volume over a rolling window and active batches, globally or per user.
- **Admin panel** with five tabs: overview (volume per day and per user, top titles and users), batches
  (search by title, user or address; full session detail), activity log, users and quota exceptions, settings.
- **Alerts** to Jellyfin's activity log and to a webhook (ntfy or JSON): batch blocked, new address,
  quota reached, batch fully downloaded.
- **Privacy by default**: no geolocation; session details (including addresses) are kept 90 days, then
  folded into per-month totals without any address.

![My links](./assets/screenshots/my-links.png)

## Prerequisites

1. **Jellyfin 10.11.10+ or 12.0.**
2. **[JavaScript Injector](https://github.com/n00bcodr/Jellyfin-JavaScript-Injector) 4.0+** — it delivers
   the Links button and the My links page (« Mes liens ») to the web UI. JellyLinks registers its script by itself; there
   is nothing to paste. Add the repository matching your server in **Dashboard → Plugins → Repositories**:

   ```plain
   https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/10.11/manifest.json
   https://raw.githubusercontent.com/n00bcodr/jellyfin-plugins/main/12/manifest.json
   ```

   File Transformation is **not** required.

## Installation

Add the JellyLinks repository in **Dashboard → Plugins → Repositories**:

```plain
https://raw.githubusercontent.com/MorganKryze/JellyLinks/main/manifest.json
```

Then install **JellyLinks** from the Catalog and restart Jellyfin.

Only users with **"Allow media downloading"** (user profile → access) see the Links button — that
permission is re-checked on every request.

Uninstalling keeps the plugin's data (database and signing key) in `plugins/Jellyfin.Plugin.JellyLinks/`, so a
reinstall or an update keeps your batches; delete that folder to wipe everything (a reinstall otherwise brings
back the links that have not expired).

## Behind a reverse proxy

- Declare your proxies in **Dashboard → Networking → Known proxies**, otherwise every download seems to
  come from the proxy and the address limit means nothing. Check an entry of the activity log after a
  connection from outside: it must show the client's public address.
- Set **Settings → Public address of the links** (Réglages → « Adresse publique des liens ») (e.g. `https://jellyfin.example`) if the address
  Jellyfin sees is not the one your users reach.

## Admin panel

**Dashboard → Plugins → JellyLinks.**

![Admin overview](./assets/screenshots/admin-overview.png)

| Tab | What you do there |
| --- | --- |
| Overview (Vue d'ensemble) | active batches, volume served and downloads completed (7 days), items to review; daily volume per user (30 days), top titles and users |
| Batches (Lots) | search and filter; open a batch to see each session (time, file, full address, client, duration, received / size, status); unblock, unblock and raise its limit, revoke, copy its links |
| Activity (Activité) | the log of sessions and events, searchable and filterable |
| Users (Utilisateurs) | permission, effective quota, usage, active batches, archived totals; set or remove a quota exception |
| Settings (Réglages) | link validity, address limit, global quota, retention, public address, webhook (with a test button), and **Revoke everything** (« Tout révoquer »; new signing key: every link ever issued stops working) |

## Troubleshooting

| Symptom | Cause |
| --- | --- |
| No Links button | JavaScript Injector missing or inactive (the panel shows a warning), or the user lacks "Allow media downloading" |
| The address limit never triggers; every session shows the same address | the proxy is not in Known proxies: Jellyfin sees the proxy's address instead of the client's |
| `403` on a link | the batch was revoked or blocked, or the user lost the permission or access to the library |
| `410` on a link | the batch expired, or the file is gone (a rename is followed; a deletion is not) |
| `429` on a link | the user's quota is reached for the current window |

## Development

```bash
just test          # server (xUnit) and client (node) tests
just v10           # build and run Jellyfin 10.11.10 on http://localhost:8098
just v12           # same with Jellyfin 12.0 on http://localhost:8097
just jsinjector v10
just package 0.2.0 # the release ZIP, as the CI builds it
```

## Security

Please report vulnerabilities privately through
[GitHub security advisories](https://github.com/MorganKryze/JellyLinks/security/advisories/new).

## Colophon

A colophon tells how the book was made, so here is mine: C#, plain SQL, a few hundred lines of
unbundled JavaScript, and [Claude Code](https://claude.com/claude-code) drafting at my side, never on
autopilot. The taste, the reviews and the final word stay mine; the tests, the CI and the public history
keep me honest.

## License

Free software under [GPL-3.0](LICENSE): use it, modify it, share it. What you redistribute stays under
the same license, source included. Running it on your own server is not distribution and asks nothing of you.
