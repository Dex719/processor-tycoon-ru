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
    /// TSK-019 (NFR-4): the string tables <c>L10n/{ru,en}/contract-transparency.json</c> against the code. Every key the sources name is in both languages, the two tables have the same keys with the same
    /// placeholders, nothing carries a prefix of another feature, nothing is left that no code asks for, the terms of the player are those of the Russian translation of the game (<c>translation/ui.json</c>),
    /// and money and IPS are printed by the game's own formatters. <c>python install.py --check</c> holds the same tables against the prefix table of the core.
    /// </summary>
    public class StringTablesTests
    {
        private static readonly Regex Placeholder = new Regex(@"\{(\d+)\}", RegexOptions.Compiled);
        private static readonly Regex KeyLiteral = new Regex("\"(ct\\.[A-Za-z0-9_.]*[A-Za-z0-9_])\"", RegexOptions.Compiled);

        // The names of the predicates for the conditions of other features look like keys and are not.
        private static readonly string[] PredicateNames = ContractPredicateNames.All;

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency");
        }

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

        private static List<string> KeysOf(JsonObject table)
        {
            var keys = new List<string>();
            foreach (KeyValuePair<string, JsonNode> pair in table) keys.Add(pair.Key);
            return keys;
        }

        /// <summary>The keys the sources spell out as string literals (comments left out), the names of the predicates excepted.</summary>
        private static SortedSet<string> LiteralKeys()
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in KeyLiteral.Matches(code))
                {
                    string key = m.Groups[1].Value;
                    if (key.EndsWith(".", StringComparison.Ordinal) || PredicateNames.Contains(key)) continue;
                    keys.Add(key);
                }
            }
            return keys;
        }

        /// <summary>The keys the code builds from a prefix: the names of the log (types, sources, outcomes, decisions, requirements, filters, risks, numbers) and the texts of the parameters.</summary>
        private static SortedSet<string> BuiltKeys()
        {
            var keys = new SortedSet<string>(LogLines.AllNameKeys(), StringComparer.Ordinal);
            foreach (PTMod.Core.ParamSpec spec in ContractParamSpecs.Create())
            {
                keys.Add(spec.LabelKey);
                keys.Add(spec.TooltipKey);
            }
            return keys;
        }

        [Test]
        public void EveryKeyTheSourcesNameAsALiteral_IsInBothTables()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            var problems = new List<string>();
            SortedSet<string> keys = LiteralKeys();
            Assert.True(keys.Count > 150, "the scan found only " + keys.Count + " keys: the pattern is wrong?");
            foreach (string key in keys)
            {
                if (TextOf(ru, key) == null) problems.Add("ru lacks " + key);
                if (TextOf(en, key) == null) problems.Add("en lacks " + key);
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void EveryKeyTheCodeBuildsFromAPrefix_IsInBothTables()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            var problems = new List<string>();
            foreach (string key in BuiltKeys())
            {
                if (TextOf(ru, key) == null) problems.Add("ru lacks " + key);
                if (TextOf(en, key) == null) problems.Add("en lacks " + key);
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void TheTwoTables_HaveTheSameKeys_TheSamePlaceholders_AndNoEmptyText()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");
            var problems = new List<string>();

            var keysRu = new SortedSet<string>(KeysOf(ru), StringComparer.Ordinal);
            var keysEn = new SortedSet<string>(KeysOf(en), StringComparer.Ordinal);
            foreach (string key in keysRu.Except(keysEn)) problems.Add("only in ru: " + key);
            foreach (string key in keysEn.Except(keysRu)) problems.Add("only in en: " + key);

            foreach (string key in keysRu.Intersect(keysEn))
            {
                string textRu = TextOf(ru, key);
                string textEn = TextOf(en, key);
                if (string.IsNullOrWhiteSpace(textRu)) problems.Add("ru: empty text of " + key);
                if (string.IsNullOrWhiteSpace(textEn)) problems.Add("en: empty text of " + key);
                if (textRu == null || textEn == null) continue;

                var inRu = new SortedSet<string>(Placeholder.Matches(textRu).Cast<Match>().Select(m => m.Value), StringComparer.Ordinal);
                var inEn = new SortedSet<string>(Placeholder.Matches(textEn).Cast<Match>().Select(m => m.Value), StringComparer.Ordinal);
                if (!inRu.SetEquals(inEn)) problems.Add(key + ": ru has {" + string.Join(",", inRu) + "}, en has {" + string.Join(",", inEn) + "}");
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void EveryKeyOfTheTables_CarriesThePrefixOfTheFeature_AndIsAskedForByTheCode()
        {
            var used = new SortedSet<string>(LiteralKeys(), StringComparer.Ordinal);
            used.UnionWith(BuiltKeys());

            var problems = new List<string>();
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in KeysOf(Table(language)))
                {
                    if (!key.StartsWith("ct.", StringComparison.Ordinal)) problems.Add(language + ": " + key + " is not a key of this feature (prefix ct.)");
                    else if (!used.Contains(key)) problems.Add(language + ": " + key + " is in the table and no code asks for it");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void TheKeysThePredicatesUse_AreNotInTheTables()
        {
            // ct.offersSent and ct.tendersWon are condition types, not texts: a text under such a key would hide a mistake.
            foreach (string name in PredicateNames)
            {
                Assert.True(TextOf(Table("ru"), name) == null && TextOf(Table("en"), name) == null, name + " is a predicate, not a string key");
            }
        }

        // ---- the terms of the player ----

        private static JsonObject Translation()
        {
            string repo = Path.GetFullPath(Path.Combine(TestEnv.ModDir ?? ".", ".."));
            string path = Path.Combine(repo, "translation", "ui.json");
            if (!File.Exists(path)) return null;
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(SourceScan.ReadText(path), out node, out error), path + ": " + error);
            return node as JsonObject;
        }

        // The game's English text -> the term of the Russian translation (ptru). TSK-019: these five.
        private static readonly string[][] Terms =
        {
            new[] { "Send Offer", "Предложить" },
            new[] { "Cancel Offer", "Отозвать" },
            new[] { "Probability:", "Вероятность:" },
            new[] { "Automatically cancel offers", "Автоотмена предложений" },
            new[] { "Automatically sign contracts", "Автоподписание контрактов" },
        };

        [Test]
        public void TheTermsOfThePlayer_AreThoseOfTheRussianTranslationOfTheGame()
        {
            JsonObject translation = Translation();
            if (translation == null)
            {
                Note.Skip("StringTablesTests.TheTermsOfThePlayer", "translation/ui.json is not next to the mod folder");
                return;
            }

            // The translation still says what this feature quotes.
            foreach (string[] term in Terms)
            {
                Assert.Equal(term[1], TextOf(translation, term[0]), "ptru translates \"" + term[0] + "\" differently now");
            }

            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            // The button the feature shows is the game's own word.
            Assert.Equal("Предложить", TextOf(ru, "ct.btn.send"));

            // Where the feature names a switch of the game, it uses the term of the game.
            Assert.True(TextOf(ru, "ct.risk.autoReject").StartsWith("Автоотмена предложений", StringComparison.Ordinal), "ct.risk.autoReject");
            Assert.Equal("Автоотмена предложений", TextOf(ru, "ct.log.filter.autoReject"));
            Assert.True(TextOf(ru, "ct.log.t.capacityNotChecked").StartsWith("Автоотмена предложений", StringComparison.Ordinal), "ct.log.t.capacityNotChecked");
            Assert.True(TextOf(ru, "ct.preview.off").StartsWith("Автоподписание контрактов", StringComparison.Ordinal), "ct.preview.off");
            Assert.True(TextOf(ru, "ct.param.logAutomation.tip").Contains("«Автоподписание контрактов»"), "ct.param.logAutomation.tip");

            // The description in the hub quotes all five, in the quotes of the language, in both languages.
            string modesRu = TextOf(ru, "ct.hub.help.modes");
            string actualRu = TextOf(ru, "ct.hub.help.actual");
            string logRu = TextOf(ru, "ct.hub.help.log");
            Assert.True(modesRu.Contains("«Предложить»") && modesRu.Contains("«Отозвать»"), "the buttons of the contract window");
            Assert.True(modesRu.Contains("автоотмена предложений"), "the switch of auto-cancel");
            Assert.True(actualRu.Contains("«Вероятность:"), "the label of the probability");
            Assert.True(logRu.Contains("«Автоподписание контрактов»") && logRu.Contains("автоотменой предложений"), "the two switches");

            string modesEn = TextOf(en, "ct.hub.help.modes");
            string actualEn = TextOf(en, "ct.hub.help.actual");
            string logEn = TextOf(en, "ct.hub.help.log");
            Assert.True(modesEn.Contains("\"Send Offer\"") && modesEn.Contains("\"Cancel Offer\""), "the buttons, in the words of the original game");
            Assert.True(modesEn.Contains("\"Automatically cancel offers\""), "the switch of auto-cancel");
            Assert.True(actualEn.Contains("\"Probability:"), "the label of the probability");
            Assert.True(logEn.Contains("\"Automatically sign contracts\"") && logEn.Contains("\"Automatically cancel offers\""), "the two switches");
        }

        [Test]
        public void TheRussianTexts_DoNotUseTheWrongWordsForTheTerms()
        {
            JsonObject ru = Table("ru");
            string[] wrong = { "Автоматическая отмена", "Автоматическое подписание", "Автоподпись", "Отменить предложение", "Автоотмена предложения " };

            var problems = new List<string>();
            foreach (KeyValuePair<string, JsonNode> pair in ru)
            {
                string text = TextOf(ru, pair.Key);
                foreach (string word in wrong)
                {
                    if (text != null && text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) problems.Add(pair.Key + " says \"" + word + "\"");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        // ---- the description for the players (TSK-023) ----

        [Test]
        public void TheDescription_ExplainsTheModesTheActualProbabilityAndTheLog_InBothLanguages()
        {
            JsonObject ru = Table("ru");
            JsonObject en = Table("en");

            // The modes: the three captions the settings page shows are the three words the description explains.
            foreach (string mode in new[] { "always", "risky", "never" })
            {
                string captionRu = TextOf(ru, "ct.hub.mode." + mode);
                string captionEn = TextOf(en, "ct.hub.mode." + mode);
                Assert.True(TextOf(ru, "ct.hub.help.modes").IndexOf("«" + captionRu + "»", StringComparison.OrdinalIgnoreCase) >= 0, "ru modes: " + captionRu);
                Assert.True(TextOf(en, "ct.hub.help.modes").IndexOf("\"" + captionEn + "\"", StringComparison.OrdinalIgnoreCase) >= 0, "en modes: " + captionEn);
            }

            // The actual probability and the log have a paragraph and a heading each.
            foreach (string key in new[] { "ct.hub.help.modes.title", "ct.hub.help.actual.title", "ct.hub.help.actual", "ct.hub.help.log.title", "ct.hub.help.log" })
            {
                Assert.True(!string.IsNullOrWhiteSpace(TextOf(ru, key)) && !string.IsNullOrWhiteSpace(TextOf(en, key)), key);
            }
            Assert.True(TextOf(ru, "ct.hub.help.actual").Contains("факт."), "the suffix the mod appends");
            Assert.True(TextOf(en, "ct.hub.help.actual").Contains("actual"), "the suffix the mod appends");
            Assert.True(TextOf(ru, "ct.hub.help.log").Contains("Журнал"), "the log button");
            Assert.True(TextOf(en, "ct.hub.help.log").Contains("\"Log\""), "the log button");

            // Every key of the description is on the page, in the order of the sections.
            var onPage = new List<string>();
            foreach (string[] section in ContractTransparencyFeatureHelp.Sections) onPage.AddRange(section);
            foreach (string key in KeysOf(ru).Where(k => k.StartsWith("ct.hub.help.", StringComparison.Ordinal))) Assert.True(onPage.Contains(key), key + " is not on the page");
        }

        [Test]
        public void TheExampleOfTheDescription_IsSixtyAndFortyGivingTwentyOnePercent()
        {
            // The numbers of the text are those of the maths: 60 % and 40 %, the stronger first, give 78.9 % and 21.1 %.
            double[] odds = OddsMath.SequentialProbabilities(new[] { 60, 40 });
            Assert.Near(0.789, odds[0], 0.0005);
            Assert.Near(0.211, odds[1], 0.0005);

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = Table(language);
                string example = TextOf(table, "ct.hub.help.example");
                Assert.True(example.Contains("60") && example.Contains("40"), language + ": the two chances");
                Assert.True(example.Contains("21"), language + ": the result for the player");
                Assert.True(example.Contains("79"), language + ": the result for the competitor");
                Assert.True(example.Contains("16") && example.Contains("24"), language + ": the steps of the calculation");
                Assert.True(TextOf(table, "ct.hub.help.actual").Contains("40") && TextOf(table, "ct.hub.help.actual").Contains("21"), language + ": the label with the suffix");
                Assert.True(TextOf(table, "ct.hub.description").Contains("21"), language + ": the short description in the hub row shows the example too");
                Assert.True(TextOf(table, "ct.hub.description").Contains("60") && TextOf(table, "ct.hub.description").Contains("40"), language + ": and the two chances");
            }
        }

        // ---- money and IPS through the game's formatters ----

        [Test]
        public void MoneyAndIps_AreFormattedOnlyByTheFormattersOfTheGame()
        {
            string ui = Path.Combine(FeatureDir(), "UI", "UiKit.cs");
            string kit = SourceScan.Strip(SourceScan.ReadText(ui), false);
            Assert.True(Regex.IsMatch(kit, @"case\s+ArgKind\.Ips\s*:\s*return\s+GameFormatter\.FloatToIps\("), "IPS goes through StringFormatter.FloatToIps");
            Assert.True(Regex.IsMatch(kit, @"case\s+ArgKind\.Money\s*:\s*return\s+GameFormatter\.FloatToMoney\("), "money goes through StringFormatter.FloatToMoney");

            // Nowhere else is a number written as money or IPS, and nobody formats a number with the grouping of a culture.
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string relative = SourceScan.Relative(FeatureDir(), path);
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);

                if (relative != "UI/UiKit.cs")
                {
                    Assert.False(code.Contains("FloatToMoney("), relative + " formats money itself");
                    Assert.False(code.Contains("FloatToIps("), relative + " formats IPS itself");
                }
                Assert.False(Regex.IsMatch(code, @"ToString\(\s*""[NnCc]\d*"""), relative + " formats with the format of a culture");
                Assert.False(Regex.IsMatch(code, @"ToString\(\s*""#,"), relative + " groups digits itself");
                Assert.False(Regex.IsMatch(code, @"\{\d+:[NnCc]\d*\}"), relative + " formats with the format of a culture in a composite format");
            }

            // The table of offers in the breakdown prints the IPS column with the same formatter.
            string panel = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "UI", "OddsPanel.cs")), false);
            Assert.True(Regex.IsMatch(panel, @"formatter\.Format\(\s*PanelArg\.Ips\(\s*row\.Ips\s*\)\s*\)"), "the IPS column of the table");
        }

        // The arguments that are money or IPS, by the key of the line: {index} -> kind. Every line the builders can produce is walked; no other argument may be money or IPS,
        // and none of these may be printed another way.
        private static readonly Dictionary<string, KeyValuePair<int, ArgKind>[]> Expected = new Dictionary<string, KeyValuePair<int, ArgKind>[]>(StringComparer.Ordinal)
        {
            { "ct.review.payment", Args(0, ArgKind.Money) },
            { "ct.review.finePerUnit", Args(0, ArgKind.Money) },
            { "ct.review.breakFine", Args(0, ArgKind.Money) },
            { "ct.review.profit", Args(0, ArgKind.Money, 1, ArgKind.Money) },
            { "ct.risk.negativeProfit", Args(0, ArgKind.Money) },
            { "ct.risk.breakFine", Args(0, ArgKind.Money, 1, ArgKind.Money) },
            { "ct.business.providerFines", Args(0, ArgKind.Money) },
            { "ct.business.clientFines", Args(0, ArgKind.Money) },
            { "ct.reason.margin", Args(2, ArgKind.Money, 3, ArgKind.Money) },
            { "ct.req.ips", Args(0, ArgKind.Ips, 1, ArgKind.Ips, 3, ArgKind.Ips) },
            { "ct.req.ips.fail", Args(0, ArgKind.Ips) },
            { "ct.odds.hint50", Args(0, ArgKind.Ips) },
            { "ct.odds.hintFirst", Args(0, ArgKind.Ips) },
            { "ct.odds.potential.row", Args(2, ArgKind.Ips) },
            { "ct.log.t.offer", Args(2, ArgKind.Ips) },
        };

        private static KeyValuePair<int, ArgKind>[] Args(params object[] pairs)
        {
            var list = new List<KeyValuePair<int, ArgKind>>();
            for (int i = 0; i < pairs.Length; i += 2) list.Add(new KeyValuePair<int, ArgKind>((int)pairs[i], (ArgKind)pairs[i + 1]));
            return list.ToArray();
        }

        [Test]
        public void TheLines_PrintMoneyAsMoney_AndIpsAsIps_AndNothingElseAsEither()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var problems = new List<string>();

            foreach (PanelLine line in SampleLines())
            {
                if (string.IsNullOrEmpty(line.Key)) continue;
                if (line.Key == "ct.log.d.data") continue;   // its value is printed by the meaning of the number: TheNumbersOfALogEntry_ArePrintedByTheirMeaning
                KeyValuePair<int, ArgKind>[] expected;
                bool known = Expected.TryGetValue(line.Key, out expected);
                if (known) seen.Add(line.Key);

                for (int i = 0; i < line.Args.Length; i++)
                {
                    ArgKind kind = line.Args[i].Kind;
                    bool want = known && expected.Any(e => e.Key == i);
                    ArgKind wantKind = want ? expected.First(e => e.Key == i).Value : kind;

                    if (want && kind != wantKind) problems.Add(line.Key + " {" + i + "} is " + kind + ", it must be " + wantKind);
                    if (!want && (kind == ArgKind.Money || kind == ArgKind.Ips)) problems.Add(line.Key + " {" + i + "} is " + kind + " and the table does not know it");
                }
            }

            foreach (string key in Expected.Keys)
            {
                if (!seen.Contains(key)) problems.Add(key + " never came out of the builders: the sample does not cover it");
            }
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void TheNumbersOfALogEntry_ArePrintedByTheirMeaning()
        {
            foreach (string key in new[] { LogDataKeys.Payment, LogDataKeys.UnitCost, LogDataKeys.ProviderFines, LogDataKeys.ClientFines })
            {
                Assert.Equal(ArgKind.Money, LogLines.ValueOf(key, "194.4").Kind, key);
            }
            Assert.Equal(ArgKind.Count, LogLines.ValueOf(LogDataKeys.Units, "52000").Kind);
            Assert.Equal(ArgKind.Fraction, LogLines.ValueOf(LogDataKeys.Margin, "0.35").Kind);
            Assert.Equal(ArgKind.Actual, LogLines.ValueOf(LogDataKeys.Actual, "0.211").Kind);

            // A value that is not a number is shown as text rather than as a wrong amount of money.
            Assert.Equal(ArgKind.Text, LogLines.ValueOf(LogDataKeys.Payment, "n/a").Kind);

            // Nothing a log entry holds is written as IPS: the log keeps no IPS of its own (the tender result does, in its table).
            foreach (string key in LogLines.DataKeys()) Assert.NotEqual(ArgKind.Ips, LogLines.ValueOf(key, "1").Kind, key);
        }

        // ---- every line the builders can produce ----

        private static List<PanelLine> SampleLines()
        {
            var lines = new List<PanelLine>();

            var red = new RequirementPanelInput
            {
                Ips = RequirementCheck.Ips(0.8f, 1.2f, 0.75f),
                Power = RequirementCheck.Power(70f, 60f),
                Package = RequirementCheck.Package(1, false, false, true),
                Tolerance = 0.75f, RequiredPackageText = "DIP/PLCC", CpuPackageText = "PGA", FailsByGame = true, MismatchWithGame = true,
            };
            lines.AddRange(RequirementLines.Build(red));
            lines.AddRange(RequirementLines.Build(new RequirementPanelInput
            {
                Ips = RequirementCheck.Ips(2f, 1.2f, 0.75f), Power = RequirementCheck.Power(45f, 60f), Package = RequirementCheck.Package(0, false, false, true),
                Tolerance = 0.75f, RequiredPackageText = "any", CpuPackageText = "PGA",
            }));

            var report = new CommitmentReport
            {
                Risks = RiskFlags.CapacityShortNow | RiskFlags.Overcommit | RiskFlags.NegativeProfit | RiskFlags.BelowAutomationMargin
                        | RiskFlags.LowActualChance | RiskFlags.BreakFineExceedsCash | RiskFlags.AutoRejectWillWithdraw,
                MonthlyProfit = -5000, TermProfit = -120000, BreakFineAtStart = 888166666.67,
            };
            lines.AddRange(RiskLines.Build(report, 0.05, 1000, 40));

            List<OfferOdds> rows = OddsMath.DrawOrder(OddsMath.BestPerCompany(new[] { new OfferInput("ai", 1, 1000f, 0), new OfferInput("me", 2, 1f, 1) }), null);
            var odds = new OddsPanelInput
            {
                Rows = rows, CandidateCpuId = 2, GameChance = 1, Actual = 0, GameChancesAvailable = false, NoOffersYet = true, ReviewDateText = "x", ChanceDiffersFromVanilla = true,
                ModsCapped = true, ShadowedCount = 1, StrongestOwnName = "y", IpsFor50 = 1000, IpsForFirstInQueue = 1000, Mismatch = true,
                Sources = new List<ModSourceText> { new ModSourceText { Feature = "f", Reason = "r", Multiplier = 1.2f }, new ModSourceText { Feature = "f", Multiplier = 1f } },
            };
            lines.AddRange(OddsLines.Build(odds));
            odds.CandidateCpuId = 1;
            lines.AddRange(OddsLines.Build(odds));

            ReviewPlan plan = ReviewLines.Build(new ReviewInput
            {
                ContractLabel = "c", CpuName = "n", Commitments = report, PaymentPerUnit = 299f, FinePerUnit = 12f,
            });
            lines.AddRange(plan.Contract);
            lines.AddRange(plan.Offer);
            lines.AddRange(plan.Risks);

            lines.AddRange(DealLines.Build(new DealInput { Provider = "p", Client = "c", Exclusive = true, ProviderFines = 1000f, ClientFines = 2000f }));

            lines.AddRange(PotentialLines.Build(new PotentialPanelInput
            {
                MovesLeft = 2, CurrentActual = 1.0, PessimisticActual = 0.28, HardDifficulty = true, MaxRows = 12,
                Rows = new List<PotentialRow> { new PotentialRow { Company = "AMD", Cpu = "Am286", Ips = 50000f } },
            }));

            // The log: the sentence with the margin, and the details of an entry with every kind of number.
            LogEntry margin = LogEntry.Create("1986-12-31", LogTypes.AutomationDecision, LogSources.Automation);
            margin.reasonKey = "ct.reason.margin";
            margin.Put(LogDataKeys.Margin, 0.35).Put(LogDataKeys.Threshold, 0.4).Put(LogDataKeys.Payment, 194.4).Put(LogDataKeys.UnitCost, 150.0);
            lines.Add(LogLines.Sentence(margin));
            lines.AddRange(LogLines.Details(margin));

            var tender = new TenderResultDto
            {
                outcome = TenderOutcomeClassifier.OutcomeLost, reasonKey = "ct.tender.outbid", playerCpuName = "Mine", playerGameChance = 40, playerActual = 0.211,
                winnerCompany = "AMD", winnerCpuName = "Am286",
                offers = new List<OfferRowDto> { new OfferRowDto { company = "AMD", cpu = "Am286", ips = 50000, chance = 60, actual = 0.789 }, new OfferRowDto { company = "Me", cpu = "Mine", ips = 30000, chance = 40, actual = 0.211, player = true } },
                pruned = new List<PrunedRowDto> { new PrunedRowDto { company = "Me", cpu = "Mine", required = 3, contracted = 2, capacity = 4, player = true }, new PrunedRowDto { company = "X", cpu = "Y", required = 3, contracted = 2, capacity = 4, maker = "Foundry" } },
                capacityNotChecked = true,
            };
            lines.AddRange(LogLines.TenderDetails(tender));

            lines.Add(ProbabilityLabel.Line("40%", 0.21));
            return lines;
        }
    }

    /// <summary>The sections of the description on the settings page of the hub, as the feature draws them (the test reads the very array the page is built from).</summary>
    internal static class ContractTransparencyFeatureHelp
    {
        internal static string[][] Sections
        {
            get { return HelpSectionsFromSource(); }
        }

        // The feature class touches the game and cannot be loaded by the tests; its table of sections is read from the source.
        private static string[][] HelpSectionsFromSource()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency", "ContractTransparencyFeature.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);
            Match table = Regex.Match(code, @"HelpSections\s*=\s*\{(?<rows>.*?)\n\s*\};", RegexOptions.Singleline);
            Assert.True(table.Success, "the table of the sections of the description is in ContractTransparencyFeature.cs");

            var sections = new List<string[]>();
            foreach (Match row in Regex.Matches(table.Groups["rows"].Value, @"new\[\]\s*\{(?<keys>[^}]*)\}"))
            {
                sections.Add(Regex.Matches(row.Groups["keys"].Value, "\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToArray());
            }
            return sections.ToArray();
        }
    }
}
