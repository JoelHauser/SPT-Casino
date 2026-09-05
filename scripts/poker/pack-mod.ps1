<#
.SYNOPSIS
    Packs the server mod into something that extracts straight into an SPT install.

.DESCRIPTION
    Produces SPT_Runtime/user/mods/Poker/, matching the layout Blackjack ships, so
    the zip is extracted at the root of the install and lands where SPT looks.

    With -InstallPath it also builds the BepInEx client plugin against that install,
    stages it under BepInEx/plugins/Poker with the card and chip art, and copies both
    halves in. Without it the zip is the server alone and is named -server-only,
    because the client cannot be compiled on a machine without the game: it targets
    net472 against Assembly-CSharp.dll and the spt-* DLLs, and 4.1.3's PluginValidator
    requires the major.minor of those references to match the running server.

.EXAMPLE
    # Anywhere. Server only.
    ./scripts/pack-mod.ps1

.EXAMPLE
    # On the box with the game: builds both halves, installs them, writes a full zip.
    ./scripts/pack-mod.ps1 -InstallPath 'H:\SPT4.1.X'
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',

    # Copies straight into an install instead of only zipping. Handy on the box with
    # the game on it.
    [string] $InstallPath
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/Poker.Server/Poker.Server.csproj'
$build = Join-Path $root 'dist/mod-build'
$stage = Join-Path $root 'dist/mod'
$modFolder = Join-Path $stage 'SPT_Runtime/user/mods/Poker'

foreach ($path in @($build, $stage)) {
    if (Test-Path $path) { Remove-Item $path -Recurse -Force }
}

Write-Host 'Building the server mod...' -ForegroundColor Cyan
& dotnet build $project -c $Configuration -o $build --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with $LASTEXITCODE" }

New-Item -ItemType Directory -Force -Path $modFolder | Out-Null

# Only this mod's own assemblies. SPT provides its own, and shipping a second copy of
# them into the same process is a load conflict looking for somewhere to happen.
$wanted = @('Poker.Server.dll', 'Poker.Server.pdb', 'Poker.Game.dll', 'Poker.Game.pdb')

foreach ($name in $wanted) {
    $source = Join-Path $build $name
    if (Test-Path $source) {
        Copy-Item $source -Destination $modFolder
    }
    else {
        Write-Warning "missing $name"
    }
}

Copy-Item (Join-Path $root 'src/Poker.Server/config.json') -Destination $modFolder

# ---------------------------------------------------------------- the client half
#
# Only buildable on a machine with the game on it: Poker.Client targets net472 and
# references Assembly-CSharp.dll and the spt-* DLLs straight out of an install,
# because 4.1.3's PluginValidator requires the major.minor of those references to
# match the running server. Without -InstallPath there is nothing to build against,
# so the zip carries the server alone and says so in its name -- a half mod that
# looks like a whole one is worse than one that is obviously partial.
$clientIncluded = $false

if ($InstallPath) {
    $clientProject = Join-Path $root 'src/Poker.Client/Poker.Client.csproj'
    $clientOut = Join-Path $root 'dist/client-build'

    if (Test-Path $clientOut) { Remove-Item $clientOut -Recurse -Force }

    Write-Host 'Building the client plugin against the install...' -ForegroundColor Cyan
    & dotnet build $clientProject -c $Configuration -o $clientOut -p:SPTPath="$InstallPath" --nologo

    if ($LASTEXITCODE -ne 0) { throw "the client plugin failed to build with $LASTEXITCODE" }

    $pluginFolder = Join-Path $stage 'BepInEx/plugins/Poker'
    New-Item -ItemType Directory -Force -Path (Join-Path $pluginFolder 'cards') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $pluginFolder 'chips') | Out-Null

    Copy-Item (Join-Path $clientOut 'Poker.Client.dll') -Destination $pluginFolder
    Copy-Item (Join-Path $root 'src/Poker.Client/assets/table.png') -Destination $pluginFolder
    Copy-Item (Join-Path $root 'src/Poker.Client/assets/cards/*.png') -Destination (Join-Path $pluginFolder 'cards')
    Copy-Item (Join-Path $root 'src/Poker.Client/assets/chips/*.png') -Destination (Join-Path $pluginFolder 'chips')

    $clientIncluded = $true
    Write-Host 'Client plugin and art staged.' -ForegroundColor Green
}

