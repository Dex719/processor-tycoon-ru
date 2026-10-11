using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.ResearchTreeUx;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The rows of the compare window of <c>research-tree-ux</c> (FR-5 item 5; design.md Component 14), registered once with
    /// <c>ResearchTreeUxApi.RegisterCompareRows</c>: "Архитектура" (the name of the family of each CPU) and "Множитель рейтинга (ВС/СС/НС/Пром)" (the
    /// contribution of the family to the rating of the CPU in the four segments this year - the numbers the market gets from the provider of
    /// <c>market.rating.cpu</c>, <see cref="ArchRatingProvider.ContributionOf"/>, without the contributions of other features). Both are text rows with
    /// no highlight: a family is not "better" or "worse" in general. They stand after the built-in rows (the built-in ones use 10…200).
    /// <para>
    /// Read only what the game itself shows for any company (the family of a CPU is what the inspector writes for it, FR-6 item 10 of
    /// <c>research-tree-ux</c>). A side that is not chosen yet (null) gets the dash. No rows while the feature is off; an exception is counted against
    /// this feature by <c>research-tree-ux</c> (it asks inside a guard) and only these rows go away until the game restarts.
    /// </para>
    /// </summary>
    internal sealed class ArchCompareRows : ICompareRowProvider
    {
        public const string ArchRowId = "cpu-architectures.arch";
        public const string RatingRowId = "cpu-architectures.rating";
        public const int ArchRowOrder = 205;
        public const int RatingRowOrder = 210;

        private readonly ArchitecturesFeature _feature;

        public ArchCompareRows(ArchitecturesFeature feature)
        {
            _feature = feature;
        }

        public IEnumerable<CompareRow> GetRows(GameCpu a, GameCpu b)
        {
            if (_feature == null || _feature.IsNeutralNow || _feature.Registry == null || _feature.Catalog == null) return null;

            var rows = new List<CompareRow>(2);
            rows.Add(new CompareRow
            {
                Id = ArchRowId,
                LabelKey = "arch.compare.arch",
                ValueA = Architecture(a),
                ValueB = Architecture(b),
                Better = Better.None,
                Order = ArchRowOrder,
            });

            if ((_feature.Parts & ArchParts.Market) != 0 && _feature.Rating != null)
            {
                rows.Add(new CompareRow
                {
                    Id = RatingRowId,
                    LabelKey = "arch.compare.rating",
                    ValueA = Rating(a),
                    ValueB = Rating(b),
                    Better = Better.None,
                    Order = RatingRowOrder,
                });
            }
            return rows;
        }

        /// <summary>The name of the family of the CPU (the same text the inspector shows).</summary>
        private string Architecture(GameCpu cpu)
        {
            if (cpu == null) return ArchDisplay.Empty;
            string name = InspectorIntegration.NameOf(_feature, cpu);
            return name ?? ArchDisplay.Empty;
        }

        /// <summary>"×1,05 / ×1,08 / ×1,05 / ×0,95": the contribution of the family of the CPU in HighEnd, MidRange, LowEnd, Industries.</summary>
        private string Rating(GameCpu cpu)
        {
            if (cpu == null) return ArchDisplay.Empty;

            ArchFamily family = _feature.Catalog.Get(_feature.Registry.ResolveCpu(cpu));
            if (family == null) return ArchDisplay.Empty;   // a family the catalog does not know has no multiplier

            var values = new float[RatingForecast.SegmentCount];
            for (int i = 0; i < values.Length; i++) values[i] = _feature.Rating.ContributionOf(family, cpu, (Segment)i);

            string[] t = RatingForecast.FormatAll(values, ModL10n.Language);
            return ModL10n.L("arch.compare.ratingValue", t[0], t[1], t[2], t[3]);
        }
    }
}
