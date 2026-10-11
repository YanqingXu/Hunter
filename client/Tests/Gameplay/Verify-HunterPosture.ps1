$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterPostureRules.cs'),
    (Join-Path $PSScriptRoot 'HunterPostureChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterPostureChecks]::Run($check)
Write-Output "PASS: $script:checks prone aim rules. Runtime posture and collision require separate scene checks."
