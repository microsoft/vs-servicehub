param($installPath, $toolsPath, $package, $project)

$analyzersPath = Join-Path (Split-Path -Path $toolsPath -Parent) "analyzers" -Resolve

# Install the language agnostic analyzers.
if (Test-Path $analyzersPath) {
    foreach ($analyzerFilePath in Get-ChildItem -Path "$analyzersPath\*.dll" -Exclude *.resources.dll) {
        if ($project.Object.AnalyzerReferences) {
            try {
                $project.Object.AnalyzerReferences.Remove($analyzerFilePath.FullName)
            }
            catch {
            }
        }
    }
}

# $project.Type gives the language name like (C# or VB.NET)
$languageFolder = ""
if ($project.Type -eq "C#") {
    $languageFolder = "cs"
}
if ($project.Type -eq "VB.NET") {
    $languageFolder = "vb"
}
if ($languageFolder -eq "") {
    return
}

# Install language specific analyzers.
$languageAnalyzersPath = join-path $analyzersPath $languageFolder
if (Test-Path $languageAnalyzersPath) {
    foreach ($analyzerFilePath in Get-ChildItem -Path "$languageAnalyzersPath\*.dll" -Exclude *.resources.dll) {
        if ($project.Object.AnalyzerReferences) {
            try {
                $project.Object.AnalyzerReferences.Remove($analyzerFilePath.FullName)
            }
            catch {
            }
        }
    }
}
