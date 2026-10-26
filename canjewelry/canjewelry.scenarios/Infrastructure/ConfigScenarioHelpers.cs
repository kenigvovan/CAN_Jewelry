using System.Reflection;
using Atlas.Api;
using canjewelry.src;
using canjewelry.src.api;
using canjewelry.src.blocks;
using canjewelry.src.utils;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Encrustable = global::canjewelry.src.CB.EncrustableCB;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios.Infrastructure;

/// <summary>
/// <see cref="JewelryScenarioBase"/> plus cleanups that run after the config is put back:
/// item snapshots, registry entries, cached payloads.
/// </summary>
public abstract class ConfigScenarioBase : JewelryScenarioBase, IDisposable
{
    private readonly List<Action> afterRestore = new();

    protected void AfterRestore(Action action) => afterRestore.Add(action);

    protected string ModVersion => World.Api.ModLoader.GetMod("canjewelry")!.Info.Version;

    protected Item ItemOf(string code)
    {
        Item item = World.Api.World.GetItem(new AssetLocation(code));
        Assert.True(item != null, "no item " + code);
        return item!;
    }

    /// <summary>Puts the item's attributes and behaviours back as they are now.</summary>
    protected void RestoreOnCleanup(Item item)
    {
        JToken? attributes = item.Attributes?.Token?.DeepClone();
        CollectibleBehavior[] behaviors = item.CollectibleBehaviors;
        AfterRestore(() =>
        {
            item.Attributes = attributes == null ? null : new JsonObject(attributes);
            item.CollectibleBehaviors = behaviors;
            Encrustable.ClearVariantTiersCache();
        });
    }

    /// <summary>Drops the prepared config bytes once the scenario's values are gone again.</summary>
    protected void InvalidateConfigOnCleanup() => AfterRestore(ModInternals.OnConfigReplaced);

    protected static int CountInPlayer(ITestPlayer player, string code)
    {
        int count = 0;
        foreach (string className in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            IInventory inventory = player.Player.InventoryManager.GetOwnInventory(className);
            if (inventory == null) continue;
            for (int i = 0; i < inventory.Count; i++)
            {
                ItemStack stack = inventory[i]?.Itemstack;
                if (stack?.Collectible.Code.ToString() == code) count += stack.StackSize;
            }
        }
        return count;
    }

    protected static void EmptyPlayerInventory(ITestPlayer player)
    {
        foreach (string className in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            IInventory inventory = player.Player.InventoryManager.GetOwnInventory(className);
            if (inventory == null) continue;
            for (int i = 0; i < inventory.Count; i++)
            {
                if (inventory[i] != null) inventory[i].Itemstack = null;
            }
        }
    }

    public new void Dispose()
    {
        base.Dispose();
        for (int i = afterRestore.Count - 1; i >= 0; i--) afterRestore[i]();
        afterRestore.Clear();
    }
}

/// <summary>The config file on the test server's disk, and the fixtures next to the test dll.</summary>
public static class ConfigFiles
{
    public const string FileName = "canjewelry.json";

    public static string PathOf(string name = FileName) => Path.Combine(GamePaths.ModConfig, name);

    public static JObject Read(string name = FileName) => JObject.Parse(File.ReadAllText(PathOf(name)));

    public static string[] Backups(string pattern = "canjewelry_*.json.bak") =>
        Directory.GetFiles(GamePaths.ModConfig, pattern);

    // Not AppContext.BaseDirectory: Atlas points it at the game install once the server boots.
    public static string Fixture(params string[] parts)
    {
        string testBin = Path.GetDirectoryName(typeof(ConfigFiles).Assembly.Location)!;
        return File.ReadAllText(Path.Combine(new[] { testBin, "fixtures" }.Concat(parts).ToArray()));
    }

    /// <summary>
    /// buffNameToPossibleItem with "$group" entries replaced by the config's own groups. Done here
    /// rather than by Config.ExpandItemGroups, which repoints the static groups the server folds by.
    /// </summary>
    public static Dictionary<string, HashSet<string>> Expanded(Config config)
    {
        var result = new Dictionary<string, HashSet<string>>();
        foreach (var gem in config.buffNameToPossibleItem)
        {
            var set = new HashSet<string>();
            foreach (string entry in gem.Value)
            {
                if (entry.StartsWith("$")) set.UnionWith(config.item_groups[entry.Substring(1)]);
                else set.Add(entry);
            }
            result[gem.Key] = set;
        }
        return result;
    }

