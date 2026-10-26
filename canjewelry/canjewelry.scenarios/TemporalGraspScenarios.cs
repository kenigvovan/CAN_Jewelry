using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using Vintagestory.API.Common;
using GraspBB = global::canjewelry.src.bb.CANTemporalGraspBB;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// Temporal grasp: a digging tool with the buff has that chance to drop the block it broke
/// instead of the block's usual drops, on the blocks the config lists.
/// </summary>
public class TemporalGraspScenarios : BuffScenarioBase
{
    private const string Pickaxe = "game:pickaxe-steel";
    // Lists in the default config through "game:ore-poor-*"; drops nuggets on its own.
    private const string Ore = "game:ore-poor-nativecopper-granite";

    private Block GetBlock(string code)
    {
        Block block = World.Api.World.GetBlock(new AssetLocation(code));
        Assert.True(block != null && block.Id != 0, "no block " + code);
        return block!;
    }

    private ItemStack GraspTool(string code, float grasp) => Socketed(code, (CANJWConstants.TEMPORALGRASP, grasp));

    private ItemStack[] DropsFor(ITestPlayer player, Block block) =>
        block.GetDrops(World.Api.World, player.Position, player.Player) ?? Array.Empty<ItemStack>();

    // By id and class: the ore's usual drop is an item under the very same code.
    private static bool IsBlock(ItemStack stack, Block block) => stack.Class == EnumItemClass.Block && stack.Id == block.Id;

    private static bool DropsItself(ItemStack[] drops, Block block) => drops.Any(s => IsBlock(s, block));

    [AtlasScenario]
    public async Task GraspValue_Should_SumGraspAcrossSockets()
    {
        ItemStack pick = Socketed(Pickaxe, (CANJWConstants.TEMPORALGRASP, 0.1f), ("walkspeed", 0.05f));
        SetSocket(pick, 2, new[] { "walkspeed", CANJWConstants.TEMPORALGRASP }, new[] { 0.05f, 0.2f });
        var encrustable = pick.Item.GetBehavior<Encrustable>();

        Assert.Equal(0.3f, encrustable.GetTemporalGraspValue(pick), 4);
        Assert.Equal(0f, encrustable.GetTemporalGraspValue(Stack(Pickaxe)));
        await World.Ticks(1);
    }

    // Sockets past what the item can take are not read.
    [AtlasScenario]
    public async Task GraspValue_Should_IgnoreSocketsPastMaximum()
    {
        ItemStack pick = GraspTool(Pickaxe, 0.1f);
        int max = Encrustable.GetMaxAmountSockets(pick);
        SetSocket(pick, max, new[] { CANJWConstants.TEMPORALGRASP }, new[] { 0.5f });

        Assert.Equal(0.1f, pick.Item.GetBehavior<Encrustable>().GetTemporalGraspValue(pick), 4);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ListedBlocks_Should_CarryGraspBehavior()
    {
        Assert.True(Mod.config.TemporalGraspEnabled);
        Assert.Contains(GetBlock(Ore).BlockBehaviors, b => b is GraspBB);
        Assert.Contains(GetBlock("game:soil-medium-normal").BlockBehaviors, b => b is GraspBB);
        Assert.Single(GetBlock(Ore).BlockBehaviors.OfType<GraspBB>());
        Assert.DoesNotContain(GetBlock("game:cobblestone-granite").BlockBehaviors, b => b is GraspBB);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task FullGrasp_Should_DropTheBlockItself()
    {
        ITestPlayer player = await Join();
        Block ore = GetBlock(Ore);
        Hold(player, GraspTool(Pickaxe, 1f));

        ItemStack[] drops = DropsFor(player, ore);

        ItemStack only = Assert.Single(drops);
        Assert.True(IsBlock(only, ore), "dropped " + only.Collectible.Code);
        Assert.Equal(1, only.StackSize);
    }

    [AtlasScenario]
    public async Task ZeroGrasp_Should_KeepUsualDrops()
    {
        ITestPlayer player = await Join();
        Block ore = GetBlock(Ore);
        Hold(player, GraspTool(Pickaxe, 0f));

        Assert.False(DropsItself(DropsFor(player, ore), ore));
    }

    // Shovels count as well as pickaxes.
    [AtlasScenario]
    public async Task FullGrasp_Should_WorkWithShovel()
    {
        ITestPlayer player = await Join();
        Block soil = GetBlock("game:soil-medium-normal");
        Hold(player, GraspTool("game:shovel-steel", 1f));

        ItemStack only = Assert.Single(DropsFor(player, soil));
        Assert.True(IsBlock(only, soil), "dropped " + only.Collectible.Code);
    }

    // Only digging tools grasp; a knife with the buff breaks the ore as usual.
    [AtlasScenario]
    public async Task NonDiggingTool_Should_NotGrasp()
    {
        ITestPlayer player = await Join();
        Block ore = GetBlock(Ore);
        Hold(player, GraspTool(JewelryCode, 1f));

        Assert.False(DropsItself(DropsFor(player, ore), ore));
    }

    [AtlasScenario]
    public async Task EmptyHand_Should_KeepUsualDrops()
    {
        ITestPlayer player = await Join();
        Block ore = GetBlock(Ore);
        Hold(player, null);

        Assert.False(DropsItself(DropsFor(player, ore), ore));
        Assert.False(DropsItself(ore.GetDrops(World.Api.World, player.Position, null) ?? Array.Empty<ItemStack>(), ore));
    }

    // Grasp on the tool is not a player stat.
    [AtlasScenario]
    public async Task HeldGraspTool_Should_NotTouchStats()
    {
        ITestPlayer player = await JoinReady();
        Hold(player, Socketed(Pickaxe, (CANJWConstants.TEMPORALGRASP, 0.5f), ("miningSpeedMul", 0.1f)));

        await UntilStat(player, "miningSpeedMul", 0.1f);
        AssertStat(player, CANJWConstants.TEMPORALGRASP, null);
    }
}
