using System.Reflection;
using System.Runtime.ExceptionServices;
using Atlas.Api;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios.Infrastructure;

/// <summary>
/// Ground for the workstation scenarios: a cleared spot per scenario, stacks in hands, and
/// reflection to reach the members the block entities keep internal or private.
/// </summary>
public abstract class WorkstationScenarioBase : JewelryScenarioBase
{
    /// <summary>How far east of the site the player stands: within chisel reach, out of pickup range.</summary>
    protected const int StandOff = 5;

    private static int lastSite;

    /// <summary>
    /// A fresh spot near spawn on a granite floor with two layers of air above it, and the player
    /// moved next to it (which also loads its chunk).
    /// </summary>
    protected async Task<BlockPos> Site(ITestPlayer player)
    {
        int n = Interlocked.Increment(ref lastSite);
        BlockPos pos = World.Spawn.Offset(8 + (n % 4) * 12, 0, 8 + (n / 4) * 6);
        await player.TeleportTo(pos.Offset(StandOff, 0, 0));
        for (int dx = -2; dx <= StandOff + 1; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                World.SetBlock("game:rock-granite", pos.Offset(dx, -1, dz));
                World.SetBlock("game:air", pos.Offset(dx, 0, dz));
                World.SetBlock("game:air", pos.Offset(dx, 1, dz));
            }
        }
        await World.Ticks(1);
        return pos;
    }

    protected T Place<T>(string blockCode, BlockPos pos) where T : BlockEntity
    {
        World.SetBlock(blockCode, pos);
        T? be = World.BlockEntityAt<T>(pos);
        Assert.True(be != null, $"no {typeof(T).Name} at {pos} after placing {blockCode}");
        return be!;
    }

    protected static BlockSelection Sel(BlockPos pos) => new BlockSelection
    {
        Position = pos.Copy(),
        Face = BlockFacing.UP,
        HitPosition = new Vec3d(0.5, 0.5, 0.5)
    };

    protected ItemStack BlockStack(string code, int size = 1)
    {
        Block block = World.Api.World.GetBlock(new AssetLocation(code));
        Assert.True(block != null && block.BlockId != 0, "no block " + code);
        return new ItemStack(block, size);
    }

    protected ItemStack Stack(string code, int size)
    {
        ItemStack stack = Stack(code);
        stack.StackSize = size;
        return stack;
    }

    protected static ItemSlot Hand(ITestPlayer player) => player.Player.InventoryManager.ActiveHotbarSlot;

    protected static void Hold(ITestPlayer player, ItemStack? stack)
    {
        ItemSlot slot = Hand(player);
        slot.Itemstack = stack;
        slot.MarkDirty();
    }

    protected static void HoldLeft(ITestPlayer player, ItemStack? stack)
    {
        ItemSlot slot = player.Entity.LeftHandItemSlot;
        slot.Itemstack = stack;
        slot.MarkDirty();
    }

    /// <summary>Hotbar and backpack slots: the creative inventory would list every item there is.</summary>
    protected static IEnumerable<ItemSlot> CarriedSlots(ITestPlayer player)
    {
        foreach (string cls in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            IInventory? inv = player.Player.InventoryManager.GetOwnInventory(cls);
            if (inv == null) continue;
            foreach (ItemSlot slot in inv)
            {
                if (slot != null) yield return slot;
            }
        }
    }

    protected static List<ItemStack> Carried(ITestPlayer player, string code) => CarriedSlots(player)
        .Where(s => s.Itemstack?.Collectible?.Code?.ToString() == code)
        .Select(s => s.Itemstack)
        .ToList();

    protected List<ItemStack> DroppedNear(BlockPos pos, string code, int radius = 2) => World.EntitiesIn(pos.Area(radius))
        .OfType<EntityItem>()
        .Where(e => e.Alive && e.Itemstack?.Collectible?.Code?.ToString() == code)
        .Select(e => e.Itemstack)
        .ToList();

    protected static int Durability(ItemStack stack) => stack.Collectible.GetRemainingDurability(stack);

    // ---- reflection --------------------------------------------------------------------------

    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Calls an instance method of any visibility, picked by name and argument count.</summary>
    protected static object? Call(object target, string method, params object?[] args)
    {
        for (Type? t = target.GetType(); t != null; t = t.BaseType)
        {
            MethodInfo? m = t.GetMethods(AnyInstance | BindingFlags.DeclaredOnly)
                .FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            if (m != null) return Invoke(m, target, args);
        }
        throw new MissingMethodException(target.GetType().Name, method);
    }

    protected static object? CallStatic(Type type, string method, params object?[] args)
    {
        MethodInfo m = type.GetMethods(AnyStatic)
            .First(x => x.Name == method && x.GetParameters().Length == args.Length);
        return Invoke(m, null, args);
    }

    private static object? Invoke(MethodInfo m, object? target, object?[] args)
    {
        try
        {
            return m.Invoke(target, args);
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    protected static T GetField<T>(object target, string name) => (T)FindField(target.GetType(), name).GetValue(target)!;

    protected static void SetField(object target, string name, object? value) => FindField(target.GetType(), name).SetValue(target, value);

    private static FieldInfo FindField(Type type, string name)
    {
        for (Type? t = type; t != null; t = t.BaseType)
        {
            FieldInfo? f = t.GetField(name, AnyInstance | BindingFlags.DeclaredOnly);
            if (f != null) return f;
        }
        throw new MissingFieldException(type.Name, name);
    }

    // ---- config ------------------------------------------------------------------------------

    /// <summary>
    /// Makes cutting a gem of this type deterministic: one possible buff, with a single main value
    /// for every tier. Returns the buff's name.
    /// </summary>
    protected string FixGemBuff(string gemType, float value)
    {
        Assert.True(Mod.config.PossibleGemBuffs.TryGetValue(gemType, out HashSet<string>? buffs) && buffs.Count > 0,
            "no buffs for " + gemType);
        string buff = buffs!.First();
        Assert.True(Mod.config.BuffAttributesDict.ContainsKey(buff), "no attributes for " + buff);

        SetConfig(c => c.PossibleGemBuffs[gemType], (c, v) => c.PossibleGemBuffs[gemType] = v, new HashSet<string> { buff });
        var fixedValue = new global::canjewelry.src.Config.BuffAttributes(
            new Dictionary<int, float[]> { { 1, new[] { value } }, { 2, new[] { value } }, { 3, new[] { value } } },
            null, null);
        SetConfig(c => c.BuffAttributesDict[buff], (c, v) => c.BuffAttributesDict[buff] = v, fixedValue);
        NoGemTraits();
        ByTiers();
        return buff;
    }

    /// <summary>Buffs by the old fixed ranges per cut size, not by the gem's weight.</summary>
    protected void ByTiers() =>
        SetConfig(c => c.GemCarats.BuffScaling, (c, v) => c.GemCarats.BuffScaling = v, "tiers");

    /// <summary>Gems roll no traits, so a cut's buffs are not multiplied by a random one.</summary>
    protected void NoGemTraits() =>
        SetConfig(c => c.GemCarats.Traits, (c, v) => c.GemCarats.Traits = v,
            new Dictionary<string, global::canjewelry.src.Config.GemTraitConfig>());
}
