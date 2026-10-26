using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>The /canjewelry server commands: what each one changes, and when it refuses.</summary>
[Trait("Category", "Commands")]
public class CommandScenarios : ConfigScenarioBase
{
    // The engine's wording for a RequiresPlayer refusal, see Atlas CommandResult.
    private const string CallerMustBePlayer = "Caller must be player";

    /// <summary>Known cutting settings for the scenario, put back afterwards.</summary>
    private void KnownCuttingSettings()
    {
        SetConfig(c => c.cuttingAccessMode, (c, v) => c.cuttingAccessMode = v, "enabled");
        SetConfig(c => c.cuttingVoxelsPerClick, (c, v) => c.cuttingVoxelsPerClick = v, 1);
        SetConfig(c => c.cuttingDurabilityPerVoxel, (c, v) => c.cuttingDurabilityPerVoxel = v, true);
        SetConfig(c => c.cuttingInstantComplete, (c, v) => c.cuttingInstantComplete = v, false);
        SetConfig(c => c.cuttingSpareRecipeVoxels, (c, v) => c.cuttingSpareRecipeVoxels = v, false);
        SetConfig(c => c.cuttingHoldStrikeIntervalMs, (c, v) => c.cuttingHoldStrikeIntervalMs = v, 0);
        SetConfig(c => c.cuttingWhitelist, (c, v) => c.cuttingWhitelist = v, new HashSet<string>());
        SetConfig(c => c.cuttingBlacklist, (c, v) => c.cuttingBlacklist = v, new HashSet<string>());
        InvalidateConfigOnCleanup();
    }

    private static Task<CommandResult> AsConsole(IWorldSession world, string command) => world.ExecuteCommand(command);

    [AtlasScenario]
    public async Task Commands_Should_RefusePlayerWithoutControlServer()
    {
        ITestPlayer player = await Join();
        KnownCuttingSettings();

        using (player.WithRole("suplayer"))
        {
            CommandResult show = await player.ExecuteCommand("/canjewelry cutting");
            CommandResult set = await player.ExecuteCommand("/canjewelry cutting voxels 3");

            Assert.False(show.Ok);
            Assert.Equal("noprivilege", show.ErrorCode);
            Assert.False(set.Ok);
        }
        Assert.Equal(1, Mod.config.cuttingVoxelsPerClick);
    }

    [AtlasScenario]
    public async Task Cutting_Should_DescribeSettings()
    {
        KnownCuttingSettings();
        Mod.config.cuttingBlacklist.Add("Bob");

        CommandResult result = await AsConsole(World, "/canjewelry cutting");

        Assert.True(result.Ok, result.Message);
        Assert.Equal(
            "gem cutting: mode enabled, voxels per click 1, durability per voxel True, instant complete False, spare recipe voxels False, hold interval 0ms"
            + "\nwhitelist: (empty)\nblacklist: Bob",
            result.Message);
    }

    [AtlasScenario]
    public async Task CuttingVoxels_Should_SetInRange_AndRefuseOutside()
    {
        KnownCuttingSettings();

        CommandResult set = await AsConsole(World, "/canjewelry cutting voxels 3");
        Assert.True(set.Ok, set.Message);
        Assert.Equal(3, Mod.config.cuttingVoxelsPerClick);
        Assert.Contains("voxels per click 3", set.Message);

        Assert.False((await AsConsole(World, "/canjewelry cutting voxels 9")).Ok);
        Assert.False((await AsConsole(World, "/canjewelry cutting voxels 0")).Ok);
        Assert.Equal(3, Mod.config.cuttingVoxelsPerClick);
    }

    [AtlasScenario]
    public async Task CuttingSwitches_Should_SetTheirValues()
    {
        KnownCuttingSettings();

        Assert.True((await AsConsole(World, "/canjewelry cutting durability false")).Ok);
        Assert.True((await AsConsole(World, "/canjewelry cutting instant true")).Ok);
        Assert.True((await AsConsole(World, "/canjewelry cutting spare true")).Ok);

        Assert.False(Mod.config.cuttingDurabilityPerVoxel);
        Assert.True(Mod.config.cuttingInstantComplete);
        Assert.True(Mod.config.cuttingSpareRecipeVoxels);
    }

