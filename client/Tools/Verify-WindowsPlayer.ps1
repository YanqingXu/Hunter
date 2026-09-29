param(
    [ValidateRange(10, 600)][int]$StartupTimeoutSeconds = 120,
    [ValidateRange(5, 120)][int]$CloseTimeoutSeconds = 30,
    [ValidateNotNullOrEmpty()][string]$GameReadyMarker = 'BIGWORLD_GAME_READY:',
    [string]$UpdateUrl,
    [string]$ExpectedCodeVersion,
    [string]$ExpectedUpdateDiagnostic,
    [switch]$RunGameplayChecks
)
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskBuildRoot = [IO.Path]::GetFullPath((Join-Path $taskProject 'Builds\Windows64')) + '\'
$taskLatest = Join-Path $taskProject 'Builds\latest-windows.json'
$taskResults = Join-Path $taskProject 'TestResults\VerticalSlice'
New-Item -ItemType Directory -Path $taskResults -Force | Out-Null
$taskId = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N')
$taskLog = Join-Path $taskResults ('standalone-smoke-' + $taskId + '.log')
$taskReport = Join-Path $taskResults ('standalone-smoke-' + $taskId + '.json')
$taskFrameworkMarker = 'BIGWORLD_ORIGINAL_YOUYOU_READY:'
$taskProcess = $null
$taskFailure = $null
$taskResult = [ordered]@{
    passed = $false; executable = $null; executableSha256 = $null; assemblySha256 = $null; buildReport = $taskLatest; buildUtc = $null
    pid = $null; startedUtc = $null; finishedUtc = $null; startupAlive = $false
    frameworkReady = $false; gameReady = $false; frameworkReadyMarker = $taskFrameworkMarker; gameReadyMarker = $GameReadyMarker
    windowFound = $false; windowHandle = $null; normalCloseRequested = $false; closeMethod = $null
    normalExit = $false; forcedKill = $false; exitCode = $null; errors = @(); failure = $null; log = $taskLog
    hybridReady = $false; expectedCodeVersion = $ExpectedCodeVersion; codeVersionMatched = $false
    expectedUpdateDiagnostic = $ExpectedUpdateDiagnostic; updateDiagnosticMatched = $false
}

function Read-TaskPlayerLog {
    if (-not (Test-Path -LiteralPath $taskLog)) { return '' }
    $taskStream = [IO.File]::Open($taskLog, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $taskReader = [IO.StreamReader]::new($taskStream, [Text.Encoding]::UTF8, $true)
        try { return $taskReader.ReadToEnd() } finally { $taskReader.Dispose() }
    } finally { $taskStream.Dispose() }
}
function Get-TaskPlayerErrors([string]$Text) {
    $taskPattern = '(?im)^[^\r\n]*(?:\b[A-Za-z_][\w.]*Exception(?::|\s*$)|\bUnhandled Exception\b|\bFatal error\b|Crash!!!|Assertion failed|Assert failed|Unable to load DLL|Could not load file or assembly|already being activated or deactivated|Cannot move GameObject|FMOD(?: Studio)?:[^\r\n]*(?:Encountered Error|ERR_[A-Z_]+))[^\r\n]*'
    return @([regex]::Matches($Text, $taskPattern) | ForEach-Object { $_.Value.Trim() } | Select-Object -Unique -First 50)
}
function Request-TaskNormalClose {
    if (-not $taskProcess -or $taskProcess.HasExited) { return }
    $taskProcess.Refresh()
    $taskHandle = $taskProcess.MainWindowHandle
    if ($taskHandle -ne [IntPtr]::Zero -and [BigWorldSmokeNativeWindow]::OwnedBy($taskHandle, $taskProcess.Id)) {
        $taskResult.windowFound = $true
        $taskResult.windowHandle = '0x' + $taskHandle.ToInt64().ToString('X')
        if ($taskProcess.CloseMainWindow()) {
            $taskResult.normalCloseRequested = $true
            $taskResult.closeMethod = 'Process.CloseMainWindow'
            return
        }
    }
    # Process.MainWindowHandle can omit an initially hidden window. Find only this owned PID's Unity window.
    $taskHandle = [BigWorldSmokeNativeWindow]::FindOwnedUnityWindow($taskProcess.Id)
    if ($taskHandle -ne [IntPtr]::Zero) {
        $taskResult.windowFound = $true
        $taskResult.windowHandle = '0x' + $taskHandle.ToInt64().ToString('X')
        if ([BigWorldSmokeNativeWindow]::CloseOwnedWindow($taskHandle, $taskProcess.Id)) {
            $taskResult.normalCloseRequested = $true
            $taskResult.closeMethod = 'WM_CLOSE to owned Unity window'
        }
    }
}

