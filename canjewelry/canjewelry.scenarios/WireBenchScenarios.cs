using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src.be;
using canjewelry.src.integration;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// The wire drawing bench: a strap goes onto the feet, a squeeze on the head turns it into wire
/// hanks after three seconds, and the two halves come and go together.
/// </summary>
public class WireBenchScenarios : WorkstationScenarioBase
{
    private const string Head = "canjewelry:canwiredrawingbench-head-north";
    private const string Feet = "canjewelry:canwiredrawingbench-feet-north";
    private const string Strap = "canjewelry:canstrap-copper";
    private const string Hank = "canjewelry:canwirehank-copper";

    // A north bench looks for its head one block south of the feet.
    private async Task<(ITestPlayer player, CANBEWireDrawingBench bench, BlockPos head, BlockPos feet)> Bench()
    {
        ITestPlayer player = await Join();
        BlockPos feet = await Site(player);
        BlockPos head = feet.SouthCopy();
        var bench = Place<CANBEWireDrawingBench>(Head, head);
        World.SetBlock(Feet, feet);
        return (player, bench, head, feet);
    }

    private static void PutStrap(CANBEWireDrawingBench bench, ItemStack strap)
    {
        bench.inventory[0].Itemstack = strap;
        bench.inventory[0].MarkDirty();
    }

    private static void Squeeze(CANBEWireDrawingBench bench, ITestPlayer player) => Call(bench, "StartSqueeze", player.Player);

    private Task Drawn(CANBEWireDrawingBench bench) => World.Until(() => bench.resultReady, "the wire is drawn", 400);

    [AtlasScenario]
    public async Task Squeeze_Should_TurnStrapIntoConfiguredHanks()
    {
        SetConfig(c => c.wirehank_per_strap, (c, v) => c.wirehank_per_strap = v, 5);
        var (player, bench, _, _) = await Bench();
        PutStrap(bench, Stack(Strap));

        Squeeze(bench, player);
        Assert.NotEqual(0L, bench.listenerId);
        await Drawn(bench);

        Assert.Equal(0L, bench.listenerId);
        Assert.Equal(Hank, bench.inventory[0].Itemstack.Collectible.Code.ToString());
        Assert.Equal(5, bench.inventory[0].StackSize);
    }

    [AtlasScenario]
    public async Task Squeeze_Should_BeIgnored_WhileRunningOrWhenReady()
    {
        var (player, bench, _, _) = await Bench();
        PutStrap(bench, Stack(Strap));

        Squeeze(bench, player);
        long listener = bench.listenerId;
        Squeeze(bench, player);
        Assert.Equal(listener, bench.listenerId);
        await Drawn(bench);

        int hanks = bench.inventory[0].StackSize;
        Squeeze(bench, player);
        Assert.Equal(0L, bench.listenerId);
        await World.Ticks(5);
        Assert.Equal(hanks, bench.inventory[0].StackSize);
    }

    [AtlasScenario]
    public async Task WireDrawEvent_Should_SeeSqueezerAndStrap_AndChangeOutput()
    {
        SetConfig(c => c.wirehank_per_strap, (c, v) => c.wirehank_per_strap = v, 4);
        var (player, bench, _, _) = await Bench();
        PutStrap(bench, Stack(Strap));
        WireDrawEvent? seen = null;
        string? inputCode = null;
        Action<WireDrawEvent> handler = e =>
        {
            seen = e;
            inputCode = e.Input?.Collectible.Code.ToString();
            e.Output.StackSize += 3;
        };
        Mod.Instance.OnWireDrawn += handler;
        try
        {
            Squeeze(bench, player);
            await Drawn(bench);
        }
        finally
        {
            Mod.Instance.OnWireDrawn -= handler;
        }

        Assert.NotNull(seen);
        Assert.Same(player.Player, seen!.Player);
        Assert.Equal(Strap, inputCode);
        Assert.Equal(7, bench.inventory[0].StackSize);
    }