    [AtlasScenario]
    public async Task CuttingHold_Should_SetInRange_AndRefuseOutside()
    {
        KnownCuttingSettings();

        Assert.True((await AsConsole(World, "/canjewelry cutting hold 500")).Ok);
        Assert.Equal(500, Mod.config.cuttingHoldStrikeIntervalMs);
        Assert.False((await AsConsole(World, "/canjewelry cutting hold 2001")).Ok);
        Assert.False((await AsConsole(World, "/canjewelry cutting hold -1")).Ok);
        Assert.Equal(500, Mod.config.cuttingHoldStrikeIntervalMs);
    }

    [AtlasScenario]
    public async Task CuttingMode_Should_TakeKnownModesOnly()
    {
        KnownCuttingSettings();

        foreach (string mode in new[] { "whitelist", "blacklist", "disabled", "enabled" })
        {
            CommandResult result = await AsConsole(World, "/canjewelry cutting mode " + mode);
            Assert.True(result.Ok, result.Message);
            Assert.Equal(mode, Mod.config.cuttingAccessMode);
        }

        Assert.False((await AsConsole(World, "/canjewelry cutting mode everyone")).Ok);
        Assert.Equal("enabled", Mod.config.cuttingAccessMode);
    }

    [AtlasScenario]
    public async Task CuttingLists_Should_ToggleNames()
    {
        KnownCuttingSettings();

        foreach (string list in new[] { "whitelist", "blacklist" })
        {
            HashSet<string> Names() => list == "whitelist" ? Mod.config.cuttingWhitelist : Mod.config.cuttingBlacklist;

            CommandResult added = await AsConsole(World, $"/canjewelry cutting {list} Alice");
            Assert.True(added.Ok, added.Message);
            Assert.Equal($"Alice added to the {list}. Now: Alice", added.Message);
            Assert.Contains("Alice", Names());

            CommandResult removed = await AsConsole(World, $"/canjewelry cutting {list} Alice");
            Assert.Equal($"Alice removed from the {list}. Now: (empty)", removed.Message);
            Assert.Empty(Names());

            CommandResult shown = await AsConsole(World, $"/canjewelry cutting {list}");
            Assert.True(shown.Ok, shown.Message);
            Assert.Equal($"{list}: (empty)", shown.Message);
        }
    }

    // A hand written config can hold null for a list.
    [AtlasScenario]
    public async Task CuttingLists_Should_SurviveNullInConfig()
    {
        KnownCuttingSettings();
        SetConfig(c => c.cuttingWhitelist, (c, v) => c.cuttingWhitelist = v, null!);
        SetConfig(c => c.cuttingBlacklist, (c, v) => c.cuttingBlacklist = v, null!);

        Assert.Equal("whitelist: (empty)", (await AsConsole(World, "/canjewelry cutting whitelist")).Message);
        Assert.True((await AsConsole(World, "/canjewelry cutting")).Ok);
        Assert.NotNull(Mod.config.cuttingWhitelist);
        Assert.NotNull(Mod.config.cuttingBlacklist);
    }

    [AtlasScenario]
    public async Task CuttingChange_Should_BeWrittenToTheConfigFile()
    {
        KnownCuttingSettings();

        Assert.True((await AsConsole(World, "/canjewelry cutting hold 750")).Ok);

        Assert.Equal(750, (int)ConfigFiles.Read()["cuttingHoldStrikeIntervalMs"]!);
    }

    [AtlasScenario]
    public async Task CuttingGui_Should_NeedAPlayer()
    {
        CommandResult result = await AsConsole(World, "/canjewelry cutting gui");

        Assert.False(result.Ok);
        Assert.Equal(CallerMustBePlayer, result.Raw.StatusMessage);
    }

    [AtlasScenario]
    public async Task ClearBuffs_Should_RemoveGemStatsOfNamedPlayer()
    {
        ITestPlayer player = await Join();
        string name = player.Player.PlayerName;
        player.Entity.Stats.Set("walkspeed", "canencrusted", 0.1f, true);
        Assert.True(player.Entity.Stats["walkspeed"].ValuesByKey.ContainsKey("canencrusted"));
        player.Client.Clear();

        CommandResult result = await player.ExecuteCommand("/canjewelry clearbuffs " + name);

        Assert.True(result.Ok, result.Message);
        Assert.False(player.Entity.Stats["walkspeed"].ValuesByKey.ContainsKey("canencrusted"));
        await World.Ticks(1);
        Assert.Contains(player.Client.ChatLines(), l => l.Contains("Buffs were cleared for " + name));
    }

