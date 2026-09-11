using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace Casino.Server;

/// <summary>
/// The casino's one line at startup.
///
/// It used to be three, one per table, because it used to be three mods. It is one
/// mod, it installs into one folder and it registers under one GUID, so it says so
/// once. Three blocks introducing three tables was a mod author's view of the thing
/// printed at somebody who has not opened it yet.
///
/// The tables still have their own `Startup`, and each is silent unless its verbose
/// switch is on -- see any of them. Turn one on and its block comes back, underneath
/// this.
///
/// Ordered ahead of them on purpose: `PostSptModLoader` against their
/// `PostSptModLoader + 1`, so the headline is above the detail rather than buried in
/// the middle of it. (4.1 spells the same last-of-all slot `PostLoad`.)
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostSptModLoader)]
public class Startup : IOnLoad
{
    public Task OnLoad()
    {
        // On 4.0 an item-event body is deserialized by a converter that throws on an
        // action it has not been told about. Each table registers its own; this is the
        // casino's one.
        ItemEventActions.Register<CasinoSyncAction>(CasinoActions.Sync);

        var metadata = new ModMetadata();

        Banner.Rainbow($"[Casino] v{metadata.Version}");

        return Task.CompletedTask;
    }
}
