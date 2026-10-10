using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using GameMarket = ProcessorTycoon.MarketSystem.Market;
using GamePopulationData = ProcessorTycoon.MarketSystem.PopulationData;

namespace PTMod.Core
{
    /// <summary>
    /// The market's segments as the patches P3-P5 see them (FR-12). The game keeps four populations in <c>Market.populations</c> in a fixed
    /// order - 0 High End, 1 Mid Range, 2 Low End, 3 Industries, the order of <c>Market.get_HighEndBudget</c> and the numbering of
    /// <see cref="Segment"/> - and passes the INDEX around: <c>CalculateRatings(population, populationIndex, …)</c> and the slot <c>Popularities[k]</c> of
    /// a CPU. The segment is therefore the index, never the name of the population (<c>PopulationData.Name</c> is a label of the asset, not a key).
    /// <list type="bullet">
    /// <item><b>P4 and P5</b> get the index from the game (the parameter and the loop variable): <see cref="FromPopulationIndex"/>.</item>
    /// <item><b>P3</b> gets only the asset (<c>PopulationData.GetCurrentSize</c> is an instance method of the asset): <see cref="OfPopulation"/> looks
    /// the instance up in the list of the live market and answers with the index it has there. Without a market (<c>Market.Instance</c> is null: the
    /// menu, a scene that is loading), for an asset the market does not hold, or when the game's fields are not what the mod expects, the answer
    /// is <see cref="Segment.Any"/> and the caller leaves the game's value alone.</item>
    /// </list>
    /// <c>Market.populations</c> is a private list of a private nested type (<c>Market.Population</c>) with a private field <c>populationData</c>, so
    /// the lookup goes through reflection on the non-generic <see cref="IList"/>: four reads of references a day, no allocation, no cache that could
    /// outlive the scene's market. The fields are resolved once; when they do not exist the error is logged once and <see cref="OfPopulation"/>
    /// answers <see cref="Segment.Any"/> until the game restarts (<c>market.demand.*</c> then has no effect; the rating and popularity channels
    /// do not depend on the lookup). Main thread only.
    /// </summary>
    internal static class SegmentMap
    {
        /// <summary>How many populations (segments) the game has. The slots of <c>Cpu.Popularities</c> are numbered the same way.</summary>
        internal const int PopulationCount = 4;

        private const string CoreId = ModLog.CoreFeatureId;
        private const BindingFlags AnyField = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static FieldInfo _populationsField;      // Market.populations
        private static FieldInfo _populationDataField;   // Market.Population.populationData
        private static bool _resolved;
        private static bool _broken;

        /// <summary>The segment of the game's population number <paramref name="populationIndex"/> (0-3); <see cref="Segment.Any"/> for any other number.</summary>
        internal static Segment FromPopulationIndex(int populationIndex)
        {
            return (uint)populationIndex < (uint)PopulationCount ? (Segment)populationIndex : Segment.Any;
        }

        /// <summary>The game's population number of a segment (0-3), or -1 for <see cref="Segment.Any"/> and values the game has no population for.</summary>
        internal static int PopulationIndexOf(Segment segment)
        {
            return segment >= Segment.HighEnd && segment <= Segment.Industries ? (int)segment : -1;
        }

        /// <summary>
        /// The segment of the population whose asset is <paramref name="data"/>, by its place in the list of the market that exists now:
        /// <see cref="Segment.Any"/> for a null asset, when there is no market yet, when the asset is none of the market's populations (or
        /// stands beyond the fourth place) and when the game's fields cannot be read. Never throws for those reasons.
        /// </summary>
        internal static Segment OfPopulation(GamePopulationData data)
        {
            if (ReferenceEquals(data, null) || _broken) return Segment.Any;
            if (!_resolved && !Resolve()) return Segment.Any;

            return FromPopulationIndex(IndexInMarket(data));
        }

        /// <summary>
        /// The place of <paramref name="item"/> in <paramref name="populations"/>, comparing the object that <paramref name="itemField"/> holds in each
        /// element by reference (the game-free half of the lookup); -1 when there is none, the list is null or an element is null.
        /// </summary>
        internal static int IndexOf(IList populations, FieldInfo itemField, object item)
        {
            if (populations == null || itemField == null || ReferenceEquals(item, null)) return -1;

            for (int i = 0; i < populations.Count; i++)
            {
                object population = populations[i];
                if (population == null) continue;
                if (ReferenceEquals(itemField.GetValue(population), item)) return i;
            }
            return -1;
        }

        // ---- the game's classes: touched only below; if they do not match the game, the JIT fails in the method and the caller catches it ----

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int IndexInMarket(GamePopulationData data)
        {
            // Compared as a reference, not with Unity's ==: only a market that was never created (or is not set yet) counts as "no market". The scene's
            // Instance is replaced by the next Awake, and nothing but a live market asks for the size.
            GameMarket market = GameMarket.Instance;
            if (ReferenceEquals(market, null)) return -1;   // no market yet: the demand stays vanilla

            return IndexOf(_populationsField.GetValue(market) as IList, _populationDataField, data);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Resolve()
        {
            _resolved = true;

            FieldInfo list = typeof(GameMarket).GetField("populations", AnyField);
            Type population = typeof(GameMarket).GetNestedType("Population", BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo data = population == null ? null : population.GetField("populationData", AnyField);

            if (list != null && data != null && typeof(IList).IsAssignableFrom(list.FieldType) && typeof(GamePopulationData).IsAssignableFrom(data.FieldType))
            {
                _populationsField = list;
                _populationDataField = data;
                return true;
            }

            _broken = true;
            ModLog.Error(CoreId, "market: Market.populations or Market.Population.populationData not found, the game's classes do not match the mod "
                                 + "(another game version?); market.demand.* has no effect until the game restarts");
            return false;
        }
    }
}
