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
    $env:COREPACK_NPM_REGISTRY = 'https://registry.npmjs.org'
    corepack prepare $packageManager --activate
    if ($lastexitcode -ne 0) { throw }
    Remove-Item Env:COREPACK_NPM_REGISTRY -ErrorAction SilentlyContinue

    if ($Restore) {
        corepack pnpm install --frozen-lockfile
        if ($lastexitcode -ne 0) { throw }
    }

    corepack pnpm build # tsc
    if ($lastexitcode -ne 0) { throw }

    dotnet build sign.proj
    if ($lastexitcode -ne 0) { throw }

    corepack pnpm nbgv-setversion
    if ($lastexitcode -ne 0) { throw }

    $Configuration = 'Debug'
    if ($env:BUILDCONFIGURATION) {
        $Configuration = $env:BUILDCONFIGURATION
    }
    $OutDir = "../../bin/Packages/$Configuration/npm"
    if (!(Test-Path $OutDir)) { New-Item $OutDir -ItemType Directory }
    corepack pnpm pack --pack-destination $OutDir
    if ($lastexitcode -ne 0) { throw }

    $Package = Get-Content package.json | ConvertFrom-Json
    $PackedTarball = Get-ChildItem -Path $OutDir -Filter *.tgz | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $ExpectedTarballName = "$($Package.name.Replace('/', '-'))-$($Package.version).tgz"
    if ($PackedTarball -and $PackedTarball.Name -ne $ExpectedTarballName) {
        Move-Item -Path $PackedTarball.FullName -Destination (Join-Path $OutDir $ExpectedTarballName) -Force
    }

    corepack pnpm nbgv-setversion --reset
    if ($lastexitcode -ne 0) { throw }
}
finally {
    Pop-Location
}
