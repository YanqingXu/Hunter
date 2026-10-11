$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Flow\HunterMission.cs'),
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Flow\HunterExtractionAttempt.cs'),
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Flow\HunterSearchLayout.cs'),
    (Join-Path $PSScriptRoot 'HunterSearchLayoutChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterSearchLayoutChecks]::Run($check)
Write-Output "PASS: $script:checks search layout rules. Run Verify-Gameplay.ps1 for scene and map UI acceptance."
