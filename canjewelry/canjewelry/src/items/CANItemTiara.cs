using canjewelry.src.CB;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using static HarmonyLib.Code;
using static Vintagestory.Server.Timer;

namespace canjewelry.src.items
{
    public class CANItemTiara: CANItemWearable, IWearableShapeSupplier
    {
        public override Size2i AtlasSize => curAtlas.Size;

        private Dictionary<int, MultiTextureMeshRef> meshrefs
        {
            get
            {
                return ObjectCacheUtil.GetOrCreate<Dictionary<int, MultiTextureMeshRef>>(this.api, "cantiarameshrefs", () => new Dictionary<int, MultiTextureMeshRef>());
            }
        }

        public string Construction
        {
            get
            {
                return this.Variant["construction"];
            }
        }

        private ITextureAtlasAPI curAtlas;

        public EnumCharacterDressType DressType { get; private set; }

        public StatModifiers StatModifers;

        private Shape nowTesselatingShape;

        private float offY;

        private float curOffY;

        private ICoreClientAPI capi;

        private ITextureAtlasAPI targetAtlas;

        private Dictionary<string, AssetLocation> tmpTextures = new Dictionary<string, AssetLocation>();

        private Dictionary<string, Dictionary<string, int>> durabilityGains;

        public override TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (!textureCode.Equals("seraph"))
                {
                    var c = 3;
                }
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
            this.durabilityGains = this.Attributes["durabilityGains"].AsObject<Dictionary<string, Dictionary<string, int>>>(null);
            this.AddAllTypesToCreativeInventory();

            string value = Attributes["clothescategory"].AsString();
            EnumCharacterDressType result = EnumCharacterDressType.Unknown;
            Enum.TryParse<EnumCharacterDressType>(value, ignoreCase: true, out result);
            DressType = result;

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

        public override void OnCreatedByCrafting(ItemSlot[] inSlots, ItemSlot outputSlot, GridRecipe byRecipe)
        {
            base.OnCreatedByCrafting(inSlots, outputSlot, byRecipe);
           /* int socketLevel = 1;
            if (inSlots[4].Itemstack != null && inSlots[4].Itemstack.Collectible.Attributes.KeyExists("levelOfSocket"))
            {
                socketLevel = inSlots[4].Itemstack.Collectible.Attributes["levelOfSocket"].AsInt();
            }
            ITreeAttribute socketSlotTree = new TreeAttribute();*/

            /*socketSlotTree.SetInt("size", 0);
            socketSlotTree.SetString("gemtype", "");
            socketSlotTree.SetInt("sockettype", socketLevel);

            ITreeAttribute socketEncrusted = new TreeAttribute();
            socketEncrusted.SetInt("socketsnumber", 1);
            socketEncrusted["slot" + 0] = socketSlotTree;
            outputSlot.Itemstack.Attributes["canencrusted"] = socketEncrusted;*/
            //add socket for gem with tier 3
        }

