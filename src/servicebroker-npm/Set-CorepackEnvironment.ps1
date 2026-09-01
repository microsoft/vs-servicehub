#!/usr/bin/env pwsh

param(
    [string]$NpmrcPath = (Join-Path $PSScriptRoot '.npmrc')
)

$registry = & (Join-Path $PSScriptRoot 'Get-NpmRegistry.ps1') -NpmrcPath $NpmrcPath
$env:COREPACK_NPM_REGISTRY = $registry

Remove-Item Env:COREPACK_NPM_TOKEN -ErrorAction SilentlyContinue
Remove-Item Env:COREPACK_NPM_USERNAME -ErrorAction SilentlyContinue
Remove-Item Env:COREPACK_NPM_PASSWORD -ErrorAction SilentlyContinue

$registryUri = [Uri]$registry
$registryScope = "$($registryUri.Authority)$($registryUri.AbsolutePath.TrimEnd('/'))"

$authEntries = foreach ($line in Get-Content $NpmrcPath) {
    if ($line -match '^\s*//(?<scope>[^:]+):(?<key>_authToken|username|_password)\s*=\s*(?<value>.+?)\s*$') {
        [pscustomobject]@{
            Scope = $matches.scope.TrimEnd('/')
            Key = $matches.key
            Value = $matches.value
        }
    }
}

$matchingEntries = $authEntries |
    Where-Object { $registryScope.StartsWith($_.Scope, [StringComparison]::OrdinalIgnoreCase) } |
    Sort-Object { $_.Scope.Length } -Descending

$tokenEntry = $matchingEntries | Where-Object Key -eq '_authToken' | Select-Object -First 1
if ($tokenEntry) {
    $env:COREPACK_NPM_TOKEN = $tokenEntry.Value
}
else {
    $usernameEntry = $matchingEntries | Where-Object Key -eq 'username' | Select-Object -First 1
    $passwordEntry = $matchingEntries | Where-Object Key -eq '_password' | Select-Object -First 1
    if ($usernameEntry -and $passwordEntry) {
        try {
            $decodedPassword = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($passwordEntry.Value))
        }
        catch {
            $decodedPassword = $passwordEntry.Value
        }

        $env:COREPACK_NPM_USERNAME = $usernameEntry.Value
        $env:COREPACK_NPM_PASSWORD = $decodedPassword
    }
}
