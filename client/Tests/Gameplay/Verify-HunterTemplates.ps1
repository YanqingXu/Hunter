$ErrorActionPreference = 'Stop'
$taskProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $taskProjectRoot 'Assets\_Project\Game\Scripts\Flow\HunterTemplates.cs'),
    (Join-Path $PSScriptRoot 'HunterTemplateChecks.cs')
)
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[HunterTemplateChecks]::Run($check)
Write-Output "PASS: $script:checks template rules. Career persistence and Unity scene results are reported separately."