# Version comes from the metadata, which must agree with the csproj. Read it back
# rather than hardcoding it here, so there is one fewer place to drift.
$metadata = Get-Content (Join-Path $root 'src/Poker.Server/ModMetadata.cs') -Raw
$version = if ($metadata -match 'new\("(?<v>\d+\.\d+\.\d+)"\)') { $Matches['v'] } else { '0.0.0' }

Write-Host "Staged v${version}:" -ForegroundColor Green
Get-ChildItem $modFolder | ForEach-Object { Write-Host ("  {0,9:N0}  {1}" -f $_.Length, $_.Name) }

$releases = Join-Path $root 'releases'
New-Item -ItemType Directory -Force -Path $releases | Out-Null
$suffix = if ($clientIncluded) { '' } else { '-server-only' }
$archive = Join-Path $releases "Poker-$version-SPT4.1$suffix.zip"
if (Test-Path $archive) { Remove-Item $archive -Force }

# Entries are written one at a time, with forward slashes, deliberately.
#
# Compress-Archive writes backslash entry names, which extract on Linux as a single
# file literally called "SPT_Runtime\user\mods\Poker\config.json". That much was
# already known from Blackjack. What was not is that ZipFile::CreateFromDirectory
# does exactly the same on Windows -- so the documented fix is not one. The zip spec
# says forward slashes, and only writing the entries by hand guarantees them.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$zip = [System.IO.Compression.ZipFile]::Open($archive, 'Create')
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $relative = $file.FullName.Substring($stage.Length).TrimStart([char]92, [char]47)
        $relative = $relative.Replace([char]92, [char]47)

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

Write-Host "Packed $archive" -ForegroundColor Green

if ($InstallPath) {
    if (-not (Test-Path $InstallPath)) { throw "No such install: $InstallPath" }

    # The server lives in a subfolder of the install, not at its root: 4.1.x ships it
    # as SPT_Runtime\ and older layouts as SPT\. Joining 'user/mods' straight onto the
    # install path silently creates a folder nothing ever reads, and a mod that never
    # loads looks exactly like a mod that loaded and did nothing.
    $runtime = @('SPT_Runtime', 'SPT') |
        ForEach-Object { Join-Path $InstallPath $_ } |
        Where-Object { Test-Path (Join-Path $_ 'SPTarkov.Server.Core.dll') } |
        Select-Object -First 1

    if (-not $runtime) {
        throw "No SPT server found under '$InstallPath' -- looked for SPT_Runtime\ and SPT\ containing SPTarkov.Server.Core.dll."
    }

    $target = Join-Path $runtime 'user/mods/Poker'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item (Join-Path $modFolder '*') -Destination $target -Force

    Write-Host "Installed the server mod to $target" -ForegroundColor Green

    if ($clientIncluded) {
        $plugins = Join-Path $InstallPath 'BepInEx/plugins/Poker'
        New-Item -ItemType Directory -Force -Path $plugins | Out-Null
        Copy-Item (Join-Path $stage 'BepInEx/plugins/Poker/*') -Destination $plugins -Recurse -Force

        Write-Host "Installed the client plugin to $plugins" -ForegroundColor Green
    }
}

Write-Host ''

if (-not $clientIncluded) {
    Write-Host 'SERVER ONLY. There is no client plugin in this zip.' -ForegroundColor Yellow
    Write-Host 'It cannot be built on a machine without the game -- 4.1.3 checks the plugin''s' -ForegroundColor Yellow
    Write-Host 'spt-* references against the running server. On the box with SPT on it, run:' -ForegroundColor Yellow
    Write-Host ''
    Write-Host "    ./scripts/pack-mod.ps1 -InstallPath 'H:\SPT4.1.X'" -ForegroundColor Cyan
    Write-Host ''
    Write-Host 'which builds both halves, installs them, and writes a complete zip.' -ForegroundColor Yellow
    Write-Host ''
}

Write-Host 'Extract the zip at the root of the SPT install, then start the server.' -ForegroundColor Cyan
Write-Host 'Look for a [Poker] block in the console -- silence means the version gate.'
