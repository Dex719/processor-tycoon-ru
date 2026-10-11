using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Json;
using PTMod.Features.EventSystem;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// TSK-025 (FR-12, NFR-4, NFR-8): the starter set, the shipped <c>Content/event-system/events.json</c> and <c>config.json</c> with the strings of both languages.
    /// The ten records pass the validator with no complaint (the same file the game loads, not a copy), every record is the row of the balance table of design.md
    /// ("Стартовый набор: баланс"), the set uses every standard effect type but <c>MarketDemand</c>, has a choice with a timer, a chance, an exclusive group, a
    /// <c>FireEvent</c> and the targets <c>Player</c>, <c>RandomAI</c> and <c>RandomCompany</c>, its conditions are the predicates of the core and the engine's own, its
    /// strings are in <c>ru</c> and <c>en</c> under the prefix <c>es.</c>, and it writes nothing into <c>market.rating.cpu</c> (NFR-8). The last tests fire every event
    /// in the rig of the choices and read the letters in both languages.
    /// </summary>
    [Covers(typeof(PackCatalog), typeof(ContentValidator), typeof(EventConfig), typeof(PackConfigReader), typeof(StandardEffectRules))]
    public class StarterContentTests
    {
        // an "es.name" that is not the end of a word ("lines.")
        private static readonly Regex KeyShowing = new Regex(@"(^|[^A-Za-z])es\.[a-z_]", RegexOptions.Compiled);

        // ---------------------------------------------------------------------------------------------------------------
        // The balance table of design.md, row by row. A slot is "common" (the effects of the event itself) or the id of a choice; the effect is written as the
        // type, its params with sorted keys and the duration: Money{direction=Spend;max=25000000;...}/365d
        // ---------------------------------------------------------------------------------------------------------------

        private sealed class Row
        {
            public string Id;
            public string Trigger;
            public string Category;
            public string Target;
            public float Weight;
            public int Cooldown;
            public int Timer;
            public string Default;
            public bool Notify;
            public string[] Slots;
        }

        private static Row Daily(string id, string target, float weight, int cooldown, int timer, string defaultChoice, params string[] slots)
        {
            return new Row { Id = id, Trigger = "DailyChance", Category = "universal", Target = target, Weight = weight, Cooldown = cooldown, Timer = timer, Default = defaultChoice, Slots = slots };
        }

        private static readonly Row[] Table =
        {
            Daily("es.star_engineer", "Player", 10, 730, 14, "decline",
                "hire=Money{direction=Spend;max=25000000;min=50000;origin=Research;revenueFraction=[0.04,0.06]} + ModifierTimed{channel=research.speed;multiplier=1.12}/365d",
                "decline="),
            Daily("es.university_grant", "Player", 8, 730, 21, "decline",
                "fund=Money{direction=Spend;max=15000000;min=40000;origin=Research;revenueFraction=[0.03,0.05]} + ResearchProgress{delta=0.2}",
                "decline="),
            Daily("es.trade_show", "Player", 10, 365, 14, "skip",
                "big_booth=Money{direction=Spend;max=12000000;min=40000;origin=Development;revenueFraction=[0.03,0.04]} + ModifierTimed{channel=popularity.gain;multiplier=1.25}/120d",
                "small_booth=Money{direction=Spend;max=4000000;min=15000;origin=Development;revenueFraction=[0.01,0.015]} + ModifierTimed{channel=popularity.gain;multiplier=1.1}/90d",
                "skip="),
            Daily("es.tax_audit", "Player", 6, 1095, 10, "cooperate",
                "cooperate=Money{direction=Spend;max=8000000;min=20000;origin=Taxes;revenueFraction=[0.01,0.02]}",
                "lawyers=Money{direction=Spend;max=3000000;min=10000;origin=Fines;revenueFraction=[0.005,0.008]} + Money{chance=0.45;direction=Spend;max=15000000;min=40000;origin=Fines;revenueFraction=[0.03,0.05]}"),
            Daily("es.factory_strike", "RandomCompany/FactoryCapacity", 6, 730, 7, "refuse",
                "raise_wages=ModifierTimed{channel=factory.operatingCost;multiplier=1.05}/365d",
                "refuse=FactoryOutput{multiplier=0.6}/21d + Reputation{chance=0.5;delta=-3}"),
            Daily("es.patent_troll", "Player", 6, 1095, 14, "settle",
                "settle=Money{direction=Spend;max=10000000;min=30000;origin=Fines;revenueFraction=[0.02,0.03]}",
                "fight=Money{direction=Spend;max=3000000;min=10000;origin=Fines;revenueFraction=[0.005,0.01]}"
                + " + FireEvent{chance=0.6;delayDays=[60,120];eventId=es.patent_win;exclusiveGroup=verdict}"
                + " + FireEvent{chance=0.4;delayDays=[60,120];eventId=es.patent_loss;exclusiveGroup=verdict}"),
            new Row { Id = "es.patent_win", Trigger = "Manual", Target = "Player", Weight = 1, Slots = new[] { "common=Reputation{delta=3} + SendMail{text=es.patent_win.mail.text;title=es.patent_win.mail.title}" } },
            new Row { Id = "es.patent_loss", Trigger = "Manual", Target = "Player", Weight = 1,
                      Slots = new[] { "common=Money{direction=Spend;max=25000000;min=80000;origin=Fines;revenueFraction=[0.05,0.08]} + Reputation{delta=-2}" } },
            Daily("es.charity_gala", "Player", 5, 730, 14, "decline",
                "donate=Money{direction=Spend;max=5000000;min=10000;origin=Development;revenueFraction=[0.01,0.02]} + Reputation{delta=4}",
                "decline="),
            new Row { Id = "es.rival_scandal", Trigger = "DailyChance", Category = "universal", Target = "RandomAI/MarketShare", Weight = 5, Cooldown = 365, Notify = true,
                      Slots = new[] { "common=Reputation{delta=-6} + Popularity{cpuSelector=AllMarket;delta=-0.03} + ModifierTimed{channel=popularity.gain;multiplier=0.85}/120d" } },
        };

        // ---------------------------------------------------------------------------------------------------------------
        // The loading
        // ---------------------------------------------------------------------------------------------------------------

        private static void WithStarter(Action<FakeEnvironment, LogCapture, PackCatalog> body)
        {
            EventTestKit.WithStarterCatalog(body);
        }

        private static JsonObject ShippedEvents()
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(EventTestKit.ShippedContent("event-system", "events.json"), out node, out error), "events.json is JSON: " + error);
            return (JsonObject)node;
        }

        [Test]
        public void TheShippedFile_IsTheRecordFileOfSchema1_WithTheTenIdsOfTheTable()
        {
            JsonObject file = ShippedEvents();
            Assert.Equal(1, file.GetInt32("schema", 0), "schema (FR-10 of the framework)");
            JsonArray records = file.GetArray("records");
            Assert.NotNull(records);
            var ids = new List<string>();
            foreach (JsonNode record in records) ids.Add(((JsonObject)record).GetString("id"));
            Assert.Equal(string.Join(",", Table.Select(r => r.Id).ToArray()), string.Join(",", ids.ToArray()), "the ids, in the order of the table");
        }

        [Test]
        public void TheStarterContent_PassesTheValidator_WithNoLineInTheLog()
        {
            WithStarter((env, log, catalog) =>
            {
                Assert.Equal(10, catalog.All.Count, "all ten records are accepted");
                Assert.Equal(0, log.Warnings.Count, "the log:\n  " + EventTestKit.Join(log.Warnings));
                PackInfo pack = catalog.PackOf("event-system");
                Assert.Equal(10, pack.EventCount);
                Assert.Equal(0, pack.SkippedCount);
                foreach (EventDefinition definition in catalog.All)
                {
                    Assert.Equal("event-system", definition.packId, definition.id + " belongs to the engine's pack");
                    Assert.True(definition.id.StartsWith("es.", StringComparison.Ordinal), "the id carries the prefix of the feature: " + definition.id);
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The balance table
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void EveryRecord_IsARowOfTheBalanceTable()
        {
            WithStarter((env, log, catalog) =>
            {
                foreach (Row row in Table)
                {
                    EventDefinition definition = catalog.Get(row.Id);
                    Assert.NotNull(definition, row.Id + " is loaded");

                    Assert.Equal(row.Trigger, definition.trigger.type.ToString(), row.Id + " trigger");
                    Assert.Equal(row.Category, definition.category, row.Id + " category");
                    Assert.Equal(row.Target, TargetOf(definition), row.Id + " target");
                    Assert.Equal(row.Weight, definition.weight, row.Id + " weight");
                    Assert.Equal(row.Cooldown, definition.cooldownDays, row.Id + " cooldown");
                    Assert.Equal(row.Timer, definition.timerDays, row.Id + " timer");
                    Assert.Equal(row.Default, definition.defaultChoice, row.Id + " default choice");
                    Assert.Equal(row.Notify, definition.notifyPlayer, row.Id + " notifyPlayer");

                    var slots = new List<string>();
                    if (definition.effects.Length > 0) slots.Add("common=" + Canon(definition.effects));
                    foreach (EventChoice choice in definition.choices) slots.Add(choice.id + "=" + Canon(choice.effects));
                    Assert.Equal(string.Join("\n", row.Slots), string.Join("\n", slots.ToArray()), row.Id + " effects");
                }
            });
        }

        [Test]
        public void TheChoicesOfTheStarterSet_CarryTheDesignedAiWeights_AndTheGrantNeedsAResearch()
        {
            WithStarter((env, log, catalog) =>
            {
                Assert.Equal(0.5f, catalog.Get("es.patent_troll").choices.First(c => c.id == "fight").aiWeight, "the AI is wary of a lawsuit");
                foreach (EventDefinition definition in catalog.All)
                {
                    foreach (EventChoice choice in definition.choices)
                    {
                        if (definition.id == "es.patent_troll" && choice.id == "fight") continue;
                        Assert.Equal(1f, choice.aiWeight, definition.id + "/" + choice.id + " has the default weight");
                    }
                }

                // M11: the button "Профинансировать" is grey while nothing is researched; the way out, the default choice, is never blocked
                EventDefinition grant = catalog.Get("es.university_grant");
                Assert.NotNull(grant.choices.First(c => c.id == "fund").requires, "fund needs a research");
                EventChoice defaultChoice = grant.choices.First(c => c.id == grant.defaultChoice);
                Assert.Null(defaultChoice.requires, "the default choice has no requires (FR-1)");
                foreach (EventDefinition definition in catalog.All)
                {
                    if (definition.id == "es.university_grant") continue;
                    foreach (EventChoice choice in definition.choices) Assert.Null(choice.requires, definition.id + "/" + choice.id + " has no requires");
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // What the set uses
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void TheSet_UsesEveryStandardEffectType_ButMarketDemand()
        {
            WithStarter((env, log, catalog) =>
            {
                var used = new SortedSet<string>(StringComparer.Ordinal);
                foreach (EventDefinition definition in catalog.All)
                {
                    foreach (EventEffect effect in definition.effects) used.Add(effect.type);
                    foreach (EventChoice choice in definition.choices)
                    {
                        foreach (EventEffect effect in choice.effects) used.Add(effect.type);
                    }
                }

                var standard = new SortedSet<string>(new[] { "Money", "ModifierTimed", "FactoryOutput", "MarketDemand", "Popularity", "Reputation", "ResearchProgress", "SendMail", "FireEvent" },
                    StringComparer.Ordinal);
                standard.Remove("MarketDemand");
                Assert.Equal(string.Join(",", standard.ToArray()), string.Join(",", used.ToArray()), "all of them, and nothing else");
            });
        }

        [Test]
        public void TheSet_HasAChoiceWithATimer_AChance_AnExclusiveGroup_AFireEvent_AndThreeKindsOfTarget()
        {
            WithStarter((env, log, catalog) =>
            {
                Assert.True(catalog.All.Any(d => d.choices.Length >= 2 && d.timerDays > 0 && !string.IsNullOrEmpty(d.defaultChoice)), "a choice with a timer and a default");

                var allEffects = new List<EventEffect>();
                foreach (EventDefinition definition in catalog.All)
                {
                    allEffects.AddRange(definition.effects);
                    foreach (EventChoice choice in definition.choices) allEffects.AddRange(choice.effects);
                }
                Assert.True(allEffects.Any(e => e.@params.GetFloat("chance", 1f) < 1f), "an effect with a chance");
                Assert.True(allEffects.Any(e => !string.IsNullOrEmpty(e.@params.GetString("exclusiveGroup", null))), "an exclusive group");
                Assert.True(allEffects.Any(e => e.type == "FireEvent"), "a chain");

                var targets = new SortedSet<string>(catalog.All.Select(d => d.targets.type.ToString()), StringComparer.Ordinal);
                foreach (string type in new[] { "Player", "RandomAI", "RandomCompany" }) Assert.True(targets.Contains(type), "a target of type " + type);
            });
        }

        [Test]
        public void TheConditions_AreThePredicatesOfTheCore_AndTheEnginesOwnWithTheirPrefix()
        {
            JsonObject file = ShippedEvents();
            var types = new SortedSet<string>(StringComparer.Ordinal);
            foreach (JsonNode node in file.GetArray("records"))
            {
                var record = (JsonObject)node;
                CollectTypes(record.GetObject("conditions"), types);
                JsonObject targets = record.GetObject("targets");
                if (targets != null) CollectTypes(targets.GetObject("filter"), types);
                JsonArray choices = record.GetArray("choices");
                if (choices == null) continue;
                foreach (JsonNode choice in choices) CollectTypes(((JsonObject)choice).GetObject("requires"), types);
            }

            foreach (string expected in new[] { "dateAtLeast", "cpuOnMarket", "researchActive", "es.hasRevenueHistory", "es.hasFab" })
            {
                Assert.True(types.Contains(expected), "the set uses " + expected + " (it uses " + string.Join(", ", types.ToArray()) + ")");
            }
            foreach (string type in types)
            {
                bool engines = type.IndexOf('.') >= 0;
                if (engines) Assert.True(type.StartsWith("es.", StringComparison.Ordinal), "a predicate with a dot is the engine's: " + type);
                if (engines) Assert.True(Array.IndexOf(BuiltinPredicates.Names, type) >= 0, "the engine registers " + type);
            }
        }

        [Test]
        public void TheSet_WritesNothingIntoMarketRatingCpu_AndKeepsPopularityGainInsideItsRange()
        {
            WithStarter((env, log, catalog) =>
            {
                var gain = new List<float>();
                foreach (EventDefinition definition in catalog.All)
                {
                    var effects = new List<EventEffect>(definition.effects);
                    foreach (EventChoice choice in definition.choices) effects.AddRange(choice.effects);
                    foreach (EventEffect effect in effects)
                    {
                        string channel = effect.@params.GetString("channel", null);
                        Assert.False(string.Equals(channel, "market.rating.cpu", StringComparison.Ordinal), definition.id + " writes market.rating.cpu (NFR-8)");
                        Assert.False(effect.type == "MarketDemand", definition.id + " uses MarketDemand");
                        if (string.Equals(channel, "popularity.gain", StringComparison.Ordinal)) gain.Add(effect.@params.GetFloat("multiplier", 1f));
                    }
                }

                Assert.Equal("0.85,1.1,1.25", string.Join(",", gain.OrderBy(x => x).Select(x => x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToArray()),
                    "the multipliers on popularity.gain: x0.85 ... x1.25 (NFR-8)");
                Assert.True(gain.All(m => m >= 0.85f && m <= 1.25f));

                EventConfig config = catalog.Config;
                Assert.Equal(0.7f, config.LimitMin("popularity.gain"), "the limit of the channel from config.json");
                Assert.Equal(1.6f, config.LimitMax("popularity.gain"));
                Assert.True(config.HasCategory("universal"), "the category of the starter set");
                Assert.Equal(0.011f, config.Category("universal").DailyChance);
                Assert.Equal(45, config.Category("universal").MinGapDays);
                Assert.Equal(2, config.Category("universal").MaxActive);
                Assert.Equal(2, config.MaxPendingChoices);
                Assert.Equal(3, config.ReminderDaysBefore);
                Assert.Equal(400, config.LogLimit);
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The strings (NFR-4)
        // ---------------------------------------------------------------------------------------------------------------

        [Test]
        public void EveryKeyOfTheSet_IsInBothLanguages_UnderThePrefixEs_AndNoStringOfTheSetIsLeftUnused()
        {
            WithStarter((env, log, catalog) =>
            {
                var text = new TableText();
                var used = new SortedSet<string>(StringComparer.Ordinal);
                foreach (EventDefinition definition in catalog.All) CollectKeys(definition, used);
                Assert.True(used.Count >= 40, "the set names its keys (" + used.Count + ")");

                var missing = new List<string>();
                foreach (string key in used)
                {
                    Assert.True(key.StartsWith("es.", StringComparison.Ordinal), "a key under the prefix of the feature: " + key);
                    foreach (string language in new[] { "ru", "en" })
                    {
                        string value;
                        if (!text.Tables.TryGet(language, key, out value) || string.IsNullOrWhiteSpace(value)) missing.Add(language + ":" + key);
                    }
                }
                Assert.True(missing.Count == 0, "keys of the starter set that the tables lack:\n  " + string.Join("\n  ", missing.ToArray()));

                // the other way round: a string written for a starter event that no event names is a dead string
                var names = new List<string>(Table.Select(r => r.Id));
                var unused = new List<string>();
                foreach (string key in text.Tables.Keys("ru"))
                {
                    string owner = names.FirstOrDefault(n => key.StartsWith(n + ".", StringComparison.Ordinal));
                    if (owner != null && !used.Contains(key)) unused.Add(key);
                }
                Assert.True(unused.Count == 0, "strings of the starter events that nothing uses:\n  " + string.Join("\n  ", unused.ToArray()));
            });
        }

        [Test]
        public void TheTextsOfTheSet_AreWrittenInTheirLanguage_AndLeaveNothingForTheLetterToFillIn()
        {
            WithStarter((env, log, catalog) =>
            {
                var text = new TableText();
                var keys = new SortedSet<string>(StringComparer.Ordinal);
                foreach (EventDefinition definition in catalog.All) CollectKeys(definition, keys);

                foreach (string key in keys)
                {
                    string ru, en;
                    Assert.True(text.Tables.TryGet("ru", key, out ru) & text.Tables.TryGet("en", key, out en), "both languages have " + key);
                    Assert.True(ru.Any(c => c >= 'Ѐ' && c <= 'ӿ'), "ru is Russian: " + key);
                    Assert.False(en.Any(c => c >= 'Ѐ' && c <= 'ӿ'), "en is English: " + key);
                    Assert.False(ru.Contains("{") || en.Contains("{"), "the texts of the events take no arguments: " + key);
                    Assert.False(KeyShowing.IsMatch(ru) || KeyShowing.IsMatch(en), "no key shows through: " + key);
                }
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // The events in the rig: every one fires, every letter reads in both languages
        // ---------------------------------------------------------------------------------------------------------------

        // A rig over the loaded events with room for ten open choices (the limit of two of the real config is not what these tests are about) and no lottery,
        // so only what the test fires happens.
        private static ChoiceRig RigOf(PackCatalog catalog, string language)
        {
            EventDefinition[] definitions = catalog.All.ToArray();
            var fake = new FakeCatalog(FakeCatalog.ConfigWith(10), definitions);
            var rig = new ChoiceRig(fake, null, true);
            rig.Text.Language = language;
            rig.SetDay(Defs.Day(1990, 3, 12));
            rig.Rig.Game.Revenue[CompanyKey.Player] = 20000000f;
            rig.Rig.Game.Revenue[CompanyKey.ForAi(0)] = 30000000f;
            return rig;
        }

        [Test]
        public void EveryStarterEvent_Fires_WithoutAFailedEffect_AndItsLetterReadsInBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                string lang = language;
                WithStarter((env, log, catalog) =>
                {
                    ChoiceRig rig = RigOf(catalog, lang);
                    foreach (EventDefinition definition in catalog.All)
                    {
                        bool rival = definition.targets.type == EventTargetType.RandomAI;
                        var context = new EventFireContext { CompanyKey = rival ? CompanyKey.ForAi(0) : CompanyKey.Player, IgnoreConditions = true };
                        Assert.Equal(EventFireResult.Fired, rig.Rig.Scheduler.Fire(definition.id, context), lang + " " + definition.id + " fires");
                    }
                    rig.Deliver(rig.Today);

                    Assert.Equal(0, rig.Rig.Faults.Count, "no fault:\n  " + string.Join("\n  ", rig.Rig.Faults.ToArray()));
                    Assert.Equal(0, rig.UiFaults.Count, "no UI fault:\n  " + string.Join("\n  ", rig.UiFaults.ToArray()));
                    foreach (EventInstanceDto instance in rig.Rig.Runtime.Instances)
                    {
                        var effects = new List<AppliedEffect>(instance.common);
                        foreach (ChoiceOptionDto option in instance.options) effects.AddRange(option.effects);
                        foreach (AppliedEffect effect in effects)
                        {
                            Assert.False(effect.state == AppliedEffectStates.Failed, lang + " " + instance.eventId + ": the effect " + effect.id + " (" + effect.type + ") failed");
                        }
                    }

                    // the letters: seven events ask the player (Critical), the two verdicts inform (Normal), the scandal of the rival is news (Low)
                    int withChoices = 0;
                    foreach (FakeMail.Letter letter in rig.Mail.Letters)
                    {
                        string title = rig.Text.L(letter.Plan.TitleKey, letter.Plan.Args);
                        string body = rig.Text.L(letter.Plan.BodyKey, letter.Plan.Args);
                        Assert.False(title.Contains("[es.") || body.Contains("[es."), lang + ": a key shows in the letter " + letter.Plan.Tag + ": " + title);
                        Assert.True(body.Length > 40, lang + ": the letter has a text " + letter.Plan.Tag);
                        foreach (MailChoicePlan choice in letter.Plan.Choices)
                        {
                            string label = rig.Text.L(choice.LabelKey);
                            Assert.False(label.StartsWith("[", StringComparison.Ordinal), lang + ": a button without a caption: " + choice.LabelKey);
                        }
                        if (letter.Plan.Choices.Count > 0) withChoices++;
                    }
                    Assert.Equal(0, rig.Text.Warnings.Count, lang + ": the texts have no hole:\n  " + string.Join("\n  ", rig.Text.Warnings.ToArray()));
                    Assert.Equal(7, withChoices, "seven letters with buttons");
                    Assert.Equal(7, rig.Mail.CountOf(MailPriority.Critical), "they are Critical");
                    Assert.Equal(2, rig.Mail.CountOf(MailPriority.Normal), "the two verdicts are Normal");
                    Assert.Equal(1, rig.Mail.CountOf(MailPriority.Low), "the scandal of the rival is Low");

                    // the mail effect of the court victory names two keys of the tables
                    Assert.Equal(1, rig.Rig.Game.Mails.Count, "SendMail of es.patent_win");
                    string[] mail = rig.Rig.Game.Mails[0];
                    Assert.False(rig.Text.L(mail[0]).StartsWith("[", StringComparison.Ordinal), lang + ": the title of the mail");
                    Assert.False(rig.Text.L(mail[1]).StartsWith("[", StringComparison.Ordinal), lang + ": the text of the mail");
                });
            }
        }

        [Test]
        public void TheFundButtonOfTheGrant_IsGreyWithTheReason_WhileNothingIsResearched_AndTheOtherWayOutStaysOpen()
        {
            WithStarter((env, log, catalog) =>
            {
                foreach (string language in new[] { "ru", "en" })
                {
                    ChoiceRig rig = RigOf(catalog, language);
                    EventInstanceDto instance = rig.FireAndDeliver("es.university_grant");
                    FakeMail.Letter letter = rig.Mail.OfInstance(instance.id);

                    FakeMail.Button fund = letter.Buttons["fund"];
                    Assert.False(fund.Enabled, language + " M11: nothing is researched, the button is grey");
                    string reason = rig.Text.L(fund.ReasonKey, fund.ReasonArgs);
                    Assert.Equal(language == "ru" ? "Нужно: текущее исследование" : "Requires: a research in progress", reason);
                    Assert.True(letter.Buttons["decline"].Enabled, language + ": declining is always possible");
                }
            });
        }

        [Test]
        public void ALawsuitFoughtInCourt_EndsWithTheVerdictOfExactlyOneEvent_InSixtyToOneHundredTwentyDays()
        {
            WithStarter((env, log, catalog) =>
            {
                ChoiceRig rig = RigOf(catalog, "ru");
                int start = rig.Today;
                EventInstanceDto troll = rig.FireAndDeliver("es.patent_troll");
                rig.ClickLetter(troll.id, "fight");
                Assert.Equal(1, rig.Rig.State.Scheduled.Count, "the verdict is scheduled (the other member of the group missed)");

                // a verdict has no timed effect, so its instance is gone the day it fires: the journal tells
                Func<List<EventLogEntry>> verdicts = () => rig.Rig.Entries(EventLogKinds.Fired).FindAll(e => e.eventId == "es.patent_win" || e.eventId == "es.patent_loss");

                rig.Days(start + 1, start + 59);
                Assert.Equal(0, verdicts().Count, "no verdict before 60 days");

                rig.Days(start + 60, start + 121);
                List<EventLogEntry> fired = verdicts();
                Assert.Equal(1, fired.Count, "exactly one verdict (the exclusive group)");
                Assert.Equal(CompanyKey.Player, fired[0].companyKey, "the verdict comes to the same company");
                Assert.True(fired[0].day - start >= 60 && fired[0].day - start <= 120, "within 60..120 days: " + (fired[0].day - start));
                Assert.Equal(0, rig.Rig.State.Scheduled.Count, "nothing is left in the queue");
            });
        }

        // ---------------------------------------------------------------------------------------------------------------
        // helpers
        // ---------------------------------------------------------------------------------------------------------------

        private static string TargetOf(EventDefinition definition)
        {
            string type = definition.targets.type.ToString();
            return definition.targets.type == EventTargetType.Player || definition.targets.weighting == TargetWeighting.Uniform ? type : type + "/" + definition.targets.weighting;
        }

        private static string Canon(IEnumerable<EventEffect> effects)
        {
            return string.Join(" + ", effects.Select(Canon).ToArray());
        }

        private static string Canon(EventEffect effect)
        {
            var sb = new StringBuilder(effect.type).Append('{');
            bool first = true;
            foreach (KeyValuePair<string, JsonNode> member in effect.@params.Raw.OrderBy(m => m.Key, StringComparer.Ordinal))
            {
                if (!first) sb.Append(';');
                first = false;
                var value = member.Value as JsonValue;
                sb.Append(member.Key).Append('=').Append(value != null && value.Kind == JsonKind.String ? value.StringValue : ModJson.Write(member.Value, false));
            }
            sb.Append('}');
            if (effect.durationDays > 0) sb.Append('/').Append(effect.durationDays).Append('d');
            return sb.ToString();
        }

        private static void CollectTypes(JsonNode node, ISet<string> types)
        {
            if (node == null) return;
            var obj = node as JsonObject;
            if (obj != null)
            {
                string type = obj.GetString("type", null);
                if (type != null) types.Add(type);
                foreach (KeyValuePair<string, JsonNode> member in obj) CollectTypes(member.Value, types);
                return;
            }
            var array = node as JsonArray;
            if (array == null) return;
            foreach (JsonNode item in array) CollectTypes(item, types);
        }

        private static void CollectKeys(EventDefinition definition, ISet<string> keys)
        {
            foreach (LocText text in new[] { definition.title, definition.text, definition.cause })
            {
                if (text != null && !string.IsNullOrEmpty(text.Key)) keys.Add(text.Key);
            }
            foreach (EventChoice choice in definition.choices)
            {
                if (!string.IsNullOrEmpty(choice.textKey)) keys.Add(choice.textKey);
            }
            var effects = new List<EventEffect>(definition.effects);
            foreach (EventChoice choice in definition.choices) effects.AddRange(choice.effects);
            foreach (EventEffect effect in effects)
            {
                if (effect.type != "SendMail") continue;
                keys.Add(effect.@params.GetString("title", string.Empty));
                keys.Add(effect.@params.GetString("text", string.Empty));
            }
        }
    }
}