try {
    if (-not (Test-Path -LiteralPath $taskLatest -PathType Leaf)) { throw 'No verified latest Windows build. Run Tools/Build-Windows.ps1 first.' }
    $taskBuild = Get-Content -LiteralPath $taskLatest -Raw | ConvertFrom-Json
    if ($taskBuild.result -ne 'Succeeded' -or $taskBuild.target -ne 'StandaloneWindows64' -or
        $taskBuild.errors -ne 0 -or $taskBuild.runtimeFilesVerified -ne $true) {
        throw 'latest-windows.json does not describe a successful build with verified original runtime files.'
    }
    $taskFolder = [IO.Path]::GetFullPath([string]$taskBuild.output)
    if (-not ($taskFolder + '\').StartsWith($taskBuildRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Latest executable is outside this project Windows build directory.' }
    $taskExecutable = Join-Path $taskFolder 'BigWorld.exe'
    if (-not (Test-Path -LiteralPath $taskExecutable -PathType Leaf)) { throw "Executable missing: $taskExecutable" }
    $taskLocalReport = Join-Path $taskFolder 'build-report.json'
    if (-not (Test-Path -LiteralPath $taskLocalReport -PathType Leaf)) { throw 'Per-build report is missing.' }
    $taskLocal = Get-Content -LiteralPath $taskLocalReport -Raw | ConvertFrom-Json
    if ($taskLocal.utc -ne $taskBuild.utc -or $taskLocal.output -ne $taskBuild.output -or
        $taskLocal.result -ne 'Succeeded' -or $taskLocal.runtimeFilesVerified -ne $true) { throw 'Latest and per-build reports disagree.' }
    foreach ($taskManifest in @('VersionFile.bytes', 'AssetInfo.bytes', 'youyou2d\project.assetbundle')) {
        if (-not (Test-Path -LiteralPath (Join-Path $taskFolder ('BigWorld_Data\StreamingAssets\' + $taskManifest)) -PathType Leaf)) {
            throw "Published original content is missing: $taskManifest"
        }
    }
    $taskResult.executable = $taskExecutable
    $taskScriptArtifacts = @(Get-ChildItem -LiteralPath (Join-Path $taskFolder 'BigWorld_Data') -Recurse -File |
        Where-Object { $_.Name -match '(?i)^(lib)?xlua|\.lua(\.txt|\.bytes)?$' })
    if ($taskScriptArtifacts.Count -gt 0) { throw 'Player still contains script-runtime assets or native libraries.' }
    $taskResult.executableSha256 = (Get-FileHash -LiteralPath $taskExecutable -Algorithm SHA256).Hash
    $taskManagedAssembly = Join-Path $taskFolder 'BigWorld_Data\Managed\Assembly-CSharp.dll'
    if ($taskBuild.scriptingBackend -eq 'IL2CPP') {
        $taskManagedAssembly = Join-Path $taskFolder 'BigWorld_Data\StreamingAssets\hotupdate\assemblies\Assembly-CSharp.dll.bytes'
        if (-not (Test-Path -LiteralPath (Join-Path $taskFolder 'GameAssembly.dll'))) { throw 'IL2CPP GameAssembly.dll is missing.' }
    }
    if (Test-Path -LiteralPath $taskManagedAssembly) { $taskResult.assemblySha256 = (Get-FileHash -LiteralPath $taskManagedAssembly -Algorithm SHA256).Hash }
    $taskResult.buildUtc = $taskBuild.utc

    if (-not ('BigWorldSmokeNativeWindow' -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class BigWorldSmokeNativeWindow
{
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr context);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    public static bool OwnedBy(IntPtr window, int processId)
    { uint owner; GetWindowThreadProcessId(window, out owner); return owner == (uint)processId; }
    public static IntPtr FindOwnedUnityWindow(int processId)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, context) =>
        {
            if (!OwnedBy(window, processId) || GetWindow(window, 4) != IntPtr.Zero) return true;
            var name = new StringBuilder(256); GetClassName(window, name, name.Capacity);
            if (name.ToString() != "UnityWndClass") return true;
            found = window; return false;
        }, IntPtr.Zero);
        return found;
    }
    public static bool CloseOwnedWindow(IntPtr window, int processId)
    { return OwnedBy(window, processId) && PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); }
}
"@
    }
    $taskArguments = @('-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '540', '-logFile', ('"' + $taskLog + '"'))
    if ($UpdateUrl) { $taskArguments += @('-bigworld-update-url', ('"' + $UpdateUrl + '"')) }
    if ($RunGameplayChecks) { $taskArguments += '-bigworld-validate-gameplay' }
    $taskResult.startedUtc = [DateTime]::UtcNow.ToString('o')
    $taskProcess = Start-Process -FilePath $taskExecutable -WorkingDirectory $taskFolder -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
    $taskResult.pid = $taskProcess.Id
    $taskDeadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    while ([DateTime]::UtcNow -lt $taskDeadline) {
        $taskProcess.Refresh()
        $taskText = Read-TaskPlayerLog
        $taskResult.frameworkReady = $taskText.Contains($taskFrameworkMarker)
        $taskResult.gameReady = $taskText.Contains($GameReadyMarker)
        $taskResult.hybridReady = $taskText.Contains('BIGWORLD_HYBRIDCLR_READY:')
        $taskResult.codeVersionMatched = -not $ExpectedCodeVersion -or $taskText -match ('(?m)^BIGWORLD_HYBRIDCLR_CODE_VERSION: ' + [regex]::Escape($ExpectedCodeVersion) + '\r?$')
        $taskResult.updateDiagnosticMatched = -not $ExpectedUpdateDiagnostic -or $taskText.Contains($ExpectedUpdateDiagnostic)
        $taskResult.errors = @(Get-TaskPlayerErrors $taskText)
        if ($taskProcess.HasExited) { throw 'Player exited before smoke test requested normal close.' }
        $taskResult.startupAlive = $true
        if ($taskResult.errors.Count -gt 0) { throw 'Player logged an exception/assertion during startup.' }
        if ($taskResult.frameworkReady -and $taskResult.gameReady -and ($taskBuild.scriptingBackend -ne 'IL2CPP' -or $taskResult.hybridReady)) { break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $taskResult.frameworkReady -or -not $taskResult.gameReady) { throw 'Timed out waiting for both original framework and game ready markers.' }
    if ($taskBuild.scriptingBackend -eq 'IL2CPP' -and -not $taskResult.hybridReady) { throw 'Timed out waiting for HybridCLR to confirm the selected release.' }
    if (-not $taskResult.codeVersionMatched) { throw 'The expected hot-update C# code version did not run.' }
    if (-not $taskResult.updateDiagnosticMatched) { throw 'The expected update rejection or rollback was not observed.' }
    # Keep the fully initialized title/HUD alive briefly before testing the real wantsToQuit shutdown path.
    Start-Sleep -Milliseconds 500
    $taskWindowDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not $taskResult.normalCloseRequested -and -not $taskProcess.HasExited -and [DateTime]::UtcNow -lt $taskWindowDeadline) {
        Request-TaskNormalClose
        if (-not $taskResult.normalCloseRequested) { Start-Sleep -Milliseconds 200 }
    }
    if (-not $taskResult.windowFound) { throw 'No real window belonging to the owned Player PID was found.' }
    if (-not $taskResult.normalCloseRequested) { throw 'The owned Player window did not accept a normal close request.' }
    $taskCloseDeadline = [DateTime]::UtcNow.AddSeconds($CloseTimeoutSeconds)
    while (-not $taskProcess.WaitForExit(200)) {
        if ([DateTime]::UtcNow -gt $taskCloseDeadline) { throw 'Player did not finish normal shutdown within the close timeout.' }
    }
    $taskResult.exitCode = $taskProcess.ExitCode
    $taskResult.normalExit = $taskProcess.ExitCode -eq 0
    if (-not $taskResult.normalExit) { throw "Player returned exit code $($taskProcess.ExitCode)." }
}
catch { $taskFailure = $_.Exception.Message }
finally {
    if ($taskProcess -and -not $taskProcess.HasExited) {
        # Cleanup never searches for Unity.exe/BigWorld.exe by name and never terminates another process.
        try { if (-not $taskResult.normalCloseRequested) { Request-TaskNormalClose } } catch { }
        if (-not $taskProcess.WaitForExit($CloseTimeoutSeconds * 1000)) {
            $taskResult.forcedKill = $true
            $taskProcess.Kill(); $taskProcess.WaitForExit()
        }
    }
    if ($taskProcess -and $taskProcess.HasExited) {
        $taskResult.exitCode = $taskProcess.ExitCode
        $taskResult.normalExit = $taskResult.normalCloseRequested -and -not $taskResult.forcedKill -and $taskProcess.ExitCode -eq 0
    }
    $taskText = Read-TaskPlayerLog
    $taskResult.errors = @(Get-TaskPlayerErrors $taskText)
    if (-not $taskFailure -and $taskResult.errors.Count -gt 0) { $taskFailure = 'Player logged an exception/assertion while shutting down.' }
    if (-not $taskFailure -and $taskResult.forcedKill) { $taskFailure = 'Player required forced termination after its owned PID timed out.' }
    $taskResult.failure = $taskFailure
    $taskResult.finishedUtc = [DateTime]::UtcNow.ToString('o')
    $taskResult.passed = -not $taskFailure -and $taskResult.frameworkReady -and $taskResult.gameReady -and $taskResult.normalExit
    $taskJson = $taskResult | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($taskReport, $taskJson, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $taskResults 'standalone-smoke.json'), $taskJson, [Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $taskLog) { Copy-Item -LiteralPath $taskLog -Destination (Join-Path $taskResults 'standalone-smoke.log') -Force }
}
if (-not $taskResult.passed) { throw "Windows Player smoke test failed: $taskFailure Report: $taskReport" }
[pscustomobject]@{ Passed = $true; ProcessId = $taskResult.pid; CloseMethod = $taskResult.closeMethod; Report = $taskReport; Log = $taskLog }
