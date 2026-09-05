<#
.SYNOPSIS
    Plays a hand of blackjack against a running SPT server, with no game client.

.DESCRIPTION
    Pings first, then plays a round. The ping is the important half on a first run:
    it proves the mod loaded, the route is reachable, the session resolved to a real
    profile, and its money can be read -- all before a rouble is at stake. If the
    ping fails there is no point running the rest.

    Watch the server console alongside this. Every line the mod writes is prefixed
    "[Blackjack]", so it can be filtered out of the noise.

    Verified against a real 4.1.3 server. Two things were wrong when this was written
    blind, and both are fixed above: the server speaks HTTPS rather than HTTP, and its
    certificate is self-signed, so the callback has to be told to accept it.

    Still assumed: that the session id travels as a PHPSESSID cookie. If the ping
    returns a blank sessionId, that is the assumption to revisit. Note 4.1 also has
    webAuthenticationConfig enabled in http.json, which may gate these routes
    independently of the cookie.

.PARAMETER SessionId
    The profile id. Find it in the filename under SPT\user\profiles\.

.PARAMETER PingOnly
    Stop after the health check without betting anything.

.PARAMETER Play
    The action to take on the first decision. Everything after it stands, since the
    point is to exercise one path rather than to play well. Double and Split are only
    legal on some hands -- see -DealUntilPlayable.

.PARAMETER DealUntilPlayable
    How many rounds to deal looking for one where -Play is legal. A pair turns up
    roughly one hand in thirteen, so hunting for a Split needs a few dozen. Each
    attempt stakes real money; keep -Wager at the minimum while hunting.

.EXAMPLE
    .\smoke.ps1 -SessionId 66e4a1b2c3d4e5f6a7b8c9d0 -PingOnly

.EXAMPLE
    .\smoke.ps1 -SessionId 66e4a1b2c3d4e5f6a7b8c9d0 -Wallet Dollars -Wager 500
#>
param(
    [Parameter(Mandatory = $true)][string]$SessionId,
    [string]$Server = "https://127.0.0.1:6969",
    [ValidateSet("Roubles", "Dollars", "Euros")][string]$Wallet = "Roubles",
    [int]$Wager = 10000,
    [ValidateSet("Stand", "Hit", "Double", "Split")][string]$Play = "Stand",
    [int]$DealUntilPlayable = 1,
    [switch]$PingOnly
)

$ErrorActionPreference = "Stop"

# SPT 4.1 serves HTTPS on the same port it used to serve HTTP, using a self-signed
# certificate it generates into user\certs\. .NET rejects that by default, and the
# failure surfaces as "the underlying connection was closed" rather than anything
# mentioning certificates -- which reads exactly like the server being down.
#
# Trusting it is safe here: this only ever talks to a loopback address.
if ($Server.StartsWith("https:")) {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
}

# SPT's listener zlib-inflates every request body and zlib-deflates every response,
# because that is what the EFT client speaks. Two headers opt out of both halves,
# and without them a plain-JSON request dies inside Inflater with "the archive entry
# was compressed using an unsupported compression method" -- an error that names
# neither the header nor the body.
#
#   requestcompressed: 0   read my body as plain UTF-8, do not inflate it
#   responsecompressed: 0  reply in plain JSON, do not deflate it
#
# Read out of SptHttpListener.HandleAsync and IsDebugRequest in 4.1.3.
$headers = @{
    "Content-Type"       = "application/json"
    "requestcompressed"  = "0"
    "responsecompressed" = "0"
}

# The session id travels as a PHPSESSID cookie -- SPT reads it with
# Request.Cookies.TryGetValue("PHPSESSID", ...) in HttpServer.HandleRequestAsync.
#
# It cannot be passed through -Headers. "Cookie" is a restricted header, and
# PowerShell drops it silently rather than complaining, so the request arrives with
# no session at all and the server answers "session id provided was empty, did you
# restart the server while the game was running?" -- which sends you looking in
# entirely the wrong place. It has to go in a WebRequestSession.
$webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$serverUri = [Uri]$Server
$webSession.Cookies.Add((New-Object System.Net.Cookie("PHPSESSID", $SessionId, "/", $serverUri.Host)))

