#!/usr/bin/env pwsh

$credProviderRegistry = 'https://pkgs.dev.azure.com/artifacts-public/23934c1b-a3b5-4b70-9dd3-d1bef4cc72a0/_packaging/AzureArtifacts/npm/registry/'
npm install --global @microsoft/artifacts-npm-credprovider --registry $credProviderRegistry
if ($LASTEXITCODE -ne 0) {
    throw 'Failure while installing the Azure Artifacts npm credential provider.'
}

artifacts-npm-credprovider
if ($LASTEXITCODE -ne 0) {
    throw 'Failure while authenticating with the Azure Artifacts npm credential provider.'
}
