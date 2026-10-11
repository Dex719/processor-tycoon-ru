using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Rng;
using PTMod.Features.HistoricalEvents;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.HistoricalEvents
{
    /// <summary>
    /// The self-check of the feature and the switches (TSK-024; FR-17, NFR-2; AC-17.1, AC-17.2): every item of the design's list as a rule over plain facts
    /// (<see cref="HistSelfCheckRules"/>), the categories a failed channel switches off (<see cref="CategoryBlocks"/>), the entries of a category that is off or comes on in the
    /// middle of a game (the real record of the Tohoku earthquake), and the lifecycle of the feature read from its source (the game is needed to run it).
    /// </summary>
    [Covers(typeof(HistSelfCheckRules), typeof(HistCheckFacts), typeof(HistCheckVerdict), typeof(HistCheckProblem), typeof(HistCheckLevel), typeof(HistChannelFact), typeof(CategoryBlocks))]
    public class HistSelfCheckTests
    {
        // ---- the facts ----

        /// <summary>Everything in order: the calendar of 42 entries, the pack, the eight types, the five predicates, the three channels.</summary>
        private static HistCheckFacts Healthy()
        {
            var facts = new HistCheckFacts
            {
                SpinOffMethodFound = true,
                LicenseEventFound = true,
                BankruptcyEventFound = true,
                CalendarLoaded = true,
                ValidEntries = 42,
                EventIdAllowed = id => id.StartsWith("hist.", StringComparison.Ordinal),
                PredicateNameAllowed = name => name.StartsWith("hist.", StringComparison.Ordinal),
            };
            facts.PackEventIds.AddRange(new[] { "hist.1995.win95", "hist.decide.antitrust.fine", BuiltInNews.SolarFoundriesEventId, BuiltInNews.X86LicenseEventId });
            facts.CalendarEventIds.AddRange(new[] { "hist.1995.win95", "hist.decide.antitrust.fine" });
            facts.EffectTypes.AddRange(HistEffectTypes.Own);
            facts.EffectTypes.Add("MarketDemand");
            facts.Predicates.AddRange(HistPredicates.Names);
            facts.Predicates.Add("es.hasFab");
            facts.Channels.Add(new HistChannelFact(HistSelfCheckRules.RatingChannel) { Declared = true, PerCpu = true, PerSegment = true });
            facts.Channels.Add(new HistChannelFact(HistSelfCheckRules.AcquisitionChannel) { Declared = true });
            facts.Channels.Add(new HistChannelFact(HistSelfCheckRules.GpuChannel) { Declared = true });
            return facts;
        }

        [Test]
        public void AHealthyFeature_PassesInOneLine()
        {
            HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(Healthy());

            Assert.True(verdict.Ok, string.Join(" | ", verdict.Problems.Select(p => p.ToString())));
            Assert.Equal(0, verdict.DisabledCategories.Count);
            Assert.Equal("selfcheck OK: 42 entries, 4 events, 8 effect types, 5 predicates", verdict.Line);
        }

        [Test]
        public void TheCalendar_NeedsTwentyFiveValidEntries()
        {
            HistCheckFacts facts = Healthy();
            facts.ValidEntries = 24;
            Assert.True(HistSelfCheckRules.Evaluate(facts).Has("calendar", "24 valid entries"));

            facts.ValidEntries = 25;
            Assert.True(HistSelfCheckRules.Evaluate(facts).Ok, "25 is enough");

            facts.CalendarLoaded = false;
            facts.ValidEntries = 0;
            HistCheckVerdict early = HistSelfCheckRules.Evaluate(facts);
            Assert.False(early.HasErrors, "a calendar that has not been read yet is not a bad calendar");
            Assert.True(early.Has("calendar", "not been read"));
        }

        [Test]
        public void ThePack_MustBeThere_AndHoldEveryEventTheCalendarFires()
        {
            HistCheckFacts none = Healthy();
            none.PackEventIds.Clear();
            Assert.True(HistSelfCheckRules.Evaluate(none).Has("pack", "accepted none"));

            HistCheckFacts missing = Healthy();
            missing.CalendarEventIds.Add("hist.nowhere");
            missing.CalendarEventIds.Add("hist.nowhere.either");
            HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(missing);
            Assert.True(verdict.HasErrors && verdict.Has("pack", "hist.nowhere, hist.nowhere.either"), string.Join(" | ", verdict.Problems.Select(p => p.ToString())));

            HistCheckFacts noNews = Healthy();
            noNews.PackEventIds.Remove(BuiltInNews.X86LicenseEventId);
            HistCheckVerdict warning = HistSelfCheckRules.Evaluate(noNews);
            Assert.False(warning.HasErrors, "the letters about the game's own events are a Could");
            Assert.True(warning.Has("pack", BuiltInNews.X86LicenseEventId));
        }

        [Test]
        public void TheEightEffectTypes_AndTheFivePredicates_MustBeRegistered()
        {
            HistCheckFacts types = Healthy();
            types.EffectTypes.Remove(HistEffectTypes.PriceWar);
            types.EffectTypes.Remove(HistEffectTypes.HistFlag);
            HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(types);
            Assert.True(verdict.HasErrors && verdict.Has("types", HistEffectTypes.PriceWar) && verdict.Has("types", HistEffectTypes.HistFlag));

            HistCheckFacts predicates = Healthy();
            predicates.Predicates.Remove(HistPredicates.IsFabless);
            Assert.True(HistSelfCheckRules.Evaluate(predicates).Has("predicates", HistPredicates.IsFabless));
        }

        [Test]
        public void TheRatingChannel_MustBeDeclaredForACpuAndASegment_OtherwiseExportAndRaceAreDisabled()
        {
            foreach (string flaw in new[] { "undeclared", "notPerCpu", "notPerSegment", "missing" })
            {
                HistCheckFacts facts = Healthy();
                HistChannelFact rating = facts.Channels.First(c => c.Name == HistSelfCheckRules.RatingChannel);
                if (flaw == "undeclared") rating.Declared = false;
                if (flaw == "notPerCpu") rating.PerCpu = false;
                if (flaw == "notPerSegment") rating.PerSegment = false;
                if (flaw == "missing") facts.Channels.Remove(rating);

                HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(facts);

                Assert.True(verdict.HasErrors && verdict.Has("channels", HistSelfCheckRules.RatingChannel), flaw);
                Assert.Equal(new[] { HistCategories.Export, HistCategories.Race }, verdict.DisabledCategories.ToArray(), flaw + ": the categories that act only through this channel");
            }
        }

        [Test]
        public void TheOtherTwoChannels_AreWarnings_AndDisableNothing()
        {
            HistCheckFacts facts = Healthy();
            facts.Channels.First(c => c.Name == HistSelfCheckRules.AcquisitionChannel).Declared = false;
            facts.Channels.First(c => c.Name == HistSelfCheckRules.GpuChannel).Declared = false;

            HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(facts);

            Assert.False(verdict.HasErrors);
            Assert.Equal(2, verdict.Warnings);
            Assert.True(verdict.Has("channels", "acquisition.price") && verdict.Has("channels", "gpu.demand"));
            Assert.Equal(0, verdict.DisabledCategories.Count);
        }

        [Test]
        public void TheFeature_HasNoPatchOnTheMethodsTheCoreOwns()
        {
            foreach (string target in HistSelfCheckRules.ForbiddenPatchTargets)
            {
                HistCheckFacts facts = Healthy();
                facts.OwnPatchTargets.Add("CompanyEventHandler.TriggerSolarFoundriesSpinOff");
                facts.OwnPatchTargets.Add(target);

                HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(facts);

                Assert.True(verdict.HasErrors && verdict.Has("patches", target), target);
            }
            Assert.True(HistSelfCheckRules.ForbiddenPatchTargets.Contains("Company.AddCpu") && HistSelfCheckRules.ForbiddenPatchTargets.Contains("AICompany.AddCpu")
                && HistSelfCheckRules.ForbiddenPatchTargets.Contains("DateController.NextMonth"), "the three of the design");

            HistCheckFacts clean = Healthy();
            clean.OwnPatchTargets.Add("CompanyEventHandler.TriggerSolarFoundriesSpinOff");
            Assert.True(HistSelfCheckRules.Evaluate(clean).Ok, "the one optional patch of FR-16 is the feature's own");
        }

        [Test]
        public void TheTargetsInTheGame_AreWarnings_BecauseTheyAreOnlyForTheLettersAndTheClosingOfTheCasesOfTheBankrupt()
        {
            HistCheckFacts facts = Healthy();
            facts.SpinOffMethodFound = false;
            facts.LicenseEventFound = false;
            facts.BankruptcyEventFound = false;

            HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(facts);

            Assert.False(verdict.HasErrors);
            Assert.Equal(3, verdict.Warnings);
            Assert.True(verdict.Has("targets", "TriggerSolarFoundriesSpinOff") && verdict.Has("targets", "OnX86LicenseRevoked") && verdict.Has("targets", "OnCompanyWentBankrupt"));
        }

        [Test]
        public void TheNames_MustCarryThePrefixOfTheFeature_AndTheStringsMustBeThere()
        {
            HistCheckFacts facts = Healthy();
            facts.PackEventIds.Add("other.event");
            facts.PredicateNameAllowed = name => name != HistPredicates.CaseActive;
            facts.MissingRu.Add("hist.x.title");
            facts.MissingEn.Add("hist.x.title");
            facts.MissingEn.Add("hist.y.text");

            HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(facts);

            Assert.True(verdict.Has("names", "other.event") && verdict.Has("names", HistPredicates.CaseActive));
            Assert.True(verdict.Has("strings", "Russian") && verdict.Has("strings", "hist.y.text"));
            Assert.Equal("selfcheck: 2 error(s), 2 warning(s)", verdict.Line);
        }

        [Test]
        public void NoFacts_IsAnError_NotAnException()
        {
            HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(null);

            Assert.True(verdict.HasErrors);
            Assert.False(verdict.Ok);
        }

        [Test]
        public void TheRequiredEffectTypes_AreTheEightOfTheFeature()
        {
            Assert.Equal(8, HistSelfCheckRules.RequiredEffectTypes.Length);
            Assert.Equal(new[] { "market.rating.cpu", "acquisition.price", "gpu.demand" }, HistSelfCheckRules.RequiredChannels);
        }

        // ---- the categories a failed channel switches off ----

        [Test]
        public void CategoryBlocks_SwitchACategoryOffForTheSession_WithAReason()
        {
            var blocks = new CategoryBlocks();
            Assert.False(blocks.IsBlocked(HistCategories.Export));
            Assert.Null(blocks.ReasonOf(HistCategories.Export));

            blocks.Block(HistCategories.Export, "first");
            blocks.Block(HistCategories.Export, "second");
            blocks.Block(null, "none");
            blocks.Block(string.Empty, "none");

            Assert.True(blocks.IsBlocked(HistCategories.Export));
            Assert.False(blocks.IsBlocked(HistCategories.Race));
            Assert.Equal("first", blocks.ReasonOf(HistCategories.Export), "the first reason stays");
            Assert.Equal(1, blocks.Count);

            blocks.Clear();
            Assert.False(blocks.IsBlocked(HistCategories.Export));
        }

        [Test]
        public void TheSettings_ConsultTheBlocks_ToTheSameEndAsThePlayersSwitch()
        {
            string settings = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents", "HistSettings.cs")), true);
            Assert.True(Regex.IsMatch(settings, @"!Blocks\.IsBlocked\(category\)\s*&&\s*ModSettings\.GetBool"), "a blocked category is off whatever the player's settings say");

            string check = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents", "HistSelfCheck.cs")), true);
            Assert.True(check.Contains("HistSettings.Blocks.Block(") && check.Contains("verdict.DisabledCategories"), "the verdict of the check is applied through the blocks");
        }

        // ---- the entries of the calendar when a category is off or a channel failed ----

        private sealed class Host : ISchedulerHost
        {
            public readonly HashSet<string> Enabled = new HashSet<string>(HistCategories.All.Where(HistCategories.DefaultEnabled), StringComparer.Ordinal);
            public readonly CategoryBlocks Blocks = new CategoryBlocks();
            public readonly List<string> Fires = new List<string>();
            public readonly List<string> Fallbacks = new List<string>();

            public bool IsCategoryEnabled(string category) { return Enabled.Contains(category) && !Blocks.IsBlocked(category); }

            public RoleResult ResolveRoles(CalendarEntry entry)
            {
                var roles = new RoleResult();
                foreach (RoleSpec role in entry.Roles) roles.Targets[role.Name] = new List<string> { "ai:6" };
                return roles;
            }

            public bool ConditionsHold(CalendarEntry entry, RoleResult roles) { return true; }

            public FireOutcome FireStage(CalendarEntry entry, int stageIndex, RoleResult roles)
            {
                Fires.Add(entry.Id + "#" + stageIndex);
                return FireOutcome.Fired;
            }

            public void FireFallback(CalendarEntry entry) { Fallbacks.Add(entry.Id); }

            public void Log(string level, string message) { }

            public void Fault(CalendarEntry entry, Exception ex) { }
        }

        private static CalendarEntry RealEntry(string id)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Content", "historical-events", "calendar.json");
            var records = HistFixture.ObjFromFile(SourceScan.ReadText(path)).GetArray("records").OfType<PTMod.Core.Logic.Json.JsonObject>();
            var json = records.First(r => r.GetString("id") == id);
            var messages = new List<string>();
            CalendarEntry entry = HistFixture.Map(json, messages);
            Assert.NotNull(entry, string.Join(" | ", messages));
            return entry;
        }

        private static int Day(string iso)
        {
            SimpleDate date;
            Assert.True(SimpleDate.TryParse(iso, out date), iso);
            return date.Days;
        }

        private sealed class Game
        {
            public readonly Host Host = new Host();
            public HistSaveData Data = new HistSaveData();
            public readonly SchedulerCore Core;

            public Game(params CalendarEntry[] entries)
            {
                List<CalendarEntry> calendar = entries.ToList();
                Core = new SchedulerCore(() => calendar, () => Data, Host, () => new Xoshiro128(7), () => 45);
            }

            public void Run(string from, string to)
            {
                for (int day = Day(from); day <= Day(to); day++)
                {
                    Core.OnDay(day);
                    if (SimpleDate.FromDays(day).Day == 1) Core.OnMonth(day);
                }
            }
        }

        [Test]
        public void AC_17_1_ACategoryThatIsOff_MakesTheEntryDisabled_WithoutALetter()
        {
            CalendarEntry tohoku = RealEntry("hist.2011.tohoku");
            Assert.Equal("disaster", tohoku.Category);
            var game = new Game(tohoku);
            Assert.False(game.Host.Enabled.Contains("disaster"), "disaster is off by default");

            Assert.True(game.Core.Initialize(Day("2010-01-01")));
            game.Run("2010-01-01", "2011-03-12");

            Assert.Equal(EntryStatus.Disabled, game.Core.GetStatus("hist.2011.tohoku"));
            Assert.Equal(0, game.Host.Fires.Count, "no event, so no letter");
            Assert.Equal(0, game.Host.Fallbacks.Count, "not even the news that it did not happen: the player chose it");
        }

        [Test]
        public void AC_17_2_ACategoryTurnedOnInTheMiddleOfAGame_LetsTheEntryFireAsUsual()
        {
            CalendarEntry tohoku = RealEntry("hist.2011.tohoku");
            var game = new Game(tohoku);

            Assert.True(game.Core.Initialize(Day("2010-01-01")));
            game.Run("2010-01-01", "2010-06-30");
            game.Host.Enabled.Add("disaster");   // the player ticks the box in 2010
            game.Run("2010-07-01", "2011-03-12");

            Assert.Equal(EntryStatus.Fired, game.Core.GetStatus("hist.2011.tohoku"));
            Assert.Equal(new[] { "hist.2011.tohoku#0" }, game.Host.Fires.ToArray());
        }

        [Test]
        public void ACategoryTurnedOff_BeforeItsDate_DisablesTheEntryAtItsDate_AndOnAgain_WithinTheWindow_ItFires()
        {
            CalendarEntry tohoku = RealEntry("hist.2011.tohoku");
            var game = new Game(tohoku);
            game.Host.Enabled.Add("disaster");

            Assert.True(game.Core.Initialize(Day("2010-01-01")));
            game.Run("2010-01-01", "2011-02-28");
            game.Host.Enabled.Remove("disaster");
            game.Run("2011-03-01", "2011-03-12");
            Assert.Equal(EntryStatus.Disabled, game.Core.GetStatus("hist.2011.tohoku"));

            game.Host.Enabled.Add("disaster");
            game.Run("2011-03-13", "2011-05-02");
            Assert.Equal(EntryStatus.Fired, game.Core.GetStatus("hist.2011.tohoku"), "an entry that was Disabled comes back on the 1st of a month while its window is open (TSK-006)");
        }

        [Test]
        public void AFailedRatingChannel_MakesTheEntriesOfExportAndRace_Disabled()
        {
            CalendarEntry export = RealEntry("hist.2022.exportcontrol");
            CalendarEntry race = RealEntry("hist.2000.ghzrace");
            CalendarEntry plain = RealEntry("hist.1995.win95");

            HistCheckFacts facts = Healthy();
            facts.Channels.First(c => c.Name == HistSelfCheckRules.RatingChannel).Declared = false;
            HistCheckVerdict verdict = HistSelfCheckRules.Evaluate(facts);

            var early = new Game(race, plain);
            foreach (string category in verdict.DisabledCategories) early.Host.Blocks.Block(category, "selfcheck");
            Assert.True(early.Core.Initialize(Day("2000-01-01")));
            early.Run("2000-01-01", "2000-03-20");
            Assert.Equal(EntryStatus.Disabled, early.Core.GetStatus("hist.2000.ghzrace"), "the race is switched off");
            Assert.Equal(0, early.Host.Fires.Count);

            var late = new Game(export);
            foreach (string category in verdict.DisabledCategories) late.Host.Blocks.Block(category, "selfcheck");
            Assert.True(late.Core.Initialize(Day("2022-01-01")));
            late.Run("2022-01-01", "2022-10-08");
            Assert.Equal(EntryStatus.Disabled, late.Core.GetStatus("hist.2022.exportcontrol"), "so are the export rules");
            Assert.Equal(0, late.Host.Fires.Count);

            // The blocks leave the others alone: Windows 95 still fires.
            var other = new Game(plain);
            foreach (string category in verdict.DisabledCategories) other.Host.Blocks.Block(category, "selfcheck");
            Assert.True(other.Core.Initialize(Day("1995-01-01")));
            other.Run("1995-01-01", "1995-10-01");
            Assert.Equal(EntryStatus.Fired, other.Core.GetStatus("hist.1995.win95"));
        }

        // ---- the lifecycle of the feature (read from the source) ----

        private static string FeatureCode()
        {
            return SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents", "HistoricalEventsFeature.cs")), true);
        }

        private static string Body(string code, string signature)
        {
            int start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature);
            int brace = code.IndexOf('{', start);
            int depth = 0;
            for (int i = brace; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) return code.Substring(brace, i - brace + 1);
            }
            return code.Substring(brace);
        }

        [Test]
        public void SwitchingTheFeatureOff_ReturnsThePricesAndForgetsTheProviders_ButNeverRemovesTheEntriesOfModModifiers()
        {
            string code = FeatureCode();
            string disabled = Body(code, "public void OnDisabled()");

            Assert.True(disabled.Contains("_services.Wars.OnFeatureDisabled()"), "the prices of the AI in the active wars go back (RestorePriceCut or the saved targets)");
            Assert.True(disabled.Contains("_services.Export.Forget()") && disabled.Contains("_services.Races.Forget()"), "the providers answer 1 until the rules are rebuilt");
            Assert.True(disabled.Contains("InvalidateProviders()"), "the cached answers of market.rating.cpu and acquisition.price are dropped");
            Assert.False(code.Contains("RemoveAll("), "the entries of the feature are kept in the save and are not applied while it is off (RemoveAll is never called)");
            Assert.True(disabled.Contains("ModHooks.Guard("), "NFR-2: no exception leaves OnDisabled");

            string invalidate = Body(code, "private static void InvalidateProviders()");
            Assert.True(invalidate.Contains("ModChannels.MarketRatingCpu") && invalidate.Contains("ModChannels.AcquisitionPrice"));
        }

        [Test]
        public void SwitchingTheFeatureOn_PicksUpWhereItStopped_WithoutMakingTheEntriesAgain()
        {
            string code = FeatureCode();
            string enabled = Body(code, "public void OnEnabled()");

            Assert.True(enabled.Contains("_scheduler.OnEnabled()"), "the queue is rebuilt from the state that was kept");
            Assert.True(enabled.Contains("InvalidateProviders()") && enabled.Contains("RunSelfCheckOnce()") && enabled.Contains("BuiltInEventNews.Attach()"));
            Assert.False(enabled.Contains("ModModifiers.Add") || enabled.Contains("CatchUp"), "no entry of ModModifiers is made again, and nothing is caught up for a game that is under way");
            Assert.True(enabled.Contains("ModHooks.Guard("));
        }

        [Test]
        public void TheSelfCheck_RunsOnce_WhenTheCalendarIsReadAndThePacksAre_AtGameReadyOrOnTheFirstDay()
        {
            string code = FeatureCode();
            string once = Body(code, "internal void RunSelfCheckOnce()");

            Assert.True(once.Contains("_selfChecked") && once.Contains("_calendarLoaded") && once.Contains("CalendarContent.LoadedPacks() == null"), "once, and only with the calendar and the packs");
            Assert.True(Body(code, "private void OnGameReady(GameReadyReason reason)").Contains("RunSelfCheckOnce()"));
            Assert.True(Body(code, "internal bool EnsureEventsChecked()").Contains("RunSelfCheckOnce()"), "the packs of a new game come after the fast-forward");
        }

        [Test]
        public void TheSelfCheck_CollectsEveryItemOfTheDesign()
        {
            string code = SourceScan.Strip(SourceScan.ReadText(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents", "HistSelfCheck.cs")), false);

            foreach (string item in new[]
            {
                "TriggerSolarFoundriesSpinOff", "OnX86LicenseRevoked", "OnCompanyWentBankrupt",            // the targets
                "EventRegistry.All", "definition.packId != HistIds.PackId",                                  // the pack
                "EventEffects.RegisteredTypes", "ModConditions.KnownTypes",                                  // the types and the predicates
                "ModModifiers.IsDeclared(", "ModModifiers.SpecOf(", "spec.PerCpu", "spec.PerSegment",        // the channels
                "feature.Calendar.Count", "ModIds.IsEventIdAllowed(", "ModIds.IsPredicateNameAllowed(",      // the calendar and the prefix
                "ModHooks.Report",                                                                             // the patches of the feature
            })
            {
                Assert.True(code.Contains(item), "the self-check looks at " + item);
            }
            Assert.True(Regex.IsMatch(code, @"ModHooks\.Guard\(\s*FeatureId\s*,\s*Site"), "NFR-2: under the guard");
            Assert.True(Regex.Matches(code, @"Ask\(""").Count >= 8, "every group of questions is asked inside its own try/catch");
        }

        [Test]
        public void TheFeature_HasNoPatchOfItsOwn_OnAddCpuOrNextMonth()
        {
            // The one patch is the optional postfix of FR-16; the ones the core owns are not touched (design.md, "Патчи Harmony").
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "HistoricalEvents");
            var patches = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), false);
                foreach (Match m in Regex.Matches(code, @"\[ModPatch\(\s*typeof\((?<type>\w+)\)\s*,\s*""(?<method>\w+)"""))
                    patches.Add(m.Groups["type"].Value + "." + m.Groups["method"].Value);
            }

            Assert.Equal(new[] { "GameCompanyEventHandler.TriggerSolarFoundriesSpinOff" }, patches.ToArray());
            Assert.False(patches.Any(p => p.EndsWith(".AddCpu", StringComparison.Ordinal) || p.EndsWith(".NextMonth", StringComparison.Ordinal)));
        }
    }
}
