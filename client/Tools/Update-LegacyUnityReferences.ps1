param(
    [Parameter(Mandatory = $true)][string]$UnityPath,
    [string]$ProjectRoot
)
$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')) }
$taskEditor = Split-Path -Parent ([IO.Path]::GetFullPath($UnityPath))
$taskEngine = Join-Path $taskEditor 'Data\Managed\UnityEngine'
Add-Type -Path (Join-Path $taskEditor 'Data\Tools\ScriptUpdater\Unity.Cecil.dll')
$taskResolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$taskResolver.AddSearchDirectory($taskEngine)
$taskParameters = [Mono.Cecil.ReaderParameters]::new()
$taskParameters.AssemblyResolver = $taskResolver
$taskRoot = [IO.Path]::GetFullPath($ProjectRoot)
$taskResolver.AddSearchDirectory((Join-Path $taskRoot 'Assets\ThirdPlugins\Demigiant\DOTween'))
$taskResolver.AddSearchDirectory((Join-Path $taskRoot 'Assets\ThirdPlugins\Demigiant\DOTweenPro'))
$taskResolver.AddSearchDirectory((Join-Path $taskRoot 'Assets\ThirdPlugins\Demigiant\DemiLib'))
$taskBackup = Join-Path $taskRoot ('Library\LegacyUnityReferenceBackup\' + [guid]::NewGuid().ToString('N'))
# These bundled pre-Unity-5 assemblies reference the obsolete UnityEngine monolith.
# Resolve the existing type forwarders and point each reference at its current module.
# No method bodies or plugin behavior are changed. Repeated execution is a no-op.
$taskPaths = @(
    'Assets/ThirdPlugins/Demigiant/DOTween/DOTween.dll',
    'Assets/ThirdPlugins/Demigiant/DOTween/DOTween43.dll',
    'Assets/ThirdPlugins/Demigiant/DOTween/DOTween46.dll',
    'Assets/ThirdPlugins/Demigiant/DOTween/DOTween50.dll',
    'Assets/ThirdPlugins/Demigiant/DOTweenPro/DOTweenPro.dll',
    'Assets/ThirdPlugins/Demigiant/DemiLib/DemiLib.dll'
)
try {
    foreach ($taskRelative in $taskPaths) {
        $taskTarget = [IO.Path]::GetFullPath((Join-Path $taskRoot $taskRelative))
        if (-not $taskTarget.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid assembly path.' }
        $taskAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($taskTarget, $taskParameters)
        try {
            $taskReferences = @($taskAssembly.MainModule.GetTypeReferences() | Where-Object { $_.Scope.Name -eq 'UnityEngine' })
            if ($taskReferences.Count -eq 0) { continue }
            if ($taskAssembly.Name.HasPublicKey) { throw "Signed assembly requires re-signing: $taskRelative" }
            foreach ($taskReference in $taskReferences) {
                $taskDefinition = $taskReference.Resolve()
                if (-not $taskDefinition) { throw "Cannot resolve $($taskReference.FullName)." }
                $taskReference.Scope = $taskAssembly.MainModule.ImportReference($taskDefinition).Scope
            }
            foreach ($taskReference in @($taskAssembly.MainModule.AssemblyReferences | Where-Object Name -eq 'UnityEngine')) {
                $taskAssembly.MainModule.AssemblyReferences.Remove($taskReference) | Out-Null
            }
            $taskSaved = Join-Path $taskBackup $taskRelative
            New-Item -ItemType Directory -Path (Split-Path -Parent $taskSaved) -Force | Out-Null
            Copy-Item -LiteralPath $taskTarget -Destination $taskSaved
            $taskAssembly.Write($taskSaved + '.updated')
            if ((Get-Item -LiteralPath ($taskSaved + '.updated')).Length -lt 512) { throw 'Invalid assembly output.' }
            Copy-Item -LiteralPath ($taskSaved + '.updated') -Destination $taskTarget
            Write-Output ("Retargeted {0} type references: {1}" -f $taskReferences.Count, $taskRelative)
        }
        finally { $taskAssembly.Dispose() }
    }
}
finally { $taskResolver.Dispose() }
