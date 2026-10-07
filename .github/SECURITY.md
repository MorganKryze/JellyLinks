# Security Policy

## Supported versions

JellyLinks is maintained on its latest release, tested against Jellyfin 10.11.10 and 12.0. Fixes ship as a new release;
older versions do not get backports.

## Reporting a vulnerability

Please **do not open a public issue** for a security problem.

Report it privately through GitHub: **Security → Report a vulnerability** on this repository. Include what you found, how
to reproduce it, and what an attacker could do with it. You will get an answer within a week.

## What JellyLinks protects, and how

- **Links are bearer tokens.** Anyone holding a link can download its file until the link expires or is revoked. Links are
  signed with HMAC-SHA256, expire (7 days by default), are checked on every request against the user's download
  permission and library access, and a batch used from more distinct addresses than allowed (3 by default) is blocked
  until an administrator releases it.
- **The signing key** lives in `signing.key` in the plugin's data folder (owner-only permissions). It is never returned by
  any API, the plugin's or Jellyfin's. **Revoke everything** in the admin panel renews it: every link ever issued stops
  working.
- **Addresses are recorded** for each download session, to enforce the address limit and show administrators who
  downloaded what. Session details and events are kept for the retention period (90 days by default), then folded into
  monthly totals without any address. Only administrators can see them.
- **Nothing leaves the server** except the alerts sent to the webhook, when one is configured.

## Endpoints that need no Jellyfin session

- `GET /JellyLinks/f/…`: the signed file links themselves. Without a valid, unexpired signature they answer `404`,
  `403` or `410` and serve nothing.
- `GET /JellyLinks/client/core.js`: the static client code and its texts, for the admin page. It is the same code every
  browser already receives, and it holds no secret.

Everything else requires a signed-in Jellyfin user; the admin API requires an administrator.

## Trust chain

JellyLinks delivers its web interface through **JavaScript Injector** (n00bcodr). That script runs with the same
privileges as the rest of the Jellyfin web app, like every injected script. Its integrity depends on this repository and
its release process, the GitHub account that publishes releases, and JavaScript Injector itself.

Jellyfin does not sign plugins. The MD5 checksum in `manifest.json` detects a corrupted or altered download; it does not
prove who built it. GitHub Actions in this repository are pinned to full commit SHAs.

## Out of scope

- Issues in Jellyfin itself or in JavaScript Injector.
- Someone who already has a valid link downloading its file before it expires: that is what a link is for. Revoke it.
- Attacks that require Jellyfin administrator credentials or physical access to the server.
