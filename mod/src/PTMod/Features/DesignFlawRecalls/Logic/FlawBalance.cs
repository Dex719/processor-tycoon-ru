using System.Collections.Generic;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The numbers of the feature: the typed form of <c>Content/design-flaw-recalls/flaws.json</c> (FR-15, NFR-7). Every formula of
    /// design.md ("Formulas and balance") takes its coefficients from here. The values below are the defaults for a missing member or a broken
    /// file and are the same as in the shipped <c>flaws.json</c> (a test compares them), so the feature works with nothing on disk.
    /// <see cref="FlawContentValidator"/> fills the object from the merged config of <c>ModContent.LoadConfig</c>, checks every range
    /// (design.md, "Security") and keeps the default of anything it rejects. The members are public fields so that the offline simulation
    /// and the tests can change them. Arrays by severity are indexed by <c>(int)FlawSeverity</c> (0 Minor, 1 Major, 2 Critical).
    /// No Unity and no game types: this file is compiled by the unit tests too.
    /// </summary>
    public sealed class FlawBalance
    {
        /// <summary>The number of severities: the length of every array "by severity".</summary>
        public const int SeverityCount = 3;

        /// <summary>The number of difficulty levels (<c>Player.DifficultyLevel</c> 0..5): the length of <see cref="RiskBalance.DifficultyMultiplier"/>.</summary>
        public const int DifficultyCount = 6;

        /// <summary>The length of the weights of an AI reaction: Recall, Stepping, Patch, Ignore (<c>FlawResponseBiasOrder</c>).</summary>
        public const int ResponseCount = 4;

        /// <summary>The first and the last year content may name (design.md, "Security").</summary>
        public const int MinYear = 1971, MaxYear = 2030;

        /// <summary>One row of the era table: the multiplier of the risk for the CPUs released in <c>[FromYear, ToYear]</c>.</summary>
        public sealed class EraRow
        {
            public int FromYear;
            public int ToYear;
            public float Multiplier = 1f;

            public EraRow()
            {
            }

            public EraRow(int fromYear, int toYear, float multiplier)
            {
                FromYear = fromYear;
                ToYear = toYear;
                Multiplier = multiplier;
            }
        }

        /// <summary>The probability of a hidden flaw (FR-2, formula 1).</summary>
        public sealed class RiskBalance
        {
            /// <summary>The probability of a flaw in a perfect design (<c>b.base</c>).</summary>
            public float Base = 0.02f;

            /// <summary>The weight of <c>CpuBase.FailureRate</c> (<c>kFailureRate</c>).</summary>
            public float KFailureRate = 0.20f;

            /// <summary>The weight of the overclock factor <c>OC</c> (<c>kOverclock</c>).</summary>
            public float KOverclock = 0.30f;

            /// <summary>The weight of the overheating factor <c>TH</c> (<c>kThermal</c>).</summary>
            public float KThermal = 0.60f;

            /// <summary>The weight of the immaturity of the process node <c>NI</c> (<c>kNodeImmaturity</c>).</summary>
            public float KNodeImmaturity = 0.08f;

            /// <summary>The weight of the rush project (<c>RS</c>, FR-20): added to the raw probability for a CPU created with a rush project.</summary>
            public float KRush = 0.06f;

            /// <summary>The largest <c>OC</c>: a design is overclocked by at most this much over the ceiling of the owner.</summary>
            public float OverclockCap = 0.5f;

            /// <summary><c>NI</c> of a node the manufacturer has no record of.</summary>
            public float UnknownNodeImmaturity = 0.5f;

            /// <summary>The ceiling of the probability.</summary>
            public float PMax = 0.5f;

            /// <summary>The multiplier of the probability for an AI company.</summary>
            public float AiRiskMultiplier = 1.0f;

            /// <summary>The multiplier by <c>Player.DifficultyLevel</c> 0..5.</summary>
            public float[] DifficultyMultiplier = { 0.5f, 0.75f, 1.0f, 1.15f, 1.3f, 1.5f };

            /// <summary>The multiplier by the release year; a year no row covers counts as 1.0.</summary>
            public List<EraRow> Era = new List<EraRow>
            {
                new EraRow(1971, 1984, 0.8f),
                new EraRow(1985, 1999, 1.0f),
                new EraRow(2000, 2030, 1.1f),
            };
        }

        /// <summary>The choice of the severity (FR-3, formula 2).</summary>
        public sealed class SeverityBalance
        {
            /// <summary>The weights of Minor, Major, Critical at p = 0.</summary>
            public float[] BaseWeights = { 0.60f, 0.30f, 0.10f };

            /// <summary>The weight of Major grows by this factor times p.</summary>
            public float ShiftMajor = 2.0f;

            /// <summary>The weight of Critical grows by this factor times p.</summary>
            public float ShiftCritical = 4.0f;
        }

        /// <summary>The leak rates per month of the cover-ups (FR-9, FR-7).</summary>
        public sealed class LeakRateBalance
        {
            /// <summary>An ignored flaw nobody outside knows about.</summary>
            public float Ignore = 0.06f;

            /// <summary>A quiet stepping that is still being made.</summary>
            public float QuietStepping = 0.03f;

            /// <summary>The tail after a quiet stepping was finished.</summary>
            public float Tail = 0.015f;
        }

        /// <summary>The discovery of a hidden flaw (FR-4, formula 3).</summary>
        public sealed class DiscoveryBalance
        {
            /// <summary>The monthly chance of discovery by severity at no sales.</summary>
            public float[] HBase = { 0.02f, 0.04f, 0.07f };

            /// <summary>The weight of the sold units in <c>h</c>.</summary>
            public float KUnits = 1.0f;

            /// <summary>The unit count at which the sales double the chance (log scale).</summary>
            public float UnitsRef = 100000f;

            /// <summary>The ceiling of the monthly chance.</summary>
            public float HMax = 0.35f;

            /// <summary>After this many months since the release the flaw is found for sure (if anything was sold).</summary>
            public int[] MaxHiddenMonths = { 48, 36, 24 };

            /// <summary>The probability that the company's own engineers find the flaw (otherwise outsiders do).</summary>
            public float[] QInternal = { 0.65f, 0.55f, 0.40f };

            /// <summary>A hidden flaw expires this many months after the CPU left the market.</summary>
            public int ExpireAfterRetiredMonths = 36;

            /// <summary>A hidden flaw expires after this many months without a sale.</summary>
            public int ExpireNoSalesMonths = 24;

            public LeakRateBalance LeakRate = new LeakRateBalance();

            /// <summary>How many months a leak is possible after a quiet stepping was finished.</summary>
            public int LeakTailMonths = 12;
        }

        /// <summary>The cost of the recall (FR-6).</summary>
        public sealed class RecallBalance
        {
            /// <summary>The share of the sold units that come back, by severity.</summary>
            public float[] ReturnRate = { 0.10f, 0.25f, 0.45f };

            /// <summary>The share of the price the logistics of a returned unit cost.</summary>
            public float LogisticsShareOfPrice = 0.15f;

            /// <summary>A fixed cost of a recall at inflation 1.0.</summary>
            public float FixedCostBase = 2000000f;

            /// <summary>The share of the unit cost the rework of the stock costs.</summary>
            public float StockReworkShare = 0.5f;

            /// <summary>The recall never costs more than this share of the revenue of the last 12 months.</summary>
            public float CapShareOfRevenue12m = 0.30f;
        }

        /// <summary>The revision of the design, "stepping" (FR-7).</summary>
        public sealed class SteppingBalance
        {
            /// <summary>The cost of the revision as a share of <c>DevelopmentCost</c>, by severity.</summary>
            public float[] CostShareOfDevCost = { 0.15f, 0.25f, 0.35f };

            /// <summary>The length of the revision in days, by severity.</summary>
            public int[] Days = { 60, 90, 120 };

            /// <summary>The claims after a public revision, as a share of the recall cost.</summary>
            public float ResidualShareOfRecall = 0.30f;
        }

        /// <summary>The software patch (FR-8).</summary>
        public sealed class PatchBalance
        {
            /// <summary>The cost of the patch as a share of <c>DevelopmentCost</c>.</summary>
            public float CostShareOfDevCost = 0.03f;

            /// <summary>A fixed cost of the patch at inflation 1.0.</summary>
            public float FixedCostBase = 500000f;

            /// <summary>How long the patch limits the damage, in months, by severity.</summary>
            public int[] Months = { 12, 18, 24 };
        }

        /// <summary>The consequences of ignoring a public flaw (FR-9).</summary>
        public sealed class IgnoreBalance
        {
            /// <summary>The chance that an OEM contract with the CPU is cancelled, by severity.</summary>
            public float[] OemCancel = { 0.10f, 0.30f, 0.60f };

            /// <summary>The multiplier of <c>contract.signChance</c> after the flaw was ignored, by severity.</summary>
            public float[] SignChance = { 0.95f, 0.85f, 0.70f };

            /// <summary>How long the multiplier of <see cref="SignChance"/> acts, in months.</summary>
            public int SignChanceMonths = 12;

            /// <summary>The chance of a class action in one month, by severity.</summary>
            public float[] ClassAction = { 0.0f, 0.05f, 0.10f };

            /// <summary>The lawsuit as a share of the revenue of the units sold.</summary>
            public float SuitShareOfRevenue = 0.03f;

            /// <summary>How many months the reputation drains.</summary>
            public int DrainMonths = 12;

            /// <summary>The change of the reputation per month while it drains (negative).</summary>
            public int DrainPerMonth = -1;
        }

        /// <summary>The one-time hit to the popularity of the CPU by the reaction (FR-10), as a share, by severity.</summary>
        public sealed class PopHitBalance
        {
            public float[] Public = { 0.02f, 0.04f, 0.08f };
            public float[] Recall = { 0.0f, 0.01f, 0.02f };
            public float[] Stepping = { 0.01f, 0.02f, 0.04f };
            public float[] Patch = { 0.01f, 0.02f, 0.03f };
            public float[] Ignore = { 0.05f, 0.12f, 0.20f };
        }

        /// <summary>The reaction to a known flaw (FR-5..FR-9).</summary>
        public sealed class ResponsesBalance
        {
            /// <summary>The days the player has to choose.</summary>
            public int TimerDays = 30;

            /// <summary>The multiplier of <c>contract.signChance</c> while a public flaw waits for the decision (range [0.5, 1]).</summary>
            public float PendingSignChanceMultiplier = 0.8f;

            /// <summary>The margin on top of <see cref="TimerDays"/> the "waiting" entry of the channel lives (covers the delay of the mail).</summary>
            public int PendingGraceDays = 14;

            public RecallBalance Recall = new RecallBalance();
            public SteppingBalance Stepping = new SteppingBalance();
            public PatchBalance Patch = new PatchBalance();
            public IgnoreBalance Ignore = new IgnoreBalance();
            public PopHitBalance PopHit = new PopHitBalance();
        }

        /// <summary>The changes of the brand reputation (FR-10, formula 7), points, by severity.</summary>
        public sealed class ReputationBalance
        {
            /// <summary>The flaw became public.</summary>
            public int[] PublicBase = { -2, -5, -10 };

            public int[] Recall = { 1, 2, 4 };
            public int[] SteppingPublic = { 0, -1, -3 };
            public int[] SteppingQuiet = { 0, 0, 0 };
            public int[] Patch = { -1, -2, -4 };
            public int[] IgnorePublic = { -3, -8, -15 };

            /// <summary>A cover-up that leaked multiplies the sum of the base and the reaction by this.</summary>
            public float CoverUpMultiplier = 2.0f;
        }

        /// <summary>The effect on the press reviews (FR-18), by severity.</summary>
        public sealed class PressBalance
        {
            /// <summary>Added to the overall score of a review of a CPU with a known flaw (the press limit is plus or minus 20).</summary>
            public int[] OverallAdjustment = { -8, -15, -15 };
        }

        /// <summary>The rush project (FR-20, formula 11).</summary>
        public sealed class RushBalance
        {
            /// <summary>The share of the project time that is cut.</summary>
            public float TimeCut = 0.25f;

            /// <summary>The project cost is multiplied by this.</summary>
            public float CostMultiplier = 1.10f;
        }

        public RiskBalance Risk = new RiskBalance();
        public SeverityBalance Severity = new SeverityBalance();
        public DiscoveryBalance Discovery = new DiscoveryBalance();
        public ResponsesBalance Responses = new ResponsesBalance();
        public ReputationBalance Reputation = new ReputationBalance();
        public PressBalance Press = new PressBalance();

        /// <summary>The kinds of flaw. Never empty: the validator falls back to <see cref="FlawTypeDef.FallbackArithmetic"/>.</summary>
        public List<FlawTypeDef> Types = FlawTypeDef.DefaultTypes();

        public AiPolicyDef AiPolicy = new AiPolicyDef();
        public RushBalance Rush = new RushBalance();

        /// <summary>The type with this id, or <c>null</c> (a record can name a type that a content override has removed since).</summary>
        public FlawTypeDef FindType(string typeId)
        {
            if (Types == null) return null;
            foreach (FlawTypeDef type in Types)
            {
                if (type != null && type.Id == typeId) return type;
            }
            return null;
        }

        /// <summary>A balance with the defaults of the code (the same as the shipped <c>flaws.json</c>).</summary>
        public static FlawBalance Defaults()
        {
            return new FlawBalance();
        }
    }

    /// <summary>
    /// The weights of the reactions of an AI company (<c>aiPolicy</c> of <c>flaws.json</c>, FR-11, formula 8). The arrays are ordered
    /// Recall, Stepping, Patch, Ignore - the order of <c>FlawResponseBiasOrder</c>. The weights are relative: the policy normalises them.
    /// </summary>
    public sealed class AiPolicyDef
    {
        /// <summary>The weights for a public flaw.</summary>
        public float[] Public = { 0.30f, 0.30f, 0.25f, 0.15f };

        /// <summary>The weights for a flaw only the company knows.</summary>
        public float[] Internal = { 0.15f, 0.45f, 0.15f, 0.25f };

        /// <summary>An AI company recalls only when its cash headroom is at least this many times the cost of the recall.</summary>
        public float RecallRequiresHeadroomFactor = 1.5f;

        /// <summary>The weights for the situation: <see cref="Public"/> or <see cref="Internal"/>.</summary>
        public float[] WeightsFor(bool isPublic)
        {
            return isPublic ? Public : Internal;
        }
    }

    /// <summary>
    /// One kind of flaw (an element of <c>types</c> in <c>flaws.json</c>, FR-3). A type that fails the validation is skipped; the id is the
    /// key of <c>FlawRecordDto.typeId</c> and of the public API (<c>FlawTypeIds</c> names the built-in ones).
    /// </summary>
    public sealed class FlawTypeDef
    {
        /// <summary>The id of the built-in fallback type used when the content has no valid type at all.</summary>
        public const string FallbackId = "arith";

        public string Id;

        /// <summary>The key of the name in <c>ModL10n</c>; starts with <c>dfr.</c> (the prefix of the feature).</summary>
        public string NameKey;

        /// <summary>The type can be rolled for a CPU released in <c>[FromYear, ToYear]</c>.</summary>
        public int FromYear;

        public int ToYear;

        /// <summary>The base weight of the type in the choice.</summary>
        public float Weight;

        /// <summary>The weight grows by this factor times <c>OC</c>.</summary>
        public float WeightByOverclock;

        /// <summary>The weight grows by this factor times <c>TH</c>.</summary>
        public float WeightByThermal;

        /// <summary>A software patch exists for the type (from <see cref="PatchFromYear"/>).</summary>
        public bool Patchable = true;

        /// <summary>The first year a patch is possible.</summary>
        public int PatchFromYear;

        /// <summary>The share of the performance (IPS) the patch costs, by severity (at most 0.5, so the multiplier of the channel stays at 0.5 or more).</summary>
        public float[] IpsLoss = { 0f, 0f, 0f };

        /// <summary>The CPUs of the type can be recalled.</summary>
        public bool Recallable = true;

        /// <summary>A copy that shares nothing with this one.</summary>
        public FlawTypeDef Clone()
        {
            var copy = (FlawTypeDef)MemberwiseClone();
            copy.IpsLoss = IpsLoss == null ? null : (float[])IpsLoss.Clone();
            return copy;
        }

        /// <summary>The four built-in types, the same as in the shipped <c>flaws.json</c>.</summary>
        public static List<FlawTypeDef> DefaultTypes()
        {
            return new List<FlawTypeDef>
            {
                new FlawTypeDef
                {
                    Id = "arith", NameKey = "dfr.type.arith", FromYear = 1971, ToYear = 2030, Weight = 3f,
                    WeightByOverclock = 0f, WeightByThermal = 0f,
                    Patchable = true, PatchFromYear = 1995, IpsLoss = new[] { 0.03f, 0.06f, 0.10f }, Recallable = true,
                },
                new FlawTypeDef
                {
                    Id = "stability", NameKey = "dfr.type.stability", FromYear = 1971, ToYear = 2030, Weight = 3f,
                    WeightByOverclock = 5f, WeightByThermal = 0f,
                    Patchable = true, PatchFromYear = 1990, IpsLoss = new[] { 0.08f, 0.12f, 0.18f }, Recallable = true,
                },
                new FlawTypeDef
                {
                    Id = "thermal", NameKey = "dfr.type.thermal", FromYear = 1971, ToYear = 2030, Weight = 2f,
                    WeightByOverclock = 0f, WeightByThermal = 5f,
                    Patchable = true, PatchFromYear = 1985, IpsLoss = new[] { 0.06f, 0.10f, 0.15f }, Recallable = true,
                },
                new FlawTypeDef
                {
                    Id = "security", NameKey = "dfr.type.security", FromYear = 2015, ToYear = 2030, Weight = 2f,
                    WeightByOverclock = 0f, WeightByThermal = 0f,
                    Patchable = true, PatchFromYear = 2015, IpsLoss = new[] { 0.05f, 0.12f, 0.25f }, Recallable = true,
                },
            };
        }

        /// <summary>The built-in <c>arith</c> type, the fallback when the content has no valid type.</summary>
        public static FlawTypeDef FallbackArithmetic()
        {
            return DefaultTypes()[0];
        }
    }
}
