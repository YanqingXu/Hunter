$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterPenetration.cs'),
    (Join-Path $PSScriptRoot 'HunterPenetrationChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterPenetrationChecks]::Run($check)
Write-Output "PASS: $script:checks penetration rules. Cartridge and Unity scene results are reported separately."
