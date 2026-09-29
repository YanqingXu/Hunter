param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe',
    [switch]$Development
)
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'Unity executable not found.' }
$taskLogs = Join-Path $taskProject 'Builds\Logs'
New-Item -ItemType Directory -Path $taskLogs -Force | Out-Null
$taskLog = Join-Path $taskLogs ('windows-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
$taskMethod = if ($Development) { 'BigWorld.Editor.BigWorldBuild.WindowsDevelopment' } else { 'BigWorld.Editor.BigWorldBuild.WindowsRelease' }
$taskArgs = @('-batchmode','-quit','-projectPath',('"' + $taskProject + '"'),'-executeMethod',$taskMethod,'-logFile',('"' + $taskLog + '"'))
$taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
while (-not $taskProcess.WaitForExit(1000)) { }
if ($taskProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $taskLog -SimpleMatch 'BIGWORLD_WINDOWS_BUILD_SUCCEEDED' -Quiet)) {
    throw "Build failed. See $taskLog"
}
Get-Content -LiteralPath (Join-Path $taskProject 'Builds\latest-windows.json')
