using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src.blocks;
using canjewelry.src.integration;
using canjewelry.src.utils;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// The jeweler's pan: drop tables built from config.panningDrops plus what companion mods
/// register at runtime, taking material from the slot next to the pan, and the drop itself.
/// </summary>
public class PanningScenarios : WorkstationScenarioBase
{
    private const string PanCode = "canjewelry:canpan-copper";
    private const string Ore = "game:ore-rich-malachite-limestone";
    private const string OreMaterial = "ore-rich-malachite-limestone";
    private const string Gem = "canjewelry:gem-rough-chipped-" + GemType;

    private CANBlockPan Pan()
    {
        var pan = World.Api.World.GetBlock(new AssetLocation(PanCode)) as CANBlockPan;
        Assert.True(pan != null, "no pan " + PanCode);
        return pan!;
    }

    private static Dictionary<string, CANPanningDrop[]> Table(CANBlockPan pan) =>
        GetField<Dictionary<string, CANPanningDrop[]>>(pan, "dropsBySourceMat");

    private static void Rebuild() => CallStatic(typeof(CANBlockPan), "OnConfigReplaced");

    private static CANPanningDrop Drop(string code, float chance, string? perk = null) => new CANPanningDrop
    {
        Code = new AssetLocation(code),
        Type = EnumItemClass.Item,
        StackSize = 1,
        Chance = new NatFloat(chance, 0f, EnumDistribution.UNIFORM),
        requiresPerk = perk
    };

