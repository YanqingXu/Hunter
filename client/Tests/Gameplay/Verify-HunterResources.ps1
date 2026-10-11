$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterResources.cs')
$script:checks = 0
function Assert-Close($actual, $expected, $name) {
    if ([math]::Abs($actual - $expected) -gt .001) { throw "$name : expected $expected, got $actual" }
    $script:checks++
}
$stamina = [BigWorld.Gameplay.HunterStamina]::new(100, 2, 20)
Assert-Close $stamina.Current 100 'Full on spawn'
if (!$stamina.TrySpend(25)) { throw 'Melee should succeed' }
Assert-Close $stamina.Current 75 'Melee cost'
$stamina.Tick(1, $false)
Assert-Close $stamina.Current 75 'Recovery delay'
$stamina.Tick(1.5, $false)
Assert-Close $stamina.Current 85 'Only time after delay regenerates'
$stamina.Tick(10, $true)
Assert-Close $stamina.Current 85 'Sprint blocks recovery without spending'
$stamina.Tick(2, $false)
Assert-Close $stamina.Current 85 'Sprint restarts delay'
$stamina.InterruptRecovery()
$stamina.Tick(2, $false)
Assert-Close $stamina.Current 85 'Jump interrupts without spending'
$stamina.Tick(10, $false)
Assert-Close $stamina.Current 100 'Recovery capped'
if (!$stamina.TrySpend(100) -or $stamina.TrySpend(25)) { throw 'Exhaustion admission failed' }
Assert-Close $stamina.Current 0 'Failed attack spends nothing'
$stamina.Tick(0, $false)
Assert-Close $stamina.Current 0 'Paused simulation'
$stamina.Tick([float]::NaN, $false)
Assert-Close $stamina.Current 0 'Invalid delta ignored'
if ($stamina.TrySpend(-1) -or $stamina.TrySpend([float]::NaN)) { throw 'Invalid costs accepted' }
$stamina.Reset()
Assert-Close $stamina.Current 100 'Respawn reset'
$segments = [float[]]@(50, 50, 25, 25)
foreach ($case in @(@(60,100), @(80,100), @(100,100), @(101,125), @(125,125), @(126,150), @(0,0))) {
    Assert-Close ([BigWorld.Gameplay.HunterHealthSegments]::RecoveryCeiling($case[0],150,$segments)) $case[1] "Health $($case[0])"
}
if ([BigWorld.Gameplay.HunterHealthSegments]::IsValid(100,$segments) -or
    [BigWorld.Gameplay.HunterHealthSegments]::IsValid(150,[float[]]@(50,0,100))) { throw 'Invalid segments accepted' }
"HUNTER_RESOURCES_PASSED: $script:checks numeric checks plus admission/configuration guards"
