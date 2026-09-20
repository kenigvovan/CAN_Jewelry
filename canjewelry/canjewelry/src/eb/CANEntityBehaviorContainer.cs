using canjewelry.src.render;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace canjewelry.src.eb
{
    public abstract class CANEntityBehaviorContainer: EntityBehavior
    {
        protected ICoreAPI Api;

        private InWorldContainer container;

        public bool hideClothing;

        private bool eventRegistered;

        private bool dropContentsOnDeath;

        public abstract InventoryBase Inventory { get; }

        public abstract string InventoryClassName { get; }

        protected CANEntityBehaviorContainer(Entity entity)
            : base(entity)
        {
            container = new InWorldContainer(() => Inventory, InventoryClassName);
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            Api = entity.World.Api;
            container.Init(Api, () => entity.Pos.AsBlockPos, delegate
            {
                entity.WatchedAttributes.MarkPathDirty(InventoryClassName);
            });
            if (Api.Side == EnumAppSide.Client)
            {
                entity.WatchedAttributes.RegisterModifiedListener(InventoryClassName, inventoryModified);
            }

            dropContentsOnDeath = attributes?.IsTrue("dropContentsOnDeath") ?? false;
        }

        private void inventoryModified()
        {
            loadInv();
            entity.MarkShapeModified();
        }

        public override void OnGameTick(float deltaTime)
        {
            if (!eventRegistered && Inventory != null)
            {
                eventRegistered = true;
                Inventory.SlotModified += Inventory_SlotModified;
            }
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            if (Inventory != null)
            {
                Inventory.SlotModified -= Inventory_SlotModified;
            }
        }

        protected void Inventory_SlotModifiedBackpack(int slotid)
        {
            if (entity is EntityPlayer entityPlayer && entityPlayer.Player.InventoryManager.GetOwnInventory("backpack")[slotid] is ItemSlotBackpack)
            {
                entity.MarkShapeModified();
            }
        }

        protected virtual void Inventory_SlotModified(int slotid)
        {
            entity.MarkShapeModified();
        }

        public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            // What hangs the jewelry of the mod's own slots - rings, earrings, armbands - onto the
            // wearer. Items in the vanilla clothing slots (a necklace is one) travel the game's own
            // EntityBehaviorContainer instead and never reach here.
            Shape before = entityShape;
            try
            {
                this.addGearToShape(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
            }
            catch (Exception e)
            {
                // One piece of jewelry that cannot be attached must not cost the player their body:
                // the shape is handed back as it came and the wearer is drawn without it.
                entityShape = before;
                BlockPos asBlockPos = this.entity.Pos.AsBlockPos;
                Api?.World?.Logger?.Error("[canjewelry] error tesselating jewelry of entity {0} at {1}, drawing it without: {2}",
                    this.entity.Code, (asBlockPos != null) ? asBlockPos.ToString() : "?", e);
            }
            base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);

            if (Inventory != null && Inventory.Count > 0)
            {
                ItemSlot itemSlot = Inventory.MaxBy((ItemSlot slot) => (!slot.Empty) ? slot.Itemstack.Collectible.LightHsv[2] : 0);
                if (itemSlot?.Empty == false && itemSlot.Itemstack.Collectible.LightHsv[2] > 0)
                {
                    byte[] jewelryLight = itemSlot.Itemstack.Collectible.GetLightHsv(entity.World.BlockAccessor, null, itemSlot.Itemstack);
                    if (entity.LightHsv == null || jewelryLight[2] > entity.LightHsv[2])
                    {
                        entity.LightHsv = jewelryLight;
                    }
                }
            }
        }

        protected Shape addGearToShape(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
        {
            IInventory inventory = Inventory;
            if (inventory == null || (!(entity is EntityPlayer) && inventory.Empty))
            {
                return entityShape;
            }

            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot itemSlot = inventory[i];
                if (!itemSlot.Empty && !hideClothing)
                {
                    entityShape = addGearToShape(entityShape, itemSlot, i.ToString(), shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);
                }
            }
            if (shapeIsCloned && Api is ICoreClientAPI coreClientAPI)
            {
                EntityProperties entityType = Api.World.GetEntityType(entity.Code);
                if (entityType != null)
                {
                    foreach (KeyValuePair<string, CompositeTexture> texture in entityType.Client.Textures)
                    {
                        
                        CompositeTexture value = texture.Value;
                        if (value.Baked.TextureFilenames[0].Path.Equals("entity/humanoid/seraph-naked-hairless"))
                        {
                            continue;
                        }
                        value.Bake(Api.Assets);
                        coreClientAPI.EntityTextureAtlas.GetOrInsertTexture(value.Baked.TextureFilenames[0], out var textureSubId, out var _);
                        value.Baked.TextureSubId = textureSubId;
                        entity.Properties.Client.Textures[texture.Key] = texture.Value;
                    }
                }
            }

            return entityShape;
        }

        protected virtual Shape addGearToShape(Shape entityShape, ItemSlot gearslot, string slotCode, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements, Dictionary<string, StepParentElementTo> overrideStepParent = null)
        {
            if (gearslot.Empty || entityShape == null)
            {
                return entityShape;
            }

            IAttachableToEntity attachableToEntity = IAttachableToEntity.FromCollectible(gearslot.Itemstack.Collectible);
            if (attachableToEntity == null || !attachableToEntity.IsAttachable(entity, gearslot.Itemstack))
            {
                return entityShape;
            }

            if (!shapeIsCloned)
            {
                entityShape = entityShape.Clone();
                shapeIsCloned = true;
            }

            return addGearToShape(entityShape, gearslot.Itemstack, attachableToEntity, slotCode, shapePathForLogging, ref willDeleteElements, overrideStepParent);
        }

        protected virtual Shape addGearToShape(Shape entityShape, ItemStack stack, IAttachableToEntity iatta, string slotCode, string shapePathForLogging, ref string[] willDeleteElements, Dictionary<string, StepParentElementTo> overrideStepParent = null)
        {
            if (stack == null || iatta == null)
            {
                return entityShape;
            }

            float damageEffect = 0f;
            JsonObject itemAttributes = stack.ItemAttributes;
            if (itemAttributes != null && itemAttributes["visibleDamageEffect"].AsBool())
            {
                damageEffect = Math.Max(0f, 1f - (float)stack.Collectible.GetRemainingDurability(stack) / (float)stack.Collectible.GetMaxDurability(stack) * 1.1f);
            }

            entityShape.RemoveElements(iatta.GetDisableElements(stack));
            string[] keepElements = iatta.GetKeepElements(stack);
            if (keepElements != null && willDeleteElements != null)
            {
                string[] array = keepElements;
                foreach (string value in array)
                {
                    willDeleteElements = willDeleteElements.Remove(value);
                }
            }

            IDictionary<string, CompositeTexture> textures = entity.Properties.Client.Textures;
            string texturePrefixCode = iatta.GetTexturePrefixCode(stack);
            if (texturePrefixCode != null)
            {
                texturePrefixCode = texturePrefixCode + "-" + slotCode;
            }
            else
            {
                texturePrefixCode = slotCode;
            }

            Shape shape = null;
            AssetLocation assetLocation = null;
            CompositeShape compositeShape = null;
            if (stack.Collectible is IWearableShapeSupplier wearableShapeSupplier)
            {
                shape = wearableShapeSupplier.GetShape(stack, entity, texturePrefixCode);
            }

            if (shape == null)
            {
                compositeShape = iatta.GetAttachedShape(stack, slotCode);
                if (compositeShape?.Base == null)
                {
                    Api.World.Logger.Warning("[canjewelry] {0} {1} sits in a jewelry slot but names no attachable shape, drawing the wearer without it.", stack.Class, stack.Collectible.Code);
                    return entityShape;
                }

                assetLocation = compositeShape.Base.CopyWithPath("shapes/" + compositeShape.Base.Path + ".json");
                shape = Shape.TryGet(Api, assetLocation);
                if (shape == null)
                {
                    Api.World.Logger.Warning("Entity attachable shape {0} defined in {1} {2} not found or errored, was supposed to be at {3}. Shape will be invisible.", compositeShape.Base, stack.Class, stack.Collectible.Code, assetLocation);
                    // Not null: the caller assigns this straight back to the entity shape, and a null
                    // there is the wearer's whole body gone over one unreadable ring.
                    return entityShape;
                }

                shape.SubclassForStepParenting(texturePrefixCode, damageEffect);
                shape.ResolveReferences(entity.World.Logger, assetLocation);
            }

            ICoreClientAPI capi = Api as ICoreClientAPI;
            Dictionary<string, CompositeTexture> dictionary = null;
            if (capi != null)
            {
                dictionary = new Dictionary<string, CompositeTexture>();
                iatta.CollectTextures(stack, shape, texturePrefixCode, dictionary);
            }

            applyStepParentOverrides(overrideStepParent, shape);

            // ".cangemworn on" prints, for every piece of jewelry hung onto the wearer, what each
            // texture code its faces ask for actually resolves to once the piece is on: the file,
            // its place in the entity atlas, and the dimensions the uvs are read against. A blank
            // white piece is one of these three going wrong.
            HashSet<string> wanted = null;
            if (capi != null && CANGemDebug.WornLog)
            {
                wanted = new HashSet<string>();
                foreach (ShapeElement element in shape.Elements ?? new ShapeElement[0])
                {
                    element.WalkRecursive(el =>
                    {
                        foreach (ShapeElementFace face in el.FacesResolved ?? new ShapeElementFace[0])
                        {
                            if (face != null && face.Enabled && face.Texture != null) wanted.Add(face.Texture);
                        }
                    });
                }
            }

            StepParentShape(entityShape, shape,
                (compositeShape?.Base.ToString() ?? "Custom texture from ItemWearableShapeSupplier") + $" defined in {stack.Class} {stack.Collectible.Code}",
                shapePathForLogging, Api.World.Logger, delegate (string texcode, AssetLocation tloc)
            {
                addTexture(texcode, tloc, textures, texturePrefixCode, capi);
            });
            
            if (compositeShape?.Overlays != null)
            {
                CompositeShape[] overlays = compositeShape.Overlays;
                foreach (CompositeShape compositeShape2 in overlays)
                {
                    Shape shape2 = Shape.TryGet(Api, compositeShape2.Base.CopyWithPath("shapes/" + compositeShape2.Base.Path + ".json"));
                    if (shape2 == null)
                    {
                        Api.World.Logger.Warning("Entity attachable shape {0} overlay {4} defined in {1} {2} not found or errored, was supposed to be at {3}. Shape will be invisible.", compositeShape.Base, stack.Class, stack.Collectible.Code, assetLocation, compositeShape2.Base);
                        continue;
                    }

                    shape2.SubclassForStepParenting(texturePrefixCode, damageEffect);
                    if (capi != null)
                    {
                        iatta.CollectTextures(stack, shape2, texturePrefixCode, dictionary);
                    }

                    applyStepParentOverrides(overrideStepParent, shape2);
                    entityShape.StepParentShape(shape2, compositeShape2.Base.ToShortString(), shapePathForLogging, Api.Logger, delegate (string texcode, AssetLocation tloc)
                    {
                        addTexture(texcode, tloc, textures, texturePrefixCode, capi);
                    });
                }
            }

            if (capi != null)
            {
                foreach (KeyValuePair<string, CompositeTexture> val2 in dictionary)
                {
                    CompositeTexture cmpt = (textures[val2.Key] = val2.Value.Clone());
                    int textureSubid;
                    TextureAtlasPosition textureAtlasPosition;
                    capi.EntityTextureAtlas.GetOrInsertTexture(cmpt, out textureSubid, out textureAtlasPosition, 0f);
                    cmpt.Baked.TextureSubId = textureSubid;
                    publishToWearablesTesselator(val2.Key, textureAtlasPosition);
                }
            }

            if (wanted != null)
            {
                var report = new StringBuilder();
                foreach (string code in wanted)
                {
                    report.Append("\n  ").Append(code).Append(" -> ");
                    if (!textures.TryGetValue(code, out CompositeTexture resolved))
                    {
                        report.Append("MISSING from the entity, will be drawn with the wearer's skin patch");
                    }
                    else
                    {
                        report.Append(resolved.Baked?.BakedName?.ToString() ?? resolved.Base?.ToString() ?? "no baked name")
                              .Append(" subid ").Append(resolved.Baked?.TextureSubId.ToString() ?? "-");
                        TextureAtlasPosition pos = resolved.Baked != null
                            ? capi.EntityTextureAtlas.Positions[resolved.Baked.TextureSubId]
                            : null;
                        if (pos != null)
                        {
                            report.Append(" atlas ").Append(pos.atlasTextureId)
                                  .Append(" uv [").Append(pos.x1).Append(", ").Append(pos.y1)
                                  .Append(" .. ").Append(pos.x2).Append(", ").Append(pos.y2).Append(']');
                        }
                    }

                    report.Append("; size ")
                          .Append(entityShape.TextureSizes.TryGetValue(code, out int[] size)
                              ? size[0] + "x" + size[1]
                              : "none, falls back to the wearer's " + entityShape.TextureWidth + "x" + entityShape.TextureHeight);
                }

                capi.World.Logger.Notification("[canjewelry] worn {0} slot '{1}' prefix '{2}', shape {3}x{4}:{5}",
                    stack.Collectible.Code, slotCode, texturePrefixCode, shape.TextureWidth, shape.TextureHeight, report);
            }

            return entityShape;
        }
        public bool StepParentShape(Shape entityShape, Shape childShape, string childLocationForLogging, string parentLocationForLogging, ILogger logger, Action<string, AssetLocation> onTexture)
        {
            return StepParentShape(entityShape, null, childShape.Elements, childShape, childLocationForLogging, parentLocationForLogging, logger, onTexture);
        }

        private bool StepParentShape(Shape entityShape, ShapeElement parentElem, ShapeElement[] elements, Shape childShape, string childLocationForLogging, string parentLocationForLogging, ILogger logger, Action<string, AssetLocation> onTexture)
        {
            bool flag = false;
            foreach (ShapeElement shapeElement in elements)
            {
                if (shapeElement.Children != null)
                {
                    bool flag2 = StepParentShape(entityShape, shapeElement, shapeElement.Children, childShape, childLocationForLogging, parentLocationForLogging, logger, onTexture);
                    flag = flag || flag2;
                }

                if (shapeElement.StepParentName != null)
                {
                    ShapeElement elementByName = GetElementByName(entityShape, shapeElement.StepParentName, StringComparison.InvariantCultureIgnoreCase);
                    if (elementByName == null)
                    {
                        logger.Warning("Step parented shape {0} requires step parent element with name {1}, but no such element was found in parent shape {2}. Will not be visible.", childLocationForLogging, shapeElement.StepParentName, parentLocationForLogging);
                        continue;
                    }

                    if (parentElem != null)
                    {
                        parentElem.Children = parentElem.Children.Remove(shapeElement);
                    }

                    if (elementByName.Children == null)
                    {
                        elementByName.Children = new ShapeElement[1] { shapeElement };
                    }
                    else
                    {
                        elementByName.Children = elementByName.Children.Append(shapeElement);
                    }

                    shapeElement.ParentElement = elementByName;
                    shapeElement.SetJointIdRecursive(elementByName.JointId);
                    flag = true;
                }
                else if (parentElem == null)
                {
                    logger.Warning("Step parented shape {0} did not define a step parent element for parent shape {1}. Will not be visible.", childLocationForLogging, parentLocationForLogging);
                }
            }

            if (!flag)
            {
                return false;
            }

            // The jewelry's own texture codes have to be carried over to the shape it was just hung
            // onto, exactly as Shape.StepParentShape does it. Without the sizes the uvs of the piece
            // are read against the wearer's texture dimensions instead of its own, and the piece
            // comes out sampling empty atlas - a plain white ring.
            if (childShape.Textures != null)
            {
                foreach (KeyValuePair<string, AssetLocation> texture in childShape.Textures)
                {
                    onTexture(texture.Key, texture.Value);
                }

                foreach (KeyValuePair<string, int[]> textureSize in childShape.TextureSizes)
                {
                    entityShape.TextureSizes[textureSize.Key] = textureSize.Value;
                }

                // A code the shape gave no size of its own: the shape's own dimensions, not the
                // wearer's.
                foreach (KeyValuePair<string, AssetLocation> texture in childShape.Textures)
                {
                    if (!entityShape.TextureSizes.ContainsKey(texture.Key))
                    {
                        entityShape.TextureSizes[texture.Key] = new int[2] { childShape.TextureWidth, childShape.TextureHeight };
                    }
                }
            }

            return flag;
        }
        public ShapeElement GetElementByName(Shape entityShape, string name, StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
        {
            if (entityShape.Elements == null)
            {
                return null;
            }

            return GetElementByName(entityShape, name, entityShape.Elements, isWildcard: false);
        }
        private ShapeElement GetElementByName(Shape entityShape, string name, ShapeElement[] elems, bool isWildcard)
        {
            foreach (ShapeElement shapeElement in elems)
            {
                if (isWildcard ? WildcardUtil.Match(name, shapeElement.Name) : shapeElement.Name.EqualsFastIgnoreCase(name))
                {
                    return shapeElement;
                }

                if (shapeElement.Children != null)
                {
                    ShapeElement elementByName = GetElementByName(entityShape, name, shapeElement.Children, isWildcard);
                    if (elementByName != null)
                    {
                        return elementByName;
                    }
                }
            }

            return null;
        }

        private static void applyStepParentOverrides(Dictionary<string, StepParentElementTo> overrideStepParent, Shape gearShape)
        {
            if (overrideStepParent == null)
            {
                return;
            }

            overrideStepParent.TryGetValue("", out var value);
            ShapeElement[] elements = gearShape.Elements;
            foreach (ShapeElement shapeElement in elements)
            {
                StepParentElementTo value2;
                if (shapeElement.StepParentName == null || shapeElement.StepParentName.Length == 0)
                {
                    shapeElement.StepParentName = value.ElementName;
                }
                else if (overrideStepParent.TryGetValue(shapeElement.StepParentName, out value2))
                {
                    shapeElement.StepParentName = value2.ElementName;
                }
            }
        }

        private void addTexture(string texcode, AssetLocation tloc, IDictionary<string, CompositeTexture> textures, string texturePrefixCode, ICoreClientAPI capi)
        {
            if (capi != null)
            {
                CompositeTexture compositeTexture = (textures[texturePrefixCode + texcode] = new CompositeTexture(tloc));
                compositeTexture.Bake(Api.Assets);
                capi.EntityTextureAtlas.GetOrInsertTexture(compositeTexture.Baked.TextureFilenames[0], out var textureSubId, out var texPos);
                compositeTexture.Baked.TextureSubId = textureSubId;
                publishToWearablesTesselator(texturePrefixCode + texcode, texPos);
            }
        }

        // PlayerModelLib swaps the player renderer for one that tesselates with its
        // WearablesTesselatorBehavior as the texture source, and that source answers only from
        // its own WearableTextures map - entity.Properties.Client.Textures, where the code above
        // files the jewelry, is never consulted. An unknown code there resolves to an empty atlas
        // position, and the piece comes out blank white. So every texture goes into that map as
        // well. Reflection keeps PlayerModelLib an optional runtime neighbour, not a build
        // dependency; the lookup is cached per entity because it runs once per texture per
        // retesselation.
        private object wearableTextures;
        private static System.Reflection.MethodInfo wearableTexturesSetValue;

        private void publishToWearablesTesselator(string textureCode, TextureAtlasPosition position)
        {
            if (position == null) return;

            if (wearableTextures == null)
            {
                var behaviors = entity.Properties?.Client?.Behaviors;
                if (behaviors == null) return;
                foreach (EntityBehavior bh in behaviors)
                {
                    Type type = bh.GetType();
                    if (type.FullName != "PlayerModelLib.WearablesTesselatorBehavior") continue;

                    System.Reflection.FieldInfo field = type.GetField("WearableTextures");
                    wearableTexturesSetValue ??= field?.FieldType.GetMethod("SetValue", new[] { typeof(string), typeof(TextureAtlasPosition) });
                    if (wearableTexturesSetValue == null) return;

                    wearableTextures = field.GetValue(bh);
                    break;
                }
                if (wearableTextures == null) return;
            }

            wearableTexturesSetValue.Invoke(wearableTextures, new object[] { textureCode, position });
        }

        public override void OnLoadCollectibleMappings(IWorldAccessor worldForNewMappings, Dictionary<int, AssetLocation> oldBlockIdMapping, Dictionary<int, AssetLocation> oldItemIdMapping, bool resolveImports)
        {
            container.OnLoadCollectibleMappings(worldForNewMappings, oldBlockIdMapping, oldItemIdMapping, 0, resolveImports);
        }

        public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
        {
            container.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        }

        public override void FromBytes(bool isSync)
        {
            loadInv();
        }

        protected virtual void loadInv()
        {
            if (Inventory != null)
            {
                container.FromTreeAttributes(entity.WatchedAttributes, entity.World);
                entity.MarkShapeModified();
            }
        }

        public override void ToBytes(bool forClient)
        {
            storeInv();
        }

        public virtual void storeInv()
        {
            container.ToTreeAttributes(entity.WatchedAttributes);
            entity.WatchedAttributes.MarkPathDirty(InventoryClassName);
            entity.World.BlockAccessor.GetChunkAtBlockPos(entity.ServerPos.AsBlockPos)?.MarkModified();
        }

        public override bool TryGiveItemStack(ItemStack itemstack, ref EnumHandling handling)
        {
            ItemSlot itemSlot = new DummySlot(null);
            itemSlot.Itemstack = itemstack.Clone();
            ItemStackMoveOperation op = new ItemStackMoveOperation(entity.World, EnumMouseButton.Left, (EnumModifierKey)0, EnumMergePriority.AutoMerge, itemstack.StackSize);
            if (Inventory != null)
            {
                WeightedSlot bestSuitedSlot = Inventory.GetBestSuitedSlot(itemSlot, null, new List<ItemSlot>());
                if (bestSuitedSlot.weight > 0f)
                {
                    itemSlot.TryPutInto(bestSuitedSlot.slot, ref op);
                    itemstack.StackSize -= op.MovedQuantity;
                    entity.WatchedAttributes.MarkAllDirty();
                    return op.MovedQuantity > 0;
                }
            }

            if ((entity as EntityAgent)?.LeftHandItemSlot?.Inventory != null)
            {
                WeightedSlot weightedSlot = (entity as EntityAgent)?.LeftHandItemSlot.Inventory.GetBestSuitedSlot(itemSlot, null, new List<ItemSlot>());
                if (weightedSlot.weight > 0f)
                {
                    itemSlot.TryPutInto(weightedSlot.slot, ref op);
                    itemstack.StackSize -= op.MovedQuantity;
                    entity.WatchedAttributes.MarkAllDirty();
                    return op.MovedQuantity > 0;
                }
            }

            return false;
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);
            if (dropContentsOnDeath)
            {
                Inventory.DropAll(entity.ServerPos.XYZ);
            }
        }
    }
}
