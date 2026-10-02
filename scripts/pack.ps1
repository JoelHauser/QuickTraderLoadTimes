<#
.SYNOPSIS
    Build both halves, test, check versions, and pack the release zip (optionally install it).

.DESCRIPTION
    The mod is two assemblies that install to two different places:

      BepInEx/plugins/QuickTraderLoadTimes/QuickTraderLoadTimes.dll                  the fixes
      SPT_Runtime/user/mods/QuickTraderLoadTimes/QuickTraderLoadTimes.Server.dll     the startup banner

    The zip is unpacked over the SPT root, so it carries those full paths. Entries are written
    by hand with forward slashes: PowerShell 5.1's Compress-Archive writes backslashes, which
    some extractors turn into odd file names instead of folders.

    Refuses to pack if any of the four version numbers disagree (both csproj files, the
    plugin's PluginVersion, the server's ModMetadata), because a zip whose name does not match
    the DLLs inside makes a bug report unanswerable.

    Run from PowerShell, not Bash: Bash mangles backslash paths.

.EXAMPLE
    scripts\pack.ps1
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X -Install
#>
[CmdletBinding()]
param(
    [string] $SPTPath = 'H:\SPT4.1.X',
    [switch] $Install,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

$pluginProj = Join-Path $root 'src\QuickTraderLoadTimes\QuickTraderLoadTimes.csproj'
$serverProj = Join-Path $root 'src\QuickTraderLoadTimes.Server\QuickTraderLoadTimes.Server.csproj'
$testProj = Join-Path $root 'tests\QuickTraderLoadTimes.Tests\QuickTraderLoadTimes.Tests.csproj'

# ---- versions ----------------------------------------------------------------

function Get-CsprojVersion([string] $path) {
    [xml] $xml = Get-Content $path -Raw
    return ($xml.Project.PropertyGroup.Version | Where-Object { $_ }) -as [string]
}

function Get-SourceVersion([string] $path, [string] $pattern) {
    $m = [regex]::Match((Get-Content $path -Raw), $pattern)
    if (-not $m.Success) { throw "No version found in $path" }
    return $m.Groups[1].Value
}

$versions = [ordered]@{
    'plugin csproj'         = Get-CsprojVersion $pluginProj
    'plugin PluginVersion'  = Get-SourceVersion (Join-Path $root 'src\QuickTraderLoadTimes\QuickTraderLoadTimesPlugin.cs') 'PluginVersion = "([0-9.]+)"'
    'server csproj'         = Get-CsprojVersion $serverProj
    'server ModMetadata'    = Get-SourceVersion (Join-Path $root 'src\QuickTraderLoadTimes.Server\ModMetadata.cs') 'Version \{ get; init; \} = new\("([0-9.]+)"\)'
    'server banner'         = Get-SourceVersion (Join-Path $root 'src\QuickTraderLoadTimes.Server\StartupBanner.cs') 'Version = "([0-9.]+)"'
}
$versions.GetEnumerator() | ForEach-Object { Write-Host ("{0,-22} {1}" -f $_.Key, $_.Value) -ForegroundColor Cyan }
$distinct = @($versions.Values | Select-Object -Unique)
if ($distinct.Count -ne 1) {
    Write-Host 'Version numbers disagree; fix them before packing.' -ForegroundColor Red
    exit 1
}
$version = $distinct[0]

# ---- build and test ------------------------------------------------------------

& $dotnet build $pluginProj -c Release "-p:SPTPath=$SPTPath" --nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet build $serverProj -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not $SkipTests) {
    & $dotnet test $testProj -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$pluginDll = Join-Path $root 'src\QuickTraderLoadTimes\bin\Release\QuickTraderLoadTimes.dll'
$serverDll = Join-Path $root 'src\QuickTraderLoadTimes.Server\bin\Release\net10.0\QuickTraderLoadTimes.Server.dll'

# ---- zip -------------------------------------------------------------------------

$entries = [ordered]@{
    'BepInEx/plugins/QuickTraderLoadTimes/QuickTraderLoadTimes.dll'              = $pluginDll
    'SPT_Runtime/user/mods/QuickTraderLoadTimes/QuickTraderLoadTimes.Server.dll' = $serverDll
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "QuickTraderLoadTimes-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($e in $entries.GetEnumerator()) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $e.Value, $e.Key, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

Write-Host ''
Write-Host "Packed $zip" -ForegroundColor Green
$check = [System.IO.Compression.ZipFile]::OpenRead($zip)
try { $check.Entries | ForEach-Object { Write-Host "  $($_.FullName)  ($($_.Length) bytes)" } } finally { $check.Dispose() }
Write-Host ("  sha256 " + (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower())

# ---- install ---------------------------------------------------------------------

if ($Install) {
    if (-not (Test-Path (Join-Path $SPTPath 'BepInEx\plugins'))) {
        Write-Host "No BepInEx\plugins under $SPTPath." -ForegroundColor Red
        exit 2
    }
    foreach ($e in $entries.GetEnumerator()) {
        $dest = Join-Path $SPTPath ($e.Key -replace '/', '\')
        New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
        Copy-Item $e.Value $dest -Force
        Write-Host "Installed $dest" -ForegroundColor Green
    }
}
