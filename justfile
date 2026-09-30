dll := "Jellyfin.Plugin.JellyLinks/bin/Release/net9.0/Jellyfin.Plugin.JellyLinks.dll"

# List recipes
default:
	@just --list

# Build the plugin
build:
	dotnet build --configuration Release --no-incremental

# Run server and client tests
test:
	dotnet test
	TZ=UTC node --test tests/client/*.test.js

# Build, deploy to v10, and start
v10: build
	mkdir -p docker/v10-config/plugins/JellyLinks
	mkdir -p docker/media
	mkdir -p docker/shows
	cp {{dll}} docker/v10-config/plugins/JellyLinks/
	docker compose -f docker/compose.v10.yml up -d --force-recreate

# Build, deploy to v12, and start
v12: build
	mkdir -p docker/v12-config/plugins/JellyLinks
	mkdir -p docker/media
	mkdir -p docker/shows
	cp {{dll}} docker/v12-config/plugins/JellyLinks/
	docker compose -f docker/compose.v12.yml up -d --force-recreate

# Stop all containers
down:
	-docker compose -f docker/compose.v10.yml down
	-docker compose -f docker/compose.v12.yml down

# Follow v10 logs
logs10:
	docker logs -f jellylinks-v10

# Follow v12 logs
logs12:
	docker logs -f jellylinks-v12

# Install JavaScript Injector 4.0.0.0 into a test server (v10 or v12), then restart it with `just v10` / `just v12`
jsinjector v:
	#!/usr/bin/env bash
	set -euo pipefail
	abi={{ if v == "v10" { "10.11.0" } else { "12.0.0" } }}
	dir="docker/{{v}}-config/plugins/JavaScript Injector_4.0.0.0"
	mkdir -p "$dir"
	curl -sfL -o "$dir/jsi.zip" "https://github.com/n00bcodr/Jellyfin-JavaScript-Injector/releases/download/4.0.0.0/Jellyfin.Plugin.JavaScriptInjector_${abi}.zip"
	unzip -qo "$dir/jsi.zip" -d "$dir" && rm "$dir/jsi.zip"

# Build the release ZIP locally (same script as the CI)
package version:
	scripts/package.sh {{version}}
