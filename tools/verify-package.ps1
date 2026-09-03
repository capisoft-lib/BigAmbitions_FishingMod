$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = [IO.Path]::GetFullPath((Join-Path $modRoot '..\..\..'))
$output = Join-Path $projectRoot 'Output\FishingMod'
$dll = Join-Path $output 'FishingMod.dll'
$manifest = Join-Path $output 'ModManifest.asset'
foreach ($required in @($dll, $manifest, (Join-Path $output 'README.md'), (Join-Path $output 'CHANGELOG.md'), (Join-Path $output 'THIRD_PARTY_NOTICES.md'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing package file: $required" }
}
$unexpectedDlls = @(Get-ChildItem -LiteralPath $output -Recurse -Filter '*.dll' -File | Where-Object Name -ne 'FishingMod.dll')
if ($unexpectedDlls.Count -ne 0) { throw "Unexpected bundled DLL(s): $($unexpectedDlls.Name -join ', ')" }
$bytes = [IO.File]::ReadAllBytes($dll)
$ascii = [Text.Encoding]::ASCII.GetString($bytes)
$unicode = [Text.Encoding]::Unicode.GetString($bytes)
foreach ($marker in @(
    'FishingMod_CastVisual',
    'FishingMod_Runtime',
    'FishingQteSession',
    'FishingBiteRules',
    'FishingHappinessService',
    'ApplyFishingActivity',
    'ApplyCatch',
    'AdvanceFight',
    'DrawControlWheel',
    'SetHandIKTargets',
    'SetGoal',
    'RaycastNonAlloc',
    'SurfaceCellKey',
    'IndexedTileCount',
    'CacheBuildCount',
    'FishingAudio',
    'FishingWaveDecoder',
    'ConsumeReleaseSoundEvent',
    'ConsumeSplashSoundEvent'
)) {
    if (-not $ascii.Contains($marker) -and -not $unicode.Contains($marker)) { throw "Compiled behavior marker missing: $marker" }
}
$fileNames = @(Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object { $_.FullName.Substring($output.Length + 1).Replace('\', '/') })
$expectedNames = @(
    'CHANGELOG.md',
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
    'THIRD_PARTY_NOTICES.md'
)
$sortedFileNames = @($fileNames | Sort-Object)
if (($sortedFileNames -join '|') -ne ($expectedNames -join '|')) {
    throw "Unexpected package files: $($sortedFileNames -join ', ')"
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
    foreach ($key in @('fishingmod_happiness_activity', 'fishingmod_qte_hooked', 'fishingmod_waiting', 'fishingmod_result_no_fish', 'fishingmod_result_escaped', 'fishingmod_result_caught')) {
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
[pscustomobject]@{
    Files = $fileNames.Count
    Names = $fileNames
    DllBytes = (Get-Item -LiteralPath $dll).Length
    DllSHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    UnexpectedDlls = $unexpectedDlls.Count
} | ConvertTo-Json -Depth 4
