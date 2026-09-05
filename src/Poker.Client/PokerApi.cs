using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace Poker.Client
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
    internal static class PokerApi
    {
        internal static JObject Ping() => Post("/poker/ping", "{}");

        internal static JObject State() => Post("/poker/state", "{}");

        internal static JObject Deal() => Post("/poker/deal", "{}");

        internal static JObject Leave() => Post("/poker/leave", "{}");

        /// <summary>
        /// Sits the player down. PascalCase property names, deliberately: SPT matches
        /// request bodies case-sensitively, so lowercase keys bind nothing and every
        /// field silently takes its default -- which is how a 10,000 buy-in arrives as
        /// a 5,000 one without anything appearing to go wrong.
        /// </summary>
        internal static JObject Sit(int seats, int buyIn, int bigBlind, int? seed = null)
        {
            var body =
                "{\"Seats\":" + Num(seats)
                + ",\"BuyIn\":" + Num(buyIn)
                + ",\"BigBlind\":" + Num(bigBlind)
                + (seed.HasValue ? ",\"Seed\":" + Num(seed.Value) : string.Empty)
                + "}";

            return Post("/poker/sit", body);
        }

        /// <summary>
        /// One betting action. <paramref name="to"/> is the total the seat is raising
        /// to, and is ignored by the server for moves that do not carry a size.
        /// </summary>
        internal static JObject Act(string move, int to = 0) =>
            Post("/poker/act", "{\"Move\":\"" + move + "\",\"To\":" + Num(to) + "}");

        /// <summary>
        /// Invariant formatting, so a machine with a comma decimal separator does not
        /// send a number the server's parser rejects.
        /// </summary>
        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static JObject Post(string route, string json)
        {
            try
            {
                var body = RequestHandler.PostJson(route, json);
                if (string.IsNullOrEmpty(body))
                {
                    PokerClientPlugin.Log.LogWarning($"[Poker] {route} returned nothing.");
                    return null;
                }

                return JObject.Parse(body);
            }
            catch (Exception ex)
            {
                // A failed request must not take the menu down with it. The caller
                // shows the player that something went wrong and stays open.
                PokerClientPlugin.Log.LogError($"[Poker] {route} failed: {ex.Message}");
                return null;
            }
        }
    }
}
