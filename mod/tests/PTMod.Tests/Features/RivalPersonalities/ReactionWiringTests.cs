using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// How the reactions are wired to the game and to the event system (TSK-013..016, TSK-028). The classes that touch the game cannot run in the unit tests, so
    /// these facts are read from the source and the content of the feature, comments aside: what the feature registers in the event system, which interface each
    /// effect handler has and what it calls, the mails, the ticks and the subscriptions, the guards, and that every string the code asks for is in both tables.
    /// </summary>
    public class ReactionWiringTests
    {
        private static string FeatureFolder()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            Assert.True(Directory.Exists(folder), "no feature folder " + folder);
            return folder;
        }

        private static string Code(params string[] relative)
        {
            string path = FeatureFolder();
            foreach (string part in relative) path = Path.Combine(path, part);
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string Feature()
        {
            return Code("RivalPersonalitiesFeature.cs");
        }

        private static string Handler(string name)
        {
            return Code("Reactions", "Effects", name + ".cs");
        }

        // ---- the event system ----

        [Test]
        public void TheFeature_RegistersThePack_AndTheFiveEffectTypes()
        {
            string feature = Feature();

            Assert.True(Regex.IsMatch(feature, @"EventRegistry\.RegisterPack\(\s*ReactionTags\.PackId\s*,\s*ReactionTags\.PackId\s*,\s*new PackOptions\s*\{\s*SettingsFeatureId\s*=\s*FeatureId\s*,\s*ConfigFile\s*=\s*ReactionTags\.PackConfigFile\s*\}\s*\)"),
                "the pack is registered, frozen with the feature's switch, with its own config file (config.json is the feature's)");
            foreach (string pair in new[]
            {
                "EffectPriceCut:RivalPriceCutHandler", "EffectCounterLaunch:RivalCounterLaunchHandler", "EffectResearchBoost:RivalResearchBoostHandler",
                "EffectPoachCounterOffer:RivalPoachCounterOfferHandler", "EffectAttitude:RivalAttitudeHandler",
            })
            {
                string[] parts = pair.Split(':');
                Assert.True(Regex.IsMatch(feature, @"EventEffects\.Register\(\s*ReactionTags\." + parts[0] + @"\s*,\s*new " + parts[1] + @"\(\)\s*\)"), parts[1] + " is registered");
            }
            Assert.Equal(5, Regex.Matches(feature, @"EventEffects\.Register\(").Count);
            Assert.True(Regex.IsMatch(feature, @"RegisterEventContent\(\)\s*;"), "Register declares them");
        }

        [Test]
        public void TheEffectTypeNames_AreThoseOfTheShippedEvents_AndNoEventUsesAnUnregisteredOne()
        {
            Assert.Equal("RivalPriceCut", ReactionTags.EffectPriceCut);
            Assert.Equal("RivalCounterLaunch", ReactionTags.EffectCounterLaunch);
            Assert.Equal("RivalResearchBoost", ReactionTags.EffectResearchBoost);
            Assert.Equal("RivalPoachCounterOffer", ReactionTags.EffectPoachCounterOffer);
            Assert.Equal("RivalAttitude", ReactionTags.EffectAttitude);
            Assert.Equal("rival-personalities", ReactionTags.PackId);

            var registered = new HashSet<string>
            {
                ReactionTags.EffectPriceCut, ReactionTags.EffectCounterLaunch, ReactionTags.EffectResearchBoost, ReactionTags.EffectPoachCounterOffer,
                ReactionTags.EffectAttitude,
                "ModifierTimed", "Reputation",   // standard types of the event system
            };
            foreach (KeyValuePair<string, List<string>> pair in EffectTypesByEvent())
            {
                foreach (string type in pair.Value) Assert.True(registered.Contains(type), pair.Key + " uses '" + type + "', which nothing registers");
            }
        }

        [Test]
        public void EachEvent_HasTheEffectsOfTheDesign()
        {
            Dictionary<string, List<string>> events = EffectTypesByEvent();

            Assert.Equal(new[] { "RivalPriceCut" }, events["rival.priceCut"].ToArray());
            Assert.Equal(new[] { "RivalCounterLaunch" }, events["rival.counterLaunch"].ToArray());
            Assert.Equal(new[] { "ModifierTimed" }, events["rival.oemPush"].ToArray());
            Assert.Equal(new[] { "Reputation" }, events["rival.smear"].ToArray());
            Assert.Equal(new[] { "RivalPoachCounterOffer", "ModifierTimed", "RivalResearchBoost" }, events["rival.poach"].ToArray());
            foreach (ReactionType type in new[] { ReactionType.PriceCut, ReactionType.CounterLaunch, ReactionType.OemPush, ReactionType.Poach, ReactionType.Smear })
            {
                Assert.True(events.ContainsKey(ReactionTags.EventIdOf(type)), ReactionTags.EventIdOf(type) + " is in events.json");
            }
        }

        [Test]
        public void TheStandardEffects_HaveTheNumbersOfTheTask()
        {
            JsonObject oem = Event("rival.oemPush");
            JsonObject oemEffect = (JsonObject)oem.GetArray("effects")[0];
            JsonObject oemParams = oemEffect.GetObject("params");
            Assert.Equal("contract.signChance", oemParams.GetString("channel", null));
            Assert.Near(1.5, oemParams.GetDouble("multiplier", 0), 1e-9);
            Assert.Equal("Target", oemParams.GetString("scope", null));
            Assert.Equal(365, oemEffect.GetInt32("durationDays", 0));

            JsonObject poach = Event("rival.poach");
            Assert.Equal(10, poach.GetInt32("timerDays", 0));
            Assert.Equal("letGo", poach.GetString("defaultChoice", null));
            JsonObject letGo = (JsonObject)poach.GetArray("choices").Cast<JsonNode>().First(c => ((JsonObject)c).GetString("id", null) == "letGo");
            JsonObject slow = (JsonObject)letGo.GetArray("effects")[0];
            Assert.Equal("research.speed", slow.GetObject("params").GetString("channel", null));
            Assert.Near(0.85, slow.GetObject("params").GetDouble("multiplier", 0), 1e-9);
            Assert.Equal("Target", slow.GetObject("params").GetString("scope", null));
            Assert.Equal(120, slow.GetInt32("durationDays", 0));
            JsonObject boost = (JsonObject)letGo.GetArray("effects")[1];
            Assert.Equal("RivalResearchBoost", boost.GetString("type", null), "the bonus of the rival has no term of its own: the handler takes it from the content");

            JsonObject smear = Event("rival.smear");
            JsonObject reputation = (JsonObject)smear.GetArray("effects")[0];
            Assert.Near(-3.0, reputation.GetObject("params").GetDouble("delta", 0), 1e-9);
            Assert.Equal("Player", smear.GetObject("targets").GetString("type", null));
        }

        // ---- the handlers ----

        [Test]
        public void ThePriceCutAndTheCounterLaunch_AreTimedHandlers_TheOthersAreOneOff()
        {
            Assert.True(Regex.IsMatch(Handler("RivalPriceCutHandler"), @"sealed class RivalPriceCutHandler\s*:\s*ITimedEffectHandler"));
            Assert.True(Regex.IsMatch(Handler("RivalCounterLaunchHandler"), @"sealed class RivalCounterLaunchHandler\s*:\s*ITimedEffectHandler"));
            Assert.True(Regex.IsMatch(Handler("RivalResearchBoostHandler"), @"sealed class RivalResearchBoostHandler\s*:\s*IEventEffectHandler"));
            Assert.True(Regex.IsMatch(Handler("RivalPoachCounterOfferHandler"), @"sealed class RivalPoachCounterOfferHandler\s*:\s*IEventEffectHandler"));
            Assert.True(Regex.IsMatch(Handler("RivalAttitudeHandler"), @"sealed class RivalAttitudeHandler\s*:\s*IEventEffectHandler"));

            foreach (string pair in new[]
            {
                "RivalPriceCutHandler:EffectPriceCut", "RivalCounterLaunchHandler:EffectCounterLaunch", "RivalResearchBoostHandler:EffectResearchBoost",
                "RivalPoachCounterOfferHandler:EffectPoachCounterOffer", "RivalAttitudeHandler:EffectAttitude",
            })
            {
                string[] parts = pair.Split(':');
                Assert.True(Regex.IsMatch(Handler(parts[0]), @"get\s*\{\s*return ReactionTags\." + parts[1] + @"\s*;\s*\}"), parts[0] + " answers to " + parts[1]);
            }
        }

        [Test]
        public void ThePriceCut_GoesThroughThePriceBook_AndGivesThePricesBack()
        {
            string code = Handler("RivalPriceCutHandler");

            Assert.True(Regex.IsMatch(code, @"Prices\.ApplyCut\(\s*args\.ReactionId\s*,\s*rival\s*,\s*segment\s*,\s*effect\.value\s*,\s*floor\s*\)"), "Apply cuts through the price book");
            Assert.True(Regex.IsMatch(code, @"Prices\.Restore\(\s*args\.ReactionId\s*\)"), "Remove gives the original target prices back");
            Assert.True(Regex.IsMatch(code, @"Executor\.OnStarted\("), "the start is reported (notification, history, the character is revealed)");
            Assert.True(Regex.IsMatch(code, @"Executor\.OnEnded\("), "and the end");
            Assert.True(Regex.IsMatch(code, @"Executor\.Restore\("), "Restore only makes sure the reaction is listed: the book holds the entries");
            Assert.True(Regex.IsMatch(code, @"if \(!EffectSupport\.FeatureIsActive\) return;"), "a pack frozen with the feature keeps the record");
            Assert.True(Regex.IsMatch(code, @"ReactionParams\.PriceCutDepth\(") && Regex.IsMatch(code, @"ReactionParams\.PriceCutFloorMargin\("), "the numbers come from the archetype and the config");
        }

        [Test]
        public void TheCounterLaunch_AddsAndRemovesTheBoost()
        {
            string code = Handler("RivalCounterLaunchHandler");

            Assert.True(Regex.IsMatch(code, @"Modifiers\.AddBoost\(\s*new ReactionBoost\("), "Apply and Restore add the boost");
            Assert.True(Regex.IsMatch(code, @"Modifiers\.RemoveBoost\(\s*args\.ReactionId\s*\)"), "Remove takes it away");
            Assert.True(Regex.IsMatch(code, @"ReactionParams\.ApprovalBoost\(") && Regex.IsMatch(code, @"ReactionParams\.FundingBoost\("));
            Assert.True(Regex.IsMatch(code, @"Executor\.OnStarted\(") && Regex.IsMatch(code, @"Executor\.OnEnded\(") && Regex.IsMatch(code, @"Executor\.Restore\("));
        }

        [Test]
        public void TheResearchBoost_IsASavedEntryOfModModifiers_ThatTheFeatureNeverRemoves()
        {
            string code = Handler("RivalResearchBoostHandler");

            Assert.True(Regex.IsMatch(code, @"ModModifiers\.Add\(\s*RivalPersonalitiesFeature\.FeatureId\s*,\s*ModChannels\.ResearchSpeed\s*,\s*ModifierScope\.Company\(args\.RivalKey\)\s*,\s*effect\.value\s*,"),
                "research.speed of the rival, owned by the feature");
            Assert.True(Regex.IsMatch(code, @"""rival\.reason\.poach"""), "the reason of the bonus");
            Assert.True(Regex.IsMatch(code, @"ReactionParams\.PoachRivalResearchSpeed\(") && Regex.IsMatch(code, @"ReactionParams\.PoachRivalBoostDays\("), "1.10 for 120 days from the config");

            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string source = SourceScan.Strip(SourceScan.ReadText(path), false);
                Assert.False(Regex.IsMatch(source, @"ModModifiers\.(Remove|RemoveAll)\("), Path.GetFileName(path) + ": the entry is made once and never removed or recreated by the feature");
            }
        }

        [Test]
        public void TheCounterOffer_PaysAsResearchSpending_AndIsTheOnlyMoneyTheFeatureSpends()
        {
            string code = Handler("RivalPoachCounterOfferHandler");

            Assert.True(Regex.IsMatch(code, @"ctx\.Game\.Spend\(\s*ctx\.Company\s*,\s*cost\s*,\s*GameExpenseOrigin\.Research\s*\)"), "Company.SpendMoney(cost, ExpenseOrigin.Research)");
            Assert.True(Regex.IsMatch(code, @"using GameExpenseOrigin\s*=\s*ProcessorTycoon\.ExpenseOrigin\s*;"));
            Assert.True(Regex.IsMatch(Code("Reactions", "ReactionExecutor.cs"), @"PoachMath\.CounterOfferCost\("), "the price is PoachMath's");

            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string source = SourceScan.Strip(SourceScan.ReadText(path), false);
                if (path.EndsWith("RivalPoachCounterOfferHandler.cs", StringComparison.Ordinal)) continue;
                Assert.False(Regex.IsMatch(source, @"\.(SpendMoney|EarnMoney)\(|\.Spend\(|\.Earn\("), Path.GetFileName(path) + ": no money moves outside the counter-offer");
            }
        }

        [Test]
        public void TheAttitudeEffect_GoesThroughTheLedger()
        {
            string code = Handler("RivalAttitudeHandler");

            Assert.True(Regex.IsMatch(code, @"Attitudes\.Change\(\s*rival\.UniqueID\s*,\s*effect\.value\s*,\s*reason\s*\)"));
            Assert.True(Regex.IsMatch(code, @"""event:"""), "the reason in the history is event:<id>");
            Assert.True(Regex.IsMatch(code, @"errors\.Add\("), "a missing or wild delta rejects the event when the content loads");
        }

        // ---- the mails ----

        [Test]
        public void TheMails_AreNormal_TaggedWithTheReaction_AndOpenTheIntelWithoutClosing()
        {
            string code = Code("Reactions", "IntelMail.cs");

            Assert.Equal(2, Regex.Matches(code, @"Priority\s*=\s*MailPriority\.Normal").Count, "the intel mail and the counter-strike mail");
            Assert.False(Regex.IsMatch(code, @"MailPriority\.Critical"));
            Assert.True(Regex.IsMatch(code, @"Tag\s*=\s*ReactionTags\.Intel\(notice\.ReactionId\)"));
            Assert.True(Regex.IsMatch(code, @"Tag\s*=\s*ReactionTags\.Counter\(reaction\.id\)"));
            Assert.Equal(2, Regex.Matches(code, @"ModMail\.Send\(").Count);
            Assert.True(Regex.IsMatch(code, @"Id\s*=\s*ReactionTags\.ActionOpenIntel\s*,\s*LabelKey\s*=\s*""rival\.mail\.open""\s*,\s*Repeatable\s*=\s*true"), "a reusable action: the mail stays open");
            Assert.False(Regex.IsMatch(code, @"Choices\s*="), "the intel mail asks for no answer");
            Assert.True(Regex.IsMatch(code, @"Actions\s*=\s*new List<ModMailAction>"));

            string feature = Feature();
            Assert.True(Regex.IsMatch(feature, @"ModMail\.RegisterActionHandler\(\s*FeatureId\s*,\s*IntelMail\.OnAction\s*\)"));
            Assert.True(Regex.IsMatch(feature, @"ModMail\.Delivered\s*\+=\s*OnMailDelivered\s*;"), "the delivery starts the count of the warning");
        }

        [Test]
        public void TheMailTags_OfTheCode_AreTheOnesOfTheDesign()
        {
            string planner = Code("Logic", "Reactions", "ReactionPlannerCore.cs");

            Assert.True(Regex.IsMatch(planner, @"ReactionTags\.TryParseIntel\(\s*tag\s*,\s*out reactionId\s*\)"), "OnMailDelivered knows its own mails by the tag");
            Assert.True(Regex.IsMatch(planner, @"ReactionTags\.Intel\(pending\.id\)"));
            Assert.True(Regex.IsMatch(Code("Reactions", "ReactionPlanner.cs"), @"string\.Equals\(record\.FeatureId\s*,\s*Feature"), "mails of other features are not ours");
        }

        // ---- the ticks and the subscriptions ----

        [Test]
        public void TheDay_AndTheYear_AreTicksOfTheFeature_AndTheHooksAreGuarded()
        {
            string feature = Feature();

            Assert.True(Regex.IsMatch(feature, @"ctx\.OnTick\(\s*TickPhase\.DayPassed\s*,\s*OnDayPassed\s*\)"));
            Assert.True(Regex.IsMatch(feature, @"ctx\.OnTick\(\s*TickPhase\.YearPassed\s*,\s*OnYearPassed\s*\)"));
            Assert.True(Regex.IsMatch(feature, @"_triggers\.TriggerRaised\s*\+=\s*OnTriggerRaised\s*;"), "the planner listens to the recorder");
            Assert.True(Regex.IsMatch(feature, @"EventBus\.OnChoiceResolved\s*\+=\s*OnChoiceResolved\s*;"));
            Assert.True(Regex.IsMatch(feature, @"_registry\.Revealed\s*\+=\s*OnRevealed\s*;"));

            foreach (string site in new[]
            {
                "TriggerRaised.plan", "DayPassed.reactions.bankrupt", "DayPassed.reactions.planner", "DayPassed.reactions.expire", "YearPassed.reactions",
                "ModMail.Delivered", "EventBus.OnChoiceResolved", "ModEvents.CpuReleased.counter",
            })
            {
                Assert.True(feature.Contains("ModHooks.Guard(FeatureId, \"" + site + "\""), "a guard at " + site);
            }
            // switching off runs in any state of the feature (a Faulted one too), so its steps are not under ModHooks.Guard, which would skip them (TSK-023)
            Assert.True(feature.Contains("Always(\"feature/disabled.boosts\""), "the boosts go in any state");
            Assert.True(Regex.IsMatch(feature, @"if \(!ModFeatures\.IsActive\(FeatureId\)\) return;"), "a trigger is planned only while the feature runs");
        }

        [Test]
        public void TheCounterLaunchWatcher_SharesTheOneSubscriptionToTheCoreEvent()
        {
            string feature = Feature();
            string watcher = Code("Reactions", "CounterLaunchWatcher.cs");

            Assert.Equal(1, Regex.Matches(feature, @"ModEvents\.CpuReleased\s*\+=").Count, "one subscription for the monitor and the watcher");
            Assert.True(Regex.IsMatch(feature, @"_counterWatcher\.OnCpuReleased\(owner, cpu, isVariant, duringFastForward\)"));
            Assert.True(Regex.IsMatch(watcher, @"CounterLaunchRules\.IsRelevantRelease\("));
            Assert.True(Regex.IsMatch(watcher, @"CounterLaunchRules\.FindOpen\(") && Regex.IsMatch(watcher, @"CounterLaunchRules\.MarkDone\("));
            Assert.True(Regex.IsMatch(watcher, @"cpu\.Strategy\.TargetMarket"), "the segment of a rival's CPU is the one it was designed for");
            Assert.True(Regex.IsMatch(watcher, @"IntelMail\.SendCounterStrike\("));
            Assert.False(Regex.IsMatch(watcher, @"CpuTransferred"), "a transfer is not a release");
            Assert.False(Regex.IsMatch(watcher, @"ModSegments\.Classify"), "no classifier by price for a rival's CPU");
        }

        [Test]
        public void TheReactions_PatchNothingOfTheGame()
        {
            foreach (string folder in new[] { "Reactions", "UI" })
            {
                foreach (string path in SourceScan.Files(Path.Combine(FeatureFolder(), folder), ".cs"))
                {
                    string source = SourceScan.Strip(SourceScan.ReadText(path), false);
                    Assert.False(Regex.IsMatch(source, @"\[ModPatch\("), Path.GetFileName(path) + " declares a patch");
                    Assert.False(Regex.IsMatch(source, @"HarmonyLib|\.Postfix|\.Prefix"), Path.GetFileName(path) + " touches Harmony");
                }
            }
        }

        [Test]
        public void ThePlanner_AsksTheCoreForTheDiceTheEventSystemAndTheMail()
        {
            string code = Code("Reactions", "ReactionPlanner.cs");

            Assert.True(Regex.IsMatch(code, @"ModRandom\.Stream\(Feature\)"), "the stream of the feature");
            Assert.True(Regex.IsMatch(code, @"EventSystemAvailable\s*\{\s*get\s*\{\s*return EventSystemApi\.IsActive;"), "the event system works now");
            Assert.True(Regex.IsMatch(code, @"RivalSettings\.ForceReactions"));
            Assert.True(Regex.IsMatch(code, @"ModMail\.Get\(") && Regex.IsMatch(code, @"ModMail\.Find\("), "a delivery the event did not announce is found in the mail service");
            Assert.False(Regex.IsMatch(code, @"System\.Random|UnityEngine\.Random|new Random\("), "no random numbers of the game");
        }

        [Test]
        public void TheDebugPage_ExistsInTheDebugBuildOnly()
        {
            string raw = SourceScan.ReadText(Path.Combine(FeatureFolder(), "UI", "DebugReactionPage.cs"));
            string[] lines = raw.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToArray();

            Assert.Equal("#if PTMOD_DEBUG", lines[0].Trim());
            Assert.Equal("#endif", lines[lines.Length - 1].Trim());
            Assert.True(Regex.IsMatch(raw, @"PlanManual\("), "the button plans through the planner");
            Assert.True(Regex.IsMatch(raw, @"ModUI\.Dropdown\(") && Regex.IsMatch(raw, @"ModUI\.Button\("), "two lists and a button");

            // TSK-018 put the section into the page of the hub it made: the section is built there, in the debug build only
            string page = SourceScan.ReadText(Path.Combine(FeatureFolder(), "UI", "IntelHubPage.cs"));
            Assert.True(Regex.IsMatch(page, @"#if PTMOD_DEBUG\s*\r?\n\s*DebugReactionPage\.Build\(page\);\s*\r?\n#endif"),
                "the section is built in the page of the hub, in the debug build only");
            string feature = SourceScan.ReadText(Path.Combine(FeatureFolder(), "RivalPersonalitiesFeature.cs"));
            Assert.False(feature.Contains("DebugReactionPage"), "the feature registers no page of its own for it any more");
        }

        // ---- the strings ----

        private static readonly Regex KeyLiteral = new Regex(@"""(rival\.[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*)""", RegexOptions.Compiled);

        [Test]
        public void EveryStringTheCodeAsksFor_IsInBothTables()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");

            var problems = new List<string>();
            string src = SourceScan.SrcDirOrFail();
            int found = 0;
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                if (path.Contains(Path.DirectorySeparatorChar + "Api" + Path.DirectorySeparatorChar) || path.Contains(Path.DirectorySeparatorChar + "Logic" + Path.DirectorySeparatorChar)) continue;
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in KeyLiteral.Matches(code))
                {
                    found++;
                    string key = match.Groups[1].Value;
                    if (!ru.ContainsKey(key)) problems.Add(SourceScan.Relative(src, path) + ": '" + key + "' is not in the ru table");
                    if (!en.ContainsKey(key)) problems.Add(SourceScan.Relative(src, path) + ": '" + key + "' is not in the en table");
                }
            }

            Assert.True(found >= 20, "too few keys found (" + found + "): the scan is wrong");
            Assert.True(problems.Count == 0, string.Join("\n  ", problems));
        }

        [Test]
        public void TheStringsBuiltFromTheNameOfAReaction_AndOfASegment_Exist()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");
            var keys = new List<string>();
            foreach (ReactionType type in new[] { ReactionType.PriceCut, ReactionType.CounterLaunch, ReactionType.OemPush, ReactionType.Poach, ReactionType.Smear })
            {
                string name = ReactionKinds.NameOf(type);
                string camel = char.ToLowerInvariant(name[0]) + name.Substring(1);
                keys.Add("rival.mail.intel." + camel);
                keys.Add("rival.reaction." + camel);
                keys.Add("rival.notify.started." + camel);
            }
            keys.Add("rival.notify.started.poachKept");
            for (int i = 0; i < 4; i++) keys.Add("rival.segment." + i);
            foreach (string level in new[] { "hostile", "rivalry", "neutral", "respect", "partnership" }) keys.Add("rival.level." + level);

            foreach (string key in keys)
            {
                Assert.True(ru.ContainsKey(key), "ru: " + key);
                Assert.True(en.ContainsKey(key), "en: " + key);
            }
        }

        [Test]
        public void TheTexts_UseOnlyTheArgumentsTheCodeGivesThem()
        {
            // the number of arguments each text is given in the code
            var arity = new Dictionary<string, int>
            {
                { "rival.mail.intel.subject", 3 }, { "rival.mail.intel.priceCut", 3 }, { "rival.mail.intel.counterLaunch", 3 }, { "rival.mail.intel.oemPush", 3 },
                { "rival.mail.intel.poach", 3 }, { "rival.mail.intel.smear", 3 },
                { "rival.mail.counter.subject", 3 }, { "rival.mail.counterLaunchDone", 3 },
                { "rival.notify.started.priceCut", 3 }, { "rival.notify.started.counterLaunch", 2 }, { "rival.notify.started.oemPush", 1 },
                { "rival.notify.started.poach", 1 }, { "rival.notify.started.poachKept", 2 }, { "rival.notify.started.smear", 1 },
                { "rival.notify.ended", 2 }, { "rival.notify.revealed", 2 }, { "rival.notify.level", 2 },
                // the lines of the effects: four fixed arguments (reaction id, segment, rival key, rival name) and the numbers
                { "rival.fx.priceCut", 6 }, { "rival.fx.counterLaunch", 7 }, { "rival.fx.researchBoost", 6 }, { "rival.fx.counterOffer", 5 }, { "rival.fx.attitude", 5 },
                { "rival.debug.planned", 1 }, { "rival.debug.refused", 2 },
                // the text of the event: [rival name, price or segment, reaction id, rival key]
                { "rival.event.priceCut.title", 4 }, { "rival.event.counterLaunch.title", 4 }, { "rival.event.oemPush.title", 4 }, { "rival.event.smear.title", 4 },
                { "rival.poach.title", 4 }, { "rival.poach.body", 4 },
            };

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = RivalTestKit.ReadL10n(language);
                foreach (KeyValuePair<string, int> pair in arity)
                {
                    JsonNode node;
                    Assert.True(table.TryGetValue(pair.Key, out node), language + ": " + pair.Key);
                    string text = ((JsonValue)node).StringValue;
                    int highest = -1;
                    foreach (Match m in Regex.Matches(text, @"\{(\d+)\}")) highest = Math.Max(highest, int.Parse(m.Groups[1].Value));
                    Assert.True(highest < pair.Value, language + ": " + pair.Key + " uses {" + highest + "} but is given " + pair.Value + " argument(s)");
                }
            }
        }

        [Test]
        public void TheKeyNames_OfTheExecutor_AreTheSpellingOfTheStrings()
        {
            string code = Code("Reactions", "ReactionExecutor.cs");
            Assert.True(Regex.IsMatch(code, @"char\.ToLowerInvariant\(name\[0\]\)\s*\+\s*name\.Substring\(1\)"), "priceCut, counterLaunch, oemPush, poach, smear");
            Assert.True(Regex.IsMatch(code, @"""rival\.notify\.started\.poachKept"""));
            Assert.True(Regex.IsMatch(Code("Reactions", "IntelMail.cs"), @"""rival\.mail\.intel\.""\s*\+\s*ReactionExecutor\.KeyName\(notice\.Type\)"));
        }

        // ---- helpers ----

        private static JsonObject Event(string id)
        {
            JsonObject file = RivalTestKit.ParseObject(RivalTestKit.ReadContent("events.json"));
            foreach (JsonNode node in file.GetArray("records"))
            {
                var record = (JsonObject)node;
                if (record.GetString("id", null) == id) return record;
            }
            Assert.Fail("no event " + id);
            return null;
        }

        /// <summary>The effect types of each event in the order of the file: the event's own effects, then those of each choice.</summary>
        private static Dictionary<string, List<string>> EffectTypesByEvent()
        {
            JsonObject file = RivalTestKit.ParseObject(RivalTestKit.ReadContent("events.json"));
            var result = new Dictionary<string, List<string>>();
            foreach (JsonNode node in file.GetArray("records"))
            {
                var record = (JsonObject)node;
                var types = new List<string>();
                AddTypes(record.GetArray("effects"), types);
                JsonArray choices = record.GetArray("choices");
                if (choices != null)
                {
                    foreach (JsonNode choice in choices) AddTypes(((JsonObject)choice).GetArray("effects"), types);
                }
                result[record.GetString("id", null)] = types;
            }
            return result;
        }

        private static void AddTypes(JsonArray effects, List<string> into)
        {
            if (effects == null) return;
            foreach (JsonNode effect in effects) into.Add(((JsonObject)effect).GetString("type", null));
        }
    }
}
