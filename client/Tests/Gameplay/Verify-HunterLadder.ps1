$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterLadderMotion.cs'),
    (Join-Path $PSScriptRoot 'HunterLadderChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterLadderChecks]::Run($check)
Write-Output "PASS: $script:checks ladder motion rules. Run Verify-Gameplay.ps1 separately for Unity scene, collision and exclusive input integration."
