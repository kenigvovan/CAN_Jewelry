using canjewelry.src.utils;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace canjewelry.src
{
    public class Config
    {
        public float grindTimeOneTick = 3;

        public Dictionary<string, HashSet<string>> buffNameToPossibleItem = new Dictionary<string, HashSet<string>>
        (new Dictionary<string, HashSet<string>> {
            {"diamond", new HashSet<string>{ "brigandine", "plate", "chain", "scale", "cansimplenecklace" } },
            {"corundum", new HashSet<string>{ "pickaxe", "shovel", "cansimplenecklace" } },
            {"emerald", new HashSet<string>{ "brigandine", "plate", "chain", "scale", "cansimplenecklace", "-antique" } },
            {"fluorite", new HashSet<string>{ "halberd", "mace", "spear", "rapier", "longsword", "zweihander", "messer", "falx", "cansimplenecklace" } },
            {"lapislazuli", new HashSet<string>{ "brigandine", "plate", "chain", "scale", "cansimplenecklace", "-antique" } },
            {"malachite", new HashSet<string>{ "brigandine", "plate", "chain", "scale", "knife", "cansimplenecklace", "-antique" } },
            {"olivine", new HashSet<string>{ "brigandine", "plate", "chain", "scale", "cansimplenecklace", "-antique" } },
            {"uranium", new HashSet<string>{ "brigandine", "plate", "chain", "scale", "cansimplenecklace" , "-antique" } },
            {"quartz", new HashSet<string>{ "pickaxe", "cansimplenecklace" } },
            {"ruby",  new HashSet<string>{ "bow" }}
        });
        public Dictionary<string, Dictionary<string, float>> gems_buffs = new Dictionary<string, Dictionary<string, float>>
            (new Dictionary<string, Dictionary<string, float>>  {
                { "walkspeed", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.04f },
                    { "3", 0.08f }
                    }
                },
                { "miningSpeedMul", new Dictionary<string, float>{
                    { "1", 0.03f },
                    { "2", 0.06f },
                    { "3", 0.09f }
                    }
                },
                { "maxhealthExtraPoints", new Dictionary<string, float>{
                    { "1", 1 },
                    { "2", 2 },
                    { "3", 4 }
                    }
                },
                { "meleeWeaponsDamage", new Dictionary<string, float>{
                    { "1", 0.03f },
                    { "2", 0.05f },
                    { "3", 0.08f }
                    }
                },
                { "hungerrate", new Dictionary<string, float>{
                    { "1", -0.03f },
                    { "2", -0.06f },
                    { "3", -0.1f }
                    }
                },
                { "wildCropDropRate", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.05f },
                    { "3", 0.09f }
                    }
                },
                { "armorDurabilityLoss", new Dictionary<string, float>{
                    { "1", 0.05f },
                    { "2", 0.1f },
                    { "3", 0.15f }
                    }
                },
                { "oreDropRate", new Dictionary<string, float>{
                    { "1", 0.05f },
                    { "2", 0.08f },
                    { "3", 0.11f }
                    }
                },
                { "healingeffectivness", new Dictionary<string, float>{
                    { "1", 0.05f },
                    { "2", 0.1f },
                    { "3", 0.12f }
                    }
                },
                { "rangedWeaponsDamage", new Dictionary<string, float>{
                    { "1", 0.02f },
                    { "2", 0.04f },
                    { "3", 0.09f }
                    }
                }
            });

        public Dictionary<string, int> items_codes_with_socket_count = new Dictionary<string, int>(new Dictionary<string, int>
        {
            { "*knife-generic-gold", 1 },
            { "*knife-generic-silver", 1 },
            { "*knife-generic-iron", 1 },
            { "*knife-generic-meteoriciron", 2 },
            { "*knife-generic-steel", 3 },

            { "*pickaxe-blackbronze", 1 },
            { "*pickaxe-tinbronze", 1 },
            { "*pickaxe-bismuthbronze", 1 },
            { "*pickaxe-gold", 1 },
            { "*pickaxe-silver", 1 },
            { "*pickaxe-iron", 2 },
            { "*pickaxe-meteoriciron", 2 },
            { "*pickaxe-steel", 3 },

            { "*scythe-blackbronze", 1 },
            { "*scythe-tinbronze", 1 },
            { "*scythe-bismuthbronze", 1 },
            { "*scythe-gold", 1 },
            { "*scythe-silver", 1 },
            { "*scythe-iron", 2 },
            { "*scythe-meteoriciron", 2 },
            { "*scythe-steel", 3 },

            { "*shovel-blackbronze", 1 },
            { "*shovel-tinbronze", 1 },
            { "*shovel-bismuthbronze", 1 },
            { "*shovel-gold", 1 },
            { "*shovel-silver", 1 },
            { "*shovel-iron", 2 },
            { "*shovel-meteoriciron", 2 },
            { "*shovel-steel", 3 },

            { "armor-head-brigandine-*", 1 },
            { "armor-legs-brigandine-*", 1 },
            { "armor-body-brigandine-*", 2 },

            { "armor-head-plate-*", 1 },
            { "armor-legs-plate-*", 1 },
            { "armor-body-plate-*", 2 },

            { "armor-head-scale-*", 1 },
            { "armor-legs-scale-*", 1 },
            { "armor-body-scale-*", 2 },

            { "armor-head-chain-*", 1 },
            { "armor-legs-chain-*", 1 },
            { "armor-body-chain-*", 2 },

            { "armor-head-antique-*", 1 },
            { "armor-legs-antique-*", 1 },
            { "armor-body-antique-*", 2 },


            { "xmelee:xzweihander-meteoriciron", 1 },
            { "xmelee:xzweihander-steel", 2 },

            { "xmelee:xlongsword-meteoriciron", 1 },
            { "xmelee:xlongsword-steel", 2 },

            { "xmelee:xhalberd-meteoriciron", 1 },
            { "xmelee:xhalberd-steel", 2 },

            { "xmelee:xmesser-meteoriciron", 1 },
            { "xmelee:xmesser-steel", 2 },

            { "xmelee:xmace-meteoriciron", 1 },
            { "xmelee:xmace-steel", 2 },

            { "xmelee:xpike-meteoriciron", 1 },
            { "xmelee:xpike-steel", 2 },

            { "xmelee:xrapier-meteoriciron", 1 },
            { "xmelee:xrapier-steel", 2 },

            { "xmelee:xspear-meteoriciron", 1 },
            { "xmelee:xspear-steel", 2 },

            { "*blade-falx-gold", 1 },
            { "*blade-falx-silver", 1 },
            { "*blade-falx-iron", 1 },
            { "*blade-falx-meteoriciron", 2 },
            { "*blade-falx-steel", 2 },
            { "*blade-falx-blackguard-iron", 2 },
            { "*blade-forlorn-iron", 2 },
            { "*blade-longsword-admin", 3 },

            { "bow-simple", 1 },
            { "bow-recurve", 2 },
            { "bow-long", 2 },
        });
        public int pan_take_per_use = 8;
    }
}
