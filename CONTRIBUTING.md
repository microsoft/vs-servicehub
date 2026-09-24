# Contributing

This project has adopted the [Microsoft Open Source Code of
Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct
FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com)
with any additional questions or comments.

## Best practices

* Use Windows PowerShell or [PowerShell Core][pwsh] (including on Linux/OSX) to run .ps1 scripts.
  Some scripts set environment variables to help you, but they are only retained if you use PowerShell as your shell.

## Prerequisites

All dependencies can be installed by running the `init.ps1` script at the root of the repository
using Windows PowerShell or [PowerShell Core][pwsh] (on any OS).
Some dependencies installed by `init.ps1` may only be discoverable from the same command line environment the init script was run from due to environment variables, so be sure to launch Visual Studio or build the repo from that same environment.
Alternatively, run `init.ps1 -InstallLocality Machine` (which may require elevation) in order to install dependencies at machine-wide locations so Visual Studio and builds work everywhere.

The only prerequisite for building, testing, and deploying from this repository
is the [.NET SDK](https://get.dot.net/).
You should install the version specified in `global.json` or a later version within
the same major.minor.Bxx "hundreds" band.
For example if 2.2.300 is specified, you may install 2.2.300, 2.2.301, or 2.2.310
while the 2.2.400 version would not be considered compatible by .NET SDK.
See [.NET Core Versioning](https://learn.microsoft.com/dotnet/core/versions/) for more information.

## Package restore

### NuGet restore

The easiest way to restore packages may be to run `init.ps1` which automatically authenticates
to the feeds that packages for this repo come from, if any.
`dotnet restore` or `nuget restore` also work but may require extra steps to authenticate to any applicable feeds.

### NPM install

The NPM package built from this repo restores its public dependencies from the Azure Artifacts public feed.
`init.ps1` will install these dependencies automatically.

Then use the checked-in install script from the repo root:

```ps1
pnpm --dir src/servicebroker-npm run auth-install
```

The root `init.ps1` script installs the pnpm version pinned by this repo's `packageManager` field.

#### NPM/pnpm Maintenance

To update the pinned pnpm version and regenerate the lockfile, run:

```ps1
Push-Location src/servicebroker-npm
corepack use pnpm@latest
corepack pnpm install
Pop-Location
```

## Building

This repository can be built on Windows, Linux, and OSX.

### .NET code

Building, testing, and packing the .NET code in this repository can be done by using the standard dotnet CLI commands (e.g. `dotnet build`, `dotnet test`, `dotnet pack`, etc.).

### Typescript code

* Build: `corepack pnpm --dir src/servicebroker-npm build`
* Test: `corepack pnpm --dir src/servicebroker-npm test`
* Pack: `pack.ps1`

For a good language service experience in VS Code, select the workspace TypeScript version from `node_modules`.

## Testing

You can use `dotnet test` to build and/or test the repo.

There may be tests that are known to be unstable or have special requirements. These can be avoided by running tests using the [dotnet-test-cloud.ps1](tools/dotnet-test-cloud.ps1) script *after* running `dotnet build`.

### Native and IPC regressions

Before changing a P/Invoke declaration, locate the owning declaration and its callers, and verify the native signature, calling convention, structure layout, and pointer/value semantics.
For example, the Windows pipe client declaration is `AsyncNamedPipeClientStream.CreateNamedPipeClient` in [AsyncNamedPipeClientStream.cs](src/Microsoft.ServiceHub.Framework/AsyncNamedPipeClientStream.cs); [ServerFactory.ConnectAsync](src/Microsoft.ServiceHub.Framework/ServerFactory.cs) selects different implementations on Windows and other operating systems.
Record the actual runner OS, target framework, runtime, and test process architecture, not just the machine architecture or build configuration.
A green x64 run does not establish x86 ABI compatibility, and a non-Windows pipe test does not exercise the Windows P/Invoke.

A native regression needs a test that crosses the real native boundary: demonstrate that it fails with the old implementation and passes with the corrected implementation on the failing architecture.
Keep the test and environment otherwise unchanged, and confirm that the test executed rather than being skipped.
Mocked calls, signature inspection, and managed RPC tests alone do not establish native compatibility.

#### Architecture-specific execution

Follow the build prerequisites above and the agent bootstrap instructions in [AGENTS.md](AGENTS.md) when applicable.
The [test runner configuration](global.json) uses Microsoft.Testing.Platform; use the runner filters documented in [AGENTS.md](AGENTS.md#running-tests), not VSTest `--filter` expressions.
For example, pass `-- --filter-not-trait "TestCategory=FailsInCloudTest"` to exclude unstable tests, matching the cloud-test script.
Choose a framework from the test project's current `TargetFrameworks`: [Microsoft.ServiceHub.Framework.Tests.csproj](test/Microsoft.ServiceHub.Framework.Tests/Microsoft.ServiceHub.Framework.Tests.csproj) targets `net8.0` and additionally `net472` on Windows.

After building the matching configuration, the existing cloud-test script supports this Windows x86 invocation from the repository root:

```ps1
./tools/dotnet-test-cloud.ps1 -Configuration Release -x86
```

The script locates a 32-bit `dotnet.exe`; use its `-dotnet32` parameter to supply an explicit path to an already installed 32-bit SDK when necessary.
Check the launched test process architecture as well: selecting a CLI executable or labeling a result "x86" is not evidence that the test host ran as x86.
The script runs with `--no-build`, excludes `TestCategory=FailsInCloudTest` (and `WindowsOnly=true` on non-Windows systems), and collects TRX results, diagnostics, and hang/crash dumps.
Its architecture parameters belong to the PowerShell script, not to `dotnet test`.
Do not infer architecture coverage from the normal [CI invocation](azure-pipelines/dotnet.yml), which does not pass `-x86`.

#### Isolate connection failures

Separate a native/transport connection smoke test from higher-level RPC and callback assertions.
Use [ServerFactoryTests.TestConnection](test/Microsoft.ServiceHub.Framework.Tests/ServerFactoryTests.cs) as existing transport-level prior art, then test RPC behavior after independently verifying connection establishment.
Preserve the first native error before cleanup or subsequent native calls can overwrite it; `AsyncNamedPipeClientStream.TryConnect` already reads `Marshal.GetLastWin32Error()` before disposing an invalid handle.
Distinguish that error from later cancellation or timeout instead of treating the final exception as the original cause.
Do not mask a deterministic failure with retries or longer timeouts.

Use existing diagnostics where appropriate: `ServerFactory.ClientOptions.FailFast` limits connection retries, and `ServerAlreadyListening` bounds retries for a missing pipe when the server is known to have started.
`AsyncNamedPipeClientStream.ConnectAsync` includes native error counts in its timeout exception; cancellation can take a different path, so those counts are not a substitute for capturing the initial error.
Existing tests use `TestBase.CreateTestTraceSource` and bounded cancellation tokens in [TestBase.cs](test/Microsoft.ServiceHub.Framework.Tests/TestBase.cs).

#### Retain reproducible evidence

Keep a complete, immutable artifact set for each before/after run: source revision, package identities, relevant manifests, tested binaries and symbols, runner configuration, results, and diagnostic attachments.
Do not mix a manifest or package from one build with binaries from another, or overwrite the failing run's evidence with the passing run.
The existing [test artifact collector](tools/artifacts/testResults.ps1) retains test logs and dump attachments; preserve the matching binaries and build identities alongside them.
Unit and JIT-based RPC test results are distinct from compatibility evidence for a published package or native host: exercise the actual deployment form when making that claim.
Keep exact build-specific evidence outside reusable guidance, and include only public-safe, appropriately sanitized evidence in public issues or pull requests.

## Releases

Use `nbgv tag` to create a tag for a particular commit that you mean to release.
[Learn more about `nbgv` and its `tag` and `prepare-release` commands](https://dotnet.github.io/Nerdbank.GitVersioning/docs/nbgv-cli.html).

Push the tag.

### Azure Pipelines

When your repo builds with Azure Pipelines, use the `azure-pipelines/release.yml` pipeline.
Trigger the pipeline by adding the `auto-release` tag on a run of your main `azure-pipelines.yml` pipeline.

## Tutorial and API documentation

API and hand-written docs are found under the `docfx/` directory and are built by [docfx](https://dotnet.github.io/docfx/).

You can make changes and host the site locally to preview them by switching to that directory and running the `dotnet docfx --serve` command.
After making a change, you can rebuild the docs site while the localhost server is running by running `dotnet docfx` again from a separate terminal.

The `.github/workflows/docs.yml` GitHub Actions workflow publishes the content of these docs to github.io if the workflow itself and [GitHub Pages is enabled for your repository](https://docs.github.com/en/pages/quickstart).

## Updating dependencies

This repo uses Renovate to keep dependencies current.
Configuration is in the `.github/renovate.json` file.
[Learn more about configuring Renovate](https://docs.renovatebot.com/configuration-options/).

When changing the renovate.json file, follow [these validation steps](https://docs.renovatebot.com/config-validation/).

If Renovate is not creating pull requests when you expect it to, check that the [Renovate GitHub App](https://github.com/apps/renovate) is configured for your account or repo.

## Merging latest from Library.Template

### Maintaining your repo based on this template

The best way to keep your repo in sync with Library.Template's evolving features and best practices is to periodically merge the template into your repo:

```ps1
git fetch
git checkout origin/main
./tools/MergeFrom-Template.ps1
# resolve any conflicts, then commit the merge commit.
git push origin -u HEAD
```

[pwsh]: https://learn.microsoft.com/powershell/scripting/install/installing-powershell
