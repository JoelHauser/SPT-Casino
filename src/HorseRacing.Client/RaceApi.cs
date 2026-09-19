using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace HorseRacing.Client
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
    internal static class RaceApi
    {
        internal static JObject Ping() => Post("/races/ping", "{}");

        internal static JObject Stats() => Post("/races/stats", "{}");

        /// <summary>
        /// Sends the slip and runs the race at a named course.
        ///
        /// PascalCase property names, deliberately, like every other body here. SPT
        /// matches request bodies case-sensitively, so lowercase keys bind nothing and
        /// every field silently takes its default -- which is how a 100,000 stake
        /// arrives as 0 while looking like it bound correctly.
        ///
        /// The body is built by hand rather than serialised, for the same reason the
        /// three sibling tables build theirs by hand: a serialiser configured
        /// elsewhere is a serialiser that can start emitting camelCase without this
        /// file changing.
        /// </summary>
        internal static JObject Place(
            string track, IEnumerable<SlipBet> bets, string wallet, bool ignoreMaximum)
        {
            var body = new StringBuilder();

            body.Append("{\"Track\":\"").Append(track)
                .Append("\",\"Wallet\":\"").Append(wallet).Append("\",\"IgnoreMaximum\":")
                .Append(ignoreMaximum ? "true" : "false")
                .Append(",\"Bets\":[");

            var first = true;

            foreach (var bet in bets)
            {
                if (!first)
                {
                    body.Append(',');
                }

                first = false;

                body.Append("{\"Kind\":\"").Append(bet.Kind)
                    .Append("\",\"First\":").Append(Num(bet.First))
                    .Append(",\"Second\":").Append(Num(bet.Second))
                    .Append(",\"Stake\":").Append(Num(bet.Stake))
                    .Append('}');
            }

            body.Append("]}");

            return Post("/races/place", body.ToString());
        }

        /// <summary>
        /// Invariant formatting, so a machine with a comma decimal separator does not
        /// send a number the server's parser rejects.
        /// </summary>
        private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static JObject Post(string route, string json)
        {
            try
            {
                var body = RequestHandler.PostJson(route, json);

                if (string.IsNullOrEmpty(body))
                {
                    RaceClientPlugin.Log.LogWarning($"[Races] {route} returned nothing.");
                    return null;
                }

                return JObject.Parse(body);
            }
            catch (Exception ex)
            {
                // A failed request must not take the menu down with it. The caller
                // shows the player that something went wrong and stays open.
                RaceClientPlugin.Log.LogError($"[Races] {route} failed: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// One bet as the panel holds it, before it goes over the wire.
    ///
    /// A struct of plain fields rather than the engine's <c>Bet</c>: the client does
    /// not reference <c>HorseRacing.Game</c> at all, and should not. It renders what
    /// the server sent and sends back what the player clicked -- every decision about
    /// what a bet means belongs on the other side of the wire, where it is tested.
    /// </summary>
    internal sealed class SlipBet
    {
        internal string Kind;

        internal int First;

        internal int Second;

        internal long Stake;

        /// <summary>The board key this bet came from, so a second click can find it.</summary>
        internal string Key => Kind + ":" + First + ":" + Second;
    }
}
