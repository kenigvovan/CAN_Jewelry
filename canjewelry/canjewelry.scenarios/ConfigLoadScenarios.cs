using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>A server started without a config file writes one with the defaults.</summary>
[Trait("Category", "Config")]
public class ConfigLoadScenarios : ConfigScenarioBase
{
    [AtlasScenario]
    public async Task FreshConfig_Should_BeWrittenWithTheModVersion()
    {
        Assert.True(File.Exists(ConfigFiles.PathOf()), "no canjewelry.json written");
        Assert.Equal(ModVersion, (string?)ConfigFiles.Read()["config_version"]);
        Assert.Equal(ModVersion, Mod.config.config_version);
        Assert.False(File.Exists(ConfigFiles.PathOf("canjewelry_old.json")));
        Assert.Empty(ConfigFiles.Backups());
        Assert.DoesNotContain(World.BootDiagnostics, e => e.Message.Contains("could not read canjewelry.json"));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task FreshConfig_Should_HoldTheDefaults()
    {
        Config c = Mod.config;

        Assert.False(c.debugMode);
        Assert.Equal(0.2f, c.chance_gem_drop_on_item_broken);
        Assert.Equal(8, c.pan_take_per_use);
        Assert.Equal(new Dictionary<string, float> { { "walkspeed", 0.5f }, { "maxhealthExtraPoints", 25 }, { "hungerrate", -0.2f } }, c.max_buff_values);
        Assert.Equal(new[] { "armor", "jewelry", "melee", "mining", "ranged", "shield" }, c.item_groups.Keys.OrderBy(k => k));
        Assert.Contains("biaxe", c.item_groups["melee"]);
        Assert.Empty(c.item_groups["jewelry"]);
        Assert.Equal("wildCropDropRate", c.gem_type_to_buff[GemType]);
        Assert.Equal(new[] { 3, 3, 3 }, c.items_codes_with_socket_count_and_tiers["*knife-generic-steel"]);
        Assert.Equal(new[] { 3, 3 }, c.items_codes_with_socket_count_and_tiers["vanillaarmory:bow-*"]);
        Assert.Equal(3, c.LevelOfSocketByType["canjewelry:cansocket-steel"]);
        Assert.Contains("game:soil-*", c.TemporalGraspBlockList);
        Assert.True(c.panningDrops!.ContainsKey("game:stone-suevite"));
        ConfigFiles.AssertSet(new[] { "walkspeed" }, c.PossibleGemBuffs["diamond"]);
        Assert.NotEmpty(c.BuffAttributesDict);
        Assert.Contains("walkspeed", c.buffs_to_show_gui);
        Assert.Equal(new[] { "green", "blue", "purple" }, c.socketTiersColorsWords);
        // InitColors turns the words into hex on server start.
        Assert.Equal(new[] { "008000", "0000FF", "800080" }, c.socketTiersColors);
        Assert.Equal(0, c.AddMissingDefaults(false));
        await World.Ticks(1);
    }

    // Values that come from the field initializers rather than FillDefaultValues.
    [AtlasScenario]
    public async Task FreshConfig_Should_KeepFieldInitializers()
    {
        Config c = Mod.config;

        Assert.Equal(3f, c.grindTimeOneTick);
        Assert.Equal(4, c.wirehank_per_strap);
        Assert.True(c.TemporalGraspEnabled);
        Assert.Equal(0.01f, c.minFineForMistake);
        Assert.Equal(0.08f, c.maxFineForMistake);
        Assert.False(c.TurnOffBuffs);
        Assert.Equal(1, c.cuttingVoxelsPerClick);
        Assert.True(c.cuttingDurabilityPerVoxel);
        Assert.False(c.cuttingInstantComplete);
        Assert.Equal(0, c.cuttingHoldStrikeIntervalMs);
        Assert.False(c.cuttingSpareRecipeVoxels);
        Assert.Equal("enabled", c.cuttingAccessMode);
        Assert.False(c.add_missing_defaults_on_update);
        Assert.Equal(0.3f, c.minGrinderProcessingSpeed);
        Assert.Equal(10, c.grinderStage1Counter);
        Assert.Equal(20, c.grinderStage2Counter);
        Assert.Equal(0.5f, c.gemExtractionReturnChance);
        Assert.Equal(0.1f, c.jewelryBreakOnExtractionChance);
        Assert.True(c.canExtractSocket);
        Assert.Equal(0.25f, c.socketExtractionReturnChance);
        Assert.False(c.suppressAdornmentsWarning);
        Assert.True(c.enableGemVisuals);
        Assert.True(c.enableAdditionalJewelrySlots);
        await World.Ticks(1);
    }

    // The file uses the short forms: "3,3,3", "$melee", "code chance".
    [AtlasScenario]
    public async Task ConfigFile_Should_UseCompactForms()
    {
        JObject file = ConfigFiles.Read();

        Assert.Equal("3,3,3", (string?)file["items_codes_with_socket_count_and_tiers"]!["*knife-generic-steel"]);
        Assert.All(((JObject)file["items_codes_with_socket_count_and_tiers"]!).Properties(),
            p => Assert.Equal(JTokenType.String, p.Value.Type));
        Assert.Equal("0.02,0.04,0.08", (string?)file["gems_buffs"]!["walkspeed"]);
        Assert.Equal(new[] { "$melee", "exoskeleton-" }, file["buffNameToPossibleItem"]!["fluorite"]!.Values<string>());
        Assert.Equal(new[] { "$armor", "$shield", "exoskeleton-", "firearm-", "walkingstick" },
            file["buffNameToPossibleItem"]!["diamond"]!.Values<string>());
        Assert.Equal("canjewelry:gem-rough-chipped-malachite 0.005", (string?)file["gems_drops_table"]!["ore-*-malachite-*"]![0]);
        Assert.Equal("canjewelry:gem-rough-normal-diamond 0.2", (string?)file["panningDrops"]!["game:stone-suevite"]![0]);
        Assert.Equal("green,blue,purple", (string?)file["socketTiersColorsWords"]);
        Assert.Equal(JTokenType.String, file["buffs_to_show_gui"]!.Type);
        Assert.Equal(JTokenType.String, file["TemporalGraspBlockList"]!.Type);
        Assert.Equal(JTokenType.String, file["PossibleGemBuffs"]!["diamond"]!.Type);
        Assert.Equal(JTokenType.String, file["BuffAttributesDict"]!["walkspeed"]!["MainStatValueRange"]!.First!.First!.Type);
        Assert.All(((JObject)file["CuttingAttributesDict"]!).Properties(),
            p => Assert.Equal(JTokenType.String, p.Value["GrindingBuffIncreaseMultipliers"]!.Type));
        await World.Ticks(1);
    }

    // Read back, the file gives the config the server runs on.
    [AtlasScenario]
    public async Task ConfigFile_Should_ReadBackToTheLoadedConfig()
    {
        Config server = Mod.config;
        Config read = JsonConvert.DeserializeObject<Config>(File.ReadAllText(ConfigFiles.PathOf()))!;

        Assert.Equal(server.config_version, read.config_version);
        ConfigFiles.AssertSameSets(server.buffNameToPossibleItem, ConfigFiles.Expanded(read));
        ConfigFiles.AssertSameSets(server.item_groups, read.item_groups);
        Assert.Equal(server.items_codes_with_socket_count_and_tiers, read.items_codes_with_socket_count_and_tiers);
        Assert.Equal(server.gems_buffs, read.gems_buffs);
        Assert.Equal(server.gem_type_to_buff, read.gem_type_to_buff);
        Assert.Equal(server.LevelOfSocketByType, read.LevelOfSocketByType);
        Assert.True(server.TemporalGraspBlockList.SetEquals(read.TemporalGraspBlockList));
        Assert.True(server.buffs_to_show_gui.SetEquals(read.buffs_to_show_gui));
        Assert.Equal(server.socketTiersColorsWords, read.socketTiersColorsWords);

        Assert.Equal(server.gems_drops_table.Keys, read.gems_drops_table.Keys);
        foreach (var pair in server.gems_drops_table)
        {
            Assert.Equal(pair.Value.Select(DropKey), read.gems_drops_table[pair.Key].Select(DropKey));
        }

        Assert.Equal(server.panningDrops!.Keys, read.panningDrops!.Keys);
        foreach (var pair in server.panningDrops)
        {
            Assert.Equal(pair.Value.Select(PanKey).OrderBy(k => k), read.panningDrops[pair.Key].Select(PanKey).OrderBy(k => k));
        }

        Assert.Equal(server.BuffAttributesDict.Keys, read.BuffAttributesDict.Keys);
        foreach (var pair in server.BuffAttributesDict)
        {
            Assert.Equal(pair.Value.MainStatValueRange, read.BuffAttributesDict[pair.Key].MainStatValueRange);
            Assert.Equal(pair.Value.SecondaryStatValueRange, read.BuffAttributesDict[pair.Key].SecondaryStatValueRange);
        }
        foreach (var pair in server.CuttingAttributesDict)
        {
            Assert.Equal(pair.Value.GrindingBuffIncreaseMultipliers, read.CuttingAttributesDict[pair.Key].GrindingBuffIncreaseMultipliers);
        }
        await World.Ticks(1);
    }

    private static string DropKey(Config.DropInfo d) =>
        $"{d.TypeCollectable}|{d.NameCollectable}|{d.avg}|{d.var}|{d.LastDrop}|{d.DropModbyStat}|{d.attributes}";

    private static string PanKey(CANPanningDrop d) => $"{d.Code}|{d.Type}|{d.StackSize}|{d.Chance?.avg}";

    // The melee group as the code declares it, with the entry the vanilla big axes need.
    [AtlasScenario]
    public async Task MeleeGroup_Should_FoldAndExpand()
    {
        HashSet<string> melee = ModInternals.ActiveItemGroups["melee"];
        Assert.Contains("biaxe", melee);

        var withExtra = new HashSet<string>(melee) { "zzz-extra" };
        string written = JsonConvert.SerializeObject(withExtra, new ItemGroupSetConverter());
        Assert.Equal("[\"$melee\",\"zzz-extra\"]", written);

        var partial = new HashSet<string>(melee);
        partial.Remove("biaxe");
        Assert.DoesNotContain("$melee", JsonConvert.SerializeObject(partial, new ItemGroupSetConverter()));

        var cfg = new Config
        {
            item_groups = new Dictionary<string, HashSet<string>> { { "melee", new HashSet<string>(melee) } },
            buffNameToPossibleItem = new Dictionary<string, HashSet<string>> { { "fluorite", new HashSet<string> { "$melee", "knife" } } }
        };
        Assert.Contains("biaxe", ConfigFiles.Expanded(cfg)["fluorite"]);
        Assert.Contains("knife", ConfigFiles.Expanded(cfg)["fluorite"]);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task Converters_Should_ReadEveryAcceptedForm()
    {
        var ints = new CompactIntArrayConverter();
        Assert.Equal(new[] { 3, 2 }, JsonConvert.DeserializeObject<int[]>("\"3, 2\"", ints));
        Assert.Equal(new[] { 1, 2 }, JsonConvert.DeserializeObject<int[]>("[1,2]", ints));
        Assert.Equal(new[] { 3 }, JsonConvert.DeserializeObject<int[]>("3", ints));
        Assert.Empty(JsonConvert.DeserializeObject<int[]>("\"\"", ints)!);
        Assert.Equal("\"3,3,3\"", JsonConvert.SerializeObject(new[] { 3, 3, 3 }, ints));
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<int[]>("\"3,x\"", ints));

        var floats = new CompactFloatArrayConverter();
        Assert.Equal(new[] { 0.01f, 0.03f }, JsonConvert.DeserializeObject<float[]>("\"0.01, 0.03\"", floats));
        Assert.Equal(new[] { 0.01f, 0.03f }, JsonConvert.DeserializeObject<float[]>("[0.01, 0.03]", floats));
        Assert.Equal(new[] { 0.5f }, JsonConvert.DeserializeObject<float[]>("0.5", floats));
        Assert.Equal("\"0.01,0.03\"", JsonConvert.SerializeObject(new[] { 0.01f, 0.03f }, floats));
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<float[]>("\"0.1,y\"", floats));

        var set = new CompactStringSetConverter();
        ConfigFiles.AssertSet(new[] { "a", "b", "c" }, JsonConvert.DeserializeObject<HashSet<string>>("\"a, b,,c\"", set));
        ConfigFiles.AssertSet(new[] { "a", "b" }, JsonConvert.DeserializeObject<HashSet<string>>("[\"a\", \" b \"]", set));
        Assert.Equal("\"a,b\"", JsonConvert.SerializeObject(new HashSet<string> { "a", "b" }, set));
        // A name with the separator in it keeps the array form.
        Assert.Equal("[\"a,b\"]", JsonConvert.SerializeObject(new HashSet<string> { "a,b" }, set));

        var array = new CompactStringArrayConverter();
        Assert.Equal(new[] { "x", "y" }, JsonConvert.DeserializeObject<string[]>("\"x,y\"", array));
        Assert.Equal("\"x,y\"", JsonConvert.SerializeObject(new[] { "x", "y" }, array));

        var tiers = new TierValuesConverter();
        var run = JsonConvert.DeserializeObject<Dictionary<string, float>>("\"0.02,0.04\"", tiers)!;
        Assert.Equal(new Dictionary<string, float> { { "1", 0.02f }, { "2", 0.04f } }, run);
        Assert.Equal("\"0.02,0.04\"", JsonConvert.SerializeObject(run, tiers));
        var gap = new Dictionary<string, float> { { "1", 0.1f }, { "3", 0.3f } };
        JObject gapWritten = JObject.Parse(JsonConvert.SerializeObject(gap, tiers));
        Assert.Equal(0.1f, (float)gapWritten["1"]!);
        Assert.Equal(0.3f, (float)gapWritten["3"]!);
        Assert.Equal(gap, JsonConvert.DeserializeObject<Dictionary<string, float>>(gapWritten.ToString(), tiers));
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<Dictionary<string, float>>("\"0.1,z\"", tiers));

