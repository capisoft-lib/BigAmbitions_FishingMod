$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'tests~\FishingMod.Tests.csproj'
dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'FishingMod tests failed.' }
