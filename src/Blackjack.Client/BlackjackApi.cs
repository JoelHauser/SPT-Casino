using System;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace Blackjack.Client
{
    /// <summary>
    /// Talks to the server mod.
    ///
    /// Everything goes through SPT's own <see cref="RequestHandler"/>, which is worth
    /// insisting on: it already knows the backend address, attaches the PHPSESSID
    /// cookie, speaks HTTPS to the self-signed certificate and handles the zlib
    /// framing the listener expects. Every one of those caught out the PowerShell
    /// harness that talks to the same routes, each failing with a message about
    /// something else entirely.
    ///
    /// Responses come back as JObject rather than typed models. The client renders
    /// what it is handed and never decides anything, so a shape it half-understands
    /// is better than a deserialiser that throws on an unfamiliar field.
    /// </summary>
    internal static class BlackjackApi
    {
        internal static JObject Ping() => Post("/blackjack/ping", "{}");

        internal static JObject State() => Post("/blackjack/state", "{}");

        /// <summary>
        /// The lifetime record. Unlike the others this comes back as the stats object
        /// itself rather than wrapped in a response, because nothing about it can fail
        /// in a way the player needs telling about.
        /// </summary>
        internal static JObject Stats() => Post("/blackjack/stats", "{}");

        /// <summary>
        /// PascalCase property names, deliberately. SPT matches request bodies
        /// case-sensitively, so lowercase keys bind nothing and every field silently
        /// takes its default -- a wager of 0, refused for being out of range.
        /// </summary>
        internal static JObject Deal(string wallet, long wager)
        {
            // The server enforces the table maximum unless told the player has turned
            // it off. Sent every time rather than only when true, so the request says
            // plainly what was asked for.
            var ignore = BlackjackClientPlugin.EnforceTableMaximum?.Value == false;

            return Post(
                "/blackjack/deal",
                "{\"Wallet\":\"" + wallet + "\",\"Wager\":" + wager
                + ",\"IgnoreMaximum\":" + (ignore ? "true" : "false") + "}");
        }

        internal static JObject Act(string action) =>
            Post("/blackjack/action", "{\"Action\":\"" + action + "\"}");

        private static JObject Post(string route, string json)
        {
            try
            {
                var body = RequestHandler.PostJson(route, json);
                if (string.IsNullOrEmpty(body))
                {
                    BlackjackClientPlugin.Log.LogWarning($"[Blackjack] {route} returned nothing.");
                    return null;
                }

                return JObject.Parse(body);
            }
            catch (Exception ex)
            {
                // A failed request must not take the menu down with it. The caller
                // shows the player that something went wrong and stays open.
                BlackjackClientPlugin.Log.LogError($"[Blackjack] {route} failed: {ex.Message}");
                return null;
            }
        }
    }
}
