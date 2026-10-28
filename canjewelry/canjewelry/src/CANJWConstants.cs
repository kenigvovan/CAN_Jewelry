namespace canjewelry.src
{
    public static class CANJWConstants
    {
        public const string SOCKETS_TIERS_STRING = "cansocketstiers";
        public const string SOCKETS_NUMBER_STRING = "canhavesocketsnumber";
        public const string ITEM_ENCRUSTED_STRING = "canencrusted";
        public const string LEVEL_OF_SOSCKET_STRING = "levelOfSocket";
        public const string SOCKET_ADDED_NUMBER = "socketsnumber";
        public const string GEM_TYPE_IN_SOCKET = "gemtype";
        public const string ADDED_SOCKET_TYPE = "sockettype";
        public const string ENCRUSTED_GEM_SIZE = "size";
        public const string GEM_ATTRIBUTE_BUFF = "attributeBuff";
        public const string GEM_ATTRIBUTE_BUFF_VALUE = "attributeBuffValue";
        public const string GEM_BUFF_TYPE = "gembufftype";
        public const string CANDURABILITY_STRING = "candurability";
        public const string CAN_CUSTOM_VARIANTS = "cancustomvariants";
        public const string CAN_CUSTOM_VARIANTS_COMPARE_KEY = "cancustomvariantscomparekey";
        public const string CUTTING_TYPE = "cuttingtype";
        public const string CUT_GEM_BUFF_VALUE = "cutgetmbuffvalue";
        public const string CUT_GEM_TREE = "cutgemtree";
        public const string CUT_GEM_MAIN_STAT_NAME = "cutgemmainstatname";
        public const string ENCRUSTABLE_BUFFS_NAMES = "encrustablebuffsnames";
        public const string ENCRUSTABLE_BUFFS_VALUES = "encrustablebuffsvalues";
        public const string GEM_FULL_PROCESSED = "gemfullprocessed";
        public const string WAS_GROUND_BEFORE = "wasGroundBefore";
        public const string SOCKET_ITEM_CODE = "socketitemcode";

        // Gem weight and traits. On a rough gem and a work item they are the hidden truth; what the
        // player has learned of it is APPRAISAL, CARATS_RANGE and GEM_TRAITS_KNOWN.
        public const string CARATS = "carats";
        public const string GEM_TRAITS = "gemtraits";
        public const string GEM_TRAITS_KNOWN = "gemtraitsknown";
        public const string APPRAISAL = "appraisal";
        public const string CARATS_RANGE = "caratsrange";
        // On a cut gem tree: the flaw pieces still in the stone when it was cut, one entry each.
        public const string FLAW_PIECES = "gemflawpieces";

        public const string TEMPORALGRASP = "temporalgrasp";
        public const string CUTTING_ROUND = "round";
        public const string CUTTING_BAGUETTE = "baguette";
        public const string CUTTING_PEAR = "pear";
        public const string FALLBACK_GEM_TYPE = "diamond";
        public const string INSCRIPTION = "inscription";
        public const int INSCRIPTION_MAX_LEN = 32;
    }
}
