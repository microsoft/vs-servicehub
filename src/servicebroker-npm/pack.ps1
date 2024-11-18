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
        yarn
    }


    yarn build # tsc
    if ($lastexitcode -ne 0) { throw }

    dotnet build # sign
    if ($lastexitcode -ne 0) { throw }

    yarn nbgv-setversion
    if ($lastexitcode -ne 0) { throw }

    $Configuration = 'Debug'
    if ($env:BUILDCONFIGURATION) {
        $Configuration = $env:BUILDCONFIGURATION
    }
    $OutDir = "../../bin/Packages/$Configuration/npm"
    if (!(Test-Path $OutDir)) { New-Item $OutDir -ItemType Directory }
    yarn pack --out $OutDir/%s-%v.tgz
    if ($lastexitcode -ne 0) { throw }

    yarn nbgv-setversion --reset
}
finally {
    Pop-Location
}
