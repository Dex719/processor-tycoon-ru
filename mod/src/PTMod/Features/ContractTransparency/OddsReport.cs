using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// One multiplier that a feature put on the signing chance (a row of <c>ModModifiers.Explain("contract.signChance", ...)</c>): the feature, the
    /// multiplier and the reason in the form the core keeps it. The text of the reason is resolved when it is shown, in the language of that moment.
    /// </summary>
    public sealed class ModifierSourceRow
    {
        /// <summary>The feature that registered the multiplier (<c>AppliedModifier.FeatureId</c>).</summary>
        public string FeatureId;

        /// <summary><c>ModL10n</c> key of the reason; null when the provider gave none (the panel then shows the feature and the multiplier only).</summary>
        public string ReasonKey;

        /// <summary>The arguments of <see cref="ReasonKey"/>; never null.</summary>
        public string[] ReasonArgs = new string[0];

        public float Multiplier;

        /// <summary>The reason in the player's language; null when there is no reason (a provider without one, E13).</summary>
        public string ReasonText
        {
            get
            {
                if (string.IsNullOrEmpty(ReasonKey)) return null;
                object[] args = new object[ReasonArgs == null ? 0 : ReasonArgs.Length];
                for (int i = 0; i < args.Length; i++) args[i] = ReasonArgs[i];
                return ModL10n.L(ReasonKey, args);
            }
        }
    }

    /// <summary>
    /// The analysis of the chance of a CPU in a contract (FR-2, FR-11, FR-12; <c>ContractInspector.BuildOdds</c>): the table of the best offers in the
    /// order of the draw, the chance the game gives the candidate, the probability that the draw picks it, the mods behind the chance and the hints
    /// "what to change". Plain data; the panel formats it.
    /// </summary>
    public sealed class OddsReport
    {
        /// <summary>The best offer of each company in the order the game draws them, the candidate in its place.</summary>
        public List<OfferOdds> Rows = new List<OfferOdds>();

        /// <summary>The best offer of each company including the candidate's CPU, as maths input (the input of the estimate of FR-11, <see cref="PotentialBidders"/>).</summary>
        public List<OfferInput> BestOffers = new List<OfferInput>();

        /// <summary>The key of the company of the CPU that is analysed.</summary>
        public string CandidateCompanyKey;

        /// <summary><c>Cpu.SaveID</c> of the CPU that is analysed.</summary>
        public int CandidateCpuId;

        /// <summary>The contract has no offers yet: the game shows 100 % (<c>Contract.ChanceToSign</c> returns early) and the AI may still bid every month (FR-2 item 7).</summary>
        public bool NoOffersYet;

        /// <summary>The chances come from <c>Contract.GetBestOffers</c>; false when the method could not be called and the vanilla chances stand in.</summary>
        public bool GameChancesAvailable = true;

        /// <summary>The chance without any mod, <c>clamp(round(100 × IPS / ΣIPS), 1, 100)</c>.</summary>
        public int CandidateVanillaChance;

        /// <summary>The chance the game gives the candidate: <c>Contract.ChanceToSign</c> after the check of FR-12 item 2.</summary>
        public int CandidateGameChance;

        /// <summary>The probability that the draw picks the candidate, 0..1 (<see cref="OddsMath.SequentialProbabilities"/>).</summary>
        public double CandidateActual;

        /// <summary>The factor mods put on the chance: the total of the breakdown when there is one, otherwise game chance over vanilla chance.</summary>
        public float ModMultiplier = 1f;

        /// <summary>The game's chance differs from the vanilla one: the panel shows the line "mod modifiers" (FR-2 item 3).</summary>
        public bool ChanceDiffersFromVanilla;

        /// <summary>The rows behind <see cref="ModMultiplier"/>, one per applied multiplier.</summary>
        public List<ModifierSourceRow> ModSources = new List<ModifierSourceRow>();

        /// <summary>The balance range or the safety fuse of the channel cut the product: the panel adds "(capped)".</summary>
        public bool ModsCapped;

        /// <summary>The IPS at which the candidate would have a 50 % share against the best offers of the other companies (their sum); 0 without competitors. The mods are not in it.</summary>
        public float IpsFor50;

        /// <summary>The candidate is drawn first when its IPS is above this (the largest IPS of the competitors); 0 without competitors. Equal IPS loses: the candidate came last.</summary>
        public float IpsForFirstInQueue;

        /// <summary>The CPU of the candidate's company that the review of the contract counts (the fastest one among its offers and the candidate).</summary>
        public int StrongestOwnCpuId;

        /// <summary>The CPUs of the company that the review will not count (FR-2 item 6).</summary>
        public List<int> ShadowedOwnCpuIds = new List<int>();

        /// <summary>The game's <c>Contract.ChanceToSign</c> for the candidate; NaN when it could not be asked.</summary>
        public float GameChanceToSign = float.NaN;

        /// <summary>The numbers of the mod and of the game's <c>ChanceToSign</c> differ by more than half a percent point (FR-12 item 2).</summary>
        public bool MismatchWithGame;

        /// <summary>The row of the candidate in <see cref="Rows"/>, or null.</summary>
        public OfferOdds? CandidateRow
        {
            get
            {
                for (int i = 0; i < Rows.Count; i++)
                {
                    if (Rows[i].CpuId == CandidateCpuId) return Rows[i];
                }
                return null;
            }
        }
    }
}