        public void AddAllTypesToCreativeInventory()
        {
            List<JsonItemStack> stacks = new List<JsonItemStack>();
            Dictionary<string, string[]> vg = this.Attributes["variantGroups"].AsObject<Dictionary<string, string[]>>(null);
            //foreach (string loop in vg["loop"])
            {
                string construction = this.Construction;
               // if (construction == "normal-neck")
                {
                    foreach (string carcassus in vg["carcassus"])
                    {
                        //foreach (string gem in vg["gem"])
                        {
                            stacks.Add(this.genJstack(string.Format("{{ carcassus: \"{0}\", gem_1: \"none\", gem_2: \"none\", gem_3: \"none\" }}", carcassus)));
                        }
                    }
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
            jsonItemStack.Resolve(this.api.World, "cantiara type", true);
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
            if (meshrefid == 0 || !this.meshrefs.TryGetValue(meshrefid, out renderinfo.ModelRef))
            {
                int id = this.meshrefs.Count + 1;
                MultiTextureMeshRef modelref = capi.Render.UploadMultiTextureMesh(this.GenMesh(itemstack, capi.ItemTextureAtlas));
                renderinfo.ModelRef = (this.meshrefs[id] = modelref);
                itemstack.TempAttributes.SetInt("meshRefId", id);
            }
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }

        private MeshData genMesh(ICoreClientAPI capi, ItemStack itemstack, ITexPositionSource texSource)
        {
            JsonObject attributes = itemstack.Collectible.Attributes;
            EntityProperties entityType = capi.World.GetEntityType(new AssetLocation("player"));
            Shape loadedShape = entityType.Client.LoadedShape;
            AssetLocation @base = entityType.Client.Shape.Base;
            Shape shape = new Shape
            {
                Elements = loadedShape.CloneElements(),
                Animations = loadedShape.Animations,
                AnimationsByCrc32 = loadedShape.AnimationsByCrc32,
                AttachmentPointsByCode = loadedShape.AttachmentPointsByCode,
                JointsById = loadedShape.JointsById,
                TextureWidth = loadedShape.TextureWidth,
                TextureHeight = loadedShape.TextureHeight,
                Textures = null
            };
            CompositeShape compositeShape = (attributes["attachShape"].Exists ? attributes["attachShape"].AsObject<CompositeShape>(null, itemstack.Collectible.Code.Domain) : ((itemstack.Class == EnumItemClass.Item) ? itemstack.Item.Shape : itemstack.Block.Shape));

            string construction = this.Construction;
            string carcassus = itemstack.Attributes.GetString("carcassus", "steel");
           /* string gem_1 = itemstack.Attributes.GetString("gem_1", "none");
            string gem_2 = itemstack.Attributes.GetString("gem_2", "none");
            string gem_3 = itemstack.Attributes.GetString("gem_3", "none");*/

            if (compositeShape == null)
            {
                capi.World.Logger.Warning("Entity armor {0} {1} does not define a shape through either the shape property or the attachShape Attribute. Armor pieces will be invisible.", itemstack.Class, itemstack.Collectible.Code);
                return null;
            }

            AssetLocation assetLocation = compositeShape.Base.CopyWithPath("shapes/" + compositeShape.Base.Path + ".json");
            Shape shape2 = Vintagestory.API.Common.Shape.TryGet(capi, assetLocation);
            if (shape2 == null)
            {
                capi.World.Logger.Warning("Entity wearable shape {0} defined in {1} {2} not found or errored, was supposed to be at {3}. Armor piece will be invisible.", compositeShape.Base, itemstack.Class, itemstack.Collectible.Code, assetLocation);
                return null;
            }
            this.tmpTextures.Clear();

            FillTextureDict(tmpTextures, itemstack);

            tmpTextures["carcassus"] = new AssetLocation("block/metal/sheet/" + carcassus + "1.png");
            shape.Textures = shape2.Textures;


            if (shape2.Textures.Count > 0 && shape2.TextureSizes.Count < shape2.Textures.Count)
            {
                shape2.TextureSizes.Clear();
                foreach (KeyValuePair<string, AssetLocation> texture in shape2.Textures)
                {
                    shape2.TextureSizes.Add(texture.Key, new int[2] { shape2.TextureWidth, shape2.TextureHeight });
                }
            }

            foreach (KeyValuePair<string, int[]> textureSize in shape2.TextureSizes)
            {
                shape.TextureSizes[textureSize.Key] = textureSize.Value;
            }

            ShapeElement[] elements = shape2.Elements;
            foreach (ShapeElement shapeElement in elements)
            {
                if (shapeElement.StepParentName != null)
                {
                    ShapeElement elementByName = shape.GetElementByName(shapeElement.StepParentName);
                    if (elementByName == null)
                    {
                        capi.World.Logger.Warning("Entity wearable shape {0} defined in {1} {2} requires step parent element with name {3}, but no such element was found in shape {3}. Will not be visible.", compositeShape.Base, itemstack.Class, itemstack.Collectible.Code, shapeElement.StepParentName, @base);
                    }
                    else if (elementByName.Children == null)
                    {
                        elementByName.Children = new ShapeElement[1] { shapeElement };
                    }
                    else
                    {
                        elementByName.Children = elementByName.Children.Append(shapeElement);
                    }
                }
                else
                {
                    capi.World.Logger.Warning("Entity wearable shape element {0} in shape {1} defined in {2} {3} did not define a step parent element. Will not be visible.", shapeElement.Name, compositeShape.Base, itemstack.Class, itemstack.Collectible.Code);
                }
            }

            nowTesselatingShape = shape;
            capi.Tesselator.TesselateShapeWithJointIds("entity", shape, out var modeldata, this, new Vec3f());
            nowTesselatingShape = null;
            return modeldata;
        }

        public override string GetHeldItemName(ItemStack itemStack)
        {
            string gem = itemStack.Attributes.GetString("1_gem", null);

            if (gem != "none")
            {
                return "Tiara" + gem;
            }
            else
            {
                return "Tiara";
            }



        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

           /* string loop = inSlot.Itemstack.Attributes.GetString("loop", null);
            string socket = inSlot.Itemstack.Attributes.GetString("socket", null);
            string gem = inSlot.Itemstack.Attributes.GetString("gem", null);*/

            /*if (gem != "none")
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

        public override MeshData GenMesh(ItemStack itemstack, ITextureAtlasAPI targetAtlas, BlockPos forBlockPos = null)
        {
            ICoreClientAPI coreClientAPI = api as ICoreClientAPI;
            curAtlas = targetAtlas;
            if (targetAtlas == coreClientAPI.ItemTextureAtlas)
            {
                ITexPositionSource textureSource = coreClientAPI.Tesselator.GetTextureSource(itemstack.Item);
                return genMesh(coreClientAPI, itemstack, this);
                /* ITexPositionSource textureSource = coreClientAPI.Tesselator.GetTextureSource(itemstack.Item);
                MeshData meshData1 =  genMesh(coreClientAPI, itemstack, this);
                MeshData meshData2 = genMesh(coreClientAPI, itemstack, this);
                meshData2.Rotate(new Vec3f(0.5f, 0.5f, 0.6f), 40, 40, 40);
                meshData1.AddMeshData(meshData2);
                return meshData1;*/
            }

            curAtlas = targetAtlas;
            MeshData meshData = genMesh(api as ICoreClientAPI, itemstack, this);
            meshData.RenderPassesAndExtraBits.Fill((short)1);
            return meshData;
        }

        public override string GetMeshCacheKey(ItemStack itemstack)
        {
            string carcassus = itemstack.Attributes.GetString("carcassus", null);
            string gem_1 = itemstack.Attributes.GetString("gem_1", null);
            string gem_2 = itemstack.Attributes.GetString("gem_2", null);
            string gem_3 = itemstack.Attributes.GetString("gem_3", null);

            return string.Concat(new string[]
            {
                this.Code.ToShortString(),
                "-",
                carcassus,
                "-",
                gem_1,
                "-",
                gem_2,
                "-",
                gem_3
            });
        }
        public void FillTextureDict(Dictionary<string, AssetLocation> newdict, ItemStack stack)
        {
            int maxSocketNumber = EncrustableCB.GetMaxAmountSockets(stack);
            if (maxSocketNumber == 1)
            {
                AssetLocation path;
                if (!canjewelry.gems_textures.TryGetValue(stack.Attributes.GetString("gem_1", "none"), out string assetPath))
                {
                    path = new AssetLocation("canjewelry:item/gem/notvis.png");
                }
                else
                {
                    path = canjewelry.capi.Assets.TryGet(assetPath + ".png").Location;
                }
                for (int i = 1; i < 4; i++)
                {
                    newdict[i.ToString() + "_gem"] = path;
                }
            }
            else if (maxSocketNumber == 2)
            {
                AssetLocation path;
                if (!canjewelry.gems_textures.TryGetValue(stack.Attributes.GetString("gem_1", "none"), out string assetPath))
                {
                    path = new AssetLocation("canjewelry:item/gem/notvis.png");
                }
                else
                {
                    path = canjewelry.capi.Assets.TryGet(assetPath + ".png").Location;
                }
                newdict["1_gem"] = path;
                newdict["3_gem"] = path;

                if (!canjewelry.gems_textures.TryGetValue(stack.Attributes.GetString("gem_2", "none"), out assetPath))
                {
                    path = new AssetLocation("canjewelry:item/gem/notvis.png");
                }
                else
                {
                    path = canjewelry.capi.Assets.TryGet(assetPath + ".png").Location;
                }
                newdict["3_gem"] = path;
            }
            else
            {
                for (int i = 1; i < 4; i++)
                {
                    if (!canjewelry.gems_textures.TryGetValue(stack.Attributes.GetString("gem_" + i.ToString(), "none"), out string assetPath))
                    {
                        newdict[i.ToString() + "_gem"] = new AssetLocation("canjewelry:item/gem/notvis.png");
                    }
                    else
                    {
                        newdict[i.ToString() + "_gem"] = canjewelry.capi.Assets.TryGet(assetPath + ".png").Location;
                    }
                }
            }
        }
        public Shape GetShape(ItemStack stack, EntityAgent forEntity, string texturePrefixCode)
        {

            JsonObject attributes = stack.Collectible.Attributes;
            EntityProperties entityType = capi.World.GetEntityType(new AssetLocation("player"));
            Shape loadedShape = entityType.Client.LoadedShape;
            AssetLocation @base = entityType.Client.Shape.Base;
            Shape shape = new Shape
            {
                Elements = loadedShape.CloneElements(),
                Animations = loadedShape.Animations,
                AnimationsByCrc32 = loadedShape.AnimationsByCrc32,
                AttachmentPointsByCode = loadedShape.AttachmentPointsByCode,
                JointsById = loadedShape.JointsById,
                TextureWidth = loadedShape.TextureWidth,
                TextureHeight = loadedShape.TextureHeight,
                Textures = null
            };
            CompositeShape compositeShape = (attributes["attachShape"].Exists ? attributes["attachShape"].AsObject<CompositeShape>(null, stack.Collectible.Code.Domain) : ((stack.Class == EnumItemClass.Item) ? stack.Item.Shape : stack.Block.Shape));

            AssetLocation assetLocation = compositeShape.Base.CopyWithPath("shapes/" + compositeShape.Base.Path + ".json");
            Shape shape2 = Vintagestory.API.Common.Shape.TryGet(capi, assetLocation);


            Dictionary<string, AssetLocation> newdict = new Dictionary<string, AssetLocation>();

            string carcassus = stack.Attributes.GetString("carcassus", null);
            string gem_1 = stack.Attributes.GetString("gem_1", null);
            string gem_2 = stack.Attributes.GetString("gem_2", null);
            string gem_3 = stack.Attributes.GetString("gem_3", null);



            newdict["carcassus"] = new AssetLocation("block/metal/sheet/" + carcassus + "1.png");

            FillTextureDict(newdict, stack);


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

                ctex.Baked = new BakedCompositeTexture() { BakedName = armorTexLoc, TextureSubId = textureSubId };

                ((EntityClientProperties)forEntity.SidedProperties).Textures[val.Key] = ctex;
            }

            return shape2;
        }
    }
}
