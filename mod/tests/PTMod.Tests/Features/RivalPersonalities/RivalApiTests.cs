using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The decisions behind the public API (FR-13, TSK-020): <see cref="RivalApiCore"/> on the real books of the feature and the shipped content, with the
    /// game replaced by a fake (<see cref="IRivalApiEffects"/>) or by the real planner of the test rig. The conversion of the game's <c>ICompany</c> into a
    /// <c>UniqueID</c>, the facade and the null-object are read as text at the end (the game does not run in the tests; the facade has tests of its own in
    /// <c>RivalSaveSectionTests</c> and the architecture checks).
    /// </summary>
    [Covers(typeof(RivalApiCore), typeof(IRivalApiEffects), typeof(ExecutedCount))]
    public class RivalApiTests
    {
        private const int Intel = AttitudeRig.Intel;     // hegemon: baseline -10, sensitivity 1.2
        private const int Amd = AttitudeRig.Amd;         // price_warrior: baseline -15, sensitivity 1.3
        private const int Tsmc = AttitudeRig.Tsmc;       // foundry_partner: baseline +20, sensitivity 0.5

        private sealed class FakeEffects : IRivalApiEffects
        {
            public readonly List<string> Calls = new List<string>();
            public int CutResult = 3;
            public bool Throw;

            public int ApplyCut(string reactionId, int uniqueId, Segment segment, float depth, float floorMargin)
            {
                Calls.Add("cut " + reactionId + " ai:" + uniqueId + " " + segment + " " + depth + " " + floorMargin);
                if (Throw) throw new InvalidOperationException("boom");
                return CutResult;
            }

            public int RestoreCut(string reactionId)
            {
                Calls.Add("restore " + reactionId);
                if (Throw) throw new InvalidOperationException("boom");
                return 1;
            }

            public int RestoreRivalCuts(int uniqueId)
            {
                Calls.Add("restore-rival ai:" + uniqueId);
                return 0;
            }

            public int CancelReactions(int uniqueId, string reason)
            {
                Calls.Add("cancel ai:" + uniqueId);
                return 0;
            }

            public void RevertCharacter(int uniqueId)
            {
                Calls.Add("revert ai:" + uniqueId);
            }

            public void ApplyCharacter(int uniqueId)
            {
                Calls.Add("apply ai:" + uniqueId);
            }
        }

        /// <summary>The effects that act on the real planner and the real book of the rig, as the game side does.</summary>
        private sealed class RigEffects : IRivalApiEffects
        {
            private readonly ReactionRig _rig;
            public readonly List<string> Calls = new List<string>();

            public RigEffects(ReactionRig rig)
            {
                _rig = rig;
            }

            public int ApplyCut(string reactionId, int uniqueId, Segment segment, float depth, float floorMargin)
            {
                return 0;
            }

            public int RestoreCut(string reactionId)
            {
                return 0;
            }

            public int RestoreRivalCuts(int uniqueId)
            {
                Calls.Add("restore-rival ai:" + uniqueId);
                return 0;
            }

            public int CancelReactions(int uniqueId, string reason)
            {
                Calls.Add("cancel ai:" + uniqueId);
                return _rig.Planner.CancelFor(uniqueId, reason);
            }

            public void RevertCharacter(int uniqueId)
            {
                Calls.Add("revert ai:" + uniqueId);
            }

            public void ApplyCharacter(int uniqueId)
            {
                Calls.Add("apply ai:" + uniqueId);
            }
        }

        private static RivalApiCore NewApi(AttitudeRig rig, IRivalApiEffects effects, List<string> warnings = null)
        {
            return new RivalApiCore(rig.Book, rig.Ledger, effects, warnings != null ? (Action<string>)warnings.Add : null);
        }

        // ---- reads ----

        [Test]
        public void TheReads_GiveNullForThePlayer_AndForACompanyWithoutACharacter()
        {
            var rig = new AttitudeRig().AssignAll();
            RivalApiCore api = NewApi(rig, new FakeEffects());

            // the player has no UniqueID: the facade passes null
            Assert.True(api.GetAttitude(null) == null);
            Assert.True(api.GetAttitudeLevel(null) == null);
            Assert.Null(api.GetArchetypeId(null));

            // a company that is not in the game, or is not given a character yet
            Assert.True(api.GetAttitude(99) == null);
            Assert.True(api.GetAttitudeLevel(-1) == null);
            Assert.Null(api.GetArchetypeId(99));
            var empty = new AttitudeRig();
            Assert.Null(new RivalApiCore(empty.Book, empty.Ledger, new FakeEffects()).GetArchetypeId(Intel), "no character yet");
        }

        [Test]
        public void TheReads_GiveTheAttitudeTheLevelAndTheArchetype_OfARival()
        {
            var rig = new AttitudeRig().AssignAll();
            RivalApiCore api = NewApi(rig, new FakeEffects());

            Assert.Near(-10f, api.GetAttitude(Intel).Value, 1e-4, "hegemon starts at -10");
            Assert.Equal(AttitudeLevel.Neutral, api.GetAttitudeLevel(Intel).Value);
            Assert.Equal("hegemon", api.GetArchetypeId(Intel));

            Assert.Near(20f, api.GetAttitude(Tsmc).Value, 1e-4);
            Assert.Equal(AttitudeLevel.Respect, api.GetAttitudeLevel(Tsmc).Value);
            Assert.Equal("foundry_partner", api.GetArchetypeId(Tsmc));
        }

        [Test]
        public void TheReads_FollowTheLedger_AfterAChange()
        {
            var rig = new AttitudeRig().AssignAll();
            RivalApiCore api = NewApi(rig, new FakeEffects());

            rig.SetAttitude(Amd, -65f);

            Assert.Near(-65f, api.GetAttitude(Amd).Value, 1e-4);
            Assert.Equal(AttitudeLevel.Hostile, api.GetAttitudeLevel(Amd).Value);
        }

        // ---- ChangeAttitude ----

        [Test]
        public void ChangeAttitude_IsTheChangeOfTheLedger_WithTheSensitivityOfTheArchetype()
        {
            var rig = new AttitudeRig().AssignAll();
            RivalApiCore api = NewApi(rig, new FakeEffects());

            Assert.True(api.ChangeAttitude(Intel, -10f, "hist:price-war"));

            Assert.Near(-22f, api.GetAttitude(Intel).Value, 1e-4, "-10 + (-10 x 1.2)");
            IReadOnlyList<AttitudeEntry> history = rig.Ledger.History(Intel);
            Assert.Equal(1, history.Count);
            Assert.Equal("hist:price-war", history[0].reason);
            Assert.Equal(AttitudeLevel.Rivalry, api.GetAttitudeLevel(Intel).Value);
            Assert.Equal(1, rig.LevelEvents.Count, "the level moved: the notification is raised");
        }

        [Test]
        public void ChangeAttitude_AnEmptyReasonIsWrittenAsApi()
        {
            var rig = new AttitudeRig().AssignAll();
            RivalApiCore api = NewApi(rig, new FakeEffects());

            api.ChangeAttitude(Intel, 5f, null);
            api.ChangeAttitude(Intel, 5f, "   ");

            Assert.Equal(new List<string> { "api", "api" }, rig.Ledger.History(Intel).Select(h => h.reason).ToList());
        }

        [Test]
        public void ChangeAttitude_IsIgnoredForThePlayer_AnUnknownCompany_ABankruptOne_AndANumberThatIsNotOne()
        {
            var rig = new AttitudeRig().AssignAll();
            RivalApiCore api = NewApi(rig, new FakeEffects());
            rig.Bankrupt.Add(Amd);

            Assert.False(api.ChangeAttitude(null, 10f, "x"), "the player");
            Assert.False(api.ChangeAttitude(99, 10f, "x"), "unknown");
            Assert.False(api.ChangeAttitude(Amd, 10f, "x"), "bankrupt");
            Assert.False(api.ChangeAttitude(Intel, float.NaN, "x"), "NaN");

            Assert.Near(-15f, api.GetAttitude(Amd).Value, 1e-4);
            Assert.Near(-10f, api.GetAttitude(Intel).Value, 1e-4);
        }

        [Test]
        public void ChangeAttitude_IsHeldToTheScale()
        {
            var rig = new AttitudeRig().AssignAll();
            RivalApiCore api = NewApi(rig, new FakeEffects());

            api.ChangeAttitude(Tsmc, 1000f, "x");
            Assert.Near(100f, api.GetAttitude(Tsmc).Value, 1e-4);

            api.ChangeAttitude(Tsmc, -1000000f, "x");
            Assert.Near(-100f, api.GetAttitude(Tsmc).Value, 1e-4);
        }

        // ---- SetDormant ----

        [Test]
        public void SetDormant_PutsTheRivalAside_DropsItsReactions_GivesThePricesBack_AndTakesTheCharacterOff()
        {
            var rig = new AttitudeRig().AssignAll();
            var effects = new FakeEffects();
            RivalApiCore api = NewApi(rig, effects);

            Assert.True(api.SetDormant(Amd, true));

            Assert.True(rig.Book.IsDormant(Amd));
            Assert.Equal(new List<string> { "cancel ai:1", "restore-rival ai:1", "revert ai:1" }, effects.Calls);
        }

        [Test]
        public void SetDormant_CancelsThePlannedReactions_OfTheRival_AndOnlyOfIt()
        {
            var reactions = new ReactionRig();
            var effects = new RigEffects(reactions);
            var rivalApi = new RivalApiCore(reactions.Base.Book, reactions.Base.Ledger, effects);
            reactions.Base.SetAttitude(AttitudeRig.Intel, -30f);   // below the gate of -20: a hostile reaction is allowed
            PendingReaction amd = reactions.PlanOne(TriggerType.StrongLaunch, AttitudeRig.Amd, Segment.MidRange);
            PendingReaction intel = reactions.PlanOne(TriggerType.StrongLaunch, AttitudeRig.Intel, Segment.HighEnd);

            Assert.True(rivalApi.SetDormant(AttitudeRig.Amd, true));

            Assert.True(reactions.Reactions.FindPending(amd.id) == null, "the plan of the dormant rival is gone");
            Assert.True(reactions.Reactions.FindPending(intel.id) != null, "the others go on");
            Assert.True(reactions.Reactions.HistoryOf(AttitudeRig.Amd, 10).Any(h => h.outcome == HistoryOutcomes.Cancelled), "the history says it was cancelled");
        }

        [Test]
        public void ADormantRival_PlansNothing_AndIsNoLongerTriggered()
        {
            var reactions = new ReactionRig();
            var rivalApi = new RivalApiCore(reactions.Base.Book, reactions.Base.Ledger, new RigEffects(reactions));
            rivalApi.SetDormant(AttitudeRig.Amd, true);
            reactions.Rng.Chances.Enqueue(true);

            PlanResult result = reactions.Planner.OnTrigger(reactions.Trigger(TriggerType.StrongLaunch, AttitudeRig.Amd, Segment.MidRange));

            Assert.False(result.IsPlanned, "a dormant rival does not answer the player");
            Assert.Equal(0, reactions.Reactions.PendingSnapshot().Count);
        }

        [Test]
        public void SetDormant_False_BringsTheRivalBack_AndPutsTheCharacterOn()
        {
            var rig = new AttitudeRig().AssignAll();
            var effects = new FakeEffects();
            RivalApiCore api = NewApi(rig, effects);
            api.SetDormant(Amd, true);
            effects.Calls.Clear();

            Assert.True(api.SetDormant(Amd, false));

            Assert.False(rig.Book.IsDormant(Amd));
            Assert.Equal(new List<string> { "apply ai:1" }, effects.Calls);
        }

        [Test]
        public void SetDormant_ARepeatedCall_ChangesNothing()
        {
            var rig = new AttitudeRig().AssignAll();
            var effects = new FakeEffects();
            RivalApiCore api = NewApi(rig, effects);

            Assert.True(api.SetDormant(Amd, true));
            effects.Calls.Clear();
            Assert.False(api.SetDormant(Amd, true), "already dormant");
            Assert.False(api.SetDormant(Intel, false), "was not dormant");

            Assert.Equal(0, effects.Calls.Count);
            Assert.Equal(1, rig.State.dormant.Count);
        }

        [Test]
        public void SetDormant_IsIgnoredForThePlayerAndForNoCompany()
        {
            var rig = new AttitudeRig().AssignAll();
            var effects = new FakeEffects();
            RivalApiCore api = NewApi(rig, effects);

            Assert.False(api.SetDormant(null, true));
            Assert.False(api.SetDormant(-1, true));

            Assert.Equal(0, effects.Calls.Count);
            Assert.Equal(0, rig.State.dormant.Count);
        }

        [Test]
        public void SetDormant_ADormantRival_KeepsItsAttitudeAndItsCharacterId()
        {
            var rig = new AttitudeRig().AssignAll();
            RivalApiCore api = NewApi(rig, new FakeEffects());
            rig.SetAttitude(Intel, -50f);

            api.SetDormant(Intel, true);

            Assert.Near(-50f, api.GetAttitude(Intel).Value, 1e-4);
            Assert.Equal("hegemon", api.GetArchetypeId(Intel));
        }

        [Test]
        public void SetDormant_AFailingStep_DoesNotStopTheOthers()
        {
            var rig = new AttitudeRig().AssignAll();
            var warnings = new List<string>();
            var effects = new ThrowingEffects();
            RivalApiCore api = NewApi(rig, effects, warnings);

            Assert.True(api.SetDormant(Amd, true));

            Assert.Equal(new List<string> { "cancel", "restore-rival", "revert" }, effects.Calls, "every step was tried");
            Assert.Equal(3, warnings.Count);
            Assert.True(rig.Book.IsDormant(Amd), "the record stands");
        }

        private sealed class ThrowingEffects : IRivalApiEffects
        {
            public readonly List<string> Calls = new List<string>();

            public int ApplyCut(string reactionId, int uniqueId, Segment segment, float depth, float floorMargin)
            {
                throw new InvalidOperationException();
            }

            public int RestoreCut(string reactionId)
            {
                throw new InvalidOperationException();
            }

            public int RestoreRivalCuts(int uniqueId)
            {
                Calls.Add("restore-rival");
                throw new InvalidOperationException();
            }

            public int CancelReactions(int uniqueId, string reason)
            {
                Calls.Add("cancel");
                throw new InvalidOperationException();
            }

            public void RevertCharacter(int uniqueId)
            {
                Calls.Add("revert");
                throw new InvalidOperationException();
            }

            public void ApplyCharacter(int uniqueId)
            {
                throw new InvalidOperationException();
            }
        }

        // ---- price wars ----

        [Test]
        public void ApplyPriceCut_GoesToThePriceBook_AndGivesTheNumberOfCpus()
        {
            var rig = new AttitudeRig().AssignAll();
            var effects = new FakeEffects { CutResult = 4 };
            RivalApiCore api = NewApi(rig, effects);

            int cut = api.ApplyPriceCut("hist:price-war-1985", Amd, Segment.MidRange, 0.10f, 0.08f);

            Assert.Equal(4, cut);
            Assert.Equal(new List<string> { "cut hist:price-war-1985 ai:1 MidRange 0.1 0.08" }, effects.Calls);
        }

        [Test]
        public void ApplyPriceCut_GivesZero_ForThePlayerAndForNoCompany()
        {
            var effects = new FakeEffects();
            RivalApiCore api = NewApi(new AttitudeRig().AssignAll(), effects);

            Assert.Equal(0, api.ApplyPriceCut("hist:war", null, Segment.MidRange, 0.1f, 0.08f));
            Assert.Equal(0, api.ApplyPriceCut("hist:war", -1, Segment.MidRange, 0.1f, 0.08f));
            Assert.Equal(0, effects.Calls.Count);
        }

        [Test]
        public void ApplyPriceCut_RefusesAnIdItCannotUse()
        {
            var warnings = new List<string>();
            var effects = new FakeEffects();
            RivalApiCore api = NewApi(new AttitudeRig().AssignAll(), effects, warnings);

            Assert.Equal(0, api.ApplyPriceCut(null, Amd, Segment.MidRange, 0.1f, 0.08f), "no id");
            Assert.Equal(0, api.ApplyPriceCut("", Amd, Segment.MidRange, 0.1f, 0.08f), "an empty id");
            Assert.Equal(0, api.ApplyPriceCut(new string('x', 65), Amd, Segment.MidRange, 0.1f, 0.08f), "more than 64 characters");
            Assert.Equal(0, api.ApplyPriceCut("r-17", Amd, Segment.MidRange, 0.1f, 0.08f), "an id of the feature's own reactions");

            Assert.Equal(0, effects.Calls.Count);
            Assert.Equal(4, warnings.Count);
            Assert.True(api.ApplyPriceCut("a:" + new string('x', 62), Amd, Segment.MidRange, 0.1f, 0.08f) > 0, "64 characters are fine");
            Assert.True(api.ApplyPriceCut("r-x", Amd, Segment.MidRange, 0.1f, 0.08f) > 0, "'r-' with a word is a caller's id");
        }

        [Test]
        public void ApplyPriceCut_RefusesASegmentThatIsNotOneOfTheFour()
        {
            var effects = new FakeEffects();
            RivalApiCore api = NewApi(new AttitudeRig().AssignAll(), effects);

            Assert.Equal(0, api.ApplyPriceCut("hist:war", Amd, Segment.Any, 0.1f, 0.08f));
            Assert.Equal(0, api.ApplyPriceCut("hist:war", Amd, (Segment)7, 0.1f, 0.08f));
            Assert.Equal(0, effects.Calls.Count);
            foreach (Segment segment in new[] { Segment.HighEnd, Segment.MidRange, Segment.LowEnd, Segment.Industries })
            {
                Assert.True(api.ApplyPriceCut("hist:war", Amd, segment, 0.1f, 0.08f) > 0, segment.ToString());
            }
        }

        [Test]
        public void ApplyPriceCut_AFailureInTheGame_GivesZero()
        {
            var warnings = new List<string>();
            RivalApiCore api = NewApi(new AttitudeRig().AssignAll(), new FakeEffects { Throw = true }, warnings);

            Assert.Equal(0, api.ApplyPriceCut("hist:war", Amd, Segment.MidRange, 0.1f, 0.08f));
            Assert.Equal(1, warnings.Count);
        }

        [Test]
        public void RestorePriceCut_GoesToThePriceBook_ForTheCallersIdOnly()
        {
            var effects = new FakeEffects();
            RivalApiCore api = NewApi(new AttitudeRig().AssignAll(), effects);

            api.RestorePriceCut("hist:price-war-1985");
            api.RestorePriceCut("r-3");     // the cut of a reaction of the feature is not for a caller to end
            api.RestorePriceCut(null);
            api.RestorePriceCut("");

            Assert.Equal(new List<string> { "restore hist:price-war-1985" }, effects.Calls);
        }

        [Test]
        public void RestorePriceCut_AFailureInTheGame_IsLoggedAndSwallowed()
        {
            var warnings = new List<string>();
            RivalApiCore api = NewApi(new AttitudeRig().AssignAll(), new FakeEffects { Throw = true }, warnings);

            api.RestorePriceCut("hist:war");

            Assert.Equal(1, warnings.Count);
        }

        // ---- the counters behind rival.reactionsExecuted ----

        [Test]
        public void ReactionsExecuted_AreCounted_PerRival_AllYearsTogether()
        {
            var reactions = new ReactionRig();
            ReactionBook book = reactions.Reactions;

            Assert.Equal(0, book.ExecutedBy(AttitudeRig.Amd));
            book.MarkExecuted(AttitudeRig.Amd, new DateTime(1981, 6, 1), true);
            book.MarkExecuted(AttitudeRig.Amd, new DateTime(1982, 6, 1), true);   // another game year: the count of the year restarts, this one does not
            book.MarkExecuted(AttitudeRig.Intel, new DateTime(1982, 7, 1), true);

            Assert.Equal(2, book.ExecutedBy(AttitudeRig.Amd));
            Assert.Equal(1, book.ExecutedBy(AttitudeRig.Intel));
            Assert.Equal(0, book.ExecutedBy(AttitudeRig.Ibm));
            Assert.Equal(3, book.ExecutedTotal());
            Assert.Equal(2, book.HostileExecutedThisYear(new DateTime(1982, 8, 1)), "the count of the year is a different counter: 2 in 1982");
        }

        [Test]
        public void TheFriendlyOffer_IsNotAReactionExecuted()
        {
            var reactions = new ReactionRig();

            reactions.Reactions.MarkPartnerOffer(AttitudeRig.Tsmc, new DateTime(1990, 5, 1));
            reactions.Reactions.MarkExecuted(AttitudeRig.Tsmc, new DateTime(1990, 6, 1), false);

            Assert.Equal(0, reactions.Reactions.ExecutedBy(AttitudeRig.Tsmc));
            Assert.Equal(0, reactions.Reactions.ExecutedTotal());
        }

        [Test]
        public void TheCounters_SurviveTheSaveRoundTrip()
        {
            var reactions = new ReactionRig();
            reactions.Reactions.MarkExecuted(AttitudeRig.Amd, new DateTime(1981, 6, 1), true);
            reactions.Reactions.MarkPartnerOffer(AttitudeRig.Tsmc, new DateTime(1981, 7, 1));

            RivalState back = RivalSaveCodec.Read(RivalSaveCodec.Write(reactions.State), null);
            var loaded = new ReactionBook(() => back);

            Assert.Equal(1, loaded.ExecutedBy(AttitudeRig.Amd));
            Assert.Equal(1, loaded.ExecutedTotal());
            Assert.Equal(3, loaded.MonthsSincePartnerOffer(AttitudeRig.Tsmc, new DateTime(1981, 10, 1)));
        }

        [Test]
        public void ADamagedCounter_IsReadAsZero()
        {
            var state = new RivalState();
            state.counters.executed.Add(new ExecutedCount { uid = 1, count = -4 });
            state.counters.executed.Add(new ExecutedCount { uid = 2, count = 5 });
            var book = new ReactionBook(() => state);

            Assert.Equal(0, book.ExecutedBy(1));
            Assert.Equal(5, book.ExecutedBy(2));
            Assert.Equal(5, book.ExecutedTotal());
            book.MarkExecuted(1, new DateTime(1990, 1, 1), true);
            Assert.Equal(1, book.ExecutedBy(1), "a negative count starts again from one");
        }

        // ---- the facade and the conversion of the company (read as text) ----

        private static string Code(params string[] parts)
        {
            string path = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities" }.Concat(parts).ToArray());
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheService_ConvertsTheCompanyIntoAUniqueId_AndTheFacadeIsRegisteredOnce()
        {
            string service = Code("Integration", "RivalApiService.cs");
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(service, @"internal sealed class RivalApiService : IRivalPersonalitiesApi, IRivalApiEffects"));
            Assert.True(Regex.IsMatch(service, @"var rival = company as AICompany;"), "the player and anything that is no AI company have no UniqueID");
            Assert.True(Regex.IsMatch(service, @"if \(ReferenceEquals\(company, null\)\) return null;"));
            Assert.True(Regex.IsMatch(service, @"public bool IsEnabled\s*\{\s*get \{ return true; \}"), "the facade answers false while the feature is off");
            Assert.Equal(1, Regex.Matches(feature, @"RivalPersonalitiesApi\.Register\(_api\);").Count, "registered once, in Register");
            Assert.True(Regex.IsMatch(service, @"feature\.Executor\.Started \+= OnReactionStarted;"), "ReactionStarted comes from the executor");
        }

        [Test]
        public void TheService_ReachesThePriceBookThePlannerAndTheRegistry_ThroughTheFeature()
        {
            string service = Code("Integration", "RivalApiService.cs");

            Assert.True(Regex.IsMatch(service, @"_feature\.Prices\.ApplyCut\(reactionId, rival, segment, depth, floorMargin\)"), "the same price book as the PriceCut reaction");
            Assert.True(Regex.IsMatch(service, @"_feature\.Prices\.Restore\(reactionId\)"));
            Assert.True(Regex.IsMatch(service, @"_feature\.Prices\.RestoreRival\(uniqueId\)"));
            Assert.True(Regex.IsMatch(service, @"_feature\.Planner\.CancelFor\(uniqueId, reason\)"));
            Assert.True(Regex.IsMatch(service, @"_feature\.Registry\.Revert\(uniqueId\)"));
            Assert.True(Regex.IsMatch(service, @"_feature\.Registry\.EnsureAssigned\(rival\)") && Regex.IsMatch(service, @"_feature\.Registry\.Apply\(rival,"));
        }

        [Test]
        public void TheApiFolder_IsUntouched_AndHasNoDependencyOnTheRestOfTheFeature()
        {
            // the Api folder is compiled into every feature that depends on this one, alone: nothing of the implementation may creep in
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities", "Api");
            string[] expected = { "IRivalPersonalitiesApi.cs", "NullRivalPersonalitiesApi.cs", "RivalPersonalities.cs", "RivalPersonalitiesApi.cs", "RivalPersonalitiesApiHost.cs" };

            foreach (string name in expected) Assert.True(File.Exists(Path.Combine(folder, name)), name);
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                Assert.False(Regex.IsMatch(code, @"\b(RivalApiCore|RivalApiService|AttitudeLedger|PersonalityBook|PriceBook|ReactionBook)\b"), Path.GetFileName(path) + " names the implementation");
            }
        }
    }
}
