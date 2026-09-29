$ErrorActionPreference = 'Stop'
$taskPackage = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskUtf8 = New-Object Text.UTF8Encoding($false)
$taskItems = Get-ChildItem -LiteralPath $taskPackage -Recurse -Force | Where-Object {
    $taskRelative = $_.FullName.Substring($taskPackage.Length + 1)
    $taskRelative -notmatch '(^|[\\/])([^\\/]*~|bin|obj|\.git)([\\/]|$)' -and
    $_.Name -notlike '.*' -and $_.Extension -ne '.meta'
}
foreach ($taskItem in $taskItems) {
    $taskMeta = $taskItem.FullName + '.meta'
    if (Test-Path -LiteralPath $taskMeta) { continue }
    $taskGuid = [guid]::NewGuid().ToString('N')
    $taskContent = "fileFormatVersion: 2`nguid: $taskGuid`n"
    if ($taskItem.PSIsContainer) {
        $taskContent += "folderAsset: yes`nDefaultImporter:`n  externalObjects: {}`n"
    } elseif ($taskItem.Extension -eq '.cs') {
        $taskContent += "MonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n"
    } elseif ($taskItem.Extension -eq '.asmdef') {
        $taskContent += "AssemblyDefinitionImporter:`n  externalObjects: {}`n"
    } else {
        $taskContent += "DefaultImporter:`n  externalObjects: {}`n"
    }
    $taskContent += "  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
    [IO.File]::WriteAllText($taskMeta, $taskContent, $taskUtf8)
}
Write-Output 'Unity metadata checked; existing GUIDs preserved.'
