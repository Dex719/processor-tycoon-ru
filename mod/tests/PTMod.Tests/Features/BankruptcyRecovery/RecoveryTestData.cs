using System;
using System.Collections.Generic;
using System.IO;
using PTMod.Core.Logic.Json;
using PTMod.Features.BankruptcyRecovery.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// Shared by the tests of bankruptcy-recovery: the shipped content read from the source tree (the tests follow the files), and small
    /// builders. Internal, so the runner never discovers it.
    /// </summary>
    internal static class RecoveryTestData
    {
        public const string FeatureId = "bankruptcy-recovery";

        /// <summary>The text of a shipped file <c>Content/bankruptcy-recovery/&lt;file&gt;</c>.</summary>
        public static string ReadContent(string file)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", FeatureId, file);
            Assert.True(File.Exists(path), "the content file is missing: " + path);
            return SourceScan.ReadText(path);
        }

        /// <summary>The text of a shipped string table <c>L10n/&lt;lang&gt;/bankruptcy-recovery.json</c>.</summary>
        public static string ReadStrings(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, FeatureId + ".json");
            Assert.True(File.Exists(path), "the string table is missing: " + path);
            return SourceScan.ReadText(path);
        }

        public static JsonObject ParseObject(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "must be valid JSON: " + error);
            var obj = node as JsonObject;
            Assert.NotNull(obj, "the root must be an object");
            return obj;
        }

        /// <summary>The defaults of the code as a JSON object: a base for tests that change one member.</summary>
        public static JsonObject DefaultsJson()
        {
            return (JsonObject)ModJson.From(RecoveryBalance.Defaults());
        }

        /// <summary>Sets <c>a.b.c</c> in the object, creating nothing: every step but the last must exist.</summary>
        public static void SetPath(JsonObject root, string path, JsonNode value)
        {
            string[] steps = path.Split('.');
            JsonObject at = root;
            for (int i = 0; i < steps.Length - 1; i++)
            {
                at = at.GetObject(steps[i]);
                Assert.NotNull(at, "no such section: " + path);
            }
            at.Set(steps[steps.Length - 1], value);
        }

        /// <summary>A state with something in every part: an open line, two loans, an investor contract, an episode, a journal.</summary>
        public static RecoveryState PopulatedState()
        {
            var state = new RecoveryState();
            state.creditLine = new CreditLineData
            {
                state = CreditLineData.StateOpen,
                opensOn = "1975-01-01",
                closesOn = "1978-01-01",
                drawn = 2660000.0,
                autoDraw = false,
                closedReason = "",
            };
            state.loans.Add(new LoanData
            {
                id = "bridge-1", kind = LoanData.KindBridge, principal = 8400000.0, outstanding = 6527277.0, annualRate = 0.30,
                monthsTotal = 12, monthsLeft = 9, payment = 818892.0, openedOn = "1976-03-01",
            });
            state.loans.Add(new LoanData
            {
                id = "rescue-2", kind = LoanData.KindRescueLoan, principal = 36075000.0, outstanding = 36075000.0, annualRate = 0.40,
                monthsTotal = 36, monthsLeft = 36, payment = 1735600.0, openedOn = "1977-05-01",
            });
            state.bailoutsUsed = 1;
            state.investorShare = new InvestorShareData { active = true, amount = 36075000.0, share = 0.3, monthsLeft = 58, paid = 2400000.0, cap = 108225000.0 };
            state.episode = new EpisodeData { preWarningSent = true, defaultMailSent = true, recoveryStreak = 3, bailoutDeclined = false };
            state.lastBridgeClosedOn = "1974-02-01";
            state.nextLoanSeq = 3;
            state.journal.Add(new JournalEntryData { date = "1976-03-01", op = "bridge.open", amount = 8400000.0, details = "12m @ 30%" });
            state.journal.Add(new JournalEntryData { date = "1976-03-01", op = "bridge.fee", amount = -420000.0, details = "" });
            state.journal.Add(new JournalEntryData { date = "1976-04-01", op = "loan.payment", amount = -818892.0, details = "bridge-1" });
            return state;
        }

        /// <summary>A loan that passes every rule; a test breaks one member of it.</summary>
        public static LoanData GoodLoan(string id = "bridge-1")
        {
            return new LoanData
            {
                id = id, kind = LoanData.KindBridge, principal = 1000000.0, outstanding = 900000.0, annualRate = 0.30,
                monthsTotal = 12, monthsLeft = 10, payment = 100000.0, openedOn = "1976-03-01",
            };
        }

        public static bool AnyContains(IEnumerable<string> lines, string fragment)
        {
            foreach (string line in lines)
            {
                if (line.IndexOf(fragment, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }
}
