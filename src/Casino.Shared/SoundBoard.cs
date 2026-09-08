using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Casino.Shared
{
    /// <summary>
    /// Every sound cue any table plays. One name each, so "where does this cue fire"
    /// is a search for the name, not a guess -- see the call sites listed on
    /// <see cref="SoundBoard"/>.
    ///
    /// Deliberately by *event*, not by file: a table asks for what just happened
    /// (<see cref="CardDeal"/>, <see cref="SlotJackpot"/>) and never names a clip.
    /// That is what lets every clip be swapped later without touching a single call
    /// site -- the whole point of this being boilerplate ahead of having any sounds
    /// at all.
    /// </summary>
    internal enum Cue
    {
        /// <summary>A card sliding in from the dealer point. Poker and Blackjack both fire this once per card, already staggered by DealAnimator -- see Deal.</summary>
        CardDeal,

        /// <summary>A card already on the table turning over. Poker's showdown, Blackjack's hole card -- see DealAnimator.Flip. Timed to the swap instant, not the start of the shrink.</summary>
        CardFlip,

        /// <summary>A wager committed -- Blackjack's bet confirmed, Poker's Bet/Call/Raise/All-in pressed. Not fired on every chip redraw; only on the action that actually moves money.</summary>
        ChipBet,

        /// <summary>A chip landing on the felt grid -- Roulette's Place, where the chip has a spot on the table rather than just an amount.</summary>
        ChipPlace,

        /// <summary>The wheel and ball starting their spin.</summary>
        RouletteSpinStart,

        /// <summary>The ball settling into its pocket -- the spin's own coroutine ending, not the server reply that triggered it.</summary>
        RouletteBallLand,

        /// <summary>A reel starting to spin. Fires once per reel, staggered the way the reels themselves are.</summary>
        SlotReelSpin,

        /// <summary>A reel stopping on its landing row.</summary>
        SlotReelStop,

        /// <summary>The plain WIN tier -- multiple &gt;= 1.</summary>
        SlotWin,

        /// <summary>BIG WIN -- multiple &gt;= 5.</summary>
        SlotBigWin,

        /// <summary>HUGE WIN -- multiple &gt;= 20.</summary>
        SlotHugeWin,

        /// <summary>JACKPOT -- multiple &gt;= 100.</summary>
        SlotJackpot,
    }

    /// <summary>
    /// Plays a <see cref="Cue"/> by loading a file named for it out of a "sounds"
    /// folder beside the plugin -- <c>sounds/card-deal.wav</c>, <c>sounds/card-deal.ogg</c>,
    /// whichever exists -- the same arrangement <see cref="CardView"/> and
    /// <see cref="ChipView"/> already use for art. That is deliberate: dropping a
    /// file in with the right name is the entire integration a future session needs
    /// to do. No code here needs to change to add, remove or replace a sound.
    ///
    /// Silent, not broken, when a file is missing -- table art already treats a
    /// missing asset this way (a drawn card in place of a photo, a flat green felt
    /// in place of a photograph), and a cue nobody has recorded yet is exactly that
    /// case. Every table plays correctly today, with nothing to hear, and gains
    /// sound the moment a file is dropped in -- no rebuild, since it is read from
    /// disk at request time, not baked into the DLL.
    ///
    /// Timing is the caller's job, on purpose. This only plays a clip; it does not
    /// know when a flip finishes or a reel stops, and should not -- DealAnimator
    /// already owns that timing, and duplicating it here would be the two
    /// eventually disagreeing. Call <see cref="Play"/> from the exact line that
    /// already knows the moment: the coroutine that starts a slide, the frame a
    /// flip's swap happens, the callback a spin coroutine reports finishing to.
    /// </summary>
    internal static class SoundBoard
    {
        private const string Folder = "sounds";

        /// <summary>
        /// The file `Play` looks for, per cue, without an extension -- both `.wav`
        /// and `.ogg` are tried. Named for what happened, not for a game system, so
        /// the folder reads as a checklist of what can still be recorded.
        /// </summary>
        private static readonly Dictionary<Cue, string> FileNames = new Dictionary<Cue, string>
        {
            [Cue.CardDeal] = "card-deal",
            [Cue.CardFlip] = "card-flip",
            [Cue.ChipBet] = "chip-bet",
            [Cue.ChipPlace] = "chip-place",
            [Cue.RouletteSpinStart] = "roulette-spin-start",
            [Cue.RouletteBallLand] = "roulette-ball-land",
            [Cue.SlotReelSpin] = "slot-reel-spin",
            [Cue.SlotReelStop] = "slot-reel-stop",
            [Cue.SlotWin] = "slot-win",
            [Cue.SlotBigWin] = "slot-win-big",
            [Cue.SlotHugeWin] = "slot-win-huge",
            [Cue.SlotJackpot] = "slot-win-jackpot",
        };

        // Loaded once per cue and kept -- a card deals dozens of times a session,
        // and re-reading the same file off disk every time would be needless I/O
        // for something that never changes mid-session.
        private static readonly Dictionary<Cue, AudioClip> _cache = new Dictionary<Cue, AudioClip>();

        // A cue whose file does not exist is asked for constantly -- every card,
        // every hand -- and is not going to start existing between one Play call
        // and the next. Tried once, remembered, never retried: the alternative is
        // a coroutine and a failed disk read on every single card dealt.
        private static readonly HashSet<Cue> _attempted = new HashSet<Cue>();

        private static AudioSource _source;

        /// <summary>
        /// Plays <paramref name="cue"/> now, at <paramref name="volume"/> (0-1). Does
        /// nothing if the plugin host is not set yet, the cue's file does not exist,
        /// or the file failed to load -- see the type's own doc for why that is the
        /// right behaviour rather than a fallback tone or an exception.
        /// </summary>
        internal static void Play(Cue cue, float volume = 1f)
        {
            var host = Host.Plugin;
            if (host == null)
            {
                return;
            }

            if (_cache.TryGetValue(cue, out var clip))
            {
                PlayClip(clip, volume);
                return;
            }

            if (_attempted.Contains(cue))
            {
                return;
            }

            host.StartCoroutine(LoadThenPlay(cue, volume));
        }

        private static IEnumerator LoadThenPlay(Cue cue, float volume)
        {
            _attempted.Add(cue);

            if (!FileNames.TryGetValue(cue, out var name))
            {
                yield break;
            }

            var path = FindFile(name);
            if (path == null)
            {
                yield break;
            }

            var type = path.EndsWith(".ogg") ? AudioType.OGGVORBIS : AudioType.WAV;

            using (var request = UnityWebRequestMultimedia.GetAudioClip("file://" + path, type))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Host.Warn($"[Casino] could not load sound '{name}': {request.error}");
                    yield break;
                }

                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null)
                {
                    yield break;
                }

                _cache[cue] = clip;
                PlayClip(clip, volume);
            }
        }

        private static string FindFile(string name)
        {
            var folder = Path.Combine(Host.AssetFolder, Folder);

            var wav = Path.Combine(folder, name + ".wav");
            if (File.Exists(wav))
            {
                return wav;
            }

            var ogg = Path.Combine(folder, name + ".ogg");
            return File.Exists(ogg) ? ogg : null;
        }

        private static void PlayClip(AudioClip clip, float volume)
        {
            var source = Source();
            if (source == null)
            {
                return;
            }

            // PlayOneShot, not Play: two cues can overlap -- a chip landing while a
            // card is still mid-slide -- and one AudioSource should not have to
            // choose between them. Unity mixes overlapping one-shots on the same
            // source for free.
            source.PlayOneShot(clip, volume);
        }

        private static AudioSource Source()
        {
            if (_source != null)
            {
                return _source;
            }

            var host = Host.Plugin;
            if (host == null)
            {
                return null;
            }

            _source = host.gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;

            // 2D: every cue here is a HUD/UI event happening for the player looking
            // at a panel, not a thing with a position in the raid world to pan
            // toward. Positional falloff would just make cues quieter for no reason.
            _source.spatialBlend = 0f;

            return _source;
        }
    }
}
