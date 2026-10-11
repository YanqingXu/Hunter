$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Flow\HunterInteractionTimer.cs'),
    (Join-Path $PSScriptRoot 'HunterInteractionChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterInteractionChecks]::Run($check)
Write-Output "PASS: $script:checks interaction timing rules. Run Verify-Gameplay.ps1 separately for Unity scene and input integration."
