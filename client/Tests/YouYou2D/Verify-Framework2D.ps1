param([string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskTemporary = [IO.Path]::GetFullPath((Join-Path $taskProject 'Assets\__Framework2DChecks'))
$taskExpected = Join-Path $taskProject 'Assets\__Framework2DChecks'
if ($taskTemporary -ne $taskExpected -or -not $taskTemporary.StartsWith($taskProject + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid validation directory.' }
if (Test-Path -LiteralPath $taskTemporary) { throw 'Validation directory already exists. Inspect it before rerunning.' }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'Unity executable not found.' }
$taskReport = Join-Path $taskProject 'TestResults\YouYouFramework\Adaptation2D'
$taskLog = Join-Path $taskReport 'play-checks-latest.log'
$taskProcess = $null
try {
    New-Item -ItemType Directory -Path (Join-Path $taskTemporary 'Editor') | Out-Null
    New-Item -ItemType Directory -Path $taskReport -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Framework2DPlayChecks.cs') -Destination $taskTemporary
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Editor\Framework2DValidation.cs') -Destination (Join-Path $taskTemporary 'Editor')
    $taskArguments = @('-batchmode', '-projectPath', ('"' + $taskProject + '"'), '-executeMethod', 'Framework2DValidation.Run', '-logFile', ('"' + $taskLog + '"'))
    $taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
    $taskDeadline = [DateTime]::UtcNow.AddMinutes(5)
    while (-not $taskProcess.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $taskDeadline) { $taskProcess.Kill(); $taskProcess.WaitForExit(); throw "Validation timed out. See $taskLog" }
    }
    if ($taskProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $taskLog -SimpleMatch 'BIGWORLD_FRAMEWORK2D_VALIDATION_PASSED' -Quiet)) {
        throw "Validation failed. See $taskLog"
    }
    Get-Content -LiteralPath (Join-Path $taskReport 'play-checks.json')
}
finally {
    if ($taskProcess -and -not $taskProcess.HasExited) { $taskProcess.Kill(); $taskProcess.WaitForExit() }
    if ((Test-Path -LiteralPath $taskTemporary) -and (Resolve-Path -LiteralPath $taskTemporary).Path -eq $taskExpected) {
        $taskArchived = [IO.Path]::GetFullPath((Join-Path $taskReport ('ValidationAssets-' + [guid]::NewGuid().ToString('N'))))
        if (-not $taskArchived.StartsWith($taskReport + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $taskArchived)) { throw 'Invalid validation archive path.' }
        Move-Item -LiteralPath $taskTemporary -Destination $taskArchived
        if (Test-Path -LiteralPath ($taskTemporary + '.meta')) { Move-Item -LiteralPath ($taskTemporary + '.meta') -Destination ($taskArchived + '.meta') }
    }
}
