using canjewelry.src.api;
using canjewelry.src.cb;
using canjewelry.src.inventories;
using canjewelry.src.items;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
using static canjewelry.src.Config;

namespace canjewelry.src.CB
{
    public class EncrustableCB : CollectibleBehavior
    {
        public EncrustableCB(CollectibleObject collObj) : base(collObj)
        {

        }
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        }

        // Inscriptions are user-supplied free text on jewelry. Allowed chars: letters (Latin /
        // Cyrillic), digits, spaces, and a small set of punctuation. The filter is conservative
        // to keep markup-injection out of tooltips; reject anything that doesn't survive it.
        private static readonly System.Text.RegularExpressions.Regex InscriptionAllowed =
            new(@"^[A-Za-z0-9А-Яа-яЁё \.\,\!\?\'\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

        public static string SanitizeInscription(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string trimmed = input.Trim();
            if (trimmed.Length == 0 || trimmed.Length > CANJWConstants.INSCRIPTION_MAX_LEN) return null;
            if (!InscriptionAllowed.IsMatch(trimmed)) return null;
            return trimmed;
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            string inscription = inSlot.Itemstack?.Attributes?.GetString(CANJWConstants.INSCRIPTION);
            if (!string.IsNullOrEmpty(inscription))
            {
                dsc.AppendLine();
                dsc.Append('"').Append(inscription).Append('"');
                dsc.AppendLine();
            }
        }
        public static bool TryAddSocket(InventoryBase inventory, ItemSlot encrustable, ItemSlot socketSlot, int socketNumber, IPlayer player = null)
        {
            inventory.TakeLocked = true;
            // ItemStack encrustable = encrustableSlot.Itemstack;
            int maxSocketNumber = EncrustableCB.GetMaxAmountSockets(encrustable.Itemstack);
            if (encrustable.Itemstack != null && maxSocketNumber > 0)
            {
                //client can send us anything
                if (socketNumber + 1 > maxSocketNumber)
                {
                    inventory.TakeLocked = false;
                    return false;
                }

                //already has itree -> has socket alteast 1
                if (encrustable.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
                {
                    var tree = encrustable.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                    if (tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER) >= maxSocketNumber)
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }
                    else
                    {
                        //if socket is already there, just skip
                        if (tree.HasAttribute("slot" + socketNumber))
                        {
                            inventory.TakeLocked = false;
                            return false;
                        }


                        if (!(socketSlot.Itemstack != null && socketSlot.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.LEVEL_OF_SOSCKET_STRING)))
                        {
                            inventory.TakeLocked = false;
                            return false;
                        }

                        if (encrustable.Itemstack != null && encrustable.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.SOCKETS_TIERS_STRING))
                        {
                            var tiersList = encrustable.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray();
                            if (tiersList.Count() < tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER))
                            {
                                inventory.TakeLocked = false;
                                return false;
                            }

                            if (tiersList[socketNumber].AsInt() < socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt())
                            {
                                inventory.TakeLocked = false;
                                return false;
                            }
                        }

                        int[] socketsTiersList = GetSocketsTiers(encrustable.Itemstack);

                        if (socketsTiersList == null || socketNumber >= socketsTiersList.Length || socketsTiersList[socketNumber] < socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt())
                        {
                            inventory.TakeLocked = false;
                            return false;
                        }

                        tree.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER) + 1);
                        ITreeAttribute socketSlotTree = new TreeAttribute();
                        socketSlotTree.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, 0);
                        socketSlotTree.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");
                        socketSlotTree.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt());
                        socketSlotTree.SetString(CANJWConstants.SOCKET_ITEM_CODE, socketSlot.Itemstack.Collectible.Code.ToString());
                        socketSlot.TakeOut(1);
                        socketSlot.MarkDirty();
                        encrustable.MarkDirty();
                        tree["slot" + socketNumber] = socketSlotTree;
                        inventory.TakeLocked = false;
                        // Xp = 0 here — companion mods own the XP economy and read their own
                        // config-driven reward in OnXpAwarded. Action string is the contract.
                        canjewelry.Instance?.FireXp(new src.integration.CANXpEvent {
                            Player = player, Action = "addSocket", Xp = 0f
                        });
                        return true;
                    }
                }
                else
                {
                    if (maxSocketNumber < 1)
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }
                    if (!(socketSlot.Itemstack != null && socketSlot.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.LEVEL_OF_SOSCKET_STRING)))
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }

                    if (encrustable.Itemstack != null && encrustable.Itemstack.Collectible.Attributes.KeyExists(CANJWConstants.SOCKETS_TIERS_STRING))
                    {
                        var tiersList = encrustable.Itemstack.Collectible.Attributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray();


                        if (tiersList[socketNumber].AsInt() < socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt())
                        {
                            inventory.TakeLocked = false;
                            return false;
                        }
                    }

                    int[] socketsTiersList = GetSocketsTiers(encrustable.Itemstack);

                    if (socketsTiersList == null || socketNumber >= socketsTiersList.Length || socketsTiersList[socketNumber] < socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt())
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }

                    ITreeAttribute socketSlotTree = new TreeAttribute();

                    socketSlotTree.SetInt(CANJWConstants.ENCRUSTED_GEM_SIZE, 0);
                    socketSlotTree.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");
                    socketSlotTree.SetInt(CANJWConstants.ADDED_SOCKET_TYPE, socketSlot.Itemstack.Collectible.Attributes[CANJWConstants.LEVEL_OF_SOSCKET_STRING].AsInt());
                    socketSlotTree.SetString(CANJWConstants.SOCKET_ITEM_CODE, socketSlot.Itemstack.Collectible.Code.ToString());

                    ITreeAttribute socketEncrusted = new TreeAttribute();
                    socketEncrusted.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, 1);
                    socketEncrusted["slot" + socketNumber] = socketSlotTree;
                    socketSlot.TakeOut(1);
                    socketSlot.MarkDirty();
                    encrustable.Itemstack.Attributes[CANJWConstants.ITEM_ENCRUSTED_STRING] = socketEncrusted;
                    encrustable.MarkDirty();
                    inventory.TakeLocked = false;
                    canjewelry.Instance?.FireXp(new src.integration.CANXpEvent {
                        Player = player, Action = "addSocket", Xp = 2f
                    });
                    return true;
                }
            }
            inventory.TakeLocked = false;
            return false;
        }
        public static bool TryToEncrustGemsIntoSockets(InventoryBase inventory, ItemSlot encrustable, ItemSlot gem_slot, int socket_number, IPlayer player = null)
        {
            inventory.TakeLocked = true;
            if (encrustable.Itemstack != null && encrustable.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
            {
                if (gem_slot.Empty || !gem_slot.Itemstack.Collectible.Attributes.KeyExists("canGemType"))
                {
                    inventory.TakeLocked = false;
                    return false;
                }
                var tree = encrustable.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                var cutGemTree = gem_slot.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);
                if(cutGemTree == null)
                {
                    inventory.TakeLocked = false;
                    return false;
                }
                if (tree.HasAttribute("slot" + socket_number))
                {
                    ITreeAttribute treeSocket = tree.GetTreeAttribute("slot" + socket_number);
                    //socket level is lower than gem type
                    if (treeSocket.GetInt(CANJWConstants.ADDED_SOCKET_TYPE) < gem_slot.Itemstack.Collectible.Attributes["canGemType"].AsInt())
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }
                    //item cannot have this gem encrusted
                    if (!canItemContainThisGem(gem_slot.Itemstack.Collectible.Code.Path.Split('-').Last(), encrustable.Itemstack))
                    {
                        inventory.TakeLocked = false;
                        return false;
                    }
                    if (!canjewelry.config.TurnOffBuffs)
                    {
                        int currentMaxDurability = encrustable.Itemstack.Collectible.GetMaxDurability(encrustable.Itemstack);
                        int currentDurability = encrustable.Itemstack.Attributes.GetInt("durability", 0);
                        string[] newBuffNames = (cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value;
                        float[] newBuffValues = (cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value;

                        string[] currentBuffNames = (treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;
                        float[] currentBuffValues = (treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;

                    
                        if (currentMaxDurability < 1)
                        {
                            if ((newBuffNames?.Contains("candurability") ?? false))
                            {
                                inventory.TakeLocked = false;
                                return false;
                            }
                            else if ((currentBuffNames?.Contains("candurability") ?? false))
                            {
                                inventory.TakeLocked = false;
                                return false;
                            }
                        }

                        //Go through current buffs
                        if (currentBuffNames != null)
                        {
                            for (int i = 0; i < currentBuffNames.Length; i++)
                            {
                                string tmpName = currentBuffNames[i];
                                float tmpValue = currentBuffValues[i];

                                //if it is dur buff then remove it's part
                                if (tmpName.Equals("candurability"))
                                {
                                    float currentDurabilityBuffOnTree = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                                    if (currentDurability > 0)
                                    {
                                        currentDurability = (int)((float)currentDurability / (1 + tmpValue));
                                        //currentDurability = 1;
                                        encrustable.Itemstack.Attributes.SetInt("durability", currentDurability);
                                    }

                                    currentDurabilityBuffOnTree -= tmpValue;

                                    if (currentDurabilityBuffOnTree == 0)
                                    {
                                        tree.RemoveAttribute(CANJWConstants.CANDURABILITY_STRING);
                                    }
                                    else
                                    {
                                        tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, currentDurabilityBuffOnTree);
                                    }
                                }
                            }
                        }
                        for (int i = 0; i < newBuffNames.Length; i++)
                        {
                            string tmpName = newBuffNames[i];
                            float tmpValue = newBuffValues[i];

                            if (tmpName.Equals("candurability"))
                            {
                                float currentDurabilityBuff = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                                if (currentDurabilityBuff == 0)
                                {
                                    tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, tmpValue);
                                }
                                else
                                {
                                    tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, currentDurabilityBuff + tmpValue);
                                }

                                if (currentDurability > 0)
                                {
                                    currentDurability = (int)((float)currentDurability * (1 + tmpValue));
                                    encrustable.Itemstack.Attributes.SetInt("durability", currentDurability);
                                }
                            }


                        }
                        // Integration hook — companion mods can scale individual buff values
                        // (e.g. Lapidary "Steady Hand" perk → +X% to main stats).
                        if (canjewelry.Instance != null)
                        {
                            int socketTier = treeSocket.GetInt(CANJWConstants.ADDED_SOCKET_TYPE);
                            for (int bi = 0; bi < newBuffValues.Length && bi < newBuffNames.Length; bi++)
                            {
                                var ev = new src.integration.EncrustEvent
                                {
                                    Player      = player,
                                    Jewelry     = encrustable.Itemstack,
                                    Gem         = gem_slot.Itemstack,
                                    SocketTier  = socketTier,
                                    SocketIndex = socket_number,
                                    BuffName    = newBuffNames[bi],
                                    Value       = newBuffValues[bi]
                                };
                                canjewelry.Instance.FireEncrust(ev);
                                newBuffValues[bi] = ev.Value;
                            }
                        }

                        if (!treeSocket.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES))
                        {
                            treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(newBuffNames);
                            treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(newBuffValues);
                        }
                        else
                        {
                            (treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value = newBuffNames;
                            (treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value = newBuffValues;
                        }
                    }
                    treeSocket.SetInt(CANJWConstants.GEM_BUFF_TYPE, (int)EnumGemBuffType.STATS_BUFF);
                    treeSocket.SetInt("size", gem_slot.Itemstack.Collectible.Attributes["canGemType"].AsInt());
                    treeSocket.SetString("gemtype", gem_slot.Itemstack.Collectible.Code.Path.Split('-').Last());

                    treeSocket.SetString(CANJWConstants.CUTTING_TYPE, cutGemTree.GetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND));

                    // Persist wasExtracted across the encrust→extract cycle so anti-farming
                    // survives a round-trip; otherwise every other extract would earn XP.
                    treeSocket.SetBool("wasExtracted", gem_slot.Itemstack.Attributes.GetBool("wasExtracted", false));

                    // Carry over grinder progress so extraction can restore it. WAS_GROUND_BEFORE
                    // captures the "fully done" flag; savedGrindLayerInfo captures mid-process state
                    // (which phase + how many clicks remain). Without this, partial grinding would
                    // be silently lost on encrust→extract, OR — worse — naive reset would let the
                    // player apply phase-1 multipliers a second time.
                    treeSocket.SetBool(CANJWConstants.WAS_GROUND_BEFORE, cutGemTree.GetBool(CANJWConstants.GEM_FULL_PROCESSED, false));
                    if (gem_slot.Itemstack.Attributes.HasAttribute("cangrindlayerinfo"))
                    {
                        treeSocket["savedGrindLayerInfo"] = gem_slot.Itemstack.Attributes["cangrindlayerinfo"].Clone();
                    }

                    string gemType = gem_slot.Itemstack.Collectible.Code.Path.Split('-').Last();
                    CANGemVisual.SetGemVisual(encrustable.Itemstack, socket_number, gemType);
                    // Anti-farming gate (spec §6): re-encrusting a previously-extracted gem
                    // grants no XP. Flag travels with the gem ItemStack via wasExtracted.
                    bool freshGem = !gem_slot.Itemstack.Attributes.GetBool("wasExtracted", false);
                    gem_slot.TakeOut(1);
                    gem_slot.MarkDirty();
                    encrustable.MarkDirty();
                    inventory.TakeLocked = false;
                    if (freshGem)
                    {
                        canjewelry.Instance?.FireXp(new src.integration.CANXpEvent {
                            Player = player, Action = "encrust", Xp = 0f
                        });
                    }
                    return true;
                }

            }
            inventory.TakeLocked = false;
            return false;
        }
        // Pulls a gem out of socket {socket_number}. Default outcome is GemOutcome.Destroyed
        // / JewelryOutcome.Intact — companion mods mutate the event to save or downgrade.
        // Surviving gem is handed to gemOutSlot if empty, otherwise player inv, otherwise
        // dropped at their feet. wasExtracted=true is set on the surviving gem stack so
        // re-encrusting it earns no XP (anti-farming).
        public static bool TryExtractGem(InventoryBase inventory, ItemSlot encrustable, ItemSlot gemOutSlot, int socket_number, IPlayer player = null)
        {
            inventory.TakeLocked = true;
            try
            {
                if (encrustable.Itemstack == null || !encrustable.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
                    return false;

                ITreeAttribute tree = encrustable.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                ITreeAttribute treeSocket = tree.GetTreeAttribute("slot" + socket_number);
                if (treeSocket == null) return false;

                string gemType = treeSocket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");
                if (string.IsNullOrEmpty(gemType)) return false;

                int sizeInt = treeSocket.GetInt(CANJWConstants.ENCRUSTED_GEM_SIZE);
                string sizeStr = sizeInt == 3 ? "exquisite" : sizeInt == 2 ? "flawless" : "normal";
                string cuttingType = treeSocket.GetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND);
                int socketTier = treeSocket.GetInt(CANJWConstants.ADDED_SOCKET_TYPE);
                string[] buffNames = (treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;
                float[] buffValues = (treeSocket[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;

                IWorldAccessor world = inventory?.Api?.World ?? player?.Entity?.World;
                if (world == null) return false;

                Item gemItem = world.GetItem(new AssetLocation("canjewelry:gem-cut-" + sizeStr + "-" + gemType));
                if (gemItem == null) return false;

                ItemStack gemStack = new ItemStack(gemItem);
                ITreeAttribute cutGemTree = new TreeAttribute();
                cutGemTree.SetString(CANJWConstants.CUTTING_TYPE, cuttingType);
                if (buffNames != null && buffValues != null)
                {
                    cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute((string[])buffNames.Clone());
                    cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute((float[])buffValues.Clone());
                }
                // Restore grinder progress saved at encrust. WAS_GROUND_BEFORE → fully done;
                // savedGrindLayerInfo → mid-process state (phase + remaining counter). Legacy
                // sockets (no flag at all) are treated as fully ground for safety.
                bool wasFullyGround = treeSocket.GetBool(CANJWConstants.WAS_GROUND_BEFORE, true);
                cutGemTree.SetBool(CANJWConstants.GEM_FULL_PROCESSED, wasFullyGround);
                gemStack.Attributes[CANJWConstants.CUT_GEM_TREE] = cutGemTree;
                if (!wasFullyGround && treeSocket.HasAttribute("savedGrindLayerInfo"))
                {
                    gemStack.Attributes["cangrindlayerinfo"] = treeSocket["savedGrindLayerInfo"].Clone();
                }

                // The taint travels through the encrust→extract cycle so a single extract
                // doesn't unlock a fresh XP roll on what was already a recycled gem.
                bool alreadyTainted = treeSocket.GetBool("wasExtracted", false);
                if (alreadyTainted) gemStack.Attributes.SetBool("wasExtracted", true);

                var ev = new src.integration.ExtractEvent
                {
                    Player = player,
                    Jewelry = encrustable.Itemstack,
                    Gem = gemStack.Clone(),
                    SocketTier = socketTier,
                    ExtractionChance = canjewelry.config.gemExtractionReturnChance,
                    JewelryBreakChance = canjewelry.config.jewelryBreakOnExtractionChance,
                    SocketIndex = socket_number,
                    BuffName = (buffNames != null && buffNames.Length > 0) ? buffNames[0] : null,
                };
                canjewelry.Instance?.FireExtract(ev);

                // Built-in jewelry break roll: if no companion changed the outcome, apply config chance.
                if (ev.JewelryOutcome == src.integration.JewelryOutcome.Intact
                    && ev.JewelryBreakChance > 0f
                    && world.Rand.NextDouble() < ev.JewelryBreakChance)
                {
                    ev.JewelryOutcome = src.integration.JewelryOutcome.Destroyed;
                }

                // Reverse candurability buff effect on the jewelry (mirrors the encrust math).
                if (buffNames != null && buffValues != null)
                {
                    int currentDurability = encrustable.Itemstack.Attributes.GetInt("durability", 0);
                    for (int i = 0; i < buffNames.Length; i++)
                    {
                        if (buffNames[i] != "candurability") continue;
                        float buffVal = buffValues[i];
                        float treeBuff = tree.TryGetFloat(CANJWConstants.CANDURABILITY_STRING).GetValueOrDefault();
                        if (currentDurability > 0)
                        {
                            currentDurability = (int)((float)currentDurability / (1f + buffVal));
                            if (currentDurability < 1) currentDurability = 1;
                            encrustable.Itemstack.Attributes.SetInt("durability", currentDurability);
                        }
                        treeBuff -= buffVal;
                        if (treeBuff == 0f) tree.RemoveAttribute(CANJWConstants.CANDURABILITY_STRING);
                        else tree.SetFloat(CANJWConstants.CANDURABILITY_STRING, treeBuff);
                    }
                }

                // Wipe gem-related socket attrs but keep the socket itself (ADDED_SOCKET_TYPE).
                treeSocket.RemoveAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES);
                treeSocket.RemoveAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_VALUES);
                treeSocket.RemoveAttribute(CANJWConstants.GEM_BUFF_TYPE);
                treeSocket.RemoveAttribute(CANJWConstants.ENCRUSTED_GEM_SIZE);
                treeSocket.RemoveAttribute(CANJWConstants.CUTTING_TYPE);
                treeSocket.SetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "");

                CANGemVisual.ClearGemVisual(encrustable.Itemstack, socket_number);

                // Resolve jewelry fate. Damage exhausting durability promotes Damaged→Destroyed
                // — consistent with vanilla item death.
                if (ev.JewelryOutcome == src.integration.JewelryOutcome.Damaged && ev.JewelryDamageAmount > 0)
                {
                    int maxDur = encrustable.Itemstack.Collectible.GetMaxDurability(encrustable.Itemstack);
                    int curDur = encrustable.Itemstack.Attributes.GetInt("durability", maxDur);
                    curDur -= ev.JewelryDamageAmount;
                    if (curDur > 0) encrustable.Itemstack.Attributes.SetInt("durability", curDur);
                    else ev.JewelryOutcome = src.integration.JewelryOutcome.Destroyed;
                }
                if (ev.JewelryOutcome == src.integration.JewelryOutcome.Destroyed)
                {
                    encrustable.Itemstack = null;
                    (player as IServerPlayer)?.SendMessage(0, Lang.Get("canjewelry:extract-jewelry-destroyed"), EnumChatType.Notification);
                }
                encrustable.MarkDirty();

                // Roll against final ExtractionChance (companion mod may have raised it via skill).
                if (ev.GemOutcome == src.integration.GemOutcome.Returned
                    && world.Rand.NextDouble() >= ev.ExtractionChance)
                {
                    ev.GemOutcome = src.integration.GemOutcome.Destroyed;
                }

                // DowngradeChance fallback: if the gem would be lost, give a chance to recover
                // a smaller version instead (set by companion mods, e.g. DelicateTouch perk).
                if (ev.GemOutcome == src.integration.GemOutcome.Destroyed
                    && ev.DowngradeChance > 0f
                    && world.Rand.NextDouble() < ev.DowngradeChance)
                {
                    ev.GemOutcome = src.integration.GemOutcome.ReturnedDowngraded;
                }

                if (ev.GemOutcome == src.integration.GemOutcome.Destroyed)
                    (player as IServerPlayer)?.SendMessage(0, Lang.Get("canjewelry:extract-gem-destroyed"), EnumChatType.Notification);
                else if (ev.GemOutcome == src.integration.GemOutcome.ReturnedDowngraded)
                    (player as IServerPlayer)?.SendMessage(0, Lang.Get("canjewelry:extract-gem-downgraded"), EnumChatType.Notification);

                // Resolve gem fate. ReturnedDowngraded swaps in a smaller cut variant; if the
                // gem is already the smallest size, fall back to a full Returned (graceful).
                // outGem is ev.Gem (not gemStack) so companion mods can modify the returned item.
                if (ev.GemOutcome != src.integration.GemOutcome.Destroyed)
                {
                    ItemStack outGem = ev.Gem;
                    if (ev.GemOutcome == src.integration.GemOutcome.ReturnedDowngraded)
                    {
                        outGem = TryDowngradeCutGem(world, gemStack, sizeInt, gemType, cutGemTree) ?? ev.Gem;
                    }
                    outGem.Attributes.SetBool("wasExtracted", true);
                    if (gemOutSlot != null && gemOutSlot.Empty)
                    {
                        gemOutSlot.Itemstack = outGem;
                        gemOutSlot.MarkDirty();
                    }
                    else if (player != null && player.InventoryManager.TryGiveItemstack(outGem, true))
                    {
                        // accepted into inventory
                    }
                    else
                    {
                        Vec3d pos = player?.Entity?.Pos.XYZ ?? new Vec3d();
                        world.SpawnItemEntity(outGem, pos);
                    }
                }
                return true;
            }
            finally
            {
                inventory.TakeLocked = false;
            }
        }

        // Removes the socket at {socket_number} and returns the socket item to the player.
        // Refuses if a gem is still encrusted — the gem must be extracted first, otherwise it
        // would be orphaned. The exact socket item is restored from SOCKET_ITEM_CODE saved at
        // insert time (EncrustableCB.cs TryAddSocket); legacy sockets without it fall back to a
        // config tier lookup. Surviving socket goes to socketOutSlot if empty, else the player's
        // inventory, else dropped at their feet. Gated by config.canExtractSocket, and the socket
        // is only recovered on a successful config.socketExtractionReturnChance roll (else lost).
        public static bool TryExtractSocket(InventoryBase inventory, ItemSlot encrustable, ItemSlot socketOutSlot, int socket_number, IPlayer player = null)
        {
            inventory.TakeLocked = true;
            try
            {
                // Socket removal can be disabled entirely via config.
                if (!canjewelry.config.canExtractSocket)
                    return false;

                if (encrustable.Itemstack == null || !encrustable.Itemstack.Attributes.HasAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING))
                    return false;

                ITreeAttribute tree = encrustable.Itemstack.Attributes.GetTreeAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                ITreeAttribute treeSocket = tree.GetTreeAttribute("slot" + socket_number);
                if (treeSocket == null) return false;

                // A gem still in the socket must be extracted first.
                if (!string.IsNullOrEmpty(treeSocket.GetString(CANJWConstants.GEM_TYPE_IN_SOCKET, "")))
                {
                    (player as IServerPlayer)?.SendMessage(0, Lang.Get("canjewelry:extract-socket-has-gem"), EnumChatType.Notification);
                    return false;
                }

                IWorldAccessor world = inventory?.Api?.World ?? player?.Entity?.World;
                if (world == null) return false;

                // Restore the exact socket item saved at insert; fall back to a tier lookup for legacy data.
                string socketCode = treeSocket.GetString(CANJWConstants.SOCKET_ITEM_CODE, "");
                if (string.IsNullOrEmpty(socketCode))
                {
                    int socketTier = treeSocket.GetInt(CANJWConstants.ADDED_SOCKET_TYPE);
                    foreach (var kv in canjewelry.config.LevelOfSocketByType)
                    {
                        if (kv.Value == socketTier) { socketCode = kv.Key; break; }
                    }
                }
                Item socketItem = string.IsNullOrEmpty(socketCode) ? null : world.GetItem(new AssetLocation(socketCode));

                // Roll for whether the socket item survives removal. A null item (unresolvable
                // legacy socket) counts as destroyed so we never silently keep a socket the
                // player can't get back.
                bool returned = socketItem != null && world.Rand.NextDouble() < canjewelry.config.socketExtractionReturnChance;

                // Remove the socket from the encrusted tree; drop the whole tree once the last one goes.
                tree.RemoveAttribute("slot" + socket_number);
                int remaining = tree.GetInt(CANJWConstants.SOCKET_ADDED_NUMBER) - 1;
                if (remaining <= 0)
                    encrustable.Itemstack.Attributes.RemoveAttribute(CANJWConstants.ITEM_ENCRUSTED_STRING);
                else
                    tree.SetInt(CANJWConstants.SOCKET_ADDED_NUMBER, remaining);
                encrustable.MarkDirty();

                // Hand the socket item back, or report it was destroyed by the failed roll.
                if (returned)
                {
                    ItemStack socketStack = new ItemStack(socketItem);
                    if (socketOutSlot != null && socketOutSlot.Empty)
                    {
                        socketOutSlot.Itemstack = socketStack;
                        socketOutSlot.MarkDirty();
                    }
                    else if (player != null && player.InventoryManager.TryGiveItemstack(socketStack, true))
                    {
                        // accepted into inventory
                    }
                    else
                    {
                        Vec3d pos = player?.Entity?.Pos.XYZ ?? new Vec3d();
                        world.SpawnItemEntity(socketStack, pos);
                    }
                }
                else
                {
                    (player as IServerPlayer)?.SendMessage(0, Lang.Get("canjewelry:extract-socket-destroyed"), EnumChatType.Notification);
                }

                // Xp = 0 — companion mods own the XP economy and read their own reward for this action.
                canjewelry.Instance?.FireXp(new src.integration.CANXpEvent {
                    Player = player, Action = "extractSocket", Xp = 0f
                });
                return true;
            }
            finally
            {
                inventory.TakeLocked = false;
            }
        }

        // Builds a smaller-cut-size variant of the given gem. Buff values are RE-ROLLED at the
        // smaller tier from BuffAttributesDict (same buff names + same cut style, fresh values),
        // and GEM_FULL_PROCESSED is cleared so the player can grind it again from scratch.
        // Returns null if already the smallest size or if the smaller item code can't be resolved.
        private static ItemStack TryDowngradeCutGem(IWorldAccessor world, ItemStack source, int currentSizeInt, string gemType, ITreeAttribute cutGemTree)
        {
            (string smallerSizeStr, int smallerTier) = currentSizeInt switch
            {
                3 => ("flawless", 2),
                2 => ("normal",   1),
                _ => (null,       0),
            };
            if (smallerSizeStr == null) return null;

            Item smallerItem = world.GetItem(new AssetLocation("canjewelry:gem-cut-" + smallerSizeStr + "-" + gemType));
            if (smallerItem == null) return null;

            string cuttingType = cutGemTree.GetString(CANJWConstants.CUTTING_TYPE, CANJWConstants.CUTTING_ROUND);
            ITreeAttribute newTree = new TreeAttribute();
            newTree.SetString(CANJWConstants.CUTTING_TYPE, cuttingType);

            string[] originalNames = (cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;
            float[]  originalValues = (cutGemTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;

            if (originalNames != null && originalValues != null && originalNames.Length > 0
                && canjewelry.config.BuffAttributesDict.TryGetValue(originalNames[0], out var mainAttrs)
                && canjewelry.config.CuttingAttributesDict.TryGetValue(cuttingType, out var cutAttrs))
            {
                float newMain = (float)Math.Round(mainAttrs.GetRandomMainValue(smallerTier), 3) * cutAttrs.GrindingBuffIncreaseMultipliers[0];
                if (originalNames.Length >= 2)
                {
                    float newSecondary = (float)Math.Round(mainAttrs.GetRandomSecondaryValue(smallerTier), 3) / 100f;
                    newTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { originalNames[0], originalNames[1] });
                    newTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(new[] { newMain, newSecondary });
                }
                else
                {
                    newTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(new[] { originalNames[0] });
                    newTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(new[] { newMain });
                }
            }
            else if (originalNames != null && originalValues != null)
            {
                // Fallback when buff config is missing — preserve the original values rather than
                // produce a buff-less gem. Won't normally trigger; sanity net.
                newTree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute((string[])originalNames.Clone());
                newTree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute((float[])originalValues.Clone());
            }

            ItemStack smaller = new ItemStack(smallerItem);
            smaller.Attributes[CANJWConstants.CUT_GEM_TREE] = newTree;
            return smaller;
        }

        public static bool canItemContainThisGem(string gemType, ItemStack targetItemStack)
        {
            if (canjewelry.config.buffNameToPossibleItem.TryGetValue(gemType, out var hashSetClasses))
            {
                foreach (var it in hashSetClasses)
                {
                    if (WildcardUtil.Match("*" + it + "*", targetItemStack.Collectible.Code.Path))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        // Parsed SocketTiers per item. GetMaxAmountSockets and GetSocketsTiers are called from mesh
        // building and from tooltips, and used to deserialise the same JSON on every single call.
        // Keyed by the collectible because the parsed table belongs to the item type, not the stack.
        private static readonly ConcurrentDictionary<CollectibleObject, Dictionary<string, int[]>> variantTiersCache
            = new ConcurrentDictionary<CollectibleObject, Dictionary<string, int[]>>();

        /// <summary>
        /// Drops the parsed tables. Has to run whenever the socket attributes are written anew -
        /// the config can arrive from the server after items are already loaded, and a stale table
        /// would keep handing out the previous socket layout.
        /// </summary>
        public static void ClearVariantTiersCache()
        {
            variantTiersCache.Clear();
        }

        private static Dictionary<string, int[]> GetVariantTiers(ItemStack itemstack)
        {
            CollectibleObject collectible = itemstack.Collectible;
            if (collectible == null) return null;

            if (variantTiersCache.TryGetValue(collectible, out var cached)) return cached;

            var attribute = itemstack.ItemAttributes[CANJWConstants.CAN_CUSTOM_VARIANTS];
            Dictionary<string, int[]> parsed = null;
            if (attribute != null)
            {
                try
                {
                    parsed = JsonConvert.DeserializeObject<Dictionary<string, int[]>>(attribute.ToString());
                }
                catch (Exception)
                {
                    // A malformed rule should cost the item its sockets, not crash the tooltip.
                    parsed = null;
                }
            }

            // Cached even when null, so a broken rule is not re-parsed on every draw.
            variantTiersCache[collectible] = parsed;
            return parsed;
        }

        /// <summary>
        /// The value a custom variant rule is keyed on. Stack attributes first, then the item's own
        /// code variant.
        /// <para>
        /// The fallback is there because adornments do not all carry their material the same way: a
        /// tiara keeps "carcassus" on the stack, while a coronet bakes the metal into its code as a
        /// variant group (cancoronet-gold). Without it such items cannot use a variant rule at all
        /// and every single code has to be listed by hand instead.
        /// </para>
        /// </summary>
        public static string ResolveVariantValue(ItemStack itemstack, string compareKey)
        {
            if (itemstack == null || string.IsNullOrEmpty(compareKey)) return null;

            string fromAttributes = itemstack.Attributes?.GetString(compareKey, null);
            if (fromAttributes != null) return fromAttributes;

            var variants = itemstack.Collectible?.Variant;
            if (variants != null && variants.ContainsKey(compareKey)) return variants[compareKey];

            return null;
        }

        public static int GetMaxAmountSockets(ItemStack itemstack)
        {
            if (itemstack != null)
            {
                if (itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists(CANJWConstants.CAN_CUSTOM_VARIANTS))
                {
                    string compareKey = itemstack.ItemAttributes.KeyExists(CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY)
                        ? itemstack.ItemAttributes[CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY].AsString()
                        : null;
                    if (compareKey == null) return -1;
                    string searchedValue = ResolveVariantValue(itemstack, compareKey);
                    if (searchedValue != null)
                    {
                        var valueDict = GetVariantTiers(itemstack);
                        if (valueDict != null && valueDict.TryGetValue(searchedValue, out var value))
                        {
                            return value.Length;
                        }
                    }
                }
                else if (itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists("canhavesocketsnumber") && itemstack.ItemAttributes["canhavesocketsnumber"].AsInt() > 0)
                {
                    return itemstack.ItemAttributes["canhavesocketsnumber"].AsInt();
                }
            }
            return -1;
        }
        public static int[] GetSocketsTiers(ItemStack itemstack)
        {
            if (itemstack != null)
            {
                if (itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists(CANJWConstants.CAN_CUSTOM_VARIANTS))
                {
                    string compareKey = itemstack.ItemAttributes.KeyExists(CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY)
                        ? itemstack.ItemAttributes[CANJWConstants.CAN_CUSTOM_VARIANTS_COMPARE_KEY].AsString()
                        : null;
                    if (compareKey == null) return new int[0];
                    string searchedValue = ResolveVariantValue(itemstack, compareKey);
                    if (searchedValue != null)
                    {
                        var valueDict = GetVariantTiers(itemstack);
                        if (valueDict != null && valueDict.TryGetValue(searchedValue, out var value))
                        {
                            return value;
                        }
                    }
                }
                else if (itemstack.ItemAttributes != null && itemstack.ItemAttributes.KeyExists("canhavesocketsnumber") && itemstack.ItemAttributes["canhavesocketsnumber"].AsInt() > 0)
                {
                    return itemstack.ItemAttributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray<int>();
                    /*var c = itemstack.ItemAttributes[CANJWConstants.SOCKETS_TIERS_STRING];
                    var f = c.AsArray<int>();
                    return new int[] { 2 };
                    return new int[] { 1 };*/
                        //itemstack.ItemAttributes[CANJWConstants.SOCKETS_TIERS_STRING].AsArray();
                }
            }
            return new int[0];
        }
        /// <summary>Writes the rolled buffs onto the cut gem, empty arrays meaning "no stat buffs".</summary>
        private static void SetCutGemBuffs(ItemStack outstack, ITreeAttribute tree, string[] names, float[] values)
        {
            tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(names);
            tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(values);
            outstack.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
        }

        private static string PickRandom(ICollection<string> values) => values.ElementAt(Config.rand.Next(values.Count));

        public static void ApplyCuttingBuff(ItemStack outstack)
        {
            if (outstack.Attributes.HasAttribute(CANJWConstants.CUT_GEM_TREE))
            {
                ITreeAttribute isTree = outstack.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE);

                string gemType = outstack.Collectible.Variant["gemtype"];
                string cuttingType = isTree.GetString(CANJWConstants.CUTTING_TYPE);
                ITreeAttribute tree = new TreeAttribute();
                tree.SetString(CANJWConstants.CUTTING_TYPE, cuttingType);
                // Anything the config cannot answer for ends as a gem without stat buffs rather
                // than as an exception during cutting: an admin who removes a buff or a cutting
                // type should not be able to crash the bench.
                if (gemType == null
                    || !canjewelry.config.PossibleGemBuffs.TryGetValue(gemType, out var possibleBuffs)
                    || possibleBuffs.Count == 0)
                {
                    SetCutGemBuffs(outstack, tree, new string[0], new float[0]);
                    return;
                }

                string selectedBuffName = PickRandom(possibleBuffs);
                if (!canjewelry.config.BuffAttributesDict.TryGetValue(selectedBuffName, out BuffAttributes buffAttributes))
                {
                    SetCutGemBuffs(outstack, tree, new string[0], new float[0]);
                    return;
                }

                if (!canjewelry.config.CuttingAttributesDict.TryGetValue(cuttingType, out var cuttingAttributes)
                    || cuttingAttributes?.GrindingBuffIncreaseMultipliers == null
                    || cuttingAttributes.GrindingBuffIncreaseMultipliers.Length == 0)
                {
                    SetCutGemBuffs(outstack, tree, new string[0], new float[0]);
                    return;
                }

                int gemTier = outstack.Collectible.Attributes["canGemType"].AsInt();

                //Main stat buff
                float buffValue = (float)Math.Round(buffAttributes.GetRandomMainValue(gemTier), 3)
                                  * cuttingAttributes.GrindingBuffIncreaseMultipliers[0];

                List<string> buffNames = new List<string> { selectedBuffName };
                List<float> buffValues = new List<float> { buffValue };

                // Baguette is the only cut that rolls a second stat, and only when the buff
                // actually declares candidates - an empty set used to index past the array.
                if (cuttingType == CANJWConstants.CUTTING_BAGUETTE
                    && buffAttributes.PossibleSecondaryStats != null
                    && buffAttributes.PossibleSecondaryStats.Count > 0)
                {
                    float secondaryBuffValue = (float)Math.Round(buffAttributes.GetRandomSecondaryValue(gemTier), 3) / 100;
                    if (secondaryBuffValue != 0)
                    {
                        buffNames.Add(PickRandom(buffAttributes.PossibleSecondaryStats));
                        buffValues.Add(secondaryBuffValue);
                    }
                }

                SetCutGemBuffs(outstack, tree, buffNames.ToArray(), buffValues.ToArray());
                outstack.Attributes.RemoveAttribute(CANJWConstants.CUTTING_TYPE);
            }
        }
        // Companion-mod hook fired right after a gem-cutting work item completes.
        // ConsumeRough=false on the event means the subscriber wants to refund a
        // rough gem to the player; the rough has already been spent at TryPut time,
        // so we hand a fresh stack of the same rough variant back here.
        // XP is intentionally not fired from here — companions own their own XP economy.
        public static void FireCutCompletionEvents(IPlayer byPlayer, ItemStack workItemStack, ItemStack outstack)
        {
            if (canjewelry.Instance == null || byPlayer == null || workItemStack == null) return;

            string cuttingType = outstack?.Attributes.GetTreeAttribute(CANJWConstants.CUT_GEM_TREE)?.GetString(CANJWConstants.CUTTING_TYPE);
            string gemSize = workItemStack.Attributes.GetString(CANJWConstants.ENCRUSTED_GEM_SIZE);
            ItemStack roughGem = workItemStack.Collectible.GetBehavior<CANGemCuttableCB>()?.GetBaseMaterial(workItemStack);

            var ev = new src.integration.CutEvent
            {
                Player = byPlayer,
                RoughGem = roughGem,
                CuttingType = cuttingType,
                GemSize = gemSize,
                ConsumeRough = true,
            };
            canjewelry.Instance.FireCut(ev);

            if (!ev.ConsumeRough && roughGem != null)
            {
                ItemStack refund = roughGem.Clone();
                if (!byPlayer.InventoryManager.TryGiveItemstack(refund))
                {
                    byPlayer.Entity.World.SpawnItemEntity(refund, byPlayer.Entity.Pos.XYZ);
                }
            }
        }

        // Companion-mod hook fired when a grinding stage begins — at first interaction with a
        // freshly cut gem (stage=1) and at each subsequent stage transition. Subscribers shorten
        // the grind by reducing Counter; core uses the post-fire value (floored at 1).
        public static int FireGrindStageStartEvent(IPlayer player, ItemStack gem, int stage, int defaultCounter)
        {
            if (canjewelry.Instance == null || player == null) return defaultCounter;
            var ev = new src.integration.GrindStageStartEvent
            {
                Player = player,
                Gem = gem,
                Stage = stage,
                Counter = defaultCounter,
            };
            canjewelry.Instance.FireGrindStageStart(ev);
            return Math.Max(1, ev.Counter);
        }

        // Companion-mod hook fired on each completed grind stage. Returns the
        // (possibly mutated) multiplier subscribers want layered on top of the
        // base grinding bonus. Returns 1f if there's no subscriber.
        public static float FireGrindStepEvent(IPlayer player, ItemStack gem, int stage)
        {
            if (canjewelry.Instance == null || player == null) return 1f;
            var ev = new src.integration.GrindEvent
            {
                Player = player,
                Gem = gem,
                Stage = stage,
                Multiplier = 1f,
            };
            canjewelry.Instance.FireGrind(ev);
            return ev.Multiplier;
        }

        public static void ReduceBuffValueBecauseOfMistakes(ItemStack itemStack, float mult)
        {
            if (itemStack.Attributes.HasAttribute("cutgemtree"))
            {
                var tree = itemStack.Attributes.GetTreeAttribute("cutgemtree");


                if (tree.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES))
                {
                    string[] buffNames = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value;
                    float[] buffValues = (tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value;
                        
                    for (int j = 0; j < buffValues.Count(); j++)
                    {
                        buffValues[j] *= mult;
                    }
                    tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(buffValues);
                }
                
            }
        }
        public float GetTemporalGraspValue(ItemStack itemStack)
        {
            if(itemStack.Attributes.HasAttribute("canencrusted"))
            {
                var tree = itemStack.Attributes.GetTreeAttribute("canencrusted");
                float collectedValue = 0;
                for (int i = 0; i < EncrustableCB.GetMaxAmountSockets(itemStack); i++)
                {
                    ITreeAttribute socketSlot = tree.GetTreeAttribute("slot" + i.ToString());

                    if (socketSlot == null)
                    {
                        continue;
                    }

                    if (socketSlot.HasAttribute(CANJWConstants.ENCRUSTABLE_BUFFS_NAMES))
                    {
                        string[] buffNames = (socketSlot[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute).value;
                        float[] buffValues = (socketSlot[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute).value;

                        for (int j = 0; j < buffNames.Length; j++)
                        {
                            float additionalValue = buffValues[j];
                            string attributeBuffName = buffNames[j];
                            if (attributeBuffName == CANJWConstants.TEMPORALGRASP)//TODO
                            {
                                collectedValue += additionalValue;
                            }
                        }
                    }
                }
                return collectedValue;
            }
            return 0;
        }
    }
}
