using canjewelry.src.api;
using canjewelry.src.CB;
using canjewelry.src.render;
using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.GameContent;

namespace canjewelry.src.commands
{
    /// <summary>
    /// The client side commands for working on gem visuals: where a pose comes from, what the built
    /// mesh looks like, and the switches that take one thing at a time out of the picture.
    ///
    /// <para>Client side because the gem pose registry is — the server never places a gem on a
    /// model. Strings are not translated on purpose: this is a tool for building the mod.</para>
    /// </summary>
    public static class CANGemVisualCommands
    {
        public static void Register(ICoreClientAPI capi)
        {
            var parsers = capi.ChatCommands.Parsers;

            // The one command here meant for players rather than for building the mod: gems on
            // models cost mesh building, and not everyone wants to pay for them.
            capi.ChatCommands.Create("cangemvisuals")
                .WithDesc("show or hide gems on item models for this client")
                .WithArgs(parsers.OptionalBool("on"))
                .HandleWith(ToggleGemVisuals);

            capi.ChatCommands.Create("cangemvisual")
                .WithDesc("show how the gem pose of the item in hand is resolved")
                .WithArgs(parsers.OptionalInt("socket"), parsers.OptionalWord("target"))
                .HandleWith(DumpGemVisualResolve);

            capi.ChatCommands.Create("cangemmesh")
                .WithDesc("print the mesh buffers of the item in hand, with and without its gems")
                .HandleWith(DumpGemMesh);

            capi.ChatCommands.Create("cangemworn")
                .WithDesc("say what happens to the gems of worn gear, and rebuild the player model")
                .HandleWith(ToggleWornVerbose);

            capi.ChatCommands.Create("cangemhide")
                .WithDesc("stop drawing gems on the held item, or on its whole group with 'group'")
                .WithArgs(parsers.OptionalWord("scope"))
                .HandleWith(ToggleHidden);

            capi.ChatCommands.Create("cangemprobe")
                .WithDesc("put the gems of the held item in an unmissable body pose, to find them on the wearer")
                .WithArgs(parsers.OptionalFloat("scale"), parsers.OptionalFloat("offset"))
                .HandleWith(ProbeBodyPose);

            capi.ChatCommands.Create("cangembaseonly")
                .WithDesc("toggle drawing the rebuilt item mesh without any gems, to isolate what breaks a model")
                .HandleWith(ToggleBaseOnly);
        }

        /// <summary>
        /// Shows or hides gems on models for this client, and remembers the choice. Without an
        /// argument it flips the current setting.
        ///
        /// <para>Every built model is dropped either way: turning them off has to take the gems off
        /// what is already on screen, and turning them back on has to put them there.</para>
        /// </summary>
        public static TextCommandResult ToggleGemVisuals(TextCommandCallingArgs args)
        {
            ICoreClientAPI capi = canjewelry.capi;
            bool show = (args[0] as bool?) ?? !CANGemVisibility.ShowGems(capi);
            CANGemVisibility.SetShowGems(capi, show);

            EncrustableCB.ClearMeshCache(capi);
            capi.World?.Player?.Entity?.MarkShapeModified();

            if (show && canjewelry.config?.enableGemVisuals == false)
            {
                return TextCommandResult.Success(
                    "[canjewelry] gems on models: on for you, but the server has them switched off");
            }
            return TextCommandResult.Success("[canjewelry] gems on models: " + (show ? "shown" : "hidden"));
        }

        /// <summary>The item being worked on, null when the hand is empty.</summary>
        private static ItemStack HeldItem()
        {
            return canjewelry.capi?.World?.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack;
        }

        /// <summary>
        /// Prints the resolution chain for the held item: which gemvisuals rules match, in which
        /// order, and which pose finally answers.
        /// </summary>
        public static TextCommandResult DumpGemVisualResolve(TextCommandCallingArgs args)
        {
            ItemStack stack = HeldItem();
            if (stack == null) return TextCommandResult.Error("Hold the item in the active hotbar slot");

            int socketIndex = (args[0] as int?) ?? 0;
            string target = args[1] as string ?? CANGemVisualTarget.Hand;

            return TextCommandResult.Success(CANGemVisualRegistry.DumpResolve(stack, socketIndex, target));
        }

        /// <summary>
        /// Prints the buffer counts of the item's own mesh and of the one built with its gems.
        /// A model that renders in pieces means two of these counts disagree, and this says which.
        /// </summary>
        public static TextCommandResult DumpGemMesh(TextCommandCallingArgs args)
        {
            ICoreClientAPI capi = canjewelry.capi;
            ItemStack stack = HeldItem();
            if (stack == null) return TextCommandResult.Error("Hold the item in the active hotbar slot");

            var gems = CANGemMeshBuilder.CollectGems(stack);
            MeshData baseMesh = CANGemMeshBuilder.BuildBase(capi, stack);
            MeshData composite = CANGemMeshBuilder.BuildComposite(capi, stack, CANGemVisualTarget.Gui, gems);

            var sb = new StringBuilder();
            sb.Append(stack.Collectible.Code.ToShortString()).Append(", gems: ").Append(gems.Count).AppendLine();
            sb.Append("wearable: ")
              .Append(stack.Collectible
                  .GetCollectibleBehavior<CollectibleBehaviorWearableAttachment>(withInheritance: true) != null)
              .Append(", attachable: ")
              .Append(IAttachableToEntity.FromCollectible(stack.Collectible) != null)
              .AppendLine();
            DescribeMesh(sb, "base", baseMesh);
            DescribeMesh(sb, "with gems", composite);
            return TextCommandResult.Success(sb.ToString());
        }

