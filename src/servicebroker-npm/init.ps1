& "$PSScriptRoot/../../init.ps1"
dotnet build "$PSScriptRoot/../../test/ServiceBrokerTest"
node "$PSScriptRoot/.yarn/releases/yarn-4.16.0.cjs"
