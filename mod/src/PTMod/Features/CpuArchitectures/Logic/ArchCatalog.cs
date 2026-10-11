using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The families of architectures the game plays with (FR-1): an immutable, validated list with a lookup by id. The pure half of the class lives
    /// here (it is under the unit tests); <c>Data/ArchCatalog.cs</c> adds the loading through <c>ModContent</c>. Build it from a list that went
    /// through <see cref="ArchCatalogValidator.Validate"/> (<see cref="TryCreate"/> does both).
    /// </summary>
    public sealed partial class ArchCatalog
    {
        /// <summary>The id of the default architecture (D7, FR-1 item 4): every CPU without a binding, every company at the start.</summary>
        public const string DefaultId = "proprietary";

        private readonly List<ArchFamily> _families = new List<ArchFamily>();
        private readonly Dictionary<string, ArchFamily> _byId = new Dictionary<string, ArchFamily>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _indexOf = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Copies the families in the given order; a null entry or a repeated id is ignored (the first one with the id stays).</summary>
        public ArchCatalog(IEnumerable<ArchFamily> families)
        {
            if (families == null) return;
            foreach (ArchFamily family in families)
            {
                if (family == null || family.Id == null || _byId.ContainsKey(family.Id)) continue;
                _indexOf[family.Id] = _families.Count;
                _byId[family.Id] = family;
                _families.Add(family);
            }
        }

        /// <summary>The families in the order of the content file (the order of the cards, the lists and the saves).</summary>
        public IReadOnlyList<ArchFamily> Families
        {
            get { return _families; }
        }

        public int Count
        {
            get { return _families.Count; }
        }

        /// <summary>The family with this id, or null (also for a null id).</summary>
        public ArchFamily Get(string id)
        {
            ArchFamily family;
            return id != null && _byId.TryGetValue(id, out family) ? family : null;
        }

        public bool Contains(string id)
        {
            return id != null && _byId.ContainsKey(id);
        }

        /// <summary>Position of the family in <see cref="Families"/>, or -1.</summary>
        public int IndexOf(string id)
        {
            int index;
            return id != null && _indexOf.TryGetValue(id, out index) ? index : -1;
        }

        /// <summary>The family <see cref="DefaultId"/>; null only for a catalog built from a list without it (a validated catalog always has it).</summary>
        public ArchFamily Default
        {
            get { return Get(DefaultId); }
        }

        /// <summary>"семейство" with the number: 1 семейство, 2-4 семейства, 0 and 5-20 семейств.</summary>
        internal static string FamiliesInRussian(int count)
        {
            int tens = count % 100;
            int ones = count % 10;
            if (tens >= 11 && tens <= 14) return "семейств";
            if (ones == 1) return "семейство";
            if (ones >= 2 && ones <= 4) return "семейства";
            return "семейств";
        }

        /// <summary>
        /// Validates the list and builds the catalog from the families that passed. False (and a null catalog) when the default family is not among
        /// them: the feature cannot work then (FR-1 item 4). <paramref name="result"/> always holds the verdict, with the reasons.
        /// </summary>
        public static bool TryCreate(IReadOnlyList<ArchFamily> raw, out ArchCatalog catalog, out ValidationResult result)
        {
            result = ArchCatalogValidator.Validate(raw);
            catalog = result.HasDefault ? new ArchCatalog(result.Families) : null;
            return catalog != null;
        }
    }
}
