using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Services.Commerce;

namespace Casino.Server;

/// <summary>
/// Puts the gift in the stash. Credit only -- it never takes anything.
///
/// A fifth copy of <c>Bank</c> was not wanted and is not what this is: the tables'
/// banks debit, settle, split three currencies and read balances, and none of that
/// applies to handing over a fixed number of roubles once. What it does keep is the
/// two lessons those four paid for, because they are about <c>InventoryHelper</c>
/// rather than about gambling:
///
/// - **AddItemToStash can decline an item without throwing.** A full stash swallows
///   the payout silently, so the balance is compared either side and the shortfall is
///   posted as mail rather than lost.
/// - **The response must come from EventOutputHolder.GetOutput**, never <c>new</c>.
///
/// One million roubles is four maximum stacks, so the splitting loop is not decoration
/// here -- a single oversized stack is rejected by the client.
/// </summary>
[Injectable]
public class GiftBank(
    InventoryHelper inventoryHelper,
    ItemHelper itemHelper,
    ProfileHelper profileHelper,
    MailSendService mailSendService,
    ICasinoLog log) : IGiftBank
{
    /// <summary>
    /// Ninety days. Long, because the message only exists when the stash was too full
    /// to take the gift, and expiring it would destroy the very payment it is
    /// rescuing. The same figure the tables use for a rescued payout.
    /// </summary>
    private const long MailStorageSeconds = 90L * 24 * 60 * 60;

    /// <summary>
    /// Pays the gift, and says honestly what happened to it.
    ///
    /// Returns <c>Paid: false</c> only when nothing moved at all -- that is the one
    /// case the caller may put the offer back for.
    /// </summary>
    public GiftPayment Pay(MongoId sessionId, int amount, ItemEventRouterResponse output)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);

        if (pmcData is null)
        {
            log.Error($"no PMC profile for session {sessionId} -- the gift was not paid.");
            return new GiftPayment(false, false);
        }

        var before = Balance(pmcData);
        var maxStack = MaxStackSize();
        var remaining = amount;

        while (remaining > 0)
        {
            var size = Math.Min(remaining, maxStack);

            try
            {
                inventoryHelper.AddItemToStash(
                    sessionId,
                    new AddItemDirectRequest
                    {
                        ItemWithModsToAdd =
                        [
                            new Item
                            {
                                Id = new MongoId(),
                                Template = GiftOffer.Tpl,
                                Upd = new Upd { StackObjectsCount = size },
                            },
                        ],
                        FoundInRaid = false,
                        UseSortingTable = true,
                    },
                    pmcData,
                    output);
            }
            catch (Exception ex)
            {
                log.Error(
                    $"AddItemToStash threw paying {size:N0} of the gift. "
                    + $"{remaining:N0} unpaid -- {ex.Message}");
                break;
            }

            remaining -= size;
        }

        // Whatever the loop believes, the stash is the authority: AddItemToStash can
        // decline an item without throwing, so this counts money rather than attempts.
        var landed = Balance(pmcData) - before;
        var owed = amount - landed;

        if (owed <= 0)
        {
            log.Info($"paid the {amount:N0} rouble gift to session {sessionId}.");
            return new GiftPayment(true, false);
        }

        log.Info(
            $"only {landed:N0} of the {amount:N0} rouble gift fit in the stash of session "
            + $"{sessionId}. Posting the remaining {owed:N0} -- a full stash would explain this.");

        var posted = PayByMail(sessionId, owed);

        // Nothing landed and nothing posted is the only true failure. Anything else is
        // the player's money, sitting either in the stash or in the message tab.
        return new GiftPayment(landed > 0 || posted, posted);
    }

    /// <summary>
    /// Total roubles in the stash, on the same terms the tables count them: loose or
    /// inside a container that is itself in the stash, never pockets, the secure
    /// container, the backpack or the rig. See <see cref="StashScope"/>.
    ///
    /// Only ever read as a difference either side of a payment, so what it excludes
    /// matters less here than it does to a table showing a balance. What it must not
    /// do is count a different set of items on the two reads, which is exactly what
    /// borrowing a looser scope for one of them would cause.
    /// </summary>
    private static int Balance(PmcData pmcData) =>
        (pmcData.Inventory?.Items ?? [])
            .Where(item => item.Template == GiftOffer.Tpl && StashScope.IsInStash(pmcData, item))
            .Sum(item => item.GetItemStackSize());

    /// <summary>
    /// Clamped to at least one, like every table's. A limit of zero -- which a
    /// careless item mod can produce -- makes the splitting loop take nothing each
    /// pass and never terminate, hanging a server thread rather than failing.
    /// </summary>
    private int MaxStackSize()
    {
        var declared = itemHelper.GetItem(GiftOffer.Tpl).Value?.Properties?.StackMaxSize;

        if (declared is null)
        {
            return int.MaxValue;
        }

        if (declared < 1)
        {
            log.Error(
                $"roubles report a maximum stack of {declared}, which cannot be honoured. "
                + "Treating it as 1 -- an item mod has set something impossible.");
            return 1;
        }

        return (int)declared;
    }

    /// <summary>
    /// Last resort for money the stash would not take. SPT's own notification tells
    /// the player it is waiting, so nothing is lost and the card only has to say where
    /// to look.
    /// </summary>
    private bool PayByMail(MongoId sessionId, int amount)
    {
        var maxStack = MaxStackSize();
        var items = new List<Item>();
        var remaining = amount;

        while (remaining > 0)
        {
            var size = Math.Min(remaining, maxStack);

            items.Add(new Item
            {
                Id = new MongoId(),
                Template = GiftOffer.Tpl,
                Upd = new Upd { StackObjectsCount = size },
            });

            remaining -= size;
        }

        try
        {
            mailSendService.SendSystemMessageToPlayer(
                sessionId,
                $"Your stash was too full to take the casino's apology. {amount:N0} roubles attached.",
                items,
                MailStorageSeconds,
                null);

            return true;
        }
        catch (Exception ex)
        {
            // Nothing left to fall back on, so this is the loudest line in the mod.
            log.Error(
                $"could not post {amount:N0} roubles of the gift to session {sessionId}. "
                + $"THE PLAYER HAS LOST IT -- {ex.Message}");
            return false;
        }
    }
}
