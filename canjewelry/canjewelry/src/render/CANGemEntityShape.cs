using canjewelry.src.api;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace canjewelry.src.render
{
    /// <summary>
    /// Puts the gems of worn gear onto the wearer.
    ///
    /// <para>Worn armour is not rendered from an item mesh at all: the game takes the gear's shape,
    /// step parents its elements into the wearer's shape and tesselates the lot as one entity. So a
    /// gem cannot be laid on top as a mesh the way it is in hand — it has to join that shape, which
    /// is what happens here. As a shape element it is parented to a bone and follows every
    /// animation for free, the same as the armour around it.</para>
    /// </summary>
    public static class CANGemEntityShape
    {
        /// <summary>A shape element is measured in sixteenths of a block, a pose in blocks.</summary>
        private const double PixelsPerBlock = 16.0;

        /// <inheritdoc cref="CANGemDebug.WornLog"/>
        public static bool Verbose
        {
            get => CANGemDebug.WornLog;
            set => CANGemDebug.WornLog = value;
        }

        /// <summary>
        /// One line of the walk-through, into the log rather than the chat — this runs for every
        /// piece of gear of every entity in sight. The chat gets the tally instead. Call sites check
        /// <see cref="Verbose"/> themselves, so a switched off log builds no strings at all.
        /// </summary>
        private static void Say(ICoreClientAPI capi, string message)
        {
            capi.Logger.Notification("[canjewelry] worn: " + message);
        }

        private static string Round(FastVec3f value)
        {
            return string.Format("[{0:0.###} {1:0.###} {2:0.###}]", value.X, value.Y, value.Z);
        }

        /// <summary>
        /// Adds a shape element per socketed gem to the wearer's shape, already carrying the gear.
        /// Does nothing when the item has no gems, when no pose is written for worn gear, or when
        /// the gear element to hang them off cannot be found.
        /// </summary>
        public static void AddGems(ICoreAPI api, ITextureAtlasAPI targetAtlas, Shape entityShape, ItemStack stack,
            IDictionary<string, CompositeTexture> collectedTextures, string texturePrefixCode,
            string parentLocationForLogging)
        {
            // The server walks this code too (gear shapes drive more than rendering), but there is
            // no atlas to put a gem texture into and nothing to draw.
            if (!(api is ICoreClientAPI capi) || targetAtlas == null || entityShape == null) return;

            // Before every other check, so the log also shows the cases that bow out early.
            if (Verbose) Say(capi, "reached for " + stack?.Collectible?.Code);

            if (!CANGemVisibility.Enabled(capi))
            {
                if (Verbose) Say(capi, "gem visuals are switched off, by the server config or by this client");
                return;
            }

            // Adornments carry their gems inside their own shape already.
            if (CANGemVisual.DrawsOwnGems(stack))
            {
                if (Verbose) Say(capi, "adornment, its gems are part of its own shape");
                return;
            }

            List<CANSocketGem> gems = CANGemMeshBuilder.CollectGems(stack);
            if (gems.Count == 0)
            {
                if (Verbose) Say(capi, stack.Collectible.Code + ": no gems");
                return;
            }
            if (Verbose)
            {
                Say(capi, stack.Collectible.Code + ": " + gems.Count + " gem(s), prefix '" + texturePrefixCode + "'");
            }

            int placed = 0;
            foreach (CANSocketGem gem in gems)
            {
                // Both placements of the gem, the same as on the item mesh: one gem on the near face
                // of the piece and one on the far face, so it is seen from either direction. Each
                // side can hang off an element of its own - the two faces of a piece are often
                // different parts of the model.
                for (int gemSide = 0; gemSide < CANGemVisualSide.Count; gemSide++)
                {
                    string attachTo = AttachElementName(entityShape, stack, gem.SocketIndex, gemSide,
                        texturePrefixCode);
                    if (attachTo == null)
                    {
                        if (Verbose)
                        {
                            Say(capi, "socket " + gem.SocketIndex + ": no element to hang it off. "
                                      + FirstElementNames(entityShape));
                        }
                        capi.Logger.Debug("[canjewelry] no element to hang the gem of {0} off, skipped",
                            stack.Collectible.Code);
                        continue;
                    }

                    ModelTransform pose = CANGemVisualRegistry.ResolvePose(
                        stack, gem.SocketIndex, CANGemVisualTarget.Body, gemSide);
                    if (pose == null || CANGemMeshBuilder.IsDegenerate(pose))
                    {
                        if (Verbose)
                        {
                            Say(capi, "socket " + gem.SocketIndex + " " + CANGemVisualSide.Name(gemSide)
                                      + ": no usable pose");
                        }
                        continue;
                    }

                    Shape gemShape = BuildGemShape(capi, gem, pose, attachTo, entityShape, stack, texturePrefixCode);
                    if (gemShape == null)
                    {
                        if (Verbose) Say(capi, "socket " + gem.SocketIndex + ": gem shape could not be built");
                        continue;
                    }

                    // Its own prefix per socket and side: step parenting renames elements and texture
                    // codes by it, and two gems would otherwise share one element name.
                    string prefix = texturePrefixCode + "canjwgem" + gem.SocketIndex
                                    + CANGemVisualSide.Name(gemSide) + "-";
                    bool added = entityShape.StepParentShape(gemShape, prefix, "canjewelry gem",
                        parentLocationForLogging, capi.Logger,
                        (texcode, tloc) => EntityBehaviorContainer.addTexture(
                            capi, texcode, tloc, collectedTextures, prefix, targetAtlas));
                    if (added) placed++;
                    if (Verbose)
                    {
                        Say(capi, "socket " + gem.SocketIndex + " " + CANGemVisualSide.Name(gemSide)
                                  + ": attached to '" + attachTo + "', ok " + added);
                    }
                }
            }

            // The one line the chat gets: what came of this piece. The steps are in the log.
            if (Verbose)
            {
                capi.ShowChatMessage("[canjewelry] worn " + stack.Collectible.Code + ": "
                                     + placed + " of " + (gems.Count * CANGemVisualSide.Count) + " placed");
            }
        }

        /// <summary>
        /// The element the gem hangs off: the one the rule names, or the first element the gear put
        /// into the wearer's shape. Gear elements are renamed with the texture prefix when they are
        /// step parented, so that prefix is what identifies them here.
        /// </summary>
        private static string AttachElementName(Shape entityShape, ItemStack stack, int socketIndex, int side,
            string texturePrefixCode)
        {
            string named = CANGemVisualRegistry.ResolveAttachElement(stack, socketIndex, side);
            if (named != null)
            {
                // A rule names the element as the gear's shape spells it; on the wearer it carries
                // the prefix. Both are tried, so a rule can also name an element of the body.
                if (FindElement(entityShape, texturePrefixCode + named) != null) return texturePrefixCode + named;
                if (FindElement(entityShape, named) != null) return named;
                return null;
            }

            if (string.IsNullOrEmpty(texturePrefixCode)) return null;
            ShapeElement first = FindElementByPrefix(entityShape.Elements, texturePrefixCode);
            return first?.Name;
        }

        /// <summary>
        /// Where a place measured from one bone sits in the wearer's own space.
        ///
        /// <para>A body pose is stored relative to the bone the gem hangs off, which is what makes
        /// it follow that bone — but bones are turned every which way (several are rotated a half
        /// turn), so editing in that space moves a gem sideways when the slider says forward and
        /// mirrors it onto the other leg. The debug menu therefore edits in the wearer's space and
        /// converts on the way in and out, and a gem also keeps its place when it is moved to
        /// another bone.</para>
        ///
        /// <para>Returns false when the bone cannot be found on the player right now — the gear has
        /// to be worn for its elements to be part of the wearer's shape at all.</para>
        /// </summary>
        private static void Apply(float[] matrix, ref FastVec3f translation)
        {
            float[] result = new float[4];
            Mat4f.MulWithVec4(matrix, new float[] { translation.X, translation.Y, translation.Z, 1 }, result);
            translation.Set(result[0], result[1], result[2]);
        }

        /// <summary>
        /// The matrix of a gear element as it stands on the player, whoever is actually wearing it.
        ///
        /// <para>Poses are tuned against the full body mesh, and vanilla always builds that on the
        /// player's shape (genFullBodyMesh reads wearerEntityCode, defaulting to "player"). An
        /// armour stand has the same bone names in different places, so converting a pose by its
        /// bones puts the gem somewhere else entirely. Taking the gear's own local transforms but
        /// the player's bone keeps one pose right on every wearer.</para>
        ///
        /// <para>Null when the player's shape or that bone cannot be found; the caller then falls
        /// back to the wearer's own bone.</para>
        /// </summary>
        private static float[] WearerBoneMatrix(ICoreClientAPI capi, ItemStack stack, ShapeElement bone,
            string texturePrefixCode)
        {
            Shape wearerShape = WearerShape(capi, stack);
            if (wearerShape == null) return null;

            ShapeElement host = HostBone(bone, texturePrefixCode);
            if (host?.Name == null) return null;

            ShapeElement hostOnWearer = wearerShape.GetElementByName(host.Name,
                System.StringComparison.InvariantCultureIgnoreCase);
            if (hostOnWearer == null) return null;

            // The gear's part of the chain, with the current wearer's bone divided out.
            float[] gearLocal = Mat4f.Create();
            float[] hostInverse = Mat4f.Create();
            if (Mat4f.Invert(hostInverse, ModelMatrix(host)) == null) return null;
            Mat4f.Mul(gearLocal, hostInverse, ModelMatrix(bone));

            float[] result = Mat4f.Create();
            Mat4f.Mul(result, ModelMatrix(hostOnWearer), gearLocal);
            return result;
        }

        /// <summary>
        /// Whether the game builds this item a full body mesh — armour and clothing fitted onto the
        /// wearer. Those have their poses written in the wearer's space; everything else that ends
        /// up on an entity (a weapon in an armour stand's hand) keeps the space of its own mesh.
        /// </summary>
        private static bool IsFittedToWearer(ItemStack stack)
        {
            return stack?.Collectible?
                       .GetCollectibleBehavior<CollectibleBehaviorWearableAttachment>(withInheritance: true)
                       is IContainedMeshSource
                   && IAttachableToEntity.FromCollectible(stack.Collectible) != null;
        }

        /// <summary>The shape the gear's full body mesh is built on — the player's, unless the item says otherwise.</summary>
        private static Shape WearerShape(ICoreClientAPI capi, ItemStack stack)
        {
            JsonObject attributes = stack?.Collectible?.Attributes;
            string code = (attributes != null ? attributes["wearerEntityCode"].AsString() : null) ?? "player";

            return capi.World.GetEntityType(new AssetLocation(code))?.Client?.LoadedShape;
        }

        /// <summary>
        /// The wearer's own bone a gear element hangs off: the nearest ancestor that is not itself
        /// part of the gear. Gear elements are the ones renamed with the texture prefix when they
        /// were step parented in.
        /// </summary>
        private static ShapeElement HostBone(ShapeElement element, string texturePrefixCode)
        {
            if (string.IsNullOrEmpty(texturePrefixCode)) return null;
            if (!IsGearElement(element, texturePrefixCode)) return element;

            List<ShapeElement> path = element.GetParentPath();
            for (int i = path.Count - 1; i >= 0; i--)
            {
                if (!IsGearElement(path[i], texturePrefixCode)) return path[i];
            }
            return null;
        }

        private static bool IsGearElement(ShapeElement element, string texturePrefixCode)
        {
            return element?.Name != null
                   && element.Name.StartsWith(texturePrefixCode, System.StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>The element's transform including every parent, in the wearer's own space.</summary>
        private static float[] ModelMatrix(ShapeElement element)
        {
            float[] model = Mat4f.Create();
            Mat4f.Identity(model);

            List<ShapeElement> path = element.GetParentPath();
            path.Add(element);

            float[] local = new float[16];
            float[] product = new float[16];
            foreach (ShapeElement step in path)
            {
                Mat4f.Identity(local);
                step.GetLocalTransformMatrix(0, local, null);
                Mat4f.Mul(product, model, local);
                model = Mat4f.CloneIt(product);
            }
            return model;
        }

        /// <summary>
        /// The element names of the gear's own shape — what a body gem can be hung off, as the gear
        /// spells them, without the prefix the wearer's shape adds. Empty when the item is not worn
        /// gear or its shape cannot be read.
        /// </summary>
        public static List<string> GearElementNames(ICoreClientAPI capi, ItemStack stack)
        {
            var names = new List<string>();
            if (stack?.Collectible == null) return names;

            IAttachableToEntity attachable = IAttachableToEntity.FromCollectible(stack.Collectible);
            CompositeShape gearShape = attachable?.GetAttachedShape(stack, "default");
            if (gearShape?.Base == null) return names;

            Shape shape = Shape.TryGet(capi, gearShape.Base.CopyWithPathPrefixAndAppendixOnce("shapes/", ".json"));
            if (shape == null) return names;

            CollectNames(shape.Elements, names, 200);
            return names;
        }

        /// <summary>The first few element names of a shape, to see what a failed lookup had to pick from.</summary>
        private static string FirstElementNames(Shape shape)
        {
            var names = new List<string>();
            CollectNames(shape.Elements, names, 12);
            return "elements: " + string.Join(", ", names);
        }

        private static void CollectNames(ShapeElement[] elements, List<string> into, int max)
        {
            if (elements == null) return;

            foreach (ShapeElement element in elements)
            {
                if (into.Count >= max) return;
                if (element?.Name != null) into.Add(element.Name);
                CollectNames(element?.Children, into, max);
            }
        }

        private static ShapeElement FindElement(Shape shape, string name)
        {
            return shape.GetElementByName(name, System.StringComparison.InvariantCultureIgnoreCase);
        }

        private static ShapeElement FindElementByPrefix(ShapeElement[] elements, string prefix)
        {
            if (elements == null) return null;

            foreach (ShapeElement element in elements)
            {
                if (element?.Name != null
                    && element.Name.StartsWith(prefix, System.StringComparison.InvariantCultureIgnoreCase))
                {
                    return element;
                }

                ShapeElement inChildren = FindElementByPrefix(element?.Children, prefix);
                if (inChildren != null) return inChildren;
            }
            return null;
        }

        /// <summary>
        /// The gem's own shape, wrapped in an element that carries the pose and the step parent.
        ///
        /// <para>The wrapper is a zero sized element with no faces, so it draws nothing itself: it
        /// exists to hold the position, the rotation and the scale in one place, with the gem
        /// elements as its children — the same thing <see cref="ModelTransform"/> does for a mesh,
        /// expressed the way a shape can carry it.</para>
        /// </summary>
        private static Shape BuildGemShape(ICoreClientAPI capi, CANSocketGem gem, ModelTransform pose, string attachTo,
            Shape entityShape, ItemStack stack, string texturePrefixCode)
        {
            IAsset asset = CANGemAssets.ShapeAsset(capi, gem.Size, gem.CuttingType);
            if (asset == null) return null;

            AssetLocation texture = CANGemAssets.ShapeTexture(capi, gem.GemType);
            if (texture == null) return null;

            Shape shape = asset.ToObject<Shape>();
            if (shape?.Elements == null || shape.Elements.Length == 0) return null;

            // The gem shapes name their one texture "gem"; saying so here is what makes the step
            // parenting hand it to the atlas.
            shape.Textures = new Dictionary<string, AssetLocation> { { "gem", texture } };

            FastVec3f placement = pose.Translation;

            // Only gear that is fitted onto the wearer has its pose written in the wearer's space:
            // that is the space its full body mesh is built in, which is what the menu tunes against.
            // Anything else - a weapon or a tool held by an armour stand - is step parented as its
            // own shape, and its pose was tuned on its own item mesh, which is exactly the space the
            // gem element already sits in. Converting that through a bone would move it off.
            ShapeElement bone = IsFittedToWearer(stack)
                ? entityShape?.GetElementByName(attachTo, System.StringComparison.InvariantCultureIgnoreCase)
                : null;
            if (bone != null)
            {
                float[] boneMatrix = WearerBoneMatrix(capi, stack, bone, texturePrefixCode) ?? ModelMatrix(bone);
                float[] inverse = Mat4f.Create();
                Mat4f.Invert(inverse, boneMatrix);
                Apply(inverse, ref placement);

                if (Verbose)
                {
                    // Round trip: taking the bone-space placement back into the wearer's space has
                    // to land on the pose again. Anything else means the bone matrix is wrong.
                    FastVec3f check = placement;
                    Apply(boneMatrix, ref check);
                    Say(capi, string.Format("pose {0} -> bone {1} -> back {2}",
                        Round(pose.Translation), Round(placement), Round(check)));
                }
            }
            else if (Verbose)
            {
                Say(capi, IsFittedToWearer(stack)
                    ? "bone '" + attachTo + "' not found in the wearer's shape, pose used as is"
                    : "not fitted to the wearer, pose used as is in the item's own space");
            }

            // One element, laid out to reproduce ModelTransform.AsMatrix exactly — that matrix is
            // what the same pose does to the gem mesh in hand and in the gui, so the two paths have
            // to agree down to the last step or the gem sits half a block off on the wearer.
            //
            // AsMatrix is  T(translation) . T(origin) . R . S . T(-origin)
            // an element is T(rotationOrigin) . R . S . T(from - rotationOrigin)
            // which match when rotationOrigin = translation + origin and from = translation.
            var holder = new ShapeElement
            {
                Name = "canjewelrygem",
                From = new double[]
                {
                    placement.X * PixelsPerBlock,
                    placement.Y * PixelsPerBlock,
                    placement.Z * PixelsPerBlock
                },
                RotationOrigin = new double[]
                {
                    (placement.X + pose.Origin.X) * PixelsPerBlock,
                    (placement.Y + pose.Origin.Y) * PixelsPerBlock,
                    (placement.Z + pose.Origin.Z) * PixelsPerBlock
                },
                RotationX = pose.Rotation.X,
                RotationY = pose.Rotation.Y,
                RotationZ = pose.Rotation.Z,
                ScaleX = pose.ScaleXYZ.X,
                ScaleY = pose.ScaleXYZ.Y,
                ScaleZ = pose.ScaleXYZ.Z,
                StepParentName = attachTo,
                Children = shape.Elements
            };
            holder.To = (double[])holder.From.Clone();

            shape.Elements = new[] { holder };

            // Fills in FacesResolved, which step parenting reads when it renames texture codes.
            shape.ResolveReferences(capi.Logger, "canjewelry gem");
            return shape;
        }
    }
}
