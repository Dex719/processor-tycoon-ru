using System.Collections.Generic;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-016, FR-9 item 3: the table of the tab "Установленная база" - the rows "socket x company" with a base, the largest first, the name of the socket (the
    /// package's or the company's own), standard or custom, the company, the base in units, the share of the whole base, how long the company has had the
    /// socket; the filter "мои / все"; the cap on the number of rows; an empty base.
    /// </summary>
    [Covers(typeof(InstalledBaseView), typeof(IbTable), typeof(IbRowView))]
    public class InstalledBaseViewTests
    {
        private const string Player = ViewTestData.Player;

        private static PlatformSnapshot World()
        {
            GenerationCatalog catalog = ViewTestData.Catalog();
            PlatformBalance balance = ViewTestData.Balance();
            SocketId own = SocketIdentity.Custom(5120, "PGA 1995");
            var s = new PlatformSnapshot
            {
                Catalog = catalog,
                Balance = balance,
                Today = ViewTestData.D(2000, 1, 1),
                InstalledBase = ViewTestData.Ledger(balance, catalog,
                    new ViewTestData.LedgerEntry("ai:1", ViewTestData.Std("PGA 1995"), 50000),
                    new ViewTestData.LedgerEntry(Player, own, 30000),
                    new ViewTestData.LedgerEntry("ai:2", ViewTestData.Std("PGA 1995"), 15000),
                    new ViewTestData.LedgerEntry(Player, ViewTestData.Std("PGA 1990"), 5000)),
            };
            s.Timeline.AddSocketIntro("ai:1", "pkg:PGA 1995", ViewTestData.D(1995, 3, 5), null, 0, 0f);
            s.Timeline.AddSocketIntro(Player, "custom:5120", ViewTestData.D(1999, 1, 1), null, 0, 0f);
            return s;
        }

        private static IbTable Table(PlatformSnapshot s, bool onlyMine, int maxRows = InstalledBaseView.DefaultMaxRows, string language = "ru")
        {
            return InstalledBaseView.Build(s.InstalledBase, s.Timeline, s.Today, Player, onlyMine, maxRows, ViewTestData.Text(language));
        }

        [Test]
        public void Build_ListsTheRowsLargestFirst_WithTheNameKindCompanyBaseShareAndAge()
        {
            IbTable table = Table(World(), false);

            Assert.Equal(4, table.Rows.Count);
            Assert.Equal("Всего в установленной базе: 100 000 шт.", table.TotalText);

            IbRowView first = table.Rows[0];
            Assert.Equal("PGA 1995", first.Socket);
            Assert.Equal("Стандартный", first.Kind);
            Assert.Equal("Intelligent", first.Company);
            Assert.Equal("50 000", first.Units);
            Assert.Equal("50 %", first.Share);
            Assert.Equal("58 мес.", first.Age, "from 5 March 1995 to 1 January 2000");
            Assert.False(first.IsPlayer);

            IbRowView second = table.Rows[1];
            Assert.Equal("MySocket", second.Socket, "the company's own socket is shown by its own name");
            Assert.Equal("Свой", second.Kind);
            Assert.Equal("MyChips (вы)", second.Company);
            Assert.Equal("30 000", second.Units);
            Assert.Equal("30 %", second.Share);
            Assert.Equal("12 мес.", second.Age);
            Assert.True(second.IsPlayer);

            Assert.Equal("15 000", table.Rows[2].Units);
            Assert.Equal("5 000", table.Rows[3].Units);
            for (int i = 1; i < table.Rows.Count; i++)
            {
                Assert.True(ToNumber(table.Rows[i - 1].Units) >= ToNumber(table.Rows[i].Units), "the largest base first");
            }
        }

        private static int ToNumber(string units)
        {
            return int.Parse(units.Replace(" ", string.Empty));
        }

        [Test]
        public void Filter_Mine_KeepsOnlyThePlayersRows_ButTheTotalIsTheWholeBase()
        {
            IbTable table = Table(World(), true);

            Assert.Equal(2, table.Rows.Count);
            Assert.True(table.Rows.TrueForAll(r => r.IsPlayer && r.CompanyKey == Player));
            Assert.Equal("MySocket", table.Rows[0].Socket);
            Assert.Equal("PGA 1990", table.Rows[1].Socket);
            Assert.Equal("Всего в установленной базе: 100 000 шт.", table.TotalText);
        }

        [Test]
        public void ASocketWithoutARecordInTheChronology_HasNoAge()
        {
            IbTable table = Table(World(), true);

            Assert.Equal("—", table.Rows[1].Age, "the player has PGA 1990 in the base but the chronology has no record of when it began");
        }

        [Test]
        public void TheCap_CountsTheRowsItLeavesOut()
        {
            IbTable table = Table(World(), false, 3);

            Assert.Equal(3, table.Rows.Count);
            Assert.Equal(1, table.Hidden);
            Assert.False(table.IsEmpty);

            IbTable none = Table(World(), false, 0);
            Assert.Equal(0, none.Rows.Count);
            Assert.Equal(4, none.Hidden);
            Assert.False(none.IsEmpty, "there are rows, the cap hides them");
        }

        [Test]
        public void AnEmptyBase_IsEmpty_AndSaysZero()
        {
            var s = new PlatformSnapshot { Today = ViewTestData.D(1975, 1, 1) };
            IbTable table = Table(s, false);

            Assert.True(table.IsEmpty);
            Assert.Equal("Всего в установленной базе: 0 шт.", table.TotalText);

            IbTable mine = Table(World(), true);
            var other = new PlatformSnapshot { Catalog = ViewTestData.Catalog(), Today = ViewTestData.D(2000, 1, 1) };
            other.InstalledBase = ViewTestData.Ledger(ViewTestData.Balance(), other.Catalog, new ViewTestData.LedgerEntry("ai:1", ViewTestData.Std("PGA 1995"), 100));
            Assert.True(Table(other, true).IsEmpty, "the player has nothing: the filter leaves nothing");
            Assert.False(mine.IsEmpty);
        }

        [Test]
        public void ANullLedger_GivesAnEmptyTable()
        {
            IbTable table = InstalledBaseView.Build(null, null, ViewTestData.D(2000, 1, 1), Player, false, 10, ViewTestData.Text("ru"));

            Assert.True(table.IsEmpty);
            Assert.Equal("Всего в установленной базе: 0 шт.", table.TotalText);
        }

        [Test]
        public void TheTable_InEnglish_UsesTheEnglishWords()
        {
            IbTable table = Table(World(), false, InstalledBaseView.DefaultMaxRows, "en");

            Assert.Equal("Whole installed base: 100 000 units", table.TotalText);
            Assert.Equal("Standard", table.Rows[0].Kind);
            Assert.Equal("Custom", table.Rows[1].Kind);
            Assert.Equal("MyChips (you)", table.Rows[1].Company);
            Assert.Equal("12 mo", table.Rows[1].Age);
        }

        [Test]
        public void ASocketTheLedgerDoesNotKnow_IsStillShownByItsKey()
        {
            // A key with no CPU behind it cannot happen in a ledger; the view still has to name what it is given.
            var ib = new InstalledBaseLedger();
            PlatformBalance balance = ViewTestData.Balance();
            ib.RebuildFull(new List<CpuLedgerView>
            {
                new CpuLedgerView { CpuId = 1, CompanyKey = "ai:9", SocketKey = "custom:777", GenId = null, Sold = new[] { 1000, 0 } },
            }, 0, balance);

            IbTable table = InstalledBaseView.Build(ib, new PlatformTimeline(), ViewTestData.D(2000, 1, 1), Player, false, 10, ViewTestData.Text("ru"));

            Assert.Equal(1, table.Rows.Count);
            Assert.Equal("custom:777", table.Rows[0].Socket);
            Assert.Equal("ai:9", table.Rows[0].Company);
        }
    }
}
