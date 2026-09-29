dll := "Jellyfin.Plugin.JellyLinks/bin/Release/net9.0/Jellyfin.Plugin.JellyLinks.dll"

# List recipes
default:
	@just --list

# Build the plugin
build:
	dotnet build --configuration Release

# Run tests
test:
	dotnet test

# Build, deploy to v10, and start
v10: build
	mkdir -p docker/v10-config/plugins/JellyLinks
	mkdir -p docker/media
	cp {{dll}} docker/v10-config/plugins/JellyLinks/
	docker compose -f docker/compose.v10.yml up -d --force-recreate

# Build, deploy to v12, and start
v12: build
	mkdir -p docker/v12-config/plugins/JellyLinks
	mkdir -p docker/media
	cp {{dll}} docker/v12-config/plugins/JellyLinks/
	docker compose -f docker/compose.v12.yml up -d --force-recreate

# Stop all containers
down:
	docker compose -f docker/compose.v10.yml down
	docker compose -f docker/compose.v12.yml down

# Follow v10 logs
logs10:
	docker logs -f jellylinks-v10

# Follow v12 logs
logs12:
	docker logs -f jellylinks-v12
