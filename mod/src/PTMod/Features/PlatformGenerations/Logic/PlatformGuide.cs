using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>One block of the description of the feature for the player: a heading and a paragraph (the example, if there is one, is the last sentence of the body).</summary>
    public sealed class GuideSection
    {
        /// <summary>A short technical name of the block (<see cref="PlatformGuide.BaseId"/> ...).</summary>
        public string Id;

        public string Title;
        public string Body;
    }

    /// <summary>One generation as the description lists it.</summary>
    public sealed class GuideGeneration
    {
        /// <summary>The name of the generation (the real names of the sockets), the sockets, the memories, the announce year and, when it has one, the year it becomes obsolete (0 = never).</summary>
        public string Name;

        public string Sockets;
        public string Memories;
        public int AnnounceYear;
        public int ObsoleteYear;

        /// <summary>The line of the list in the language of the strings.</summary>
        public string Line;
    }

    /// <summary>
    /// The description of the feature for the player (TSK-023; FR-9, FR-13): how the installed base works, the early transition, the change of a socket,
    /// the OEM customers and the full platform, with every number taken from the balance in force - so the page in the mods hub always says what the game
    /// does - and the list of the generations with the real names of their sockets. The same blocks, built with the default <c>balance.json</c> and the
    /// shipped <c>generations.json</c>, are the section "Поколения платформ" of the README (a test keeps the two equal). Pure: the strings come from
    /// <see cref="ViewText"/>, the numbers are written by <see cref="PlatformNumbers"/> (a dot, no trailing zeros).
    /// </summary>
    public static class PlatformGuide
    {
        public const string BaseId = "base";
        public const string EarlyId = "early";
        public const string ChurnId = "churn";
        public const string OemId = "oem";
        public const string FullId = "full";

        /// <summary>The window of the early adopters the text names when the generations are not loaded (or have none): the usual 24 months of the shipped content.</summary>
        public const int DefaultEarlyWindowMonths = 24;

        /// <summary>
        /// The five blocks in the order of the description: the base, the early transition, the change of a socket, the OEM customers, the full platform.
        /// <paramref name="catalog"/> may be null or empty (the window of the early adopters is then the usual 24 months and the full platform has no example).
        /// </summary>
        public static List<GuideSection> Build(PlatformBalance balance, GenerationCatalog catalog, ViewText text)
        {
            PlatformBalance b = balance ?? PlatformBalance.Defaults();
            ViewText t = text ?? new ViewText();
            PlatformBalance.RatingBalance r = b.Rating;
            PlatformBalance.OemBalance o = b.Oem;

            var sections = new List<GuideSection>();

            // The installed base (F1-F3). The example share is three quarters of the saturation, so the sum is always on the slope and not on the ceiling.
            double exampleShare = r.IbSaturationShare * 0.75;
            double exampleBonus = r.IbMaxBonus * 0.75;
            sections.Add(new GuideSection
            {
                Id = BaseId,
                Title = t.L("pg.hub.guide.base.title"),
                Body = t.L("pg.hub.guide.base.body",
                    PlatformNumbers.Int(b.InstalledBase.LifetimeMonths),
                    PlatformNumbers.Percent(b.InstalledBase.ContractUnitWeight),
                    PlatformNumbers.Percent(r.IbMaxBonus),
                    PlatformNumbers.Percent(r.IbSaturationShare),
                    PlatformNumbers.Percent(r.SharedWeight),
                    PlatformNumbers.Percent(r.IndustriesWeight))
                       + " " + t.L("pg.hub.guide.base.example",
                           PlatformNumbers.Percent(exampleShare),
                           PlatformNumbers.Percent(r.IbMaxBonus),
                           PlatformNumbers.Percent(r.IbSaturationShare),
                           PlatformNumbers.Percent(exampleBonus)),
            });

            // The early adopters (F4).
            sections.Add(new GuideSection
            {
                Id = EarlyId,
                Title = t.L("pg.hub.guide.early.title"),
                Body = t.L("pg.hub.guide.early.body",
                    PlatformNumbers.Int(r.EarlyAdopterSlots),
                    PlatformNumbers.Percent(r.EarlyBonus),
                    PlatformNumbers.Int(r.EarlyDurationMonths),
                    PlatformNumbers.Int(UsualEarlyWindow(catalog))),
            });

            // The change of a socket (F5): the example is a main socket that has lived a third of its minimum life.
            int exampleAge = Math.Max(1, r.MinSocketLifeMonths / 3);
            double examplePenalty = r.MinSocketLifeMonths > 0 ? r.ChurnPenalty * (1.0 - (double)exampleAge / r.MinSocketLifeMonths) : 0;
            sections.Add(new GuideSection
            {
                Id = ChurnId,
                Title = t.L("pg.hub.guide.churn.title"),
                Body = t.L("pg.hub.guide.churn.body",
                    PlatformNumbers.Percent(r.ChurnPenalty),
                    PlatformNumbers.Int(r.MinSocketLifeMonths),
                    PlatformNumbers.Percent(r.ChurnMinBaseShare),
                    PlatformNumbers.Int(r.ChurnDurationMonths),
                    PlatformNumbers.Int(exampleAge),
                    PlatformNumbers.Percent(examplePenalty)),
            });

            // The OEM customers (F8, F9a, F9c).
            sections.Add(new GuideSection
            {
                Id = OemId,
                Title = t.L("pg.hub.guide.oem.title"),
                Body = t.L("pg.hub.guide.oem.body",
                    PlatformNumbers.Percent(o.AdoptBonus),
                    PlatformNumbers.Percent(o.LagPenalty1),
                    PlatformNumbers.Percent(o.LagPenalty2),
                    PlatformNumbers.Percent(o.OemLoyaltyBonus),
                    PlatformNumbers.Percent(r.IbSaturationShare)),
            });

            // The full platform (D11).
            string full = t.L("pg.hub.guide.full.body", PlatformNumbers.Percent(o.FullPlatformBonus));
            GenerationDef example = FullPlatformExample(catalog);
            if (example != null)
            {
                full += " " + t.L("pg.hub.guide.full.example", t.Gen(example), PlatformNumbers.Join(example.memories));
            }
            sections.Add(new GuideSection { Id = FullId, Title = t.L("pg.hub.guide.full.title"), Body = full });

            return sections;
        }

        /// <summary>The list of the generations, oldest first, with the real names of their sockets (what the table of the README shows).</summary>
        public static List<GuideGeneration> Generations(GenerationCatalog catalog, ViewText text)
        {
            ViewText t = text ?? new ViewText();
            var list = new List<GuideGeneration>();
            if (catalog == null) return list;

            IReadOnlyList<GenerationDef> generations = catalog.Generations;
            for (int i = 0; i < generations.Count; i++)
            {
                GenerationDef gen = generations[i];
                var item = new GuideGeneration
                {
                    Name = t.Gen(gen),
                    Sockets = PlatformNumbers.Join(gen.sockets),
                    Memories = PlatformNumbers.Join(gen.memories),
                    AnnounceYear = gen.announceYear,
                    ObsoleteYear = gen.obsoleteYear,
                };
                item.Line = gen.obsoleteYear > 0
                    ? t.L("pg.hub.guide.gens.lineObsolete", item.Name, item.Sockets, item.Memories, PlatformNumbers.Int(gen.announceYear), PlatformNumbers.Int(gen.obsoleteYear))
                    : t.L("pg.hub.guide.gens.line", item.Name, item.Sockets, item.Memories, PlatformNumbers.Int(gen.announceYear));
                list.Add(item);
            }
            return list;
        }

        /// <summary>The window of the early adopters that most generations have (the smaller one when two are equally common); the usual 24 months for no generations.</summary>
        public static int UsualEarlyWindow(GenerationCatalog catalog)
        {
            if (catalog == null || catalog.Count == 0) return DefaultEarlyWindowMonths;

            var counts = new Dictionary<int, int>();
            IReadOnlyList<GenerationDef> generations = catalog.Generations;
            for (int i = 0; i < generations.Count; i++)
            {
                int months = generations[i].earlyWindowMonths;
                int count;
                counts.TryGetValue(months, out count);
                counts[months] = count + 1;
            }

            int best = DefaultEarlyWindowMonths;
            int bestCount = 0;
            foreach (KeyValuePair<int, int> pair in counts)
            {
                if (pair.Value > bestCount || (pair.Value == bestCount && pair.Key < best))
                {
                    best = pair.Key;
                    bestCount = pair.Value;
                }
            }
            return best;
        }

        /// <summary>The generation with the most memory standards (the earliest when several have as many): the one whose memories show what a full platform is. Null when no generation lists a memory.</summary>
        public static GenerationDef FullPlatformExample(GenerationCatalog catalog)
        {
            if (catalog == null) return null;

            GenerationDef best = null;
            IReadOnlyList<GenerationDef> generations = catalog.Generations;
            for (int i = 0; i < generations.Count; i++)
            {
                GenerationDef gen = generations[i];
                if (gen.memories == null || gen.memories.Count == 0) continue;
                if (best == null || gen.memories.Count > best.memories.Count) best = gen;
            }
            return best;
        }
    }
}
