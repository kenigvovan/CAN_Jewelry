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

        private Dictionary<string, Dictionary<string, int>> durabilityGains;
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
        private Dictionary<int, MeshRef> meshrefs
        {
            get
            {
                return ObjectCacheUtil.GetOrCreate<Dictionary<int, MeshRef>>(this.api, "processedmeshrefs", () => new Dictionary<int, MeshRef>());
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
            // this.durabilityGains = this.Attributes["durabilityGains"].AsObject<Dictionary<string, Dictionary<string, int>>>(null);
            //this.AddAllTypesToCreativeInventory();
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
        public static Dictionary<string, int> gemSizeToInt = new Dictionary<string, int>{ {"normal", 100 }, { "flawed", 200 }, { "chipped", 300 } };
        public static Dictionary<string, int> gemBaseToInt = new Dictionary<string, int> { { "olivine_peridot", 10 }, { "corundum", 20 }, { "diamond", 30 }, { "emerald", 40 }, { "fluorite", 50 }, { "lapislazuli", 60 }, { "malachite", 70 }, { "quartz", 80 }, { "uranium", 90 } };
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
                //var tmp = (int)tree.GetString("gembase")[0];
                meshrefid = gemBaseToInt[tree.GetString("gembase")] + tree.GetInt("grindtype") + gemSizeToInt[tree.GetString("gemsize")];
                // gembase gemsize grindtype
               // meshrefid = (int)
            }
            
            if (meshrefid == 0 || !this.meshrefs.TryGetValue(meshrefid, out renderinfo.ModelRef))
            {
                int id = meshrefid;
                MeshRef modelref = capi.Render.UploadMesh(this.GenMesh(itemstack, capi.ItemTextureAtlas));
               
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
           /* string wood = itemstack.Attributes.GetString("wood", null);
            string metal = itemstack.Attributes.GetString("metal", null);
            string color = itemstack.Attributes.GetString("color", null);
            string deco = itemstack.Attributes.GetString("deco", null);*/

            string gemBase = itemstack.Attributes.GetString("gembase", null);
            string gemSize = itemstack.Attributes.GetString("gemsize", null);

            /* if (wood == null && metal == null && this.Construction != "crude" && this.Construction != "blackguard")
             {
                 return new MeshData(true);
             }
             if (wood == null || wood == "")
             {
                 wood = "generic";
             }
             this.tmpTextures["front"] = (this.tmpTextures["back"] = (this.tmpTextures["handle"] = new AssetLocation("block/wood/planks/generic.png")));*/
            foreach (KeyValuePair<string, AssetLocation> ctex in this.capi.TesselatorManager.GetCachedShape(this.Shape.Base).Textures)
            {
                this.tmpTextures[ctex.Key] = ctex.Value;
            }
            var f = this.capi.TesselatorManager.GetCachedShape(this.Shape.Base);
            string construction = this.Construction;
            ITreeAttribute itree;
            if (itemstack.Attributes.HasAttribute("cangrindlayerinfo"))
            {
                itree = itemstack.Attributes.GetTreeAttribute("cangrindlayerinfo");
                
                gemBase = itree.GetString("gembase");
                gemSize = itree.GetString("gemsize");
                if(gemBase.Equals("olivine_peridot"))
                {
                    gemBase = "olivine";
                }
                this.tmpTextures["gembase"] = new AssetLocation("game:block/stone/gem/" + gemBase + ".png");
                if (itree.GetInt("grindtype") <= 0)
                {
                    this.tmpTextures["emeralddefect0"] = new AssetLocation("canjewelry:item/gem/" + gemBase + "-defect.png");
                }
               else
                {
                    this.tmpTextures["emeralddefect0"] = new AssetLocation("canjewelry:item/gem/notvis.png");
                }

                 if (itree.GetInt("grindtype") <= 1)
                {
                    this.tmpTextures["emeralddefect1"] = new AssetLocation("canjewelry:item/gem/" + gemBase + "-defect.png");
                }
                else
                {
                    this.tmpTextures["emeralddefect1"] = new AssetLocation("canjewelry:item/gem/notvis.png");
                }
                this.tmpTextures["emeralddefect2"] = new AssetLocation("canjewelry:item/gem/" + gemBase + "-defect.png");
            }
            else
            {
                if (gemBase.Equals("olivine_peridot"))
                {
                    gemBase = "olivine";
                }
                this.tmpTextures["gembase"] = new AssetLocation("game:block/stone/gem/" + gemBase + ".png");
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
                return Lang.Get("canjewelry:processedgem-" + tree.GetString("gemsize") + "-" + tree.GetString("gembase"));
            }
            return "";          
        }
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
           /* ItemStack itemstack = inSlot.Itemstack;
            JsonObject jsonObject;
            if (itemstack == null)
            {
                jsonObject = null;
            }
            else
            {
                JsonObject itemAttributes = itemstack.ItemAttributes;
                jsonObject = ((itemAttributes != null) ? itemAttributes["shield"] : null);
            }
            JsonObject attr = jsonObject;
            if (attr == null || !attr.Exists)
            {
                return;
            }
            float acdmgabsorb = attr["damageAbsorption"]["active"].AsFloat(0f);
            float acchance = attr["protectionChance"]["active"].AsFloat(0f);
            float padmgabsorb = attr["damageAbsorption"]["passive"].AsFloat(0f);
            float pachance = attr["protectionChance"]["passive"].AsFloat(0f);
            dsc.AppendLine(Lang.Get("shield-stats", new object[]
            {
                (int)(100f * acchance),
                (int)(100f * pachance),
                acdmgabsorb,
                padmgabsorb
            }));
            string construction = this.Construction;
            if (construction == "woodmetal")
            {
                dsc.AppendLine("Wood: " + inSlot.Itemstack.Attributes.GetString("wood", null));
                dsc.AppendLine("Metal: " + inSlot.Itemstack.Attributes.GetString("metal", null));
                return;
            }
            if (!(construction == "woodmetalleather"))
            {
                return;
            }
            dsc.AppendLine("Metal: " + inSlot.Itemstack.Attributes.GetString("metal", null));*/
        }
        public MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            return this.GenMesh(itemstack, targetAtlas);
        }
        public string GetMeshCacheKey(ItemStack itemstack)
        {
            string gemBase = itemstack.Attributes.GetString("gembase", null);
            string gemSize = itemstack.Attributes.GetString("gemsize", null);
            /*string wood = itemstack.Attributes.GetString("wood", null);
            string metal = itemstack.Attributes.GetString("metal", null);
            string color = itemstack.Attributes.GetString("color", null);
            string deco = itemstack.Attributes.GetString("deco", null);*/
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
