param([string]$Method, [string]$LogName = 'compile-latest.log', [switch]$PlayMode)
$ErrorActionPreference = 'Stop'
$migrationProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$migrationLog = Join-Path $migrationProject ('Logs\' + $LogName)
$migrationArgs = @('-batchmode', '-projectPath', ('"' + $migrationProject + '"'), '-logFile', ('"' + $migrationLog + '"'))
if (-not $PlayMode) { $migrationArgs += '-quit' }
if ($Method) { $migrationArgs += @('-executeMethod', $Method) }
$migrationProcess = Start-Process -FilePath 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe' -ArgumentList $migrationArgs -WindowStyle Hidden -PassThru
'Unity PID: ' + $migrationProcess.Id
while (-not $migrationProcess.WaitForExit(1000)) { }
'Unity exit: ' + $migrationProcess.ExitCode
Select-String -LiteralPath $migrationLog -Pattern '^.*error CS\d+.*|YOUYOU_FULL_.*' | ForEach-Object Line | Sort-Object -Unique
if ($migrationProcess.ExitCode -ne 0) { throw "Unity failed; see $migrationLog" }
