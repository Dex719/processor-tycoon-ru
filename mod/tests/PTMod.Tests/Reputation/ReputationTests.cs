using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Reputation;

namespace PTMod.Tests.Reputation
{
    /// <summary>
    /// Reputation rules (FR-13, TSK-018) and company keys: AC-13.1 (clamp and history), AC-13.2 (drift without history and
    /// without Changed), AC-13.3 (48 months of drift of 14 companies keep the 6 Add entries), invalid input, history limit,
    /// restore from the side file, and the key check <c>^(player|ai:\d+)$</c>.
    /// </summary>
    [Covers(typeof(ReputationStore), typeof(ReputationChange), typeof(CompanyKey))]
    public class ReputationTests
    {
        private static readonly DateTime Day = new DateTime(1985, 3, 1);

        private static ReputationStore NewStore(out List<string> warnings, int limit = ReputationStore.DefaultHistoryLimit)
        {
            var sink = new List<string>();
            warnings = sink;
            return new ReputationStore(limit, sink.Add);
        }

        // ---- company keys ----

        [Test]
        public void CompanyKey_ValidKeys()
        {
            Assert.True(CompanyKey.IsValid("player"));
            Assert.True(CompanyKey.IsValid("ai:0"));
            Assert.True(CompanyKey.IsValid("ai:12"));
            Assert.True(CompanyKey.IsValid("ai:007"), "the pattern allows leading zeros");
            Assert.True(CompanyKey.IsValid("ai:99999999999999999999"), "the pattern puts no limit on the digits");
        }

        [Test]
        public void CompanyKey_InvalidKeys()
        {
            string[] bad =
            {
                null, "", " ", "Player", "PLAYER", "player ", " player", "player\n", "players", "ai", "ai:", "ai: 1", "ai:1 ", "ai:1\n",
                "AI:1", "ai:-1", "ai:+1", "ai:1.5", "ai:x", "ai:1x", "ai::1", "ai:١٢", "ai:１", "anyAI", "company", "ai:1,2",
            };
            foreach (string key in bad)
                Assert.False(CompanyKey.IsValid(key), "key \"" + key + "\" must be invalid");
        }

        [Test]
        public void CompanyKey_PlayerAndAiChecks()
        {
            Assert.True(CompanyKey.IsPlayer("player"));
            Assert.False(CompanyKey.IsPlayer("ai:1"));
            Assert.False(CompanyKey.IsPlayer(null));

            Assert.True(CompanyKey.IsAi("ai:5"));
            Assert.False(CompanyKey.IsAi("player"));
            Assert.False(CompanyKey.IsAi("ai:"));
            Assert.False(CompanyKey.IsAi(null));
        }

        [Test]
        public void CompanyKey_ForAiAndTryGetAiId()
        {
            Assert.Equal("ai:0", CompanyKey.ForAi(0));
            Assert.Equal("ai:13", CompanyKey.ForAi(13));
            Assert.Equal("ai:2147483647", CompanyKey.ForAi(int.MaxValue));
            Assert.Null(CompanyKey.ForAi(-1));

            int id;
            Assert.True(CompanyKey.TryGetAiId("ai:13", out id));
            Assert.Equal(13, id);
            Assert.True(CompanyKey.TryGetAiId("ai:2147483647", out id));
            Assert.Equal(int.MaxValue, id);
            Assert.True(CompanyKey.TryGetAiId("ai:007", out id));
            Assert.Equal(7, id);

            Assert.False(CompanyKey.TryGetAiId("ai:2147483648", out id), "does not fit in an int");
            Assert.False(CompanyKey.TryGetAiId("ai:99999999999999999999", out id));
            Assert.False(CompanyKey.TryGetAiId("player", out id));
            Assert.False(CompanyKey.TryGetAiId("ai:", out id));
            Assert.False(CompanyKey.TryGetAiId(null, out id));
        }

        [Test]
        public void CompanyKey_KeyOrSelector()
        {
            Assert.True(CompanyKey.IsKeyOrSelector("player"));
            Assert.True(CompanyKey.IsKeyOrSelector("ai:4"));
            Assert.True(CompanyKey.IsKeyOrSelector("anyAI"));
            Assert.False(CompanyKey.IsKeyOrSelector("anyai"));
            Assert.False(CompanyKey.IsKeyOrSelector("all"));
            Assert.False(CompanyKey.IsKeyOrSelector(null));
        }

        // ---- defaults ----

