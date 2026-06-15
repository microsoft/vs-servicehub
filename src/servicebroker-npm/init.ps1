& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
if ($lastexitcode -ne 0) { throw "Failure while building ServiceBrokerTest." }
$packageManager = (Get-Content "$PSScriptRoot/package.json" -Raw | ConvertFrom-Json).packageManager
if ($env:GITHUB_ACTIONS -ne 'true') {
    $npmRegistry = & "$PSScriptRoot/Get-NpmRegistry.ps1"
    $env:COREPACK_NPM_REGISTRY = $npmRegistry
}
try {
    corepack prepare $packageManager --activate
    if ($lastexitcode -ne 0) { throw "Failure while preparing package manager." }
}
finally {
    Remove-Item Env:COREPACK_NPM_REGISTRY -ErrorAction SilentlyContinue
}
corepack pnpm --dir "$PSScriptRoot" run auth-install
if ($lastexitcode -ne 0) { throw "Failure while restoring packages." }
