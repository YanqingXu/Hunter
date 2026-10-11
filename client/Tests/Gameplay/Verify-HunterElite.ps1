$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Enemies\HunterEliteRules.cs'),
    (Join-Path $PSScriptRoot 'HunterEliteChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterEliteChecks]::Run($check)
Write-Output "PASS: $script:checks elite and summon lifecycle rules. Run Verify-Gameplay.ps1 separately for Unity scene validation."
