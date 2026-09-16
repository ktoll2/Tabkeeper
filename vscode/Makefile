.PHONY: help install ci current-version build watch test run package clean

help:
	@echo "Targets:"
	@echo "  install         - npm install dependencies (updates package-lock.json)"
	@echo "  ci              - npm ci; reproducible install from package-lock.json, for CI"
	@echo "  current-version - print package.json's current version"
	@echo "  build           - compile TypeScript to dist/"
	@echo "  watch           - compile in watch mode"
	@echo "  test            - compile and run the extension test suite"
	@echo "  run             - launch an Extension Development Host with this extension loaded"
	@echo "  package         - build a .vsix package with vsce"
	@echo "  clean           - remove build output and packaged artifacts"

install:
	npm install

ci:
	npm ci

current-version:
	@node -p "require('./package.json').version"

build:
	npm run compile

watch:
	npm run watch

test:
	npm test

run: build
	code --extensionDevelopmentPath="$(CURDIR)" .

package: build
	npx --yes @vscode/vsce package --out Tabkeeper.VSCode.vsix

clean:
	rm -rf dist out .vscode-test *.vsix *.tsbuildinfo
