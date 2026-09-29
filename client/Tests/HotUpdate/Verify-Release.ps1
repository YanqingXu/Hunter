param([Parameter(Mandatory=$true)][string]$PythonPath, [switch]$IncludeGameplay)
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskResults = Join-Path $taskProject 'TestResults\HybridCLR'
$taskStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$taskRun = Join-Path $taskResults ('player-' + $taskStamp)
New-Item -ItemType Directory -Path $taskRun -Force | Out-Null
$taskBuild = Get-Content -LiteralPath (Join-Path $taskProject 'Builds\latest-windows.json') -Raw | ConvertFrom-Json
if ($taskBuild.scriptingBackend -ne 'IL2CPP') { throw 'An IL2CPP Player is required.' }
$taskConfig = Get-Content -LiteralPath (Join-Path $taskProject 'ProjectSettings\HotUpdateProject.json') -Raw | ConvertFrom-Json
if ([string]$taskConfig.baseId -notmatch '^bw-[a-f0-9]{32}$') { throw 'Invalid main Player base ID.' }
$taskBaseManifest = Get-Content -LiteralPath (Join-Path $taskBuild.output 'BigWorld_Data\StreamingAssets\hotupdate\manifest.json') -Raw | ConvertFrom-Json
if ($taskBaseManifest.baseId -ne $taskConfig.baseId) { throw 'Project and Player base IDs disagree.' }
$taskSettings = Get-Content -LiteralPath (Join-Path $taskProject 'ProjectSettings\ProjectSettings.asset') -Raw
if ($taskSettings -notmatch '(?m)^  companyName: DefaultCompany\r?$' -or $taskSettings -notmatch '(?m)^  productName: BigWorld\r?$') {
    throw 'Update this project-specific cache location when changing the Player company/product names.'
}
$taskCacheParent = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\BigWorld\HybridCLR'))
$taskCache = [IO.Path]::GetFullPath((Join-Path $taskCacheParent $taskConfig.baseId))
if (-not $taskCache.StartsWith($taskCacheParent + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid cache scope.' }
$taskCode = Join-Path $taskProject 'Assets\_Project\Game\Scripts\Flow\GameEntryPoint.cs'
$taskOriginalCode = [IO.File]::ReadAllBytes($taskCode)
$taskText = [Text.Encoding]::UTF8.GetString($taskOriginalCode)
if ($taskText -notmatch 'public const string CodeVersion = "1";') { throw 'This proof expects CodeVersion 1 in the main Player.' }
$taskNative = Join-Path $taskBuild.output 'GameAssembly.dll'
$taskExe = Join-Path $taskBuild.output 'BigWorld.exe'
$taskBeforeNative = (Get-FileHash -LiteralPath $taskNative -Algorithm SHA256).Hash
$taskBeforeExe = (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash
$taskServer = $null
$taskLatestPatchPath = Join-Path $taskProject 'Builds\HotUpdate\latest-patch.json'
$taskPreviousPatchReport = if (Test-Path -LiteralPath $taskLatestPatchPath) { [IO.File]::ReadAllBytes($taskLatestPatchPath) } else { $null }
$taskSavedCache = Join-Path $taskRun 'original-cache'
$taskCases = @()
$taskFixture = [IO.Path]::GetFullPath((Join-Path $taskProject 'Assets\__HybridCLRGameplayChecks'))
if (Test-Path -LiteralPath $taskFixture) { throw 'Gameplay fixture already exists; inspect it before testing.' }
$taskPassed = $false
$taskFailure = $null
function Invoke-TaskCase([string]$Name, [string]$Version, [string]$Url = '', [string]$Diagnostic = '', [bool]$Gameplay = $false) {
    $taskMarker = if ($Gameplay) { 'BIGWORLD_GAMEPLAY_VALIDATION_PASSED' } else { 'BIGWORLD_GAME_READY:' }
    & (Join-Path $taskProject 'Tools\Verify-WindowsPlayer.ps1') -StartupTimeoutSeconds 360 -ExpectedCodeVersion $Version -UpdateUrl $Url -ExpectedUpdateDiagnostic $Diagnostic -GameReadyMarker $taskMarker -RunGameplayChecks:$Gameplay | Out-Null
    $taskReport = Get-Content -LiteralPath (Join-Path $taskProject 'TestResults\VerticalSlice\standalone-smoke.json') -Raw | ConvertFrom-Json
    if (-not $taskReport.passed) { throw "Failed case: $Name" }
    Copy-Item -LiteralPath (Join-Path $taskProject 'TestResults\VerticalSlice\standalone-smoke.json') -Destination (Join-Path $taskRun ($Name + '.json'))
    $script:taskCases += [pscustomobject]@{ name = $Name; passed = $true; codeVersion = $Version; log = $taskReport.log }
    Write-Output "Passed: $Name (code $Version)"
}
try {
    if (Test-Path -LiteralPath $taskCache) {
        if ((Resolve-Path -LiteralPath $taskCache).Path -ne $taskCache) { throw 'Cache identity changed.' }
        Move-Item -LiteralPath $taskCache -Destination $taskSavedCache
    }
    Invoke-TaskCase 'baseline' '1'
    try {
        $taskPatchedCode = $taskText.Replace('public const string CodeVersion = "1";', 'public const string CodeVersion = "2";')
        if ($IncludeGameplay) {
            New-Item -ItemType Directory -Path $taskFixture | Out-Null
            $taskProbe = [IO.File]::ReadAllText((Join-Path $taskProject 'Tests\Gameplay\GameplayPlayChecks.cs'))
            # Keep every gameplay assertion; omit image export, which is unrelated to the hot-update contract.
            $taskProbe = [regex]::Replace($taskProbe, '(?s)    private IEnumerator Capture\(string name\).*?(?=    private void ClickButton)', "    private IEnumerator Capture(string name) { yield return null; }`r`n`r`n")
            [IO.File]::WriteAllText((Join-Path $taskFixture 'GameplayPlayChecks.cs'), $taskProbe, [Text.UTF8Encoding]::new($false))
            Copy-Item -LiteralPath (Join-Path $taskProject 'Tests\PureCSharp\PureCSharpChecks.cs') -Destination $taskFixture
            $taskHook = 'if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-bigworld-validate-gameplay") >= 0) { var probe = new GameObject("HybridCLR Gameplay Validation"); Object.DontDestroyOnLoad(probe); probe.AddComponent<GameplayPlayChecks>(); }'
            $taskPatchedCode = $taskPatchedCode.Replace('Debug.Log("BIGWORLD_HYBRIDCLR_CODE_VERSION: " + CodeVersion);', 'Debug.Log("BIGWORLD_HYBRIDCLR_CODE_VERSION: " + CodeVersion);' + "`r`n            " + $taskHook)
        }
        [IO.File]::WriteAllText($taskCode, $taskPatchedCode, [Text.UTF8Encoding]::new($false))
        & (Join-Path $taskProject 'Tools\Build-HotUpdatePatch.ps1') | Out-Null
    } finally {
        [IO.File]::WriteAllBytes($taskCode, $taskOriginalCode)
        if (Test-Path -LiteralPath $taskFixture) {
            if ((Resolve-Path -LiteralPath $taskFixture).Path -ne (Join-Path $taskProject 'Assets\__HybridCLRGameplayChecks')) { throw 'Unexpected fixture path.' }
            Move-Item -LiteralPath $taskFixture -Destination (Join-Path $taskRun 'gameplay-fixture')
            if (Test-Path -LiteralPath ($taskFixture + '.meta')) { Move-Item -LiteralPath ($taskFixture + '.meta') -Destination (Join-Path $taskRun 'gameplay-fixture.meta') }
        }
    }
    $taskPatch = Get-Content -LiteralPath (Join-Path $taskProject 'Builds\HotUpdate\latest-patch.json') -Raw | ConvertFrom-Json
    if ($taskPatch.baseId -ne $taskConfig.baseId) { throw 'Patch base changed.' }
    [IO.File]::WriteAllText((Join-Path $taskPatch.output 'VALIDATION-ONLY.txt'), 'This code-version proof may contain a gameplay test probe. Do not deploy it as a production update.')
    $taskRemote = Join-Path $taskRun 'remote'
    New-Item -ItemType Directory -Path $taskRemote | Out-Null
    Copy-Item -LiteralPath $taskPatch.output -Destination (Join-Path $taskRemote 'valid') -Recurse
    Copy-Item -LiteralPath $taskPatch.output -Destination (Join-Path $taskRemote 'corrupt') -Recurse
    $taskCorruptManifest = Join-Path $taskRemote 'corrupt\manifest.json'
    $taskCorrupt = Get-Content -LiteralPath $taskCorruptManifest -Raw | ConvertFrom-Json
    $taskCorrupt.version += '-corrupt-test'
    $taskCorrupt | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskCorruptManifest -Encoding utf8NoBOM
    [IO.File]::WriteAllText((Join-Path $taskRemote 'corrupt\hotupdate\assemblies\Assembly-CSharp.dll.bytes'), 'broken-download')
    New-Item -ItemType Directory -Path (Join-Path $taskRemote 'wrong-base') | Out-Null
    $taskWrong = Get-Content -LiteralPath (Join-Path $taskPatch.output 'manifest.json') -Raw | ConvertFrom-Json
    $taskWrong.baseId = 'bw-wrong-base'
    $taskWrong | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRemote 'wrong-base\manifest.json') -Encoding utf8NoBOM
    $taskListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $taskListener.Start(); $taskPort = $taskListener.LocalEndpoint.Port; $taskListener.Stop()
    $taskServer = Start-Process -FilePath $PythonPath -ArgumentList @('-m','http.server', $taskPort, '--bind','127.0.0.1','--directory',('"' + $taskRemote + '"')) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRun 'http-out.log') -RedirectStandardError (Join-Path $taskRun 'http-error.log')
    $taskOrigin = 'http://127.0.0.1:' + $taskPort
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 30; $taskAttempt++) {
        try { Invoke-WebRequest ($taskOrigin + '/valid/manifest.json') -TimeoutSec 1 | Out-Null; $taskReady = $true; break } catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $taskReady) { throw 'Local test server failed to start.' }
    Invoke-TaskCase 'remote-code-update' '2' ($taskOrigin + '/valid/manifest.json')
    Invoke-TaskCase 'cached-code-without-update-url' '2'
    Invoke-TaskCase 'corrupt-download-fallback' '2' ($taskOrigin + '/corrupt/manifest.json') 'BIGWORLD_HYBRIDCLR_UPDATE_REJECTED:'
    Invoke-TaskCase 'wrong-base-fallback' '2' ($taskOrigin + '/wrong-base/manifest.json') 'BIGWORLD_HYBRIDCLR_UPDATE_REJECTED:'
    if ($IncludeGameplay) {
        Invoke-TaskCase 'actual-2d-gameplay-on-hot-code' '2' '' '' $true
        Copy-Item -LiteralPath (Join-Path $taskBuild.output 'TestResults\VerticalSlice\play-checks.json') -Destination (Join-Path $taskRun 'gameplay-checks.json')
    }
    # Simulate a startup interrupted before WorldReady by leaving the loader's pending marker.
    $taskActive = [IO.File]::ReadAllText((Join-Path $taskCache 'active.txt'))
    [IO.File]::WriteAllText((Join-Path $taskCache 'booting.txt'), $taskActive)
    Invoke-TaskCase 'incomplete-startup-marker-rollback' '1' '' 'BIGWORLD_HYBRIDCLR_ROLLBACK:'
    Invoke-TaskCase 'quarantined-release-rejected' '1' ($taskOrigin + '/valid/manifest.json') 'BIGWORLD_HYBRIDCLR_REJECTED:'
    if ((Get-FileHash -LiteralPath $taskNative -Algorithm SHA256).Hash -ne $taskBeforeNative -or
        (Get-FileHash -LiteralPath $taskExe -Algorithm SHA256).Hash -ne $taskBeforeExe) { throw 'The main Player changed while testing its patch.' }
    $taskPassed = $true
} catch { $taskFailure = $_.ToString(); throw }
finally {
    [IO.File]::WriteAllBytes($taskCode, $taskOriginalCode)
    if ($taskServer -and -not $taskServer.HasExited) { $taskServer.Kill(); $taskServer.WaitForExit() }
    if (Test-Path -LiteralPath $taskCache) {
        if ((Resolve-Path -LiteralPath $taskCache).Path -ne $taskCache) { throw 'Cache identity changed; refusing to move it.' }
        $taskArchiveCache = [IO.Path]::GetFullPath((Join-Path $taskRun 'tested-cache'))
        if (-not $taskArchiveCache.StartsWith($taskRun + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid archive destination.' }
        Move-Item -LiteralPath $taskCache -Destination $taskArchiveCache
    }
    if (Test-Path -LiteralPath $taskSavedCache) { Move-Item -LiteralPath $taskSavedCache -Destination $taskCache }
    if (Test-Path -LiteralPath $taskLatestPatchPath) {
        # Preserve the test report without replacing the project's latest production patch pointer.
        $taskExpectedPatchPath = [IO.Path]::GetFullPath((Join-Path $taskProject 'Builds\HotUpdate\latest-patch.json'))
        if ((Resolve-Path -LiteralPath $taskLatestPatchPath).Path -ne $taskExpectedPatchPath) { throw 'Unexpected patch report path.' }
        Move-Item -LiteralPath $taskLatestPatchPath -Destination (Join-Path $taskRun 'validation-patch-report.json')
        if ($taskPreviousPatchReport) { [IO.File]::WriteAllBytes($taskLatestPatchPath, $taskPreviousPatchReport) }
    }
    $taskSummary = [pscustomobject]@{ passed = $taskPassed; baseId = $taskConfig.baseId; player = $taskBuild.output; patch = $taskPatch.output; cases = $taskCases; gameAssemblySha256 = $taskBeforeNative; exeSha256 = $taskBeforeExe; failure = $taskFailure; results = $taskRun }
    $taskSummary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskRun 'summary.json') -Encoding utf8NoBOM
    $taskSummary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskResults 'player-latest.json') -Encoding utf8NoBOM
}
$taskSummary
