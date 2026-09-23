<#
.SYNOPSIS
    Fires overlapping slot pulls and pings at one profile, to reproduce the
    2026-09-22 profile corruption -- or to show it is gone.

.DESCRIPTION
    On AUTO the client's post-spin sync can land while the next pull is still running.
    That sync is a ping, a ping refunds anything in escrow, and a live pull holds its
    stake in escrow -- so the ping paid the stake back from a second thread while the
    pull was writing to the same stash. The double-pay was the refund; the second
    writer was what left a null in Inventory.Items and took the launcher down.

    This does the same thing without a game client, and far more often than AUTO
    manages: each round sends one pull and several pings at the same instant.

    It then checks the only invariant that matters -- the wallet moved by exactly
    what the pulls said they paid, less what they staked. A refund of a live pull
    shows up as a surplus of exactly one stake.

    **Point it at a throwaway profile.** Against a build without the fix, it is
    meant to corrupt one. Back up SPT_Runtime\user\profiles first either way.

.PARAMETER SessionId
    The profile id -- the filename under SPT_Runtime\user\profiles\.

.EXAMPLE
    .\stress.ps1 -SessionId 6a8cd3a7e0b8272790f41285 -Rounds 300
#>
param(
    [Parameter(Mandatory = $true)][string]$SessionId,
    [string]$Server = "https://127.0.0.1:6969",
    [int]$Rounds = 200,
    [int]$PingsPerPull = 3,
    [int]$Stake = 5000
)

$ErrorActionPreference = "Stop"

# In C# rather than PowerShell because the requests have to overlap. Windows
# PowerShell has no -Parallel, and a scriptblock certificate callback would be
# invoked on a thread-pool thread with no runspace, which kills the process.
Add-Type -ReferencedAssemblies System.Net.Http -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

public static class SlotHammer
{
    public static List<string[]> Run(string server, string session, int rounds, int pings, int stake)
    {
        // Loopback only, and SPT's certificate is self-signed.
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;
        ServicePointManager.DefaultConnectionLimit = 64;

        var handler = new HttpClientHandler { CookieContainer = new CookieContainer() };
        handler.CookieContainer.Add(new Uri(server), new Cookie("PHPSESSID", session));

        var results = new List<string[]>();

        using (var client = new HttpClient(handler))
        {
            client.DefaultRequestHeaders.Add("requestcompressed", "0");
            client.DefaultRequestHeaders.Add("responsecompressed", "0");

            var pull = "{\"Wallet\":\"Roubles\",\"Stake\":" + stake + ",\"IgnoreMaximum\":false}";

            for (var round = 0; round < rounds; round++)
            {
                var sent = new List<KeyValuePair<string, Task<HttpResponseMessage>>>();
                sent.Add(Post(client, server, "/slots/pull", pull));

                for (var i = 0; i < pings; i++)
                {
                    sent.Add(Post(client, server, "/slots/ping", "{}"));
                }

                foreach (var entry in sent)
                {
                    string body;

                    try
                    {
                        body = entry.Value.Result.Content.ReadAsStringAsync().Result;
                    }
                    catch (Exception ex)
                    {
                        body = "EXCEPTION " + ex.GetBaseException().Message;
                    }

                    results.Add(new[] { entry.Key, body });
                }
            }
        }

        return results;
    }

    private static KeyValuePair<string, Task<HttpResponseMessage>> Post(
        HttpClient client, string server, string route, string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return new KeyValuePair<string, Task<HttpResponseMessage>>(route, client.PostAsync(server + route, content));
    }
}
"@

# The balance is read by a ping sent alone, before and after. The first one also
# settles anything already in escrow, so a refund from before this run cannot be
# counted as one this run caused.
[System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
$headers = @{ "Content-Type" = "application/json"; "requestcompressed" = "0"; "responsecompressed" = "0" }
$webSession = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$webSession.Cookies.Add((New-Object System.Net.Cookie("PHPSESSID", $SessionId, "/", ([Uri]$Server).Host)))

function Ping-Alone {
    Invoke-RestMethod -Uri "$Server/slots/ping" -Method Post -Headers $headers -Body "{}" -WebSession $webSession
}

$first = Ping-Alone
if (-not $first.HasProfile) { throw "No profile for session '$SessionId'. Is the server up and the id right?" }

$before = [long]$first.Balances.Roubles
$needed = [long]$Stake * $Rounds
Write-Host "start: $($before.ToString('N0')) roubles; $Rounds rounds of 1 pull + $PingsPerPull pings at $($Stake.ToString('N0'))"
if ($before -lt $Stake) { throw "Not enough roubles for even one pull." }
if ($before -lt $needed) { Write-Warning "Only enough for some of the pulls if they all lose; later ones will be refused, which is fine." }

$results = [SlotHammer]::Run($Server, $SessionId, $Rounds, $PingsPerPull, $Stake)

$net = 0L
$pulls = 0
$refused = 0
$refunds = 0
$failures = @()

foreach ($r in $results) {
    $route = $r[0]
    $body = $r[1]

    if ($body.StartsWith("EXCEPTION") -or -not $body.StartsWith("{")) {
        $failures += "$route -> $body"
        continue
    }

    $reply = $body | ConvertFrom-Json

    # The note is only ever set by a refund. Nothing in this run crashed, so any
    # refund at all is a live pull being paid back.
    if ($reply.Note) { $refunds++ }

    if ($route -eq "/slots/pull") {
        if ($reply.Pull) {
            $pulls++
            $net += [long]$reply.Pull.Paid - [long]$reply.Pull.Staked
        }
        else {
            $refused++
        }
    }
}

$after = [long](Ping-Alone).Balances.Roubles
$drift = ($after - $before) - $net

Write-Host ""
Write-Host "pulls settled:    $pulls  (refused: $refused)"
Write-Host "failed requests:  $($failures.Count)"
Write-Host "refund notes:     $refunds"
Write-Host "wallet moved:     $(($after - $before).ToString('N0'))"
Write-Host "pulls say:        $($net.ToString('N0'))"
Write-Host "drift:            $($drift.ToString('N0'))"

$failures | Select-Object -First 5 | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }

Write-Host ""
if ($refunds -eq 0 -and $drift -eq 0 -and $failures.Count -eq 0) {
    Write-Host "CLEAN -- now check the server log for '[Slots]' errors too." -ForegroundColor Green
}
else {
    Write-Host "BROKEN -- a nonzero drift or any refund note is the bug. Check the profile before playing it again." -ForegroundColor Red
}
