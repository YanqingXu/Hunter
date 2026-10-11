$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterResources.cs'),
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterHealthLayout.cs'),
    (Join-Path $PSScriptRoot 'HunterHealthLayoutChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterHealthLayoutChecks]::Run($check)
Write-Output "PASS: $script:checks health layout rules. Run Verify-Gameplay.ps1 for camp, persistence and player acceptance."
