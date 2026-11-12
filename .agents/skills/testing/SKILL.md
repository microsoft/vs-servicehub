---
name: testing
description: Provides guidance and instructions for writing, running and filtering tests.
user-invocable: false
---

**IMPORTANT**: This repository uses xUnit v3 with Microsoft.Testing.Platform (MTP v2). Do not use TUnit-specific commands or VSTest filtering options.

Tests use xUnit v3 and Microsoft.Testing.Platform (MTP) v2. The cloud test script applies repository filters and handles platform-specific test exclusions.

## Running Tests

There *may* be unstable tests that are attributed as such.
After building the tests, running the [dotnet-test-cloud.ps1](../../../tools/dotnet-test-cloud.ps1) script executes the previously built tests except those marked unstable. The commands below do not automatically exclude unstable tests.

**Run the previously built managed tests** (from the repository root):
```bash
./tools/dotnet-test-cloud.ps1 -Configuration Release
```

**Build and run all managed and NativeAOT tests** (from the repository root):
```bash
dotnet publish test -c Release
./tools/dotnet-test-cloud.ps1 -Configuration Release -IncludeNativeAOT
```

**Run a specific test project** (from the repository root):
```bash
dotnet test test/Microsoft.ServiceHub.Framework.Tests/Microsoft.ServiceHub.Framework.Tests.csproj --no-build -c Release
```

From that project's directory, the equivalent command is:
```bash
dotnet test --no-build -c Release
```

To target one framework, use a target supported by the project (for example, `net10.0` for `test/Microsoft.ServiceHub.Framework.Tests/Microsoft.ServiceHub.Framework.Tests.csproj`):
```bash
dotnet test test/Microsoft.ServiceHub.Framework.Tests/Microsoft.ServiceHub.Framework.Tests.csproj --no-build -c Release --framework net10.0
```

Options after `--` are passed to the xUnit v3 MTP runner. For runner options such as selecting a test case, inspect its help:
```bash
dotnet test test/Microsoft.ServiceHub.Framework.Tests/Microsoft.ServiceHub.Framework.Tests.csproj --no-build -c Release -- --help
```

**Exclude tests marked unstable**:
```bash
dotnet test test/Microsoft.ServiceHub.Framework.Tests/Microsoft.ServiceHub.Framework.Tests.csproj --no-build -c Release -- --filter-not-trait TestCategory=FailsInCloudTest
```

The repository's cloud test script also excludes tests with `WindowsOnly=true` on Linux and macOS. MTP runner filters such as `--filter-not-trait` are passed after `--`; VSTest `--filter` expressions and TUnit's `--treenode-filter` are not used by this xUnit repository.