        var groups = new ItemGroupSetConverter();
        ConfigFiles.AssertSet(new[] { "$melee" }, JsonConvert.DeserializeObject<HashSet<string>>("\"$melee\"", groups));
        ConfigFiles.AssertSet(new[] { "a" }, JsonConvert.DeserializeObject<HashSet<string>>("[\" a \", \"\", null]", groups));
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<HashSet<string>>("5", groups));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task DropConverters_Should_KeepShortAndLongForms()
    {
        Config.DropInfo shortDrop = JsonConvert.DeserializeObject<Config.DropInfo>("\"canjewelry:gem-rough-chipped-malachite 0.25\"")!;
        Assert.Equal(EnumItemClass.Item, shortDrop.TypeCollectable);
        Assert.Equal("canjewelry:gem-rough-chipped-malachite", shortDrop.NameCollectable);
        Assert.Equal(0.25f, shortDrop.avg);
        Assert.Equal(0f, shortDrop.var);
        Assert.True(shortDrop.LastDrop);
        Assert.Equal(DropInfoConverter.DEFAULT_DROP_MOD_STAT, shortDrop.DropModbyStat);
        Assert.Equal("\"canjewelry:gem-rough-chipped-malachite 0.25\"", JsonConvert.SerializeObject(shortDrop));

        Config.DropInfo longDrop = JsonConvert.DeserializeObject<Config.DropInfo>(
            "{\"typecollectable\": \"block\", \"namecollectable\": \"game:gravel-granite\", \"avg\": 0.5, \"var\": 0.1}")!;
        Assert.Equal(EnumItemClass.Block, longDrop.TypeCollectable);
        Assert.True(longDrop.LastDrop);
        Assert.Equal("", longDrop.DropModbyStat);
        JToken longWritten = JToken.Parse(JsonConvert.SerializeObject(longDrop));
        Assert.Equal(JTokenType.Object, longWritten.Type);
        Assert.Equal("game:gravel-granite", (string?)longWritten["NameCollectable"]);
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<Config.DropInfo>("\"nochance\""));

        CANPanningDrop shortPan = JsonConvert.DeserializeObject<CANPanningDrop>("\"canjewelry:gem-rough-normal-diamond 0.2\"")!;
        Assert.Equal(new AssetLocation("canjewelry:gem-rough-normal-diamond"), shortPan.Code);
        Assert.Equal(EnumItemClass.Item, shortPan.Type);
        Assert.Equal(1, shortPan.StackSize);
        Assert.Equal(0.2f, shortPan.Chance.avg);
        Assert.Equal("\"canjewelry:gem-rough-normal-diamond 0.2\"", JsonConvert.SerializeObject(shortPan));

        var gated = new CANPanningDrop
        {
            Code = new AssetLocation("canjewelry:gem-rough-normal-diamond"),
            Type = EnumItemClass.Item,
            StackSize = 2,
            Chance = new NatFloat(0.3f, 0f, EnumDistribution.UNIFORM),
            requiresPerk = "some.perk"
        };
        string gatedJson = JsonConvert.SerializeObject(gated);
        Assert.StartsWith("{", gatedJson);
        CANPanningDrop gatedBack = JsonConvert.DeserializeObject<CANPanningDrop>(gatedJson)!;
        Assert.Equal(2, gatedBack.StackSize);
        Assert.Equal("some.perk", gatedBack.requiresPerk);
        Assert.Equal(0.3f, gatedBack.Chance.avg);
        Assert.ThrowsAny<JsonException>(() => JsonConvert.DeserializeObject<CANPanningDrop>("\"canjewelry:x abc\""));
        await World.Ticks(1);
    }
}

