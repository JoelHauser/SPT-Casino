using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Casino.Server;

/// <summary>
/// Decides whether an inventory item is actually put away in the stash, as opposed
/// to worn or carried -- pockets, the secure container, backpack, rig, and anything
/// else hanging off <c>Inventory.Equipment</c> rather than <c>Inventory.Stash</c>.
///
/// Every table's Bank matches currency stacks by template alone, and a stack in the
/// player's pockets carries the same template as one sitting in the stash. Without
/// this check a debit would draw from whichever it found first -- which in practice
/// meant equipment before the stash, since `PmcData.Inventory.Items` lists items in
/// no particular order. A table has no business emptying gear the player is
/// carrying into a raid; it may only spend what has been put away.
/// </summary>
public static class StashScope
{
    /// <summary>
    /// Walks <paramref name="item"/>'s parent chain looking for the stash. True for
    /// a stack resting loose in it, or nested inside a container that is itself in
    /// the stash; false for one in equipment, or for a chain that runs out before
    /// reaching either root.
    /// </summary>
    public static bool IsInStash(PmcData pmcData, Item item)
    {
        var stash = pmcData.Inventory?.Stash;
        var items = pmcData.Inventory?.Items;

        if (stash is null || items is null)
        {
            return false;
        }

        var stashId = stash.Value.ToString();
        var byId = items.ToDictionary(i => i.Id.ToString());

        var current = item;
        var hops = 0;

        while (current.ParentId is not null)
        {
            if (current.ParentId == stashId)
            {
                return true;
            }

            // A cycle or a dangling reference stops here rather than spinning
            // forever -- a corrupt profile is not this method's problem to solve.
            if (++hops > byId.Count || !byId.TryGetValue(current.ParentId, out var parent))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }
}
