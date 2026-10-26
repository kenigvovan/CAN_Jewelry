using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Hooks = global::canjewelry.src.integration;
using JewelerSet = global::canjewelry.src.jewelry.JewelerSetBE;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

/// <summary>
/// The jeweler's set slot bands and the server side of the dialog's socket packets. The slot
/// handlers, the basic inscription rules and ctrl+click are in <see cref="JewelerSetScenarios"/>.
/// </summary>
public class JewelerSetPacketScenarios : SocketScenarioBase
{
    private const int AddSocketPacket = 1004;
    private const int EncrustPacket = 1005;
    private const int ExtractGemPacket = 1006;
    private const int InscribePacket = 1007;
    private const int ExtractSocketPacket = 1008;
    private const int ClosePacket = 1001;

    private const int FirstGemSlot = 1;
    private const int FirstSocketSlot = 5;

    private static int lastSet;

    /// <summary>A set of its own next to the player, opened for the player unless told not to.</summary>
    private async Task<JewelerSet> PlaceSet(ITestPlayer player, bool open = true)
    {
        int n = Interlocked.Increment(ref lastSet);
        BlockPos pos = player.Position.AddCopy(2, 0, n);
        World.SetBlock("canjewelry:jewelerset-north", pos);
        await World.Ticks(1);

        JewelerSet? found = World.BlockEntityAt<JewelerSet>(pos);
        Assert.True(found != null, "no jeweler set block entity at " + pos);
        JewelerSet set = found!;
        if (open)
        {
            player.Player.InventoryManager.OpenInventory(set.Inventory);
            OnCleanup(() => player.Player.InventoryManager.CloseInventory(set.Inventory));
        }
        return set;
    }

