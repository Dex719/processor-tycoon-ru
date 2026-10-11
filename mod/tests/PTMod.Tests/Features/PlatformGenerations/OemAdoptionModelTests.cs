using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-008, FR-8: the OEM side - the adoption of a generation (AC-8.1), the preference of a contract, the dice, the platform multiplier with and
    /// without the full platform (AC-8.2, AC-8.5), the market average (AC-8.4), the loyalty and the clamps, the contract preferences of the save and
    /// the contract key.
    /// </summary>
    [Covers(typeof(OemAdoptionModel), typeof(GenAdoptionInput), typeof(OemPreferences), typeof(ContractKeyBuilder))]
    public class OemAdoptionModelTests
    {
        private const double Eps = 1e-6;

        private static GenerationCatalog Catalog()
        {
            var problems = new List<string>();
            GenerationCatalog catalog = PlatformTestData.BuiltInCatalog(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return catalog;
        }

        private static PlatformBalance Balance()
        {
            var problems = new List<string>();
            PlatformBalance balance = PlatformTestData.BuiltInBalance(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            return balance;
        }

        private static GameDate D(int year, int month, int day = 1)
        {
            return LedgerTestData.D(year, month, day);
        }

        /// <summary>A generation whose adoption is exactly 1 once launched six months (no delay, six months of ramp, nothing else).</summary>
        private static GenerationDef Sure(string id, int order)
        {
            GenerationDef g = PlatformTestData.Gen(id, order, "PGA 1990");
            g.oem = new OemCurveDef { delayMonths = 0, rampMonths = 6, maxAdoption = 1f, ibAccel = 0f };
            return g;
        }

        private static GenAdoptionInput In(GenerationDef gen, bool launched, int months, double ibShare = 0, bool obsolete = false)
        {
            return new GenAdoptionInput { Gen = gen, Launched = launched, MonthsSinceLaunch = months, IbShareG = ibShare, ObsoleteAtT = obsolete };
        }

        // ---- the reasons the provider reports ----

        [Test]
        public void TheReasonKeys_HaveATextInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                JsonNode node;
                JsonError error;
                Assert.True(ModJson.TryParse(PlatformTestData.ReadStrings(language), out node, out error), language + ": " + error);
                var table = (JsonObject)node;
                foreach (string key in new[]
                {
                    OemAdoptionModel.ReasonPrefers, OemAdoptionModel.ReasonLag, OemAdoptionModel.ReasonAdoption, OemAdoptionModel.ReasonLoyalty, "pg.reason.rating",
                })
                {
                    Assert.True(table.ContainsKey(key), language + " lacks " + key);
                }
            }
        }

        // ---- F7: the adoption ----

        [Test]
        public void Adoption_AC81_GenTwoThousandInJanuaryTwoThousandTwo_IsAboutAHalf()
        {
            GenerationCatalog catalog = Catalog();
            OemCurveDef curve = catalog.ById("gen-2000").oem;
            Assert.Equal(6, curve.delayMonths);
            Assert.Equal(36, curve.rampMonths);

            // Launched in March 2000, today is January 2002: m = 22; the generation holds 15 % of the base.
            double a = OemAdoptionModel.Adoption(curve, true, D(2000, 3, 14).MonthsTo(D(2002, 1, 1)), 0.15);

            Assert.Near(0.9 * 16.0 / 36.0 + 0.5 * 0.15, a, Eps);
            Assert.Near(0.475, a, Eps);
        }

        [Test]
        public void Adoption_ANotLaunchedGeneration_IsZero()
        {
            var curve = new OemCurveDef { delayMonths = 6, rampMonths = 36, maxAdoption = 0.9f, ibAccel = 0.5f };
            Assert.Equal(0.0, OemAdoptionModel.Adoption(curve, false, 40, 0.5));
            Assert.Equal(0.0, OemAdoptionModel.Adoption(null, true, 40, 0.5));
        }

        [Test]
        public void Adoption_RampsLinearlyAfterTheDelay_AndStopsAtTheMaximum()
        {
            var curve = new OemCurveDef { delayMonths = 6, rampMonths = 36, maxAdoption = 0.9f, ibAccel = 0f };
            Assert.Equal(0.0, OemAdoptionModel.Adoption(curve, true, 0, 0));
            Assert.Equal(0.0, OemAdoptionModel.Adoption(curve, true, 6, 0));
            Assert.Near(0.9 * 0.5, OemAdoptionModel.Adoption(curve, true, 24, 0), Eps);
            Assert.Near(0.9, OemAdoptionModel.Adoption(curve, true, 42, 0), Eps);
            Assert.Near(0.9, OemAdoptionModel.Adoption(curve, true, 400, 0), Eps);
            Assert.Equal(0.0, OemAdoptionModel.Adoption(curve, true, -10, 0), "a launch in the future");
        }

        [Test]
        public void Adoption_TheInstalledBaseAcceleratesIt_ButNeverPastTheMaximum()
        {
            var curve = new OemCurveDef { delayMonths = 6, rampMonths = 36, maxAdoption = 0.9f, ibAccel = 0.5f };
            Assert.Near(0.25, OemAdoptionModel.Adoption(curve, true, 3, 0.5), Eps, "before the ramp only the base counts");
            Assert.Near(0.9, OemAdoptionModel.Adoption(curve, true, 100, 1.0), Eps);
            Assert.Near(0.9, OemAdoptionModel.Adoption(curve, true, 30, 0.9), Eps);
            Assert.Near(0.5, OemAdoptionModel.Adoption(curve, true, 3, 5.0), Eps, "a share above 1 is cut to 1");
        }

        [Test]
        public void Adoption_BrokenInputs_StayInRange()
        {
            var curve = new OemCurveDef { delayMonths = 6, rampMonths = 0, maxAdoption = 0.9f, ibAccel = 0.5f };
            double a = OemAdoptionModel.Adoption(curve, true, 10, double.NaN);
            Assert.InRange(a, 0.0, 0.9 + 1e-9);
            Assert.InRange(OemAdoptionModel.Adoption(curve, true, 10, -3.0), 0.0, 0.9 + 1e-9);
        }

        [Test]
        public void Adoption_OfAnInput_UsesItsCurve()
        {
            GenerationCatalog catalog = Catalog();
            GenerationDef g = catalog.ById("gen-2000");
            Assert.Near(0.475, OemAdoptionModel.Adoption(In(g, true, 22, 0.15)), Eps);
            Assert.Equal(0.0, OemAdoptionModel.Adoption(In(g, false, 22, 0.15)));
            Assert.Equal(0.0, OemAdoptionModel.Adoption(In(null, true, 22, 0.15)));
        }

        // ---- the inputs of a day ----

        [Test]
        public void BuildInputs_AreNewestFirst_WithTheDaysFacts()
        {
            GenerationCatalog catalog = Catalog();
            var timeline = new PlatformTimeline();
            timeline.SetLaunched("gen-1995-pga", D(1995, 3, 1), "ai:1");
            timeline.SetLaunched("gen-2000", D(2000, 3, 14), "player");
            timeline.SetLaunched("gen-2010", D(2010, 5, 1), "ai:2");   // after the day: not launched yet at that day
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(new[]
            {
                LedgerTestData.Cpu(1, "player", LedgerTestData.Std("LGA 2000"), "gen-2000", new[] { 15000, 0 }),
                LedgerTestData.Cpu(2, "ai:1", LedgerTestData.Std("PGA 1995"), "gen-1995-pga", new[] { 85000, 0 }),
            }, 0, Balance());

            List<GenAdoptionInput> inputs = OemAdoptionModel.BuildInputs(catalog, timeline, ledger, D(2002, 1, 1), null);

            Assert.Equal(catalog.Count, inputs.Count);
            Assert.Equal("gen-2030", inputs[0].Gen.id);
            Assert.Equal("gen-1971-dip", inputs[inputs.Count - 1].Gen.id);
            for (int i = 1; i < inputs.Count; i++) Assert.True(inputs[i - 1].Gen.order > inputs[i].Gen.order, "descending order #" + i);

            GenAdoptionInput g2000 = inputs.Find(x => x.Gen.id == "gen-2000");
            Assert.True(g2000.Launched);
            Assert.Equal(22, g2000.MonthsSinceLaunch);
            Assert.Near(0.15, g2000.IbShareG, Eps);
            Assert.False(g2000.ObsoleteAtT);
            Assert.Near(0.475, OemAdoptionModel.Adoption(g2000), Eps);

            GenAdoptionInput g1995 = inputs.Find(x => x.Gen.id == "gen-1995-pga");
            Assert.True(g1995.Launched);
            Assert.False(g1995.ObsoleteAtT, "gen-1995-pga is obsolete from 2005");
            Assert.Near(0.85, g1995.IbShareG, Eps);

            Assert.False(inputs.Find(x => x.Gen.id == "gen-2010").Launched, "launched after the day");
            Assert.False(inputs.Find(x => x.Gen.id == "gen-1990-pga").Launched, "never launched in the chronology");
            Assert.True(inputs.Find(x => x.Gen.id == "gen-1990-pga").ObsoleteAtT, "gen-1990-pga is obsolete from 2000");
        }

        [Test]
        public void BuildInputs_ReusesTheListGiven_AndToleratesNothingAtAll()
        {
            GenerationCatalog catalog = Catalog();
            var reused = new List<GenAdoptionInput> { default(GenAdoptionInput) };

            List<GenAdoptionInput> a = OemAdoptionModel.BuildInputs(catalog, null, null, D(2002, 1, 1), reused);
            Assert.True(ReferenceEquals(reused, a));
            Assert.Equal(catalog.Count, a.Count, "the list is cleared first");
            Assert.False(a.Exists(x => x.Launched));

            Assert.Equal(0, OemAdoptionModel.BuildInputs(null, null, null, D(2002, 1, 1), null).Count);
        }

        // ---- F8: the preference ----

        [Test]
        public void Roll_IsDeterministic_AndInTheHalfOpenUnitInterval()
        {
            double a = OemAdoptionModel.Roll("Apple|1993-1|52000|299|1", "gen-1990-pga", 12345u);
            double b = OemAdoptionModel.Roll("Apple|1993-1|52000|299|1", "gen-1990-pga", 12345u);
            Assert.Equal(a, b);
            Assert.InRange(a, 0.0, 1.0);
            Assert.True(a < 1.0);
        }

        [Test]
        public void Roll_IsTheMixedFnv1aHashOfKeyGenAndSeed_PinnedForEveryMachine()
        {
            // Reference values from an independent implementation: FNV-1a 64 of the UTF-8 text key#gen#seed, then fmix64, the top 53 bits over 2^53.
            Assert.Near(0.4388427468134596, OemAdoptionModel.Roll("Apple|1993-1|52000|299|1", "gen-1990-pga", 12345u), 1e-15);
            Assert.Near(0.11171663361647732, OemAdoptionModel.Roll("Sony|1993-1|30000|199|2", "gen-2000", 0u), 1e-15);
            Assert.Near(0.6400685551903493, OemAdoptionModel.Roll("Яндекс|2001-12|100|5|1", "gen-2000", 4294967295u), 1e-15);
        }

        [Test]
        public void Roll_DependsOnTheKeyTheGenerationAndTheSeed()
        {
            double baseline = OemAdoptionModel.Roll("Apple|1993-1|52000|299|1", "gen-1990-pga", 12345u);
            Assert.NotEqual(baseline, OemAdoptionModel.Roll("Apple|1993-2|52000|299|1", "gen-1990-pga", 12345u));
            Assert.NotEqual(baseline, OemAdoptionModel.Roll("Apple|1993-1|52000|299|1", "gen-1995-pga", 12345u));
            Assert.NotEqual(baseline, OemAdoptionModel.Roll("Apple|1993-1|52000|299|1", "gen-1990-pga", 12346u));
        }

        [Test]
        public void Roll_AChangeOfTheLastDigitOfTheSeed_ChangesTheNumberCompletely()
        {
            // Plain FNV-1a would move the number by about 1e-5 here; two scenarios with seeds 1 and 2 would play alike.
            int different = 0;
            for (int i = 0; i < 200; i++)
            {
                string key = ContractKeyBuilder.Build("Client" + i, 2000, 1, 100 + i, 50f, 1);
                if (Math.Abs(OemAdoptionModel.Roll(key, "gen-2000", 1u) - OemAdoptionModel.Roll(key, "gen-2000", 2u)) > 1e-3) different++;
            }
            Assert.True(different >= 190, "only " + different + " of 200 keys rolled differently");
        }

        [Test]
        public void Roll_IsUniform_ChiSquaredOnTenThousandKeys_PIsAboveOnePercent()
        {
            const int n = 10000;
            const int binCount = 10;
            string[] clients = { "Apple", "Sony", "Intel", "AMD", "Dell", "HP", "Acer", "Asus" };
            var bins = new int[binCount];
            for (int i = 0; i < n; i++)
            {
                string key = ContractKeyBuilder.Build(clients[i % 8], 1971 + (i / 96) % 60, 1 + (i / 8) % 12, 1000 + i * 37, 50 + (i * 7) % 400, 1 + i % 2);
                double r = OemAdoptionModel.Roll(key, "gen-2000", 20261010u);
                bins[(int)(r * binCount)]++;
            }

            double expected = n / (double)binCount;
            double chi = 0;
            foreach (int observed in bins) chi += (observed - expected) * (observed - expected) / expected;

            // The critical value of the chi-squared distribution with 9 degrees of freedom at p = 0.01 is 21.666.
            Assert.True(chi < 21.666, "chi-squared " + chi.ToString("0.00", CultureInfo.InvariantCulture) + " with bins " + string.Join(",", bins));
        }

        [Test]
        public void AssignPreference_TakesTheNewestGenerationThatIsSureToWin()
        {
            GenerationDef newer = Sure("g-new", 5);
            GenerationDef older = Sure("g-old", 4);
            var list = new List<GenAdoptionInput> { In(newer, true, 12), In(older, true, 40) };
            Assert.Equal("g-new", OemAdoptionModel.AssignPreference("k", 1u, list));
        }

        [Test]
        public void AssignPreference_SkipsNotLaunchedAndObsoleteGenerations()
        {
            GenerationDef newest = Sure("g-newest", 6);
            GenerationDef obsolete = Sure("g-obsolete", 5);
            GenerationDef unlaunched = Sure("g-unlaunched", 4);
            GenerationDef old = Sure("g-old", 3);
            var list = new List<GenAdoptionInput>
            {
                In(newest, false, 0),
                In(obsolete, true, 40, 0, true),
                In(unlaunched, false, 40),
                In(old, true, 40),
            };

            Assert.Equal("g-old", OemAdoptionModel.AssignPreference("k", 1u, list));
        }

        [Test]
        public void AssignPreference_WithNothingToChoose_IsEmpty()
        {
            GenerationDef a = Sure("a", 2);
            GenerationDef b = Sure("b", 1);
            var none = new List<GenAdoptionInput> { In(a, false, 0), In(b, true, 40, 0, true) };

            Assert.Equal(string.Empty, OemAdoptionModel.AssignPreference("k", 1u, none));
            Assert.Equal(string.Empty, OemAdoptionModel.AssignPreference("k", 1u, new List<GenAdoptionInput>()));
            Assert.Equal(string.Empty, OemAdoptionModel.AssignPreference("k", 1u, null));
            Assert.Equal(string.Empty, OemAdoptionModel.AssignPreference(null, 1u, new List<GenAdoptionInput> { In(a, true, 40) }));
            Assert.Equal(string.Empty, OemAdoptionModel.AssignPreference(string.Empty, 1u, new List<GenAdoptionInput> { In(a, true, 40) }));
        }

        [Test]
        public void AssignPreference_AGenerationWithoutAdoptionYet_IsPassedOver()
        {
            var slow = Sure("g-slow", 6);
            slow.oem = new OemCurveDef { delayMonths = 12, rampMonths = 36, maxAdoption = 0.9f, ibAccel = 0f };
            GenerationDef old = Sure("g-old", 5);
            var list = new List<GenAdoptionInput> { In(slow, true, 3), In(old, true, 40) };

            for (int i = 0; i < 50; i++) Assert.Equal("g-old", OemAdoptionModel.AssignPreference("key" + i, 7u, list));
        }

        [Test]
        public void AssignPreference_IsDeterministic_AC86_AndFollowsTheSeed()
        {
            GenerationCatalog catalog = Catalog();
            var inputs = new List<GenAdoptionInput> { In(catalog.ById("gen-2000"), true, 22, 0.15), In(catalog.ById("gen-1995-pga"), true, 80, 0.85) };

            var first = new List<string>();
            var second = new List<string>();
            var otherSeed = new List<string>();
            for (int i = 0; i < 200; i++)
            {
                string key = ContractKeyBuilder.Build("Client" + i, 2002, 1, 1000 + i, 100 + i, 1);
                first.Add(OemAdoptionModel.AssignPreference(key, 77u, inputs));
                second.Add(OemAdoptionModel.AssignPreference(key, 77u, inputs));
                otherSeed.Add(OemAdoptionModel.AssignPreference(key, 78u, inputs));
            }

            Assert.Equal(first, second);
            Assert.NotEqual(first, otherSeed);
            Assert.True(first.Contains("gen-2000") && first.Contains("gen-1995-pga") && first.Contains(string.Empty), "all three outcomes occur");
        }

        [Test]
        public void AssignPreference_ChoosesAGenerationAboutAsOftenAsItsAdoption()
        {
            GenerationCatalog catalog = Catalog();
            var inputs = new List<GenAdoptionInput> { In(catalog.ById("gen-2000"), true, 22, 0.15) };   // A = 0.475

            int chosen = 0;
            const int n = 4000;
            for (int i = 0; i < n; i++)
            {
                string key = ContractKeyBuilder.Build("Client" + (i % 31), 2000 + i % 20, 1 + i % 12, 500 + i * 13, 90 + i % 90, 1 + i % 2);
                if (OemAdoptionModel.AssignPreference(key, 1234u, inputs) == "gen-2000") chosen++;
            }

            Assert.Near(0.475, chosen / (double)n, 0.03);
        }

        // ---- F9a: the platform multiplier ----

        [Test]
        public void PlatformMultiplier_WithoutAFullPlatform_IsTwelveTenthsThenSeventyFiveThenFifty()
        {
            PlatformBalance balance = Balance();
            string reason;

            Assert.Near(1.20, OemAdoptionModel.PlatformMultiplier(5, 5, false, balance, out reason), Eps);
            Assert.Equal(OemAdoptionModel.ReasonPrefers, reason);
            Assert.Near(1.20, OemAdoptionModel.PlatformMultiplier(7, 5, false, balance, out reason), Eps);
            Assert.Equal(OemAdoptionModel.ReasonPrefers, reason, "a newer generation than the preferred one is as good");
            Assert.Near(0.75, OemAdoptionModel.PlatformMultiplier(4, 5, false, balance, out reason), Eps);
            Assert.Equal(OemAdoptionModel.ReasonLag, reason);
            Assert.Near(0.50, OemAdoptionModel.PlatformMultiplier(3, 5, false, balance, out reason), Eps);
            Assert.Near(0.50, OemAdoptionModel.PlatformMultiplier(0, 5, false, balance, out reason), Eps);
            Assert.Equal(OemAdoptionModel.ReasonLag, reason);
            Assert.Equal(1f, OemAdoptionModel.PlatformMultiplier(null, 5, false, balance, out reason));
            Assert.Null(reason);
            Assert.Equal(1f, OemAdoptionModel.PlatformMultiplier(5, null, false, balance, out reason));
            Assert.Null(reason);
        }

        [Test]
        public void PlatformMultiplier_WithAFullPlatform_AddsFiveHundredths()
        {
            PlatformBalance balance = Balance();
            string reason;

            Assert.Near(1.25, OemAdoptionModel.PlatformMultiplier(5, 5, true, balance, out reason), Eps);
            Assert.Near(0.80, OemAdoptionModel.PlatformMultiplier(4, 5, true, balance, out reason), Eps);
            Assert.Near(0.55, OemAdoptionModel.PlatformMultiplier(3, 5, true, balance, out reason), Eps);
            Assert.Equal(1f, OemAdoptionModel.PlatformMultiplier(null, 5, true, balance, out reason), "no generation, no bonus either");
            Assert.Equal(1f, OemAdoptionModel.PlatformMultiplier(5, null, true, balance, out reason));
        }

        [Test]
        public void PlatformMultiplier_FollowsTheBalance()
        {
            PlatformBalance balance = Balance();
            balance.Oem.AdoptBonus = 0.3f;
            balance.Oem.LagPenalty1 = 0.1f;
            balance.Oem.LagPenalty2 = 0.2f;
            balance.Oem.FullPlatformBonus = 0.02f;
            string reason;

            Assert.Near(1.32, OemAdoptionModel.PlatformMultiplier(5, 5, true, balance, out reason), Eps);
            Assert.Near(0.90, OemAdoptionModel.PlatformMultiplier(4, 5, false, balance, out reason), Eps);
            Assert.Near(0.82, OemAdoptionModel.PlatformMultiplier(2, 5, true, balance, out reason), Eps);
        }

        [Test]
        public void PlatformMultiplier_AC82_TheNumbersOfTheDesign_WithTheRealGenerations()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            GenerationDef preferred = catalog.ById("gen-2000");
            string reason;

            // The player's CPU on PGA 2000 with DDR: the preferred generation and a full platform.
            GenerationDef own = catalog.ByBasePackage("PGA 2000");
            bool ownFull = catalog.IsFullPlatform(own, "DDR");
            float ownMult = OemAdoptionModel.PlatformMultiplier(own.order, preferred.order, ownFull, balance, out reason);
            Assert.Near(1.25, ownMult, Eps);

            // The rival's CPU on PGA 1995 with SDR: one generation behind, with the memory of its own generation.
            GenerationDef rival = catalog.ByBasePackage("PGA 1995");
            bool rivalFull = catalog.IsFullPlatform(rival, "SDR");
            float rivalMult = OemAdoptionModel.PlatformMultiplier(rival.order, preferred.order, rivalFull, balance, out reason);
            Assert.Near(0.80, rivalMult, Eps);

            // The vanilla chances 40 and 60 become 50 and 48.
            Assert.Equal(50, (int)Math.Round(40 * ownMult));
            Assert.Equal(48, (int)Math.Round(60 * rivalMult));
        }

        [Test]
        public void PlatformMultiplier_AC85_AMemoryOutsideTheGeneration_GetsNoFullPlatformBonus()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            GenerationDef preferred = catalog.ById("gen-2000");
            GenerationDef lga = catalog.ByBasePackage("LGA 2000");
            string reason;

            bool sdrFull = catalog.IsFullPlatform(lga, "SDR");
            Assert.False(sdrFull);
            Assert.Near(1.20, OemAdoptionModel.PlatformMultiplier(lga.order, preferred.order, sdrFull, balance, out reason), Eps);
            Assert.Near(1.25, OemAdoptionModel.PlatformMultiplier(lga.order, preferred.order, catalog.IsFullPlatform(lga, "DDR"), balance, out reason), Eps);
        }

        [Test]
        public void PlatformMultiplier_ACalculatorContract_IsNeutral_AC83()
        {
            PlatformBalance balance = Balance();
            string reason;

            // Calculator is 0, Computer 1, Console 2; only the last two are in contractTypes by default.
            Assert.Equal(1f, OemAdoptionModel.PlatformMultiplier(5, 5, true, 0, balance, out reason));
            Assert.Null(reason);
            Assert.Equal(1f, OemAdoptionModel.PlatformMultiplier(2, 5, true, 0, balance, out reason));
            Assert.Near(1.25, OemAdoptionModel.PlatformMultiplier(5, 5, true, 1, balance, out reason), Eps);
            Assert.Near(0.80, OemAdoptionModel.PlatformMultiplier(4, 5, true, 2, balance, out reason), Eps);
            Assert.Equal(1f, OemAdoptionModel.PlatformMultiplier(5, 5, true, 7, balance, out reason), "an unknown type is not in the list");
        }

        [Test]
        public void PlatformMultiplier_TheTypesComeFromTheBalance()
        {
            PlatformBalance balance = Balance();
            balance.Oem.ContractTypes = new[] { 0 };
            string reason;
            Assert.Near(1.25, OemAdoptionModel.PlatformMultiplier(5, 5, true, 0, balance, out reason), Eps);
            Assert.Equal(1f, OemAdoptionModel.PlatformMultiplier(5, 5, true, 1, balance, out reason));
        }

        // ---- F9b: the market average ----

        private static List<GenAdoptionInput> January2002(GenerationCatalog catalog)
        {
            // gen-2000: A = 0.475 (launched March 2000, 15 % of the base); gen-1995-pga: A = 0.9; gen-1990-pga and older are obsolete.
            var list = new List<GenAdoptionInput>
            {
                In(catalog.ById("gen-2000"), true, 22, 0.15),
                In(catalog.ById("gen-1995-pga"), true, 82, 0.0),
                In(catalog.ById("gen-1990-pga"), true, 140, 0.0, true),
                In(catalog.ById("gen-1985-pga"), true, 200, 0.0, true),
            };
            return list;
        }

        [Test]
        public void MarketAverageMultiplier_AC84_ThreeCpus()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);
            int order2000 = catalog.ById("gen-2000").order;
            int order1995 = catalog.ById("gen-1995-pga").order;

            // A CPU on PGA 2000 with DDR: a full platform of the newest generation.
            float newest = OemAdoptionModel.MarketAverageMultiplier(order2000, true, inputs, balance);
            Assert.Near(0.475 * 1.25 + 0.4725 * 1.25 + 0.0525, newest, 1e-4);
            Assert.Near(1.237, newest, 1e-3);

            // A CPU on PGA 1995 with SDR: a full platform, one generation behind.
            float behind = OemAdoptionModel.MarketAverageMultiplier(order1995, true, inputs, balance);
            Assert.Near(0.475 * 0.80 + 0.4725 * 1.25 + 0.0525, behind, 1e-4);
            Assert.Near(1.023, behind, 1e-3);

            // The same CPU with a memory that is not the standard of its generation.
            float notFull = OemAdoptionModel.MarketAverageMultiplier(order1995, false, inputs, balance);
            Assert.Near(0.475 * 0.75 + 0.4725 * 1.20 + 0.0525, notFull, 1e-4);
            Assert.Near(0.976, notFull, 1e-3);
        }

        [Test]
        public void MarketDistribution_AC84_TheWeightsAreTheDesignOnes_AndAddUpToOne()
        {
            GenerationCatalog catalog = Catalog();
            List<GenAdoptionInput> inputs = January2002(catalog);
            var p = new double[inputs.Count];
            double none;

            OemAdoptionModel.MarketDistribution(inputs, p, out none);

            Assert.Near(0.475, p[0], 1e-6);
            Assert.Near(0.9 * 0.525, p[1], 1e-6);
            Assert.Equal(0.0, p[2], "obsolete");
            Assert.Equal(0.0, p[3], "obsolete");
            Assert.Near(0.525 * 0.1, none, 1e-6);
            Assert.Near(1.0, p[0] + p[1] + p[2] + p[3] + none, 1e-9);
        }

        [Test]
        public void MarketDistribution_AddsUpToOne_ForAnyAdoptions()
        {
            GenerationCatalog catalog = Catalog();
            var rng = new LedgerTestData.Lcg(3u);
            for (int run = 0; run < 100; run++)
            {
                var list = new List<GenAdoptionInput>();
                IReadOnlyList<GenerationDef> all = catalog.Generations;
                for (int i = all.Count - 1; i >= 0; i--)
                {
                    list.Add(In(all[i], rng.Next(3) != 0, rng.Next(160), rng.Next(100) / 100.0, rng.Next(4) == 0));
                }

                var p = new double[list.Count];
                double none;
                OemAdoptionModel.MarketDistribution(list, p, out none);

                double sum = none;
                foreach (double x in p)
                {
                    Assert.True(x >= 0.0 && x <= 1.0 + 1e-12);
                    sum += x;
                }
                Assert.Near(1.0, sum, 1e-9, "run " + run);
            }
        }

        [Test]
        public void MarketAverageMultiplier_AnOldCpuGeneration_IsNeutral()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);
            Assert.Equal(2, balance.Oem.MarketAverageMinGenOrder);

            Assert.Equal(1f, OemAdoptionModel.MarketAverageMultiplier(0, true, inputs, balance), "DIP: the market of calculators");
            Assert.Equal(1f, OemAdoptionModel.MarketAverageMultiplier(1, true, inputs, balance), "PLCC");
            Assert.NotEqual(1f, OemAdoptionModel.MarketAverageMultiplier(2, true, inputs, balance));

            balance.Oem.MarketAverageMinGenOrder = 5;
            Assert.Equal(1f, OemAdoptionModel.MarketAverageMultiplier(4, true, inputs, balance));
            Assert.NotEqual(1f, OemAdoptionModel.MarketAverageMultiplier(5, true, inputs, balance));
        }

        [Test]
        public void MarketAverageMultiplier_WithoutAGenerationOrOnAnEmptyMarket_IsNeutral()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);

            Assert.Equal(1f, OemAdoptionModel.MarketAverageMultiplier(null, true, inputs, balance));
            Assert.Equal(1f, OemAdoptionModel.MarketAverageMultiplier(5, true, new List<GenAdoptionInput>(), balance), "p_none = 1");
            Assert.Equal(1f, OemAdoptionModel.MarketAverageMultiplier(5, true, null, balance));

            var nothingLaunched = new List<GenAdoptionInput> { In(catalog.ById("gen-2000"), false, 0), In(catalog.ById("gen-1995-pga"), false, 0) };
            Assert.Equal(1f, OemAdoptionModel.MarketAverageMultiplier(5, true, nothingLaunched, balance));
        }

        // ---- F9c: the loyalty and the clamps ----

        [Test]
        public void WithLoyalty_AddsTheLoyaltyOfABigBase()
        {
            PlatformBalance balance = Balance();
            Assert.Near(1.0, OemAdoptionModel.WithLoyalty(1.0f, 0.0, balance), Eps);
            Assert.Near(1.05, OemAdoptionModel.WithLoyalty(1.0f, 0.2, balance), Eps);   // half of the saturation share
            Assert.Near(1.10, OemAdoptionModel.WithLoyalty(1.0f, 0.4, balance), Eps);
            Assert.Near(1.10, OemAdoptionModel.WithLoyalty(1.0f, 0.9, balance), Eps, "the loyalty saturates");
            Assert.Near(1.10, OemAdoptionModel.WithLoyalty(1.0f, 5.0, balance), Eps);
            Assert.Near(1.0, OemAdoptionModel.WithLoyalty(1.0f, -1.0, balance), Eps);
        }

        [Test]
        public void WithLoyalty_TheLargestMultiplierIs1375_AndTheSmallestIsHalf()
        {
            PlatformBalance balance = Balance();
            string reason;

            float best = OemAdoptionModel.PlatformMultiplier(5, 5, true, balance, out reason);
            Assert.Near(1.375, OemAdoptionModel.WithLoyalty(best, 0.9, balance), Eps);

            float worst = OemAdoptionModel.PlatformMultiplier(0, 5, false, balance, out reason);
            Assert.Near(0.50, OemAdoptionModel.WithLoyalty(worst, 0.0, balance), Eps);
        }

        [Test]
        public void WithLoyalty_KeepsTheResultInsideTheGuardClamps()
        {
            PlatformBalance balance = Balance();
            balance.Oem.OemLoyaltyBonus = 0.5f;
            Assert.Near(1.40, OemAdoptionModel.WithLoyalty(1.25f, 0.4, balance), Eps, "1.875 is cut to maxChanceMult");
            Assert.Near(0.40, OemAdoptionModel.WithLoyalty(0.1f, 0.0, balance), Eps, "0.1 is lifted to minChanceMult");

            balance.Oem.MinChanceMult = 0.6f;
            Assert.Near(0.6, OemAdoptionModel.WithLoyalty(0.5f, 0.0, balance), Eps);
            Assert.Equal(1f, OemAdoptionModel.WithLoyalty(float.NaN, 0.0, balance));
        }

        [Test]
        public void WithLoyalty_WithoutABalance_UsesTheDefaults()
        {
            Assert.Near(1.375, OemAdoptionModel.WithLoyalty(1.25f, 1.0, null), Eps);
        }

        // ---- the preferences of the contracts ----

        [Test]
        public void OemPreferences_AssignOnce_AndAnEmptyAnswerIsAnAnswer()
        {
            var prefs = new OemPreferences();
            Assert.Null(prefs.PreferenceOf("k1"));

            Assert.True(prefs.Assign("k1", "gen-2000"));
            Assert.True(prefs.Assign("k2", string.Empty));
            Assert.True(prefs.Assign("k3", null));
            Assert.False(prefs.Assign("k1", "gen-1995-pga"), "decided once");

            Assert.Equal("gen-2000", prefs.PreferenceOf("k1"));
            Assert.Equal(string.Empty, prefs.PreferenceOf("k2"));
            Assert.Equal(string.Empty, prefs.PreferenceOf("k3"));
            Assert.Null(prefs.PreferenceOf("k4"));
            Assert.Null(prefs.PreferenceOf(null));
            Assert.False(prefs.Assign(null, "gen-2000"));
            Assert.False(prefs.Assign(string.Empty, "gen-2000"));
            Assert.Equal(3, prefs.Count);
        }

        [Test]
        public void OemPreferences_Prune_RemovesTheContractsThatAreGone()
        {
            var prefs = new OemPreferences();
            prefs.Assign("keep1", "gen-2000");
            prefs.Assign("gone1", "gen-2000");
            prefs.Assign("keep2", string.Empty);
            prefs.Assign("gone2", "gen-1995-pga");
            var alive = new HashSet<string> { "keep1", "keep2", "never-assigned" };

            int removed = prefs.Prune(key => alive.Contains(key));

            Assert.Equal(2, removed);
            Assert.Equal(2, prefs.Count);
            Assert.Null(prefs.PreferenceOf("gone1"));
            Assert.Null(prefs.PreferenceOf("gone2"));
            Assert.Equal("gen-2000", prefs.PreferenceOf("keep1"));
            Assert.Equal(string.Empty, prefs.PreferenceOf("keep2"));
            Assert.Equal(0, prefs.Prune(null));
            Assert.Equal(2, prefs.Count);
            Assert.True(prefs.Assign("gone1", "gen-2010"), "a removed key can be decided again");
        }

        [Test]
        public void OemPreferences_RoundTripThroughTheSave_KeepsUnknownGenerations()
        {
            var prefs = new OemPreferences();
            prefs.Assign("Apple|1993-1|52000|299|1", "gen-1990-pga");
            prefs.Assign("Sony|1993-1|30000|199|2", string.Empty);
            prefs.Assign("Old|1980-1|10|10|1", "gen-from-an-old-content");

            var save = new PlatformGenerationsSave();
            save.generations.Add(new GenerationStateDto { id = "untouched" });
            prefs.WriteTo(save);
            Assert.Equal(3, save.contractPrefs.Count);
            Assert.Equal("Apple|1993-1|52000|299|1", save.contractPrefs[0].key);
            Assert.Equal(1, save.generations.Count, "the chronology is not touched");

            var again = new OemPreferences();
            var problems = new List<string>();
            again.ReadFrom(save, problems);

            Assert.Equal(0, problems.Count);
            Assert.Equal(3, again.Count);
            Assert.Equal("gen-1990-pga", again.PreferenceOf("Apple|1993-1|52000|299|1"));
            Assert.Equal(string.Empty, again.PreferenceOf("Sony|1993-1|30000|199|2"));
            Assert.Equal("gen-from-an-old-content", again.PreferenceOf("Old|1980-1|10|10|1"));
        }

        [Test]
        public void OemPreferences_ReadFrom_DropsBrokenAndRepeatedRecords_AndReplacesWhatWasThere()
        {
            var prefs = new OemPreferences();
            prefs.Assign("stale", "gen-2000");
            var save = new PlatformGenerationsSave();
            save.contractPrefs.Add(new ContractPrefDto { key = "a", gen = "gen-2000" });
            save.contractPrefs.Add(new ContractPrefDto { key = "a", gen = "gen-1995-pga" });
            save.contractPrefs.Add(new ContractPrefDto { key = string.Empty, gen = "gen-2000" });
            save.contractPrefs.Add(null);
            save.contractPrefs.Add(new ContractPrefDto { key = "b", gen = null });
            var problems = new List<string>();

            prefs.ReadFrom(save, problems);

            Assert.Equal(2, prefs.Count);
            Assert.Null(prefs.PreferenceOf("stale"));
            Assert.Equal("gen-2000", prefs.PreferenceOf("a"), "the first record of a key stands");
            Assert.Equal(string.Empty, prefs.PreferenceOf("b"));
            Assert.Equal(2, problems.Count, string.Join("; ", problems));

            prefs.ReadFrom(null, null);
            Assert.Equal(0, prefs.Count);
        }

        [Test]
        public void OemPreferences_Clear_ForgetsEverything()
        {
            var prefs = new OemPreferences();
            prefs.Assign("a", "g");
            prefs.Clear();
            Assert.Equal(0, prefs.Count);
            Assert.Null(prefs.PreferenceOf("a"));
        }

        // ---- the contract key ----

        [Test]
        public void ContractKeyBuilder_TheExampleOfTheDesign()
        {
            Assert.Equal("Apple|1993-1|52000|299|1", ContractKeyBuilder.Build("Apple", 1993, 1, 52000, 299f, 1));
            Assert.Equal("Sony|1993-1|30000|199|2", ContractKeyBuilder.Build("Sony", 1993, 1, 30000, 199f, 2));
        }

        [Test]
        public void ContractKeyBuilder_IsStable_AndTheMonthIsNotPadded()
        {
            string a = ContractKeyBuilder.Build("Apple", 2002, 12, 1000, 55.4f, 1);
            string b = ContractKeyBuilder.Build("Apple", 2002, 12, 1000, 55.4f, 1);
            Assert.Equal(a, b);
            Assert.Equal("Apple|2002-12|1000|55|1", a);
            Assert.Equal("Apple|2002-1|1000|55|1", ContractKeyBuilder.Build("Apple", 2002, 1, 1000, 55.4f, 1));
        }

        [Test]
        public void ContractKeyBuilder_EscapesTheSeparatorInTheNameOfTheClient()
        {
            string key = ContractKeyBuilder.Build("A|B", 1993, 1, 52000, 299f, 1);
            Assert.Equal("A\\|B|1993-1|52000|299|1", key);

            // No client name can move a part of the key into another one.
            Assert.NotEqual(ContractKeyBuilder.Build("A|1993-1|52000|299|1|X", 2000, 1, 1, 1f, 1), ContractKeyBuilder.Build("A", 1993, 1, 52000, 299f, 1));
            Assert.NotEqual(ContractKeyBuilder.Build("A\\", 1993, 1, 1, 1f, 1), ContractKeyBuilder.Build("A|", 1993, 1, 1, 1f, 1));
            Assert.Equal(4, CountSeparators(ContractKeyBuilder.Build("X", 1993, 1, 52000, 299f, 1)), "four separators between five parts");
            Assert.Equal("A\\\\|1993-1|1|1|1", ContractKeyBuilder.Build("A\\", 1993, 1, 1, 1f, 1));
        }

        [Test]
        public void ContractKeyBuilder_RoundsThePaymentToAWholeNumber()
        {
            Assert.Equal("C|2000-1|1|300|1", ContractKeyBuilder.Build("C", 2000, 1, 1, 299.5f, 1));
            Assert.Equal("C|2000-1|1|299|1", ContractKeyBuilder.Build("C", 2000, 1, 1, 299.4f, 1));
            Assert.Equal("C|2000-1|1|0|1", ContractKeyBuilder.Build("C", 2000, 1, 1, float.NaN, 1));
            Assert.Equal("C|2000-1|1|0|1", ContractKeyBuilder.Build("C", 2000, 1, 1, float.PositiveInfinity, 1));
        }

        [Test]
        public void ContractKeyBuilder_ANullNameIsAnEmptyName()
        {
            Assert.Equal("|2000-1|1|5|0", ContractKeyBuilder.Build(null, 2000, 1, 1, 5f, 0));
            Assert.Equal("|2000-1|1|5|0", ContractKeyBuilder.Build(string.Empty, 2000, 1, 1, 5f, 0));
        }

        private static int CountSeparators(string key)
        {
            int count = 0;
            foreach (char c in key)
            {
                if (c == ContractKeyBuilder.Separator) count++;
            }
            return count;
        }
    }
}
