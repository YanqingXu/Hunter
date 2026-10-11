$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterPointerStick.cs'),
    (Join-Path $PSScriptRoot 'HunterPointerStickChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterPointerStickChecks]::Run($check)
Write-Output "PASS: $script:checks pointer ownership and stick rules. Unity UI integration is checked separately by Verify-Gameplay.ps1."
