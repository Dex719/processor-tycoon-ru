using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>One multiplier behind the chance, as the panel prints it: who put it there, why (already in the player's language, null when the provider gave no reason), and by how much.</summary>
    public struct ModSourceText
    {
        /// <summary>The id of the feature that registered the multiplier.</summary>
        public string Feature;

        /// <summary>The reason in the player's language; null or empty when the provider gave none (E13 of design.md).</summary>
        public string Reason;

        public float Multiplier;
    }

    /// <summary>What the text part of the breakdown of the chance needs (FR-2, FR-11, FR-12); the panel fills it from <c>OddsReport</c>.</summary>
    public sealed class OddsPanelInput
    {
        /// <summary>The contract has no offers yet: the game says 100 % before it looks at any chance.</summary>
        public bool NoOffersYet;

        /// <summary>The chances come from the game; false when only the vanilla ones could be computed.</summary>
        public bool GameChancesAvailable = true;

        /// <summary>The game's chance differs from the vanilla one (the mods are at work).</summary>
        public bool ChanceDiffersFromVanilla;

        /// <summary>The balance range or the fuse of the channel cut the product.</summary>
        public bool ModsCapped;

        /// <summary>The numbers of the mod and of the game's <c>ChanceToSign</c> differ (FR-12 item 2).</summary>
        public bool Mismatch;

        public int VanillaChance;

        /// <summary>The chance the game gives the CPU, percent.</summary>
        public int GameChance;

        /// <summary>The probability that the draw picks the CPU, 0..1.</summary>
        public double Actual;

        /// <summary>The factor the mods put on the chance.</summary>
        public float ModMultiplier = 1f;

        /// <summary>The probability under which the panel marks the result as bad (<c>risk.lowActualChance</c>).</summary>
        public double LowActualChance = 0.10;

        /// <summary><c>Cpu.SaveID</c> of the CPU that is analysed.</summary>
        public int CandidateCpuId;

        /// <summary>The best offer of each company in the order of the draw, the candidate among them.</summary>
        public IList<OfferOdds> Rows;

        public float IpsFor50;
        public float IpsForFirstInQueue;

        /// <summary>The CPUs of the player that the review will not count (FR-2 item 6).</summary>
        public int ShadowedCount;

        /// <summary>The name of the CPU of the player that counts instead of the shadowed ones.</summary>
        public string StrongestOwnName;

        /// <summary>The review date for people, for the line "the AI bids every month until ...".</summary>
        public string ReviewDateText;

        public IList<ModSourceText> Sources;
    }

    /// <summary>
    /// The text part of the breakdown of the chance (FR-2): the summary, the notes (no offers yet, the CPU is shut out, only the strongest CPU counts), the line "mod modifiers" with its sources and the hints "what
    /// to change". The table of the offers is drawn by the panel from the same rows. Pure: the decisions of what to say are here, so they are tested, and the panel only prints.
    /// </summary>
    public static class OddsLines
    {
        /// <summary>The position of the candidate in the draw order (0 is the first), or -1 when it is not among the rows.</summary>
        public static int CandidatePosition(IList<OfferOdds> rows, int candidateCpuId)
        {
            if (rows == null) return -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].CpuId == candidateCpuId) return i;
            }
            return -1;
        }

        /// <summary>
        /// True when an offer that is drawn before the candidate cannot lose (its chance is 100 %): the draw never gets to the candidate, and its probability is 0 (AC-2.3).
        /// </summary>
        public static bool IsShutOut(IList<OfferOdds> rows, int candidateCpuId)
        {
            int position = CandidatePosition(rows, candidateCpuId);
            if (position <= 0) return false;
            for (int i = 0; i < position; i++)
            {
                if (rows[i].GameChance >= OddsMath.MaxChance) return true;
            }
            return false;
        }

        public static List<PanelLine> Build(OddsPanelInput input)
        {
            var lines = new List<PanelLine>();
            if (input == null) return lines;

            lines.Add(new PanelLine("ct.odds.summary", input.Actual < input.LowActualChance ? LineTone.Bad : LineTone.Normal,
                PanelArg.Whole(input.GameChance), PanelArg.Actual(input.Actual)).AsBold());

            if (!input.GameChancesAvailable) lines.Add(new PanelLine("ct.odds.noGameData", LineTone.Warn));

            // No offers yet: the game answers 100 % and the AI may still bid every month until the review (FR-2 item 7).
            if (input.NoOffersYet) lines.Add(new PanelLine("ct.odds.noOffers", LineTone.Muted, PanelArg.OfText(input.ReviewDateText)));

            if (IsShutOut(input.Rows, input.CandidateCpuId)) lines.Add(new PanelLine("ct.odds.blocked", LineTone.Bad));

            if (input.ChanceDiffersFromVanilla)
            {
                // "(capped)" is the text of the core (the key of ModifierBreakdown.CappedKey), so every feature that shows a breakdown says it the same way.
                lines.Add(input.ModsCapped
                    ? new PanelLine("ct.odds.mods.capped", LineTone.Normal, PanelArg.Multiplier(input.ModMultiplier), PanelArg.OfKey("core.explain.capped"))
                    : new PanelLine("ct.odds.mods", LineTone.Normal, PanelArg.Multiplier(input.ModMultiplier)));
                if (input.Sources != null)
                {
                    foreach (ModSourceText source in input.Sources)
                    {
                        if (string.IsNullOrEmpty(source.Reason))
                            lines.Add(new PanelLine("ct.odds.source.noReason", LineTone.Muted, PanelArg.OfText(source.Feature), PanelArg.Multiplier(source.Multiplier)).Indented(1));
                        else
                            lines.Add(new PanelLine("ct.odds.source", LineTone.Muted, PanelArg.OfText(source.Feature), PanelArg.OfText(source.Reason), PanelArg.Multiplier(source.Multiplier)).Indented(1));
                    }
                }
            }

            if (input.ShadowedCount > 0) lines.Add(new PanelLine("ct.odds.shadowed", LineTone.Warn, PanelArg.OfText(input.StrongestOwnName)));

            // "What to change" (FR-2 item 5): only against competitors.
            if (input.IpsFor50 > 0f)
            {
                lines.Add(new PanelLine("ct.odds.hint50", LineTone.Normal, PanelArg.Ips(input.IpsFor50)));
                int position = CandidatePosition(input.Rows, input.CandidateCpuId);
                if (position == 0) lines.Add(new PanelLine("ct.odds.first", LineTone.Good));
                else if (input.IpsForFirstInQueue > 0f) lines.Add(new PanelLine("ct.odds.hintFirst", LineTone.Normal, PanelArg.Ips(input.IpsForFirstInQueue)));
            }

            if (input.Mismatch) lines.Add(new PanelLine("ct.odds.mismatch", LineTone.Warn));
            return lines;
        }
    }
}
