using System.Reflection;
using Atlas.Api;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using BuffAffected = global::canjewelry.src.eb.CANGemBuffAffected;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using GemBuffType = global::canjewelry.src.cb.EnumGemBuffType;

namespace canjewelry.scenarios.Infrastructure;

/// <summary>
/// Ground for the scenarios on how gem buffs reach the player: stacks with filled sockets, the
/// places a player keeps them and a read of what the mod wrote to the stats.
/// </summary>
public abstract class BuffScenarioBase : JewelryScenarioBase
{
    // The stats layer the mod writes its summed up buffs to, and the one older versions left behind.
    protected const string BuffKey = "canencrusted";
    protected const string LegacyNegKey = "canencrustedneg";
    protected const string AdditionalInventory = "additionaljewelrycharacter";

    // Vanilla wearables with sockets in the default config: one on the helmet, two on the body.
    protected const string ArmorBody = "game:armor-body-plate-iron";
    protected const string ArmorHead = "game:armor-head-plate-iron";

    /// <summary>Joins a player and waits until the buff behavior has hooked its inventories.</summary>
    protected async Task<ITestPlayer> JoinReady()
    {
        ITestPlayer player = await Join();
        await World.Until(() => BuffsOf(player).initialized, "buff behavior initialized");
        return player;
    }

    protected static BuffAffected BuffsOf(ITestPlayer player)
    {
        BuffAffected? beh = player.Entity.GetBehavior<BuffAffected>();
        Assert.True(beh != null, "the player has no cangembuffaffected behavior");
        return beh!;
    }

    /// <summary>What the mod wrote to a stat, null when it wrote nothing there.</summary>
    protected static float? GemStat(EntityPlayer ep, string stat, string key = BuffKey)
    {
        // The stats indexer throws on a missing category.
        foreach (var category in ep.Stats)
        {
            if (category.Key == stat && category.Value.ValuesByKey.TryGetValue(key, out var value)) return value.Value;
        }
        return null;
    }

    protected static float? GemStat(ITestPlayer player, string stat) => GemStat(player.Entity, stat);

    protected static bool Near(float? actual, float? expected) =>
        actual == null || expected == null ? actual == expected : Math.Abs(actual.Value - expected.Value) < 1e-4f;

    protected Task UntilStat(ITestPlayer player, string stat, float? expected) =>
        World.Until(() => Near(GemStat(player, stat), expected), $"{stat} gem buff to become {expected?.ToString() ?? "absent"}");

    protected static void AssertStat(ITestPlayer player, string stat, float? expected)
    {
        float? actual = GemStat(player, stat);
        Assert.True(Near(actual, expected), $"{stat}: expected {expected?.ToString() ?? "absent"}, was {actual?.ToString() ?? "absent"}");
    }