    public static void AssertSet(IEnumerable<string> expected, IEnumerable<string>? actual)
    {
        Assert.NotNull(actual);
        Assert.True(new HashSet<string>(expected).SetEquals(actual!),
            $"expected {{{string.Join(", ", expected)}}}, got {{{string.Join(", ", actual!)}}}");
    }

    public static void AssertSameSets(Dictionary<string, HashSet<string>> expected, Dictionary<string, HashSet<string>> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(k => k), actual.Keys.OrderBy(k => k));
        foreach (var pair in expected)
        {
            Assert.True(pair.Value.SetEquals(actual[pair.Key]), "differs for " + pair.Key);
        }
    }
}

/// <summary>Non public members of the mod, reached by reflection: the mod has no InternalsVisibleTo.</summary>
public static class ModInternals
{
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static void OnConfigReplaced() =>
        typeof(Mod).GetMethod("OnConfigReplaced", AnyStatic)!.Invoke(null, null);

    public static bool AllowConfigRequest(IServerPlayer player) =>
        (bool)typeof(Mod).GetMethod("AllowConfigRequest", AnyStatic)!.Invoke(null, new object[] { player })!;

    public static void SendGemVisualsIfChanged(IServerPlayer player, string? clientHash) =>
        typeof(Mod).GetMethod("sendGemVisualsIfChanged", AnyInstance)!.Invoke(Mod.Instance, new object?[] { player, clientHash });

    public static string? ServerGemVisuals =>
        (string?)typeof(Mod).GetField("serverGemVisuals", AnyStatic)!.GetValue(null);

    public static string ServerGemVisualsHash =>
        (string)typeof(Mod).GetField("serverGemVisualsHash", AnyStatic)!.GetValue(null)!;

    public static Dictionary<string, HashSet<string>> ActiveItemGroups =>
        (Dictionary<string, HashSet<string>>)typeof(Config).GetProperty("ActiveItemGroups", AnyStatic)!.GetValue(null)!;

    public static bool RegistrySealed =>
        (bool)typeof(CANJewelryRegistry).GetProperty("Sealed", AnyStatic)!.GetValue(null)!;

    public static Dictionary<string, int[]> RegisteredSockets => RegistryField<Dictionary<string, int[]>>("extraSocketCounts");

    public static Dictionary<string, Config.CustomVariantSocketsTiers> RegisteredVariantSockets =>
        RegistryField<Dictionary<string, Config.CustomVariantSocketsTiers>>("extraVariantSockets");

    public static Dictionary<string, HashSet<string>> RegisteredGroupMembers =>
        RegistryField<Dictionary<string, HashSet<string>>>("extraItemGroupMembers");

    public static List<string> MaterialKeys => RegistryField<List<string>>("materialAttributeKeys");

    public static void UnregisterGroupMembers(string group, params string[] fragments)
    {
        if (!RegisteredGroupMembers.TryGetValue(group, out var set)) return;
        foreach (string fragment in fragments) set.Remove(fragment);
        if (set.Count == 0) RegisteredGroupMembers.Remove(group);
    }

    public static Dictionary<string, CANPanningDrop[]> PanTable(CANBlockPan pan) =>
        (Dictionary<string, CANPanningDrop[]>)typeof(CANBlockPan).GetField("dropsBySourceMat", AnyInstance)!.GetValue(pan)!;

    public static void PanTakeMaterial(CANBlockPan pan, ItemSlot slot, EntityAgent byEntity) =>
        typeof(CANBlockPan).GetMethod("TryTakeMaterial", AnyInstance)!.Invoke(pan, new object[] { slot, byEntity });

    public static void PanCreateDrop(CANBlockPan pan, EntityAgent byEntity, string material) =>
        typeof(CANBlockPan).GetMethod("CreateDrop", AnyInstance)!.Invoke(pan, new object[] { byEntity, material });

    private static T RegistryField<T>(string name) =>
        (T)typeof(CANJewelryRegistry).GetField(name, AnyStatic)!.GetValue(null)!;
}
