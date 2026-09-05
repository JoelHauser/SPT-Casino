<#
.SYNOPSIS
    Plays roulette against a running SPT server, with no game client attached.

.DESCRIPTION
    Pings first, then puts chips on the cloth and turns the wheel. The ping is the
    important half on a first run: it proves the mod loaded, the route is reachable,
    the session resolved to a real profile, and its money can be read. If the ping
    fails there is no point running the rest.

    Nothing here stakes real currency. This build has no way to move money at all --
    there is no debit or credit on IBank -- so it is safe to point at a real profile.

    Watch the server console alongside this. Every line the mod writes is prefixed
    "[Roulette]", so it can be filtered out of the noise.

    Three things about SPT's HTTP layer are handled below and each cost a round trip
    to discover on Blackjack: it serves HTTPS with a self-signed certificate, it
    zlib-inflates every request body unless told not to, and the session id has to
    travel in a WebRequestSession rather than a header.

.PARAMETER SessionId
    The profile id. Find it in the filename under SPT_Runtime\user\profiles\.

.PARAMETER PingOnly
    Stop after the health check without betting.

.EXAMPLE
    .\smoke.ps1 -SessionId 6a8cd3a7e0b8272790f41285 -PingOnly

.EXAMPLE
    .\smoke.ps1 -SessionId 6a8cd3a7e0b8272790f41285 -Spins 5
#>
param(
    [Parameter(Mandatory = $true)][string]$SessionId,
    [string]$Server = "https://127.0.0.1:6969",
    [int]$Spins = 3,
    [int]$Chip = 10000,
    [switch]$PingOnly
)

$ErrorActionPreference = "Stop"

# SPT 4.1 serves HTTPS on the same port it used to serve HTTP, with a self-signed
# certificate it generates into user\certs\. .NET rejects that by default and the
# failure surfaces as "the underlying connection was closed" rather than anything
# mentioning certificates -- which reads exactly like the server being down.
#
# Trusting it is safe here: this only ever talks to a loopback address.
if ($Server.StartsWith("https:")) {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
}

# SPT's listener zlib-inflates every request body and deflates every response, because
# that is what the EFT client speaks. Two headers opt out of both halves; without them
# a plain-JSON body dies inside Inflater complaining about an unsupported compression
# method, an error that names neither the header nor the body.
$headers = @{
    "Content-Type"       = "application/json"
    "requestcompressed"  = "0"
    "responsecompressed" = "0"
}

# The session id travels as a PHPSESSID cookie. It cannot go through -Headers:
# "Cookie" is restricted and PowerShell drops it *silently*, so the request arrives
# with no session and the server answers "session id provided was empty", which sends
# you looking in entirely the wrong place.
$webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$serverUri = [Uri]$Server
$webSession.Cookies.Add((New-Object System.Net.Cookie("PHPSESSID", $SessionId, "/", $serverUri.Host)))

function Invoke-Roulette {
    param([string]$Route, [hashtable]$Body = @{})

    $json = $Body | ConvertTo-Json -Compress

    try {
        return Invoke-RestMethod -Uri "$Server$Route" -Method Post -Headers $headers -Body $json -WebSession $webSession
    }
    catch {
        Write-Host "  request to $Route failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  a closed connection usually means the scheme is wrong -- 4.1 serves https," -ForegroundColor DarkGray
        Write-Host "  not http. A refused connection means the server is not running. A 404 means" -ForegroundColor DarkGray
        Write-Host "  the mod did not load -- check the server console for a [Roulette] banner." -ForegroundColor DarkGray
        exit 1
    }
}

function Show-Table {
    param($Table)

    if (-not $Table) { return }

    Write-Host ("  {0}, {1:N0} on the cloth" -f $Table.Phase.ToLower(), $Table.Staked) -ForegroundColor DarkGray

    foreach ($bet in $Table.Bets) {
        Write-Host ("    {0,12:N0}  {1}" -f $bet.Amount, $bet.Description)
    }
}

Write-Host ""
Write-Host "Roulette smoke test -> $Server" -ForegroundColor Cyan
Write-Host ""

$ping = Invoke-Roulette "/roulette/ping"

Write-Host ("  mod version   {0}" -f $ping.ModVersion)
Write-Host ("  session       '{0}'" -f $ping.SessionId)
Write-Host ("  profile       {0}" -f $(if ($ping.HasProfile) { "found" } else { "NOT FOUND" }))

if (-not $ping.HasProfile) {
    Write-Host ""
    Write-Host "  No profile for that session. If the id above is blank the cookie did not" -ForegroundColor Red
    Write-Host "  resolve; otherwise check the id against SPT_Runtime\user\profiles\." -ForegroundColor Red
    exit 1
}

Write-Host "  balances:"
foreach ($name in $ping.Balances.PSObject.Properties.Name) {
    Write-Host ("    {0,-12} {1,15:N0}" -f $name, $ping.Balances.$name)
}

Write-Host ""
if ($ping.MoneyIsNotMovedYet) {
    Write-Host "  This build cannot move money. Nothing above is at stake." -ForegroundColor DarkGray
}

if ($PingOnly) {
    Write-Host ""
    Write-Host "  Ping only. The mod is loaded, reachable, and can read the profile." -ForegroundColor Green
    Write-Host ""
    exit 0
}

# A spread that exercises several settlement rules at once: one number, a colour, a
# dozen and a corner all resolve differently on the same spin.
$spread = @(
    @{ Kind = "Straight"; Selection = 17; Amount = $Chip },
    @{ Kind = "Red";      Selection = 0;  Amount = $Chip },
    @{ Kind = "Dozen";    Selection = 2;  Amount = $Chip },
    @{ Kind = "Corner";   Selection = 1;  Amount = $Chip }
)

for ($spin = 1; $spin -le $Spins; $spin++) {
    Write-Host ""
    Write-Host "  --- spin $spin ---" -ForegroundColor Cyan

    foreach ($bet in $spread) {
        $reply = Invoke-Roulette "/roulette/place" $bet

        if (-not $reply.Ok) {
            Write-Host ("  refused: {0}" -f $reply.Error) -ForegroundColor Yellow
        }
    }

    $reply = Invoke-Roulette "/roulette/spin"

    if (-not $reply.Ok) {
        Write-Host ("  refused: {0}" -f $reply.Error) -ForegroundColor Yellow
        continue
    }

    $last = $reply.Table.Last

    Write-Host ("  the ball landed in {0} {1}, position {2} of {3}" -f `
        $last.Label, $last.Colour.ToLower(), $last.Position, $reply.Table.Pockets.Count) -ForegroundColor Green

    foreach ($outcome in $last.Outcomes) {
        $verdict = if ($outcome.Won) { "won {0,12:N0}" -f $outcome.Returned } else { "lost" }
        Write-Host ("    {0,12:N0}  {1,-28} {2}" -f $outcome.Amount, $outcome.Description, $verdict)
    }

    Write-Host ("  {0:N0} staked, {1:N0} back, {2:N0} on the spin" -f `
        $last.Staked, $last.Returned, $last.Profit)

    # Turning the wheel again on a settled table is what opens the next one.
    Invoke-Roulette "/roulette/spin" | Out-Null
}

Invoke-Roulette "/roulette/leave" | Out-Null

Write-Host ""
Write-Host "  left the table." -ForegroundColor DarkGray
Write-Host ""
