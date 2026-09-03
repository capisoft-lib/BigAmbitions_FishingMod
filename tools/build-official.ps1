param([int]$TimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$modRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectRoot = [IO.Path]::GetFullPath((Join-Path $modRoot '..\..\..'))
. (Join-Path $projectRoot 'scripts\_project.ps1')

if ($env:BA_MOD_BUILD_CLI) { throw 'Unset BA_MOD_BUILD_CLI; it would enable automatic installation.' }
$openEditor = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -like "*$projectRoot*" }
if ($openEditor) { throw 'The Unity project is already open. No existing Editor will be stopped.' }

$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$logRoot = Join-Path $projectRoot "Logs\FishingMod\$stamp"
$output = Join-Path $projectRoot 'Output\FishingMod'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
if (Test-Path -LiteralPath $output) {
    Copy-Item -LiteralPath $output -Destination (Join-Path $logRoot 'previous-output') -Recurse
}
$log = Join-Path $logRoot 'unity.log'
$workspaceRoot = Split-Path $projectRoot -Parent
$isolatedProject = Join-Path $workspaceRoot ".analysis\fishing-mod\official-$stamp"
New-Item -ItemType Directory -Path $isolatedProject -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Packages') -Destination $isolatedProject -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'ProjectSettings') -Destination $isolatedProject -Recurse
$isolatedAssets = Join-Path $isolatedProject 'Assets'
New-Item -ItemType Directory -Path $isolatedAssets -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets\Editor') -Destination $isolatedAssets -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'Assets\_BaDependencies') -Destination $isolatedAssets -Recurse
$isolatedMods = Join-Path $isolatedAssets 'Mods'
New-Item -ItemType Directory -Path $isolatedMods -Force | Out-Null
Copy-Item -LiteralPath $modRoot -Destination $isolatedMods -Recurse
foreach ($metaName in @('Editor.meta', '_BaDependencies.meta', 'Mods.meta')) {
    $metaSource = Join-Path (Join-Path $projectRoot 'Assets') $metaName
    if (Test-Path -LiteralPath $metaSource) { Copy-Item -LiteralPath $metaSource -Destination $isolatedAssets }
}
$isolatedOutput = Join-Path $isolatedProject 'Output\FishingMod'
$arguments = @(
    '-batchmode', '-nographics', '-ignoreCompilerErrors', '-disable-assembly-updater',
    '-projectPath', ('"' + $isolatedProject + '"'),
    '-executeMethod', 'FishingMod.Editor.FishingModBuild.Run',
    '-logFile', ('"' + $log + '"')
)
$started = [DateTime]::UtcNow
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Host "[build] Official Unity PID=$($process.Id), log=$log"
while (!$process.WaitForExit(1000)) {
    if (([DateTime]::UtcNow - $started).TotalSeconds -gt ($TimeoutSeconds + 120)) {
        $process.Kill()
        throw "FishingMod official build timed out. See $log"
    }
}
if ($process.ExitCode -ne 0) { throw "Official Unity exited $($process.ExitCode). See $log" }
if (!(Select-String -LiteralPath $log -SimpleMatch '[FishingMod.Build] Official Mod Builder; installation=false.') -or
    !(Select-String -LiteralPath $log -SimpleMatch '[FishingMod.Verify] Official package verified; installation=false.') -or
    !(Select-String -LiteralPath $log -SimpleMatch "[ModBuildCli] Build succeeded: $isolatedOutput")) {
    throw "Unity exited without a completed FishingMod build. See $log"
}
$officialFiles = @(Get-ChildItem -LiteralPath $isolatedOutput -File | Sort-Object Name)
$officialNames = @($officialFiles.Name)
$expectedNames = @('CHANGELOG.md', 'FishingMod.dll', 'ModManifest.asset', 'README.md', 'THIRD_PARTY_NOTICES.md', 'Thumbnail.png')
if (($officialNames -join '|') -ne ($expectedNames -join '|')) {
    throw "Unexpected official package contents: $($officialNames -join ', ')"
}
$officialPackageFiles = @(Get-ChildItem -LiteralPath $isolatedOutput -File -Recurse | Sort-Object FullName)
$officialRelativeNames = @($officialPackageFiles | ForEach-Object {
    $_.FullName.Substring($isolatedOutput.Length + 1).Replace('\', '/')
})
$expectedRelativeNames = @(
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
    'THIRD_PARTY_NOTICES.md',
    'Thumbnail.png'
)
if (($officialRelativeNames -join '|') -ne ($expectedRelativeNames -join '|')) {
    throw "Unexpected recursive official package contents: $($officialRelativeNames -join ', ')"
}
$outputRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Output')).TrimEnd('\')
$resolvedOutput = [IO.Path]::GetFullPath($output)
if (!$resolvedOutput.StartsWith($outputRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace output outside project Output: $resolvedOutput"
}
if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Recurse -Force }
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
Copy-Item (Join-Path $isolatedOutput '*') $resolvedOutput -Recurse -Force
$dll = Get-Item -LiteralPath (Join-Path $output 'FishingMod.dll')
if ($officialFiles.Where({ $_.Name -eq 'FishingMod.dll' })[0].LastWriteTimeUtc -lt $started) { throw 'Official FishingMod DLL is stale.' }
$result = [pscustomobject]@{
    Path = $dll.FullName
    Bytes = $dll.Length
    SHA256 = (Get-FileHash -LiteralPath $dll.FullName -Algorithm SHA256).Hash
    Installation = $false
    Log = $log
    IsolatedProjectRemoved = $true
}
$scratchRoot = [IO.Path]::GetFullPath((Join-Path $workspaceRoot '.analysis\fishing-mod'))
$resolvedIsolatedProject = [IO.Path]::GetFullPath($isolatedProject)
$scratchPrefix = $scratchRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (!$resolvedIsolatedProject.StartsWith($scratchPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove isolated project outside FishingMod scratch root: $resolvedIsolatedProject"
}
Remove-Item -LiteralPath $resolvedIsolatedProject -Recurse -Force
$result | ConvertTo-Json
