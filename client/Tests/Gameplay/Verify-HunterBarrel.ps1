$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Flow\HunterBarrelFuse.cs'),
    (Join-Path $PSScriptRoot 'HunterBarrelChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterBarrelChecks]::Run($check)
Write-Output "PASS: $script:checks barrel fuse rules. Run Verify-Gameplay.ps1 separately for Unity scene validation."
