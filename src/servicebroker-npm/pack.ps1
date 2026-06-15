#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Build, sign, stamp version, and pack this NPM package.
.PARAMETER Restore
    Restore packages before building
#>
Param(
    [Parameter()]
    [switch]$Restore
)

Push-Location $PSScriptRoot
try {
    $packageManager = (Get-Content package.json -Raw | ConvertFrom-Json).packageManager
    $packageManagerName, $packageManagerVersion = $packageManager -split '@', 2
    if ($packageManagerName -ne 'pnpm' -or !$packageManagerVersion) {
        throw "Unsupported package manager '$packageManager'."
    }

    $actualPnpmVersion = (pnpm --version)
    if ($lastexitcode -ne 0) { throw "Failure while verifying package manager." }
    if ($actualPnpmVersion.Trim() -ne $packageManagerVersion) {
        throw "Expected pnpm $packageManagerVersion but found $($actualPnpmVersion.Trim())."
    }

    if ($Restore) {
        pnpm install --frozen-lockfile
        if ($lastexitcode -ne 0) { throw "Failure while restoring packages." }
    }

    pnpm build # tsc
    if ($lastexitcode -ne 0) { throw "Failure while building the npm package." }

    dotnet build sign.proj
    if ($lastexitcode -ne 0) { throw "Failure while building sign.proj." }

    pnpm exec nbgv-setversion
    if ($lastexitcode -ne 0) { throw "Failure while stamping the npm package version." }

    $Configuration = 'Debug'
    if ($env:BUILDCONFIGURATION) {
        $Configuration = $env:BUILDCONFIGURATION
    }
    $OutDir = "../../bin/Packages/$Configuration/npm"
    if (!(Test-Path $OutDir)) { New-Item $OutDir -ItemType Directory }
    $ExistingTarballs = @(Get-ChildItem -Path $OutDir -Filter *.tgz -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    pnpm pack --pack-destination $OutDir
    if ($lastexitcode -ne 0) { throw "Failure while packing the npm package." }

    $Package = Get-Content package.json -Raw | ConvertFrom-Json
    $PackedTarball = Get-ChildItem -Path $OutDir -Filter *.tgz | Where-Object { $_.FullName -notin $ExistingTarballs } | Select-Object -First 1
    if (!$PackedTarball) {
        throw "pnpm pack did not produce a tarball in '$OutDir'."
    }

    $ExpectedTarballName = "$($Package.name.TrimStart('@').Replace('/', '-'))-$($Package.version).tgz"
    if ($PackedTarball.Name -ne $ExpectedTarballName) {
        Move-Item -Path $PackedTarball.FullName -Destination (Join-Path $OutDir $ExpectedTarballName) -Force
    }

    pnpm exec nbgv-setversion --reset
    if ($lastexitcode -ne 0) { throw "Failure while resetting the stamped npm package version." }
}
finally {
    Pop-Location
}
