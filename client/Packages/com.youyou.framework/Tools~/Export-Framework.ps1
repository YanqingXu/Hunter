param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$taskPackage = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskManifest = Get-Content -LiteralPath (Join-Path $taskPackage 'package.json') -Raw | ConvertFrom-Json
if (-not $OutputPath) { $OutputPath = Join-Path (Split-Path $taskPackage -Parent) ("YouYouFramework-" + $taskManifest.version + '.zip') }
$taskOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $taskOutput) { throw "Output already exists; choose another -OutputPath: $taskOutput" }
if ($taskOutput.StartsWith($taskPackage + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Place the archive outside the package directory.'
}
& (Join-Path $PSScriptRoot 'Update-Metadata.ps1')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskStream = [IO.File]::Open($taskOutput, [IO.FileMode]::CreateNew)
$taskArchive = New-Object IO.Compression.ZipArchive($taskStream, [IO.Compression.ZipArchiveMode]::Create)
$taskCount = 0
try {
    foreach ($taskFile in Get-ChildItem -LiteralPath $taskPackage -File -Recurse -Force) {
        $taskRelative = $taskFile.FullName.Substring($taskPackage.Length + 1).Replace('\', '/')
        if ($taskRelative -match '(^|/)(bin|obj|\.git)(/|$)') { continue }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskArchive, $taskFile.FullName,
            ($taskManifest.name + '/' + $taskRelative), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        $taskCount++
    }
} finally { $taskArchive.Dispose(); $taskStream.Dispose() }
Write-Output "Exported $taskCount files: $taskOutput"
Get-FileHash -LiteralPath $taskOutput -Algorithm SHA256
