using canjewelry.src.eb;
using canjewelry.src.items;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.CommandAbbr;
using Vintagestory.API.Common.Entities;
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
            // RequiresPlayer sits on the individual subcommands rather than here: the ones below
            // that only read or write config values work just as well from the server console,
            // and demanding a calling player would lock an admin out of them.
            sapi.ChatCommands.Create("canjewelry")
                                .RequiresPrivilege(Privilege.controlserver)
                                    .BeginSub("clearbuffs")
                                        .RequiresPlayer()
                                        .WithDesc("clear cancrusted buffs for player selected by name")
                                        .WithArgs(parsers.Word("playerName"))
                                        .HandleWith(clearCancrustedBuffFromPlayer)
                                    .EndSub()
                                    .BeginSub("reapplybuffs")
                                        .RequiresPlayer()
                                        .WithDesc("reapply cancrusted buffs for player selected by name")
                                        .WithArgs(parsers.Word("playerName"))
                                        .HandleWith(reapplyCancrustedBuffFromPlayer)
                                    .EndSub()
                                    .BeginSub("setgembuffs")
                                        .RequiresPlayer()
                                        .WithAlias("sgb")
                                        .WithArgs(parsers.WordRange("cutting", CANJWConstants.CUTTING_ROUND, CANJWConstants.CUTTING_BAGUETTE, CANJWConstants.CUTTING_PEAR), parsers.OptionalAll("buffNamesAndValues"))
                                        .HandleWith(SetGemParams)
                                    .EndSub()
                                    .BeginSub("cutting")
                                        .WithDesc("show the gem cutting table settings")
                                        .HandleWith(ShowCuttingSettings)
                                        .BeginSub("voxels")
                                            .WithDesc("how many chisel strikes one click performs, 1x1 tool mode only")
                                            .WithArgs(parsers.IntRange("count", 1, 8))
                                            .HandleWith(SetCuttingVoxelsPerClick)
                                        .EndSub()
                                        .BeginSub("durability")
                                            .WithDesc("whether every strike of a click costs chisel durability")
                                            .WithArgs(parsers.Bool("enabled"))
                                            .HandleWith(SetCuttingDurabilityPerVoxel)
                                        .EndSub()
                                        .BeginSub("instant")
                                            .WithDesc("complete the selected recipe on the first strike")
                                            .WithArgs(parsers.Bool("enabled"))
                                            .HandleWith(SetCuttingInstantComplete)
                                        .EndSub()
                                        .BeginSub("spare")
                                            .WithDesc("line tool modes skip voxels the recipe still needs")
                                            .WithArgs(parsers.Bool("enabled"))
                                            .HandleWith(SetCuttingSpareRecipeVoxels)
                                        .EndSub()
                                        .BeginSub("hold")
                                            .WithDesc("milliseconds between strikes while the attack button is held, 0 to switch off")
                                            .WithArgs(parsers.IntRange("milliseconds", 0, 2000))
                                            .HandleWith(SetCuttingHoldInterval)
                                        .EndSub()
                                        .BeginSub("mode")
                                            .WithDesc("who the cutting settings apply to")
                                            .WithArgs(parsers.WordRange("mode", "disabled", "enabled", "whitelist", "blacklist"))
                                            .HandleWith(SetCuttingAccessMode)
                                        .EndSub()
                                        .BeginSub("whitelist")
                                            .WithDesc("add or remove a player name from the cutting whitelist")
                                            .WithArgs(parsers.OptionalWord("playerName"))
                                            .HandleWith(ToggleCuttingWhitelist)
                                        .EndSub()
                                        .BeginSub("blacklist")
                                            .WithDesc("add or remove a player name from the cutting blacklist")
                                            .WithArgs(parsers.OptionalWord("playerName"))
                                            .HandleWith(ToggleCuttingBlacklist)
                                        .EndSub()
                                        .BeginSub("gui")
                                            .RequiresPlayer()
                                            .WithDesc("open the cutting settings dialogue")
                                            .HandleWith(OpenCuttingSettingsGui)
                                        .EndSub()
                                    .EndSub()
                                    ;
            if (canjewelry.config.debugMode)
            {
                sapi.Logger.VerboseDebug("[canjewelry] " + "Server commands registered");
            }
        }


        /// <summary>Resolves an online player by name, null when nobody matches.</summary>
        private static IServerPlayer FindOnlinePlayer(IServerPlayer caller, string playerName)
        {
            foreach (var pl in caller.Entity.Api.World.AllOnlinePlayers)
            {
                if (pl.PlayerName.Equals(playerName))
                {
                    return pl as IServerPlayer;
                }
            }
            return null;
        }

        public static TextCommandResult clearCancrustedBuffFromPlayer(TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            TextCommandResult tcr = new TextCommandResult();
            tcr.Status = EnumCommandStatus.Success;

            IServerPlayer targetPlayer = FindOnlinePlayer(player, (string)args.LastArg);
            if (targetPlayer == null)
            {
                return tcr;
            }
            CANGemBuffAffected.ClearBuffs(targetPlayer.Entity);
            canjewelry.sapi.SendMessage(player, 0, String.Format("Buffs were cleared for {0}", targetPlayer.PlayerName), EnumChatType.Notification);
            return tcr;
        }

        public static TextCommandResult reapplyCancrustedBuffFromPlayer(TextCommandCallingArgs args)
        {
            var pl = args.Caller.Player as IServerPlayer;
            TextCommandResult tcr = new TextCommandResult();
            tcr.Status = EnumCommandStatus.Success;

            IServerPlayer targetPlayer = FindOnlinePlayer(pl, (string)args.LastArg);
            if (targetPlayer == null)
            {
                return tcr;
            }
            var beh = targetPlayer.Entity.GetBehavior<CANGemBuffAffected>();
            if (beh == null)
            {
                return tcr;
            }
            beh.RecomputeBuffs(true);

            canjewelry.sapi.SendMessage(pl, 0, String.Format("Buffs were reapplied for {0}", targetPlayer.PlayerName), EnumChatType.Notification);
            return tcr;
        }
        /// <summary>
        /// Writes the config back and pushes it to everyone online. The cutting settings are read
        /// on both sides while a strike is resolved, so a value that changed on the server only
        /// would have the two sides taking different numbers of voxels off the same grid.
        /// </summary>
        internal static void applyAndBroadcastConfig()
        {
            var sapi = canjewelry.sapi;
            if (sapi == null) return;

            try
            {
                sapi.StoreModConfig(canjewelry.config, canjewelry.Instance.Mod.Info.ModID + ".json");
            }
            catch (Exception e)
            {
                // Not fatal: the running server already has the new value, it just will not
                // survive a restart. Better to say so than to fail the command.
                sapi.Logger.Error("[canjewelry] could not write the config back after a command changed it. {0}", e);
            }

            foreach (var pl in sapi.World.AllOnlinePlayers)
            {
                if (pl is IServerPlayer serverPlayer)
                {
                    canjewelry.Instance.sendNewValues(serverPlayer);
                }
            }
        }

        /// <summary>
        /// A config written by hand can carry a literal null for either list, which the field
        /// initialisers do not catch because they run before deserialisation.
        /// </summary>
        private static void ensureCuttingLists()
        {
            if (canjewelry.config.cuttingWhitelist == null) canjewelry.config.cuttingWhitelist = new HashSet<string>();
            if (canjewelry.config.cuttingBlacklist == null) canjewelry.config.cuttingBlacklist = new HashSet<string>();
        }

        private static string describeCuttingSettings()
        {
            ensureCuttingLists();
            var cfg = canjewelry.config;
            return String.Format(
                "gem cutting: mode {0}, voxels per click {1}, durability per voxel {2}, instant complete {3}, spare recipe voxels {4}, hold interval {5}ms"
                + "\nwhitelist: {6}\nblacklist: {7}",
                cfg.cuttingAccessMode,
                cfg.cuttingVoxelsPerClick,
                cfg.cuttingDurabilityPerVoxel,
                cfg.cuttingInstantComplete,
                cfg.cuttingSpareRecipeVoxels,
                cfg.cuttingHoldStrikeIntervalMs,
                cfg.cuttingWhitelist.Count == 0 ? "(empty)" : String.Join(", ", cfg.cuttingWhitelist),
                cfg.cuttingBlacklist.Count == 0 ? "(empty)" : String.Join(", ", cfg.cuttingBlacklist));
        }

        /// <summary>
        /// Adds the name when it is missing, removes it when it is there. One command for both
        /// directions, the way Knapster's list commands work.
        /// </summary>
        private static TextCommandResult toggleListEntry(HashSet<string> list, string listName, TextCommandCallingArgs args)
        {
            string playerName = args.Parsers[0].IsMissing ? null : args.Parsers[0].GetValue() as string;

            if (String.IsNullOrWhiteSpace(playerName))
            {
                return TextCommandResult.Success(String.Format("{0}: {1}",
                    listName, list.Count == 0 ? "(empty)" : String.Join(", ", list)));
            }

            bool added = list.Add(playerName);
            if (!added) list.Remove(playerName);
            applyAndBroadcastConfig();

            return TextCommandResult.Success(String.Format("{0} {1} the {2}. Now: {3}",
                playerName, added ? "added to" : "removed from", listName,
                list.Count == 0 ? "(empty)" : String.Join(", ", list)));
        }

        public static TextCommandResult ShowCuttingSettings(TextCommandCallingArgs args)
        {
            return TextCommandResult.Success(describeCuttingSettings());
        }

        public static TextCommandResult SetCuttingVoxelsPerClick(TextCommandCallingArgs args)
        {
            canjewelry.config.cuttingVoxelsPerClick = (int)args.Parsers[0].GetValue();
            applyAndBroadcastConfig();
            return TextCommandResult.Success(describeCuttingSettings());
        }

        public static TextCommandResult SetCuttingDurabilityPerVoxel(TextCommandCallingArgs args)
        {
            canjewelry.config.cuttingDurabilityPerVoxel = (bool)args.Parsers[0].GetValue();
            applyAndBroadcastConfig();
            return TextCommandResult.Success(describeCuttingSettings());
        }

        public static TextCommandResult SetCuttingInstantComplete(TextCommandCallingArgs args)
        {
            canjewelry.config.cuttingInstantComplete = (bool)args.Parsers[0].GetValue();
            applyAndBroadcastConfig();
            return TextCommandResult.Success(describeCuttingSettings());
        }

        public static TextCommandResult SetCuttingSpareRecipeVoxels(TextCommandCallingArgs args)
        {
            canjewelry.config.cuttingSpareRecipeVoxels = (bool)args.Parsers[0].GetValue();
            applyAndBroadcastConfig();
            return TextCommandResult.Success(describeCuttingSettings());
        }

        public static TextCommandResult SetCuttingHoldInterval(TextCommandCallingArgs args)
        {
            canjewelry.config.cuttingHoldStrikeIntervalMs = (int)args.Parsers[0].GetValue();
            applyAndBroadcastConfig();
            return TextCommandResult.Success(describeCuttingSettings());
        }

        public static TextCommandResult SetCuttingAccessMode(TextCommandCallingArgs args)
        {
            canjewelry.config.cuttingAccessMode = args.Parsers[0].GetValue().ToString();
            applyAndBroadcastConfig();
            return TextCommandResult.Success(describeCuttingSettings());
        }

        public static TextCommandResult OpenCuttingSettingsGui(TextCommandCallingArgs args)
        {
            IServerPlayer player = args.Caller.Player as IServerPlayer;
            if (player == null)
            {
                return TextCommandResult.Error("Needs a player to send the dialogue to - use the subcommands from the console instead.");
            }

            canjewelry.serverChannel?.SendPacket(CuttingSettingsPacket.FromConfig(canjewelry.config), player);
            return TextCommandResult.Success();
        }

        public static TextCommandResult ToggleCuttingWhitelist(TextCommandCallingArgs args)
        {
            ensureCuttingLists();
            return toggleListEntry(canjewelry.config.cuttingWhitelist, "whitelist", args);
        }

        public static TextCommandResult ToggleCuttingBlacklist(TextCommandCallingArgs args)
        {
            ensureCuttingLists();
            return toggleListEntry(canjewelry.config.cuttingBlacklist, "blacklist", args);
        }

        public static TextCommandResult SetGemParams(TextCommandCallingArgs args)
        {
            var pl = args.Caller.Player as IServerPlayer;
            var beh = pl.Entity.GetBehavior<CANGemBuffAffected>();
            TextCommandResult tcr = new TextCommandResult();
            tcr.Status = EnumCommandStatus.Success;
            if (pl.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                return tcr;
            }
            var itemStack = pl.InventoryManager.ActiveHotbarSlot.Itemstack;
            if(itemStack == null)
            {
                return tcr;
            }
            ITreeAttribute tree = new TreeAttribute();
            tree.SetString(CANJWConstants.CUTTING_TYPE, args.Parsers[0].GetValue().ToString());

            var namesAndValues = args.Parsers[1].GetValue().ToString().Split(' ');
            List<string> buffNames = new();
            List<float> buffValues = new();
            for (int i = 0; i < namesAndValues.Length; i+=2)
            {
                try
                {
                    buffNames.Add(namesAndValues[i]);
                    buffValues.Add(float.Parse(namesAndValues[i + 1], CultureInfo.InvariantCulture));
                }
                catch {
                    return tcr;
                }
            }

            tree[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] = new StringArrayAttribute(buffNames.ToArray());
            tree[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] = new FloatArrayAttribute(buffValues.ToArray());
            itemStack.Attributes[CANJWConstants.CUT_GEM_TREE] = tree;
            pl.InventoryManager.ActiveHotbarSlot.MarkDirty();
            return tcr;
        }
    }
}