    /// <summary>
    /// A piece with one filled socket per gem, written as the current version writes them. The
    /// buff code reads only these attributes, so no gem has to go through a cut.
    /// </summary>
    protected ItemStack Socketed(string code, params (string Buff, float Value)[] gems)
    {
        ItemStack stack = Stack(code);
        ITreeAttribute encrusted = new TreeAttribute();
        encrusted.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, gems.Length);
        stack.Attributes[CANJWConstants.ITEM_ENCRUSTED_STRING] = encrusted;
        for (int i = 0; i < gems.Length; i++)
        {
            SetSocket(stack, i, new[] { gems[i].Buff }, new[] { gems[i].Value });
        }
        Assert.True(Encrustable.GetMaxAmountSockets(stack) >= gems.Length, $"{code} takes fewer than {gems.Length} sockets");
        return stack;
    }

    /// <summary>Fills a socket with a gem carrying any number of buffs.</summary>
    protected static void SetSocket(ItemStack stack, int socket, string[] buffs, float[] values)
    {
        ITreeAttribute slot = NewSocket(stack, socket);
        slot.SetInt(CANJWConstants.GEM_BUFF_TYPE, (int)GemBuffType.STATS_BUFF);
        slot[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(buffs);
        slot[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(values);
    }

    /// <summary>Fills a socket the way the oldest versions did: a single attributeBuff pair.</summary>
    protected static void SetLegacyAttributeSocket(ItemStack stack, int socket, string buff, float value, GemBuffType? type = null)
    {
        ITreeAttribute slot = NewSocket(stack, socket);
        slot.SetString(CANJWConstants.GEM_ATTRIBUTE_BUFF, buff);
        slot.SetFloat(CANJWConstants.GEM_ATTRIBUTE_BUFF_VALUE, value);
        if (type != null) slot.SetInt(CANJWConstants.GEM_BUFF_TYPE, (int)type.Value);
    }

    private static ITreeAttribute NewSocket(ItemStack stack, int socket)
    {
        ITreeAttribute encrusted = stack.Attributes.GetOrAddTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
        encrusted.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, Math.Max(encrusted.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER), socket + 1));
        ITreeAttribute slot = new TreeAttribute();
        slot.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, 3);
        slot.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, 1);
        slot.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, GemType);
        encrusted["slot" + socket] = slot;
        return slot;
    }

    protected static void Put(ItemSlot slot, ItemStack? stack)
    {
        slot.Itemstack = stack;
        slot.MarkDirty();
    }

    protected static void Hold(ITestPlayer player, ItemStack? stack) => Put(player.Player.InventoryManager.ActiveHotbarSlot, stack);

    protected static IInventory Hotbar(ITestPlayer player) => player.Player.InventoryManager.GetOwnInventory("hotbar");

    protected static IInventory Character(ITestPlayer player) => player.Player.InventoryManager.GetOwnInventory("character");

    protected static IInventory? Additional(ITestPlayer player) => player.Player.InventoryManager.GetOwnInventory(AdditionalInventory);

    /// <summary>The character slot that takes this wearable.</summary>
    protected static ItemSlot CharacterSlotFor(ITestPlayer player, ItemStack wearable)
    {
        IInventory inv = Character(player);
        for (int i = 0; i < inv.Count; i++)
        {
            if (inv[i] != null && inv[i].CanHold(new DummySlot(wearable))) return inv[i];
        }
        Assert.Fail("no character slot takes " + wearable.Collectible.Code);
        return null!;
    }

    protected static ItemSlot Wear(ITestPlayer player, ItemStack wearable)
    {
        ItemSlot slot = CharacterSlotFor(player, wearable);
        Put(slot, wearable);
        return slot;
    }

    /// <summary>
    /// Switches the selected hotbar slot as the server does on the client's packet: the number
    /// changes, then the engine's after-change event fires, which the mod has a postfix on.
    /// </summary>
    protected void SelectHotbarSlot(ITestPlayer player, int to)
    {
        int from = player.Player.InventoryManager.ActiveHotbarSlotNumber;
        player.Player.InventoryManager.ActiveHotbarSlotNumber = to;
        object eventManager = ServerEventManager();
        MethodInfo trigger = eventManager.GetType().GetMethod("TriggerAfterActiveSlotChanged")!;
        trigger.Invoke(eventManager, new object[] { player.Player, from, to });
    }

    // ServerMain.EventManager, the CoreServerEventManager the mod patches.
    protected object ServerEventManager()
    {
        object server = World.Api.World;
        FieldInfo? field = server.GetType().GetField("EventManager", BindingFlags.Public | BindingFlags.Instance);
        Assert.True(field != null, "ServerMain.EventManager not found");
        return field!.GetValue(server)!;
    }

    /// <summary>Drops the player and joins it again under the same name.</summary>
    protected async Task<ITestPlayer> Rejoin(ITestPlayer player)
    {
        string name = player.Player.PlayerName;
        player.Player.Disconnect();
        await World.Until(() => !player.IsConnected, "player disconnected");
        ITestPlayer again = Track(await World.JoinPlayer(name));
        await World.Until(() => BuffsOf(again).initialized, "buff behavior initialized after rejoin");
        return again;
    }
}
