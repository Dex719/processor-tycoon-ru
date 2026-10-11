using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-011...TSK-016, TSK-019: the lines the panels are made of, checked against the string tables. A line carries a key and arguments; every key a builder can produce must have a text in both
    /// languages, and the text must not ask for an argument the line does not carry ({3} for a line with three arguments would print nothing). The builders are run on inputs that make them produce
    /// every kind of line.
    /// </summary>
    public class PanelStringsTests
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);

        private static JsonObject Table(string language)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "contract-transparency.json");
            Assert.True(File.Exists(path), "missing string table " + path);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), path + ": " + error);
            return (JsonObject)node;
        }

        private static string TextOf(JsonObject table, string key)
        {
            string text;
            return table.TryGetString(key, out text) ? text : null;
        }

        private static void CheckLines(IEnumerable<PanelLine> lines, string what, List<string> problems)
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            foreach (PanelLine line in lines)
            {
                if (string.IsNullOrEmpty(line.Key)) continue;
                foreach (JsonObject table in new[] { ru, en })
                {
                    string text = TextOf(table, line.Key);
                    string language = ReferenceEquals(table, ru) ? "ru" : "en";
                    if (text == null)
                    {
                        problems.Add(what + ": " + language + " lacks " + line.Key);
                        continue;
                    }
                    foreach (Match m in Placeholder.Matches(text))
                    {
                        int index = int.Parse(m.Groups[1].Value);
                        if (index >= line.Args.Length) problems.Add(what + ": " + language + ":" + line.Key + " asks for {" + index + "} but the line has " + line.Args.Length + " argument(s)");
                    }
                }

                // The keys the arguments name must be in the tables as well.
                foreach (PanelArg arg in line.Args)
                {
                    IEnumerable<string> keys = arg.Kind == ArgKind.Key ? new[] { arg.Text }
                        : arg.Kind == ArgKind.KeyList ? (arg.Text ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        : new string[0];
                    foreach (string key in keys)
                    {
                        if (key.StartsWith("core.", StringComparison.Ordinal)) continue;   // the texts of the core are checked with the core's tables
                        if (TextOf(ru, key) == null) problems.Add(what + ": ru lacks " + key + " (argument of " + line.Key + ")");
                        if (TextOf(en, key) == null) problems.Add(what + ": en lacks " + key + " (argument of " + line.Key + ")");
                    }
                }
            }
        }

        [Test]
        public void EveryNameKeyOfTheLog_HasATextInBothLanguages()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            var problems = new List<string>();
            foreach (string key in LogLines.AllNameKeys())
            {
                if (TextOf(ru, key) == null) problems.Add("ru lacks " + key);
                if (TextOf(en, key) == null) problems.Add("en lacks " + key);
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void TheLinesOfTheRequirementsTheRisksTheOddsAndTheReview_UseKeysWithTextsAndEnoughArguments()
        {
            var problems = new List<string>();

            // Requirements: everything red, then everything green, then the disagreement.
            var red = new RequirementPanelInput
            {
                Ips = RequirementCheck.Ips(0.8f, 1.2f, 0.75f),
                Power = RequirementCheck.Power(70f, 60f),
                Package = RequirementCheck.Package(1, false, false, true),
                Tolerance = 0.75f, RequiredPackageText = "DIP/PLCC", CpuPackageText = "PGA", FailsByGame = true, MismatchWithGame = true,
            };
            CheckLines(RequirementLines.Build(red), "requirements (red)", problems);
            var green = new RequirementPanelInput
            {
                Ips = RequirementCheck.Ips(2f, 1.2f, 0.75f), Power = RequirementCheck.Power(45f, 60f), Package = RequirementCheck.Package(0, false, false, true),
                Tolerance = 0.75f, RequiredPackageText = "any", CpuPackageText = "PGA",
            };
            CheckLines(RequirementLines.Build(green), "requirements (green)", problems);

            // Risks: all seven.
            var report = new CommitmentReport
            {
                Risks = RiskFlags.CapacityShortNow | RiskFlags.Overcommit | RiskFlags.NegativeProfit | RiskFlags.BelowAutomationMargin
                        | RiskFlags.LowActualChance | RiskFlags.BreakFineExceedsCash | RiskFlags.AutoRejectWillWithdraw,
            };
            CheckLines(RiskLines.Build(report, 0.05, 1000, 40), "risks", problems);

            // Odds: every note.
            List<OfferOdds> rows = OddsMath.DrawOrder(OddsMath.BestPerCompany(new[] { new OfferInput("ai", 1, 1000f, 0), new OfferInput("me", 2, 1f, 1) }), null);
            var odds = new OddsPanelInput
            {
                Rows = rows, CandidateCpuId = 2, GameChance = 1, Actual = 0, GameChancesAvailable = false, NoOffersYet = true, ReviewDateText = "x", ChanceDiffersFromVanilla = true,
                ModsCapped = true, ShadowedCount = 1, StrongestOwnName = "y", IpsFor50 = 1000, IpsForFirstInQueue = 1000, Mismatch = true,
                Sources = new List<ModSourceText> { new ModSourceText { Feature = "f", Reason = "r", Multiplier = 1.2f }, new ModSourceText { Feature = "f", Multiplier = 1f } },
            };
            CheckLines(OddsLines.Build(odds), "odds", problems);
            odds.ModsCapped = false;
            odds.CandidateCpuId = 1;
            CheckLines(OddsLines.Build(odds), "odds (first)", problems);

            // The review screen with and without risks.
            var input = new ReviewInput { ContractLabel = "c", CpuName = "n", Commitments = report };
            ReviewPlan plan = ReviewLines.Build(input);
            CheckLines(plan.Contract.Concat(plan.Offer).Concat(plan.Risks), "review", problems);
            plan = ReviewLines.Build(new ReviewInput { ContractLabel = "c", CpuName = "n" });
            CheckLines(plan.Contract.Concat(plan.Offer).Concat(plan.Risks), "review (safe)", problems);

            // The probability and the deal.
            CheckLines(new[] { ProbabilityLabel.Line("40%", 0.21) }, "probability", problems);
            CheckLines(DealLines.Build(new DealInput { Provider = "p", Client = "c", Exclusive = true }), "deal", problems);

            Assert.True(problems.Count == 0, "problems with the lines:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheLinesOfThePossibleParticipants_UseKeysWithTextsAndEnoughArguments()
        {
            // TSK-017: the block with rows, cut, empty and with the note for difficulty 5 - every line it can produce.
            var problems = new List<string>();
            var rows = new List<PotentialRow>
            {
                new PotentialRow { Company = "AMD", Cpu = "Am286", Ips = 50000f },
                new PotentialRow { Company = "Intel", Cpu = "i386", Ips = 70000f },
            };
            var input = new PotentialPanelInput { MovesLeft = 2, CurrentActual = 1.0, PessimisticActual = 0.28, Rows = rows, HardDifficulty = true, MaxRows = 1 };
            List<PanelLine> lines = PotentialLines.Build(input);
            CheckLines(lines, "potential (cut)", problems);
            Assert.True(lines.Any(l => l.Key == "ct.odds.potential.more"), "the case of a cut list is covered");

            input.MaxRows = 12;
            input.PessimisticActual = 0.01;
            CheckLines(PotentialLines.Build(input), "potential (bad)", problems);

            input.Rows = new List<PotentialRow>();
            input.HardDifficulty = false;
            lines = PotentialLines.Build(input);
            CheckLines(lines, "potential (nobody)", problems);
            Assert.True(lines.Any(l => l.Key == "ct.odds.potential.none"), "the case of an empty list is covered");

            Assert.True(problems.Count == 0, "problems with the lines:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void ThePossibleParticipantsStrings_AreInBothLanguages_WithTheSamePlaceholders()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");
            var problems = new List<string>();

            foreach (string key in new[]
            {
                "ct.odds.potential.title", "ct.odds.potential.moves", "ct.odds.potential.row", "ct.odds.potential.more", "ct.odds.potential.none",
                "ct.odds.potential.pessimistic", "ct.odds.potential.note", "ct.odds.potential.hard",
            })
            {
                string textRu = TextOf(ru, key);
                string textEn = TextOf(en, key);
                if (textRu == null || textEn == null)
                {
                    problems.Add(key + " is missing in " + (textRu == null ? "ru" : "en"));
                    continue;
                }

                var inRu = new SortedSet<string>(Placeholder.Matches(textRu).Cast<Match>().Select(m => m.Value));
                var inEn = new SortedSet<string>(Placeholder.Matches(textEn).Cast<Match>().Select(m => m.Value));
                if (!inRu.SetEquals(inEn)) problems.Add(key + ": ru has " + string.Join(",", inRu) + ", en has " + string.Join(",", inEn));
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void TheLinesOfTheLogAndThePreview_UseKeysWithTextsAndEnoughArguments()
        {
            var problems = new List<string>();

            // One entry for each reason the feature writes.
            var entries = new List<LogEntry>();
            foreach (string reason in new[]
            {
                "ct.reason.sentManual", "ct.reason.sentApi", "ct.reason.autoConfirmedSafe", "ct.reason.autoConfirmedNever", "ct.reason.autoConfirmedNoScreen",
                "ct.reason.cancelledOnReview", "ct.reason.sent", "ct.reason.notSent", "ct.reason.alreadyOffered", "ct.reason.retired", "ct.reason.notForContracts",
                "ct.reason.margin", "ct.reason.requirements", "ct.reason.automationOff", "ct.reason.automationSummary", "ct.reason.mismatch", "ct.reason.autoReject",
            })
            {
                var entry = LogEntry.Create("1986-12-31", LogTypes.AutomationDecision, LogSources.Automation);
                entry.reasonKey = reason;
                entry.Put(LogDataKeys.Failed, "ips,power,package").Put(LogDataKeys.Predicted, "WillOffer").Put(LogDataKeys.Observed, "notSent").Put(LogDataKeys.Risks, 127);
                entries.Add(entry);
            }
            foreach (string outcome in new[] { TenderOutcomeClassifier.OutcomeWon, TenderOutcomeClassifier.OutcomeLost, TenderOutcomeClassifier.OutcomeVoid })
            {
                var entry = LogEntry.Create("1987-01-01", LogTypes.TenderResult, LogSources.Game);
                entry.reasonKey = outcome == TenderOutcomeClassifier.OutcomeWon ? "ct.tender.won" : "ct.tender.outbid";
                entry.Put(LogDataKeys.Outcome, outcome);
                entries.Add(entry);
            }
            entries.Add(DealLines.LogEntryOf(new DealInput { Provider = "p", Client = "c" }, "1987-03-05"));

            foreach (LogEntry entry in entries)
            {
                CheckLines(new[] { LogLines.Sentence(entry) }, "log row " + entry.reasonKey, problems);
                CheckLines(LogLines.Details(entry), "log details " + entry.reasonKey, problems);
            }

            // The details of a tender, with everything in it.
            var tender = new TenderResultDto { outcome = TenderOutcomeClassifier.OutcomeLost, reasonKey = "ct.tender.autoReject", playerCpuName = "n", winnerCompany = "w", winnerCpuName = "x", capacityNotChecked = true };
            tender.offers.Add(new OfferRowDto { company = "w", cpu = "x", player = false });
            tender.pruned.Add(new PrunedRowDto { company = "a", cpu = "b", maker = string.Empty, player = true });
            tender.pruned.Add(new PrunedRowDto { company = "a", cpu = "b", maker = "m" });
            CheckLines(LogLines.TenderDetails(tender), "tender details", problems);
            foreach (string reason in new[] { "ct.tender.won", "ct.tender.outbid", "ct.tender.autoReject", "ct.tender.shadowed", "ct.tender.retired", "ct.tender.noOffers" })
            {
                CheckLines(new[] { new PanelLine("ct.log.t.result", LineTone.Normal, PanelArg.OfKey("ct.log.outcome.Lost"), PanelArg.OfKey(reason)) }, "tender reason " + reason, problems);
            }

            // The preview: one row of each decision.
            var rowsOfTheGame = new List<AutomationRow>
            {
                new AutomationRow { Decision = AutomationDecision.WillOffer, ContractKey = "k", Margin = 0.5f },
                new AutomationRow { Decision = AutomationDecision.SkippedMargin, ContractKey = "k", Margin = 0.3f, Threshold = 0.4f },
                new AutomationRow { Decision = AutomationDecision.RejectedByRequirements, ContractKey = "k", Failed = "ips" },
                new AutomationRow { Decision = AutomationDecision.AlreadyOffered, ContractKey = "k" },
                new AutomationRow { Decision = AutomationDecision.SkippedRetired },
                new AutomationRow { Decision = AutomationDecision.SkippedNotForContracts },
            };
            foreach (bool on in new[] { true, false })
            {
                PreviewPlan preview = PreviewLines.Build(rowsOfTheGame, on, 40, "31 Dec", 3, 3);
                CheckLines(preview.Header.Concat(preview.Rows), "preview", problems);
                Assert.True(preview.Hidden > 0, "the limit of three rows cuts something");
            }

            Assert.True(problems.Count == 0, "problems with the lines:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheSentencesOfTheLog_AreEnoughForEveryTypeOfEntry()
        {
            // A type of entry the log can hold always has a name, so a row never shows an empty sentence.
            foreach (string type in new[]
            {
                LogTypes.OfferSent, LogTypes.OfferCancelledOnReview, LogTypes.OfferAutoConfirmed, LogTypes.AutomationDecision, LogTypes.AutomationSummary,
                LogTypes.AutoRejected, LogTypes.TenderResult, LogTypes.BusinessSigned, LogTypes.Mismatch,
            })
            {
                var entry = LogEntry.Create("1986-12-31", type, LogSources.Game);
                Assert.True(LogLines.Sentence(entry).Key.Length > 0, type + " has no sentence");
            }
        }
    }
}
