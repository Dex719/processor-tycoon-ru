using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// A CPU as the effect engine sees it (design, Component 5): the generation of its socket, whether it is a full platform, the share of the
    /// installed base behind it (<c>effShare</c>, F2) and whether the new game's fast-forward runs. Built by the provider from its cache; plain data.
    /// </summary>
    public struct EffectCpu
    {
        /// <summary>The <c>order</c> of the CPU's generation; null = the CPU is outside the generations.</summary>
        public int? GenOrder;

        /// <summary>The id of the CPU's generation; null = outside the generations.</summary>
        public string GenId;

        /// <summary>The memory of the CPU is one of the memories of its generation (D11, "full platform").</summary>
        public bool FullPlatform;

        /// <summary><c>effShare</c> of the CPU's company on the CPU's socket (F2).</summary>
        public double EffShare;

        /// <summary>The new game's fast-forward runs: the feature is neutral (FR-12 item 2).</summary>
        public bool IsFastForwarding;
    }

    /// <summary>The contract of a question to <c>contract.signChance</c>: whether there is one (patch P8 + P23 gives it), its key and its type.</summary>
    public struct EffectContract
    {
        /// <summary>The question has a contract: the "per contract" mode is possible.</summary>
        public bool Present;

        /// <summary>The key of the contract (<see cref="ContractKeyBuilder"/>); the preference is stored under it.</summary>
        public string Key;

        /// <summary><c>ContractType</c> of the game: Calculator 0, Computer 1, Console 2.</summary>
        public int ContractType;
    }

    /// <summary>
    /// What the engine answers for <c>contract.signChance</c>: the multiplier and the one reason that explains it (D12). The reason names the
    /// biggest part: the platform of the client (preferred generation or lag, "per contract"), the market's adoption ("market" mode), or - when the
    /// platform changes nothing - the loyalty of the OEM to the company's base.
    /// </summary>
    public struct ChanceResult
    {
        /// <summary>The multiplier of the chance; 1 = no change.</summary>
        public float Multiplier;

        /// <summary>One of <c>OemAdoptionModel.Reason*</c>; null when the multiplier is 1.</summary>
        public string ReasonKey;

        /// <summary>The generation the reason names: the preferred one (<c>ReasonPrefers</c>) or the leading one of the market (<c>ReasonAdoption</c>); null otherwise.</summary>
        public string GenId;

        /// <summary>For <c>ReasonLag</c>: how many generations the CPU is behind the preferred one.</summary>
        public int LagGenerations;

        /// <summary>For <c>ReasonAdoption</c>: the adoption of <see cref="GenId"/> in percent (0..100).</summary>
        public double AdoptionPercent;

        /// <summary>No change, no reason.</summary>
        public static ChanceResult Neutral
        {
            get { return new ChanceResult { Multiplier = 1f }; }
        }
    }

    /// <summary>
    /// The pure engine behind the two providers (design, Component 5/6): the multiplier of the rating of a CPU from the base and the chronology
    /// (formulas F2-F6), and the multiplier of the signing chance of an offer from the OEM model (F9a-F9c) in the mode that the question allows.
    /// Takes plain inputs and holds no state, so the offline simulation and the tests drive it without the game.
    /// </summary>
    public sealed class PlatformEffectEngine
    {
        // ---- rating (FR-5) ----

        /// <summary>
        /// F2-F6 for one CPU: the base of the company on the socket, the early adopter bonus of the company in the generation, the penalty of the
        /// company's first CPU on the socket. A CPU outside the generations (<paramref name="genId"/> null), a missing company or socket, or a
        /// missing ledger give 1 for every segment. <paramref name="timeline"/> may be null (no bonus, no penalty); <paramref name="today"/> not
        /// set counts the fades as just begun.
        /// </summary>
        public RatingBreakdown RatingFor(string companyKey, SocketId socket, string genId, InstalledBaseLedger ib, PlatformTimeline timeline,
            GameDate today, PlatformBalance b)
        {
            var neutral = new RatingBreakdown { ConsumerMult = 1f, IndustriesMult = 1f };
            if (string.IsNullOrEmpty(companyKey) || !socket.IsValid || string.IsNullOrEmpty(genId) || ib == null) return neutral;

            var input = new RatingInput
            {
                OwnIb = ib.Of(companyKey, socket.Key),
                SocketIb = ib.OfSocket(socket.Key),
                TotalIb = ib.Total,
                IsStandard = socket.IsStandard,
            };

            if (timeline != null)
            {
                GameDate since;
                if (timeline.IsEarlyAdopter(companyKey, genId, out since))
                {
                    input.IsEarlyAdopter = true;
                    input.MonthsSinceEarly = today.IsSet ? Math.Max(0, since.MonthsTo(today)) : 0;
                }

                SocketIntro intro = timeline.IntroOf(companyKey, socket.Key);
                if (intro.Exists && intro.Severity > 0)
                {
                    input.ChurnSeverity = intro.Severity;
                    input.MonthsSinceIntro = today.IsSet ? Math.Max(0, intro.Date.MonthsTo(today)) : 0;
                }
            }

            return RatingEffectModel.Compute(input, b);
        }

        // ---- the signing chance (FR-8) ----

        /// <summary>
        /// The mode that a question gets: <see cref="OemMode.Auto"/> here means "per contract" (a contract is in the question and the setting
        /// is <c>auto</c>), <see cref="OemMode.MarketAverage"/> means the "market" mode (the setting forces it, or there is no contract to
        /// look at, FR-8 item 2).
        /// </summary>
        public OemMode EffectiveMode(OemMode configured, bool contractPresent)
        {
            return configured == OemMode.Auto && contractPresent ? OemMode.Auto : OemMode.MarketAverage;
        }

        /// <summary>
        /// The multiplier of the signing chance of one CPU (F9a/F9b, then F9c). No change (1, no reason) while the fast-forward runs, for a CPU
        /// outside the generations, for a contract of a type that is not in <c>contractTypes</c> (AC-8.3: Calculator), and in the "market" mode for
        /// a generation below <c>marketAverageMinGenOrder</c> (FR-8 item 5). "Per contract": the platform multiplier against the preference
        /// stored for the contract (a contract without a preference gives a platform multiplier of 1, so only the loyalty remains);
        /// "market": the expected multiplier over the preferences of the market on the day (<paramref name="newestFirst"/>, newest generation
        /// first). The loyalty of the OEM to the company's base is applied last in both (F9c).
        /// </summary>
        public ChanceResult Chance(EffectCpu cpu, EffectContract contract, OemMode configured, OemPreferences prefs,
            IReadOnlyList<GenAdoptionInput> newestFirst, PlatformBalance b)
        {
            if (cpu.IsFastForwarding || !cpu.GenOrder.HasValue) return ChanceResult.Neutral;

            PlatformBalance balance = b ?? PlatformBalance.Defaults();
            if (contract.Present && !balance.Oem.IncludesContractType(contract.ContractType)) return ChanceResult.Neutral;

            if (EffectiveMode(configured, contract.Present) == OemMode.Auto)
            {
                return PerContract(cpu, contract, prefs, newestFirst, balance);
            }
            return Market(cpu, newestFirst, balance);
        }

        private static ChanceResult PerContract(EffectCpu cpu, EffectContract contract, OemPreferences prefs,
            IReadOnlyList<GenAdoptionInput> newestFirst, PlatformBalance balance)
        {
            string preferred = prefs == null ? null : prefs.PreferenceOf(contract.Key);
            GenerationDef preferredGen = FindGeneration(newestFirst, preferred);   // an id that the content no longer has is "no preference"

            int? preferredOrder = preferredGen == null ? (int?)null : preferredGen.order;
            string reason;
            float platform = OemAdoptionModel.PlatformMultiplier(cpu.GenOrder, preferredOrder, cpu.FullPlatform, contract.ContractType, balance, out reason);
            float total = OemAdoptionModel.WithLoyalty(platform, cpu.EffShare, balance);

            var result = new ChanceResult { Multiplier = total };
            if (reason != null && platform != 1f)
            {
                result.ReasonKey = reason;
                if (reason == OemAdoptionModel.ReasonLag)
                {
                    result.LagGenerations = preferredOrder.Value - cpu.GenOrder.Value;
                    result.GenId = preferredGen.id;
                }
                else
                {
                    result.GenId = preferredGen.id;
                }
            }
            else if (total != 1f)
            {
                result.ReasonKey = OemAdoptionModel.ReasonLoyalty;
            }
            return result;
        }

        private static ChanceResult Market(EffectCpu cpu, IReadOnlyList<GenAdoptionInput> newestFirst, PlatformBalance balance)
        {
            if (cpu.GenOrder.Value < balance.Oem.MarketAverageMinGenOrder) return ChanceResult.Neutral;

            float average = OemAdoptionModel.MarketAverageMultiplier(cpu.GenOrder, cpu.FullPlatform, newestFirst, balance);
            float total = OemAdoptionModel.WithLoyalty(average, cpu.EffShare, balance);

            var result = new ChanceResult { Multiplier = total };
            if (average != 1f)
            {
                result.ReasonKey = OemAdoptionModel.ReasonAdoption;
                GenAdoptionInput leading = LeadingGeneration(newestFirst);
                if (leading.Gen != null)
                {
                    result.GenId = leading.Gen.id;
                    result.AdoptionPercent = OemAdoptionModel.Adoption(leading) * 100.0;
                }
            }
            else if (total != 1f)
            {
                result.ReasonKey = OemAdoptionModel.ReasonLoyalty;
            }
            return result;
        }

        /// <summary>The newest generation that is launched, not obsolete and adopted by someone: what the market is moving to. An input without a generation when there is none.</summary>
        private static GenAdoptionInput LeadingGeneration(IReadOnlyList<GenAdoptionInput> newestFirst)
        {
            if (newestFirst == null) return default(GenAdoptionInput);
            for (int i = 0; i < newestFirst.Count; i++)
            {
                GenAdoptionInput input = newestFirst[i];
                if (input.Gen == null || !input.Launched || input.ObsoleteAtT) continue;
                if (OemAdoptionModel.Adoption(input) > 0) return input;
            }
            return default(GenAdoptionInput);
        }

        private static GenerationDef FindGeneration(IReadOnlyList<GenAdoptionInput> inputs, string genId)
        {
            if (string.IsNullOrEmpty(genId) || inputs == null) return null;
            for (int i = 0; i < inputs.Count; i++)
            {
                GenerationDef gen = inputs[i].Gen;
                if (gen != null && string.Equals(gen.id, genId, StringComparison.Ordinal)) return gen;
            }
            return null;
        }
    }
}