/// <summary>
/// A canjewelry.json that is not valid JSON: kept aside under a timestamped name, and the server
/// starts on the defaults.
/// </summary>
[Trait("Category", "Config")]
[AtlasDataFiles("fixtures/UnreadableConfig", TargetPath = "ModConfig")]
public class UnreadableConfigScenarios : ConfigScenarioBase
{
    [AtlasScenario]
    public async Task UnreadableConfig_Should_BeBackedUp_AndReplacedWithDefaults()
    {
        string[] backups = ConfigFiles.Backups("canjewelry_unreadable_*.json.bak");
        Assert.Single(backups);
        Assert.Equal(ConfigFiles.Fixture("UnreadableConfig", "canjewelry.json"), File.ReadAllText(backups[0]));

        Assert.Contains(World.BootDiagnostics, e =>
            e.Level == EnumLogType.Error && e.Message.Contains("could not read canjewelry.json"));

        // Defaults, not the values of the broken file.
        Assert.False(Mod.config.debugMode);
        Assert.Equal(0.2f, Mod.config.chance_gem_drop_on_item_broken);
        Assert.Equal(8, Mod.config.pan_take_per_use);
        Assert.Equal(ModVersion, Mod.config.config_version);

        JObject rewritten = ConfigFiles.Read();
        Assert.Equal(ModVersion, (string?)rewritten["config_version"]);
        Assert.Equal(8, (int)rewritten["pan_take_per_use"]!);
        await World.Ticks(1);
    }
}

