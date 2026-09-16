.PHONY: help restore current-version stamp-version build test package clean

help:
	@echo "Targets:"
	@echo "  restore         - dotnet restore"
	@echo "  current-version - print the current version (from the repo-root VERSION file); also"
	@echo "                    stamps the VSIX manifest to match, so it never needs hand-editing"
	@echo "  stamp-version   - set the version (make stamp-version VERSION=1.2.3); updates VERSION"
	@echo "                    and the VSIX manifest together"
	@echo "  build           - build Tabkeeper.slnx (Release), stamped to VERSION's value"
	@echo "  test            - build and run Tabkeeper.Core.Tests"
	@echo "  package         - build the .vsix (same as build; VSIX packaging runs as part of the build)"
	@echo "  clean           - remove build output"

restore:
	dotnet restore Tabkeeper.slnx

current-version:
	@pwsh -NoProfile -File scripts/version.ps1

stamp-version:
ifndef VERSION
	$(error VERSION is required, e.g. make stamp-version VERSION=1.2.3)
endif
	pwsh -NoProfile -File scripts/version.ps1 -Version $(VERSION)

build: restore current-version
	dotnet build Tabkeeper.slnx --configuration Release --no-restore

test: build
	dotnet test Tabkeeper.Core.Tests/Tabkeeper.Core.Tests.csproj --configuration Release --no-build --no-restore

package: build
	@echo "VSIX at $$(find Tabkeeper.Vsix/bin/Release -name '*.vsix' | head -n1)"

clean:
	dotnet clean Tabkeeper.slnx
	find . -type d \( -iname bin -o -iname obj \) -prune -exec rm -rf {} +
