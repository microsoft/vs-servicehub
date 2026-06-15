& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
if ($lastexitcode -ne 0) { throw "Failure while building ServiceBrokerTest." }
$packageManager = (Get-Content "$PSScriptRoot/package.json" -Raw | ConvertFrom-Json).packageManager
try {
    if ($env:GITHUB_ACTIONS -ne 'true') {
        . "$PSScriptRoot/Set-CorepackEnvironment.ps1"
    }
    corepack prepare $packageManager --activate
    if ($lastexitcode -ne 0) { throw "Failure while preparing package manager." }
}
finally {
    Remove-Item Env:COREPACK_NPM_REGISTRY -ErrorAction SilentlyContinue
    Remove-Item Env:COREPACK_NPM_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:COREPACK_NPM_USERNAME -ErrorAction SilentlyContinue
    Remove-Item Env:COREPACK_NPM_PASSWORD -ErrorAction SilentlyContinue
}
corepack pnpm --dir "$PSScriptRoot" run auth-install
if ($lastexitcode -ne 0) { throw "Failure while restoring packages." }
