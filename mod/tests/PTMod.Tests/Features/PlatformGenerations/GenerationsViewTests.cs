using System.Collections.Generic;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-015, FR-9 item 2, AC-9.1: the cards of the tab "Поколения" - every generation, the newest first, with the real name of the platform, the sockets, the
    /// memory, the status, the dates, the pioneer, the early adopters and the adoption by the OEM clients (the bar and the percent are the number the OEM
    /// model uses, AC-8.1: 47.5 %). Without generations the tab says "нет поколений" (the window does; here the list is empty).
    /// </summary>
    [Covers(typeof(GenerationsView), typeof(GenerationCard))]
    public class GenerationsViewTests
    {
        private const string Player = ViewTestData.Player;

        /// <summary>The world of AC-8.1: gen-2000 launched by the player on 14 March 2000 with 15 % of the installed base; the older generations launched before it.</summary>
        private static PlatformSnapshot World(GameDate today)
        {
            GenerationCatalog catalog = ViewTestData.Catalog();
            PlatformBalance balance = ViewTestData.Balance();
            var s = new PlatformSnapshot
            {
                Catalog = catalog,
                Balance = balance,
                Today = today,
                InstalledBase = ViewTestData.Ledger(balance, catalog,
                    new ViewTestData.LedgerEntry(Player, ViewTestData.Std("LGA 2000"), 15000),
                    new ViewTestData.LedgerEntry("ai:1", ViewTestData.Std("PGA 1995"), 85000)),
            };

            PlatformTimeline t = s.Timeline;
            t.Announce("gen-1990-pga", ViewTestData.D(1990, 1, 1));
            t.SetLaunched("gen-1990-pga", ViewTestData.D(1990, 4, 10), "ai:1");
            t.AddEarlyAdopter("gen-1990-pga", "ai:1", ViewTestData.D(1990, 4, 10), 3);
            t.Announce("gen-1995-pga", ViewTestData.D(1995, 1, 1));
            t.SetLaunched("gen-1995-pga", ViewTestData.D(1995, 3, 5), "ai:1");
            t.Announce("gen-2000", ViewTestData.D(2000, 1, 1));
            t.SetLaunched("gen-2000", ViewTestData.D(2000, 3, 14), Player);
            t.AddEarlyAdopter("gen-2000", Player, ViewTestData.D(2000, 3, 14), 3);
            t.AddEarlyAdopter("gen-2000", "ai:1", ViewTestData.D(2000, 5, 2), 3);
            return s;
        }

        private static List<GenerationCard> Cards(PlatformSnapshot s, string language)
        {
            return GenerationsView.Build(s.Catalog, s.Timeline, s.InstalledBase, s.Today, ViewTestData.Text(language));
        }

        private static GenerationCard Card(List<GenerationCard> cards, string id)
        {
            GenerationCard card = cards.Find(c => c.Id == id);
            Assert.NotNull(card, id);
            return card;
        }

        [Test]
        public void Build_ListsEveryGeneration_TheNewestFirst()
        {
            List<GenerationCard> cards = Cards(World(ViewTestData.D(2002, 1, 1)), "ru");

            Assert.Equal(10, cards.Count);
            Assert.Equal("gen-2030", cards[0].Id);
            Assert.Equal("gen-1971-dip", cards[9].Id);
            Assert.Equal("LGA 2030 A/B", cards[0].Title);
            for (int i = 1; i < cards.Count; i++)
            {
                Assert.True(string.CompareOrdinal(cards[i - 1].Id, cards[i].Id) != 0, "no generation twice");
            }
        }

        [Test]
        public void Card_ShowsTheNameSocketsMemoryDatesPioneerAndEarlyAdopters()
        {
            GenerationCard card = Card(Cards(World(ViewTestData.D(2002, 1, 1)), "ru"), "gen-2000");

            Assert.Equal("LGA 775 / Socket 939", card.Title);
            Assert.Equal(new[]
            {
                "Сокеты: LGA 2000, PGA 2000",
                "Память: DDR, DDR2",
                "Анонс: 2000-01-01",
                "Запуск: 2000-03-14",
                "Первопроходец: MyChips (вы)",
                "Ранние последователи: MyChips (вы) (2000-03-14), Intelligent (2000-05-02)",
                "Устаревает с 2013 года",
            }, card.Lines.ToArray());
        }

        [Test]
        public void Card_InEnglish_UsesTheEnglishTables()
        {
            GenerationCard card = Card(Cards(World(ViewTestData.D(2002, 1, 1)), "en"), "gen-2000");

            Assert.Equal("Sockets: LGA 2000, PGA 2000", card.Lines[0]);
            Assert.Equal("Launched: 2000-03-14", card.Lines[3]);
            Assert.Equal("Pioneer: MyChips (you)", card.Lines[4]);
            Assert.Equal("Obsolete from 2013", card.Lines[6]);
            Assert.Equal("OEM adoption: 47.5 %", card.AdoptionText);
        }

        [Test]
        public void Card_AC81_TheAdoptionIsTheNumberOfTheOemModel_475()
        {
            PlatformSnapshot s = World(ViewTestData.D(2002, 1, 1));   // 22 months after the launch, 15 % of the base
            GenerationCard card = Card(Cards(s, "ru"), "gen-2000");

            Assert.Near(0.475, card.Adoption, 1e-6);
            Assert.Equal("Принятие OEM: 47.5 %", card.AdoptionText);

            List<GenAdoptionInput> inputs = OemAdoptionModel.BuildInputs(s.Catalog, s.Timeline, s.InstalledBase, s.Today, null);
            GenAdoptionInput gen2000 = inputs.Find(i => i.Gen.id == "gen-2000");
            Assert.Near(OemAdoptionModel.Adoption(gen2000), card.Adoption, 1e-9, "the same number the preferences and the multipliers use");
        }

        [Test]
        public void Card_OfAGenerationNobodyHasLaunched_HasNoAdoption()
        {
            GenerationCard card = Card(Cards(World(ViewTestData.D(2002, 1, 1)), "ru"), "gen-2010");

            Assert.Equal(0.0, card.Adoption);
            Assert.Equal("Принятие OEM: 0 %", card.AdoptionText);
        }

        // ---- the statuses ----

        [Test]
        public void Status_ExpectedAnnouncedEarlyWindowMatureObsolete_AreToldInWords()
        {
            PlatformSnapshot s = World(ViewTestData.D(2002, 1, 1));
            List<GenerationCard> cards = Cards(s, "ru");

            Assert.Equal(GenerationStatus.Expected, Card(cards, "gen-2010").Status);
            Assert.Equal("Ожидается (2010)", Card(cards, "gen-2010").StatusText);

            Assert.Equal(GenerationStatus.EarlyWindow, Card(cards, "gen-2000").Status);
            Assert.Equal("Окно ранних последователей: ещё 2 мес.", Card(cards, "gen-2000").StatusText);

            Assert.Equal(GenerationStatus.Mature, Card(cards, "gen-1995-pga").Status);
            Assert.Equal("Зрелое", Card(cards, "gen-1995-pga").StatusText);

            Assert.Equal(GenerationStatus.Obsolete, Card(cards, "gen-1990-pga").Status);
            Assert.Equal("Устарело", Card(cards, "gen-1990-pga").StatusText);

            s.Timeline.Announce("gen-2010", ViewTestData.D(2010, 1, 1));
            s.Today = ViewTestData.D(2010, 6, 1);
            GenerationCard announced = Card(Cards(s, "ru"), "gen-2010");
            Assert.Equal(GenerationStatus.Announced, announced.Status);
            Assert.Equal("Анонсировано", announced.StatusText);
        }

        [Test]
        public void Status_AC91_ThreeYearsAfterTheLaunch_TheGenerationIsMature_AndTheNextOneIsExpected()
        {
            List<GenerationCard> cards = Cards(World(ViewTestData.D(2003, 6, 1)), "ru");

            Assert.Equal(GenerationStatus.Mature, Card(cards, "gen-2000").Status);
            Assert.Equal("Зрелое", Card(cards, "gen-2000").StatusText);
            Assert.Equal(GenerationStatus.Expected, Card(cards, "gen-2010").Status);
        }

        [Test]
        public void Card_OfAnAnnouncedGenerationWithoutALaunch_HasNoPioneerAndNoEarlyAdopters()
        {
            PlatformSnapshot s = World(ViewTestData.D(2010, 6, 1));
            s.Timeline.Announce("gen-2010", ViewTestData.D(2010, 1, 1));
            GenerationCard card = Card(Cards(s, "ru"), "gen-2010");

            Assert.Equal("Анонс: 2010-01-01", card.Lines[2]);
            Assert.Equal("Запуск: —", card.Lines[3]);
            Assert.False(card.Lines.Exists(l => l.StartsWith("Первопроходец")), "nobody has launched it");
            Assert.False(card.Lines.Exists(l => l.StartsWith("Ранние последователи")));
        }

        [Test]
        public void Card_OfALaunchedGenerationWithoutEarlyAdopters_SaysSo()
        {
            GenerationCard card = Card(Cards(World(ViewTestData.D(2002, 1, 1)), "ru"), "gen-1995-pga");

            Assert.True(card.Lines.Contains("Ранние последователи: пока нет"), string.Join(" | ", card.Lines.ToArray()));
            Assert.True(card.Lines.Contains("Первопроходец: Intelligent"));
        }

        [Test]
        public void Card_OfAGenerationThatNeverBecomesObsolete_HasNoLineAboutIt()
        {
            GenerationCard card = Card(Cards(World(ViewTestData.D(2002, 1, 1)), "ru"), "gen-2030");

            Assert.False(card.Lines.Exists(l => l.StartsWith("Устаревает")));
        }

        // ---- edges ----

        [Test]
        public void Build_WithoutGenerations_GivesNoCards()
        {
            var s = new PlatformSnapshot { Today = ViewTestData.D(2002, 1, 1) };

            Assert.Equal(0, GenerationsView.Build(s.Catalog, s.Timeline, s.InstalledBase, s.Today, ViewTestData.Text("ru")).Count);
            Assert.Equal(0, GenerationsView.Build(null, null, null, s.Today, null).Count);
        }

        [Test]
        public void Build_WithoutAChronologyOrABase_StillShowsTheGenerations()
        {
            GenerationCatalog catalog = ViewTestData.Catalog();
            List<GenerationCard> cards = GenerationsView.Build(catalog, null, null, ViewTestData.D(1991, 1, 1), null);

            Assert.Equal(10, cards.Count);
            GenerationCard card = Card(cards, "gen-1990-pga");
            Assert.Equal(GenerationStatus.Announced, card.Status, "announced by the calendar when the chronology knows nothing");
            Assert.Equal("pg.status.announced", card.StatusText, "without tables the key is the text");
        }

        [Test]
        public void ACompanyWithoutAName_IsShownByItsKey()
        {
            PlatformSnapshot s = World(ViewTestData.D(2002, 1, 1));
            s.Timeline.SetLaunched("gen-2010", ViewTestData.D(2001, 1, 1), "ai:77");
            GenerationCard card = Card(Cards(s, "ru"), "gen-2010");

            Assert.True(card.Lines.Contains("Первопроходец: ai:77"), string.Join(" | ", card.Lines.ToArray()));
        }
    }
}
