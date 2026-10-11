using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalAcquisitions;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// <c>AcquisitionsBalance</c> (TSK-002, FR-14, NFR-7): the shipped <c>balance.json</c> is the example of design.md and equals the defaults of the
    /// code; a field outside its range, of the wrong type or unknown is replaced by its default with one line for <c>ModLog</c>; <c>maxShare</c> of
    /// 0.5 or more is refused; stake options above <c>maxShare</c> are dropped; <c>rescueTargetDebtLimitShare</c> outside (-1; 0] becomes -0.5.
    /// </summary>
    [Covers(typeof(AcquisitionsBalance), typeof(ValuationConfig), typeof(BankruptDealConfig), typeof(DistressedDealConfig), typeof(HostileDealConfig),
        typeof(DealsConfig), typeof(StakeConfig), typeof(ReputationConfig))]
    public class AcquisitionsBalanceTests
    {
        private const double Tiny = 1e-12;

        // ---- helpers ----

        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            var obj = node as JsonObject;
            Assert.NotNull(obj, "test JSON must be an object");
            return obj;
        }

        private static AcquisitionsBalance Load(string json, List<string> problems)
        {
            return AcquisitionsBalance.FromJson(Parse(json), problems);
        }

        private static string ShippedText()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            string path = Path.Combine(TestEnv.SrcDir, "Content", "rival-acquisitions", "balance.json");
            Assert.True(File.Exists(path), "missing " + path);
            return File.ReadAllText(path);
        }

        /// <summary>Exactly one problem line, and it names the path.</summary>
        private static void OneProblem(List<string> problems, string pathPart)
        {
            Assert.Equal(1, problems.Count, "problems: " + string.Join(" | ", problems));
            Assert.True(problems[0].Contains(pathPart), "the problem should name '" + pathPart + "': " + problems[0]);
        }

        private static void AssertSameAsDefaults(AcquisitionsBalance b)
        {
            var d = AcquisitionsBalance.Defaults();
            Assert.Near(d.Valuation.KFab, b.Valuation.KFab, Tiny);
            Assert.Near(d.Valuation.KTech, b.Valuation.KTech, Tiny);
            Assert.Near(d.Valuation.KCpu, b.Valuation.KCpu, Tiny);
            Assert.Equal(d.Valuation.CpuIncomeMonths, b.Valuation.CpuIncomeMonths);
            Assert.Near(d.Valuation.KShare, b.Valuation.KShare, Tiny);
            Assert.Near(d.Valuation.FloorDebtLimitShare, b.Valuation.FloorDebtLimitShare, Tiny);
            Assert.Near(d.Valuation.BankruptSnapshotCpuFactor, b.Valuation.BankruptSnapshotCpuFactor, Tiny);
            Assert.Near(d.Valuation.RoundTo, b.Valuation.RoundTo, Tiny);
            Assert.Near(d.Deals.Bankrupt.Multiplier, b.Deals.Bankrupt.Multiplier, Tiny);
            Assert.Equal(d.Deals.Bankrupt.AssumeCash, b.Deals.Bankrupt.AssumeCash);
            Assert.Equal(d.Deals.Bankrupt.AuctionDays, b.Deals.Bankrupt.AuctionDays);
            Assert.Near(d.Deals.Distressed.Multiplier, b.Deals.Distressed.Multiplier, Tiny);
            Assert.Equal(d.Deals.Distressed.AssumeCash, b.Deals.Distressed.AssumeCash);
            Assert.Near(d.Deals.Hostile.Multiplier, b.Deals.Hostile.Multiplier, Tiny);
            Assert.Near(d.Deals.Hostile.BiggerTargetMultiplier, b.Deals.Hostile.BiggerTargetMultiplier, Tiny);
            Assert.Equal(d.Deals.Hostile.CooldownDays, b.Deals.Hostile.CooldownDays);
            Assert.Equal(d.Deals.Hostile.AssumeCash, b.Deals.Hostile.AssumeCash);
            Assert.Near(d.Deals.MinCashAfter, b.Deals.MinCashAfter, Tiny);
            Assert.Equal(d.Stake.Options, b.Stake.Options);
            Assert.Near(d.Stake.MaxShare, b.Stake.MaxShare, Tiny);
            Assert.Near(d.Stake.HealthyMultiplier, b.Stake.HealthyMultiplier, Tiny);
            Assert.Near(d.Stake.DistressedMultiplier, b.Stake.DistressedMultiplier, Tiny);
            Assert.Near(d.Stake.PayoutRatio, b.Stake.PayoutRatio, Tiny);
            Assert.Near(d.Stake.RescueTargetDebtLimitShare, b.Stake.RescueTargetDebtLimitShare, Tiny);
            Assert.Equal(d.Reputation.Hostile, b.Reputation.Hostile);
            Assert.Equal(d.Reputation.Rescue, b.Reputation.Rescue);
            Assert.Equal(d.Reputation.Distressed, b.Reputation.Distressed);
            Assert.Equal(d.Reputation.Bankrupt, b.Reputation.Bankrupt);
            Assert.Equal(d.TechUnlockMaxPasses, b.TechUnlockMaxPasses);
        }

        // ---- the shipped file ----

        [Test]
        public void ShippedFile_IsTheExampleOfTheDesign_AndLoadsWithoutProblems()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load(ShippedText(), problems);

            Assert.Equal(0, problems.Count, "problems: " + string.Join(" | ", problems));

            Assert.Near(2.0, b.Valuation.KFab);
            Assert.Near(0.5, b.Valuation.KTech);
            Assert.Near(1.0, b.Valuation.KCpu);
            Assert.Equal(12, b.Valuation.CpuIncomeMonths);
            Assert.Near(1.0, b.Valuation.KShare);
            Assert.Near(0.25, b.Valuation.FloorDebtLimitShare);
            Assert.Near(0.5, b.Valuation.BankruptSnapshotCpuFactor);
            Assert.Near(1000, b.Valuation.RoundTo);

            Assert.Near(0.4, b.Deals.Bankrupt.Multiplier);
            Assert.False(b.Deals.Bankrupt.AssumeCash);
            Assert.Equal(365, b.Deals.Bankrupt.AuctionDays);
            Assert.Near(0.7, b.Deals.Distressed.Multiplier);
            Assert.True(b.Deals.Distressed.AssumeCash);
            Assert.Near(1.5, b.Deals.Hostile.Multiplier);
            Assert.Near(1.75, b.Deals.Hostile.BiggerTargetMultiplier);
            Assert.Equal(365, b.Deals.Hostile.CooldownDays);
            Assert.True(b.Deals.Hostile.AssumeCash);
            Assert.Near(0, b.Deals.MinCashAfter);

            Assert.Equal(new[] { 0.10, 0.25, 0.49 }, b.Stake.Options);
            Assert.Near(0.49, b.Stake.MaxShare);
            Assert.Near(1.0, b.Stake.HealthyMultiplier);
            Assert.Near(0.6, b.Stake.DistressedMultiplier);
            Assert.Near(0.5, b.Stake.PayoutRatio);
            Assert.Near(-0.5, b.Stake.RescueTargetDebtLimitShare);
            Assert.True(b.Stake.StakesEnabled);

            Assert.Equal(-5, b.Reputation.Hostile);
            Assert.Equal(3, b.Reputation.Rescue);
            Assert.Equal(0, b.Reputation.Distressed);
            Assert.Equal(0, b.Reputation.Bankrupt);
            Assert.Equal(20, b.TechUnlockMaxPasses);
        }

        [Test]
        public void ShippedFile_EqualsTheDefaultsOfTheCode()
        {
            AssertSameAsDefaults(Load(ShippedText(), null));
        }

        [Test]
        public void ShippedFile_GoesThroughTheContentMerger_AndAnOverrideMergesFieldByField()
        {
            string text = ShippedText();
            ConfigLoad plain = ContentMerger.MergeConfig("rival-acquisitions", "balance.json", text, null, AcquisitionsBalance.SchemaVersion);
            Assert.Equal(0, plain.Report.Errors.Count, "report: " + string.Join("; ", plain.Report.Errors));
            AssertSameAsDefaults(AcquisitionsBalance.FromJson(plain.Config, null));

            const string over = "{ \"schema\": 1, \"deals\": { \"hostile\": { \"multiplier\": 2.0 } }, \"stake\": { \"options\": [0.2] } }";
            ConfigLoad merged = ContentMerger.MergeConfig("rival-acquisitions", "balance.json", text, over, AcquisitionsBalance.SchemaVersion);
            Assert.True(merged.Report.OverrideApplied);

            var problems = new List<string>();
            AcquisitionsBalance b = AcquisitionsBalance.FromJson(merged.Config, problems);
            Assert.Equal(0, problems.Count, "problems: " + string.Join(" | ", problems));
            Assert.Near(2.0, b.Deals.Hostile.Multiplier);
            Assert.Near(1.75, b.Deals.Hostile.BiggerTargetMultiplier, Tiny, "the rest of the section stays");
            Assert.Equal(365, b.Deals.Hostile.CooldownDays);
            Assert.Equal(new[] { 0.2 }, b.Stake.Options);
            Assert.Near(0.7, b.Deals.Distressed.Multiplier);
        }

        [Test]
        public void Defaults_AreIndependentObjects()
        {
            AcquisitionsBalance a = AcquisitionsBalance.Defaults();
            AcquisitionsBalance b = AcquisitionsBalance.Defaults();
            a.Deals.Hostile.Multiplier = 9;
            a.Stake.Options[0] = 0.05;

            Assert.Near(1.5, b.Deals.Hostile.Multiplier);
            Assert.Near(0.10, b.Stake.Options[0]);
        }

        // ---- missing and broken input ----

        [Test]
        public void NullConfig_EmptyObject_AndMissingSections_GiveDefaults()
        {
            var problems = new List<string>();
            AssertSameAsDefaults(AcquisitionsBalance.FromJson(null, problems));
            AssertSameAsDefaults(Load("{}", problems));
            AssertSameAsDefaults(Load("{ \"schema\": 1, \"valuation\": {}, \"deals\": { \"hostile\": {} }, \"stake\": {} }", problems));
            Assert.Equal(0, problems.Count, "problems: " + string.Join(" | ", problems));
        }

        [Test]
        public void ProblemsList_MayBeNull()
        {
            AcquisitionsBalance b = Load("{ \"valuation\": { \"kFab\": 99, \"kTech\": \"x\" }, \"stake\": { \"maxShare\": 0.7 }, \"extra\": 1 }", null);
            Assert.Near(2.0, b.Valuation.KFab);
            Assert.Near(0.5, b.Valuation.KTech);
            Assert.Near(0.49, b.Stake.MaxShare);
        }

        [Test]
        public void ValuationFields_OutsideTheirRange_AreReplacedByDefaults()
        {
            var cases = new[]
            {
                new { Json = "{ \"valuation\": { \"kFab\": 10.5 } }", Path = "balance.valuation.kFab" },
                new { Json = "{ \"valuation\": { \"kTech\": -0.1 } }", Path = "balance.valuation.kTech" },
                new { Json = "{ \"valuation\": { \"kCpu\": 11 } }", Path = "balance.valuation.kCpu" },
                new { Json = "{ \"valuation\": { \"cpuIncomeMonths\": 0 } }", Path = "balance.valuation.cpuIncomeMonths" },
                new { Json = "{ \"valuation\": { \"cpuIncomeMonths\": 61 } }", Path = "balance.valuation.cpuIncomeMonths" },
                new { Json = "{ \"valuation\": { \"kShare\": -1 } }", Path = "balance.valuation.kShare" },
                new { Json = "{ \"valuation\": { \"floorDebtLimitShare\": 10.01 } }", Path = "balance.valuation.floorDebtLimitShare" },
                new { Json = "{ \"valuation\": { \"bankruptSnapshotCpuFactor\": 1.5 } }", Path = "balance.valuation.bankruptSnapshotCpuFactor" },
                new { Json = "{ \"valuation\": { \"roundTo\": 0 } }", Path = "balance.valuation.roundTo" },
                new { Json = "{ \"valuation\": { \"roundTo\": 2000000 } }", Path = "balance.valuation.roundTo" },
            };

            foreach (var c in cases)
            {
                var problems = new List<string>();
                AcquisitionsBalance b = Load(c.Json, problems);
                OneProblem(problems, c.Path);
                AssertSameAsDefaults(b);
            }
        }

        [Test]
        public void ValuationFields_AtTheEdgesOfTheirRange_AreAccepted()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load(
                "{ \"valuation\": { \"kFab\": 0, \"kTech\": 10, \"cpuIncomeMonths\": 60, \"floorDebtLimitShare\": 0, \"bankruptSnapshotCpuFactor\": 1, \"roundTo\": 1000000 } }", problems);

            Assert.Equal(0, problems.Count, "problems: " + string.Join(" | ", problems));
            Assert.Near(0, b.Valuation.KFab);
            Assert.Near(10, b.Valuation.KTech);
            Assert.Equal(60, b.Valuation.CpuIncomeMonths);
            Assert.Near(0, b.Valuation.FloorDebtLimitShare);
            Assert.Near(1, b.Valuation.BankruptSnapshotCpuFactor);
            Assert.Near(1000000, b.Valuation.RoundTo);
        }

        [Test]
        public void DealFields_OutsideTheirRange_AreReplacedByDefaults()
        {
            var cases = new[]
            {
                new { Json = "{ \"deals\": { \"bankrupt\": { \"multiplier\": 10.1 } } }", Path = "balance.deals.bankrupt.multiplier" },
                new { Json = "{ \"deals\": { \"bankrupt\": { \"auctionDays\": 3651 } } }", Path = "balance.deals.bankrupt.auctionDays" },
                new { Json = "{ \"deals\": { \"bankrupt\": { \"auctionDays\": -1 } } }", Path = "balance.deals.bankrupt.auctionDays" },
                new { Json = "{ \"deals\": { \"distressed\": { \"multiplier\": -0.5 } } }", Path = "balance.deals.distressed.multiplier" },
                new { Json = "{ \"deals\": { \"hostile\": { \"multiplier\": 11 } } }", Path = "balance.deals.hostile.multiplier" },
                new { Json = "{ \"deals\": { \"hostile\": { \"biggerTargetMultiplier\": -1 } } }", Path = "balance.deals.hostile.biggerTargetMultiplier" },
                new { Json = "{ \"deals\": { \"hostile\": { \"cooldownDays\": 4000 } } }", Path = "balance.deals.hostile.cooldownDays" },
                new { Json = "{ \"deals\": { \"minCashAfter\": -1 } }", Path = "balance.deals.minCashAfter" },
            };

            foreach (var c in cases)
            {
                var problems = new List<string>();
                AcquisitionsBalance b = Load(c.Json, problems);
                OneProblem(problems, c.Path);
                AssertSameAsDefaults(b);
            }
        }

        [Test]
        public void StakeAndReputationFields_OutsideTheirRange_AreReplacedByDefaults()
        {
            var cases = new[]
            {
                new { Json = "{ \"stake\": { \"healthyMultiplier\": 11 } }", Path = "balance.stake.healthyMultiplier" },
                new { Json = "{ \"stake\": { \"distressedMultiplier\": -0.1 } }", Path = "balance.stake.distressedMultiplier" },
                new { Json = "{ \"stake\": { \"payoutRatio\": 1.01 } }", Path = "balance.stake.payoutRatio" },
                new { Json = "{ \"reputation\": { \"hostile\": -101 } }", Path = "balance.reputation.hostile" },
                new { Json = "{ \"reputation\": { \"rescue\": 101 } }", Path = "balance.reputation.rescue" },
                new { Json = "{ \"reputation\": { \"distressed\": -2.5 } }", Path = "balance.reputation.distressed" },
                new { Json = "{ \"techUnlockMaxPasses\": 0 }", Path = "balance.techUnlockMaxPasses" },
                new { Json = "{ \"techUnlockMaxPasses\": 101 }", Path = "balance.techUnlockMaxPasses" },
            };

            foreach (var c in cases)
            {
                var problems = new List<string>();
                AcquisitionsBalance b = Load(c.Json, problems);
                OneProblem(problems, c.Path);
                AssertSameAsDefaults(b);
            }
        }

        [Test]
        public void WrongTypes_KeepTheDefault_AndSayWhat()
        {
            var cases = new[]
            {
                new { Json = "{ \"valuation\": { \"kFab\": \"two\" } }", Path = "balance.valuation.kFab" },
                new { Json = "{ \"valuation\": { \"cpuIncomeMonths\": 12.5 } }", Path = "balance.valuation.cpuIncomeMonths" },
                new { Json = "{ \"deals\": { \"bankrupt\": { \"assumeCash\": 1 } } }", Path = "balance.deals.bankrupt.assumeCash" },
                new { Json = "{ \"deals\": { \"hostile\": { \"cooldownDays\": true } } }", Path = "balance.deals.hostile.cooldownDays" },
                new { Json = "{ \"stake\": { \"maxShare\": [0.4] } }", Path = "balance.stake.maxShare" },
                new { Json = "{ \"stake\": { \"options\": 0.25 } }", Path = "balance.stake.options" },
                new { Json = "{ \"valuation\": [1, 2] }", Path = "balance.valuation" },
                new { Json = "{ \"deals\": 5 }", Path = "balance.deals" },
                new { Json = "{ \"deals\": { \"distressed\": \"x\" } }", Path = "balance.deals.distressed" },
                new { Json = "{ \"reputation\": { \"hostile\": \"low\" } }", Path = "balance.reputation.hostile" },
                new { Json = "{ \"techUnlockMaxPasses\": \"20\" }", Path = "balance.techUnlockMaxPasses" },
            };

            foreach (var c in cases)
            {
                var problems = new List<string>();
                AcquisitionsBalance b = Load(c.Json, problems);
                OneProblem(problems, c.Path);
                AssertSameAsDefaults(b);
            }
        }

        [Test]
        public void JsonNull_CountsAsAbsent()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"valuation\": { \"kFab\": null }, \"stake\": { \"maxShare\": null, \"options\": null }, \"deals\": null }", problems);

            Assert.Equal(0, problems.Count, "problems: " + string.Join(" | ", problems));
            AssertSameAsDefaults(b);
        }

        [Test]
        public void UnknownMembers_AreReportedAndIgnored()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"schema\": 1, \"extra\": 1, \"valuation\": { \"kFabb\": 3 }, \"deals\": { \"hostile\": { \"mult\": 2 } } }", problems);

            Assert.Equal(3, problems.Count, "problems: " + string.Join(" | ", problems));
            Assert.True(problems.Any(p => p.StartsWith("balance.extra")));
            Assert.True(problems.Any(p => p.StartsWith("balance.valuation.kFabb")));
            Assert.True(problems.Any(p => p.StartsWith("balance.deals.hostile.mult")));
            AssertSameAsDefaults(b);
        }

        [Test]
        public void AFieldGoneWrong_DoesNotSpoilItsNeighbours()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"valuation\": { \"kFab\": 99, \"kTech\": 3.5, \"roundTo\": 500 }, \"techUnlockMaxPasses\": 7 }", problems);

            OneProblem(problems, "balance.valuation.kFab");
            Assert.Near(2.0, b.Valuation.KFab);
            Assert.Near(3.5, b.Valuation.KTech);
            Assert.Near(500, b.Valuation.RoundTo);
            Assert.Equal(7, b.TechUnlockMaxPasses);
        }

        // ---- stake: maxShare, options, rescue target ----

        [Test]
        public void MaxShare_OfHalfOrMore_IsRefused()
        {
            foreach (string value in new[] { "0.5", "0.51", "0.75", "1", "1.5" })
            {
                var problems = new List<string>();
                AcquisitionsBalance b = Load("{ \"stake\": { \"maxShare\": " + value + " } }", problems);

                OneProblem(problems, "balance.stake.maxShare");
                Assert.Near(0.49, b.Stake.MaxShare, Tiny, "maxShare " + value);
                Assert.Equal(new[] { 0.10, 0.25, 0.49 }, b.Stake.Options, "maxShare " + value);
            }
        }

        [Test]
        public void MaxShare_BelowHalf_IsAccepted_AndLimitsTheOptions()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"stake\": { \"maxShare\": 0.499 } }", problems);
            Assert.Equal(0, problems.Count, "problems: " + string.Join(" | ", problems));
            Assert.Near(0.499, b.Stake.MaxShare, Tiny);
            Assert.Equal(new[] { 0.10, 0.25, 0.49 }, b.Stake.Options);

            problems.Clear();
            b = Load("{ \"stake\": { \"maxShare\": 0.3 } }", problems);
            Assert.Equal(0, problems.Count, "the default options above maxShare are not offered, no problem: " + string.Join(" | ", problems));
            Assert.Equal(new[] { 0.10, 0.25 }, b.Stake.Options);
        }

        [Test]
        public void MaxShare_Zero_DisablesTheStakes()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"stake\": { \"maxShare\": 0 } }", problems);

            Assert.Near(0, b.Stake.MaxShare);
            Assert.Equal(0, b.Stake.Options.Length);
            Assert.False(b.Stake.StakesEnabled);
        }

        [Test]
        public void Options_AboveMaxShare_AreDropped()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"stake\": { \"maxShare\": 0.3, \"options\": [0.1, 0.25, 0.49] } }", problems);

            OneProblem(problems, "balance.stake.options[2]");
            Assert.Equal(new[] { 0.10, 0.25 }, b.Stake.Options);
            Assert.True(b.Stake.StakesEnabled);

            // The default maxShare 0.49 limits the options too.
            problems.Clear();
            b = Load("{ \"stake\": { \"options\": [0.1, 0.5, 0.6, 1] } }", problems);
            Assert.Equal(3, problems.Count, "problems: " + string.Join(" | ", problems));
            Assert.Equal(new[] { 0.10 }, b.Stake.Options);
        }

        [Test]
        public void Options_AreSortedDeduplicatedAndCleaned()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"stake\": { \"options\": [0.4, 0.1, 0.25, 0.1, 0, -0.2, \"x\"] } }", problems);

            Assert.Equal(4, problems.Count, "the repeat, 0, -0.2 and the text: " + string.Join(" | ", problems));
            Assert.Equal(new[] { 0.10, 0.25, 0.40 }, b.Stake.Options);
        }

        [Test]
        public void Options_NoneUsable_FallBackToTheDefaultsUpToMaxShare()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"stake\": { \"maxShare\": 0.3, \"options\": [0.6, 0.9] } }", problems);

            Assert.Equal(3, problems.Count, "two options dropped and the fallback: " + string.Join(" | ", problems));
            Assert.True(problems[2].Contains("balance.stake.options"));
            Assert.Equal(new[] { 0.10, 0.25 }, b.Stake.Options);
        }

        [Test]
        public void Options_AnExplicitEmptyList_MeansNoStakes()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load("{ \"stake\": { \"options\": [] } }", problems);

            Assert.Equal(0, problems.Count, "problems: " + string.Join(" | ", problems));
            Assert.Equal(0, b.Stake.Options.Length);
            Assert.False(b.Stake.StakesEnabled);
        }

        [Test]
        public void RescueTargetDebtLimitShare_OutsideMinusOneToZero_BecomesMinusHalf()
        {
            foreach (string value in new[] { "-1.5", "-1", "-1.0001", "0.2", "1", "-100" })
            {
                var problems = new List<string>();
                AcquisitionsBalance b = Load("{ \"stake\": { \"rescueTargetDebtLimitShare\": " + value + " } }", problems);

                OneProblem(problems, "balance.stake.rescueTargetDebtLimitShare");
                Assert.Near(-0.5, b.Stake.RescueTargetDebtLimitShare, Tiny, "value " + value);
            }
        }

        [Test]
        public void RescueTargetDebtLimitShare_InsideMinusOneToZero_IsKept()
        {
            foreach (string value in new[] { "0", "-0.5", "-0.2", "-0.999", "-0.0001" })
            {
                var problems = new List<string>();
                AcquisitionsBalance b = Load("{ \"stake\": { \"rescueTargetDebtLimitShare\": " + value + " } }", problems);

                Assert.Equal(0, problems.Count, "value " + value + ": " + string.Join(" | ", problems));
                Assert.Near(double.Parse(value, System.Globalization.CultureInfo.InvariantCulture), b.Stake.RescueTargetDebtLimitShare, Tiny);
            }
        }

        [Test]
        public void ValidOverrides_ChangeTheNumbers()
        {
            var problems = new List<string>();
            AcquisitionsBalance b = Load(@"{
                ""schema"": 1,
                ""valuation"": { ""kFab"": 3, ""cpuIncomeMonths"": 24, ""roundTo"": 5000 },
                ""deals"": { ""bankrupt"": { ""assumeCash"": true, ""auctionDays"": 180 }, ""hostile"": { ""cooldownDays"": 90 }, ""minCashAfter"": 1000000 },
                ""stake"": { ""options"": [0.05, 0.2], ""maxShare"": 0.3, ""payoutRatio"": 0.25 },
                ""reputation"": { ""hostile"": -10, ""bankrupt"": 1 },
                ""techUnlockMaxPasses"": 40 }", problems);

            Assert.Equal(0, problems.Count, "problems: " + string.Join(" | ", problems));
            Assert.Near(3, b.Valuation.KFab);
            Assert.Equal(24, b.Valuation.CpuIncomeMonths);
            Assert.Near(5000, b.Valuation.RoundTo);
            Assert.True(b.Deals.Bankrupt.AssumeCash);
            Assert.Equal(180, b.Deals.Bankrupt.AuctionDays);
            Assert.Equal(90, b.Deals.Hostile.CooldownDays);
            Assert.Near(1000000, b.Deals.MinCashAfter);
            Assert.Equal(new[] { 0.05, 0.2 }, b.Stake.Options);
            Assert.Near(0.3, b.Stake.MaxShare);
            Assert.Near(0.25, b.Stake.PayoutRatio);
            Assert.Equal(-10, b.Reputation.Hostile);
            Assert.Equal(1, b.Reputation.Bankrupt);
            Assert.Equal(40, b.TechUnlockMaxPasses);
        }

        [Test]
        public void FromJson_NeverThrows_OnOddInput()
        {
            string[] inputs =
            {
                "{ \"valuation\": { \"kFab\": 1e400 } }",
                "{ \"stake\": { \"options\": [[], {}, null, true] } }",
                "{ \"stake\": { \"options\": [1e999, -1e999] } }",
                "{ \"deals\": { \"bankrupt\": [], \"hostile\": null, \"distressed\": \"\" } }",
                "{ \"reputation\": { \"hostile\": 2147483648 } }",
            };

            foreach (string text in inputs)
            {
                JsonNode node;
                JsonError error;
                if (!ModJson.TryParse(text, out node, out error)) continue;   // the parser may refuse a literal; that is its business

                var problems = new List<string>();
                AcquisitionsBalance b = AcquisitionsBalance.FromJson(node as JsonObject, problems);
                Assert.NotNull(b, text);
                Assert.True(b.Stake.MaxShare < 0.5, text);
                Assert.True(b.Stake.RescueTargetDebtLimitShare > -1.0 && b.Stake.RescueTargetDebtLimitShare <= 0.0, text);
            }
        }
    }
}
