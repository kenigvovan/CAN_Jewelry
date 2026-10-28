using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Hooks = global::canjewelry.src.integration;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// Pulling gems and sockets back out: the config chances, the extract hook and what it may
/// change, the durability gem on the way out, and removing sockets.
/// </summary>
public class ExtractScenarios : SocketScenarioBase
{
    private void SureReturn()
    {
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 1f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);
    }

    /// <summary>The knife with a gem in each of the first <paramref name="gems"/> sockets.</summary>
    private DummySlot KnifeWithGems(int sockets, params ItemStack[] gems)
    {
        var jewelry = new DummySlot(SocketedKnife(sockets));
        for (int i = 0; i < gems.Length; i++)
        {
            Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(gems[i]), i));
        }
        return jewelry;
    }

    private bool Extract(ItemSlot jewelry, ItemSlot outSlot, int socket, ITestPlayer? player = null) =>
        Encrustable.TryExtractGem(ScratchInventory(), jewelry, outSlot, socket, player?.Player);

    // ---- gems ----

    [AtlasScenario]
    public async Task Extract_Should_EmptyTheSocketButKeepIt()
    {
        SureReturn();
        DummySlot jewelry = KnifeWithGems(2, CutGem("flawless"));

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        ITreeAttribute socket = SocketOf(jewelry.Itemstack, 0);
        Assert.Equal("", socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        Assert.False(socket.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES));
        Assert.False(socket.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_VALUES));
        Assert.False(socket.HasAttribute(CANJWConstants.GEM_BUFF_TYPE));
        Assert.False(socket.HasAttribute(CANJWConstants.ENCRUSTED_GEM_SIZE));
        Assert.False(socket.HasAttribute(CANJWConstants.CUTTING_TYPE));
        Assert.Equal(3, socket.GetInt(CANJWConstants.ADDED_SOCKET_TYPE));
        Assert.Equal(SteelSocket, socket.GetString(CANJWConstants.SOCKET_ITEM_CODE));
        Assert.Equal(2, EncrustedTree(jewelry.Itemstack)!.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        Assert.Equal($"canjewelry:gem-cut-flawless-{GemType}", outSlot.Itemstack?.Collectible.Code.ToString());
        await World.Ticks(1);
    }

    private static ItemStack HalfGround(ItemStack gem)
    {
        var layer = new TreeAttribute();
        layer.SetInt("scenariostage", 1);
        gem.Attributes["cangrindlayerinfo"] = layer;
        return gem;
    }

    // The next gem in the socket must not inherit the previous one's grinding or extraction taint.
    [AtlasScenario]
    public async Task Extract_Should_LeaveNothingOfTheGemInTheSocket()
    {
        SureReturn();
        ItemStack first = HalfGround(CutGem(ground: false));
        first.Attributes.SetBool("wasExtracted", true);
        DummySlot jewelry = KnifeWithGems(1, first);

        Assert.True(Extract(jewelry, new DummySlot(null), 0));

        ITreeAttribute socket = SocketOf(jewelry.Itemstack, 0);
        Assert.False(socket.HasAttribute("savedGrindLayerInfo"));
        Assert.False(socket.HasAttribute("wasExtracted"));
        Assert.False(socket.HasAttribute(CANJWConstants.WAS_GROUND_BEFORE));

        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(CutGem(ground: false)), 0));
        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        ItemStack second = outSlot.Itemstack!;
        Assert.False(second.Attributes.HasAttribute("cangrindlayerinfo"), "the second gem got the first one's grinding");
        Assert.False(second.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE).GetBool(CANJWConstants.GEM_FULL_PROCESSED));
        await World.Ticks(1);
    }

    // Sockets emptied by older versions still hold the previous gem's grinding.
    [AtlasScenario]
    public async Task Encrust_Should_DropStaleGrindingOfAnEmptiedSocket()
    {
        SureReturn();
        var jewelry = new DummySlot(SocketedKnife(1));
        var stale = new TreeAttribute();
        stale.SetInt("scenariostage", 1);
        SocketOf(jewelry.Itemstack, 0)["savedGrindLayerInfo"] = stale;

        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(CutGem(ground: false)), 0));

        Assert.False(SocketOf(jewelry.Itemstack, 0).HasAttribute("savedGrindLayerInfo"));
        await World.Ticks(1);
    }

    // ---- weight ----

    private ItemStack WeighedGem(string quality, float carats, params string[] traits)
    {
        ItemStack gem = CutGem(quality);
        CANGemAppraisal.SetWeight(gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE), carats, traits);
        return gem;
    }

    [AtlasScenario]
    public async Task WeightAndTraits_Should_GoIntoTheSocket_AndComeBackOut()
    {
        SureReturn();
        DummySlot jewelry = KnifeWithGems(1, WeighedGem("flawless", 2.2f, "cloudy"));

        ITreeAttribute socket = SocketOf(jewelry.Itemstack, 0);
        Assert.Equal(2.2f, CANGemAppraisal.GetCarats(socket), 3);
        Assert.Equal(new[] { "cloudy" }, CANGemAppraisal.GetTraits(socket));

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        Assert.False(CANGemAppraisal.HasCarats(SocketOf(jewelry.Itemstack, 0)));
        Assert.Empty(CANGemAppraisal.GetTraits(SocketOf(jewelry.Itemstack, 0)));
        ITreeAttribute back = outSlot.Itemstack!.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal(2.2f, CANGemAppraisal.GetCarats(back), 3);
        Assert.Equal(new[] { "cloudy" }, CANGemAppraisal.GetTraits(back));
        await World.Ticks(1);
    }

    // Gems from before carats get the weight of their size written once they go in.
    [AtlasScenario]
    public async Task LegacyGem_Should_GetTheLegacyWeightOfItsSize()
    {
        SureReturn();
        var legacy = Mod.config.GemCarats.LegacyCutCarats;
        DummySlot jewelry = KnifeWithGems(3, CutGem("normal"), CutGem("flawless"), CutGem("exquisite"));

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(legacy[i + 1], CANGemAppraisal.GetCarats(SocketOf(jewelry.Itemstack, i)), 3);
        }
        await World.Ticks(1);
    }

    // A tier 2 socket holds 2.5 ct: a heavier flawless gem needs a bigger socket, unless by tiers.
    [AtlasScenario]
    public async Task Encrust_Should_RefuseGemTooHeavyForTheSocket()
    {
        var knife = new DummySlot(WithSockets(Stack(JewelryCode), 1, IronSocket));
        Assert.Equal(2, SocketOf(knife.Itemstack, 0).GetInt(CANJWConstants.ADDED_SOCKET_TYPE));

        Assert.False(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), knife, new DummySlot(WeighedGem("flawless", 2.6f)), 0));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), knife, new DummySlot(WeighedGem("flawless", 2.5f)), 0));

        var byTiers = new DummySlot(WithSockets(Stack(JewelryCode), 1, IronSocket));
        SetConfig(c => c.GemCarats.CapacityMode, (c, v) => c.GemCarats.CapacityMode = v, "tiers");
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), byTiers, new DummySlot(WeighedGem("flawless", 2.6f)), 0));
        await World.Ticks(1);
    }

    // A budget limits all the item's gems together; switched off in the config, it limits nothing.
    [AtlasScenario]
    public async Task CaratBudget_Should_LimitTheGemsTogether_UnlessTurnedOff()
    {
        SetConfig(c => c.GemCarats.CaratBudgets, (c, v) => c.GemCarats.CaratBudgets = v,
            new Dictionary<string, float> { { JewelryCode, 3.0f } });

        var knife = new DummySlot(SocketedKnife(2));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), knife, new DummySlot(WeighedGem("flawless", 2.0f)), 0));
        Assert.False(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), knife, new DummySlot(WeighedGem("normal", 1.5f)), 1),
            "2.0 + 1.5 is over a budget of 3.0");
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), knife, new DummySlot(WeighedGem("normal", 1.0f)), 1));

        // Replacing a gem frees its share: 1.0 + 2.0 fits in place of the first 2.0.
        Assert.Equal(3.0f, CANGemAppraisal.CaratsInSockets(Mod.config.GemCarats, knife.Itemstack, -1), 3);
        Assert.Equal(1.0f, CANGemAppraisal.CaratsInSockets(Mod.config.GemCarats, knife.Itemstack, 0), 3);

        SetConfig(c => c.GemCarats.CaratBudgetEnabled, (c, v) => c.GemCarats.CaratBudgetEnabled = v, false);
        var free = new DummySlot(SocketedKnife(2));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), free, new DummySlot(WeighedGem("flawless", 2.0f)), 0));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), free, new DummySlot(WeighedGem("normal", 1.5f)), 1));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task EncrustAndExtractEvents_Should_CarryTheWeight()
    {
        SureReturn();
        float encrusted = 0, extracted = 0;
        OnEncrust(e => encrusted = e.Carats);
        OnExtract(e => extracted = e.Carats);

        DummySlot jewelry = KnifeWithGems(1, WeighedGem("flawless", 1.9f));
        Assert.True(Extract(jewelry, new DummySlot(null), 0));

        Assert.Equal(1.9f, encrusted, 3);
        Assert.Equal(1.9f, extracted, 3);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task Extract_Should_RefuseEmptyOrMissingSockets()
    {
        SureReturn();
        DummySlot jewelry = KnifeWithGems(2, CutGem());

        Assert.False(Extract(jewelry, new DummySlot(null), 1), "an empty socket gave a gem");
        Assert.False(Extract(jewelry, new DummySlot(null), 2), "a socket never added gave a gem");
        Assert.False(Extract(new DummySlot(Stack(JewelryCode)), new DummySlot(null), 0), "a piece without sockets gave a gem");
        Assert.False(Extract(new DummySlot(null), new DummySlot(null), 0));
        Assert.Equal(GemType, SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task FailedRoll_Should_DestroyTheGem()
    {
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 0f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);
        DummySlot jewelry = KnifeWithGems(1, CutGem());

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        Assert.True(outSlot.Empty, "a gem came back on a chance of 0");
        Assert.False(jewelry.Empty);
        Assert.Equal("", SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        await World.Ticks(1);
    }

    // The gem still comes back when the piece breaks.
    [AtlasScenario]
    public async Task BreakRoll_Should_DestroyThePiece()
    {
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 1f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 1f);
        DummySlot jewelry = KnifeWithGems(1, CutGem());

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        Assert.True(jewelry.Empty, "the piece survived a break chance of 1");
        Assert.False(outSlot.Empty);
        await World.Ticks(1);
    }

    // With the output slot taken the gem goes to the player.
    [AtlasScenario]
    public async Task OccupiedOutSlot_Should_SendTheGemToThePlayer()
    {
        ITestPlayer player = await Join();
        SureReturn();
        string code = $"canjewelry:gem-cut-exquisite-{GemType}";
        DropCarried(player, code);
        DummySlot jewelry = KnifeWithGems(1, CutGem());
        var occupied = new DummySlot(Stack("game:flint"));

        Assert.True(Extract(jewelry, occupied, 0, player));

        Assert.Equal("game:flint", occupied.Itemstack.Collectible.Code.ToString());
        ItemStack? given = CarriedStacks(player).FirstOrDefault(s => s.Collectible.Code.ToString() == code);
        Assert.NotNull(given);
        Assert.True(given!.Attributes.GetBool("wasExtracted"));
        DropCarried(player, code);
    }

    [AtlasScenario]
    public async Task Extract_Should_UnlockTheInventory()
    {
        SureReturn();
        InventoryBase inventory = ScratchInventory();
        DummySlot jewelry = KnifeWithGems(1, CutGem());

        Encrustable.TryExtractGem(inventory, jewelry, new DummySlot(null), 0);
        Assert.False(inventory.TakeLocked);
        Encrustable.TryExtractGem(inventory, jewelry, new DummySlot(null), 0);
        Assert.False(inventory.TakeLocked);
        await World.Ticks(1);
    }

    // ---- durability gem ----

    [AtlasScenario]
    public async Task ExtractingDurabilityGem_Should_TakeTheBonusBack()
    {
        SureReturn();
        ItemStack knife = SocketedKnife(1);
        knife.Attributes.SetInt("durability", 1000);
        var jewelry = new DummySlot(knife);
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));
        Assert.Equal(1500, knife.Attributes.GetInt("durability"));

        Assert.True(Extract(jewelry, new DummySlot(null), 0));

        Assert.Equal(1000, knife.Attributes.GetInt("durability"));
        Assert.False(EncrustedTree(knife)!.HasAttribute(CANJWConstants.CANDURABILITY_STRING));
        Assert.Equal(knife.Collectible.Durability, knife.Collectible.GetMaxDurability(knife));
        await World.Ticks(1);
    }

    // One of two durability gems out: the other's bonus stays.
    [AtlasScenario]
    public async Task ExtractingOneDurabilityGem_Should_KeepTheOther()
    {
        SureReturn();
        ItemStack knife = SocketedKnife(2);
        knife.Attributes.SetInt("durability", 1000);
        var jewelry = new DummySlot(knife);
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.5f)), 0));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(DurabilityGem(0.25f)), 1));

        Assert.True(Extract(jewelry, new DummySlot(null), 1));

        Assert.Equal(1500, knife.Attributes.GetInt("durability"));
        Assert.Equal(0.5f, EncrustedTree(knife)!.GetFloat(CANJWConstants.CANDURABILITY_STRING));
        await World.Ticks(1);
    }

    // A nearly worn piece never drops below 1 on the way out.
    [AtlasScenario]
    public async Task ExtractingDurabilityGem_Should_LeaveAtLeastOne()
    {
        SureReturn();
        DummySlot jewelry = KnifeWithGems(1, DurabilityGem(0.5f));
        jewelry.Itemstack.Attributes.SetInt("durability", 1);

        Assert.True(Extract(jewelry, new DummySlot(null), 0));
        Assert.Equal(1, jewelry.Itemstack.Attributes.GetInt("durability"));
        await World.Ticks(1);
    }

    // ---- extract hook ----

    [AtlasScenario]
    public async Task ExtractHook_Should_SeeTheSocket()
    {
        ITestPlayer player = await Join();
        Hooks.ExtractEvent? seen = null;
        OnExtract(e =>
        {
            seen = e;
            // Keep the outcome fixed whatever the rolls say.
            e.ExtractionChance = 1f;
            e.JewelryBreakChance = 0f;
        });
        DummySlot jewelry = KnifeWithGems(2, CutGem(), CutGem("flawless", names: new[] { "miningSpeedMul", "walkspeed" }, values: new[] { 0.04f, 0.01f }));

        Assert.True(Extract(jewelry, new DummySlot(null), 1, player));

        Assert.NotNull(seen);
        Assert.Equal(1, seen!.SocketIndex);
        Assert.Equal(3, seen.SocketTier);
        Assert.Equal("miningSpeedMul", seen.BuffName);
        Assert.Same(player.Player, seen.Player);
        Assert.Equal($"canjewelry:gem-cut-flawless-{GemType}", seen.Gem.Collectible.Code.ToString());
        Assert.Equal(Hooks.JewelryOutcome.Intact, seen.JewelryOutcome);
        Assert.Equal(0f, seen.DowngradeChance);
    }

    // What the event starts with: the config chances and the default outcomes.
    [AtlasScenario]
    public async Task ExtractHook_Should_StartFromConfigChances()
    {
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 0.75f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0.125f);
        float extraction = -1, breaking = -1;
        Hooks.GemOutcome gemOutcome = Hooks.GemOutcome.Destroyed;
        OnExtract(e =>
        {
            extraction = e.ExtractionChance;
            breaking = e.JewelryBreakChance;
            gemOutcome = e.GemOutcome;
            e.ExtractionChance = 1f;
            e.JewelryBreakChance = 0f;
        });

        Assert.True(Extract(KnifeWithGems(1, CutGem()), new DummySlot(null), 0));

        Assert.Equal(0.75f, extraction);
        Assert.Equal(0.125f, breaking);
        Assert.Equal(Hooks.GemOutcome.Returned, gemOutcome);
        await World.Ticks(1);
    }

    // The chances on the event are the ones rolled, not the config's.
    [AtlasScenario]
    public async Task ExtractHook_Should_OverrideConfigChances()
    {
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 0f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 1f);
        OnExtract(e =>
        {
            e.ExtractionChance = 1f;
            e.JewelryBreakChance = 0f;
        });
        DummySlot jewelry = KnifeWithGems(1, CutGem());

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        Assert.False(jewelry.Empty, "the piece broke though the hook set the chance to 0");
        Assert.False(outSlot.Empty, "the gem was lost though the hook set the chance to 1");
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ExtractHook_Should_BeAbleToDestroyTheGem()
    {
        SureReturn();
        OnExtract(e => e.GemOutcome = Hooks.GemOutcome.Destroyed);

        var outSlot = new DummySlot(null);
        Assert.True(Extract(KnifeWithGems(1, CutGem()), outSlot, 0));
        Assert.True(outSlot.Empty);
        await World.Ticks(1);
    }

    // A subscriber may change the gem handed back.
    [AtlasScenario]
    public async Task ExtractHook_Should_BeAbleToChangeTheReturnedGem()
    {
        SureReturn();
        OnExtract(e => e.Gem.Attributes.SetString("polishedBy", "companion"));

        var outSlot = new DummySlot(null);
        Assert.True(Extract(KnifeWithGems(1, CutGem()), outSlot, 0));

        Assert.Equal("companion", outSlot.Itemstack?.Attributes.GetString("polishedBy"));
        Assert.True(outSlot.Itemstack!.Attributes.GetBool("wasExtracted"));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task DamagedOutcome_Should_CostDurability()
    {
        SureReturn();
        OnExtract(e =>
        {
            e.JewelryOutcome = Hooks.JewelryOutcome.Damaged;
            e.JewelryDamageAmount = 100;
        });
        DummySlot jewelry = KnifeWithGems(1, CutGem());
        int max = jewelry.Itemstack.Collectible.GetMaxDurability(jewelry.Itemstack);

        Assert.True(Extract(jewelry, new DummySlot(null), 0));

        Assert.False(jewelry.Empty);
        Assert.Equal(max - 100, jewelry.Itemstack.Attributes.GetInt("durability"));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task DamageBeyondDurability_Should_DestroyThePiece()
    {
        SureReturn();
        OnExtract(e =>
        {
            e.JewelryOutcome = Hooks.JewelryOutcome.Damaged;
            e.JewelryDamageAmount = 50;
        });
        DummySlot jewelry = KnifeWithGems(1, CutGem());
        jewelry.Itemstack.Attributes.SetInt("durability", 50);

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        Assert.True(jewelry.Empty);
        Assert.False(outSlot.Empty, "the gem should still come back");
        await World.Ticks(1);
    }

    // Damaged with no amount does nothing to the piece.
    [AtlasScenario]
    public async Task DamagedOutcome_WithoutAmount_Should_LeaveThePiece()
    {
        SureReturn();
        OnExtract(e => e.JewelryOutcome = Hooks.JewelryOutcome.Damaged);
        DummySlot jewelry = KnifeWithGems(1, CutGem());

        Assert.True(Extract(jewelry, new DummySlot(null), 0));
        Assert.False(jewelry.Empty);
        Assert.False(jewelry.Itemstack.Attributes.HasAttribute("durability"));
        await World.Ticks(1);
    }

    // A lost gem may come back one size smaller, its buffs rolled again at that size and its
    // grinding to be done again.
    [AtlasScenario]
    public async Task DowngradeChance_Should_ReturnASmallerGem()
    {
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 0f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);
        OnExtract(e => e.DowngradeChance = 1f);
        DummySlot jewelry = KnifeWithGems(1, CutGem("flawless", names: new[] { "walkspeed" }, values: new[] { 0.05f }));

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        // A legacy flawless gem weighs 2.0 ct; it loses weight down into the normal size and its
        // buff follows the weight, grinding kept.
        var cfg = Mod.config.GemCarats;
        float from = cfg.LegacyCutCarats[2];
        float to = CANGemAppraisal.RoundCarats(Math.Min(from * (1 - cfg.ExtractionDowngradeLoss), cfg.BucketBounds[1]), cfg.Step);
        Assert.Equal($"canjewelry:gem-cut-normal-{GemType}", outSlot.Itemstack?.Collectible.Code.ToString());
        ITreeAttribute tree = outSlot.Itemstack!.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal(CANJWConstants.CUTTING_ROUND, tree.GetString(CANJWConstants.CUTTING_TYPE));
        Assert.Equal(new[] { "walkspeed" }, BuffNames(tree));
        Assert.Equal(to, CANGemAppraisal.GetCarats(tree), 3);
        Assert.Equal(CANGemBuffMath.Scale(0.05f, from, to, cfg.BuffExponent), BuffValues(tree)![0], 5);
        Assert.True(tree.GetBool(CANJWConstants.GEM_FULL_PROCESSED));
        Assert.True(outSlot.Itemstack.Attributes.GetBool("wasExtracted"));
        await World.Ticks(1);
    }

    // By tiers the smaller gem rerolls its buff in the smaller range and has to be ground again.
    [AtlasScenario]
    public async Task DowngradeByTiers_Should_RerollInTheSmallerRange()
    {
        SetConfig(c => c.GemCarats.BuffScaling, (c, v) => c.GemCarats.BuffScaling = v, "tiers");
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 0f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);
        OnExtract(e => e.DowngradeChance = 1f);
        DummySlot jewelry = KnifeWithGems(1, CutGem("flawless", names: new[] { "walkspeed" }, values: new[] { 0.05f }));

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        ITreeAttribute tree = outSlot.Itemstack!.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal($"canjewelry:gem-cut-normal-{GemType}", outSlot.Itemstack.Collectible.Code.ToString());
        Assert.InRange(BuffValues(tree)![0], 0.01f, 0.03f);
        Assert.False(tree.GetBool(CANJWConstants.GEM_FULL_PROCESSED));
        await World.Ticks(1);
    }

    // Already the smallest size: the gem comes back as it was.
    [AtlasScenario]
    public async Task DowngradeOfSmallestGem_Should_ReturnItWhole()
    {
        SureReturn();
        OnExtract(e => e.GemOutcome = Hooks.GemOutcome.ReturnedDowngraded);
        DummySlot jewelry = KnifeWithGems(1, CutGem("normal", names: new[] { "walkspeed" }, values: new[] { 0.05f }));

        var outSlot = new DummySlot(null);
        Assert.True(Extract(jewelry, outSlot, 0));

        Assert.Equal($"canjewelry:gem-cut-normal-{GemType}", outSlot.Itemstack?.Collectible.Code.ToString());
        Assert.Equal(new[] { 0.05f }, BuffValues(outSlot.Itemstack!.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task NoDowngradeChance_Should_LoseTheGem()
    {
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 0f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);
        OnExtract(e => e.DowngradeChance = 0f);

        var outSlot = new DummySlot(null);
        Assert.True(Extract(KnifeWithGems(1, CutGem("flawless")), outSlot, 0));
        Assert.True(outSlot.Empty);
        await World.Ticks(1);
    }

    // ---- sockets ----

    private bool ExtractSocket(ItemSlot jewelry, ItemSlot outSlot, int socket, ITestPlayer? player = null) =>
        Encrustable.TryExtractSocket(ScratchInventory(), jewelry, outSlot, socket, player?.Player);

    private void SureSocketReturn() =>
        SetConfig(c => c.socketExtractionReturnChance, (c, v) => c.socketExtractionReturnChance = v, 1f);

    [AtlasScenario]
    public async Task ExtractSocket_Should_GiveBackTheSocketItem()
    {
        SureSocketReturn();
        var jewelry = new DummySlot(WithSockets(Stack(JewelryCode), 1, IronSocket));
        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(SteelSocket)), 1));

        var outSlot = new DummySlot(null);
        Assert.True(ExtractSocket(jewelry, outSlot, 0));

        Assert.Equal(IronSocket, outSlot.Itemstack?.Collectible.Code.ToString());
        ITreeAttribute tree = EncrustedTree(jewelry.Itemstack)!;
        Assert.Equal(1, tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        Assert.Null(tree.GetTreeAttribute("slot0"));
        Assert.NotNull(tree.GetTreeAttribute("slot1"));
        await World.Ticks(1);
    }

    // The last socket out takes the whole encrusted tree with it.
    [AtlasScenario]
    public async Task LastSocketOut_Should_RemoveTheTree()
    {
        SureSocketReturn();
        var jewelry = new DummySlot(SocketedKnife(1));

        Assert.True(ExtractSocket(jewelry, new DummySlot(null), 0));
        Assert.False(jewelry.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING));
        await World.Ticks(1);
    }

    // A freed socket index can take a socket again.
    [AtlasScenario]
    public async Task FreedSocket_Should_TakeASocketAgain()
    {
        SureSocketReturn();
        var jewelry = new DummySlot(SocketedKnife(3));
        Assert.True(ExtractSocket(jewelry, new DummySlot(null), 1));

        Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack(TinbronzeSocket)), 1));
        Assert.Equal(3, EncrustedTree(jewelry.Itemstack)!.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        Assert.Equal(TinbronzeSocket, SocketOf(jewelry.Itemstack, 1).GetString(CANJWConstants.SOCKET_ITEM_CODE));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task FailedSocketRoll_Should_StillRemoveTheSocket()
    {
        SetConfig(c => c.socketExtractionReturnChance, (c, v) => c.socketExtractionReturnChance = v, 0f);
        var jewelry = new DummySlot(SocketedKnife(2));

        var outSlot = new DummySlot(null);
        Assert.True(ExtractSocket(jewelry, outSlot, 0));

        Assert.True(outSlot.Empty);
        Assert.Equal(1, EncrustedTree(jewelry.Itemstack)!.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        Assert.Null(EncrustedTree(jewelry.Itemstack)!.GetTreeAttribute("slot0"));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ExtractSocket_Should_BeOffWhenConfigSaysSo()
    {
        SureSocketReturn();
        SetConfig(c => c.canExtractSocket, (c, v) => c.canExtractSocket = v, false);
        var jewelry = new DummySlot(SocketedKnife(1));

        var outSlot = new DummySlot(null);
        Assert.False(ExtractSocket(jewelry, outSlot, 0));

        Assert.True(outSlot.Empty);
        Assert.NotNull(SocketOf(jewelry.Itemstack, 0));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task SocketWithGem_Should_NotComeOut()
    {
        SureSocketReturn();
        DummySlot jewelry = KnifeWithGems(1, CutGem());

        Assert.False(ExtractSocket(jewelry, new DummySlot(null), 0));
        Assert.Equal(GemType, SocketOf(jewelry.Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ExtractSocket_Should_RefuseMissingSockets()
    {
        SureSocketReturn();
        Assert.False(ExtractSocket(new DummySlot(SocketedKnife(1)), new DummySlot(null), 2));
        Assert.False(ExtractSocket(new DummySlot(Stack(JewelryCode)), new DummySlot(null), 0));
        Assert.False(ExtractSocket(new DummySlot(null), new DummySlot(null), 0));
        await World.Ticks(1);
    }

    // Sockets from versions that did not save the socket item are looked up by tier.
    [AtlasScenario]
    public async Task LegacySocket_Should_ComeBackByTier()
    {
        SureSocketReturn();
        SetConfig(c => c.LevelOfSocketByType, (c, v) => c.LevelOfSocketByType = v,
            new Dictionary<string, int> { [TinbronzeSocket] = 1, [IronSocket] = 2 });

        var outSlot = new DummySlot(null);
        Assert.True(ExtractSocket(new DummySlot(LegacySocketedJewelry(tier: 2)), outSlot, 0));
        Assert.Equal(IronSocket, outSlot.Itemstack?.Collectible.Code.ToString());

        // No socket item of that tier: the socket goes, nothing comes back.
        var lost = new DummySlot(null);
        var jewelry = new DummySlot(LegacySocketedJewelry(tier: 3));
        Assert.True(ExtractSocket(jewelry, lost, 0));
        Assert.True(lost.Empty);
        Assert.False(jewelry.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ExtractedSocket_Should_GoToThePlayer_WhenOutSlotIsTaken()
    {
        ITestPlayer player = await Join();
        SureSocketReturn();
        DropCarried(player, SteelSocket);
        var jewelry = new DummySlot(SocketedKnife(1));

        Assert.True(ExtractSocket(jewelry, new DummySlot(Stack("game:flint")), 0, player));

        Assert.Contains(CarriedStacks(player), s => s.Collectible.Code.ToString() == SteelSocket);
        DropCarried(player, SteelSocket);
    }

    [AtlasScenario]
    public async Task ExtractSocket_Should_FireXpEvent()
    {
        SureSocketReturn();
        var jewelry = new DummySlot(SocketedKnife(1));
        List<string> xp = RecordXp();

        Assert.True(ExtractSocket(jewelry, new DummySlot(null), 0));
        Assert.Equal(new[] { "extractSocket" }, xp);

        InventoryBase inventory = ScratchInventory();
        Encrustable.TryExtractSocket(inventory, new DummySlot(SocketedKnife(1)), new DummySlot(null), 0);
        Assert.False(inventory.TakeLocked);
        await World.Ticks(1);
    }
}
