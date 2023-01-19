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

    yarn clean
    if ($lastexitcode -ne 0) { throw }

    yarn build # tsc
    if ($lastexitcode -ne 0) { throw }

    dotnet build # sign
    if ($lastexitcode -ne 0) { throw }

    yarn nbgv-setversion
    yarn prepare-for-pack
    if ($lastexitcode -ne 0) { throw }
    yarn nbgv-setversion --reset
    
    Push-Location "$PSScriptRoot\js\package"

    yarn pack
    if ($lastexitcode -ne 0) { throw }
}
finally {
    Pop-Location
}
