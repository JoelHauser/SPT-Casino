using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace Roulette.Client
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
    /// what it is handed and never decides anything, so a shape it half-understands is
    /// better than a deserialiser that throws on an unfamiliar field.
    /// </summary>
    internal static class RouletteApi
    {
        internal static JObject Ping() => Post("/roulette/ping", "{}");

        internal static JObject State() => Post("/roulette/state", "{}");

        internal static JObject Clear() => Post("/roulette/clear", "{}");

        internal static JObject Spin() => Post("/roulette/spin", "{}");

        internal static JObject Leave() => Post("/roulette/leave", "{}");

        /// <summary>
        /// Lifts chips back off a spot. Zero takes the whole pile.
        ///
        /// PascalCase property names, deliberately, like every other body here. SPT
        /// matches request bodies case-sensitively, so lowercase keys bind nothing and
        /// every field silently takes its default -- which is how a 100,000 stake
        /// arrives as 0 while looking like it bound correctly.
        /// </summary>
        internal static JObject Remove(string kind, int selection, int amount) =>
            Post(
                "/roulette/remove",
                "{\"Kind\":\"" + kind + "\",\"Selection\":" + Num(selection) + ",\"Amount\":" + Num(amount) + "}");

        internal static JObject Place(string kind, int selection, int amount) =>
            Post(
                "/roulette/place",
                "{\"Kind\":\"" + kind + "\",\"Selection\":" + Num(selection) + ",\"Amount\":" + Num(amount) + "}");

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
                    RouletteClientPlugin.Log.LogWarning($"[Roulette] {route} returned nothing.");
                    return null;
                }

                return JObject.Parse(body);
            }
            catch (Exception ex)
            {
                // A failed request must not take the menu down with it. The caller
                // shows the player that something went wrong and stays open.
                RouletteClientPlugin.Log.LogError($"[Roulette] {route} failed: {ex.Message}");
                return null;
            }
        }
    }
}
