using canjewelry.src.inventories;
using canjewelry.src.items;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace canjewelry.src.utils
{
    public static class EncrustableFunctions
    {
        public static void TryToEncrustGemsIntoSockets(InventoryJewelerSet inventory)
        {
            ItemSlot encrustable = inventory[0];
            if (encrustable.Itemstack != null && encrustable.Itemstack.Attributes.HasAttribute("canencrusted"))
            {
                inventory.TakeLocked = true;
                var tree = encrustable.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                for (int i = 1; i < tree.GetInt("socketsnumber") + 1; i++)
                {
                    ITreeAttribute treeSocket = tree.GetTreeAttribute("slot" + (i - 1).ToString());
                    if (inventory[i].Itemstack != null && inventory[i].Itemstack.Collectible.Attributes.KeyExists("canGemType"))
                    {
                        if (treeSocket.GetInt("sockettype") < inventory[i].Itemstack.Collectible.Attributes["canGemType"].AsInt())
                        {
                            inventory.TakeLocked = false;
                            return;
                        }
                        if (!canItemContainThisGem(inventory[i].Itemstack.Collectible.Code.Path.Split('-').Last(), encrustable.Itemstack))
                        {
                            inventory.TakeLocked = false;
                            return;
                        }
                        treeSocket.SetInt("size", inventory[i].Itemstack.Collectible.Attributes["canGemType"].AsInt());
                        treeSocket.SetString("gemtype", inventory[i].Itemstack.Collectible.Code.Path.Split('-').Last());
                        treeSocket.SetString("attributeBuff", inventory[i].Itemstack.Collectible.Attributes["canGemTypeToAttribute"].AsString());


                        treeSocket.SetFloat("attributeBuffValue", canjewelry.config.gems_buffs
                            [inventory[i].Itemstack.Collectible.Attributes["canGemTypeToAttribute"].ToString()][inventory[i].Itemstack.Collectible.Attributes["canGemType"].AsInt().ToString()]);
                        if (encrustable.Itemstack.Item is CANItemSimpleNecklace)
                        {
                            encrustable.Itemstack.Attributes.SetString("gem", inventory[i].Itemstack.Collectible.Code.Path.Split('-').Last());
                        }

                        inventory[i].TakeOut(1);
                        inventory[i].MarkDirty();
                        encrustable.MarkDirty();
                    }
                }
                inventory.TakeLocked = false;
            }
        }
        public static void TryToAddSocket(InventoryJewelerSet inventory)
        {
            inventory.TakeLocked = true;
            ItemSlot encrustable = inventory[0];
            if (encrustable.Itemstack != null && encrustable.Itemstack.Collectible.Attributes.KeyExists("canhavesocketsnumber"))
            {
                //already has itree -> has socket alteast 1
                if (encrustable.Itemstack.Attributes.HasAttribute("canencrusted"))
                {
                    var tree = encrustable.Itemstack.Attributes.GetTreeAttribute("canencrusted");
                    if (tree.GetInt("socketsnumber") >= encrustable.Itemstack.Collectible.Attributes["canhavesocketsnumber"].AsInt())
                    {
                        inventory.TakeLocked = false;
                        return;
                    }
                    else
                    {
                        if (!(inventory[1].Itemstack != null && inventory[1].Itemstack.Collectible.Attributes.KeyExists("levelOfSocket")))
                        {
                            inventory.TakeLocked = false;
                            return;
                        }

                        tree.SetInt("socketsnumber", tree.GetInt("socketsnumber") + 1);
                        ITreeAttribute socketSlotTree = new TreeAttribute();
                        socketSlotTree.SetInt("size", 0);
                        socketSlotTree.SetString("gemtype", "");
                        socketSlotTree.SetInt("sockettype", inventory[1].Itemstack.Collectible.Attributes["levelOfSocket"].AsInt());
                        inventory[1].TakeOut(1);
                        inventory[1].MarkDirty();
                        encrustable.MarkDirty();
                        tree["slot" + (tree.GetInt("socketsnumber") - 1).ToString()] = socketSlotTree;
                    }
                }
                else
                {
                    if (encrustable.Itemstack.Collectible.Attributes["canhavesocketsnumber"].AsInt() < 1)
                    {
                        inventory.TakeLocked = false;
                        return;
                    }
                    if (!(inventory[1].Itemstack != null && inventory[1].Itemstack.Collectible.Attributes.KeyExists("levelOfSocket")))
                    {
                        inventory.TakeLocked = false;
                        return;
                    }

                    ITreeAttribute socketSlotTree = new TreeAttribute();

                    socketSlotTree.SetInt("size", 0);
                    socketSlotTree.SetString("gemtype", "");
                    socketSlotTree.SetInt("sockettype", inventory[1].Itemstack.Collectible.Attributes["levelOfSocket"].AsInt());

                    ITreeAttribute socketEncrusted = new TreeAttribute();
                    socketEncrusted.SetInt("socketsnumber", 1);
                    socketEncrusted["slot" + 0] = socketSlotTree;
                    inventory[1].TakeOut(1);
                    inventory[1].MarkDirty();
                    encrustable.Itemstack.Attributes["canencrusted"] = socketEncrusted;
                    encrustable.MarkDirty();
                    
                }
            }
            inventory.TakeLocked = false;
            //check left item that can be encrusted
            //if it at all can be ecnrusted
            //can add more sockets to it
            //if ok we take socket form slot
            //create itree for left item and add info about socket
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
    }
}