/// <summary>
/// A config written by an older version: backed up once under its version, then topped up with
/// what is missing while the admin's own values stay.
/// </summary>
[Trait("Category", "Config")]
[AtlasDataFiles("fixtures/OutdatedConfig", TargetPath = "ModConfig")]
public class OutdatedConfigScenarios : ConfigScenarioBase
{
    [AtlasScenario]
    public async Task OutdatedConfig_Should_BeBackedUpUnderItsVersion()
    {
        string backup = ConfigFiles.PathOf("canjewelry_0.0.1.json.bak");
        Assert.True(File.Exists(backup), "no backup of the 0.0.1 config");
        Assert.Equal(ConfigFiles.Fixture("OutdatedConfig", "canjewelry.json"), File.ReadAllText(backup));
        Assert.Empty(ConfigFiles.Backups("canjewelry_unreadable_*.json.bak"));

        Assert.Equal(ModVersion, Mod.config.config_version);
        Assert.Equal(ModVersion, (string?)ConfigFiles.Read()["config_version"]);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task OutdatedConfig_Should_KeepAdminValues()
    {
        Config c = Mod.config;

        Assert.True(c.debugMode);
        // An explicit 0 means "never drop" and survives the update.
        Assert.Equal(0f, c.chance_gem_drop_on_item_broken);
        Assert.Equal(7.5f, c.grindTimeOneTick);
        Assert.Equal("whitelist", c.cuttingAccessMode);
        Assert.Equal(new Dictionary<string, float> { { "walkspeed", 0.9f } }, c.max_buff_values);
        Assert.Equal(new[] { "testgroup", "melee" }.OrderBy(k => k), c.item_groups.Keys.OrderBy(k => k));
        Assert.Equal(new Dictionary<string, float> { { "1", 0.1f }, { "2", 0.2f }, { "3", 0.3f } }, c.gems_buffs["walkspeed"]);
        Assert.Equal(new Dictionary<string, float> { { "1", -0.01f }, { "3", -0.03f } }, c.gems_buffs["hungerrate"]);
        Assert.Equal(2, c.gems_buffs.Count);
        ConfigFiles.AssertSet(new[] { "walkspeed", "hungerrate" }, c.buffs_to_show_gui);
        ConfigFiles.AssertSet(new[] { "walkspeed", "hungerrate" }, c.PossibleGemBuffs[GemType]);
        Assert.Single(c.PossibleGemBuffs);
        Assert.Equal(new Dictionary<string, int> { { "canjewelry:cansocket-steel", 2 } }, c.LevelOfSocketByType);
        Assert.Equal(new[] { "red", "notacolour" }, c.socketTiersColorsWords);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task OutdatedConfig_Should_GetMissingDefaults()
    {
        Config c = Mod.config;

        // A zero take per use is read as "not set".
        Assert.Equal(8, c.pan_take_per_use);
        Assert.Equal("wildCropDropRate", c.gem_type_to_buff[GemType]);
        Assert.NotEmpty(c.BuffAttributesDict);
        Assert.Contains("game:soil-*", c.TemporalGraspBlockList);
        Assert.True(c.panningDrops!.ContainsKey("game:stone-suevite"));
        Assert.Equal(new[] { 3, 3 }, c.items_codes_with_socket_count_and_tiers["vanillaarmory:bow-*"]);
        // Keys the file did not have keep their field initializers.
        Assert.Equal(0.01f, c.minFineForMistake);
        Assert.Equal(1, c.cuttingVoxelsPerClick);
        Assert.True(c.enableGemVisuals);
        Assert.Equal(10, c.grinderStage1Counter);
        Assert.Equal(0.5f, c.gemExtractionReturnChance);
        // Only "red" names a colour; the other word is skipped with a warning.
        Assert.Equal(new[] { "FF0000" }, c.socketTiersColors);
        Assert.Contains(World.BootDiagnostics, e => e.Level == EnumLogType.Warning && e.Message.Contains("notacolour"));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task OutdatedConfig_Should_ReadEveryShortForm()
    {
        var tiers = Mod.config.items_codes_with_socket_count_and_tiers;

        Assert.Equal(new[] { 2, 1 }, tiers["*knife-generic-steel"]);
        Assert.Equal(new[] { 1, 1 }, tiers["*knife-generic-iron"]);
        Assert.Equal(new[] { 3 }, tiers["*knife-generic-meteoriciron"]);
        Assert.Empty(tiers["*pickaxe-steel"]);

        Config.DropInfo[] drops = Mod.config.gems_drops_table["ore-*-malachite-*"];
        Assert.Equal(2, drops.Length);
        Assert.Equal("canjewelry:gem-rough-chipped-malachite", drops[0].NameCollectable);
        Assert.Equal(0.25f, drops[0].avg);
        Assert.Equal(EnumItemClass.Item, drops[1].TypeCollectable);
        Assert.Equal(0.1f, drops[1].var);
        Assert.False(drops[1].LastDrop);
        await World.Ticks(1);
    }

    // The old per item socket counts move to the tier table with tier 3 sockets.
    [AtlasScenario]
    public async Task LegacySocketCounts_Should_MoveToTiers()
    {
        var tiers = Mod.config.items_codes_with_socket_count_and_tiers;

        Assert.Equal(new[] { 3, 3 }, tiers["*knife-generic-gold"]);
        Assert.Equal(new[] { 3 }, tiers["*knife-generic-silver"]);
        Assert.Equal(new Dictionary<string, int> { { "moved to items_codes_with_socket_count_and_tiers", 42 } },
            Mod.config.items_codes_with_socket_count);
        Assert.Equal(new[] { 3, 3 }, Encrustable.GetSocketsTiers(Stack("game:knife-generic-gold")));
        await World.Ticks(1);
    }

    // The file's socket rules reach the items once the game runs.
    [AtlasScenario]
    public async Task ConfigSocketRules_Should_ReachItems()
    {
        Assert.Equal(new[] { 2, 1 }, Encrustable.GetSocketsTiers(Stack(JewelryCode)));
        Assert.Equal(2, Encrustable.GetMaxAmountSockets(Stack(JewelryCode)));
        Assert.Equal(new[] { 1, 1 }, Encrustable.GetSocketsTiers(Stack("game:knife-generic-iron")));
        Assert.Equal(new[] { 3 }, Encrustable.GetSocketsTiers(Stack("game:knife-generic-meteoriciron")));
        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(Stack("game:pickaxe-steel")));
        Assert.Empty(Encrustable.GetSocketsTiers(Stack("game:pickaxe-steel")));
        Assert.Equal(2, Stack("canjewelry:cansocket-steel").Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt());
        await World.Ticks(1);
    }

    // "$group" references resolve against the file's own groups and are written back folded.
    [AtlasScenario]
    public async Task ItemGroups_Should_ExpandInMemory_AndFoldOnDisk()
    {
        Config c = Mod.config;

        ConfigFiles.AssertSet(new[] { "testitem-a", "testitem-b", "knife" }, c.buffNameToPossibleItem[GemType]);
        ConfigFiles.AssertSet(new[] { "biaxe", "halberd" }, c.buffNameToPossibleItem["diamond"]);
        Assert.Contains("testgroup", c.gemToGroups[GemType]);
        Assert.Contains("melee", c.gemToGroups["diamond"]);

        JObject file = ConfigFiles.Read();
        Assert.Equal(new[] { "$testgroup", "knife" }, file["buffNameToPossibleItem"]![GemType]!.Values<string>());
        Assert.Equal(new[] { "$melee" }, file["buffNameToPossibleItem"]!["diamond"]!.Values<string>());
        Assert.True((bool)file["debugMode"]!);
        Assert.Equal(0f, (float)file["chance_gem_drop_on_item_broken"]!);
        Assert.Equal("2,1", (string?)file["items_codes_with_socket_count_and_tiers"]!["*knife-generic-steel"]);
        Assert.Equal("", (string?)file["items_codes_with_socket_count_and_tiers"]!["*pickaxe-steel"]);
        Assert.Equal(JTokenType.Object, file["gems_buffs"]!["hungerrate"]!.Type);
        Assert.Equal("0.1,0.2,0.3", (string?)file["gems_buffs"]!["walkspeed"]);
        Assert.Equal("canjewelry:gem-rough-chipped-malachite 0.25", (string?)file["gems_drops_table"]!["ore-*-malachite-*"]![0]);
        Assert.Equal(JTokenType.Object, file["gems_drops_table"]!["ore-*-malachite-*"]![1]!.Type);
        Assert.Equal(42, (int)file["items_codes_with_socket_count"]!["moved to items_codes_with_socket_count_and_tiers"]!);
        await World.Ticks(1);
    }
}

/// <summary>A config of the format before config_version existed, every value wrapped in "Val".</summary>
[Trait("Category", "Config")]
[AtlasDataFiles("fixtures/OldFormatConfig", TargetPath = "ModConfig")]
public class OldFormatConfigScenarios : ConfigScenarioBase
{
    [AtlasScenario]
    public async Task OldFormatConfig_Should_BeCopiedAside_AndConverted()
    {
        Assert.True(File.Exists(ConfigFiles.PathOf("canjewelry_old.json")), "the old config was not kept");
        JObject old = ConfigFiles.Read("canjewelry_old.json");
        Assert.Equal(9f, (float)old["grindTimeOneTick"]!["Val"]!);

        Assert.Equal(9f, Mod.config.grindTimeOneTick);

        // The new file carries the version key, so the next start reads it as the current format.
        JObject converted = ConfigFiles.Read();
        Assert.True(converted.ContainsKey("config_version"));
        Assert.Equal(9f, (float)converted["grindTimeOneTick"]!);
        await World.Ticks(1);
    }
}
