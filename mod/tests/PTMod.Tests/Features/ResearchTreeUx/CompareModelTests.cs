using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// FR-6 on the compare table (TSK-012), with the shipped Russian and English tables and a formatter that writes plain texts: the difference
    /// (B - A) / |A| and the dash for a zero or a text, the threshold of 2.5 %, the three directions, equal processors (AC-6.2), the 26 built-in
    /// rows and their order, the rows other features add (by <c>Order</c>, duplicates dropped, the limit), a side that is not chosen, and the
    /// words of the table in both languages.
    /// </summary>
    [Covers(typeof(CompareModel), typeof(CompareLine), typeof(CompareDirection), typeof(CpuFacts), typeof(IFormatter))]
    public class CompareModelTests
    {
        private static readonly IFormatter Ru = UxTestKit.Formatter("ru");

        private static CpuFacts Cpu(string name = "A-CPU", float ips = 100f, float power = 50f, float price = 200f, float unitCost = 80f)
        {
            return new CpuFacts
            {
                SaveId = 1, Company = "Player", Name = name, Manufacturer = "Player", Package = "PGA", Lithography = "130nm", Memory = "DDR",
                CoreCount = 4, L1Steps = 8, L2Steps = 32, L3Steps = 64, Year = 2005, Smt = true, DieSize = 100f, Transistors = 5e7f,
                Temperature = 60f, Power = power, Frequency = 3000f, Ipc = 2f, Ips = ips, FailureRate = 0.1f, RealUnitCost = unitCost, Price = price,
                AvgPopularity = 0.4f, SoldLastMonth = 1000,
            };
        }

        private static CompareLine Row(List<CompareLine> rows, string id)
        {
            CompareLine row = rows.FirstOrDefault(r => r.Id == id);
            Assert.NotNull(row, "no row '" + id + "'; rows: " + string.Join(", ", rows.Select(r => r.Id)));
            return row;
        }

        private static List<CompareLine> Build(CpuFacts a, CpuFacts b, CompareConfig cfg = null, IReadOnlyList<CompareLine> extra = null, IFormatter fmt = null)
        {
            return CompareModel.Build(a, b, cfg, fmt ?? Ru, extra);
        }

        // ---- the difference ----

        [Test]
        public void TheDifference_IsBMinusAOverTheSizeOfA()
        {
            Assert.Near(18.0, CompareModel.DeltaPercent(100f, 118f).Value, 1e-4);
            Assert.Near(-10.0, CompareModel.DeltaPercent(100f, 90f).Value, 1e-4);
            Assert.Near(100.0, CompareModel.DeltaPercent(50f, 100f).Value, 1e-4);
            Assert.Near(-50.0, CompareModel.DeltaPercent(200f, 100f).Value, 1e-4);
        }

        [Test]
        public void TheDifference_OfANegativeA_UsesTheSizeOfA()
        {
            // (b - a) / |a|: from -100 to -50 is an improvement of 50 %, from -100 to -150 a fall of 50 %.
            Assert.Near(50.0, CompareModel.DeltaPercent(-100f, -50f).Value, 1e-4);
            Assert.Near(-50.0, CompareModel.DeltaPercent(-100f, -150f).Value, 1e-4);
        }

        [Test]
        public void TheDifference_IsNothing_WhenAIsZero_OrAnyNumberIsMissing()
        {
            Assert.Null(CompareModel.DeltaPercent(0f, 5f));
            Assert.Null(CompareModel.DeltaPercent(null, 5f));
            Assert.Null(CompareModel.DeltaPercent(5f, null));
            Assert.Null(CompareModel.DeltaPercent(null, null));
            Assert.Null(CompareModel.DeltaPercent(float.NaN, 5f));
            Assert.Null(CompareModel.DeltaPercent(5f, float.PositiveInfinity));
        }

        [Test]
        public void TheDifference_OfTwoEqualNumbers_IsZero_EvenWhenBothAreZero()
        {
            Assert.Equal(0f, CompareModel.DeltaPercent(7f, 7f).Value);
            Assert.Equal(0f, CompareModel.DeltaPercent(0f, 0f).Value);
        }

        [Test]
        public void TheDifferenceText_RoundsToTenthsBelowTenAndToWholesAbove()
        {
            Assert.Equal("+18 %", CompareModel.DeltaText(18.2f, ","));
            Assert.Equal("−5,4 %", CompareModel.DeltaText(-5.4f, ","));
            Assert.Equal("+2,5 %", CompareModel.DeltaText(2.5f, ","));
            Assert.Equal("+2.5 %", CompareModel.DeltaText(2.5f, "."));
            Assert.Equal("−9,9 %", CompareModel.DeltaText(-9.94f, ","));
            Assert.Equal("+10 %", CompareModel.DeltaText(9.96f, ","));
            Assert.Equal("+13 300 %", CompareModel.DeltaText(13300.4f, ","));
            Assert.Equal("+3 %", CompareModel.DeltaText(3.0f, ","), "a whole tenth shows no decimal");
        }

        [Test]
        public void TheDifferenceText_OfNothingIsTheDash_AndOfAHairIsZero()
        {
            Assert.Equal("—", CompareModel.DeltaText(null, ","));
            Assert.Equal("0 %", CompareModel.DeltaText(0f, ","));
            Assert.Equal("0 %", CompareModel.DeltaText(0.04f, ","));
            Assert.Equal("0 %", CompareModel.DeltaText(-0.04f, ","));
        }

        // ---- the verdict ----

        [Test]
        public void TheBetterSide_FollowsTheDirectionOfTheRow()
        {
            Assert.Equal(1, CompareModel.WinnerOf(18f, CompareDirection.Higher, 2.5f), "more is better: B is ahead");
            Assert.Equal(-1, CompareModel.WinnerOf(-18f, CompareDirection.Higher, 2.5f), "more is better: A is ahead");
            Assert.Equal(1, CompareModel.WinnerOf(-10f, CompareDirection.Lower, 2.5f), "less is better: B is ahead");
            Assert.Equal(-1, CompareModel.WinnerOf(10f, CompareDirection.Lower, 2.5f), "less is better: A is ahead");
        }

        [Test]
        public void ARowWithoutADirection_IsNeverHighlighted()
        {
            Assert.Equal(0, CompareModel.WinnerOf(80f, CompareDirection.None, 2.5f));
            Assert.Equal(0, CompareModel.WinnerOf(-80f, CompareDirection.None, 2.5f));
        }

        [Test]
        public void TheThreshold_IsInclusive_AndSmallerDifferencesHighlightNothing()
        {
            Assert.Equal(1, CompareModel.WinnerOf(2.5f, CompareDirection.Higher, 2.5f), "exactly the threshold counts");
            Assert.Equal(0, CompareModel.WinnerOf(2.4f, CompareDirection.Higher, 2.5f));
            Assert.Equal(-1, CompareModel.WinnerOf(-2.5f, CompareDirection.Higher, 2.5f));
            Assert.Equal(0, CompareModel.WinnerOf(null, CompareDirection.Higher, 2.5f), "no difference, no verdict");
            Assert.Equal(0, CompareModel.WinnerOf(0f, CompareDirection.Higher, 0f), "equal is never a win, even with a threshold of nothing");
        }

        [Test]
        public void TheThreshold_OfTheConfig_IsUsed()
        {
            var cfg = CompareConfig.Defaults();
            cfg.DeltaHighlightPercent = 10f;

            var rows = Build(Cpu(ips: 100f), Cpu(ips: 105f), cfg);

            Assert.Equal("+5 %", Row(rows, "ips").DeltaText);
            Assert.Equal(0, Row(rows, "ips").Winner, "5 % is under a threshold of 10 %");
        }

        // ---- the table ----

        [Test]
        public void TheTable_HasTheRowsOfTheSpec_InTheOrderOfTheInspector()
        {
            var rows = Build(Cpu(), Cpu());

            Assert.Equal(26, rows.Count);
            Assert.True(rows.Count >= 25, "AC-6.1: 25 rows and more");
            Assert.Equal(new[]
            {
                "company", "manufacturer", "package", "lithography", "ips", "ipc", "frequency", "cores", "smt", "power", "temperature", "yield", "unitCost",
                "price", "margin", "ipsPerWatt", "ipsPerDollar", "popularity", "soldLastMonth", "dieSize", "transistors", "memory", "l1", "l2", "l3", "year",
            }, rows.Select(r => r.Id).ToArray());
            foreach (CompareLine row in rows) Assert.Equal(CompareModel.LabelKeyPrefix + row.Id, row.LabelKey);
        }

        [Test]
        public void TheRowsOfTheSpec_AreAllThere()
        {
            // FR-6 item 3: company, package, lithography, cores, SMT, die size, transistors, temperature, power, frequency, IPC, IPS, memory, L1/L2/L3,
            // yield, unit cost, price, margin, year, average popularity, sold last month, IPS per watt, IPS per dollar.
            var ids = new HashSet<string>(CompareModel.BuiltInIds);
            foreach (string id in new[]
            {
                "company", "package", "lithography", "cores", "smt", "dieSize", "transistors", "temperature", "power", "frequency", "ipc", "ips", "memory",
                "l1", "l2", "l3", "yield", "unitCost", "price", "margin", "year", "popularity", "soldLastMonth", "ipsPerWatt", "ipsPerDollar",
            })
            {
                Assert.True(ids.Contains(id), id);
            }
        }

        [Test]
        public void TheIpsOfIntel_AheadByEighteenPercent_IsHighlightedOnTheSideOfB()
        {
            // AC-6.1: IPS of B is 18 % above A.
            var rows = Build(Cpu(ips: 100f), Cpu("Intel", ips: 118f));

            CompareLine ips = Row(rows, "ips");
            Assert.Equal("+18 %", ips.DeltaText);
            Assert.Equal(1, ips.Winner);
            Assert.Equal("ips:100", ips.ValueA);
            Assert.Equal("ips:118", ips.ValueB);
        }

        [Test]
        public void TwoEqualProcessors_DifferByZeroEverywhere_AndNothingIsHighlighted()
        {
            // AC-6.2
            var rows = Build(Cpu(), Cpu());

            foreach (CompareLine row in rows)
            {
                Assert.Equal(0, row.Winner, row.Id);
                if (row.NumA.HasValue && row.NumB.HasValue) Assert.Equal("0 %", row.DeltaText, row.Id);
            }
        }

        [Test]
        public void LowerIsBetter_ForThePowerTheTemperatureAndTheCost()
        {
            var rows = Build(Cpu(power: 100f, unitCost: 100f), Cpu(power: 80f, unitCost: 130f));

            Assert.Equal("−20 %", Row(rows, "power").DeltaText);
            Assert.Equal(1, Row(rows, "power").Winner, "B draws less: B is ahead");
            Assert.Equal("+30 %", Row(rows, "unitCost").DeltaText);
            Assert.Equal(-1, Row(rows, "unitCost").Winner, "B costs more: A is ahead");
        }

        [Test]
        public void ThePrice_HasNoDirection_ByDefault()
        {
            var rows = Build(Cpu(price: 100f), Cpu(price: 300f));

            Assert.Equal("+200 %", Row(rows, "price").DeltaText);
            Assert.Equal(0, Row(rows, "price").Winner);
        }

        [Test]
        public void TheDirection_OfTheConfig_IsUsed()
        {
            var cfg = ConfigWith("{ \"compare\": { \"rows\": [ { \"id\": \"price\", \"better\": \"lower\" } ] } }");

            var rows = Build(Cpu(price: 100f), Cpu(price: 300f), cfg);

            Assert.Equal(-1, Row(rows, "price").Winner, "B is dearer, and cheaper is better now");
        }

        [Test]
        public void TheOrder_OfTheConfig_IsUsed()
        {
            var cfg = ConfigWith("{ \"compare\": { \"rows\": [ { \"id\": \"year\", \"order\": 0 } ] } }");

            var rows = Build(Cpu(), Cpu(), cfg);

            Assert.Equal("year", rows[0].Id);
        }

        [Test]
        public void ARowTheConfigDoesNotMention_KeepsItsDefaults()
        {
            var cfg = ConfigWith("{ \"compare\": { \"rows\": [ { \"id\": \"year\", \"order\": 0 } ] } }");

            var rows = Build(Cpu(ips: 100f), Cpu(ips: 150f), cfg);

            Assert.Equal(1, Row(rows, "ips").Winner);
            Assert.Equal(10, Row(rows, "ips").Order);
        }

        // ---- the dash ----

        [Test]
        public void TextRows_HaveNoDifference_AndTheValuesAreTheTexts()
        {
            var rows = Build(Cpu(), Cpu("B-CPU"));

            CompareLine company = Row(rows, "company");
            Assert.Equal("Player", company.ValueA);
            Assert.Equal("—", company.DeltaText);
            Assert.Equal(0, company.Winner);
            Assert.False(company.NumA.HasValue);
            Assert.Equal("130nm", Row(rows, "lithography").ValueB);
            Assert.Equal("DDR", Row(rows, "memory").ValueA);
        }

        [Test]
        public void AZeroOnTheSideOfA_GivesTheDash_NotADivisionByZero()
        {
            var a = Cpu();
            a.SoldLastMonth = 0;
            var b = Cpu();
            b.SoldLastMonth = 500;

            CompareLine sold = Row(Build(a, b), "soldLastMonth");

            Assert.Equal("—", sold.DeltaText);
            Assert.Equal(0, sold.Winner);
            Assert.Equal("0", sold.ValueA);
            Assert.Equal("500", sold.ValueB);
        }

        [Test]
        public void ASideThatIsNotChosen_IsDashesOnly()
        {
            var rows = Build(Cpu(), null);

            foreach (CompareLine row in rows)
            {
                Assert.Equal("—", row.ValueB, row.Id);
                Assert.Equal("—", row.DeltaText, row.Id);
                Assert.Equal(0, row.Winner, row.Id);
                Assert.False(row.NumB.HasValue, row.Id);
            }
            Assert.Equal("ips:100", Row(rows, "ips").ValueA, "the chosen side is shown");
            Assert.Equal(26, rows.Count, "the table keeps its rows");
        }

        [Test]
        public void BothSidesEmpty_StillGiveTheTable()
        {
            var rows = Build(null, null);

            Assert.Equal(26, rows.Count);
            Assert.True(rows.All(r => r.ValueA == "—" && r.ValueB == "—"));
        }

        // ---- the computed rows ----

        [Test]
        public void TheYield_IsOneMinusTheFailureRate_Clamped()
        {
            var a = Cpu();
            a.FailureRate = 0.1f;
            var b = Cpu();
            b.FailureRate = 1.7f;

            var rows = Build(a, b);

            Assert.Equal("90%", Row(rows, "yield").ValueA);
            Assert.Equal("0%", Row(rows, "yield").ValueB, "a failure rate over 1 is a yield of 0");
            Assert.Equal(-1, Row(rows, "yield").Winner);
        }

        [Test]
        public void TheMargin_IsThePriceMinusTheUnitCost()
        {
            var rows = Build(Cpu(price: 200f, unitCost: 80f), Cpu(price: 200f, unitCost: 140f));

            Assert.Equal("$120", Row(rows, "margin").ValueA);
            Assert.Equal("$60", Row(rows, "margin").ValueB);
            Assert.Equal("−50 %", Row(rows, "margin").DeltaText);
            Assert.Equal(-1, Row(rows, "margin").Winner, "the bigger margin is better");
        }

        [Test]
        public void IpsPerWatt_ClampsThePowerLikeTheMarketDoes()
        {
            var a = Cpu(ips: 100f, power: 50f);
            var b = Cpu(ips: 100f, power: 0.1f);   // the market never divides by less than half a watt

            var rows = Build(a, b);

            Assert.Equal("ips:2", Row(rows, "ipsPerWatt").ValueA);
            Assert.Equal("ips:200", Row(rows, "ipsPerWatt").ValueB);
        }

        [Test]
        public void IpsPerDollar_IsTheDash_WhenThePriceIsNotSet()
        {
            var rows = Build(Cpu(ips: 100f, price: 50f), Cpu(ips: 100f, price: 0f));

            CompareLine row = Row(rows, "ipsPerDollar");
            Assert.Equal("ips:2", row.ValueA);
            Assert.Equal("—", row.ValueB);
            Assert.Equal("—", row.DeltaText);
        }

        [Test]
        public void TheSmtRow_TellsOnOffAndNotSupported()
        {
            var on = Cpu();
            var off = Cpu();
            off.Smt = false;
            var single = Cpu();
            single.MultiCoreSupported = false;

            Assert.Equal("Включён", Row(Build(on, off), "smt").ValueA);
            Assert.Equal("Выключен", Row(Build(on, off), "smt").ValueB);
            Assert.Equal("Не поддерживается", Row(Build(single, on), "smt").ValueA);
        }

        [Test]
        public void TheCoresAndTheCacheRows_UseTheGamesFormatters()
        {
            CompareLine cores = Row(Build(Cpu(), Cpu()), "cores");
            Assert.Equal("4 cores", cores.ValueA);
            Assert.Equal(4f, cores.NumA.Value);

            var rows = Build(Cpu(), Cpu());
            Assert.Equal("L1:8", Row(rows, "l1").ValueA);
            Assert.Equal("L2:32", Row(rows, "l2").ValueA);
            Assert.Equal("L3:64", Row(rows, "l3").ValueA);
        }

        [Test]
        public void ANumberThatIsNotANumber_BecomesTheDash()
        {
            var a = Cpu();
            a.Ips = float.NaN;

            CompareLine ips = Row(Build(a, Cpu()), "ips");

            Assert.Equal("—", ips.ValueA);
            Assert.Equal("—", ips.DeltaText);
            Assert.Equal(0, ips.Winner);
        }

        // ---- the rows of other features (FR-6 item 8) ----

        [Test]
        public void TheRowsOfOtherFeatures_AreSortedByOrder_AmongTheBuiltInOnes()
        {
            var extra = new List<CompareLine>
            {
                new CompareLine { Id = "arch", LabelKey = "arch.compare.arch", ValueA = "x86", ValueB = "ARM", Order = 205 },
                new CompareLine { Id = "early", LabelKey = "e.l", ValueA = "1", ValueB = "2", Order = 5 },
            };

            var rows = Build(Cpu(), Cpu(), null, extra);

            Assert.Equal(28, rows.Count);
            Assert.Equal("arch", rows[rows.Count - 1].Id, "205 comes after the year (200)");
            Assert.Equal("early", rows[4].Id, "5 comes after the four texts of the head and before IPS (10)");
        }

        [Test]
        public void ARowFromAProvider_GetsItsDifferenceAndItsVerdictFromItsNumbers()
        {
            var extra = new List<CompareLine>
            {
                new CompareLine
                {
                    Id = "rating", LabelKey = "arch.compare.rating", ValueA = "×1.0", ValueB = "×1.2", NumA = 1.0f, NumB = 1.2f,
                    Better = CompareDirection.Higher, Order = 210, DeltaText = null,
                },
            };

            CompareLine row = Row(Build(Cpu(), Cpu(), null, extra), "rating");

            Assert.Equal("+20 %", row.DeltaText);
            Assert.Equal(1, row.Winner);
        }

        [Test]
        public void ARowFromAProvider_ThatBroughtItsOwnDifference_KeepsIt()
        {
            var extra = new List<CompareLine>
            {
                new CompareLine { Id = "own", LabelKey = "k", ValueA = "a", ValueB = "b", Order = 206, DeltaText = "лучше", Winner = -1 },
            };

            CompareLine row = Row(Build(Cpu(), Cpu(), null, extra), "own");

            Assert.Equal("лучше", row.DeltaText);
            Assert.Equal(-1, row.Winner);
        }

        [Test]
        public void ARowFromAProvider_WithATakenIdOrNoId_IsDropped()
        {
            var extra = new List<CompareLine>
            {
                new CompareLine { Id = "ips", LabelKey = "k", ValueA = "x", ValueB = "y", Order = 1 },
                new CompareLine { Id = "", LabelKey = "k", ValueA = "x", ValueB = "y" },
                null,
                new CompareLine { Id = "a", LabelKey = "k", ValueA = "x", ValueB = "y", Order = 300 },
                new CompareLine { Id = "a", LabelKey = "k2", ValueA = "z", ValueB = "z", Order = 301 },
            };

            var rows = Build(Cpu(ips: 100f), Cpu(ips: 100f), null, extra);

            Assert.Equal(27, rows.Count, "26 + the first 'a'");
            Assert.Equal("ips:100", Row(rows, "ips").ValueA, "the built-in IPS row is the first of its id");
            Assert.Equal("k", Row(rows, "a").LabelKey);
        }

        [Test]
        public void RowsWithTheSameOrder_KeepTheOrderInWhichTheyWereAdded()
        {
            var extra = new List<CompareLine>();
            for (int i = 0; i < 5; i++) extra.Add(new CompareLine { Id = "x" + i, LabelKey = "k", ValueA = "1", ValueB = "1", Order = 250 });

            var rows = Build(Cpu(), Cpu(), null, extra);

            Assert.Equal(new[] { "x0", "x1", "x2", "x3", "x4" }, rows.Skip(26).Select(r => r.Id).ToArray());
        }

        [Test]
        public void TheTable_IsCappedAtSixtyFourRows()
        {
            var extra = new List<CompareLine>();
            for (int i = 0; i < 100; i++) extra.Add(new CompareLine { Id = "x" + i, LabelKey = "k", ValueA = "1", ValueB = "1", Order = 250 });

            var rows = Build(Cpu(), Cpu(), null, extra);

            Assert.Equal(CompareModel.MaxRows, rows.Count);
            Assert.Equal(64, rows.Count);
        }

        [Test]
        public void ARowFromAProvider_WithEmptyValues_ShowsDashes_AndNumbersThatAreNoNumbersAreDropped()
        {
            var extra = new List<CompareLine>
            {
                new CompareLine { Id = "e", LabelKey = "k", ValueA = null, ValueB = "", NumA = float.NaN, NumB = 3f, Better = CompareDirection.Higher, Order = 260 },
            };

            CompareLine row = Row(Build(Cpu(), Cpu(), null, extra), "e");

            Assert.Equal("—", row.ValueA);
            Assert.Equal("—", row.ValueB);
            Assert.False(row.NumA.HasValue);
            Assert.Equal("—", row.DeltaText);
            Assert.Equal(0, row.Winner);
        }

        // ---- the words ----

        [Test]
        public void EveryBuiltInRow_HasALabel_InBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                var words = UxTestKit.Words(language);
                foreach (string id in CompareModel.BuiltInIds)
                {
                    string text = words(CompareModel.LabelKeyPrefix + id, new object[0]);
                    Assert.False(text.StartsWith("["), language + ": " + id + " has no label");
                }
            }
        }

        [Test]
        public void TheDecimalSeparator_FollowsTheLanguage()
        {
            var a = Cpu(ips: 1000f);
            var b = Cpu(ips: 1025f);

            Assert.Equal("+2,5 %", Row(Build(a, b, null, null, UxTestKit.Formatter("ru")), "ips").DeltaText);
            Assert.Equal("+2.5 %", Row(Build(a, b, null, null, UxTestKit.Formatter("en")), "ips").DeltaText);
        }

        [Test]
        public void TheBuilder_RefusesToWorkWithoutAFormatter()
        {
            Assert.Throws<ArgumentNullException>(() => CompareModel.Build(Cpu(), Cpu(), null, null, null));
        }

        private static CompareConfig ConfigWith(string json)
        {
            PTMod.Core.Logic.Json.JsonNode node;
            PTMod.Core.Logic.Json.JsonError error;
            Assert.True(PTMod.Core.Logic.Json.ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            var problems = new List<string>();
            CompareConfig cfg = CompareConfig.FromConfig((PTMod.Core.Logic.Json.JsonObject)node, problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return cfg;
        }
    }
}
