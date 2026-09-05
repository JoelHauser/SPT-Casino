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

namespace Blackjack.Server;

/// <summary>
/// Moves currency in and out of the player's stash.
///
/// This deliberately does not use PaymentService. Both of its entry points derive
/// the currency from a trader -- GiveProfileMoney reads trader.Currency, and the
/// no-trader path in PayMoney is hardcoded to RUB -- so neither can settle a bet
/// denominated in dollars or euros. Walking the stacks directly is the only way
/// to support all three.
///
/// This is also the least proven code in the mod: every InventoryHelper call here
/// is one that has never run against a real profile. Hence the logging, and hence
/// the try/catch -- an exception escaping into the router would surface as an
/// opaque 500 with nothing to debug from.
/// </summary>
[Injectable]
public class Bank(
    InventoryHelper inventoryHelper,
    ItemHelper itemHelper,
    ProfileHelper profileHelper,
    MailSendService mailSendService,
    BlackjackLog log)
    : IBank
{
    /// <summary>
    /// How long a mailed payout waits to be collected. Long, because the message only
    /// exists when the stash was too full to take the winnings directly -- expiring it
    /// would destroy the very payout this is rescuing.
    /// </summary>
    private const long MailStorageSeconds = 90L * 24 * 60 * 60;

    /// <summary>
    /// How many of an item may sit in one stack, as the running server sees it.
    ///
    /// Read live, never assumed. The base database says roubles stack to 1,000,000
    /// and that bitcoin and Lega medals do not stack at all; BarterItemsStacks raises
    /// those to 20,000,000 and 20. Both are correct, on different servers, so the only
    /// safe source is the database in front of us.
    ///
    /// Clamped to at least one. A limit of zero -- which a careless item mod can
    /// produce -- would make the splitting loops take zero each pass and never
    /// terminate, hanging a server thread rather than failing.
    /// </summary>
    public int MaxStackSize(Wallet wallet) => MaxStackSize(wallet, int.MaxValue);

    private int MaxStackSize(Wallet wallet, int fallback)
    {
        var declared = itemHelper.GetItem(TplFor(wallet)).Value?.Properties?.StackMaxSize;

        if (declared is null)
        {
            // No limit published: treat the whole amount as one stack, which is what
            // this did before, rather than inventing a number.
            return Math.Max(1, fallback);
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

    public static MongoId TplFor(Wallet wallet) => WalletInfo.For(wallet).Tpl;

    /// <summary>
    /// Total of every stack of this currency the profile holds. Counts money in
    /// containers as well as loose in the stash, which matches what the player
    /// would consider their balance.
    /// </summary>
    public int GetBalance(MongoId sessionId, Wallet wallet)
    {
        var pmcData = profileHelper.GetPmcProfile(sessionId);
        if (pmcData is null)
        {
            log.Error($"GetBalance: no PMC profile for session '{sessionId}'.");
            return 0;
        }

        return StacksOf(pmcData, TplFor(wallet)).Sum(item => item.GetItemStackSize());
    }

    /// <summary>
    /// Takes the stake. Returns false without touching anything if the player is
    /// short -- the caller must not deal a hand it cannot collect on.
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

        var tpl = TplFor(wallet);
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
                // short with no hand to show for it. Say so explicitly.
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
            // every balance the client shows is now suspect.
            log.Error($"debit mismatch: {wallet} is {after:N0} but should be {before - amount:N0}.");
        }

        return remaining == 0;
    }

    /// <summary>Pays winnings back into the stash, respecting max stack size.</summary>
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

        var tpl = TplFor(wallet);
        var before = GetBalance(sessionId, wallet);

        // One oversized stack would be rejected by the client, so the payout is split
        // before it is handed over.
        var maxStack = MaxStackSize(wallet, amount);
        var remaining = amount;
        var stacksMade = 0;

        log.Detail($"credit {amount:N0} {wallet} (max stack {maxStack:N0}), balance {before:N0}");

        while (remaining > 0)
        {
            var size = (int)Math.Min(remaining, maxStack);
            var item = new Item
            {
                Id = new MongoId(),
                Template = tpl,
                Upd = new Upd { StackObjectsCount = size },
            };

            try
            {
                inventoryHelper.AddItemToStash(
                    sessionId,
                    new AddItemDirectRequest
                    {
                        ItemWithModsToAdd = [item],
                        FoundInRaid = false,
                        UseSortingTable = true,
                    },
                    pmcData,
                    output);
            }
            catch (Exception ex)
            {
                // Losing a payout is the worst outcome in the mod, so this is loud and
                // says exactly how much never made it.
                log.Error($"AddItemToStash threw paying {size:N0} {wallet}. {remaining:N0} unpaid.", ex);
                return;
            }

            remaining -= size;
            stacksMade++;
        }

        var after = GetBalance(sessionId, wallet);
        log.Detail($"credit done: {wallet} {before:N0} -> {after:N0} in {stacksMade} stack(s)");

        // AddItemToStash can decline to place an item without throwing -- a full stash
        // is the usual reason. Detecting that is not enough: the winnings would simply
        // be gone. Whatever failed to land is posted instead.
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
    /// Last resort for winnings the stash would not take. Mail holds the items until
    /// the player makes room, and SPT's own new-message notification tells them it is
    /// waiting, so nothing is lost and nothing needs a popup of our own.
    /// </summary>
    private void PayByMail(MongoId sessionId, Wallet wallet, int amount)
    {
        var tpl = TplFor(wallet);
        var maxStack = MaxStackSize(wallet, amount);
        var items = new List<Item>();
        var remaining = amount;

        while (remaining > 0)
        {
            var size = (int)Math.Min(remaining, maxStack);
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
                $"Your table winnings could not fit in your stash. {amount:N0} {WalletInfo.For(wallet).Label} attached.",
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
