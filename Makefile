.PHONY: build test lint publish clean

build:
	dotnet build

test:
	dotnet test

lint:
	dotnet format --verify-no-changes

publish:
	dotnet publish src/TorVps.App -c Release -r win-x64 --self-contained

clean:
	dotnet clean
	rm -rf src/TorVps.Core/bin src/TorVps.Core/obj
	rm -rf src/TorVps.App/bin src/TorVps.App/obj
	rm -rf tests/TorVps.Tests/bin tests/TorVps.Tests/obj
