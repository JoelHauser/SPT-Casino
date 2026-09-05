<#
.SYNOPSIS
    Plays hold'em against a running SPT server, with no game client attached.

.DESCRIPTION
    Pings first, then sits down and plays. The ping is the important half on a first
    run: it proves the mod loaded, the route is reachable, the session resolved to a
    real profile, and its money can be read. If the ping fails there is no point
    running the rest.

    THIS STAKES REAL CURRENCY. Sitting down debits the buy-in from the profile and
    standing up pays back whatever is left, at one chip to the rouble. Use -PingOnly
    if you only want to prove the mod is loaded and reachable.

    Watch the server console alongside this. Every line the mod writes is prefixed
    "[Poker]", so it can be filtered out of the noise.

    Three things about SPT's HTTP layer are handled below and each cost a round trip
    to discover on Blackjack: it serves HTTPS with a self-signed certificate, it
    zlib-inflates every request body unless told not to, and the session id has to
    travel in a WebRequestSession rather than a header.

.PARAMETER SessionId
    The profile id. Find it in the filename under SPT\user\profiles\.

.PARAMETER PingOnly
    Stop after the health check without sitting down.

.PARAMETER Hands
    How many hands to play. The script calls, checks and folds its way through them;
    the point is to exercise the routes rather than to play well.

.EXAMPLE
    .\smoke.ps1 -SessionId 66e4a1b2c3d4e5f6a7b8c9d0 -PingOnly

.EXAMPLE
    .\smoke.ps1 -SessionId 66e4a1b2c3d4e5f6a7b8c9d0 -Seats 4 -Hands 5
#>
param(
    [Parameter(Mandatory = $true)][string]$SessionId,
    [string]$Server = "https://127.0.0.1:6969",
    [int]$Seats = 4,
    [int]$BuyIn = 1000000,
    [int]$BigBlind = 20000,
    [int]$Hands = 3,
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

function Invoke-Poker {
    param([string]$Route, [hashtable]$Body = @{})

    $json = $Body | ConvertTo-Json -Compress

    try {
        return Invoke-RestMethod -Uri "$Server$Route" -Method Post -Headers $headers -Body $json -WebSession $webSession
    }
    catch {
        Write-Host "  request to $Route failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  a closed connection usually means the scheme is wrong -- 4.1 serves https," -ForegroundColor DarkGray
        Write-Host "  not http. A refused connection means the server is not running. A 404 means" -ForegroundColor DarkGray
        Write-Host "  the mod did not load -- check the server console for a [Poker] banner." -ForegroundColor DarkGray
        exit 1
    }
}

Write-Host "Poker smoke test -> $Server" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------------------- ping

$ping = Invoke-Poker "/poker/ping"

Write-Host "  mod version   $($ping.modVersion)"
Write-Host "  session       '$($ping.sessionId)'"
Write-Host "  profile       $(if ($ping.hasProfile) { 'found' } else { 'NOT FOUND' })" -ForegroundColor $(if ($ping.hasProfile) { 'Green' } else { 'Red' })

if (-not $ping.hasProfile) {
    Write-Host ""
    Write-Host "  A blank session id above means the cookie did not resolve." -ForegroundColor Yellow
    Write-Host "  Check the id against the filename under SPT\user\profiles\." -ForegroundColor Yellow
    exit 1
}

Write-Host "  balances:"
foreach ($wallet in $ping.balances.PSObject.Properties) {
    Write-Host ("    {0,-12} {1,14:N0}" -f $wallet.Name, $wallet.Value)
}

if ($ping.chipsAreNotional) {
    Write-Host ""
    Write-Host "  The chips are notional in this build. Nothing above is at stake." -ForegroundColor DarkGray
}
else {
    Write-Host ""
    Write-Host "  Sitting down will debit $BuyIn from the balance above." -ForegroundColor Yellow
}

if ($PingOnly) {
    Write-Host ""
    Write-Host "  Ping only. The mod is loaded, reachable, and can read the profile." -ForegroundColor Green
    exit 0
}

# -------------------------------------------------------------------------- sit

Write-Host ""
$table = Invoke-Poker "/poker/sit" @{ Seats = $Seats; BuyIn = $BuyIn; BigBlind = $BigBlind }

if (-not $table.ok) {
    Write-Host "  sit refused: $($table.error)" -ForegroundColor Red
    exit 1
}

Write-Host "  sat down against: $($table.characters -join ', ')" -ForegroundColor Green

function Show-Table {
    param($view)

    $board = if ($view.community.Count) { $view.community -join " " } else { "--" }
    Write-Host ("  {0}  pot {1:N0}  board {2}" -f $view.street, $view.pot, $board)

    foreach ($seat in $view.seats) {
        $cards = if ($seat.cards.Count) { $seat.cards -join " " } else { "?? ??" }
        $marks = @()
        if ($seat.folded) { $marks += "folded" }
        if ($seat.isAllIn) { $marks += "all-in" }
        if ($seat.isTurn) { $marks += "<- to act" }

        Write-Host ("    {0,-10} {1,8:N0}  {2,-7} {3}" -f $seat.name, $seat.stack, $cards, ($marks -join " "))
    }
}

# ------------------------------------------------------------------------ play

for ($hand = 1; $hand -le $Hands; $hand++) {
    Write-Host ""
    Write-Host "  --- hand $hand ---" -ForegroundColor Cyan

    $response = Invoke-Poker "/poker/deal"

    if (-not $response.ok) {
        Write-Host "  deal refused: $($response.error)" -ForegroundColor Red
        break
    }

    $guard = 0

    while ($response.table.awaitingPlayer -and $guard -lt 40) {
        $guard++
        Show-Table $response.table

        # Call when something is owed, check when nothing is. Enough to walk every
        # street without pretending to play well.
        $options = $response.table.options
        $move = if ($options.moves -contains "Check") { "Check" } else { "Call" }

        Write-Host "    -> $move" -ForegroundColor DarkGray
        $response = Invoke-Poker "/poker/act" @{ Move = $move; To = 0 }

        if (-not $response.ok) {
            Write-Host "    refused: $($response.error)" -ForegroundColor Yellow
        }
    }

    Show-Table $response.table
}

Write-Host ""
Invoke-Poker "/poker/leave" | Out-Null
Write-Host "  left the table." -ForegroundColor Green
