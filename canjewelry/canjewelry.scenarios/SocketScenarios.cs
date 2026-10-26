using System.Reflection;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Mod = global::canjewelry.src.canjewelry;
using Registry = global::canjewelry.src.api.CANJewelryRegistry;

namespace canjewelry.scenarios;

/// <summary>
/// Adding sockets: which socket items fit which slot of which piece, what a socket records,
/// and how the number and tiers of sockets are read off an item.
/// </summary>
public class SocketScenarios : SocketScenarioBase
{
    // The bare knife with two sockets: tier 1 then tier 2.
    private const string LowTierRule = "{ \"cansocketstiers\": [1, 2], \"canhavesocketsnumber\": 2 }";

    [AtlasScenario]
    public async Task FirstSocket_Should_CreateTheEncrustedTree()
    {
        var jewelry = new DummySlot(Stack(JewelryCode));
        var socket = new DummySlot(Stack(SteelSocket));
        socket.Itemstack.StackSize = 2;

        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, socket, 0));

        ITreeAttribute tree = EncrustedTree(jewelry.Itemstack)!;
        Assert.Equal(1, tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        ITreeAttribute slot = SocketOf(jewelry.Itemstack, 0);
        Assert.Equal(3, slot.GetInt(CANJWConstants.ADDED_SOCKET_TYPE));
        Assert.Equal(SteelSocket, slot.GetString(CANJWConstants.SOCKET_ITEM_CODE));
        Assert.Equal(0, slot.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE));
        Assert.Equal("", slot.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        Assert.Equal(1, socket.StackSize);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task NextSockets_Should_CountUp_InAnyOrder()
    {
        var jewelry = new DummySlot(Stack(JewelryCode));
        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 2));
        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(IronSocket)), 0));

        ITreeAttribute tree = EncrustedTree(jewelry.Itemstack)!;
        Assert.Equal(2, tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        Assert.Null(tree.GetTreeAttribute("slot1"));
        Assert.Equal(IronSocket, SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.SOCKET_ITEM_CODE));
        Assert.Equal(2, SocketOf(jewelry.Itemstack, 0).GetInt(CANJWConstants.ADDED_SOCKET_TYPE));
        Assert.Equal(SteelSocket, SocketOf(jewelry.Itemstack, 2).GetString(CANJWConstants.SOCKET_ITEM_CODE));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task OccupiedSocket_Should_RefuseSecondSocket()
    {
        var jewelry = new DummySlot(SocketedKnife(1));
        var socket = new DummySlot(Stack(SteelSocket));

        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), jewelry, socket, 0));
        Assert.False(socket.Empty, "a refused socket must stay where it was");
        Assert.Equal(1, EncrustedTree(jewelry.Itemstack)!.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task FullPiece_Should_RefuseAnotherSocket()
    {
        var jewelry = new DummySlot(SocketedKnife(3));

        foreach (int index in new[] { 0, 3 })
        {
            Assert.False(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), index));
        }
        Assert.Equal(3, EncrustedTree(jewelry.Itemstack)!.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        await World.Ticks(1);
    }

    // What a hand written packet can name: past the end, negative, or no slot at all.
    [AtlasScenario]
    public async Task BadSocketIndexOrSlot_Should_BeRefused()
    {
        foreach (int index in new[] { -1, 3, 10 })
        {
            var jewelry = new DummySlot(Stack(JewelryCode));
            Assert.False(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), index),
                $"socket index {index} was accepted");
            Assert.Null(EncrustedTree(jewelry.Itemstack));
        }

        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), new DummySlot(Stack(JewelryCode)), null!, 0));
        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), new DummySlot(SocketedKnife(1)), null!, 1));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task NonSocketItem_Should_BeRefused()
    {
        var gem = new DummySlot(CutGem());
        var empty = new DummySlot(null);

        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), new DummySlot(Stack(JewelryCode)), gem, 0));
        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), new DummySlot(Stack(JewelryCode)), empty, 0));
        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), new DummySlot(SocketedKnife(1)), gem, 1));
        Assert.False(gem.Empty);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ItemWithoutSockets_Should_RefuseSocket()
    {
        var flint = new DummySlot(Stack("game:flint"));
        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), flint, new DummySlot(Stack(SteelSocket)), 0));
        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), new DummySlot(null), new DummySlot(Stack(SteelSocket)), 0));
        await World.Ticks(1);
    }

    // A socket item may go into a socket of its own tier or higher, never lower, on the first
    // socket (no tree yet) as on the later ones.
    [AtlasScenario]
    public async Task SocketTier_Should_NotExceedSlotTier()
    {
        WithItemAttributes(BareKnifeCode, LowTierRule);
        var jewelry = new DummySlot(Stack(BareKnifeCode));

        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 1),
            "a tier 3 socket went into a tier 2 slot");
        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(IronSocket)), 0),
            "a tier 2 socket went into a tier 1 slot");
        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(IronSocket)), 1));

        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(IronSocket)), 0),
            "a tier 2 socket went into a tier 1 slot of a piece that has a socket already");
        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(TinbronzeSocket)), 0));

        Assert.Equal(1, SocketOf(jewelry.Itemstack, 0).GetInt(CANJWConstants.ADDED_SOCKET_TYPE));
        Assert.Equal(2, SocketOf(jewelry.Itemstack, 1).GetInt(CANJWConstants.ADDED_SOCKET_TYPE));
        Assert.Equal(2, EncrustedTree(jewelry.Itemstack)!.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task LowerTierSocket_Should_FitHigherTierSlot()
    {
        var jewelry = new DummySlot(Stack(JewelryCode));
        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(TinbronzeSocket)), 0));
        Assert.Equal(1, SocketOf(jewelry.Itemstack, 0).GetInt(CANJWConstants.ADDED_SOCKET_TYPE));
        await World.Ticks(1);
    }

    // Socket items get their level from config.LevelOfSocketByType at startup.
    [AtlasScenario]
    public async Task SocketItems_Should_CarryTheirConfiguredLevel()
    {
        Assert.NotEmpty(Mod.config.LevelOfSocketByType);
        foreach (var pair in Mod.config.LevelOfSocketByType)
        {
            Item item = World.Api.World.GetItem(new AssetLocation(pair.Key));
            if (item == null) continue;
            Assert.Equal(pair.Value, item.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt(-1));
        }
        Assert.Equal(3, Stack(SteelSocket).Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt());
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task SocketCount_Should_ComeFromTheConfigRule()
    {
        ItemStack knife = Stack(JewelryCode);
        Assert.Equal(3, Encrustable.GetMaxAmountSockets(knife));
        Assert.Equal(new[] { 3, 3, 3 }, Encrustable.GetSocketsTiers(knife));
        Assert.True(knife.Collectible.HasBehavior<Encrustable>(), "the config rule did not attach the behavior");

        ItemStack flint = Stack("game:flint");
        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(flint));
        Assert.Empty(Encrustable.GetSocketsTiers(flint));
        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(null!));
        Assert.Empty(Encrustable.GetSocketsTiers(null!));
        await World.Ticks(1);
    }

    // Items from other mods may declare only the tier list; its length is the count.
    [AtlasScenario]
    public async Task TiersWithoutCount_Should_GiveTheCount()
    {
        WithItemAttributes(BareKnifeCode, "{ \"cansocketstiers\": [2, 1] }");
        ItemStack knife = Stack(BareKnifeCode);

        Assert.Equal(2, Encrustable.GetMaxAmountSockets(knife));
        Assert.Equal(new[] { 2, 1 }, Encrustable.GetSocketsTiers(knife));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ZeroCount_Should_MeanNoSockets()
    {
        WithItemAttributes(BareKnifeCode, "{ \"cansocketstiers\": [3], \"canhavesocketsnumber\": 0 }");
        ItemStack knife = Stack(BareKnifeCode);

        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(knife));
        Assert.Empty(Encrustable.GetSocketsTiers(knife));
        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), new DummySlot(knife), new DummySlot(Stack(SteelSocket)), 0));
        await World.Ticks(1);
    }

    // Custom variant rule keyed on a stack attribute.
    [AtlasScenario]
    public async Task VariantRule_Should_PickTiersByStackAttribute()
    {
        WithItemAttributes(BareKnifeCode,
            "{ \"cancustomvariants\": { \"gold\": [3, 3], \"silver\": [2] }, \"cancustomvariantscomparekey\": \"carcassus\" }");

        ItemStack gold = Stack(BareKnifeCode);
        gold.Attributes.SetString("carcassus", "gold");
        ItemStack silver = Stack(BareKnifeCode);
        silver.Attributes.SetString("carcassus", "silver");

        Assert.Equal(2, Encrustable.GetMaxAmountSockets(gold));
        Assert.Equal(new[] { 3, 3 }, Encrustable.GetSocketsTiers(gold));
        Assert.Equal(1, Encrustable.GetMaxAmountSockets(silver));
        Assert.Equal(new[] { 2 }, Encrustable.GetSocketsTiers(silver));
        await World.Ticks(1);
    }

    // Without the stack attribute the item's own code variant is used (cancoronet-gold style).
    [AtlasScenario]
    public async Task VariantRule_Should_FallBackToCodeVariant()
    {
        WithItemAttributes(BareKnifeCode,
            "{ \"cancustomvariants\": { \"copper\": [1, 1, 1, 1] }, \"cancustomvariantscomparekey\": \"material\" }");
        ItemStack knife = Stack(BareKnifeCode);

        Assert.Equal("copper", Encrustable.ResolveVariantValue(knife, "material"));
        Assert.Equal(4, Encrustable.GetMaxAmountSockets(knife));

        knife.Attributes.SetString("material", "gold");
        Assert.Equal("gold", Encrustable.ResolveVariantValue(knife, "material"));
        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(knife));
        await World.Ticks(1);
    }

    // "*" answers for a value the rule does not list and for a stack without the key at all.
    [AtlasScenario]
    public async Task VariantRule_Should_UseStarForUnknownValues()
    {
        WithItemAttributes(BareKnifeCode,
            "{ \"cancustomvariants\": { \"gold\": [3, 3], \"*\": [1] }, \"cancustomvariantscomparekey\": \"carcassus\" }");

        ItemStack unknown = Stack(BareKnifeCode);
        unknown.Attributes.SetString("carcassus", "tin");
        ItemStack none = Stack(BareKnifeCode);

        Assert.Equal(new[] { 1 }, Encrustable.GetSocketsTiers(unknown));
        Assert.Equal(1, Encrustable.GetMaxAmountSockets(none));
        Assert.Equal("*", Encrustable.VariantTiersFallbackKey);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task VariantRule_WithoutMatch_Should_GiveNoSockets()
    {
        WithItemAttributes(BareKnifeCode,
            "{ \"cancustomvariants\": { \"gold\": [3] }, \"cancustomvariantscomparekey\": \"carcassus\", \"canhavesocketsnumber\": 5, \"cansocketstiers\": [3,3,3,3,3] }");
        ItemStack knife = Stack(BareKnifeCode);

        // The variant rule wins over a plain count on the same item, even when it has no answer.
        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(knife));
        Assert.Empty(Encrustable.GetSocketsTiers(knife));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task VariantRule_WithoutCompareKey_Should_GiveNoSockets()
    {
        WithItemAttributes(BareKnifeCode, "{ \"cancustomvariants\": { \"*\": [3] } }");
        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(Stack(BareKnifeCode)));
        await World.Ticks(1);
    }

    // A rule that does not parse costs the item its sockets, not a crash.
    [AtlasScenario]
    public async Task MalformedVariantRule_Should_GiveNoSockets()
    {
        WithItemAttributes(BareKnifeCode,
            "{ \"cancustomvariants\": \"not a table\", \"cancustomvariantscomparekey\": \"material\" }");
        ItemStack knife = Stack(BareKnifeCode);

        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(knife));
        Assert.Empty(Encrustable.GetSocketsTiers(knife));
        await World.Ticks(1);
    }

    // The parsed table is cached per item; ClearVariantTiersCache makes a rewrite visible.
    [AtlasScenario]
    public async Task VariantCache_Should_BeDroppedOnClear()
    {
        Item item = WithItemAttributes(BareKnifeCode,
            "{ \"cancustomvariants\": { \"*\": [3] }, \"cancustomvariantscomparekey\": \"material\" }");
        Assert.Equal(1, Encrustable.GetMaxAmountSockets(Stack(BareKnifeCode)));

        item.Attributes = JsonObject.FromJson("{ \"cancustomvariants\": { \"*\": [3, 3] }, \"cancustomvariantscomparekey\": \"material\" }");
        Assert.Equal(1, Encrustable.GetMaxAmountSockets(Stack(BareKnifeCode)));

        Encrustable.ClearVariantTiersCache();
        Assert.Equal(2, Encrustable.GetMaxAmountSockets(Stack(BareKnifeCode)));
        await World.Ticks(1);
    }

    // A variant rule's tiers gate TryAddSocket like a plain rule does.
    [AtlasScenario]
    public async Task VariantRule_Should_GateSocketTiers()
    {
        WithItemAttributes(BareKnifeCode,
            "{ \"cancustomvariants\": { \"copper\": [1, 3] }, \"cancustomvariantscomparekey\": \"material\" }");
        var jewelry = new DummySlot(Stack(BareKnifeCode));

        Assert.False(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 0));
        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 1));
        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(TinbronzeSocket)), 0));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task AddSocket_Should_FireXpEvent()
    {
        List<string> xp = RecordXp();
        var jewelry = new DummySlot(Stack(JewelryCode));

        Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 0);
        Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 1);
        Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 1);

        Assert.Equal(new[] { "addSocket", "addSocket" }, xp);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task AddSocket_Should_UnlockTheInventory()
    {
        InventoryBase inventory = ScratchInventory();
        Encrustable.TryAddSocket(inventory, new DummySlot(Stack(JewelryCode)), new DummySlot(Stack(SteelSocket)), 0);
        Assert.False(inventory.TakeLocked);
        Encrustable.TryAddSocket(inventory, new DummySlot(Stack(JewelryCode)), new DummySlot(Stack(SteelSocket)), 7);
        Assert.False(inventory.TakeLocked);
        await World.Ticks(1);
    }

    // Registry rules land on items when the rules are applied; config rules are applied later
    // and win over them.
    [AtlasScenario]
    public async Task RegisteredSockets_Should_ApplyUnderConfigRules()
    {
        Item copper = World.Api.World.GetItem(new AssetLocation(BareKnifeCode));
        Assert.True(Encrustable.GetMaxAmountSockets(new ItemStack(copper)) < 1, "the bare knife already has sockets");
        KeepItem(copper);
        Unregister("extraSocketCounts", BareKnifeCode);
        Unregister("extraSocketCounts", JewelryCode);

        Registry.RegisterDefaultSockets(BareKnifeCode, 1, 2);
        Registry.RegisterDefaultSockets(JewelryCode, 1);
        Mod.Instance.AddBehaviorAndSocketNumber(World.Api);

        Assert.Equal(new[] { 1, 2 }, Encrustable.GetSocketsTiers(new ItemStack(copper)));
        Assert.True(copper.HasBehavior<Encrustable>());
        Assert.Equal(new[] { 3, 3, 3 }, Encrustable.GetSocketsTiers(Stack(JewelryCode)));
        await World.Ticks(1);
    }

    // Re-registering the same wildcard replaces the earlier entry.
    [AtlasScenario]
    public async Task RegisteredSockets_Should_ReplaceOnReRegister()
    {
        Item copper = World.Api.World.GetItem(new AssetLocation(BareKnifeCode));
        KeepItem(copper);
        Unregister("extraSocketCounts", BareKnifeCode);

        Registry.RegisterDefaultSockets(BareKnifeCode, 3, 3, 3);
        Registry.RegisterDefaultSockets(BareKnifeCode, 2);
        Mod.Instance.AddBehaviorAndSocketNumber(World.Api);

        Assert.Equal(new[] { 2 }, Encrustable.GetSocketsTiers(new ItemStack(copper)));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task RegisteredVariantSockets_Should_ApplyByVariant()
    {
        Item copper = World.Api.World.GetItem(new AssetLocation(BareKnifeCode));
        KeepItem(copper);
        Unregister("extraVariantSockets", BareKnifeCode);

        Registry.RegisterDefaultVariantSockets(BareKnifeCode, "material",
            new Dictionary<string, int[]> { ["copper"] = new[] { 2, 2 }, ["*"] = new[] { 1 } });
        Mod.Instance.AddBehaviorAndSocketNumber(World.Api);

        ItemStack knife = new ItemStack(copper);
        Assert.Equal(new[] { 2, 2 }, Encrustable.GetSocketsTiers(knife));
        knife.Attributes.SetString("material", "tin");
        Assert.Equal(new[] { 1 }, Encrustable.GetSocketsTiers(knife));
        Assert.True(copper.HasBehavior<Encrustable>());
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task Registry_Should_IgnoreEmptyInput()
    {
        Unregister("extraSocketCounts", BareKnifeCode);
        Unregister("extraVariantSockets", BareKnifeCode);

        Registry.RegisterDefaultSockets("", 3);
        Registry.RegisterDefaultSockets(BareKnifeCode, null!);
        Registry.RegisterDefaultVariantSockets(BareKnifeCode, "", new Dictionary<string, int[]> { ["*"] = new[] { 1 } });
        Registry.RegisterDefaultVariantSockets(BareKnifeCode, "material", new Dictionary<string, int[]>());

        Assert.False(RegistryTable("extraSocketCounts").Contains(""));
        Assert.False(RegistryTable("extraSocketCounts").Contains(BareKnifeCode));
        Assert.False(RegistryTable("extraVariantSockets").Contains(BareKnifeCode));
        await World.Ticks(1);
    }

    /// <summary>Puts the item's attributes and behaviors back after the rules are re-applied.</summary>
    private void KeepItem(Item item)
    {
        // A copy: applying the rules writes into the existing attribute token.
        JsonObject attributes = item.Attributes.Clone();
        CollectibleBehavior[] behaviors = item.CollectibleBehaviors;
        OnCleanup(() =>
        {
            item.Attributes = attributes;
            item.CollectibleBehaviors = behaviors;
            Encrustable.ClearVariantTiersCache();
        });
    }

    /// <summary>Removes a registry entry now and again after the scenario: the registry has no
    /// public way out and outlives the server.</summary>
    private void Unregister(string field, string key)
    {
        System.Collections.IDictionary table = RegistryTable(field);
        table.Remove(key);
        OnCleanup(() => table.Remove(key));
    }

    private static System.Collections.IDictionary RegistryTable(string field) =>
        (System.Collections.IDictionary)(typeof(Registry).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("CANJewelryRegistry has no field " + field)).GetValue(null)!;
}
