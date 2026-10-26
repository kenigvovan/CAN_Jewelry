using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.harmony;
using canjewelry.src.jewelry;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// Gems that come out of the world: rough gems added to rock and ore drops from
/// config.gems_drops_table, gems falling out of jewelry that breaks, and the processed gem item.
/// </summary>
public class WorldDropScenarios : WorkstationScenarioBase
{
    // Applied once at server start, so it reflects the config the server booted with.
    [AtlasScenario]
    public async Task GemDropTable_Should_BeAppendedToMatchingBlocks()
    {
        Assert.NotEmpty(Mod.config.gems_drops_table);
        int checkedDrops = 0;

        foreach (var (key, infos) in Mod.config.gems_drops_table)
        {
            foreach (Block block in World.Api.World.SearchBlocks(new AssetLocation(key)))
            {
                foreach (Config.DropInfo info in infos)
                {
                    if (info.TypeCollectable == EnumItemClass.Item && World.Api.World.GetItem(new AssetLocation(info.NameCollectable)) == null)
                    {
                        continue;
                    }
                    string code = new AssetLocation(info.NameCollectable).ToString();
                    Assert.True(block.Drops.Any(d => d.Code?.ToString() == code
                            && Math.Abs(d.Quantity.avg - info.avg) < 1e-7
                            && Math.Abs(d.Quantity.var - info.var) < 1e-7
                            && d.LastDrop == info.LastDrop
                            && d.ResolvedItemstack != null),
                        $"{block.Code} lacks the drop {code} from {key}");
                    checkedDrops++;
                }
            }
        }

        Assert.True(checkedDrops > 0, "no block matched the gem drop table");
        await World.Ticks(1);
    }

    [AtlasScenario]
    public async Task GemDrop_Should_ComeOutOfBlockDrops_WhenRolled()
    {
        string key = Mod.config.gems_drops_table.Keys.First(k => World.Api.World.SearchBlocks(new AssetLocation(k)).Length > 0);
        Block block = World.Api.World.SearchBlocks(new AssetLocation(key))[0];
        BlockDropItemStack gemDrop = block.Drops.First(d => d.Code?.Domain == "canjewelry");
        float avg = gemDrop.Quantity.avg, var = gemDrop.Quantity.var;
        gemDrop.Quantity.avg = 1;
        gemDrop.Quantity.var = 0;
        try
        {
            ItemStack[] drops = block.GetDrops(World.Api.World, World.Spawn, null);

            Assert.Contains(drops, s => s.Collectible.Code.Equals(gemDrop.Code));
        }
        finally
        {
            gemDrop.Quantity.avg = avg;
            gemDrop.Quantity.var = var;
        }
        await World.Ticks(1);
    }

    private ItemStack SocketedKnife(ITestPlayer player)
    {
        var jewelry = new DummySlot(LegacySocketedJewelry());
        var gem = new DummySlot(LegacyCutGem("flawless", "walkspeed", 0.04f));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, gem, 0, player.Player));
        return jewelry.Itemstack;
    }

    [AtlasScenario]
    public async Task BrokenJewelry_Should_DropItsGems_WithChance()
    {
        SetConfig(c => c.chance_gem_drop_on_item_broken, (c, v) => c.chance_gem_drop_on_item_broken = v, 1f);
        ITestPlayer player = await Join();
        await Site(player);
        const string gemCode = "canjewelry:gem-cut-flawless-" + GemType;
        BlockPos at = player.Entity.Pos.AsBlockPos;

        harmPatch.TryDropGems(World.Api.World, player.Entity, new DummySlot(SocketedKnife(player)));

        await World.Until(() => DroppedNear(at, gemCode, 3).Count + Carried(player, gemCode).Count == 1, "the gem fell out", 100);
        ItemStack gem = DroppedNear(at, gemCode, 3).Concat(Carried(player, gemCode)).First();
        Assert.True(gem.Attributes.GetBool("wasExtracted"));
        ITreeAttribute tree = gem.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal(new[] { "walkspeed" }, (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value);
    }

    [AtlasScenario]
    public async Task BrokenJewelry_Should_KeepGems_WhenChanceIsZero()
    {
        SetConfig(c => c.chance_gem_drop_on_item_broken, (c, v) => c.chance_gem_drop_on_item_broken = v, 0f);
        ITestPlayer player = await Join();
        await Site(player);
        const string gemCode = "canjewelry:gem-cut-flawless-" + GemType;
        BlockPos at = player.Entity.Pos.AsBlockPos;

        harmPatch.TryDropGems(World.Api.World, player.Entity, new DummySlot(SocketedKnife(player)));

        await World.Ticks(5);
        Assert.Empty(DroppedNear(at, gemCode, 3));
        Assert.Empty(Carried(player, gemCode));
    }

    [AtlasScenario]
    public async Task ProcessedGem_Should_Exist_AndHaveNoName_WithoutGrindInfo()
    {
        foreach (string construction in new[] { "flawedvariant", "chippedvariant", "normalvariant" })
        {
            ItemStack stack = Stack("canjewelry:processedgem-" + construction);
            Assert.IsType<ProcessedGem>(stack.Collectible);
            Assert.Equal(construction, ((ProcessedGem)stack.Collectible).Construction);
            Assert.Equal("", stack.Collectible.GetHeldItemName(stack));
        }
        await World.Ticks(1);
    }
}
