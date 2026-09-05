<#
.SYNOPSIS
    Builds the terminal harness into something you can carry to another machine.

.DESCRIPTION
    Self-contained by default, so the result runs on a box with no .NET installed --
    which is the point, since the machine with the game on it is not the machine this
    is usually built on. Pass -FrameworkDependent for a small build if the target has
    the .NET 10 runtime.

    This is NOT the SPT mod. There is no server project and no client plugin yet, so
    there is nothing to drop into user/mods. This is the engine harness only.

.EXAMPLE
    ./scripts/pack-console.ps1
    ./scripts/pack-console.ps1 -Zip
    ./scripts/pack-console.ps1 -FrameworkDependent -Zip
#>
[CmdletBinding()]
param(
    [string] $Runtime = 'win-x64',
    [string] $Configuration = 'Release',
    [switch] $FrameworkDependent,
    [switch] $Zip
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tools/Poker.Console/Poker.Console.csproj'
$output = Join-Path $root "dist/console-$Runtime"

if (-not (Test-Path $project)) {
    throw "Cannot find $project"
}

if (Test-Path $output) {
    Remove-Item $output -Recurse -Force
}

Write-Host "Publishing the harness -> $output" -ForegroundColor Cyan

$arguments = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '-o', $output,
    '-p:PublishSingleFile=true',
    '--nologo'
)

# Self-contained carries the runtime with it, which is the difference between "runs
# anywhere" and "runs where somebody already installed .NET 10".
$arguments += if ($FrameworkDependent) { '--self-contained:false' } else { '--self-contained:true' }

& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with $LASTEXITCODE"
}

$exe = Get-ChildItem $output -Filter 'Poker.Console.exe' | Select-Object -First 1
if (-not $exe) {
    throw "No Poker.Console.exe was produced in $output"
}

$size = [math]::Round($exe.Length / 1MB, 1)
Write-Host "Built $($exe.Name) ($size MB)" -ForegroundColor Green

if ($Zip) {
    $releases = Join-Path $root 'releases'
    New-Item -ItemType Directory -Force -Path $releases | Out-Null

    $stamp = Get-Date -Format 'yyyyMMdd'
    $archive = Join-Path $releases "Poker.Console-$stamp-$Runtime.zip"

    if (Test-Path $archive) {
        Remove-Item $archive -Force
    }

    # System.IO.Compression rather than Compress-Archive, which writes backslash
    # entries that extract as one literal filename on Linux. Learned on Blackjack.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($output, $archive)

    Write-Host "Packed $archive" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Run it with:' -ForegroundColor Cyan
Write-Host "  $($exe.FullName)"
Write-Host "  $($exe.FullName) --soak 2000 --samples 12"
Write-Host "  $($exe.FullName) --help"
