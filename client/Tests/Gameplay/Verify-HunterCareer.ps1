$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -AssemblyName System.Web.Extensions
$output = Join-Path $projectRoot ('TestResults\Career\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$runner = Join-Path $output 'JsonHarness.cs'
@'
using System;
using System.Web.Script.Serialization;
using BigWorld.Gameplay;
public static class JsonHarness {
    public static void Run(Action<bool,string> check, string path) {
        var codec = new JavaScriptSerializer();
        HunterCareerChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), path);
        HunterProgressionChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "Progression"));
        HunterSkillChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "Skills"));
        HunterFieldSkillChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "FieldSkills"));
        HunterRevivalChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "Revival"));
        HunterItemChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "Items"));
        HunterSupplyChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "Supplies"));
        HunterItemSkillChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "ItemSkills"));
        HunterOnceSkillChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "OnceSkills"));
        HunterTemplateCareerChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "Templates"));
        HunterHealthCareerChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "HealthLayouts"));
        HunterAccountCareerChecks.Run(check, json => codec.Deserialize<HunterCareerData>(json), data => codec.Serialize(data), System.IO.Path.Combine(path, "AccountLevels"));
    }
}
'@ | Set-Content -LiteralPath $runner -Encoding UTF8
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterTemplates.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterCareer.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterCareerStore.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterProgression.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterAccountProgression.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterSkills.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterItems.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterItemSupply.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterWeapons.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterPenetration.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterRoundDefinition.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterResources.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterHealthLayout.cs'),
    (Join-Path $PSScriptRoot 'HunterCareerChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterProgressionChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterSkillChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterFieldSkillChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterRevivalChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterItemChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterSupplyChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterItemSkillChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterOnceSkillChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterTemplateCareerChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterHealthCareerChecks.cs'),
    (Join-Path $PSScriptRoot 'HunterAccountCareerChecks.cs'),
    $runner
) -ReferencedAssemblies System.Web.Extensions
$script:checks = 0
$check = [Action[bool,string]] { param($passed,$name) if (-not $passed) { throw $name }; $script:checks++ }
[JsonHarness]::Run($check,$output)
Write-Output "PASS: $script:checks career rules and persistence checks. Fixtures: $output"
