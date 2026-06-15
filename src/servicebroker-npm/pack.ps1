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
    $npmRegistry = & "$PSScriptRoot/Get-NpmRegistry.ps1"
    try {
        $env:COREPACK_NPM_REGISTRY = $npmRegistry
        corepack prepare $packageManager --activate
        if ($lastexitcode -ne 0) { throw "Failure while preparing package manager." }
    }
    finally {
        Remove-Item Env:COREPACK_NPM_REGISTRY -ErrorAction SilentlyContinue
    }

    if ($Restore) {
        corepack pnpm run auth-install
        if ($lastexitcode -ne 0) { throw "Failure while restoring packages." }
    }

    corepack pnpm build # tsc
    if ($lastexitcode -ne 0) { throw "Failure while building the npm package." }

    dotnet build sign.proj
    if ($lastexitcode -ne 0) { throw "Failure while building sign.proj." }

    corepack pnpm exec nbgv-setversion
    if ($lastexitcode -ne 0) { throw "Failure while stamping the npm package version." }

    $Configuration = 'Debug'
    if ($env:BUILDCONFIGURATION) {
        $Configuration = $env:BUILDCONFIGURATION
    }
    $OutDir = "../../bin/Packages/$Configuration/npm"
    if (!(Test-Path $OutDir)) { New-Item $OutDir -ItemType Directory }
    $ExistingTarballs = @(Get-ChildItem -Path $OutDir -Filter *.tgz -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    corepack pnpm pack --pack-destination $OutDir
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

    corepack pnpm exec nbgv-setversion --reset
    if ($lastexitcode -ne 0) { throw "Failure while resetting the stamped npm package version." }
}
finally {
    Pop-Location
}
