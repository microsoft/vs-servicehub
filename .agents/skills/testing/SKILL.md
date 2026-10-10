---
name: testing
description: Provides guidance and instructions for writing, running and filtering tests.
user-invocable: false
---

**IMPORTANT**: This repository uses xUnit v3 with Microsoft.Testing.Platform (MTP v2). Do not use TUnit-specific commands or VSTest filtering options.

Tests use xUnit v3 and MTP v2. The cloud test script applies repository filters and handles platform-specific test exclusions.

## Running Tests

**Run all tests with the repository's CI filters**:
```bash
./tools/dotnet-test-cloud.ps1 -Configuration Release
```

**Run tests for a specific test project**:
```bash
dotnet test test/Project.Tests/Project.Tests.csproj --no-build -c Release
```

For runner options such as selecting a test case, inspect the xUnit v3 MTP runner help:
```bash
dotnet test test/Project.Tests/Project.Tests.csproj --no-build -c Release -- --help
```

Options after `--` are passed to the test runner. The repository's CI filters excluded tests with xUnit v3 options such as `--filter-not-trait TestCategory=FailsInCloudTest`.
