using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The global numbers of the feature: the typed form of <c>Content/rival-personalities/config.json</c> (design.md, "Контент: config.json").
    /// The field initialisers are the shipped values, so a missing member or a missing file leaves the design numbers in place. The fields are
    /// public and mirror the JSON member names (the case matters), because <see cref="FromJson"/> fills them with <c>ModJson.Bind</c>.
    /// <para>
    /// <c>segmentPriceBand</c> is not here on purpose: the segment of a CPU is the core's business (<c>ModSegments.Classify</c>,
    /// <c>segments.priceBand</c> of <c>core.json</c>).
    /// </para>
    /// </summary>
    public sealed class RivalConfig
    {
        /// <summary>The multiplier d of the chance of a hostile reaction for the difficulty 0..5 (FR-6 item 6).</summary>
        public float[] difficultyMultipliers = { 0.4f, 0.7f, 1.0f, 1.2f, 1.4f, 1.6f };

        /// <summary>The largest chance of any reaction (FR-6 item 1).</summary>
        public float chanceCap = 0.9f;

        /// <summary>No hostile reaction in the first days of a game (FR-6 item 2).</summary>
        public int graceDays = 180;

        /// <summary>Planned or active hostile reactions at one time (FR-6 item 4).</summary>
        public int maxConcurrentHostile = 2;

        /// <summary>Hostile reactions executed in one game year (FR-6 item 4).</summary>
        public int maxHostilePerYear = 4;

        /// <summary>The warning before a reaction, in days after the intelligence mail is delivered: [lowest, highest] (FR-7 item 1).</summary>
        public int[] telegraphDays = { 14, 45 };

        /// <summary>The reaction is cancelled when its mail is not delivered within so many days (FR-7 item 9).</summary>
        public int mailWaitDays = 30;

        public AttitudeSettings attitude = new AttitudeSettings();
        public TriggerDeltaSettings triggerDeltas = new TriggerDeltaSettings();
        public TriggerThresholdSettings triggerThresholds = new TriggerThresholdSettings();
        public ReactionSettings reactions = new ReactionSettings();
        public PriceCutSettings priceCut = new PriceCutSettings();
        public FlawCautionSettings flawCaution = new FlawCautionSettings();
        public IntelFogSettings intelFog = new IntelFogSettings();
        public EventChooserSettings eventChooser = new EventChooserSettings();
        public AcquisitionSettings acquisition = new AcquisitionSettings();

        /// <summary>A new AI launch price is never below <c>RealUnitCost x (1 + this)</c> (formula 3).</summary>
        public float launchPriceFloorMargin = 0.05f;

        // ---- nested groups (the JSON objects) ----

        /// <summary>The scale of the attitude (FR-4).</summary>
        public sealed class AttitudeSettings
        {
            /// <summary>The borders of the levels: [Hostile, Rivalry, Respect, Partnership], strictly increasing.</summary>
            public int[] thresholds = { -60, -20, 20, 60 };

            /// <summary>The share of the distance to the baseline that the attitude covers every month.</summary>
            public float monthlyDrift = 0.10f;

            /// <summary>How many entries of the history are kept for one rival.</summary>
            public int historySize = 20;

            /// <summary>Hostile reactions need an attitude not above this value (unless the rule says <c>anyAttitude</c>).</summary>
            public int hostileGate = -20;

            /// <summary>The divisor of the attitude in the negotiation bonus to the interest: <c>round(a / divisor)</c>.</summary>
            public int negotiationDivisor = 5;

            /// <summary>The divisor of the attitude in the markup discount of a foundry partner.</summary>
            public int discountDivisor = 10;

            /// <summary>The largest markup discount, in points.</summary>
            public int discountMaxPoints = 10;

            /// <summary>An archetype is shown once the attitude is at least this (FR-11).</summary>
            public int revealAttitude = 20;
        }

        /// <summary>The change of the attitude for each observed action of the player (FR-4, FR-5).</summary>
        public sealed class TriggerDeltaSettings
        {
            public float StrongLaunch = -8f;
            public float ShareGain = -5f;
            public float PriceUndercut = -4f;
            public float OemLoss = -6f;
            public float TechLead = -3f;
            public float BusinessSigned = 15f;
            public float BusinessBrokenByPlayer = -25f;
            public float BusinessActiveMonthly = 2f;
        }

        /// <summary>When an action of the player counts as a trigger (FR-5, formula 12).</summary>
        public sealed class TriggerThresholdSettings
        {
            public float strongLaunchIpsRatio = 1.15f;
            public float shareGainPp = 3.0f;
            public float undercutPriceRatio = 0.85f;
            public float undercutIpsRatio = 0.90f;
            public int undercutCooldownDays = 90;
            public float minApprovalForBoost = 1.05f;
            public List<string> techLeadTypes = new List<string> { "Lithography", "Frequency", "Multicore" };
        }

        /// <summary>Price cut: the default depth of the blow and the floor above the unit cost (formula 4).</summary>
        public sealed class PriceCutReaction
        {
            public float depth = 0.08f;
            public float floorMargin = 0.08f;
        }

        /// <summary>Counter-launch: the boosts of the design rating and of the research funding (formulas 1, 2).</summary>
        public sealed class CounterLaunchReaction
        {
            public float approvalBoost = 1.35f;
            public float fundingBoost = 1.25f;
        }

        /// <summary>Poaching of engineers (formulas 6, 9, 10).</summary>
        public sealed class PoachReaction
        {
            public float rivalResearchSpeed = 1.10f;
            public int rivalBoostDays = 120;
            public int minCost = 250000;
            public int researchMonths = 2;
            public float underfundedChanceMult = 1.5f;
        }

        /// <summary>The friendly offer of a rival (FR-12).</summary>
        public sealed class PartnershipReaction
        {
            public int minAttitude = 40;
            public float monthlyChance = 0.10f;
            public int repeatMonths = 6;
        }

        /// <summary>The defaults of the reactions; an archetype overrides some of them in <c>reactionParams</c>.</summary>
        public sealed class ReactionSettings
        {
            public PriceCutReaction PriceCut = new PriceCutReaction();
            public CounterLaunchReaction CounterLaunch = new CounterLaunchReaction();
            public PoachReaction Poach = new PoachReaction();
            public PartnershipReaction Partnership = new PartnershipReaction();
        }

        /// <summary>The price cuts that other features ask for through the API (FR-13 item 6).</summary>
        public sealed class PriceCutSettings
        {
            /// <summary>A cut that its caller did not take back is taken off after so many days.</summary>
            public int externalMaxDays = 400;
        }

        /// <summary>The caution of an AI company after a public design flaw (FR-17, formula 16).</summary>
        public sealed class FlawCautionSettings
        {
            /// <summary>The acceptable failure rate is multiplied by this while the caution lasts.</summary>
            public float factor = 0.85f;

            /// <summary>How long the caution lasts, in months.</summary>
            public int months = 24;

            /// <summary>The least severity of a flaw that starts the caution: <c>Minor</c>, <c>Major</c> or <c>Critical</c>.</summary>
            public string minSeverity = "Major";
        }

        /// <summary>The fog of the intelligence panel (FR-16, formula 15).</summary>
        public sealed class IntelFogSettings
        {
            public float shareStepPp = 5f;
            public int ipsSignificantDigits = 2;
        }

        /// <summary>How rivals choose in the events of the event system (FR-14).</summary>
        public sealed class EventChooserSettings
        {
            public float styleWeight = 1.5f;
            public int priority = 100;
        }

        /// <summary>The link with rival-acquisitions (FR-15).</summary>
        public sealed class AcquisitionSettings
        {
            public int hostilePriceDivisor = 200;
            public int hostileDealAttitude = -10;
        }

        // ---- reading ----

        /// <summary>A config with every number of the design.</summary>
        public static RivalConfig Defaults()
        {
            return new RivalConfig();
        }

        /// <summary>
        /// Builds the config from the merged <c>config.json</c> (<c>ModContent.LoadConfig</c>). A member that is missing keeps its default; a member
        /// of the wrong type keeps its default too; a number outside its range is clipped (<see cref="Normalize"/>). Every such case adds one
        /// English line to <paramref name="messages"/> (may be null). Never returns null and never throws.
        /// </summary>
        public static RivalConfig FromJson(JsonObject json, List<string> messages)
        {
            RivalConfig config = null;
            if (json != null && json.Count > 0)
            {
                var errors = new List<string>();
                try
                {
                    config = ModJson.Bind<RivalConfig>(json, errors);
                }
                catch (Exception ex)
                {
                    errors.Add("config could not be read: " + ex.GetType().Name + ": " + ex.Message);
                }
                if (messages != null) messages.AddRange(errors);
            }

            if (config == null) config = Defaults();
            config.Normalize(messages);
            return config;
        }

        /// <summary>
        /// Brings every member into its range and repairs the lists that came with a wrong length (a missing group becomes the default one).
        /// One line per correction goes to <paramref name="messages"/> (may be null). Idempotent.
        /// </summary>
        public void Normalize(List<string> messages)
        {
            if (attitude == null) attitude = new AttitudeSettings();
            if (triggerDeltas == null) triggerDeltas = new TriggerDeltaSettings();
            if (triggerThresholds == null) triggerThresholds = new TriggerThresholdSettings();
            if (reactions == null) reactions = new ReactionSettings();
            if (reactions.PriceCut == null) reactions.PriceCut = new PriceCutReaction();
            if (reactions.CounterLaunch == null) reactions.CounterLaunch = new CounterLaunchReaction();
            if (reactions.Poach == null) reactions.Poach = new PoachReaction();
            if (reactions.Partnership == null) reactions.Partnership = new PartnershipReaction();
            if (priceCut == null) priceCut = new PriceCutSettings();
            if (flawCaution == null) flawCaution = new FlawCautionSettings();
            if (intelFog == null) intelFog = new IntelFogSettings();
            if (eventChooser == null) eventChooser = new EventChooserSettings();
            if (acquisition == null) acquisition = new AcquisitionSettings();

            difficultyMultipliers = FixedFloats(difficultyMultipliers, new RivalConfig().difficultyMultipliers, "difficultyMultipliers", messages);
            for (int i = 0; i < difficultyMultipliers.Length; i++)
                Limits.Float(ref difficultyMultipliers[i], 0.1f, 3f, 1f, "difficultyMultipliers[" + i + "]", messages);

            Limits.Float(ref chanceCap, 0.05f, 1f, 0.9f, "chanceCap", messages);
            Limits.Int(ref graceDays, 0, 3650, "graceDays", messages);
            Limits.Int(ref maxConcurrentHostile, 1, 10, "maxConcurrentHostile", messages);
            Limits.Int(ref maxHostilePerYear, 1, 50, "maxHostilePerYear", messages);

            telegraphDays = FixedInts(telegraphDays, new RivalConfig().telegraphDays, "telegraphDays", messages);
            Limits.Int(ref telegraphDays[0], 1, 365, "telegraphDays[0]", messages);
            Limits.Int(ref telegraphDays[1], 1, 365, "telegraphDays[1]", messages);
            if (telegraphDays[0] > telegraphDays[1])
            {
                int low = telegraphDays[1];
                telegraphDays[1] = telegraphDays[0];
                telegraphDays[0] = low;
                Say(messages, "telegraphDays: the lowest value was above the highest, they were swapped");
            }

            Limits.Int(ref mailWaitDays, 1, 365, "mailWaitDays", messages);
            Limits.Float(ref launchPriceFloorMargin, 0f, 0.5f, 0.05f, "launchPriceFloorMargin", messages);

            NormalizeAttitude(messages);
            NormalizeTriggers(messages);
            NormalizeReactions(messages);

            Limits.Int(ref priceCut.externalMaxDays, 1, 3650, "priceCut.externalMaxDays", messages);

            Limits.Float(ref flawCaution.factor, 0.1f, 1f, 0.85f, "flawCaution.factor", messages);
            Limits.Int(ref flawCaution.months, 0, 120, "flawCaution.months", messages);
            if (flawCaution.minSeverity != "Minor" && flawCaution.minSeverity != "Major" && flawCaution.minSeverity != "Critical")
            {
                Say(messages, "flawCaution.minSeverity: '" + flawCaution.minSeverity + "' is not Minor, Major or Critical, 'Major' is used");
                flawCaution.minSeverity = "Major";
            }

            Limits.Float(ref intelFog.shareStepPp, 0.5f, 50f, 5f, "intelFog.shareStepPp", messages);
            Limits.Int(ref intelFog.ipsSignificantDigits, 1, 6, "intelFog.ipsSignificantDigits", messages);

            Limits.Float(ref eventChooser.styleWeight, 1f, 10f, 1.5f, "eventChooser.styleWeight", messages);
            Limits.Int(ref eventChooser.priority, 0, 1000, "eventChooser.priority", messages);

            Limits.Int(ref acquisition.hostilePriceDivisor, 1, 10000, "acquisition.hostilePriceDivisor", messages);
            Limits.Int(ref acquisition.hostileDealAttitude, -100, 100, "acquisition.hostileDealAttitude", messages);
        }

        private void NormalizeAttitude(List<string> messages)
        {
            AttitudeSettings a = attitude;
            a.thresholds = FixedInts(a.thresholds, new AttitudeSettings().thresholds, "attitude.thresholds", messages);
            bool ordered = true;
            for (int i = 0; i < a.thresholds.Length; i++)
            {
                if (a.thresholds[i] < -100 || a.thresholds[i] > 100) ordered = false;
                if (i > 0 && a.thresholds[i] <= a.thresholds[i - 1]) ordered = false;
            }
            if (!ordered)
            {
                Say(messages, "attitude.thresholds: the borders must rise strictly inside -100..100, the defaults are used");
                a.thresholds = new AttitudeSettings().thresholds;
            }

            Limits.Float(ref a.monthlyDrift, 0f, 1f, 0.10f, "attitude.monthlyDrift", messages);
            Limits.Int(ref a.historySize, 1, 100, "attitude.historySize", messages);
            Limits.Int(ref a.hostileGate, -100, 100, "attitude.hostileGate", messages);
            Limits.Int(ref a.negotiationDivisor, 1, 100, "attitude.negotiationDivisor", messages);
            Limits.Int(ref a.discountDivisor, 1, 100, "attitude.discountDivisor", messages);
            Limits.Int(ref a.discountMaxPoints, 0, 50, "attitude.discountMaxPoints", messages);
            Limits.Int(ref a.revealAttitude, -100, 100, "attitude.revealAttitude", messages);
        }

        private void NormalizeTriggers(List<string> messages)
        {
            TriggerDeltaSettings d = triggerDeltas;
            Limits.Float(ref d.StrongLaunch, -100f, 100f, -8f, "triggerDeltas.StrongLaunch", messages);
            Limits.Float(ref d.ShareGain, -100f, 100f, -5f, "triggerDeltas.ShareGain", messages);
            Limits.Float(ref d.PriceUndercut, -100f, 100f, -4f, "triggerDeltas.PriceUndercut", messages);
            Limits.Float(ref d.OemLoss, -100f, 100f, -6f, "triggerDeltas.OemLoss", messages);
            Limits.Float(ref d.TechLead, -100f, 100f, -3f, "triggerDeltas.TechLead", messages);
            Limits.Float(ref d.BusinessSigned, -100f, 100f, 15f, "triggerDeltas.BusinessSigned", messages);
            Limits.Float(ref d.BusinessBrokenByPlayer, -100f, 100f, -25f, "triggerDeltas.BusinessBrokenByPlayer", messages);
            Limits.Float(ref d.BusinessActiveMonthly, -100f, 100f, 2f, "triggerDeltas.BusinessActiveMonthly", messages);

            TriggerThresholdSettings t = triggerThresholds;
            Limits.Float(ref t.strongLaunchIpsRatio, 1f, 5f, 1.15f, "triggerThresholds.strongLaunchIpsRatio", messages);
            Limits.Float(ref t.shareGainPp, 0.1f, 50f, 3f, "triggerThresholds.shareGainPp", messages);
            Limits.Float(ref t.undercutPriceRatio, 0.1f, 1f, 0.85f, "triggerThresholds.undercutPriceRatio", messages);
            Limits.Float(ref t.undercutIpsRatio, 0.1f, 1.5f, 0.90f, "triggerThresholds.undercutIpsRatio", messages);
            Limits.Int(ref t.undercutCooldownDays, 0, 720, "triggerThresholds.undercutCooldownDays", messages);
            Limits.Float(ref t.minApprovalForBoost, 0.5f, 3f, 1.05f, "triggerThresholds.minApprovalForBoost", messages);

            var types = new List<string>();
            if (t.techLeadTypes != null)
            {
                foreach (string type in t.techLeadTypes)
                {
                    if (!string.IsNullOrEmpty(type) && !types.Contains(type)) types.Add(type);
                }
            }
            if (types.Count == 0)
            {
                Say(messages, "triggerThresholds.techLeadTypes: no technology type is listed, the defaults are used");
                types = new TriggerThresholdSettings().techLeadTypes;
            }
            t.techLeadTypes = types;
        }

        private void NormalizeReactions(List<string> messages)
        {
            Limits.Float(ref reactions.PriceCut.depth, 0f, 0.5f, 0.08f, "reactions.PriceCut.depth", messages);
            Limits.Float(ref reactions.PriceCut.floorMargin, 0f, 0.5f, 0.08f, "reactions.PriceCut.floorMargin", messages);

            Limits.Float(ref reactions.CounterLaunch.approvalBoost, 1f, 3f, 1.35f, "reactions.CounterLaunch.approvalBoost", messages);
            Limits.Float(ref reactions.CounterLaunch.fundingBoost, 1f, 3f, 1.25f, "reactions.CounterLaunch.fundingBoost", messages);

            Limits.Float(ref reactions.Poach.rivalResearchSpeed, 1f, 2f, 1.10f, "reactions.Poach.rivalResearchSpeed", messages);
            Limits.Int(ref reactions.Poach.rivalBoostDays, 1, 720, "reactions.Poach.rivalBoostDays", messages);
            Limits.Int(ref reactions.Poach.minCost, 0, 1000000000, "reactions.Poach.minCost", messages);
            Limits.Int(ref reactions.Poach.researchMonths, 1, 24, "reactions.Poach.researchMonths", messages);
            Limits.Float(ref reactions.Poach.underfundedChanceMult, 1f, 5f, 1.5f, "reactions.Poach.underfundedChanceMult", messages);

            Limits.Int(ref reactions.Partnership.minAttitude, -100, 100, "reactions.Partnership.minAttitude", messages);
            Limits.Float(ref reactions.Partnership.monthlyChance, 0f, 1f, 0.10f, "reactions.Partnership.monthlyChance", messages);
            Limits.Int(ref reactions.Partnership.repeatMonths, 1, 120, "reactions.Partnership.repeatMonths", messages);
        }

        private static float[] FixedFloats(float[] value, float[] defaults, string path, List<string> messages)
        {
            if (value != null && value.Length == defaults.Length) return value;
            Say(messages, path + ": expected " + defaults.Length.ToString(CultureInfo.InvariantCulture) + " numbers, the defaults are used");
            return defaults;
        }

        private static int[] FixedInts(int[] value, int[] defaults, string path, List<string> messages)
        {
            if (value != null && value.Length == defaults.Length) return value;
            Say(messages, path + ": expected " + defaults.Length.ToString(CultureInfo.InvariantCulture) + " whole numbers, the defaults are used");
            return defaults;
        }

        private static void Say(List<string> messages, string text)
        {
            if (messages != null) messages.Add(text);
        }

        // ---- read access for the logic ----

        /// <summary>
        /// The multiplier d of the chance for a difficulty level: the table entry, and for a level outside the table the nearest end (below 0 the
        /// first entry, above 5 the last one).
        /// </summary>
        public float DifficultyMultiplier(int difficulty)
        {
            float[] table = difficultyMultipliers;
            if (table == null || table.Length == 0) return 1f;
            int index = difficulty < 0 ? 0 : (difficulty >= table.Length ? table.Length - 1 : difficulty);
            return table[index];
        }
    }

    /// <summary>
    /// The range checks that content values go through (a clipped value is a warning, not an error). Internal: shared by the config and the
    /// archetypes.
    /// </summary>
    internal static class Limits
    {
        /// <summary>Brings the value into [min, max]; NaN and infinities become <paramref name="fallback"/>. One line to <paramref name="messages"/> per correction.</summary>
        public static void Float(ref float value, float min, float max, float fallback, string path, List<string> messages)
        {
            float before = value;
            if (float.IsNaN(value) || float.IsInfinity(value)) value = fallback;
            else if (value < min) value = min;
            else if (value > max) value = max;

            if (before.Equals(value) || messages == null) return;
            messages.Add(path + ": " + Show(before) + " is outside " + Show(min) + ".." + Show(max) + ", " + Show(value) + " is used");
        }

        public static void Int(ref int value, int min, int max, string path, List<string> messages)
        {
            int before = value;
            if (value < min) value = min;
            else if (value > max) value = max;

            if (before == value || messages == null) return;
            messages.Add(path + ": " + before.ToString(CultureInfo.InvariantCulture) + " is outside " + min.ToString(CultureInfo.InvariantCulture)
                         + ".." + max.ToString(CultureInfo.InvariantCulture) + ", " + value.ToString(CultureInfo.InvariantCulture) + " is used");
        }

        public static string Show(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
