$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Flow\HunterAccountProgression.cs'),
    (Join-Path $PSScriptRoot 'HunterAccountProgressionChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterAccountProgressionChecks]::Run($check)
Write-Output "PASS: $script:checks account progression rules. Use Verify-Gameplay.ps1 for scene acceptance."
