param(
    [string]$UnityPath,
    [string]$OutputDirectory,
    [switch]$SkipCore
)
$ErrorActionPreference = 'Stop'
$taskPackage = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipCore) {
    & dotnet run --project (Join-Path $PSScriptRoot 'CoreChecks/CoreChecks.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core validation failed.' }
}
if (-not $UnityPath) { Write-Output 'Core checks complete. Supply -UnityPath to also validate Unity import and Play Mode.'; return }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw "Unity executable not found: $UnityPath" }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path ([IO.Path]::GetTempPath()) ('YouYouValidation-' + [guid]::NewGuid().ToString('N')) }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $taskOutput) { throw 'OutputDirectory must be a new directory; validation never overwrites an existing project.' }
foreach ($taskDir in @('Assets/Editor', 'Assets/Resources', 'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $taskOutput $taskDir) -Force | Out-Null
}
$taskManifest = @{ dependencies = @{ 'com.youyou.framework' = ('file:' + $taskPackage.Replace('\', '/')); 'com.unity.modules.imgui' = '1.0.0' } } | ConvertTo-Json -Depth 5
$taskUtf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $taskOutput 'Packages/manifest.json'), $taskManifest, $taskUtf8)
$taskEditorVersion = (Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion.Split('_')[0]
[IO.File]::WriteAllText((Join-Path $taskOutput 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: $taskEditorVersion`n", $taskUtf8)
[IO.File]::WriteAllText((Join-Path $taskOutput 'Assets/Resources/framework-validation.txt'), 'standalone-resource-ok', $taskUtf8)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'UnityChecks/FrameworkUnityChecks.cs') -Destination (Join-Path $taskOutput 'Assets/Editor')
foreach ($taskFile in @('FrameworkTestForm.cs','FrameworkPlayProbe.cs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "UnityChecks/$taskFile") -Destination (Join-Path $taskOutput 'Assets')
}
Copy-Item -LiteralPath (Join-Path $taskPackage 'Tests~/CoreChecks.cs') -Destination (Join-Path $taskOutput 'Assets')
Copy-Item -LiteralPath (Join-Path $taskPackage 'Samples~/QuickStart/QuickStart.cs') -Destination (Join-Path $taskOutput 'Assets')
$taskLog = Join-Path $taskOutput 'validation.log'
$taskArguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $taskOutput + '"'), '-executeMethod', 'FrameworkUnityChecks.Run', '-logFile', ('"' + $taskLog + '"'))
Write-Output "Unity validation project: $taskOutput"
$taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArguments -WindowStyle Hidden -PassThru
$taskDeadline = [DateTime]::UtcNow.AddMinutes(8)
while (-not $taskProcess.WaitForExit(1000)) {
    if ([DateTime]::UtcNow -gt $taskDeadline) { $taskProcess.Kill(); throw "Unity validation timed out. Inspect $taskLog" }
}
if ($taskProcess.ExitCode -ne 0) { throw "Unity validation failed (exit $($taskProcess.ExitCode)). Inspect $taskLog" }
if (-not (Select-String -LiteralPath $taskLog -SimpleMatch 'YOUYOU_FRAMEWORK_VALIDATION_PASSED' -Quiet)) {
    throw "Unity exited without completing validation. Inspect $taskLog"
}
Write-Output "Unity validation passed. Log: $taskLog"