function Invoke-Blackjack {
    param([string]$Route, [hashtable]$Body = @{})

    $json = $Body | ConvertTo-Json -Compress
    try {
        return Invoke-RestMethod -Uri "$Server$Route" -Method Post -Headers $headers -Body $json -WebSession $webSession
    }
    catch {
        Write-Host "  request to $Route failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  a closed connection usually means the scheme is wrong -- 4.1 serves https," -ForegroundColor DarkGray
        Write-Host "  not http. A refused connection means the server is not running. A 404 means" -ForegroundColor DarkGray
        Write-Host "  the mod did not load -- check the server console for a [Blackjack] banner." -ForegroundColor DarkGray
        exit 1
    }
}

# SPT serialises enums as their integer values, not their names, so the wire carries
# phase 1 rather than "PlayerTurn". Comparing against the name silently never matches,
# which left a dealt hand sitting in PlayerTurn with the stake in escrow and this
# script reporting success. Accept either form.
$RoundPhaseNames = @("AwaitingBet", "PlayerTurn", "DealerTurn", "Settled")
$HandOutcomeNames = @("Pending", "Win", "Lose", "Push", "Blackjack", "Bust")
$PlayerActionNames = @("Hit", "Stand", "Double", "Split")

function ConvertTo-EnumName {
    param($Value, [string[]]$Names)

    if ($null -eq $Value) { return $null }
    $index = 0
    if ([int]::TryParse("$Value", [ref]$index)) {
        if ($index -ge 0 -and $index -lt $Names.Count) { return $Names[$index] }
        return "$Value"
    }
    return "$Value"
}

function Show-Round {
    param($Response)

    if (-not $Response.ok) {
        Write-Host "  refused: $($Response.error)" -ForegroundColor Yellow
    }

    $round = $Response.round
    if (-not $round) { return }

    Write-Host "  dealer  $($round.dealer.cards -join ' ') ($($round.dealer.value))"
    foreach ($hand in $round.playerHands) {
        $outcomeName = ConvertTo-EnumName $hand.outcome $HandOutcomeNames
        $outcome = if ($outcomeName -eq "Pending") { "" } else { "  $outcomeName" }
        Write-Host "  player  $($hand.cards -join ' ') ($($hand.value))  staked $($hand.wager)$outcome"
    }

    Write-Host "  phase $(ConvertTo-EnumName $round.phase $RoundPhaseNames)   balance $($Response.balance) $($Response.wallet)"
    if ($round.availableActions) {
        $legal = $round.availableActions | ForEach-Object { ConvertTo-EnumName $_ $PlayerActionNames }
        Write-Host "  legal: $($legal -join ', ')" -ForegroundColor DarkGray
    }
}

# ---- health check -----------------------------------------------------------

Write-Host "Pinging $Server" -ForegroundColor Cyan
$ping = Invoke-Blackjack -Route "/blackjack/ping"

Write-Host "  mod version   $($ping.modVersion)"
Write-Host "  session       '$($ping.sessionId)'"
Write-Host "  profile       $(if ($ping.hasProfile) { 'found' } else { 'NOT FOUND' })"

if (-not $ping.sessionId) {
    Write-Host ""
    Write-Host "The server did not resolve a session. The cookie is not reaching it -- check" -ForegroundColor Red
    Write-Host "that -WebSession is still being passed, and that the id matches a filename in" -ForegroundColor Red
    Write-Host "SPT_Runtime\user\profiles\ without the .json." -ForegroundColor Red
    exit 1
}

if (-not $ping.hasProfile) {
    Write-Host ""
    Write-Host "Session resolved but no profile matched it. Check the id against the" -ForegroundColor Red
    Write-Host "filename in SPT\user\profiles\." -ForegroundColor Red
    exit 1
}

