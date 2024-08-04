using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.items
{
    public class CANItemRottenKingMask : CANItemWearable, IWearableShapeSupplier
    {
        private Shape nowTesselatingShape;
        private ITextureAtlasAPI curAtlas;
        private ICoreClientAPI capi;
        private float offY;
        private float curOffY;
        public StatModifiers StatModifers;
        public override Size2i AtlasSize => curAtlas.Size;
        private Dictionary<int, MultiTextureMeshRef> meshrefs
        {
            
            get
            {
                return ObjectCacheUtil.GetOrCreate<Dictionary<int, MultiTextureMeshRef>>(this.api, "canrottenkingmaskmeshrefs", () => new Dictionary<int, MultiTextureMeshRef>());
            }
        }
        public EnumCharacterDressType DressType { get; private set; }
        private Dictionary<string, AssetLocation> tmpTextures = new Dictionary<string, AssetLocation>();
        protected TextureAtlasPosition getOrCreateTexPos(AssetLocation texturePath)
        {
            ICoreClientAPI capi = api as ICoreClientAPI;
            curAtlas.GetOrInsertTexture(texturePath, out var _, out var texPos, delegate
            {
                IAsset asset = capi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                if (asset != null)
                {
                    return asset.ToBitmap(capi);
                }

                capi.World.Logger.Warning("Item {0} defined texture {1}, not no such texture found.", Code, texturePath);
                return null;
            }, 0.1f);
            return texPos;
        }
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            this.curOffY = (this.offY = this.FpHandTransform.Translation.Y);
            this.capi = (api as ICoreClientAPI);

            this.AddAllTypesToCreativeInventory();

            string value = Attributes["clothescategory"].AsString();
            EnumCharacterDressType result = EnumCharacterDressType.Unknown;
            Enum.TryParse<EnumCharacterDressType>(value, ignoreCase: true, out result);
            //DressType = result;

            JsonObject jsonObject = Attributes?["statModifiers"];
            if (jsonObject != null && jsonObject.Exists)
            {
                try
                {
                    StatModifers = jsonObject.AsObject<StatModifiers>();
                }
                catch (Exception ex)
                {
                    api.World.Logger.Error("Failed loading statModifiers for item/block {0}. Will ignore. Exception: {1}", Code, ex);
                    StatModifers = null;
                }
            }

            ProtectionModifiers protectionModifiers = null;
            jsonObject = Attributes?["defaultProtLoss"];
            if (jsonObject != null && jsonObject.Exists)
            {
                try
                {
                    protectionModifiers = jsonObject.AsObject<ProtectionModifiers>();
                }
                catch (Exception ex2)
                {
                    api.World.Logger.Error("Failed loading defaultProtLoss for item/block {0}. Will ignore. Exception: {1}", Code, ex2);
                }
            }
        }
        public void AddAllTypesToCreativeInventory()
        {
            List<JsonItemStack> list = new List<JsonItemStack>();
            foreach (string arg in this.Attributes["variantGroups"].AsObject<Dictionary<string, string[]>>(null)["metal"])
            {
                list.Add(this.genJstack(string.Format("{{ metal: \"{0}\"}}", arg)));
            }
            this.CreativeInventoryStacks = new CreativeTabAndStackList[]
            {
                new CreativeTabAndStackList
                {
                    Stacks = list.ToArray(),
                    Tabs = new string[]
                    {
                        "general",
                        "items",
                        "canjewelry"
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
            jsonItemStack.Resolve(this.api.World, "canrottenkingmask type", true);
            return jsonItemStack;
        }
        public Shape GetShape(ItemStack stack, EntityAgent forEntity, string texturePrefixCode)
        {           
            Shape gearShape = null;
            CompositeShape compGearShape = null;
            JsonObject attrObj = stack.Collectible.Attributes;
            float damageEffect = 0f;
            compGearShape = ((!attrObj["attachShape"].Exists) ? ((stack.Class == EnumItemClass.Item) ? stack.Item.Shape : stack.Block.Shape) : attrObj["attachShape"].AsObject<CompositeShape>(null, stack.Collectible.Code.Domain));
            AssetLocation shapePath = compGearShape.Base.CopyWithPath("shapes/" + compGearShape.Base.Path + ".json");
            gearShape = Vintagestory.API.Common.Shape.TryGet(api, shapePath);
            if (gearShape == null)
            {
                api.World.Logger.Warning("Entity armor shape {0} defined in {1} {2} not found or errored, was supposed to be at {3}. Armor piece will be invisible.", new object[]
                {
                        compGearShape.Base,
                        stack.Class,
                        stack.Collectible.Code,
                        shapePath
                });
                return null;
            }
            
            //gearShape.SubclassForStepParenting(texturePrefixCode, damageEffect);
            HashSet<string> textureCodes = new HashSet<string>();
            ShapeElement[] elements = gearShape.Elements;
            for (int i = 0; i < elements.Length; i++)
            {
                elements[i].WalkRecursive(delegate (ShapeElement el)
                {
                    el.DamageEffect = damageEffect;
                    ShapeElementFace[] facesResolved = el.FacesResolved;
                    foreach (ShapeElementFace shapeElementFace in facesResolved)
                    {
                        if (shapeElementFace != null && shapeElementFace.Enabled && !shapeElementFace.Texture.Contains("silver"))
                        {
                            textureCodes.Add(shapeElementFace.Texture);
                            shapeElementFace.Texture = texturePrefixCode + "-" + shapeElementFace.Texture;
                        }
                    }
                });
            }

            if (gearShape.Textures != null)
            {
                KeyValuePair<string, int[]>[] array = gearShape.TextureSizes.ToArray();
                gearShape.TextureSizes.Clear();
                KeyValuePair<string, int[]>[] array2 = array;
                for (int i = 0; i < array2.Length; i++)
                {
                    KeyValuePair<string, int[]> keyValuePair = array2[i];
                    gearShape.TextureSizes[texturePrefixCode + "-" + keyValuePair.Key] = keyValuePair.Value;
                    textureCodes.Remove(keyValuePair.Key);
                }

                foreach (string item in textureCodes)
                {
                    gearShape.TextureSizes[texturePrefixCode + "-" + item] = new int[2] { gearShape.TextureWidth, gearShape.TextureHeight };
                }
            }


            string maskMetal = stack.Attributes.GetString("metal", null);
            gearShape.Textures["canjewelry:canrottenkingmask-normal-silver1"] = new AssetLocation("block/metal/sheet/" + maskMetal + "1.png");
            gearShape.Textures["rotten-king-mask"] = new AssetLocation("canjewelry:item/rottenking.png");
            gearShape.Textures["rotten-king-cloth"] = new AssetLocation("canjewelry:item/rottenkingcloth.png");
            gearShape.Textures["silver1"] = new AssetLocation("block/metal/sheet/" + maskMetal + "1.png");

           Dictionary<string, AssetLocation> newdict = new Dictionary<string, AssetLocation>();
            newdict["canjewelry:canrottenkingmask-normal-silver1"] = new AssetLocation("block/metal/sheet/" + maskMetal + "1.png");
            newdict["silver1"] = new AssetLocation("block/metal/sheet/" + maskMetal + "1.png");
            //"canjewelry:canrottenkingmask-normal-silver1"
            newdict["rotten-king-mask"] = new AssetLocation("canjewelry:item/rottenking.png");
            newdict["rotten-king-cloth"] = new AssetLocation("canjewelry:item/rottenkingcloth.png");
            foreach (var val in newdict)
            {
                CompositeTexture ctex = new CompositeTexture() { Base = val.Value };

                ICoreClientAPI capi = this.capi as ICoreClientAPI;

                AssetLocation armorTexLoc = val.Value;

                int textureSubId = 0;
                TextureAtlasPosition texpos;

                capi.EntityTextureAtlas.GetOrInsertTexture(armorTexLoc, out textureSubId, out texpos, () =>
                {
                    IAsset texAsset = this.capi.Assets.TryGet(armorTexLoc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                    if (texAsset != null)
                    {
                        return texAsset.ToBitmap(capi);
                    }
                    return null;
                });

                if (val.Key == null)
                    continue;
                ctex.Baked = new BakedCompositeTexture() { BakedName = armorTexLoc, TextureSubId = textureSubId };

                ((EntityClientProperties)forEntity.SidedProperties).Textures[val.Key] = ctex;
            }


            return gearShape;
        }
        public override string GetMeshCacheKey(ItemStack itemstack)
        {
            string metal = itemstack.Item.Variant.Get("loop", "steel");
            return string.Concat(new string[]
            {
                this.Code.ToShortString(),
                "-",
                metal
            });
        }
        public override TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (this.tmpTextures.TryGetValue(textureCode, out var res))
                {
                    return this.getOrCreateTexPos(res);
                }

                AssetLocation value = null;
                if (Textures.TryGetValue(textureCode, out var value2))
                {
                    value = value2.Baked.BakedName;
                }

                if (value == null && Textures.TryGetValue("all", out value2))
                {
                    value = value2.Baked.BakedName;
                }

                if (value == null)
                {
                    nowTesselatingShape?.Textures.TryGetValue(textureCode, out value);
                }

                if (value == null)
                {
                    value = new AssetLocation(textureCode);
                }

                return getOrCreateTexPos(value);
            }
        }
        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            string maskMetal = inSlot.Itemstack.Item.Variant.Get("loop", "steel");

            /* if (gem != "none")
            {
                dsc.AppendLine(Lang.Get("canjewelry:necklace-parts-with-gem-held-info", Lang.Get("material-" + loop), Lang.Get("material-" + socket), gem));
            }
            else
            {
                dsc.AppendLine(Lang.Get("canjewelry:necklace-parts-without-gem-held-info", Lang.Get("material-" + loop), Lang.Get("material-" + socket)));
            }*/
            if ((api as ICoreClientAPI).Settings.Bool["extendedDebugInfo"])
    {
        if (DressType == EnumCharacterDressType.Unknown)
        {
            dsc.AppendLine(Lang.Get("Cloth Category: Unknown"));
        }
        else
        {
            dsc.AppendLine(Lang.Get("Cloth Category: {0}", Lang.Get("clothcategory-" + inSlot.Itemstack.ItemAttributes["clothescategory"].AsString())));
        }
    }

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
            if (meshrefid == 0 || !this.meshrefs.TryGetValue(meshrefid, out renderinfo.ModelRef))
            {
                int id = this.meshrefs.Count + 1;
                MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(this.GenMesh(itemstack, capi.ItemTextureAtlas));
                renderinfo.ModelRef = (this.meshrefs[id] = modelref);
                itemstack.TempAttributes.SetInt("meshRefId", id);
            }
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }
        public override MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos forBlockPos = null)
        {
            ICoreClientAPI coreClientAPI = api as ICoreClientAPI;
            curAtlas = targetAtlas;
            if (targetAtlas == coreClientAPI.ItemTextureAtlas)
            {
                ITexPositionSource textureSource = coreClientAPI.Tesselator.GetTextureSource(itemstack.Item);
                return genMesh(coreClientAPI, itemstack, this);
            }

            curAtlas = targetAtlas;
            MeshData meshData = genMesh(api as ICoreClientAPI, itemstack, this);
            meshData.RenderPassesAndExtraBits.Fill((short)1);
            return meshData;
        }
        private MeshData genMesh(ICoreClientAPI capi, ItemStack itemstack, ITexPositionSource texSource)
        {
            string carcassus = itemstack.Attributes.GetString("metal", null);
            tmpTextures["silver1"] = new AssetLocation("block/metal/sheet/" + carcassus + "1.png");
            tmpTextures["rotten-king-mask"] = new AssetLocation("canjewelry:item/rottenking.png");
            tmpTextures["rotten-king-cloth"] = new AssetLocation("canjewelry:item/rottenkingcloth.png");

            ContainedTextureSource cnts = new ContainedTextureSource(this.api as ICoreClientAPI, curAtlas, new Dictionary<string, AssetLocation>(), string.Format("For render in shield {0}", this.Code));
            cnts.Textures.Clear();

            cnts.Textures["silver1"] = new AssetLocation("block/metal/sheet/" + carcassus + "1.png");
            cnts.Textures["rotten-king-mask"] = new AssetLocation("canjewelry:item/rottenking.png");
            cnts.Textures["rotten-king-cloth"] = new AssetLocation("canjewelry:item/rottenkingcloth.png");

            MeshData mesh;
            this.capi.Tesselator.TesselateItem(this, out mesh, cnts);
            return mesh;
        }
        public override string GetHeldItemName(ItemStack itemStack)
        {

            string variant = itemStack.Item.Variant.Get("loop");
            return Lang.Get("game:material-" + variant) + Lang.Get("canjewelry:item-canrottenkingmask");
        }
    }
}