    private static byte[] SocketPacket(int socket)
    {
        var tree = new TreeAttribute();
        tree.SetInt("selectedSocketSlot", socket);
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            tree.ToBytes(writer);
        }
        return ms.ToArray();
    }

    private static byte[] TextPacket(string text)
    {
        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(text);
        }
        return ms.ToArray();
    }

    private static void Put(JewelerSet set, int slot, ItemStack? stack)
    {
        set.Inventory[slot].Itemstack = stack;
        set.Inventory[slot].MarkDirty();
    }

    // ---- block and inventory ----

    [AtlasScenario]
    public async Task Set_Should_HaveNineSingleItemSlots()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player, open: false);

        Assert.Equal(9, set.Inventory.Count);
        Assert.Equal("canjewelerset", set.InventoryClassName);
        Assert.All(Enumerable.Range(0, 9), i => Assert.Equal(1, set.Inventory[i].MaxSlotStackSize));
        Assert.Null(set.Inventory[9]);
        Assert.Null(set.Inventory[-1]);
    }

    // Slot 0 takes a piece that has sockets; the gem band (1..4) cut gems and the socket band
    // (5..8) socket items, each only as wide as the piece has sockets.
    [AtlasScenario]
    public async Task Slots_Should_AcceptByBand()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player, open: false);
        InventoryBase inv = set.Inventory;
        var knife = new DummySlot(Stack(JewelryCode));
        var gem = new DummySlot(CutGem());
        var socket = new DummySlot(Stack(SteelSocket));

        Assert.False(inv.CanContain(inv[1], gem), "a gem slot took a gem with no piece on the set");
        Assert.True(inv.CanContain(inv[0], knife));
        Assert.False(inv.CanContain(inv[0], gem));
        Assert.False(inv.CanContain(inv[0], new DummySlot(Stack("game:flint"))));

        inv[0].Itemstack = Stack(JewelryCode);
        foreach (int i in new[] { 1, 2, 3 }) Assert.True(inv.CanContain(inv[i], gem), $"gem slot {i}");
        Assert.False(inv.CanContain(inv[4], gem), "a fourth gem slot on a three socket piece");
        foreach (int i in new[] { 5, 6, 7 }) Assert.True(inv.CanContain(inv[i], socket), $"socket slot {i}");
        Assert.False(inv.CanContain(inv[8], socket), "a fourth socket slot on a three socket piece");
        Assert.False(inv.CanContain(inv[1], socket));
        Assert.False(inv.CanContain(inv[5], gem));
    }

    [AtlasScenario]
    public async Task RightClick_Should_OpenTheSetForThePlayer()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player, open: false);
        OnCleanup(() => player.Player.InventoryManager.CloseInventory(set.Inventory));

        Assert.True(set.OnPlayerRightClick(player.Player, null!));
        Assert.True(set.Inventory.HasOpened(player.Player));
    }

    // The fourth gem slot rolls a cut as well (the handler used to stop at slot 3).
    [AtlasScenario]
    public async Task FourthGemSlot_Should_RollACut()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player, open: false);

        Put(set, 4, Stack($"canjewelry:gem-cut-normal-{GemType}"));

        ITreeAttribute? tree = set.Inventory[4].Itemstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
        Assert.True(tree != null, "no cut rolled in gem slot 4");
        Assert.Contains(tree!.GetString(CANJWConstants.CUTTING_TYPE), Mod.config.CuttingAttributesDict.Keys);
    }

    // A socket with only half of the old single-buff pair is left as it was.
    [AtlasScenario]
    public async Task PieceSlot_Should_SkipHalfMigratedSockets()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player, open: false);
        ItemStack knife = LegacySocketedJewelry(sockets: 2);
        SocketOf(knife, 1).SetString(CANJWConstants.GEM_ATTRIBUTE_BUFF, "walkspeed");

        Put(set, 0, knife);

        ITreeAttribute partial = SocketOf(set.Inventory[0].Itemstack, 1);
        Assert.True(partial.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF));
        Assert.Null(BuffNames(partial));
    }

    // ---- socket packets ----

    [AtlasScenario]
    public async Task AddSocketPacket_Should_UseTheSocketBand()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player);
        Put(set, 0, Stack(JewelryCode));
        Put(set, FirstSocketSlot + 2, Stack(IronSocket));

        set.OnReceivedClientPacket(player.Player, AddSocketPacket, SocketPacket(2));

        Assert.True(set.Inventory[FirstSocketSlot + 2].Empty);
        Assert.Equal(IronSocket, SocketOf(set.Inventory[0].Itemstack, 2).GetString(CANJWConstants.SOCKET_ITEM_CODE));
    }

    [AtlasScenario]
    public async Task EncrustPacket_Should_UseTheGemBand()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player);
        Put(set, 0, SocketedKnife(2));
        Put(set, FirstGemSlot + 1, CutGem("flawless"));

        set.OnReceivedClientPacket(player.Player, EncrustPacket, SocketPacket(1));

        Assert.True(set.Inventory[FirstGemSlot + 1].Empty);
        Assert.Equal(GemType, SocketOf(set.Inventory[0].Itemstack, 1).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
    }

    // The gem comes back into the gem slot of that socket.
    [AtlasScenario]
    public async Task ExtractGemPacket_Should_ReturnIntoTheGemSlot()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.gemExtractionReturnChance, (c, v) => c.gemExtractionReturnChance = v, 1f);
        SetConfig(c => c.jewelryBreakOnExtractionChance, (c, v) => c.jewelryBreakOnExtractionChance = v, 0f);
        JewelerSet set = await PlaceSet(player);
        Put(set, 0, SocketedKnife(3));
        Put(set, FirstGemSlot + 2, CutGem());
        set.OnReceivedClientPacket(player.Player, EncrustPacket, SocketPacket(2));
        Assert.True(set.Inventory[FirstGemSlot + 2].Empty);

        set.OnReceivedClientPacket(player.Player, ExtractGemPacket, SocketPacket(2));

        Assert.Equal($"canjewelry:gem-cut-exquisite-{GemType}", set.Inventory[FirstGemSlot + 2].Itemstack?.Collectible.Code.ToString());
        Assert.Equal("", SocketOf(set.Inventory[0].Itemstack, 2).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
    }

    [AtlasScenario]
    public async Task ExtractSocketPacket_Should_ReturnIntoTheSocketSlot()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.socketExtractionReturnChance, (c, v) => c.socketExtractionReturnChance = v, 1f);
        JewelerSet set = await PlaceSet(player);
        Put(set, 0, SocketedKnife(2));

        set.OnReceivedClientPacket(player.Player, ExtractSocketPacket, SocketPacket(1));

        Assert.Equal(SteelSocket, set.Inventory[FirstSocketSlot + 1].Itemstack?.Collectible.Code.ToString());
        Assert.Null(EncrustedTree(set.Inventory[0].Itemstack)!.GetTreeAttribute("slot1"));
    }

    // Nothing happens for a player who has not opened the set.
    [AtlasScenario]
    public async Task SocketPackets_Should_BeIgnored_WhenTheSetIsNotOpen()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.socketExtractionReturnChance, (c, v) => c.socketExtractionReturnChance = v, 1f);
        JewelerSet set = await PlaceSet(player, open: false);
        Put(set, 0, SocketedKnife(1));
        Put(set, FirstSocketSlot + 1, Stack(SteelSocket));
        Put(set, FirstGemSlot, CutGem());

        set.OnReceivedClientPacket(player.Player, AddSocketPacket, SocketPacket(1));
        set.OnReceivedClientPacket(player.Player, EncrustPacket, SocketPacket(0));
        set.OnReceivedClientPacket(player.Player, ExtractSocketPacket, SocketPacket(0));

        Assert.False(set.Inventory[FirstSocketSlot + 1].Empty);
        Assert.False(set.Inventory[FirstGemSlot].Empty);
        Assert.Equal(1, EncrustedTree(set.Inventory[0].Itemstack)!.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER));
        Assert.Equal("", SocketOf(set.Inventory[0].Itemstack, 0).GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
    }

    // Socket numbers outside 0..3, or no payload, are dropped.
    [AtlasScenario]
    public async Task BadSocketNumber_Should_BeIgnored()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player);
        Put(set, 0, Stack(JewelryCode));
        Put(set, FirstSocketSlot, Stack(SteelSocket));

        set.OnReceivedClientPacket(player.Player, AddSocketPacket, SocketPacket(4));
        set.OnReceivedClientPacket(player.Player, AddSocketPacket, SocketPacket(-1));
        set.OnReceivedClientPacket(player.Player, AddSocketPacket, null!);

        Assert.False(set.Inventory[FirstSocketSlot].Empty);
        Assert.Null(EncrustedTree(set.Inventory[0].Itemstack));
    }

    [AtlasScenario]
    public async Task ClosePacket_Should_CloseTheSet()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player);
        Assert.True(set.Inventory.HasOpened(player.Player));

        set.OnReceivedClientPacket(player.Player, ClosePacket, null!);

        Assert.False(set.Inventory.HasOpened(player.Player));
    }

    // ---- inscription cases not in JewelerSetScenarios ----

    [AtlasScenario]
    public async Task InscribePacket_Should_RejectBlankText()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player);
        Put(set, 0, Stack(JewelryCode));

        foreach (string text in new[] { "", "   " })
        {
            set.OnReceivedClientPacket(player.Player, InscribePacket, TextPacket(text));
            Assert.False(set.Inventory[0].Itemstack.Attributes.HasAttribute(CANJWConstants.INSCRIPTION), $"accepted \"{text}\"");
        }
    }

    // Cyrillic, digits and the allowed punctuation, at exactly the maximum length.
    [AtlasScenario]
    public async Task InscribePacket_Should_AcceptCyrillicAtMaxLength()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player);
        Put(set, 0, Stack(JewelryCode));
        string text = "Привет, 1st! " + new string('ё', CANJWConstants.INSCRIPTION_MAX_LEN - 13);
        Assert.Equal(CANJWConstants.INSCRIPTION_MAX_LEN, text.Length);

        set.OnReceivedClientPacket(player.Player, InscribePacket, TextPacket(text));

        Assert.Equal(text, set.Inventory[0].Itemstack.Attributes.GetString(CANJWConstants.INSCRIPTION));
    }

    // The permission hook is asked with the player and the piece on the set.
    [AtlasScenario]
    public async Task InscribeHook_Should_SeePlayerAndPiece()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player);
        Put(set, 0, Stack(JewelryCode));
        Hooks.CanInscribeEvent? seen = null;
        Action<Hooks.CanInscribeEvent> watch = e => seen = e;
        Mod mod = Mod.Instance;
        mod.OnCanInscribe += watch;
        OnCleanup(() => mod.OnCanInscribe -= watch);

        set.OnReceivedClientPacket(player.Player, InscribePacket, TextPacket("Mine"));

        Assert.NotNull(seen);
        Assert.Same(player.Player, seen!.Player);
        Assert.Same(set.Inventory[0].Itemstack, seen.Jewelry);
        Assert.True(seen.Allowed);
        Assert.Equal("Mine", set.Inventory[0].Itemstack.Attributes.GetString(CANJWConstants.INSCRIPTION));
    }

    [AtlasScenario]
    public async Task InscribePacket_OnEmptySet_Should_DoNothing()
    {
        ITestPlayer player = await Join();
        JewelerSet set = await PlaceSet(player);

        set.OnReceivedClientPacket(player.Player, InscribePacket, TextPacket("Mine"));
        Assert.True(set.Inventory[0].Empty);
    }
}
