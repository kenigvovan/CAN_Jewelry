using Atlas.Api;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Hooks = global::canjewelry.src.integration;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios.Infrastructure;

/// <summary>
/// Ground for the socket, encrusting and extraction scenarios: sockets added the way the
/// jeweler's set adds them, cut gems as the bench makes them now, hook subscriptions and item
/// attribute rewrites that are all undone on Dispose.
/// </summary>
public abstract class SocketScenarioBase : JewelryScenarioBase, IDisposable
{
    protected const string SteelSocket = "canjewelry:cansocket-steel";
    protected const string IronSocket = "canjewelry:cansocket-iron";
    protected const string TinbronzeSocket = "canjewelry:cansocket-tinbronze";

    // A vanilla knife without a default socket rule; scenarios write their own rules onto it.
    protected const string BareKnifeCode = "game:knife-generic-copper";

    private readonly List<Action> socketCleanups = new();

    protected void OnCleanup(Action undo) => socketCleanups.Add(undo);

    /// <summary>A cut gem as the bench and the grinder leave it: cut, buffs and the ground flag.</summary>
    protected ItemStack CutGem(string quality = "exquisite", string gemType = GemType,
        string[]? names = null, float[]? values = null, string cut = CANJWConstants.CUTTING_ROUND, bool ground = true)
    {
        ItemStack gem = Stack($"canjewelry:gem-cut-{quality}-{gemType}");
        ITreeAttribute tree = new TreeAttribute();
        tree.SetString(CANJWConstants.CUTTING_TYPE, cut);
        tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(names ?? new[] { "walkspeed" });
        tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(values ?? new[] { 0.05f });
        tree.SetBool(CANJWConstants.GEM_FULL_PROCESSED, ground);
        gem.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
        return gem;
    }

    protected ItemStack DurabilityGem(float value, string quality = "exquisite") =>
        CutGem(quality, names: new[] { CANJWConstants.CANDURABILITY_STRING }, values: new[] { value });

    /// <summary>Adds sockets 0..count-1 through TryAddSocket, one socket item each.</summary>
    protected ItemStack WithSockets(ItemStack jewelry, int count, string socketCode = SteelSocket)
    {
        var slot = new DummySlot(jewelry);
        for (int i = 0; i < count; i++)
        {
            Assert.True(Encrustable.TryAddSocket(ScratchInventory(), slot, new DummySlot(Stack(socketCode)), i),
                $"socket {i} ({socketCode}) was refused by {jewelry.Collectible.Code}");
        }
        return slot.Itemstack;
    }

    protected ItemStack SocketedKnife(int sockets = 1) => WithSockets(Stack(JewelryCode), sockets);

    protected static ITreeAttribute? EncrustedTree(ItemStack? jewelry) =>
        jewelry?.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);

    protected static string[]? BuffNames(ITreeAttribute tree) =>
        (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;

    protected static float[]? BuffValues(ITreeAttribute tree) =>
        (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;

    /// <summary>
    /// Replaces an item's whole attribute set for this scenario. Built from JSON so nothing of
    /// the previous rules leaks in; the parsed socket tables are dropped both ways.
    /// </summary>
    protected Item WithItemAttributes(string code, string json)
    {
        Item item = World.Api.World.GetItem(new AssetLocation(code));
        Assert.True(item != null, "no item " + code);
        JsonObject previous = item!.Attributes;
        item.Attributes = JsonObject.FromJson(json);
        Encrustable.ClearVariantTiersCache();
        OnCleanup(() =>
        {
            item.Attributes = previous;
            Encrustable.ClearVariantTiersCache();
        });
        return item;
    }

    protected void WithDurability(string code, int durability)
    {
        Item item = World.Api.World.GetItem(new AssetLocation(code));
        int previous = item.Durability;
        item.Durability = durability;
        OnCleanup(() => item.Durability = previous);
    }

    protected void OnEncrust(Action<Hooks.EncrustEvent> handler)
    {
        Mod mod = Mod.Instance;
        mod.OnEncrustBuff += handler;
        OnCleanup(() => mod.OnEncrustBuff -= handler);
    }

    protected void OnExtract(Action<Hooks.ExtractEvent> handler)
    {
        Mod mod = Mod.Instance;
        mod.OnExtractGem += handler;
        OnCleanup(() => mod.OnExtractGem -= handler);
    }

    protected List<string> RecordXp()
    {
        var actions = new List<string>();
        Action<Hooks.CANXpEvent> handler = e => actions.Add(e.Action);
        Mod mod = Mod.Instance;
        mod.OnXpAwarded += handler;
        OnCleanup(() => mod.OnXpAwarded -= handler);
        return actions;
    }

    /// <summary>Everything in the player's hotbar and backpack.</summary>
    protected static List<ItemStack> CarriedStacks(ITestPlayer player)
    {
        var stacks = new List<ItemStack>();
        foreach (string inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            IInventory? inventory = player.Player.InventoryManager.GetOwnInventory(inv);
            if (inventory == null) continue;
            foreach (ItemSlot slot in inventory)
            {
                if (slot?.Itemstack != null) stacks.Add(slot.Itemstack);
            }
        }
        return stacks;
    }

    /// <summary>Takes every carried stack of this code away, so a scenario counts only its own.</summary>
    protected static void DropCarried(ITestPlayer player, string code)
    {
        foreach (string inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            IInventory? inventory = player.Player.InventoryManager.GetOwnInventory(inv);
            if (inventory == null) continue;
            foreach (ItemSlot slot in inventory)
            {
                if (slot?.Itemstack?.Collectible.Code.ToString() == code) slot.Itemstack = null;
            }
        }
    }

    public new void Dispose()
    {
        for (int i = socketCleanups.Count - 1; i >= 0; i--) socketCleanups[i]();
        socketCleanups.Clear();
        base.Dispose();
    }
}
