using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>Time and money of researching a family in the shared lab (FR-2): <c>baseDays</c> 0 means "available at once, for free".</summary>
    public sealed class ResearchDef
    {
        public float BaseDays;
        public float MonthlyCost;
    }

    /// <summary>The multipliers a family gives to a CPU designed on it (FR-4); 1 is neutral.</summary>
    public sealed class DesignDef
    {
        public float Ipc = 1f;
        public float Power = 1f;
        public float DevCost = 1f;
    }

    /// <summary>
    /// The ageing of a family in some segments (D6): the affinity falls linearly from 1 in <see cref="FromYear"/> to <see cref="Min"/> in
    /// <see cref="ToYear"/> and stays there. <see cref="Segments"/> are the numbers of <c>Segment</c> (0 HighEnd … 3 Industries).
    /// </summary>
    public sealed class FadeDef
    {
        public int FromYear;
        public int ToYear;
        public float Min = 1f;
        public int[] Segments = new int[0];
    }

    /// <summary>Software support of a family (FR-10, stage 2): upkeep in money per month, decay rate, the cap of the target level and the era cost points.</summary>
    public sealed class SupportDef
    {
        public float UpkeepPerMonth;
        public float DecayRate = 0.05f;
        public float TargetCap = 100f;

        /// <summary>Points <c>[year, factor]</c> of the era factor of the upkeep, years ascending; null for none.</summary>
        public float[][] EraCost;
    }

    /// <summary>
    /// One family of CPU architectures of <c>architectures.json</c> (FR-1): the record <c>ModContent</c> hands out. Fields are public and mirror the
    /// JSON of the file (design.md, "Контент"); <see cref="Map"/> reads them strictly, <see cref="ArchCatalogValidator"/> checks the values.
    /// </summary>
    public sealed class ArchFamily : IContentRecord
    {
        /// <summary>The key of the family (<c>x86</c>); <c>[a-z0-9_]{1,32}</c>, the same in saves, content overrides and AI preferences.</summary>
        public string Id { get; set; }

        /// <summary>The ISA group (<c>x86</c>, <c>arm</c>, <c>risc</c>, <c>riscv</c>, <c>own</c>): families of one group share experience and licences.</summary>
        public string Isa;

        /// <summary><c>ModL10n</c> keys of the name and the description.</summary>
        public string NameKey;
        public string DescKey;

        /// <summary>The year the family appears; earlier it can be researched at a time penalty.</summary>
        public int Year;

        /// <summary>Ids of the families that must be researched first.</summary>
        public string[] Requires = new string[0];

        public ResearchDef Research = new ResearchDef();
        public DesignDef Design = new DesignDef();

        /// <summary>The affinity with the four segments, in the order of <c>Segment</c>: HighEnd, MidRange, LowEnd, Industries.</summary>
        public float[] Affinity = { 1f, 1f, 1f, 1f };

        /// <summary>Null for a family that does not age.</summary>
        public FadeDef Fade;

        public SupportDef Support = new SupportDef();

        /// <summary>
        /// Reads one record of <c>architectures.json</c>. Returns null (explaining in <paramref name="errors"/>) when a member is of the wrong kind
        /// or a required member is missing; the values themselves are checked by <see cref="ArchCatalogValidator.ValidateRecord"/>.
        /// </summary>
        public static ArchFamily Map(JsonObject json, List<string> errors)
        {
            var reader = new ArchJsonReader(json, errors);
            var family = new ArchFamily();

            family.Id = reader.String("id", true);
            family.Isa = reader.String("isa", true);
            family.NameKey = reader.String("nameKey", true);
            family.DescKey = reader.String("descKey", true);
            family.Year = reader.Int("year", true);
            family.Requires = reader.StringArray("requires", false) ?? new string[0];

            ArchJsonReader research = reader.Object("research", true);
            if (research != null)
            {
                family.Research.BaseDays = research.Float("baseDays", true);
                family.Research.MonthlyCost = research.Float("monthlyCost", true);
                if (research.Failed) reader.Fail("research", "is unusable");
            }

            ArchJsonReader design = reader.Object("design", false);
            if (design != null)
            {
                family.Design.Ipc = design.Float("ipc", false, 1f);
                family.Design.Power = design.Float("power", false, 1f);
                family.Design.DevCost = design.Float("devCost", false, 1f);
                if (design.Failed) reader.Fail("design", "is unusable");
            }

            float[] affinity = reader.FloatArray("affinity", true);
            if (affinity != null) family.Affinity = affinity;

            ArchJsonReader fade = reader.Object("fade", false);
            if (fade != null)
            {
                family.Fade = new FadeDef
                {
                    FromYear = fade.Int("fromYear", true),
                    ToYear = fade.Int("toYear", true),
                    Min = fade.Float("min", true),
                    Segments = fade.IntArray("segments", true) ?? new int[0],
                };
                if (fade.Failed) reader.Fail("fade", "is unusable");
            }

            ArchJsonReader support = reader.Object("support", false);
            if (support != null)
            {
                family.Support.UpkeepPerMonth = support.Float("upkeepPerMonth", false, 0f);
                family.Support.DecayRate = support.Float("decayRate", false, 0.05f);
                family.Support.TargetCap = support.Float("targetCap", false, 100f);
                family.Support.EraCost = ReadEraCost(support);
                if (support.Failed) reader.Fail("support", "is unusable");
            }

            return reader.Failed ? null : family;
        }

        private static float[][] ReadEraCost(ArchJsonReader support)
        {
            JsonArray array = support.ArrayOf("eraCost", false);
            if (array == null) return null;

            var points = new float[array.Count][];
            for (int i = 0; i < array.Count; i++)
            {
                var pair = array[i] as JsonArray;
                float year, factor;
                if (pair == null || pair.Count != 2 || !ArchJsonReader.TryFloat(pair[0], out year) || !ArchJsonReader.TryFloat(pair[1], out factor))
                {
                    support.Fail("eraCost[" + i + "]", "expected a pair [year, factor] of numbers");
                    return null;
                }
                points[i] = new[] { year, factor };
            }
            return points;
        }

        public override string ToString()
        {
            return "ArchFamily(" + Id + ")";
        }
    }
}
