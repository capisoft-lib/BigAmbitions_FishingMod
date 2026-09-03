$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = [IO.Path]::GetFullPath((Join-Path $modRoot '..\..\..'))
$output = Join-Path $projectRoot 'Output\FishingMod'
$dll = Join-Path $output 'FishingMod.dll'
$manifest = Join-Path $output 'ModManifest.asset'
foreach ($required in @($dll, $manifest, (Join-Path $output 'README.md'), (Join-Path $output 'CHANGELOG.md'))) {
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
    'CacheBuildCount'
)) {
    if (-not $ascii.Contains($marker) -and -not $unicode.Contains($marker)) { throw "Compiled behavior marker missing: $marker" }
}
$fileNames = @(Get-ChildItem -LiteralPath $output -Recurse -File | ForEach-Object { $_.FullName.Substring($output.Length + 1).Replace('\', '/') })
$expectedNames = @('CHANGELOG.md', 'FishingMod.dll', 'Locales/en.json', 'Locales/fr.json', 'ModManifest.asset', 'README.md')
$sortedFileNames = @($fileNames | Sort-Object)
if (($sortedFileNames -join '|') -ne ($expectedNames -join '|')) {
    throw "Unexpected package files: $($sortedFileNames -join ', ')"
}
foreach ($locale in @('en.json', 'fr.json')) {
    $localePath = Join-Path $output "Locales\$locale"
    $entries = Get-Content -LiteralPath $localePath -Raw | ConvertFrom-Json
    foreach ($key in @('fishingmod_happiness_activity', 'fishingmod_qte_hooked', 'fishingmod_result_caught')) {
        if (-not $entries.PSObject.Properties[$key] -or [string]::IsNullOrWhiteSpace([string]$entries.$key)) {
            throw "Missing locale key '$key' in $localePath"
        }
    }
}
[pscustomobject]@{
    Files = $fileNames.Count
    Names = $fileNames
    DllBytes = (Get-Item -LiteralPath $dll).Length
    DllSHA256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    UnexpectedDlls = $unexpectedDlls.Count
} | ConvertTo-Json -Depth 4