    [AtlasScenario]
    public async Task ClearBuffs_Should_DoNothingForUnknownName()
    {
        ITestPlayer player = await Join();
        player.Client.Clear();

        CommandResult result = await player.ExecuteCommand("/canjewelry clearbuffs nobody-here");

        Assert.True(result.Ok, result.Message);
        await World.Ticks(1);
        Assert.DoesNotContain(player.Client.ChatLines(), l => l.Contains("Buffs were cleared"));
    }

    [AtlasScenario]
    public async Task ReapplyBuffs_Should_ReportForNamedPlayer()
    {
        ITestPlayer player = await Join();
        string name = player.Player.PlayerName;
        player.Client.Clear();

        CommandResult result = await player.ExecuteCommand("/canjewelry reapplybuffs " + name);
        CommandResult unknown = await player.ExecuteCommand("/canjewelry reapplybuffs nobody-here");

        Assert.True(result.Ok, result.Message);
        Assert.True(unknown.Ok, unknown.Message);
        await World.Ticks(1);
        Assert.Single(player.Client.ChatLines(), l => l.Contains("Buffs were reapplied for"));
        Assert.Contains(player.Client.ChatLines(), l => l.Contains("Buffs were reapplied for " + name));
    }

    [AtlasScenario]
    public async Task BuffCommands_Should_NeedAPlayer()
    {
        foreach (string command in new[] { "clearbuffs x", "reapplybuffs x", "setgembuffs round walkspeed 1" })
        {
            CommandResult result = await AsConsole(World, "/canjewelry " + command);
            Assert.False(result.Ok, command);
            Assert.Equal(CallerMustBePlayer, result.Raw.StatusMessage);
        }
    }

    private async Task<ItemStack?> SetGemBuffs(ITestPlayer player, string command, EnumGameMode mode = EnumGameMode.Creative,
        bool holding = true, bool expectOk = true)
    {
        ItemSlot hand = player.Player.InventoryManager.ActiveHotbarSlot;
        EnumGameMode before = player.Player.WorldData.CurrentGameMode;
        player.Player.WorldData.CurrentGameMode = mode;
        hand.Itemstack = holding ? Stack($"canjewelry:gem-cut-normal-{GemType}") : null;
        try
        {
            CommandResult result = await player.ExecuteCommand(command);
            Assert.True(result.Ok == expectOk, command + ": " + result.Message);
            return hand.Itemstack;
        }
        finally
        {
            hand.Itemstack = null;
            player.Player.WorldData.CurrentGameMode = before;
        }
    }

    [AtlasScenario]
    public async Task SetGemBuffs_Should_WriteCutAndBuffsOnHeldItem()
    {
        ITestPlayer player = await Join();

        ItemStack? gem = await SetGemBuffs(player, "/canjewelry sgb pear walkspeed 0.05 hungerrate -0.1");

        ITreeAttribute tree = gem!.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal(CANJWConstants.CUTTING_PEAR, tree.GetString(CANJWConstants.CUTTING_TYPE));
        Assert.Equal(new[] { "walkspeed", "hungerrate" }, (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value);
        Assert.Equal(new[] { 0.05f, -0.1f }, (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value);

        ItemStack? full = await SetGemBuffs(player, "/canjewelry setgembuffs baguette walkspeed 0.02");
        Assert.Equal(CANJWConstants.CUTTING_BAGUETTE, full!.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE).GetString(CANJWConstants.CUTTING_TYPE));
    }

    [AtlasScenario]
    public async Task SetGemBuffs_Should_LeaveItemAlone_WhenItCannotApply()
    {
        ITestPlayer player = await Join();

        ItemStack? survival = await SetGemBuffs(player, "/canjewelry sgb round walkspeed 0.05", EnumGameMode.Survival);
        ItemStack? oddCount = await SetGemBuffs(player, "/canjewelry sgb round walkspeed 0.05 hungerrate");
        ItemStack? notNumber = await SetGemBuffs(player, "/canjewelry sgb round walkspeed fast");

        foreach (ItemStack? gem in new[] { survival, oddCount, notNumber })
        {
            Assert.False(gem!.Attributes.HasAttribute(CANJWConstants.CUT_GEM_TREE));
        }

        // Nothing in hand: succeeds without effect.
        Assert.Null(await SetGemBuffs(player, "/canjewelry sgb round walkspeed 0.05", holding: false));
        // Only the three cuts are accepted.
        await SetGemBuffs(player, "/canjewelry sgb oval walkspeed 0.05", expectOk: false);
    }
}
