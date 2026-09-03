$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = [IO.Path]::GetFullPath((Join-Path $modRoot '..\..\..'))
& (Join-Path $projectRoot 'scripts\compile-install-ba-mod.ps1') -ModId FishingMod -NoInstall
$output = Join-Path $projectRoot 'Output\FishingMod'
foreach ($name in @('ModManifest.asset', 'README.md', 'CHANGELOG.md', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $modRoot $name) -Destination $output -Force
}
$outputSounds = Join-Path $output 'Sounds'
if (Test-Path -LiteralPath $outputSounds) { Remove-Item -LiteralPath $outputSounds -Recurse -Force }
Copy-Item -LiteralPath (Join-Path $modRoot 'Sounds~') -Destination $outputSounds -Recurse
$dll = Get-Item -LiteralPath (Join-Path $output 'FishingMod.dll')
[pscustomobject]@{
    Path = $dll.FullName
    Bytes = $dll.Length
    SHA256 = (Get-FileHash -LiteralPath $dll.FullName -Algorithm SHA256).Hash
} | ConvertTo-Json
