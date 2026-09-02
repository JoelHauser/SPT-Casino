<#
.SYNOPSIS
    Builds both halves in Release and packs releases\Blackjack_V<ver>.zip.

.DESCRIPTION
    The zip is laid out to extract straight into an SPT folder:

        SPT_Runtime\user\mods\Blackjack\    the server mod
        BepInEx\plugins\Blackjack\          the client plugin, the table, the cards
        README.txt

    Two things this does rather than trusts:

    It checks that all four version strings agree before building anything. They
    live in Blackjack.Server.csproj, ModMetadata.cs, Blackjack.Client.csproj and
    BlackjackClientPlugin.cs, and a release where they disagree is a release the
    Forge argues with.

    It writes the zip through System.IO.Compression with forward-slash entry
    names. Compress-Archive writes backslashes, which extract on Linux as one
    file with slashes in its name rather than as a tree.

.PARAMETER SPTPath
    The SPT install to build the client half against. The plugin is compiled
    against the game's own assemblies, so this decides which EFT build it will
    load on. Defaults to whatever Blackjack.Client.csproj finds.

.EXAMPLE
    scripts\pack.ps1
    scripts\pack.ps1 -SPTPath H:\SPT4.1.X
#>
[CmdletBinding()]
param(
    [string]$SPTPath,
    [switch]$KeepStage
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Get-Match {
    param([string]$Path, [string]$Pattern)

    $text = Get-Content -Raw -Path (Join-Path $root $Path)
    $found = [regex]::Match($text, $Pattern)
    if (-not $found.Success) { throw "no version found in $Path" }
    return $found.Groups[1].Value
}

# ---------------------------------------------------------------- the version

$versions = [ordered]@{
    'Blackjack.Server.csproj' = Get-Match 'src\Blackjack.Server\Blackjack.Server.csproj' '<Version>([^<]+)</Version>'
    'ModMetadata.cs'          = Get-Match 'src\Blackjack.Server\ModMetadata.cs' 'Version\s*\{\s*get;\s*init;\s*\}\s*=\s*new\("([^"]+)"\)'
    'Blackjack.Client.csproj' = Get-Match 'src\Blackjack.Client\Blackjack.Client.csproj' '<Version>([^<]+)</Version>'
    'BlackjackClientPlugin'   = Get-Match 'src\Blackjack.Client\BlackjackClientPlugin.cs' 'PluginVersion\s*=\s*"([^"]+)"'
}

# Forced to an array: a single string indexes as characters, so $distinct[0] on a
# scalar hands back "1" and packs Blackjack_V1.zip.
$distinct = @($versions.Values | Select-Object -Unique)
if ($distinct.Count -ne 1) {
    $versions.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-26} {1}" -f $_.Key, $_.Value) }
    throw "the four version strings disagree"
}

$version = $distinct[0]
Write-Host "Blackjack $version" -ForegroundColor Cyan

# ----------------------------------------------------------------- the builds

$sptArg = @()
if ($SPTPath) { $sptArg = @("-p:SPTPath=$SPTPath") }

dotnet build (Join-Path $root 'src\Blackjack.Server\Blackjack.Server.csproj') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "the server half did not build" }

dotnet build (Join-Path $root 'src\Blackjack.Client\Blackjack.Client.csproj') -c Release --nologo -v q @sptArg
if ($LASTEXITCODE -ne 0) { throw "the client half did not build" }

# ------------------------------------------------------------------ the stage

$stage = Join-Path $root 'dist\stage'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }

$serverDir = Join-Path $stage 'SPT_Runtime\user\mods\Blackjack'
$pluginDir = Join-Path $stage 'BepInEx\plugins\Blackjack'
$cardDir = Join-Path $pluginDir 'cards'
New-Item -ItemType Directory -Force -Path $serverDir, $cardDir | Out-Null

# Found rather than assumed: the server project writes to a nested output path of
# its own, and guessing at it produces an empty zip that looks fine.
$serverBuilt = Get-ChildItem -Recurse -Path (Join-Path $root 'src\Blackjack.Server\bin\Release') -Filter 'Blackjack.Server.dll' |
    Select-Object -First 1
if (-not $serverBuilt) { throw "no Blackjack.Server.dll under src\Blackjack.Server\bin\Release" }
$serverOut = $serverBuilt.DirectoryName

foreach ($file in 'Blackjack.Server.dll', 'Blackjack.Server.pdb', 'Blackjack.Game.dll', 'Blackjack.Game.pdb', 'config.json') {
    Copy-Item (Join-Path $serverOut $file) $serverDir
}

$clientBuilt = Get-ChildItem -Recurse -Path (Join-Path $root 'src\Blackjack.Client\bin\Release') -Filter 'Blackjack.Client.dll' |
    Select-Object -First 1
if (-not $clientBuilt) { throw "no Blackjack.Client.dll under src\Blackjack.Client\bin\Release" }

Copy-Item $clientBuilt.FullName $pluginDir
Copy-Item (Join-Path $root 'src\Blackjack.Client\assets\table.png') $pluginDir
Copy-Item (Join-Path $root 'src\Blackjack.Client\assets\cards\*.png') $cardDir

$readme = Get-Content -Raw (Join-Path $root 'releases\package-readme.txt')
$readme = $readme.Replace('{VERSION}', $version)
Set-Content -Path (Join-Path $stage 'README.txt') -Value $readme -Encoding UTF8

# -------------------------------------------------------------------- the zip

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zipPath = Join-Path $root ("releases\Blackjack_V{0}.zip" -f $version)
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    foreach ($file in Get-ChildItem -Recurse -File -Path $stage) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entry, 'Optimal') | Out-Null
    }
}
finally {
    $zip.Dispose()
}

if (-not $KeepStage) { Remove-Item -Recurse -Force $stage }

$size = (Get-Item $zipPath).Length / 1MB
Write-Host ("packed {0} ({1:N1} MB)" -f $zipPath, $size) -ForegroundColor Green
