using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.jewelry
{
    public class ProcessedGem : Item, ITexPositionSource, IContainedMeshSource
    {
        private float offY;

        private float curOffY;

        private ICoreClientAPI capi;

        private ITextureAtlasAPI targetAtlas;

        private Dictionary<string, AssetLocation> tmpTextures = new Dictionary<string, AssetLocation>();

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                return this.getOrCreateTexPos(this.tmpTextures[textureCode]);
            }
        }
        protected TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
        {
            TextureAtlasPosition texpos = this.targetAtlas[texturePath];
            if (texpos == null)
            {
                IAsset texAsset = this.capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"), true);
                if (texAsset != null)
                {
                    int num;
                    this.targetAtlas.GetOrInsertTexture(texturePath, out num, out texpos, () => texAsset.ToBitmap(this.capi), 0.005f);
                }
                else
                {
                    this.capi.World.Logger.Warning("For render in shield {0}, require texture {1}, but no such texture found.", new object[]
                    {
                        this.Code,
                        texturePath
                    });
                }
            }
            return texpos;
        }

        public Size2i AtlasSize
        {
            get
            {
                return this.targetAtlas.Size;
            }
        }
        private Dictionary<int, MultiTextureMeshRef> meshrefs
        {
            get
            {
                return ObjectCacheUtil.GetOrCreate<Dictionary<int, MultiTextureMeshRef>>(this.api, "processedmeshrefs", () => new Dictionary<int, MultiTextureMeshRef>());
            }
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
            if (target == EnumItemRenderTarget.HandFp)
            {
                bool sneak = capi.World.Player.Entity.Controls.Sneak;
                this.curOffY += ((sneak ? 0.4f : this.offY) - this.curOffY) * renderinfo.dt * 8f;
                renderinfo.Transform.Translation.X = this.curOffY;
                renderinfo.Transform.Translation.Y = this.curOffY * 1.2f;
                renderinfo.Transform.Translation.Z = this.curOffY * 1.2f;
            }
            int meshrefid = itemstack.TempAttributes.GetInt("meshRefId", 0);
            ITreeAttribute tree;
            if (itemstack.Attributes.HasAttribute("cangrindlayerinfo"))
            {
                tree = itemstack.Attributes.GetTreeAttribute("cangrindlayerinfo");
                meshrefid = (tree.GetString("gembase") + tree.GetInt("grindtype").ToString() + tree.GetString("gemsize").ToString()).GetHashCode();
            }
            
            if (meshrefid == 0 || !this.meshrefs.TryGetValue(meshrefid, out renderinfo.ModelRef))
            {
                int id = meshrefid;
                MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(this.GenMesh(itemstack, capi.ItemTextureAtlas));
               
                renderinfo.ModelRef = (this.meshrefs[id] = modelref);
                itemstack.TempAttributes.SetInt("meshRefId", id);
            }
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }
        public MeshData outGenMesh(ItemStack itemstack)
        {
            return GenMesh(itemstack, targetAtlas);
        }
        public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas)
        {
            this.targetAtlas = targetAtlas;
            this.tmpTextures.Clear();

            string gemBase = itemstack.Attributes.GetString("gembase", null);
            string gemSize = itemstack.Attributes.GetString("gemsize", null);

            foreach (KeyValuePair<string, AssetLocation> ctex in this.capi.TesselatorManager.GetCachedShape(this.Shape.Base).Textures)
            {
                this.tmpTextures[ctex.Key] = ctex.Value;
            }
            string construction = this.Construction;
            ITreeAttribute itree;
            if (itemstack.Attributes.HasAttribute("cangrindlayerinfo"))
            {
                itree = itemstack.Attributes.GetTreeAttribute("cangrindlayerinfo");
                
                gemBase = itree.GetString("gembase");

                if (gemBase.Equals("olivine_peridot"))
                {
                    gemBase = "olivine";
                }

                if (!canjewelry.gems_textures.TryGetValue(gemBase, out string assetPath))
                {
                    canjewelry.gems_textures.TryGetValue("diamond", out assetPath);
                }
                AssetLocation asset = canjewelry.capi.Assets.TryGet(assetPath + ".png")?.Location;

                this.tmpTextures["gembase"] = asset;

                for(int i = 0; i < 2; i++)
                {
                    if (itree.GetInt("grindtype") <= i)
                    {
                        this.tmpTextures["emeralddefect" + i] = asset;
                    }
                    else
                    {
                        this.tmpTextures["emeralddefect" + i] = new AssetLocation("canjewelry:item/gem/notvis.png");
                    }
                }
                this.tmpTextures["emeralddefect2"] = asset;
            }
            else
            {
                if (gemBase.Equals("olivine_peridot"))
                {
                    gemBase = "olivine";
                }

                if (!canjewelry.gems_textures.TryGetValue(gemBase, out string assetPath))
                {
                    canjewelry.gems_textures.TryGetValue("diamond", out assetPath);
                }
                AssetLocation asset = canjewelry.capi.Assets.TryGet(assetPath + ".png")?.Location;

                this.tmpTextures["gembase"] = asset;
            }        
            MeshData mesh;
            this.capi.Tesselator.TesselateItem(this, out mesh, this);
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

        public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            return this.GenMesh(itemstack, targetAtlas);
        }
        public string GetMeshCacheKey(ItemStack itemstack)
        {
            string gemBase = itemstack.Attributes.GetString("gembase", null);
            string gemSize = itemstack.Attributes.GetString("gemsize", null);
            return string.Concat(new string[]
            {
                this.Code.ToShortString(),
                "-",
                gemBase,
                "-",
                gemSize
            }) ;
        }
    }
}
