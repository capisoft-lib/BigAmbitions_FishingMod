param([Parameter(Mandatory = $true)][string]$HarmonyZip)

$ErrorActionPreference = 'Stop'
$expectedSha256 = 'A5FC5F9D9640B927D786A0527FAA18BF7AA776788235140C59E9B73DE87A7774'
$actualSha256 = (Get-FileHash -LiteralPath $HarmonyZip -Algorithm SHA256).Hash
if ($actualSha256 -ne $expectedSha256) {
    throw "Unexpected Harmony archive hash: $actualSha256"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -Path 'C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Data\il2cpp\build\deploy\Mono.Cecil.dll'

$destination = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Dependencies\0Harmony.dll'))
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $HarmonyZip))
try {
    $entry = $archive.GetEntry('net48/0Harmony.dll')
    if ($null -eq $entry) { throw 'The official archive does not contain net48/0Harmony.dll.' }

    $entryStream = $entry.Open()
    $buffer = New-Object IO.MemoryStream
    try { $entryStream.CopyTo($buffer) } finally { $entryStream.Dispose() }
    $buffer.Position = 0

    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($buffer)
    try {
        $core = $assembly.MainModule.AssemblyReferences | Where-Object Name -EQ 'mscorlib'
        $mergedTypes = @(
            'MonoMod.Backports.MethodImplOptionsEx',
            'System.Runtime.CompilerServices.Unsafe',
            'MonoMod.Backports.ILHelpers.UnsafeRaw'
        )
        foreach ($type in $assembly.MainModule.GetTypeReferences()) {
            if ($type.FullName -in @(
                'System.Collections.Generic.Queue`1',
                'System.Collections.Generic.Stack`1',
                'System.Runtime.CompilerServices.ExtensionAttribute')) {
                $type.Scope = $core
            }
            if ($type.FullName -in $mergedTypes) { $type.Scope = $assembly.MainModule }
        }
        $assembly.Write($destination)
    }
    finally {
        $assembly.Dispose()
        $buffer.Dispose()
    }
}
finally {
    $archive.Dispose()
}

Get-FileHash -LiteralPath $destination -Algorithm SHA256
