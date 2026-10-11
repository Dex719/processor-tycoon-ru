using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// What happened during the run, as the checks see it: how many times each internal event (<see cref="ContentVocabulary.Events"/>) came, and
    /// the journal of the CPUs the player released. The counts live in the <see cref="MetricsState"/> of the run (they are saved, <c>eventCounts</c>);
    /// the journal is only in memory - a goal or an achievement on a release is settled the moment the release happens, so a load loses nothing.
    /// A CPU that came to the player by a transfer is not a release: it neither counts nor lands in the journal.
    /// </summary>
    public sealed class EventBook
    {
        private readonly MetricsState _state;
        private readonly List<CpuInfo> _playerReleases = new List<CpuInfo>();

        public EventBook(MetricsState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            _state = state;
        }

        /// <summary>How many times the event happened in this run.</summary>
        public int Count(string eventId)
        {
            int count;
            return eventId != null && _state.EventCounts.TryGetValue(eventId, out count) ? count : 0;
        }

        /// <summary>Counts an event and returns how many times it has happened now. A null or empty id is ignored (returns 0).</summary>
        public int Record(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return 0;
            int count;
            _state.EventCounts.TryGetValue(eventId, out count);
            count++;
            _state.EventCounts[eventId] = count;
            return count;
        }

        /// <summary>The CPU came to the player by a transfer (<see cref="MetricsState.TransferredCpuIds"/>).</summary>
        public bool IsTransferred(int cpuSaveId)
        {
            return _state.TransferredCpuIds.Contains(cpuSaveId);
        }

        /// <summary>The CPUs the player released since the run started (or since the load), oldest first; transferred ones are not in it.</summary>
        public IList<CpuInfo> PlayerReleases
        {
            get { return _playerReleases; }
        }

        /// <summary>
        /// Counts a release of a CPU: <c>cpu.released.player</c> or <c>cpu.released.ai</c>, and puts a player's CPU into the journal. Returns false
        /// (and records nothing) for a CPU that came by a transfer or for a null.
        /// </summary>
        public bool RecordCpuRelease(CpuInfo cpu, bool isPlayer)
        {
            if (cpu == null) return false;
            if (isPlayer && IsTransferred(cpu.SaveId)) return false;

            Record(isPlayer ? "cpu.released.player" : "cpu.released.ai");
            if (isPlayer) _playerReleases.Add(cpu);
            return true;
        }

        /// <summary>
        /// An AI company went bankrupt: counts the event <c>rival.bankrupt</c> and the metric <c>rivals.bankruptCount</c>, unless the player had
        /// absorbed the company (<c>RivalAcquisitionsApi.IsAbsorbedByPlayer</c>, FR-12 item 7) - then nothing is recorded, so absorbing rivals cannot
        /// farm "Outlived a rival" and "Last one standing". Returns true when the bankruptcy was counted.
        /// </summary>
        public bool RecordRivalBankrupt(bool absorbedByPlayer)
        {
            if (absorbedByPlayer) return false;
            MetricsCalculator.OnRivalBankrupt(_state);
            Record("rival.bankrupt");
            return true;
        }

        /// <summary>An OEM contract was signed for a CPU of the player: the metric <c>oem.signed.total</c> and the event <c>oem.signed</c>.</summary>
        public void RecordOemSigned()
        {
            MetricsCalculator.OnOemSigned(_state);
            Record("oem.signed");
        }

        /// <summary>The player went bankrupt: the event <c>player.bankrupt</c> (the condition <c>notBankrupt</c> of the achievements reads it).</summary>
        public void RecordPlayerBankrupt()
        {
            Record("player.bankrupt");
        }

        /// <summary>A CPU came to the player by a transfer: from now on it neither counts as released nor in the metrics of characteristics.</summary>
        public void RecordTransfer(int cpuSaveId)
        {
            MetricsCalculator.OnCpuTransferred(_state, cpuSaveId);
        }

        /// <summary>Empties the journal of the player's releases (the counts stay).</summary>
        public void ClearJournal()
        {
            _playerReleases.Clear();
        }
    }
}
