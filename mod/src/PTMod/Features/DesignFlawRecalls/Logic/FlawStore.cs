using System;
using System.Collections.Generic;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The records of the feature and the marks next to them (design.md Component 4, the internal half of <c>FlawRegistry</c>): the typed face over
    /// <see cref="FlawSaveData"/>. It adds nothing to the save; every record and every mark is a field of the saved data, so a save/load round trip
    /// is the round trip of the DTOs. The data object is asked for on every call (<c>FlawSaveSection</c> replaces it on a load or a new game), and the
    /// indexes are rebuilt when it was replaced. Main thread only; pure (no game types).
    /// </summary>
    public sealed class FlawStore
    {
        private readonly Func<FlawSaveData> _source;
        private readonly List<FlawRecord> _records = new List<FlawRecord>();
        private readonly Dictionary<int, FlawRecord> _byId = new Dictionary<int, FlawRecord>();
        private readonly Dictionary<int, FlawRecord> _byCpu = new Dictionary<int, FlawRecord>();
        private readonly HashSet<int> _rolled = new HashSet<int>();
        private readonly HashSet<int> _rush = new HashSet<int>();

        private FlawSaveData _data;
        private int _recordListCount, _rolledListCount, _rushListCount;
        private int _maxId;

        /// <param name="source">Gives the current saved data (<c>() =&gt; section.Data</c>); called on every access, so a replaced object is noticed.</param>
        public FlawStore(Func<FlawSaveData> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            _source = source;
        }

        /// <summary>The saved data the store works on.</summary>
        public FlawSaveData Data
        {
            get
            {
                Sync();
                return _data;
            }
        }

        /// <summary>The last game day the feature saw while enabled (the end of <c>OnDay</c>, <c>OnDisabled</c>, <c>OnResume</c>); 0 before the first.</summary>
        public int LastActiveDay
        {
            get { return Data.lastActiveDay; }
            set { Data.lastActiveDay = value; }
        }

        /// <summary>Every record, in the order of the ids (the final ones too). Do not change the list.</summary>
        public IReadOnlyList<FlawRecord> All
        {
            get
            {
                Sync();
                return _records;
            }
        }

        /// <summary>The records that are not over (not Resolved, Expired or Closed), in the order of the ids. A fresh list each time.</summary>
        public List<FlawRecord> Active()
        {
            Sync();
            var list = new List<FlawRecord>();
            foreach (FlawRecord record in _records)
            {
                if (!record.IsFinal) list.Add(record);
            }
            return list;
        }

        /// <summary>
        /// The records the monthly monitor has to look at: the ones that are not over, and the ones that are resolved by a quiet revision whose leak
        /// tail still runs on <paramref name="today"/> (FR-7 item 4). In the order of the ids; a fresh list each time.
        /// </summary>
        public List<FlawRecord> Monitored(int today)
        {
            Sync();
            var list = new List<FlawRecord>();
            foreach (FlawRecord record in _records)
            {
                if (!record.IsFinal)
                {
                    list.Add(record);
                }
                else if (record.State == FlawState.Resolved && record.Data.quietStepping && record.Data.leakTailUntilDay >= today)
                {
                    list.Add(record);
                }
            }
            return list;
        }

        /// <summary>The record with this id, or <c>null</c>.</summary>
        public FlawRecord Find(int recordId)
        {
            Sync();
            FlawRecord record;
            return _byId.TryGetValue(recordId, out record) ? record : null;
        }

        /// <summary>The record that covers the CPU (the base CPU or one of its variants), final or not; <c>null</c> when there is none.</summary>
        public FlawRecord FindByCpu(int cpuSaveId)
        {
            Sync();
            FlawRecord record;
            return _byCpu.TryGetValue(cpuSaveId, out record) ? record : null;
        }

        // ---- the marks ----

        /// <summary>The CPU was already rolled for a flaw (FR-1 item 5), in this save or before the mod knew the save.</summary>
        public bool WasRolled(int cpuSaveId)
        {
            Sync();
            return _rolled.Contains(cpuSaveId);
        }

        /// <summary>Remembers that the CPU was rolled; a second call changes nothing.</summary>
        public void MarkRolled(int cpuSaveId)
        {
            Sync();
            if (!_rolled.Add(cpuSaveId)) return;

            _data.rolledCpuIds.Add(cpuSaveId);
            _rolledListCount = _data.rolledCpuIds.Count;
        }

        /// <summary>The CPU was made with the rush project (FR-20).</summary>
        public bool IsRush(int cpuSaveId)
        {
            Sync();
            return _rush.Contains(cpuSaveId);
        }

        /// <summary>Remembers that the CPU was made with the rush project; a second call changes nothing.</summary>
        public void MarkRush(int cpuSaveId)
        {
            Sync();
            if (!_rush.Add(cpuSaveId)) return;

            _data.rushCpuIds.Add(cpuSaveId);
            _rushListCount = _data.rushCpuIds.Count;
        }

        // ---- changes ----

        /// <summary>
        /// Creates the record of a hidden flaw for a base CPU (design.md Component 4): the next id, the state <see cref="FlawState.Hidden"/>, the base
        /// CPU as the only affected one. Returns <c>null</c>, and creates nothing, when a record already covers this CPU. Does not mark the CPU as
        /// rolled: the caller does (a CPU without a flaw is marked too).
        /// </summary>
        /// <param name="risk">The probability the flaw was rolled with (<c>riskAtRelease</c>).</param>
        /// <param name="releaseDay">The day of the release (days from 1971-01-01).</param>
        /// <param name="inflation">The inflation at the release (the quote brings the development cost to today's prices with it).</param>
        public FlawRecord Create(string companyKey, int baseCpuId, FlawTypeDef type, FlawSeverity severity, float risk, int releaseDay, float inflation)
        {
            Sync();
            if (type == null || _byCpu.ContainsKey(baseCpuId)) return null;

            var dto = new FlawRecordDto
            {
                id = NextId(),
                companyKey = companyKey,
                baseCpuId = baseCpuId,
                cpuIds = new List<int> { baseCpuId },
                typeId = type.Id,
                severity = (int)severity,
                state = (int)FlawState.Hidden,
                source = (int)DiscoverySource.None,
                response = (int)FlawResponse.None,
                riskAtRelease = risk,
                inflationAtRelease = inflation,
                releaseDay = releaseDay,
            };
            _data.records.Add(dto);
            _recordListCount = _data.records.Count;

            var record = new FlawRecord(dto);
            Index(record);
            return record;
        }

        /// <summary>
        /// Adds a variant of the base CPU to the CPUs the flaw affects (a variant shares the die of its base, FR-1 item 3). Refused (<c>false</c>) for a
        /// record that is over: units made after the fix are not affected. Adding the same CPU twice is not an error and changes nothing.
        /// </summary>
        public bool AttachVariant(FlawRecord record, int variantCpuId)
        {
            Sync();
            if (record == null || record.IsFinal) return false;
            if (record.CoversCpu(variantCpuId)) return true;

            FlawRecord other;
            if (_byCpu.TryGetValue(variantCpuId, out other) && !ReferenceEquals(other, record)) return false;

            record.Data.cpuIds.Add(variantCpuId);
            _byCpu[variantCpuId] = record;
            return true;
        }

        /// <summary>The record follows its CPU to another company (FR-19): only the key of the owner changes.</summary>
        public void MoveOwner(FlawRecord record, string newCompanyKey)
        {
            if (record == null || string.IsNullOrEmpty(newCompanyKey)) return;
            record.CompanyKey = newCompanyKey;
        }

        // ---- indexes ----

        private int NextId()
        {
            int id = Math.Max(Math.Max(_data.nextRecordId, 1), _maxId + 1);
            _data.nextRecordId = id + 1;
            return id;
        }

        private void Index(FlawRecord record)
        {
            _records.Add(record);
            _byId[record.Id] = record;
            if (record.Id > _maxId) _maxId = record.Id;
            foreach (int cpuId in record.Data.cpuIds) _byCpu[cpuId] = record;
        }

        /// <summary>Follows the data object: rebuilds the indexes when it was replaced or its lists were changed from outside.</summary>
        private void Sync()
        {
            FlawSaveData data = _source();
            if (data == null) throw new InvalidOperationException("the source of the flaw data returned null");

            bool same = ReferenceEquals(data, _data)
                        && data.records.Count == _recordListCount
                        && data.rolledCpuIds.Count == _rolledListCount
                        && data.rushCpuIds.Count == _rushListCount;
            if (same) return;

            _data = data;
            _maxId = 0;
            _records.Clear();
            _byId.Clear();
            _byCpu.Clear();
            _rolled.Clear();
            _rush.Clear();

            foreach (FlawRecordDto dto in data.records)
            {
                if (dto == null) continue;
                if (dto.cpuIds == null) dto.cpuIds = new List<int>();
                Index(new FlawRecord(dto));
            }
            _records.Sort(CompareById);

            foreach (int id in data.rolledCpuIds) _rolled.Add(id);
            foreach (int id in data.rushCpuIds) _rush.Add(id);

            _recordListCount = data.records.Count;
            _rolledListCount = data.rolledCpuIds.Count;
            _rushListCount = data.rushCpuIds.Count;
        }

        private static int CompareById(FlawRecord a, FlawRecord b)
        {
            return a.Id.CompareTo(b.Id);
        }
    }
}
