& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
$env:COREPACK_NPM_REGISTRY = 'https://pkgs.dev.azure.com/azure-public/vside/_packaging/msft_consumption/npm/registry/'
$packageManager = (Get-Content "$PSScriptRoot/package.json" -Raw | ConvertFrom-Json).packageManager
corepack prepare $packageManager --activate
if ($lastexitcode -ne 0) { throw "Failure while preparing package manager." }
corepack pnpm install --dir "$PSScriptRoot" --frozen-lockfile
