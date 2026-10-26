using Atlas.XUnit;
using canjewelry.scenarios.Infrastructure;
using canjewelry.src;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using CuttableCB = global::canjewelry.src.cb.CANGemCuttableCB;

namespace canjewelry.scenarios;

/// <summary>What a cancelled cut hands back, for work items started from our own rough gems and
/// from vanilla ones.</summary>
public class GemCuttingScenarios : JewelryScenarioBase
{
    private ItemStack WorkItem(string quality, string gemType)
    {
        ItemStack workItem = Stack("canjewelry:gemcuttingworkitem");
        ITreeAttribute attributes = new TreeAttribute();
        attributes.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, gemType);
        attributes.SetString(CANJWConstants.ENCRUSTED_GEM_SIZE, quality);
        workItem.Attributes = attributes;
        return workItem;
    }

    [AtlasScenario]
    public async Task WorkItem_Should_GiveBackOwnRoughGem()
    {
        ItemStack rough = CuttableCB.RoughGemOfWorkItem(World.Api.World, WorkItem("flawed", GemType));

        Assert.Equal($"canjewelry:gem-rough-flawed-{GemType}", rough.Collectible.Code.ToString());
        await World.Ticks(1);
    }

    // A gem type with no rough gem of ours falls back to the vanilla one, which carries its
    // quality as "potential".
    [AtlasScenario]
    public async Task WorkItem_Should_GiveBackVanillaRoughGem_WithPotential()
    {
        string vanillaOnly = World.Api.World.SearchItems(new AssetLocation("game:gem-*-rough"))
            .Select(i => i.Code.Path.Split('-')[1])
            .FirstOrDefault(type => World.Api.World.GetItem(new AssetLocation($"canjewelry:gem-rough-chipped-{type}")) == null)
            ?? "";
        if (vanillaOnly == "")
        {
            // Every vanilla gem has a rough gem of ours in this game version: nothing to fall back from.
            await World.Ticks(1);
            return;
        }

        ItemStack rough = CuttableCB.RoughGemOfWorkItem(World.Api.World, WorkItem("chipped", vanillaOnly));

        Assert.Equal($"game:gem-{vanillaOnly}-rough", rough.Collectible.Code.ToString());
        Assert.Equal("low", rough.Attributes.GetString("potential"));
    }

    // Recipes that name attributes still match a stack carrying more of them.
    [AtlasScenario]
    public async Task RecipeMatch_Should_IgnoreExtraStackAttributes()
    {
        var matcher = new CuttableCB(World.Api.World.GetItem(new AssetLocation("canjewelry:gemcuttingworkitem")));
        ItemStack rough = Stack($"canjewelry:gem-rough-normal-{GemType}");
        int before = matcher.GetMatchingRecipes(rough).Count;
        rough.Attributes.SetFloat("carats", 2.15f);

        Assert.Equal(before, matcher.GetMatchingRecipes(rough).Count);
        await World.Ticks(1);
    }
}
