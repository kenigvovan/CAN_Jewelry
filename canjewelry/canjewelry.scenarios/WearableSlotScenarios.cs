using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using AdditionalJewelryInventory = global::canjewelry.src.inventories.InventoryCharacterAdditionalJewelry;
using AdditionalJewelrySlot = global::canjewelry.src.inventories.ItemSlotAdditionalJewelryCharacter;
using DressType = global::canjewelry.src.inventories.EnumCharacterAdditionalJewelryDressType;

namespace canjewelry.scenarios;

/// <summary>
/// Which of the player's slots are buff sources: the selected hotbar slot, the character
/// (clothes and armor) slots and the extra jewelry slots, and nothing else.
/// </summary>
public class WearableSlotScenarios : BuffScenarioBase
{
    private const int AdditionalSlotCount = 18;

    private ItemStack Earring(params (string Buff, float Value)[] gems)
    {
        ItemStack stack = Socketed(JewelryCode, gems);
        stack.Attributes.SetString("clothescategory", "leftearrings");
        return stack;
    }

    [AtlasScenario]
    public async Task NonSelectedHotbarSlot_Should_GiveNothing()
    {
        ITestPlayer player = await JoinReady();
        int other = player.Player.InventoryManager.ActiveHotbarSlotNumber == 3 ? 4 : 3;

        Put(Hotbar(player)[other], Socketed(JewelryCode, ("walkspeed", 0.04f)));
        BuffsOf(player).RecomputeBuffs();

        AssertStat(player, "walkspeed", null);
    }

    // Selecting the slot applies the buff, selecting another one takes it away again.
    [AtlasScenario]
    public async Task SelectingHotbarSlot_Should_SwitchBuffs()
    {
        ITestPlayer player = await JoinReady();
        SelectHotbarSlot(player, 0);
        Put(Hotbar(player)[0], Socketed(JewelryCode, ("walkspeed", 0.04f)));
        Put(Hotbar(player)[2], Socketed(JewelryCode, ("miningSpeedMul", 0.1f)));
        await UntilStat(player, "walkspeed", 0.04f);

        SelectHotbarSlot(player, 2);
        await UntilStat(player, "miningSpeedMul", 0.1f);
        AssertStat(player, "walkspeed", null);

        SelectHotbarSlot(player, 5);
        await UntilStat(player, "miningSpeedMul", null);
        AssertStat(player, "walkspeed", null);
    }

    [AtlasScenario]
    public async Task WornArmor_Should_BuffUntilTakenOff()
    {
        ITestPlayer player = await JoinReady();
        ItemSlot slot = Wear(player, Socketed(ArmorBody, ("walkspeed", 0.03f), ("miningSpeedMul", 0.1f)));
        await UntilStat(player, "walkspeed", 0.03f);
        AssertStat(player, "miningSpeedMul", 0.1f);

        Put(slot, null);

        await UntilStat(player, "walkspeed", null);
        AssertStat(player, "miningSpeedMul", null);
    }

    // Swapping one piece for another replaces its buffs rather than adding to them.
    [AtlasScenario]
    public async Task SwappedArmor_Should_ReplaceBuffs()
    {
        ITestPlayer player = await JoinReady();
        ItemSlot slot = Wear(player, Socketed(ArmorBody, ("walkspeed", 0.03f)));
        await UntilStat(player, "walkspeed", 0.03f);

        Put(slot, Socketed(ArmorBody, ("miningSpeedMul", 0.2f)));

        await UntilStat(player, "miningSpeedMul", 0.2f);
        AssertStat(player, "walkspeed", null);
    }

    // A wearable counts where it is worn; held in the hand it gives nothing.
    [AtlasScenario]
    public async Task WearableInHand_Should_GiveNothing()
    {
        ITestPlayer player = await JoinReady();

        Hold(player, Socketed(ArmorBody, ("walkspeed", 0.03f)));
        BuffsOf(player).RecomputeBuffs();

        AssertStat(player, "walkspeed", null);
    }

    [AtlasScenario]
    public async Task AdditionalInventory_Should_HaveEighteenTypedSlots()
    {
        ITestPlayer player = await JoinReady();

        IInventory? inv = Additional(player);
        Assert.IsType<AdditionalJewelryInventory>(inv);
        Assert.Equal(AdditionalSlotCount, inv!.Count);
        for (int i = 0; i < inv.Count; i++)
        {
            var slot = Assert.IsType<AdditionalJewelrySlot>(inv[i]);
            Assert.Equal((DressType)i, slot.Type);
            Assert.Equal(EnumItemStorageFlags.Outfit, slot.StorageType);
        }
        Assert.Null(inv[-1]);
        Assert.Null(inv[AdditionalSlotCount]);
    }

