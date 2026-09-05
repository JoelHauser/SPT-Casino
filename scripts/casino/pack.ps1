<#
.SYNOPSIS
    Builds SPT Casino and, with -InstallPath, installs it over a real SPT folder.

.DESCRIPTION
    One plugin, three server mods. The client half is a single DLL carrying all three
    tables; the server halves stay exactly as they were, each on its own routes, because
    that is where the money lives and the merge deliberately did not go near it.

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
    [string]$InstallPath
)

$ErrorActionPreference = 'Stop'

# Two levels up: this sits in scripts/<mod>/.
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

$version = '1.0.0'
$plugin = Join-Path $root 'src\Casino.Client\Casino.Client.csproj'
$stage = Join-Path $root 'dist\casino'

Write-Host 'Building the casino plugin...' -ForegroundColor Cyan
dotnet build $plugin -c Release --nologo -v q "-p:SPTPath=$SPTPath"
if ($LASTEXITCODE -ne 0) { throw "the plugin did not build ($LASTEXITCODE)" }

$built = Get-ChildItem -Recurse -Path (Join-Path $root 'src\Casino.Client\bin\Release') -Filter 'Casino.Client.dll' |
    Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $built) { throw 'no Casino.Client.dll under src\Casino.Client\bin\Release' }

# --- stage -----------------------------------------------------------------------
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$pluginDir = Join-Path $stage 'BepInEx\plugins\Casino'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

Copy-Item $built.FullName -Destination $pluginDir -Force

# The union of the three asset trees. Verified identical wherever they overlap.
foreach ($game in @('Roulette', 'Poker', 'Blackjack')) {
    $assets = Join-Path $root "src\$game.Client\assets"
    if (Test-Path $assets) {
        Copy-Item (Join-Path $assets '*') -Destination $pluginDir -Recurse -Force
    }
}

$art = (Get-ChildItem $pluginDir -Recurse -File | Measure-Object).Count - 1
Write-Host "Staged the plugin and $art art file(s)." -ForegroundColor Green

# --- install ---------------------------------------------------------------------
if (-not $InstallPath) {
    Write-Host ''
    Write-Host 'Not installed. Pass -InstallPath to put it in a real SPT folder.' -ForegroundColor Yellow
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

foreach ($old in @('Blackjack', 'Poker', 'Roulette')) {
    $dir = Join-Path $target "BepInEx\plugins\$old"
    if (Test-Path $dir) {
        New-Item -ItemType Directory -Force -Path $retired | Out-Null
        $to = Join-Path $retired $old
        if (Test-Path $to) { Remove-Item $to -Recurse -Force }
        Move-Item $dir -Destination $to -Force
        Write-Host "Retired the old $old plugin to $to" -ForegroundColor Yellow
    }
}

Copy-Item (Join-Path $stage 'BepInEx') -Destination $target -Recurse -Force
Write-Host "Installed SPT Casino $version to $target\BepInEx\plugins\Casino" -ForegroundColor Green

Write-Host ''
Write-Host 'The three server mods are unchanged and stay where they are.' -ForegroundColor Cyan
Write-Host 'Look for a [Casino] client loaded line in BepInEx/LogOutput.log.' -ForegroundColor Cyan
