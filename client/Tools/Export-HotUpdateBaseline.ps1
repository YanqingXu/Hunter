param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskConfig = Get-Content -LiteralPath (Join-Path $taskProject 'ProjectSettings\HotUpdateProject.json') -Raw | ConvertFrom-Json
if ([string]$taskConfig.baseId -notmatch '^bw-[a-f0-9]{32}$') { throw 'No valid HybridCLR base ID. Build a Player first.' }
$taskPlayer = Get-Content -LiteralPath (Join-Path $taskProject 'Builds\latest-windows.json') -Raw | ConvertFrom-Json
if ($taskPlayer.result -ne 'Succeeded' -or $taskPlayer.scriptingBackend -ne 'IL2CPP' -or -not $taskPlayer.runtimeFilesVerified) { throw 'A successful HybridCLR Player is required.' }
$taskShippedManifest = Get-Content -LiteralPath (Join-Path $taskPlayer.output 'BigWorld_Data\StreamingAssets\hotupdate\manifest.json') -Raw | ConvertFrom-Json
if ($taskShippedManifest.baseId -ne $taskConfig.baseId) { throw 'Current baseline does not belong to the latest successful Player.' }
$taskBase = Join-Path $taskProject ('Builds\HotUpdate\Bases\' + $taskConfig.baseId)
foreach ($taskRequired in @('manifest.json','config.json','StrippedAOT','CompiledAOT','content')) {
    if (-not (Test-Path -LiteralPath (Join-Path $taskBase $taskRequired))) { throw "Incomplete HybridCLR baseline: $taskRequired" }
}
$taskDestination = Join-Path $taskProject 'Builds\HotUpdate\BaselineArchives'
New-Item -ItemType Directory -Path $taskDestination -Force | Out-Null
$taskArchive = Join-Path $taskDestination ($taskConfig.baseId + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.zip')
$taskZip = [IO.Compression.ZipFile]::Open($taskArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskBase -Recurse -File) {
        $taskRelative = $taskFile.FullName.Substring($taskProject.Length + 1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip, $taskFile.FullName, $taskRelative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $taskZip.Dispose() }
[pscustomobject]@{ Archive = $taskArchive; BaseId = $taskConfig.baseId; SizeMB = [math]::Round((Get-Item -LiteralPath $taskArchive).Length / 1MB, 2) }