foreach ($k in $ping.balances.PSObject.Properties) {
    Write-Host "  $($k.Name.PadRight(13))$($k.Value)"
}

if ($PingOnly) {
    Write-Host ""
    Write-Host "Ping OK. The mod is loaded, reachable, and can read the profile." -ForegroundColor Green
    exit 0
}

# ---- play a round -----------------------------------------------------------

Write-Host ""
Write-Host "Dealing $Wager $Wallet, aiming to $Play" -ForegroundColor Cyan
# PascalCase deliberately. SPT deserialises request bodies case-sensitively, so
# lowercase keys bind nothing and every property silently takes its default -- which
# for DealRequest means wager 0, refused as "bets run from 1,000 to 500,000" while
# the number you passed was well inside that range.
$state = Invoke-Blackjack -Route "/blackjack/deal" -Body @{ Wallet = $Wallet; Wager = $Wager }
Show-Round $state

# Hunt for a hand where the requested action is legal. Double needs a two-card hand
# and Split needs a pair, so neither is reachable by dealing once and hoping.
$attempt = 1
while ($state.ok -and $Play -ne "Stand" -and $attempt -lt $DealUntilPlayable) {
    $legal = @($state.round.availableActions | ForEach-Object { ConvertTo-EnumName $_ $PlayerActionNames })
    if ($legal -contains $Play) { break }

    # Not this hand. Stand it off and deal another.
    while ((ConvertTo-EnumName $state.round.phase $RoundPhaseNames) -eq "PlayerTurn") {
        $state = Invoke-Blackjack -Route "/blackjack/action" -Body @{ Action = "Stand" }
    }

    $attempt++
    $state = Invoke-Blackjack -Route "/blackjack/deal" -Body @{ Wallet = $Wallet; Wager = $Wager }
    if (-not $state.ok) {
        Write-Host "  refused on attempt ${attempt}: $($state.error)" -ForegroundColor Yellow
        break
    }
}

if ($Play -ne "Stand") {
    $legal = @($state.round.availableActions | ForEach-Object { ConvertTo-EnumName $_ $PlayerActionNames })
    if ($legal -contains $Play) {
        Write-Host "  $Play is legal on attempt $attempt" -ForegroundColor Green
        Show-Round $state
        Write-Host "$Play..." -ForegroundColor Cyan
        $state = Invoke-Blackjack -Route "/blackjack/action" -Body @{ Action = $Play }
        Show-Round $state
    }
    else {
        Write-Host "  $Play never came up in $attempt hand(s) -- standing instead." -ForegroundColor Yellow
    }
}

# Whatever happened above, play the round out. Standing is enough: the point is that
# it settles and the money matches, not that it is played well.
while ($state.ok -and (ConvertTo-EnumName $state.round.phase $RoundPhaseNames) -eq "PlayerTurn") {
    Write-Host "Standing..." -ForegroundColor Cyan
    $state = Invoke-Blackjack -Route "/blackjack/action" -Body @{ Action = "Stand" }
    Show-Round $state
}

$after = Invoke-Blackjack -Route "/blackjack/ping"
$before = $ping.balances.$Wallet
$now = $after.balances.$Wallet

Write-Host ""
Write-Host "$Wallet $before -> $now (moved $($now - $before))" -ForegroundColor Cyan

if ($before -eq $now -and $state.round.net -ne 0) {
    Write-Host "Balance did not move but the round was not a push -- money is not reaching" -ForegroundColor Red
    Write-Host "the stash. Check the console for a [Blackjack] mismatch line." -ForegroundColor Red
}
else {
    Write-Host "Money moved. Open the stash in-game to confirm it is really there." -ForegroundColor Green
}

$stats = Invoke-Blackjack -Route "/blackjack/stats"
Write-Host "Record: $($stats.roundsPlayed) rounds, $($stats.wins)W/$($stats.pushes)P/$($stats.losses)L"
