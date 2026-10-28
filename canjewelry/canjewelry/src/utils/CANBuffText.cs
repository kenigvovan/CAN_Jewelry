using System.Globalization;
using System.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace canjewelry.src.utils
{
    /// <summary>
    /// Buff values as players read them: extra health in points, everything else in percent.
    /// Names come from lang with their own font tags, so the results are meant for VTML.
    /// </summary>
    public static class CANBuffText
    {
        // The one stat counted in points rather than as a fraction.
        public const string FlatBuff = "maxhealthExtraPoints";

        public static string Name(string buff) => Lang.Get("canjewelry:buff-name-" + buff);

        /// <summary>"+3.1" or "+4%".</summary>
        public static string Value(string buff, float value)
        {
            string sign = value > 0 ? "+" : "";
            return buff == FlatBuff
                ? sign + value.ToString("0.#", CultureInfo.InvariantCulture)
                : sign + (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>"2.7/4.5" or "+3%/+4.5%".</summary>
        public static string Range(string buff, float[] range) => Value(buff, range[0]) + "/" + Value(buff, range[1]);

        /// <summary>The buffs a cut gem tree or a socket carries, as "Health +3.8, Walk speed +4%"; empty for none.</summary>
        public static string Of(ITreeAttribute tree)
        {
            string[] names = (tree?[CANJWConstants.ENCRUSTABLE_BUFFS_NAMES] as StringArrayAttribute)?.value;
            float[] values = (tree?[CANJWConstants.ENCRUSTABLE_BUFFS_VALUES] as FloatArrayAttribute)?.value;
            if (names == null || values == null) return "";
            return string.Join(", ", names.Zip(values, (name, value) => Name(name) + " " + Value(name, value)));
        }
    }
}
