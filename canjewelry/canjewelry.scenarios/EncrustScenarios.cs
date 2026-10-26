using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Hooks = global::canjewelry.src.integration;

namespace canjewelry.scenarios;

/// <summary>
/// Setting gems into sockets: what the socket records, which gems an item takes, swapping a gem,
/// the durability gem and the encrust hook.
/// </summary>
public class EncrustScenarios : SocketScenarioBase
{
    private static bool Encrust(InventoryBase inventory, ItemSlot jewelry, ItemSlot gem, int socket, ITestPlayer? player = null) =>
        Encrustable.TryToEncrustGemsIntoSockets(inventory, jewelry, gem, socket, player?.Player);

    [AtlasScenario]
    public async Task Encrust_Should_RecordTheGemInTheSocket()
    {
        ITestPlayer player = await Join();
        var jewelry = new DummySlot(SocketedKnife(2));
        var gem = new DummySlot(CutGem("flawless", names: new[] { "walkspeed", "miningSpeedMul" },
            values: new[] { 0.05f, 0.02f }, cut: CANJWConstants.CUTTING_BAGUETTE));

        Assert.True(Encrust(ScratchInventory(), jewelry, gem, 1, player));

        Assert.True(gem.Empty);
        ITreeAttribute socket = SocketOf(jewelry.Itemstack, 1);
        Assert.Equal(GemType, socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        Assert.Equal(2, socket.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE));
        Assert.Equal(CANJWConstants.CUTTING_BAGUETTE, socket.GetString(CANJWConstants.CUTTING_TYPE));
        Assert.Equal((int)global::canjewelry.src.cb.EnumGemBuffType.STATS_BUFF, socket.GetInt(CANJWConstants.GEM_BUFF_TYPE));
        Assert.Equal(new[] { "walkspeed", "miningSpeedMul" }, BuffNames(socket));
        Assert.Equal(new[] { 0.05f, 0.02f }, BuffValues(socket));
        Assert.True(socket.GetBool(CANJWConstants.WAS_GROUND_BEFORE));
        Assert.False(socket.GetBool("wasExtracted"));
        Assert.Equal(3, socket.GetInt(CANJWConstants.ADDED_SOCKET_TYPE));
        Assert.Equal(SteelSocket, socket.GetString(CANJWConstants.SOCKET_ITEM_CODE));
        Assert.Equal("", SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
    }

    // Only one gem leaves a stack of several.
    [AtlasScenario]
    public async Task Encrust_Should_TakeOneGemFromTheStack()
    {
        var jewelry = new DummySlot(SocketedKnife(1));
        ItemStack gems = CutGem();
        gems.StackSize = 3;
        var gem = new DummySlot(gems);

        Assert.True(Encrust(ScratchInventory(), jewelry, gem, 0));
        Assert.Equal(2, gem.StackSize);
        await World.Ticks(1);
    }

    // Unfinished grinding travels with the gem into the socket.
    [AtlasScenario]
    public async Task Encrust_Should_KeepGrindingProgress()
    {
        var jewelry = new DummySlot(SocketedKnife(1));
        ItemStack unground = CutGem(ground: false);
        ITreeAttribute grind = new TreeAttribute();
        grind.SetInt("stage", 2);
        unground.Attributes["cangrindlayerinfo"] = grind;
        unground.Attributes.SetBool("wasExtracted", true);

        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(unground), 0));