    // Feet take one strap, the head starts the squeeze, feet give the result back.
    [AtlasScenario]
    public async Task Interaction_Should_LoadSqueezeAndUnloadBench()
    {
        SetConfig(c => c.wirehank_per_strap, (c, v) => c.wirehank_per_strap = v, 4);
        var (player, bench, head, feet) = await Bench();
        Block feetBlock = World.BlockAt(feet);
        Block headBlock = World.BlockAt(head);

        // Nothing to draw yet.
        Hold(player, null);
        Assert.False(headBlock.OnBlockInteractStart(World.Api.World, player.Player, Sel(head)));

        Hold(player, Stack(Strap, 3));
        Assert.True(feetBlock.OnBlockInteractStart(World.Api.World, player.Player, Sel(feet)));
        Assert.Equal(2, Hand(player).StackSize);
        Assert.Equal(1, bench.inventory[0].StackSize);

        Hold(player, null);
        Assert.True(headBlock.OnBlockInteractStart(World.Api.World, player.Player, Sel(head)));
        await Drawn(bench);

        Assert.True(feetBlock.OnBlockInteractStart(World.Api.World, player.Player, Sel(feet)));
        Assert.True(bench.inventory[0].Empty);
        Assert.False(bench.resultReady);
        Assert.Equal(Hank, Hand(player).Itemstack.Collectible.Code.ToString());
        Assert.Equal(4, Hand(player).StackSize);
    }

    [AtlasScenario]
    public async Task FeetClickWithStrap_Should_TakeLoadedStrapBack()
    {
        var (player, bench, _, feet) = await Bench();
        Block feetBlock = World.BlockAt(feet);
        Hold(player, Stack(Strap, 2));
        feetBlock.OnBlockInteractStart(World.Api.World, player.Player, Sel(feet));
        Assert.Equal(1, Hand(player).StackSize);

        Assert.True(feetBlock.OnBlockInteractStart(World.Api.World, player.Player, Sel(feet)));

        Assert.True(bench.inventory[0].Empty);
        Assert.Equal(2, Hand(player).StackSize);
        Assert.Equal(0L, bench.listenerId);
    }

    [AtlasScenario]
    public async Task TreeAttributes_Should_RoundTrip_InventoryResultAndWood()
    {
        var (_, bench, _, _) = await Bench();
        bench.woodType = "birch";
        bench.resultReady = true;
        PutStrap(bench, Stack(Hank, 4));

        var tree = new TreeAttribute();
        bench.ToTreeAttributes(tree);
        bench.woodType = null;
        bench.resultReady = false;
        bench.inventory[0].Itemstack = null;
        bench.FromTreeAttributes(tree, World.Api.World);

        Assert.Equal("birch", bench.woodType);
        Assert.True(bench.resultReady);
        Assert.Equal(Hank, bench.inventory[0].Itemstack.Collectible.Code.ToString());
        Assert.Equal(4, bench.inventory[0].StackSize);
    }

    [AtlasScenario]
    public async Task Drops_Should_BeTheHeadNorthItem_WithWood_FromEitherHalf()
    {
        var (_, bench, head, feet) = await Bench();
        bench.woodType = "birch";

        foreach (BlockPos pos in new[] { head, feet })
        {
            ItemStack drop = Assert.Single(World.BlockAt(pos).GetDrops(World.Api.World, pos, null));
            Assert.Equal(Head, drop.Collectible.Code.ToString());
            Assert.Equal("birch", drop.Attributes.GetString("type"));
        }
    }

    [AtlasScenario]
    public async Task BreakingEitherHalf_Should_RemoveTheOther()
    {
        foreach (bool breakHead in new[] { true, false })
        {
            var (_, _, head, feet) = await Bench();

            World.Api.World.BlockAccessor.BreakBlock(breakHead ? head : feet, null);

            Assert.Equal(0, World.BlockAt(head).BlockId);
            Assert.Equal(0, World.BlockAt(feet).BlockId);
        }
    }
}
