using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.api;
using canjewelry.src.blocks;
using canjewelry.src.items;
using canjewelry.src.utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// The surface companion mods use: CANJewelryRegistry, the collectible attributes of
/// CANJewelryAttributes and canjewelry.RegisterPanDrops.
/// </summary>
[Trait("Category", "Api")]
public class ApiScenarios : ConfigScenarioBase
{
    /// <summary>Items of a family that have no socket rule at all, so registering one is visible.</summary>
    private Item[] ItemsWithoutSockets(string pattern, int count)
    {
        Item[] items = World.Api.World.SearchItems(new AssetLocation(pattern))
            .Where(i => Encrustable.GetMaxAmountSockets(new ItemStack(i)) == -1)
            .OrderBy(i => i.Code.ToString())
            .Take(count)
            .ToArray();
        Assert.True(items.Length == count, "not enough unsocketed items for " + pattern);
        return items;
    }

    private void Reapply() => Mod.Instance.AddBehaviorAndSocketNumber(World.Api);

    // The core seals the registry in StartPre; registering later still works in game.
    [AtlasScenario]
    public async Task Registry_Should_BeSealedOnceTheServerRuns()
    {
        Assert.True(ModInternals.RegistrySealed);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task RegisteredSockets_Should_ReachItemOnNextApply_ButNotTheConfig()
    {
        Item hoe = ItemsWithoutSockets("game:hoe-*", 1)[0];
        string code = hoe.Code.ToString();
        RestoreOnCleanup(hoe);
        AfterRestore(() => ModInternals.RegisteredSockets.Remove(code));

        CANJewelryRegistry.RegisterDefaultSockets(code, 2, 1);
        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(new ItemStack(hoe)));
        Reapply();

        Assert.True(hoe.HasBehavior<Encrustable>());
        Assert.Equal(new[] { 2, 1 }, Encrustable.GetSocketsTiers(new ItemStack(hoe)));
        Assert.Equal(2, hoe.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt());
        Assert.False(Mod.config.items_codes_with_socket_count_and_tiers.ContainsKey(code));

        // Same wildcard again: replaced, not added.
        CANJewelryRegistry.RegisterDefaultSockets(code, 3);
        Reapply();
        Assert.Equal(new[] { 3 }, Encrustable.GetSocketsTiers(new ItemStack(hoe)));
        Assert.Equal(new[] { 3 }, ModInternals.RegisteredSockets[code]);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task RegisteredSockets_Should_BeCopied_AndIgnoreEmptyCalls()
    {
        Item hoe = ItemsWithoutSockets("game:hoe-*", 1)[0];
        string code = hoe.Code.ToString();
        RestoreOnCleanup(hoe);
        AfterRestore(() => ModInternals.RegisteredSockets.Remove(code));
        int before = ModInternals.RegisteredSockets.Count;

        CANJewelryRegistry.RegisterDefaultSockets("", 1);
        CANJewelryRegistry.RegisterDefaultSockets(code, null!);
        Assert.Equal(before, ModInternals.RegisteredSockets.Count);

        int[] tiers = { 2, 2 };
        CANJewelryRegistry.RegisterDefaultSockets(code, tiers);
        tiers[0] = 1;
        Assert.Equal(new[] { 2, 2 }, ModInternals.RegisteredSockets[code]);

        // Registered with no tiers: kept, but gives no sockets.
        CANJewelryRegistry.RegisterDefaultSockets(code);
        Reapply();
        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(new ItemStack(hoe)));
        await World.Ticks(1);
    }

    // Config rules are applied after registered ones and win.
    [AtlasScenario]
    public async Task ConfigRule_Should_WinOverRegisteredSockets()
    {
        Item knife = ItemOf(JewelryCode);
        string wildcard = "*knife-generic-steel";
        RestoreOnCleanup(knife);
        AfterRestore(() => ModInternals.RegisteredSockets.Remove(wildcard));

        CANJewelryRegistry.RegisterDefaultSockets(wildcard, 1);
        Reapply();

        Assert.Equal(new[] { 3, 3, 3 }, Encrustable.GetSocketsTiers(Stack(JewelryCode)));
        await World.Ticks(1);
    }

