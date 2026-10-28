using System.Text;
using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.be;
using canjewelry.src.integration;
using canjewelry.src.jewelry;
using canjewelry.src.utils;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using AppraisalCB = global::canjewelry.src.cb.CANGemAppraisalCB;
using CANGemInspectionServer = global::canjewelry.src.cb.CANGemInspectionServer;
using CuttableCB = global::canjewelry.src.cb.CANGemCuttableCB;
using Mod = global::canjewelry.src.canjewelry;
using TraitConfig = global::canjewelry.src.Config.GemTraitConfig;

namespace canjewelry.scenarios;

/// <summary>
/// Gem weight and traits: the loupe, the silent roll on the cutting table, the weight a cut
/// keeps and what the tooltips show of it.
/// </summary>
public class AppraisalScenarios : WorkstationScenarioBase
{
    private const string Loupe = "canjewelry:canloupe-simple";
    private const string TableCode = "canjewelry:cangemcuttingtable";
    private const string Rough = "canjewelry:gem-rough-chipped-" + GemType;
    private const string CutGem = "canjewelry:gem-cut-normal-" + GemType;

    // ---- helpers -----------------------------------------------------------------------------

    /// <summary>
    /// The right click with the loupe held through to its end, as the server sees it - the plain
    /// loupe, without the inspection dialog (that one has its own scenarios).
    /// </summary>
    private void UseLoupe(ITestPlayer player, float seconds = 2f, bool inspect = false)
    {
        SetConfig(c => c.GemCarats.Inspection.Enabled, (c, v) => c.GemCarats.Inspection.Enabled = v, inspect);
        ItemSlot hand = Hand(player);
        EnumHandling handling = EnumHandling.PassThrough;
        hand.Itemstack.Collectible.GetBehavior<AppraisalCB>()
            .OnHeldInteractStop(seconds, hand, player.Entity, null, null, ref handling);
    }

    private static ITreeAttribute Attrs(ItemStack stack) => stack.Attributes;

    /// <summary>Only the given traits, each with the given chance, in this order.</summary>
    private void OnlyTraits(params (string code, float chance, float yieldMult, float buffMult)[] traits)
    {
        var dict = new Dictionary<string, TraitConfig>();
        foreach (var t in traits)
        {
            dict[t.code] = new TraitConfig(new[] { t.chance, t.chance, t.chance }, t.yieldMult, t.buffMult, false);
        }
        SetConfig(c => c.GemCarats.Traits, (c, v) => c.GemCarats.Traits = v, dict);
    }

    /// <summary>A rough gem whose truth is already set, as if the loupe had seen it.</summary>
    private ItemStack AppraisedRough(float carats, params string[] traits)
    {
        ItemStack rough = Stack(Rough);
        CANGemAppraisal.SetWeight(rough.Attributes, carats, traits);
        CANGemAppraisal.SetKnowledge(rough.Attributes, CANGemAppraisal.Loupe, new[] { carats - 0.3f, carats + 0.3f }, new string[0]);
        return rough;
    }

    private bool Put(BlockEntityGemCuttingTable table, ITestPlayer player, BlockPos pos) =>
        (bool)Call(table, "TryPut", World.Api.World, player.Player, Sel(pos))!;

