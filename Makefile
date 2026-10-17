.PHONY: all build clean contracts format

all: build

build:
	dotnet build AirPlatform.slnx

contracts:
	dotnet run --project src/Air.UiContracts -- air-ui/src/core/generated

format:
	dotnet format --verbosity quiet

clean:
	find . -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} +
