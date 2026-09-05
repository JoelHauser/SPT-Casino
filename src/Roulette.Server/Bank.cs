using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Roulette.Server;

/// <summary>
/// Reads what the player has.
///
/// **Reading only, for now.** Debit, credit and the shortfall-to-mail path are not
/// here yet, which is what makes this build safe to point at a real profile: there is
/// no code in the mod that can move currency. Poker's `Bank` is the thing to port when
/// settlement is written, and it carries three lessons worth arriving with:
///
/// - **`PaymentService` cannot settle a bet.** Both of its entry points derive the
///   currency from a trader, so neither can pay out dollars or euros. Walk the item
///   stacks directly, as this does.
/// - **`AddItemToStash` can decline an item without throwing.** A full stash silently
///   swallows a payout, so compare the balance either side of every move against what
///   was intended and post the shortfall as mail rather than losing it.
/// - **The response must come from `EventOutputHolder.GetOutput`.** A hand-built
///   `ItemEventRouterResponse` initialises nothing and throws after the items have
///   already gone.
/// </summary>
[Injectable]
public class Bank(ItemHelper itemHelper, ProfileHelper profileHelper, RouletteLog log) : IBank
{
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

    public int MaxStackSize(Wallet wallet)
    {
        var declared = itemHelper.GetItem(WalletInfo.For(wallet).Tpl).Value?.Properties?.StackMaxSize;

        if (declared is null)
        {
            return int.MaxValue;
        }

        // A limit of zero makes a splitting loop take zero each pass and hang a server
        // thread rather than fail, and a careless item mod can produce one.
        if (declared < 1)
        {
            log.Error(
                $"{wallet} reports a maximum stack of {declared}, which cannot be honoured. "
                + "Treating it as 1 -- an item mod has set something impossible.");
            return 1;
        }

        return (int)declared;
    }

    private static IEnumerable<Item> StacksOf(PmcData pmcData, MongoId tpl) =>
        pmcData.Inventory?.Items?.Where(item => item.Template == tpl) ?? [];
}
