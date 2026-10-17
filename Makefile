.PHONY: all build clean contracts format prepare reset

VOLUME ?= default

all: build

build:
	dotnet build AirPlatform.slnx

prepare:
	dotnet run --project demo/src/Demo.DevMigrations -- prepare --volume $(VOLUME)

reset:
	dotnet run --project demo/src/Demo.DevMigrations -- reset --volume $(VOLUME)

contracts:
	dotnet run --project src/Air.UiContracts -- air-ui/src/core/generated

format:
	dotnet format --verbosity quiet

clean:
	find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} +
