$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Player\HunterThrowPath.cs'),
    (Join-Path $PSScriptRoot 'HunterThrowPathChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterThrowPathChecks]::Run($check)
Write-Output "PASS: $script:checks directional throw path rules. Item preparation and Unity collision require separate scene checks."
