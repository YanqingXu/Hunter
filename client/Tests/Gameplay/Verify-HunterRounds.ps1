$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterPenetration.cs'),
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterRoundDefinition.cs'),
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterWeapons.cs'),
    (Join-Path $PSScriptRoot 'HunterRoundChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterRoundChecks]::Run($check)
Write-Output "PASS: $script:checks cartridge and loadout rules."
