using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.api;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// What the server sends on the "canjewelry" channel: the config (gzipped JSON, on request and
/// after a command changed it) and the admin's gem poses.
/// </summary>
[Trait("Category", "Sync")]
public class ConfigSyncScenarios : ConfigScenarioBase
{
    private const string Channel = "canjewelry";

    private static bool IsGzip(byte[]? bytes) => bytes != null && bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b;

    // The client asks for the config; a player that never asks gets none.
    [AtlasScenario]
    public async Task Join_Should_NotPushConfigUnasked()
    {
        ITestPlayer player = await Join();
        await World.Ticks(5);

        Assert.Empty(player.Client.Packets<SyncCANJewelryPacket>(Channel));
    }

    [AtlasScenario]
    public async Task SentConfig_Should_MatchServerConfig()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.cuttingVoxelsPerClick, (c, v) => c.cuttingVoxelsPerClick = v, 5);
        SetConfig(c => c.enableGemVisuals, (c, v) => c.enableGemVisuals = v, false);
        SetConfig(c => c.chance_gem_drop_on_item_broken, (c, v) => c.chance_gem_drop_on_item_broken = v, 0f);
        InvalidateConfigOnCleanup();
        ModInternals.OnConfigReplaced();
        player.Client.Clear();

        Mod.Instance.sendNewValues(player.Player);

        SyncCANJewelryPacket packet = Assert.Single(player.Client.Packets<SyncCANJewelryPacket>(Channel));
        Assert.True(IsGzip(packet.ConfigGz), "the config is not gzipped");
        Assert.True(string.IsNullOrEmpty(packet.GemVisualsHash));

        Config received = JsonConvert.DeserializeObject<Config>(packet.CompressedConfig)!;
        Assert.Equal(5, received.cuttingVoxelsPerClick);
        Assert.False(received.enableGemVisuals);
        Assert.Equal(0f, received.chance_gem_drop_on_item_broken);
        Assert.Equal(Mod.config.config_version, received.config_version);
        Assert.Equal(Mod.config.items_codes_with_socket_count_and_tiers, received.items_codes_with_socket_count_and_tiers);
        Assert.Equal(Mod.config.gem_type_to_buff, received.gem_type_to_buff);
        Assert.Equal(Mod.config.LevelOfSocketByType, received.LevelOfSocketByType);
        ConfigFiles.AssertSameSets(Mod.config.item_groups, received.item_groups);
        // Sent folded, the client expands the "$group" entries itself.
        ConfigFiles.AssertSameSets(Mod.config.buffNameToPossibleItem, ConfigFiles.Expanded(received));
        Assert.Contains(received.buffNameToPossibleItem["fluorite"], e => e == "$melee");
        await World.Ticks(1);
    }

    // The bytes are made once and handed to everyone until the config is replaced.
    [AtlasScenario]
    public async Task ConfigPayload_Should_BeSharedUntilReplaced()
    {
        ITestPlayer first = await Join();
        ITestPlayer second = await Join();
        SetConfig(c => c.cuttingHoldStrikeIntervalMs, (c, v) => c.cuttingHoldStrikeIntervalMs = v, 0);
        InvalidateConfigOnCleanup();
        ModInternals.OnConfigReplaced();
        first.Client.Clear();
        second.Client.Clear();

        Mod.Instance.sendNewValues(first.Player);
        Mod.Instance.sendNewValues(second.Player);
        byte[] a = Assert.Single(first.Client.Packets<SyncCANJewelryPacket>(Channel)).ConfigGz;
        byte[] b = Assert.Single(second.Client.Packets<SyncCANJewelryPacket>(Channel)).ConfigGz;
        Assert.Equal(a, b);

        Mod.config.cuttingHoldStrikeIntervalMs = 300;
        ModInternals.OnConfigReplaced();
        first.Client.Clear();
        Mod.Instance.sendNewValues(first.Player);

        SyncCANJewelryPacket changed = Assert.Single(first.Client.Packets<SyncCANJewelryPacket>(Channel));
        Assert.Equal(300, JsonConvert.DeserializeObject<Config>(changed.CompressedConfig)!.cuttingHoldStrikeIntervalMs);
    }

    // One request on join is answered; another within five seconds is not.
    [AtlasScenario]
    public async Task ConfigRequest_Should_BeThrottledPerPlayer()
    {
        ITestPlayer first = await Join();
        ITestPlayer second = await Join();

        Assert.True(ModInternals.AllowConfigRequest(first.Player));
        Assert.False(ModInternals.AllowConfigRequest(first.Player));
        Assert.True(ModInternals.AllowConfigRequest(second.Player));
        await World.Ticks(1);
    }

    // A command that changes the config sends it to everyone online and writes the file.
    [AtlasScenario]
    public async Task ConfigCommand_Should_BroadcastToEveryone()
    {
        ITestPlayer first = await Join();
        ITestPlayer second = await Join();
        SetConfig(c => c.cuttingVoxelsPerClick, (c, v) => c.cuttingVoxelsPerClick = v, 1);
        InvalidateConfigOnCleanup();
        first.Client.Clear();
        second.Client.Clear();

        CommandResult result = await World.ExecuteCommand("/canjewelry cutting voxels 4");

        Assert.True(result.Ok, result.Message);
        foreach (ITestPlayer player in new[] { first, second })
        {
            SyncCANJewelryPacket packet = Assert.Single(player.Client.Packets<SyncCANJewelryPacket>(Channel));
            Assert.Equal(4, JsonConvert.DeserializeObject<Config>(packet.CompressedConfig)!.cuttingVoxelsPerClick);
        }
        Assert.Equal(4, (int)ConfigFiles.Read()["cuttingVoxelsPerClick"]!);
    }

    // A server without an own pose folder fills it from the mod's assets and hands those poses out.
    [AtlasScenario]
    public async Task ServerPoses_Should_ComeFromTheSeededFolder()
    {
        Assert.True(Directory.Exists(CANGemVisualRegistry.UserRuleDirectory));
        Assert.NotEmpty(Directory.GetFiles(CANGemVisualRegistry.UserRuleDirectory, "*.json"));

        string? poses = ModInternals.ServerGemVisuals;
        Assert.NotNull(poses);
        Assert.Equal(CANGemVisualRegistry.CollectUserRulesJson(World.Api), poses);
        Assert.Equal(CANGemVisualRegistry.Fingerprint(poses), ModInternals.ServerGemVisualsHash);
        Assert.NotEmpty(JArray.Parse(poses!));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ServerPoses_Should_GoOnlyToClientsWithoutThem()
    {
        ITestPlayer player = await Join();
        string hash = ModInternals.ServerGemVisualsHash;
        player.Client.Clear();

        ModInternals.SendGemVisualsIfChanged(player.Player, null);
        GemVisualsPacket packet = Assert.Single(player.Client.Packets<GemVisualsPacket>(Channel));
        Assert.Equal(hash, packet.Hash);
        Assert.True(IsGzip(packet.RulesGz), "the poses are not gzipped");
        Assert.Equal(ModInternals.ServerGemVisuals, packet.Rules);

        // Same fingerprint: nothing to send.
        player.Client.Clear();
        ModInternals.SendGemVisualsIfChanged(player.Player, hash);
        Assert.Empty(player.Client.Packets<GemVisualsPacket>(Channel));

        // A stale fingerprint gets the poses again, the same bytes.
        ModInternals.SendGemVisualsIfChanged(player.Player, "stale");
        Assert.Equal(packet.RulesGz, Assert.Single(player.Client.Packets<GemVisualsPacket>(Channel)).RulesGz);
        await World.Ticks(1);
    }

    // The settings dialogue gets its values in a packet of its own.
    [AtlasScenario]
    public async Task CuttingGui_Should_SendCurrentSettings()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.cuttingVoxelsPerClick, (c, v) => c.cuttingVoxelsPerClick = v, 6);
        SetConfig(c => c.cuttingAccessMode, (c, v) => c.cuttingAccessMode = v, "blacklist");
        SetConfig(c => c.cuttingBlacklist, (c, v) => c.cuttingBlacklist = v, new HashSet<string> { "Bob" });
        SetConfig(c => c.cuttingWhitelist, (c, v) => c.cuttingWhitelist = v, new HashSet<string>());
        player.Client.Clear();

        CommandResult result = await player.ExecuteCommand("/canjewelry cutting gui");

        Assert.True(result.Ok, result.Message);
        CuttingSettingsPacket packet = Assert.Single(player.Client.Packets<CuttingSettingsPacket>(Channel));
        Assert.Equal(6, packet.VoxelsPerClick);
        Assert.Equal("blacklist", packet.AccessMode);
        Assert.Equal(new[] { "Bob" }, packet.Blacklist);
        Assert.Empty(packet.Whitelist);
        Assert.Equal(Mod.config.cuttingDurabilityPerVoxel, packet.DurabilityPerVoxel);
        Assert.Equal(Mod.config.cuttingInstantComplete, packet.InstantComplete);
        Assert.Equal(Mod.config.cuttingSpareRecipeVoxels, packet.SpareRecipeVoxels);
        Assert.Equal(Mod.config.cuttingHoldStrikeIntervalMs, packet.HoldStrikeIntervalMs);
    }
}

