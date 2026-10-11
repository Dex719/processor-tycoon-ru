using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>What the game says about one CPU when another feature asks the registry to force a flaw on it (<see cref="IFlawApiHost.FindCpu"/>).</summary>
    public sealed class FlawCpuInfo
    {
        /// <summary><c>SaveID</c> of the CPU that was asked about.</summary>
        public int SaveId;

        /// <summary><c>SaveID</c> of the base CPU: <c>Cpu.Base.SaveID</c> for a variant, <see cref="SaveId"/> otherwise (a flaw belongs to the base CPU, FR-1 item 3).</summary>
        public int BaseSaveId;

        /// <summary>The CPU is a variant of another CPU.</summary>
        public bool IsVariant;

        /// <summary><c>ModCompanies.KeyOf</c> of the company that owns the CPU; <c>null</c> when the owner is not in the world.</summary>
        public string OwnerKey;

        /// <summary>The CPU was released (<c>Cpu.IsReleased</c>).</summary>
        public bool IsReleased;

        /// <summary>The CPU is off the market (<c>Cpu.IsRetired</c>): a flaw would not matter any more.</summary>
        public bool IsRetired;

        /// <summary>The day the CPU was released, as well as the game tells it (an estimate: the game keeps the year only).</summary>
        public int ReleaseDay;

        /// <summary>The numbers of the design, for the type weights and the risk of the record; <see cref="HasRisk"/> says whether they were read.</summary>
        public FlawRiskInput Risk;

        /// <summary>The numbers in <see cref="Risk"/> are real.</summary>
        public bool HasRisk;

        /// <summary><c>SaveID</c>s of the variants the base CPU has now (they join the record, FR-1 item 3). May be <c>null</c>.</summary>
        public List<int> VariantSaveIds;
    }

    /// <summary>What <see cref="FlawApiEngine"/> needs from the game and the settings; <c>FlawApiBackend</c> is the game's implementation, a test brings a fake.</summary>
    public interface IFlawApiHost
    {
        /// <summary>Today as a day number (days from 1971-01-01).</summary>
        int Today { get; }

        /// <summary>The numbers of the feature.</summary>
        FlawBalance Balance { get; }

        /// <summary>The random stream of the API (the choice of a type when the caller names none).</summary>
        IModRng Rng { get; }

        /// <summary>The <c>flawFrequency</c> setting (the multiplier of the probability stored in a new record).</summary>
        float FlawFrequency { get; }

        /// <summary>The <c>aiFlaws</c> setting: AI companies get flaws at all.</summary>
        bool AiFlawsEnabled { get; }

        /// <summary>The inflation now (stored in a new record).</summary>
        float Inflation { get; }

        /// <summary>The save of the feature is held back (written by a newer mod): no flaws are started for this save.</summary>
        bool SaveLocked { get; }

        /// <summary>The CPU with this <c>SaveID</c>, or <c>null</c> when the game has none. Reads the design and the variants too: used by <c>ForceFlaw</c> only.</summary>
        FlawCpuInfo FindCpu(int cpuSaveId);

        /// <summary>The game has a CPU with this <c>SaveID</c> (a cheap question: <c>TryReveal</c> asks it).</summary>
        bool CpuExists(int cpuSaveId);

        /// <summary>The company exists and has not gone bankrupt.</summary>
        bool OwnerIsAlive(string companyKey);

        /// <summary>A line for the log.</summary>
        void Log(string line);
    }

    /// <summary>
    /// The rules behind the public API of the feature (<c>FlawRegistry</c>, FR-13, design.md Component 4): make a hidden flaw known (<see cref="TryReveal"/>), force
    /// a flaw on a CPU (<see cref="ForceFlaw"/>) and read the records that are not hidden (<see cref="Known"/>, <see cref="FindByCpu"/>). Pure: the game is behind
    /// <see cref="IFlawApiHost"/>; <c>FlawCycle.Reveal</c> moves the record, saves the quote and lets the game react (the window of the player, the choice of an AI
    /// company, the announcement to the subscribers of <c>OnFlawStateChanged</c>).
    /// <para>
    /// Nothing here throws for ordinary input: an unknown CPU, an unknown type, a record that is already known are answered with <c>false</c> or <c>null</c> and
    /// change nothing. A hidden record is never handed out and never hinted at: <see cref="FindByCpu"/> answers <c>null</c> for it, <see cref="Known"/> skips it.
    /// </para>
    /// </summary>
    public sealed class FlawApiEngine
    {
        private readonly FlawStore _store;
        private readonly FlawCycle _cycle;
        private readonly IFlawApiHost _host;

        public FlawApiEngine(FlawStore store, FlawCycle cycle, IFlawApiHost host)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (cycle == null) throw new ArgumentNullException("cycle");
            if (host == null) throw new ArgumentNullException("host");
            _store = store;
            _cycle = cycle;
            _host = host;
        }

        /// <summary>The source is a way a flaw can come to light by the call of another feature: <c>Internal</c>, <c>External</c>, <c>Forced</c> or <c>Api</c> (not <c>None</c>, not <c>Leak</c>, nothing outside the enum).</summary>
        public static bool IsDiscoverySource(DiscoverySource source)
        {
            return source == DiscoverySource.Internal || source == DiscoverySource.External || source == DiscoverySource.Forced || source == DiscoverySource.Api;
        }

        // ---- TryReveal (FR-13 item 1) ----

        /// <summary>
        /// Makes the hidden flaw of the CPU (the base CPU or a variant) known by <paramref name="source"/>. <c>false</c>, and nothing is touched, for a CPU
        /// without a record, a record that is not hidden, a source that is not a discovery, an owner that is gone and a base CPU the game no longer has (the
        /// monthly pass closes such a record).
        /// </summary>
        public bool TryReveal(int cpuSaveId, DiscoverySource source)
        {
            if (_host.SaveLocked || !IsDiscoverySource(source)) return false;

            FlawRecord record = _store.FindByCpu(cpuSaveId);
            if (record == null || record.State != FlawState.Hidden) return false;
            if (!_host.OwnerIsAlive(record.CompanyKey) || !_host.CpuExists(record.BaseCpuId)) return false;

            bool revealed = _cycle.Reveal(record, source);
            if (revealed) _host.Log("api: " + record + " revealed by " + source);
            return revealed;
        }

        // ---- ForceFlaw (FR-13 item 2) ----

        /// <summary>
        /// Creates the record of a flaw for the CPU, or raises the severity of the hidden record it has, and starts the discovery by <paramref name="discovery"/> at
        /// once (the owner is asked for a decision when the discovery makes the flaw known, AC-13.1). <c>true</c> when the record exists afterwards and the
        /// discovery has started.
        /// <para>
        /// <c>false</c>, with no record made, for: an unknown CPU, a CPU that is not released, retired or has no owner in the world, an owner that is bankrupt, an AI
        /// owner while <c>aiFlaws</c> is off, a type id the content does not know (an empty id lets the feature choose by its usual weights), a severity or a
        /// discovery outside their enums, a CPU whose flaw is over (resolved, expired, closed) and a CPU whose flaw is known already (nothing to discover again).
        /// A variant is asked about through its base CPU: the record follows the base CPU and covers its variants.
        /// </para>
        /// </summary>
        public bool ForceFlaw(int cpuSaveId, string typeId, FlawSeverity severity, DiscoverySource discovery)
        {
            if (_host.SaveLocked || !IsDiscoverySource(discovery) || !Enum.IsDefined(typeof(FlawSeverity), severity)) return false;

            FlawBalance balance = _host.Balance;
            FlawTypeDef type = null;
            if (!string.IsNullOrEmpty(typeId))
            {
                type = balance.FindType(typeId);
                if (type == null) return false;
            }

            FlawCpuInfo cpu = _host.FindCpu(cpuSaveId);
            if (cpu == null || !cpu.IsReleased || cpu.IsRetired || string.IsNullOrEmpty(cpu.OwnerKey)) return false;
            if (!_host.OwnerIsAlive(cpu.OwnerKey)) return false;
            if (!IsPlayerKey(cpu.OwnerKey) && !_host.AiFlawsEnabled) return false;

            int baseId = cpu.BaseSaveId;
            FlawRecord record = _store.FindByCpu(baseId) ?? _store.FindByCpu(cpuSaveId);
            if (record != null)
            {
                if (record.State != FlawState.Hidden) return false;   // over, or known already
                if ((int)severity > record.Data.severity) record.Data.severity = (int)severity;
            }
            else
            {
                record = CreateRecord(cpu, type, severity, balance);
                if (record == null) return false;
            }

            AttachVariants(record, cpu);

            bool started = _cycle.Reveal(record, discovery);
            if (started) _host.Log("api: " + record + " forced by " + discovery);
            return started;
        }

        private FlawRecord CreateRecord(FlawCpuInfo cpu, FlawTypeDef type, FlawSeverity severity, FlawBalance balance)
        {
            int today = _host.Today;
            FlawRiskInput risk = cpu.HasRisk ? cpu.Risk : new FlawRiskInput { Year = FlawDays.YearOf(today) };
            risk.IsAi = !IsPlayerKey(cpu.OwnerKey);

            if (type == null) type = FlawRiskModel.PickType(FlawRiskModel.Factors(risk, balance), risk.Year > 0 ? risk.Year : FlawDays.YearOf(today), balance.Types, _host.Rng);

            // The probability is what the roll would have used; it is only stored (riskAtRelease) and read by nobody who decides.
            float p = cpu.HasRisk ? FlawRiskModel.Probability(risk, balance, _host.FlawFrequency) : 0f;
            int releaseDay = cpu.ReleaseDay > 0 && cpu.ReleaseDay <= today ? cpu.ReleaseDay : today;

            FlawRecord record = _store.Create(cpu.OwnerKey, cpu.BaseSaveId, type, severity, p, releaseDay, _host.Inflation);
            if (record != null) _store.MarkRolled(cpu.BaseSaveId);   // the CPU has its flaw: the roll at the release must never run for it
            return record;
        }

        private void AttachVariants(FlawRecord record, FlawCpuInfo cpu)
        {
            if (cpu.VariantSaveIds != null)
            {
                foreach (int variantId in cpu.VariantSaveIds) _store.AttachVariant(record, variantId);
            }
            if (cpu.IsVariant) _store.AttachVariant(record, cpu.SaveId);
        }

        private static bool IsPlayerKey(string companyKey)
        {
            return CompanyKey.IsPlayer(companyKey);
        }

        // ---- the records that are not hidden (FR-13 item 3) ----

        /// <summary>Snapshots of the records of one company that are not hidden (final ones included); empty for a company without records or a <c>null</c> key.</summary>
        public IReadOnlyList<FlawRecordView> Known(string companyKey)
        {
            if (companyKey == null) return new FlawRecordView[0];
            return FlawViews.Known(_store.All, companyKey, _host.Today).AsReadOnly();
        }

        /// <summary>Snapshots of the records of every company that are not hidden.</summary>
        public IReadOnlyList<FlawRecordView> AllKnown()
        {
            return FlawViews.Known(_store.All, null, _host.Today).AsReadOnly();
        }

        /// <summary>The snapshot of the record that covers the CPU (the base CPU or a variant), or <c>null</c> for none and for a hidden one.</summary>
        public FlawRecordView FindByCpu(int cpuSaveId)
        {
            FlawRecord record = _store.FindByCpu(cpuSaveId);
            if (record == null || record.State == FlawState.Hidden) return null;
            return FlawViews.Of(record, _host.Today);
        }
    }
}
