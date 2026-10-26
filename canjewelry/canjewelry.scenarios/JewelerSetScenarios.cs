using System.IO;
using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using canjewelry.src.integration;
using canjewelry.src.jewelry;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// The jeweler's set as a block entity: what it does to stacks put into its slots, the
/// inscription packet and who may send packets at all. Encrusting itself is covered elsewhere.
/// </summary>
public class JewelerSetScenarios : WorkstationScenarioBase
{
    private const string SetCode = "canjewelry:jewelerset-north";
    private const int InscribePacket = 1007;

    private async Task<(ITestPlayer player, JewelerSetBE set, BlockPos pos)> JewelerSet()
    {
        ITestPlayer player = await Join();
        BlockPos pos = await Site(player);
        return (player, Place<JewelerSetBE>(SetCode, pos), pos);
    }

    private static void PutInto(JewelerSetBE set, int slot, ItemStack? stack)
    {
        set.inventory[slot].Itemstack = stack;
        set.inventory[slot].MarkDirty();
    }

    private static byte[] Text(string text)
    {
        using var ms = new MemoryStream();
        new BinaryWriter(ms).Write(text);
        return ms.ToArray();
    }

    private static string? Inscription(JewelerSetBE set) =>
        set.inventory[0].Itemstack.Attributes.GetString(CANJWConstants.INSCRIPTION);

    [AtlasScenario]
    public async Task CutGemWithoutCut_InGemSlot_Should_GetRolledCutAndBuffs()
    {
        var (_, set, _) = await JewelerSet();
        PutInto(set, 0, Stack(JewelryCode));
        PutInto(set, 1, Stack($"canjewelry:gem-cut-normal-{GemType}"));

        ITreeAttribute tree = set.inventory[1].Itemstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.NotNull(tree);
        Assert.Contains(tree.GetString(CANJWConstants.CUTTING_TYPE), Mod.config.CuttingAttributesDict.Keys);
        string[] names = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)!.value;
        Assert.NotEmpty(names);
        Assert.Contains(names[0], Mod.config.PossibleGemBuffs[GemType]);
    }

    [AtlasScenario]
    public async Task GemWithCut_OrInSocketSlot_Should_BeLeftAlone()
    {
        var (_, set, _) = await JewelerSet();
        PutInto(set, 0, Stack(JewelryCode));

        PutInto(set, 2, LegacyCutGem("normal", "walkspeed", 0.03f));
        ITreeAttribute tree = set.inventory[2].Itemstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.Equal(new[] { 0.03f }, (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)!.value);

        PutInto(set, 5, Stack($"canjewelry:gem-cut-normal-{GemType}"));
        Assert.False(set.inventory[5].Itemstack.Attributes.HasAttribute(CANJWConstants.CUT_GEM_TREE));
    }

    [AtlasScenario]
    public async Task LegacyJewelry_InSlotZero_Should_MigrateSingleBuffToArrays()
    {
        var (_, set, _) = await JewelerSet();
        ItemStack knife = LegacySocketedJewelry();
        ITreeAttribute socket = SocketOf(knife, 0);
        socket.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, GemType);
        socket.SetString(CANJWConstants.GEM_ATTRIBUTE_BUFF, "walkspeed");
        socket.SetFloat(CANJWConstants.GEM_ATTRIBUTE_BUFF_VALUE, 0.02f);

        PutInto(set, 0, knife);

        socket = SocketOf(set.inventory[0].Itemstack, 0);
        Assert.False(socket.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF));
        Assert.False(socket.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF_VALUE));
        Assert.Equal(new[] { "walkspeed" }, (socket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value);
        Assert.Equal(new[] { 0.02f }, (socket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value);
    }

    [AtlasScenario]
    public async Task InscribePacket_Should_NeedOpenInventory_ValidText_AndNoEarlierInscription()
    {
        var (player, set, _) = await JewelerSet();
        PutInto(set, 0, Stack(JewelryCode));

        set.OnReceivedClientPacket(player.Player, InscribePacket, Text("Hello"));
        Assert.Null(Inscription(set));

        player.Player.InventoryManager.OpenInventory(set.inventory);
        try
        {
            set.OnReceivedClientPacket(player.Player, InscribePacket, Text("<b>bold</b>"));
            Assert.Null(Inscription(set));
            set.OnReceivedClientPacket(player.Player, InscribePacket, Text(new string('a', CANJWConstants.INSCRIPTION_MAX_LEN + 1)));
            Assert.Null(Inscription(set));

            set.OnReceivedClientPacket(player.Player, InscribePacket, Text("  Hello  "));
            Assert.Equal("Hello", Inscription(set));

            set.OnReceivedClientPacket(player.Player, InscribePacket, Text("Again"));
            Assert.Equal("Hello", Inscription(set));
        }
        finally
        {
            player.Player.InventoryManager.CloseInventory(set.inventory);
        }
    }

    [AtlasScenario]
    public async Task CanInscribeEvent_Should_BeAbleToDeny()
    {
        var (player, set, _) = await JewelerSet();
        PutInto(set, 0, Stack(JewelryCode));
        Action<CanInscribeEvent> deny = e => e.Allowed = false;
        player.Player.InventoryManager.OpenInventory(set.inventory);
        Mod.Instance.OnCanInscribe += deny;
        try
        {
            set.OnReceivedClientPacket(player.Player, InscribePacket, Text("Hello"));
        }
        finally
        {
            Mod.Instance.OnCanInscribe -= deny;
            player.Player.InventoryManager.CloseInventory(set.inventory);
        }

        Assert.Null(Inscription(set));
    }

    [AtlasScenario]
    public async Task CtrlRightClick_Should_PutJewelryInAndTakeItOut()
    {
        var (player, set, pos) = await JewelerSet();
        player.Entity.ServerControls.CtrlKey = true;
        try
        {
            Hold(player, Stack(JewelryCode));
            Assert.True(set.OnPlayerRightClick(player.Player, Sel(pos)));
            Assert.Equal(JewelryCode, set.inventory[0].Itemstack?.Collectible.Code.ToString());
            Assert.True(Hand(player).Empty);

            Assert.True(set.OnPlayerRightClick(player.Player, Sel(pos)));
            Assert.True(set.inventory[0].Empty);
            Assert.Equal(JewelryCode, Hand(player).Itemstack?.Collectible.Code.ToString());
        }
        finally
        {
            player.Entity.ServerControls.CtrlKey = false;
        }
    }
}
