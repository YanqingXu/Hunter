param([string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskLog = Join-Path $taskProject ('Builds\Logs\hotupdate-patch-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.log')
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($taskLog)) -Force | Out-Null
$taskArgs = @('-batchmode','-quit','-buildTarget','Win64','-projectPath',('"'+$taskProject+'"'),'-executeMethod','BigWorld.HotUpdate.Editor.HybridProjectBuild.BuildPatch','-logFile',('"'+$taskLog+'"'))
$taskProcess = Start-Process -FilePath $UnityPath -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
while (-not $taskProcess.WaitForExit(1000)) { }
if ($taskProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $taskLog -SimpleMatch 'BIGWORLD_HYBRIDCLR_PATCH_SUCCEEDED' -Quiet)) {
    throw "HybridCLR patch build failed. See $taskLog"
}
Get-Content -LiteralPath (Join-Path $taskProject 'Builds/HotUpdate/latest-patch.json')
