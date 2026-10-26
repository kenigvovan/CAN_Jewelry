using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.be;
using canjewelry.src.blocks;
using canjewelry.src.integration;
using canjewelry.src.jewelry;
using canjewelry.src.utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// The events on canjewelry.Instance that companion mods subscribe to: what each carries, and what
/// a subscriber's changes do. Every handler is taken off again in a finally block.
/// </summary>
[Trait("Category", "Hooks")]
public class HookScenarios : ConfigScenarioBase
{
    private ItemStack WorkItem(string quality)
    {
        ItemStack workItem = Stack("canjewelry:gemcuttingworkitem");
        ITreeAttribute attributes = new TreeAttribute();
        attributes.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, GemType);
        attributes.SetString(CANJWConstants.ENCRUSTED_GEM_SIZE, quality);
        workItem.Attributes = attributes;
        return workItem;
    }

    private void SafeExtraction()
    {
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 1f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);
    }

    [AtlasScenario]
    public async Task EncrustEvent_Should_CarryBuff_AndApplyChangedValue()
    {
        ITestPlayer player = await Join();
        var seen = new List<EncrustEvent>();
        var xp = new List<CANXpEvent>();
        Action<EncrustEvent> onEncrust = e => { seen.Add(e); e.Value *= 2; };
        Action<CANXpEvent> onXp = xp.Add;
        Mod.Instance.OnEncrustBuff += onEncrust;
        Mod.Instance.OnXpAwarded += onXp;
        try
        {
            var jewelry = new DummySlot(LegacySocketedJewelry());
            var gem = new DummySlot(LegacyCutGem("normal", "walkspeed", 0.03f));
            ItemStack gemStack = gem.Itemstack;

            Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, gem, 0, player.Player));

            EncrustEvent e = Assert.Single(seen);
            Assert.Same(player.Player, e.Player);
            Assert.Same(jewelry.Itemstack, e.Jewelry);
            Assert.Same(gemStack, e.Gem);
            Assert.Equal(3, e.SocketTier);
            Assert.Equal(0, e.SocketIndex);
            Assert.Equal("walkspeed", e.BuffName);
            Assert.Equal(0.06f, e.Value);
            Assert.Equal(new[] { 0.06f }, (SocketOf(jewelry.Itemstack, 0)[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value);

            CANXpEvent award = Assert.Single(xp);
            Assert.Equal("encrust", award.Action);
            Assert.Same(player.Player, award.Player);
            Assert.Equal(0f, award.Xp);
        }
        finally
        {
            Mod.Instance.OnEncrustBuff -= onEncrust;
            Mod.Instance.OnXpAwarded -= onXp;
        }
    }

    // With buffs switched off nothing is offered for scaling, the XP still goes out.
    [AtlasScenario]
    public async Task EncrustEvent_Should_NotFire_WhenBuffsAreOff()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.TurnOffBuffs, (c, v) => c.TurnOffBuffs = v, true);
        int encrusts = 0;
        var xp = new List<CANXpEvent>();
        Action<EncrustEvent> onEncrust = _ => encrusts++;
        Action<CANXpEvent> onXp = xp.Add;
        Mod.Instance.OnEncrustBuff += onEncrust;
        Mod.Instance.OnXpAwarded += onXp;
        try
        {
            Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), new DummySlot(LegacySocketedJewelry()),
                new DummySlot(LegacyCutGem()), 0, player.Player));

            Assert.Equal(0, encrusts);
            Assert.Equal("encrust", Assert.Single(xp).Action);
        }
        finally
        {
            Mod.Instance.OnEncrustBuff -= onEncrust;
            Mod.Instance.OnXpAwarded -= onXp;
        }
    }

    // A gem that came out of a socket earns no XP when set in again.
    [AtlasScenario]
    public async Task ExtractedGem_Should_EarnNoEncrustXp()
    {
        ITestPlayer player = await Join();
        var xp = new List<CANXpEvent>();
        Action<CANXpEvent> onXp = xp.Add;
        Mod.Instance.OnXpAwarded += onXp;
        try
        {
            ItemStack gem = LegacyCutGem();
            gem.Attributes.SetBool("wasExtracted", true);

            Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), new DummySlot(LegacySocketedJewelry()),
                new DummySlot(gem), 0, player.Player));

            Assert.DoesNotContain(xp, e => e.Action == "encrust");
        }
        finally
        {
            Mod.Instance.OnXpAwarded -= onXp;
        }
    }

    [AtlasScenario]
    public async Task SocketXp_Should_FireOnAddAndRemove()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.canExtractSocket, (c, v) => c.canExtractSocket = v, true);
        var xp = new List<CANXpEvent>();
        Action<CANXpEvent> onXp = xp.Add;
        Mod.Instance.OnXpAwarded += onXp;
        try
        {
            var jewelry = new DummySlot(Stack(JewelryCode));
            Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack("canjewelry:cansocket-steel")), 0, player.Player));
            Assert.True(Encrustable.TryAddSocket(ScratchInventory(), jewelry, new DummySlot(Stack("canjewelry:cansocket-steel")), 1, player.Player));
            Assert.True(Encrustable.TryExtractSocket(ScratchInventory(), jewelry, new DummySlot(null), 1, player.Player));

            Assert.Equal(new[] { "addSocket", "addSocket", "extractSocket" }, xp.Select(e => e.Action));
            Assert.All(xp, e => Assert.Same(player.Player, e.Player));
        }
        finally
        {
            Mod.Instance.OnXpAwarded -= onXp;
        }
    }

    [AtlasScenario]
    public async Task ExtractEvent_Should_CarrySocketAndConfigChances()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 0.37f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0.11f);
        var seen = new List<ExtractEvent>();
        float extractChance = -1f, breakChance = -1f;
        string outcomes = "";
        Action<ExtractEvent> onExtract = e =>
        {
            seen.Add(e);
            // What the core offered, before the changes below.
            extractChance = e.ExtractionChance;
            breakChance = e.JewelryBreakChance;
            outcomes = e.GemOutcome + "/" + e.JewelryOutcome;
            e.ExtractionChance = 1f;
            e.JewelryBreakChance = 0f;
        };
        var jewelry = new DummySlot(LegacySocketedJewelry());
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem("flawless", "walkspeed", 0.04f)), 0, player.Player));
        Mod.Instance.OnExtractGem += onExtract;
        try
        {
            var outSlot = new DummySlot(null);
            Assert.True(Encrustable.TryExtractGem(ScratchInventory(), jewelry, outSlot, 0, player.Player));

            ExtractEvent e = Assert.Single(seen);
            Assert.Same(player.Player, e.Player);
            Assert.Equal(3, e.SocketTier);
            Assert.Equal(0, e.SocketIndex);
            Assert.Equal("walkspeed", e.BuffName);
            Assert.Equal($"canjewelry:gem-cut-flawless-{GemType}", e.Gem.Collectible.Code.ToString());
            Assert.Equal(0.37f, extractChance);
            Assert.Equal(0.11f, breakChance);
            Assert.Equal("Returned/Intact", outcomes);
            Assert.Equal(0f, e.DowngradeChance);

            // The subscriber's chances decide: the gem comes back, the jewelry stays.
            Assert.Equal($"canjewelry:gem-cut-flawless-{GemType}", outSlot.Itemstack?.Collectible.Code.ToString());
            Assert.NotNull(jewelry.Itemstack);
        }
        finally
        {
            Mod.Instance.OnExtractGem -= onExtract;
        }
    }

    [AtlasScenario]
    public async Task ExtractOutcomes_Should_FollowTheSubscriber()
    {
        ITestPlayer player = await Join();
        SafeExtraction();

        async Task<(DummySlot jewelry, DummySlot gemOut)> Extract(Action<ExtractEvent> decide, string quality = "flawless")
        {
            var jewelry = new DummySlot(LegacySocketedJewelry());
            Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem(quality)), 0, player.Player));
            var gemOut = new DummySlot(null);
            Mod.Instance.OnExtractGem += decide;
            try
            {
                Assert.True(Encrustable.TryExtractGem(ScratchInventory(), jewelry, gemOut, 0, player.Player));
            }
            finally
            {
                Mod.Instance.OnExtractGem -= decide;
            }
            await World.Ticks(1);
            return (jewelry, gemOut);
        }

        var destroyedGem = await Extract(e => e.GemOutcome = GemOutcome.Destroyed);
        Assert.True(destroyedGem.gemOut.Empty, "a destroyed gem came back");
        Assert.Equal("", SocketOf(destroyedGem.jewelry.Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));

        var destroyedJewelry = await Extract(e => e.JewelryOutcome = JewelryOutcome.Destroyed);
        Assert.True(destroyedJewelry.jewelry.Empty, "the destroyed jewelry is still there");
        Assert.False(destroyedJewelry.gemOut.Empty);

        var damaged = await Extract(e => { e.JewelryOutcome = JewelryOutcome.Damaged; e.JewelryDamageAmount = 5; });
        ItemStack knife = damaged.jewelry.Itemstack;
        Assert.Equal(knife.Collectible.GetMaxDurability(knife) - 5, knife.Attributes.GetInt("durability"));

        var downgraded = await Extract(e => e.GemOutcome = GemOutcome.ReturnedDowngraded);
        Assert.Equal($"canjewelry:gem-cut-normal-{GemType}", downgraded.gemOut.Itemstack?.Collectible.Code.ToString());
        Assert.True(downgraded.gemOut.Itemstack!.Attributes.GetBool("wasExtracted"));

        // The smallest cut cannot go lower and comes back as it was.
        var smallest = await Extract(e => e.GemOutcome = GemOutcome.ReturnedDowngraded, "normal");
        Assert.Equal($"canjewelry:gem-cut-normal-{GemType}", smallest.gemOut.Itemstack?.Collectible.Code.ToString());
    }

    [AtlasScenario]
    public async Task CutEvent_Should_CarryTheCut_AndRefundOnRequest()
    {
        ITestPlayer player = await Join();
        EmptyPlayerInventory(player);
        string rough = $"canjewelry:gem-rough-flawed-{GemType}";
        var seen = new List<CutEvent>();
        bool keepRough = false;
        Action<CutEvent> onCut = e => { seen.Add(e); e.ConsumeRough = !keepRough; };
        Mod.Instance.OnCutGem += onCut;
        try
        {
            ItemStack cut = Stack($"canjewelry:gem-cut-flawless-{GemType}");
            ITreeAttribute tree = new TreeAttribute();
            tree.SetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_PEAR);
            cut.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;

            Encrustable.FireCutCompletionEvents(player.Player, WorkItem("flawed"), cut);

            CutEvent e = Assert.Single(seen);
            Assert.Same(player.Player, e.Player);
            Assert.Equal(CANJWConstants.CUTTING_PEAR, e.CuttingType);
            Assert.Equal("flawed", e.GemSize);
            Assert.Equal(rough, e.RoughGem.Collectible.Code.ToString());
            Assert.Equal(0, CountInPlayer(player, rough));

            keepRough = true;
            Encrustable.FireCutCompletionEvents(player.Player, WorkItem("flawed"), cut);
            Assert.Equal(1, CountInPlayer(player, rough));

            // No player, no event.
            Encrustable.FireCutCompletionEvents(null!, WorkItem("flawed"), cut);
            Assert.Equal(2, seen.Count);
        }
        finally
        {
            Mod.Instance.OnCutGem -= onCut;
            EmptyPlayerInventory(player);
        }
    }

    [AtlasScenario]
    public async Task GrindStageStart_Should_LetSubscriberShortenTheStage()
    {
        ITestPlayer player = await Join();
        ItemStack gem = Stack($"canjewelry:gem-cut-normal-{GemType}");
        Assert.Equal(10, Encrustable.FireGrindStageStartEvent(player.Player, gem, 1, 10));

        var seen = new List<GrindStageStartEvent>();
        Action<GrindStageStartEvent> onStart = e => { seen.Add(e); e.Counter -= 25; };
        Mod.Instance.OnGrindStageStart += onStart;
        try
        {
            // Floored at 1.
            Assert.Equal(1, Encrustable.FireGrindStageStartEvent(player.Player, gem, 2, 20));
            GrindStageStartEvent e = Assert.Single(seen);
            Assert.Same(player.Player, e.Player);
            Assert.Same(gem, e.Gem);
            Assert.Equal(2, e.Stage);

            Assert.Equal(15, Encrustable.FireGrindStageStartEvent(player.Player, gem, 3, 40));
            // Without a player the default stands and nothing fires.
            Assert.Equal(20, Encrustable.FireGrindStageStartEvent(null!, gem, 1, 20));
            Assert.Equal(2, seen.Count);
        }
        finally
        {
            Mod.Instance.OnGrindStageStart -= onStart;
        }
    }

    [AtlasScenario]
    public async Task GrindStep_Should_ReturnSubscriberMultiplier()
    {
        ITestPlayer player = await Join();
        ItemStack gem = Stack($"canjewelry:gem-cut-normal-{GemType}");
        Assert.Equal(1f, Encrustable.FireGrindStepEvent(player.Player, gem, 1));

        var seen = new List<GrindEvent>();
        Action<GrindEvent> onGrind = e => { seen.Add(e); e.Multiplier = 1.5f; };
        Mod.Instance.OnGrindStep += onGrind;
        try
        {
            Assert.Equal(1.5f, Encrustable.FireGrindStepEvent(player.Player, gem, 2));
            GrindEvent e = Assert.Single(seen);
            Assert.Same(player.Player, e.Player);
            Assert.Same(gem, e.Gem);
            Assert.Equal(2, e.Stage);
            Assert.Equal(1f, Encrustable.FireGrindStepEvent(null!, gem, 2));
            Assert.Single(seen);
        }
        finally
        {
            Mod.Instance.OnGrindStep -= onGrind;
        }
    }

    // --- panning -----------------------------------------------------------------------------

    private const string OpenMaterial = "gravel-granite";
    private const string GatedMaterial = "gravel-andesite";
    private const string Perk = "cantest.perk";
    private static readonly string PanGem = $"canjewelry:gem-rough-normal-{GemType}";

    private CANBlockPan PanBlock() => (CANBlockPan)World.Api.World.GetBlock(new AssetLocation("canjewelry:canpan-steel"));

    /// <summary>Two pan materials of our own: one open, one behind a perk. Both always drop.</summary>
    private void RegisterTestPanDrops()
    {
        CANPanningDrop Sure(string? perk) => new CANPanningDrop
        {
            Code = new AssetLocation(PanGem),
            Type = EnumItemClass.Item,
            StackSize = 1,
            Chance = new NatFloat(1f, 0f, EnumDistribution.UNIFORM),
            requiresPerk = perk
        };
        var extras = Mod.Instance.runtimeExtraPanDrops;
        AfterRestore(() =>
        {
            extras.Remove(OpenMaterial);
            extras.Remove(GatedMaterial);
            ModInternals.OnConfigReplaced();
        });
        Mod.Instance.RegisterPanDrops(World.Api.World, OpenMaterial, new[] { Sure(null) });
        Mod.Instance.RegisterPanDrops(World.Api.World, GatedMaterial, new[] { Sure(Perk) });
        ModInternals.OnConfigReplaced();
    }

    /// <summary>The pan in the active hotbar slot and the material right after it.</summary>
    private (ItemSlot pan, ItemSlot material) LoadHotbar(ITestPlayer player, string material, int amount)
    {
        EmptyPlayerInventory(player);
        player.Player.InventoryManager.ActiveHotbarSlotNumber = 0;
        IInventory hotbar = player.Player.InventoryManager.GetHotbarInventory();
        Block block = World.Api.World.GetBlock(new AssetLocation("game:" + material));
        Assert.True(block != null, "no block game:" + material);
        hotbar[0].Itemstack = new ItemStack(PanBlock());
        hotbar[1].Itemstack = new ItemStack(block, amount);
        return (hotbar[0], hotbar[1]);
    }

    [AtlasScenario]
    public async Task PanTakeEvent_Should_SetHowMuchIsTaken()
    {
        ITestPlayer player = await Join();
        RegisterTestPanDrops();
        SetConfig(c => c.pan_take_per_use, (c, v) => c.pan_take_per_use = v, 8);
        var seen = new List<PanTakeEvent>();
        Action<PanTakeEvent> onTake = e => { seen.Add(e); e.Amount = 3; };
        Mod.Instance.OnPanMaterialTake += onTake;
        try
        {
            var (pan, material) = LoadHotbar(player, OpenMaterial, 8);
            ModInternals.PanTakeMaterial(PanBlock(), pan, player.Entity);

            PanTakeEvent e = Assert.Single(seen);
            Assert.Same(player.Player, e.Player);
            Assert.Equal(OpenMaterial, e.MaterialCode);
            Assert.Equal(5, material.StackSize);
            Assert.Equal(OpenMaterial, PanBlock().GetBlockMaterialCode(pan.Itemstack));

            // Floored at one.
            Mod.Instance.OnPanMaterialTake -= onTake;
            onTake = ev => ev.Amount = 0;
            Mod.Instance.OnPanMaterialTake += onTake;
            (pan, material) = LoadHotbar(player, OpenMaterial, 8);
            ModInternals.PanTakeMaterial(PanBlock(), pan, player.Entity);
            Assert.Equal(7, material.StackSize);
        }
        finally
        {
            Mod.Instance.OnPanMaterialTake -= onTake;
            EmptyPlayerInventory(player);
        }
    }

    [AtlasScenario]
    public async Task PanEvent_Should_LetSubscriberChangeTheDrop()
    {
        ITestPlayer player = await Join();
        RegisterTestPanDrops();
        EmptyPlayerInventory(player);

        ModInternals.PanCreateDrop(PanBlock(), player.Entity, OpenMaterial);
        Assert.Equal(1, CountInPlayer(player, PanGem));

        var seen = new List<PanEvent>();
        Action<PanEvent> onPan = e => { seen.Add(e); e.Drop.StackSize = 2; };
        Mod.Instance.OnPanGem += onPan;
        try
        {
            ModInternals.PanCreateDrop(PanBlock(), player.Entity, OpenMaterial);

            PanEvent e = Assert.Single(seen);
            Assert.Same(player.Player, e.Player);
            Assert.Equal(OpenMaterial, e.SourceMaterial);
            Assert.Equal(PanGem, e.Drop.Collectible.Code.ToString());
            Assert.Equal(3, CountInPlayer(player, PanGem));

            // A subscriber that takes the drop away leaves the player without one.
            Mod.Instance.OnPanGem -= onPan;
            onPan = ev => ev.Drop = null!;
            Mod.Instance.OnPanGem += onPan;
            ModInternals.PanCreateDrop(PanBlock(), player.Entity, OpenMaterial);
            Assert.Equal(3, CountInPlayer(player, PanGem));
        }
        finally
        {
            Mod.Instance.OnPanGem -= onPan;
            EmptyPlayerInventory(player);
        }
    }

    // A drop behind a perk is only there when a subscriber allows it.
    [AtlasScenario]
    public async Task CanPanDrop_Should_GatePerkDrops()
    {
        ITestPlayer player = await Join();
        RegisterTestPanDrops();
        SetConfig(c => c.pan_take_per_use, (c, v) => c.pan_take_per_use = v, 8);

        // Nobody allows it: the material is not taken and nothing drops.
        var (pan, material) = LoadHotbar(player, GatedMaterial, 8);
        ModInternals.PanTakeMaterial(PanBlock(), pan, player.Entity);
        Assert.Equal(8, material.StackSize);
        Assert.Null(PanBlock().GetBlockMaterialCode(pan.Itemstack));
        EmptyPlayerInventory(player);
        ModInternals.PanCreateDrop(PanBlock(), player.Entity, GatedMaterial);
        Assert.Equal(0, CountInPlayer(player, PanGem));

        var perks = new List<string>();
        Action<CanPanDropEvent> onGate = e => { perks.Add(e.PerkCode); Assert.Same(player.Player, e.Player); e.Allowed = e.PerkCode == Perk; };
        Mod.Instance.OnCanPanDrop += onGate;
        try
        {
            (pan, material) = LoadHotbar(player, GatedMaterial, 8);
            ModInternals.PanTakeMaterial(PanBlock(), pan, player.Entity);
            Assert.Equal(0, material.StackSize);
            Assert.Equal(GatedMaterial, PanBlock().GetBlockMaterialCode(pan.Itemstack));

            EmptyPlayerInventory(player);
            ModInternals.PanCreateDrop(PanBlock(), player.Entity, GatedMaterial);
            Assert.Equal(1, CountInPlayer(player, PanGem));
            Assert.All(perks, p => Assert.Equal(Perk, p));
        }
        finally
        {
            Mod.Instance.OnCanPanDrop -= onGate;
            EmptyPlayerInventory(player);
        }
    }

    // --- inscription --------------------------------------------------------------------------

    private static byte[] InscriptionPacket(string text)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms)) writer.Write(text);
        return ms.ToArray();
    }

    [AtlasScenario]
    public async Task CanInscribe_Should_GateTheInscription()
    {
        ITestPlayer player = await Join();
        Block setBlock = World.Api.World.SearchBlocks(new AssetLocation("canjewelry:jewelerset-*")).First();
        BlockPos pos = World.Spawn.UpCopy(3);
        World.SetBlock(setBlock.Code.ToString(), pos);
        await World.Ticks(1);
        JewelerSetBE be = World.BlockEntityAt<JewelerSetBE>(pos)!;
        Assert.NotNull(be);

        var seen = new List<CanInscribeEvent>();
        Action<CanInscribeEvent> deny = e => { seen.Add(e); e.Allowed = false; };
        try
        {
            be.inventory[0].Itemstack = Stack(JewelryCode);

            // The set has to be open for the player first: no event, no text.
            Mod.Instance.OnCanInscribe += deny;
            be.OnReceivedClientPacket(player.Player, 1007, InscriptionPacket("Hello"));
            Assert.Empty(seen);

            player.Player.InventoryManager.OpenInventory(be.Inventory);
            be.OnReceivedClientPacket(player.Player, 1007, InscriptionPacket("Hello"));
            CanInscribeEvent e = Assert.Single(seen);
            Assert.Same(player.Player, e.Player);
            Assert.Same(be.inventory[0].Itemstack, e.Jewelry);
            Assert.False(be.inventory[0].Itemstack.Attributes.HasAttribute(CANJWConstants.INSCRIPTION));

            // Nobody objects: the text is written.
            Mod.Instance.OnCanInscribe -= deny;
            be.OnReceivedClientPacket(player.Player, 1007, InscriptionPacket("Hello"));
            Assert.Equal("Hello", be.inventory[0].Itemstack.Attributes.GetString(CANJWConstants.INSCRIPTION));
        }
        finally
        {
            Mod.Instance.OnCanInscribe -= deny;
            player.Player.InventoryManager.CloseInventory(be.Inventory);
            be.inventory[0].Itemstack = null;
            World.SetBlock("game:air", pos);
        }
    }
}
