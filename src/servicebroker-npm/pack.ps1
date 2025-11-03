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
    if ($Restore) {
        node .yarn/releases/yarn-4.5.0.cjs
    }

    node .yarn/releases/yarn-4.5.0.cjs build # tsc
    if ($lastexitcode -ne 0) { throw }

    dotnet build # sign
    if ($lastexitcode -ne 0) { throw }

    node .yarn/releases/yarn-4.5.0.cjs nbgv-setversion
    if ($lastexitcode -ne 0) { throw }

    $Configuration = 'Debug'
    if ($env:BUILDCONFIGURATION) {
        $Configuration = $env:BUILDCONFIGURATION
    }
    $OutDir = "../../bin/Packages/$Configuration/npm"
    if (!(Test-Path $OutDir)) { New-Item $OutDir -ItemType Directory }
    node .yarn/releases/yarn-4.5.0.cjs pack --out $OutDir/%s-%v.tgz
    if ($lastexitcode -ne 0) { throw }

    node .yarn/releases/yarn-4.5.0.cjs nbgv-setversion --reset
}
finally {
    Pop-Location
}
