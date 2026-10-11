$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterPenetration.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterRoundDefinition.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Player\HunterWeapons.cs')
)
$script:checks = 0
function Assert-Equal($actual, $expected, $name) {
    if ($actual -ne $expected) { throw "$name : expected $expected, got $actual" }
    $script:checks++
}
function New-Loadout {
    $rifle = [BigWorld.Gameplay.HunterGunDefinition]::new()
    $rifle.Name='Rifle'; $rifle.MagazineCapacity=3; $rifle.FireInterval=.5
    $pistol = [BigWorld.Gameplay.HunterGunDefinition]::new()
    $pistol.Name='Pistol'; $pistol.Weight=1; $pistol.MagazineCapacity=2
    $ammo = [BigWorld.Gameplay.HunterAmmoDefinition]::new()
    $ammo.MaximumTotal=14; $ammo.RefillAmount=4
    [BigWorld.Gameplay.HunterWeapons]::new(@($rifle,$pistol),@($ammo),3)
}
$standard=[BigWorld.Gameplay.HunterAmmoType]::Standard
$guns=New-Loadout
Assert-Equal $guns.Magazine 3 'Rifle magazine'
Assert-Equal $guns.Reserve 9 'Reserve subtracts both magazines'
Assert-Equal ($guns.Total($standard)) 14 'Loaded rounds count towards total'
Assert-Equal ($guns.Refill()) 0 'Full supply is rejected'
Assert-Equal ($guns.TryReload()) $false 'Full magazine cannot reload'
Assert-Equal ($guns.TryFire()) $true 'First shot'
Assert-Equal $guns.Magazine 2 'Exactly one round spent'
Assert-Equal ($guns.TryFire()) $false 'Fire interval enforced'
Assert-Equal ($guns.TrySwitch(1)) $true 'Switch weapon'
Assert-Equal $guns.Magazine 2 'Pistol independent magazine'
Assert-Equal $guns.CanShoot $false 'Switch preserves fire interval'
$guns.Tick(.5)
Assert-Equal ($guns.TryFire()) $true 'Pistol fires after interval'
Assert-Equal ($guns.Total($standard)) 12 'Both weapons consume same ammo total'
Assert-Equal ($guns.Refill()) 2 'Refill stops at inclusive total cap'
Assert-Equal $guns.Reserve 11 'Refill enters reserve, not magazines'
Assert-Equal $guns.Magazine 1 'Refill does not reload'
Assert-Equal ($guns.TryReload()) $true 'Partial magazine reload'
Assert-Equal ($guns.TryFire()) $false 'Cannot fire while reloading'
$guns.Tick(0)
Assert-Equal $guns.Magazine 1 'Pause does not transfer ammunition'
$guns.Tick(1)
Assert-Equal $guns.Magazine 1 'Reload transfers only at completion'
$guns.CancelReload()
Assert-Equal $guns.Reserve 11 'Cancellation preserves reserve'
Assert-Equal $guns.Magazine 1 'Cancellation preserves magazine'
Assert-Equal ($guns.TryReload()) $true 'Reload can restart'
Assert-Equal ($guns.TryReload()) $false 'Duplicate reload rejected'
$guns.Tick(2)
Assert-Equal $guns.Magazine 2 'Completed reload fills missing round'
Assert-Equal $guns.Reserve 10 'Completed reload deducts exact reserve'
Assert-Equal ($guns.Total($standard)) 14 'Reload conserves total'
$guns.TrySwitch(0) | Out-Null
Assert-Equal $guns.Magazine 2 'Rifle retains earlier magazine'
$guns.TryReload() | Out-Null
Assert-Equal ($guns.TrySwitch(-1)) $false 'Invalid switch rejected'
Assert-Equal $guns.IsReloading $true 'Invalid switch does not cancel reload'
Assert-Equal ($guns.TrySwitch(0)) $false 'Same slot rejected'
Assert-Equal $guns.IsReloading $true 'Same slot does not cancel reload'
$guns.TrySwitch(1) | Out-Null
Assert-Equal $guns.IsReloading $false 'Valid switch cancels reload'
$guns.Reset()
for($i=0;$i -lt 3;$i++) { Assert-Equal ($guns.TryFire()) $true "Round $i"; $guns.Tick(.5) }
Assert-Equal $guns.Magazine 0 'Empty magazine'
Assert-Equal ($guns.TryFire()) $false 'Empty fire rejected'
Assert-Equal ($guns.TryReload()) $true 'Empty reload admitted'
$guns.Tick(1)
Assert-Equal $guns.Magazine 0 'No early reload transfer'
$guns.Tick(1)
Assert-Equal $guns.Magazine 3 'Reload completes at exact duration'
Assert-Equal $guns.Reserve 6 'Empty reload deducts capacity'
Assert-Equal ($guns.Total($standard)) 11 'Total conserved after empty reload'
Assert-Equal ($guns.Refill()) 3 'Shared caliber is replenished once'
$guns.Reset()
Assert-Equal $guns.ActiveIndex 0 'New run selects first weapon'
Assert-Equal ($guns.Total($standard)) 14 'New run restores total'

