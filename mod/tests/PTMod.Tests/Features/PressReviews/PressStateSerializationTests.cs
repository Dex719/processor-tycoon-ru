using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.PressReviews;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// Saving, loading, switching off and migration of the section "press-reviews" (FR-12, NFR-3, TSK-018), driven through the real model of the side save file
    /// (<c>SideFileModel</c>) with the feature's own holder (<c>PressStateHolder</c>): the section round-trips without loss and without a version field of its own (the
    /// core writes <c>_schema</c>), the queue comes back with the same dates, a section newer than the mod is left to the core (defaults for the feature, the stored
    /// section written back unchanged), a feature that is switched off keeps its section as it was, the hype continues with its old end after the feature is back, and the
    /// provider of <c>popularity.gain</c> is registered once, in <c>Register</c>, never after a load. The migrations are checked on the mechanism (there is no real step yet).
    /// </summary>
    [Covers(typeof(PressStateHolder), typeof(PressStateMigrations))]
    public class PressStateSerializationTests
    {
        private const string Feature = "press-reviews";

        // ---- helpers ----

        private static PressState Sample()
        {
            var state = new PressState();
            ReviewQueue.Enqueue(state, 5120, "player", new DateTime(1995, 3, 1), 14);
            ReviewQueue.Enqueue(state, 5121, "ai:2", new DateTime(1995, 3, 2), 18);
            ReviewQueue.AddSales(state.pending[0], 1200, 4300, 0, 0);
            state.pending[1].attempts = 2;
            state.reviews.Add(new ReviewRecord
            {
                cpuSaveId = 4988, cpuName = "K6-300", companyKey = "ai:1", companyName = "AMD", releasedOn = "1995-01-10", publishedOn = "1995-01-24", segment = 1,
                axes = new[] { 73.2f, 65f, 55.9f, 62.5f }, metascore = 68, verdictKey = "press.verdict.average",
                outlets = new List<OutletLine> { new OutletLine { outletId = "kilobyte", score = 69, quoteKey = "press.quote.performance.high.2" } },
                peerSaveIds = new List<int> { 4410, 4502 }, notes = new List<string>(), reputationDelta = 1, hypeAtPublish = 1.04f,
            });
            state.reviews.Add(new ReviewRecord
            {
                cpuSaveId = 4989, cpuName = "P5-90", companyKey = "ai:0", companyName = "Intel", releasedOn = "1995-02-01", publishedOn = "1995-02-15", segment = 0,
                axes = new[] { 80f, 70f, 60f, 62f }, metascore = 90, verdictKey = "press.verdict.masterpiece", outlets = new List<OutletLine>(),
                peerSaveIds = new List<int>(), notes = new List<string>(), reputationDelta = 5, hypeAtPublish = 1.357f,
            });
            state.awards.Add(new YearAwards { year = 1994, cpuOfYear = new List<AwardLine> { new AwardLine { segment = 0, cpuSaveId = 4400, cpuName = "P5-60", companyKey = "ai:0", score = 86 } }, companyOfYearKey = "ai:0", companyOfYearAvg = 79.5f, disappointmentCpuSaveId = -1 });
            state.articles.Add(new ArticleRecord { date = "1995-02-01", source = "design-flaw-recalls", titleKey = "dfr.article.title", bodyKey = "dfr.article.body", args = new List<string> { "K6-300" } });
            state.noteCooldowns.Add(new NoteCooldown { noteId = "perf_bar_highend", until = "1996-01-01" });
            state.yearStats.Add(new SegmentYearStat { year = 1994, segment = 0, leaderIps = 150f, medianPrice = 549f });
            state.lastDigest = "1995-03";
            return state;
        }

        private static string Text(PressState state)
        {
            return ModJson.Write(PressStateCodec.Write(state), false);
        }

        private static JsonObject Parse(string text)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "not valid JSON: " + error);
            return (JsonObject)node;
        }

        private static string FileWith(string features)
        {
            return "{\"schema\":1,\"modVersion\":\"0.1.0\",\"gameVersion\":\"0.2.3\",\"saveName\":\"1995_3_1\",\"gameDate\":\"1995-03-01\",\"features\":{" + features + "}}";
        }

        private sealed class Session
        {
            public readonly SideFileModel Model;
            public readonly PressStateHolder Holder;
            public readonly List<string> Log = new List<string>();

            public Session(Func<string, bool> isActive = null)
            {
                Model = new SideFileModel(isActive);
                Holder = new PressStateHolder(Log.Add);
                string problem;
                Assert.True(Model.TryRegister(Holder, out problem), problem);
            }

            /// <summary>The text of the side file the game would write now.</summary>
            public string Save()
            {
                SideFileBuildReport build = Model.Build(null);
                Assert.Equal(0, build.Warnings.Count, "a clean save: " + string.Join("; ", build.Warnings));
                return ModJson.Write(build.Document, false);
            }

            public JsonObject SectionOfSave()
            {
                return Parse(Save()).GetObject("features").GetObject(Feature);
            }
        }

        private static Session Reload(string savedText, Func<string, bool> isActive = null)
        {
            var session = new Session(isActive);
            SideFileReadReport report = session.Model.Apply(savedText);
            Assert.Equal(SideFileStatus.Loaded, report.Status, string.Join("; ", report.Warnings));
            return session;
        }

        // ---- the section as the core stores it ----

        [Test]
        public void TheHolder_IsTheSectionOfTheFeature_AtVersionOne()
        {
            var holder = new PressStateHolder(null);
            Assert.Equal("press-reviews", holder.SectionId);
            Assert.Equal(Feature, PressStateHolder.Id);
            Assert.Equal(1, holder.SchemaVersion);
            Assert.Equal(PressStateCodec.SectionVersion, holder.SchemaVersion);
            Assert.True(holder is ISideFileParticipant);
            Assert.NotNull(holder.State);
        }

        [Test]
        public void TheCoreWritesTheVersionOfTheSection_TheDataOfTheFeatureHasNone()
        {
            var session = new Session();
            session.Model.ApplyMissing();

            JsonObject section = session.SectionOfSave();
            Assert.Equal(1, section.GetInt32("_schema"), "the core adds _schema");

            JsonObject data = session.Holder.Write();
            Assert.False(data.ContainsKey("_schema") || data.ContainsKey("schema") || data.ContainsKey("version"), "the feature's own data has no version field");
        }

        [Test]
        public void ARoundTrip_ThroughTheModelOfTheSideFile_LosesNothing()
        {
            var first = new Session();
            first.Model.ApplyMissing();
            PressState original = Sample();
            SetState(first.Holder, original);
            string saved = first.Save();

            Session second = Reload(saved);
            Assert.Equal(0, second.Log.Count, "a clean section reads without a word: " + string.Join("; ", second.Log));
            Assert.Equal(Text(original), Text(second.Holder.State), "the state after the load is the state before the save");
            Assert.False(ReferenceEquals(original, second.Holder.State));
            Assert.Equal(saved, second.Save(), "and saving it again changes nothing");
        }

        private static void SetState(PressStateHolder holder, PressState state)
        {
            // the holder is given a state the way a load does: through Read of the written data
            holder.Read(PressStateCodec.Write(state), PressStateCodec.SectionVersion);
        }

        // ---- the queue ----

        [Test]
        public void TheQueue_SurvivesASaveAndALoad_AndPublishesOnTheSameDates()
        {
            // AC-12.1: a queue of 2 reviews, a save and a load: both are published on their dates
            var first = new Session();
            first.Model.ApplyMissing();
            var queued = new PressState();
            ReviewQueue.Enqueue(queued, 5120, "player", new DateTime(1995, 3, 1), 14);   // due 15 March
            ReviewQueue.Enqueue(queued, 5121, "ai:2", new DateTime(1995, 3, 2), 18);     // due 20 March
            ReviewQueue.AddSales(queued.pending[0], 10, 20, 30, 40);
            queued.pending[0].attempts = 1;
            SetState(first.Holder, queued);

            Session second = Reload(first.Save());
            PressState state = second.Holder.State;

            Assert.Equal(2, state.pending.Count);
            Assert.Equal("1995-03-15", state.pending[0].publishOn);
            Assert.Equal("1995-03-20", state.pending[1].publishOn);
            Assert.Equal("1995-03-01", state.pending[0].releasedOn);
            Assert.Equal(new[] { 10, 20, 30, 40 }, state.pending[0].soldBySegment, "what the CPU sold while it waited");
            Assert.Equal(1, state.pending[0].attempts);

            Assert.Equal(0, ReviewQueue.DueOn(state, new DateTime(1995, 3, 14)).Count, "nothing is due before the date");
            List<PendingReview> on15 = ReviewQueue.DueOn(state, new DateTime(1995, 3, 15));
            Assert.Equal(new List<int> { 5120 }, on15.ConvertAll(p => p.cpuSaveId), "the first on its day");
            Assert.Equal(new List<int> { 5120, 5121 }, ReviewQueue.DueOn(state, new DateTime(1995, 3, 20)).ConvertAll(p => p.cpuSaveId), "both on the second day");
        }

        [Test]
        public void ThePublishedReviews_AndTheirHype_ComeBackAfterALoad()
        {
            // the provider "reads the restored reviews after a load": the hype is made again from the archive and equals the one before the save
            HypeConfig hype = PressContentFixture.Config().Hype;
            PressState state = Sample();
            DateTime today = new DateTime(1995, 3, 1);

            var before = new HypeBook();
            before.Rebuild(state.reviews, today, hype);

            var first = new Session();
            first.Model.ApplyMissing();
            SetState(first.Holder, state);
            var after = new HypeBook();
            after.Rebuild(Reload(first.Save()).Holder.State.reviews, today, hype);

            Assert.Equal(before.Count, after.Count);
            Assert.True(after.Count >= 2, "both reviews of the sample are within their year");
            foreach (int id in new[] { 4988, 4989 })
            {
                float a, b;
                int ma, mb;
                Assert.True(before.TryGet(id, out a, out ma) && after.TryGet(id, out b, out mb), "CPU " + id);
                after.TryGet(id, out b, out mb);
                Assert.Equal(a, b, "the same hype for CPU " + id);
                Assert.Equal(ma, mb);
            }
        }

        // ---- a missing section, a section without a version, a section from the future ----

        [Test]
        public void ASaveWithoutTheSection_StartsWithAnEmptyArchive()
        {
            Session session = Reload(FileWith("\"other-feature\":{\"_schema\":1}"));

            Assert.Equal(0, session.Holder.State.pending.Count);
            Assert.Equal(0, session.Holder.State.reviews.Count);
            Assert.Null(session.Holder.State.lastDigest);
            Assert.Equal(0, session.Log.Count);

            var missing = new Session();
            Assert.Equal(SideFileStatus.Missing, missing.Model.ApplyMissing().Status, "a save from before the mod");
            Assert.Equal(0, missing.Holder.State.reviews.Count);
        }

        [Test]
        public void ASectionWithoutAVersion_IsReadAsVersionOne()
        {
            var session = new Session();
            SideFileReadReport report = session.Model.Apply(FileWith("\"press-reviews\":{\"lastDigest\":\"1995-02\",\"pending\":[{\"cpuSaveId\":9,\"companyKey\":\"player\","
                + "\"releasedOn\":\"1995-03-01\",\"publishOn\":\"1995-03-15\"}]}"));

            Assert.Equal(SideFileStatus.Loaded, report.Status);
            Assert.Equal("1995-02", session.Holder.State.lastDigest);
            Assert.Equal(1, session.Holder.State.pending.Count);
            Assert.Equal(1, session.SectionOfSave().GetInt32("_schema"), "the field appears on the next save");
        }

        [Test]
        public void ASectionNewerThanTheMod_GivesTheFeatureItsDefaults_AndIsWrittenBackAsItWas()
        {
            const string stored = "{\"_schema\":2,\"pending\":[],\"reviews\":[{\"cpuSaveId\":1,\"metascore\":77,\"something\":\"from the future\"}],\"newThing\":{\"a\":[1,2,3]}}";
            var session = new Session();
            SetState(session.Holder, Sample());   // whatever the feature held before the load is replaced

            SideFileReadReport report = session.Model.Apply(FileWith("\"press-reviews\":" + stored));

            Assert.Equal(SideFileStatus.Newer, report.Status);
            Assert.Equal(1, report.NewerSections.Count);
            Assert.Equal(Feature, report.NewerSections[0]);
            Assert.Equal(0, session.Holder.State.reviews.Count, "defaults: the feature does not guess at a format it does not know");
            Assert.Equal(0, session.Holder.State.pending.Count);
            Assert.Null(session.Holder.State.lastDigest);

            JsonObject written = session.SectionOfSave();
            Assert.True(Parse(stored).Equals(written), "the stored section is kept as it was, also the part this mod does not understand");
        }

        [Test]
        public void ASectionThatIsNotAnObject_GivesDefaults_AndStaysInTheFile()
        {
            var session = new Session();
            SideFileReadReport report = session.Model.Apply(FileWith("\"press-reviews\":[1,2,3]"));

            Assert.True(report.Warnings.Count > 0);
            Assert.Equal(0, session.Holder.State.reviews.Count);
        }

        // ---- limits and clips on read ----

        [Test]
        public void AHolder_ClipsAndLimits_WhatItReads_AndSaysSo()
        {
            var state = new PressState();
            for (int i = 0; i < PressStateCodec.MaxReviews + 10; i++)
                state.reviews.Add(new ReviewRecord { cpuSaveId = i, segment = 1, axes = new[] { 1f, 2f, 3f, 4f }, metascore = i == 0 ? 150 : 50, publishedOn = "1995-01-01", releasedOn = "1995-01-01" });
            state.pending.Add(new PendingReview { cpuSaveId = 1, companyKey = "player", releasedOn = "1995-03-01", publishOn = "soon" });
            string text = FileWith("\"press-reviews\":" + Text(state));

            var session = new Session();
            session.Model.Apply(text);

            Assert.Equal(PressStateCodec.MaxReviews, session.Holder.State.reviews.Count, "the limit of the archive");
            Assert.Equal(10, session.Holder.State.reviews[0].cpuSaveId, "the oldest go first");
            Assert.Equal(0, session.Holder.State.pending.Count, "a queued review with a bad date is dropped");
            Assert.True(session.Log.Count >= 2, "each repair is reported: " + string.Join("; ", session.Log));
            Assert.True(session.Log.TrueForAll(line => line.StartsWith("save: ", StringComparison.Ordinal)), "and reaches the log with its prefix");

            var clipped = new PressState();
            clipped.reviews.Add(new ReviewRecord { cpuSaveId = 1, segment = 1, axes = new[] { 1f, 2f, 3f, 4f }, metascore = 150 });
            var second = new Session();
            second.Model.Apply(FileWith("\"press-reviews\":" + Text(clipped)));
            Assert.Equal(100, second.Holder.State.reviews[0].metascore, "a metascore is held to 0..100");
        }

        [Test]
        public void AHolderWithoutALog_StillReads()
        {
            var holder = new PressStateHolder(null);
            holder.Read(Parse("{\"reviews\":[{\"cpuSaveId\":1,\"segment\":9,\"metascore\":50}]}"), 1);
            Assert.True(holder.State.reviews[0].compact);
        }

        // ---- switching the feature off ----

        [Test]
        public void ASectionOfAFeatureThatIsSwitchedOff_IsKeptAsItWasSaved()
        {
            // OnDisabled changes nothing of the state, and the core does not write a feature that is off: the section stays exactly as the last save left it
            bool active = true;
            var session = new Session(id => active);
            session.Model.ApplyMissing();
            SetState(session.Holder, Sample());
            JsonObject lastSaved = session.SectionOfSave();

            active = false;
            session.Holder.State.lastDigest = "1999-12";   // were it written, the section would change
            session.Holder.State.pending.Clear();
            JsonObject whileOff = session.SectionOfSave();

            Assert.True(lastSaved.Equals(whileOff), "the section is written as it was while the feature is off");
            Assert.Equal(2, whileOff.GetArray("pending").Count, "the queue is still there");
            Assert.Equal("1995-03", whileOff.GetString("lastDigest"));

            // back on: the feature's live state is what it left (OnDisabled and OnEnabled do not touch it)
            session.Holder.State.lastDigest = "1995-03";
            SetState(session.Holder, Sample());
            active = true;
            Assert.True(lastSaved.Equals(session.SectionOfSave()), "and the next save is the section it was");
            Assert.Equal(Text(Sample()), Text(Reload(ModJson.Write(session.Model.Build(null).Document, false)).Holder.State), "everything is there when the feature is back");
        }

        [Test]
        public void TheQueueIsFrozenWhileTheFeatureIsOff_AndWaitsForItsTurn()
        {
            // nothing runs while the feature is off (the tick handlers are skipped by the core): the due reviews are still in the queue in their order
            var state = new PressState();
            ReviewQueue.Enqueue(state, 1, "player", new DateTime(1995, 1, 1), 10);
            ReviewQueue.Enqueue(state, 2, "ai:1", new DateTime(1995, 1, 2), 10);
            ReviewQueue.Enqueue(state, 3, "ai:2", new DateTime(1995, 1, 20), 10);

            List<PendingReview> backOn = ReviewQueue.DueOn(state, new DateTime(1995, 6, 1));
            Assert.Equal(new List<int> { 1, 2, 3 }, backOn.ConvertAll(p => p.cpuSaveId), "all three are due on the first tick after the feature is back, in the order they were queued");
            Assert.Equal(3, state.pending.Count, "DueOn only looks");
        }

        [Test]
        public void TheHype_ContinuesAfterTheFeatureIsBack_WithTheOldEnd()
        {
            HypeConfig cfg = PressContentFixture.Config().Hype;
            DateTime published = new DateTime(1995, 3, 15);
            Assert.Equal(365, cfg.DurationDays);

            // the feature runs without a break
            var continuous = new HypeBook();
            continuous.Add(77, 90, published, published, cfg);

            // the same feature is off from July to November and comes back: the book is made again from the archive (ReviewEffects.Reset), the days are by date
            var archive = new List<ReviewRecord> { new ReviewRecord { cpuSaveId = 77, metascore = 90, publishedOn = PressDates.ToText(published) } };
            var returning = new HypeBook();
            DateTime back = new DateTime(1995, 11, 20);
            returning.Rebuild(archive, back, cfg);
            continuous.Refresh(back, cfg);

            float a, b;
            int ma, mb;
            Assert.True(continuous.TryGet(77, out a, out ma) && returning.TryGet(77, out b, out mb));
            returning.TryGet(77, out b, out mb);
            Assert.Equal(a, b, "the hype on the day the feature is back is what it would have been");
            Assert.True(a > 1f && a < 1.357f, "it has faded since the review: " + a);

            // and it ends on the same day: 365 days after the review
            DateTime lastDay = published.AddDays(364);
            DateTime endDay = published.AddDays(365);
            continuous.Refresh(lastDay, cfg);
            returning.Rebuild(archive, lastDay, cfg);
            Assert.True(continuous.Contains(77) && returning.Contains(77), "still there on day 364");
            continuous.Refresh(endDay, cfg);
            returning.Rebuild(archive, endDay, cfg);
            Assert.False(continuous.Contains(77) || returning.Contains(77), "gone on day 365 in both");
        }

        // ---- the migrations ----

        private static JsonObject WithMember(JsonObject section, string name, int value)
        {
            section.Set(name, value);
            return section;
        }

        [Test]
        public void MigrationsCoverEveryVersionOfTheSection()
        {
            // a step for each version below the current one: raising SectionVersion without writing the step turns this red
            Assert.Equal(PressStateCodec.SectionVersion - 1, PressStateMigrations.Steps.Count, "SectionVersion " + PressStateCodec.SectionVersion + " needs " + (PressStateCodec.SectionVersion - 1) + " step(s)");
        }

        [Test]
        public void Upgrade_AnUpToDateSection_IsReturnedAsItIs()
        {
            JsonObject section = Parse("{\"lastDigest\":\"1995-02\"}");
            var problems = new List<string>();

            Assert.True(ReferenceEquals(section, PressStateMigrations.Upgrade(section, 1, 1, problems)));
            Assert.Equal(0, problems.Count);
            Assert.Null(PressStateMigrations.Upgrade(null, 1, 3, problems), "no section, nothing to migrate");
        }

        [Test]
        public void Upgrade_RunsTheStepsInOrder_OnACopy()
        {
            var steps = new List<PressStateMigrations.Step>
            {
                s => WithMember(s, "step1", 1),                                       // 1 -> 2
                s => WithMember(s, "step2", s.ContainsKey("step1") ? 2 : -1),         // 2 -> 3 (sees the result of the first)
            };
            JsonObject stored = Parse("{\"keep\":7}");
            string before = ModJson.Write(stored, false);
            var problems = new List<string>();

            JsonObject result = PressStateMigrations.Upgrade(stored, 1, 3, steps, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal(7, result.GetInt32("keep"));
            Assert.Equal(1, result.GetInt32("step1"));
            Assert.Equal(2, result.GetInt32("step2"), "the second step got the output of the first");
            Assert.Equal(before, ModJson.Write(stored, false), "the section the core holds is not modified (the contract of Read)");
            Assert.False(ReferenceEquals(stored, result));
        }

        [Test]
        public void Upgrade_StartsFromTheStoredVersion()
        {
            var steps = new List<PressStateMigrations.Step> { s => WithMember(s, "step1", 1), s => WithMember(s, "step2", 1) };

            JsonObject result = PressStateMigrations.Upgrade(Parse("{}"), 2, 3, steps, null);
            Assert.False(result.ContainsKey("step1"), "version 2 does not go through the step 1 -> 2");
            Assert.True(result.ContainsKey("step2"));
        }

        [Test]
        public void Upgrade_AStepThatReturnsNull_KeepsTheData()
        {
            var steps = new List<PressStateMigrations.Step> { s => { s.Set("touched", 1); return null; } };
            JsonObject result = PressStateMigrations.Upgrade(Parse("{\"a\":1}"), 1, 2, steps, null);
            Assert.Equal(1, result.GetInt32("a"));
            Assert.Equal(1, result.GetInt32("touched"));
        }

        [Test]
        public void Upgrade_AStepThatThrows_StopsTheChain_AndTheRestIsReadAsFarAsItCan()
        {
            var steps = new List<PressStateMigrations.Step>
            {
                s => WithMember(s, "step1", 1),
                s => { throw new InvalidOperationException("boom"); },
                s => WithMember(s, "step3", 1),
            };
            var problems = new List<string>();

            JsonObject result = PressStateMigrations.Upgrade(Parse("{}"), 1, 4, steps, problems);

            Assert.True(result.ContainsKey("step1"), "what was migrated before the failure is kept");
            Assert.False(result.ContainsKey("step3"), "the chain stops");
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("boom") && problems[0].Contains("2 to 3"), problems[0]);
        }

        [Test]
        public void Upgrade_AMissingStep_StopsTheChain_AndIsReported()
        {
            var problems = new List<string>();
            JsonObject result = PressStateMigrations.Upgrade(Parse("{\"a\":1}"), 1, 3, new List<PressStateMigrations.Step>(), problems);

            Assert.Equal(1, result.GetInt32("a"));
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("no step from version 1 to 2"), problems[0]);

            var nullStep = new List<PressStateMigrations.Step> { null };
            problems.Clear();
            PressStateMigrations.Upgrade(Parse("{}"), 1, 2, nullStep, problems);
            Assert.Equal(1, problems.Count, "a null in the table is a missing step");
        }

        [Test]
        public void Upgrade_AVersionBelowOne_IsReadAsOne_AndANewerOneAsItIs()
        {
            var steps = new List<PressStateMigrations.Step> { s => WithMember(s, "step1", 1) };
            var problems = new List<string>();

            JsonObject low = PressStateMigrations.Upgrade(Parse("{}"), 0, 2, steps, problems);
            Assert.True(low.ContainsKey("step1"), "version 0 is version 1");
            Assert.Equal(1, problems.Count);

            problems.Clear();
            JsonObject section = Parse("{\"x\":1}");
            Assert.True(ReferenceEquals(section, PressStateMigrations.Upgrade(section, 5, 2, steps, problems)), "a section from the future is not touched");
            Assert.Equal(1, problems.Count);
        }

        [Test]
        public void TheCodec_MigratesBeforeItReads_AndNeverFailsOnAStrangeVersion()
        {
            JsonObject section = Parse("{\"lastDigest\":\"1995-02\"}");

            var problems = new List<string>();
            PressState zero = PressStateCodec.Read(section, 0, problems);
            Assert.Equal("1995-02", zero.lastDigest, "version 0 is read as version 1");
            Assert.True(problems.Count > 0);

            problems.Clear();
            PressState future = PressStateCodec.Read(section, 7, problems);
            Assert.Equal("1995-02", future.lastDigest, "the core never gives a newer section to Read, but the codec survives one");
            Assert.True(problems.Count > 0);

            problems.Clear();
            Assert.Equal("1995-02", PressStateCodec.Read(section, PressStateCodec.SectionVersion, problems).lastDigest);
            Assert.Equal(0, problems.Count);
        }

        // ---- the provider, by the source ----

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews");
        }

        private static IEnumerable<KeyValuePair<string, string>> AllCode()
        {
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
                yield return new KeyValuePair<string, string>(SourceScan.Relative(FeatureDir(), path), SourceScan.Strip(SourceScan.ReadText(path), false));
        }

        [Test]
        public void TheProvider_IsRegisteredOnceInRegister_AndNotAfterALoad()
        {
            var places = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode())
            {
                foreach (Match match in Regex.Matches(file.Value, @"\bModModifiers\s*\.\s*Register(?:Reasoned)?Provider\b"))
                    places.Add(file.Key + ":" + SourceScan.LineOf(file.Value, match.Index));
            }
            Assert.Equal(1, places.Count, "one registration in the whole feature: " + string.Join(", ", places));
            Assert.True(places[0].StartsWith("PressReviewsFeature.cs", StringComparison.Ordinal), "and it is in the entry point: " + places[0]);

            // the load goes through the holder and the adapter, which register nothing and subscribe to nothing
            foreach (string name in new[] { "Logic/PressStateHolder.cs", "State/PressSaveAdapter.cs" })
            {
                string text = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), name.Replace('/', Path.DirectorySeparatorChar))), false);
                Assert.False(Regex.IsMatch(text, @"ModModifiers|ModSave\s*\.\s*AfterRead|\+="), name + " neither registers nor subscribes");
            }
        }

        [Test]
        public void TheAdapter_PassesEveryCallOnToTheHolder_UnderTheIdOfTheFeature()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "PressReviewsFeature.cs")), false);
            Match id = Regex.Match(feature, @"const string FeatureId = ""([^""]+)""");
            Assert.True(id.Success);
            Assert.Equal(PressStateHolder.Id, id.Groups[1].Value, "the section id is the slug of the feature (the core refuses another)");

            string adapter = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "State", "PressSaveAdapter.cs")), false);
            Assert.True(Regex.IsMatch(adapter, @"class PressSaveAdapter\s*:\s*IModSaveParticipant"));
            foreach (string call in new[] { "holder.ResetToDefaults()", "holder.Read(section, schema)", "holder.Write()", "holder.SectionId", "holder.SchemaVersion", "holder.State" })
                Assert.True(adapter.Contains(call), "the adapter passes on: " + call);
            Assert.True(feature.Contains("ctx.SaveParticipant(PressSaveAdapter.Instance)"), "and it is the participant the feature registers");
        }

        [Test]
        public void NothingInTheFeature_ReactsToAfterRead_ByRegisteringAProvider()
        {
            // the hype is rebuilt by ReviewEffects.EnsureFresh when the state object changes; no handler of ModSave.AfterRead is needed (and none may register a provider)
            foreach (KeyValuePair<string, string> file in AllCode())
            {
                foreach (Match match in Regex.Matches(file.Value, @"ModSave\s*\.\s*AfterRead\s*\+="))
                {
                    string rest = file.Value.Substring(match.Index, Math.Min(400, file.Value.Length - match.Index));
                    Assert.False(rest.Contains("Provider"), file.Key + ": a handler of AfterRead must not register a provider");
                }
            }

            string effects = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "Effects", "ReviewEffects.cs")), false);
            Assert.True(Regex.IsMatch(effects, @"ReferenceEquals\s*\(\s*state\s*,\s*builtFor\s*\)"), "the effects notice a new state object (a load or a new game) and build the hype again");
        }

        [Test]
        public void SwitchingTheFeature_DoesNotTouchTheProvider_OrTheState()
        {
            string feature = SourceScan.Strip(SourceScan.ReadText(Path.Combine(FeatureDir(), "PressReviewsFeature.cs")), true);
            int start = feature.IndexOf("public void OnEnabled()", StringComparison.Ordinal);
            Assert.True(start > 0);
            string tail = feature.Substring(start, feature.IndexOf("private static string LevelKeyOf", start, StringComparison.Ordinal) - start);

            Assert.False(tail.Contains("ModModifiers"), "OnEnabled and OnDisabled leave the provider alone: it is neither removed nor made again");
            Assert.False(Regex.IsMatch(tail, @"ReviewQueue|\.pending\b|\.reviews\b|ResetToDefaults|CurrentState\s*\("), "and they leave the queue and the archive alone: the state stays as it is");
        }
    }
}
