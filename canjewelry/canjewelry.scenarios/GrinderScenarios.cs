using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.integration;
using canjewelry.src.jewelry;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// The jewel grinder: two grinding stages per cut gem, each on its own grind layer, raising the
/// buffs by the cut's multipliers and marking the gem fully processed at the end.
/// </summary>
public class GrinderScenarios : WorkstationScenarioBase
{
    private const string GrinderCode = "canjewelry:jewelgrinder";
    private const string RoughLayer = "canjewelry:grindlayeritem-rough";
    private const string FineLayer = "canjewelry:grindlayeritem-fine";
    private const string CutGem = "canjewelry:gem-cut-normal-" + GemType;

    private float clock;

    private async Task<(ITestPlayer player, BEJewelGrinder grinder, BlockPos pos)> Grinder(string layer = RoughLayer)
    {
        // No mechanical power in a test world: a speed of 0 has to be enough.
        SetConfig(c => c.minGrinderProcessingSpeed, (c, v) => c.minGrinderProcessingSpeed = v, 0f);
        SetConfig(c => c.grinderStage1Counter, (c, v) => c.grinderStage1Counter = v, 3);
        SetConfig(c => c.grinderStage2Counter, (c, v) => c.grinderStage2Counter = v, 2);
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var grinder = Place<BEJewelGrinder>(GrinderCode, pos);
        grinder.InputStack = Stack(layer);
        grinder.SetPlayerGrinding(player.Player, true);
        clock = 0;
        return (player, grinder, pos);
    }

    /// <summary>One grinding second as the block's interaction step reports it.</summary>
    private void Grind(BEJewelGrinder grinder, ITestPlayer player)
    {
        clock += 1;
        grinder.doGrind(player.Player, clock);
    }

    private ItemStack FreshCutGem(float value = 0.03f, int size = 1)
    {
        ItemStack gem = LegacyCutGem("normal", "walkspeed", value);
        gem.StackSize = size;
        return gem;
    }

    private static ITreeAttribute? GrindInfo(ItemStack gem) => gem.Attributes.GetTreeAttribute("cangrindlayerinfo");

    private static float BuffValue(ItemStack gem) =>
        (gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)!.value[0];

