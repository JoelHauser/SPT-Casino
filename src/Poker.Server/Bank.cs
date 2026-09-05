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

namespace Poker.Server;

/// <summary>
/// Reads what the player has.
///
/// Ported from Blackjack's Bank, which walks item stacks directly rather than going
/// through PaymentService -- both of that service's entry points derive the currency
/// from a trader, so neither can settle anything denominated in dollars or euros.
///
/// The debit and credit halves walk stacks directly too, and the shortfall-to-mail
/// path exists because AddItemToStash can decline an item without throwing -- a full
/// stash silently swallows a payout, and comparing the balance either side of every
/// move against what was intended is the only way to notice.
/// </summary>
[Injectable]
public class Bank(
    InventoryHelper inventoryHelper,
    ItemHelper itemHelper,
    ProfileHelper profileHelper,
    MailSendService mailSendService,
    PokerLog log) : IBank
{
    /// <summary>
    /// How long a mailed cash-out waits to be collected. Long, because the message
    /// only exists when the stash was too full to take the chips back -- expiring it
    /// would destroy the very payout this is rescuing.
    /// </summary>
    private const long MailStorageSeconds = 90L * 24 * 60 * 60;

    /// <summary>
    /// Total of every stack of this currency the profile holds. Counts money in
    /// containers as well as loose in the stash, which is what a player would call
    /// their balance.
    /// </summary>
    public int GetBalance(MongoId sessionId, Wallet wallet)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);

        if (pmcData is null)
        {
            log.Error($"GetBalance: no PMC profile for session '{sessionId}'.");
            return 0;
        }

        return StacksOf(pmcData, WalletInfo.For(wallet).Tpl).Sum(item => item.GetItemStackSize());
    }

    /// <summary>
    /// Clamped to at least one. A limit of zero -- which a careless item mod can
    /// produce -- would make a payout's splitting loop take zero each pass and never
    /// terminate, hanging a server thread rather than failing.
    /// </summary>
    public int MaxStackSize(Wallet wallet)
    {
        var declared = itemHelper.GetItem(WalletInfo.For(wallet).Tpl).Value?.Properties?.StackMaxSize;

        if (declared is null)
        {
            return int.MaxValue;
        }

        if (declared < 1)
        {
            log.Error(
                $"{wallet} reports a maximum stack of {declared}, which cannot be honoured. "
                + "Treating it as 1 -- an item mod has set something impossible.");
            return 1;
        }

        return (int)declared;
    }

    /// <summary>
    /// Takes the buy-in. Returns false without touching anything if the player is
    /// short -- the caller must not seat somebody it cannot collect from.
    /// </summary>
    public bool TryDebit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);

        if (pmcData is null)
        {
            log.Error($"TryDebit: no PMC profile for session '{sessionId}'.");
            return false;
        }

        if (amount <= 0)
        {
            return false;
        }

        var tpl = WalletInfo.For(wallet).Tpl;
        var before = GetBalance(sessionId, wallet);

        if (before < amount)
        {
            log.Detail($"debit refused: wanted {amount:N0} {wallet}, player has {before:N0}.");
            return false;
        }

        var remaining = amount;

        // Smallest stacks first, so the stash ends up with fewer loose piles rather
        // than more.
        var stacks = StacksOf(pmcData, tpl).OrderBy(item => item.GetItemStackSize()).ToList();
        log.Detail($"debit {amount:N0} {wallet} across {stacks.Count} stack(s), balance {before:N0}");

        foreach (var stack in stacks)
        {
            if (remaining <= 0)
            {
                break;
            }

            var take = Math.Min(remaining, stack.GetItemStackSize());

            try
            {
                inventoryHelper.RemoveItemByCount(pmcData, stack.Id, take, sessionId, output);
            }
            catch (Exception ex)
            {
                // Partial removal may already have happened, so the player could be
                // short with nothing to show for it. Say exactly how much.
                log.Error(
                    $"RemoveItemByCount threw taking {take:N0} from stack {stack.Id}. "
                    + $"{amount - remaining:N0} of {amount:N0} {wallet} may already be gone.",
                    ex);
                return false;
            }

            remaining -= take;
        }

        var after = GetBalance(sessionId, wallet);
        log.Detail($"debit done: {wallet} {before:N0} -> {after:N0} (expected {before - amount:N0})");

        if (after != before - amount)
        {
            // The arithmetic disagreeing with the stash is the most valuable signal
            // there is: InventoryHelper did something other than what was asked, and
            // every balance shown from here on is suspect.
            log.Error($"debit mismatch: {wallet} is {after:N0} but should be {before - amount:N0}.");
        }

        return remaining == 0;
    }

    /// <summary>Pays a cash-out back into the stash, respecting the stack limit.</summary>
    public void Credit(MongoId sessionId, Wallet wallet, int amount, ItemEventRouterResponse output)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);

        if (pmcData is null)
        {
            log.Error($"Credit: no PMC profile for session '{sessionId}' -- {amount:N0} {wallet} not paid.");
            return;
        }

        if (amount <= 0)
        {
            return;
        }

        var tpl = WalletInfo.For(wallet).Tpl;
        var before = GetBalance(sessionId, wallet);

        // One oversized stack would be rejected by the client, so the payout is split
        // before it is handed over.
        var maxStack = MaxStackSize(wallet);
        var remaining = amount;
        var stacksMade = 0;

        log.Detail($"credit {amount:N0} {wallet} (max stack {maxStack:N0}), balance {before:N0}");

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
                                Template = tpl,
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
                // Losing a payout is the worst outcome available, so this is loud and
                // says exactly how much never made it.
                log.Error($"AddItemToStash threw paying {size:N0} {wallet}. {remaining:N0} unpaid.", ex);
                return;
            }

            remaining -= size;
            stacksMade++;
        }

        var after = GetBalance(sessionId, wallet);
        log.Detail($"credit done: {wallet} {before:N0} -> {after:N0} in {stacksMade} stack(s)");

        // AddItemToStash can decline to place an item **without throwing** -- a full
        // stash is the usual reason. Detecting that is not enough on its own: the
        // winnings would simply be gone. Whatever failed to land is posted instead.
        var shortfall = before + amount - after;

        if (shortfall > 0)
        {
            log.Error(
                $"credit shortfall: {wallet} is {after:N0} but should be {before + amount:N0}. "
                + $"Posting the missing {shortfall:N0} instead -- a full stash would explain this.");

            PayByMail(sessionId, wallet, shortfall);
        }
    }

    /// <summary>
    /// Last resort for a cash-out the stash would not take. Mail holds the items until
    /// the player makes room and SPT's own notification tells them it is waiting, so
    /// nothing is lost and nothing needs a popup of our own.
    /// </summary>
    private void PayByMail(MongoId sessionId, Wallet wallet, int amount)
    {
        var tpl = WalletInfo.For(wallet).Tpl;
        var maxStack = MaxStackSize(wallet);
        var items = new List<Item>();
        var remaining = amount;

        while (remaining > 0)
        {
            var size = Math.Min(remaining, maxStack);

            items.Add(new Item
            {
                Id = new MongoId(),
                Template = tpl,
                Upd = new Upd { StackObjectsCount = size },
            });

            remaining -= size;
        }

        try
        {
            mailSendService.SendSystemMessageToPlayer(
                sessionId,
                $"Your chips would not fit in your stash. {amount:N0} {WalletInfo.For(wallet).Label} attached.",
                items,
                MailStorageSeconds,
                null);

            log.Info($"posted {amount:N0} {wallet} to the player -- collect it from messages.");
        }
        catch (Exception ex)
        {
            // Nothing left to fall back on, so this is the loudest line in the mod.
            log.Error($"could not post {amount:N0} {wallet}. THE PLAYER HAS LOST THIS PAYOUT.", ex);
        }
    }

    private static IEnumerable<Item> StacksOf(PmcData pmcData, MongoId tpl) =>
        pmcData.Inventory?.Items?.Where(item => item.Template == tpl) ?? [];
}
