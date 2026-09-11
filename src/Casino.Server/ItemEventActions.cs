using System.Text.Json;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Utils.Json.Converters;

namespace Casino.Server;

/// <summary>
/// Tells SPT how to read the casino's own item-event bodies. **4.0 only, and not
/// optional.**
///
/// ## Why this file exists here and not on the 4.1 branch
///
/// On 4.1 a router declares `ItemRouteAction&lt;BlackjackDealAction&gt;(...)`, and the
/// type argument is how SPT knows what to deserialize the body into. 4.0 has no such
/// thing: every item-event body is read by `BaseInteractionRequestDataConverter`, which
/// switches on the action name over a hardcoded list of EFT's own actions and, for
/// anything it does not recognise, looks in a registry a mod has to have filled in
/// first.
///
/// **An unregistered action throws rather than degrading.** The converter ends in
///
///     throw new Exception("Unhandled action type " + action + ", ...")
///
/// so a missed registration is not a table that silently does nothing -- it is an
/// exception raised while deserializing the request, before any router is consulted.
/// Every action name in <see cref="BlackjackActions"/> and its siblings has to be
/// registered exactly once, at startup, or that table's every move fails.
///
/// ## Default serializer options are deliberate
///
/// The registry hands a handler the raw JSON and nothing else -- there is no
/// `JsonSerializerOptions` on the delegate -- so these deserialize with the default
/// ones. That is safe here and would not be for an arbitrary body: every casino action
/// carries only `string` and `int`, named in PascalCase exactly as the client sends
/// them, so none of SPT's own converters (MongoId, the enum and list shims) is needed
/// to read one.
/// </summary>
public static class ItemEventActions
{
    /// <summary>
    /// Registers one action name against the type its body deserializes to.
    ///
    /// Call once per action, from an <c>IOnLoad</c>. The underlying registry throws on
    /// a duplicate, which is the right behaviour -- two registrations for one name
    /// means two tables are fighting over it -- but it does mean this must not be
    /// called from anything the container may construct more than once.
    /// </summary>
    public static void Register<TAction>(string action)
        where TAction : BaseInteractionRequestData =>
        BaseInteractionRequestDataConverter.RegisterModDataHandler(
            action,
            json => JsonSerializer.Deserialize<TAction>(json));
}
