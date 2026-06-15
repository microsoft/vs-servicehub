& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
if ($lastexitcode -ne 0) { throw "Failure while building ServiceBrokerTest." }
$packageManager = (Get-Content "$PSScriptRoot/package.json" -Raw | ConvertFrom-Json).packageManager
corepack prepare $packageManager --activate
if ($lastexitcode -ne 0) { throw "Failure while preparing package manager." }
& "$PSScriptRoot/Install-ArtifactsNpmCredProvider.ps1"
corepack pnpm --dir "$PSScriptRoot" run auth-install
if ($lastexitcode -ne 0) { throw "Failure while restoring packages." }
