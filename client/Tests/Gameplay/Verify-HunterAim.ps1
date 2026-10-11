$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterAim.cs'),
    (Join-Path $PSScriptRoot 'HunterAimChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterAimChecks]::Run($check)
Write-Output "PASS: $script:checks aim input rules. Runtime input and projectile integration require separate scene checks."
