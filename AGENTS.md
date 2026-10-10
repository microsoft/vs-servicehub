# Copilot instructions for this repository

## High level guidance

* Review the `CONTRIBUTING.md` file for instructions to build and test the software.
* Run the `.github/Prime-ForCopilot.ps1` script (once) before running any `dotnet` or `msbuild` commands.
  If you see any build errors about not finding git objects or a shallow clone, it may be time to run this script again.

## npm package registry

* For restores/installs and tool bootstrap in this repo, always use the registry in `src/servicebroker-npm/.npmrc` (do not switch to npmjs.org). Publishing to npmjs.org is handled separately by the release pipelines.
* Refresh the Azure Artifacts npm credential before accessing the registry:
  ```powershell
  cd src/servicebroker-npm
  artifacts-npm-credprovider -f -c .\.npmrc
  ```
* The registry exposes packages only after a seven-day delay from their npmjs.org publication. If an install cannot find a requested version, use the newest version available from this registry that was published at least seven days ago.

## Software Design

* Design APIs to be highly testable, and all functionality should be tested.
* Avoid introducing binary breaking changes in public APIs of projects under `src` unless their project files have `IsPackable` set to `false`.

## Coding style

* Honor StyleCop rules and fix any reported build warnings *after* getting tests to pass.
* In C# files, use namespace *statements* instead of namespace *blocks* for all new files.
* Add API doc comments to all new public and internal members in shipping code under `src`. Tests and samples do not require XML API documentation; do not add XML docs to test or sample members solely to satisfy this rule.
