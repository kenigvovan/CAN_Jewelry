using Atlas.Api;
using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using GemBuffType = global::canjewelry.src.cb.EnumGemBuffType;

namespace canjewelry.scenarios;

/// <summary>
/// Gem buffs on what the player holds and wears end up in the player's stats, under one layer
/// of the mod's own, summed up and capped, and leave again with the gear.
/// </summary>
public class BuffScenarios : BuffScenarioBase
{
    [AtlasScenario]
    public async Task Behavior_Should_InitializeOnJoin()
    {
        ITestPlayer player = await JoinReady();
        Assert.True(BuffsOf(player).initialized);
    }

    [AtlasScenario]
    public async Task HeldGemJewelry_Should_BuffPlayer()
    {
        ITestPlayer player = await JoinReady();

        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));

        await UntilStat(player, "walkspeed", 0.04f);
    }

    // The value is rebuilt on every login, so it is written as non persistent.
    [AtlasScenario]
    public async Task BuffStat_Should_NotBePersistent()
    {
        ITestPlayer player = await JoinReady();
        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));
        await UntilStat(player, "walkspeed", 0.04f);

        Assert.False(player.Entity.Stats["walkspeed"].ValuesByKey[BuffKey].Persistent);
    }

    // Gone means the layer is removed, not left at zero.
    [AtlasScenario]
    public async Task EmptiedHand_Should_RemoveBuffLayer()
    {
        ITestPlayer player = await JoinReady();
        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));
        await UntilStat(player, "walkspeed", 0.04f);

        Hold(player, null);

        await UntilStat(player, "walkspeed", null);
    }

    [AtlasScenario]
    public async Task GemsInEverySocket_Should_Sum()
    {
        ITestPlayer player = await JoinReady();

        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.02f), ("walkspeed", 0.03f), ("miningSpeedMul", 0.1f)));

        await UntilStat(player, "walkspeed", 0.05f);
        AssertStat(player, "miningSpeedMul", 0.1f);
    }

    // One gem can carry several buffs (a main stat and a secondary one).
    [AtlasScenario]
    public async Task GemWithSeveralBuffs_Should_ApplyEach()
    {
        ITestPlayer player = await JoinReady();
        ItemStack knife = Socketed(JewelryCode, ("walkspeed", 0.01f));
        SetSocket(knife, 0, new[] { "walkspeed", "miningSpeedMul" }, new[] { 0.02f, 0.05f });

        Hold(player, knife);

        await UntilStat(player, "walkspeed", 0.02f);
        AssertStat(player, "miningSpeedMul", 0.05f);
    }

    // Held and worn gear add up into the same layer.
    [AtlasScenario]
    public async Task HeldAndWornGear_Should_Sum()
    {
        ITestPlayer player = await JoinReady();

        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.02f)));
        Wear(player, Socketed(ArmorBody, ("walkspeed", 0.03f), ("walkspeed", 0.01f)));
        Wear(player, Socketed(ArmorHead, ("walkspeed", 0.04f)));

        await UntilStat(player, "walkspeed", 0.10f);
    }

    // Durability and temporal grasp are item side buffs, never player stats.
    [AtlasScenario]
    public async Task ItemSideBuffs_Should_NotReachStats()
    {
        ITestPlayer player = await JoinReady();
        ItemStack pick = Socketed("game:pickaxe-steel", ("walkspeed", 0.01f));
        SetSocket(pick, 0, new[] { CANJWConstants.CANDURABILITY_STRING, "walkspeed" }, new[] { 0.2f, 0.03f });
        SetSocket(pick, 1, new[] { CANJWConstants.TEMPORALGRASP }, new[] { 0.5f });

        Hold(player, pick);

        await UntilStat(player, "walkspeed", 0.03f);
        AssertStat(player, CANJWConstants.CANDURABILITY_STRING, null);
        AssertStat(player, CANJWConstants.TEMPORALGRASP, null);
        Assert.Equal(new[] { "walkspeed" }, BuffsOf(player).GetItemStackBuffs(pick).Keys.ToArray());
    }

    [AtlasScenario]
    public async Task MaxBuffValue_Should_CapFromAbove()
    {
        ITestPlayer player = await JoinReady();
        SetConfig(c => c.max_buff_values, (c, v) => c.max_buff_values = v, new Dictionary<string, float> { { "walkspeed", 0.05f } });

        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f), ("walkspeed", 0.04f)));

        await UntilStat(player, "walkspeed", 0.05f);
    }

    // A negative cap bounds a stat that gems push down.
    [AtlasScenario]
    public async Task NegativeMaxBuffValue_Should_CapFromBelow()
    {
        ITestPlayer player = await JoinReady();
        SetConfig(c => c.max_buff_values, (c, v) => c.max_buff_values = v, new Dictionary<string, float> { { "hungerrate", -0.2f } });

        Hold(player, Socketed(JewelryCode, ("hungerrate", -0.15f), ("hungerrate", -0.15f)));

        await UntilStat(player, "hungerrate", -0.2f);
    }

    // Caps apply to the sum across all gear, and stats with no cap are summed in full.
    [AtlasScenario]
    public async Task MaxBuffValue_Should_CapSumOverAllGear()
    {
        ITestPlayer player = await JoinReady();
        SetConfig(c => c.max_buff_values, (c, v) => c.max_buff_values = v, new Dictionary<string, float> { { "walkspeed", 0.05f } });

        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.03f), ("miningSpeedMul", 0.3f)));
        Wear(player, Socketed(ArmorBody, ("walkspeed", 0.03f), ("miningSpeedMul", 0.3f)));

        await UntilStat(player, "walkspeed", 0.05f);
        AssertStat(player, "miningSpeedMul", 0.6f);
    }

    // Only the mod's own layer is capped; what else sits on the stat is left alone.
    [AtlasScenario]
    public async Task MaxBuffValue_Should_IgnoreOtherLayers()
    {
        ITestPlayer player = await JoinReady();
        SetConfig(c => c.max_buff_values, (c, v) => c.max_buff_values = v, new Dictionary<string, float> { { "walkspeed", 0.05f } });
        player.Entity.Stats.Set("walkspeed", "scenario", 0.5f, false);

        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));

        await UntilStat(player, "walkspeed", 0.04f);
        Assert.Equal(0.5f, GemStat(player.Entity, "walkspeed", "scenario"));
        player.Entity.Stats.Remove("walkspeed", "scenario");
    }

    [AtlasScenario]
    public async Task MaxHealthGem_Should_RaiseMaxHealth()
    {
        ITestPlayer player = await JoinReady();
        SetConfig(c => c.max_buff_values, (c, v) => c.max_buff_values = v, new Dictionary<string, float>());
        var health = player.Entity.GetBehavior<EntityBehaviorHealth>();
        health.UpdateMaxHealth();
        float before = health.MaxHealth;

        Hold(player, Socketed(JewelryCode, ("maxhealthExtraPoints", 3f), ("maxhealthExtraPoints", 2f)));
        await UntilStat(player, "maxhealthExtraPoints", 5f);

        // The health behavior reads the stat whenever it recalculates.
        health.UpdateMaxHealth();
        Assert.Equal(before + 5f, health.MaxHealth, 3);
    }

    // Sockets past what the item can take are not read.
    [AtlasScenario]
    public async Task SocketsPastItemMaximum_Should_BeIgnored()
    {
        ITestPlayer player = await JoinReady();
        ItemStack knife = Socketed(JewelryCode, ("walkspeed", 0.01f));
        int max = Encrustable.GetMaxAmountSockets(knife);
        SetSocket(knife, max, new[] { "miningSpeedMul" }, new[] { 0.5f });

        var buffs = BuffsOf(player).GetItemStackBuffs(knife);

        Assert.Equal(new[] { "walkspeed" }, buffs.Keys.ToArray());
        Assert.Empty(BuffsOf(player).GetItemStackBuffs(Stack(JewelryCode)));
        Assert.Empty(BuffsOf(player).GetItemStackBuffs(null!));
    }

    // Empty sockets of older versions (no buffs, empty gem type) give nothing and break nothing.
    [AtlasScenario]
    public async Task EmptyLegacySockets_Should_GiveNothing()
    {
        ITestPlayer player = await JoinReady();

        Assert.Empty(BuffsOf(player).GetItemStackBuffs(LegacySocketedJewelry(sockets: 3)));
    }

    // The oldest format: one attributeBuff pair per socket.
    [AtlasScenario]
    public async Task LegacyAttributeSocket_Should_BuffPlayer()
    {
        ITestPlayer player = await JoinReady();
        ItemStack knife = Stack(JewelryCode);
        SetLegacyAttributeSocket(knife, 0, "walkspeed", 0.03f);
        SetLegacyAttributeSocket(knife, 1, "walkspeed", 0.02f, GemBuffType.STATS_BUFF);

        Hold(player, knife);

        await UntilStat(player, "walkspeed", 0.05f);
    }

    // In the oldest format a socket marked as anything but a stats buff gives nothing, and the
    // item side buffs are skipped there as well.
    [AtlasScenario]
    public async Task LegacyAttributeSocket_Should_SkipNonStatBuffs()
    {
        ITestPlayer player = await JoinReady();
        ItemStack knife = Stack(JewelryCode);
        SetLegacyAttributeSocket(knife, 0, "walkspeed", 0.03f, GemBuffType.ONE_TIME_APPLIED);
        SetLegacyAttributeSocket(knife, 1, CANJWConstants.CANDURABILITY_STRING, 0.2f);
        SetLegacyAttributeSocket(knife, 2, "miningSpeedMul", 0.1f);

        var buffs = BuffsOf(player).GetItemStackBuffs(knife);

        Assert.Equal(new[] { "miningSpeedMul" }, buffs.Keys.ToArray());
        Assert.Equal(0.1f, buffs["miningSpeedMul"], 4);
    }

    // Both formats on one piece add up.
    [AtlasScenario]
    public async Task MixedSocketFormats_Should_Sum()
    {
        ITestPlayer player = await JoinReady();
        ItemStack knife = Socketed(JewelryCode, ("walkspeed", 0.02f));
        SetLegacyAttributeSocket(knife, 1, "walkspeed", 0.03f);

        Hold(player, knife);

        await UntilStat(player, "walkspeed", 0.05f);
    }

    // A gem set into a socket by the encrusting code gives exactly the buff its cut carried.
    [AtlasScenario]
    public async Task EncrustedGem_Should_BuffPlayer()
    {
        ITestPlayer player = await JoinReady();
        var jewelry = new DummySlot(LegacySocketedJewelry());
        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry,
            new DummySlot(LegacyCutGem("normal", "walkspeed", 0.03f)), 0, player.Player));

        Hold(player, jewelry.Itemstack);

        await UntilStat(player, "walkspeed", 0.03f);
    }

    [AtlasScenario]
    public async Task ActiveSlotPostfix_Should_BeApplied()
    {
        object eventManager = ServerEventManager();
        var method = eventManager.GetType().GetMethod("TriggerAfterActiveSlotChanged");
        Patches? patches = Harmony.GetPatchInfo(method);

        Assert.True(patches != null && patches.Postfixes.Any(p => p.PatchMethod.Name == "Postfix_TriggerAfterActiveSlotChanged"),
            "no buff postfix on CoreServerEventManager.TriggerAfterActiveSlotChanged");
        await World.Ticks(1);
    }

    // The admin clear drops every gem layer, the old negative one included, and reapply brings
    // back what the gear gives.
    [AtlasScenario]
    public async Task ClearAndReapplyCommands_Should_DropAndRebuildBuffs()
    {
        ITestPlayer player = await JoinReady();
        string name = player.Player.PlayerName;
        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));
        await UntilStat(player, "walkspeed", 0.04f);
        player.Entity.Stats.Set("miningSpeedMul", LegacyNegKey, -0.1f, true);

        CommandResult cleared = await player.ExecuteCommand($"/canjewelry clearbuffs {name}");
        Assert.True(cleared.Ok, cleared.Message);
        AssertStat(player, "walkspeed", null);
        Assert.Null(GemStat(player.Entity, "miningSpeedMul", LegacyNegKey));

        CommandResult reapplied = await player.ExecuteCommand($"/canjewelry reapplybuffs {name}");
        Assert.True(reapplied.Ok, reapplied.Message);
        AssertStat(player, "walkspeed", 0.04f);
    }

    // After a clear, the next ordinary recompute writes everything again rather than taking the
    // stats as unchanged.
    [AtlasScenario]
    public async Task RecomputeAfterClear_Should_WriteAgain()
    {
        ITestPlayer player = await JoinReady();
        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));
        await UntilStat(player, "walkspeed", 0.04f);

        global::canjewelry.src.eb.CANGemBuffAffected.ClearBuffs(player.Entity);
        AssertStat(player, "walkspeed", null);
        BuffsOf(player).RecomputeBuffs();

        AssertStat(player, "walkspeed", 0.04f);
    }

    // Layers the gear does not account for (from a save of the persistent era, the old negative
    // key) are swept on a forced recompute; the rest is kept.
    [AtlasScenario]
    public async Task ForcedRecompute_Should_SweepForeignLayers()
    {
        ITestPlayer player = await JoinReady();
        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));
        await UntilStat(player, "walkspeed", 0.04f);
        player.Entity.Stats.Set("rangedWeaponsDamage", BuffKey, 0.5f, true);
        player.Entity.Stats.Set("walkspeed", LegacyNegKey, -0.1f, true);

        BuffsOf(player).RecomputeBuffs(force: true);

        AssertStat(player, "rangedWeaponsDamage", null);
        Assert.Null(GemStat(player.Entity, "walkspeed", LegacyNegKey));
        AssertStat(player, "walkspeed", 0.04f);
    }

    // The debug log path walks the same sources and must not change the outcome.
    [AtlasScenario]
    public async Task DebugMode_Should_NotChangeBuffs()
    {
        ITestPlayer player = await JoinReady();
        SetConfig(c => c.debugMode, (c, v) => c.debugMode = v, true);
        Wear(player, Socketed(ArmorBody, ("walkspeed", 0.03f)));
        Hold(player, LegacySocketedJewelry());

        BuffsOf(player).RecomputeBuffs();

        AssertStat(player, "walkspeed", 0.03f);
        await World.Ticks(1);
    }

    // With buffs switched off, a player joining is never hooked up, and gear gives nothing.
    [AtlasScenario]
    public async Task TurnOffBuffs_Should_LeaveJoiningPlayerUnbuffed()
    {
        SetConfig(c => c.TurnOffBuffs, (c, v) => c.TurnOffBuffs = v, true);
        ITestPlayer player = await Join();

        Hold(player, Socketed(JewelryCode, ("walkspeed", 0.04f)));
        await World.Ticks(5);

        Assert.False(BuffsOf(player).initialized);
        AssertStat(player, "walkspeed", null);
    }

    // With buffs switched off, a gem still goes into a socket but brings no buffs along.
    [AtlasScenario]
    public async Task TurnOffBuffs_Should_EncrustWithoutBuffs()
    {
        ITestPlayer player = await Join();
        SetConfig(c => c.TurnOffBuffs, (c, v) => c.TurnOffBuffs = v, true);
        var jewelry = new DummySlot(LegacySocketedJewelry());

        Assert.True(Encrustable.TryToEncrustGemsIntoSockets(ScratchInventory(), jewelry, new DummySlot(LegacyCutGem()), 0, player.Player));

        ITreeAttribute socket = SocketOf(jewelry.Itemstack, 0);
        Assert.Equal(GemType, socket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET));
        Assert.False(socket.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES));
        Assert.Empty(BuffsOf(player).GetItemStackBuffs(jewelry.Itemstack));
    }

    // Buffs are not saved; on a new login they are rebuilt from what the player wears.
    [AtlasScenario]
    public async Task WornBuffs_Should_ComeBackAfterRejoin()
    {
        ITestPlayer player = await JoinReady();
        Wear(player, Socketed(ArmorBody, ("walkspeed", 0.03f)));
        Hold(player, Socketed(JewelryCode, ("miningSpeedMul", 0.1f)));
        await UntilStat(player, "walkspeed", 0.03f);
        await UntilStat(player, "miningSpeedMul", 0.1f);

        ITestPlayer again = await Rejoin(player);

        await UntilStat(again, "walkspeed", 0.03f);
        await UntilStat(again, "miningSpeedMul", 0.1f);
    }
}
