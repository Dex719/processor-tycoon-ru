using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-002 (FR-12 item 1, NFR-7): <c>balance.json</c> is the example of design.md, the typed form reads it, and every field outside its range
    /// goes back to its default with one line for the log (AC-12.1); the arrays per difficulty level are all or nothing.
    /// </summary>
    [Covers(typeof(RecoveryBalance), typeof(LimitsConfig), typeof(PreWarningConfig), typeof(EarlyExitConfig), typeof(StartupCreditConfig),
        typeof(BridgeConfig), typeof(LoanTermsConfig), typeof(InvestorConfig), typeof(BailoutConfig), typeof(AssetsConfig))]
    public class RecoveryBalanceTests
    {
        // One row of the table of the fields (design.md, "Контент: balance.json").
        private sealed class Rule
        {
            public string Path;
            public double Min;
            public double Max;
            public bool IsInt;
        }

        private static Rule R(string path, double min, double max, bool isInt = false)
        {
            return new Rule { Path = path, Min = min, Max = max, IsInt = isInt };
        }

        // Every scalar number of the file with the range of the table.
        private static readonly Rule[] Rules =
        {
            R("limits.floorBase", 0, 1e9),
            R("preWarning.thresholdDebtLimitShare", 0, 10),
            R("preWarning.resetDebtLimitShare", 0, 10),
            R("earlyExit.streakDays", 1, 120, true),
            R("startupCredit.years", 1, 10, true),
            R("startupCredit.revenueShare", 0, 1),
            R("startupCredit.capDebtLimitShare", 0, 10),
            R("startupCredit.annualRate", 0, 2),
            R("startupCredit.autoDrawTriggerShare", 0, 10),
            R("startupCredit.autoDrawTargetShare", 0, 10),
            R("startupCredit.sweepShare", 0, 1),
            R("startupCredit.termMonths", 1, 120, true),
            R("startupCredit.termAnnualRate", 0, 2),
            R("bridgeLoan.eligibleDebtLimitShare", 0, 10),
            R("bridgeLoan.baseDebtLimitShare", 0, 10),
            R("bridgeLoan.revenueShare", 0, 1),
            R("bridgeLoan.fee", 0, 1),
            R("bridgeLoan.annualRate", 0, 2),
            R("bridgeLoan.months", 1, 120, true),
            R("bridgeLoan.cooldownDays", 0, 3650, true),
            R("bailout.targetDebtLimitShare", 0, 10),
            R("bailout.loan.annualRate", 0, 2),
            R("bailout.loan.months", 1, 120, true),
            R("bailout.investor.profitShare", 0, 1),
            R("bailout.investor.months", 1, 120, true),
            R("bailout.investor.capMultiple", 0, 100),
            R("bailout.reputationDelta", -100, 100, true),
            R("assets.minLinesLeft", 1, 100, true),
        };

        private static JsonNode Number(Rule rule, double value)
        {
            return rule.IsInt ? (JsonNode)(int)value : (JsonNode)value;
        }

        private static RecoveryBalance Read(JsonObject config, out List<string> problems)
        {
            problems = new List<string>();
            return RecoveryBalance.FromJson(config, problems);
        }

        private static void AssertIsDefaults(RecoveryBalance balance, string message = null)
        {
            JsonNode actual = ModJson.From(balance);
            JsonNode expected = ModJson.From(RecoveryBalance.Defaults());
            Assert.True(expected.Equals(actual), (message ?? "expected the defaults") + ": " + actual);
        }

        // ---- the shipped file ----

        [Test]
        public void ShippedFile_IsTheExampleOfTheDesign()
        {
            JsonObject config = RecoveryTestData.ParseObject(RecoveryTestData.ReadContent("balance.json"));

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);
            Assert.Equal(0, problems.Count, "the shipped balance has problems: " + string.Join("; ", problems));

            Assert.Equal(1, config.GetInt32("schema"), "schema of the content file");
            Assert.Equal(RecoveryBalance.Schema, balance.schema);

            Assert.Equal(2000000.0, balance.limits.floorBase);
            Assert.Equal(0.75, balance.preWarning.thresholdDebtLimitShare);
            Assert.Equal(0.5, balance.preWarning.resetDebtLimitShare);
            Assert.Equal(7, balance.earlyExit.streakDays);

            StartupCreditConfig sc = balance.startupCredit;
            Assert.Equal(3, sc.years);
            Assert.Equal(new[] { 1.5, 1.25, 1.0, 0.75, 0.5, 0.25 }, sc.baseDebtLimitShareByDifficulty);
            Assert.Equal(0.5, sc.revenueShare);
            Assert.Equal(3.0, sc.capDebtLimitShare);
            Assert.Equal(0.10, sc.annualRate);
            Assert.Equal(0.9, sc.autoDrawTriggerShare);
            Assert.Equal(0.6, sc.autoDrawTargetShare);
            Assert.Equal(0.25, sc.sweepShare);
            Assert.Equal(24, sc.termMonths);
            Assert.Equal(0.15, sc.termAnnualRate);

            BridgeConfig br = balance.bridgeLoan;
            Assert.Equal(0.75, br.eligibleDebtLimitShare);
            Assert.Equal(1.0, br.baseDebtLimitShare);
            Assert.Equal(0.25, br.revenueShare);
            Assert.Equal(0.05, br.fee);
            Assert.Equal(0.30, br.annualRate);
            Assert.Equal(12, br.months);
            Assert.Equal(180, br.cooldownDays);

            BailoutConfig bo = balance.bailout;
            Assert.Equal(new[] { 2, 1, 1, 1, 1, 0 }, bo.perGameByDifficulty);
            Assert.Equal(0.25, bo.targetDebtLimitShare);
            Assert.Equal(0.40, bo.loan.annualRate);
            Assert.Equal(36, bo.loan.months);
            Assert.Equal(0.30, bo.investor.profitShare);
            Assert.Equal(60, bo.investor.months);
            Assert.Equal(3.0, bo.investor.capMultiple);
            Assert.Equal("Royalties", bo.investor.expenseOrigin);
            Assert.Equal(-10, bo.reputationDelta);

            Assert.Equal(1, balance.assets.minLinesLeft);
        }

        [Test]
        public void DefaultsOfTheCode_AreTheShippedFile()
        {
            // The initializers of the classes and the file are two copies of one table; they must not drift apart.
            JsonObject config = RecoveryTestData.ParseObject(RecoveryTestData.ReadContent("balance.json"));
            JsonNode defaults = ModJson.From(RecoveryBalance.Defaults());
            Assert.True(config.Equals(defaults), "the defaults of RecoveryBalance differ from Content/bankruptcy-recovery/balance.json: " + defaults);
        }

        [Test]
        public void Defaults_PassTheirOwnValidation()
        {
            var problems = new List<string>();
            RecoveryBalance.Defaults().Validate(problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        // ---- the ranges ----

        [Test]
        public void EveryNumber_OutOfRange_GetsItsDefault_AndOneLine()
        {
            Assert.True(Rules.Length >= 28, "the table lost a row");
            foreach (Rule rule in Rules)
            {
                foreach (double bad in new[] { rule.Min - 1, rule.Max + 1 })
                {
                    JsonObject config = RecoveryTestData.DefaultsJson();
                    RecoveryTestData.SetPath(config, rule.Path, Number(rule, bad));

                    List<string> problems;
                    RecoveryBalance balance = Read(config, out problems);

                    string what = rule.Path + " = " + bad.ToString(CultureInfo.InvariantCulture);
                    Assert.Equal(1, problems.Count, what + ": " + string.Join("; ", problems));
                    Assert.True(problems[0].StartsWith(rule.Path + " out of range, default ", System.StringComparison.Ordinal), what + ": " + problems[0]);
                    Assert.True(problems[0].EndsWith(" used", System.StringComparison.Ordinal), what + ": " + problems[0]);
                    AssertIsDefaults(balance, what + " must come back as the default");
                }
            }
        }

        [Test]
        public void TheEndsOfEveryRange_AreAccepted()
        {
            foreach (Rule rule in Rules)
            {
                foreach (double edge in new[] { rule.Min, rule.Max })
                {
                    // These four edges break a rule about a PAIR (tested below), not the range.
                    if (rule.Path == "preWarning.resetDebtLimitShare" && edge == rule.Max) continue;
                    if (rule.Path == "preWarning.thresholdDebtLimitShare" && edge == rule.Min) continue;
                    if (rule.Path == "startupCredit.autoDrawTargetShare" && edge == rule.Max) continue;
                    if (rule.Path == "startupCredit.autoDrawTriggerShare" && edge == rule.Min) continue;

                    JsonObject config = RecoveryTestData.DefaultsJson();
                    RecoveryTestData.SetPath(config, rule.Path, Number(rule, edge));

                    List<string> problems;
                    Read(config, out problems);
                    Assert.Equal(0, problems.Count, rule.Path + " = " + edge.ToString(CultureInfo.InvariantCulture) + ": " + string.Join("; ", problems));
                }
            }
        }

        [Test]
        public void BridgeRateBelowZero_WritesTheLineOfAC12_1()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "bridgeLoan.annualRate", -0.3);

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(1, problems.Count);
            Assert.Equal("bridgeLoan.annualRate out of range, default 0.30 used", problems[0]);
            Assert.Equal(0.30, balance.bridgeLoan.annualRate);
            Assert.Equal(12, balance.bridgeLoan.months, "the rest of the section is untouched");
        }

        [Test]
        public void FloorBase_IsChecked_AndTheLineSaysTheDefault()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "limits.floorBase", -5.0);

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(2000000.0, balance.limits.floorBase);
            Assert.Equal(1, problems.Count);
            Assert.Equal("limits.floorBase out of range, default 2000000 used", problems[0]);

            RecoveryTestData.SetPath(config, "limits.floorBase", 1000000000.0);
            balance = Read(config, out problems);
            Assert.Equal(1000000000.0, balance.limits.floorBase, "1e9 is the highest legal value");
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void AValidOverride_IsTaken_AndTheRestKeepsTheDefaults()
        {
            JsonObject config = RecoveryTestData.ParseObject(@"{ ""schema"": 1, ""bridgeLoan"": { ""annualRate"": 0.5, ""months"": 24 },
                ""limits"": { ""floorBase"": 3000000 } }");

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(0.5, balance.bridgeLoan.annualRate);
            Assert.Equal(24, balance.bridgeLoan.months);
            Assert.Equal(0.05, balance.bridgeLoan.fee, "a member the file does not have keeps the default");
            Assert.Equal(3000000.0, balance.limits.floorBase);
            Assert.Equal(7, balance.earlyExit.streakDays);
        }

        // ---- the arrays per difficulty ----

        [Test]
        public void ArraysOfTheWrongLength_AreRejectedWhole()
        {
            foreach (string path in new[] { "startupCredit.baseDebtLimitShareByDifficulty", "bailout.perGameByDifficulty" })
            {
                foreach (int length in new[] { 0, 1, 5, 7, 12 })
                {
                    JsonObject config = RecoveryTestData.DefaultsJson();
                    var array = new JsonArray();
                    for (int i = 0; i < length; i++) array.Add(1);
                    RecoveryTestData.SetPath(config, path, array);

                    List<string> problems;
                    RecoveryBalance balance = Read(config, out problems);

                    string what = path + " with " + length + " values";
                    Assert.Equal(1, problems.Count, what + ": " + string.Join("; ", problems));
                    Assert.True(problems[0].StartsWith(path + " needs exactly 6 values, got " + length + ", default [", System.StringComparison.Ordinal), what + ": " + problems[0]);
                    AssertIsDefaults(balance, what + " must come back as the default");
                }
            }
        }

        [Test]
        public void AnArrayWithOneBadValue_IsRejectedWhole()
        {
            // not just the bad element: the six values belong together
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "startupCredit.baseDebtLimitShareByDifficulty", new JsonArray { 2.0, 1.0, -0.5, 1.0, 1.0, 1.0 });
            RecoveryTestData.SetPath(config, "bailout.perGameByDifficulty", new JsonArray { 2, 1, 101, 1, 1, 0 });

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(2, problems.Count, string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "startupCredit.baseDebtLimitShareByDifficulty value 2 is out of range, default [1.50, 1.25, 1, 0.75, 0.50, 0.25] used"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "bailout.perGameByDifficulty value 2 is out of range, default [2, 1, 1, 1, 1, 0] used"), string.Join("; ", problems));
            AssertIsDefaults(balance);
        }

        [Test]
        public void AnArrayWithAValueOfTheWrongType_IsRejectedWhole()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "bailout.perGameByDifficulty", new JsonArray { 2, 1, "one", 1, 1, 0 });
            RecoveryTestData.SetPath(config, "startupCredit.baseDebtLimitShareByDifficulty", new JsonArray { 1.5, 1.25, true, 0.75, 0.5, 0.25 });

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.True(RecoveryTestData.AnyContains(problems, "bailout.perGameByDifficulty needs exactly 6 values, got 5"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "startupCredit.baseDebtLimitShareByDifficulty needs exactly 6 values, got 5"), string.Join("; ", problems));
            AssertIsDefaults(balance);
        }

        [Test]
        public void ArraysOfSixValidValues_AreTaken()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "startupCredit.baseDebtLimitShareByDifficulty", new JsonArray { 3.0, 2.0, 1.0, 1.0, 0.0, 10.0 });
            RecoveryTestData.SetPath(config, "bailout.perGameByDifficulty", new JsonArray { 3, 3, 2, 1, 0, 0 });

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(new[] { 3.0, 2.0, 1.0, 1.0, 0.0, 10.0 }, balance.startupCredit.baseDebtLimitShareByDifficulty);
            Assert.Equal(new[] { 3, 3, 2, 1, 0, 0 }, balance.bailout.perGameByDifficulty);
        }

        [Test]
        public void ANullArray_GetsTheDefault()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "bailout.perGameByDifficulty", JsonValue.Null);

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.True(RecoveryTestData.AnyContains(problems, "bailout.perGameByDifficulty is missing, default [2, 1, 1, 1, 1, 0] used"), string.Join("; ", problems));
            AssertIsDefaults(balance);
        }

        // ---- the expense item ----

        [Test]
        public void ExpenseOrigin_IsRoyaltiesOrInterest()
        {
            foreach (string spelling in new[] { "Royalties", "royalties", "ROYALTIES" })
            {
                JsonObject config = RecoveryTestData.DefaultsJson();
                RecoveryTestData.SetPath(config, "bailout.investor.expenseOrigin", spelling);
                List<string> problems;
                RecoveryBalance balance = Read(config, out problems);
                Assert.Equal(0, problems.Count, spelling + ": " + string.Join("; ", problems));
                Assert.Equal("Royalties", balance.bailout.investor.expenseOrigin, "the spelling of the table is kept");
            }

            JsonObject interest = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(interest, "bailout.investor.expenseOrigin", "interest");
            List<string> none;
            Assert.Equal("Interest", Read(interest, out none).bailout.investor.expenseOrigin);
            Assert.Equal(0, none.Count);
        }

        [Test]
        public void AnUnknownExpenseOrigin_GetsTheDefault()
        {
            foreach (string bad in new[] { "Dividends", "", " Royalties" })
            {
                JsonObject config = RecoveryTestData.DefaultsJson();
                RecoveryTestData.SetPath(config, "bailout.investor.expenseOrigin", bad);

                List<string> problems;
                RecoveryBalance balance = Read(config, out problems);

                Assert.Equal(1, problems.Count, "'" + bad + "': " + string.Join("; ", problems));
                Assert.Equal("bailout.investor.expenseOrigin must be Royalties or Interest, default Royalties used", problems[0]);
                Assert.Equal("Royalties", balance.bailout.investor.expenseOrigin);
            }
        }

        // ---- values that contradict each other ----

        [Test]
        public void ResetAboveTheWarningThreshold_BringsBackBothDefaults()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "preWarning.thresholdDebtLimitShare", 0.4);
            RecoveryTestData.SetPath(config, "preWarning.resetDebtLimitShare", 0.6);

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.Equal("preWarning.resetDebtLimitShare must not exceed preWarning.thresholdDebtLimitShare, defaults 0.75 and 0.50 used", problems[0]);
            Assert.Equal(0.75, balance.preWarning.thresholdDebtLimitShare);
            Assert.Equal(0.5, balance.preWarning.resetDebtLimitShare);
        }

        [Test]
        public void ResetEqualToTheWarningThreshold_IsAccepted()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "preWarning.thresholdDebtLimitShare", 0.5);

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(0.5, balance.preWarning.thresholdDebtLimitShare);
            Assert.Equal(0.5, balance.preWarning.resetDebtLimitShare);
        }

        [Test]
        public void AutoDrawTargetAboveTheTrigger_BringsBackBothDefaults()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "startupCredit.autoDrawTriggerShare", 0.5);
            RecoveryTestData.SetPath(config, "startupCredit.autoDrawTargetShare", 0.8);

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.Equal("startupCredit.autoDrawTargetShare must not exceed startupCredit.autoDrawTriggerShare, defaults 0.90 and 0.60 used", problems[0]);
            Assert.Equal(0.9, balance.startupCredit.autoDrawTriggerShare);
            Assert.Equal(0.6, balance.startupCredit.autoDrawTargetShare);
        }

        // ---- wrong types, missing and unknown members ----

        [Test]
        public void AMemberOfTheWrongType_KeepsItsDefault_AndIsReported()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "limits.floorBase", "lots");
            RecoveryTestData.SetPath(config, "bridgeLoan.months", 2.5);
            RecoveryTestData.SetPath(config, "bailout.investor.expenseOrigin", 7);
            RecoveryTestData.SetPath(config, "startupCredit", "none");

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.True(RecoveryTestData.AnyContains(problems, "limits.floorBase: expected number"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "bridgeLoan.months"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "bailout.investor.expenseOrigin: expected string"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "startupCredit: expected object"), string.Join("; ", problems));
            AssertIsDefaults(balance, "every wrong member keeps its default");
        }

        [Test]
        public void MissingMembersAndSections_KeepTheDefaults_WithoutAProblem()
        {
            List<string> problems;
            RecoveryBalance balance = Read(RecoveryTestData.ParseObject(@"{ ""schema"": 1 }"), out problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            AssertIsDefaults(balance);

            balance = Read(RecoveryTestData.ParseObject(@"{ ""bailout"": { ""investor"": { } }, ""bridgeLoan"": { } }"), out problems);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            AssertIsDefaults(balance);
        }

        [Test]
        public void ANullSection_IsReplacedByItsDefaults_WithALine()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(config, "limits", JsonValue.Null);
            RecoveryTestData.SetPath(config, "bailout.loan", JsonValue.Null);
            RecoveryTestData.SetPath(config, "bailout.investor", JsonValue.Null);
            RecoveryTestData.SetPath(config, "assets", JsonValue.Null);

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(4, problems.Count, string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "limits is missing, defaults used"));
            Assert.True(RecoveryTestData.AnyContains(problems, "bailout.loan is missing, defaults used"));
            Assert.True(RecoveryTestData.AnyContains(problems, "bailout.investor is missing, defaults used"));
            Assert.True(RecoveryTestData.AnyContains(problems, "assets is missing, defaults used"));
            AssertIsDefaults(balance);
        }

        [Test]
        public void UnknownMembers_AreIgnored()
        {
            JsonObject config = RecoveryTestData.DefaultsJson();
            config.Set("somethingNew", 5);
            RecoveryTestData.SetPath(config, "limits.futureField", true);

            List<string> problems;
            RecoveryBalance balance = Read(config, out problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            AssertIsDefaults(balance);
        }

        [Test]
        public void ANullOrEmptyConfig_GivesTheDefaults_AndTheProblemListMayBeNull()
        {
            List<string> problems = new List<string>();
            AssertIsDefaults(RecoveryBalance.FromJson(null, problems));
            AssertIsDefaults(RecoveryBalance.FromJson(new JsonObject(), problems));
            Assert.Equal(0, problems.Count, "the built-in file's failure is reported by ModContent, not repeated here");

            RecoveryBalance withoutList = RecoveryBalance.FromJson(RecoveryTestData.DefaultsJson(), null);
            AssertIsDefaults(withoutList);

            JsonObject broken = RecoveryTestData.DefaultsJson();
            RecoveryTestData.SetPath(broken, "bridgeLoan.fee", 5);
            AssertIsDefaults(RecoveryBalance.FromJson(broken, null), "a null list must not stop the repair");
        }

        [Test]
        public void Validate_RepairsADtoBuiltInCode()
        {
            // the services and the simulation set fields directly; Validate is the same repair for them
            var balance = new RecoveryBalance();
            balance.bridgeLoan.annualRate = double.NaN;
            balance.limits.floorBase = double.PositiveInfinity;
            balance.bailout.perGameByDifficulty = null;
            balance.bailout.investor = null;
            balance.startupCredit = null;

            var problems = new List<string>();
            balance.Validate(problems);

            Assert.Equal(5, problems.Count, string.Join("; ", problems));
            AssertIsDefaults(balance);
        }
    }
}
