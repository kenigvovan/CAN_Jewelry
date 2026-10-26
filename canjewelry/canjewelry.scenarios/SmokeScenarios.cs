using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using HarmonyLib;
using Vintagestory.API.Common;
using Mod = global::canjewelry.src.canjewelry;

namespace canjewelry.scenarios;

[Trait("Category", "Smoke")]
public class SmokeScenarios : JewelryScenarioBase
{
    // The server boots with the mod; errors logged while booting are listed.
    [AtlasScenario]
    public async Task Server_Should_BootWithoutErrors()
    {
        var errors = World.BootDiagnostics
            .Where(e => e.Tick is null && e.Level >= EnumLogType.Error)
            .Select(e => $"[{e.DescribeSource()}] {e.Message}")
            .ToList();

        Assert.True(errors.Count == 0, "errors while booting:\n" + string.Join("\n", errors));
        Assert.NotNull(Mod.config);
        await World.Ticks(1);
    }

    // The gem drop on breaking hangs off DestroyItem. Its earlier transpiler on DamageItem
    // stopped matching on a game update without a word; this fails instead.
    [AtlasScenario]
    public async Task GemDropPatch_Should_BeApplied()
    {
        var destroyItem = typeof(CollectibleObject).GetMethod("DestroyItem",
            new[] { typeof(IWorldAccessor), typeof(Vintagestory.API.Common.Entities.Entity), typeof(ItemSlot) });
        Patches? patches = Harmony.GetPatchInfo(destroyItem);

        Assert.True(patches != null && patches.Prefixes.Any(p => p.PatchMethod.Name == "Prefix_CollectibleObject_DestroyItem"),
            "no gem drop prefix on CollectibleObject.DestroyItem");
        await World.Ticks(1);
    }

    // Every item code the scenarios build stacks of exists in this game version.
    [AtlasScenario]
    public async Task ScenarioItems_Should_Exist()
    {
        Stack(JewelryCode);
        foreach (string quality in new[] { "normal", "flawless", "exquisite" })
        {
            Stack($"canjewelry:gem-cut-{quality}-{GemType}");
        }
        await World.Ticks(1);
    }
}
