---
name: testing
description: Provides guidance and instructions for writing, running and filtering tests.
user-invocable: false
---

**IMPORTANT**: This repository uses xUnit v3 with Microsoft.Testing.Platform (MTP v2). Do not use TUnit-specific commands or VSTest filtering options.

Tests use xUnit v3 and MTP v2. The cloud test script applies repository filters and handles platform-specific test exclusions.

## Running Tests

There *may* be unstable tests that are attributed as such.
Running the [dotnet-test-cloud.ps1](../../../tools/dotnet-test-cloud.ps1) script will execute all previously built tests except the unstable ones.
The commands below will *not* automatically exclude unstable tests, but you may do so by adding a parameter to filter out those that set their `TestCategory` property to `FailsInCloudTest`.

**Run all tests** (from the root of the repository):
```bash
./tools/dotnet-test-cloud.ps1 -Configuration Release
```

**Run tests for a specific test project**:
```bash
dotnet test --no-build -c Release
```

**Run tests for a specific test project** (within that test project's directory):
```bash
dotnet test --no-build -c Release
```

For runner options such as selecting a test case, inspect the xUnit v3 MTP runner help:
```bash
dotnet test --no-build -c Release -- --help
```

Options after `--` are passed to the test runner. The repository's CI filters excluded tests with xUnit v3 options such as `--filter-not-trait TestCategory=FailsInCloudTest`.
