using System.Runtime.CompilerServices;
using Atlas.Api;
using Atlas.XUnit;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios.Infrastructure;

/// <summary>
/// Shared ground for scenarios of the mod. One server per class and the mod's statics are not
/// rolled back between scenarios, so config changes go through <see cref="SetConfig"/>, which
/// puts the old value back on Dispose.
/// </summary>
public abstract class JewelryScenarioBase : AtlasScenarioBase, IDisposable
{
    // A vanilla knife that takes three tier 3 sockets in the default config, and a gem the
    // default config allows on knives.
    protected const string JewelryCode = "game:knife-generic-steel";
    protected const string GemType = "malachite";

    private static int lastPlayer;
    // Players of earlier scenarios, per class server: it takes 16 clients at most and keeps every
    // joined player until the class ends. Dispose runs off the game thread, where the engine's
    // disconnect throws, so they are kicked by the next Join, which runs on it. Keyed by the
    // server API: every scenario gets a new IWorldSession over the same server.
    private static readonly ConditionalWeakTable<object, List<ITestPlayer>> leftOver = new();
    private readonly List<Action> cleanups = new();
    private readonly List<ITestPlayer> players = new();

    protected async Task<ITestPlayer> Join()
    {
        List<ITestPlayer> stale;
        lock (leftOver)
        {
            List<ITestPlayer> left = leftOver.GetOrCreateValue(World.Api);
            stale = new List<ITestPlayer>(left);
            left.Clear();
        }
        if (stale.Count > 0)
        {
            foreach (ITestPlayer player in stale)
            {
                if (player.IsConnected) player.Player.Disconnect();
            }
            await World.Until(() => stale.TrueForAll(p => !p.IsConnected), "earlier scenarios' players disconnected", 100);
        }

        // A random spawn spot in an unloaded chunk lets the test client's join request arrive
        // before the player is placed; the engine's HandleRequestJoin then throws and the join
        // never completes. Same fix as in claims.scenarios.
        World.Api.World.Config.SetString("spawnRadius", "0");
        return Track(await World.JoinPlayer("jeweler" + Interlocked.Increment(ref lastPlayer)));
    }

    /// <summary>Has the player kicked before the next scenario's join.</summary>
    protected ITestPlayer Track(ITestPlayer player)
    {
        players.Add(player);
        return player;
    }

    /// <summary>Changes a config value for this scenario only.</summary>
    protected void SetConfig<T>(System.Func<Config, T> get, Action<Config, T> set, T value)
    {
        T previous = get(Mod.config);
        set(Mod.config, value);
        cleanups.Add(() => set(Mod.config, previous));
    }

    /// <summary>Runs on Dispose, after the scenario: undoes test state the base does not know about.</summary>
    protected void AddCleanup(Action cleanup) => cleanups.Add(cleanup);

    protected ItemStack Stack(string code)
    {
        Item item = World.Api.World.GetItem(new AssetLocation(code));
        Assert.True(item != null, "no item " + code);
        return new ItemStack(item);
    }

    /// <summary>
    /// A cut gem as versions before 0.7.13 wrote it: only the cut and the buffs in the cut gem
    /// tree, nothing else on the stack.
    /// </summary>
    protected ItemStack LegacyCutGem(string quality = "normal", string buff = "walkspeed", float value = 0.03f)
    {
        ItemStack gem = Stack($"canjewelry:gem-cut-{quality}-{GemType}");
        ITreeAttribute tree = new TreeAttribute();
        tree.SetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND);
        tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { buff });
        tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(new[] { value });
        gem.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
        return gem;
    }

    /// <summary>
    /// The knife with empty sockets as older versions wrote them: no socket item code, size 0
    /// and an empty gem type.
    /// </summary>
    protected ItemStack LegacySocketedJewelry(int sockets = 1, int tier = 3)
    {
        ItemStack jewelry = Stack(JewelryCode);
        ITreeAttribute encrusted = new TreeAttribute();
        encrusted.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, sockets);
        for (int i = 0; i < sockets; i++)
        {
            ITreeAttribute socket = new TreeAttribute();
            socket.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, tier);
            socket.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, 0);
            socket.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");
            encrusted["slot" + i] = socket;
        }
        jewelry.Attributes[CANJWConstants.ITEM_ENCRUSTED_STRING] = encrusted;
        return jewelry;
    }

    protected static ITreeAttribute SocketOf(ItemStack jewelry, int socket) =>
        jewelry.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING).GetTreeAttribute("slot" + socket);

    protected InventoryBase ScratchInventory() => new DummyInventory(World.Api);

    public void Dispose()
    {
        for (int i = cleanups.Count - 1; i >= 0; i--) cleanups[i]();
        cleanups.Clear();
        if (players.Count > 0 && World != null)
        {
            lock (leftOver) leftOver.GetOrCreateValue(World.Api).AddRange(players);
            players.Clear();
        }
        GC.SuppressFinalize(this);
    }
}
