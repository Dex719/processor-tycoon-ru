using System.Collections.Generic;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>One generation as the tab "Поколения" shows it (FR-9 item 2): a title, the status, a few lines of facts and how far the OEM clients have come.</summary>
    public sealed class GenerationCard
    {
        public string Id;

        /// <summary>The real name of the platform ("LGA 775 / Socket 939").</summary>
        public string Title;

        public GenerationStatus Status;

        /// <summary>The status in words ("Зрелое", "Окно ранних последователей: ещё 14 мес.").</summary>
        public string StatusText;

        /// <summary>Sockets, memory, the dates, the pioneer, the early adopters, the obsolete year - one line each, in this order; a line without a fact is left out.</summary>
        public readonly List<string> Lines = new List<string>();

        /// <summary><c>A_G</c> (F7) on the day, 0..1: the length of the bar.</summary>
        public double Adoption;

        /// <summary>"Принятие OEM: 47.5 %".</summary>
        public string AdoptionText;
    }

    /// <summary>
    /// The model of the tab "Поколения" (FR-9 item 2, AC-9.1): for every generation of the catalog, newest first, its name, sockets, memory,
    /// status, the dates of the announcement and of the launch, the pioneer, the early adopters and the adoption by OEM clients on the day
    /// (<c>A_G</c>, the same number the OEM model uses). Pure logic over the state of the feature.
    /// </summary>
    public static class GenerationsView
    {
        /// <summary>Builds the cards; an empty list for an empty catalog. <paramref name="timeline"/> and <paramref name="ib"/> may be null (nothing happened yet).</summary>
        public static List<GenerationCard> Build(GenerationCatalog catalog, PlatformTimeline timeline, InstalledBaseLedger ib, GameDate today, ViewText text)
        {
            var cards = new List<GenerationCard>();
            if (catalog == null || catalog.Count == 0) return cards;
            if (text == null) text = new ViewText();

            List<GenAdoptionInput> inputs = OemAdoptionModel.BuildInputs(catalog, timeline, ib, today, null);   // newest first

            for (int i = 0; i < inputs.Count; i++)
            {
                GenAdoptionInput input = inputs[i];
                if (input.Gen != null) cards.Add(Card(input, catalog, timeline, today, text));
            }
            return cards;
        }

        private static GenerationCard Card(GenAdoptionInput input, GenerationCatalog catalog, PlatformTimeline timeline, GameDate today, ViewText text)
        {
            GenerationDef gen = input.Gen;
            GenerationStatus status = catalog.StatusAt(gen, today, timeline);

            GameDate launched = GameDate.None;
            string pioneerKey = null;
            bool isLaunched = timeline != null && timeline.IsLaunched(gen.id, out launched, out pioneerKey) && launched.IsSet;
            GameDate announced = timeline == null ? GameDate.None : timeline.AnnouncedOn(gen.id);

            var card = new GenerationCard
            {
                Id = gen.id,
                Title = text.Gen(gen),
                Status = status,
                StatusText = StatusText(status, gen, catalog, isLaunched ? launched : GameDate.None, today, text),
                Adoption = Clamp01(OemAdoptionModel.Adoption(input)),
            };
            card.AdoptionText = text.L("pg.oem.adoption", PlatformNumbers.Percent(card.Adoption) + " %");

            card.Lines.Add(text.L("pg.gen.sockets", PlatformNumbers.Join(gen.sockets)));
            card.Lines.Add(text.L("pg.gen.memory", PlatformNumbers.Join(gen.memories)));
            card.Lines.Add(text.L("pg.gen.announced", Date(announced, text)));
            card.Lines.Add(text.L("pg.gen.launched", Date(isLaunched ? launched : GameDate.None, text)));
            if (isLaunched) card.Lines.Add(text.L("pg.gen.pioneer", Company(pioneerKey, text)));

            if (isLaunched)
            {
                IReadOnlyList<EarlyAdopterRecord> adopters = timeline.EarlyAdoptersOf(gen.id);
                if (adopters.Count == 0)
                {
                    card.Lines.Add(text.L("pg.gen.earlyNone"));
                }
                else
                {
                    var names = new List<string>(adopters.Count);
                    for (int i = 0; i < adopters.Count; i++) names.Add(Company(adopters[i].CompanyKey, text) + " (" + adopters[i].Date + ")");
                    card.Lines.Add(text.L("pg.gen.early", PlatformNumbers.Join(names)));
                }
            }

            if (gen.obsoleteYear > 0) card.Lines.Add(text.L("pg.gen.obsoleteFrom", PlatformNumbers.Int(gen.obsoleteYear)));
            return card;
        }

        /// <summary>The status in words: the year for "ожидается", the months left for the early adopter window.</summary>
        public static string StatusText(GenerationStatus status, GenerationDef gen, GenerationCatalog catalog, GameDate launched, GameDate today, ViewText text)
        {
            switch (status)
            {
                case GenerationStatus.Expected:
                    return text.L("pg.status.expected", PlatformNumbers.Int(gen.announceYear));
                case GenerationStatus.Announced:
                    return text.L("pg.status.announced");
                case GenerationStatus.EarlyWindow:
                    return text.L("pg.status.earlyWindow", PlatformNumbers.Int(catalog.EarlyWindowMonthsLeft(gen, launched, today)));
                case GenerationStatus.Obsolete:
                    return text.L("pg.status.obsolete");
                default:
                    return text.L("pg.status.mature");
            }
        }

        private static string Date(GameDate date, ViewText text)
        {
            return date.IsSet ? date.ToString() : text.L("pg.gen.none");
        }

        private static string Company(string key, ViewText text)
        {
            if (string.IsNullOrEmpty(key)) return text.L("pg.gen.none");

            string name = text.Company(key);
            return CompanyKey.IsPlayer(key) ? text.L("pg.company.you", name) : name;
        }

        private static double Clamp01(double value)
        {
            if (!(value > 0)) return 0;
            return value > 1 ? 1 : value;
        }
    }
}
