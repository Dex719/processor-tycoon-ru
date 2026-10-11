using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// What the six effect types of the non-standard services share, and the content and the glue they ship with (TSK-009 to TSK-014): the check of the <c>params</c> that the handlers
    /// run when the content is loaded, run here on the shipped <c>events.json</c>; the strings of the events; the arguments of the table; the registration of the handlers and of the
    /// provider in the entry class, which needs the game and is therefore read as source.
    /// </summary>
    [Covers(typeof(HistExpense), typeof(HistIncome), typeof(IHistEconomy), typeof(IHistModifiers), typeof(IDelayedEventFirer), typeof(HistTime), typeof(HistMoney),
        typeof(HistEffectTypes), typeof(HistArgs), typeof(HistEffectRules))]
    public class HistEffectRulesTests
    {
        private static readonly string[] ExpectedEvents =
        {
            "hist.decide.antitrust.fine", "hist.decide.antitrust.consent", "hist.decide.antitrust.reimpose", "hist.decide.settlement",
            "hist.2022.export.rule", "hist.2023.export.update", "hist.decide.export", "hist.decide.export.update",
            "hist.decide.pricewar.aggressor", "hist.decide.pricewar.follower",
            "hist.decide.patent.plaintiff", "hist.decide.patent.withdrawal", "hist.decide.patent.infringement",
        };

        private static string ContentDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events");
        }

        private static JsonObject[] Records()
        {
            JsonObject pack = HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(ContentDir(), "events.json")));
            JsonArray records = pack.GetArray("records");
            Assert.NotNull(records, "events.json has records");
            return records.Select(r => (JsonObject)r).ToArray();
        }

        private static IEnumerable<KeyValuePair<string, JsonObject>> EffectsOf(JsonObject record)
        {
            JsonArray common = record.GetArray("effects");
            if (common != null)
            {
                foreach (JsonNode node in common) yield return new KeyValuePair<string, JsonObject>("effects", (JsonObject)node);
            }

            JsonArray choices = record.GetArray("choices");
            if (choices == null) yield break;
            foreach (JsonNode choice in choices)
            {
                JsonArray effects = ((JsonObject)choice).GetArray("effects");
                if (effects == null) continue;
                foreach (JsonNode node in effects) yield return new KeyValuePair<string, JsonObject>("choice " + ((JsonObject)choice).GetString("id"), (JsonObject)node);
            }
        }

        private static Dictionary<string, string> Table(string language)
        {
            JsonObject table = HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "historical-events.json")));
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, JsonNode> pair in table)
            {
                var value = pair.Value as JsonValue;
                if (value != null && value.Kind == JsonKind.String) result[pair.Key] = value.StringValue;
            }
            return result;
        }

        // ---- the shipped events ----

        [Test]
        public void ThePackHasTheThirteenEventsOfTheseTasks_WithTheIdsOfTheDeliverables()
        {
            string[] ids = Records().Select(r => r.GetString("id")).ToArray();
            foreach (string expected in ExpectedEvents) Assert.True(ids.Contains(expected), "events.json lacks " + expected);
            Assert.Equal(ids.Length, ids.Distinct().Count(), "ids are unique");
            foreach (string id in ids) Assert.True(id.StartsWith("hist."), id + " carries the prefix of the feature");
        }

        [Test]
        public void EveryEffectOfTheFeaturesOwnTypes_PassesTheCheckTheHandlerRunsAtLoad()
        {
            // The standard types of the engine (the calendar's market and cost effects) are judged by BuiltinCalendarTests; here only the feature's own types.
            string[] standard = { "MarketDemand", "ModifierTimed", "FactoryOutput", "Money", "Reputation" };
            int checkedEffects = 0;
            foreach (JsonObject record in Records())
            {
                foreach (KeyValuePair<string, JsonObject> effect in EffectsOf(record))
                {
                    string type = effect.Value.GetString("type");
                    if (Array.IndexOf(standard, type) >= 0) continue;
                    Assert.True(HistEffectRules.IsOwnType(type), record.GetString("id") + " (" + effect.Key + ") uses the type " + type + ", which this part does not own");

                    var errors = new List<string>();
                    JsonObject raw = effect.Value.GetObject("params") ?? new JsonObject();
                    Assert.True(HistEffectRules.Validate(type, raw, errors), record.GetString("id") + " (" + effect.Key + "): " + string.Join(" | ", errors));
                    checkedEffects++;
                }
            }
            Assert.True(checkedEffects >= 25, "the scan found the effects: " + checkedEffects);
        }

        [Test]
        public void TheEventsHaveTheShapeTheEngineDemands()
        {
            foreach (JsonObject record in Records())
            {
                string id = record.GetString("id");
                Assert.Equal("history", record.GetString("category"), id + ": the category of the pack");
                Assert.Equal("Manual", record.GetObject("trigger").GetString("type"), id + ": fired from code only");

                JsonObject targets = record.GetObject("targets");
                Assert.NotNull(targets, id);
                if (targets.GetString("type") == "Specific") Assert.Equal("player", targets.GetString("companyKey"), id + ": a Specific target needs a key; the firing names the real one");

                JsonArray choices = record.GetArray("choices");
                if (choices == null || choices.Count == 0) continue;

                Assert.True(record.GetInt32("timerDays") >= 1, id + ": an event with choices has a timer");
                string fallback = record.GetString("defaultChoice");
                JsonObject chosen = choices.Select(c => (JsonObject)c).FirstOrDefault(c => c.GetString("id") == fallback);
                Assert.NotNull(chosen, id + ": the default choice is one of the choices");
                Assert.False(chosen.ContainsKey("requires"), id + ": the default choice is always available");
                Assert.True(record.GetBool("important"), id + ": a choice for the player opens the window");
                Assert.True(record.GetBool("notifyPlayer"), id + ": and an AI target is news");
                Assert.Equal(choices.Count, choices.Select(c => ((JsonObject)c).GetString("id")).Distinct().Count(), id + ": choice ids are unique");
            }
        }

        [Test]
        public void EveryTextKeyOfTheEvents_HasATextInBothLanguages()
        {
            Dictionary<string, string> ru = Table("ru");
            Dictionary<string, string> en = Table("en");

            foreach (JsonObject record in Records())
            {
                var keys = new List<string>();
                foreach (string field in new[] { "title", "text", "cause" })
                {
                    JsonObject loc = record.GetObject(field);
                    Assert.NotNull(loc, record.GetString("id") + " has " + field);
                    keys.Add(loc.GetString("key"));
                }
                JsonArray choices = record.GetArray("choices");
                if (choices != null) keys.AddRange(choices.Select(c => ((JsonObject)c).GetString("textKey")));

                foreach (string key in keys)
                {
                    Assert.True(key != null && key.StartsWith("hist."), record.GetString("id") + ": " + key + " carries the prefix");
                    Assert.True(ru.ContainsKey(key) && ru[key].Trim().Length > 0, "ru lacks " + key);
                    Assert.True(en.ContainsKey(key) && en[key].Trim().Length > 0, "en lacks " + key);
                }
            }
        }

        [Test]
        public void TheLinesOfTheEffects_AllHaveTextsInBothLanguages()
        {
            Dictionary<string, string> ru = Table("ru");
            Dictionary<string, string> en = Table("en");
            string[] keys =
            {
                "hist.fx.case.fine", "hist.fx.case.consent", "hist.fx.case.warning", "hist.fx.case.pay", "hist.fx.case.provision", "hist.fx.case.appeal", "hist.fx.case.settle",
                "hist.fx.case.fight", "hist.fx.case.reimposePay", "hist.fx.case.reimposeAppeal", "hist.fx.settlement.sue", "hist.fx.settlement.decline",
                "hist.fx.export.open", "hist.fx.export.update", "hist.fx.export.comply", "hist.fx.export.license", "hist.fx.export.lobby",
                "hist.fx.war.start", "hist.fx.war.decline", "hist.fx.war.join", "hist.fx.war.hold", "hist.fx.war.marketing",
                "hist.fx.dispute.open", "hist.fx.dispute.arbitrate", "hist.fx.dispute.license", "hist.fx.dispute.independent", "hist.fx.dispute.fight", "hist.fx.dispute.settle",
                "hist.fx.dispute.redesign", "hist.fx.dispute.sue", "hist.fx.dispute.decline", "hist.fx.race.frequency", "hist.fx.race.cores",
                "hist.mod.remedy", "hist.mod.export", "hist.mod.race.frequency", "hist.mod.race.cores", "hist.mod.dispute", "hist.mod.warMarketing",
                CaseKeys.ReputationFine, CaseKeys.ReputationPaid, CaseKeys.ReputationConsent, CaseKeys.ReputationCleared, CaseKeys.ReputationWarning,
                CaseKeys.NoteAnnulled, CaseKeys.NoteAnnulledRefund, CaseKeys.NoteReduced, CaseKeys.NoteUpheld, CaseKeys.NoteSecondReduced, CaseKeys.NoteSecondUpheld,
                CaseKeys.NoteConsentWon, CaseKeys.NoteConsentLost, SettlementKeys.NoteWon, SettlementKeys.NoteLost, SettlementKeys.NotePaid,
                ExportKeys.NoteLicenseGranted, ExportKeys.NoteLicenseDenied, ExportKeys.NoteLobbyWon, ExportKeys.NoteLobbyLost, ExportKeys.ReasonExport,
                PriceWarCore.NoteEnded, PriceWarCore.MarketingReasonKey, DisputeKeys.NoteWon, DisputeKeys.NoteFightWon, DisputeKeys.NoteLost, DisputeKeys.ResearchReasonKey,
                CaseCore.RemedyReasonKey, "hist.segment.highend", "hist.segment.midrange", "hist.segment.lowend", "hist.segment.industries",
            };
            foreach (string key in keys)
            {
                Assert.True(ru.ContainsKey(key), "ru lacks " + key);
                Assert.True(en.ContainsKey(key), "en lacks " + key);
            }
            foreach (string plural in new[] { "hist.fx.months", "hist.fx.days" })
            {
                Assert.True(ru.ContainsKey(plural + ".one") && ru.ContainsKey(plural + ".few") && ru.ContainsKey(plural + ".many"), "ru plural group " + plural);
                Assert.True(en.ContainsKey(plural + ".one") && en.ContainsKey(plural + ".other"), "en plural group " + plural);
            }
        }

        [Test]
        public void ThePackCategory_IsDefinedByTheConfigOfThePack()
        {
            JsonObject config = HistFixture.ObjFromFile(SourceScan.ReadText(Path.Combine(ContentDir(), "config.json")));
            Assert.Equal(1, config.GetInt32("schema"));
            JsonArray categories = config.GetArray("categories");
            Assert.NotNull(categories);
            Assert.True(categories.Any(c => ((JsonObject)c).GetString("id") == "history"), "the category history");
        }

        [Test]
        public void TheFineEvent_CarriesTheNumbersOfTheDesignExample()
        {
            JsonObject fine = Records().Single(r => r.GetString("id") == "hist.decide.antitrust.fine");
            JsonObject open = fine.GetArray("effects").Select(e => (JsonObject)e).Single().GetObject("params");
            CaseParams p = CaseParams.Read(open, null);

            Assert.Equal(CaseModes.Fine, p.Mode);
            Assert.Equal(0.04f, p.FinePct);
            Assert.Equal("contract.reward", p.RemedyChannel);
            Assert.Equal(0.95f, p.RemedyMultiplier);
            Assert.Equal(730, p.RemedyDays);
            Assert.Equal(60, p.AppealMonths);
            Assert.Equal(0.35f, p.PAnnul);
            Assert.Equal(0.355f, p.ReimposeShare);
            Assert.True(p.Reimpose);
            Assert.Equal(15, p.SecondAppealMonths);
            Assert.Equal("appeal", fine.GetString("defaultChoice"));
            Assert.Equal(30, fine.GetInt32("timerDays"));

            JsonObject[] choices = fine.GetArray("choices").Select(c => (JsonObject)c).ToArray();
            Assert.Equal(new[] { "pay", "provision", "appeal" }, choices.Select(c => c.GetString("id")).ToArray());
            Assert.Equal(new[] { 0.3, 0.3, 0.4 }, choices.Select(c => c.GetDouble("aiWeight")).ToArray());
        }

        [Test]
        public void TheExportEvents_CarryTheTwoStagesOfTheRule()
        {
            ExportParams first = ExportParams.Read(ParamsOf("hist.2022.export.rule", 0), null);
            Assert.Equal(ExportActions.Open, ExportParams.ActionOf(ParamsOf("hist.2022.export.rule", 0)));
            Assert.Equal(0.15f, first.BlockedShare);
            Assert.Equal(0.90f, first.Percentile);

            ExportParams second = ExportParams.Read(ParamsOf("hist.2023.export.update", 0), null);
            Assert.Equal(ExportActions.Update, ExportParams.ActionOf(ParamsOf("hist.2023.export.update", 0)));
            Assert.Equal(0.20f, second.BlockedShare);
            Assert.Equal(0.75f, second.Percentile);
        }

        [Test]
        public void TheSettlementEvent_NeedsTenPercentOfTheMarketForTheSuit()
        {
            JsonObject settlement = Records().Single(r => r.GetString("id") == "hist.decide.settlement");
            JsonObject sue = settlement.GetArray("choices").Select(c => (JsonObject)c).Single(c => c.GetString("id") == "sue");
            JsonObject requires = sue.GetObject("requires");
            Assert.NotNull(requires, "the suit has a condition");
            Assert.Equal("hist.ShareAtLeast", requires.GetString("type"));
            Assert.Near(0.10, requires.GetDouble("value"), 1e-9);
            Assert.Equal(36, requires.GetInt32("months"));
        }

        private static JsonObject ParamsOf(string eventId, int effectIndex)
        {
            JsonObject record = Records().Single(r => r.GetString("id") == eventId);
            return ((JsonObject)record.GetArray("effects")[effectIndex]).GetObject("params");
        }

        // ---- the check itself ----

        [Test]
        public void TheCheck_RejectsABadNumber_PassesAGoodOne_AndIgnoresTypesItDoesNotOwn()
        {
            var errors = new List<string>();
            Assert.False(HistEffectRules.Validate(HistEffectTypes.AntitrustCase, HistFixture.Obj("{'finePct':0.9}"), errors));
            Assert.Equal(1, errors.Count);

            Assert.True(HistEffectRules.Validate(HistEffectTypes.AntitrustCase, HistFixture.Obj("{'finePcts':0.04}"), null), "an unknown member is a warning, not a rejection");
            Assert.True(HistEffectRules.Validate("MarketDemand", HistFixture.Obj("{'anything':true}"), null), "a type of the engine is not judged here");
            Assert.True(HistEffectRules.Validate(HistEffectTypes.PriceWar, null, null), "no params at all reads as the defaults");

            Assert.False(HistEffectRules.Validate(HistEffectTypes.AttributeRace, HistFixture.Obj("{'bonus':2}"), null));
            Assert.False(HistEffectRules.Validate(HistEffectTypes.ExportControl, HistFixture.Obj("{'blockedShare':0.4}"), null));
            Assert.False(HistEffectRules.Validate(HistEffectTypes.PatentDispute, HistFixture.Obj("{'mode':'infringement','action':'arbitrate'}"), null));
            Assert.False(HistEffectRules.Validate(HistEffectTypes.AntitrustSettlement, HistFixture.Obj("{'pWin':3}"), null));
            Assert.False(HistEffectRules.Validate(HistEffectTypes.PriceWar, HistFixture.Obj("{'cut':0.9}"), null));

            Assert.Equal(6, HistEffectTypes.Services.Length);
            Assert.True(HistEffectRules.IsServiceType("PriceWar"));
            Assert.False(HistEffectRules.IsServiceType("Money"));
        }

        [Test]
        public void TheArgumentsOfTheTable_AreReadByPosition_AndMissingOnesAreEmpty()
        {
            string[] args = { "entry", "Intel", "AMD", "ai:1", "eu2009" };
            Assert.Equal("entry", HistArgs.At(args, HistArgs.EntryIndex));
            Assert.Equal("ai:1", HistArgs.At(args, HistArgs.SecondKeyIndex));
            Assert.Equal(string.Empty, HistArgs.At(args, 9));
            Assert.Equal(string.Empty, HistArgs.At(null, 0));
            Assert.Equal(string.Empty, HistArgs.At(args, -1));

            Assert.Equal("named", HistArgs.IdOf("named", args), "the effect's own id wins");
            Assert.Equal("eu2009", HistArgs.IdOf(null, args), "then the first extra argument");
            Assert.Equal("entry", HistArgs.IdOf(string.Empty, new[] { "entry", "Intel" }), "then the calendar entry");
            Assert.Equal(string.Empty, HistArgs.IdOf(null, null));
        }

        [Test]
        public void TheSmallRules_OfMoneyAndTime()
        {
            Assert.Equal(0, HistTime.MonthsToDays(0));
            Assert.Equal(0, HistTime.MonthsToDays(-3));
            Assert.Equal(730, HistTime.MonthsToDays(24));
            Assert.Equal(1826, HistTime.MonthsToDays(60));
            Assert.Equal(0f, HistMoney.Positive(float.NaN));
            Assert.Equal(0f, HistMoney.Positive(-5f));
            Assert.Equal(0f, HistMoney.Positive(float.PositiveInfinity));
            Assert.Equal(7f, HistMoney.Positive(7f));
            Assert.Near(20, HistMoney.Of(0.02f, 1000f), 1e-4);
            Assert.Equal(0f, HistMoney.Of(-1f, 1000f));
            Assert.Equal(3f, HistMoney.Finite(float.NaN, 3f));
            Assert.Equal(2f, HistMoney.Finite(2f, 3f));
        }

        // ---- the glue, read as source ----

        private static string Code(params string[] relative)
        {
            string path = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents" }.Concat(relative).ToArray());
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheEntryClass_RegistersTheEightHandlers_TheProvider_AndTheTicksOfTheServices()
        {
            string feature = Code("HistoricalEventsFeature.cs");

            foreach (string handler in new[] { "CaseService", "SettlementService", "ExportRuleService", "PriceWarService", "DisputeService", "RaceService", "FlawBridge", "HistFlagEffect" })
                Assert.True(Regex.IsMatch(feature, @"new\s+" + handler + @"\s*\(\s*_services\s*\)"), handler + " is made and registered");
            Assert.True(Regex.IsMatch(feature, @"EventEffects\.Register\(\s*handler\.Type\s*,\s*handler\s*\)"), "with EventEffects.Register");

            Assert.Equal(2, Regex.Matches(feature, @"ModModifiers\.RegisterReasonedProvider\(").Count, "two providers, each registered once");
            Assert.True(Regex.IsMatch(feature, @"RegisterReasonedProvider\(\s*FeatureId\s*,\s*ModChannels\.MarketRatingCpu\s*,\s*_services\.Rating\.Provide\s*\)"), "on market.rating.cpu");
            Assert.True(Regex.IsMatch(feature, @"RegisterReasonedProvider\(\s*FeatureId\s*,\s*ModChannels\.AcquisitionPrice\s*,\s*_services\.Acquisition\.Multiplier\s*\)"), "and on acquisition.price (TSK-028)");

            foreach (string priority in new[] { "Cases", "Settlements", "ExportRules", "Disputes", "Races" })
                Assert.True(feature.Contains("HistTickPriority." + priority), "the monthly pass of " + priority + " has its priority");
            Assert.True(Regex.IsMatch(feature, @"TickPhase\.DayPassed\s*,\s*\(\)\s*=>\s*ModHooks\.Guard\([^;]*priceWars/day"), "the war ends on the daily pass");
            Assert.True(feature.Contains("_runtime.State.CaseActive") && feature.Contains("_runtime.State.ExportThreshold"), "the predicates ask the services");
            Assert.True(feature.Contains("ModCompanies.Changed += OnCompaniesChanged"), "a bankruptcy gives the prices back");
        }

        [Test]
        public void EveryHandler_IsGuardedByTheBase_AndTheServiceCallsAreInsideTryCatch()
        {
            string baseCode = Code("Effects", "HistEffectBase.cs");
            foreach (string method in new[] { "Validate", "Resolve", "Apply", "Describe" })
                Assert.True(Regex.IsMatch(baseCode, @"public\s+\w+\s+" + method + @"\s*\([^)]*\)\s*\{\s*try"), method + " runs inside try/catch");
            Assert.True(baseCode.Contains("ModHooks.ReportFault"), "and reports the fault against the feature");

            foreach (string file in new[] { "CaseService", "SettlementService", "ExportRuleService", "PriceWarService", "DisputeService", "RaceService", "FlawBridge", "HistFlagEffect" })
            {
                string code = Code("Effects", file + ".cs");
                Assert.True(Regex.IsMatch(code, @"class\s+" + file + @"\s*:\s*HistEffectBase"), file + " is a HistEffectBase");
                Assert.False(code.Contains("UnityEngine"), file + " keeps the game's engine out");
            }
        }

        [Test]
        public void OnlyTheAdapterOfPrices_WritesThePriceOfACpu()
        {
            foreach (string path in SourceScan.Files(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents"), ".cs"))
            {
                string name = Path.GetFileName(path);
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                bool writes = Regex.IsMatch(code, @"\.(Price|TargetPrice)\s*=[^=]");
                if (name == "GamePrices.cs") continue;
                Assert.False(writes, name + " writes a price of a CPU; only GamePrices does (and the logic goes through IMarketPrices)");
            }
        }

        [Test]
        public void ThePureLogic_NeverNamesTheGameOrUnity()
        {
            foreach (string path in SourceScan.Files(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents", "Logic"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(code.Contains("UnityEngine"), Path.GetFileName(path) + " uses UnityEngine");
                Assert.False(Regex.IsMatch(code, @"ProcessorTycoon\.|PTMod\.Features\.(EventSystem|RivalPersonalities|DesignFlawRecalls)"), Path.GetFileName(path) + " names the game or another feature");
            }
        }

        [Test]
        public void TheRivalApiIsReachedOnlyThroughItsNullObject_AndByItsOwnClassName()
        {
            string prices = Code("Effects", "GamePrices.cs");
            Assert.True(prices.Contains("RivalPersonalitiesApi.Instance"), "the API of rival-personalities is read from its public holder, a null-object while the feature is off");
            Assert.False(Regex.IsMatch(prices, @"RivalPersonalities\.Api"), "RivalPersonalities.Api does not compile from a sibling namespace");
            Assert.True(prices.Contains("ApplyPriceCut") && prices.Contains("RestorePriceCut"));
        }
    }
}
