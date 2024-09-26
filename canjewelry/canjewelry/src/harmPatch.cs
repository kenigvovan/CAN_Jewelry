using Cairo;
using canjewelry.src.be;
using canjewelry.src.cb;
using canjewelry.src.CB;
using canjewelry.src.eb;
using canjewelry.src.items;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace canjewelry.src
{
    [HarmonyPatch]
    public class harmPatch
    {

        /***
          
         Comment were mostly written after some time after code was added, so at some points I had to guess what I meant before or rewrite some too hideous parts.
         Enter at your own risk.
         
         ***/

        /*
          
         Add or remove buff on player, buff type and value are taken from tree attribute and applied on ep.
          
         */
        public static void applyBuffFromItemStack(ITreeAttribute socketSlot, EntityPlayer ep, bool add)
        {
            if (!socketSlot.HasAttribute(CANJWConstants.GEM_ATTRIBUTE_BUFF))
            {
                return;
            }
            else
            {
                if (socketSlot.HasAttribute(CANJWConstants.GEM_BUFF_TYPE) && (EnumGemBuffType)socketSlot.GetInt(CANJWConstants.GEM_BUFF_TYPE) != EnumGemBuffType.STATS_BUFF)
                {
                    return;
                }
            }
            float additionalValue = socketSlot.GetFloat("attributeBuffValue");
            string attributeBuffName = socketSlot.GetString("attributeBuff");
            float blendedStatValue = ep.Stats[attributeBuffName].GetBlended();
            canjewelry.config.max_buff_values.TryGetValue(attributeBuffName, out float buffThreshold);

            if (!ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrusted"))
            {
                ep.Stats.Set(attributeBuffName, "canencrusted", 0, true);
            }

            if (add)
            {
                //overflow already present just add to neg part and standard part
                if(ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrustedneg"))
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue, true);
                    if (additionalValue > 0)
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", (ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value) - additionalValue, true);
                    }
                    else
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", (ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value) + additionalValue, true);
                    }
                    //ep.Stats.Set(attributeBuffName, "canencrustedneg", ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value + additionalValue, true);
                }
                //no neg part, add additional and add neg difference
                else if (buffThreshold != 0 && additionalValue > 0 ?  Math.Abs(ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue) > buffThreshold : (ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue) < buffThreshold)
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue, true);
                    if (additionalValue > 0)
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", -((ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value) - buffThreshold), true);
                    }
                    else
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", -(((ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value) - buffThreshold)), true);
                    }
                }
                //no neg part and under threshold
                else
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value + additionalValue, true);
                }
            }
            else
            {
                //overflow already present
                if (ep.Stats[attributeBuffName].ValuesByKey.ContainsKey("canencrustedneg"))
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value - additionalValue, true);
                    if (additionalValue > 0 ? ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value - additionalValue <= 0
                                            : ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value + additionalValue <= 0)
                    {
                        ep.Stats[attributeBuffName].Remove("canencrustedneg");
                    }
                    else
                    {
                        ep.Stats.Set(attributeBuffName, "canencrustedneg", ep.Stats[attributeBuffName].ValuesByKey["canencrustedneg"].Value - additionalValue, true);
                    }
                }
                else
                {
                    ep.Stats.Set(attributeBuffName, "canencrusted", ep.Stats[attributeBuffName].ValuesByKey["canencrusted"].Value - additionalValue, true);
                }
            }

            //ep.Stats[attributeBuffName].Remove("canencrustedneg");

            //canjewelry.sapi.SendMessage(ep.Player, 0, add.ToString() + ep.Stats[attributeBuffName].GetBlended().ToString() + attributeBuffName, EnumChatType.Notification);
        }

        //Active slot item can have canecrusted attribute and buffs player, so we need to know when he change holding item

        /*
        
         Catch active slot changed, also it catches moment when active change and we also flip slots. At that moment try_flip patch called as well
        
         */
        public static void Postfix_TriggerAfterActiveSlotChanged(Vintagestory.Server.CoreServerEventManager __instance, IServerPlayer player,
            int fromSlot,
            int toSlot)
        {
            if(player.Entity.HasBehavior<CANGemBuffAffected>())
            {
                player.Entity.GetBehavior<CANGemBuffAffected>().OnActiveSlotSwapped(player, fromSlot, toSlot);
            }
        }
        /*
        
         Used in transplier Transpiler_ComposeSlotOverlays_Add_Socket_Overlays_Not_Draw_ItemDamage for GuiElementItemSlotGridBase.ComposeSlotOverlays()
         to draw color rhombus
        
         */
        public static void addSocketsOverlaysNotDrawItemDamage(ElementBounds[] slotBounds, int slotIndex, ItemSlot slot, LoadedTexture[] slotQuantityTextures, ImageSurface textSurface, Context context)
        {
            var unsSlotSize = GuiElementPassiveItemSlot.unscaledSlotSize;
            if(textSurface == null)
            {
                textSurface = new ImageSurface(0, (int)slotBounds[slotIndex].InnerWidth, (int)slotBounds[slotIndex].InnerHeight);
                context = new Context(textSurface);
            }
            
            ITreeAttribute encrustTree = slot.Itemstack.Attributes.GetTreeAttribute("canencrusted");
            if (encrustTree == null)
            {
                return;
            }

            for (int i = 0; i < EncrustableCB.GetMaxAmountSockets(slot.Itemstack); i++)
            {
                
                ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + i.ToString());
                if (socketSlot == null || socketSlot.GetString("gemtype").Equals(""))
                {
                    continue;
                }

                if(!canjewelry.gems_textures.TryGetValue(socketSlot.GetString("gemtype"), out string assetPath))
                {
                    continue;
                }
                AssetLocation asset = canjewelry.capi.Assets.TryGet(assetPath + ".png")?.Location;
                /*AssetLocation asset = canjewelry.capi.Assets.TryGet("game:textures/block/stone/gem/" + socketSlot.GetString("gemtype") + ".png")?.Location;*/
                /*if (canjewelry.capi.Assets.TryGet("game:textures/block/stone/gem/" + socketSlot.GetString("gemtype") + ".png") == null)
                {
                    asset = canjewelry.capi.Assets.TryGet("game:textures/item/resource/ungraded/" + socketSlot.GetString("gemtype") + ".png")?.Location;
                }*/



                if (asset == null) { continue; }
                var socketSurface = GuiElement.getImageSurfaceFromAsset(canjewelry.capi, asset, 255);

                double tr = unsSlotSize / 4;
                
                context.NewPath();
                
                context.LineTo((int)GuiElement.scaled(0), (int)GuiElement.scaled(unsSlotSize / 8) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 8), (int)GuiElement.scaled(0) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 4), (int)GuiElement.scaled(unsSlotSize/ 8) + i * (int)GuiElement.scaled(tr));
                context.LineTo((int)GuiElement.scaled(unsSlotSize / 8), (int)GuiElement.scaled(unsSlotSize / 4) + i * (int)GuiElement.scaled(tr));
               
                context.ClosePath();
                
                context.SetSourceSurface(socketSurface, 0, (int)tr * i);

                context.FillPreserve();
                socketSurface.Dispose();
            }
            canjewelry.capi.Gui.LoadOrUpdateCairoTexture(textSurface, true, ref slotQuantityTextures[slotIndex]);
            context.Dispose();
            textSurface.Dispose();
            return;
        }
       
        public static MethodInfo GetItemStackFromItemSlot = typeof(ItemSlot).GetMethod("get_Itemstack");
        public static MethodInfo GetAttributesFromItemStack = typeof(ItemStack).GetMethod("get_Attributes");
        public static MethodInfo HasAttributeITreeAttribute = typeof(ITreeAttribute).GetMethod("HasAttribute");

        public static FieldInfo ElementBoundsSlotGrid = typeof(GuiElementItemSlotGridBase).GetField("slotBounds", BindingFlags.NonPublic | BindingFlags.Instance);
        
        public static FieldInfo slotQuantityTexturesSlotGrid = typeof(GuiElementItemSlotGridBase).GetField("slotQuantityTextures", BindingFlags.NonPublic | BindingFlags.Instance);
        
        public static IEnumerable<CodeInstruction> Transpiler_ComposeSlotOverlays_Add_Socket_Overlays_Not_Draw_ItemDamage(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            bool found = false;
            bool foundSec = false;
            var codes = new List<CodeInstruction>(instructions);
            var proxyMethod = AccessTools.Method(typeof(harmPatch), "addSocketsOverlaysNotDrawItemDamage");
            Label returnLabelNoAttribute = il.DefineLabel();
            Label returnLabelNoAttribute2 = il.DefineLabel();
            for (int i = 0; i < codes.Count; i++)
            {
 
                if (!found && 
                        codes[i].opcode == OpCodes.Ldc_I4_1 && codes[i + 1].opcode == OpCodes.Ret && codes[i + 2].opcode == OpCodes.Ldc_I4_0 && codes[i - 1].opcode == OpCodes.Stelem_Ref)
                    {
                        yield return new CodeInstruction(OpCodes.Ldarg_1);
                        yield return new CodeInstruction(OpCodes.Callvirt, GetItemStackFromItemSlot);
                        yield return new CodeInstruction(OpCodes.Callvirt, GetAttributesFromItemStack);
                        yield return new CodeInstruction(OpCodes.Ldstr, "canencrusted");
                        yield return new CodeInstruction(OpCodes.Callvirt, HasAttributeITreeAttribute);
                        yield return new CodeInstruction(OpCodes.Brfalse_S, returnLabelNoAttribute);

                   
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldfld, ElementBoundsSlotGrid);
                        yield return new CodeInstruction(OpCodes.Ldarg_3);
                        yield return new CodeInstruction(OpCodes.Ldarg_1);
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Ldfld, slotQuantityTexturesSlotGrid);
                        yield return new CodeInstruction(OpCodes.Ldloc_1);
                        yield return new CodeInstruction(OpCodes.Ldloc_2);
                        yield return new CodeInstruction(OpCodes.Call, proxyMethod);
                        yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                        yield return new CodeInstruction(OpCodes.Ret);
                        codes[i].labels.Add(returnLabelNoAttribute);
                        found = true;
                    }

                if (!foundSec &&
                        codes[i].opcode == OpCodes.Ldloc_2 && codes[i + 1].opcode == OpCodes.Callvirt && codes[i + 2].opcode == OpCodes.Ldloc_1 && codes[i - 1].opcode == OpCodes.Call)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Callvirt, GetItemStackFromItemSlot);
                    yield return new CodeInstruction(OpCodes.Callvirt, GetAttributesFromItemStack);
                    yield return new CodeInstruction(OpCodes.Ldstr, "canencrusted");
                    yield return new CodeInstruction(OpCodes.Callvirt, HasAttributeITreeAttribute);
                    yield return new CodeInstruction(OpCodes.Brfalse_S, returnLabelNoAttribute2);

                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, ElementBoundsSlotGrid);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldfld, slotQuantityTexturesSlotGrid);
                    yield return new CodeInstruction(OpCodes.Ldloc_1);
                    yield return new CodeInstruction(OpCodes.Ldloc_2);
                    yield return new CodeInstruction(OpCodes.Call, proxyMethod);
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    yield return new CodeInstruction(OpCodes.Ret);
                    codes[i].labels.Add(returnLabelNoAttribute2);
                    foundSec = true;
                }
                yield return codes[i];
            }
        }

        public static void Postfix_GetHeldItemInfo(Vintagestory.API.Common.CollectibleObject __instance, ItemSlot inSlot,
        StringBuilder dsc,
        IWorldAccessor world,
        bool withDebugInfo)
        {
            ItemStack itemstack = inSlot.Itemstack;
            if (itemstack.Attributes.HasAttribute("canencrusted"))
            {             
                ITreeAttribute tree = itemstack.Attributes.GetTreeAttribute("canencrusted");
                int maxSocketsNumber = EncrustableCB.GetMaxAmountSockets(itemstack);
                int canHaveNsocketsMore = maxSocketsNumber - tree.GetInt("socketsnumber");
                if (canHaveNsocketsMore > 0)
                {
                    dsc.Append(Lang.Get("canjewelry:item-can-have-n-sockets", canHaveNsocketsMore)).Append("\n");
                }
                
                for (int i = 0; i < maxSocketsNumber; i++)
                {
                    var treeSlot = tree.GetTreeAttribute("slot" + i);
                    if(treeSlot == null)
                    {
                        continue;
                    } 
                    dsc.Append(Lang.Get("canjewelry:item-socket-tier", treeSlot.GetAsInt("sockettype")));
                    dsc.Append("\n");
                    if(treeSlot.GetString("gemtype") != "")
                    {
                        if (treeSlot.HasAttribute("attributeBuff"))
                        {


                            if (treeSlot.GetString("attributeBuff").Equals("maxhealthExtraPoints"))
                            {
                                dsc.Append(Lang.Get("canjewelry:socket-has-attribute", i, treeSlot.GetFloat("attributeBuffValue"))).Append(Lang.Get("canjewelry:buff-name-" + treeSlot.GetString("attributeBuff")));
                            }
                            else
                            {
                                dsc.Append(Lang.Get("canjewelry:socket-has-attribute-percent", i, treeSlot.GetFloat("attributeBuffValue") * 100)).Append(Lang.Get("canjewelry:buff-name-" + treeSlot.GetString("attributeBuff")));
                            }                        
                            dsc.Append('\n');
                        }
                        else
                        {
                            string[] buffNames = (treeSlot[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value;
                            float[] buffValues = (treeSlot[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value;

                            for (int j = 0; j < buffNames.Length; j++)
                            {
                                if (buffNames[j].Equals("maxhealthExtraPoints"))
                                {
                                    dsc.Append(Lang.Get("canjewelry:buff-name-" + buffNames[j])).Append(" +" + buffValues[j].ToString());
                                }
                                else
                                {
                                    if (canjewelry.config.gems_buffs.TryGetValue(buffNames[j], out var buffValuesDict))
                                    {
                                        dsc.Append(Lang.Get("canjewelry:buff-name-" + buffNames[j]));
                                        dsc.Append(buffValues[j] * 100 > 0 ? " +" + Math.Round(buffValues[j] * 100, 3) + "%" : " " + Math.Round(buffValues[j] * 100, 3) + "%");
                                        dsc.AppendLine();
                                    }
                                }
                            }
                        }
                    }
                }

            }
            //if item has cutom variant handling or just normal canhavenumbersocket parameter, just print it in item's info
            else if(itemstack.ItemAttributes != null && 
                (itemstack.ItemAttributes.KeyExists(CANJWConstants.CAN_CUSTOM_VARIANTS) ||
                itemstack.ItemAttributes.KeyExists("canhavesocketsnumber")))
            {
                int maxSocketsNumber = EncrustableCB.GetMaxAmountSockets(itemstack);
                if (maxSocketsNumber > 0)
                {
                    dsc.AppendLine(Lang.Get("canjewelry:item-can-have-n-sockets", maxSocketsNumber));
                }
            }
        }

        //Prefix_GetDrops
        public static bool Prefix_GetDrops(Vintagestory.API.Common.Block __instance, IWorldAccessor world, BlockPos pos, IPlayer byPlayer, ref ItemStack[] __result, float dropQuantityMultiplier = 1f)
        {
            var f = 3;

            bool flag = false;
            List<ItemStack> list = new List<ItemStack>();
            BlockBehavior[] blockBehaviors = __instance.BlockBehaviors;
            foreach (BlockBehavior obj in blockBehaviors)
            {
                EnumHandling handling = EnumHandling.PassThrough;
                ItemStack[] drops = obj.GetDrops(world, pos, byPlayer, ref dropQuantityMultiplier, ref handling);
                if (drops != null)
                {
                    list.AddRange(drops);
                }

                switch (handling)
                {
                    case EnumHandling.PreventSubsequent:
                        return false;
                    case EnumHandling.PreventDefault:
                        flag = true;
                        break;
                }
            }

            if (flag)
            {
                return false;
            }

            if (__instance.Drops == null)
            {
                return false;
            }

            List<ItemStack> list2 = new List<ItemStack>();
            for (int j = 0; j < __instance.Drops.Length; j++)
            {
                BlockDropItemStack blockDropItemStack = __instance.Drops[j];
                if (blockDropItemStack.Tool.HasValue && (byPlayer == null || blockDropItemStack.Tool != byPlayer.InventoryManager.ActiveTool))
                {
                    continue;
                }

                float num = 1f;
                if (blockDropItemStack.DropModbyStat != null)
                {
                    num = byPlayer.Entity.Stats.GetBlended(blockDropItemStack.DropModbyStat);
                }

                ItemStack itemStack = __instance.Drops[j].GetNextItemStack(dropQuantityMultiplier * num);
                if (itemStack != null)
                {
                    if (itemStack.Collectible is IResolvableCollectible resolvableCollectible)
                    {
                        DummySlot dummySlot = new DummySlot(itemStack);
                        resolvableCollectible.Resolve(dummySlot, world);
                        itemStack = dummySlot.Itemstack;
                    }

                    list2.Add(itemStack);
                    if (__instance.Drops[j].LastDrop)
                    {
                        break;
                    }
                }
            }

            list2.AddRange(list);
            __result = list2.ToArray();
            return false;
        }

        public static void Postfix_CollectibleObject_GetMaxDurability(ref int __result, ItemStack itemstack)
        {
            if(itemstack != null)
            {
                ITreeAttribute tree = itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                if(tree == null)
                {
                    return;
                }

                float valueOrDefault = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                if (valueOrDefault > 0f && __result > 1)
                {
                    __result = (int)((float)__result * (1f + valueOrDefault));
                }
            }
        }
        public static void TryDropGems(Entity byEntity, ItemSlot itemslot)
        {
            if (canjewelry.capi != null)
            {
                return;
            }
            /*if(byEntity.Api.Side == EnumAppSide.Client)
            {
                return;
            }*/
            if (itemslot.Itemstack != null && itemslot.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
            {
                Random r = new Random();
                var tree = itemslot.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                for (int i = 0; i < EncrustableCB.GetMaxAmountSockets(itemslot.Itemstack); i++)
                {
                    if(canjewelry.config.chance_gem_drop_on_item_broken == 0 || r.NextDouble() > canjewelry.config.chance_gem_drop_on_item_broken)
                    {
                        return;
                    }
                    ITreeAttribute socketSlot = tree.GetTreeAttribute("slot" + i.ToString());
                    if (socketSlot != null)
                    {
                        int size = socketSlot.GetInt("size");
                        string gemType = socketSlot.GetString("gemtype");
                        string gemSize;
                        switch (size)
                        {
                            case 1:
                                gemSize = "normal";
                                break;
                            case 2:
                                gemSize = "flawless";
                                break;
                            case 3:
                                gemSize = "exquisite";
                                break;
                            default:
                                return;
                        }

                        Item currentItem = canjewelry.sapi.World.GetItem(new AssetLocation("canjewelry:" + "gem-cut-" + gemSize + "-" + gemType));
                        ItemStack newIS = new ItemStack(currentItem, 1);
                        canjewelry.sapi.World.SpawnItemEntity(newIS, byEntity.Pos.XYZ.Clone().Add(0.5f, 0.25f, 0.5f));
                    }
                }
            }              
        }
        public static IEnumerable<CodeInstruction> Transpiler_CollectibleObject_DamageItem(IEnumerable<CodeInstruction> instructions, ILGenerator il)
        {
            bool found = false;
            var codes = new List<CodeInstruction>(instructions);
            var proxyMethod = AccessTools.Method(typeof(harmPatch), "TryDropGems");
            for (int i = 0; i < codes.Count; i++)
            {

                if (!found &&
                        codes[i].opcode == OpCodes.Ldarg_3 && codes[i + 1].opcode == OpCodes.Ldnull && codes[i + 2].opcode == OpCodes.Callvirt && codes[i - 1].opcode == OpCodes.Bgt)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Call, proxyMethod);
                    found = true;
                }               
                yield return codes[i];
            }
        }

        public static void Postfix_CharacterSystem_StartClientSide(CharacterSystem __instance, ICoreClientAPI api, GuiDialogCharacterBase ___charDlg)
        {
            ___charDlg.Tabs.Add(new GuiTab()
            {
                Name = Lang.Get("canjewelry:stats-tab-name"),
                DataInt = 2
            });
            ___charDlg.RenderTabHandlers.Add(new Action<GuiComposer>(composeProgressTab));
        }
        public static void Postfix_ItemChisel_OnHeldAttackStart(ItemChisel __instance, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            if (blockSel != null)
            {
                BlockEntity be = byEntity.World.BlockAccessor.GetBlockEntity(blockSel.Position);
                BlockEntityGemCuttingTable bea = be as BlockEntityGemCuttingTable;
                if (bea == null)
                {
                    return;
                }
                if (byEntity.World.Side == EnumAppSide.Client)
                {
                    bea.OnUseOver((byEntity as EntityPlayer).Player, blockSel.SelectionBoxIndex);
                    handling = EnumHandHandling.PreventDefault;
                }
            }
        }
        public static int lineCounter = 0;
        public static StringBuilder BuildText()
        {
            StringBuilder sb = new StringBuilder();

            var player = canjewelry.capi.World.Player;
            var playerAttributes = player.Entity.WatchedAttributes;
            lineCounter = 0;
            if (playerAttributes.Keys.Contains("stats"))
            {
                var stats = playerAttributes.GetTreeAttribute("stats");
                foreach(var it in canjewelry.config.buffs_to_show_gui)
                {
                    if(stats.HasAttribute(it))
                    {
                        lineCounter++;
                        sb.AppendLine(Lang.Get("canjewelry:buff-name-" + it) + " " + player.Entity.Stats[it].GetBlended());
                    }
                    
                }
            }

            return sb;
        }
        private static void composeProgressTab(GuiComposer compo)
        {
            var mainBounds = ElementBounds.Fixed(0.0, 35.0, 355.0, 250);
            var textBounds = mainBounds.FlatCopy();
            mainBounds.Alignment = EnumDialogArea.LeftTop;
            ElementBounds scrollbarBounds = textBounds.CopyOffsetedSibling(textBounds.fixedWidth + 7, -32).WithFixedWidth(20);
            compo.AddInset(textBounds);
            //compo.AddRichtext("hello", CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15).WithFontSize(16), mainBounds);
            compo.BeginChildElements(mainBounds)
                .BeginClip(textBounds);

            var sb = BuildText();
            compo.AddRichtext(sb.ToString(), CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15).WithFontSize(16), ElementBounds.Fixed(0.0, 35.0, 350.0, 250), "credits")
            //.AddRichtext("hello", CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15).WithFontSize(16), ElementBounds.Fixed(0.0, 25.0 + 10, 100.0, 50))
            .EndClip()
            .AddVerticalScrollbar(new Action<float>(delegate (float value)
            {
                ElementBounds bounds = compo.GetRichtext("credits").Bounds;
                bounds.fixedY = (double)(10f - value);
                bounds.CalcWorldBounds();
            }), scrollbarBounds, "scrollbar")
            .EndChildElements();
            TextExtents textExtents = CairoFont.WhiteDetailText().GetTextExtents("hello\n1\ndffd\ns\nhello\n1\ndffd\ns\nhello\n1\ndffd\ns\nhello\n1\ndffd\ns\nhello\n1\ndffd\ns\nhello\n1\ndffd\ns\n");
            //currentBounds.fixedWidth = textExtents.Width;
            compo.GetScrollbar("scrollbar").SetHeights((float)textBounds.fixedHeight, (float)lineCounter * 25);
            //scrollbarBounds.fixedY = 10;
            //scrollbarBounds.CalcWorldBounds();
           //textBounds.absFixedY = -40;
            //c.WithChild(b);
            //c.fixedHeight
            //  compo.AddInset(c);
            //compo.AddRichtext("hello", CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15).WithFontSize(16), b);

            // compo.AddStaticText("hel", CairoFont.WhiteMediumText(), c.BelowCopy());

            return;
           /* var mainBounds = ElementBounds.Fixed(0.0, 25.0, 385.0, 200.0);
            var pageBounds = mainBounds.BelowCopy().WithFixedSize(200, 100);
            ElementBounds clippingBounds = pageBounds.ForkBoundingParent();
            var f = pageBounds.renderX;
            //ElementBounds scrollbarBounds = pageBounds.CopyOffsetedSibling(pageBounds.fixedWidth + 7).WithFixedWidth(20);

            //compo.BeginChildElements(pageBounds)
                        //.BeginClip(clippingBounds)
                        compo.AddRichtext("hello", CairoFont.WhiteDetailText().WithLineHeightMultiplier(1.15).WithFontSize(16), pageBounds)
                        //.EndClip()
                       /* .AddVerticalScrollbar(new Action<float> (delegate (float value)
                        {

                            ElementBounds bounds = compo.GetRichtext("credits").Bounds;
                            bounds.fixedY = (double)(10f - value);
                            bounds.CalcWorldBounds();
                        } ),
                        scrollbarBounds, "scrollbar")*/
                    //.AddSmallButton("Close", OnButtonClose, closeButtonBounds)
                    //.EndChildElements()

                    //.Compose();*/

            
        }
    }
}
