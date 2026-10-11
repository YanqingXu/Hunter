param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe',
    [string]$PackageCachePath,
    [ValidateSet('all','interactions','ladder','barrels','elites','boss','alert','search','health','templates','ballistics','extraction','account')][string]$Scenario = 'all'
)
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskTemporary = [IO.Path]::GetFullPath((Join-Path $taskProject 'Assets\__GameplayChecks'))
$taskExpected = Join-Path $taskProject 'Assets\__GameplayChecks'
if ($taskTemporary -ne $taskExpected -or -not $taskTemporary.StartsWith($taskProject + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid validation directory.' }
if ((Test-Path -LiteralPath $taskTemporary) -or (Test-Path -LiteralPath ($taskTemporary + '.meta'))) { throw 'Validation directory already exists. Inspect it before rerunning.' }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'Unity executable not found.' }
$taskReport = [IO.Path]::GetFullPath((Join-Path $taskProject 'TestResults\VerticalSlice'))
$taskLog = Join-Path $taskReport 'play-checks-latest.log'
$taskProcess = $null
$taskPreviousCache = $env:UPM_CACHE_ROOT
$taskPreviousTemp = $env:TEMP
$taskPreviousTmp = $env:TMP
$taskPreviousScenario = $env:HUNTER_GAMEPLAY_SCENARIO
try {
    $env:HUNTER_GAMEPLAY_SCENARIO = $Scenario
    # Keep large package downloads and temporary files on the project drive.
    if (-not $PackageCachePath) { $PackageCachePath = if ($env:UPM_CACHE_ROOT) { $env:UPM_CACHE_ROOT } else { Join-Path $taskProject 'Library\AcceptancePackageCache' } }
    $env:UPM_CACHE_ROOT = [IO.Path]::GetFullPath($PackageCachePath)
    $env:TEMP = Join-Path $taskProject 'Library\AcceptanceTemp'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:UPM_CACHE_ROOT, $env:TEMP -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $taskTemporary 'Editor') | Out-Null
    New-Item -ItemType Directory -Path $taskReport -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayPlayChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayTouchChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplaySceneInteractionChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayLadderChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayBarrelChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayEliteChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayBossChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayAlertChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplaySearchChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayHealthChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayTemplateChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayBallisticChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayExtractionChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GameplayAccountChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterAccountProgressionChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterAccountCareerChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterExtractionAttemptChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterMissionExtractionChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterPenetrationChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterRoundChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterTemplateChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterTemplateCareerChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterCareerChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterProgressionChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterSkillChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterFieldSkillChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterRevivalChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterItemChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterSupplyChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterItemSkillChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterOnceSkillChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterAimChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterPostureChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterThrowPathChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterPointerStickChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterInteractionChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterLadderChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterBarrelChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterEliteChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterBossAbilityChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterAlertMemoryChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterSearchLayoutChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterHealthLayoutChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HunterHealthCareerChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $taskProject 'Tests\PureCSharp\PureCSharpChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor\GameplayValidation.cs') -Destination (Join-Path $taskTemporary 'Editor')
    $taskArguments = @('-batchmode', '-accept-apiupdate', '-projectPath', ('"' + $taskProject + '"'), '-executeMethod', 'GameplayValidation.Run', '-logFile', ('"' + $taskLog + '"'))
    $taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
    $taskDeadline = [DateTime]::UtcNow.AddMinutes(20)
    while (-not $taskProcess.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $taskDeadline) { $taskProcess.Kill(); $taskProcess.WaitForExit(); throw "Gameplay validation timed out. See $taskLog" }
    }
    if ($taskProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $taskLog -SimpleMatch 'BIGWORLD_GAMEPLAY_VALIDATION_PASSED' -Quiet)) {
        throw "Gameplay validation failed. See $taskLog"
    }
    Get-Content -LiteralPath (Join-Path $taskReport 'play-checks.json')
}
finally {
    $env:UPM_CACHE_ROOT = $taskPreviousCache
    $env:TEMP = $taskPreviousTemp
    $env:TMP = $taskPreviousTmp
    $env:HUNTER_GAMEPLAY_SCENARIO = $taskPreviousScenario
    if ($taskProcess -and -not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
    if ((Test-Path -LiteralPath $taskTemporary) -and (Resolve-Path -LiteralPath $taskTemporary).Path -eq $taskExpected) {
        $taskArchived = [IO.Path]::GetFullPath((Join-Path $taskReport ('ValidationAssets-' + [guid]::NewGuid().ToString('N'))))
        if (-not $taskArchived.StartsWith($taskReport + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskArchived)) { throw 'Invalid validation archive path.' }
        Move-Item -LiteralPath $taskTemporary -Destination $taskArchived
        if (Test-Path -LiteralPath ($taskTemporary + '.meta')) { Move-Item -LiteralPath ($taskTemporary + '.meta') -Destination ($taskArchived + '.meta') }
    }
}
