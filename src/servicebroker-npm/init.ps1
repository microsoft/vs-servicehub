& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
corepack pnpm install --dir "$PSScriptRoot" --frozen-lockfile
