using canjewelry.src.cb;
using Vintagestory.API.Datastructures;
using Xunit;

namespace canjewelry.Tests.CANGemCuttableCB
{
    public class RecipeAttributesMatchTests
    {
        private static ITreeAttribute Tree(params (string Key, string Value)[] pairs)
        {
            ITreeAttribute tree = new TreeAttribute();
            foreach (var (key, value) in pairs) tree.SetString(key, value);
            return tree;
        }

        private static bool Match(ITreeAttribute stack, ITreeAttribute recipe)
            => src.cb.CANGemCuttableCB.RecipeAttributesMatch(stack, recipe);

        [Fact]
        public void NoRecipeAttributes_MatchesAnything()
        {
            Assert.True(Match(Tree(("potential", "low")), null!));
            Assert.True(Match(null!, null!));
        }

        [Fact]
        public void SameAttributes_Match()
        {
            Assert.True(Match(Tree(("potential", "low")), Tree(("potential", "low"))));
        }

        // What the old whole-tree comparison got wrong: anything else on the stack failed it.
        [Fact]
        public void ExtraStackAttributes_StillMatch()
        {
            var stack = Tree(("potential", "low"), ("gemtraits", "cloudy"));
            stack.SetFloat("carats", 2.15f);

            Assert.True(Match(stack, Tree(("potential", "low"))));
        }

        [Fact]
        public void DifferentValue_DoesNotMatch()
        {
            Assert.False(Match(Tree(("potential", "high")), Tree(("potential", "low"))));
        }

        [Fact]
        public void MissingAttribute_DoesNotMatch()
        {
            Assert.False(Match(Tree(("size", "flawed")), Tree(("potential", "low"))));
            Assert.False(Match(null!, Tree(("potential", "low"))));
        }

        [Fact]
        public void EveryRecipeAttribute_IsRequired()
        {
            var recipe = Tree(("potential", "low"), ("gemtype", "ruby"));

            Assert.True(Match(Tree(("potential", "low"), ("gemtype", "ruby")), recipe));
            Assert.False(Match(Tree(("potential", "low"), ("gemtype", "emerald")), recipe));
        }

        [Fact]
        public void EmptyRecipeTree_MatchesAnything()
        {
            Assert.True(Match(Tree(("potential", "low")), new TreeAttribute()));
            Assert.True(Match(null!, new TreeAttribute()));
        }
    }
}
