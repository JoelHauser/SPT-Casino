using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Casino.Server.Tests;

/// <summary>
/// What <see cref="StashScope"/> does with a profile that is already damaged.
///
/// On 2026-09-22 two concurrent credits left a null in a player's
/// <c>Inventory.Items</c>. The index this builds used to throw on it, so every balance
/// read -- and so every pull, ping and refund -- failed for that player from then on.
/// </summary>
public class StashScopeTests
{
    private static readonly MongoId Stash = new("6a9b474574813708e8fc3d00");

    [Fact]
    public void ANullEntryInTheItemListDoesNotStopTheWalk()
    {
        var stack = new Item { Id = new MongoId(), Template = new MongoId(), ParentId = Stash.ToString() };
        var pmc = Profile([null!, stack]);

        Assert.True(StashScope.IsInStash(pmc, stack));
    }

    [Fact]
    public void ARepeatedIdDoesNotStopTheWalk()
    {
        var id = new MongoId();
        var box = new Item { Id = id, Template = new MongoId(), ParentId = Stash.ToString() };
        var twin = new Item { Id = id, Template = new MongoId(), ParentId = Stash.ToString() };
        var stack = new Item { Id = new MongoId(), Template = new MongoId(), ParentId = id.ToString() };

        Assert.True(StashScope.IsInStash(Profile([box, twin, stack]), stack));
    }

    [Fact]
    public void ANullItemIsNotInTheStash()
    {
        Assert.False(StashScope.IsInStash(Profile([]), null!));
    }

    private static PmcData Profile(List<Item> items) => new()
    {
        Inventory = new BotBaseInventory { Stash = Stash, Items = items },
    };
}
