using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core.Logic.Json;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// NFR-7 and the "Validation Rules" of design.md for the section of the side save file: a record that cannot be right (a negative balance, a rate
    /// of 500 percent, a date that is not yyyy-MM-dd) is dropped with a line for the log and the rest of the section is kept; a damaged member never
    /// stops the load.
    /// </summary>
    [Covers(typeof(RecoveryStateCodec))]
    public class RecoveryStateCodecTests
    {
        private static RecoveryState Read(string sectionJson, out List<string> problems)
        {
            problems = new List<string>();
            return RecoveryStateCodec.Read(RecoveryTestData.ParseObject(sectionJson), problems);
        }

        private static RecoveryState Sanitized(RecoveryState state, out List<string> problems)
        {
            problems = new List<string>();
            RecoveryStateCodec.Sanitize(state, problems);
            return state;
        }

        private static RecoveryState WithLoan(LoanData loan)
        {
            var state = new RecoveryState();
            state.loans.Add(loan);
            return state;
        }

        // ---- reading ----

        [Test]
        public void ANullSection_IsANewGame_WithoutAProblem()
        {
            var problems = new List<string>();
            RecoveryState state = RecoveryStateCodec.Read(null, problems);

            Assert.Equal(0, problems.Count);
            Assert.True(ModJson.From(new RecoveryState()).Equals(ModJson.From(state)));
        }

        [Test]
        public void AnEmptyObject_KeepsEveryDefault_WithoutAProblem()
        {
            List<string> problems;
            RecoveryState state = Read("{}", out problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.True(ModJson.From(new RecoveryState()).Equals(ModJson.From(state)));
        }

        [Test]
        public void TheVersionFieldOfTheCore_IsIgnoredOnRead_AndNeverWritten()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""_schema"": 1, ""bailoutsUsed"": 2 }", out problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(2, state.bailoutsUsed);
            Assert.False(RecoveryStateCodec.Write(state).ContainsKey("_schema"));
        }

        [Test]
        public void MembersOfTheWrongType_KeepTheirDefaults_AndAreReported()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""bailoutsUsed"": ""many"", ""loans"": ""none"", ""episode"": { ""recoveryStreak"": true }, ""nextLoanSeq"": 3 }", out problems);

            Assert.Equal(0, state.bailoutsUsed);
            Assert.Equal(0, state.loans.Count);
            Assert.Equal(0, state.episode.recoveryStreak);
            Assert.Equal(3, state.nextLoanSeq, "the good member beside the bad ones is read");
            Assert.True(RecoveryTestData.AnyContains(problems, "bailoutsUsed"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "loans"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "episode.recoveryStreak"), string.Join("; ", problems));
        }

        [Test]
        public void NullMembers_AreReplacedByEmptyOnes()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""creditLine"": null, ""loans"": null, ""investorShare"": null, ""episode"": null, ""journal"": null, ""lastBridgeClosedOn"": null }", out problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.True(ModJson.From(new RecoveryState()).Equals(ModJson.From(state)));
        }

        [Test]
        public void UnknownMembers_AreIgnored()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""somethingNew"": [1, 2], ""bailoutsUsed"": 1, ""creditLine"": { ""futureField"": 1 } }", out problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(1, state.bailoutsUsed);
        }

        // ---- the credit line ----

        [Test]
        public void TheThreeStatesOfTheLine_AreReadAndWritten()
        {
            List<string> problems;
            RecoveryState none = Read(@"{ ""creditLine"": { ""state"": ""none"" } }", out problems);
            Assert.Equal(CreditLineData.StateNone, none.creditLine.state);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            RecoveryState open = Read(@"{ ""creditLine"": { ""state"": ""open"", ""opensOn"": ""1975-01-01"", ""closesOn"": ""1978-01-01"", ""drawn"": 100.5, ""autoDraw"": false } }", out problems);
            Assert.Equal(CreditLineData.StateOpen, open.creditLine.state);
            Assert.Equal(100.5, open.creditLine.drawn);
            Assert.False(open.creditLine.autoDraw);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            RecoveryState closed = Read(@"{ ""creditLine"": { ""state"": ""closed"", ""opensOn"": ""1975-01-01"", ""closesOn"": ""1978-01-01"", ""drawn"": 0, ""closedReason"": ""disabled"" } }", out problems);
            Assert.Equal(CreditLineData.StateClosed, closed.creditLine.state);
            Assert.Equal(CreditLineData.ReasonDisabled, closed.creditLine.closedReason);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));

            foreach (RecoveryState state in new[] { none, open, closed })
            {
                var again = RecoveryStateCodec.Read(RecoveryStateCodec.Write(state), problems);
                Assert.True(ModJson.From(state).Equals(ModJson.From(again)), "the line must survive a write and a read");
            }
        }

        [Test]
        public void ALineInAnUnknownState_IsDropped()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""creditLine"": { ""state"": ""paused"", ""drawn"": 5 }, ""bailoutsUsed"": 1 }", out problems);

            Assert.Equal(CreditLineData.StateNone, state.creditLine.state);
            Assert.Equal(0, state.creditLine.drawn);
            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].StartsWith("creditLine.state 'paused'", StringComparison.Ordinal), problems[0]);
            Assert.Equal(1, state.bailoutsUsed, "the rest is kept");
        }

        [Test]
        public void ANegativeOrBrokenDrawnPart_DropsTheLine()
        {
            List<string> problems;
            RecoveryState negative = Read(@"{ ""creditLine"": { ""state"": ""open"", ""opensOn"": ""1975-01-01"", ""closesOn"": ""1978-01-01"", ""drawn"": -1 } }", out problems);
            Assert.Equal(CreditLineData.StateNone, negative.creditLine.state);
            Assert.Equal(1, problems.Count, string.Join("; ", problems));
            Assert.True(problems[0].StartsWith("creditLine.drawn -1", StringComparison.Ordinal), problems[0]);

            var state = new RecoveryState();
            state.creditLine.state = CreditLineData.StateOpen;
            state.creditLine.opensOn = "1975-01-01";
            state.creditLine.closesOn = "1978-01-01";
            state.creditLine.drawn = double.NaN;
            Sanitized(state, out problems);
            Assert.Equal(CreditLineData.StateNone, state.creditLine.state, "NaN cannot come from JSON but can come from code");
            Assert.Equal(1, problems.Count);
        }

        [Test]
        public void AnOpenLineNeedsBothDates()
        {
            foreach (string dates in new[] { @"""opensOn"": ""1975-01-01""", @"""closesOn"": ""1978-01-01""", @"""opensOn"": ""75-1-1"", ""closesOn"": ""1978-01-01""",
                                              @"""opensOn"": ""1975-01-01"", ""closesOn"": ""1978-02-30""", @"""opensOn"": ""1975-01-01"", ""closesOn"": ""soon""" })
            {
                List<string> problems;
                RecoveryState state = Read(@"{ ""creditLine"": { ""state"": ""open"", ""drawn"": 10, " + dates + " } }", out problems);

                Assert.Equal(CreditLineData.StateNone, state.creditLine.state, dates);
                Assert.Equal(1, problems.Count, dates + ": " + string.Join("; ", problems));
                Assert.True(problems[0].StartsWith("creditLine: an open line needs opensOn and closesOn", StringComparison.Ordinal), problems[0]);
            }
        }

        [Test]
        public void AClosedLine_KeepsItsStateWhenADateIsBroken()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""creditLine"": { ""state"": ""closed"", ""opensOn"": ""yesterday"", ""closesOn"": ""1978-01-01"", ""closedReason"": ""bankrupt"" } }", out problems);

            Assert.Equal(CreditLineData.StateClosed, state.creditLine.state, "a closed line must stay closed: it is never opened again");
            Assert.Equal("", state.creditLine.opensOn);
            Assert.Equal("1978-01-01", state.creditLine.closesOn);
            Assert.Equal("", state.creditLine.closedReason);
            Assert.Equal(2, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ALineThatWasNeverOpened_HasNothingElse()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""creditLine"": { ""state"": ""none"", ""opensOn"": ""1975-01-01"", ""drawn"": 50, ""closedReason"": ""expired"", ""autoDraw"": false } }", out problems);

            Assert.Equal(CreditLineData.StateNone, state.creditLine.state);
            Assert.Equal(0, state.creditLine.drawn);
            Assert.Equal("", state.creditLine.opensOn);
            Assert.Equal("", state.creditLine.closedReason);
            Assert.False(state.creditLine.autoDraw, "the switch of the player is kept");
            Assert.Equal(1, problems.Count, string.Join("; ", problems));
        }

        // ---- the loans ----

        [Test]
        public void AGoodLoan_IsKept()
        {
            List<string> problems;
            RecoveryState state = Sanitized(WithLoan(RecoveryTestData.GoodLoan()), out problems);

            Assert.Equal(1, state.loans.Count);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void EveryBrokenRuleOfALoan_DropsOnlyThatLoan()
        {
            var breakers = new Dictionary<string, Action<LoanData>>
            {
                { "it has no id", l => l.id = "" },
                { "kind 'Gift' is not Bridge, Term or RescueLoan", l => l.kind = "Gift" },
                { "kind '' is not Bridge, Term or RescueLoan", l => l.kind = null },
                { "principal -1 is not a finite number of 0 or more", l => l.principal = -1 },
                { "principal NaN is not a finite number of 0 or more", l => l.principal = double.NaN },
                { "outstanding -0.5 is not a finite number of 0 or more", l => l.outstanding = -0.5 },
                { "outstanding 1020000.1 is above the principal 1000000", l => l.outstanding = 1020000.1 },
                { "payment -1 is not a finite number of 0 or more", l => l.payment = -1 },
                { "annualRate 2.01 is not between 0 and 2", l => l.annualRate = 2.01 },
                { "annualRate -0.1 is not between 0 and 2", l => l.annualRate = -0.1 },
                { "monthsTotal 121 is not between 0 and 120", l => l.monthsTotal = 121 },
                { "monthsTotal -1 is not between 0 and 120", l => { l.monthsTotal = -1; l.monthsLeft = -1; } },
                { "monthsLeft 13 is not between 0 and monthsTotal", l => l.monthsLeft = 13 },
                { "monthsLeft -1 is not between 0 and monthsTotal", l => l.monthsLeft = -1 },
                { "openedOn '' is not a date yyyy-MM-dd", l => l.openedOn = "" },
                { "openedOn '1976-13-01' is not a date yyyy-MM-dd", l => l.openedOn = "1976-13-01" },
                { "openedOn '1976-3-1' is not a date yyyy-MM-dd", l => l.openedOn = "1976-3-1" },
            };

            foreach (KeyValuePair<string, Action<LoanData>> breaker in breakers)
            {
                var state = new RecoveryState();
                LoanData bad = RecoveryTestData.GoodLoan("bridge-1");
                breaker.Value(bad);
                state.loans.Add(RecoveryTestData.GoodLoan("rescue-0"));
                state.loans.Add(bad);
                state.loans.Add(RecoveryTestData.GoodLoan("term-5"));

                List<string> problems;
                Sanitized(state, out problems);

                Assert.Equal(2, state.loans.Count, breaker.Key + ": the loan must be dropped, the others kept");
                Assert.Equal("rescue-0", state.loans[0].id);
                Assert.Equal("term-5", state.loans[1].id);
                Assert.Equal(1, problems.Count, breaker.Key + ": " + string.Join("; ", problems));
                Assert.True(problems[0].IndexOf(breaker.Key, StringComparison.Ordinal) >= 0, "expected '" + breaker.Key + "' in: " + problems[0]);
                Assert.True(problems[0].StartsWith("loans[1]", StringComparison.Ordinal), problems[0]);
                Assert.True(problems[0].EndsWith("the loan is dropped", StringComparison.Ordinal), problems[0]);
            }
        }

        [Test]
        public void TheEdgesOfTheLoanRules_AreAccepted()
        {
            var state = new RecoveryState();
            LoanData a = RecoveryTestData.GoodLoan("bridge-1");
            a.outstanding = a.principal * 1.01;                 // the slack of the rounding
            a.annualRate = 2.0;
            a.monthsTotal = 120;
            a.monthsLeft = 120;
            LoanData b = RecoveryTestData.GoodLoan("bridge-2");
            b.outstanding = 0;
            b.principal = 0;
            b.annualRate = 0;
            b.monthsTotal = 0;
            b.monthsLeft = 0;
            b.payment = 0;
            state.loans.Add(a);
            state.loans.Add(b);

            List<string> problems;
            Sanitized(state, out problems);

            Assert.Equal(2, state.loans.Count, string.Join("; ", problems));
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ADuplicateLoanId_DropsTheSecond()
        {
            var state = new RecoveryState();
            state.loans.Add(RecoveryTestData.GoodLoan("bridge-1"));
            state.loans.Add(RecoveryTestData.GoodLoan("bridge-1"));

            List<string> problems;
            Sanitized(state, out problems);

            Assert.Equal(1, state.loans.Count);
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].StartsWith("loans[1] (bridge-1): the id is used twice", StringComparison.Ordinal), problems[0]);
        }

        [Test]
        public void ALoanBrokenInTheFile_IsDroppedOnRead()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""loans"": [
                { ""id"": ""bridge-1"", ""kind"": ""Bridge"", ""principal"": 1000, ""outstanding"": 900, ""annualRate"": 0.3, ""monthsTotal"": 12, ""monthsLeft"": 10, ""payment"": 100, ""openedOn"": ""1976-03-01"" },
                { ""id"": ""bridge-2"", ""kind"": ""Bridge"", ""principal"": ""a lot"", ""outstanding"": 900, ""annualRate"": 0.3, ""monthsTotal"": 12, ""monthsLeft"": 10, ""payment"": 100, ""openedOn"": ""1976-03-01"" },
                { ""id"": ""bridge-3"", ""kind"": ""Bridge"", ""principal"": 1000, ""outstanding"": 900, ""annualRate"": 5, ""monthsTotal"": 12, ""monthsLeft"": 10, ""payment"": 100, ""openedOn"": ""1976-03-01"" },
                42,
                null ] }", out problems);

            Assert.Equal(1, state.loans.Count);
            Assert.Equal("bridge-1", state.loans[0].id);
            Assert.True(RecoveryTestData.AnyContains(problems, "loans[1].principal"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "(bridge-3): annualRate 5 is not between 0 and 2"), string.Join("; ", problems));
        }

        [Test]
        public void TheNextLoanNumber_StaysAboveTheIdsInTheList()
        {
            var state = new RecoveryState();
            state.loans.Add(RecoveryTestData.GoodLoan("bridge-7"));
            state.loans.Add(RecoveryTestData.GoodLoan("rescue-12"));
            state.loans.Add(RecoveryTestData.GoodLoan("odd"));
            state.nextLoanSeq = 3;
            Sanitized(state, out _);
            Assert.Equal(13, state.nextLoanSeq);

            var calm = new RecoveryState { nextLoanSeq = 9 };
            calm.loans.Add(RecoveryTestData.GoodLoan("bridge-2"));
            Sanitized(calm, out _);
            Assert.Equal(9, calm.nextLoanSeq, "a number that is high enough is not touched");

            var zero = new RecoveryState { nextLoanSeq = 0 };
            Sanitized(zero, out _);
            Assert.Equal(1, zero.nextLoanSeq);

            var negative = new RecoveryState { nextLoanSeq = -4 };
            Sanitized(negative, out _);
            Assert.Equal(1, negative.nextLoanSeq);
        }

        // ---- the investor, the episode, the counters ----

        [Test]
        public void ABrokenInvestorContract_IsDropped()
        {
            var cases = new Dictionary<string, Action<InvestorShareData>>
            {
                { "amount -1", i => i.amount = -1 },
                { "paid -1", i => i.paid = -1 },
                { "cap -1", i => i.cap = -1 },
                { "share 1.5", i => i.share = 1.5 },
                { "share -0.1", i => i.share = -0.1 },
                { "monthsLeft 121", i => i.monthsLeft = 121 },
                { "monthsLeft -1", i => i.monthsLeft = -1 },
            };

            foreach (KeyValuePair<string, Action<InvestorShareData>> bad in cases)
            {
                var state = new RecoveryState();
                state.investorShare = new InvestorShareData { active = true, amount = 1000, share = 0.3, monthsLeft = 50, paid = 100, cap = 3000 };
                bad.Value(state.investorShare);
                state.bailoutsUsed = 1;

                List<string> problems;
                Sanitized(state, out problems);

                Assert.False(state.investorShare.active, bad.Key + ": the contract must be dropped");
                Assert.Equal(1, problems.Count, bad.Key + ": " + string.Join("; ", problems));
                Assert.True(problems[0].StartsWith("investorShare: " + bad.Key, StringComparison.Ordinal), problems[0]);
                Assert.Equal(1, state.bailoutsUsed, "the rest is kept");
            }
        }

        [Test]
        public void AGoodInvestorContract_IsKept_EvenAtTheEdges()
        {
            var state = new RecoveryState();
            state.investorShare = new InvestorShareData { active = true, amount = 0, share = 1, monthsLeft = 120, paid = 0, cap = 0 };

            List<string> problems;
            Sanitized(state, out problems);

            Assert.True(state.investorShare.active);
            Assert.Equal(0, problems.Count, string.Join("; ", problems));
        }

        [Test]
        public void ANegativeStreakAndCounter_AreRaisedToZero()
        {
            var state = new RecoveryState();
            state.episode.recoveryStreak = -3;
            state.bailoutsUsed = -1;

            List<string> problems;
            Sanitized(state, out problems);

            Assert.Equal(0, state.episode.recoveryStreak);
            Assert.Equal(0, state.bailoutsUsed);
            Assert.Equal(1, problems.Count, "the counter of the rescues is reported, the streak is a quiet repair: " + string.Join("; ", problems));
        }

        [Test]
        public void TheDateOfTheLastBridgeLoan_IsADateOrEmpty()
        {
            List<string> problems;
            RecoveryState good = Read(@"{ ""lastBridgeClosedOn"": ""1976-03-01"" }", out problems);
            Assert.Equal("1976-03-01", good.lastBridgeClosedOn);
            Assert.Equal(0, problems.Count);

            RecoveryState bad = Read(@"{ ""lastBridgeClosedOn"": ""last spring"" }", out problems);
            Assert.Equal("", bad.lastBridgeClosedOn);
            Assert.Equal(1, problems.Count, string.Join("; ", problems));
        }

        // ---- the journal ----

        [Test]
        public void ABrokenJournalRecord_IsDropped()
        {
            List<string> problems;
            RecoveryState state = Read(@"{ ""journal"": [
                { ""date"": ""1976-03-01"", ""op"": ""bridge.open"", ""amount"": 100.0, ""details"": ""a"" },
                { ""date"": ""1976-3-1"", ""op"": ""bridge.open"", ""amount"": 100.0, ""details"": ""bad date"" },
                { ""date"": ""1976-03-02"", ""op"": """", ""amount"": 100.0, ""details"": ""no op"" },
                { ""date"": ""1976-03-04"", ""op"": ""loan.payment"", ""amount"": -5.0, ""details"": null },
                null ] }", out problems);

            Assert.Equal(2, state.journal.Count);
            Assert.Equal("bridge.open", state.journal[0].op);
            Assert.Equal("loan.payment", state.journal[1].op);
            Assert.Equal("", state.journal[1].details, "no details is an empty text");
            Assert.True(RecoveryTestData.AnyContains(problems, "journal[1]: date '1976-3-1'"), string.Join("; ", problems));
            Assert.True(RecoveryTestData.AnyContains(problems, "journal[2]: it has no operation code"), string.Join("; ", problems));
        }

        [Test]
        public void TheJournal_KeepsTheLast200Records()
        {
            var state = new RecoveryState();
            for (int i = 0; i < 230; i++)
            {
                state.journal.Add(new JournalEntryData { date = "1976-03-01", op = "loan.payment", amount = -(i + 1), details = i.ToString(CultureInfo.InvariantCulture) });
            }

            List<string> problems;
            Sanitized(state, out problems);

            Assert.Equal(RecoveryStateCodec.MaxJournalEntries, state.journal.Count);
            Assert.Equal(200, state.journal.Count);
            Assert.Equal("30", state.journal[0].details, "the oldest 30 are the ones to go");
            Assert.Equal("229", state.journal[199].details);
            Assert.Equal(0, problems.Count, "a long journal is not damage");
        }

        // ---- writing ----

        [Test]
        public void Write_OfNull_IsANewGame()
        {
            JsonObject section = RecoveryStateCodec.Write(null);
            Assert.True(ModJson.From(new RecoveryState()).Equals(section));
        }

        [Test]
        public void TheStateThatWasWritten_IsReadWithoutProblems()
        {
            var problems = new List<string>();
            RecoveryState original = RecoveryTestData.PopulatedState();

            RecoveryState back = RecoveryStateCodec.Read(RecoveryStateCodec.Write(original), problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.True(ModJson.From(original).Equals(ModJson.From(back)));
        }

        [Test]
        public void TheSchemaVersion_IsOne()
        {
            Assert.Equal(1, RecoveryStateCodec.SchemaVersion);
        }
    }
}
