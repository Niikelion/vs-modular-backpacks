using System.Linq;
using Vintagestory.API.Common;

namespace ImmersiveBackpacks;

/// <summary>
/// Adds <see cref="BackpackHandbookBehavior"/> to every modular backpack so its handbook page gains the intro
/// and clickable addon models. Client-only (the handbook is a client GUI); runs at asset-finalize, before the
/// handbook is first opened. The addon list itself is built lazily by the behavior, so it just resets that
/// cache here in case assets were reloaded.
/// </summary>
public class BackpackHandbookModSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    public override void AssetsFinalize(ICoreAPI api)
    {
        BackpackHandbookBehavior.InvalidateCache();

        foreach (var coll in api.World.Collectibles)
        {
            // Modular backpack hosts are the only collectibles carrying attachmentPoints.
            if (coll.Attributes?["immersiveBackpack"]["attachmentPoints"].Exists != true) continue;
            if (coll.HasBehavior<BackpackHandbookBehavior>(false)) continue;

            var behavior = new BackpackHandbookBehavior(coll);
            behavior.OnLoaded(api);
            var behaviors = coll.CollectibleBehaviors.ToList();
            behaviors.Add(behavior);
            coll.CollectibleBehaviors = behaviors.ToArray();
        }
    }
}
