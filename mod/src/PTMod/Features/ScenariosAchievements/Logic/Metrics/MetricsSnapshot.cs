using System;
using System.Collections.Generic;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>
    /// The values of the metrics at one moment: a number per metric of <see cref="ContentVocabulary.Metrics"/>, or "not known". A metric is not
    /// known when the sample it needs was not taken yet (no day since the load, no month report), and for an id the code does not know (content
    /// newer than the code); a goal on such a metric simply stays open. The values live in arrays indexed by the position of the metric in the
    /// vocabulary, so the daily evaluation allocates nothing but the snapshot itself.
    /// </summary>
    public sealed class MetricsSnapshot
    {
        private static readonly Dictionary<string, int> Index = BuildIndex();

        private readonly double[] _values = new double[Index.Count];
        private readonly bool[] _known = new bool[Index.Count];

        /// <summary>True when the snapshot has a value for the metric.</summary>
        public bool Has(string metricId)
        {
            int index;
            return metricId != null && Index.TryGetValue(metricId, out index) && _known[index];
        }

        /// <summary>The value of the metric, or <see cref="double.NaN"/> when it is not known (every comparison with it is false).</summary>
        public double Get(string metricId)
        {
            double value;
            return TryGet(metricId, out value) ? value : double.NaN;
        }

        /// <summary>The value of the metric; false when it is not known.</summary>
        public bool TryGet(string metricId, out double value)
        {
            int index;
            if (metricId != null && Index.TryGetValue(metricId, out index) && _known[index])
            {
                value = _values[index];
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>Sets the value of a metric. An id that is not in the vocabulary is a programming error.</summary>
        public void Set(string metricId, double value)
        {
            int index;
            if (metricId == null || !Index.TryGetValue(metricId, out index))
            {
                throw new ArgumentException("not a metric of the vocabulary: " + metricId, "metricId");
            }
            _values[index] = value;
            _known[index] = true;
        }

        /// <summary>Marks a metric as not known again.</summary>
        public void Clear(string metricId)
        {
            int index;
            if (metricId != null && Index.TryGetValue(metricId, out index)) _known[index] = false;
        }

        /// <summary>How many metrics have a value.</summary>
        public int KnownCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _known.Length; i++)
                {
                    if (_known[i]) count++;
                }
                return count;
            }
        }

        private static Dictionary<string, int> BuildIndex()
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            IReadOnlyList<MetricInfo> metrics = ContentVocabulary.Metrics;
            for (int i = 0; i < metrics.Count; i++) index[metrics[i].Id] = i;
            return index;
        }
    }
}
