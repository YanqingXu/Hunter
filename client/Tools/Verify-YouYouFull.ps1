param(
    [ValidateSet('Core', 'Content', 'Bundles', 'Scenes', 'Network')][string]$Stage = 'Core',
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskTemporary = [IO.Path]::GetFullPath((Join-Path $taskProject 'Assets\YouYouFullValidation'))
$taskExpected = Join-Path $taskProject 'Assets\YouYouFullValidation'
$taskSources = Join-Path $taskProject 'Tests\YouYouFull'
$taskResults = [IO.Path]::GetFullPath((Join-Path $taskProject 'TestResults\YouYouFull'))
$taskSettings = Join-Path $taskProject 'ProjectSettings\ProjectSettings.asset'
$taskBuildSettings = Join-Path $taskProject 'ProjectSettings\EditorBuildSettings.asset'
if ($taskTemporary -ne $taskExpected -or -not $taskTemporary.StartsWith($taskProject + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid validation directory.' }
if ((Test-Path -LiteralPath $taskTemporary) -or (Test-Path -LiteralPath ($taskTemporary + '.meta'))) { throw 'Validation directory already exists. Inspect/archive it before rerunning.' }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity executable not found: $UnityPath" }

function Get-TaskDefineBlock([string]$Text) {
    # Match only this YAML mapping, never buildNumber.Standalone or another similarly named key.
    $taskMatch = [regex]::Match($Text, '(?m)^  scriptingDefineSymbols:[^\r\n]*(?:\r?\n    [^\r\n]*)*')
    if (-not $taskMatch.Success) { throw 'Cannot find scriptingDefineSymbols in ProjectSettings.asset.' }
    return $taskMatch
}
function Get-TaskDirectMode([string]$Text) {
    $taskBlock = Get-TaskDefineBlock $Text
    $taskField = [regex]::Match($taskBlock.Value, '(?m)^    Standalone:[ \t]*([^\r\n]*)')
    if (-not $taskField.Success) { return $false }
    $taskSymbols = $taskField.Groups[1].Value.Trim().Trim([char[]]@([char]34, [char]39)).Split(';')
    return $taskSymbols -ccontains 'DISABLE_ASSETBUNDLE'
}
function Set-TaskDirectMode([string]$Text, [bool]$Enabled) {
    $taskBlock = Get-TaskDefineBlock $Text
    $taskField = [regex]::Match($taskBlock.Value, '(?m)^    Standalone:[ \t]*([^\r\n]*)')
    $taskSymbols = @()
    if ($taskField.Success) {
        $taskSymbols = @($taskField.Groups[1].Value.Trim().Trim([char[]]@([char]34, [char]39)).Split(';') | Where-Object { $_ -and $_ -cne 'DISABLE_ASSETBUNDLE' })
    }
    if ($Enabled) { $taskSymbols += 'DISABLE_ASSETBUNDLE' }
    if (-not $taskField.Success -and -not $Enabled) { return $Text }
    $taskLine = '    Standalone: ' + ($taskSymbols -join ';')
    if ($taskField.Success) {
        $taskReplacement = $taskBlock.Value.Remove($taskField.Index, $taskField.Length).Insert($taskField.Index, $taskLine)
    } else {
        $taskNewLine = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
        $taskReplacement = $taskBlock.Value -replace '^  scriptingDefineSymbols:[ \t]*\{\}', '  scriptingDefineSymbols:'
        $taskReplacement += $taskNewLine + $taskLine
    }
    return $Text.Remove($taskBlock.Index, $taskBlock.Length).Insert($taskBlock.Index, $taskReplacement)
}
function Invoke-TaskUnity([string]$Method, [string]$Log, [bool]$Quit) {
    $taskArguments = @('-batchmode', '-buildTarget', 'Win64', '-projectPath', ('"' + $taskProject + '"'), '-executeMethod', $Method, '-logFile', ('"' + $Log + '"'))
    if ($Quit) { $taskArguments += '-quit' }
    $script:taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArguments -PassThru -WindowStyle Hidden
    $taskDeadline = [DateTime]::UtcNow.AddMinutes(10)
    while (-not $script:taskProcess.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $taskDeadline) {
            $script:taskProcess.Kill(); $script:taskProcess.WaitForExit()
            throw "YouYou validation timed out: $Method. Log: $Log"
        }
    }
    if ($script:taskProcess.ExitCode -ne 0) { throw "Unity exited $($script:taskProcess.ExitCode): $Method. Log: $Log" }
}

$taskBeforeText = [IO.File]::ReadAllText($taskSettings)
$taskBeforeDirect = Get-TaskDirectMode $taskBeforeText
$taskOriginalBuildSettings = [IO.File]::ReadAllBytes($taskBuildSettings)
$taskDesiredDirect = if ($Stage -in @('Content', 'Scenes')) { $true } elseif ($Stage -eq 'Bundles') { $false } else { $taskBeforeDirect }
$taskRunId = $Stage.ToLowerInvariant() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N')
$taskLog = Join-Path $taskResults ($Stage.ToLowerInvariant() + '-latest.log')
$taskPrepareLog = Join-Path $taskResults ($Stage.ToLowerInvariant() + '-prepare-latest.log')
$taskReport = Join-Path $taskResults ($Stage.ToLowerInvariant() + '-checks.json')
$taskProcess = $null
$taskCreated = $false
$taskOutcome = $null
try {
    New-Item -ItemType Directory -Path $taskTemporary | Out-Null
    $taskCreated = $true
    New-Item -ItemType Directory -Path (Join-Path $taskTemporary 'Editor') | Out-Null
    New-Item -ItemType Directory -Path $taskResults -Force | Out-Null
    foreach ($taskName in @('YouYouFullValidationProbe.cs', 'YouYouNetworkValidation.cs', 'YouYouSceneValidation.cs')) {
        Copy-Item -LiteralPath (Join-Path $taskSources ('Runtime\' + $taskName)) -Destination $taskTemporary
    }
    Copy-Item -LiteralPath (Join-Path $taskProject 'Tests\PureCSharp\PureCSharpChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $taskSources 'Editor\YouYouFullValidation.cs') -Destination (Join-Path $taskTemporary 'Editor')
    [pscustomobject]@{ Stage = $Stage; OriginalDirectMode = $taskBeforeDirect; ValidationDirectMode = $taskDesiredDirect } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskResults ($taskRunId + '-mode.json')) -Encoding UTF8
    $taskPrepareMethod = if ($taskDesiredDirect) { 'YouYouFullValidation.PrepareDirectMode' } else { 'YouYouFullValidation.PrepareBundleMode' }
    Invoke-TaskUnity $taskPrepareMethod $taskPrepareLog $true
    if (-not (Select-String -LiteralPath $taskPrepareLog -SimpleMatch 'YOUYOU_FULL_VALIDATION_MODE_PREPARED:' -Quiet)) { throw "Mode preparation marker missing: $taskPrepareLog" }
    $taskStart = Get-Date
    Invoke-TaskUnity ('YouYouFullValidation.Run' + $Stage) $taskLog $false
    if (-not (Test-Path -LiteralPath $taskReport) -or (Get-Item -LiteralPath $taskReport).LastWriteTime -lt $taskStart) { throw "Fresh validation report missing: $taskReport" }
    $taskData = Get-Content -LiteralPath $taskReport -Raw | ConvertFrom-Json
    if (-not $taskData.passed) { throw "Validation failed: $($taskData.failure)" }
    if (-not (Select-String -LiteralPath $taskLog -SimpleMatch 'YOUYOU_FULL_VALIDATION_PASSED:' -Quiet)) { throw "Success marker missing: $taskLog" }
    $taskOutcome = [pscustomobject]@{ Stage = $Stage; Passed = $taskData.checks.Count; Report = $taskReport; Log = $taskLog }
}
finally {
    if ($taskProcess -and -not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
    try {
        if ($taskCreated) {
            # Unity has exited. Restore only this macro in the current mapping, retaining all other current symbols.
            # This also works after a compile failure, when no Editor executeMethod can run.
            $taskCurrent = [IO.File]::ReadAllText($taskSettings)
            $taskRestored = Set-TaskDirectMode $taskCurrent $taskBeforeDirect
            if ($taskRestored -cne $taskCurrent) { [IO.File]::WriteAllText($taskSettings, $taskRestored, [Text.UTF8Encoding]::new($false)) }
            if ((Get-TaskDirectMode ([IO.File]::ReadAllText($taskSettings))) -ne $taskBeforeDirect) { throw 'Original DISABLE_ASSETBUNDLE state was not restored.' }
            [IO.File]::WriteAllBytes($taskBuildSettings, $taskOriginalBuildSettings)
        }
    }
    finally {
        if ($taskCreated -and (Test-Path -LiteralPath $taskTemporary)) {
            if ((Resolve-Path -LiteralPath $taskTemporary).Path -ne $taskExpected) { throw 'Validation directory changed identity; refusing to move it.' }
            $taskArchived = [IO.Path]::GetFullPath((Join-Path $taskResults ('ValidationAssets-' + $taskRunId)))
            if (-not $taskArchived.StartsWith($taskResults + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskArchived)) { throw 'Invalid validation archive path.' }
            Move-Item -LiteralPath $taskTemporary -Destination $taskArchived
            if (Test-Path -LiteralPath ($taskTemporary + '.meta')) { Move-Item -LiteralPath ($taskTemporary + '.meta') -Destination ($taskArchived + '.meta') }
        }
    }
}
$taskOutcome