    // A slot takes only an item of its own dress type; a stack may name its type itself.
    [AtlasScenario]
    public async Task AdditionalSlot_Should_TakeOnlyItsDressType()
    {
        ITestPlayer player = await JoinReady();
        IInventory inv = Additional(player)!;
        var earring = new DummySlot(Earring(("walkspeed", 0.01f)));
        var plain = new DummySlot(Stack(JewelryCode));

        // The game's ItemSlot also wants the Outfit storage flag, which real jewelry carries and
        // the knife standing in for it does not.
        CollectibleObject knife = earring.Itemstack.Collectible;
        EnumItemStorageFlags flags = knife.StorageFlags;
        knife.StorageFlags = flags | EnumItemStorageFlags.Outfit;
        AddCleanup(() => knife.StorageFlags = flags);

        Assert.True(inv[(int)DressType.LeftEarrings].CanHold(earring));
        Assert.True(inv[(int)DressType.LeftEarrings].CanTakeFrom(earring));
        Assert.False(inv[(int)DressType.RightEarrings].CanHold(earring));
        Assert.False(inv[(int)DressType.RightEarrings].CanTakeFrom(earring));
        Assert.False(inv[(int)DressType.LeftEarrings].CanHold(plain));
        Assert.False(AdditionalJewelrySlot.IsDressType(null!, DressType.LeftEarrings));
        Assert.True(AdditionalJewelrySlot.IsDressType(earring.Itemstack, DressType.LeftEarrings));
    }

    // Shift clicks never route an item into the extra slots.
    [AtlasScenario]
    public async Task AdditionalInventory_Should_NeverBeBestSuited()
    {
        ITestPlayer player = await JoinReady();
        var inv = (AdditionalJewelryInventory)Additional(player)!;
        var earring = new DummySlot(Earring(("walkspeed", 0.01f)));
        var op = new ItemStackMoveOperation(World.Api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 1);

        WeightedSlot best = inv.GetBestSuitedSlot(earring, op);

        Assert.Null(best.slot);
    }

    [AtlasScenario]
    public async Task AdditionalSlotJewelry_Should_BuffUntilTakenOff()
    {
        ITestPlayer player = await JoinReady();
        ItemSlot slot = Additional(player)![(int)DressType.LeftEarrings];

        Put(slot, Earring(("walkspeed", 0.02f)));
        await UntilStat(player, "walkspeed", 0.02f);

        Put(slot, null);
        await UntilStat(player, "walkspeed", null);
    }

    // Additional slots, character slots and the hand all add into one sum.
    [AtlasScenario]
    public async Task AllSources_Should_Sum()
    {
        ITestPlayer player = await JoinReady();

        Put(Additional(player)![(int)DressType.LeftEarrings], Earring(("walkspeed", 0.01f)));
        Wear(player, Socketed(ArmorBody, ("walkspeed", 0.02f)));
        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));

        await UntilStat(player, "walkspeed", 0.07f);
    }

    // Switched off, whatever a save still keeps in the extra slots gives no buffs.
    [AtlasScenario]
    public async Task DisabledAdditionalSlots_Should_GiveNothing()
    {
        ITestPlayer player = await JoinReady();
        SetConfig(c => c.enableAdditionalJewelrySlots, (c, v) => c.enableAdditionalJewelrySlots = v, false);

        Put(Additional(player)![(int)DressType.LeftEarrings], Earring(("walkspeed", 0.02f)));
        Hold(player, Socketed(JewelryCode, ("miningSpeedMul", 0.1f)));
        await UntilStat(player, "miningSpeedMul", 0.1f);

        AssertStat(player, "walkspeed", null);
        BuffsOf(player).RecomputeBuffs();
        AssertStat(player, "walkspeed", null);
    }

    // The extra slots keep their contents when the owner dies and are never discarded wholesale.
    [AtlasScenario]
    public async Task AdditionalInventory_Should_KeepContentsOnDeathAndDiscard()
    {
        ITestPlayer player = await JoinReady();
        var inv = (AdditionalJewelryInventory)Additional(player)!;
        Put(inv[(int)DressType.LeftEarrings], Earring(("walkspeed", 0.02f)));

        inv.OnOwningEntityDeath(player.Entity.Pos.XYZ);
        inv.DiscardAll();

        Assert.False(inv[(int)DressType.LeftEarrings].Empty);
    }

    // A save holding fewer slots loads into the full set, contents kept in place.
    [AtlasScenario]
    public async Task AdditionalInventory_Should_LoadShortSaveIntoFullSet()
    {
        ITestPlayer player = await JoinReady();
        var source = (AdditionalJewelryInventory)Additional(player)!;
        Put(source[5], Earring(("walkspeed", 0.02f)));
        ITreeAttribute tree = new TreeAttribute();
        source.ToTreeAttributes(tree);
        tree.SetInt("qslots", 7);

        var loaded = new AdditionalJewelryInventory(AdditionalInventory + "-scenario", World.Api);
        loaded.FromTreeAttributes(tree);

        Assert.Equal(AdditionalSlotCount, loaded.Count);
        Assert.Equal(JewelryCode, loaded[5].Itemstack?.Collectible.Code.ToString());
        Assert.IsType<AdditionalJewelrySlot>(loaded[17]);
        Put(source[5], null);
    }

    [AtlasScenario]
    public async Task AdditionalSlotBuffs_Should_ComeBackAfterRejoin()
    {
        ITestPlayer player = await JoinReady();
        Put(Additional(player)![(int)DressType.LeftEarrings], Earring(("walkspeed", 0.02f)));
        await UntilStat(player, "walkspeed", 0.02f);

        ITestPlayer again = await Rejoin(player);

        Assert.False(Additional(again)![(int)DressType.LeftEarrings].Empty, "the extra slot lost its jewelry over a rejoin");
        await UntilStat(again, "walkspeed", 0.02f);
    }
}