    // An empty list in the config takes the sockets away, whatever gave them.
    [AtlasScenario]
    public async Task EmptyConfigRule_Should_RemoveSockets()
    {
        Item gold = ItemOf("game:knife-generic-gold");
        RestoreOnCleanup(gold);
        var rules = new Dictionary<string, int[]>(Mod.config.items_codes_with_socket_count_and_tiers)
        {
            ["*knife-generic-gold"] = new int[0]
        };
        SetConfig(c => c.items_codes_with_socket_count_and_tiers, (c, v) => c.items_codes_with_socket_count_and_tiers = v, rules);
        Assert.Equal(new[] { 3 }, Encrustable.GetSocketsTiers(new ItemStack(gold)));

        Reapply();

        Assert.Equal(-1, Encrustable.GetMaxAmountSockets(new ItemStack(gold)));
        Assert.Empty(Encrustable.GetSocketsTiers(new ItemStack(gold)));
        Assert.Equal(0, gold.Attributes[CANJWConstants.SOCKETS_NUMBER_STRING].AsInt());
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task RegisteredVariantSockets_Should_PickTiersByStackValue()
    {
        Item hoe = ItemsWithoutSockets("game:hoe-*", 1)[0];
        string code = hoe.Code.ToString();
        RestoreOnCleanup(hoe);
        AfterRestore(() => ModInternals.RegisteredVariantSockets.Remove(code));

        var byValue = new Dictionary<string, int[]> { { "a", new[] { 1 } }, { "*", new[] { 2, 2 } } };
        CANJewelryRegistry.RegisterDefaultVariantSockets(code, "cantestkey", byValue);
        byValue["a"] = new[] { 3, 3, 3 };
        Reapply();

        Assert.True(hoe.HasBehavior<Encrustable>());
        Assert.Equal("cantestkey", hoe.Attributes[CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY].AsString());
        var withValue = new ItemStack(hoe);
        withValue.Attributes.SetString("cantestkey", "a");
        Assert.Equal(new[] { 1 }, Encrustable.GetSocketsTiers(withValue));
        Assert.Equal(1, Encrustable.GetMaxAmountSockets(withValue));
        // No value, or one the rule does not list: the "*" entry.
        Assert.Equal(new[] { 2, 2 }, Encrustable.GetSocketsTiers(new ItemStack(hoe)));
        var unknown = new ItemStack(hoe);
        unknown.Attributes.SetString("cantestkey", "zzz");
        Assert.Equal(new[] { 2, 2 }, Encrustable.GetSocketsTiers(unknown));
        Assert.False(Mod.config.custom_variants_sockets_tiers.Any(v => v.ItemCode == code));
        await World.Ticks(1);
    }

    // Without a stack value the item's own code variant is used.
    [AtlasScenario]
    public async Task RegisteredVariantSockets_Should_FallBackToCodeVariant()
    {
        Item hoe = ItemsWithoutSockets("game:hoe-*", 1)[0];
        string code = hoe.Code.ToString();
        RestoreOnCleanup(hoe);
        AfterRestore(() => ModInternals.RegisteredVariantSockets.Remove(code));
        var variant = hoe.Variant.First();

        CANJewelryRegistry.RegisterDefaultVariantSockets(code, "cantestkey", new Dictionary<string, int[]> { { "x", new[] { 1 } } });
        // Same item again: replaces the whole entry.
        CANJewelryRegistry.RegisterDefaultVariantSockets(code, variant.Key, new Dictionary<string, int[]> { { variant.Value, new[] { 3, 1 } } });
        Reapply();

        Assert.Equal(variant.Key, ModInternals.RegisteredVariantSockets[code].AttributeKey);
        Assert.Equal(new[] { 3, 1 }, Encrustable.GetSocketsTiers(new ItemStack(hoe)));
        Assert.Equal(variant.Value, Encrustable.ResolveVariantValue(new ItemStack(hoe), variant.Key));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task RegisteredVariantSockets_Should_IgnoreEmptyCalls()
    {
        int before = ModInternals.RegisteredVariantSockets.Count;

        CANJewelryRegistry.RegisterDefaultVariantSockets("", "key", new Dictionary<string, int[]> { { "a", new[] { 1 } } });
        CANJewelryRegistry.RegisterDefaultVariantSockets("game:nothing", "", new Dictionary<string, int[]> { { "a", new[] { 1 } } });
        CANJewelryRegistry.RegisterDefaultVariantSockets("game:nothing", "key", null!);
        CANJewelryRegistry.RegisterDefaultVariantSockets("game:nothing", "key", new Dictionary<string, int[]>());

        Assert.Equal(before, ModInternals.RegisteredVariantSockets.Count);
        await World.Ticks(1);
    }

    // Group members from code are merged in memory and never written into the config's groups.
    [AtlasScenario]
    public async Task RegisteredGroupMembers_Should_JoinTheGroupInMemoryOnly()
    {
        AfterRestore(() =>
        {
            ModInternals.UnregisterGroupMembers("melee", "cantest-blade");
            ModInternals.UnregisterGroupMembers("cantestgroup", "cantest-thing");
            Mod.config.ExpandItemGroups();
        });

        CANJewelryRegistry.RegisterItemGroupMembers("melee", "cantest-blade", "", null!);
        CANJewelryRegistry.RegisterItemGroupMembers("cantestgroup", "cantest-thing");
        CANJewelryRegistry.RegisterItemGroupMembers("", "ignored");
        CANJewelryRegistry.RegisterItemGroupMembers("ignoredgroup");
        Mod.config.ExpandItemGroups();

        ConfigFiles.AssertSet(new[] { "cantest-blade" }, ModInternals.RegisteredGroupMembers["melee"]);
        Assert.False(ModInternals.RegisteredGroupMembers.ContainsKey(""));
        Assert.False(ModInternals.RegisteredGroupMembers.ContainsKey("ignoredgroup"));
        Assert.Contains("cantest-blade", ModInternals.ActiveItemGroups["melee"]);
        Assert.Contains("cantest-thing", ModInternals.ActiveItemGroups["cantestgroup"]);
        Assert.DoesNotContain("cantest-blade", Mod.config.item_groups["melee"]);
        Assert.False(Mod.config.item_groups.ContainsKey("cantestgroup"));

        // A set holding the whole merged group folds to "$melee"; without the new member it does not.
        var melee = new HashSet<string>(ModInternals.ActiveItemGroups["melee"]);
        Assert.Equal("[\"$melee\"]", JsonConvert.SerializeObject(melee, new ItemGroupSetConverter()));
        melee.Remove("cantest-blade");
        Assert.DoesNotContain("$melee", JsonConvert.SerializeObject(melee, new ItemGroupSetConverter()));

        // A config may name a group that only exists through the registry.
        var cfg = new Config
        {
            item_groups = new Dictionary<string, HashSet<string>> { { "melee", new HashSet<string> { "x" } } },
            buffNameToPossibleItem = new Dictionary<string, HashSet<string>> { { "testgem", new HashSet<string> { "$cantestgroup", "lit" } } }
        };
        cfg.ExpandItemGroups();
        ConfigFiles.AssertSet(new[] { "cantest-thing", "lit" }, cfg.buffNameToPossibleItem["testgem"]);
        Assert.Contains("cantestgroup", cfg.gemToGroups["testgem"]);
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task MaterialKeys_Should_FollowTheBaseSet_WithoutDuplicates()
    {
        AfterRestore(() => ModInternals.MaterialKeys.Remove("cantestmaterial"));
        int baseCount = CANItemWearable.BaseMaterialAttrKeys.Length;

        CANJewelryRegistry.RegisterMaterialAttributeKeys("cantestmaterial", "", null!, CANItemWearable.BaseMaterialAttrKeys[0]);
        CANJewelryRegistry.RegisterMaterialAttributeKeys("cantestmaterial");

        Assert.Equal(CANItemWearable.BaseMaterialAttrKeys, CANJewelryRegistry.MaterialAttributeKeys.Take(baseCount));
        Assert.Equal(1, CANJewelryRegistry.MaterialAttributeKeys.Count(k => k == "cantestmaterial"));
        Assert.Equal("cantestmaterial", CANJewelryRegistry.MaterialAttributeKeys.Last());
        Assert.Equal(1, CANJewelryRegistry.MaterialAttributeKeys.Count(k => k == CANItemWearable.BaseMaterialAttrKeys[0]));
        await World.Ticks(1);
    }

    // canAllowedGems and canGemGroups on an item widen which gems it takes.
    [AtlasScenario]
    public async Task GemAttributes_Should_DecideWhichGemsFit()
    {
        Item hoe = ItemsWithoutSockets("game:hoe-*", 1)[0];
        RestoreOnCleanup(hoe);
        var stack = new ItemStack(hoe);
        Assert.False(Encrustable.canItemContainThisGem("ruby", stack));
        Assert.False(Encrustable.canItemContainThisGem("fluorite", stack));

        JObject token = hoe.Attributes?.Token as JObject ?? new JObject();
        token[CANJewelryAttributes.AllowedGems] = new JArray("ruby");
        token[CANJewelryAttributes.GemGroups] = new JArray("melee");
        hoe.Attributes = new JsonObject(token);

        Assert.True(Encrustable.canItemContainThisGem("ruby", stack));
        // fluorite's entry covers the whole melee group.
        Assert.Contains("melee", Mod.config.gemToGroups["fluorite"]);
        Assert.True(Encrustable.canItemContainThisGem("fluorite", stack));
        Assert.False(Encrustable.canItemContainThisGem("citrine", stack));
        Assert.False(Encrustable.canItemContainThisGem(null!, stack));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task RegisterPanDrops_Should_ExtendThePanInMemoryOnly()
    {
        const string material = "cantest-material";
        const string existing = "game:stone-suevite";
        var pan = (CANBlockPan)World.Api.World.GetBlock(new AssetLocation("canjewelry:canpan-steel"));
        var extras = Mod.Instance.runtimeExtraPanDrops;
        bool hadExisting = extras.TryGetValue(existing, out var existingBefore);
        AfterRestore(() =>
        {
            extras.Remove(material);
            if (hadExisting) extras[existing] = existingBefore!; else extras.Remove(existing);
            ModInternals.OnConfigReplaced();
        });

        CANPanningDrop Drop() => new CANPanningDrop
        {
            Code = new AssetLocation($"canjewelry:gem-rough-normal-{GemType}"),
            Type = EnumItemClass.Item,
            StackSize = 1,
            Chance = new NatFloat(0.5f, 0f, EnumDistribution.UNIFORM)
        };

        Mod.Instance.RegisterPanDrops(World.Api.World, material, new[] { Drop() });
        Mod.Instance.RegisterPanDrops(World.Api.World, material, new[] { Drop() });
        Mod.Instance.RegisterPanDrops(World.Api.World, existing, new[] { Drop() });

        Assert.Equal(2, extras[material].Length);
        Assert.All(extras[material], d => Assert.NotNull(d.ResolvedItemstack));
        Assert.False(Mod.config.panningDrops!.ContainsKey(material));
        int configured = Mod.config.panningDrops[existing].Length;

        // The pan picks them up once its table is built again.
        Assert.False(ModInternals.PanTable(pan).ContainsKey(material));
        ModInternals.OnConfigReplaced();

        Assert.Equal(2, ModInternals.PanTable(pan)[material].Length);
        Assert.Equal(configured + 1, ModInternals.PanTable(pan)[existing].Length);
        Assert.Equal(configured, Mod.config.panningDrops[existing].Length);
        await World.Ticks(1);
    }
}
