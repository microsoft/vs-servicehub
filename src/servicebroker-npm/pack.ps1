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

function Invoke-Pnpm {
    Param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & $script:PnpmExecutable @script:PnpmPrefixArguments @Arguments
}

Push-Location $PSScriptRoot
try {
    $packageManager = (Get-Content package.json -Raw | ConvertFrom-Json).packageManager
    $packageManagerName, $packageManagerVersion = $packageManager -split '@', 2
    if ($packageManagerName -ne 'pnpm' -or !$packageManagerVersion) {
        throw "Unsupported package manager '$packageManager'."
    }

    $script:PnpmExecutable = 'pnpm'
    $script:PnpmPrefixArguments = @()
    if (!(Get-Command $script:PnpmExecutable -ErrorAction SilentlyContinue)) {
        $script:PnpmExecutable = 'corepack'
        $script:PnpmPrefixArguments = @('pnpm')
    }

    $actualPnpmVersion = (Invoke-Pnpm --version)
    if ($lastexitcode -ne 0) { throw "Failure while verifying package manager." }
    if ($actualPnpmVersion.Trim() -ne $packageManagerVersion) {
        throw "Expected pnpm $packageManagerVersion but found $($actualPnpmVersion.Trim())."
    }

    if ($Restore) {
        Invoke-Pnpm install --frozen-lockfile
        if ($lastexitcode -ne 0) { throw "Failure while restoring packages." }
    }

    Invoke-Pnpm build # tsc
    if ($lastexitcode -ne 0) { throw "Failure while building the npm package." }

    dotnet build sign.proj
    if ($lastexitcode -ne 0) { throw "Failure while building sign.proj." }

    Invoke-Pnpm exec nbgv-setversion
    if ($lastexitcode -ne 0) { throw "Failure while stamping the npm package version." }

    $Configuration = 'Debug'
    if ($env:BUILDCONFIGURATION) {
        $Configuration = $env:BUILDCONFIGURATION
    }
    $OutDir = "../../bin/Packages/$Configuration/npm"
    if (!(Test-Path $OutDir)) { New-Item $OutDir -ItemType Directory }
    $ExistingTarballs = @(Get-ChildItem -Path $OutDir -Filter *.tgz -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    Invoke-Pnpm pack --pack-destination $OutDir
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

    Invoke-Pnpm exec nbgv-setversion --reset
    if ($lastexitcode -ne 0) { throw "Failure while resetting the stamped npm package version." }
}
finally {
    Pop-Location
}
