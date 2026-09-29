.PHONY: build test v10 v12 down logs10 logs12

DLL := Jellyfin.Plugin.JellyLinks/bin/Release/net9.0/Jellyfin.Plugin.JellyLinks.dll

build:
	dotnet build --configuration Release

test:
	dotnet test

## build, drop the DLL into a test server, (re)start it
v10: build
	mkdir -p docker/v10-config/plugins/JellyLinks
	cp $(DLL) docker/v10-config/plugins/JellyLinks/
	docker compose -f docker/compose.v10.yml up -d --force-recreate

v12: build
	mkdir -p docker/v12-config/plugins/JellyLinks
	cp $(DLL) docker/v12-config/plugins/JellyLinks/
	docker compose -f docker/compose.v12.yml up -d --force-recreate

down:
	docker compose -f docker/compose.v10.yml down; docker compose -f docker/compose.v12.yml down

logs10:
	docker logs -f jellylinks-v10
logs12:
	docker logs -f jellylinks-v12
