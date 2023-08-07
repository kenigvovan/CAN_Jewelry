﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace canjewelry.src
{
    public class Config
    {
        public static Config Current { get; set; } = new Config();
        public class Part<Config>
        {
            public readonly string Comment;
            public readonly Config Default;
            private Config val;
            public Config Val
            {
                get => (val != null ? val : val = Default);
                set => val = (value != null ? value : Default);
            }
            public Part(Config Default, string Comment = null)
            {
                this.Default = Default;
                this.Val = Default;
                this.Comment = Comment;
            }
            public Part(Config Default, string prefix, string[] allowed, string postfix = null)
            {
                this.Default = Default;
                this.Val = Default;
                this.Comment = prefix;

                this.Comment += "[" + allowed[0];
                for (int i = 1; i < allowed.Length; i++)
                {
                    this.Comment += ", " + allowed[i];
                }
                this.Comment += "]" + postfix;
            }
        }
        public Part<float> grindTimeOneTick = new Part<float>(3);

        public Part<Dictionary<string, HashSet<string>>> buffNameToPossibleItem = new Part<Dictionary<string, HashSet<string>>>
        (new Dictionary<string, HashSet<string>> {
            {"diamond", new HashSet<string>{ "brigandine", "plate", "chain", "scale" } },
            {"corundum", new HashSet<string>{ "pickaxe" } },
            {"emerald", new HashSet<string>{ "brigandine", "plate", "chain", "scale" } },
            {"fluorite", new HashSet<string>{ "halberd", "mace", "spear", "rapier", "longsword", "zweihander", "messer", "falx" } },
            {"lapislazuli", new HashSet<string>{ "brigandine", "plate", "chain", "scale" } },
            {"malachite", new HashSet<string>{ "brigandine", "plate", "chain", "scale", "knife" } },
            {"olivine", new HashSet<string>{ "brigandine", "plate", "chain", "scale" } },
            {"uranium", new HashSet<string>{ "brigandine", "plate", "chain", "scale" } },
            {"quartz", new HashSet<string>{ "pickaxe"} }
        });
        public Part<Dictionary<string, Dictionary<string, float>>> gems_buffs = new Part<Dictionary<string, Dictionary<string, float>>>
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
                }
            });

        public Part<Dictionary<string, int>> items_codes_with_socket_count = new Part<Dictionary<string, int>>(new Dictionary<string, int>
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


            { "xmelee:zweihander-meteoriciron", 1 },
            { "xmelee:zweihander-steel", 2 },

            { "xmelee:longsword-meteoriciron", 1 },
            { "xmelee:longsword-steel", 2 },

            { "xmelee:halberd-meteoriciron", 1 },
            { "xmelee:halberd-steel", 2 },

            { "xmelee:messer-meteoriciron", 1 },
            { "xmelee:messer-steel", 2 },

            { "xmelee:mace-meteoriciron", 1 },
            { "xmelee:mace-steel", 2 },

            { "xmelee:pike-meteoriciron", 1 },
            { "xmelee:pike-steel", 2 },

            { "xmelee:rapier-meteoriciron", 1 },
            { "xmelee:rapier-steel", 2 },

            { "xmelee:spear-meteoriciron", 1 },
            { "xmelee:spear-steel", 2 },

            { "*blade-gold", 1 },
            { "*blade-silver", 1 },
            { "*blade-iron", 1 },
            { "*blade-meteoriciron", 2 },
            { "*blade-steel", 2 },
            { "*blade-blackguard", 2 },
            { "*blade-forlorn", 2 },
            { "*blade-admin", 3 },
        });
    }
}
