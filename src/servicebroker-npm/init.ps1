& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
if ($lastexitcode -ne 0) { throw "Failure while building ServiceBrokerTest." }
$packageManager = (Get-Content "$PSScriptRoot/package.json" -Raw | ConvertFrom-Json).packageManager
try {
    $env:COREPACK_NPM_REGISTRY = 'https://registry.npmjs.org'
    corepack prepare $packageManager --activate
    if ($lastexitcode -ne 0) { throw "Failure while preparing package manager." }
}
finally {
    Remove-Item Env:COREPACK_NPM_REGISTRY -ErrorAction SilentlyContinue
}
try {
    $env:NPM_CONFIG_REGISTRY = 'https://registry.npmjs.org/'
    corepack pnpm install --dir "$PSScriptRoot" --frozen-lockfile
    if ($lastexitcode -ne 0) { throw "Failure while restoring packages." }
}
finally {
    Remove-Item Env:NPM_CONFIG_REGISTRY -ErrorAction SilentlyContinue
}
