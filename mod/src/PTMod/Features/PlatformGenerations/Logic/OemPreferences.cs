using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The generation each contract prefers in the "per contract" mode (FR-8 items 3 and 8, design D6): assigned once per <c>ContractKey</c>,
    /// kept in the save, dropped when the contract is gone. "" is a recorded answer too: the contract prefers no generation. Pure logic; the
    /// ids of generations that the content no longer has are kept as they are (FR-12 item 6).
    /// </summary>
    public sealed class OemPreferences
    {
        private readonly Dictionary<string, string> _byContract = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        /// <summary>How many contracts have a recorded preference (the ones with "" included).</summary>
        public int Count
        {
            get { return _byContract.Count; }
        }

        /// <summary>The generation id the contract prefers, "" when it was decided that it prefers none, null when nothing is assigned yet.</summary>
        public string PreferenceOf(string contractKey)
        {
            string gen;
            return contractKey != null && _byContract.TryGetValue(contractKey, out gen) ? gen : null;
        }

        /// <summary>
        /// Records the preference of a contract (null or "" = none). The first answer stands - a preference is decided once, so that it does not
        /// jump when the installed base changes (D6). False for an empty key and when the contract has an answer already.
        /// </summary>
        public bool Assign(string contractKey, string genId)
        {
            if (string.IsNullOrEmpty(contractKey) || _byContract.ContainsKey(contractKey)) return false;

            _byContract[contractKey] = genId ?? string.Empty;
            _order.Add(contractKey);
            return true;
        }

        /// <summary>
        /// FR-8 item 8: removes the contracts for which <paramref name="stillExists"/> says false (they are not among the available and active
        /// contracts any more). Returns how many were removed. A null function removes nothing.
        /// </summary>
        public int Prune(Func<string, bool> stillExists)
        {
            if (stillExists == null) return 0;

            int removed = 0;
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                string key = _order[i];
                if (stillExists(key)) continue;
                _byContract.Remove(key);
                _order.RemoveAt(i);
                removed++;
            }
            return removed;
        }

        public void Clear()
        {
            _byContract.Clear();
            _order.Clear();
        }

        /// <summary>Writes the preferences into <paramref name="save"/> (<c>contractPrefs</c>), in the order they were assigned; the chronology is not touched.</summary>
        public void WriteTo(PlatformGenerationsSave save)
        {
            if (save == null) return;

            var prefs = new List<ContractPrefDto>(_order.Count);
            foreach (string key in _order) prefs.Add(new ContractPrefDto { key = key, gen = _byContract[key] });
            save.contractPrefs = prefs;
        }

        /// <summary>
        /// Replaces the preferences with the ones in <paramref name="save"/>. A record with an empty key and a repeated key are dropped (the first
        /// stands), a missing <c>gen</c> reads as "" (none). One English line per problem goes to <paramref name="problems"/> (may be null). Never throws.
        /// </summary>
        public void ReadFrom(PlatformGenerationsSave save, List<string> problems)
        {
            Clear();
            if (save == null || save.contractPrefs == null) return;

            for (int i = 0; i < save.contractPrefs.Count; i++)
            {
                ContractPrefDto dto = save.contractPrefs[i];
                if (dto == null) continue;
                if (string.IsNullOrEmpty(dto.key))
                {
                    if (problems != null) problems.Add("contractPrefs[" + i + "]: the key is empty, the record is dropped");
                    continue;
                }
                if (!Assign(dto.key, dto.gen) && problems != null)
                    problems.Add("contractPrefs[" + i + "]: key '" + dto.key + "' repeats an earlier record, the record is dropped");
            }
        }
    }
}
