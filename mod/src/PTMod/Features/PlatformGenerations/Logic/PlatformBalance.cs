using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>How the OEM effect on the contract chance picks its rule (design D10, FR-8).</summary>
    public enum OemMode
    {
        /// <summary>A contract in the call context gives the "per contract" rule, a call without a contract the "market average" rule.</summary>
        Auto,

        /// <summary>The "market average" rule always (the expected multiplier over the preferences of the market).</summary>
        MarketAverage,
    }

    /// <summary>
    /// The numbers of the feature: the typed form of <c>Content/platform-generations/balance.json</c> (NFR-5). Everything the models use
    /// comes from here; the values below are only the defaults for a missing or broken file and are the same as in the shipped file and in
    /// design.md ("Контент balance.json"). <see cref="FromJson"/> reads the merged config of <c>ModContent.LoadConfig</c> and keeps the
    /// default of any member that is missing, has the wrong type or lies outside its range, saying so in the problem list.
    /// The members are public fields so that the offline simulation (TSK-022) and the tests can change them.
    /// </summary>
    public sealed class PlatformBalance
    {
        /// <summary>Installed base (FR-4, formula F1).</summary>
        public sealed class InstalledBaseBalance
        {
            /// <summary>The base of a CPU fades with the factor <see cref="Decay"/> per month; 60 months is the mean life.</summary>
            public int LifetimeMonths = 60;

            /// <summary>The weight of a unit sold through a contract against a unit sold on the market (<c>w_c</c>).</summary>
            public float ContractUnitWeight = 0.5f;

            /// <summary>The monthly factor <c>δ = 1 − 1/lifetimeMonths</c>.</summary>
            public double Decay
            {
                get { return LifetimeMonths <= 0 ? 0.0 : 1.0 - 1.0 / LifetimeMonths; }
            }
        }

        /// <summary>The effect on the CPU rating and the socket-change penalty (FR-5, FR-6, formulas F2-F6).</summary>
        public sealed class RatingBalance
        {
            /// <summary>The largest bonus of the installed base (<c>B_ib</c> at saturation).</summary>
            public float IbMaxBonus = 0.10f;

            /// <summary>The share of the whole base at which the bonus is the largest.</summary>
            public float IbSaturationShare = 0.40f;

            /// <summary>The weight of other companies' base on a standard socket in your share (a standard is a common one).</summary>
            public float SharedWeight = 0.25f;

            /// <summary>The bonus of an early adopter at the start.</summary>
            public float EarlyBonus = 0.06f;

            /// <summary>The months over which the early adopter bonus fades to 0.</summary>
            public int EarlyDurationMonths = 60;

            /// <summary>How many companies get the early adopter bonus per generation.</summary>
            public int EarlyAdopterSlots = 3;

            /// <summary>The largest penalty for a change of the socket (at severity 1).</summary>
            public float ChurnPenalty = 0.10f;

            /// <summary>A main socket younger than this many months is "not supported long enough".</summary>
            public int MinSocketLifeMonths = 36;

            /// <summary>The months over which the socket-change penalty fades to 0.</summary>
            public int ChurnDurationMonths = 24;

            /// <summary>A main socket with a smaller share of the whole base than this does not count.</summary>
            public float ChurnMinBaseShare = 0.02f;

            /// <summary>A socket on the same base package as another one (standard or custom) is compatible with it.</summary>
            public bool CustomOnSameBaseIsCompatible = true;

            /// <summary>The weight of the effect on the "Industries" segment against the consumer segments.</summary>
            public float IndustriesWeight = 0.5f;

            /// <summary>The guard range of the rating multiplier of the feature (for overrides of the content).</summary>
            public float MinMult = 0.85f;

            public float MaxMult = 1.20f;
        }

        /// <summary>The effect on the contract signing chance (FR-8, formulas F7-F9).</summary>
        public sealed class OemBalance
        {
            public OemMode Mode = OemMode.Auto;

            /// <summary>The <c>ContractType</c> values the effect applies to (Calculator 0, Computer 1, Console 2). Calculator is out by default.</summary>
            public int[] ContractTypes = { 1, 2 };

            /// <summary>The "market average" rule is 1 for a CPU of a generation with a smaller order.</summary>
            public int MarketAverageMinGenOrder = 2;

            /// <summary>The bonus for a CPU of the preferred generation or a newer one.</summary>
            public float AdoptBonus = 0.20f;

            /// <summary>The penalty for a CPU one generation behind the preferred one.</summary>
            public float LagPenalty1 = 0.25f;

            /// <summary>The penalty for a CPU two or more generations behind.</summary>
            public float LagPenalty2 = 0.50f;

            /// <summary>Added to the platform multiplier of a CPU with the memory standard of its generation ("full platform", D11).</summary>
            public float FullPlatformBonus = 0.05f;

            /// <summary>The bonus of a big installed base (up to this much) on top of the platform multiplier.</summary>
            public float OemLoyaltyBonus = 0.10f;

            /// <summary>The guard range of the chance multiplier of the feature.</summary>
            public float MinChanceMult = 0.40f;

            public float MaxChanceMult = 1.40f;

            /// <summary>True when the effect applies to contracts of this type.</summary>
            public bool IncludesContractType(int contractType)
            {
                if (ContractTypes == null) return false;
                for (int i = 0; i < ContractTypes.Length; i++)
                {
                    if (ContractTypes[i] == contractType) return true;
                }
                return false;
            }
        }

        public InstalledBaseBalance InstalledBase = new InstalledBaseBalance();
        public RatingBalance Rating = new RatingBalance();
        public OemBalance Oem = new OemBalance();

        /// <summary>A balance with the defaults of the code (the same as the shipped <c>balance.json</c>).</summary>
        public static PlatformBalance Defaults()
        {
            return new PlatformBalance();
        }

        /// <summary>
        /// Reads the merged <c>balance.json</c> (<c>ModContent.LoadConfig</c>, schema 1). Never throws and never returns null: a section or a member
        /// that is missing keeps its default; a member of the wrong type or outside its range keeps its default too and adds a line
        /// "balance.rating.ibMaxBonus: ..." to <paramref name="problems"/> (may be null); an unknown member is reported and ignored.
        /// </summary>
        public static PlatformBalance FromJson(JsonObject config, List<string> problems)
        {
            var balance = new PlatformBalance();
            if (config == null) return balance;

            ReportUnknown(config, "balance", problems, "schema", "installedBase", "rating", "oem");

            JsonObject installed = Section(config, "installedBase", problems);
            if (installed != null)
            {
                ReportUnknown(installed, "balance.installedBase", problems, "lifetimeMonths", "contractUnitWeight");
                InstalledBaseBalance b = balance.InstalledBase;
                ReadInt(installed, "balance.installedBase", "lifetimeMonths", 12, 240, ref b.LifetimeMonths, problems);
                ReadFloat(installed, "balance.installedBase", "contractUnitWeight", 0f, 2f, ref b.ContractUnitWeight, problems);
            }

            JsonObject rating = Section(config, "rating", problems);
            if (rating != null)
            {
                ReportUnknown(rating, "balance.rating", problems, "ibMaxBonus", "ibSaturationShare", "sharedWeight", "earlyBonus", "earlyDurationMonths",
                    "earlyAdopterSlots", "churnPenalty", "minSocketLifeMonths", "churnDurationMonths", "churnMinBaseShare", "customOnSameBaseIsCompatible",
                    "industriesWeight", "minMult", "maxMult");
                RatingBalance r = balance.Rating;
                const string path = "balance.rating";
                ReadFloat(rating, path, "ibMaxBonus", 0f, 0.5f, ref r.IbMaxBonus, problems);
                ReadFloat(rating, path, "ibSaturationShare", 0.01f, 1f, ref r.IbSaturationShare, problems);
                ReadFloat(rating, path, "sharedWeight", 0f, 1f, ref r.SharedWeight, problems);
                ReadFloat(rating, path, "earlyBonus", 0f, 0.5f, ref r.EarlyBonus, problems);
                ReadInt(rating, path, "earlyDurationMonths", 1, 240, ref r.EarlyDurationMonths, problems);
                ReadInt(rating, path, "earlyAdopterSlots", 1, 10, ref r.EarlyAdopterSlots, problems);
                ReadFloat(rating, path, "churnPenalty", 0f, 0.5f, ref r.ChurnPenalty, problems);
                ReadInt(rating, path, "minSocketLifeMonths", 1, 240, ref r.MinSocketLifeMonths, problems);
                ReadInt(rating, path, "churnDurationMonths", 1, 120, ref r.ChurnDurationMonths, problems);
                ReadFloat(rating, path, "churnMinBaseShare", 0f, 1f, ref r.ChurnMinBaseShare, problems);
                ReadBool(rating, path, "customOnSameBaseIsCompatible", ref r.CustomOnSameBaseIsCompatible, problems);
                ReadFloat(rating, path, "industriesWeight", 0f, 1f, ref r.IndustriesWeight, problems);
                ReadFloat(rating, path, "minMult", 0.5f, 1f, ref r.MinMult, problems);
                ReadFloat(rating, path, "maxMult", 1f, 2f, ref r.MaxMult, problems);
            }

            JsonObject oem = Section(config, "oem", problems);
            if (oem != null)
            {
                ReportUnknown(oem, "balance.oem", problems, "mode", "contractTypes", "marketAverageMinGenOrder", "adoptBonus", "lagPenalty1",
                    "lagPenalty2", "fullPlatformBonus", "oemLoyaltyBonus", "minChanceMult", "maxChanceMult");
                OemBalance o = balance.Oem;
                const string path = "balance.oem";
                ReadMode(oem, path, ref o.Mode, problems);
                ReadContractTypes(oem, path, ref o.ContractTypes, problems);
                ReadInt(oem, path, "marketAverageMinGenOrder", 0, 9, ref o.MarketAverageMinGenOrder, problems);
                ReadFloat(oem, path, "adoptBonus", 0f, 0.5f, ref o.AdoptBonus, problems);
                ReadFloat(oem, path, "lagPenalty1", 0f, 0.5f, ref o.LagPenalty1, problems);
                ReadFloat(oem, path, "lagPenalty2", 0f, 0.5f, ref o.LagPenalty2, problems);
                ReadFloat(oem, path, "fullPlatformBonus", 0f, 0.2f, ref o.FullPlatformBonus, problems);
                ReadFloat(oem, path, "oemLoyaltyBonus", 0f, 0.5f, ref o.OemLoyaltyBonus, problems);
                ReadFloat(oem, path, "minChanceMult", 0f, 1f, ref o.MinChanceMult, problems);
                ReadFloat(oem, path, "maxChanceMult", 1f, 5f, ref o.MaxChanceMult, problems);
            }

            return balance;
        }

        // ---- reading ----

        private static JsonObject Section(JsonObject config, string name, List<string> problems)
        {
            return JsonFields.Object(config, name, "balance", false, problems);
        }

        private static void ReportUnknown(JsonObject obj, string path, List<string> problems, params string[] known)
        {
            if (problems == null) return;
            foreach (string member in JsonFields.Unknown(obj, known)) problems.Add(path + "." + member + ": unknown member is ignored");
        }

        private static void ReadInt(JsonObject obj, string path, string name, int min, int max, ref int target, List<string> problems)
        {
            if (JsonFields.Get(obj, name) == null) return;   // absent: the default stays

            int value;
            var local = new List<string>();
            if (!JsonFields.Int(obj, name, path, false, out value, local))
            {
                Keep(local, problems, Text(target));
                return;
            }

            if (value < min || value > max)
            {
                Report(problems, path + "." + name + ": " + Text(value) + " is outside " + Text(min) + ".." + Text(max) + ", the default " + Text(target) + " is used");
                return;
            }
            target = value;
        }

        private static void ReadFloat(JsonObject obj, string path, string name, float min, float max, ref float target, List<string> problems)
        {
            if (JsonFields.Get(obj, name) == null) return;

            double value;
            var local = new List<string>();
            if (!JsonFields.Number(obj, name, path, false, out value, local))
            {
                Keep(local, problems, Text(target));
                return;
            }

            if (value < min || value > max)
            {
                Report(problems, path + "." + name + ": " + Text(value) + " is outside " + Text(min) + ".." + Text(max) + ", the default " + Text(target) + " is used");
                return;
            }
            target = (float)value;
        }

        private static void ReadBool(JsonObject obj, string path, string name, ref bool target, List<string> problems)
        {
            if (JsonFields.Get(obj, name) == null) return;

            bool value;
            var local = new List<string>();
            if (!JsonFields.Bool(obj, name, path, false, out value, local))
            {
                Keep(local, problems, target ? "true" : "false");
                return;
            }
            target = value;
        }

        private static void ReadMode(JsonObject obj, string path, ref OemMode target, List<string> problems)
        {
            if (JsonFields.Get(obj, "mode") == null) return;

            string text;
            var local = new List<string>();
            if (!JsonFields.String(obj, "mode", path, false, out text, local))
            {
                Keep(local, problems, "'" + target + "'");
                return;
            }

            if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase)) target = OemMode.Auto;
            else if (string.Equals(text, "marketAverage", StringComparison.OrdinalIgnoreCase)) target = OemMode.MarketAverage;
            else Report(problems, path + ".mode: '" + text + "' is not 'auto' or 'marketAverage', the default '" + target + "' is used");
        }

        private static void ReadContractTypes(JsonObject obj, string path, ref int[] target, List<string> problems)
        {
            if (JsonFields.Get(obj, "contractTypes") == null) return;

            var local = new List<string>();
            JsonArray array = JsonFields.Array(obj, "contractTypes", path, false, local);
            if (array == null)
            {
                Keep(local, problems, "list");
                return;
            }

            var result = new List<int>();
            for (int i = 0; i < array.Count; i++)
            {
                var scalar = array[i] as JsonValue;
                int value;
                if (scalar == null || scalar.Kind != JsonKind.Number || !scalar.TryGetInt32(out value) || value < 0 || value > 2)
                {
                    Report(problems, path + ".contractTypes[" + Text(i) + "]: expected 0 (Calculator), 1 (Computer) or 2 (Console); the default list is used");
                    return;
                }
                if (!result.Contains(value)) result.Add(value);
            }
            target = result.ToArray();
        }

        /// <summary>A reader that failed: its message(s) plus what stays in force.</summary>
        private static void Keep(List<string> local, List<string> problems, string keptValue)
        {
            if (problems == null) return;
            foreach (string line in local) problems.Add(line + " - the default " + keptValue + " is used");
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Text(double value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static void Report(List<string> problems, string line)
        {
            if (problems != null) problems.Add(line);
        }
    }
}
