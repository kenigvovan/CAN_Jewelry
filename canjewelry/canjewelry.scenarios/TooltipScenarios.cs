using System.Text;
using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using BuffAttributes = global::canjewelry.src.Config.BuffAttributes;
using Mod = global::canjewelry.src.canjewelry;
using Patches = global::canjewelry.src.harmony.harmPatch;

namespace canjewelry.scenarios;

/// <summary>
/// Tooltips of gems and socketed pieces with data the config does not know or that lacks the buff
/// arrays: they skip what they cannot show instead of throwing.
/// </summary>
public class TooltipScenarios : JewelryScenarioBase
{
    private const string UnknownBuff = "scenario-unknown-buff";

    private string Tooltip(ItemStack stack)
    {
        var dsc = new StringBuilder();
        stack.Collectible.GetHeldItemInfo(new DummySlot(stack), dsc, World.Api.World, false);
        return dsc.ToString();
    }

    // The socket lines come from a postfix the client patches in; it is called directly here.
    private string SocketTooltip(ItemStack jewelry)
    {
        var dsc = new StringBuilder();
        Patches.Postfix_GetHeldItemInfo(jewelry.Collectible, new DummySlot(jewelry), dsc, World.Api.World, false);
        return dsc.ToString();
    }

    [AtlasScenario]
    public async Task CutGem_WithUnknownBuff_Should_SkipIt()
    {
        await World.Ticks(1);
        string known = Tooltip(LegacyCutGem(buff: "walkspeed"));
        string unknown = Tooltip(LegacyCutGem(buff: UnknownBuff));

        Assert.Contains(Lang("buff-name-walkspeed"), known);
        Assert.DoesNotContain(UnknownBuff, unknown);
    }

    // A gem from the creative tab or a give command has a cut gem tree without buffs.
    [AtlasScenario]
    public async Task CutGem_WithTreeWithoutBuffs_Should_NotThrow()
    {
        await World.Ticks(1);
        ItemStack gem = Stack($"canjewelry:gem-cut-normal-{GemType}");
        ITreeAttribute tree = new TreeAttribute();
        tree.SetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND);
        gem.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;

        Tooltip(gem);
    }

    [AtlasScenario]
    public async Task Socket_WithUnknownBuff_OrWithoutBuffs_Should_NotThrow()
    {
        ITestPlayer player = await Join();
        var jewelry = new DummySlot(LegacySocketedJewelry(sockets: 2));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem()), 0, player.Player));
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem()), 1, player.Player));

        ITreeAttribute unknown = SocketOf(jewelry.Itemstack, 0);
        unknown[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { UnknownBuff });
        ITreeAttribute bare = SocketOf(jewelry.Itemstack, 1);
        bare.RemoveAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES);
        bare.RemoveAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_VALUES);
        Assert.NotEqual("", bare.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));

        string text = SocketTooltip(jewelry.Itemstack);

        Assert.DoesNotContain(UnknownBuff, text);
    }

    // A buff the gem may roll that the config has no attributes for is left out of the list.
    [AtlasScenario]
    public async Task RoughGem_WithUnknownPossibleBuff_Should_SkipIt()
    {
        await World.Ticks(1);
        var possible = new Dictionary<string, HashSet<string>>(Mod.config.PossibleGemBuffs)
        {
            [GemType] = new HashSet<string> { UnknownBuff }
        };
        SetConfig(c => c.PossibleGemBuffs, (c, v) => c.PossibleGemBuffs = v, possible);

        string text = Tooltip(Stack($"canjewelry:gem-rough-normal-{GemType}"));

        Assert.DoesNotContain(UnknownBuff, text);
        Assert.DoesNotContain(Lang("rough-gem-possible-stats-header"), text);
    }

    // A rough gem whose tier the buff's ranges do not list.
    [AtlasScenario]
    public async Task RoughGem_WithTierMissingFromRanges_Should_SkipIt()
    {
        await World.Ticks(1);
        string buff = Mod.config.PossibleGemBuffs[GemType].First();
        BuffAttributes attributes = Mod.config.BuffAttributesDict[buff];
        Dictionary<int, float[]> ranges = attributes.MainStatValueRange;
        attributes.MainStatValueRange = new Dictionary<int, float[]>();
        AddCleanup(() => attributes.MainStatValueRange = ranges);

        var possible = new Dictionary<string, HashSet<string>>(Mod.config.PossibleGemBuffs)
        {
            [GemType] = new HashSet<string> { buff }
        };
        SetConfig(c => c.PossibleGemBuffs, (c, v) => c.PossibleGemBuffs = v, possible);

        string text = Tooltip(Stack($"canjewelry:gem-rough-normal-{GemType}"));

        Assert.DoesNotContain(Lang("rough-gem-possible-stats-header"), text);
    }

    private static string Lang(string key) => Vintagestory.API.Config.Lang.Get("canjewelry:" + key);
}
