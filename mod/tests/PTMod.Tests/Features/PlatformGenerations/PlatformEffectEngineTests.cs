using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PlatformGenerations;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-009, FR-5, FR-8, FR-10: the pure engine behind the two providers - the mode follows the contract in the question, the setting
    /// <c>marketAverage</c> forces the market mode, the fast-forward, a CPU outside the generations, a Calculator contract and an offer
    /// without a preference (AC-8.2, AC-8.3, AC-8.4, AC-8.5), the reasons of the answers and the arguments prepared for them, and the rating
    /// multiplier of a CPU read from the installed base and the chronology (AC-5.1).
    /// </summary>
    [Covers(typeof(PlatformEffectEngine), typeof(EffectCpu), typeof(EffectContract), typeof(ChanceResult), typeof(PlatformReasonArgs))]
    public class PlatformEffectEngineTests
    {
        private const double Eps = 1e-6;
        private const string Key = "Apple|2002-1|52000|299|1";

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

        private static GenAdoptionInput In(GenerationDef gen, bool launched, int months, double ibShare = 0, bool obsolete = false)
        {
            return new GenAdoptionInput { Gen = gen, Launched = launched, MonthsSinceLaunch = months, IbShareG = ibShare, ObsoleteAtT = obsolete };
        }

        /// <summary>January 2002 as in AC-8.4: gen-2000 adopted 47.5 %, gen-1995-pga 90 %, the older ones obsolete; the newest first.</summary>
        private static List<GenAdoptionInput> January2002(GenerationCatalog catalog)
        {
            return new List<GenAdoptionInput>
            {
                In(catalog.ById("gen-2010"), false, 0),
                In(catalog.ById("gen-2000"), true, 22, 0.15),
                In(catalog.ById("gen-1995-pga"), true, 82),
                In(catalog.ById("gen-1990-pga"), true, 140, 0.0, true),
                In(catalog.ById("gen-1985-pga"), true, 200, 0.0, true),
            };
        }

        private static EffectCpu Cpu(GenerationCatalog catalog, string genId, bool full, double effShare = 0)
        {
            return new EffectCpu { GenOrder = catalog.ById(genId).order, GenId = genId, FullPlatform = full, EffShare = effShare };
        }

        private static EffectContract Offer(int type = 1, string key = Key)
        {
            return new EffectContract { Present = true, Key = key, ContractType = type };
        }

        private static OemPreferences Prefers(string genId, string key = Key)
        {
            var prefs = new OemPreferences();
            prefs.Assign(key, genId);
            return prefs;
        }

        // ---- the mode ----

        [Test]
        public void EffectiveMode_FollowsTheContract_AndTheSettingForcesTheMarket()
        {
            var engine = new PlatformEffectEngine();

            Assert.Equal(OemMode.Auto, engine.EffectiveMode(OemMode.Auto, true), "a contract in the question: per contract");
            Assert.Equal(OemMode.MarketAverage, engine.EffectiveMode(OemMode.Auto, false), "no contract: the market average");
            Assert.Equal(OemMode.MarketAverage, engine.EffectiveMode(OemMode.MarketAverage, true), "marketAverage forces the market mode");
            Assert.Equal(OemMode.MarketAverage, engine.EffectiveMode(OemMode.MarketAverage, false));
        }

        // ---- per contract (AC-8.2, AC-8.3, AC-8.5) ----

        [Test]
        public void Chance_PerContract_AC82_ThePreferredGenerationAndOneBehind()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);
            OemPreferences prefs = Prefers("gen-2000");
            var engine = new PlatformEffectEngine();

            // The player's CPU on PGA 2000 with DDR: the preferred generation and a full platform.
            ChanceResult mine = engine.Chance(Cpu(catalog, "gen-2000", true), Offer(), OemMode.Auto, prefs, inputs, balance);
            Assert.Near(1.25, mine.Multiplier, Eps);
            Assert.Equal(OemAdoptionModel.ReasonPrefers, mine.ReasonKey);
            Assert.Equal("gen-2000", mine.GenId);

            // The CPU of the rival on PGA 1995 with SDR: one generation behind (0.75) and a full platform (+0.05).
            ChanceResult rival = engine.Chance(Cpu(catalog, "gen-1995-pga", true), Offer(), OemMode.Auto, prefs, inputs, balance);
            Assert.Near(0.80, rival.Multiplier, Eps);
            Assert.Equal(OemAdoptionModel.ReasonLag, rival.ReasonKey);
            Assert.Equal(1, rival.LagGenerations);
            Assert.Equal("gen-2000", rival.GenId);
        }

        [Test]
        public void Chance_PerContract_TwoOrMoreBehind_IsTheBiggestPenalty()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            var engine = new PlatformEffectEngine();

            ChanceResult result = engine.Chance(Cpu(catalog, "gen-1990-pga", false), Offer(), OemMode.Auto, Prefers("gen-2000"),
                January2002(catalog), balance);

            Assert.Near(0.50, result.Multiplier, Eps);
            Assert.Equal(OemAdoptionModel.ReasonLag, result.ReasonKey);
            Assert.Equal(2, result.LagGenerations);
        }

        [Test]
        public void Chance_PerContract_AC85_AMemoryOutsideTheStandard_HasNoFullPlatformBonus()
        {
            GenerationCatalog catalog = Catalog();
            var engine = new PlatformEffectEngine();

            // LGA 2000 with SDR: gen-2000, but SDR is not one of its memories.
            ChanceResult result = engine.Chance(Cpu(catalog, "gen-2000", false), Offer(), OemMode.Auto, Prefers("gen-2000"),
                January2002(catalog), Balance());

            Assert.Near(1.20, result.Multiplier, Eps);
        }

        [Test]
        public void Chance_PerContract_AC83_ACalculatorContract_IsOneForEveryone_EvenWithALoyalBase()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            var engine = new PlatformEffectEngine();

            foreach (string genId in new[] { "gen-2000", "gen-1995-pga", "gen-1990-pga" })
            {
                ChanceResult result = engine.Chance(Cpu(catalog, genId, true, 0.4), Offer(0), OemMode.Auto, Prefers("gen-2000"),
                    January2002(catalog), balance);
                Assert.Equal(1f, result.Multiplier, genId);
                Assert.Null(result.ReasonKey, genId);
            }
        }

        [Test]
        public void Chance_PerContract_TheTypesComeFromTheBalance()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            balance.Oem.ContractTypes = new[] { 0 };
            var engine = new PlatformEffectEngine();

            ChanceResult calculator = engine.Chance(Cpu(catalog, "gen-2000", true), Offer(0), OemMode.Auto, Prefers("gen-2000"), January2002(catalog), balance);
            ChanceResult computer = engine.Chance(Cpu(catalog, "gen-2000", true), Offer(1), OemMode.Auto, Prefers("gen-2000"), January2002(catalog), balance);

            Assert.Near(1.25, calculator.Multiplier, Eps);
            Assert.Equal(1f, computer.Multiplier);
        }

        [Test]
        public void Chance_PerContract_WithoutAPreference_IsOne_AndOnlyTheLoyaltyRemains()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);
            var engine = new PlatformEffectEngine();

            // Nothing is assigned, an assigned "no generation", and a generation that the content does not have: all mean "no preference".
            var none = new OemPreferences();
            var decidedNone = Prefers(string.Empty);
            var gone = Prefers("gen-9999-old");
            foreach (OemPreferences prefs in new[] { none, decidedNone, gone, null })
            {
                ChanceResult plain = engine.Chance(Cpu(catalog, "gen-2000", true, 0), Offer(), OemMode.Auto, prefs, inputs, balance);
                Assert.Equal(1f, plain.Multiplier);
                Assert.Null(plain.ReasonKey);

                // 40 % of the base is the saturation: the full loyalty bonus 1.10, named as the reason.
                ChanceResult loyal = engine.Chance(Cpu(catalog, "gen-2000", true, 0.4), Offer(), OemMode.Auto, prefs, inputs, balance);
                Assert.Near(1.10, loyal.Multiplier, Eps);
                Assert.Equal(OemAdoptionModel.ReasonLoyalty, loyal.ReasonKey);
            }
        }

        [Test]
        public void Chance_PerContract_Loyalty_MakesTheBiggestMultiplier1375_AndTheSmallestStaysHalf()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);
            var engine = new PlatformEffectEngine();

            ChanceResult most = engine.Chance(Cpu(catalog, "gen-2000", true, 0.4), Offer(), OemMode.Auto, Prefers("gen-2000"), inputs, balance);
            Assert.Near(1.375, most.Multiplier, Eps);
            Assert.Equal(OemAdoptionModel.ReasonPrefers, most.ReasonKey, "the platform is the reason, the loyalty only adds");

            ChanceResult least = engine.Chance(Cpu(catalog, "gen-1990-pga", false, 0), Offer(), OemMode.Auto, Prefers("gen-2000"), inputs, balance);
            Assert.Near(0.50, least.Multiplier, Eps);
        }

        [Test]
        public void Chance_APlatformThatChangesNothing_NamesTheLoyalty()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            balance.Oem.AdoptBonus = 0f;
            balance.Oem.FullPlatformBonus = 0f;
            var engine = new PlatformEffectEngine();

            ChanceResult result = engine.Chance(Cpu(catalog, "gen-2000", true, 0.2), Offer(), OemMode.Auto, Prefers("gen-2000"), January2002(catalog), balance);

            Assert.Near(1.05, result.Multiplier, Eps);
            Assert.Equal(OemAdoptionModel.ReasonLoyalty, result.ReasonKey);
        }

        // ---- the market average (AC-8.4) ----

        [Test]
        public void Chance_Market_AC84_ThreeCpus()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);
            var engine = new PlatformEffectEngine();
            var noContract = new EffectContract();

            ChanceResult newest = engine.Chance(Cpu(catalog, "gen-2000", true), noContract, OemMode.Auto, null, inputs, balance);
            Assert.Near(1.237, newest.Multiplier, 1e-3);
            Assert.Equal(OemAdoptionModel.ReasonAdoption, newest.ReasonKey);

            ChanceResult behind = engine.Chance(Cpu(catalog, "gen-1995-pga", true), noContract, OemMode.Auto, null, inputs, balance);
            Assert.Near(1.023, behind.Multiplier, 1e-3);

            ChanceResult notFull = engine.Chance(Cpu(catalog, "gen-1995-pga", false), noContract, OemMode.Auto, null, inputs, balance);
            Assert.Near(0.976, notFull.Multiplier, 1e-3);
        }

        [Test]
        public void Chance_Market_TheReasonNamesTheLeadingGeneration_WithItsAdoption()
        {
            GenerationCatalog catalog = Catalog();
            var engine = new PlatformEffectEngine();

            ChanceResult result = engine.Chance(Cpu(catalog, "gen-2000", true), new EffectContract(), OemMode.Auto, null, January2002(catalog), Balance());

            // gen-2010 is not launched; the newest launched generation with an adoption is gen-2000: 0.9·16/36 + 0.5·0.15 = 47.5 %.
            Assert.Equal("gen-2000", result.GenId);
            Assert.Near(47.5, result.AdoptionPercent, 1e-4);
        }

        [Test]
        public void Chance_Market_TheSettingMarketAverage_IgnoresThePreferenceOfTheContract()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);
            var engine = new PlatformEffectEngine();

            // The contract prefers gen-2000 (which would give 1.25), but the setting asks for the market: 1.237.
            ChanceResult forced = engine.Chance(Cpu(catalog, "gen-2000", true), Offer(), OemMode.MarketAverage, Prefers("gen-2000"), inputs, balance);
            Assert.Near(1.237, forced.Multiplier, 1e-3);
            Assert.Equal(OemAdoptionModel.ReasonAdoption, forced.ReasonKey);

            // The Calculator contract is still left alone in that mode.
            ChanceResult calculator = engine.Chance(Cpu(catalog, "gen-2000", true), Offer(0), OemMode.MarketAverage, null, inputs, balance);
            Assert.Equal(1f, calculator.Multiplier);
        }

        [Test]
        public void Chance_Market_TheCalculatorEra_BelowTheMinimumOrder_IsOne()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            Assert.True(balance.Oem.MarketAverageMinGenOrder >= 1, "the default leaves the first generations out");
            var engine = new PlatformEffectEngine();

            foreach (string genId in new[] { "gen-1971-dip", "gen-1980-plcc" })
            {
                ChanceResult result = engine.Chance(Cpu(catalog, genId, true, 0.4), new EffectContract(), OemMode.Auto, null, January2002(catalog), balance);
                Assert.Equal(1f, result.Multiplier, genId);
                Assert.Null(result.ReasonKey, genId);
            }
        }

        [Test]
        public void Chance_Market_Loyalty_IsAppliedLast()
        {
            GenerationCatalog catalog = Catalog();
            var engine = new PlatformEffectEngine();

            ChanceResult result = engine.Chance(Cpu(catalog, "gen-2000", true, 0.4), new EffectContract(), OemMode.Auto, null, January2002(catalog), Balance());

            Assert.Near(1.237 * 1.10, result.Multiplier, 2e-3);
            Assert.Equal(OemAdoptionModel.ReasonAdoption, result.ReasonKey);
        }

        // ---- neutral cases ----

        [Test]
        public void Chance_DuringTheFastForward_IsOne_InEveryMode()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            var engine = new PlatformEffectEngine();

            EffectCpu cpu = Cpu(catalog, "gen-2000", true, 0.4);
            cpu.IsFastForwarding = true;
            foreach (OemMode mode in new[] { OemMode.Auto, OemMode.MarketAverage })
            {
                ChanceResult withContract = engine.Chance(cpu, Offer(), mode, Prefers("gen-2000"), January2002(catalog), balance);
                ChanceResult without = engine.Chance(cpu, new EffectContract(), mode, null, January2002(catalog), balance);
                Assert.Equal(1f, withContract.Multiplier, mode.ToString());
                Assert.Equal(1f, without.Multiplier, mode.ToString());
                Assert.Null(withContract.ReasonKey);
                Assert.Null(without.ReasonKey);
            }
        }

        [Test]
        public void Chance_ACpuOutsideTheGenerations_IsOne()
        {
            GenerationCatalog catalog = Catalog();
            var engine = new PlatformEffectEngine();
            var outside = new EffectCpu { GenOrder = null, GenId = null, FullPlatform = false, EffShare = 0.4 };

            Assert.Equal(1f, engine.Chance(outside, Offer(), OemMode.Auto, Prefers("gen-2000"), January2002(catalog), Balance()).Multiplier);
            Assert.Equal(1f, engine.Chance(outside, new EffectContract(), OemMode.Auto, null, January2002(catalog), Balance()).Multiplier);
        }

        [Test]
        public void Chance_NoGenerationsAtAll_AndNullInputs_AreHandled()
        {
            GenerationCatalog catalog = Catalog();
            var engine = new PlatformEffectEngine();
            EffectCpu cpu = Cpu(catalog, "gen-2000", true);

            Assert.Equal(1f, engine.Chance(cpu, new EffectContract(), OemMode.Auto, null, null, null).Multiplier);
            Assert.Equal(1f, engine.Chance(cpu, Offer(), OemMode.Auto, null, new List<GenAdoptionInput>(), null).Multiplier);
            Assert.Equal(1f, engine.Chance(cpu, new EffectContract(), OemMode.Auto, null, new List<GenAdoptionInput>(), Balance()).Multiplier,
                "nobody has adopted anything: the expectation is 1");
        }

        [Test]
        public void Chance_AlwaysStaysInTheRangeOfTheBalance()
        {
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            var engine = new PlatformEffectEngine();
            var rng = new LedgerTestData.Lcg(7u);
            string[] gens = { "gen-1971-dip", "gen-1980-plcc", "gen-1985-pga", "gen-1990-pga", "gen-1995-pga", "gen-2000", "gen-2010", "gen-2015" };
            List<GenAdoptionInput> inputs = January2002(catalog);

            for (int i = 0; i < 2000; i++)
            {
                string genId = gens[rng.Next(gens.Length)];
                string pref = gens[rng.Next(gens.Length)];
                EffectCpu cpu = Cpu(catalog, genId, rng.Next(2) == 0, rng.Next(1001) / 1000.0);
                EffectContract contract = rng.Next(3) == 0 ? new EffectContract() : Offer(rng.Next(3));
                OemMode mode = rng.Next(4) == 0 ? OemMode.MarketAverage : OemMode.Auto;

                ChanceResult r = engine.Chance(cpu, contract, mode, Prefers(pref), inputs, balance);

                Assert.True(r.Multiplier == 1f || (r.Multiplier >= balance.Oem.MinChanceMult && r.Multiplier <= balance.Oem.MaxChanceMult),
                    "out of range: " + r.Multiplier);
                Assert.True(r.ReasonKey != null || r.Multiplier == 1f, "a multiplier without a reason: " + r.Multiplier);
            }
        }

        [Test]
        public void Chance_IsFast_ForTheProvider()
        {
            // The provider answers tens of questions a year and one per click: a call must stay far under the 20 microseconds of NFR-1.
            GenerationCatalog catalog = Catalog();
            PlatformBalance balance = Balance();
            List<GenAdoptionInput> inputs = January2002(catalog);
            OemPreferences prefs = Prefers("gen-2000");
            var engine = new PlatformEffectEngine();
            EffectCpu cpu = Cpu(catalog, "gen-1995-pga", true, 0.1);
            EffectContract contract = Offer();

            engine.Chance(cpu, contract, OemMode.Auto, prefs, inputs, balance);   // warm up
            var watch = Stopwatch.StartNew();
            float sum = 0;
            const int calls = 100000;
            for (int i = 0; i < calls; i++) sum += engine.Chance(cpu, contract, OemMode.Auto, prefs, inputs, balance).Multiplier;
            watch.Stop();

            Assert.True(sum > 0);
            double microseconds = watch.Elapsed.TotalMilliseconds * 1000.0 / calls;
            Assert.True(microseconds < 20.0, "a call took " + microseconds.ToString("0.00", CultureInfo.InvariantCulture) + " microseconds");
        }

        // ---- the rating (FR-5) ----

        private static InstalledBaseLedger Ledger(PlatformBalance balance, params CpuLedgerView[] cpus)
        {
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(cpus, 0, balance);
            return ledger;
        }

        /// <summary>A CPU that sold <paramref name="units"/> in the one full month of the ledger.</summary>
        private static CpuLedgerView Sold(int id, string company, SocketId socket, string genId, int units)
        {
            return LedgerTestData.Cpu(id, company, socket, genId, new[] { units, 0 });
        }

        [Test]
        public void RatingFor_AC51_ThirtyPercentOnAClosedSocket_Gives1075_AndForIndustries10375()
        {
            PlatformBalance balance = Balance();
            SocketId closed = LedgerTestData.Custom(5, "PGA 1995");
            InstalledBaseLedger ledger = Ledger(balance,
                Sold(1, LedgerTestData.Player, closed, "gen-1995-pga", 30),
                Sold(2, "ai:1", LedgerTestData.Std("PGA 1990"), "gen-1990-pga", 70));

            RatingBreakdown r = new PlatformEffectEngine().RatingFor(LedgerTestData.Player, closed, "gen-1995-pga", ledger, new PlatformTimeline(), D(1996, 1), balance);

            Assert.Near(0.30, r.EffShare, Eps);
            Assert.Near(1.075, r.ConsumerMult, Eps);
            Assert.Near(1.0375, r.IndustriesMult, Eps);
        }

        [Test]
        public void RatingFor_TheEarlyAdopterBonus_FadesWithTheMonthsOfTheToday()
        {
            PlatformBalance balance = Balance();
            SocketId socket = LedgerTestData.Std("PGA 1995");
            InstalledBaseLedger empty = Ledger(balance);
            var timeline = new PlatformTimeline();
            Assert.True(timeline.AddEarlyAdopter("gen-1995-pga", "ai:1", D(1995, 3), 3));
            var engine = new PlatformEffectEngine();

            // 12 months later: 0.06·(1 − 12/60) = 0.048.
            Assert.Near(1.048, engine.RatingFor("ai:1", socket, "gen-1995-pga", empty, timeline, D(1996, 3), balance).ConsumerMult, Eps);
            // The day of joining: the whole bonus. The company that is not an adopter: nothing.
            Assert.Near(1.06, engine.RatingFor("ai:1", socket, "gen-1995-pga", empty, timeline, D(1995, 3), balance).ConsumerMult, Eps);
            Assert.Near(1.0, engine.RatingFor("ai:2", socket, "gen-1995-pga", empty, timeline, D(1996, 3), balance).ConsumerMult, Eps);
            // After 60 months it is gone.
            Assert.Near(1.0, engine.RatingFor("ai:1", socket, "gen-1995-pga", empty, timeline, D(2000, 3), balance).ConsumerMult, Eps);
            // The Industries segment gets half.
            Assert.Near(1.024, engine.RatingFor("ai:1", socket, "gen-1995-pga", empty, timeline, D(1996, 3), balance).IndustriesMult, Eps);
        }

        [Test]
        public void RatingFor_TheSocketChangePenalty_FadesFromTheDayTheSocketAppeared()
        {
            PlatformBalance balance = Balance();
            SocketId socket = LedgerTestData.Std("PGA 1995");
            InstalledBaseLedger empty = Ledger(balance);
            var timeline = new PlatformTimeline();
            Assert.True(timeline.AddSocketIntro(LedgerTestData.Player, socket.Key, D(1995, 3), "pkg:PGA 1990", 12, 0.6667f));
            var engine = new PlatformEffectEngine();

            // At once: 0.10·0.667 = 0.0667; after 12 months half of it: 0.0333.
            Assert.Near(1.0 - 0.06667, engine.RatingFor(LedgerTestData.Player, socket, "gen-1995-pga", empty, timeline, D(1995, 3), balance).ConsumerMult, 1e-4);
            Assert.Near(1.0 - 0.03333, engine.RatingFor(LedgerTestData.Player, socket, "gen-1995-pga", empty, timeline, D(1996, 3), balance).ConsumerMult, 1e-4);
            Assert.Near(1.0, engine.RatingFor(LedgerTestData.Player, socket, "gen-1995-pga", empty, timeline, D(1997, 3), balance).ConsumerMult, Eps);
        }

        [Test]
        public void RatingFor_ASocketWithSeverityZero_AndAReconstructedAppearance_CostNothing()
        {
            PlatformBalance balance = Balance();
            SocketId socket = LedgerTestData.Std("PGA 1995");
            var timeline = new PlatformTimeline();
            Assert.True(timeline.AddSocketIntro("ai:1", socket.Key, D(1995, 1), null, 0, 0f));

            RatingBreakdown r = new PlatformEffectEngine().RatingFor("ai:1", socket, "gen-1995-pga", Ledger(balance), timeline, D(1995, 6), balance);

            Assert.Near(1.0, r.ConsumerMult, Eps);
        }

        [Test]
        public void RatingFor_WithoutAGenerationAFeatureOrALedger_IsNeutral()
        {
            PlatformBalance balance = Balance();
            SocketId socket = LedgerTestData.Std("PGA 1995");
            InstalledBaseLedger ledger = Ledger(balance, Sold(1, LedgerTestData.Player, socket, "gen-1995-pga", 100));
            var engine = new PlatformEffectEngine();

            Assert.Equal(1f, engine.RatingFor(LedgerTestData.Player, socket, null, ledger, null, D(1996, 1), balance).ConsumerMult, "outside the generations");
            Assert.Equal(1f, engine.RatingFor(null, socket, "gen-1995-pga", ledger, null, D(1996, 1), balance).IndustriesMult, "no company");
            Assert.Equal(1f, engine.RatingFor(LedgerTestData.Player, default(SocketId), "gen-1995-pga", ledger, null, D(1996, 1), balance).ConsumerMult, "no socket");
            Assert.Equal(1f, engine.RatingFor(LedgerTestData.Player, socket, "gen-1995-pga", null, null, D(1996, 1), balance).ConsumerMult, "no ledger");

            // No timeline and no date are fine: the base alone counts (all of it is the player's: the whole bonus).
            RatingBreakdown alone = engine.RatingFor(LedgerTestData.Player, socket, "gen-1995-pga", ledger, null, GameDate.None, balance);
            Assert.Near(1.10, alone.ConsumerMult, Eps);
        }

        [Test]
        public void RatingFor_ForAMarketOfCpus_StaysInTheRange_AndTheFeatureNeverLiftsItAbove116()
        {
            PlatformBalance balance = Balance();
            List<CpuLedgerView> market = LedgerTestData.Market(300, 120, 11u);
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(market, 118, balance);
            var timeline = new PlatformTimeline();
            var engine = new PlatformEffectEngine();
            var rng = new LedgerTestData.Lcg(5u);

            foreach (CpuLedgerView cpu in market)
            {
                if (rng.Next(5) == 0) timeline.AddEarlyAdopter(cpu.GenId, cpu.CompanyKey, D(1975, 1), 100);
                if (rng.Next(5) == 0) timeline.AddSocketIntro(cpu.CompanyKey, cpu.SocketKey, D(1978, 1), "pkg:PGA 1985", 10, 0.8f);
            }

            foreach (CpuLedgerView cpu in market)
            {
                SocketId socket = ledger.SocketOf(cpu.SocketKey);
                RatingBreakdown r = engine.RatingFor(cpu.CompanyKey, socket, cpu.GenId, ledger, timeline, D(1980, 6), balance);
                Assert.True(r.ConsumerMult >= 0.90f - 1e-6f && r.ConsumerMult <= 1.16f + 1e-6f, "consumer " + r.ConsumerMult);
                Assert.True(r.IndustriesMult >= 0.95f - 1e-6f && r.IndustriesMult <= 1.08f + 1e-6f, "industries " + r.IndustriesMult);
            }
        }

        // ---- the arguments of the reasons (FR-10 item 3) ----

        private static PlatformReasonArgs Prepared(GenerationCatalog catalog, List<GenAdoptionInput> inputs)
        {
            var args = new PlatformReasonArgs();
            args.Rebuild(catalog, inputs, gen => "Name " + gen.id);
            return args;
        }

        [Test]
        public void ReasonArgs_Of_GivesTheMultiplierTheReasonAndThePreparedArguments()
        {
            GenerationCatalog catalog = Catalog();
            List<GenAdoptionInput> inputs = January2002(catalog);
            PlatformReasonArgs reasons = Prepared(catalog, inputs);

            ProviderResult prefers = reasons.Of(new ChanceResult { Multiplier = 1.25f, ReasonKey = OemAdoptionModel.ReasonPrefers, GenId = "gen-2000" });
            Assert.Equal(1.25f, prefers.Multiplier);
            Assert.Equal(OemAdoptionModel.ReasonPrefers, prefers.ReasonKey);
            Assert.Equal(1, prefers.ReasonArgs.Length);
            Assert.Equal("Name gen-2000", prefers.ReasonArgs[0]);

            ProviderResult lag = reasons.Of(new ChanceResult { Multiplier = 0.8f, ReasonKey = OemAdoptionModel.ReasonLag, GenId = "gen-2000", LagGenerations = 2 });
            Assert.Equal(1, lag.ReasonArgs.Length);
            Assert.Equal("2", lag.ReasonArgs[0]);

            ProviderResult adoption = reasons.Of(new ChanceResult { Multiplier = 1.2f, ReasonKey = OemAdoptionModel.ReasonAdoption, GenId = "gen-2000", AdoptionPercent = 47.5 });
            Assert.Equal(2, adoption.ReasonArgs.Length);
            Assert.Equal("Name gen-2000", adoption.ReasonArgs[0]);
            Assert.Equal("47.5", adoption.ReasonArgs[1]);

            ProviderResult loyalty = reasons.Of(new ChanceResult { Multiplier = 1.1f, ReasonKey = OemAdoptionModel.ReasonLoyalty });
            Assert.Equal(OemAdoptionModel.ReasonLoyalty, loyalty.ReasonKey);
            Assert.Equal(0, loyalty.ReasonArgs == null ? 0 : loyalty.ReasonArgs.Length);

            ProviderResult plain = reasons.Of(ChanceResult.Neutral);
            Assert.Equal(1f, plain.Multiplier);
            Assert.Null(plain.ReasonKey);
        }

        [Test]
        public void ReasonArgs_AreTheSameArraysEveryTime_SoACallBuildsNothing()
        {
            GenerationCatalog catalog = Catalog();
            PlatformReasonArgs reasons = Prepared(catalog, January2002(catalog));
            var prefers = new ChanceResult { Multiplier = 1.25f, ReasonKey = OemAdoptionModel.ReasonPrefers, GenId = "gen-2000" };
            var lag = new ChanceResult { Multiplier = 0.75f, ReasonKey = OemAdoptionModel.ReasonLag, GenId = "gen-2000", LagGenerations = 1 };
            var adoption = new ChanceResult { Multiplier = 1.2f, ReasonKey = OemAdoptionModel.ReasonAdoption, GenId = "gen-1995-pga", AdoptionPercent = 90 };

            Assert.True(ReferenceEquals(reasons.Of(prefers).ReasonArgs, reasons.Of(prefers).ReasonArgs), "prefers");
            Assert.True(ReferenceEquals(reasons.Of(lag).ReasonArgs, reasons.Of(lag).ReasonArgs), "lag");
            Assert.True(ReferenceEquals(reasons.Of(adoption).ReasonArgs, reasons.Of(adoption).ReasonArgs), "adoption");
        }

        [Test]
        public void ReasonArgs_Rebuild_PicksUpANewLanguageAndANewAdoption()
        {
            GenerationCatalog catalog = Catalog();
            List<GenAdoptionInput> inputs = January2002(catalog);
            PlatformReasonArgs reasons = Prepared(catalog, inputs);
            var adoption = new ChanceResult { Multiplier = 1.2f, ReasonKey = OemAdoptionModel.ReasonAdoption, GenId = "gen-2000", AdoptionPercent = 47.5 };
            Assert.Equal("47.5", reasons.Of(adoption).ReasonArgs[1]);

            // A month later gen-2000 is adopted by 0.9·17/36 + 0.5·0.2 = 52.5 %; the names are in another language.
            inputs[1] = In(catalog.ById("gen-2000"), true, 23, 0.20);
            reasons.Rebuild(catalog, inputs, gen => "Имя " + gen.id);

            ProviderResult after = reasons.Of(adoption);
            Assert.Equal("Имя gen-2000", after.ReasonArgs[0]);
            Assert.Equal("52.5", after.ReasonArgs[1]);
        }

        [Test]
        public void ReasonArgs_ForAGenerationThatWasNotPrepared_ArePutTogetherOnTheSpot()
        {
            var reasons = new PlatformReasonArgs();

            ProviderResult prefers = reasons.Of(new ChanceResult { Multiplier = 1.25f, ReasonKey = OemAdoptionModel.ReasonPrefers, GenId = "gen-x" });
            ProviderResult lag = reasons.Of(new ChanceResult { Multiplier = 0.75f, ReasonKey = OemAdoptionModel.ReasonLag, LagGenerations = 3 });
            ProviderResult adoption = reasons.Of(new ChanceResult { Multiplier = 1.1f, ReasonKey = OemAdoptionModel.ReasonAdoption, GenId = "gen-x", AdoptionPercent = 12.34 });

            Assert.Equal("gen-x", prefers.ReasonArgs[0]);
            Assert.Equal("3", lag.ReasonArgs[0]);
            Assert.Equal("gen-x", adoption.ReasonArgs[0]);
            Assert.Equal("12.3", adoption.ReasonArgs[1]);

            reasons.Clear();
            Assert.Equal(0, reasons.Count);
        }

        [Test]
        public void ReasonArgs_Percent_UsesADotAndOneDecimal_WhateverTheCulture()
        {
            Assert.Equal("47.5", PlatformReasonArgs.Percent(47.5));
            Assert.Equal("0.0", PlatformReasonArgs.Percent(0));
            Assert.Equal("100.0", PlatformReasonArgs.Percent(100));
            Assert.Equal("0.0", PlatformReasonArgs.Percent(double.NaN));
        }

        [Test]
        public void ReasonArgs_FillEveryPlaceholderOfTheTexts_InBothLanguages()
        {
            GenerationCatalog catalog = Catalog();
            PlatformReasonArgs reasons = Prepared(catalog, January2002(catalog));
            var results = new[]
            {
                new ChanceResult { Multiplier = 1.25f, ReasonKey = OemAdoptionModel.ReasonPrefers, GenId = "gen-2000" },
                new ChanceResult { Multiplier = 0.75f, ReasonKey = OemAdoptionModel.ReasonLag, GenId = "gen-2000", LagGenerations = 1 },
                new ChanceResult { Multiplier = 1.2f, ReasonKey = OemAdoptionModel.ReasonAdoption, GenId = "gen-2000", AdoptionPercent = 47.5 },
                new ChanceResult { Multiplier = 1.1f, ReasonKey = OemAdoptionModel.ReasonLoyalty },
            };

            foreach (string language in new[] { "ru", "en" })
            {
                var tables = new L10nTables();
                tables.AddJson(language, language, PlatformTestData.ReadStrings(language), null);
                foreach (ChanceResult result in results)
                {
                    string text;
                    Assert.True(tables.TryGet(language, result.ReasonKey, out text), language + " lacks " + result.ReasonKey);

                    object[] args = reasons.Of(result).ReasonArgs ?? new string[0];
                    int highest = -1;
                    for (int i = 0; i < 6; i++)
                    {
                        if (text.Contains("{" + i.ToString(CultureInfo.InvariantCulture) + "}")) highest = i;
                    }
                    Assert.True(args.Length > highest, language + " " + result.ReasonKey + ": the text takes {" + highest + "} but the arguments are " + args.Length);
                    string shown = string.Format(CultureInfo.InvariantCulture, text, args);
                    Assert.False(shown.Contains("{"), shown);
                }
            }
        }
    }
}
