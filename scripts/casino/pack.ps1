<#
.SYNOPSIS
    Builds SPT Casino and, with -InstallPath, installs it over a real SPT folder.

.DESCRIPTION
    Builds and installs the whole of SPT Casino: one plugin carrying all three tables,
    and the three server mods they talk to.

    One folder each side. SPT loads every .dll in a mod folder into a single mod and
    registers the injectables from all of them, so the four server assemblies sit
    together under user/mods/Casino. The one thing it will not tolerate is two
    IModMetadata classes in one folder, which is why exactly one of them declares it.

    The art is one folder. The three mods' asset trees were byte-identical everywhere
    they overlapped -- 59 of 61 files appear in more than one and not one of them
    differed -- so the union is safe and every table finds its own art beside the DLL
    without a line of it changing.

    -InstallPath also REMOVES the old per-game plugin folders. Leaving them installed
    gives four tabs on the bar and four Harmony patches on the same method, which is
    the single most likely way this upgrade goes wrong.
#>
[CmdletBinding()]
param(
    [string]$SPTPath = 'H:\SPT4.1.X',
    [string]$InstallPath,

    # Writes releases/casino/SPT_CasinoV<version>.zip, laid out relative to the SPT
    # folder so it extracts straight over an install.
    [switch]$Zip
)

$ErrorActionPreference = 'Stop'

# Two levels up: this sits in scripts/<mod>/.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$version = '1.0.1'

# What the download is called. Deliberately not $version: the plugin carries a
# three-part version because BepInEx expects one, and the release is named the way it
# is published.
$release = '1.0.1'
$tables = @('Blackjack', 'Poker', 'Roulette')
$plugin = Join-Path $root 'src\Casino.Client\Casino.Client.csproj'
$stage = Join-Path $root 'dist\casino'

Write-Host 'Building the casino plugin...' -ForegroundColor Cyan
dotnet build $plugin -c Release --nologo -v q "-p:SPTPath=$SPTPath"
if ($LASTEXITCODE -ne 0) { throw "the plugin did not build ($LASTEXITCODE)" }

$built = Get-ChildItem -Recurse -Path (Join-Path $root 'src\Casino.Client\bin\Release') -Filter 'Casino.Client.dll' |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $built) { throw 'no Casino.Client.dll under src\Casino.Client\bin\Release' }

Write-Host 'Building the server half...' -ForegroundColor Cyan
foreach ($project in @('Casino') + $tables) {
    dotnet build (Join-Path $root "src\$project.Server\$project.Server.csproj") -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "$project.Server did not build ($LASTEXITCODE)" }
}

# --- stage -----------------------------------------------------------------------
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$pluginDir = Join-Path $stage 'BepInEx\plugins\Casino'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

Copy-Item $built.FullName -Destination $pluginDir -Force

# The casino's own art -- the tab icon -- then the union of the three asset trees,
# which was verified identical wherever they overlap.
foreach ($game in @('Casino', 'Roulette', 'Poker', 'Blackjack', 'SlotMachine')) {
    $assets = Join-Path $root "src\$game.Client\assets"
    if (Test-Path $assets) {
        Copy-Item (Join-Path $assets '*') -Destination $pluginDir -Recurse -Force
    }
}

$art = (Get-ChildItem $pluginDir -Recurse -File | Measure-Object).Count - 1
Write-Host "Staged the plugin and $art art file(s)." -ForegroundColor Green

# One server folder, holding every assembly. SPT_Runtime is part of the path inside the
# zip rather than the folder you extract into: dropping that prefix produces something
# that looks right and installs nothing.
$modDir = Join-Path $stage 'SPT_Runtime\user\mods\Casino'
New-Item -ItemType Directory -Force -Path $modDir | Out-Null

$wanted = @('Casino.Server.dll', 'Casino.Server.pdb')
foreach ($table in $tables) {
    $wanted += @("$table.Server.dll", "$table.Server.pdb", "$table.Game.dll", "$table.Game.pdb")
}

foreach ($name in $wanted) {
    $file = Get-ChildItem -Recurse -Path (Join-Path $root 'src') -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\bin\Release\*' } |
        Sort-Object LastWriteTime | Select-Object -Last 1

    if ($file) { Copy-Item $file.FullName -Destination $modDir -Force }
    elseif ($name -notlike '*.pdb') { throw "no $name under any src\*\bin\Release" }
}

# One config per table, named apart. They were all called config.json when each table
# had a folder to itself, and in one folder that would be one file read three times.
foreach ($table in $tables) {
    $config = Join-Path $root ("src\{0}.Server\{1}.config.json" -f $table, $table.ToLower())
    if (Test-Path $config) { Copy-Item $config -Destination $modDir -Force }
}

$assemblies = (Get-ChildItem $modDir -Filter *.dll | Measure-Object).Count
Write-Host "Staged the server half: $assemblies assemblies in one folder." -ForegroundColor Green

