using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>The coefficients of the valuation of a company (<c>valuation</c> in balance.json; design.md, "Оценка").</summary>
    public sealed class ValuationConfig
    {
        /// <summary>The factory counts <c>kFab</c> times its sale cost.</summary>
        public double KFab = 2.0;

        /// <summary>The technology gap counts <c>kTech</c> times its research cost.</summary>
        public double KTech = 0.5;

        /// <summary>The CPUs count <c>kCpu</c> times their margin income.</summary>
        public double KCpu = 1.0;

        /// <summary>How many past months of CPU income are summed.</summary>
        public int CpuIncomeMonths = 12;

        /// <summary>The premium for the market share: the value is multiplied by <c>1 + kShare × share</c>.</summary>
        public double KShare = 1.0;

        /// <summary>The minimal valuation <c>E_min</c> as a share of the target's debt limit.</summary>
        public double FloorDebtLimitShare = 0.25;

        /// <summary>The CPUs of a bankrupt company's snapshot count this much of their income.</summary>
        public double BankruptSnapshotCpuFactor = 0.5;

        /// <summary>A price is rounded UP to a multiple of this.</summary>
        public double RoundTo = 1000.0;
    }

    /// <summary>The purchase of the assets of a bankrupt company at the auction (<c>deals.bankrupt</c>).</summary>
    public sealed class BankruptDealConfig
    {
        public double Multiplier = 0.4;

        /// <summary>The cash of the company comes with the deal (a bankrupt company's does not by default).</summary>
        public bool AssumeCash;

        /// <summary>How long the auction is open after the bankruptcy.</summary>
        public int AuctionDays = 365;
    }

    /// <summary>The purchase of a company in default (<c>deals.distressed</c>).</summary>
    public sealed class DistressedDealConfig
    {
        public double Multiplier = 0.7;

        public bool AssumeCash = true;
    }

    /// <summary>The hostile takeover of a healthy company (<c>deals.hostile</c>).</summary>
    public sealed class HostileDealConfig
    {
        public double Multiplier = 1.5;

        /// <summary>The multiplier for a target with a bigger market share than the player's.</summary>
        public double BiggerTargetMultiplier = 1.75;

        /// <summary>The days between two hostile takeovers.</summary>
        public int CooldownDays = 365;

        public bool AssumeCash = true;
    }

    /// <summary>The prices and rules of the deals (<c>deals</c> in balance.json).</summary>
    public sealed class DealsConfig
    {
        public BankruptDealConfig Bankrupt = new BankruptDealConfig();
        public DistressedDealConfig Distressed = new DistressedDealConfig();
        public HostileDealConfig Hostile = new HostileDealConfig();

        /// <summary>The money the player must still have after a deal.</summary>
        public double MinCashAfter;
    }

    /// <summary>The minority stakes and the rescue orders (<c>stake</c> in balance.json).</summary>
    public sealed class StakeConfig
    {
        /// <summary>The shares the dialog offers, ascending, each above 0 and not above <see cref="MaxShare"/>. Empty: no stakes can be bought.</summary>
        public double[] Options = { 0.10, 0.25, 0.49 };

        /// <summary>The most the player may own of one company: below 0.5, so a stake never gives control.</summary>
        public double MaxShare = 0.49;

        /// <summary>The price multiplier for a healthy company.</summary>
        public double HealthyMultiplier = 1.0;

        /// <summary>The price multiplier for a company in default.</summary>
        public double DistressedMultiplier = 0.6;

        /// <summary>The share of last month's profit paid out as dividends.</summary>
        public double PayoutRatio = 0.5;

        /// <summary>A rescue brings the target's balance to this share of its debt limit, negative: -0.5 is half way to the default threshold. In (-1; 0].</summary>
        public double RescueTargetDebtLimitShare = -0.5;

        /// <summary>True when at least one share can be bought.</summary>
        public bool StakesEnabled
        {
            get { return MaxShare > 0.0 && Options != null && Options.Length > 0; }
        }
    }

    /// <summary>The change of the player's reputation per deal type (<c>reputation</c> in balance.json), in points.</summary>
    public sealed class ReputationConfig
    {
        public int Hostile = -5;
        public int Rescue = 3;
        public int Distressed;
        public int Bankrupt;
    }

    /// <summary>
    /// The numbers of the feature: the typed form of <c>Content/rival-acquisitions/balance.json</c> (FR-14, NFR-7). Everything the rules use
    /// comes from here; the values below are only the defaults for a missing or broken file and are the same as in the shipped file and in
    /// design.md ("Контент: balance.json"). The formulas themselves are fixed in the code, the file holds multipliers and limits.
    /// <para>
    /// <see cref="FromJson"/> reads the merged config of <c>ModContent.LoadConfig</c> and keeps the default of any member that is missing, has
    /// the wrong type or lies outside its range, saying so in the problem list for <c>ModLog</c>. <c>stake.maxShare</c> of 0.5 or more is refused
    /// (a stake never gives control), the stake options above <c>maxShare</c> are dropped, and <c>stake.rescueTargetDebtLimitShare</c> outside
    /// (-1; 0] becomes -0.5. The members are public fields so that the tests and the offline simulation can change them.
    /// </para>
    /// </summary>
    public sealed class AcquisitionsBalance
    {
        /// <summary>The <c>schema</c> of balance.json this version reads (the argument of <c>ModContent.LoadConfig</c>).</summary>
        public const int SchemaVersion = 1;

        /// <summary>The content file name (without the extension), in the folder <c>rival-acquisitions</c>.</summary>
        public const string FileName = "balance";

        /// <summary>The stake options of the code, used when the file gives none that are usable.</summary>
        private static readonly double[] DefaultOptions = { 0.10, 0.25, 0.49 };

        public ValuationConfig Valuation = new ValuationConfig();
        public DealsConfig Deals = new DealsConfig();
        public StakeConfig Stake = new StakeConfig();
        public ReputationConfig Reputation = new ReputationConfig();

        /// <summary>The most passes of <c>ResearchSector.Update(true)</c> when technologies are unlocked for the player (FR-7).</summary>
        public int TechUnlockMaxPasses = 20;

        /// <summary>A balance with the defaults of the code (the same as the shipped <c>balance.json</c>).</summary>
        public static AcquisitionsBalance Defaults()
        {
            return new AcquisitionsBalance();
        }

        /// <summary>
        /// Reads the merged <c>balance.json</c> (<c>ModContent.LoadConfig</c>, schema 1). Never throws and never returns null: a section or a
        /// member that is missing keeps its default; a member of the wrong type or outside its range keeps its default too and adds a line
        /// "balance.stake.maxShare: ..." to <paramref name="problems"/> (may be null); an unknown member is reported and ignored.
        /// </summary>
        public static AcquisitionsBalance FromJson(JsonObject config, List<string> problems)
        {
            var balance = new AcquisitionsBalance();
            if (config == null) return balance;

            try
            {
                var reader = new Reader(problems);
                reader.Unknown(config, "balance", "schema", "valuation", "deals", "stake", "reputation", "techUnlockMaxPasses");

                ReadValuation(reader, config, balance.Valuation);
                ReadDeals(reader, config, balance.Deals);
                ReadStake(reader, config, balance.Stake);
                ReadReputation(reader, config, balance.Reputation);
                reader.Int(config, "balance", "techUnlockMaxPasses", 1, 100, ref balance.TechUnlockMaxPasses);
            }
            catch (Exception e)
            {
                // The readers do not throw; this is the last line of defence for the "never throws" promise.
                if (problems != null) problems.Add("balance: reading failed (" + e.GetType().Name + ": " + e.Message + "), the defaults are used");
                return new AcquisitionsBalance();
            }
            return balance;
        }

        private static void ReadValuation(Reader reader, JsonObject config, ValuationConfig v)
        {
            JsonObject section = reader.Section(config, "balance", "valuation");
            if (section == null) return;

            const string path = "balance.valuation";
            reader.Unknown(section, path, "kFab", "kTech", "kCpu", "cpuIncomeMonths", "kShare", "floorDebtLimitShare", "bankruptSnapshotCpuFactor", "roundTo");
            reader.Number(section, path, "kFab", 0.0, 10.0, ref v.KFab);
            reader.Number(section, path, "kTech", 0.0, 10.0, ref v.KTech);
            reader.Number(section, path, "kCpu", 0.0, 10.0, ref v.KCpu);
            reader.Int(section, path, "cpuIncomeMonths", 1, 60, ref v.CpuIncomeMonths);
            reader.Number(section, path, "kShare", 0.0, 10.0, ref v.KShare);
            reader.Number(section, path, "floorDebtLimitShare", 0.0, 10.0, ref v.FloorDebtLimitShare);
            reader.Number(section, path, "bankruptSnapshotCpuFactor", 0.0, 1.0, ref v.BankruptSnapshotCpuFactor);
            reader.Number(section, path, "roundTo", 1.0, 1000000.0, ref v.RoundTo);
        }

        private static void ReadDeals(Reader reader, JsonObject config, DealsConfig d)
        {
            JsonObject section = reader.Section(config, "balance", "deals");
            if (section == null) return;

            const string path = "balance.deals";
            reader.Unknown(section, path, "bankrupt", "distressed", "hostile", "minCashAfter");
            reader.Number(section, path, "minCashAfter", 0.0, 1e12, ref d.MinCashAfter);

            JsonObject bankrupt = reader.Section(section, path, "bankrupt");
            if (bankrupt != null)
            {
                const string p = "balance.deals.bankrupt";
                reader.Unknown(bankrupt, p, "multiplier", "assumeCash", "auctionDays");
                reader.Number(bankrupt, p, "multiplier", 0.0, 10.0, ref d.Bankrupt.Multiplier);
                reader.Bool(bankrupt, p, "assumeCash", ref d.Bankrupt.AssumeCash);
                reader.Int(bankrupt, p, "auctionDays", 0, 3650, ref d.Bankrupt.AuctionDays);
            }

            JsonObject distressed = reader.Section(section, path, "distressed");
            if (distressed != null)
            {
                const string p = "balance.deals.distressed";
                reader.Unknown(distressed, p, "multiplier", "assumeCash");
                reader.Number(distressed, p, "multiplier", 0.0, 10.0, ref d.Distressed.Multiplier);
                reader.Bool(distressed, p, "assumeCash", ref d.Distressed.AssumeCash);
            }

            JsonObject hostile = reader.Section(section, path, "hostile");
            if (hostile != null)
            {
                const string p = "balance.deals.hostile";
                reader.Unknown(hostile, p, "multiplier", "biggerTargetMultiplier", "cooldownDays", "assumeCash");
                reader.Number(hostile, p, "multiplier", 0.0, 10.0, ref d.Hostile.Multiplier);
                reader.Number(hostile, p, "biggerTargetMultiplier", 0.0, 10.0, ref d.Hostile.BiggerTargetMultiplier);
                reader.Int(hostile, p, "cooldownDays", 0, 3650, ref d.Hostile.CooldownDays);
                reader.Bool(hostile, p, "assumeCash", ref d.Hostile.AssumeCash);
            }
        }

        private static void ReadStake(Reader reader, JsonObject config, StakeConfig s)
        {
            JsonObject section = reader.Section(config, "balance", "stake");
            if (section == null) return;

            const string path = "balance.stake";
            reader.Unknown(section, path, "options", "maxShare", "healthyMultiplier", "distressedMultiplier", "payoutRatio", "rescueTargetDebtLimitShare");

            // maxShare first: the options are checked against the value that is in force. 0.5 and more is refused, whatever the file says.
            reader.Number(section, path, "maxShare", 0.0, 0.5, ref s.MaxShare, false, true);
            reader.Number(section, path, "healthyMultiplier", 0.0, 10.0, ref s.HealthyMultiplier);
            reader.Number(section, path, "distressedMultiplier", 0.0, 10.0, ref s.DistressedMultiplier);
            reader.Number(section, path, "payoutRatio", 0.0, 1.0, ref s.PayoutRatio);
            reader.Number(section, path, "rescueTargetDebtLimitShare", -1.0, 0.0, ref s.RescueTargetDebtLimitShare, true, false);

            // The defaults are checked against the maxShare in force too: a default option above it is not offered.
            s.Options = FilterByMax(DefaultOptions, s.MaxShare);
            ReadOptions(reader, section, path, s);
        }

        private static void ReadOptions(Reader reader, JsonObject section, string path, StakeConfig s)
        {
            JsonArray array = reader.Items(section, path, "options");
            if (array == null) return;   // absent or not an array: the defaults filtered by maxShare stay

            var options = new List<double>();
            for (int i = 0; i < array.Count; i++)
            {
                var scalar = array[i] as JsonValue;
                if (scalar == null || scalar.Kind != JsonKind.Number)
                {
                    reader.Add(path + ".options[" + i.ToString(CultureInfo.InvariantCulture) + "]: expected a number, the option is dropped");
                    continue;
                }

                double value = scalar.NumberValue;
                string where = path + ".options[" + i.ToString(CultureInfo.InvariantCulture) + "]: " + Reader.Text(value);
                if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
                {
                    reader.Add(where + " is not above 0, the option is dropped");
                }
                else if (value > s.MaxShare)
                {
                    reader.Add(where + " is above maxShare " + Reader.Text(s.MaxShare) + ", the option is dropped");
                }
                else if (options.Contains(value))
                {
                    reader.Add(where + " is repeated, the option is dropped");
                }
                else
                {
                    options.Add(value);
                }
            }

            if (array.Count == 0)
            {
                s.Options = new double[0];   // an explicit empty list: the player asked for no stakes
                return;
            }

            if (options.Count == 0)
            {
                reader.Add(path + ".options: no usable option, the default options up to maxShare are used");
                return;   // the filtered defaults stay
            }

            options.Sort();
            s.Options = options.ToArray();
        }

        private static double[] FilterByMax(double[] values, double max)
        {
            var result = new List<double>(values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > 0.0 && values[i] <= max) result.Add(values[i]);
            }
            return result.ToArray();
        }

        private static void ReadReputation(Reader reader, JsonObject config, ReputationConfig r)
        {
            JsonObject section = reader.Section(config, "balance", "reputation");
            if (section == null) return;

            const string path = "balance.reputation";
            reader.Unknown(section, path, "hostile", "rescue", "distressed", "bankrupt");
            reader.Int(section, path, "hostile", -100, 100, ref r.Hostile);
            reader.Int(section, path, "rescue", -100, 100, ref r.Rescue);
            reader.Int(section, path, "distressed", -100, 100, ref r.Distressed);
            reader.Int(section, path, "bankrupt", -100, 100, ref r.Bankrupt);
        }

        /// <summary>
        /// Typed reads of the members of a content object. A member that is absent (or JSON null) keeps the value in <c>target</c>; one with the
        /// wrong type or outside its range keeps it too and adds one English line "path.name: reason, the default X is used".
        /// </summary>
        private sealed class Reader
        {
            private readonly List<string> _problems;

            public Reader(List<string> problems)
            {
                _problems = problems;
            }

            public void Add(string line)
            {
                if (_problems != null) _problems.Add(line);
            }

            public static string Text(double value)
            {
                return value.ToString("0.####", CultureInfo.InvariantCulture);
            }

            private static JsonNode Member(JsonObject obj, string name)
            {
                JsonNode node = obj == null ? null : obj[name];
                return node == null || node.IsNull ? null : node;
            }

            private static string KindName(JsonNode node)
            {
                switch (node.Kind)
                {
                    case JsonKind.Bool: return "a boolean";
                    case JsonKind.Number: return "a number";
                    case JsonKind.String: return "a string";
                    case JsonKind.Object: return "an object";
                    case JsonKind.Array: return "an array";
                    default: return "null";
                }
            }

            public void Unknown(JsonObject obj, string path, params string[] known)
            {
                if (_problems == null || obj == null) return;
                foreach (KeyValuePair<string, JsonNode> pair in obj)
                {
                    if (Array.IndexOf(known, pair.Key) < 0) _problems.Add(path + "." + pair.Key + ": unknown member is ignored");
                }
            }

            /// <summary>A member that must be an object; null when absent or of another kind (a line is added then).</summary>
            public JsonObject Section(JsonObject parent, string path, string name)
            {
                JsonNode node = Member(parent, name);
                if (node == null) return null;

                var result = node as JsonObject;
                if (result == null) Add(path + "." + name + ": expected an object, got " + KindName(node) + " - the defaults are used");
                return result;
            }

            /// <summary>A member that must be an array; null when absent or of another kind (a line is added then).</summary>
            public JsonArray Items(JsonObject parent, string path, string name)
            {
                JsonNode node = Member(parent, name);
                if (node == null) return null;

                var result = node as JsonArray;
                if (result == null) Add(path + "." + name + ": expected an array, got " + KindName(node) + " - the default is used");
                return result;
            }

            public void Int(JsonObject obj, string path, string name, int min, int max, ref int target)
            {
                JsonNode node = Member(obj, name);
                if (node == null) return;

                var scalar = node as JsonValue;
                int value;
                if (scalar == null || scalar.Kind != JsonKind.Number || !scalar.TryGetInt32(out value))
                {
                    Add(path + "." + name + ": expected a whole number, got " + Describe(node) + ", the default " + target.ToString(CultureInfo.InvariantCulture) + " is used");
                    return;
                }
                if (value < min || value > max)
                {
                    Add(path + "." + name + ": " + value.ToString(CultureInfo.InvariantCulture) + " is outside " + min.ToString(CultureInfo.InvariantCulture) + ".."
                        + max.ToString(CultureInfo.InvariantCulture) + ", the default " + target.ToString(CultureInfo.InvariantCulture) + " is used");
                    return;
                }
                target = value;
            }

            public void Number(JsonObject obj, string path, string name, double min, double max, ref double target,
                bool minExclusive = false, bool maxExclusive = false)
            {
                JsonNode node = Member(obj, name);
                if (node == null) return;

                var scalar = node as JsonValue;
                if (scalar == null || scalar.Kind != JsonKind.Number || double.IsNaN(scalar.NumberValue) || double.IsInfinity(scalar.NumberValue))
                {
                    Add(path + "." + name + ": expected a number, got " + Describe(node) + ", the default " + Text(target) + " is used");
                    return;
                }

                double value = scalar.NumberValue;
                bool belowMin = minExclusive ? value <= min : value < min;
                bool aboveMax = maxExclusive ? value >= max : value > max;
                if (belowMin || aboveMax)
                {
                    Add(path + "." + name + ": " + Text(value) + " is outside the range " + (minExclusive ? "(" : "[") + Text(min) + "; " + Text(max)
                        + (maxExclusive ? ")" : "]") + ", the default " + Text(target) + " is used");
                    return;
                }
                target = value;
            }

            public void Bool(JsonObject obj, string path, string name, ref bool target)
            {
                JsonNode node = Member(obj, name);
                if (node == null) return;

                var scalar = node as JsonValue;
                if (scalar == null || scalar.Kind != JsonKind.Bool)
                {
                    Add(path + "." + name + ": expected true or false, got " + Describe(node) + ", the default " + (target ? "true" : "false") + " is used");
                    return;
                }
                target = scalar.BoolValue;
            }

            private static string Describe(JsonNode node)
            {
                var scalar = node as JsonValue;
                if (scalar != null && scalar.Kind == JsonKind.Number) return "the number " + Text(scalar.NumberValue);
                if (scalar != null && scalar.Kind == JsonKind.String) return "the text '" + scalar.StringValue + "'";
                return KindName(node);
            }
        }
    }
}
