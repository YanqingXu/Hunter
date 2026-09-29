param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe',
    [switch]$Development
)
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'Unity executable not found.' }
$taskLogs = Join-Path $taskProject 'Builds\Logs'
New-Item -ItemType Directory -Path $taskLogs -Force | Out-Null
$taskStamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'

function Invoke-TaskUnityPhase([string]$Method, [string]$Log, [string]$SuccessMarker) {
    $taskArgs = @('-batchmode','-quit','-buildTarget','Win64','-projectPath',('"' + $taskProject + '"'),'-executeMethod',$Method,'-logFile',('"' + $Log + '"'))
    $taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
    while (-not $taskProcess.WaitForExit(1000)) { }
    if ($taskProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $Log) -or
        -not (Select-String -LiteralPath $Log -SimpleMatch $SuccessMarker -Quiet)) {
        throw "Unity phase failed: $Method. See $Log"
    }
}

# Removing DISABLE_ASSETBUNDLE changes compiled code. A fresh Unity process must compile it before BuildPlayer.
$taskPrepareLog = Join-Path $taskLogs ('windows-prepare-' + $taskStamp + '.log')
Invoke-TaskUnityPhase 'BigWorld.Editor.BigWorldBuild.PrepareWindowsBuild' $taskPrepareLog 'BIGWORLD_WINDOWS_PREPARATION_SUCCEEDED'
$taskLog = Join-Path $taskLogs ('windows-' + $taskStamp + '.log')
$taskMethod = if ($Development) { 'BigWorld.Editor.BigWorldBuild.WindowsDevelopment' } else { 'BigWorld.Editor.BigWorldBuild.WindowsRelease' }
Invoke-TaskUnityPhase $taskMethod $taskLog 'BIGWORLD_WINDOWS_BUILD_SUCCEEDED'
Get-Content -LiteralPath (Join-Path $taskProject 'Builds\latest-windows.json')
