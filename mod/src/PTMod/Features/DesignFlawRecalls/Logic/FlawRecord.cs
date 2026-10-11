using System;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// One flaw record at run time: a typed face over the saved <see cref="FlawRecordDto"/> (the same object, so a change is saved without any copying
    /// back). The enums are kept in the DTO as ints (<see cref="FlawSaveCodec"/> checked them on load). The state changes only through
    /// <see cref="FlawStateMachine"/>: the setters here are internal. The three "run time only" members at the bottom are not saved.
    /// </summary>
    public sealed class FlawRecord
    {
        public FlawRecord(FlawRecordDto data)
        {
            if (data == null) throw new ArgumentNullException("data");
            Data = data;
        }

        /// <summary>The saved fields. The same object that is in <c>FlawSaveData.records</c>.</summary>
        public FlawRecordDto Data { get; private set; }

        /// <summary>The id of the record, unique in a save.</summary>
        public int Id
        {
            get { return Data.id; }
        }

        /// <summary>The key of the owner (<c>player</c> or <c>ai:&lt;UniqueID&gt;</c>). It changes when the CPU is taken over by another company.</summary>
        public string CompanyKey
        {
            get { return Data.companyKey; }
            internal set { Data.companyKey = value; }
        }

        /// <summary><c>SaveID</c> of the base CPU the flaw was rolled for.</summary>
        public int BaseCpuId
        {
            get { return Data.baseCpuId; }
        }

        /// <summary>The id of the type of the flaw (<see cref="FlawTypeDef.Id"/>).</summary>
        public string TypeId
        {
            get { return Data.typeId; }
        }

        public FlawSeverity Severity
        {
            get { return (FlawSeverity)Data.severity; }
        }

        public FlawState State
        {
            get { return (FlawState)Data.state; }
            internal set { Data.state = (int)value; }
        }

        public DiscoverySource Source
        {
            get { return (DiscoverySource)Data.source; }
            internal set { Data.source = (int)value; }
        }

        public FlawResponse Response
        {
            get { return (FlawResponse)Data.response; }
            internal set { Data.response = (int)value; }
        }

        /// <summary>The record is over (<see cref="FlawState.Resolved"/>, <see cref="FlawState.Expired"/> or <see cref="FlawState.Closed"/>).</summary>
        public bool IsFinal
        {
            get { return FlawStateMachine.IsFinal(State); }
        }

        /// <summary>The flaw affects this CPU: the base CPU or one of its variants.</summary>
        public bool CoversCpu(int cpuSaveId)
        {
            return Data.cpuIds != null && Data.cpuIds.Contains(cpuSaveId);
        }

        /// <summary>The quote saved at the discovery, or <c>null</c> before it.</summary>
        public FlawQuote Quote
        {
            get { return FlawQuote.FromDto(Data.quote); }
        }

        /// <summary>A one-line description for the log.</summary>
        public override string ToString()
        {
            return "flaw #" + Data.id + " " + Data.typeId + "/" + Severity + " " + State + " of " + Data.companyKey + " (cpu " + Data.baseCpuId + ")";
        }

        // ---- run time only (not saved) ----

        /// <summary>The units sold by the affected CPUs, as the monthly monitor read them last time (<c>U</c> of the formulas, cached once a month).</summary>
        public long CachedUnits { get; internal set; }

        /// <summary>The month (<see cref="FlawDays.MonthKey"/>) <see cref="CachedUnits"/> belongs to; -1 before the first reading.</summary>
        public int CachedUnitsMonth { get; internal set; } = -1;

        /// <summary>How many times in a row the processing of this record failed (3 close it, design.md "Error Handling").</summary>
        public int FailStreak { get; internal set; }
    }
}
