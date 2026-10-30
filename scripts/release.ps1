# Builds the installer dist/MUMUsic-Setup-<version>.exe. The version is <Version> in src/MUMUsic.Desktop/MUMUsic.Desktop.csproj:
# bump it before a release, installed apps compare it with the newest release on GitHub.
# The app is published self-contained (the .NET runtime included), so installing it needs nothing else.
$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot/.."
$innoVersion = '7.1.0'
$innoCompiler = Join-Path $root ".tools/innosetup/tools/ISCC.exe"

[xml]$project = Get-Content "$root/src/MUMUsic.Desktop/MUMUsic.Desktop.csproj"
$version = ($project.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$appDir = Join-Path $root 'dist/app'

dotnet test "$root/tests/MUMUsic.Desktop.Tests" -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

# Inno Setup compresses better than the .NET bundle, so the single file is not compressed here.
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
dotnet publish "$root/src/MUMUsic.Desktop" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $appDir
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# Portable Inno Setup, downloaded once from NuGet into .tools/
if (-not (Test-Path $innoCompiler)) {
    $package = Join-Path $root '.tools/innosetup.zip'
    New-Item -ItemType Directory -Force (Split-Path $package) | Out-Null
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$innoVersion/tools.innosetup.$innoVersion.nupkg" -OutFile $package
    Expand-Archive $package -DestinationPath (Join-Path $root '.tools/innosetup') -Force
}

& $innoCompiler /Q "/DAppVersion=$version" "/DSourceDir=$appDir" "/DOutputDir=$root\dist" "$root/installer/MUMUsic.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

$setup = Join-Path $root "dist/MUMUsic-Setup-$version.exe"
Write-Host ("Done: $setup ({0:N0} MB)" -f ((Get-Item $setup).Length / 1MB))
