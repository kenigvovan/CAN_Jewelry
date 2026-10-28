using System.IO;
using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.be;
using canjewelry.src.integration;
using canjewelry.src.jewelry;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using CuttableCB = global::canjewelry.src.cb.CANGemCuttableCB;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// The gem cutting table on a real server: putting rough gems on it, choosing a recipe, chisel
/// strikes, finishing a cut and what comes back when the work is abandoned.
/// </summary>
public class GemCuttingTableScenarios : WorkstationScenarioBase
{
    private const string TableCode = "canjewelry:cangemcuttingtable";
    private const string WorkItemCode = "canjewelry:gemcuttingworkitem";
    private const string Rough = "canjewelry:gem-rough-chipped-" + GemType;
    private const string CutGem = "canjewelry:gem-cut-normal-" + GemType;
    private const string Chisel = "canjewelry:cangemchisel-copper";
    private const string Hammer = "game:hammer-copper";

    // ---- helpers -----------------------------------------------------------------------------

    private static List<GemCuttingRecipe> Matching(ItemStack stack) =>
        stack.Collectible.GetBehavior<CuttableCB>().GetMatchingRecipes(stack);

    private static string? CutOf(GemCuttingRecipe recipe) =>
        recipe.Output.ResolvedItemstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)?.GetString(CANJWConstants.CUTTING_TYPE);

    private GemCuttingRecipe RoundRecipe() => Matching(Stack(Rough)).First(r => CutOf(r) == CANJWConstants.CUTTING_ROUND);

    private bool Put(BlockEntityGemCuttingTable table, ITestPlayer player, BlockPos pos) =>
        (bool)Call(table, "TryPut", World.Api.World, player.Player, Sel(pos))!;

    private static void SelectRecipe(BlockEntityGemCuttingTable table, ITestPlayer player, int recipeId) =>
        table.OnReceivedClientPacket(player.Player, (int)EnumAnvilPacket.SelectRecipe, SerializerUtil.Serialize(recipeId));

    private static void Strike(BlockEntityGemCuttingTable table, ITestPlayer player, Vec3i voxel)
    {
        using var ms = new MemoryStream();
        var writer = new BinaryWriter(ms);
        writer.Write(voxel.X);
        writer.Write(voxel.Y);
        writer.Write(voxel.Z);
        table.OnReceivedClientPacket(player.Player, (int)EnumAnvilPacket.OnUserOver, ms.ToArray());
    }

    /// <summary>A table with a chipped rough gem on it and, unless asked otherwise, the round cut chosen.</summary>
    private async Task<(ITestPlayer player, BlockEntityGemCuttingTable table, BlockPos pos)> TableWithGem(bool selectRound = true)
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        Hold(player, Stack(Rough));
        Assert.True(Put(table, player, pos), "the rough gem was not put on the table");
        if (selectRound) SelectRecipe(table, player, RoundRecipe().RecipeId);
        return (player, table, pos);
    }

    private ItemStack ReadyChisel(ITestPlayer player)
    {
        ItemStack chisel = Stack(Chisel);
        Hold(player, chisel);
        HoldLeft(player, Stack(Hammer));
        return chisel;
    }

    private void Cutting(int perClick = 1, bool perVoxel = true, bool instant = false, string mode = "enabled")
    {
        SetConfig(c => c.cuttingVoxelsPerClick, (c, v) => c.cuttingVoxelsPerClick = v, perClick);
        SetConfig(c => c.cuttingDurabilityPerVoxel, (c, v) => c.cuttingDurabilityPerVoxel = v, perVoxel);
        SetConfig(c => c.cuttingInstantComplete, (c, v) => c.cuttingInstantComplete = v, instant);
        SetConfig(c => c.cuttingSpareRecipeVoxels, (c, v) => c.cuttingSpareRecipeVoxels = v, false);
        SetConfig(c => c.cuttingAccessMode, (c, v) => c.cuttingAccessMode = v, mode);
    }

    private static byte[,,] RecipeGrid(BlockEntityGemCuttingTable table)
    {
        bool[,,] recipe = table.recipeVoxels;
        var grid = new byte[16, 14, 16];
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 14; y++)
                for (int z = 0; z < 16; z++)
                    grid[x, y, z] = (byte)(recipe[x, y, z] ? 1 : 0);
        return grid;
    }

    private static IEnumerable<Vec3i> Cells()
    {
        for (int x = 0; x < 16; x++)
            for (int y = 0; y < 14; y++)
                for (int z = 0; z < 16; z++)
                    yield return new Vec3i(x, y, z);
    }

    private static void SameGrid(byte[,,] expected, byte[,,] actual) =>
        Assert.Equal(BlockEntityGemCuttingTable.serializeVoxels(expected), BlockEntityGemCuttingTable.serializeVoxels(actual));

    private static int CountMetal(byte[,,] grid) => Cells().Count(c => grid[c.X, c.Y, c.Z] != 0);

    /// <summary>A metal voxel the recipe does not want.</summary>
    private static Vec3i SpareVoxel(BlockEntityGemCuttingTable table)
    {
        bool[,,] recipe = table.recipeVoxels;
        return Cells().First(c => table.Voxels[c.X, c.Y, c.Z] == 1 && !recipe[c.X, c.Y, c.Z]);
    }

    private static void AssertTableEmpty(BlockEntityGemCuttingTable table)
    {
        Assert.Null(table.WorkItemStack);
        Assert.Equal(-1, table.SelectedRecipeId);
        Assert.Equal(0, CountMetal(table.Voxels));
    }

    private static (string[] names, float[] values) Buffs(ItemStack cutGem)
    {
        ITreeAttribute tree = cutGem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.NotNull(tree);
        return ((tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)!.value,
                (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)!.value);
    }

    private static float RoundMult(int stage) =>
        Mod.config.CuttingAttributesDict[CANJWConstants.CUTTING_ROUND].GrindingBuffIncreaseMultipliers[stage];

    // ---- putting a gem on the table ----------------------------------------------------------

    [AtlasScenario]
    public async Task PutRoughGem_Should_StartWorkItem_WithGemTypeAndSize()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        Hold(player, Stack(Rough, 2));
        int recipes = Matching(Stack(Rough)).Count;

        Assert.True(Put(table, player, pos));

        Assert.Equal(WorkItemCode, table.WorkItemStack.Collectible.Code.ToString());
        Assert.Equal(GemType, table.WorkItemStack.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        Assert.Equal("chipped", table.WorkItemStack.Attributes.GetString(CANJWConstants.ENCRUSTED_GEM_SIZE));
        Assert.Equal(1, Hand(player).StackSize);
        // The rough gem's core block is always there, whatever the random edges did.
        for (int x = 3; x < 12; x++)
            for (int y = 0; y < 6; y++)
                for (int z = 3; z < 12; z++)
                    Assert.Equal(1, table.Voxels[x, y, z]);
        // Several cuts fit, so the server waits for the client to pick one.
        Assert.True(recipes > 1, "expected round, pear and baguette for a chipped gem");
        Assert.Equal(-1, table.SelectedRecipeId);
    }

    [AtlasScenario]
    public async Task PutRoughGem_WithOneRecipe_Should_SelectItRightAway()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        int roundId = RoundRecipe().RecipeId;
        var all = Mod.gemCuttingRecipes.ToList();
        try
        {
            Mod.gemCuttingRecipes.RemoveAll(r => CutOf(r) != CANJWConstants.CUTTING_ROUND);
            Hold(player, Stack(Rough));

            Assert.True(Put(table, player, pos));
            Assert.Equal(roundId, table.SelectedRecipeId);
        }
        finally
        {
            Mod.gemCuttingRecipes.Clear();
            Mod.gemCuttingRecipes.AddRange(all);
        }
    }

    [AtlasScenario]
    public async Task PutGemWithoutRecipe_Should_BeRefused_AndLeaveGridEmpty()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        ItemStack vanilla = Stack("game:gem-diamond-rough", 3);
        vanilla.Attributes.SetString("potential", "low");
        Assert.True(vanilla.Collectible.HasBehavior<CuttableCB>(), "vanilla rough gems are not cuttable");
        if (Matching(vanilla).Count > 0)
        {
            // A content mod added a recipe for it: nothing to refuse.
            return;
        }
        Hold(player, vanilla);

        Assert.False(Put(table, player, pos));
        Assert.Null(table.WorkItemStack);
        Assert.Equal(0, CountMetal(table.Voxels));
        Assert.Equal(3, Hand(player).StackSize);
    }

    [AtlasScenario]
    public async Task PlaceOn_Should_TakeSize_FromPotential_OrFromQualityVariant()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);

        foreach (var (potential, quality) in new[] { ("low", "chipped"), ("medium", "flawed"), ("high", "normal") })
        {
            ItemStack vanilla = Stack("game:gem-diamond-rough");
            vanilla.Attributes.SetString("potential", potential);
            ItemStack workItem = vanilla.Collectible.GetBehavior<CuttableCB>().TryPlaceOn(vanilla, table);

            Assert.Equal(WorkItemCode, workItem.Collectible.Code.ToString());
            Assert.Equal("diamond", workItem.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
            Assert.Equal(quality, workItem.Attributes.GetString(CANJWConstants.ENCRUSTED_GEM_SIZE));
        }

        ItemStack own = Stack(Rough);
        Assert.Equal("chipped", own.Collectible.GetBehavior<CuttableCB>().TryPlaceOn(own, table)
            .Attributes.GetString(CANJWConstants.ENCRUSTED_GEM_SIZE));
        own.Attributes.SetString("potential", "high");
        Assert.Equal("normal", own.Collectible.GetBehavior<CuttableCB>().TryPlaceOn(own, table)
            .Attributes.GetString(CANJWConstants.ENCRUSTED_GEM_SIZE));
    }

    [AtlasScenario]
    public async Task SecondGem_Should_BeRefused_WhileWorkItemIsOnTable()
    {
        var (player, table, pos) = await TableWithGem(selectRound: false);
        Hold(player, Stack(Rough));

        Assert.False(Put(table, player, pos));
        Assert.Equal(1, Hand(player).StackSize);
    }

    // ---- recipe choice and giving work back --------------------------------------------------

    [AtlasScenario]
    public async Task SelectRecipePacket_Should_SetRecipe()
    {
        var (player, table, _) = await TableWithGem(selectRound: false);
        GemCuttingRecipe round = RoundRecipe();

        SelectRecipe(table, player, round.RecipeId);

        Assert.Equal(round.RecipeId, table.SelectedRecipeId);
        Assert.Same(round, table.SelectedRecipe);
        Assert.NotNull(table.WorkItemStack);
    }

    [AtlasScenario]
    public async Task SelectRecipePacket_WithForeignOrUnknownRecipe_Should_GiveRoughGemBack()
    {
        var matching = Matching(Stack(Rough));
        int foreign = Mod.gemCuttingRecipes.First(r => !matching.Contains(r)).RecipeId;

        foreach (int id in new[] { foreign, int.MaxValue })
        {
            var (player, table, _) = await TableWithGem(selectRound: false);

            SelectRecipe(table, player, id);

            AssertTableEmpty(table);
            Assert.Single(Carried(player, Rough));
        }
    }

    [AtlasScenario]
    public async Task CancelSelectPacket_Should_GiveTheSameRoughGemBack()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        ItemStack rough = Stack(Rough);
        rough.Attributes.SetString("scenariomark", "kept");
        Hold(player, rough);
        Assert.True(Put(table, player, pos));

        table.OnReceivedClientPacket(player.Player, (int)EnumAnvilPacket.CancelSelect, null);

        AssertTableEmpty(table);
        ItemStack back = Assert.Single(Carried(player, Rough));
        Assert.Equal("kept", back.Attributes.GetString("scenariomark"));
    }

    // Tables saved by older versions have no remembered stack; without it the work item's own
    // gem type and size decide.
    [AtlasScenario]
    public async Task Ditch_WithoutRememberedStack_Should_GiveBaseMaterial()
    {
        var (player, table, _) = await TableWithGem(selectRound: false);
        SetField(table, "returnOnCancelStack", null);

        table.ditchWorkItemStack(player.Player);

        AssertTableEmpty(table);
        Assert.Single(Carried(player, Rough));
    }

    [AtlasScenario]
    public async Task Take_WithRecipe_Should_GiveWorkItemWithProgress_ThatPutsBackAsItWas()
    {
        var (player, table, pos) = await TableWithGem();
        Cutting();
        ReadyChisel(player);
        Strike(table, player, SpareVoxel(table));
        byte[,,] progress = (byte[,,])table.Voxels.Clone();
        int recipeId = table.SelectedRecipeId;
        Hold(player, null);

        Assert.True((bool)Call(table, "TryTake", World.Api.World, player.Player, Sel(pos))!);

        AssertTableEmpty(table);
        ItemStack workItem = Assert.Single(Carried(player, WorkItemCode));
        Assert.Equal(recipeId, workItem.Attributes.GetInt("selectedRecipeId"));
        Assert.Equal(BlockEntityGemCuttingTable.serializeVoxels(progress), workItem.Attributes.GetBytes("voxels"));

        Hold(player, workItem.Clone());
        foreach (ItemSlot slot in CarriedSlots(player).Where(s => s != Hand(player) && s.Itemstack?.Collectible.Code.ToString() == WorkItemCode))
        {
            slot.Itemstack = null;
        }
        Assert.True(Put(table, player, pos));

        Assert.True(Hand(player).Empty);
        Assert.Equal(recipeId, table.SelectedRecipeId);
        SameGrid(progress, table.Voxels);
    }

    [AtlasScenario]
    public async Task TreeAttributes_Should_RoundTrip_GridRecipeWorkItemAndMaterials()
    {
        var (_, table, _) = await TableWithGem();
        table.stoneType = "basalt";
        table.metalType = "iron";
        byte[,,] grid = (byte[,,])table.Voxels.Clone();
        int recipeId = table.SelectedRecipeId;

        var tree = new TreeAttribute();
        table.ToTreeAttributes(tree);
        table.stoneType = "granite";
        table.metalType = "copper";
        table.Voxels = new byte[16, 14, 16];
        table.SelectedRecipeId = -1;
        table.FromTreeAttributes(tree, World.Api.World);

        SameGrid(grid, table.Voxels);
        Assert.Equal(recipeId, table.SelectedRecipeId);
        Assert.Equal("basalt", table.stoneType);
        Assert.Equal("iron", table.metalType);
        Assert.Equal(WorkItemCode, table.WorkItemStack.Collectible.Code.ToString());
        Assert.Equal(GemType, table.WorkItemStack.Attributes.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
    }

    /// <summary>A table with a marked rough gem on it and no recipe chosen yet.</summary>
    private async Task<(ITestPlayer player, BlockEntityGemCuttingTable table, BlockPos pos)> TableWithMarkedGem()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        ItemStack rough = Stack(Rough);
        rough.Attributes.SetString("scenariomark", "kept");
        Hold(player, rough);
        Assert.True(Put(table, player, pos));
        Assert.Equal(-1, table.SelectedRecipeId);
        return (player, table, pos);
    }

    // A reload must not lose the rough gem the cancel gives back, nor turn the work item.
    [AtlasScenario]
    public async Task TreeAttributes_Should_RoundTrip_RememberedGemAndRotation()
    {
        var (player, table, _) = await TableWithMarkedGem();
        table.rotation = 270;

        var tree = new TreeAttribute();
        table.ToTreeAttributes(tree);
        SetField(table, "returnOnCancelStack", null);
        table.rotation = 0;
        table.FromTreeAttributes(tree, World.Api.World);

        Assert.Equal(270, table.rotation);
        table.OnReceivedClientPacket(player.Player, (int)EnumAnvilPacket.CancelSelect, null);
        ItemStack back = Assert.Single(Carried(player, Rough));
        Assert.Equal("kept", back.Attributes.GetString("scenariomark"));
    }

    [AtlasScenario]
    public async Task Ditch_Should_ForgetTheRememberedGem()
    {
        var (player, table, _) = await TableWithMarkedGem();

        table.ditchWorkItemStack(player.Player);

        Assert.Null(GetField<ItemStack>(table, "returnOnCancelStack"));
        var tree = new TreeAttribute();
        table.ToTreeAttributes(tree);
        Assert.Null(tree.GetItemstack("returnOnCancelStack"));
    }

    // ---- chisel strikes ----------------------------------------------------------------------

    [AtlasScenario]
    public async Task ChiselStrike_Should_RemoveOnlyUnneededVoxels_AndCostOneDurability()
    {
        var (player, table, _) = await TableWithGem();
        Cutting();
        ItemStack chisel = ReadyChisel(player);
        int durability = Durability(chisel);
        bool[,,] recipe = table.recipeVoxels;
        byte[,,] before = (byte[,,])table.Voxels.Clone();
        Vec3i target = SpareVoxel(table);

        Strike(table, player, target);

        Assert.Equal(0, table.Voxels[target.X, target.Y, target.Z]);
        foreach (Vec3i c in Cells())
        {
            if (table.Voxels[c.X, c.Y, c.Z] == before[c.X, c.Y, c.Z]) continue;
            Assert.False(recipe[c.X, c.Y, c.Z], $"a voxel the recipe needs was removed at {c}");
            Assert.True(Math.Abs(c.X - target.X) <= 1 && Math.Abs(c.Y - target.Y) <= 1 && Math.Abs(c.Z - target.Z) <= 1,
                $"a voxel outside the 3x3x3 split was removed at {c}");
        }
        Assert.Equal(durability - 1, Durability(Hand(player).Itemstack));
        Assert.NotNull(table.WorkItemStack);
    }

    [AtlasScenario]
    public async Task ChiselStrike_Should_BeIgnored_WithoutChisel_OutsideGrid_OrFromAfar()
    {
        var (player, table, pos) = await TableWithGem();
        Cutting();
        byte[,,] before = (byte[,,])table.Voxels.Clone();
        Vec3i target = SpareVoxel(table);

        Hold(player, Stack(Hammer));
        Strike(table, player, target);
        SameGrid(before, table.Voxels);

        ReadyChisel(player);
        Strike(table, player, new Vec3i(16, 0, 0));
        Strike(table, player, new Vec3i(0, -1, 0));
        SameGrid(before, table.Voxels);

        await player.TeleportTo(pos.Offset(20, 0, 0));
        Strike(table, player, target);
        SameGrid(before, table.Voxels);
        Assert.NotNull(table.WorkItemStack);
    }

    [AtlasScenario]
    public async Task VoxelsPerClick_Should_StrikeSeveralTimes_AndChargeByConfig()
    {
        foreach (var (perVoxel, mode, expectedDamage) in new[] { (true, "enabled", 4), (false, "enabled", 1), (true, "disabled", 1) })
        {
            var (player, table, _) = await TableWithGem();
            Cutting(perClick: 4, perVoxel: perVoxel, mode: mode);
            ItemStack chisel = ReadyChisel(player);
            int durability = Durability(chisel);
            int metal = CountMetal(table.Voxels);

            Strike(table, player, SpareVoxel(table));

            Assert.Equal(durability - expectedDamage, Durability(Hand(player).Itemstack));
            Assert.True(CountMetal(table.Voxels) < metal);
        }
    }

    [AtlasScenario]
    public async Task InstantComplete_Should_FinishCut_OnFirstStrike()
    {
        var (player, table, _) = await TableWithGem();
        Cutting(instant: true);
        ItemStack chisel = ReadyChisel(player);
        int durability = Durability(chisel);

        Strike(table, player, new Vec3i(5, 0, 5));

        AssertTableEmpty(table);
        Assert.Single(Carried(player, CutGem));
        Assert.Equal(durability - 1, Durability(Hand(player).Itemstack));
    }

    [AtlasScenario]
    public async Task InstantComplete_Should_NotApply_ToPlayersOutsideTheWhitelist()
    {
        var (player, table, _) = await TableWithGem();
        Cutting(instant: true, mode: "whitelist");
        SetConfig(c => c.cuttingWhitelist, (c, v) => c.cuttingWhitelist = v, new HashSet<string>());
        ReadyChisel(player);

        Strike(table, player, SpareVoxel(table));

        Assert.NotNull(table.WorkItemStack);
        Assert.Empty(Carried(player, CutGem));
    }

    // A strike before any recipe is chosen gives the gem back onto the table top.
    [AtlasScenario]
    public async Task StrikeBeforeRecipeChoice_Should_DropRoughGemAtTable()
    {
        var (player, table, pos) = await TableWithGem(selectRound: false);
        Cutting();
        ReadyChisel(player);

        Strike(table, player, new Vec3i(5, 0, 5));

        AssertTableEmpty(table);
        await World.Until(() => DroppedNear(pos, Rough).Count == 1, "the rough gem lies at the table", 100);
    }

    [AtlasScenario]
    public async Task StrikingLastVoxel_Should_ClearTable_WithNothingBack()
    {
        var (player, table, pos) = await TableWithGem();
        Cutting();
        ItemStack chisel = ReadyChisel(player);
        int durability = Durability(chisel);
        Vec3i last = SpareVoxel(table);
        table.Voxels = new byte[16, 14, 16];
        table.Voxels[last.X, last.Y, last.Z] = 1;

        Strike(table, player, last);

        AssertTableEmpty(table);
        Assert.Equal(durability - 1, Durability(Hand(player).Itemstack));
        await World.Ticks(5);
        Assert.Empty(DroppedNear(pos, Rough));
        Assert.Empty(DroppedNear(pos, WorkItemCode));
        Assert.Empty(Carried(player, CutGem));
    }

    [AtlasScenario]
    public async Task ChiselRightClick_Should_RotateWorkItem_ClockwiseOrWithShiftBack()
    {
        foreach (bool shift in new[] { false, true })
        {
            var (player, table, pos) = await TableWithGem();
            Hold(player, Stack(Chisel));
            table.Voxels = new byte[16, 14, 16];
            table.Voxels[1, 0, 2] = 1;
            player.Entity.Controls.ShiftKey = shift;
            try
            {
                Assert.True((bool)Call(table, "OnPlayerInteract", World.Api.World, player.Player, Sel(pos))!);
            }
            finally
            {
                player.Entity.Controls.ShiftKey = false;
            }

            Assert.Equal(90, table.rotation);
            Assert.Equal(1, CountMetal(table.Voxels));
            if (shift) Assert.Equal(1, table.Voxels[13, 0, 1]);
            else Assert.Equal(1, table.Voxels[2, 0, 14]);
        }
    }

    [AtlasScenario]
    public async Task CleanModes_Should_ClearLayersAndRows_SparingRecipeVoxelsOnlyWhenAsked()
    {
        var (_, table, _) = await TableWithGem();
        bool[,,] recipe = table.recipeVoxels;
        byte[,,] full = new byte[16, 14, 16];
        foreach (Vec3i c in Cells()) full[c.X, c.Y, c.Z] = 1;

        table.Voxels = (byte[,,])full.Clone();
        table.OnCleanHorizontal(new Vec3i(5, 2, 5), BlockFacing.NORTH);
        foreach (Vec3i c in Cells()) Assert.Equal(c.Y == 2 ? 0 : 1, table.Voxels[c.X, c.Y, c.Z]);

        table.Voxels = (byte[,,])full.Clone();
        table.OnCleanHorizontal(new Vec3i(5, 2, 5), BlockFacing.NORTH, spareRecipeVoxels: true);
        foreach (Vec3i c in Cells()) Assert.Equal(c.Y == 2 && !recipe[c.X, c.Y, c.Z] ? 0 : 1, table.Voxels[c.X, c.Y, c.Z]);

        // Vertical cleaning reaches the lowest seven layers of one row.
        table.Voxels = (byte[,,])full.Clone();
        table.OnCleanVertical(new Vec3i(5, 0, 7), BlockFacing.NORTH);
        foreach (Vec3i c in Cells()) Assert.Equal(c.X == 5 && c.Y < 7 ? 0 : 1, table.Voxels[c.X, c.Y, c.Z]);

        table.Voxels = (byte[,,])full.Clone();
        table.OnCleanVertical(new Vec3i(5, 0, 7), BlockFacing.EAST);
        foreach (Vec3i c in Cells()) Assert.Equal(c.Z == 7 && c.Y < 7 ? 0 : 1, table.Voxels[c.X, c.Y, c.Z]);

        table.Voxels = (byte[,,])full.Clone();
        table.OnSplit(new Vec3i(8, 3, 8));
        foreach (Vec3i c in Cells())
        {
            bool inSplit = Math.Abs(c.X - 8) <= 1 && Math.Abs(c.Y - 3) <= 1 && Math.Abs(c.Z - 8) <= 1;
            Assert.Equal(inSplit && !recipe[c.X, c.Y, c.Z] ? 0 : 1, table.Voxels[c.X, c.Y, c.Z]);
        }
    }

    // ---- finishing a cut ---------------------------------------------------------------------

    [AtlasScenario]
    public async Task ExactMatch_Should_GiveCutGem_WithBuffRolledFromConfig()
    {
        NoGemTraits();
        ByTiers();
        var (player, table, _) = await TableWithGem();
        table.Voxels = RecipeGrid(table);

        table.CheckIfFinished(player.Player);

        AssertTableEmpty(table);
        ItemStack gem = Assert.Single(Carried(player, CutGem));
        Assert.False(gem.Attributes.HasAttribute(CANJWConstants.CUTTING_TYPE));
        Assert.Equal(CANJWConstants.CUTTING_ROUND, gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE).GetString(CANJWConstants.CUTTING_TYPE));
        var (names, values) = Buffs(gem);
        string buff = Assert.Single(names);
        Assert.Contains(buff, Mod.config.PossibleGemBuffs[GemType]);
        float[] range = Mod.config.BuffAttributesDict[buff].MainStatValueRange[1];
        float m0 = RoundMult(0);
        float min = (float)Math.Round(range[0], 3) * m0;
        float max = (float)Math.Round(range[range.Length - 1], 3) * m0;
        Assert.InRange(values[0], min - 1e-4f, max + 1e-4f);
    }

    [AtlasScenario]
    public async Task ExactMatch_WithoutPlayer_Should_DropCutGemOnTable_WithoutCutEvent()
    {
        var (player, table, pos) = await TableWithGem();
        table.Voxels = RecipeGrid(table);
        int fired = 0;
        Action<CutEvent> handler = _ => fired++;
        Mod.Instance.OnCutGem += handler;
        try
        {
            table.CheckIfFinished(null);
        }
        finally
        {
            Mod.Instance.OnCutGem -= handler;
        }

        AssertTableEmpty(table);
        Assert.Equal(0, fired);
        await World.Until(() => DroppedNear(pos, CutGem).Count == 1, "the cut gem lies on the table", 100);
    }

    [AtlasScenario]
    public async Task Hammer_WithMissingRecipeVoxels_Should_FinishWithLoweredBuff()
    {
        // Before the gem goes on the table: its traits are rolled there.
        FixGemBuff(GemType, 0.05f);
        var (player, table, pos) = await TableWithGem();
        SetConfig(c => c.minFineForMistake, (c, v) => c.minFineForMistake = v, 0.1f);
        SetConfig(c => c.maxFineForMistake, (c, v) => c.maxFineForMistake = v, 0.1f);
        byte[,,] grid = RecipeGrid(table);
        foreach (Vec3i c in Cells().Where(c => grid[c.X, c.Y, c.Z] == 1).Take(3)) grid[c.X, c.Y, c.Z] = 0;
        table.Voxels = grid;
        Hold(player, Stack(Hammer));
        player.Entity.Controls.ShiftKey = false;

        Assert.False((bool)Call(table, "OnPlayerInteract", World.Api.World, player.Player, Sel(pos))!);

        AssertTableEmpty(table);
        var (_, values) = Buffs(Assert.Single(Carried(player, CutGem)));
        Assert.Equal(0.05f * RoundMult(0) * (1 - 0.1f * 3), values[0], 4);
    }

    // Extra voxels are not a mistake the hammer forgives: the click falls through to taking the piece.
    [AtlasScenario]
    public async Task Hammer_WithExtraVoxels_Should_TakeWorkItemInstead()
    {
        var (player, table, pos) = await TableWithGem();
        byte[,,] grid = RecipeGrid(table);
        Vec3i extra = Cells().First(c => grid[c.X, c.Y, c.Z] == 0);
        grid[extra.X, extra.Y, extra.Z] = 1;
        table.Voxels = grid;
        Hold(player, Stack(Hammer));
        player.Entity.Controls.ShiftKey = false;

        Call(table, "OnPlayerInteract", World.Api.World, player.Player, Sel(pos));

        AssertTableEmpty(table);
        Assert.Empty(Carried(player, CutGem));
        Assert.Single(Carried(player, WorkItemCode));
    }

    [AtlasScenario]
    public async Task CutEvent_Should_DescribeCut_AndRefundRoughGem_WhenNotConsumed()
    {
        var (player, table, _) = await TableWithGem();
        table.Voxels = RecipeGrid(table);
        CutEvent? seen = null;
        Action<CutEvent> handler = e => { seen = e; e.ConsumeRough = false; };
        Mod.Instance.OnCutGem += handler;
        try
        {
            table.CheckIfFinished(player.Player);
        }
        finally
        {
            Mod.Instance.OnCutGem -= handler;
        }

        Assert.NotNull(seen);
        Assert.Same(player.Player, seen!.Player);
        Assert.Equal(CANJWConstants.CUTTING_ROUND, seen.CuttingType);
        Assert.Equal("chipped", seen.GemSize);
        Assert.Equal(Rough, seen.RoughGem.Collectible.Code.ToString());
        Assert.Single(Carried(player, CutGem));
        Assert.Single(Carried(player, Rough));
    }

    [AtlasScenario]
    public async Task CutEvent_LeftAlone_Should_ConsumeRoughGem()
    {
        var (player, table, _) = await TableWithGem();
        table.Voxels = RecipeGrid(table);

        table.CheckIfFinished(player.Player);

        Assert.Single(Carried(player, CutGem));
        Assert.Empty(Carried(player, Rough));
    }

    // ---- breaking the table ------------------------------------------------------------------

    [AtlasScenario]
    public async Task BreakingTable_Should_DropWorkItemWithProgress_AndTableWithMaterials()
    {
        var (_, table, pos) = await TableWithGem();
        table.stoneType = "basalt";
        table.metalType = "iron";
        byte[] voxels = BlockEntityGemCuttingTable.serializeVoxels(table.Voxels);
        int recipeId = table.SelectedRecipeId;

        World.Api.World.BlockAccessor.BreakBlock(pos, null);

        await World.Until(() => DroppedNear(pos, WorkItemCode).Count == 1 && DroppedNear(pos, TableCode).Count == 1,
            "the work item and the table lie on the floor", 100);
        ItemStack workItem = DroppedNear(pos, WorkItemCode)[0];
        Assert.Equal(voxels, workItem.Attributes.GetBytes("voxels"));
        Assert.Equal(recipeId, workItem.Attributes.GetInt("selectedRecipeId"));
        ItemStack tableStack = DroppedNear(pos, TableCode)[0];
        Assert.Equal("basalt", tableStack.Attributes.GetString("stone"));
        Assert.Equal("iron", tableStack.Attributes.GetString("metal"));
    }

    // A work item without a recipe would not go back on a table, so the rough gem drops instead.
    [AtlasScenario]
    public async Task BreakingTable_BeforeRecipeChoice_Should_DropTheRoughGem()
    {
        var (_, _, pos) = await TableWithMarkedGem();

        World.Api.World.BlockAccessor.BreakBlock(pos, null);

        await World.Until(() => DroppedNear(pos, Rough).Count == 1 && DroppedNear(pos, TableCode).Count == 1,
            "the rough gem and the table lie on the floor", 100);
        Assert.Empty(DroppedNear(pos, WorkItemCode));
        Assert.Equal("kept", DroppedNear(pos, Rough)[0].Attributes.GetString("scenariomark"));
    }

    [AtlasScenario]
    public async Task BreakingTable_Should_KeepTheRotationOnTheWorkItem()
    {
        var (_, table, pos) = await TableWithGem();
        table.rotation = 90;

        World.Api.World.BlockAccessor.BreakBlock(pos, null);

        await World.Until(() => DroppedNear(pos, WorkItemCode).Count == 1, "the work item lies on the floor", 100);
        Assert.Equal(90, DroppedNear(pos, WorkItemCode)[0].Attributes.GetInt("rotation"));
    }
}