        ITreeAttribute socket = SocketOf(jewelry.Itemstack, 0);
        Assert.False(socket.GetBool(CANJWConstants.WAS_GROUND_BEFORE, true));
        Assert.Equal(2, (socket["savedGrindLayerInfo"] as ITreeAttribute)?.GetInt("stage"));
        Assert.True(socket.GetBool("wasExtracted"));
        await World.Ticks(1);
    }

    // A cut gem tree without a cut is read as round.
    [AtlasScenario]
    public async Task GemWithoutCut_Should_CountAsRound()
    {
        var jewelry = new DummySlot(SocketedKnife(1));
        ItemStack gem = CutGem();
        gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE).RemoveAttribute(CANJWConstants.CUTTING_TYPE);

        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(gem), 0));
        Assert.Equal(CANJWConstants.CUTTING_ROUND, SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.CUTTING_TYPE));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task Encrust_Should_RefuseWhatIsNotACutGem()
    {
        var jewelry = new DummySlot(SocketedKnife(1));
        ItemStack bareGem = Stack($"canjewelry:gem-cut-normal-{GemType}");

        Assert.False(Encrust(ScratchInventory(), jewelry, new DummySlot(bareGem), 0), "a gem without a cut gem tree went in");
        Assert.False(Encrust(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 0), "a socket item went in as a gem");
        Assert.False(Encrust(ScratchInventory(), jewelry, new DummySlot(null), 0));
        Assert.Equal("", SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task Encrust_Should_NeedASocket()
    {
        var gem = new DummySlot(CutGem());

        Assert.False(Encrust(ScratchInventory(), new DummySlot(Stack(JewelryCode)), gem, 0), "a piece without sockets took a gem");
        Assert.False(Encrust(ScratchInventory(), new DummySlot(SocketedKnife(1)), gem, 1), "a socket that was never added took a gem");
        Assert.False(Encrust(ScratchInventory(), new DummySlot(null), gem, 0));
        Assert.False(gem.Empty);
        await World.Ticks(1);
    }

    // Size against socket tier: a size 2 gem needs a tier 2 socket or better.
    [AtlasScenario]
    public async Task GemSize_Should_NotExceedSocketTier()
    {
        var jewelry = new DummySlot(WithSockets(Stack(JewelryCode), 1, IronSocket));

        Assert.False(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem("exquisite")), 0));
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem("flawless")), 0));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task Encrust_Should_UnlockTheInventory()
    {
        InventoryBase inventory = ScratchInventory();
        Encrust(inventory, new DummySlot(SocketedKnife(1)), new DummySlot(CutGem()), 0);
        Assert.False(inventory.TakeLocked);
        Encrust(inventory, new DummySlot(Stack(JewelryCode)), new DummySlot(CutGem()), 0);
        Assert.False(inventory.TakeLocked);
        await World.Ticks(1);
    }

    // ---- which gems an item takes ----

    [AtlasScenario]
    public async Task GemNotMeantForTheItem_Should_BeRefused()
    {
        SetConfig(c => c.buffNameToPossibleItem, (c, v) => c.buffNameToPossibleItem = v,
            new Dictionary<string, HashSet<string>> { [GemType] = new() { "knife" } });
        var jewelry = new DummySlot(SocketedKnife(1));
        var emerald = new DummySlot(CutGem(gemType: "emerald"));

        Assert.False(Encrust(ScratchInventory(), jewelry, emerald, 0));
        Assert.False(emerald.Empty);
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem()), 0));
        await World.Ticks(1);
    }

    // buffNameToPossibleItem entries are code fragments matched anywhere in the item path.
    [AtlasScenario]
    public async Task GemItemTable_Should_MatchCodeFragments()
    {
        SetConfig(c => c.buffNameToPossibleItem, (c, v) => c.buffNameToPossibleItem = v,
            new Dictionary<string, HashSet<string>> { ["emerald"] = new() { "generic-st" }, [GemType] = new() { "pickaxe" } });

        Assert.True(Encrustable.canItemContainThisGem("emerald", Stack(JewelryCode)));
        Assert.False(Encrustable.canItemContainThisGem(GemType, Stack(JewelryCode)));
        Assert.False(Encrustable.canItemContainThisGem("ruby", Stack(JewelryCode)));
        Assert.False(Encrustable.canItemContainThisGem(null!, Stack(JewelryCode)));
        Assert.False(Encrustable.canItemContainThisGem(GemType, null!));
        await World.Ticks(1);
    }

    // canAllowedGems on the item admits a gem the config table does not.
    [AtlasScenario]
    public async Task AllowedGemsAttribute_Should_AdmitTheGem()
    {
        SetConfig(c => c.buffNameToPossibleItem, (c, v) => c.buffNameToPossibleItem = v, new Dictionary<string, HashSet<string>>());
        WithItemAttributes(BareKnifeCode,
            "{ \"cansocketstiers\": [3], \"canhavesocketsnumber\": 1, \"canAllowedGems\": [\"emerald\"] }");

        Assert.True(Encrustable.canItemContainThisGem("emerald", Stack(BareKnifeCode)));
        Assert.False(Encrustable.canItemContainThisGem(GemType, Stack(BareKnifeCode)));

        var jewelry = new DummySlot(WithSockets(Stack(BareKnifeCode), 1));
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem(gemType: "emerald")), 0));
        Assert.Equal("emerald", SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        await World.Ticks(1);
    }

    // canGemGroups on the item admits every gem config.gemToGroups puts in one of those groups.
    [AtlasScenario]
    public async Task GemGroupsAttribute_Should_AdmitGemsOfTheGroup()
    {
        SetConfig(c => c.buffNameToPossibleItem, (c, v) => c.buffNameToPossibleItem = v, new Dictionary<string, HashSet<string>>());
        SetConfig(c => c.gemToGroups, (c, v) => c.gemToGroups = v,
            new Dictionary<string, HashSet<string>> { ["emerald"] = new() { "armor", "melee" }, ["ruby"] = new() { "ranged" } });
        WithItemAttributes(BareKnifeCode,
            "{ \"cansocketstiers\": [3], \"canhavesocketsnumber\": 1, \"canGemGroups\": [\"melee\"] }");

        Assert.True(Encrustable.canItemContainThisGem("emerald", Stack(BareKnifeCode)));
        Assert.False(Encrustable.canItemContainThisGem("ruby", Stack(BareKnifeCode)));
        Assert.False(Encrustable.canItemContainThisGem(GemType, Stack(BareKnifeCode)));
        await World.Ticks(1);
    }

    // ---- swapping ----

    // Setting a gem into a filled socket replaces the one there; the old gem is not given back.
    [AtlasScenario]
    public async Task GemInFilledSocket_Should_ReplaceTheOldOne()
    {
        var jewelry = new DummySlot(SocketedKnife(1));
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem("flawless", names: new[] { "walkspeed" }, values: new[] { 0.05f })), 0));

        var second = new DummySlot(CutGem("normal", gemType: "amethyst", names: new[] { "miningSpeedMul" }, values: new[] { 0.03f },
            cut: CANJWConstants.CUTTING_PEAR));
        SetConfig(c => c.buffNameToPossibleItem, (c, v) => c.buffNameToPossibleItem = v,
            new Dictionary<string, HashSet<string>> { ["amethyst"] = new() { "knife" } });
        Assert.True(Encrust(ScratchInventory(), jewelry, second, 0));

        ITreeAttribute socket = SocketOf(jewelry.Itemstack, 0);
        Assert.Equal("amethyst", socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        Assert.Equal(1, socket.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE));
        Assert.Equal(CANJWConstants.CUTTING_PEAR, socket.GetString(CANJWConstants.CUTTING_TYPE));
        Assert.Equal(new[] { "miningSpeedMul" }, BuffNames(socket));
        Assert.Equal(new[] { 0.03f }, BuffValues(socket));
        Assert.Equal(1, EncrustedTree(jewelry.Itemstack)!.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        await World.Ticks(1);
    }

    // ---- the durability gem ----

    [AtlasScenario]
    public async Task DurabilityGem_Should_RaiseDurabilityAndMax()
    {
        ItemStack knife = SocketedKnife(1);
        int baseMax = knife.Collectible.GetMaxDurability(knife);
        knife.Attributes.SetInt("durability", 1000);
        var jewelry = new DummySlot(knife);

        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));

        Assert.Equal(1500, jewelry.Itemstack.Attributes.GetInt("durability"));
        Assert.Equal(0.5f, EncrustedTree(jewelry.Itemstack)!.GetFloat(CANJWConstants.CANDURABILITY_STRING));
        Assert.Equal((int)(baseMax * 1.5f), jewelry.Itemstack.Collectible.GetMaxDurability(jewelry.Itemstack));
        await World.Ticks(1);
    }

    // Two durability gems add up on the piece.
    [AtlasScenario]
    public async Task DurabilityGems_Should_AddUp()
    {
        ItemStack knife = SocketedKnife(2);
        knife.Attributes.SetInt("durability", 1000);
        var jewelry = new DummySlot(knife);

        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.25f)), 1));

        Assert.Equal(1875, jewelry.Itemstack.Attributes.GetInt("durability"));
        Assert.Equal(0.75f, EncrustedTree(jewelry.Itemstack)!.GetFloat(CANJWConstants.CANDURABILITY_STRING));
        await World.Ticks(1);
    }

    // A fresh piece has no durability attribute yet: only the maximum moves.
    [AtlasScenario]
    public async Task DurabilityGem_OnFreshPiece_Should_OnlyRaiseMax()
    {
        var jewelry = new DummySlot(SocketedKnife(1));
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));

        Assert.False(jewelry.Itemstack.Attributes.HasAttribute("durability"));
        Assert.Equal(0.5f, EncrustedTree(jewelry.Itemstack)!.GetFloat(CANJWConstants.CANDURABILITY_STRING));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task SwappingDurabilityGem_Should_RemoveTheOldBonusFirst()
    {
        ItemStack knife = SocketedKnife(1);
        knife.Attributes.SetInt("durability", 1000);
        var jewelry = new DummySlot(knife);
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));

        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.25f)), 0));

        Assert.Equal(1250, jewelry.Itemstack.Attributes.GetInt("durability"));
        Assert.Equal(0.25f, EncrustedTree(jewelry.Itemstack)!.GetFloat(CANJWConstants.CANDURABILITY_STRING));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task SwappingOutDurabilityGem_Should_DropTheBonus()
    {
        ItemStack knife = SocketedKnife(1);
        knife.Attributes.SetInt("durability", 1000);
        var jewelry = new DummySlot(knife);
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));

        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem()), 0));

        Assert.Equal(1000, jewelry.Itemstack.Attributes.GetInt("durability"));
        Assert.False(EncrustedTree(jewelry.Itemstack)!.HasAttribute(CANJWConstants.CANDURABILITY_STRING));
        Assert.Equal(knife.Collectible.Durability, jewelry.Itemstack.Collectible.GetMaxDurability(jewelry.Itemstack));
        await World.Ticks(1);
    }

    // An item without durability takes no durability gem, and one stuck in it cannot be swapped.
    [AtlasScenario]
    public async Task DurabilityGem_Should_NotGoIntoItemWithoutDurability()
    {
        ItemStack knife = SocketedKnife(1);
        WithDurability(JewelryCode, 0);
        var jewelry = new DummySlot(knife);

        Assert.False(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem()), 0));

        ITreeAttribute socket = SocketOf(knife, 0);
        socket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { CANJWConstants.CANDURABILITY_STRING });
        socket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(new[] { 0.5f });
        Assert.False(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem()), 0));
        await World.Ticks(1);
    }

    // The postfix on GetMaxDurability reads the bonus off the encrusted tree.
    [AtlasScenario]
    public async Task MaxDurability_Should_FollowTheTreeBonus()
    {
        ItemStack knife = Stack(JewelryCode);
        int baseMax = knife.Collectible.Durability;
        Assert.Equal(baseMax, knife.Collectible.GetMaxDurability(knife));

        ItemStack socketed = SocketedKnife(1);
        Assert.Equal(baseMax, socketed.Collectible.GetMaxDurability(socketed));

        EncrustedTree(socketed)!.SetFloat(CANJWConstants.CANDURABILITY_STRING, 0.25f);
        Assert.Equal((int)(baseMax * 1.25f), socketed.Collectible.GetMaxDurability(socketed));

        EncrustedTree(socketed)!.SetFloat(CANJWConstants.CANDURABILITY_STRING, -0.25f);
        Assert.Equal(baseMax, socketed.Collectible.GetMaxDurability(socketed));
        await World.Ticks(1);
    }

    // ---- buffs switched off ----

    [AtlasScenario]
    public async Task BuffsOff_Should_SetTheGemWithoutBuffs()
    {
        SetConfig(c => c.TurnOffBuffs, (c, v) => c.TurnOffBuffs = v, true);
        ItemStack knife = SocketedKnife(1);
        knife.Attributes.SetInt("durability", 1000);
        var jewelry = new DummySlot(knife);

        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));

        ITreeAttribute socket = SocketOf(knife, 0);
        Assert.Equal(GemType, socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        Assert.Null(BuffNames(socket));
        Assert.Equal(1000, knife.Attributes.GetInt("durability"));
        Assert.False(EncrustedTree(knife)!.HasAttribute(CANJWConstants.CANDURABILITY_STRING));
        await World.Ticks(1);
    }

    // ---- hooks ----

    // A subscriber sees each buff of the gem and its value is what lands in the socket.
    [AtlasScenario]
    public async Task EncrustHook_Should_ScaleTheStoredValues()
    {
        ITestPlayer player = await Join();
        var seen = new List<Hooks.EncrustEvent>();
        OnEncrust(e =>
        {
            seen.Add(e);
            if (e.BuffName == "walkspeed") e.Value *= 2f;
        });

        var jewelry = new DummySlot(SocketedKnife(3));
        ItemStack gem = CutGem("flawless", names: new[] { "walkspeed", "miningSpeedMul" }, values: new[] { 0.05f, 0.02f });
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(gem), 2, player));

        Assert.Equal(new[] { 0.1f, 0.02f }, BuffValues(SocketOf(jewelry.Itemstack, 2)));
        Assert.Equal(2, seen.Count);
        Assert.Equal(new[] { "walkspeed", "miningSpeedMul" }, seen.Select(e => e.BuffName));
        Assert.All(seen, e =>
        {
            Assert.Equal(2, e.SocketIndex);
            Assert.Equal(3, e.SocketTier);
            Assert.Same(player.Player, e.Player);
            Assert.Same(jewelry.Itemstack, e.Jewelry);
            Assert.Equal(gem.Collectible.Code, e.Gem.Collectible.Code);
        });
    }

    // With buffs switched off the hook is not asked.
    [AtlasScenario]
    public async Task EncrustHook_Should_NotFire_WhenBuffsAreOff()
    {
        SetConfig(c => c.TurnOffBuffs, (c, v) => c.TurnOffBuffs = v, true);
        int calls = 0;
        OnEncrust(_ => calls++);

        Assert.True(Encrust(ScratchInventory(), new DummySlot(SocketedKnife(1)), new DummySlot(CutGem()), 0));
        Assert.Equal(0, calls);
        await World.Ticks(1);
    }

    // A fresh gem earns encrust XP; one that came out of a socket before does not.
    [AtlasScenario]
    public async Task EncrustXp_Should_SkipExtractedGems()
    {
        var jewelry = new DummySlot(SocketedKnife(2));
        List<string> xp = RecordXp();
        ItemStack extracted = CutGem();
        extracted.Attributes.SetBool("wasExtracted", true);

        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(CutGem()), 0));
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(extracted), 1));

        Assert.Equal(new[] { "encrust" }, xp);
        await World.Ticks(1);
    }

    // What the socket stores is enough to rebuild the gem: code, buffs and the ground flag.
    [AtlasScenario]
    public async Task EncrustedSocket_Should_BeReadBackAsTheSameGem()
    {
        var jewelry = new DummySlot(SocketedKnife(1));
        ItemStack gem = CutGem("flawless", names: new[] { "walkspeed" }, values: new[] { 0.06f });
        Assert.True(Encrust(ScratchInventory(), jewelry, new DummySlot(gem.Clone()), 0));

        ItemStack? rebuilt = global::canjewelry.src.utils.CANGemSize.BuildCutGemFromSocket(World.Api.World, SocketOf(jewelry.Itemstack, 0));

        Assert.NotNull(rebuilt);
        Assert.Equal(gem.Collectible.Code, rebuilt!.Collectible.Code);
        ITreeAttribute tree = rebuilt.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal(new[] { 0.06f }, BuffValues(tree));
        Assert.True(tree.GetBool(CANJWConstants.GEM_FULL_PROCESSED));
        await World.Ticks(1);
    }
}
