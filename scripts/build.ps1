# Builds and tests the app. The build runs from bin/ with the maintainer's data/library.db (no key, no download).
#   scripts/build.ps1            Release build + tests
#   scripts/build.ps1 -Run       ... then start it (add -Debug for the web inspector)
param([switch]$Run, [switch]$Debug)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."

dotnet test "$root/tests/MUMUsic.Desktop.Tests" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
dotnet build "$root/src/MUMUsic.Desktop" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$exe = "$root\src\MUMUsic.Desktop\bin\Release\net8.0-windows\MUMUsic.exe"
Write-Host "Done: $exe"
if ($Run) { Start-Process $exe -ArgumentList $(if ($Debug) { '--debug' } else { @() }) }