        [Test]
        public void Get_DefaultsToFifty()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            Assert.Equal(50f, ReputationStore.Default);
            Assert.Equal(50f, store.Get("player"));
            Assert.Equal(50f, store.Get("ai:7"), "a new AI company starts at 50");
            Assert.Equal(50f, store.Get(null));
            Assert.Equal(50f, store.Get("not a key"));
            Assert.Equal(0, store.History("player").Count);
            Assert.Equal(0, store.History("ai:7").Count);
            Assert.Equal(0, store.History(null).Count);
            Assert.Equal(0, store.Count);
            Assert.Equal(0, warnings.Count, "reading never warns");
        }

        // ---- Add (AC-13.1) ----

        [Test]
        public void Add_ClampsAtMaxAndRecordsRequestedDelta_AC_13_1()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            Assert.True(store.Set("player", 95f, "setup", Day));

            Assert.True(store.Add("player", 10f, "review", Day));

            Assert.Equal(100f, store.Get("player"));
            IReadOnlyList<ReputationChange> history = store.History("player");
            Assert.Equal(2, history.Count);
            Assert.Equal(10f, history[1].Delta, "the history keeps the requested delta");
            Assert.Equal(100f, history[1].Value);
            Assert.Equal("review", history[1].Reason);
            Assert.Equal(Day, history[1].Date);
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void Add_ClampsAtMin()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            store.Add("ai:3", -80f, "scandal", Day);