    private static int RoundRecipeId(ItemStack rough) => rough.Collectible.GetBehavior<CuttableCB>().GetMatchingRecipes(rough)
        .First(r => r.Output.ResolvedItemstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)?.GetString(CANJWConstants.CUTTING_TYPE) == CANJWConstants.CUTTING_ROUND)
        .RecipeId;

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

    /// <summary>
    /// Puts the gem on a fresh table, picks the round cut and finishes it at once, or with the
    /// given number of recipe voxels missing and the hammer. Returns the cut gem, whatever its size.
    /// </summary>
    private async Task<ItemStack> CutRound(ItemStack rough, int mistakes = 0)
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        int recipeId = RoundRecipeId(rough);
        Hold(player, rough);
        Assert.True(Put(table, player, pos));
        table.OnReceivedClientPacket(player.Player, (int)EnumAnvilPacket.SelectRecipe, SerializerUtil.Serialize(recipeId));
        byte[,,] grid = RecipeGrid(table);
        int missing = 0;
        for (int x = 0; x < 16 && missing < mistakes; x++)
            for (int y = 0; y < 14 && missing < mistakes; y++)
                for (int z = 0; z < 16 && missing < mistakes; z++)
                    if (grid[x, y, z] == 1) { grid[x, y, z] = 0; missing++; }
        table.Voxels = grid;
        if (mistakes == 0)
        {
            table.CheckIfFinished(player.Player);
        }
        else
        {
            Hold(player, Stack("game:hammer-copper"));
            Call(table, "OnPlayerInteract", World.Api.World, player.Player, Sel(pos));
        }
        return Assert.Single(CarriedSlots(player)
            .Select(s => s.Itemstack)
            .Where(s => s?.Collectible.Code.Path.StartsWith("gem-cut-") == true));
    }

    private static float CaratsOf(ItemStack cut) => CANGemAppraisal.GetCarats(cut.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE));

    private static string Text(string key, params object[] args) => Vintagestory.API.Config.Lang.Get("canjewelry:" + key, args);

    private static string Tooltip(ItemStack stack, IWorldAccessor world)
    {
        var sb = new StringBuilder();
        stack.Collectible.GetHeldItemInfo(new DummySlot(stack), sb, world, false);
        return sb.ToString();
    }

    // ---- the loupe ---------------------------------------------------------------------------

    [AtlasScenario]
    public async Task Loupe_Should_TakeOneGemOffTheStack_AndShowRangeAndOneTrait()
    {
        OnlyTraits(("crack", 1f, 0.8f, 1f), ("cloudy", 1f, 1f, 0.9f));
        ITestPlayer player = await Join();
        Hold(player, Stack(Rough, 3));
        HoldLeft(player, Stack(Loupe));

        UseLoupe(player);

        List<ItemStack> roughs = Carried(player, Rough);
        Assert.Equal(2, roughs.Count);
        ItemStack rest = roughs.Single(s => s.StackSize == 2);
        Assert.Equal(CANGemAppraisal.Unappraised, CANGemAppraisal.GetAppraisal(Attrs(rest)));
        Assert.False(CANGemAppraisal.HasCarats(Attrs(rest)));

        ItemStack seen = roughs.Single(s => s.StackSize == 1);
        Assert.Equal(CANGemAppraisal.Loupe, CANGemAppraisal.GetAppraisal(Attrs(seen)));
        float carats = CANGemAppraisal.GetCarats(Attrs(seen));
        float[] quality = Mod.config.GemCarats.RoughRanges["chipped"];
        Assert.InRange(carats, quality[0] - 0.001f, quality[1] + 0.001f);
        float[] range = CANGemAppraisal.GetCaratsRange(Attrs(seen))!;
        Assert.InRange(carats, range[0], range[1]);
        Assert.Equal(new[] { "crack", "cloudy" }, CANGemAppraisal.GetTraits(Attrs(seen)));
        string known = Assert.Single(CANGemAppraisal.GetKnownTraits(Attrs(seen)));
        Assert.Contains(known, new[] { "crack", "cloudy" });
    }

    [AtlasScenario]
    public async Task Loupe_LastGemOfStack_Should_StayInHand()
    {
        ITestPlayer player = await Join();
        Hold(player, Stack(Rough));
        HoldLeft(player, Stack(Loupe));

        UseLoupe(player);

        Assert.Equal(CANGemAppraisal.Loupe, CANGemAppraisal.GetAppraisal(Attrs(Hand(player).Itemstack)));
        Assert.Single(Carried(player, Rough));
    }

    [AtlasScenario]
    public async Task Loupe_Should_DoNothing_WithoutLoupe_OrWhenReleasedEarly()
    {
        ITestPlayer player = await Join();
        Hold(player, Stack(Rough, 2));

        UseLoupe(player);
        HoldLeft(player, Stack(Loupe));
        UseLoupe(player, seconds: 0.5f);

        ItemStack hand = Assert.Single(Carried(player, Rough));
        Assert.Equal(2, hand.StackSize);
        Assert.False(CANGemAppraisal.HasCarats(Attrs(hand)));
    }

    [AtlasScenario]
    public async Task Loupe_Should_NotAppraiseTwice()
    {
        ITestPlayer player = await Join();
        Hold(player, AppraisedRough(2.4f, "crack"));
        HoldLeft(player, Stack(Loupe));
        float[] before = CANGemAppraisal.GetCaratsRange(Attrs(Hand(player).Itemstack))!;

        UseLoupe(player);

        ItemStack gem = Hand(player).Itemstack;
        Assert.Equal(2.4f, CANGemAppraisal.GetCarats(Attrs(gem)), 3);
        Assert.Equal(before, CANGemAppraisal.GetCaratsRange(Attrs(gem)));
        Assert.Empty(CANGemAppraisal.GetKnownTraits(Attrs(gem)));
    }

    // A gem the table already rolled keeps its truth under the loupe.
    [AtlasScenario]
    public async Task Loupe_OnGemWithTruth_Should_OnlyReveal()
    {
        ITestPlayer player = await Join();
        ItemStack rough = Stack(Rough);
        CANGemAppraisal.SetWeight(rough.Attributes, 2.2f, new[] { "inclusions" });
        Hold(player, rough);
        HoldLeft(player, Stack(Loupe));

        UseLoupe(player);

        ItemStack gem = Hand(player).Itemstack;
        Assert.Equal(2.2f, CANGemAppraisal.GetCarats(Attrs(gem)), 3);
        Assert.Equal(new[] { "inclusions" }, CANGemAppraisal.GetKnownTraits(Attrs(gem)));
    }

    [AtlasScenario]
    public async Task Loupe_OnVanillaGem_Should_UseQualityOfPotential()
    {
        ITestPlayer player = await Join();
        ItemStack vanilla = Stack("game:gem-diamond-rough");
        vanilla.Attributes.SetString("potential", "high");
        Assert.True(vanilla.Collectible.HasBehavior<AppraisalCB>(), "vanilla rough gems have no appraisal behavior");
        Hold(player, vanilla);
        HoldLeft(player, Stack(Loupe));

        UseLoupe(player);

        float[] normal = CANGemAppraisal.RoughRange(Mod.config.GemCarats, "normal", "diamond")!;
        Assert.InRange(CANGemAppraisal.GetCarats(Attrs(Hand(player).Itemstack)), normal[0] - 0.001f, normal[1] + 0.001f);
    }

    [AtlasScenario]
    public async Task AppraiseEvent_Should_LetSubscriberChangeTruthAndReveal()
    {
        OnlyTraits(("crack", 1f, 0.8f, 1f), ("cloudy", 1f, 1f, 0.9f));
        ITestPlayer player = await Join();
        Hold(player, Stack(Rough));
        HoldLeft(player, Stack(Loupe));
        AppraiseEvent? seen = null;
        Action<AppraiseEvent> handler = e => { seen = e; e.Carats = 2.0f; e.RevealCount = 2; };
        Mod.Instance.OnAppraiseGem += handler;
        try
        {
            UseLoupe(player);
        }
        finally
        {
            Mod.Instance.OnAppraiseGem -= handler;
        }

        Assert.NotNull(seen);
        Assert.Equal(AppraiseStage.Loupe, seen!.Stage);
        Assert.Same(player.Player, seen.Player);
        ItemStack gem = Hand(player).Itemstack;
        Assert.Equal(2.0f, CANGemAppraisal.GetCarats(Attrs(gem)), 3);
        Assert.Equal(2, CANGemAppraisal.GetKnownTraits(Attrs(gem)).Length);
    }

    // ---- the inspection ----------------------------------------------------------------------

    private const int ShapeSeed = 12345;

    /// <summary>A rough gem with a fixed own shape, so a scenario can work out the view the server uses.</summary>
    private ItemStack SeededRough()
    {
        ItemStack rough = Stack(Rough);
        rough.Attributes.SetInt(CANGemRoughShape.SeedKey, ShapeSeed);
        return rough;
    }

    private ItemStack RoughWithInclusion(float carats, float[] pos)
    {
        // The test world's light is whatever it is: one mode that covers any light and shows all.
        LightingCovers(0, 99);
        ItemStack rough = SeededRough();
        CANGemAppraisal.SetWeight(rough.Attributes, carats, new[] { "inclusions", "cloudy" });
        CANGemDefects.Write(rough.Attributes, new List<GemDefect> { new GemDefect { Trait = "inclusions", Pos = pos } });
        return rough;
    }

    /// <summary>A single lighting mode, showing everything, for light levels min..max.</summary>
    private void LightingCovers(int min, int max) =>
        SetConfig(c => c.GemCarats.Inspection.Modes, (c, v) => c.GemCarats.Inspection.Modes = v,
            new Dictionary<string, global::canjewelry.src.Config.InspectModeConfig>
            {
                { "test", new global::canjewelry.src.Config.InspectModeConfig { MinLight = min, MaxLight = max,
                    Visibility = new Dictionary<string, float> { { "inclusions", 1f }, { "crack", 1f } } } }
            });

    // The click carries the transform the view was built with, as the client sends it.
    private GemInspectClickPacket ClickAt(string session, GemView view, float[] pos, float offset = 0)
    {
        float[] p = CANGemDefects.Project(view, pos);
        float[][] t = CANGemInspectionServer.Transform(Stack(Rough).Collectible);
        return new GemInspectClickPacket
        {
            SessionId = session, X = p[0] + offset, Y = p[1], GuiScale = 1f,
            Origin = t[0], Translation = t[1], Scale = t[2], BaseRotation = t[3]
        };
    }

    private static string SessionOf(ITestPlayer player)
    {
        var sessions = typeof(CANGemInspectionServer).GetField("sessions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null) as System.Collections.IDictionary;
        object? s = sessions![player.Player.PlayerUID];
        return (string)s!.GetType().GetField("Id")!.GetValue(s)!;
    }

    [AtlasScenario]
    public async Task Inspection_Hit_Should_GrindOutTheFlaw_AtItsCost()
    {
        SetConfig(c => c.GemCarats.Inspection.Attempts, (c, v) => c.GemCarats.Inspection.Attempts = v, 3);
        ITestPlayer player = await Join();
        float[] pos = { 0f, 0f, 0f };
        Hold(player, RoughWithInclusion(5.0f, pos));
        HoldLeft(player, Stack(Loupe));

        UseLoupe(player, inspect: true);
        Assert.True(Hand(player).Empty, "the gem stays with the inspection meanwhile");
        Assert.True(CANGemInspectionServer.HasSession(player.Player as Vintagestory.API.Server.IServerPlayer));

        var sp = (Vintagestory.API.Server.IServerPlayer)player.Player;
        GemView view = CANGemInspectionServer.ViewFor(World.Api, SeededRough(), 0, 0, 1f, Mod.config.GemCarats.Inspection.FboSize);
        string session = SessionOf(player);
        CANGemInspectionServer.Click(sp, ClickAt(session, view, pos));
        CANGemInspectionServer.Close(sp, session);

        ItemStack gem = Assert.Single(Carried(player, Rough));
        float cost = Mod.config.GemCarats.Traits["inclusions"].RemoveCost;
        Assert.Equal(CANGemAppraisal.RoundCarats(5.0f * (1 - cost), 0.1f), CANGemAppraisal.GetCarats(gem.Attributes), 3);
        Assert.Equal(new[] { "cloudy" }, CANGemAppraisal.GetTraits(gem.Attributes));
        Assert.Equal(new[] { "cloudy" }, CANGemAppraisal.GetKnownTraits(gem.Attributes));
        Assert.Equal(CANGemAppraisal.Loupe, CANGemAppraisal.GetAppraisal(gem.Attributes));
        Assert.Empty(CANGemDefects.Read(gem.Attributes));
    }

    [AtlasScenario]
    public async Task Inspection_Miss_Should_CostWeight_AndAttemptsRunOut()
    {
        SetConfig(c => c.GemCarats.Inspection.Attempts, (c, v) => c.GemCarats.Inspection.Attempts = v, 2);
        ITestPlayer player = await Join();
        float[] pos = { 0f, 0f, 0f };
        Hold(player, RoughWithInclusion(5.0f, pos));
        HoldLeft(player, Stack(Loupe));
        UseLoupe(player, inspect: true);

        var sp = (Vintagestory.API.Server.IServerPlayer)player.Player;
        GemView view = CANGemInspectionServer.ViewFor(World.Api, SeededRough(), 0, 0, 1f, Mod.config.GemCarats.Inspection.FboSize);
        string session = SessionOf(player);
        CANGemInspectionServer.Click(sp, ClickAt(session, view, pos, offset: 120));
        CANGemInspectionServer.Click(sp, ClickAt(session, view, pos, offset: 120));

        Assert.False(CANGemInspectionServer.HasSession(sp), "two misses should have used up both clicks");
        ItemStack gem = Assert.Single(Carried(player, Rough));
        float miss = Mod.config.GemCarats.Inspection.MissCost;
        float expected = CANGemAppraisal.RoundCarats(CANGemAppraisal.RoundCarats(5.0f * (1 - miss), 0.1f) * (1 - miss), 0.1f);
        Assert.Equal(expected, CANGemAppraisal.GetCarats(gem.Attributes), 3);
        Assert.Contains("inclusions", CANGemAppraisal.GetTraits(gem.Attributes));
        Assert.DoesNotContain("inclusions", CANGemAppraisal.GetKnownTraits(gem.Attributes));
    }

    // Light no lighting mode covers sees nothing and costs nothing; once the light fits, the click counts.
    [AtlasScenario]
    public async Task Inspection_ClickInLightNoModeCovers_Should_CostNothing()
    {
        SetConfig(c => c.GemCarats.Inspection.Attempts, (c, v) => c.GemCarats.Inspection.Attempts = v, 1);
        ITestPlayer player = await Join();
        float[] pos = { 0f, 0f, 0f };
        Hold(player, RoughWithInclusion(5.0f, pos));
        HoldLeft(player, Stack(Loupe));
        UseLoupe(player, inspect: true);

        var sp = (Vintagestory.API.Server.IServerPlayer)player.Player;
        GemView view = CANGemInspectionServer.ViewFor(World.Api, SeededRough(), 0, 0, 1f, Mod.config.GemCarats.Inspection.FboSize);
        string session = SessionOf(player);
        LightingCovers(99, 99);
        CANGemInspectionServer.Click(sp, ClickAt(session, view, pos));

        Assert.True(CANGemInspectionServer.HasSession(sp), "the only click was not spent");
        LightingCovers(0, 99);
        CANGemInspectionServer.Click(sp, ClickAt(session, view, pos));
        ItemStack gem = Assert.Single(Carried(player, Rough));
        Assert.DoesNotContain("inclusions", CANGemAppraisal.GetTraits(gem.Attributes));
    }

    // The same gem can be looked at again elsewhere, with the clicks it has left; its range is
    // not rolled again.
    [AtlasScenario]
    public async Task Inspection_Should_ReopenWithTheClicksLeft()
    {
        SetConfig(c => c.GemCarats.Inspection.Attempts, (c, v) => c.GemCarats.Inspection.Attempts = v, 3);
        ITestPlayer player = await Join();
        float[] pos = { 0f, 0f, 0f };
        Hold(player, RoughWithInclusion(5.0f, pos));
        HoldLeft(player, Stack(Loupe));
        var sp = (Vintagestory.API.Server.IServerPlayer)player.Player;
        GemView view = CANGemInspectionServer.ViewFor(World.Api, SeededRough(), 0, 0, 1f, Mod.config.GemCarats.Inspection.FboSize);

        UseLoupe(player, inspect: true);
        CANGemInspectionServer.Click(sp, ClickAt(SessionOf(player), view, pos, offset: 120));
        CANGemInspectionServer.Close(sp, SessionOf(player));
        ItemStack once = Assert.Single(Carried(player, Rough));
        Assert.Equal(2, once.Attributes.GetInt(CANGemInspectionServer.ClicksLeftKey));
        float[] range = CANGemAppraisal.GetCaratsRange(once.Attributes)!;

        // Back in hand, opened again: two clicks, one of them finds the inclusion.
        foreach (ItemSlot slot in CarriedSlots(player).Where(s => s.Itemstack?.Collectible.Code.ToString() == Rough)) slot.Itemstack = null;
        Hold(player, once.Clone());
        UseLoupe(player, inspect: true);
        Assert.True(CANGemInspectionServer.HasSession(sp));
        CANGemInspectionServer.Click(sp, ClickAt(SessionOf(player), view, pos));
        CANGemInspectionServer.Close(sp, SessionOf(player));

        ItemStack twice = Assert.Single(Carried(player, Rough));
        Assert.Equal(1, twice.Attributes.GetInt(CANGemInspectionServer.ClicksLeftKey));
        Assert.DoesNotContain("inclusions", CANGemAppraisal.GetTraits(twice.Attributes));
        float ratio = CANGemAppraisal.GetCarats(twice.Attributes) / CANGemAppraisal.GetCarats(once.Attributes);
        Assert.Equal(CANGemAppraisal.RoundCarats(range[0] * ratio, 0.1f), CANGemAppraisal.GetCaratsRange(twice.Attributes)![0], 3);
    }

    [AtlasScenario]
    public async Task Inspection_ForeignOrStaleClick_Should_BeIgnored()
    {
        ITestPlayer player = await Join();
        float[] pos = { 0f, 0f, 0f };
        Hold(player, RoughWithInclusion(5.0f, pos));
        HoldLeft(player, Stack(Loupe));
        UseLoupe(player, inspect: true);

        var sp = (Vintagestory.API.Server.IServerPlayer)player.Player;
        GemView view = CANGemInspectionServer.ViewFor(World.Api, SeededRough(), 0, 0, 1f, Mod.config.GemCarats.Inspection.FboSize);
        CANGemInspectionServer.Click(sp, ClickAt("not-the-session", view, pos));
        CANGemInspectionServer.Close(sp, "not-the-session");
        Assert.True(CANGemInspectionServer.HasSession(sp));

        // Leaving the game ends it and the gem comes back untouched.
        CANGemInspectionServer.Finish(sp);
        ItemStack gem = Assert.Single(Carried(player, Rough));
        Assert.Contains("inclusions", CANGemAppraisal.GetTraits(gem.Attributes));
        Assert.Equal(5.0f, CANGemAppraisal.GetCarats(gem.Attributes), 3);
    }

    // ---- the cutting table -------------------------------------------------------------------

    [AtlasScenario]
    public async Task Table_Should_RollTruthUnseen_AndCancelGivesTheSameGemBack()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        Hold(player, Stack(Rough));

        Assert.True(Put(table, player, pos));
        ITreeAttribute work = table.WorkItemStack.Attributes;
        Assert.True(CANGemAppraisal.HasCarats(work));
        Assert.Equal(CANGemAppraisal.Unappraised, CANGemAppraisal.GetAppraisal(work));
        float carats = CANGemAppraisal.GetCarats(work);
        string[] traits = CANGemAppraisal.GetTraits(work);

        table.OnReceivedClientPacket(player.Player, (int)EnumAnvilPacket.CancelSelect, null);

        ItemStack back = Assert.Single(Carried(player, Rough));
        Assert.Equal(carats, CANGemAppraisal.GetCarats(Attrs(back)), 3);
        Assert.Equal(traits, CANGemAppraisal.GetTraits(Attrs(back)));
        Assert.Equal(CANGemAppraisal.Unappraised, CANGemAppraisal.GetAppraisal(Attrs(back)));

        // Back on the table it is not rolled again.
        Hold(player, back.Clone());
        foreach (ItemSlot slot in CarriedSlots(player).Where(s => s != Hand(player) && s.Itemstack?.Collectible.Code.ToString() == Rough))
        {
            slot.Itemstack = null;
        }
        Assert.True(Put(table, player, pos));
        Assert.Equal(carats, CANGemAppraisal.GetCarats(table.WorkItemStack.Attributes), 3);
    }

    [AtlasScenario]
    public async Task Table_Should_KeepWhatTheLoupeSaw()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        Hold(player, AppraisedRough(2.6f, "cloudy"));

        Assert.True(Put(table, player, pos));

        ITreeAttribute work = table.WorkItemStack.Attributes;
        Assert.Equal(2.6f, CANGemAppraisal.GetCarats(work), 3);
        Assert.Equal(new[] { "cloudy" }, CANGemAppraisal.GetTraits(work));
        Assert.Equal(CANGemAppraisal.Loupe, CANGemAppraisal.GetAppraisal(work));
    }

    [AtlasScenario]
    public async Task BreakingTable_Should_DropGemWithItsTruth()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        Hold(player, AppraisedRough(2.3f, "crack"));
        Assert.True(Put(table, player, pos));

        World.Api.World.BlockAccessor.BreakBlock(pos, null);

        await World.Until(() => DroppedNear(pos, Rough).Count == 1, "the rough gem lies on the floor", 100);
        ItemStack dropped = DroppedNear(pos, Rough)[0];
        Assert.Equal(2.3f, CANGemAppraisal.GetCarats(Attrs(dropped)), 3);
        Assert.Equal(new[] { "crack" }, CANGemAppraisal.GetTraits(Attrs(dropped)));
    }

    // ---- the cut -----------------------------------------------------------------------------

    [AtlasScenario]
    public async Task Cut_Should_KeepYieldOfTheRoughWeight_InTheRecipeSize()
    {
        OnlyTraits(("crack", 0f, 0.8f, 1f));
        float yield = Mod.config.GemCarats.CutYield[CANJWConstants.CUTTING_ROUND];

        ItemStack cut = await CutRound(AppraisedRough(2.4f));
        Assert.Equal(CutGem, cut.Collectible.Code.ToString());
        Assert.Equal(CANGemAppraisal.RoundCarats(2.4f * yield, 0.1f), CaratsOf(cut), 3);
    }

    // The weight, not the recipe, decides the size; MaxCutCarats caps it.
    [AtlasScenario]
    public async Task HeavyCut_Should_ComeOutBigger_UpToTheCap()
    {
        OnlyTraits(("crack", 0f, 0.8f, 1f));
        float yield = Mod.config.GemCarats.CutYield[CANJWConstants.CUTTING_ROUND];

        ItemStack mid = await CutRound(AppraisedRough(4.0f));
        Assert.Equal($"canjewelry:gem-cut-flawless-{GemType}", mid.Collectible.Code.ToString());
        Assert.Equal(CANGemAppraisal.RoundCarats(4.0f * yield, 0.1f), CaratsOf(mid), 3);

        ItemStack heavy = await CutRound(AppraisedRough(9f));
        Assert.Equal($"canjewelry:gem-cut-exquisite-{GemType}", heavy.Collectible.Code.ToString());
        Assert.Equal(Mod.config.GemCarats.MaxCutCarats, CaratsOf(heavy), 3);
    }

    [AtlasScenario]
    public async Task KeepRecipeBucket_Should_KeepTheRecipeSize()
    {
        OnlyTraits(("crack", 0f, 0.8f, 1f));
        SetConfig(c => c.GemCarats.KeepRecipeBucket, (c, v) => c.GemCarats.KeepRecipeBucket = v, true);

        ItemStack cut = await CutRound(AppraisedRough(6f));

        Assert.Equal(CutGem, cut.Collectible.Code.ToString());
        Assert.Equal(Mod.config.GemCarats.BucketBounds[1], CaratsOf(cut), 3);
    }

    // By carats a mistake costs weight, and the buff is rolled from what is left.
    [AtlasScenario]
    public async Task Mistakes_Should_CostWeight()
    {
        OnlyTraits(("crack", 0f, 0.8f, 1f));
        SetConfig(c => c.minFineForMistake, (c, v) => c.minFineForMistake = v, 0.1f);
        SetConfig(c => c.maxFineForMistake, (c, v) => c.maxFineForMistake = v, 0.1f);
        float yield = Mod.config.GemCarats.CutYield[CANJWConstants.CUTTING_ROUND];

        ItemStack cut = await CutRound(AppraisedRough(2.4f), mistakes: 3);

        Assert.Equal(CANGemAppraisal.RoundCarats(2.4f * yield * (1 - 0.1f * 3), 0.1f), CaratsOf(cut), 3);
    }

    // A heavier gem rolls a stronger buff: mid·c^k within the spread.
    [AtlasScenario]
    public async Task Buff_Should_FollowTheWeight()
    {
        OnlyTraits(("crack", 0f, 0.8f, 1f));
        string buff = Mod.config.PossibleGemBuffs[GemType].First();
        SetConfig(c => c.PossibleGemBuffs[GemType], (c, v) => c.PossibleGemBuffs[GemType] = v, new HashSet<string> { buff });
        var attrs = Mod.config.BuffAttributesDict[buff];
        SetConfig(c => c.BuffAttributesDict[buff], (c, v) => c.BuffAttributesDict[buff] = v,
            new global::canjewelry.src.Config.BuffAttributes(attrs.MainStatValueRange, null, null)
            {
                MainStatPerCarat = 0.02f, MainStatSpread = 0f, MaxValue = 0f
            });
        float m0 = Mod.config.CuttingAttributesDict[CANJWConstants.CUTTING_ROUND].GrindingBuffIncreaseMultipliers[0];
        float k = Mod.config.GemCarats.BuffExponent;

        ItemStack light = await CutRound(AppraisedRough(2.0f));
        ItemStack heavy = await CutRound(AppraisedRough(6.0f));

        Assert.Equal((float)Math.Round(0.02 * Math.Pow(CaratsOf(light), k), 3) * m0, BuffOf(light), 4);
        Assert.Equal((float)Math.Round(0.02 * Math.Pow(CaratsOf(heavy), k), 3) * m0, BuffOf(heavy), 4);
        Assert.True(BuffOf(heavy) > BuffOf(light));
    }

    [AtlasScenario]
    public async Task Cut_WithCrack_Should_LoseYield()
    {
        OnlyTraits(("crack", 0f, 0.8f, 1f));
        float yield = Mod.config.GemCarats.CutYield[CANJWConstants.CUTTING_ROUND];

        ItemStack cut = await CutRound(AppraisedRough(2.5f, "crack"));

        ITreeAttribute tree = cut.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal(CANGemAppraisal.RoundCarats(2.5f * yield * 0.8f, 0.1f), CANGemAppraisal.GetCarats(tree), 3);
        Assert.Equal(new[] { "crack" }, CANGemAppraisal.GetTraits(tree));
    }

    [AtlasScenario]
    public async Task Cut_WithInclusions_Should_LowerTheBuff_UnlessTurnedOff()
    {
        FixGemBuff(GemType, 0.05f);
        OnlyTraits(("inclusions", 0f, 1f, 0.8f));
        float m0 = Mod.config.CuttingAttributesDict[CANJWConstants.CUTTING_ROUND].GrindingBuffIncreaseMultipliers[0];

        ItemStack flawed = await CutRound(AppraisedRough(2.4f, "inclusions"));
        Assert.Equal(0.05f * m0 * 0.8f, BuffOf(flawed), 4);

        SetConfig(c => c.GemCarats.TraitsAffectBuffs, (c, v) => c.GemCarats.TraitsAffectBuffs = v, false);
        ItemStack ignored = await CutRound(AppraisedRough(2.4f, "inclusions"));
        Assert.Equal(0.05f * m0, BuffOf(ignored), 4);
    }

    private static float BuffOf(ItemStack cut) =>
        (cut.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)!.value[0];

    [AtlasScenario]
    public async Task CutEvent_Should_CarryRoughAndCutWeight()
    {
        OnlyTraits(("crack", 0f, 0.8f, 1f));
        CutEvent? seen = null;
        Action<CutEvent> handler = e => seen = e;
        Mod.Instance.OnCutGem += handler;
        ItemStack cut;
        try
        {
            cut = await CutRound(AppraisedRough(2.4f));
        }
        finally
        {
            Mod.Instance.OnCutGem -= handler;
        }

        Assert.NotNull(seen);
        Assert.Equal(2.4f, seen!.RoughCarats, 3);
        Assert.Equal(CANGemAppraisal.GetCarats(cut.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)), seen.CutCarats, 3);
    }

    // ---- splitting ---------------------------------------------------------------------------

    // A rough gem whose crack the loupe has found.
    private ItemStack RoughWithFoundCrack(float carats)
    {
        ItemStack rough = Stack("canjewelry:gem-rough-normal-" + GemType);
        CANGemAppraisal.SetWeight(rough.Attributes, carats, new[] { "crack", "cloudy" });
        CANGemAppraisal.SetKnowledge(rough.Attributes, CANGemAppraisal.Loupe, new[] { carats - 0.5f, carats + 0.5f }, new[] { "crack" });
        return rough;
    }

    private static List<ItemStack> RoughGems(IEnumerable<ItemSlot> slots) => slots
        .Select(s => s.Itemstack)
        .Where(s => s?.Collectible.Code.Path.StartsWith("gem-rough-") == true)
        .ToList();

    /// <summary>Puts the gem on a fresh table, picks the "cleave in halves" recipe and finishes the groove.</summary>
    private async Task<ITestPlayer> CleaveInHalves(ItemStack rough)
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        var table = Place<BlockEntityGemCuttingTable>(TableCode, pos);
        int recipeId = rough.Collectible.GetBehavior<CuttableCB>().GetMatchingRecipes(rough)
            .First(r => r.Output.ResolvedItemstack.Collectible.Code.Path == "cancleave-halves").RecipeId;
        Hold(player, rough);
        Assert.True(Put(table, player, pos));
        table.OnReceivedClientPacket(player.Player, (int)EnumAnvilPacket.SelectRecipe, SerializerUtil.Serialize(recipeId));
        table.Voxels = RecipeGrid(table);
        table.CheckIfFinished(player.Player);
        Assert.Null(table.WorkItemStack);
        return player;
    }

    // Along the found crack: little loss, no crack in the pieces, cloudiness in both.
    [AtlasScenario]
    public async Task Cleave_AlongAFoundCrack_Should_GiveCleanRoughPieces()
    {
        ITestPlayer player = await CleaveInHalves(RoughWithFoundCrack(6.0f));

        List<ItemStack> pieces = RoughGems(CarriedSlots(player));
        Assert.Equal(2, pieces.Sum(p => p.StackSize));
        Assert.All(pieces, p => Assert.Equal(new[] { "cloudy" }, CANGemAppraisal.GetTraits(p.Attributes)));
    }

    // Too light to part: the gem comes back whole, its truth kept.
    [AtlasScenario]
    public async Task Cleave_TooSmall_Should_GiveTheGemBackWhole()
    {
        ITestPlayer player = await CleaveInHalves(RoughWithFoundCrack(1.5f));

        ItemStack back = Assert.Single(RoughGems(CarriedSlots(player)));
        Assert.Equal(1.5f, CANGemAppraisal.GetCarats(back.Attributes), 3);
    }

    // ---- conversion recipes ------------------------------------------------------------------

    // Converting to the vanilla gem and back used to drop the weight, which rerolled it.
    [AtlasScenario]
    public async Task ConversionRecipes_Should_CarryWeight_ButNotPotentialOntoOurGem()
    {
        await World.Ticks(1);
        // Our one-slot recipes turning a rough gem into the same gem of the other kind.
        var conversions = World.Api.World.GridRecipes
            .Where(r => r.Width == 1 && r.Height == 1
                && r.Ingredients != null && r.Ingredients.TryGetValue("D", out var d)
                && d.Code != null && d.Code.Path.StartsWith("gem-") && d.Code.Path.Contains("rough")
                && (d.Code.Domain == "canjewelry" || d.Code.Domain == "game"))
            .ToList();
        Assert.NotEmpty(conversions);
        Assert.All(conversions, r => Assert.Equal("D", r.CopyAttributesFrom));

        ItemStack ours = AppraisedRough(2.2f, "crack");
        ours.Attributes.SetString("potential", "low");
        var output = new DummySlot(ours);
        EnumHandling handling = EnumHandling.PassThrough;
        ours.Collectible.GetBehavior<AppraisalCB>().OnCreatedByCrafting(new ItemSlot[0], output, conversions[0], ref handling);

        Assert.False(output.Itemstack.Attributes.HasAttribute("potential"));
        Assert.Equal(2.2f, CANGemAppraisal.GetCarats(output.Itemstack.Attributes), 3);

        ItemStack vanilla = Stack("game:gem-diamond-rough");
        vanilla.Attributes.SetString("potential", "low");
        var vanillaOut = new DummySlot(vanilla);
        vanilla.Collectible.GetBehavior<AppraisalCB>().OnCreatedByCrafting(new ItemSlot[0], vanillaOut, conversions[0], ref handling);
        Assert.Equal("low", vanillaOut.Itemstack.Attributes.GetString("potential"));
    }

    // ---- tooltips ----------------------------------------------------------------------------

    [AtlasScenario]
    public async Task Tooltips_Should_ShowWhatIsKnown_NeverTheTruth()
    {
        await World.Ticks(1);
        IWorldAccessor world = World.Api.World;

        float[] chipped = Mod.config.GemCarats.RoughRanges["chipped"];
        string fresh = Tooltip(Stack(Rough), world);
        Assert.Contains(Text("appraisal-none-range", AppraisalCB.FormatCarats(chipped[0]), AppraisalCB.FormatCarats(chipped[1])), fresh);

        ItemStack rolled = Stack(Rough);
        CANGemAppraisal.SetWeight(rolled.Attributes, 2.3f, new[] { "crack" });
        string unseen = Tooltip(rolled, world);
        // The table rolled it, nobody looked: the quality range and no trait.
        Assert.Contains(Text("appraisal-none-range", AppraisalCB.FormatCarats(chipped[0]), AppraisalCB.FormatCarats(chipped[1])), unseen);
        Assert.DoesNotContain(Text("gem-trait-crack"), unseen);

        string seen = Tooltip(AppraisedRough(2.3f, "crack"), world);
        Assert.Contains(Text("appraisal-loupe", "2.0–2.6"), seen);
        Assert.Contains(Text("appraisal-no-visible-traits"), seen);

        ItemStack cut = Stack(CutGem);
        var tree = new TreeAttribute();
        tree.SetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND);
        CANGemAppraisal.SetWeight(tree, 1.2f, new[] { "cloudy" });
        cut.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
        Assert.Contains(Text("gem-carats", "1.2"), Tooltip(cut, world));

        // A legacy cut gem has no weight line and still renders.
        ItemStack legacy = LegacyCutGem();
        Assert.False(string.IsNullOrEmpty(Tooltip(legacy, world)));
    }

    [AtlasScenario]
    public async Task Tooltip_WithoutCaratsConfig_Should_NotThrow()
    {
        await World.Ticks(1);
        SetConfig(c => c.GemCarats, (c, v) => c.GemCarats = v, null!);

        string text = Tooltip(Stack(Rough), World.Api.World);
        Assert.False(string.IsNullOrEmpty(text));
    }
}
