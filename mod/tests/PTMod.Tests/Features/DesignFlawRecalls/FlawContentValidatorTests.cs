using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.DesignFlawRecalls;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-002 (FR-2, FR-3, FR-15, FR-20, NFR-7): the shipped <c>flaws.json</c> is the example of design.md and equals the defaults of the code; the validator
    /// checks every range of the "Security" section, keeps the default of a rejected value and never throws.
    /// </summary>
    [Covers(typeof(FlawContentValidator), typeof(FlawContentReport), typeof(FlawBalance), typeof(FlawTypeDef), typeof(AiPolicyDef))]
    public class FlawContentValidatorTests
    {
        // ---- the shipped file ----

        [Test]
        public void ShippedFile_IsReadWithoutAnyNote()
        {
            var report = new FlawContentReport();
            FlawBalance balance = FlawContentValidator.Parse(FlawTestKit.ShippedFlawsText(), report);

            Assert.NotNull(balance);
            Assert.True(report.IsClean, Dump(report));
            Assert.Equal(4, balance.Types.Count);
        }

        [Test]
        public void ShippedFile_EqualsTheDefaultsOfTheCode()
        {
            FlawBalance fromFile = FlawContentValidator.Parse(FlawTestKit.ShippedFlawsText(), new FlawContentReport());
            List<string> diffs = FlawTestKit.Diff(FlawBalance.Defaults(), fromFile);

            Assert.True(diffs.Count == 0, "the defaults of the code differ from flaws.json:\n  " + string.Join("\n  ", diffs));
        }

        [Test]
        public void ShippedFile_IsTheExampleOfTheDesign()
        {
            string block = FlawTestKit.DesignJsonBlock("Content/design-flaw-recalls/flaws.json");
            if (block == null) return;   // the spec is not next to the sources (a copy of the mod alone): nothing to compare with

            JsonNode design = FlawTestKit.ParseNode(block);
            JsonNode shipped = FlawTestKit.ShippedFlaws();
            Assert.True(design.Equals(shipped), "Content/design-flaw-recalls/flaws.json differs from the example in design.md (Data Models)");
        }

        [Test]
        public void ShippedFile_HasTheNewBlocks_AndNoAiCaution()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();

            Assert.Equal(1, root.GetInt32("schema"));
            Assert.True(root.GetObject("risk").ContainsKey("kRush"), "risk.kRush");
            Assert.True(root.ContainsKey("rush"), "rush");
            Assert.True(root.GetObject("responses").ContainsKey("pendingGraceDays"), "responses.pendingGraceDays");
            Assert.False(root.ContainsKey("aiCaution"), "the caution of the AI is led by rival-personalities, not by this feature");
            Assert.False(root.GetObject("aiPolicy").ContainsKey("aiCaution"), "aiPolicy.aiCaution");
        }

        [Test]
        public void ShippedFile_Values_AreTheDesignNumbers()
        {
            FlawBalance b = FlawContentValidator.Parse(FlawTestKit.ShippedFlawsText(), new FlawContentReport());

            Assert.Near(0.02, b.Risk.Base, 1e-6);
            Assert.Near(0.06, b.Risk.KRush, 1e-6);
            Assert.Near(0.5, b.Risk.PMax, 1e-6);
            Assert.Equal(6, b.Risk.DifficultyMultiplier.Length);
            Assert.Equal(3, b.Risk.Era.Count);
            Assert.Near(0.8, b.Risk.Era[0].Multiplier, 1e-6);
            Assert.Equal(new[] { 0.60f, 0.30f, 0.10f }, b.Severity.BaseWeights);
            Assert.Equal(new[] { 48, 36, 24 }, b.Discovery.MaxHiddenMonths);
            Assert.Equal(30, b.Responses.TimerDays);
            Assert.Equal(14, b.Responses.PendingGraceDays);
            Assert.Equal(new[] { 60, 90, 120 }, b.Responses.Stepping.Days);
            Assert.Equal(new[] { -2, -5, -10 }, b.Reputation.PublicBase);
            Assert.Equal(new[] { -8, -15, -15 }, b.Press.OverallAdjustment);
            Assert.Near(0.25, b.Rush.TimeCut, 1e-6);
            Assert.Near(1.10, b.Rush.CostMultiplier, 1e-6);
            Assert.Equal("security", b.Types[3].Id);
            Assert.Equal(2015, b.Types[3].FromYear);
            Assert.Equal(new[] { 0.30f, 0.30f, 0.25f, 0.15f }, b.AiPolicy.Public);
        }

        [Test]
        public void Defaults_AreANewObjectEveryTime()
        {
            FlawBalance a = FlawBalance.Defaults();
            FlawBalance b = FlawBalance.Defaults();
            a.Risk.PMax = 0.1f;
            a.Types[0].IpsLoss[0] = 0.4f;
            a.AiPolicy.Public[0] = 0.9f;
            a.Risk.DifficultyMultiplier[0] = 9f;

            Assert.Near(0.5, b.Risk.PMax, 1e-6);
            Assert.Near(0.03, b.Types[0].IpsLoss[0], 1e-6);
            Assert.Near(0.30, b.AiPolicy.Public[0], 1e-6);
            Assert.Near(0.5, b.Risk.DifficultyMultiplier[0], 1e-6);
        }

        [Test]
        public void Types_CloneSharesNothing()
        {
            FlawTypeDef type = FlawTypeDef.DefaultTypes()[1];
            FlawTypeDef copy = type.Clone();
            copy.IpsLoss[1] = 0.5f;

            Assert.Equal("stability", copy.Id);
            Assert.Near(0.12, type.IpsLoss[1], 1e-6);
        }

        // ---- the rules of the "Security" section: one test per rule ----

        [Test]
        public void Probabilities_AreInZeroToOne()
        {
            Rejects("risk.base", 1.5, "risk.base out of range [0,1]");
            Rejects("risk.base", -0.1, "risk.base out of range [0,1]");
            Rejects("risk.overclockCap", 2, "risk.overclockCap out of range [0,1]");
            Rejects("risk.unknownNodeImmaturity", 1.1, "risk.unknownNodeImmaturity out of range [0,1]");
            Rejects("discovery.hMax", 1.2, "discovery.hMax out of range [0,1]");
            Rejects("discovery.leakRate.ignore", 1.2, "discovery.leakRate.ignore out of range [0,1]");
            Rejects("discovery.leakRate.quietStepping", -1, "discovery.leakRate.quietStepping out of range [0,1]");
            Rejects("discovery.leakRate.tail", 3, "discovery.leakRate.tail out of range [0,1]");
            Rejects("responses.recall.stockReworkShare", 1.5, "responses.recall.stockReworkShare out of range [0,1]");
            Rejects("responses.recall.capShareOfRevenue12m", 1.5, "responses.recall.capShareOfRevenue12m out of range [0,1]");
            Rejects("responses.stepping.residualShareOfRecall", 2, "responses.stepping.residualShareOfRecall out of range [0,1]");
            Rejects("responses.patch.costShareOfDevCost", 1.5, "responses.patch.costShareOfDevCost out of range [0,1]");
            Rejects("responses.ignore.suitShareOfRevenue", 1.5, "responses.ignore.suitShareOfRevenue out of range [0,1]");
        }

        [Test]
        public void PMax_OutOfRange_LogsTheErrorAndKeepsHalf()
        {
            // AC-15.1: pMax = 3.0 gives "pMax out of range [0,1]" and 0.5.
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk.pMax", 3.0);
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(1, report.Errors.Count, Dump(report));
            Assert.True(report.Errors[0].Contains("pMax out of range [0,1]"), report.Errors[0]);
            Assert.Near(0.5, b.Risk.PMax, 1e-6);
        }

        [Test]
        public void Multipliers_AreInZeroToTen()
        {
            Rejects("risk.kFailureRate", 11, "risk.kFailureRate out of range [0,10]");
            Rejects("risk.kOverclock", -0.5, "risk.kOverclock out of range [0,10]");
            Rejects("risk.kThermal", 10.5, "risk.kThermal out of range [0,10]");
            Rejects("risk.kNodeImmaturity", 11, "risk.kNodeImmaturity out of range [0,10]");
            Rejects("risk.aiRiskMultiplier", -1, "risk.aiRiskMultiplier out of range [0,10]");
            Rejects("severity.shiftMajor", 11, "severity.shiftMajor out of range [0,10]");
            Rejects("severity.shiftCritical", 11, "severity.shiftCritical out of range [0,10]");
            Rejects("discovery.kUnits", 11, "discovery.kUnits out of range [0,10]");
            Rejects("reputation.coverUpMultiplier", 11, "reputation.coverUpMultiplier out of range [0,10]");
            Rejects("aiPolicy.recallRequiresHeadroomFactor", 11, "aiPolicy.recallRequiresHeadroomFactor out of range [0,10]");
        }

        [Test]
        public void Days_AreInOneToThreeThousandSixHundredFifty()
        {
            Rejects("responses.timerDays", 0, "responses.timerDays out of range [1,3650]");
            Rejects("responses.timerDays", 3651, "responses.timerDays out of range [1,3650]");
            RejectsArray("responses.stepping.days", "[0, 90, 120]", "responses.stepping.days[0] out of range [1,3650]");
            RejectsArray("responses.stepping.days", "[60, 90, 4000]", "responses.stepping.days[2] out of range [1,3650]");
        }

        [Test]
        public void Months_AreInOneToOneHundredTwenty()
        {
            RejectsArray("discovery.maxHiddenMonths", "[48, 0, 24]", "discovery.maxHiddenMonths[1] out of range [1,120]");
            RejectsArray("discovery.maxHiddenMonths", "[48, 36, 121]", "discovery.maxHiddenMonths[2] out of range [1,120]");
            Rejects("discovery.expireAfterRetiredMonths", 0, "discovery.expireAfterRetiredMonths out of range [1,120]");
            Rejects("discovery.expireNoSalesMonths", 121, "discovery.expireNoSalesMonths out of range [1,120]");
            Rejects("discovery.leakTailMonths", 0, "discovery.leakTailMonths out of range [1,120]");
            RejectsArray("responses.patch.months", "[12, 18, 200]", "responses.patch.months[2] out of range [1,120]");
            Rejects("responses.ignore.signChanceMonths", 0, "responses.ignore.signChanceMonths out of range [1,120]");
            Rejects("responses.ignore.drainMonths", 121, "responses.ignore.drainMonths out of range [1,120]");
        }

        [Test]
        public void PendingGraceDays_AreInZeroTo365()
        {
            Rejects("responses.pendingGraceDays", -1, "responses.pendingGraceDays out of range [0,365]");
            Rejects("responses.pendingGraceDays", 366, "responses.pendingGraceDays out of range [0,365]");

            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "responses.pendingGraceDays", 0);
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);
            Assert.True(report.IsClean, Dump(report));
            Assert.Equal(0, b.Responses.PendingGraceDays);
        }

        [Test]
        public void KRush_IsInZeroToOne()
        {
            Rejects("risk.kRush", 1.5, "risk.kRush out of range [0,1]");
            Rejects("risk.kRush", -0.01, "risk.kRush out of range [0,1]");

            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk.kRush", 0.2);
            FlawBalance b = FlawContentValidator.Validate(root, new FlawContentReport());
            Assert.Near(0.2, b.Risk.KRush, 1e-6);
        }

        [Test]
        public void RushTimeCut_IsInZeroToHalf()
        {
            Rejects("rush.timeCut", 0.6, "rush.timeCut out of range [0,0.5]");
            Rejects("rush.timeCut", -0.1, "rush.timeCut out of range [0,0.5]");
        }

        [Test]
        public void RushCostMultiplier_IsInOneToTwo()
        {
            Rejects("rush.costMultiplier", 0.9, "rush.costMultiplier out of range [1,2]");
            Rejects("rush.costMultiplier", 2.1, "rush.costMultiplier out of range [1,2]");
        }

        [Test]
        public void ArraysBySeverity_HaveExactlyThreeNumbers()
        {
            RejectsArray("severity.baseWeights", "[0.6, 0.3]", "severity.baseWeights must have exactly 3 numbers, got 2");
            RejectsArray("discovery.hBase", "[0.02, 0.04, 0.07, 0.1]", "discovery.hBase must have exactly 3 numbers, got 4");
            RejectsArray("discovery.qInternal", "[]", "discovery.qInternal must have exactly 3 numbers, got 0");
            RejectsArray("responses.recall.returnRate", "[0.1]", "responses.recall.returnRate must have exactly 3 numbers");
            RejectsArray("responses.stepping.costShareOfDevCost", "[0.1, 0.2]", "responses.stepping.costShareOfDevCost must have exactly 3 numbers");
            RejectsArray("responses.ignore.oemCancel", "[0.1, 0.2, 0.3, 0.4]", "responses.ignore.oemCancel must have exactly 3 numbers");
            RejectsArray("responses.ignore.classAction", "[0, 0, 0, 0]", "responses.ignore.classAction must have exactly 3 numbers");
            RejectsArray("responses.popHit.public", "[0.1, 0.2]", "responses.popHit.public must have exactly 3 numbers");
            RejectsArray("responses.popHit.recall", "[0.1, 0.2]", "responses.popHit.recall must have exactly 3 numbers");
            RejectsArray("responses.popHit.stepping", "[0.1, 0.2]", "responses.popHit.stepping must have exactly 3 numbers");
            RejectsArray("responses.popHit.patch", "[0.1, 0.2]", "responses.popHit.patch must have exactly 3 numbers");
            RejectsArray("responses.popHit.ignore", "[0.1, 0.2]", "responses.popHit.ignore must have exactly 3 numbers");
            RejectsArray("reputation.publicBase", "[-2, -5]", "reputation.publicBase must have exactly 3 numbers");
            RejectsArray("reputation.recall", "[1, 2, 4, 5]", "reputation.recall must have exactly 3 numbers");
            RejectsArray("press.overallAdjustment", "[-8, -15]", "press.overallAdjustment must have exactly 3 numbers");
        }

        [Test]
        public void ArraysBySeverity_KeepTheDefaultsWhenRejected()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "discovery.hBase", FlawTestKit.ParseNode("[0.02, 5, 0.07]"));
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(new[] { 0.02f, 0.04f, 0.07f }, b.Discovery.HBase);
            Assert.True(report.Errors.Any(e => e.Contains("discovery.hBase[1] out of range [0,1]")), Dump(report));
        }

        [Test]
        public void DifficultyMultiplier_HasExactlySixNumbersInZeroToTen()
        {
            RejectsArray("risk.difficultyMultiplier", "[0.5, 0.75, 1.0, 1.15, 1.3]", "risk.difficultyMultiplier must have exactly 6 numbers, got 5");
            RejectsArray("risk.difficultyMultiplier", "[0.5, 0.75, 1.0, 1.15, 1.3, 1.5, 2]", "risk.difficultyMultiplier must have exactly 6 numbers, got 7");
            RejectsArray("risk.difficultyMultiplier", "[0.5, 0.75, 1.0, 1.15, 1.3, 11]", "risk.difficultyMultiplier[5] out of range [0,10]");
            RejectsArray("risk.difficultyMultiplier", "[0.5, -1, 1.0, 1.15, 1.3, 1.5]", "risk.difficultyMultiplier[1] out of range [0,10]");
        }

        [Test]
        public void AiPolicyWeights_AreInZeroToOne()
        {
            Rejects("aiPolicy.public.recall", 1.5, "aiPolicy.public.recall out of range [0,1]");
            Rejects("aiPolicy.public.stepping", -0.1, "aiPolicy.public.stepping out of range [0,1]");
            Rejects("aiPolicy.internal.patch", 2, "aiPolicy.internal.patch out of range [0,1]");
            Rejects("aiPolicy.internal.ignore", 1.01, "aiPolicy.internal.ignore out of range [0,1]");
        }

        [Test]
        public void AiPolicyWeights_AreFourNamedNumbersPerSituation()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "aiPolicy.public", FlawTestKit.ParseNode("[0.3, 0.3, 0.25, 0.15]"));
            FlawTestKit.Set(root, "aiPolicy.internal.recal", 0.5);
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.True(report.Errors.Any(e => e.Contains("aiPolicy.public must be an object")), Dump(report));
            Assert.True(report.Warnings.Any(w => w.Contains("aiPolicy.internal.recal is not a known member")), Dump(report));
            Assert.Equal(new[] { 0.30f, 0.30f, 0.25f, 0.15f }, b.AiPolicy.Public);
        }

        [Test]
        public void AiPolicyWeights_AreReadInTheOrderOfTheBias()
        {
            const string json = "{ \"aiPolicy\": { \"public\": { \"recall\": 0.1, \"stepping\": 0.2, \"patch\": 0.3, \"ignore\": 0.4 }, " +
                                "\"internal\": { \"ignore\": 0.9 } } }";
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Parse(json, report);

            Assert.True(report.IsClean, Dump(report));
            Assert.Equal(new[] { 0.1f, 0.2f, 0.3f, 0.4f }, b.AiPolicy.Public);
            Assert.Equal(new[] { 0.15f, 0.45f, 0.15f, 0.9f }, b.AiPolicy.Internal);
        }

        [Test]
        public void SeverityWeights_AreNotAllZero()
        {
            RejectsArray("severity.baseWeights", "[0, 0, 0]", "severity.baseWeights must not be all zero");
            RejectsArray("severity.baseWeights", "[0.6, -0.3, 0.1]", "severity.baseWeights[1] out of range [0,10]");
        }

        [Test]
        public void Years_AreInTheRangeOfTheGame()
        {
            RejectsEra("[ { \"fromYear\": 1960, \"toYear\": 1984, \"multiplier\": 0.8 } ]", "risk.era[0].fromYear out of range [1971,2030]");
            RejectsEra("[ { \"fromYear\": 1985, \"toYear\": 2031, \"multiplier\": 1.0 } ]", "risk.era[0].toYear out of range [1971,2030]");
            RejectsType(0, "fromYear", 1970, "fromYear out of range [1971,2030]");
            RejectsType(0, "toYear", 2031, "toYear out of range [1971,2030]");
            RejectsType(0, "patchFromYear", 2031, "patchFromYear out of range [1971,2030]");
        }

        [Test]
        public void EraRows_AreReadAndCheckedOneByOne()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk.era", FlawTestKit.ParseNode(
                "[ { \"fromYear\": 1971, \"toYear\": 1990, \"multiplier\": 0.5 }," +
                "  { \"fromYear\": 1995, \"toYear\": 1990, \"multiplier\": 1.0 }," +
                "  { \"fromYear\": 1991, \"toYear\": 2030, \"multiplier\": 20 }," +
                "  7 ]"));
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(1, b.Risk.Era.Count);
            Assert.Equal(1990, b.Risk.Era[0].ToYear);
            Assert.True(report.Errors.Any(e => e.Contains("risk.era[1]: fromYear 1995 is after toYear 1990")), Dump(report));
            Assert.True(report.Errors.Any(e => e.Contains("risk.era[2].multiplier out of range [0,10]")), Dump(report));
            Assert.True(report.Errors.Any(e => e.Contains("risk.era[3] must be an object")), Dump(report));
            Assert.True(report.Errors.Any(e => e.Contains("risk.era[1] is skipped")), Dump(report));
        }

        [Test]
        public void EraThatIsNotAnArray_KeepsTheDefaultTable()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk.era", 5);
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(3, b.Risk.Era.Count);
            Assert.True(report.Errors.Any(e => e.Contains("risk.era must be an array of rows")), Dump(report));
        }

        [Test]
        public void SignChanceMultipliers_NeverGoBelowHalf()
        {
            // NFR-8 / TSK-014: the channel contract.signChance gets no factor below 0.5 from this feature.
            Rejects("responses.pendingSignChanceMultiplier", 0.4, "responses.pendingSignChanceMultiplier out of range [0.5,1]");
            Rejects("responses.pendingSignChanceMultiplier", 1.2, "responses.pendingSignChanceMultiplier out of range [0.5,1]");
            RejectsArray("responses.ignore.signChance", "[0.95, 0.85, 0.4]", "responses.ignore.signChance[2] out of range [0.5,1]");
        }

        [Test]
        public void IpsLoss_IsAtMostHalf()
        {
            // The multiplier of market.rating.cpu is 1 - ipsLoss, so it stays at 0.5 or more.
            RejectsTypeArray(1, "ipsLoss", "[0.08, 0.12, 0.6]", "ipsLoss[2] out of range [0,0.5]");
            RejectsTypeArray(1, "ipsLoss", "[0.08, -0.1, 0.1]", "ipsLoss[1] out of range [0,0.5]");

            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "types.1.ipsLoss", FlawTestKit.ParseNode("[0.08, 0.12, 0.5]"));
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);
            Assert.True(report.IsClean, Dump(report));
            Assert.Near(0.5, b.Types[1].IpsLoss[2], 1e-6);
        }

        [Test]
        public void ReputationPoints_AreWholeNumbersInMinusToPlusOneHundred()
        {
            RejectsArray("reputation.publicBase", "[-2, -5, -101]", "reputation.publicBase[2] out of range [-100,100]");
            RejectsArray("reputation.ignorePublic", "[-3, -8, 101]", "reputation.ignorePublic[2] out of range [-100,100]");
            RejectsArray("reputation.recall", "[1, 2.5, 4]", "reputation.recall[1] must be a whole number");
            RejectsArray("press.overallAdjustment", "[-8, -15, -21]", "press.overallAdjustment[2] out of range [-20,20]");
            Rejects("responses.ignore.drainPerMonth", 1, "responses.ignore.drainPerMonth out of range [-100,0]");
        }

        [Test]
        public void MoneyAndUnits_HaveSaneBounds()
        {
            Rejects("responses.recall.fixedCostBase", -1, "responses.recall.fixedCostBase out of range");
            Rejects("responses.patch.fixedCostBase", 1e12, "responses.patch.fixedCostBase out of range");
            Rejects("discovery.unitsRef", 0, "discovery.unitsRef out of range");
        }

        // ---- the types ----

        [Test]
        public void Type_WithAnotherPrefixInNameKey_IsSkipped()
        {
            RejectsType(0, "nameKey", "ms.type.arith", "does not belong to the feature");

            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "types.0.nameKey", "ms.type.arith");
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);
            Assert.Equal(3, b.Types.Count);
            Assert.False(b.Types.Any(t => t.Id == "arith"), "the type with a foreign key is gone");
            Assert.True(report.Errors.Any(e => e.Contains("types[0] (arith) is skipped")), Dump(report));
        }

        [Test]
        public void Type_NameKeyIsJudgedByTheGivenPredicate()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            var report = new FlawContentReport();
            var asked = new List<string>();
            FlawBalance b = FlawContentValidator.Validate(root, report, key =>
            {
                asked.Add(key);
                return key != "dfr.type.thermal";
            });

            Assert.Equal(3, b.Types.Count);
            Assert.False(b.Types.Any(t => t.Id == "thermal"));
            Assert.Equal(4, asked.Count);
        }

        [Test]
        public void Type_NameKeyMissingInTheTables_IsOnlyAWarning()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report, null, key => key != "dfr.type.security");

            Assert.Equal(4, b.Types.Count);
            Assert.Equal(0, report.Errors.Count);
            Assert.Equal(1, report.Warnings.Count);
            Assert.True(report.Warnings[0].Contains("dfr.type.security"), report.Warnings[0]);
        }

        [Test]
        public void Type_IdsMustBeUnique()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "types.2.id", "arith");
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(3, b.Types.Count);
            Assert.Equal(new[] { "arith", "stability", "security" }, b.Types.Select(t => t.Id).ToArray());
            Assert.True(report.Errors.Any(e => e.Contains("the id 'arith' is used by an earlier type")), Dump(report));
        }

        [Test]
        public void Type_IdMustBeASafeName()
        {
            RejectsType(0, "id", "bad id!", "is not a valid id");
            RejectsType(0, "id", "", "must be a non-empty string");
            RejectsType(0, "id", 5, "must be a non-empty string");
        }

        [Test]
        public void Type_RequiredMembers_MustBePresent()
        {
            foreach (string member in new[] { "id", "nameKey", "fromYear", "toYear", "weight", "ipsLoss" })
            {
                JsonObject root = FlawTestKit.ShippedFlaws();
                FlawTestKit.Remove(root, "types.0." + member);
                var report = new FlawContentReport();
                FlawBalance b = FlawContentValidator.Validate(root, report);

                Assert.Equal(3, b.Types.Count, member);
                Assert.True(report.Errors.Any(e => e.Contains(member + " is required")), member + ": " + Dump(report));
            }
        }

        [Test]
        public void Type_OptionalMembers_HaveDefaults()
        {
            const string json = "{ \"types\": [ { \"id\": \"quirk\", \"nameKey\": \"dfr.type.quirk\", \"fromYear\": 1980, \"toYear\": 1990, \"weight\": 1, \"ipsLoss\": [0.01, 0.02, 0.03] } ] }";
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Parse(json, report);

            Assert.True(report.IsClean, Dump(report));
            Assert.Equal(1, b.Types.Count);
            FlawTypeDef type = b.Types[0];
            Assert.Equal(0f, type.WeightByOverclock);
            Assert.Equal(0f, type.WeightByThermal);
            Assert.True(type.Patchable);
            Assert.True(type.Recallable);
            Assert.Equal(1980, type.PatchFromYear);
        }

        [Test]
        public void Type_WeightsAndYears_AreChecked()
        {
            RejectsType(0, "weight", 11, "weight out of range [0,10]");
            RejectsType(0, "weightByOverclock", -1, "weightByOverclock out of range [0,10]");
            RejectsType(0, "weightByThermal", 11, "weightByThermal out of range [0,10]");
            RejectsType(0, "patchable", "yes", "patchable must be true or false");
            RejectsType(0, "recallable", 1, "recallable must be true or false");
            RejectsType(0, "fromYear", 2020, "fromYear 2020 is after toYear 2010", "toYear", 2010);
        }

        [Test]
        public void Type_IpsLossArray_HasExactlyThreeNumbers()
        {
            RejectsTypeArray(0, "ipsLoss", "[0.03, 0.06]", "ipsLoss must have exactly 3 numbers, got 2");
            RejectsTypeArray(2, "ipsLoss", "[0.1, 0.1, 0.1, 0.1]", "ipsLoss must have exactly 3 numbers, got 4");
        }

        [Test]
        public void NoValidType_FallsBackToArith()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "types", FlawTestKit.ParseNode("[ { \"id\": \"x\" }, 5 ]"));
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(1, b.Types.Count);
            Assert.Equal("arith", b.Types[0].Id);
            Assert.True(report.Errors.Any(e => e.Contains("types has no valid entry")), Dump(report));
            Assert.True(report.Errors.Any(e => e.Contains("types[1] must be an object")), Dump(report));
        }

        [Test]
        public void EmptyTypeList_FallsBackToArith()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "types", new JsonArray());
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(1, b.Types.Count);
            Assert.Equal("arith", b.Types[0].Id);
            Assert.True(report.Errors.Any(e => e.Contains("types has no valid entry")), Dump(report));
        }

        [Test]
        public void TypesThatAreNotAnArray_KeepTheBuiltInTypes()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "types", "none");
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(4, b.Types.Count);
            Assert.True(report.Errors.Any(e => e.Contains("types must be an array of types")), Dump(report));
        }

        [Test]
        public void ANewValidType_IsAccepted()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            ((JsonArray)root["types"]).Add(FlawTestKit.ParseNode(
                "{ \"id\": \"fan-noise\", \"nameKey\": \"dfr.type.fanNoise\", \"fromYear\": 1995, \"toYear\": 2030, \"weight\": 1.5, \"weightByThermal\": 2, " +
                "\"patchable\": false, \"ipsLoss\": [0, 0, 0], \"recallable\": true }"));
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.True(report.IsClean, Dump(report));
            Assert.Equal(5, b.Types.Count);
            Assert.Equal("fan-noise", b.Types[4].Id);
            Assert.False(b.Types[4].Patchable);
            Assert.Near(1.5, b.Types[4].Weight, 1e-6);
        }

        // ---- shape of the file: wrong types, unknown members, partial overrides, broken text ----

        [Test]
        public void WrongJsonType_IsAnErrorAndTheDefaultStays()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk.pMax", "high");
            FlawTestKit.Set(root, "risk.base", true);
            FlawTestKit.Set(root, "responses.timerDays", 2.5);
            FlawTestKit.Set(root, "discovery.hBase", 3);
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Near(0.5, b.Risk.PMax, 1e-6);
            Assert.Near(0.02, b.Risk.Base, 1e-6);
            Assert.Equal(30, b.Responses.TimerDays);
            Assert.Equal(new[] { 0.02f, 0.04f, 0.07f }, b.Discovery.HBase);
            Assert.True(report.Errors.Any(e => e.Contains("risk.pMax must be a number, got a string")), Dump(report));
            Assert.True(report.Errors.Any(e => e.Contains("risk.base must be a number, got a boolean")), Dump(report));
            Assert.True(report.Errors.Any(e => e.Contains("responses.timerDays must be a whole number, got 2.5")), Dump(report));
            Assert.True(report.Errors.Any(e => e.Contains("discovery.hBase must be an array of 3 numbers, got a number")), Dump(report));
        }

        [Test]
        public void SectionThatIsNotAnObject_KeepsItsDefaults()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk", 5);
            FlawTestKit.Set(root, "responses.recall", FlawTestKit.ParseNode("[]"));
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Near(0.02, b.Risk.Base, 1e-6);
            Assert.Near(0.25, b.Responses.Recall.ReturnRate[1], 1e-6);
            Assert.True(report.Errors.Any(e => e.Contains("risk must be an object")), Dump(report));
            Assert.True(report.Errors.Any(e => e.Contains("responses.recall must be an object")), Dump(report));
        }

        [Test]
        public void UnknownMember_IsAWarning_AndIgnored()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk.pMaxx", 0.9);
            FlawTestKit.Set(root, "aiCaution", FlawTestKit.ParseNode("{ \"multiplier\": 0.85 }"));
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Validate(root, report);

            Assert.Equal(0, report.Errors.Count, Dump(report));
            Assert.Equal(2, report.Warnings.Count, Dump(report));
            Assert.True(report.Warnings.Any(w => w.Contains("risk.pMaxx is not a known member")), Dump(report));
            Assert.True(report.Warnings.Any(w => w.Contains("aiCaution is not a known member")), Dump(report));
            Assert.Near(0.5, b.Risk.PMax, 1e-6);
        }

        [Test]
        public void PartialOverride_ChangesOnlyWhatItNames()
        {
            const string json = "{ \"schema\": 1, \"risk\": { \"pMax\": 0.3 }, \"rush\": { \"timeCut\": 0.4 } }";
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Parse(json, report);

            Assert.True(report.IsClean, Dump(report));
            Assert.Near(0.3, b.Risk.PMax, 1e-6);
            Assert.Near(0.4, b.Rush.TimeCut, 1e-6);

            // everything else is the default
            FlawBalance expected = FlawBalance.Defaults();
            expected.Risk.PMax = 0.3f;
            expected.Rush.TimeCut = 0.4f;
            Assert.True(FlawTestKit.Diff(expected, b).Count == 0, string.Join("\n", FlawTestKit.Diff(expected, b)));
        }

        [Test]
        public void EmptyConfig_GivesTheDefaultsWithoutANote()
        {
            var report = new FlawContentReport();
            FlawBalance b = FlawContentValidator.Parse("{}", report);

            Assert.True(report.IsClean, Dump(report));
            Assert.True(FlawTestKit.Diff(FlawBalance.Defaults(), b).Count == 0);
        }

        [Test]
        public void OtherSchema_IsAWarning()
        {
            var report = new FlawContentReport();
            FlawContentValidator.Parse("{ \"schema\": 2 }", report);

            Assert.Equal(0, report.Errors.Count);
            Assert.True(report.Warnings.Any(w => w.Contains("schema 2")), Dump(report));
        }

        [Test]
        public void BrokenJson_GivesTheDefaultsAndAnError()
        {
            foreach (string text in new[] { "{ \"risk\": { \"pMax\": 0.3 ", "not json at all", "{\"a\":}", "" })
            {
                var report = new FlawContentReport();
                FlawBalance b = FlawContentValidator.Parse(text, report);

                Assert.NotNull(b, text);
                Assert.True(FlawTestKit.Diff(FlawBalance.Defaults(), b).Count == 0, "defaults expected for: " + text);
                Assert.Equal(1, report.Errors.Count, text);
                Assert.True(report.Errors[0].StartsWith("flaws.json is not valid JSON"), report.Errors[0]);
            }
        }

        [Test]
        public void BrokenJson_NamesLineAndColumn()
        {
            var report = new FlawContentReport();
            FlawContentValidator.Parse("{\n  \"risk\": {\n    \"pMax\": oops\n  }\n}", report);

            Assert.Equal(1, report.Errors.Count);
            Assert.True(report.Errors[0].Contains("line 3"), report.Errors[0]);
        }

        [Test]
        public void NotAnObject_OrNothing_GivesTheDefaults()
        {
            var report = new FlawContentReport();
            Assert.True(FlawTestKit.Diff(FlawBalance.Defaults(), FlawContentValidator.Parse("[1, 2]", report)).Count == 0);
            Assert.Equal(1, report.Errors.Count);
            Assert.True(report.Errors[0].Contains("must be a JSON object"), report.Errors[0]);

            Assert.True(FlawTestKit.Diff(FlawBalance.Defaults(), FlawContentValidator.Parse(null, new FlawContentReport())).Count == 0);
            Assert.True(FlawTestKit.Diff(FlawBalance.Defaults(), FlawContentValidator.Validate(null, new FlawContentReport())).Count == 0);
        }

        [Test]
        public void ReportCanBeOmitted()
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk.pMax", 3.0);
            FlawBalance b = FlawContentValidator.Validate(root, null);
            Assert.Near(0.5, b.Risk.PMax, 1e-6);
            Assert.NotNull(FlawContentValidator.Parse("garbage", null));
        }

        [Test]
        public void Garbage_NeverThrows_AndAlwaysGivesAUsableBalance()
        {
            string[] documents =
            {
                "{ \"risk\": null, \"severity\": [], \"discovery\": 1, \"responses\": \"x\", \"reputation\": true, \"press\": [1], \"types\": {}, \"aiPolicy\": null, \"rush\": 0 }",
                "{ \"risk\": { \"era\": [null, [], \"x\", {}], \"difficultyMultiplier\": [null, null, null, null, null, null] } }",
                "{ \"types\": [ null, [], { \"id\": null }, { \"id\": 1, \"nameKey\": 2, \"fromYear\": \"a\", \"ipsLoss\": {} } ] }",
                "{ \"responses\": { \"recall\": { \"returnRate\": [\"a\", true, null] }, \"stepping\": { \"days\": [1.5, 2, 3] }, \"ignore\": [] } }",
                "{ \"discovery\": { \"leakRate\": 5, \"maxHiddenMonths\": [1e10, -1e10, 0.1] } }",
            };

            foreach (string document in documents)
            {
                var report = new FlawContentReport();
                FlawBalance b = FlawContentValidator.Parse(document, report);

                Assert.NotNull(b, document);
                Assert.True(report.Errors.Count > 0, "an error was expected for: " + document);
                Assert.True(b.Types.Count >= 1, document);
                Assert.Equal(3, b.Severity.BaseWeights.Length);
                Assert.Equal(6, b.Risk.DifficultyMultiplier.Length);
                Assert.Equal(4, b.AiPolicy.Public.Length);
                Assert.True(b.Risk.PMax >= 0f && b.Risk.PMax <= 1f, document);
            }
        }

        // ---- the AI policy helper ----

        [Test]
        public void AiPolicy_WeightsFor_PicksPublicOrInternal()
        {
            AiPolicyDef policy = FlawBalance.Defaults().AiPolicy;

            Assert.Equal(new[] { 0.30f, 0.30f, 0.25f, 0.15f }, policy.WeightsFor(true));
            Assert.Equal(new[] { 0.15f, 0.45f, 0.15f, 0.25f }, policy.WeightsFor(false));
            Assert.Near(1.5, policy.RecallRequiresHeadroomFactor, 1e-6);
        }

        // ---- helpers ----

        private static void Rejects(string path, double value, string expectedError)
        {
            RejectsNode(path, value, expectedError);
        }

        private static void RejectsNode(string path, JsonNode value, string expectedError)
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, path, value);
            var report = new FlawContentReport();
            FlawBalance balance = FlawContentValidator.Validate(root, report);

            Assert.True(report.Errors.Any(e => e.Contains(expectedError)), "'" + expectedError + "' was expected for " + path + " = " + value + "\n" + Dump(report));
            // a rejected value is replaced by the default, so nothing else changes
            Assert.True(FlawTestKit.Diff(FlawBalance.Defaults(), balance).Count == 0,
                "the default must stay for " + path + ": " + string.Join("; ", FlawTestKit.Diff(FlawBalance.Defaults(), balance)));
        }

        private static void RejectsArray(string path, string arrayJson, string expectedError)
        {
            RejectsNode(path, FlawTestKit.ParseNode(arrayJson), expectedError);
        }

        private static void RejectsEra(string eraJson, string expectedError)
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            FlawTestKit.Set(root, "risk.era", FlawTestKit.ParseNode(eraJson));
            var report = new FlawContentReport();
            FlawBalance balance = FlawContentValidator.Validate(root, report);

            Assert.True(report.Errors.Any(e => e.Contains(expectedError)), "'" + expectedError + "' was expected\n" + Dump(report));
            Assert.Equal(0, balance.Risk.Era.Count, "the bad row is skipped");
        }

        private static void RejectsType(int index, string member, double value, string expectedError)
        {
            RejectsTypeNode(index, member, value, expectedError, null, null);
        }

        private static void RejectsType(int index, string member, string value, string expectedError)
        {
            RejectsTypeNode(index, member, value, expectedError, null, null);
        }

        private static void RejectsType(int index, string member, int value, string expectedError)
        {
            RejectsTypeNode(index, member, value, expectedError, null, null);
        }

        private static void RejectsType(int index, string member, int value, string expectedError, string otherMember, int otherValue)
        {
            RejectsTypeNode(index, member, value, expectedError, otherMember, otherValue);
        }

        private static void RejectsTypeArray(int index, string member, string arrayJson, string expectedError)
        {
            RejectsTypeNode(index, member, FlawTestKit.ParseNode(arrayJson), expectedError, null, null);
        }

        /// <summary>One member of one type is made bad: the type is skipped, the others stay, the error names the member.</summary>
        private static void RejectsTypeNode(int index, string member, JsonNode value, string expectedError, string otherMember, JsonNode otherValue)
        {
            JsonObject root = FlawTestKit.ShippedFlaws();
            JsonNode item;
            ((JsonArray)root["types"]).TryGetValue(index, out item);
            string typeId = ((JsonObject)item).GetString("id");

            FlawTestKit.Set(root, "types." + index + "." + member, value);
            if (otherMember != null) FlawTestKit.Set(root, "types." + index + "." + otherMember, otherValue);

            var report = new FlawContentReport();
            FlawBalance balance = FlawContentValidator.Validate(root, report);

            Assert.True(report.Errors.Any(e => e.Contains(expectedError)), "'" + expectedError + "' was expected for types[" + index + "]." + member + " " + Dump(report));
            Assert.Equal(3, balance.Types.Count, "the bad type is skipped (" + typeId + ")");
            Assert.False(balance.Types.Any(t => t.Id == typeId), "the bad type '" + typeId + "' must be gone");
            Assert.True(report.Errors.Any(e => e.Contains("types[" + index + "]") && e.Contains("is skipped")), Dump(report));
        }

        private static string Dump(FlawContentReport report)
        {
            return "errors: [" + string.Join(" | ", report.Errors) + "] warnings: [" + string.Join(" | ", report.Warnings) + "]";
        }
    }
}
