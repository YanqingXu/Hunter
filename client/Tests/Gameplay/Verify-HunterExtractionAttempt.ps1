$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Flow\HunterExtractionAttempt.cs'),
    (Join-Path $PSScriptRoot 'HunterExtractionAttemptChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterExtractionAttemptChecks]::Run($check)
Write-Output "PASS: $script:checks extraction rules. Use Verify-Gameplay.ps1 for live-scene acceptance."
