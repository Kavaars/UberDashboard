[CmdletBinding()]
param([string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'plugins/PhotoFrame/PhotoFrame.Plugin.csproj'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot ('Release/Plugins/PhotoFrame-1.1.0-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Katalog wynikowy już istnieje. Wybierz nowy katalog.' }
& dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build pluginu nie powiódł się (exit $LASTEXITCODE)." }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
$package = Join-Path $OutputDirectory 'PhotoFrame-1.1.0.udplugin'
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($package, [IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $repoRoot 'plugins/PhotoFrame/plugin.json'), 'plugin.json') | Out-Null
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $repoRoot 'plugins/PhotoFrame/bin/Release/net10.0-windows/PhotoFrame.Plugin.dll'), 'PhotoFrame.Plugin.dll') | Out-Null
}
finally { $archive.Dispose() }
Write-Host "Gotowy plugin: $package"
