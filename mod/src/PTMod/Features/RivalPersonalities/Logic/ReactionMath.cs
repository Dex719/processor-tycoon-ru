namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// Why a hostile reaction may not be planned right now, or <see cref="Allowed"/> (FR-6, design.md Component 8). The first reason found in the
    /// order of <c>ReactionMath.Gate</c> is returned.
    /// </summary>
    public enum GateResult
    {
        /// <summary>Nothing stands in the way.</summary>
        Allowed = 0,

        /// <summary>The game is younger than <c>graceDays</c> (FR-6 item 2).</summary>
        GracePeriod = 1,

        /// <summary>The rival made its last reaction less than <c>cooldownDays</c> of its archetype ago (FR-6 item 3).</summary>
        Cooldown = 2,

        /// <summary>Two hostile reactions are planned or active already (FR-6 item 4).</summary>
        ConcurrentCap = 3,

        /// <summary>Four hostile reactions were executed this game year (FR-6 item 4).</summary>
        YearlyCap = 4,

        /// <summary>The attitude is above the gate (-20) and the rule does not say <c>anyAttitude</c> (FR-6 item 5).</summary>
        AttitudeTooHigh = 5,

        /// <summary>A reaction of the same type is planned or active for this rival (FR-6 item 8).</summary>
        SameTypeActive = 6,

        /// <summary>The rival is bankrupt.</summary>
        Bankrupt = 7,

        /// <summary>The rival is put aside (<c>SetDormant</c>, FR-13 item 3).</summary>
        Dormant = 8,

        /// <summary>The event system is off or missing: no reaction can be carried out (FR-7 item 8).</summary>
        NoEventSystem = 9,
    }

    /// <summary>
    /// What <c>ReactionMath.Gate</c> needs to know to judge one reaction against the honesty limits of FR-6. The
    /// caller fills it from the state: the days since the start of the grace period, the days since the last reaction of this rival (use
    /// <see cref="int.MaxValue"/> when it never had one), the counters of the ledger. The limits themselves come from the config.
    /// </summary>
    public struct GateInput
    {
        /// <summary>The reaction is hostile (every reaction but the friendly offer). A friendly one skips the limits of the grace period, the cooldown, the caps and the attitude.</summary>
        public bool Hostile;

        /// <summary>The rule of the reaction says <c>anyAttitude</c>: the attitude gate does not apply.</summary>
        public bool AnyAttitude;

        /// <summary>The attitude of the rival to the player, -100..+100.</summary>
        public float Attitude;

        /// <summary>Days since the grace period started (<c>TickPhase.FastForwardEnded</c>, or the first load of an old save).</summary>
        public int DaysSinceGraceStart;

        /// <summary>Days since the rival's last reaction; <see cref="int.MaxValue"/> when it never had one.</summary>
        public int DaysSinceLastReactionOfRival;

        /// <summary>The <c>cooldownDays</c> of the rival's archetype.</summary>
        public int CooldownDays;

        /// <summary>Hostile reactions planned or active now (all rivals).</summary>
        public int ActiveOrPendingHostile;

        /// <summary>Hostile reactions executed in the current game year (all rivals).</summary>
        public int HostileExecutedThisYear;

        /// <summary>A reaction of the same type is planned or active for this rival.</summary>
        public bool SameTypeActiveOrPending;

        public bool RivalBankrupt;
        public bool RivalDormant;

        /// <summary>The event system is on and can carry the reaction out.</summary>
        public bool EventSystemAvailable;
    }

    /// <summary>
    /// The chance of a reaction and the honesty limits (FR-6, formulas 6 and 7 of design.md). Pure: the numbers come from the config, the facts
    /// from the caller.
    /// </summary>
    public static class ReactionMath
    {
        /// <summary>
        /// The chance of a reaction: <c>p = clamp(base x f(a) x d, 0, chanceCap)</c>, where <c>f(a)</c> is
        /// <see cref="AttitudeMath.ReactionMultiplier"/> and <c>d</c> the multiplier of the difficulty level (0..5). A base chance that is NaN
        /// gives 0.
        /// </summary>
        public static float Chance(float baseChance, float attitude, int difficulty, RivalConfig config)
        {
            return Chance(baseChance, attitude, difficulty, config, 1f);
        }

        /// <summary>
        /// The same with one more factor applied before the cap: <c>Poach</c> against a player whose research is underfunded multiplies the
        /// chance (formula 6, <c>reactions.Poach.underfundedChanceMult</c>). A factor that is NaN or negative counts as 1.
        /// </summary>
        public static float Chance(float baseChance, float attitude, int difficulty, RivalConfig config, float extraMultiplier)
        {
            RivalConfig cfg = config ?? RivalConfig.Defaults();
            if (float.IsNaN(baseChance) || float.IsInfinity(baseChance)) return 0f;

            float extra = float.IsNaN(extraMultiplier) || float.IsInfinity(extraMultiplier) || extraMultiplier < 0f ? 1f : extraMultiplier;
            float p = baseChance * AttitudeMath.ReactionMultiplier(attitude) * cfg.DifficultyMultiplier(difficulty) * extra;
            float cap = cfg.chanceCap;
            if (float.IsNaN(p) || p < 0f) return 0f;
            return p > cap ? cap : p;
        }

        /// <summary>The honesty limits with the numbers of the design (180 days of grace, two at a time, four a year, the gate at -20).</summary>
        public static GateResult Gate(in GateInput input)
        {
            return Gate(in input, null);
        }

        /// <summary>
        /// Judges a reaction against the limits and returns the first reason to refuse, in this order: the event system is missing
        /// (<see cref="GateResult.NoEventSystem"/>), the rival is bankrupt, the rival is dormant; then, for a hostile reaction only, the grace
        /// period, the cooldown of the rival, the cap of two at a time, the cap of four a year and the attitude gate; at last a reaction of the same
        /// type already planned or active. <see cref="GateResult.Allowed"/> when none applies.
        /// </summary>
        public static GateResult Gate(in GateInput input, RivalConfig config)
        {
            RivalConfig cfg = config ?? RivalConfig.Defaults();

            if (!input.EventSystemAvailable) return GateResult.NoEventSystem;
            if (input.RivalBankrupt) return GateResult.Bankrupt;
            if (input.RivalDormant) return GateResult.Dormant;

            if (input.Hostile)
            {
                if (input.DaysSinceGraceStart < cfg.graceDays) return GateResult.GracePeriod;
                if (input.DaysSinceLastReactionOfRival < input.CooldownDays) return GateResult.Cooldown;
                if (input.ActiveOrPendingHostile >= cfg.maxConcurrentHostile) return GateResult.ConcurrentCap;
                if (input.HostileExecutedThisYear >= cfg.maxHostilePerYear) return GateResult.YearlyCap;
                if (!input.AnyAttitude && !AttitudeAllowsHostile(input.Attitude, cfg.attitude.hostileGate)) return GateResult.AttitudeTooHigh;
            }

            if (input.SameTypeActiveOrPending) return GateResult.SameTypeActive;
            return GateResult.Allowed;
        }

        private static bool AttitudeAllowsHostile(float attitude, int hostileGate)
        {
            // "above -20" forbids; exactly -20 is allowed. NaN counts as 0 (neutral).
            return AttitudeMath.Clamp(attitude) <= hostileGate;
        }
    }
}
