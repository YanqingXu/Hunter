param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe',
    [ValidateRange(1, 60)][int]$TimeoutMinutes = 10
)
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskTemporary = [IO.Path]::GetFullPath((Join-Path $taskProject 'Assets\__MapCollisionChecks'))
$taskExpected = Join-Path $taskProject 'Assets\__MapCollisionChecks'
$taskResults = [IO.Path]::GetFullPath((Join-Path $taskProject 'TestResults\Map2D\native-migration'))
$taskBuildSettings = Join-Path $taskProject 'ProjectSettings\EditorBuildSettings.asset'
$taskSources = @('GridMapPhysicsVerification.cs', 'GridMapPlayVerification.cs', 'GridMapStreamingVerification.cs', 'GridMapBatchExit.cs')
if ($taskTemporary -ne $taskExpected -or -not $taskTemporary.StartsWith($taskProject + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid validation directory.' }
if ((Test-Path -LiteralPath $taskTemporary) -or (Test-Path -LiteralPath ($taskTemporary + '.meta'))) { throw 'Validation directory already exists. Inspect/archive it before rerunning.' }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity executable not found: $UnityPath" }
if (-not (Test-Path -LiteralPath $taskBuildSettings -PathType Leaf)) { throw 'EditorBuildSettings.asset is missing; refusing to run without a restorable snapshot.' }
foreach ($taskSource in $taskSources) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot ('Editor\' + $taskSource)) -PathType Leaf)) { throw "Missing verification source: $taskSource" }
}

# Dedicated processes run sequentially. Close any other Unity instance for this project first.
$taskRunId = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N')
$taskRun = [IO.Path]::GetFullPath((Join-Path $taskResults ('Run-' + $taskRunId)))
if (-not $taskRun.StartsWith($taskResults + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskRun)) { throw 'Invalid or existing report directory.' }
$taskOriginalBuildSettings = [IO.File]::ReadAllBytes($taskBuildSettings)
$taskOriginalBuildHash = (Get-FileHash -LiteralPath $taskBuildSettings -Algorithm SHA256).Hash
$taskCreated = $false
$taskProcess = $null
$taskSummary = [ordered]@{ runId = $taskRunId; startedUtc = [DateTime]::UtcNow.ToString('O'); completedUtc = $null; passed = $false; failure = $null; stages = @(); originalBuildSettingsRestored = $false }
$taskStages = @(
    @{ Name = 'solid'; Method = 'GridMapPhysicsVerification.RunSolidRegressionBatch'; ReportFlag = '-mapPhysicsReport'; Quit = $true; NoGraphics = $true; MinimumPassed = 3; RequireCleanup = $false },
    @{ Name = 'play'; Method = 'GridMapPlayVerification.BeginBatch'; ReportFlag = '-mapPlayReport'; Quit = $false; NoGraphics = $false; MinimumPassed = 7; RequireCleanup = $true },
    @{ Name = 'streaming'; Method = 'GridMapStreamingVerification.BeginBatch'; ReportFlag = '-mapStreamingReport'; Quit = $false; NoGraphics = $false; MinimumPassed = 1; RequireCleanup = $true }
)
try {
    New-Item -ItemType Directory -Path $taskRun | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $taskRun 'EditorBuildSettings.before.asset'), $taskOriginalBuildSettings)
    New-Item -ItemType Directory -Path $taskTemporary | Out-Null
    $taskCreated = $true
    $taskEditor = Join-Path $taskTemporary 'Editor'
    New-Item -ItemType Directory -Path $taskEditor | Out-Null
    foreach ($taskSource in $taskSources) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('Editor\' + $taskSource)) -Destination $taskEditor
    }
    foreach ($taskStage in $taskStages) {
        $taskReport = Join-Path $taskRun ($taskStage.Name + '-checks.json')
        $taskLog = Join-Path $taskRun ($taskStage.Name + '.log')
        if (Test-Path -LiteralPath $taskReport) { throw "Refusing stale report: $taskReport" }
        $taskArguments = @('-batchmode', '-projectPath', ('"' + $taskProject + '"'),
            '-executeMethod', $taskStage.Method, $taskStage.ReportFlag, ('"' + $taskReport + '"'), '-logFile', ('"' + $taskLog + '"'))
        # Keep graphics initialized for the real Play/streaming stages; the window remains hidden.
        if ($taskStage.NoGraphics) { $taskArguments += '-nographics' }
        if ($taskStage.Quit) { $taskArguments += '-quit' }
        $taskStarted = [DateTime]::UtcNow
        Write-Output ("Running {0}: {1}" -f $taskStage.Name, $taskLog)
        $taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
        $taskDeadline = $taskStarted.AddMinutes($TimeoutMinutes)
        $taskShutdownDeadline = $null
        while (-not $taskProcess.WaitForExit(1000)) {
            # A completed probe can still leave a native Editor shutdown hanging. Never
            # turn a forced exit into success, even when its JSON assertions passed.
            if (-not $taskShutdownDeadline -and (Test-Path -LiteralPath $taskReport -PathType Leaf) -and
                (Get-Item -LiteralPath $taskReport).LastWriteTimeUtc -ge $taskStarted -and (Test-Path -LiteralPath $taskLog -PathType Leaf)) {
                if (Select-String -LiteralPath $taskLog -Pattern 'Input System module state changed to: Shutdown|Cleanup mono|Shutting down' -Quiet) {
                    $taskShutdownDeadline = [DateTime]::UtcNow.AddSeconds(60)
                }
            }
            if ($taskShutdownDeadline -and [DateTime]::UtcNow -ge $taskShutdownDeadline) {
                $taskProcess.Kill(); $taskProcess.WaitForExit()
                throw "Unity shutdown exceeded 60 seconds at $($taskStage.Name); the owned process was terminated and this run failed. See $taskReport and $taskLog"
            }
            if ([DateTime]::UtcNow -gt $taskDeadline) {
                $taskProcess.Kill(); $taskProcess.WaitForExit()
                throw "Collision validation timed out at $($taskStage.Name). See $taskLog"
            }
        }
        if (-not (Test-Path -LiteralPath $taskReport -PathType Leaf) -or (Get-Item -LiteralPath $taskReport).LastWriteTimeUtc -lt $taskStarted) {
            throw "Fresh $($taskStage.Name) report missing (Unity exit $($taskProcess.ExitCode)). See $taskLog"
        }
        $taskData = Get-Content -LiteralPath $taskReport -Raw | ConvertFrom-Json
        # PowerShell 7 converts ISO dates to DateTime; keep the UTC Kind instead of
        # formatting that object as a culture-specific string and parsing it again.
        $taskGeneratedUtc = if ($taskData.generatedUtc -is [DateTime]) { $taskData.generatedUtc.ToUniversalTime() }
            elseif ($taskData.generatedUtc) { [DateTimeOffset]::Parse($taskData.generatedUtc).UtcDateTime }
            else { [DateTime]::MinValue }
        if ($taskGeneratedUtc -lt $taskStarted) {
            throw "Report timestamp does not belong to this run: $taskReport"
        }
        $taskItems = @($taskData.results)
        $taskFailedItems = @($taskItems | Where-Object { $_.passed -ne $true })
        $taskSummary.stages += [pscustomobject]@{
            name = $taskStage.Name; passed = $taskData.passed; failed = $taskData.failed; report = $taskReport;
            log = $taskLog; exitCode = $taskProcess.ExitCode; seconds = ([DateTime]::UtcNow - $taskStarted).TotalSeconds
        }
        if ($taskProcess.ExitCode -ne 0 -or $taskData.failed -ne 0 -or $taskData.passed -lt $taskStage.MinimumPassed -or
            $taskItems.Count -ne $taskData.passed -or $taskFailedItems.Count -ne 0) {
            throw "Collision validation failed at $($taskStage.Name): $($taskFailedItems.error -join '; '). See $taskReport and $taskLog"
        }
        if ($taskStage.RequireCleanup -and $taskData.temporaryAssetsCleaned -ne $true) { throw "Temporary probe assets were not cleaned: $taskReport" }
        if ($taskStage.Name -eq 'streaming' -and ($taskData.observedFrames -le 0 -or $taskData.observedFixedTimeChanges -le 0 -or $taskData.duplicateIdentityFrames -ne 0)) {
            throw "Streaming did not observe real physics frames or created duplicate identities: $taskReport"
        }
        Write-Output ("PASS {0}: {1} checks" -f $taskStage.Name, $taskData.passed)
    }
    $taskSummary.passed = $true
}
catch {
    $taskSummary.failure = $_.Exception.Message
    throw
}
finally {
    if ($taskProcess -and -not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
    try {
        if ($taskCreated) {
            if (-not (Test-Path -LiteralPath $taskBuildSettings -PathType Leaf) -or
                (Get-FileHash -LiteralPath $taskBuildSettings -Algorithm SHA256).Hash -ne $taskOriginalBuildHash) {
                [IO.File]::WriteAllBytes($taskBuildSettings, $taskOriginalBuildSettings)
            }
            $taskSummary.originalBuildSettingsRestored = (Get-FileHash -LiteralPath $taskBuildSettings -Algorithm SHA256).Hash -eq $taskOriginalBuildHash
            if (-not $taskSummary.originalBuildSettingsRestored) { throw 'Original build settings were not restored.' }
        }
    }
    finally {
        try {
            if ($taskCreated -and (Test-Path -LiteralPath $taskTemporary)) {
                if ((Resolve-Path -LiteralPath $taskTemporary).Path -ne $taskExpected) { throw 'Validation directory changed identity; refusing to move it.' }
                $taskArchive = [IO.Path]::GetFullPath((Join-Path $taskRun 'ValidationAssets'))
                if (-not $taskArchive.StartsWith($taskRun + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskArchive)) { throw 'Invalid validation archive path.' }
                Move-Item -LiteralPath $taskTemporary -Destination $taskArchive
                if (Test-Path -LiteralPath ($taskTemporary + '.meta')) { Move-Item -LiteralPath ($taskTemporary + '.meta') -Destination ($taskArchive + '.meta') }
            }
        }
        finally {
            $taskSummary.completedUtc = [DateTime]::UtcNow.ToString('O')
            if (Test-Path -LiteralPath $taskRun -PathType Container) {
                $taskSummary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding UTF8
            }
        }
    }
}
$taskSummary | ConvertTo-Json -Depth 6
