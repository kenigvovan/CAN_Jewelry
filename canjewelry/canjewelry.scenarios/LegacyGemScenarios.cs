using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.utils;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;

namespace canjewelry.scenarios;

/// <summary>
/// Gems and sockets written by older versions keep working: they go into sockets, come back out
/// as the same gem, and drop from a piece that breaks.
/// </summary>
public class LegacyGemScenarios : JewelryScenarioBase
{
    [AtlasScenario]
    public async Task LegacyGem_Should_GoIntoLegacySocket()
    {
        ITestPlayer player = await Join();
        var jewelry = new DummySlot(LegacySocketedJewelry());
        var gem = new DummySlot(LegacyCutGem());

        bool encrusted = Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, gem, 0, player.Player);

        Assert.True(encrusted, "the old gem was refused by the old socket");
        Assert.True(gem.Empty, "the gem was not taken out of its slot");
        ITreeAttribute socket = SocketOf(jewelry.Itemstack, 0);
        Assert.Equal(GemType, socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        Assert.Equal(1, socket.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE));
        Assert.Equal(new[] { "walkspeed" }, (socket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value);
    }

    [AtlasScenario]
    public async Task EveryGemSize_Should_FitTierThreeSocket()
    {
        ITestPlayer player = await Join();
        foreach (string quality in new[] { "normal", "flawless", "exquisite" })
        {
            var jewelry = new DummySlot(LegacySocketedJewelry());
            var gem = new DummySlot(LegacyCutGem(quality));

            Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, gem, 0, player.Player),
                $"a {quality} gem did not fit a tier 3 socket");
            Assert.Equal(CANGemSize.CutSizeOf(quality), SocketOf(jewelry.Itemstack, 0).GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE));
        }
    }

    [AtlasScenario]
    public async Task LargerGem_Should_NotFitLowerTierSocket()
    {
        ITestPlayer player = await Join();
        var jewelry = new DummySlot(LegacySocketedJewelry(tier: 1));
        var gem = new DummySlot(LegacyCutGem("exquisite"));

        Assert.False(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, gem, 0, player.Player));
        Assert.False(gem.Empty, "a refused gem must stay where it was");
    }

    // Encrust then extract gives back the gem that went in: item, cut and buffs.
    [AtlasScenario]
    public async Task ExtractedGem_Should_BeTheGemThatWentIn()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 1f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);

        var jewelry = new DummySlot(LegacySocketedJewelry());
        ItemStack original = LegacyCutGem("flawless", "walkspeed", 0.04f);
        var gem = new DummySlot(original.Clone());
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, gem, 0, player.Player));

        var outSlot = new DummySlot(null);
        Assert.True(Encrustable.TryExtractGem(ScratchInventory(), jewelry, outSlot, 0, player.Player));

        ItemStack back = outSlot.Itemstack;
        Assert.NotNull(back);
        Assert.Equal(original.Collectible.Code, back.Collectible.Code);
        ITreeAttribute tree = back.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal(CANJWConstants.CUTTING_ROUND, tree.GetString(CANJWConstants.CUTTING_TYPE));
        Assert.Equal(new[] { 0.04f }, (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value);
        Assert.True(back.Attributes.GetBool("wasExtracted"));
        Assert.Equal("", SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
    }

    // A gem that went through extraction once can be set in again.
    [AtlasScenario]
    public async Task ExtractedGem_Should_GoBackIn()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 1f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);

        var jewelry = new DummySlot(LegacySocketedJewelry());
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem()), 0, player.Player));
        var outSlot = new DummySlot(null);
        Assert.True(Encrustable.TryExtractGem(ScratchInventory(), jewelry, outSlot, 0, player.Player));

        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, outSlot, 0, player.Player),
            "an extracted gem was refused");
    }

    // A socket filled by an older version, read straight from its attributes.
    [AtlasScenario]
    public async Task GemInLegacySocket_Should_ComeOut()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 1f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);

        ItemStack knife = LegacySocketedJewelry();
        ITreeAttribute socket = SocketOf(knife, 0);
        socket.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, GemType);
        socket.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, 3);
        socket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { "walkspeed" });
        socket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(new[] { 0.07f });

        var outSlot = new DummySlot(null);
        Assert.True(Encrustable.TryExtractGem(ScratchInventory(), new DummySlot(knife), outSlot, 0, player.Player));

        Assert.Equal($"canjewelry:gem-cut-exquisite-{GemType}", outSlot.Itemstack?.Collectible.Code.ToString());
        ITreeAttribute tree = outSlot.Itemstack!.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.True(tree.GetBool(CANJWConstants.GEM_FULL_PROCESSED), "a legacy socket's gem counts as fully ground");
    }

    // The piece breaks in the player's hand and, at a drop chance of 1, every gem lands nearby.
    [AtlasScenario]
    public async Task BrokenJewelry_Should_DropItsGems()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.chance_gem_drop_on_item_broken, (c, v) => c.chance_gem_drop_on_item_broken = v, 1f);

        var jewelry = new DummySlot(LegacySocketedJewelry(sockets: 2));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem("normal")), 0, player.Player));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem("flawless")), 1, player.Player));

        ItemSlot hand = player.Player.InventoryManager.ActiveHotbarSlot;
        hand.Itemstack = jewelry.Itemstack;
        int durability = hand.Itemstack.Collectible.GetRemainingDurability(hand.Itemstack);
        hand.Itemstack.Collectible.DamageItem(World.Api.World, player.Entity, hand, durability + 1);
        Assert.True(hand.Empty, "the knife did not break");

        await World.Ticks(2);
        var dropped = DroppedOrPickedUpGems(player);

        Assert.Equal(2, dropped.Count);
        Assert.Contains(dropped, s => s.Collectible.Code.Path == $"gem-cut-normal-{GemType}");
        Assert.Contains(dropped, s => s.Collectible.Code.Path == $"gem-cut-flawless-{GemType}");
        Assert.All(dropped, s => Assert.NotNull(s.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)?[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES]));
    }

    // An empty socket between two gems used to end the drop loop; a dropped gem carries its own
    // tree, not the piece's, and the extraction taint.
    [AtlasScenario]
    public async Task BrokenJewelry_Should_DropGemsPastAnEmptySocket_AsExtracted()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.chance_gem_drop_on_item_broken, (c, v) => c.chance_gem_drop_on_item_broken = v, 1f);

        var jewelry = new DummySlot(LegacySocketedJewelry(sockets: 3));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem("normal")), 0, player.Player));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem("flawless")), 2, player.Player));

        ItemSlot hand = player.Player.InventoryManager.ActiveHotbarSlot;
        hand.Itemstack = jewelry.Itemstack;
        hand.Itemstack.Collectible.DamageItem(World.Api.World, player.Entity, hand,
            hand.Itemstack.Collectible.GetRemainingDurability(hand.Itemstack) + 1);
        Assert.True(hand.Empty, "the knife did not break");

        await World.Ticks(2);
        var dropped = DroppedOrPickedUpGems(player);

        Assert.Equal(2, dropped.Count);
        Assert.Contains(dropped, s => s.Collectible.Code.Path == $"gem-cut-flawless-{GemType}");
        Assert.All(dropped, s =>
        {
            Assert.True(s.Attributes.GetBool("wasExtracted"), "a dropped gem is not marked extracted");
            ITreeAttribute tree = s.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
            Assert.NotNull(tree);
            Assert.False(tree.HasAttribute("slot0"), "the dropped gem carries the piece's socket tree");
        });
    }

    [AtlasScenario]
    public async Task BrokenJewelry_Should_DropNothing_WhenChanceIsZero()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.chance_gem_drop_on_item_broken, (c, v) => c.chance_gem_drop_on_item_broken = v, 0f);

        var jewelry = new DummySlot(LegacySocketedJewelry());
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem()), 0, player.Player));

        ItemSlot hand = player.Player.InventoryManager.ActiveHotbarSlot;
        hand.Itemstack = jewelry.Itemstack;
        hand.Itemstack.Collectible.DamageItem(World.Api.World, player.Entity, hand,
            hand.Itemstack.Collectible.GetRemainingDurability(hand.Itemstack) + 1);

        await World.Ticks(2);
        Assert.Empty(DroppedOrPickedUpGems(player));
    }

    // A dropped gem has no thrower, so the player standing on it collects it the same tick:
    // count what lies around and what went into the inventory.
    private List<ItemStack> DroppedOrPickedUpGems(ITestPlayer player)
    {
        BlockPos at = player.Position;
        IEnumerable<ItemStack> lying = World.EntitiesIn(new Cuboidi(at.X - 3, at.Y - 3, at.Z - 3, at.X + 3, at.Y + 3, at.Z + 3))
            .OfType<EntityItem>()
            .Where(e => e.Alive)
            .Select(e => e.Itemstack);
        IEnumerable<ItemStack> carried = new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName }
            .Select(cls => player.Player.InventoryManager.GetOwnInventory(cls))
            .Where(inv => inv != null)
            .SelectMany(inv => inv!)
            .Select(slot => slot.Itemstack);

        return lying.Concat(carried)
            .Where(s => s?.Collectible.Code.Path.StartsWith("gem-cut-") == true)
            .ToList();
    }
}
