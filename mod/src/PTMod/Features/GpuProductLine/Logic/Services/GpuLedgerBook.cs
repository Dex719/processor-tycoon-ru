using System;
using System.Collections.Generic;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The monthly books of a product (<see cref="GpuLedgerSave"/>; FR-7 item 6): five lists of the same length, one slot a month starting at
    /// <c>startMonthIndex</c>, at most <c>limits.ledgerMonths</c> slots (the oldest go first). Production writes produced, failed and cost (TSK-016), the market
    /// writes sold and income (TSK-019). A month index is <c>year x 12 + month - 1</c> of the game date.
    /// </summary>
    public static class GpuLedgerBook
    {
        /// <summary>The month index of a date: <c>year x 12 + month - 1</c>.</summary>
        public static int MonthIndex(DateTime date)
        {
            return date.Year * 12 + date.Month - 1;
        }

        /// <summary>Adds a day of production to the month of <paramref name="monthIndex"/>.</summary>
        public static void AddProduction(GpuLedgerSave ledger, int monthIndex, int produced, int failed, float cost, int maxMonths)
        {
            int slot = Slot(ledger, monthIndex, maxMonths);
            if (slot < 0) return;
            ledger.produced[slot] = Sum(ledger.produced[slot], produced);
            ledger.failed[slot] = Sum(ledger.failed[slot], failed);
            ledger.cost[slot] = Sum(ledger.cost[slot], cost);
        }

        /// <summary>Adds a day of sales to the month of <paramref name="monthIndex"/>.</summary>
        public static void AddSales(GpuLedgerSave ledger, int monthIndex, int sold, float income, int maxMonths)
        {
            int slot = Slot(ledger, monthIndex, maxMonths);
            if (slot < 0) return;
            ledger.sold[slot] = Sum(ledger.sold[slot], sold);
            ledger.income[slot] = Sum(ledger.income[slot], income);
        }

        /// <summary>Adds a cost that is not production (storage, royalties) to the month.</summary>
        public static void AddCost(GpuLedgerSave ledger, int monthIndex, float cost, int maxMonths)
        {
            int slot = Slot(ledger, monthIndex, maxMonths);
            if (slot < 0) return;
            ledger.cost[slot] = Sum(ledger.cost[slot], cost);
        }

        /// <summary>
        /// The slot of the month, after the lists were brought to one length, grown up to the month and cut to <paramref name="maxMonths"/> from the front.
        /// An empty book starts at the month; a month before the start goes into the first slot. -1 for a null book.
        /// </summary>
        public static int Slot(GpuLedgerSave ledger, int monthIndex, int maxMonths)
        {
            if (ledger == null) return -1;
            if (ledger.sold == null) ledger.sold = new List<int>();
            if (ledger.produced == null) ledger.produced = new List<int>();
            if (ledger.failed == null) ledger.failed = new List<int>();
            if (ledger.income == null) ledger.income = new List<float>();
            if (ledger.cost == null) ledger.cost = new List<float>();

            int length = Math.Max(Math.Max(ledger.sold.Count, ledger.produced.Count), Math.Max(ledger.failed.Count, Math.Max(ledger.income.Count, ledger.cost.Count)));
            if (length == 0) ledger.startMonthIndex = monthIndex;
            int slot = Math.Max(0, monthIndex - ledger.startMonthIndex);
            length = Math.Max(length, slot + 1);
            Grow(ledger.sold, length);
            Grow(ledger.produced, length);
            Grow(ledger.failed, length);
            Grow(ledger.income, length);
            Grow(ledger.cost, length);

            int keep = Math.Max(1, maxMonths);
            if (length > keep)
            {
                int drop = length - keep;
                ledger.sold.RemoveRange(0, drop);
                ledger.produced.RemoveRange(0, drop);
                ledger.failed.RemoveRange(0, drop);
                ledger.income.RemoveRange(0, drop);
                ledger.cost.RemoveRange(0, drop);
                ledger.startMonthIndex += drop;
                slot -= drop;
            }
            return slot;
        }

        /// <summary>Keeps at most <paramref name="maxMonths"/> months (the oldest go first) without opening a new one; the lists are brought to one length first.</summary>
        public static void Trim(GpuLedgerSave ledger, int maxMonths)
        {
            if (ledger == null) return;
            int length = ledger.sold != null || ledger.produced != null || ledger.failed != null || ledger.income != null || ledger.cost != null
                ? Math.Max(Math.Max(Count(ledger.sold), Count(ledger.produced)), Math.Max(Count(ledger.failed), Math.Max(Count(ledger.income), Count(ledger.cost))))
                : 0;
            if (length == 0) return;
            Slot(ledger, ledger.startMonthIndex + length - 1, maxMonths);
        }

        private static int Count<T>(List<T> list)
        {
            return list != null ? list.Count : 0;
        }

        private static void Grow<T>(List<T> list, int length)
        {
            while (list.Count < length) list.Add(default(T));
        }

        private static int Sum(int a, int b)
        {
            long sum = (long)a + Math.Max(0, b);
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        private static float Sum(float a, float b)
        {
            float sum = a + (b > 0f && !float.IsInfinity(b) ? b : 0f);
            return float.IsInfinity(sum) ? float.MaxValue : sum;
        }
    }
}
