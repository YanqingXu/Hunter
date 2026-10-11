$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Enemies\HunterBossAbilityClock.cs'),
    (Join-Path $PSScriptRoot 'HunterBossAbilityChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterBossAbilityChecks]::Run($check)
Write-Output "PASS: $script:checks boss ability timing rules. Run Verify-Gameplay.ps1 for the separate Unity scene checks."
