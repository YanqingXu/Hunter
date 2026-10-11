$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Enemies\HunterAlertMemory.cs'),
    (Join-Path $PSScriptRoot 'HunterAlertMemoryChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterAlertMemoryChecks]::Run($check)
Write-Output "PASS: $script:checks alert memory rules. Run Verify-Gameplay.ps1 separately for Unity scene validation."
