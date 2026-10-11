using System;
using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>One offer of a contract as the recorder saw it just before <c>Contract.ChooseCpu</c> checked the production lines.</summary>
    public struct TenderOffer
    {
        /// <summary><c>Cpu.SaveID</c>.</summary>
        public int CpuId;

        /// <summary><c>Cpu.Name</c>.</summary>
        public string CpuName;

        /// <summary>The key of the CPU's company (<see cref="OfferInput.CompanyKey"/>).</summary>
        public string CompanyKey;

        /// <summary>The name of the CPU's company as the game shows it.</summary>
        public string CompanyName;

        /// <summary>The CPU belongs to the player.</summary>
        public bool IsPlayer;

        /// <summary><c>Cpu.Ips</c>.</summary>
        public float Ips;

        /// <summary><c>Cpu.IsRetired</c>.</summary>
        public bool IsRetired;

        /// <summary><c>ProductionMath.RequiredProductionLines(cpu, Units)</c>; 0 when the game could not tell.</summary>
        public int RequiredLines;

        /// <summary><c>ContractHelper.ContractedProductionLines(manufacturer.Owner)</c>.</summary>
        public int ContractedLines;

        /// <summary><c>FactoryBase.ProductionCapacity</c> of the manufacturer.</summary>
        public int CapacityLines;

        /// <summary>The name of the factory when it is not the company's own (a foundry); empty for the company's own factory.</summary>
        public string MakerName;
    }

    /// <summary>Everything the recorder gathers around one <c>Contract.ChooseCpu</c> (design.md, Component 8). Plain data: the game is not needed to judge it.</summary>
    public sealed class TenderSnapshot
    {
        /// <summary><see cref="ContractKeyBuilder"/> key of the contract.</summary>
        public string ContractKey = string.Empty;

        /// <summary>The contract for people.</summary>
        public string ContractLabel = string.Empty;

        /// <summary>The day of the draw.</summary>
        public DateTime Date;

        /// <summary>The offers before the check of lines, in the order of <c>Contract.OffersIDs</c>.</summary>
        public List<TenderOffer> Before = new List<TenderOffer>();

        /// <summary><c>Contract.OffersIDs</c> after the check of lines (what the draw worked on).</summary>
        public List<int> AfterIds = new List<int>();

        /// <summary>
        /// The chances the game gave the offers of the draw: (CPU id, chance) from a second call of <c>Contract.GetBestOffers(AfterIds)</c>. Null when the call
        /// could not be made (the chances without mods stand in then).
        /// </summary>
        public List<KeyValuePair<int, int>> DrawChances;

        /// <summary><c>Contract.ChosenCpuID</c> after the draw, -1 for nobody.</summary>
        public int ChosenCpuId = -1;

        /// <summary><c>Player.AutomateRejection</c> at the time of the draw: whether the game checked the lines of the player's offers.</summary>
        public bool AutomateRejection;
    }

    /// <summary>What a draw leaves in the log: the result and the lines about the player's offers the game withdrew.</summary>
    public sealed class TenderRecord
    {
        /// <summary>The result of the tender for the player.</summary>
        public TenderResultDto Result;

        /// <summary>The verdict the result was built from.</summary>
        public TenderOutcome Outcome;

        /// <summary>One <see cref="LogTypes.AutoRejected"/> line for each of the player's offers withdrawn for lack of lines (FR-7 item 1).</summary>
        public List<LogEntry> Withdrawn = new List<LogEntry>();
    }

    /// <summary>
    /// Builds the result of a tender from a <see cref="TenderSnapshot"/> (FR-5, FR-7): who played, with what chances and what probabilities (<see cref="OddsMath"/>),
    /// who was withdrawn and why, who won, and the verdict of <see cref="TenderOutcomeClassifier"/>. The recorder's patches only gather the snapshot; everything that
    /// can be wrong in the arithmetic is here, where a test reaches it.
    /// </summary>
    public static class TenderAssembler
    {
        /// <summary>
        /// The record of the draw, or null when the player had no offer in it (nothing to tell) or the snapshot is empty. The chances of the game stand in the rows
        /// (the chance without mods where the game gave none); the probabilities are those of the order of the draw.
        /// </summary>
        public static TenderRecord Build(TenderSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Before == null || snapshot.Before.Count == 0) return null;

            List<TenderOffer> before = snapshot.Before;
            var byId = new Dictionary<int, TenderOffer>();
            bool anyPlayer = false;
            for (int i = 0; i < before.Count; i++)
            {
                byId[before[i].CpuId] = before[i];
                if (before[i].IsPlayer) anyPlayer = true;
            }
            if (!anyPlayer) return null;

            var after = new HashSet<int>(snapshot.AfterIds ?? new List<int>());
            var participants = new List<TenderOffer>();
            var withdrawn = new List<TenderOffer>();
            for (int i = 0; i < before.Count; i++)
            {
                if (after.Contains(before[i].CpuId)) participants.Add(before[i]);
                else withdrawn.Add(before[i]);
            }

            // The draw: the best offer of each company, the chances of the game, the order of the draw and the probabilities.
            var inputs = new List<OfferInput>(participants.Count);
            for (int i = 0; i < participants.Count; i++)
            {
                inputs.Add(new OfferInput(participants[i].CompanyKey, participants[i].CpuId, participants[i].Ips, i));
            }
            List<OfferInput> best = OddsMath.BestPerCompany(inputs);

            int[] chances = null;
            if (snapshot.DrawChances != null)
            {
                float sum = OddsMath.SumIps(best);
                chances = new int[best.Count];
                for (int i = 0; i < best.Count; i++)
                {
                    int chance;
                    chances[i] = TryFind(snapshot.DrawChances, best[i].CpuId, out chance) ? chance : OddsMath.VanillaChance(best[i].Ips, sum);
                }
            }
            List<OfferOdds> rows = OddsMath.DrawOrder(best, chances);

            // The CPU that played for the player: the first player's row in the order of the draw.
            OfferOdds? playerRow = null;
            for (int i = 0; i < rows.Count && !playerRow.HasValue; i++)
            {
                if (byId[rows[i].CpuId].IsPlayer) playerRow = rows[i];
            }
            TenderOffer? counted = playerRow.HasValue ? byId[playerRow.Value.CpuId] : (TenderOffer?)null;

            TenderOffer strongest = StrongestPlayerOffer(before);
            TenderOffer lastOffered = LastPlayerOffer(before);
            TenderOffer? chosen = snapshot.ChosenCpuId >= 0 && byId.ContainsKey(snapshot.ChosenCpuId) ? byId[snapshot.ChosenCpuId] : (TenderOffer?)null;

            var facts = new TenderFacts
            {
                OffersAfter = participants.Count,
                ChosenCpuId = snapshot.ChosenCpuId,
                ChosenIsPlayer = chosen.HasValue && chosen.Value.IsPlayer,
                PlayerStrongestWithdrawn = !after.Contains(strongest.CpuId),
                PlayerCpuRetired = counted.HasValue && counted.Value.IsRetired,
                PlayerCpuShadowed = counted.HasValue && counted.Value.CpuId != lastOffered.CpuId && after.Contains(lastOffered.CpuId),
            };
            TenderOutcome outcome = TenderOutcomeClassifier.Classify(facts);

            TenderOffer playerCpu = counted.HasValue ? counted.Value : strongest;
            string date = LogDates.Write(snapshot.Date);

            var result = new TenderResultDto
            {
                contractKey = snapshot.ContractKey,
                contractLabel = snapshot.ContractLabel,
                date = date,
                outcome = outcome.Name,
                reasonKey = outcome.ReasonKey,
                playerCpuId = playerCpu.CpuId,
                playerCpuName = playerCpu.CpuName,
                playerGameChance = playerRow.HasValue ? playerRow.Value.GameChance : 0,
                playerActual = playerRow.HasValue ? Round(playerRow.Value.ActualProbability, 4) : 0,
                capacityNotChecked = !snapshot.AutomateRejection && Short(playerCpu),
            };
            if (chosen.HasValue)
            {
                result.winnerCpuId = chosen.Value.CpuId;
                result.winnerCompany = chosen.Value.CompanyName;
                result.winnerCpuName = chosen.Value.CpuName;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                TenderOffer offer = byId[rows[i].CpuId];
                result.offers.Add(new OfferRowDto
                {
                    company = offer.CompanyName,
                    cpu = offer.CpuName,
                    ips = Round(offer.Ips, 3),
                    chance = rows[i].GameChance,
                    actual = Round(rows[i].ActualProbability, 4),
                    player = offer.IsPlayer,
                });
            }

            var record = new TenderRecord { Result = result, Outcome = outcome };
            for (int i = 0; i < withdrawn.Count; i++)
            {
                TenderOffer offer = withdrawn[i];
                result.pruned.Add(new PrunedRowDto
                {
                    company = offer.CompanyName,
                    cpu = offer.CpuName,
                    required = offer.RequiredLines,
                    contracted = offer.ContractedLines,
                    capacity = offer.CapacityLines,
                    maker = offer.MakerName ?? string.Empty,
                    player = offer.IsPlayer,
                });

                if (!offer.IsPlayer) continue;
                LogEntry line = LogEntry.Create(date, LogTypes.AutoRejected, LogSources.Game);
                line.contractKey = snapshot.ContractKey;
                line.contractLabel = snapshot.ContractLabel;
                line.cpuId = offer.CpuId;
                line.cpuName = offer.CpuName;
                line.reasonKey = "ct.reason.autoReject";
                line.Put(LogDataKeys.Required, offer.RequiredLines);
                line.Put(LogDataKeys.Contracted, offer.ContractedLines);
                line.Put(LogDataKeys.Capacity, offer.CapacityLines);
                line.Put(LogDataKeys.Maker, offer.MakerName ?? string.Empty);
                line.Put(LogDataKeys.Own, string.IsNullOrEmpty(offer.MakerName) ? 1 : 0);
                record.Withdrawn.Add(line);
            }
            return record;
        }

        /// <summary>The offer needs more lines than the producer has free (and the numbers are known): the game, not checking, would let the player sign it.</summary>
        private static bool Short(TenderOffer offer)
        {
            return offer.CapacityLines > 0 && offer.RequiredLines + offer.ContractedLines > offer.CapacityLines;
        }

        /// <summary>The player's offer with the most IPS; the first one offered when two are equal. The caller has checked there is one.</summary>
        private static TenderOffer StrongestPlayerOffer(List<TenderOffer> offers)
        {
            TenderOffer best = default(TenderOffer);
            bool found = false;
            for (int i = 0; i < offers.Count; i++)
            {
                if (!offers[i].IsPlayer) continue;
                if (!found || offers[i].Ips > best.Ips)
                {
                    best = offers[i];
                    found = true;
                }
            }
            return best;
        }

        /// <summary>The player's offer that came last in <c>OffersIDs</c>.</summary>
        private static TenderOffer LastPlayerOffer(List<TenderOffer> offers)
        {
            for (int i = offers.Count - 1; i >= 0; i--)
            {
                if (offers[i].IsPlayer) return offers[i];
            }
            return default(TenderOffer);
        }

        private static bool TryFind(List<KeyValuePair<int, int>> chances, int cpuId, out int chance)
        {
            for (int i = 0; i < chances.Count; i++)
            {
                if (chances[i].Key == cpuId)
                {
                    chance = chances[i].Value;
                    return true;
                }
            }
            chance = 0;
            return false;
        }

        private static double Round(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        }
    }
}
