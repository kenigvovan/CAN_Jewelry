using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace canjewelry.src.commands
{
    public class RegisterCommands
    {
        public static void registerServerCommands(ICoreServerAPI sapi)
        {
            var parsers = sapi.ChatCommands.Parsers;
            sapi.ChatCommands.Create("canjewelry")
                                .RequiresPlayer().RequiresPrivilege(Privilege.controlserver)
                                    .BeginSub("clearbuffs")
                                        .WithDesc("clear cancrusted buffs for player selected by name")
                                        .WithArgs(parsers.Word("playerName"))
                                        .HandleWith(clearCancrustedBuffFromPlayer)
                                    .EndSub()
                                    .BeginSub("reapplybuffs")
                                        .WithDesc("reapply cancrusted buffs for player selected by name")
                                        .WithArgs(parsers.Word("playerName"))
                                        .HandleWith(reapplyCancrustedBuffFromPlayer)
                                    .EndSub()
                                    ;
            sapi.Logger.Debug("[canjewelry] " + "Server commands registered");
        }


        public static TextCommandResult clearCancrustedBuffFromPlayer(TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            TextCommandResult tcr = new TextCommandResult();
            tcr.Status = EnumCommandStatus.Success;

            string targetPlayerName = (string)args.LastArg;
            IServerPlayer targetPlayer = null;
            foreach (var pl in player.Entity.Api.World.AllOnlinePlayers)
            {
                if (pl.PlayerName.Equals(targetPlayerName))
                {
                    targetPlayer = pl as IServerPlayer;
                }
            }
            if (targetPlayer == null)
            {
                return tcr;
            }
            foreach (KeyValuePair<string, EntityFloatStats> stat in targetPlayer.Entity.Stats)
            {
                foreach (KeyValuePair<string, EntityStat<float>> keyValuePair in stat.Value.ValuesByKey)
                {
                    if (keyValuePair.Key == "canencrusted")
                    {
                        stat.Value.Set(keyValuePair.Key, 0);
                        //stat.Value.Remove(keyValuePair.Key);
                        break;
                    }
                }
                targetPlayer.Entity.WatchedAttributes.MarkPathDirty("stats");
            }
            canjewelry.sapi.SendMessage(player, 0, String.Format("Buffs were cleared for {0}", targetPlayer.PlayerName), EnumChatType.Notification);
            return tcr;
        }

        public static TextCommandResult reapplyCancrustedBuffFromPlayer(TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            TextCommandResult tcr = new TextCommandResult();
            tcr.Status = EnumCommandStatus.Success;

            string targetPlayerName = (string)args.LastArg;
            IServerPlayer targetPlayer = null;
            foreach (var pl in player.Entity.Api.World.AllOnlinePlayers)
            {
                if (pl.PlayerName.Equals(targetPlayerName))
                {
                    targetPlayer = pl as IServerPlayer;
                }
            }
            if (targetPlayer == null)
            {
                return tcr;
            }
            foreach (KeyValuePair<string, EntityFloatStats> stat in targetPlayer.Entity.Stats)
            {
                foreach (KeyValuePair<string, EntityStat<float>> keyValuePair in stat.Value.ValuesByKey)
                {
                    if (keyValuePair.Key == "canencrusted")
                    {
                        stat.Value.Set(keyValuePair.Key, 0);
                        //stat.Value.Remove(keyValuePair.Key);
                        break;
                    }
                }
                targetPlayer.Entity.WatchedAttributes.MarkPathDirty("stats");
            }
            //go through hotbar active slot, character slots and apply all buffs
            IInventory playerBackpacks = targetPlayer.InventoryManager.GetHotbarInventory();
            {
                //playerBackpacks.Player
                if (playerBackpacks != null)
                {
                    for (int i = 0; i < playerBackpacks.Count; ++i)
                    {
                        if (i != player.InventoryManager.ActiveHotbarSlotNumber || playerBackpacks[i].Itemstack == null || playerBackpacks[i].Itemstack.Item is ItemWearable)
                        {
                            continue;
                        }
                        if (playerBackpacks[i] != null)
                        {
                            ItemSlot itemSlot = playerBackpacks[i];
                            ItemStack itemStack = itemSlot.Itemstack;
                            if (itemStack != null)
                            {
                                if (itemStack.Attributes.HasAttribute("canencrusted"))
                                {
                                    ITreeAttribute encrustTree = itemStack.Attributes.GetTreeAttribute("canencrusted");
                                    if (encrustTree == null)
                                    {
                                        continue;
                                    }
                                    for (int j = 0; j < encrustTree.GetInt("socketsnumber"); j++)
                                    {
                                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + j.ToString());
                                        if (!socketSlot.HasAttribute("attributeBuff"))
                                        {
                                            continue;
                                        }
                                        if (targetPlayer.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey.ContainsKey("canencrusted"))
                                        {
                                            targetPlayer.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", targetPlayer.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value + socketSlot.GetFloat("attributeBuffValue"), true);
                                        }
                                        else
                                        {
                                            targetPlayer.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", socketSlot.GetFloat("attributeBuffValue"), true);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            IInventory charakterInv = targetPlayer.InventoryManager.GetOwnInventory("character");
            {
                //playerBackpacks.Player
                if (charakterInv != null)
                {
                    for (int i = 0; i < charakterInv.Count; ++i)
                    {
                        var f = charakterInv[i];
                        if (charakterInv[i] != null)
                        {
                            ItemSlot itemSlot = charakterInv[i];
                            ItemStack itemStack = itemSlot.Itemstack;
                            if (itemStack != null)
                            {
                                if (itemStack.Attributes.HasAttribute("canencrusted"))
                                {
                                    ITreeAttribute encrustTree = itemStack.Attributes.GetTreeAttribute("canencrusted");
                                    if (encrustTree == null)
                                    {
                                        continue;
                                    }
                                    for (int j = 0; j < encrustTree.GetInt("socketsnumber"); j++)
                                    {
                                        ITreeAttribute socketSlot = encrustTree.GetTreeAttribute("slot" + j.ToString());
                                        if (!socketSlot.HasAttribute("attributeBuff"))
                                        {
                                            continue;
                                        }
                                        if (targetPlayer.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey.ContainsKey("canencrusted"))
                                        {
                                            targetPlayer.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", targetPlayer.Entity.Stats[socketSlot.GetString("attributeBuff")].ValuesByKey["canencrusted"].Value + socketSlot.GetFloat("attributeBuffValue"), true);
                                        }
                                        else
                                        {
                                            targetPlayer.Entity.Stats.Set(socketSlot.GetString("attributeBuff"), "canencrusted", socketSlot.GetFloat("attributeBuffValue"), true);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            canjewelry.sapi.SendMessage(player, 0, String.Format("Buffs were reapplied for {0}", targetPlayer.PlayerName), EnumChatType.Notification);
            return tcr;
        }
    }
}
