using System;
using System.Collections.Generic;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The snapshots the public API hands out (<see cref="FlawRecordView"/>), made from the records of the feature. A snapshot is a copy: it does not
    /// change when the record does (design.md Component 4). This half of the class is pure (it is in <c>Logic/</c> so that the tests compile it); the
    /// other half, <c>FlawViews.cs</c>, announces a snapshot to the subscribers of the registry.
    /// </summary>
    public static partial class FlawViews
    {
        /// <summary>The snapshot of a record as of day <paramref name="today"/> (the days left of a running revision or patch are counted to it).</summary>
        public static FlawRecordView Of(FlawRecord record, int today)
        {
            if (record == null) throw new ArgumentNullException("record");
            FlawRecordDto d = record.Data;

            int daysLeft = 0;
            if (record.State == FlawState.Stepping) daysLeft = d.steppingEndDay - today;
            else if (record.State == FlawState.Mitigated) daysLeft = d.mitigationEndDay - today;

            return new FlawRecordView
            {
                Id = d.id,
                CompanyKey = d.companyKey,
                BaseCpuId = d.baseCpuId,
                CpuIds = new List<int>(d.cpuIds ?? new List<int>()).AsReadOnly(),
                TypeId = d.typeId,
                Severity = record.Severity,
                State = record.State,
                Response = record.Response,
                Source = record.Source,
                CoverUp = d.coverUp,
                DaysLeft = Math.Max(0, daysLeft),
                SpentTotal = d.spentTotal,
                IpsLoss = d.ipsLoss,
                PublicSinceDay = d.publicSinceDay,
            };
        }

        /// <summary>
        /// The snapshots of the records the API shows: every record that is not <see cref="FlawState.Hidden"/> (a hidden flaw is never shown, FR-13 item 3),
        /// final ones included. <paramref name="companyKey"/> limits them to one owner; <c>null</c> takes every owner. In the order of the ids.
        /// </summary>
        public static List<FlawRecordView> Known(IEnumerable<FlawRecord> records, string companyKey, int today)
        {
            var views = new List<FlawRecordView>();
            if (records == null) return views;

            foreach (FlawRecord record in records)
            {
                if (record == null || record.State == FlawState.Hidden) continue;
                if (companyKey != null && !string.Equals(record.CompanyKey, companyKey, StringComparison.Ordinal)) continue;
                views.Add(Of(record, today));
            }
            return views;
        }
    }
}
