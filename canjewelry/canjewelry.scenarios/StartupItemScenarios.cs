using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.bb;
using canjewelry.src.cb;
using canjewelry.src.utils;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// What the server writes onto items and blocks at startup from the default config: socket rules,
/// gem attributes, socket levels, temporal grasp, gem drops and the gem texture tables.
/// </summary>
[Trait("Category", "Startup")]
public class StartupItemScenarios : ConfigScenarioBase
{
    [AtlasScenario]
    public async Task DefaultJewelry_Should_HaveSocketsAndBehavior()
    {
        Item knife = ItemOf(JewelryCode);

        Assert.True(knife.HasBehavior<Encrustable>(), "no Encrustable behavior on the knife");
        Assert.Equal(3, knife.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt());
        Assert.Equal(new[] { 3, 3, 3 }, knife.Attributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray<int>());
        Assert.Equal(new[] { 3, 3, 3 }, Encrustable.GetSocketsTiers(Stack(JewelryCode)));
        Assert.Equal(3, Encrustable.GetMaxAmountSockets(Stack(JewelryCode)));
        await World.Ticks(1);
    }

    // Every config rule lands on the items it matches; for an item matched twice the later rule wins.
    [AtlasScenario]
    public async Task EveryConfigSocketRule_Should_ReachItsItems()
    {
        var expected = new Dictionary<Item, int[]>();
        foreach (var rule in Mod.config.items_codes_with_socket_count_and_tiers)
        {
            if (rule.Value == null) continue;
            foreach (Item item in World.Api.World.SearchItems(new AssetLocation(rule.Key)))
            {
                expected[item] = rule.Value;
            }
        }
        Assert.NotEmpty(expected);

        var wrong = new List<string>();
        foreach (var pair in expected)
        {
            if (pair.Value.Length == 0)
            {
                if (Encrustable.GetMaxAmountSockets(new ItemStack(pair.Key)) != -1) wrong.Add(pair.Key.Code + " kept its sockets");
                continue;
            }
            if (!pair.Key.HasBehavior<Encrustable>()) wrong.Add(pair.Key.Code + " has no Encrustable");
            int[] tiers = pair.Key.Attributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray<int>(null);
            if (tiers == null || !tiers.SequenceEqual(pair.Value)) wrong.Add(pair.Key.Code + " tiers " + string.Join(",", tiers ?? new int[0]));
            if (pair.Key.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt() != pair.Value.Length) wrong.Add(pair.Key.Code + " socket number");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task ItemWithoutRule_Should_HaveNoSockets()
    {
        Assert.False(Mod.config.items_codes_with_socket_count_and_tiers.Keys
            .Any(k => WildcardUtil.Match(new AssetLocation(k), new AssetLocation("game:hoe-iron"))));

        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(Stack("game:hoe-iron")));
        Assert.Empty(Encrustable.GetSocketsTiers(Stack("game:hoe-iron")));
        await World.Ticks(1);
    }

    // Rough and cut gems carry the buff their type gives, from gem_type_to_buff.
    [AtlasScenario]
    public async Task OwnGems_Should_CarryTheirBuffName()
    {
        var wrong = new List<string>();
        foreach (string pattern in new[] { "canjewelry:gem-rough-*", "canjewelry:gem-cut-*" })
        {
            foreach (Item gem in World.Api.World.SearchItems(new AssetLocation(pattern)))
            {
                string type = CANGemSize.GemTypeOf(gem);
                if (!Mod.config.gem_type_to_buff.TryGetValue(type, out string? buff)) continue;
                string? actual = gem.Attributes?["canGemTypeToAttribute"].AsString();
                if (actual != buff) wrong.Add($"{gem.Code}: {actual} instead of {buff}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        Assert.Equal("wildCropDropRate", Stack($"canjewelry:gem-rough-normal-{GemType}").Collectible.Attributes["canGemTypeToAttribute"].AsString());
        Assert.Equal("wildCropDropRate", Stack($"canjewelry:gem-cut-normal-{GemType}").Collectible.Attributes["canGemTypeToAttribute"].AsString());
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task RoughGems_Should_BeCuttable()
    {
        Item[] rough = World.Api.World.SearchItems(new AssetLocation("canjewelry:gem-rough-*"))
            .Concat(World.Api.World.SearchItems(new AssetLocation("game:gem-*-rough")))
            .ToArray();

        Assert.NotEmpty(rough);
        Assert.All(rough, gem => Assert.True(gem.HasBehavior<CANGemCuttableCB>(), gem.Code + " is not cuttable"));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task SocketItems_Should_CarryTheirLevel()
    {
        Assert.NotEmpty(Mod.config.LevelOfSocketByType);
        foreach (var level in Mod.config.LevelOfSocketByType)
        {
            foreach (Item socket in World.Api.World.SearchItems(new AssetLocation(level.Key)))
            {
                Assert.Equal(level.Value, socket.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt());
            }
        }
        Assert.Equal(3, Stack("canjewelry:cansocket-steel").Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt());
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task TemporalGraspBlocks_Should_HaveTheBehavior()
    {
        Assert.True(Mod.config.TemporalGraspEnabled);
        int seen = 0;
        foreach (string code in Mod.config.TemporalGraspBlockList)
        {
            foreach (Block block in World.Api.World.SearchBlocks(new AssetLocation(code)))
            {
                seen++;
                Assert.True(block.HasBehavior<CANTemporalGraspBB>(), block.Code + " has no temporal grasp");
            }
        }
        Assert.True(seen > 0, "no block matched the temporal grasp list");
        await World.Ticks(1);
    }

    // Every item drop of the gem drop table is added to the blocks its key matches.
    [AtlasScenario]
    public async Task GemDropTable_Should_ReachBlockDrops()
    {
        var missing = new List<string>();
        int checkedDrops = 0;
        foreach (var rule in Mod.config.gems_drops_table)
        {
            foreach (Block block in World.Api.World.SearchBlocks(new AssetLocation(rule.Key)))
            {
                foreach (Config.DropInfo drop in rule.Value)
                {
                    if (drop.TypeCollectable != EnumItemClass.Item) continue;
                    if (World.Api.World.GetItem(new AssetLocation(drop.NameCollectable)) == null) continue;
                    checkedDrops++;
                    bool found = block.Drops.Any(d =>
                        d.Code?.ToString() == new AssetLocation(drop.NameCollectable).ToString()
                        && d.Quantity.avg == drop.avg
                        && d.Quantity.var == drop.var
                        && d.LastDrop == drop.LastDrop);
                    if (!found) missing.Add($"{block.Code}: {drop.NameCollectable} {drop.avg}");
                }
            }
        }
        Assert.True(checkedDrops > 0, "no gem drop was checked");
        Assert.True(missing.Count == 0, string.Join("\n", missing.Take(20)));
        await World.Ticks(1);
    }

    // Every cut gem type gets its texture paths. The server frees item textures after loading
    // (Item.FreeRAMServer), so the expectation follows gem-cut.json: canjewelry:item/gem/{gemtype}.
    [AtlasScenario]
    public async Task GemTextures_Should_ListEveryCutGemType()
    {
        var expected = new Dictionary<string, string>();
        var expectedPngs = new Dictionary<string, string>();
        foreach (Item gem in World.Api.World.SearchItems(new AssetLocation("canjewelry:gem-cut-*")))
        {
            string type = CANGemSize.GemTypeOf(gem);
            expected.TryAdd(type, "canjewelry:textures/item/gem/" + type);
            expectedPngs.TryAdd(type, "canjewelry:item/gem/" + type + ".png");
        }

        Assert.NotEmpty(expected);
        Assert.Equal(expected, Mod.gems_textures);
        Assert.Equal(expectedPngs, Mod.gems_textures_pngs);
        Assert.True(Mod.gems_textures.ContainsKey(GemType));
        await World.Ticks(1);
    }

    // AddBehaviorAndSocketNumber runs again whenever the config arrives; doing so changes nothing.
    [AtlasScenario]
    public async Task ApplyingRulesTwice_Should_ChangeNothing()
    {
        Item knife = ItemOf(JewelryCode);
        RestoreOnCleanup(knife);
        int behaviors = knife.CollectibleBehaviors.Length;

        Mod.Instance.AddBehaviorAndSocketNumber(World.Api);

        Assert.Equal(behaviors, knife.CollectibleBehaviors.Length);
        Assert.Equal(new[] { 3, 3, 3 }, Encrustable.GetSocketsTiers(Stack(JewelryCode)));
        await World.Ticks(1);
    }
}
