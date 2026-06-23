<#
.SYNOPSIS
    This script translates the variables returned by the _all.ps1 script
    into commands that instruct Azure Pipelines to actually set those variables for other pipeline tasks to consume.

    The build or release definition may have set these variables to override
    what the build would do. So only set them if they have not already been set.
#>

[CmdletBinding()]
param (
)

(& "$PSScriptRoot\_all.ps1").GetEnumerator() |% {
    # Always use ALL CAPS for env var names since Azure Pipelines converts variable names to all caps and on non-Windows OS, env vars are case sensitive.
    $keyCaps = $_.Key.ToUpper()
    $existingValue = if (Test-Path "env:$keyCaps") { Get-Content "env:$keyCaps" } else { $null }
    # On Azure Pipelines, treat any pre-set env var slot as authoritative -- even when empty -- because
    # queue-time variables are exposed as env vars and are read-only on the run. Emitting task.setvariable
    # against an empty queue-time variable used to be silently overwritten; under the readonly-variables
    # rollout it now hard-fails with "Overwriting readonly variable".
    # Off Azure Pipelines (local, GitHub Actions, etc.), keep requiring a non-empty value: on Linux,
    # env vars can be inherited as empty for unrelated reasons and shouldn't suppress the computed value.
    $isAlreadySet = if ($env:TF_BUILD) { $null -ne $existingValue } else { [bool]$existingValue }
    if ($isAlreadySet) {
        $displayValue = if ($existingValue) { "'$existingValue'" } else { "(empty)" }
        Write-Host "Skipping setting $keyCaps because variable is already set to $displayValue." -ForegroundColor Cyan
    } else {
        Write-Host "$keyCaps=$($_.Value)" -ForegroundColor Yellow
        if ($env:TF_BUILD) {
            # Create two variables: the first that can be used by its simple name and accessible only within this job.
            Write-Host "##vso[task.setvariable variable=$keyCaps]$($_.Value)"
            # and the second that works across jobs and stages but must be fully qualified when referenced.
            Write-Host "##vso[task.setvariable variable=$keyCaps;isOutput=true]$($_.Value)"
        } elseif ($env:GITHUB_ACTIONS) {
            Add-Content -LiteralPath $env:GITHUB_ENV -Value "$keyCaps=$($_.Value)"
        }
        Set-Item -LiteralPath "env:$keyCaps" -Value $_.Value
    }
}
