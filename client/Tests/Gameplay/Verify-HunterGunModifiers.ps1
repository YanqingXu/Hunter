$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterGunModifiers.cs'),
    (Join-Path $PSScriptRoot 'HunterGunModifierChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterGunModifierChecks]::Run($check)
Write-Output "PASS: $script:checks gun modifier rules. Skill catalogue, camp, ammo state and scene integration remain pending."
