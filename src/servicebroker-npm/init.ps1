& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
$packageManager = (Get-Content "$PSScriptRoot/package.json" -Raw | ConvertFrom-Json).packageManager
$env:COREPACK_NPM_REGISTRY = 'https://registry.npmjs.org'
corepack prepare $packageManager --activate
if ($lastexitcode -ne 0) { throw "Failure while preparing package manager." }
Remove-Item Env:COREPACK_NPM_REGISTRY -ErrorAction SilentlyContinue
corepack pnpm install --dir "$PSScriptRoot" --frozen-lockfile
