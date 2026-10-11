using System;
using PTMod.Core;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The arithmetic of the metrics (design.md, "Метрики"; FR-5 item 1, FR-16 item 1): folds a day or a month into the accumulators of a
    /// <see cref="MetricsState"/> and reads all the metrics of the vocabulary off the state and the last samples as a
    /// <see cref="MetricsSnapshot"/>. No game types, no clock: the day and the month come in as samples, so every series is tested without Unity.
    /// </summary>
    public static class MetricsCalculator
    {
        /// <summary>
        /// Folds one game day into the state: the window of sales, the total of sales, the minimum of the money, the streaks of solvency,
        /// of days without missed sales and of the lead in performance, and the exit from default. Call once per day, in order.
        /// </summary>
        public static void ApplyDay(MetricsState state, DaySample day)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (day == null) throw new ArgumentNullException("day");

            state.RunDays = day.RunDays;
            state.ShareWindow.Push(day.PlayerSold, day.TotalSold);

            long sold = day.PlayerSold != null && day.PlayerSold.Length > 0 ? day.PlayerSold[0] : 0L;
            if (sold > 0) state.SalesTotal += sold;

            if (day.Money < state.MinMoney) state.MinMoney = day.Money;

            if (day.Money >= 0)
            {
                state.SolventStreakDays++;
                state.NegativeStreakDays = 0;
            }
            else
            {
                state.NegativeStreakDays++;
                state.SolventStreakDays = 0;
            }

            if (sold > 0 && day.MissedSales <= 0) state.MissedZeroStreakDays++;
            else state.MissedZeroStreakDays = 0;

            double playerIps = PlayerBestIps(state, day);
            if (playerIps > 0 && playerIps > day.RivalBestIps) state.IpsLeaderStreakDays++;
            else state.IpsLeaderStreakDays = 0;

            if (state.WasInDefaultYesterday && !day.IsInDefault && !day.IsBankrupt) state.DefaultRecovered++;
            state.WasInDefaultYesterday = day.IsInDefault;
        }

        /// <summary>
        /// Folds the last full month into the state: the streak of profitable months grows when the profit is above zero and starts over
        /// otherwise. A month without a report, and a month that was already counted (or an older one), change nothing.
        /// </summary>
        public static void ApplyMonth(MetricsState state, MonthSample month)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (month == null) throw new ArgumentNullException("month");
            if (!month.HasReport) return;
            if (month.Key <= state.LastMonthKey) return;

            state.LastMonthKey = month.Key;
            if (month.Profit > 0) state.ProfitStreakMonths++;
            else state.ProfitStreakMonths = 0;
        }

        /// <summary>An OEM contract was signed for a CPU of the player.</summary>
        public static void OnOemSigned(MetricsState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            state.OemSignedTotal++;
        }

        /// <summary>An AI company went bankrupt (and the player did not absorb it: the caller filters that).</summary>
        public static void OnRivalBankrupt(MetricsState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            state.RivalsBankrupt++;
        }

        /// <summary>The player received a CPU by a transfer: it no longer counts as released, nor in the metrics of characteristics.</summary>
        public static void OnCpuTransferred(MetricsState state, int cpuSaveId)
        {
            if (state == null) throw new ArgumentNullException("state");
            state.TransferredCpuIds.Add(cpuSaveId);
        }

        /// <summary>
        /// Reads every metric that the state and the samples can answer. The metrics of the state are always there; those of the day need
        /// <paramref name="lastDay"/>, those of the month need <paramref name="lastMonth"/> (with a report) - otherwise they are not known.
        /// </summary>
        public static MetricsSnapshot Snapshot(MetricsState state, DaySample lastDay, MonthSample lastMonth)
        {
            if (state == null) throw new ArgumentNullException("state");
            var s = new MetricsSnapshot();

            // the accumulators: known from the start of the run
            s.Set("money.solventStreakDays", state.SolventStreakDays);
            s.Set("money.negativeStreakDays", state.NegativeStreakDays);
            s.Set("profit.streakMonths", state.ProfitStreakMonths);
            s.Set("sales.total", state.SalesTotal);
            s.Set("missed.zeroStreakDays", state.MissedZeroStreakDays);
            s.Set("ips.leaderStreakDays", state.IpsLeaderStreakDays);
            s.Set("oem.signed.total", state.OemSignedTotal);
            s.Set("rivals.bankruptCount", state.RivalsBankrupt);
            s.Set("default.recoveredCount", state.DefaultRecovered);
            s.Set("run.days", state.RunDays);
            if (state.MinMoney < MetricsState.NoMinMoney) s.Set("money.min", state.MinMoney);

            RollingWindow window = state.ShareWindow;
            s.Set("share.total.avg30", window.Share(0));
            double highEnd = window.Share(RollingWindow.ColumnOf(Segment.HighEnd));
            double midRange = window.Share(RollingWindow.ColumnOf(Segment.MidRange));
            double lowEnd = window.Share(RollingWindow.ColumnOf(Segment.LowEnd));
            double industries = window.Share(RollingWindow.ColumnOf(Segment.Industries));
            s.Set("share.highend.avg30", highEnd);
            s.Set("share.midrange.avg30", midRange);
            s.Set("share.lowend.avg30", lowEnd);
            s.Set("share.industries.avg30", industries);
            s.Set("share.minSegment.avg30", Math.Min(Math.Min(highEnd, midRange), Math.Min(lowEnd, industries)));

            if (lastDay != null)
            {
                s.Set("money", lastDay.Money);
                s.Set("popularity.max", lastDay.MaxPopularity);
                s.Set("oem.active.count", lastDay.ActiveOemContracts);
                s.Set("foundry.clients.active", lastDay.FoundryClientsActive);
                s.Set("foundry.asClient.count", lastDay.FoundryAsClientCount);
                s.Set("factory.lines", lastDay.FactoryLines);
                s.Set("run.difficulty", lastDay.Difficulty);
                SetCharacteristics(s, state, lastDay);
            }

            if (lastMonth != null && lastMonth.HasReport)
            {
                s.Set("profit.lastMonth", lastMonth.Profit);
                s.Set("revenue.last12m", lastMonth.Revenue12m);
                s.Set("revenue.clients.last12m", lastMonth.ClientsRevenue12m);
            }

            return s;
        }

        // The CPUs that came by a transfer are not the player's releases: they stay out of the count and of every characteristic.
        private static void SetCharacteristics(MetricsSnapshot s, MetricsState state, DaySample day)
        {
            int count = 0;
            float maxFrequency = 0;
            int maxCores = 0;
            bool smt = false;
            if (day.PlayerCpus != null)
            {
                for (int i = 0; i < day.PlayerCpus.Count; i++)
                {
                    CpuInfo cpu = day.PlayerCpus[i];
                    if (cpu == null || state.TransferredCpuIds.Contains(cpu.SaveId)) continue;
                    count++;
                    if (cpu.Frequency > maxFrequency) maxFrequency = cpu.Frequency;
                    if (cpu.Cores > maxCores) maxCores = cpu.Cores;
                    if (cpu.Smt) smt = true;
                }
            }
            s.Set("cpu.released.count", count);
            s.Set("cpu.maxFrequency", maxFrequency);
            s.Set("cpu.maxCores", maxCores);
            s.Set("cpu.hasSmt", smt ? 1 : 0);
        }

        // The fastest CPU of the player that is still sold and was released by the player (a transferred one does not count).
        private static double PlayerBestIps(MetricsState state, DaySample day)
        {
            double best = 0;
            if (day.PlayerCpus == null) return best;
            for (int i = 0; i < day.PlayerCpus.Count; i++)
            {
                CpuInfo cpu = day.PlayerCpus[i];
                if (cpu == null || !cpu.OnMarket || state.TransferredCpuIds.Contains(cpu.SaveId)) continue;
                if (cpu.Ips > best) best = cpu.Ips;
            }
            return best;
        }
    }
}
