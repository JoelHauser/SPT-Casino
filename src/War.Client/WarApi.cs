using System;
using System.Globalization;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace War.Client
{
    /// <summary>
    /// Talks to the server mod, through SPT's own <see cref="RequestHandler"/> -- it
    /// already knows the backend address, attaches the session cookie and handles the
    /// framing the listener expects.
    ///
    /// Responses come back as JObject rather than typed models. The panel renders what
    /// it is handed and never decides anything, so a shape it half-understands is better
    /// than a deserialiser that throws on an unfamiliar field.
    /// </summary>
    internal static class WarApi
    {
        internal static JObject Ping() => Post("/war/ping", "{}");

        internal static JObject State() => Post("/war/state", "{}");

        internal static JObject Stats() => Post("/war/stats", "{}");

        /// <summary>
        /// Deals a hand. PascalCase keys, like every body here: SPT binds request bodies
        /// case-sensitively, and a lowercase key arrives as zero while looking bound.
        /// </summary>
        internal static JObject Deal(string wallet, long ante, long tieBet)
        {
            // Sent every time rather than only when true, so the request says plainly
            // what was asked for. The server still decides.
            var uncapped = WarClientPlugin.NoBetCap?.Value == true;

            return Post(
                "/war/deal",
                "{\"Wallet\":\"" + wallet + "\",\"Ante\":" + Num(ante) + ",\"TieBet\":" + Num(tieBet)
                + ",\"IgnoreMaximum\":" + (uncapped ? "true" : "false") + "}");
        }

        /// <summary>"War" or "Surrender".</summary>
        internal static JObject Decide(string choice) => Post("/war/decide", "{\"Choice\":\"" + choice + "\"}");

        /// <summary>Invariant, so a machine with a comma decimal separator sends a number the server reads.</summary>
        private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static JObject Post(string route, string json)
        {
            try
            {
                var body = RequestHandler.PostJson(route, json);

                if (string.IsNullOrEmpty(body))
                {
                    WarClientPlugin.Log?.LogWarning($"[War] {route} returned nothing.");
                    return null;
                }

                return JObject.Parse(body);
            }
            catch (Exception ex)
            {
                // A failed request must not take the menu down with it. The panel says
                // something went wrong and stays open.
                WarClientPlugin.Log?.LogError($"[War] {route} failed: {ex.Message}");
                return null;
            }
        }
    }
}