$rifle=[BigWorld.Gameplay.HunterGunDefinition]::new()
$rifle.Name='RifleSnapshot'
$rifle.MagazineCapacity=3
$shellGun=[BigWorld.Gameplay.HunterGunDefinition]::new()
$shellGun.Weight=1; $shellGun.MagazineCapacity=2; $shellGun.AmmoType=[BigWorld.Gameplay.HunterAmmoType]::Shotgun
$ammo=[BigWorld.Gameplay.HunterAmmoDefinition]::new(); $ammo.MaximumTotal=10; $ammo.RefillAmount=2
$shells=[BigWorld.Gameplay.HunterAmmoDefinition]::new(); $shells.Type=$shellGun.AmmoType; $shells.MaximumTotal=8; $shells.RefillAmount=3
$mixed=[BigWorld.Gameplay.HunterWeapons]::new(@($rifle,$shellGun),@($ammo,$shells),3)
$mixed.TryFire() | Out-Null; $mixed.Tick(1); $mixed.TryFire() | Out-Null; $mixed.Tick(1)
$mixed.TrySwitch(1) | Out-Null; $mixed.TryFire() | Out-Null
Assert-Equal ($mixed.Refill()) 3 'Different ammo types each receive capped refill'
Assert-Equal ($mixed.Total($standard)) 10 'Standard pool full'
Assert-Equal ($mixed.Total($shellGun.AmmoType)) 8 'Shell pool full'
$rifle.Name='Changed'; $rifle.MagazineCapacity=99
$mixed.TrySwitch(0) | Out-Null
Assert-Equal $mixed.Name 'RifleSnapshot' 'Runtime weapon configuration is snapshotted'
Assert-Equal $mixed.MagazineCapacity 3 'Runtime capacity unaffected by external edits'
$mixed.Tick([float]::NaN)
Assert-Equal $mixed.Magazine 1 'NaN time ignored'

$rifle.MagazineCapacity=3
$tiny=[BigWorld.Gameplay.HunterAmmoDefinition]::new(); $tiny.MaximumTotal=3
$empty=[BigWorld.Gameplay.HunterWeapons]::new(@($rifle),@($tiny),3)
$empty.TryFire() | Out-Null
Assert-Equal ($empty.TryReload()) $false 'No reserve rejects reload'
Assert-Equal ($empty.Refill()) 1 'Small capacity remains capped'
function Assert-Invalid($gunsToTest,$ammoToTest,$weight,$name) {
    $rejected=$false
    try { [BigWorld.Gameplay.HunterWeapons]::new($gunsToTest,$ammoToTest,$weight) | Out-Null } catch { $rejected=$true }
    Assert-Equal $rejected $true $name
}
Assert-Invalid @($rifle,$shellGun) @($ammo,$shells) 2 'Overweight rejected'
Assert-Invalid @($rifle) @($ammo,$ammo) 3 'Duplicate caliber rejected'
$tiny.MaximumTotal=2
Assert-Invalid @($rifle) @($tiny) 3 'Impossible magazine capacity rejected'
$rifle.ReloadSeconds=[float]::NaN
Assert-Invalid @($rifle) @($ammo) 3 'NaN configuration rejected'
"HUNTER_WEAPONS_PASSED: $script:checks checks"
