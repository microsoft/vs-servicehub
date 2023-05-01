[string]::join(',',(@{
    ('MicrosoftServiceHubFrameworkVersion') = & { (dotnet tool run nbgv get-version --project "$PSScriptRoot\..\..\src\Microsoft.ServiceHub.Framework" --format json | ConvertFrom-Json).AssemblyVersion };
}.GetEnumerator() |% { "$($_.key)=$($_.value)" }))