    private static bool FullyProcessed(ItemStack gem) =>
        gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE).GetBool(CANJWConstants.GEM_FULL_PROCESSED);

    private static float RoundMult(int stage) =>
        Mod.config.CuttingAttributesDict[CANJWConstants.CUTTING_ROUND].GrindingBuffIncreaseMultipliers[stage];

    [AtlasScenario]
    public async Task FirstGrind_Should_StartStageOne_WithConfiguredCounter()
    {
        var (player, grinder, _) = await Grinder();
        Hold(player, FreshCutGem());
        int durability = Durability(grinder.InputStack);

        Grind(grinder, player);

        ITreeAttribute? info = GrindInfo(Hand(player).Itemstack);
        Assert.NotNull(info);
        Assert.Equal(1, info!.GetInt("grindtype"));
        Assert.Equal(3, info.GetInt("grindcounter"));
        Assert.Equal(durability, Durability(grinder.InputStack));
        Assert.Equal(0.03f, BuffValue(Hand(player).Itemstack), 5);
    }

    [AtlasScenario]
    public async Task FullGrind_Should_ApplyStageMultipliers_AndMarkGemProcessed()
    {
        var (player, grinder, _) = await Grinder();
        Hold(player, FreshCutGem(0.03f));
        int roughDurability = Durability(grinder.InputStack);

        Grind(grinder, player); // starts stage 1
        Grind(grinder, player);
        Grind(grinder, player);
        Assert.Equal(1, GrindInfo(Hand(player).Itemstack)!.GetInt("grindcounter"));
        Assert.Equal(0.03f, BuffValue(Hand(player).Itemstack), 5);

        Grind(grinder, player); // ends stage 1
        ItemStack gem = Hand(player).Itemstack;
        Assert.Equal(0.03f * RoundMult(1), BuffValue(gem), 5);
        Assert.Equal(2, GrindInfo(gem)!.GetInt("grindtype"));
        Assert.Equal(2, GrindInfo(gem)!.GetInt("grindcounter"));
        Assert.Equal(roughDurability - 3, Durability(grinder.InputStack));

        // Stage two wants the fine layer: the rough one does nothing, not even wear.
        Grind(grinder, player);
        Assert.Equal(2, GrindInfo(Hand(player).Itemstack)!.GetInt("grindcounter"));
        Assert.Equal(roughDurability - 3, Durability(grinder.InputStack));

        grinder.InputStack = Stack(FineLayer);
        int fineDurability = Durability(grinder.InputStack);
        Grind(grinder, player);
        Grind(grinder, player); // ends stage 2

        gem = Hand(player).Itemstack;
        Assert.Null(GrindInfo(gem));
        Assert.True(FullyProcessed(gem));
        Assert.Equal(0.03f * RoundMult(1) * RoundMult(2), BuffValue(gem), 5);
        Assert.Equal(fineDurability - 2, Durability(grinder.InputStack));

        // A processed gem is done for good.
        Grind(grinder, player);
        Assert.Null(GrindInfo(Hand(player).Itemstack));
        Assert.Equal(0.03f * RoundMult(1) * RoundMult(2), BuffValue(Hand(player).Itemstack), 5);
    }

    [AtlasScenario]
    public async Task FineLayer_Should_NotStartStageOne()
    {
        var (player, grinder, _) = await Grinder(FineLayer);
        Hold(player, FreshCutGem());
        int durability = Durability(grinder.InputStack);

        for (int i = 0; i < 5; i++) Grind(grinder, player);

        Assert.Equal(1, GrindInfo(Hand(player).Itemstack)!.GetInt("grindtype"));
        Assert.Equal(3, GrindInfo(Hand(player).Itemstack)!.GetInt("grindcounter"));
        Assert.Equal(durability, Durability(grinder.InputStack));
    }

    [AtlasScenario]
    public async Task StackOfGems_Should_BeRefused()
    {
        var (player, grinder, _) = await Grinder();
        Hold(player, FreshCutGem(size: 2));

        for (int i = 0; i < 3; i++) Grind(grinder, player);

        Assert.Null(GrindInfo(Hand(player).Itemstack));
        Assert.Equal(2, Hand(player).StackSize);
    }

    [AtlasScenario]
    public async Task Grind_Should_DoNothing_ForUnfitGemsOrConditions()
    {
        var (player, grinder, _) = await Grinder();

        // Already processed, no cut, or not a cut gem at all.
        ItemStack processed = FreshCutGem();
        processed.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE).SetBool(CANJWConstants.GEM_FULL_PROCESSED, true);
        ItemStack uncut = FreshCutGem();
        uncut.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE).RemoveAttribute(CANJWConstants.CUTTING_TYPE);
        ItemStack noTree = Stack(CutGem);
        foreach (ItemStack gem in new[] { processed, uncut, noTree })
        {
            Hold(player, gem);
            Grind(grinder, player);
            Assert.Null(GrindInfo(Hand(player).Itemstack));
        }

        // Steps less than a second apart.
        Hold(player, FreshCutGem());
        Grind(grinder, player);
        grinder.doGrind(player.Player, clock + 0.5f);
        Assert.Equal(3, GrindInfo(Hand(player).Itemstack)!.GetInt("grindcounter"));

        // A player who is not at the grinder.
        grinder.SetPlayerGrinding(player.Player, false);
        Grind(grinder, player);
        Assert.Equal(3, GrindInfo(Hand(player).Itemstack)!.GetInt("grindcounter"));

        // Too slow: without power the speed is 0, below the default minimum.
        grinder.SetPlayerGrinding(player.Player, true);
        clock = 0;
        SetConfig(c => c.minGrinderProcessingSpeed, (c, v) => c.minGrinderProcessingSpeed = v, 0.3f);
        Grind(grinder, player);
        Assert.Equal(3, GrindInfo(Hand(player).Itemstack)!.GetInt("grindcounter"));
    }

    [AtlasScenario]
    public async Task GrindEvents_Should_SetStageLength_AndMultiplyEachStage()
    {
        var (player, grinder, _) = await Grinder();
        Hold(player, FreshCutGem(0.03f));
        var starts = new List<(int stage, int counter)>();
        var steps = new List<int>();
        Action<GrindStageStartEvent> onStart = e => { starts.Add((e.Stage, e.Counter)); e.Counter = 0; };
        Action<GrindEvent> onStep = e => { steps.Add(e.Stage); e.Multiplier = 2f; };
        Mod.Instance.OnGrindStageStart += onStart;
        Mod.Instance.OnGrindStep += onStep;
        try
        {
            Grind(grinder, player);
            // A counter of 0 is floored at 1: the next matching grind ends the stage.
            Assert.Equal(1, GrindInfo(Hand(player).Itemstack)!.GetInt("grindcounter"));
            Grind(grinder, player);
            grinder.InputStack = Stack(FineLayer);
            Grind(grinder, player);
        }
        finally
        {
            Mod.Instance.OnGrindStageStart -= onStart;
            Mod.Instance.OnGrindStep -= onStep;
        }

        Assert.Equal(new[] { (1, 3), (2, 2) }, starts);
        Assert.Equal(new[] { 1, 2 }, steps);
        Assert.True(FullyProcessed(Hand(player).Itemstack));
        Assert.Equal(0.03f * RoundMult(1) * 2 * RoundMult(2) * 2, BuffValue(Hand(player).Itemstack), 5);
    }

    [AtlasScenario]
    public async Task WornOutLayer_Should_BreakOutOfGrinder()
    {
        var (player, grinder, _) = await Grinder();
        ItemStack layer = grinder.InputStack;
        layer.Collectible.SetDurability(layer, 1);
        Hold(player, FreshCutGem());

        Grind(grinder, player);
        Grind(grinder, player);

        Assert.True(grinder.InputSlot.Empty);
        Assert.Equal(2, GrindInfo(Hand(player).Itemstack)!.GetInt("grindcounter"));
    }

    // Ctrl + right click swaps the layer in hand with the one in the grinder; other items stay put.
    [AtlasScenario]
    public async Task CtrlClick_Should_SwapGrindLayer_ButNothingElse()
    {
        var (player, grinder, pos) = await Grinder();
        grinder.InputStack = null;
        Block block = World.BlockAt(pos);
        player.Entity.ServerControls.CtrlKey = true;
        try
        {
            Hold(player, FreshCutGem());
            block.OnBlockInteractStart(World.Api.World, player.Player, Sel(pos));
            Assert.True(grinder.InputSlot.Empty);
            Assert.False(Hand(player).Empty);

            Hold(player, Stack(FineLayer));
            Assert.True(block.OnBlockInteractStart(World.Api.World, player.Player, Sel(pos)));
            Assert.Equal(FineLayer, grinder.InputStack.Collectible.Code.ToString());
            Assert.True(Hand(player).Empty);

            Assert.True(block.OnBlockInteractStart(World.Api.World, player.Player, Sel(pos)));
            Assert.True(grinder.InputSlot.Empty);
            Assert.Equal(FineLayer, Hand(player).Itemstack.Collectible.Code.ToString());
        }
        finally
        {
            player.Entity.ServerControls.CtrlKey = false;
        }
    }

    [AtlasScenario]
    public async Task TreeAttributes_Should_RoundTripLayer()
    {
        var (_, grinder, _) = await Grinder(FineLayer);
        grinder.InputStack.Collectible.SetDurability(grinder.InputStack, 42);

        var tree = new TreeAttribute();
        grinder.ToTreeAttributes(tree);
        grinder.InputStack = null;
        grinder.FromTreeAttributes(tree, World.Api.World);

        Assert.Equal(FineLayer, grinder.InputStack.Collectible.Code.ToString());
        Assert.Equal(42, Durability(grinder.InputStack));
    }
}