/// <summary>
/// The admin's pose folder exists but holds no pose: the shipped poses are not seeded into it and
/// the server has none to send.
/// </summary>
[Trait("Category", "Sync")]
[AtlasDataFiles("fixtures/GemVisualsOptOut", TargetPath = "ModConfig/canjewelry/gemvisuals")]
public class GemVisualsOptOutScenarios : ConfigScenarioBase
{
    private const string Channel = "canjewelry";

    [AtlasScenario]
    public async Task EmptyPoseFolder_Should_NotBeSeeded()
    {
        Assert.Equal(new[] { "empty-pose.json" },
            Directory.GetFiles(CANGemVisualRegistry.UserRuleDirectory, "*.json").Select(Path.GetFileName));
        Assert.Null(ModInternals.ServerGemVisuals);
        Assert.Equal("", ModInternals.ServerGemVisualsHash);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task NoServerPoses_Should_ClearOnlyClientsThatHaveSome()
    {
        ITestPlayer player = await Join();
        player.Client.Clear();

        ModInternals.SendGemVisualsIfChanged(player.Player, null);
        ModInternals.SendGemVisualsIfChanged(player.Player, "");
        Assert.Empty(player.Client.Packets<GemVisualsPacket>(Channel));

        // A client still holding poses of an earlier server is told there are none.
        ModInternals.SendGemVisualsIfChanged(player.Player, "earlier");
        GemVisualsPacket packet = Assert.Single(player.Client.Packets<GemVisualsPacket>(Channel));
        Assert.True(string.IsNullOrEmpty(packet.Hash));
        Assert.Null(packet.Rules);
        await World.Ticks(1);
    }
}
