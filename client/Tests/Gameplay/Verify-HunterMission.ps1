$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Add-Type -Path @((Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterMission.cs'),
    (Join-Path $projectRoot 'Assets\_Project\Game\Scripts\Flow\HunterExtractionAttempt.cs'),
    (Join-Path $PSScriptRoot 'HunterMissionExtractionChecks.cs'))
$script:checks = 0
function Assert-Equal($actual, $expected, $name) {
    if ($actual -ne $expected) { throw "$name : expected $expected, got $actual" }
    $script:checks++
}
$mission = [BigWorld.Gameplay.HunterMission]::new(3,10,250)
Assert-Equal $mission.Stage 'Tracking' 'Initial stage'
Assert-Equal $mission.Reward 0 'No reward before extraction'
Assert-Equal ($mission.PickUpBounty()) $false 'Cannot pick up missing bounty'
Assert-Equal ($mission.TickExtraction(100,$false,$false)) $false 'Exit still requires physical arrival'
Assert-Equal ($mission.Investigate('')) $false 'Reject empty clue'
Assert-Equal ($mission.Investigate('   ')) $false 'Reject whitespace clue'
Assert-Equal ($mission.Investigate('one')) $true 'First clue'
Assert-Equal ($mission.Investigate('one')) $false 'Clue only counts once'
Assert-Equal $mission.ClueCount 1 'Duplicate does not increment count'
Assert-Equal ($mission.Investigate('two')) $true 'Second clue'
Assert-Equal $mission.Stage 'Tracking' 'Not revealed too soon'
Assert-Equal ($mission.Investigate('three')) $true 'Final clue'
Assert-Equal $mission.Stage 'Hunting' 'Boss revealed'
Assert-Equal ($mission.Investigate('four')) $false 'No excess clues after reveal'
Assert-Equal ($mission.DefeatBoss()) $true 'Boss drops bounty'
Assert-Equal ($mission.DefeatBoss()) $false 'Boss death cannot duplicate bounty'
Assert-Equal $mission.Stage 'BountyAvailable' 'Bounty awaits pickup'
Assert-Equal $mission.SecondWaveStarted $false 'Boss kill alone does not spawn wave'
Assert-Equal ($mission.PickUpBounty()) $true 'Pickup accepted'
Assert-Equal $mission.SecondWaveStarted $true 'Pickup triggers second wave'
Assert-Equal ($mission.PickUpBounty()) $false 'Duplicate pickup rejected'
Assert-Equal ($mission.TickExtraction(3,$true,$false)) $false 'Partial extraction'
Assert-Equal $mission.ExtractionElapsed 3 'Elapsed time advances'
Assert-Equal ($mission.TickExtraction(0,$true,$false)) $false 'Pause cannot complete'
Assert-Equal $mission.ExtractionElapsed 3 'Pause freezes progress'
Assert-Equal ($mission.TickExtraction([float]::NaN,$true,$false)) $false 'Reject NaN time'
Assert-Equal ($mission.TickExtraction([float]::PositiveInfinity,$true,$false)) $false 'Reject infinite time'
Assert-Equal ($mission.TickExtraction(-1,$true,$false)) $false 'Reject negative time'
Assert-Equal $mission.ExtractionElapsed 3 'Invalid time does not corrupt progress'
Assert-Equal ($mission.TickExtraction(1,$false,$false)) $false 'Leaving exit interrupts'
Assert-Equal $mission.ExtractionElapsed 0 'Leaving resets countdown'
$mission.TickExtraction(4,$true,$false) | Out-Null
Assert-Equal ($mission.TickExtraction(1,$true,$true)) $false 'Enemy blocks extraction'
Assert-Equal $mission.ExtractionElapsed 0 'Contest resets countdown'
$mission.TickExtraction(4,$true,$false) | Out-Null
Assert-Equal ($mission.DropBounty()) $true 'Death drops carried bounty'
Assert-Equal $mission.ExtractionElapsed 0 'Death clears countdown'
Assert-Equal $mission.Stage 'BountyAvailable' 'Dropped bounty remains recoverable'
Assert-Equal $mission.Reward 0 'Failure earns no bounty'
Assert-Equal $mission.SecondWaveStarted $true 'Death does not erase wave identity'
Assert-Equal ($mission.TickExtraction(100,$false,$false)) $false 'Dropped bounty does not skip physical arrival'
Assert-Equal ($mission.PickUpBounty()) $true 'Recover dropped bounty'
Assert-Equal ($mission.TickExtraction(9,$true,$false)) $false 'Countdown must finish'
Assert-Equal $mission.Reward 0 'No partial reward'
Assert-Equal ($mission.TickExtraction(1,$true,$false)) $true 'Exact completion'
Assert-Equal $mission.Stage 'Extracted' 'Success stage'
Assert-Equal $mission.Reward 250 'Single settlement reward'
Assert-Equal ($mission.TickExtraction(10,$true,$false)) $false 'Settlement cannot repeat'
Assert-Equal ($mission.DropBounty()) $false 'Success cannot lose reward'
$mission.Reset()
Assert-Equal $mission.ClueCount 0 'Restart clears clues'
Assert-Equal $mission.SecondWaveStarted $false 'Restart clears wave'
Assert-Equal $mission.Reward 0 'Restart clears result'
Assert-Equal $mission.Stage 'Tracking' 'Restart starts new hunt'
Assert-Equal ($mission.DefeatBoss()) $true 'Finding boss without clues is allowed'
foreach ($config in @(@(0,10,250),@(3,0,250),@(3,-1,250),@(3,[float]::NaN,250),@(3,[float]::PositiveInfinity,250),@(3,10,-1))) {
    $rejected = $false
    try { [BigWorld.Gameplay.HunterMission]::new($config[0],$config[1],$config[2]) | Out-Null } catch { $rejected = $true }
    Assert-Equal $rejected $true 'Invalid configuration rejected'
}
[HunterMissionExtractionChecks]::Run([Action[bool,string]]{ param($ok,$name) Assert-Equal $ok $true $name })
Write-Output "PASS: $script:checks mission checks."
