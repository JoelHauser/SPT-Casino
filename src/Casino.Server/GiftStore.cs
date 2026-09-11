using System.Reflection;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace Casino.Server;

/// <summary>
/// The record of which profiles have taken which gift, kept in the mod's own folder.
///
/// **The server owns this, not the client.** The welcome card's `seen.txt` sits beside
/// the plugin and is only a display flag, so losing it costs a player a second reading
/// of the intro. This one is money: if the client decided, a reinstall -- or anyone who
/// opened the file -- would be worth another million every time. The client is told
/// what is pending and is never believed about what was paid.
///
/// Same shape and same reasoning as each table's <c>StatsStore</c>: a file under
/// <c>data/</c>, deliberately not a new field on the SPT profile, so removing the mod
/// leaves nothing behind and never needs a wipe.
///
/// The decisions all live in <see cref="GiftLedger"/>, which has no file and no SPT
/// types in it. This is the lock, the disk and the logging around them.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class GiftStore : IGiftStore
{
    private const string FileName = "gifts.json";

    private readonly ISptLogger<GiftStore> _logger;
    private readonly FileUtil _fileUtil;
    private readonly JsonUtil _jsonUtil;
    private readonly string _path;
    private readonly GiftLedger _ledger;

    /// <summary>
    /// Guards the read-decide-write that decides whether a million roubles moves.
    ///
    /// Two requests arriving together is not hypothetical here: the card claims as it
    /// opens, and a double click on the tab opens it twice. Everything that touches
    /// the ledger is inside this lock, so the loser of the race sees the winner's write
    /// and is refused -- rather than both reading "unclaimed" and both paying.
    /// </summary>
    private readonly Lock _gate = new();

    public GiftStore(
        ISptLogger<GiftStore> logger,
        FileUtil fileUtil,
        JsonUtil jsonUtil,
        ModHelper modHelper)
    {
        _logger = logger;
        _fileUtil = fileUtil;
        _jsonUtil = jsonUtil;

        var folder = Path.Combine(
            modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "data");

        _fileUtil.CreateDirectory(folder);
        _path = Path.Combine(folder, FileName);
        _ledger = new GiftLedger(Load(out var readable));

        if (!readable)
        {
            // Deliberately not overwritten with an empty record: the unreadable file is
            // the only evidence of what was already paid, and this refuses to pay
            // anything until a person has looked at it.
            Writable = false;
            return;
        }

        try
        {
            _fileUtil.WriteFile(_path, Serialized());
            Writable = true;
        }
        catch (Exception ex)
        {
            Writable = false;
            _logger.Error($"[Casino] the gift record is not writable at {_path} -- {ex.Message}");
        }
    }

    /// <summary>
    /// Whether the record can be trusted to remember a payment, probed once at
    /// construction. It gates the payment itself -- see <see cref="GiftService"/>.
    ///
    /// False in two cases, and both would otherwise pay the gift again on every open
    /// for ever, because nothing written to say "paid" survives: a mod folder that
    /// cannot be written to, and an existing record that will not parse. Refusing to
    /// pay is the safe end of that -- nobody loses money they already had, and the log
    /// says which of the two it was.
    /// </summary>
    public bool Writable { get; }

    public bool HasClaimed(string giftKey, MongoId sessionId)
    {
        lock (_gate)
        {
            return _ledger.HasClaimed(giftKey, sessionId.ToString());
        }
    }

    /// <summary>
    /// Claims the gift for this profile, or refuses because somebody already has.
    ///
    /// **Marked paid before the money moves, on purpose.** The two failures are not
    /// equal: paying twice invents roubles out of nothing and there is no taking them
    /// back, while a payment that fails after this returns true is recoverable -- it is
    /// logged loudly, and <see cref="Release"/> puts the offer back when the caller
    /// knows for certain that nothing moved.
    /// </summary>
    public bool TryClaim(string giftKey, MongoId sessionId, int amount)
    {
        lock (_gate)
        {
            if (!_ledger.TryClaim(giftKey, sessionId.ToString(), amount, DateTime.UtcNow))
            {
                return false;
            }

            if (Persist())
            {
                return true;
            }

            // The claim is not honoured if it could not be recorded. Paying money the
            // record will have forgotten by the next restart is how one gift becomes an
            // income stream, so the claim is rolled back in memory too.
            _ledger.Release(giftKey, sessionId.ToString());
            return false;
        }
    }

    /// <summary>
    /// Puts the offer back after a payment that provably moved nothing.
    ///
    /// Only ever called from the one path that knows that: the credit threw before
    /// anything was added and the balance is unchanged. A shortfall is not this case --
    /// that money went to the message tab and is the player's.
    /// </summary>
    public void Release(string giftKey, MongoId sessionId)
    {
        lock (_gate)
        {
            if (_ledger.Release(giftKey, sessionId.ToString()))
            {
                Persist();
            }
        }
    }

    /// <summary>
    /// The record as JSON, never null.
    ///
    /// Throwing beats writing whatever a null serialises to: both callers already treat
    /// an exception here as "it did not save", and both refuse to pay on that. Passing
    /// the null through would truncate the file to nothing and look like a fresh
    /// install on the next boot.
    /// </summary>
    private string Serialized() =>
        _jsonUtil.Serialize(_ledger.Claims, true)
        ?? throw new InvalidOperationException("the gift record would not serialise");

    /// <summary>Writes the record out. Called with <see cref="_gate"/> held.</summary>
    private bool Persist()
    {
        try
        {
            _fileUtil.WriteFile(_path, Serialized());
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"[Casino] could not record the gift claim at {_path} -- {ex.Message}");
            return false;
        }
    }

    private Dictionary<string, Dictionary<string, GiftClaim>> Load(out bool readable)
    {
        readable = true;

        if (!_fileUtil.FileExists(_path))
        {
            // A fresh install, which is the ordinary case and not a failure.
            return [];
        }

        try
        {
            return _jsonUtil.Deserialize<Dictionary<string, Dictionary<string, GiftClaim>>>(
                _fileUtil.ReadFile(_path)) ?? [];
        }
        catch (Exception ex)
        {
            // A corrupt stats file can start fresh; this one cannot. An empty record
            // here means every profile already paid is owed another million, so the
            // gift is switched off until a person has looked at the file.
            readable = false;
            _logger.Error(
                $"[Casino] the gift record at {_path} will not parse, so the gift is withheld "
                + $"rather than risk paying it twice. Move or repair the file -- {ex.Message}");
            return [];
        }
    }
}
