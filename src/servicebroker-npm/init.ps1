# The repo-root init script restores this project's pnpm packages.
& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
if ($lastexitcode -ne 0) { throw "Failure while building ServiceBrokerTest." }
