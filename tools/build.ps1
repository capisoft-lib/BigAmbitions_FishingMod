$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = [IO.Path]::GetFullPath((Join-Path $modRoot '..\..\..'))
& (Join-Path $projectRoot 'scripts\compile-install-ba-mod.ps1') -ModId FishingMod -NoInstall
$output = Join-Path $projectRoot 'Output\FishingMod'
Copy-Item -LiteralPath (Join-Path $modRoot 'ModManifest.asset') -Destination $output -Force
$dll = Get-Item -LiteralPath (Join-Path $output 'FishingMod.dll')
[pscustomobject]@{
    Path = $dll.FullName
    Bytes = $dll.Length
    SHA256 = (Get-FileHash -LiteralPath $dll.FullName -Algorithm SHA256).Hash
} | ConvertTo-Json