        private static void DescribeMesh(StringBuilder sb, string name, MeshData mesh)
        {
            if (mesh == null)
            {
                sb.Append(name).AppendLine(": null");
                return;
            }

            sb.Append(name)
              .Append(": verts ").Append(mesh.VerticesCount)
              .Append(", indices ").Append(mesh.IndicesCount)
              .Append(", faces ").Append(mesh.VerticesCount / MeshData.StandardVerticesPerFace)
              .Append(", texIndices ").Append(mesh.TextureIndicesCount)
              .Append(", texIds ").Append(mesh.TextureIds == null ? 0 : mesh.TextureIds.Length)
              .Append(", renderPasses ").Append(mesh.RenderPassCount)
              .Append(", colorMaps ").Append(mesh.ColorMapIdsCount)
              .Append(", xyzFaces ").Append(mesh.XyzFacesCount)
              .Append(", jointIds ").Append(mesh.CustomInts == null ? -1 : mesh.CustomInts.Count)
              .Append(", flags ").Append(mesh.Flags == null ? -1 : mesh.Flags.Length)
              .AppendLine();
        }

        /// <summary>
        /// Reports what the worn gear path does with the gems, and rebuilds the player's shape so
        /// the report arrives without having to take the armour off and put it back on.
        /// </summary>
        public static TextCommandResult ToggleWornVerbose(TextCommandCallingArgs args)
        {
            CANGemEntityShape.Verbose = !CANGemEntityShape.Verbose;
            canjewelry.capi?.World?.Player?.Entity?.MarkShapeModified();
            return TextCommandResult.Success("[canjewelry] worn gem log: " + CANGemEntityShape.Verbose
                                             + " (the model was rebuilt; the steps are in client-main.log, "
                                             + "the tally per piece is in the chat)");
        }

        /// <summary>
        /// Turns gem meshes off while still replacing the model with our own rebuild of the item.
        /// A model that still comes out wrong is broken by the rebuild, not by the gems.
        /// </summary>
        public static TextCommandResult ToggleBaseOnly(TextCommandCallingArgs args)
        {
            CANGemMeshBuilder.DebugBaseOnly = !CANGemMeshBuilder.DebugBaseOnly;
            EncrustableCB.ClearMeshCache(canjewelry.capi);
            return TextCommandResult.Success("[canjewelry] base only: " + CANGemMeshBuilder.DebugBaseOnly);
        }

        /// <summary>
        /// Switches the gems of the held item — or of its whole group — off and on again, so one
        /// family can be worked on while the rest of the game stays clean. Export writes it into the
        /// rule as "hidden", which is what makes it outlast the session.
        /// </summary>
        public static TextCommandResult ToggleHidden(TextCommandCallingArgs args)
        {
            ICoreClientAPI capi = canjewelry.capi;
            ItemStack stack = HeldItem();
            if (stack?.Collectible?.Code == null) return TextCommandResult.Error("Hold the item in the active hotbar slot");

            bool wholeGroup = string.Equals(args[0] as string, "group", StringComparison.InvariantCultureIgnoreCase);
            string subject = stack.Collectible.Code.ToShortString();
            if (wholeGroup)
            {
                string group = CANGemVisualRegistry.FindGroup(stack.Collectible.Code);
                if (group == null) return TextCommandResult.Error("This item belongs to no item group");
                subject = "$" + group;
            }

            bool hidden = !(CANGemVisualRegistry.GetHiddenOverride(subject) ?? false);
            CANGemVisualRegistry.SetHiddenOverride(subject, hidden);
            EncrustableCB.ClearMeshCache(capi);
            capi.World.Player.Entity.MarkShapeModified();

            return TextCommandResult.Success(string.Format("[canjewelry] gems on {0}: {1}",
                subject, hidden ? "hidden" : "shown"));
        }

        /// <summary>
        /// Writes a deliberately loud body pose for every socket of the held item: oversized and
        /// pushed well clear of the bone it hangs off. A gem that stays invisible after this is not
        /// merely hidden inside the model, and the problem is elsewhere.
        /// </summary>
        public static TextCommandResult ProbeBodyPose(TextCommandCallingArgs args)
        {
            ICoreClientAPI capi = canjewelry.capi;
            ItemStack stack = HeldItem();
            if (stack?.Collectible?.Code == null) return TextCommandResult.Error("Hold the item in the active hotbar slot");

            // An omitted optional float arrives as 0, not as null, and a scale of zero would shrink
            // the gem to nothing - exactly what this command is meant to rule out.
            float scale = (args[0] as float?) ?? 0f;
            if (scale <= 0) scale = 4f;
            float offset = (args[1] as float?) ?? 0f;
            if (offset == 0) offset = 0.5f;

            string subject = stack.Collectible.Code.ToShortString();
            int sockets = Math.Max(1, EncrustableCB.GetMaxAmountSockets(stack));
            for (int i = 0; i < sockets; i++)
            {
                var pose = ModelTransform.NoTransform.Clone();
                pose.Translation.Set(offset, offset, offset);
                pose.ScaleXYZ.Set(scale, scale, scale);
                // Scratch: this pose is a diagnostic, not somebody's work, and closing the debug
                // menu must not write it into a config file.
                CANGemVisualRegistry.SetOverride(subject, CANGemVisualTarget.Body, i, CANGemVisualSide.Front, pose,
                    scratch: true);
            }

            capi.World.Player.Entity.MarkShapeModified();
            return TextCommandResult.Success(string.Format(
                "[canjewelry] body pose of {0}: offset {1}, scale {2} on {3} socket(s). Reopen the debug menu to tune it.",
                subject, offset, scale, sockets));
        }
    }
}
