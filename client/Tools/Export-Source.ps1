param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
foreach ($taskChecks in @('Assets\__Framework2DChecks', 'Assets\__GameplayChecks', 'Assets\YouYouFullValidation', 'Assets\__MapCollisionChecks', 'Assets\__HybridCLRGameplayChecks')) {
    if (Test-Path -LiteralPath (Join-Path $taskProject $taskChecks)) { throw 'Wait for the integration checks to finish before exporting source.' }
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $taskProject 'Builds\Source' }
$taskOutput = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($taskSourceName in @('Assets','Packages','ProjectSettings','Docs','Tools','Tests')) {
    $taskSourceRoot = Join-Path $taskProject $taskSourceName
    if ($taskOutput.StartsWith($taskSourceRoot + '\', [StringComparison]::OrdinalIgnoreCase) -or $taskOutput -eq $taskSourceRoot) { throw 'Source archives must be outside source directories.' }
}
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskArchive = Join-Path $taskOutput ('BigWorld-Source-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.zip')
$taskFiles = @(foreach ($taskName in @('Assets','Packages','ProjectSettings','Docs','Tools','Tests')) {
    $taskPath = Join-Path $taskProject $taskName
    if (Test-Path -LiteralPath $taskPath) {
        Get-ChildItem -LiteralPath $taskPath -File -Recurse -Force | Where-Object {
            $taskName -notin @('Tests','Tools') -or $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
        }
    }
})
foreach ($taskName in @('README.md','.gitignore')) {
    $taskPath = Join-Path $taskProject $taskName
    if (Test-Path -LiteralPath $taskPath) { $taskFiles += Get-Item -LiteralPath $taskPath -Force }
}
$taskZip = [IO.Compression.ZipFile]::Open($taskArchive, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($taskFile in $taskFiles) {
        $taskRelative = $taskFile.FullName.Substring($taskProject.Length+1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip, $taskFile.FullName, 'BigWorld/' + $taskRelative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $taskZip.Dispose() }
[pscustomobject]@{Archive=$taskArchive;Files=$taskFiles.Count;SizeMB=[math]::Round((Get-Item -LiteralPath $taskArchive).Length/1MB,2)}

