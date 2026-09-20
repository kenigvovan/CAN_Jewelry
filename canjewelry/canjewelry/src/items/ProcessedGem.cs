using System.Collections.Generic;
using canjewelry.src;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.jewelry
{
    public class ProcessedGem : Item, IContainedMeshSource
    {
        private float offY;
        private float curOffY;
        private ICoreClientAPI capi;

        /// <summary>
        /// The uploaded meshes of ground gems, one per look: the gem, how far it has been ground and
        /// its size. Under our own key and keyed by that look rather than by a hash of it.
        /// </summary>
        private Dictionary<string, MultiTextureMeshRef> meshrefs
        {
            get
            {
                return ObjectCacheUtil.GetOrCreate(this.api, "canjewelry:processedGemMeshRefs",
                    () => new Dictionary<string, MultiTextureMeshRef>());
            }
        }

        public override void OnUnloaded(ICoreAPI api)
        {
            if (api is ICoreClientAPI)
            {
                var refs = ObjectCacheUtil.TryGet<Dictionary<string, MultiTextureMeshRef>>(api, "canjewelry:processedGemMeshRefs");
                if (refs != null)
                {
                    foreach (MultiTextureMeshRef meshRef in refs.Values) meshRef?.Dispose();
                    refs.Clear();
                    ObjectCacheUtil.Delete(api, "canjewelry:processedGemMeshRefs");
                }
            }

            base.OnUnloaded(api);
        }

        /// <summary>What this stack looks like, and so what its mesh can be cached under.</summary>
        private static string LookKey(ItemStack stack)
        {
            ITreeAttribute tree = stack?.Attributes?.GetTreeAttribute("cangrindlayerinfo");
            if (tree == null) return stack?.Attributes?.GetString("gembase", "") ?? "";

            return tree.GetString("gembase") + "-" + tree.GetInt("grindtype") + "-" + tree.GetString("gemsize");
        }

        /// <summary>
        /// The texture source for one mesh build: the shape's own textures, with the gem colour
        /// written over them and the polishing defects shown or hidden by how far it was ground.
        /// </summary>
        private render.CANTexSource TexSource(ItemStack itemstack, ITextureAtlasAPI atlas)
        {
            var shapeTextures = new Dictionary<string, CompositeTexture>();
            Shape shape = this.capi.TesselatorManager.GetCachedShape(this.Shape.Base);
            if (shape?.Textures != null)
            {
                foreach (KeyValuePair<string, AssetLocation> ctex in shape.Textures)
                {
                    shapeTextures[ctex.Key] = new CompositeTexture(ctex.Value);
                }
            }

            var source = new render.CANTexSource(this.capi, atlas, shapeTextures, "processed gem " + this.Code);

            ITreeAttribute itree = itemstack?.Attributes?.GetTreeAttribute("cangrindlayerinfo");
            string gemBase = itree != null
                ? itree.GetString("gembase")
                : itemstack?.Attributes?.GetString("gembase", null);

            // One gem is listed under two names; the textures only know the first.
            if ("olivine_peridot".Equals(gemBase)) gemBase = "olivine";

            if (string.IsNullOrEmpty(gemBase) || !canjewelry.gems_textures.TryGetValue(gemBase, out string assetPath))
            {
                canjewelry.gems_textures.TryGetValue(CANJWConstants.FALLBACK_GEM_TYPE, out assetPath);
            }
            AssetLocation asset = canjewelry.capi.Assets.TryGet(assetPath + ".png")?.Location;
            if (asset == null) return source;

            source.Overrides["gembase"] = asset;
            if (itree == null) return source;

            // A defect layer is gone once grinding has passed it: those still there wear the gem's
            // own texture, the ones ground away wear the invisible one.
            AssetLocation invisible = new AssetLocation("canjewelry:item/gem/notvis.png");
            for (int i = 0; i < 2; i++)
            {
                source.Overrides["emeralddefect" + i] = itree.GetInt("grindtype") <= i ? asset : invisible;
            }
            source.Overrides["emeralddefect2"] = asset;

            return source;
        }
        public string Construction
        {
            get
            {
                return this.Variant["construction"];
            }
        }
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            this.curOffY = (this.offY = this.FpHandTransform.Translation.Y);
            this.capi = (api as ICoreClientAPI);
        }
        public void AddAllTypesToCreativeInventory()
        {

            List<JsonItemStack> stacks = new List<JsonItemStack>();
            Dictionary<string, string[]> vg = this.Attributes["variantGroups"].AsObject<Dictionary<string, string[]>>(null);
            foreach (string metal in vg["gembase"])
            {
                string construction = this.Construction;
                if ((construction == "flawedvariant"))
                {
                    stacks.Add(this.genJstack(string.Format("{{ gembase: \"{0}\", gemsize: \"{1}\" }}", metal, "flawed")));
                }
                if ((construction == "chippedvariant"))
                {
                    stacks.Add(this.genJstack(string.Format("{{ gembase: \"{0}\", gemsize: \"{1}\" }}", metal, "chipped")));
                }
                if ((construction == "normalvariant"))
                {
                    stacks.Add(this.genJstack(string.Format("{{ gembase: \"{0}\", gemsize: \"{1}\" }}", metal, "normal")));
                }
            }
            this.CreativeInventoryStacks = new CreativeTabAndStackList[]
            {
                new CreativeTabAndStackList
                {
                    Stacks = stacks.ToArray(),
                    Tabs = new string[]
                    {
                        "general",
                        "decorative"
                    }
                }
            };
        }
        private JsonItemStack genJstack(string json)
        {
            JsonItemStack jsonItemStack = new JsonItemStack();
            jsonItemStack.Code = this.Code;
            jsonItemStack.Type = EnumItemClass.Item;
            jsonItemStack.Attributes = new JsonObject(JToken.Parse(json));
            jsonItemStack.Resolve(this.api.World, "shield type", true);
            return jsonItemStack;
        }
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            if (target == EnumItemRenderTarget.HandTp)
            {
                bool sneak = capi.World.Player.Entity.Controls.Sneak;
                this.curOffY += ((sneak ? 0.4f : this.offY) - this.curOffY) * renderinfo.dt * 8f;
                renderinfo.Transform.Translation.X = this.curOffY;
                renderinfo.Transform.Translation.Y = this.curOffY * 1.2f;
                renderinfo.Transform.Translation.Z = this.curOffY * 1.2f;
            }
            // One uploaded mesh per look of the gem. A stack with no grinding info used to leave the
            // id at zero, which made the condition below always true: every frame uploaded a fresh
            // mesh and dropped the previous one on the floor, and the dictionary was never emptied.
            string key = LookKey(itemstack);
            if (!this.meshrefs.TryGetValue(key, out MultiTextureMeshRef modelref) || modelref.Disposed)
            {
                MeshData mesh = this.GenMesh(itemstack, capi.ItemTextureAtlas, null);
                if (mesh == null)
                {
                    base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
                    return;
                }

                modelref = this.meshrefs[key] = capi.Render.UploadMultiTextureMesh(mesh);
            }

            renderinfo.ModelRef = modelref;
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }
        public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
            => GenMesh(slot?.Itemstack, targetAtlas, atBlockPos);
        /// <summary>
        /// The gem as grinding has left it. This and the slot overload above were two copies of the
        /// same seventy lines, differing only in how they reached the stack.
        /// </summary>
        public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            if (itemstack == null) return null;

            this.capi.Tesselator.TesselateItem(this, out MeshData mesh, TexSource(itemstack, targetAtlas));
            return mesh;
        }
        public override string GetHeldItemName(ItemStack itemStack)  
        {
            if(itemStack.Attributes.HasAttribute("cangrindlayerinfo"))
            {
                var tree = itemStack.Attributes.GetTreeAttribute("cangrindlayerinfo");
                return Lang.Get("canjewelry:processedgem-" + tree.GetString("gemsize") + "-" + tree.GetString("gembase")) +
                       Lang.Get("canjewelry:processedgem-stage", tree.GetInt("grindtype") + 1); ;
            }
            return "";          
        }
        /// <summary>
        /// Names the mesh a holder caches for this gem. Goes through the same look as the render
        /// path, so a gem ground one step further is a different mesh in a display case too — the
        /// key used to read only the root attributes, which a ground gem does not carry.
        /// </summary>
        public string GetMeshCacheKey(ItemSlot slot)
        {
            return this.Code.ToShortString() + "-" + LookKey(slot?.Itemstack);
        }
    }
}
