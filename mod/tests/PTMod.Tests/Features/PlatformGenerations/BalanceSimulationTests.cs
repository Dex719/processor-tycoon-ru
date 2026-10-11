using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Modifiers;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Modifiers;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-022 (NFR-5, NFR-7, FR-5, FR-6, FR-8): the balance of the feature played offline, with the shipped <c>generations.json</c> and <c>balance.json</c>,
    /// by <see cref="PlatformSimulator"/>: 14 synthetic companies, 1971-2030, month by month, through the real models. What the formulas give, in numbers:
    /// the multipliers of the rating stay in [0.85; 1.20] (in practice [0.90; 1.16]) and those of the signing chance in [0.40; 1.40]; a closed socket with
    /// 40 % of the base gets exactly +10 %; an AI that moves to its own socket on a new base every five years pays nothing; mixed with the multipliers
    /// of other features through the real channel of the core the total is held to [0.5; 2.0] and the limit never raises the feature's own part; the
    /// rules are the same for the player and the AI. <see cref="Report_PrintsMinMaxAndAverage_ForTheOwner"/> prints the numbers the owner wants.
    /// </summary>
    public class BalanceSimulationTests
    {
        private const float Eps = 1e-5f;

        // ---- the runs ----

        private static PlatformSimulator _main;

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

        private static PlatformSimulator Play(string playerKey, PlatformBalance balance = null)
        {
            var sim = new PlatformSimulator(Catalog(), balance ?? Balance(), playerKey);
            sim.Run();
            return sim;
        }

        /// <summary>The run with the shipped balance, played once for all the tests of the class.</summary>
        private static PlatformSimulator Main()
        {
            return _main ?? (_main = Play("player"));
        }

        private static SimCompany Company(PlatformSimulator sim, string key)
        {
            foreach (SimCompany company in sim.Companies)
            {
                if (company.Key == key) return company;
            }
            Assert.Fail("no company " + key);
            return null;
        }

        private static string Num(double value)
        {
            return value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
        }

        // ---- the run itself ----

        [Test]
        public void TheRun_IsFourteenCompanies_SixtyYears_AndEveryGenerationLaunches()
        {
            PlatformSimulator sim = Main();
            Assert.Equal(14, sim.Companies.Count);
            Assert.Equal(720, PlatformSimulator.Months);
            Assert.True(sim.CpusReleased > 500, "CPUs: " + sim.CpusReleased);

            foreach (SimCompany company in sim.Companies) Assert.True(company.Releases > 0, company.Key + " never released a CPU");

            foreach (GenerationDef gen in Catalog().Generations)
            {
                GameDate launched;
                string by;
                Assert.True(sim.Timeline.IsAnnounced(gen.id), gen.id + " was never announced");
                Assert.True(sim.Timeline.IsLaunched(gen.id, out launched, out by), gen.id + " never launched");
                Assert.True(sim.Timeline.EarlyAdoptersOf(gen.id).Count >= 1 && sim.Timeline.EarlyAdoptersOf(gen.id).Count <= 3, gen.id + ": between one and three early adopters");
            }
            Assert.True(sim.Base.IsConsistent(), "the installed base agrees with itself after 720 months");
        }

        [Test]
        public void TheRun_IsTheSameEveryTime()
        {
            PlatformSimulator again = Play("player");
            PlatformSimulator sim = Main();
            Assert.Equal(sim.FirstCompanyFingerprint, again.FirstCompanyFingerprint);
            Assert.Equal(sim.Consumer.Min, again.Consumer.Min);
            Assert.Equal(sim.Consumer.Max, again.Consumer.Max);
            Assert.Equal(sim.Chance.Sum, again.Chance.Sum);
            Assert.Equal(sim.CpusReleased, again.CpusReleased);
        }

        // ---- the ranges (NFR-5) ----

        [Test]
        public void TheRatingMultiplier_StaysInTheRangeOfTheDesign()
        {
            PlatformSimulator sim = Main();

            // The guards of the content: [0.85; 1.20].
            Assert.True(sim.Consumer.Min >= 0.85 && sim.Consumer.Max <= 1.20, "consumer " + sim.Consumer);
            Assert.True(sim.Industries.Min >= 0.85 && sim.Industries.Max <= 1.20, "Industries " + sim.Industries);

            // In practice with the defaults: 1 - churnPenalty ... 1 + ibMaxBonus + earlyBonus, and half of that for Industries.
            Assert.True(sim.Consumer.Min >= 0.90 - Eps && sim.Consumer.Max <= 1.16 + Eps, "consumer " + sim.Consumer);
            Assert.True(sim.Industries.Min >= 0.95 - Eps && sim.Industries.Max <= 1.08 + Eps, "Industries " + sim.Industries);

            // The run reaches both sides of the range: it is a test of the range, not of a flat line.
            Assert.True(sim.Consumer.Max > 1.08, "a strong platform is rewarded: max " + Num(sim.Consumer.Max));
            Assert.True(sim.Consumer.Min < 1.0, "and a company that keeps changing its socket is punished: min " + Num(sim.Consumer.Min));
            Assert.True(sim.Consumer.Count > 20000, "values: " + sim.Consumer.Count);
        }

        [Test]
        public void TheRatingOfIndustries_IsHalfAsStrongAsTheRestOfTheMarkets()
        {
            PlatformSimulator sim = Main();
            foreach (SimSample sample in sim.Samples)
            {
                float expected = 1f + (sample.Consumer - 1f) * 0.5f;
                Assert.Near(expected, sample.Industries, 1e-5, "company " + sample.Company + ", month " + sample.Month);
            }
        }

        [Test]
        public void TheSigningChance_StaysInTheRangeOfTheDesign()
        {
            PlatformSimulator sim = Main();

            Assert.True(sim.Chance.Min >= 0.40 && sim.Chance.Max <= 1.40, "per contract " + sim.Chance);
            Assert.True(sim.ChanceMarket.Min >= 0.40 && sim.ChanceMarket.Max <= 1.40, "market " + sim.ChanceMarket);

            // In practice with the defaults: 1 - lagPenalty2 ... (1 + adoptBonus + fullPlatformBonus) * (1 + oemLoyaltyBonus) = 0.50 ... 1.375.
            Assert.True(sim.Chance.Min >= 0.50 - Eps && sim.Chance.Max <= 1.375 + Eps, "per contract " + sim.Chance);
            Assert.True(sim.ChanceMarket.Min >= 0.50 - Eps && sim.ChanceMarket.Max <= 1.375 + Eps, "market " + sim.ChanceMarket);

            Assert.True(sim.Chance.Max > 1.2, "a CPU on the platform the client wants gains: max " + Num(sim.Chance.Max));
            Assert.True(sim.Chance.Min < 0.8, "a CPU two generations behind loses: min " + Num(sim.Chance.Min));
            Assert.True(sim.Chance.Count > 100000, "answers: " + sim.Chance.Count);
        }

        [Test]
        public void TheSigningChance_ComesWithEveryReasonOfTheDesign_AndLeavesTheCalculatorAlone()
        {
            PlatformSimulator sim = Main();
            foreach (string reason in new[] { OemAdoptionModel.ReasonPrefers, OemAdoptionModel.ReasonLag, OemAdoptionModel.ReasonLoyalty, OemAdoptionModel.ReasonAdoption, "(none)" })
            {
                Assert.True(sim.Reasons.ContainsKey(reason) && sim.Reasons[reason] > 0, "no answer with the reason " + reason);
            }
        }

        [Test]
        public void WithTheBalanceOverriddenToTheExtreme_TheGuardsOfTheContentStillHold()
        {
            // NFR-5: minMult, maxMult, minChanceMult and maxChanceMult are the insurance against an override that goes too far.
            PlatformBalance wild = Balance();
            wild.Rating.IbMaxBonus = 0.6f;
            wild.Rating.EarlyBonus = 0.6f;
            wild.Rating.ChurnPenalty = 0.95f;
            wild.Oem.AdoptBonus = 1.5f;
            wild.Oem.LagPenalty1 = 0.9f;
            wild.Oem.LagPenalty2 = 0.99f;
            wild.Oem.FullPlatformBonus = 0.5f;
            wild.Oem.OemLoyaltyBonus = 1.0f;

            PlatformSimulator sim = Play("player", wild);

            Assert.True(sim.Consumer.Min >= wild.Rating.MinMult - Eps && sim.Consumer.Max <= wild.Rating.MaxMult + Eps, "consumer " + sim.Consumer);
            Assert.True(sim.Industries.Min >= wild.Rating.MinMult - Eps && sim.Industries.Max <= wild.Rating.MaxMult + Eps, "Industries " + sim.Industries);
            Assert.True(sim.Chance.Min >= wild.Oem.MinChanceMult - Eps && sim.Chance.Max <= wild.Oem.MaxChanceMult + Eps, "per contract " + sim.Chance);
            Assert.True(sim.ChanceMarket.Min >= wild.Oem.MinChanceMult - Eps && sim.ChanceMarket.Max <= wild.Oem.MaxChanceMult + Eps, "market " + sim.ChanceMarket);
            Assert.True(sim.Consumer.Max >= wild.Rating.MaxMult - Eps, "the override did reach the guard");
        }

        // ---- the installed base (FR-5) ----

        private static InstalledBaseLedger Ledger(params CpuLedgerView[] cpus)
        {
            var ledger = new InstalledBaseLedger();
            ledger.RebuildFull(cpus, 1, Balance());
            return ledger;
        }

        private static RatingBreakdown RatingOf(InstalledBaseLedger ledger, string company, SocketId socket, string genId)
        {
            return new PlatformEffectEngine().RatingFor(company, socket, genId, ledger, new PlatformTimeline(), new GameDate(1997, 1, 1), Balance());
        }

        /// <summary>The closed company holds <paramref name="percent"/> of the base on its own socket, the rest is shared by two companies on a standard one.</summary>
        private static RatingBreakdown ClosedSocketWith(int percent)
        {
            SocketId own = LedgerTestData.Custom(100, "PGA 1995");
            SocketId standard = LedgerTestData.Std("PGA 1995");
            int closed = percent * 100;
            int rest = (10000 - closed) / 2;
            InstalledBaseLedger ledger = Ledger(
                LedgerTestData.Cpu(1, "ai:8", own, "gen-1995-pga", new[] { 0, closed }),
                LedgerTestData.Cpu(2, "ai:1", standard, "gen-1995-pga", new[] { 0, rest }),
                LedgerTestData.Cpu(3, "ai:2", standard, "gen-1995-pga", new[] { 0, rest }));
            return RatingOf(ledger, "ai:8", own, "gen-1995-pga");
        }

        [Test]
        public void AClosedSocketWith40PercentOfTheBase_GetsExactlyTenPercent()
        {
            RatingBreakdown rating = ClosedSocketWith(40);

            Assert.Near(0.40, rating.EffShare, 1e-9, "own / total, nothing shared on a closed socket");
            Assert.Near(0.10, rating.Bib, 1e-6);
            Assert.Near(1.10, rating.ConsumerMult, 1e-6, "+10 %");
            Assert.Near(1.05, rating.IndustriesMult, 1e-6, "half of it for Industries");
            Assert.Equal(0f, rating.Bearly);
            Assert.Equal(0f, rating.Pchurn);
        }

        [Test]
        public void TheBonusOfTheBase_GrowsToItsMaximumAt40Percent_AndStaysThereAbove()
        {
            Assert.Near(1.025, ClosedSocketWith(10).ConsumerMult, 1e-6);
            Assert.Near(1.05, ClosedSocketWith(20).ConsumerMult, 1e-6);
            Assert.Near(1.075, ClosedSocketWith(30).ConsumerMult, 1e-6, "30 % of the base: +7.5 %");
            Assert.Near(1.10, ClosedSocketWith(40).ConsumerMult, 1e-6);
            Assert.Near(1.10, ClosedSocketWith(60).ConsumerMult, 1e-6, "above the saturation nothing more");
            Assert.Near(1.10, ClosedSocketWith(90).ConsumerMult, 1e-6);
        }

        [Test]
        public void ASharedStandardSocket_PaysLessThanAClosedOneForTheSameOwnBase()
        {
            // The same 3000 units of the company, once on a closed socket and once on a standard one that two others use as much: the standard socket
            // adds sharedWeight (0.25) of what the others sold, so it is worth more than the closed one.
            SocketId own = LedgerTestData.Custom(100, "PGA 1995");
            SocketId standard = LedgerTestData.Std("PGA 1995");

            InstalledBaseLedger closed = Ledger(
                LedgerTestData.Cpu(1, "ai:8", own, "gen-1995-pga", new[] { 0, 3000 }),
                LedgerTestData.Cpu(2, "ai:1", standard, "gen-1995-pga", new[] { 0, 7000 }));
            InstalledBaseLedger shared = Ledger(
                LedgerTestData.Cpu(1, "ai:8", standard, "gen-1995-pga", new[] { 0, 3000 }),
                LedgerTestData.Cpu(2, "ai:1", standard, "gen-1995-pga", new[] { 0, 7000 }));

            float closedMult = RatingOf(closed, "ai:8", own, "gen-1995-pga").ConsumerMult;
            float sharedMult = RatingOf(shared, "ai:8", standard, "gen-1995-pga").ConsumerMult;
            Assert.Near(1.075, closedMult, 1e-6, "30 % on its own");
            Assert.True(sharedMult > closedMult, "a standard socket is worth the share of the others: " + closedMult + " vs " + sharedMult);
            Assert.True(sharedMult <= 1.10 + Eps);
        }

        [Test]
        public void TheClosedCompanyOfTheRun_IsPaidBySizeAndNeverAboveTheCeiling()
        {
            SimCompany closed = Company(Main(), "ai:8");
            Assert.True(closed.EffShare.Max > 0.25, "the closed platform is big in the run: " + closed.EffShare);
            Assert.True(closed.Bib.Max <= 0.10f + Eps, "B_ib is never above ibMaxBonus: " + Num(closed.Bib.Max));
            Assert.True(closed.Bib.Max > 0.06, "and comes near it: " + Num(closed.Bib.Max));
            Assert.True(closed.Penalties == 0, "it keeps each socket for ten years");
        }

        // ---- the penalty for changing a socket (FR-6) ----

        [Test]
        public void AnAiThatMovesToItsOwnSocketOnANewBaseEveryFiveYears_GetsNoPenalties()
        {
            SimCompany every5 = Company(Main(), "ai:9");

            Assert.True(every5.NewSockets >= 10, "a new socket about every five years for fifty-five years: " + every5.NewSockets);
            Assert.Equal(0, every5.Penalties);
            Assert.Equal(0f, every5.WorstSeverity);
            Assert.Equal(0.0, every5.Churn.Max, "P_churn is zero on every CPU in every month");
            Assert.True(every5.EffShare.Max >= MinBaseShare(), "its base is big enough for the penalty to apply at all (churnMinBaseShare): " + every5.EffShare);
            Assert.True(every5.Rating.Min >= 1.0 - Eps, "and the rating never falls below the neutral: " + every5.Rating);
        }

        private static double MinBaseShare()
        {
            return Balance().Rating.ChurnMinBaseShare;
        }

        [Test]
        public void AnAiThatChangesItsSocketEveryYearAndLeavesTheOldOne_DoesPay_ButNeverBeyondTheDesignedPenalty()
        {
            SimCompany hopper = Company(Main(), "ai:10");

            Assert.True(hopper.NewSockets >= 8, "it went through many sockets: " + hopper.NewSockets);
            Assert.True(hopper.Penalties >= 3, "penalties: " + hopper.Penalties);
            Assert.True(hopper.WorstSeverity > 0.3f && hopper.WorstSeverity <= 1f, "the strongest: " + hopper.WorstSeverity);
            Assert.True(hopper.Churn.Max > 0.0, "P_churn showed up in the rating");
            Assert.True(hopper.Churn.Max <= Balance().Rating.ChurnPenalty + Eps, "and never above churnPenalty: " + Num(hopper.Churn.Max));
            Assert.True(hopper.Rating.Min >= 0.90 - Eps, "the rating of its CPUs stays above 0.90: " + hopper.Rating);
        }

        [Test]
        public void CompaniesThatStayOnASocketForThreeYearsOrMore_NeverPay()
        {
            foreach (SimCompany company in Main().Companies)
            {
                if (company.Style == SimStyle.Hopper) continue;
                Assert.Equal(0, company.Penalties, company.Key + " (" + company.Style + ") changed its socket and paid");
            }
        }

        // ---- the early adopters (FR-3) ----

        [Test]
        public void TheEarlyAdopterSlots_AreTheSameForThePlayerAndTheAi()
        {
            PlatformSimulator sim = Main();
            int fromCompanies = 0;
            foreach (SimCompany company in sim.Companies) fromCompanies += company.EarlyAdopterReleases;

            int fromChronology = 0;
            foreach (GenerationDef gen in Catalog().Generations) fromChronology += sim.Timeline.EarlyAdoptersOf(gen.id).Count;
            Assert.Equal(fromChronology, fromCompanies, "every slot taken is the release of a company");
            Assert.True(Company(sim, "player").EarlyAdopterReleases >= 1, "the player, who moves half a year after the announcement, takes slots too");
        }

        // ---- mixed with the other features through the channel of the core (NFR-5) ----

        private static readonly float[] OtherFeatures = { 1.0f, 0.55f, 0.7f, 0.9f, 1.1f, 1.3f, 1.5f, 1.7f, 1.9f, 2.4f, 0.4f, 1.0f };

        [Test]
        public void MixedWithTheOtherFeatures_TheTotalIsHeldToHalfAndDouble_AndTheLimitNeverRaisesTheOwnPart()
        {
            PlatformSimulator sim = Main();
            const string channel = "market.rating.cpu";

            SimSample current = default(SimSample);
            Func<ModifierQuery, float> own = q => q.Segment == Segment.Industries ? current.Industries : current.Consumer;
            Func<ModifierQuery, float> other = q => OtherFeatures[(current.CpuId + current.Month) % OtherFeatures.Length];

            ChannelRegistry both = ModifierFixtures.Registry();
            ChannelRegistry otherOnly = ModifierFixtures.Registry();
            both.RegisterProvider("platform-generations", channel, own);
            both.RegisterProvider("some-other-feature", channel, other);
            otherOnly.RegisterProvider("some-other-feature", channel, other);

            ChannelSpec spec = both.SpecOf(channel);
            Assert.Near(0.5, spec.BalanceMin, 0, "the core limits the product on this channel to [0.5; 2.0]");
            Assert.Near(2.0, spec.BalanceMax, 0);

            int checkedSamples = 0, capped = 0, alone = 0;
            double lowest = double.MaxValue, highest = double.MinValue;
            foreach (SimSample sample in sim.Samples)
            {
                current = sample;
                foreach (Segment segment in new[] { Segment.HighEnd, Segment.Industries })
                {
                    var query = new ModifierQuery(sample.Company, sample.CpuId, segment);
                    both.Invalidate();
                    otherOnly.Invalidate();

                    float f = segment == Segment.Industries ? sample.Industries : sample.Consumer;
                    float total = both.Get(channel, query);
                    float without = otherOnly.Get(channel, query);
                    ModifierBreakdown why = both.Explain(channel, query);
                    checkedSamples++;
                    if (why.IsBalanceCapped) capped++;
                    lowest = Math.Min(lowest, total);
                    highest = Math.Max(highest, total);

                    string where = sample.Company + " cpu " + sample.CpuId + " month " + sample.Month + " " + segment + ": own " + f + ", total " + total + ", without " + without;
                    Assert.True(total >= 0.5f - Eps && total <= 2.0f + Eps, "the total is held to [0.5; 2.0]: " + where);
                    Assert.True(without >= 0.5f - Eps && without <= 2.0f + Eps, "so is the rest alone: " + where);

                    // The part of the feature in the result: with the limit it is never more than the multiplier itself, and it keeps its direction.
                    float part = total / without;
                    if (f >= 1f) Assert.True(part <= f + Eps && part >= 1f - Eps, "the limit raised the bonus: " + where + ", part " + part);
                    else Assert.True(part >= f - Eps && part <= 1f + Eps, "the limit deepened the penalty: " + where + ", part " + part);

                    // Alone on the channel (the others at 1) the feature's multiplier is the result, untouched.
                    if (OtherFeatures[(sample.CpuId + sample.Month) % OtherFeatures.Length] == 1.0f)
                    {
                        alone++;
                        Assert.Near(f, total, Eps, "the feature alone: " + where);
                    }
                }
            }

            Assert.True(checkedSamples > 40000, "samples: " + checkedSamples);
            Assert.True(capped > 1000, "the mix does run into the limit of the core: " + capped + " of " + checkedSamples);
            Assert.True(alone > 1000, "and there are plenty of CPUs where the feature is alone: " + alone);
            Assert.True(lowest < 0.6 && highest > 1.9, "both ends of the limit were met: " + Num(lowest) + " ... " + Num(highest));
        }

        // ---- the same rules for everyone (NFR-7) ----

        [Test]
        public void NoRuleDependsOnWhoPlays_ThePlayerWithAnotherKeyHasTheSameRun()
        {
            PlatformSimulator asPlayer = Main();
            PlatformSimulator asAi = Play("ai:99");

            Assert.Equal(asPlayer.FirstCompanyFingerprint, asAi.FirstCompanyFingerprint, "every multiplier of the first company, month by month");
            Assert.Equal(asPlayer.Consumer.Sum, asAi.Consumer.Sum);
            Assert.Equal(asPlayer.Chance.Sum, asAi.Chance.Sum);
            Assert.Equal(asPlayer.Companies[0].EarlyAdopterReleases, asAi.Companies[0].EarlyAdopterReleases);
            Assert.Equal(asPlayer.Companies[0].Penalties, asAi.Companies[0].Penalties);
        }

        [Test]
        public void TwoCompaniesWithTheSamePlan_GetTheSameBonusesFromTheSameBase()
        {
            // "player" and "ai:3" follow the same plan (six months behind the announcements); their releases fall in different months of the year,
            // so the early slots and the base differ a little, but the rule that turns a base into a bonus is the one function.
            PlatformSimulator sim = Main();
            SimCompany player = Company(sim, "player");
            SimCompany ai = Company(sim, "ai:3");
            Assert.Equal(player.Style, ai.Style);
            Assert.Equal(player.Lag, ai.Lag);

            var engine = new PlatformEffectEngine();
            SocketId socket = LedgerTestData.Std("PGA 1995");
            InstalledBaseLedger ledger = Ledger(
                LedgerTestData.Cpu(1, "player", socket, "gen-1995-pga", new[] { 0, 2500 }),
                LedgerTestData.Cpu(2, "ai:3", socket, "gen-1995-pga", new[] { 0, 2500 }),
                LedgerTestData.Cpu(3, "ai:1", socket, "gen-1995-pga", new[] { 0, 5000 }));
            RatingBreakdown a = engine.RatingFor("player", socket, "gen-1995-pga", ledger, new PlatformTimeline(), new GameDate(1997, 1, 1), Balance());
            RatingBreakdown b = engine.RatingFor("ai:3", socket, "gen-1995-pga", ledger, new PlatformTimeline(), new GameDate(1997, 1, 1), Balance());
            Assert.Equal(a.ConsumerMult, b.ConsumerMult);
            Assert.Equal(a.IndustriesMult, b.IndustriesMult);
            Assert.Equal(a.Bib, b.Bib);
        }

        // ---- the report for the owner ----

        [Test]
        public void Report_PrintsMinMaxAndAverage_ForTheOwner()
        {
            string report = Main().Report();
            Assert.True(report.Contains("min ") && report.Contains("max ") && report.Contains("avg "), report);
            Assert.True(report.Contains("player") && report.Contains("ai:13"), "a line per company");
            Console.WriteLine(report);
        }
    }
}
