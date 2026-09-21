$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $modRoot '..\..\..\..'))
$shared = Join-Path $workspaceRoot 'shared\StaticWater'
$dataset = Get-Content -LiteralPath (Join-Path $workspaceRoot 'data\water-zones\water-zones.json') -Raw | ConvertFrom-Json
if ($dataset.height -ne -2.8 -or $dataset.features.Count -lt 1) {
    throw 'Atlas height/count changed: update the FishingWaterDetector contract and tests before syncing.'
}
$target = Join-Path $modRoot 'Scripts\StaticWater'
New-Item -ItemType Directory -Path $target -Force | Out-Null
foreach ($name in @('StaticWaterAtlas.cs', 'GameWaterAtlas.g.cs')) {
    $source = Join-Path $shared $name
    $text = [IO.File]::ReadAllText($source).Replace('Capisoft.StaticWater', 'FishingMod.StaticWater')
    $text = $text.Replace('public sealed class StaticWaterAtlas', 'internal sealed class StaticWaterAtlas')
    $text = $text.Replace('public static class GameWaterAtlas', 'internal static class GameWaterAtlas')
    [IO.File]::WriteAllText((Join-Path $target $name), $text, [Text.UTF8Encoding]::new($false))
}
Write-Host "Embedded $($dataset.features.Count) static polygons under FishingMod.StaticWater; no external DLL dependency."
