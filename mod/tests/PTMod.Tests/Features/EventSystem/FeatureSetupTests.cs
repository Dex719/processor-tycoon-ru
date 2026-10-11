using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.EventSystem;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.EventSystem
{
    /// <summary>
    /// The entry point of the feature (TSK-006; design.md "Точка входа фичи"): the registration in the order of the design over a fake feature context, the
    /// three parameters, the identity of <c>EventSystemFeature</c>, no Harmony patch anywhere in the feature, and the scheduler's part of the save section
    /// (no version field, bad records dropped, a round trip without losses).
    /// </summary>
    [Covers(typeof(EventFeatureSetup), typeof(IEventFeatureHost), typeof(EventParams), typeof(EventSaveSection), typeof(EventSystemSaveDto), typeof(SchedulerState))]
    public class FeatureSetupTests
    {
        private sealed class RecordingHost : IEventFeatureHost
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<ParamSpec> Params = new List<ParamSpec>();
            public ISideFileParticipant Participant;
            public Action DayHandler;

            public void SaveParticipant(ISideFileParticipant participant)
            {
                Calls.Add("save:" + participant.SectionId);
                Participant = participant;
            }

            public void OnDayPassed(Action handler)
            {
                Calls.Add("tick:DayPassed");
                DayHandler = handler;
            }

            public void Param(ParamSpec spec)
            {
                Calls.Add("param:" + spec.Key);
                Params.Add(spec);
            }

            public void HubPage()
            {
                Calls.Add("hubpage");
            }

            public void InstallApi()
            {
                Calls.Add("api");
            }

            public void RegisterOwnContent()
            {
                Calls.Add("content");
            }

            public void SubscribeCoreEvents()
            {
                Calls.Add("subscribe");
            }
        }

        private static string FeatureFolder()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "EventSystem");
        }

        [Test]
        public void Register_DeclaresEverythingInTheOrderOfTheDesign_OverAFakeContext()
        {
            var host = new RecordingHost();
            var section = new EventSaveSection(new SchedulerState(), new EventHistory(), null);
            int days = 0;

            EventFeatureSetup.Register(host, section, () => days++, "event-system");

            Assert.Equal("save:event-system,tick:DayPassed,param:frequency,param:pauseAllChoices,param:rivalNews,hubpage,api,content,subscribe",
                string.Join(",", host.Calls));
            Assert.True(ReferenceEquals(section, host.Participant));
            host.DayHandler();
            Assert.Equal(1, days, "the day handler is the one given");

            Assert.Throws<ArgumentNullException>(() => EventFeatureSetup.Register(null, section, () => { }, "event-system"));
            Assert.Throws<ArgumentNullException>(() => EventFeatureSetup.Register(host, null, () => { }, "event-system"));
            Assert.Throws<ArgumentNullException>(() => EventFeatureSetup.Register(host, section, null, "event-system"));
        }

        [Test]
        public void TheParameters_AreTheThreeOfFR_11()
        {
            ParamSpec[] specs = EventParams.Create("event-system");
            Assert.Equal(3, specs.Length);

            Assert.Equal("frequency", specs[0].Key);
            Assert.Equal(ParamType.Int, specs[0].Type);
            Assert.Equal(2.0, specs[0].Default, "'Обычно' by default");
            Assert.Equal(0.0, specs[0].Min);
            Assert.Equal(3.0, specs[0].Max);

            Assert.Equal("pauseAllChoices", specs[1].Key);
            Assert.Equal(ParamType.Bool, specs[1].Type);
            Assert.Equal(0.0, specs[1].Default);

            Assert.Equal("rivalNews", specs[2].Key);
            Assert.Equal(ParamType.Bool, specs[2].Type);
            Assert.Equal(1.0, specs[2].Default);

            foreach (ParamSpec spec in specs)
            {
                Assert.Equal("event-system", spec.FeatureId);
                Assert.True(spec.LabelKey.StartsWith("es.param.", StringComparison.Ordinal), spec.LabelKey);
                Assert.Equal(spec.LabelKey + ".tooltip", spec.TooltipKey);
                string problem;
                Assert.True(spec.Clone().Normalize(out problem), spec.Key + ": " + problem);
            }
        }

        [Test]
        public void TheFeatureClass_IsTheEntryPointTheDesignDescribes()
        {
            string path = Path.Combine(FeatureFolder(), "EventSystemFeature.cs");
            Assert.True(File.Exists(path), "no EventSystemFeature.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);

            Assert.True(Regex.IsMatch(code, @"\[ModFeature\]\s*public\s+sealed\s+class\s+EventSystemFeature\s*:\s*IModFeature\b"));
            Assert.True(Regex.IsMatch(code, @"namespace\s+PTMod\.Features\.EventSystem\b"));
            Assert.Equal("event-system", FeatureGraph.FeatureOfNamespace("PTMod.Features.EventSystem"));
            Assert.True(Regex.IsMatch(code, @"NameKey\s*\{\s*get\s*\{\s*return\s+""es\.feature\.name"";"), "NameKey");
            Assert.True(Regex.IsMatch(code, @"DescriptionKey\s*\{\s*get\s*\{\s*return\s+""es\.feature\.desc"";"), "DescriptionKey");
            Assert.True(Regex.IsMatch(code, @"DependsOn\s*\{\s*get\s*\{\s*return\s+new\s+string\[0\];"), "DependsOn is empty");
            Assert.True(Regex.IsMatch(code, @"EnabledByDefault\s*\{\s*get\s*\{\s*return\s+true;"), "EnabledByDefault");
            Assert.True(Regex.IsMatch(code, @"SupportsHotToggle\s*\{\s*get\s*\{\s*return\s+true;"), "SupportsHotToggle");
            Assert.True(code.Contains("EventFeatureSetup.Register("), "Register goes through EventFeatureSetup");
            Assert.True(code.Contains("_ctx.OnTick(TickPhase.DayPassed, handler)"), "the day tick of the core");
            Assert.True(code.Contains("EventSystemApi.Install(_engine)"), "the API backend is installed");

            int entries = 0;
            foreach (string file in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                if (SourceScan.Strip(SourceScan.ReadText(file), true).Contains("[ModFeature]")) entries++;
            }
            Assert.Equal(1, entries, "one [ModFeature] class in the feature");
        }

        [Test]
        public void TheFeature_HasNoHarmonyPatchOfItsOwn()
        {
            var patch = new Regex(@"\bctx\s*\.\s*Patch\s*\(|\[\s*ModPatch\b|\bHarmonyLib\b|\bHarmony\s*\(");
            var violations = new List<string>();
            string src = SourceScan.SrcDirOrFail();
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (Match match in patch.Matches(code)) violations.Add(SourceScan.Relative(src, path) + ":" + SourceScan.LineOf(code, match.Index) + ": " + match.Value);
            }
            Assert.True(violations.Count == 0, "the engine declares no patches (design.md, 'Патчи Harmony'):\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheSaveSection_RoundTrips_WithoutAVersionField()
        {
            var state = new SchedulerState { LastProcessedDay = 6339 };
            state.SetLastFired("es.star_engineer", "player", 6330);
            state.SetLastFired("es.all", EventCompanyKeys.AllCompaniesCooldown, 6300);
            state.SetCategoryLastDay("universal", 6330);
            state.MarkUsedUp("es.once");
            state.SetEdgeLevel("es.edge", true);
            state.Scheduled.Add(new ScheduledFire { eventId = "es.patent_win", dueDay = 6402, companyKey = "player", parentId = 49 });
            state.Deferred.Add(new DeferredFire { eventId = "es.date", companyKey = "player", queuedDay = 6338, args = new[] { "a" } });
            var history = new EventHistory();
            history.RecordFired("es.star_engineer", "player", 6330);
            history.SetFlag("patent_fought", "player", 6290);
            var section = new EventSaveSection(state, history, line => Assert.Fail(line));

            Assert.Equal("event-system", section.SectionId);
            Assert.Equal(1, section.SchemaVersion);

            JsonObject written = section.Write();
            Assert.False(written.ContainsKey("_schema"), "the core writes _schema");
            Assert.False(written.ContainsKey("schema"));
            Assert.Equal(6339, written.GetInt32("lastProcessedDay"));
            string text = ModJson.Write(written, false);

            var state2 = new SchedulerState();
            var history2 = new EventHistory();
            var section2 = new EventSaveSection(state2, history2, line => Assert.Fail(line));
            section2.Read(written, 1);
            Assert.Equal(text, ModJson.Write(section2.Write(), false), "the same section after a round trip");

            Assert.Equal(6339, state2.LastProcessedDay);
            Assert.Equal(6330, state2.LastFired("es.star_engineer", "player"));
            Assert.Equal(6300, state2.LastFired("es.all", "*"));
            Assert.Equal(6330, state2.CategoryLastDay("universal"));
            Assert.True(state2.IsUsedUp("es.once"));
            Assert.True(state2.EdgeLevel("es.edge"));
            Assert.Equal(49, state2.Scheduled[0].parentId);
            Assert.True(state2.Scheduled[0].ignoreConditions, "a chain skips the conditions by default");
            Assert.Equal("a", state2.Deferred[0].args[0]);
            Assert.True(history2.HasFlag("patent_fought", "player"));
        }

        [Test]
        public void TheSaveSection_DropsRecordsThatMakeNoSense_AndResets()
        {
            JsonObject section = EventTestKit.Obj(
                "{ 'lastProcessedDay': 100, "
                + "'cooldowns': [ { 'eventId': 'es.a', 'companyKey': 'player', 'lastDay': 90 }, { 'eventId': 'es.b', 'companyKey': 'bank', 'lastDay': 1 }, { 'eventId': '', 'companyKey': 'player', 'lastDay': 1 } ], "
                + "'categories': [ { 'category': 'universal', 'lastDay': -5 } ], "
                + "'scheduled': [ { 'eventId': 'es.c', 'dueDay': 120, 'companyKey': 'nobody' }, { 'eventId': 'es.d', 'dueDay': 130 } ], "
                + "'deferred': [ { 'eventId': 'es.e', 'queuedDay': -1 } ], "
                + "'unknownFutureField': [ 1, 2, 3 ] }");
            var warnings = new List<string>();
            var state = new SchedulerState();
            var history = new EventHistory();
            var save = new EventSaveSection(state, history, warnings.Add);

            save.Read(section, 1);
            Assert.Equal(5, warnings.Count, "two cooldowns, the category, a scheduled and a deferred record: " + string.Join("; ", warnings));
            Assert.Equal(90, state.LastFired("es.a", "player"));
            Assert.Equal(1, state.Scheduled.Count);
            Assert.Null(state.Scheduled[0].companyKey, "the definition decides the target");
            Assert.Equal(0, state.Deferred.Count);

            save.ResetToDefaults();
            Assert.Equal(SchedulerState.NoDay, state.LastProcessedDay);
            Assert.Equal(0, state.Scheduled.Count);
            Assert.Equal(SchedulerState.NoDay, state.LastFired("es.a", "player"));

            save.Read(null, 1);
            Assert.Equal(SchedulerState.NoDay, state.LastProcessedDay, "no section: the defaults");
        }
    }
}