# --- zip ---------------------------------------------------------------------------
if ($Zip) {
    $releases = Join-Path $root 'releases\casino'
    New-Item -ItemType Directory -Force -Path $releases | Out-Null

    $archive = Join-Path $releases "SPT_CasinoV$release.zip"
    if (Test-Path $archive) { Remove-Item $archive -Force }

    # Entries are written one at a time, with forward slashes, deliberately.
    #
    # Compress-Archive writes backslash entry names, which extract on Linux as a single
    # file literally called "SPT_Runtime\user\mods\Roulette\config.json". That much was
    # already known. What was not is that ZipFile::CreateFromDirectory does exactly the
    # same on Windows, so the documented fix is not one. The zip spec says forward
    # slashes, and only writing the entries by hand guarantees them.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $zipFile = [System.IO.Compression.ZipFile]::Open($archive, 'Create')
    try {
        foreach ($file in Get-ChildItem $stage -Recurse -File) {
            $relative = $file.FullName.Substring($stage.Length).TrimStart([char]92, [char]47)
            $relative = $relative.Replace([char]92, [char]47)

            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zipFile, $file.FullName, $relative) | Out-Null
        }
    }
    finally {
        $zipFile.Dispose()
    }

    $mb = (Get-Item $archive).Length / 1MB
    Write-Host ("Packed {0} ({1:N1} MB)" -f $archive, $mb) -ForegroundColor Green

    # No README at the top of the zip, decided for Blackjack 1.1.2 and kept: this is
    # extracted *over* an SPT folder, so a loose file at its root lands in the install
    # root beside the game's own, where it is litter rather than documentation.
}

# --- install ---------------------------------------------------------------------
if (-not $InstallPath) {
    Write-Host ''
    Write-Host 'Not installed. Pass -InstallPath to put it in a real SPT folder.' -ForegroundColor Yellow
    Write-Host 'Pass -Zip to write a release archive instead.' -ForegroundColor Yellow
    return
}

# 4.1.x keeps the server under SPT_Runtime; the plugins sit at the install root.
$target = $InstallPath
if (-not (Test-Path (Join-Path $target 'BepInEx'))) {
    throw "no BepInEx folder under '$target' -- that is not an SPT install root."
}

# The old plugins have to go, or the bar gets four tabs and the input tree four
# patches. Moved aside rather than deleted: they are somebody's working install.
$retired = Join-Path $target 'BepInEx\plugins\_replaced-by-SPT-Casino'

foreach ($old in $tables) {
    $dir = Join-Path $target "BepInEx\plugins\$old"
    if (Test-Path $dir) {
        New-Item -ItemType Directory -Force -Path $retired | Out-Null
        $to = Join-Path $retired $old
        if (Test-Path $to) { Remove-Item $to -Recurse -Force }
        Move-Item $dir -Destination $to -Force
        Write-Host "Retired the old $old plugin to $to" -ForegroundColor Yellow
    }
}

# The old server folders too. Left in place they are still whole mods with their own
# metadata, so SPT would load them beside this one and register every route twice.
#
# Moved rather than deleted, and that is not politeness: each carries a data folder
# holding what the house owes a player whose hand was interrupted, and the tables look
# in here for it on first run. See Casino.Server.LegacyData.
# Beside user/mods, never inside it: SPT walks every directory under mods and throws
# "No Assemblies found in path" at Critical on one holding no assemblies. Parking the
# old mods in there traded three folders for a stack trace on every boot.
$retiredMods = Join-Path $target "SPT_Runtime\user\_replaced-by-SPT-Casino"

foreach ($old in $tables) {
    $dir = Join-Path $target "SPT_Runtime\user\mods\$old"
    if (Test-Path $dir) {
        New-Item -ItemType Directory -Force -Path $retiredMods | Out-Null
        $to = Join-Path $retiredMods $old
        if (Test-Path $to) { Remove-Item $to -Recurse -Force }
        Move-Item $dir -Destination $to -Force
        Write-Host "Retired the old $old server mod to $to" -ForegroundColor Yellow
    }
}

Copy-Item (Join-Path $stage 'BepInEx') -Destination $target -Recurse -Force
Write-Host "Installed the plugin to $target\BepInEx\plugins\Casino" -ForegroundColor Green

Copy-Item (Join-Path $stage 'SPT_Runtime') -Destination $target -Recurse -Force
Write-Host "Installed the server half to $target\SPT_Runtime\user\mods\Casino" -ForegroundColor Green

Write-Host ''
Write-Host "SPT Casino $version is installed. Restart the server." -ForegroundColor Cyan
Write-Host 'Look for a [Casino] client loaded line in BepInEx/LogOutput.log, and one' -ForegroundColor Cyan
Write-Host '[Casino] line in the server console. Silence there means the version gate' -ForegroundColor Cyan
Write-Host 'rather than a bug. Per-table detail is behind VerboseLogging in the' -ForegroundColor Cyan
Write-Host 'config files beside the mod.' -ForegroundColor Cyan
