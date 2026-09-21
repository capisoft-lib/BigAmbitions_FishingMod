$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = [IO.Path]::GetFullPath((Join-Path $modRoot '..\..\..'))
$output = Join-Path $projectRoot 'Output\FishingMod'
$dll = Join-Path $output 'FishingMod.dll'
$manifest = Join-Path $output 'ModManifest.asset'
foreach ($required in @($dll, $manifest, (Join-Path $output 'README.md'), (Join-Path $output 'CHANGELOG.md'), (Join-Path $output 'THIRD_PARTY_NOTICES.md'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing package file: $required" }
}
$dllNames = @(Get-ChildItem -LiteralPath $output -Recurse -Filter '*.dll' -File |
    ForEach-Object { $_.FullName.Substring($output.Length + 1).Replace('\', '/') } |
    Sort-Object)
$expectedDllNames = @('Dependencies/0Harmony.dll', 'FishingMod.dll')
if (($dllNames -join '|') -ne ($expectedDllNames -join '|')) {
    throw "Unexpected bundled DLL(s): $($dllNames -join ', ')"
}
$harmonyDll = Join-Path $output 'Dependencies\0Harmony.dll'
if ([Reflection.AssemblyName]::GetAssemblyName($harmonyDll).Name -ne '0Harmony') {
    throw 'Harmony dependency identity is invalid.'
}
$cecilPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\il2cpp\build\deploy\Mono.Cecil.dll'
Add-Type -Path $cecilPath
$compiledAssembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)
try {
    $references = @($compiledAssembly.MainModule.AssemblyReferences | Select-Object -ExpandProperty Name)
    if ('0Harmony' -notin $references) {
        throw 'FishingMod must reference the packaged Harmony assembly.'
    }
    if ($compiledAssembly.Name.Version.ToString() -ne '1.0.1.0') {
        throw "Unexpected FishingMod assembly version $($compiledAssembly.Name.Version)."
    }
}
finally {
    $compiledAssembly.Dispose()
}
$bytes = [IO.File]::ReadAllBytes($dll)
$ascii = [Text.Encoding]::ASCII.GetString($bytes)
$unicode = [Text.Encoding]::Unicode.GetString($bytes)
$unicodeOffset = [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
foreach ($marker in @(
    'FishingMod_CastVisual',
    'FishingMod_Runtime',
    'FishingQteSession',
    'FishingBiteRules',
    'FishingBiteTimer',
    'FishingInputRules',
    'TryCancelPendingActivity',
    'AddHdrpSurface',
    'TryGetStaticWaterPoint',
    'IsHamptonsMarinaMousePlane', 'IsHamptonsMarinaGroundVolume', 'IsHamptonsEastCoastOccluder', 'IsVerifiedSupport', 'StartCastAtCurrentPosition', 'IsPlayerNearWater', 'DistanceToWater', 'ConstrainGrip',
    'GameWaterAtlas',
    'LastMatchedZoneId',
    'ContainsFootprint',
    'FishingEconomyService',
    'FishingEconomyRules',
    'TryClaimSettlement',
    'ChangeMoneySafe',
    'FishingHappinessService',
    'FishingModInitializationEntry',
    'ModEntryOnInitializationLoadAttribute',
    'RegisterDefinitions',
    'ApplyFishingActivity',
    'ApplyCatch',
    'AdvanceFight',
    'DrawControlWheel',
    'SetHandIKTargets',
    'ResetWalkingAnimation',
    'RaycastNonAlloc',
    'SurfaceCellKey',
    'IndexedTileCount',
    'CacheBuildCount',
    'AdvanceIndexing',
    'CancelIndexing',
    'FishingAudio',
    'FishingWaveDecoder',
    'ConsumeReleaseSoundEvent',
    'ConsumeSplashSoundEvent'
)) {
    if (-not $ascii.Contains($marker) -and -not $unicode.Contains($marker) -and -not $unicodeOffset.Contains($marker)) {
        throw "Compiled behavior marker missing: $marker"
    }
}
$fileNames = @(Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object { $_.FullName.Substring($output.Length + 1).Replace('\', '/') })
$expectedNames = @(
    'CHANGELOG.md',
    'Dependencies/0Harmony.dll',
    'FishingMod.dll',
    'Locales/en.json',
    'Locales/fr.json',
    'ModManifest.asset',
    'README.md',
    'Sounds/bobber-splash.wav',
    'Sounds/cast-whoosh.wav',
    'Sounds/fish-landed.wav',
    'Sounds/line-snap.wav',
    'Sounds/qte-failure.wav',
    'Sounds/qte-success.wav',
    'Sounds/reel-in.wav',
    'Sounds/reel-out.wav',
    'THIRD_PARTY_NOTICES.md',
    'Thumbnail.png'
)
$sortedFileNames = @($fileNames | Sort-Object)
if (($sortedFileNames -join '|') -ne ($expectedNames -join '|')) {
    throw "Unexpected package files: $($sortedFileNames -join ', ')"
}
$thumbnail = Join-Path $output 'Thumbnail.png'
$thumbnailBytes = [IO.File]::ReadAllBytes($thumbnail)
$hasPngSignature = $thumbnailBytes[0] -eq 0x89 -and $thumbnailBytes[1] -eq 0x50 -and $thumbnailBytes[2] -eq 0x4E -and $thumbnailBytes[3] -eq 0x47
if ($thumbnailBytes.Length -le 16 -or $thumbnailBytes.Length -ge 1MB -or -not $hasPngSignature) {
    throw 'Thumbnail.png must be a valid PNG between 16 bytes and 1 MiB.'
}
$soundNames = @($expectedNames | Where-Object { $_ -like 'Sounds/*.wav' })
$soundHashes = @()
foreach ($soundName in $soundNames) {
    $soundPath = Join-Path $output $soundName
    if (-not (Test-Path -LiteralPath $soundPath -PathType Leaf)) { throw "Missing fishing sound: $soundName" }
    $soundBytes = [IO.File]::ReadAllBytes($soundPath)
    if ($soundBytes.Length -lt 1000 -or [Text.Encoding]::ASCII.GetString($soundBytes, 0, 4) -ne 'RIFF' -or
        [Text.Encoding]::ASCII.GetString($soundBytes, 8, 4) -ne 'WAVE') {
        throw "Fishing sound is not a valid packaged WAV: $soundName"
    }
    $soundHashes += (Get-FileHash -LiteralPath $soundPath -Algorithm SHA256).Hash
}
if (($soundHashes | Sort-Object -Unique).Count -ne $soundNames.Count) {
    throw 'Fishing sounds must be eight distinct WAV assets.'
}
foreach ($locale in @('en.json', 'fr.json')) {
    $localePath = Join-Path $output "Locales\$locale"
    $entries = Get-Content -LiteralPath $localePath -Raw | ConvertFrom-Json
    foreach ($key in @('fishingmod_happiness_activity', 'fishingmod_qte_hooked', 'fishingmod_waiting', 'fishingmod_wait_cancel_hint', 'fishingmod_wait_cancelled', 'fishingmod_result_no_fish', 'fishingmod_result_escaped', 'fishingmod_result_caught', 'fishingmod_result_sold', 'fishingmod_result_line_cost', 'fishingmod_result_line_free', 'fishingmod_result_money_unconfirmed', 'fishingmod_transaction_sale', 'fishingmod_transaction_line_break')) {
        if (-not $entries.PSObject.Properties[$key] -or [string]::IsNullOrWhiteSpace([string]$entries.$key)) {
            throw "Missing locale key '$key' in $localePath"
        }
    }
}
$readmeText = Get-Content -LiteralPath (Join-Path $output 'README.md') -Raw
foreach ($legalReference in @(
    'https://creativecommons.org/publicdomain/zero/1.0/',
    'https://freesound.org/people/el_boss/sounds/853287/',
    'https://opengameart.org/content/40-cc0-water-splash-slime-sfx',
    'https://kenney.nl/assets/interface-sounds',
    'https://kenney.nl/assets/music-jingles',
    'https://colorosse.com/assets/audio/sfx/arcade-ui-sfx',
    'https://github.com/Julian-adv/OpenMMO/blob/master/doc/assets/sfx.md',
    'THIRD_PARTY_NOTICES.md'
)) {
    if (-not $readmeText.Contains($legalReference)) {
        throw "README legal audio reference missing: $legalReference"
    }
}
$noticesText = Get-Content -LiteralPath (Join-Path $output 'THIRD_PARTY_NOTICES.md') -Raw
foreach ($harmonyNotice in @('github.com/pardeike/Harmony', 'MIT License', 'Copyright (c) 2016 Andreas Pardeike')) {
    if (-not $noticesText.Contains($harmonyNotice)) {
        throw "Harmony legal notice missing: $harmonyNotice"
    }
}
[pscustomobject]@{
    Files = $fileNames.Count
    Names = $fileNames
    DllBytes = (Get-Item -LiteralPath $dll).Length
    DllSHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    UnexpectedDlls = 0
} | ConvertTo-Json -Depth 4