            Assert.Equal(0f, store.Get("ai:3"));
            Assert.Equal(-80f, store.History("ai:3")[0].Delta);
            Assert.Equal(0f, store.History("ai:3")[0].Value);
        }

        [Test]
        public void Add_AccumulatesFromDefault()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            store.Add("player", 7.5f, "a", Day);
            store.Add("player", -2.5f, "b", Day.AddDays(1));

            Assert.Near(55.0, store.Get("player"), 1e-5);
            Assert.Equal(2, store.History("player").Count);
            Assert.Near(57.5, store.History("player")[0].Value, 1e-5);
            Assert.Near(55.0, store.History("player")[1].Value, 1e-5);
            Assert.Equal(1, store.Count);
        }

        [Test]
        public void Add_InvalidKeyChangesNothing()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            foreach (string key in new[] { null, "", "Player", "ai:", "ai:x", "ai:1\n", "company" })
                Assert.False(store.Add(key, 5f, "x", Day), "key \"" + key + "\"");

            Assert.Equal(0, store.Count);
            Assert.Equal(7, warnings.Count);
            Assert.True(warnings[0].Contains("Add"), warnings[0]);
            Assert.True(warnings[0].Contains("not a company key"), warnings[0]);
        }

        [Test]
        public void Add_NanAndInfinityAreIgnoredWithWarning()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Add("player", 10f, "ok", Day);

            Assert.False(store.Add("player", float.NaN, "nan", Day));
            Assert.False(store.Add("player", float.PositiveInfinity, "inf", Day));
            Assert.False(store.Add("player", float.NegativeInfinity, "-inf", Day));

            Assert.Equal(60f, store.Get("player"));
            Assert.Equal(1, store.History("player").Count);
            Assert.Equal(3, warnings.Count);
            Assert.True(warnings[0].Contains("not a finite number"), warnings[0]);
        }

        [Test]
        public void Add_WithoutWarningSinkDoesNotThrow()
        {
            var store = new ReputationStore();

            Assert.False(store.Add("bogus", 1f, "x", Day));
            Assert.False(store.Add("player", float.NaN, "x", Day));
            Assert.True(store.Add("player", 1f, null, Day));
            Assert.Equal(string.Empty, store.History("player")[0].Reason, "a null reason becomes an empty string");
        }

        [Test]
        public void Add_ZeroDeltaStillWritesHistory()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            Assert.True(store.Add("player", 0f, "noted", Day));

            Assert.Equal(50f, store.Get("player"));
            Assert.Equal(1, store.History("player").Count);
        }

        // ---- Set ----

        [Test]
        public void Set_WritesEffectiveDelta()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 70f, "boost", Day);
            store.Set("player", 120f, "cap", Day);

            Assert.Equal(100f, store.Get("player"));
            IReadOnlyList<ReputationChange> history = store.History("player");
            Assert.Equal(20f, history[0].Delta);
            Assert.Equal(70f, history[0].Value);
            Assert.Equal(30f, history[1].Delta, "100 - 70, not the requested 120");
            Assert.Equal(100f, history[1].Value);
        }

        [Test]
        public void Set_InvalidInputChangesNothing()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            Assert.False(store.Set("nobody", 10f, "x", Day));
            Assert.False(store.Set("player", float.NaN, "x", Day));
            Assert.False(store.Set("player", float.PositiveInfinity, "x", Day));

            Assert.Equal(0, store.Count);
            Assert.Equal(3, warnings.Count);
        }

        // ---- events ----

        [Test]
        public void Changed_RaisedByAddAndSetWithOldAndNewValue()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            var calls = new List<string>();
            store.Changed += (key, oldValue, newValue, reason) =>
                calls.Add(key + "|" + oldValue + "|" + newValue + "|" + reason);

            store.Add("player", 10f, "review", Day);
            store.Set("ai:2", 30f, "reset", Day);

            Assert.Equal(new[] { "player|50|60|review", "ai:2|50|30|reset" }, calls.ToArray());
        }

        [Test]
        public void Changed_NotRaisedForRejectedCalls()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            int raised = 0;
            store.Changed += (key, oldValue, newValue, reason) => raised++;

            store.Add("bogus", 1f, "x", Day);
            store.Add("player", float.NaN, "x", Day);
            store.Set("player", float.NaN, "x", Day);

            Assert.Equal(0, raised);
        }

        [Test]
        public void Changed_StateIsUpdatedBeforeTheEvent()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            float seen = -1;
            int historySeen = -1;
            store.Changed += (key, oldValue, newValue, reason) =>
            {
                seen = store.Get(key);
                historySeen = store.History(key).Count;
            };

            store.Add("player", 4f, "x", Day);

            Assert.Equal(54f, seen);
            Assert.Equal(1, historySeen);
        }

        // ---- history limit ----

        [Test]
        public void History_KeepsNewest50()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            for (int i = 0; i < 60; i++) store.Add("player", 0.5f, "r" + i, Day.AddDays(i));

            IReadOnlyList<ReputationChange> history = store.History("player");

            Assert.Equal(50, history.Count);
            Assert.Equal(50, store.HistoryLimit);
            Assert.Equal("r10", history[0].Reason, "the 10 oldest are gone");
            Assert.Equal("r59", history[49].Reason);
            Assert.Equal(80f, store.Get("player"), "the value keeps all 60 steps");
        }

        [Test]
        public void History_LimitIsPerCompany()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings, 3);
            for (int i = 0; i < 5; i++)
            {
                store.Add("player", 1f, "p" + i, Day);
                store.Add("ai:1", 1f, "a" + i, Day);
            }

            Assert.Equal(new[] { "p2", "p3", "p4" }, Reasons(store.History("player")));
            Assert.Equal(new[] { "a2", "a3", "a4" }, Reasons(store.History("ai:1")));
        }

        [Test]
        public void History_ReturnsASnapshot()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Add("player", 1f, "first", Day);

            IReadOnlyList<ReputationChange> before = store.History("player");
            store.Add("player", 1f, "second", Day);

            Assert.Equal(1, before.Count);
            Assert.Equal(2, store.History("player").Count);
        }

        [Test]
        public void SetHistoryLimit_TrimsExistingHistoryAndClamps()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            for (int i = 0; i < 10; i++) store.Add("player", 1f, "r" + i, Day);

            store.SetHistoryLimit(4);

            Assert.Equal(4, store.HistoryLimit);
            Assert.Equal(new[] { "r6", "r7", "r8", "r9" }, Reasons(store.History("player")));

            store.SetHistoryLimit(0);
            Assert.Equal(1, store.HistoryLimit);
            store.SetHistoryLimit(1000000);
            Assert.Equal(1000, store.HistoryLimit);
            Assert.Equal(1, new ReputationStore(-5).HistoryLimit);
        }

        // ---- Drift (AC-13.2) ----

        [Test]
        public void Drift_EightyBecomesSeventyNineDotOne_AC_13_2()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("ai:1", 80f, "start", Day);
            int historyBefore = store.History("ai:1").Count;
            int changed = 0;
            int drifted = 0;
            store.Changed += (key, oldValue, newValue, reason) => changed++;
            store.Drifted += () => drifted++;

            bool moved = store.Drift("ai:1", 0.03f, 50f);

            Assert.True(moved);
            Assert.Near(79.1, store.Get("ai:1"), 1e-4);
            Assert.Equal(historyBefore, store.History("ai:1").Count, "drift writes no history");
            Assert.Equal(0, changed, "drift does not raise Changed");
            Assert.Equal(0, drifted, "a single Drift does not raise Drifted");
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void Drift_DefaultTargetIsFifty()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 20f, "start", Day);

            store.Drift("player", 0.5f);

            Assert.Near(35.0, store.Get("player"), 1e-5);
        }

        [Test]
        public void Drift_MovesUpAndTowardsAnotherTarget()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 20f, "start", Day);

            store.Drift("player", 0.03f, 50f);
            Assert.Near(20.9, store.Get("player"), 1e-4);

            store.Drift("ai:5", 0.1f, 70f);
            Assert.Near(52.0, store.Get("ai:5"), 1e-4, "an unknown company drifts from the default 50");
        }

        [Test]
        public void Drift_SnapsToTargetWhenCloserThanFiveHundredths()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            store.Set("player", 50.04f, "x", Day);
            Assert.True(store.Drift("player", 0.03f, 50f));
            Assert.Equal(50f, store.Get("player"));

            store.Set("ai:1", 49.96f, "x", Day);
            Assert.True(store.Drift("ai:1", 0.03f, 50f));
            Assert.Equal(50f, store.Get("ai:1"));

            // The deviation after the step decides: 50.05 -> 50.0485 is closer than 0.05.
            store.Set("ai:2", 50.05f, "x", Day);
            store.Drift("ai:2", 0.03f, 50f);
            Assert.Equal(50f, store.Get("ai:2"));

            // 50.1 -> 50.097 is not.
            store.Set("ai:3", 50.1f, "x", Day);
            store.Drift("ai:3", 0.03f, 50f);
            Assert.Near(50.097, store.Get("ai:3"), 1e-4);
        }

        [Test]
        public void Drift_ReachesTheTargetInFiniteTime()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 100f, "x", Day);

            int months = 0;
            while (store.Drift("player", 0.03f, 50f))
            {
                months++;
                Assert.True(months < 1000, "the drift must stop");
            }

            Assert.Equal(50f, store.Get("player"));
            Assert.True(months > 100 && months < 250, "months: " + months);
        }

        [Test]
        public void Drift_NoChangeReturnsFalse()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            Assert.False(store.Drift("player", 0.03f, 50f), "already at the target");
            Assert.False(store.Drift("player", 0f, 80f), "rate zero does not move");
            Assert.Equal(0, store.Count, "an untouched company is not stored");
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void Drift_BackToDefaultWithoutHistoryDropsTheEntry()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Load("ai:1", 50.03f, null);
            Assert.Equal(1, store.Count);

            store.Drift("ai:1", 0.03f, 50f);

            Assert.Equal(50f, store.Get("ai:1"));
            Assert.Equal(0, store.Count, "nothing worth saving is left");
        }

        [Test]
        public void Drift_KeepsEntryWithHistory()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("ai:1", 50.03f, "x", Day);

            store.Drift("ai:1", 0.03f, 50f);

            Assert.Equal(50f, store.Get("ai:1"));
            Assert.Equal(1, store.Count);
            Assert.Equal(1, store.History("ai:1").Count);
        }

        [Test]
        public void Drift_ClampsTheTarget()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 90f, "x", Day);

            store.Drift("player", 1f, 500f);

            Assert.Equal(100f, store.Get("player"));
        }

        [Test]
        public void Drift_BadArgumentsAreIgnoredWithWarning()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 80f, "x", Day);

            Assert.False(store.Drift("player", float.NaN, 50f));
            Assert.False(store.Drift("player", -0.1f, 50f));
            Assert.False(store.Drift("player", 1.5f, 50f));
            Assert.False(store.Drift("player", float.PositiveInfinity, 50f));
            Assert.False(store.Drift("player", 0.03f, float.NaN));
            Assert.False(store.Drift("player", 0.03f, float.NegativeInfinity));
            Assert.False(store.Drift("bogus", 0.03f, 50f));

            Assert.Equal(80f, store.Get("player"));
            Assert.Equal(7, warnings.Count);
        }

        // ---- DriftAll (AC-13.2, AC-13.3) ----

        [Test]
        public void DriftAll_RaisesDriftedOnceAndNotChanged_AC_13_2()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 80f, "setup", Day);
            store.Set("ai:0", 30f, "setup", Day);
            var keys = new List<string> { "player", "ai:0", "ai:1" };
            var calls = new List<string>();
            store.Changed += (key, oldValue, newValue, reason) => calls.Add("changed " + key);
            store.Drifted += () => calls.Add("drifted");

            int moved = store.DriftAll(keys, 0.03f, 50f);

            Assert.Equal(2, moved, "ai:1 sits at the target already");
            Assert.Near(79.1, store.Get("player"), 1e-4);
            Assert.Near(30.6, store.Get("ai:0"), 1e-4);
            Assert.Equal(50f, store.Get("ai:1"));
            Assert.Equal(new[] { "drifted" }, calls.ToArray(), "one Drifted, no Changed");
            Assert.Equal(1, store.History("player").Count, "history is unchanged");
            Assert.Equal(0, warnings.Count);
        }

        [Test]
        public void DriftAll_StateIsFinalWhenDriftedIsRaised()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 80f, "setup", Day);
            store.Set("ai:0", 20f, "setup", Day);
            float playerSeen = -1;
            float aiSeen = -1;
            store.Drifted += () =>
            {
                playerSeen = store.Get("player");
                aiSeen = store.Get("ai:0");
            };

            store.DriftAll(new[] { "player", "ai:0" }, 0.5f, 50f);

            Assert.Equal(65f, playerSeen);
            Assert.Equal(35f, aiSeen);
        }

        [Test]
        public void DriftAll_DriftedIsRaisedEvenWhenNothingMoved()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            int drifted = 0;
            store.Drifted += () => drifted++;

            Assert.Equal(0, store.DriftAll(new[] { "player", "ai:0" }, 0.03f, 50f));
            Assert.Equal(1, drifted);
            Assert.Equal(0, store.DriftAll(new string[0], 0.03f, 50f));
            Assert.Equal(2, drifted);
        }

        [Test]
        public void DriftAll_NullListOrBadArgumentsDoNothing()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 80f, "x", Day);
            int drifted = 0;
            store.Drifted += () => drifted++;

            Assert.Equal(0, store.DriftAll(null, 0.03f, 50f));
            Assert.Equal(0, store.DriftAll(new[] { "player" }, float.NaN, 50f));
            Assert.Equal(0, store.DriftAll(new[] { "player" }, 0.03f, float.PositiveInfinity));

            Assert.Equal(0, drifted);
            Assert.Equal(80f, store.Get("player"));
            Assert.Equal(2, warnings.Count, "bad arguments warn once per call, a null list is silent");
        }

        [Test]
        public void DriftAll_InvalidKeysAreSkippedWithWarning()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Set("player", 80f, "x", Day);

            int moved = store.DriftAll(new[] { "bogus", "player", null }, 0.5f, 50f);

            Assert.Equal(1, moved);
            Assert.Equal(65f, store.Get("player"));
            Assert.Equal(2, warnings.Count);
        }

        [Test]
        public void DriftAll_FortyEightMonthsOfFourteenCompaniesKeepSixAddEntries_AC_13_3()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            var keys = new List<string> { "player" };
            for (int i = 0; i < 13; i++) keys.Add(CompanyKey.ForAi(i));
            Assert.Equal(14, keys.Count);

            for (int i = 0; i < 6; i++) store.Add("player", i % 2 == 0 ? 6f : -2f, "significant" + i, Day.AddDays(i));
            for (int i = 0; i < 13; i++) store.Set(keys[i + 1], 20f + 5f * i, "start", Day);
            float afterAdds = store.Get("player");

            for (int month = 0; month < 48; month++) store.DriftAll(keys, 0.03f, 50f);

            IReadOnlyList<ReputationChange> history = store.History("player");
            Assert.Equal(6, history.Count, "no drift entry ever displaced an Add");
            for (int i = 0; i < 6; i++)
            {
                Assert.Equal("significant" + i, history[i].Reason);
                Assert.Equal(i % 2 == 0 ? 6f : -2f, history[i].Delta);
            }
            Assert.True(store.Get("player") != afterAdds, "the value itself did drift");
            for (int i = 0; i < 13; i++)
                Assert.Equal(1, store.History(keys[i + 1]).Count, keys[i + 1] + " keeps only its Set entry");
        }

        [Test]
        public void DriftAll_LongRunConvergesWithoutHistory()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            var keys = new List<string> { "player", "ai:0", "ai:1" };
            store.Load("player", 100f, null);
            store.Load("ai:0", 0f, null);

            for (int month = 0; month < 600; month++) store.DriftAll(keys, 0.03f, 50f);

            Assert.Equal(50f, store.Get("player"));
            Assert.Equal(50f, store.Get("ai:0"));
            Assert.Equal(0, store.Count, "everyone is back at the default and has no history");
        }

        // ---- Load / Keys / Clear ----

        [Test]
        public void Load_RestoresValueAndHistory()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            var history = new List<ReputationChange>
            {
                new ReputationChange(Day, 7.5f, 57.5f, "smp.test"),
                new ReputationChange(Day.AddDays(3), -1f, 56.5f, null),
            };
            int raised = 0;
            store.Changed += (key, oldValue, newValue, reason) => raised++;

            Assert.True(store.Load("player", 56.5f, history));

            Assert.Equal(56.5f, store.Get("player"));
            IReadOnlyList<ReputationChange> loaded = store.History("player");
            Assert.Equal(2, loaded.Count);
            Assert.Equal(Day, loaded[0].Date);
            Assert.Equal(7.5f, loaded[0].Delta);
            Assert.Equal("smp.test", loaded[0].Reason);
            Assert.Equal(string.Empty, loaded[1].Reason);
            Assert.Equal(0, raised, "loading raises nothing");
        }

        [Test]
        public void Load_ReplacesWhatWasThere()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Add("player", 10f, "old", Day);

            store.Load("player", 30f, new[] { new ReputationChange(Day, 1f, 30f, "new") });

            Assert.Equal(30f, store.Get("player"));
            Assert.Equal(new[] { "new" }, Reasons(store.History("player")));
        }

        [Test]
        public void Load_ClampsDropsBrokenEntriesAndTrims()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings, 3);
            var history = new List<ReputationChange>();
            for (int i = 0; i < 6; i++) history.Add(new ReputationChange(Day, 1f, 51f + i, "r" + i));
            history.Insert(2, new ReputationChange(Day, float.NaN, 50f, "nan-delta"));
            history.Insert(3, new ReputationChange(Day, 1f, float.PositiveInfinity, "inf-value"));
            history.Add(new ReputationChange(Day, 1f, 250f, "too-big"));

            Assert.True(store.Load("ai:4", 150f, history));

            Assert.Equal(100f, store.Get("ai:4"));
            IReadOnlyList<ReputationChange> loaded = store.History("ai:4");
            Assert.Equal(new[] { "r4", "r5", "too-big" }, Reasons(loaded), "the broken ones are gone, the newest three stay");
            Assert.Equal(100f, loaded[2].Value, "history values are clamped too");
            Assert.Equal(2, warnings.Count);
        }

        [Test]
        public void Load_RejectsBadKeyAndBadValue()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);

            Assert.False(store.Load("nobody", 60f, null));
            Assert.False(store.Load(null, 60f, null));
            Assert.False(store.Load("player", float.NaN, null));
            Assert.False(store.Load("player", float.NegativeInfinity, null));

            Assert.Equal(0, store.Count);
            Assert.Equal(4, warnings.Count);
        }

        [Test]
        public void Load_DefaultValueWithoutHistoryStoresNothing()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Add("player", 10f, "x", Day);

            Assert.True(store.Load("player", 50f, new ReputationChange[0]));

            Assert.Equal(0, store.Count);
            Assert.Equal(50f, store.Get("player"));
        }

        [Test]
        public void Keys_AreSortedAndClearForgetsAll()
        {
            List<string> warnings;
            ReputationStore store = NewStore(out warnings);
            store.Add("ai:10", 1f, "x", Day);
            store.Add("player", 1f, "x", Day);
            store.Add("ai:2", 1f, "x", Day);

            Assert.Equal(new[] { "ai:10", "ai:2", "player" }, store.Keys(), "ordinal order");

            store.Clear();

            Assert.Equal(0, store.Count);
            Assert.Equal(0, store.Keys().Count);
            Assert.Equal(50f, store.Get("player"));
            Assert.Equal(0, store.History("player").Count);
        }

        [Test]
        public void ReputationChange_NullReasonBecomesEmpty()
        {
            var change = new ReputationChange(Day, 1.5f, 51.5f, null);

            Assert.Equal(string.Empty, change.Reason);
            Assert.Equal(1.5f, change.Delta);
            Assert.Equal(51.5f, change.Value);
            Assert.Equal(Day, change.Date);
        }

        private static string[] Reasons(IReadOnlyList<ReputationChange> history)
        {
            var reasons = new string[history.Count];
            for (int i = 0; i < reasons.Length; i++) reasons[i] = history[i].Reason;
            return reasons;
        }
    }
}
