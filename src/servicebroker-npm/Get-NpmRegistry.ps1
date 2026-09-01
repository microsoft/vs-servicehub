#!/usr/bin/env pwsh

param(
    [string]$NpmrcPath = (Join-Path $PSScriptRoot '.npmrc')
)

$registryLine = Get-Content $npmrcPath | Where-Object { $_ -match '^\s*registry\s*=' } | Select-Object -First 1
if (!$registryLine) {
    throw "Could not find a registry entry in '$npmrcPath'."
}

($registryLine -replace '^\s*registry\s*=\s*', '').Trim().TrimEnd('/')