    /// <summary>Pans by this table until disposed, then by the config's own again.</summary>
    private static IDisposable UseTable(Dictionary<string, CANPanningDrop[]> table)
    {
        var previous = Mod.config.panningDrops;
        Mod.config.panningDrops = table;
        Rebuild();
        return new Restore(() => { Mod.config.panningDrops = previous; Rebuild(); });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>A pan in hotbar slot 0 and the material right of it in slot 1.</summary>
    private static ItemSlot PanAndMaterial(ITestPlayer player, ItemStack pan, ItemStack? material)
    {
        player.Player.InventoryManager.ActiveHotbarSlotNumber = 0;
        IInventory hotbar = player.Player.InventoryManager.GetHotbarInventory();
        hotbar[0].Itemstack = pan;
        hotbar[1].Itemstack = material;
        hotbar[0].MarkDirty();
        hotbar[1].MarkDirty();
        return hotbar[0];
    }

    private static ItemSlot MaterialSlot(ITestPlayer player) => player.Player.InventoryManager.GetHotbarInventory()[1];

    private static void TakeMaterial(CANBlockPan pan, ItemSlot panSlot, ITestPlayer player) =>
        Call(pan, "TryTakeMaterial", panSlot, player.Entity);

    private static string? Material(ItemSlot panSlot) => panSlot.Itemstack.Attributes.GetString("materialBlockCode");

    // ---- drop tables -------------------------------------------------------------------------

    [AtlasScenario]
    public async Task DropTable_Should_HoldEveryConfigEntry_Resolved()
    {
        Assert.NotNull(Mod.config.panningDrops);
        Assert.NotEmpty(Mod.config.panningDrops);

        foreach (Block block in World.Api.World.SearchBlocks(new AssetLocation("canjewelry:canpan-*")))
        {
            var table = Table((CANBlockPan)block);
            foreach (var (key, drops) in Mod.config.panningDrops)
            {
                Assert.True(table.ContainsKey(key), $"{block.Code} has no drops for {key}");
                foreach (CANPanningDrop drop in table[key].Where(d => d.Code != null && !d.Code.Path.Contains("{rocktype}")))
                {
                    Assert.True(drop.ResolvedItemstack != null, $"{drop.Code} for {key} did not resolve");
                }
            }
        }
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task RegisteredDrops_Should_BeMergedIntoTable_WithoutTouchingConfig()
    {
        string existingKey = Mod.config.panningDrops.Keys.First();
        int configCount = Mod.config.panningDrops[existingKey].Length;
        const string newKey = "game:rock-granite";
        var saved = Mod.Instance.runtimeExtraPanDrops.ToDictionary(kv => kv.Key, kv => kv.Value);
        try
        {
            Mod.Instance.RegisterPanDrops(World.Api.World, existingKey, new[] { Drop(Gem, 0.1f) });
            Mod.Instance.RegisterPanDrops(World.Api.World, newKey, new[] { Drop(Gem, 0.1f) });
            Mod.Instance.RegisterPanDrops(World.Api.World, newKey, new[] { Drop(Gem, 0.2f) });
            Assert.Equal(2, Mod.Instance.runtimeExtraPanDrops[newKey].Length);
            Assert.NotNull(Mod.Instance.runtimeExtraPanDrops[newKey][0].ResolvedItemstack);

            Rebuild();

            var table = Table(Pan());
            Assert.Equal(configCount + 1 + (saved.TryGetValue(existingKey, out var pre) ? pre.Length : 0), table[existingKey].Length);
            Assert.Equal(2, table[newKey].Length);
            Assert.Equal(configCount, Mod.config.panningDrops[existingKey].Length);
            Assert.False(Mod.config.panningDrops.ContainsKey(newKey));
        }
        finally
        {
            Mod.Instance.runtimeExtraPanDrops.Clear();
            foreach (var kv in saved) Mod.Instance.runtimeExtraPanDrops[kv.Key] = kv.Value;
            Rebuild();
        }
        await World.Ticks(1);
    }

    // Through the mod's own entry point, the one a config sync and the reload command use.
    [AtlasScenario]
    public async Task ReplacedConfig_Should_RebuildTable()
    {
        var previous = Mod.config.panningDrops;
        try
        {
            Mod.config.panningDrops = new Dictionary<string, CANPanningDrop[]> { { "game:rock-granite", new[] { Drop(Gem, 1f) } } };
            CallStatic(typeof(Mod), "OnConfigReplaced");

            var table = Table(Pan());
            Assert.Equal(new[] { "game:rock-granite" }, table.Keys.Except(Mod.Instance.runtimeExtraPanDrops.Keys));
            Assert.NotNull(table["game:rock-granite"][0].ResolvedItemstack);
        }
        finally
        {
            Mod.config.panningDrops = previous;
            CallStatic(typeof(Mod), "OnConfigReplaced");
        }
        Assert.True(Table(Pan()).ContainsKey(Mod.config.panningDrops.Keys.First()));
        await World.Ticks(1);
    }

    // ---- taking material ---------------------------------------------------------------------

    [AtlasScenario]
    public async Task TakeMaterial_Should_TakeConfiguredAmount_FromSlotRightOfPan()
    {
        SetConfig(c => c.pan_take_per_use, (c, v) => c.pan_take_per_use = v, 2);
        ITestPlayer player = await Join();
        ItemSlot panSlot = PanAndMaterial(player, BlockStack(PanCode), BlockStack(Ore, 5));

        TakeMaterial(Pan(), panSlot, player);

        Assert.Equal(OreMaterial, Material(panSlot));
        Assert.Equal(3, MaterialSlot(player).StackSize);
    }

    [AtlasScenario]
    public async Task TakeMaterial_Should_Refuse_TooLittleOrUnknownMaterial()
    {
        SetConfig(c => c.pan_take_per_use, (c, v) => c.pan_take_per_use = v, 2);
        ITestPlayer player = await Join();

        ItemSlot panSlot = PanAndMaterial(player, BlockStack(PanCode), BlockStack(Ore, 1));
        TakeMaterial(Pan(), panSlot, player);
        Assert.Null(Material(panSlot));
        Assert.Equal(1, MaterialSlot(player).StackSize);

        panSlot = PanAndMaterial(player, BlockStack(PanCode), Stack("game:flint", 10));
        TakeMaterial(Pan(), panSlot, player);
        Assert.Null(Material(panSlot));
        Assert.Equal(10, MaterialSlot(player).StackSize);
    }

    [AtlasScenario]
    public async Task TakeMaterial_Should_MapCrystalizedOreAndStone_ToTheirSources()
    {
        SetConfig(c => c.pan_take_per_use, (c, v) => c.pan_take_per_use = v, 2);
        ITestPlayer player = await Join();
        using (UseTable(new Dictionary<string, CANPanningDrop[]>
        {
            { OreMaterial, new[] { Drop(Gem, 1f) } },
            { "game:rock-granite", new[] { Drop(Gem, 1f) } }
        }))
        {
            ItemSlot panSlot = PanAndMaterial(player, BlockStack(PanCode), Stack("game:crystalizedore-rich-malachite-limestone", 3));
            TakeMaterial(Pan(), panSlot, player);
            Assert.Equal(OreMaterial, Material(panSlot));
            Assert.Equal(1, MaterialSlot(player).StackSize);

            // Stone goes four times the amount at a time.
            panSlot = PanAndMaterial(player, BlockStack(PanCode), Stack("game:stone-granite", 9));
            TakeMaterial(Pan(), panSlot, player);
            Assert.Equal("game:rock-granite", Material(panSlot));
            Assert.Equal(1, MaterialSlot(player).StackSize);
        }
    }

    [AtlasScenario]
    public async Task PanTakeEvent_Should_ChangeAmount_FlooredAtOne()
    {
        SetConfig(c => c.pan_take_per_use, (c, v) => c.pan_take_per_use = v, 4);
        ITestPlayer player = await Join();
        PanTakeEvent? seen = null;
        Action<PanTakeEvent> handler = e => { seen = e; e.Amount = 0; };
        Mod.Instance.OnPanMaterialTake += handler;
        try
        {
            ItemSlot panSlot = PanAndMaterial(player, BlockStack(PanCode), BlockStack(Ore, 5));
            TakeMaterial(Pan(), panSlot, player);
        }
        finally
        {
            Mod.Instance.OnPanMaterialTake -= handler;
        }

        Assert.NotNull(seen);
        Assert.Equal(OreMaterial, seen!.MaterialCode);
        Assert.Same(player.Player, seen.Player);
        Assert.Equal(4, MaterialSlot(player).StackSize);
    }

    [AtlasScenario]
    public async Task TakeMaterial_Should_Refuse_WhenEveryDropNeedsAPerk()
    {
        SetConfig(c => c.pan_take_per_use, (c, v) => c.pan_take_per_use = v, 2);
        ITestPlayer player = await Join();
        using (UseTable(new Dictionary<string, CANPanningDrop[]> { { OreMaterial, new[] { Drop(Gem, 1f, "scenario.perk") } } }))
        {
            ItemSlot panSlot = PanAndMaterial(player, BlockStack(PanCode), BlockStack(Ore, 5));
            TakeMaterial(Pan(), panSlot, player);
            Assert.Null(Material(panSlot));

            Action<CanPanDropEvent> allow = e => e.Allowed = e.PerkCode == "scenario.perk";
            Mod.Instance.OnCanPanDrop += allow;
            try
            {
                TakeMaterial(Pan(), panSlot, player);
            }
            finally
            {
                Mod.Instance.OnCanPanDrop -= allow;
            }
            Assert.Equal(OreMaterial, Material(panSlot));
        }
    }

    // ---- panning -----------------------------------------------------------------------------

    [AtlasScenario]
    public async Task FinishedPanning_Should_GiveDrop_EmptyPan_AndWearIt()
    {
        ITestPlayer player = await Join();
        using (UseTable(new Dictionary<string, CANPanningDrop[]> { { OreMaterial, new[] { Drop(Gem, 1f) } } }))
        {
            CANBlockPan pan = Pan();
            ItemSlot panSlot = PanAndMaterial(player, BlockStack(PanCode), null);
            pan.SetMaterial(panSlot, OreMaterial);
            int durability = Durability(panSlot.Itemstack);

            pan.OnHeldInteractStop(3.5f, panSlot, player.Entity, null, null);

            Assert.Null(Material(panSlot));
            Assert.Equal(durability - 1, Durability(panSlot.Itemstack));
            Assert.Single(Carried(player, Gem));
        }
    }

    [AtlasScenario]
    public async Task ShortPanning_Should_KeepMaterial()
    {
        ITestPlayer player = await Join();
        CANBlockPan pan = Pan();
        ItemSlot panSlot = PanAndMaterial(player, BlockStack(PanCode), null);
        pan.SetMaterial(panSlot, OreMaterial);

        pan.OnHeldInteractStop(1f, panSlot, player.Entity, null, null);

        Assert.Equal(OreMaterial, Material(panSlot));
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task Drop_Should_FollowChance_PanEvent_PerkGate_AndRockType()
    {
        ITestPlayer player = await Join();
        CANBlockPan pan = Pan();
        using (UseTable(new Dictionary<string, CANPanningDrop[]>
        {
            { OreMaterial, new[] { Drop(Gem, 1f) } },
            { "ore-poor-malachite-limestone", new[] { Drop(Gem, 0f) } },
            { "ore-medium-malachite-limestone", new[] { Drop(Gem, 1f, "scenario.perk") } },
            { "ore-bountiful-malachite-limestone", new[] { Drop("game:stone-{rocktype}", 1f) } }
        }))
        {
            Call(pan, "CreateDrop", player.Entity, "ore-poor-malachite-limestone");
            Call(pan, "CreateDrop", player.Entity, "ore-medium-malachite-limestone");
            Assert.Empty(Carried(player, Gem));

            Call(pan, "CreateDrop", player.Entity, "ore-bountiful-malachite-limestone");
            Assert.NotEmpty(Carried(player, "game:stone-limestone"));

            PanEvent? seen = null;
            Action<PanEvent> bump = e => { seen = e; e.Drop.StackSize = 3; };
            Mod.Instance.OnPanGem += bump;
            try
            {
                Call(pan, "CreateDrop", player.Entity, OreMaterial);
            }
            finally
            {
                Mod.Instance.OnPanGem -= bump;
            }
            Assert.Equal(OreMaterial, seen!.SourceMaterial);
            Assert.Equal(3, Carried(player, Gem).Sum(s => s.StackSize));

            Action<PanEvent> swallow = e => e.Drop = null!;
            Mod.Instance.OnPanGem += swallow;
            try
            {
                Call(pan, "CreateDrop", player.Entity, OreMaterial);
            }
            finally
            {
                Mod.Instance.OnPanGem -= swallow;
            }
            Assert.Equal(3, Carried(player, Gem).Sum(s => s.StackSize));
        }
    }
}
