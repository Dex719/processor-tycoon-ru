using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Rng;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-016 (FR-11 items 3-5; AC-11.1): the flaws of the AI companies. A public flaw is answered at once and paid by the company, a <c>Major</c> or <c>Critical</c> incident is a
    /// letter (<c>Normal</c>, tag <c>dfr:news:&lt;recordId&gt;</c>), a <c>Minor</c> one a notification, every public incident goes to the event journal, an internal flaw handled
    /// quietly is not told to anybody before a leak, the fast-forward of a new game makes no news, and a failed answer is tried again by the monthly pass.
    /// </summary>
    [Covers(typeof(FlawAiEngine), typeof(IFlawAiHost), typeof(FlawNewsPlanner), typeof(FlawNewsPlan), typeof(FlawNewsKind))]
    public class FlawAiNewsTests
    {
        private static readonly int Day = FlawCycleKit.Day1990 + 100;

        private static readonly float[] OnlyRecall = { 1f, 0f, 0f, 0f };
        private static readonly float[] OnlyStepping = { 0f, 1f, 0f, 0f };
        private static readonly float[] OnlyPatch = { 0f, 0f, 1f, 0f };
        private static readonly float[] OnlyIgnore = { 0f, 0f, 0f, 1f };

        // ---- the plan: whom the player is told about ----

        [Test]
        public void Plan_MajorAndCritical_PublicFlaw_IsALetterWithTheTag()
        {
            foreach (FlawSeverity severity in new[] { FlawSeverity.Major, FlawSeverity.Critical })
            {
                foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
                {
                    FlawRecord record = AiWorld.Ai(FlawState.Public, 7, severity);
                    FlawNewsPlan plan = FlawNewsPlanner.Plan(record, response, FlawState.Public, false);

                    string what = severity + "/" + response;
                    Assert.Equal(FlawNewsKind.Mail, plan.Kind, what);
                    Assert.Equal("dfr:news:7", plan.MailTag, what);
                    Assert.Equal("dfr.news.title", plan.TitleKey, what);
                    Assert.Equal("dfr.news.body", plan.BodyKey, what);
                    Assert.True(plan.Journal, what + ": the journal gets a line");
                    Assert.True(plan.TellsPlayer && !plan.IsEmpty, what);
                    Assert.Null(plan.NotifyKey, what);
                }
            }
        }

        [Test]
        public void Plan_Minor_PublicFlaw_IsANotification_NotALetter()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public, 3, FlawSeverity.Minor);

            FlawNewsPlan plan = FlawNewsPlanner.Plan(record, FlawResponse.Patch, FlawState.Public, false);

            Assert.Equal(FlawNewsKind.Notify, plan.Kind);
            Assert.Equal("dfr.news.rival.title", plan.NotifyKey);
            Assert.Equal("dfr.news.rival.body", plan.NotifyBodyKey, "the second line of the notification (design.md, the keys of the texts)");
            Assert.Null(plan.MailTag, "no letter, no tag");
            Assert.Null(plan.TitleKey);
            Assert.Null(plan.BodyKey);
            Assert.True(plan.Journal, "a public incident is written to the journal whatever its severity");
        }

        [Test]
        public void Plan_InternalFlaw_QuietAnswers_AreNotNews_NoRumours()
        {
            // FR-11 item 5: the company found it inside and fixed it quietly (a revision, a patch) or did nothing: the player hears nothing until a leak.
            foreach (FlawSeverity severity in new[] { FlawSeverity.Minor, FlawSeverity.Major, FlawSeverity.Critical })
            {
                foreach (FlawResponse response in new[] { FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
                {
                    FlawNewsPlan plan = FlawNewsPlanner.Plan(AiWorld.Ai(FlawState.InternalKnown, 1, severity), response, FlawState.InternalKnown, false);

                    Assert.True(plan.IsEmpty, severity + "/" + response + ": nothing is said");
                    Assert.Equal(FlawNewsKind.None, plan.Kind);
                    Assert.False(plan.Journal, "and nothing is written");
                    Assert.False(plan.TellsPlayer);
                }
            }
        }

        [Test]
        public void Plan_InternalFlaw_Recall_IsNews_BecauseARecallIsDoneInTheOpen()
        {
            FlawNewsPlan major = FlawNewsPlanner.Plan(AiWorld.Ai(FlawState.InternalKnown, 4, FlawSeverity.Major), FlawResponse.Recall, FlawState.InternalKnown, false);
            Assert.Equal(FlawNewsKind.Mail, major.Kind);
            Assert.Equal("dfr:news:4", major.MailTag);
            Assert.True(major.Journal);

            FlawNewsPlan minor = FlawNewsPlanner.Plan(AiWorld.Ai(FlawState.InternalKnown, 5, FlawSeverity.Minor), FlawResponse.Recall, FlawState.InternalKnown, false);
            Assert.Equal(FlawNewsKind.Notify, minor.Kind);
        }

        [Test]
        public void Plan_FastForward_IsNothing()
        {
            FlawNewsPlan plan = FlawNewsPlanner.Plan(AiWorld.Ai(FlawState.Public, 1, FlawSeverity.Critical), FlawResponse.Recall, FlawState.Public, true);

            Assert.True(plan.IsEmpty, "no news and no journal during the fast-forward of a new game");
        }

        [Test]
        public void Plan_PlayersFlaws_NullRecords_AndNoResponse_AreNothing()
        {
            FlawRecord mine = ResponseWorld.Known(FlawState.Public, 1, FlawSeverity.Critical);

            Assert.True(FlawNewsPlanner.Plan(mine, FlawResponse.Recall, FlawState.Public, false).IsEmpty, "the player has the windows of the event system, not the news");
            Assert.True(FlawNewsPlanner.Plan(null, FlawResponse.Recall, FlawState.Public, false).IsEmpty);
            Assert.True(FlawNewsPlanner.Plan(AiWorld.Ai(FlawState.Public), FlawResponse.None, FlawState.Public, false).IsEmpty);
            Assert.True(FlawNewsPlan.Nothing.IsEmpty);
        }

        [Test]
        public void Plan_ALeakedCoverUp_UsesTheLeakedTexts()
        {
            FlawRecord leaked = AiWorld.Ai(FlawState.Public, 9, FlawSeverity.Major, coverUp: true);
            leaked.Source = DiscoverySource.Leak;

            FlawNewsPlan plan = FlawNewsPlanner.Plan(leaked, FlawResponse.Stepping, FlawState.Public, false);

            Assert.Equal(FlawNewsKind.Mail, plan.Kind);
            Assert.Equal("dfr.news.leaked.title", plan.TitleKey);
            Assert.Equal("dfr.news.leaked.body", plan.BodyKey);
            Assert.Equal("dfr:news:9", plan.MailTag);

            FlawRecord coverUpOnly = AiWorld.Ai(FlawState.Public, 10, FlawSeverity.Major, coverUp: true);
            Assert.Equal("dfr.news.title", FlawNewsPlanner.Plan(coverUpOnly, FlawResponse.Stepping, FlawState.Public, false).TitleKey, "only a leak makes the leaked texts");
        }

        [Test]
        public void ReactionKeys_NameTheFourResponses()
        {
            Assert.Equal("dfr.news.reaction.recall", FlawNewsPlanner.ReactionKeyOf(FlawResponse.Recall));
            Assert.Equal("dfr.news.reaction.stepping", FlawNewsPlanner.ReactionKeyOf(FlawResponse.Stepping));
            Assert.Equal("dfr.news.reaction.patch", FlawNewsPlanner.ReactionKeyOf(FlawResponse.Patch));
            Assert.Equal("dfr.news.reaction.ignore", FlawNewsPlanner.ReactionKeyOf(FlawResponse.Ignore));
            Assert.Null(FlawNewsPlanner.ReactionKeyOf(FlawResponse.None));

            FlawNewsPlan plan = FlawNewsPlanner.Plan(AiWorld.Ai(FlawState.Public), FlawResponse.Patch, FlawState.Public, false);
            Assert.Equal("dfr.news.reaction.patch", plan.ReactionKey);
        }

        [Test]
        public void IsPublicIncident_IsAPublicFlaw_OrARecall()
        {
            Assert.True(FlawNewsPlanner.IsPublicIncident(FlawState.Public, FlawResponse.Ignore));
            Assert.True(FlawNewsPlanner.IsPublicIncident(FlawState.Public, FlawResponse.Stepping));
            Assert.True(FlawNewsPlanner.IsPublicIncident(FlawState.InternalKnown, FlawResponse.Recall));
            Assert.False(FlawNewsPlanner.IsPublicIncident(FlawState.InternalKnown, FlawResponse.Patch));
            Assert.False(FlawNewsPlanner.IsPublicIncident(FlawState.Hidden, FlawResponse.Recall), "a hidden flaw is nobody's business");
        }

        // ---- the engine: the answer is applied at once ----

        [Test]
        public void PublicFlaw_IsAnsweredAtOnce_AndTheCompanyPays()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public, 1, FlawSeverity.Major);
            var w = new AiWorld(record);
            w.Host.Bias = OnlyRecall;

            Assert.True(w.Engine.Decide(record));

            Assert.Equal(FlawState.Resolved, record.State);
            Assert.Equal(FlawResponse.Recall, record.Response);
            Assert.True(record.Data.automaticChoice, "chosen by the policy, not by a click");

            Assert.Equal(1, w.World.Host.Spent.Count, "one payment, through SpendMoney of the company");
            Assert.Equal(FlawSpendKind.Fines, w.World.Host.Spent[0].Kind, "a recall is a fine");
            Assert.Equal(record.Quote.RecallCost, w.World.Host.Spent[0].Amount, "the amount of the quote");
            Assert.Equal(1, w.World.Host.Spent[0].RecordId);
            Assert.True(w.World.Host.Reputation.Count > 0, "the same consequences as for the player: the reputation");
            Assert.True(w.World.Host.PopularityHits.Count > 0, "and the popularity");
        }

        [Test]
        public void EveryReaction_IsApplied_WithTheRulesOfThePlayer()
        {
            var expected = new Dictionary<FlawResponse, FlawState>
            {
                { FlawResponse.Recall, FlawState.Resolved },
                { FlawResponse.Stepping, FlawState.Stepping },
                { FlawResponse.Patch, FlawState.Mitigated },
                { FlawResponse.Ignore, FlawState.Public },
            };
            var biases = new Dictionary<FlawResponse, float[]>
            {
                { FlawResponse.Recall, OnlyRecall }, { FlawResponse.Stepping, OnlyStepping }, { FlawResponse.Patch, OnlyPatch }, { FlawResponse.Ignore, OnlyIgnore },
            };

            foreach (KeyValuePair<FlawResponse, FlawState> pair in expected)
            {
                FlawRecord record = AiWorld.Ai(FlawState.Public);
                var w = new AiWorld(record);
                w.Host.Bias = biases[pair.Key];

                Assert.True(w.Engine.Decide(record), pair.Key.ToString());
                Assert.Equal(pair.Value, record.State, pair.Key.ToString());
                Assert.Equal(pair.Key, record.Response, pair.Key.ToString());
            }
        }

        [Test]
        public void ThePatch_IsPaidAsDevelopment_AndTheStepping_IsPaidByTheDay()
        {
            FlawRecord patched = AiWorld.Ai(FlawState.Public, 1);
            var a = new AiWorld(patched);
            a.Host.Bias = OnlyPatch;
            a.Engine.Decide(patched);
            Assert.Equal(FlawSpendKind.Development, a.World.Host.Spent[0].Kind);
            Assert.Equal(patched.Quote.PatchCost, a.World.Host.Spent[0].Amount);

            FlawRecord stepping = AiWorld.Ai(FlawState.Public, 1);
            var b = new AiWorld(stepping);
            b.Host.Bias = OnlyStepping;
            b.Engine.Decide(stepping);
            Assert.Equal(0, b.World.Host.Spent.Count, "a revision costs nothing at once: the days are charged by the daily pass");
            Assert.True(stepping.Data.steppingDailyCost > 0f);
        }

        [Test]
        public void AC_11_1_ARecallIsNeverChosen_WhenTheCompanyCannotPayIt_AndThePlayerGetsALetter()
        {
            // Critical, public; the headroom is 1.4 times the recall: below the 1.5 the content asks for.
            for (uint seed = 1; seed <= 200; seed++)
            {
                FlawRecord record = AiWorld.Ai(FlawState.Public, 1, FlawSeverity.Critical);
                var w = new AiWorld(record);
                w.Host.Threshold = 0f;
                w.Host.Money = record.Quote.RecallCost * 1.4f;
                w.Host.Rng_ = new Xoshiro128(seed);

                Assert.True(w.Engine.Decide(record), "seed " + seed);

                Assert.True(record.Response != FlawResponse.Recall, "seed " + seed + ": " + record.Response);
                Assert.True(record.Response == FlawResponse.Stepping || record.Response == FlawResponse.Patch || record.Response == FlawResponse.Ignore, "seed " + seed);
                Assert.Equal(1, w.Host.News.Count, "seed " + seed + ": the player is told");
                Assert.Equal(FlawNewsKind.Mail, w.Host.News[0].Plan.Kind, "a Critical flaw is a letter");
                Assert.Equal("dfr:news:1", w.Host.News[0].Plan.MailTag);
            }
        }

        [Test]
        public void TheDebtLimit_CountsAsHeadroom_ThroughTheThreshold()
        {
            // Money alone (0.8 of the recall) is short of the 1.5 the content asks for, but a debt limit of 0.8 of the recall (threshold -0.8) lifts the headroom to 1.6.
            FlawRecord poor = AiWorld.Ai(FlawState.Public, 1, FlawSeverity.Major);
            float recall = poor.Quote.RecallCost;
            Assert.True(recall > 0f);
            var a = new AiWorld(poor);
            a.Host.Money = recall * 0.8f;
            a.Host.Threshold = 0f;
            a.Host.Bias = new[] { 1f, 0f, 0f, 0f };
            Assert.True(a.Engine.Decide(poor));
            Assert.True(poor.Response != FlawResponse.Recall, "no headroom: the only reaction left is the fallback");
            Assert.Equal(FlawResponse.Ignore, poor.Response, "everything but the cut recall is switched off: all weights 0 means ignore");

            FlawRecord credit = AiWorld.Ai(FlawState.Public, 1, FlawSeverity.Major);
            var b = new AiWorld(credit);
            b.Host.Money = recall * 0.8f;
            b.Host.Threshold = -recall * 0.8f;
            b.Host.Bias = new[] { 1f, 0f, 0f, 0f };
            Assert.True(b.Engine.Decide(credit));
            Assert.Equal(FlawResponse.Recall, credit.Response, "with the credit the company can afford it");
        }

        [Test]
        public void AnOwnerNotInTheWorld_HasNoHeadroom_AndDoesNotRecall()
        {
            for (uint seed = 1; seed <= 60; seed++)
            {
                FlawRecord record = AiWorld.Ai(FlawState.Public);
                var w = new AiWorld(record);
                w.Host.OwnerMissing = true;
                w.Host.Rng_ = new Xoshiro128(seed);

                w.Engine.Decide(record);

                Assert.True(record.Response != FlawResponse.Recall, "seed " + seed);
            }
        }

        [Test]
        public void TheBias_IsAskedOnce_ForTheSituationOfTheFlaw()
        {
            FlawRecord open = AiWorld.Ai(FlawState.Public, 1);
            var a = new AiWorld(open);
            a.Engine.Decide(open);
            Assert.Equal("True", string.Join(",", a.Host.BiasAskedForPublic));

            FlawRecord inside = AiWorld.Ai(FlawState.InternalKnown, 1);
            var b = new AiWorld(inside);
            b.Engine.Decide(inside);
            Assert.Equal("False", string.Join(",", b.Host.BiasAskedForPublic));
        }

        [Test]
        public void TheBias_ChangesTheChoice_AndTheContentWeightsDecideWithoutIt()
        {
            // A random of 0.35 sits in the Stepping band of the plain weights [0.30, 0.30, 0.25, 0.15] ...
            FlawRecord plain = AiWorld.Ai(FlawState.Public);
            var a = new AiWorld(plain);
            a.Host.Rng_ = FakeRng.Always(0.35f);
            a.Engine.Decide(plain);
            Assert.Equal(FlawResponse.Stepping, plain.Response);

            // ... and in the Recall band of [0.30*10, 0.30, 0.25, 0.15] (3.0 of 3.7: 0.35 * 3.7 = 1.295 < 3.0).
            FlawRecord biased = AiWorld.Ai(FlawState.Public);
            var b = new AiWorld(biased);
            b.Host.Bias = new[] { 10f, 1f, 1f, 1f };
            b.Host.Rng_ = FakeRng.Always(0.35f);
            b.Engine.Decide(biased);
            Assert.Equal(FlawResponse.Recall, biased.Response);
        }

        [Test]
        public void OneNumberIsDrawn_PerDecision()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public);
            var w = new AiWorld(record);
            var rng = FakeRng.Always(0.3f);
            w.Host.Rng_ = rng;

            w.Engine.Decide(record);

            Assert.Equal(1, rng.Draws);
        }

        [Test]
        public void TheContentWeights_AreTheOnesOfTheBalance()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public);
            var w = new AiWorld(record);
            w.Host.Balance_.AiPolicy.Public = new[] { 0f, 0f, 0f, 1f };

            w.Engine.Decide(record);

            Assert.Equal(FlawResponse.Ignore, record.Response, "the weights come from aiPolicy of flaws.json");
        }

        // ---- the news: public, quiet, leaked ----

        [Test]
        public void PublicIncident_IsReported_WithTheResponse()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public, 12, FlawSeverity.Major);
            var w = new AiWorld(record);
            w.Host.Bias = OnlyPatch;

            w.Engine.Decide(record);

            Assert.Equal(1, w.Host.News.Count);
            FakeAiHost.Published news = w.Host.News[0];
            Assert.Equal(12, news.RecordId);
            Assert.Equal(FlawResponse.Patch, news.Response);
            Assert.Equal(FlawNewsKind.Mail, news.Plan.Kind);
            Assert.Equal("dfr:news:12", news.Plan.MailTag);
            Assert.True(news.Plan.Journal, "and written to the event journal");
            Assert.Equal("dfr.news.reaction.patch", news.Plan.ReactionKey);
        }

        [Test]
        public void MinorPublicIncident_IsANotification()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public, 2, FlawSeverity.Minor);
            var w = new AiWorld(record);

            w.Engine.Decide(record);

            Assert.Equal(1, w.Host.News.Count);
            Assert.Equal(FlawNewsKind.Notify, w.Host.News[0].Plan.Kind);
        }

        [Test]
        public void InternalFlaw_QuietAnswers_ReachNoOne_UntilTheLeak()
        {
            foreach (float[] bias in new[] { OnlyStepping, OnlyPatch, OnlyIgnore })
            {
                FlawRecord record = AiWorld.Ai(FlawState.InternalKnown, 1, FlawSeverity.Critical);
                var w = new AiWorld(record);
                w.Host.Bias = bias;

                Assert.True(w.Engine.Decide(record));

                Assert.Equal(0, w.Host.News.Count, "no news and no rumours: " + record.Response);
            }
        }

        [Test]
        public void InternalRecall_IsReported()
        {
            FlawRecord record = AiWorld.Ai(FlawState.InternalKnown, 6, FlawSeverity.Major);
            var w = new AiWorld(record);
            w.Host.Bias = OnlyRecall;

            Assert.True(w.Engine.Decide(record));

            Assert.Equal(1, w.Host.News.Count);
            Assert.Equal(FlawResponse.Recall, w.Host.News[0].Response);
        }

        [Test]
        public void ACoverUpThatLeaks_IsAnsweredAgain_WithoutIgnore_AndTheLeakIsReported()
        {
            FlawRecord record = AiWorld.Ai(FlawState.InternalKnown, 5, FlawSeverity.Major);
            var w = new AiWorld(record);
            w.Host.Bias = OnlyIgnore;
            Assert.True(w.Engine.Decide(record), "the quiet decision");
            Assert.Equal(FlawResponse.Ignore, record.Response);
            Assert.Equal(0, w.Host.News.Count, "nothing is told while the secret holds");

            // The monthly pass: the ignored flaw leaks, the record is public with a cover-up and waits for an answer again.
            Assert.True(FlawStateMachine.Leak(record, Day));
            Assert.Equal(FlawResponse.None, record.Response);
            Assert.True(FlawResponseEngine.AwaitsDecision(record));

            // The bias still wants nothing but "ignore": the way is closed after a leak, so the company starts the revision.
            Assert.True(w.Engine.Decide(record));
            Assert.Equal(FlawResponse.Stepping, record.Response, "ignoring is not an answer after a leak");
            Assert.Equal(1, w.Host.News.Count, "the leaked incident is news");
            Assert.Equal("dfr.news.leaked.title", w.Host.News[0].Plan.TitleKey);
            Assert.Equal("dfr:news:5", w.Host.News[0].Plan.MailTag);
        }

        [Test]
        public void ALeakedFlaw_NeverIgnores_WhateverTheDraw()
        {
            for (uint seed = 1; seed <= 100; seed++)
            {
                FlawRecord record = AiWorld.Ai(FlawState.Public, 1, FlawSeverity.Major, coverUp: true);
                record.Source = DiscoverySource.Leak;
                var w = new AiWorld(record);
                w.Host.Rng_ = new Xoshiro128(seed);

                Assert.True(w.Engine.Decide(record));

                Assert.True(record.Response != FlawResponse.Ignore && record.Response != FlawResponse.None, "seed " + seed + ": " + record.Response);
            }
        }

        [Test]
        public void FastForward_AnswersTheFlaw_ButMakesNoNews()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public, 1, FlawSeverity.Critical);
            var w = new AiWorld(record);
            w.Host.FastForward = true;

            Assert.True(w.Engine.Decide(record));

            Assert.Equal(0, w.Host.News.Count, "no news and no journal line during the fast-forward of a new game");
            Assert.True(record.Response != FlawResponse.None);
        }

        // ---- what is left alone, and the failures ----

        [Test]
        public void ThePlayersRecord_IsNotDecidedByThePolicy()
        {
            FlawRecord mine = ResponseWorld.Known(FlawState.Public, 1);
            var w = new AiWorld(mine);

            Assert.False(w.Engine.Decide(mine));

            Assert.Equal(FlawResponse.None, mine.Response);
            Assert.Equal(1, w.Host.Warnings.Count, "it is a mistake of the caller and is said so");
            Assert.Equal(0, w.World.Host.Spent.Count);
        }

        [Test]
        public void ARecordThatDoesNotWait_IsLeftAlone()
        {
            FlawRecord answered = AiWorld.Ai(FlawState.Public, 1);
            var w = new AiWorld(answered);
            w.Host.Bias = OnlyRecall;
            Assert.True(w.Engine.Decide(answered));
            int spent = w.World.Host.Spent.Count;

            Assert.False(w.Engine.Decide(answered), "a second answer is not given");
            Assert.Equal(spent, w.World.Host.Spent.Count, "nothing is paid twice");
            Assert.Equal(1, w.Host.News.Count, "and told twice");

            FlawRecord hidden = AiWorld.Ai(FlawState.Hidden, 2);
            Assert.False(new AiWorld(hidden).Engine.Decide(hidden), "a hidden flaw has nobody to answer to");
            Assert.False(w.Engine.Decide(null));
        }

        [Test]
        public void ARecordWithoutAQuote_Waits()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public);
            record.Data.quote = null;
            var w = new AiWorld(record);

            Assert.False(w.Engine.Decide(record));

            Assert.Equal(FlawResponse.None, record.Response);
            Assert.Equal(1, w.Host.Warnings.Count);
        }

        [Test]
        public void AFailingPolicy_LeavesTheRecordWaiting_AndIsReported()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public);
            var w = new AiWorld(record);
            w.Host.ThrowOnBias = true;

            Assert.False(w.Engine.Decide(record), "the exception does not leave the method");

            Assert.Equal(FlawResponse.None, record.Response);
            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(1, w.Host.Errors.Count);
            Assert.Equal(0, w.Host.News.Count);
        }

        [Test]
        public void AFailingReadOfTheMoney_LeavesTheRecordWaiting()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public);
            var w = new AiWorld(record);
            w.Host.ThrowOnHeadroom = true;

            Assert.False(w.Engine.Decide(record));

            Assert.Equal(FlawResponse.None, record.Response);
            Assert.Equal(1, w.Host.Errors.Count);
            Assert.Equal(0, w.World.Host.Spent.Count);
        }

        [Test]
        public void AFailingPayment_LeavesTheRecordWaiting()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public);
            var w = new AiWorld(record);
            w.Host.Bias = OnlyRecall;
            w.World.Host.ThrowOnSpend = true;

            Assert.False(w.Engine.Decide(record));

            Assert.Equal(FlawResponse.None, record.Response, "the money is taken before the record moves");
            Assert.Equal(FlawState.Public, record.State);
            Assert.Equal(1, w.Host.Errors.Count);
            Assert.Equal(0, w.Host.News.Count, "no news about an answer that did not happen");
        }

        [Test]
        public void AFailingNews_DoesNotUndoTheAnswer()
        {
            FlawRecord record = AiWorld.Ai(FlawState.Public);
            var w = new AiWorld(record);
            w.Host.Bias = OnlyRecall;
            w.Host.ThrowOnPublish = true;

            Assert.True(w.Engine.Decide(record), "the answer stands");

            Assert.Equal(FlawResponse.Recall, record.Response);
            Assert.Equal(1, w.Host.Errors.Count, "only the lost message is reported");
        }

        [Test]
        public void Constructor_NeedsItsParts()
        {
            var w = new AiWorld();
            Assert.Throws<ArgumentNullException>(() => new FlawAiEngine(null, w.Host));
            Assert.Throws<ArgumentNullException>(() => new FlawAiEngine(w.World.Engine, null));
        }

        // ---- the monthly pass and the cycle ----

        [Test]
        public void TheMonthlyPass_OffersAWaitingAiFlaw_ToThePolicyAgain()
        {
            FlawRecord ai = AiWorld.Ai(FlawState.Public, 1);
            FlawRecord mine = ResponseWorld.Known(FlawState.Public, 2);
            mine.Data.cpuIds = new List<int> { 200 };
            var w = new AiWorld(ai, mine);
            var host = new ForwardingDecisionHost { Ai = w.Engine };
            var decisions = new FlawDecisionEngine(w.World.Store, w.World.Engine, w.World.Book, host);

            // The first answer fails (the bias of the other feature throws): the flaw waits.
            w.Host.ThrowOnBias = true;
            Assert.Equal(FlawDispatchResult.AiDeferred, decisions.Dispatch(ai));
            Assert.Equal(FlawResponse.None, ai.Response);

            // The monthly pass tries again; the player's window is not a matter of the AI.
            w.Host.ThrowOnBias = false;
            host.Inner.Day = Day;
            decisions.Sweep();

            Assert.Equal("1,1", string.Join(",", host.AiDispatched), "dispatched at the discovery and by the pass; never the player's record");
            Assert.True(ai.Response != FlawResponse.None, "answered by the monthly pass");
            Assert.Equal(FlawResponse.None, mine.Response, "the window of the player is the event system's");

            Assert.Equal(0, decisions.RetryAi(), "an answered flaw is not offered again");
        }

        [Test]
        public void ARetryThatFailsAgain_IsCountedAndKeepsWaiting()
        {
            FlawRecord ai = AiWorld.Ai(FlawState.Public, 1);
            var w = new AiWorld(ai);
            var host = new ForwardingDecisionHost { Ai = w.Engine };
            var decisions = new FlawDecisionEngine(w.World.Store, w.World.Engine, w.World.Book, host);
            w.Host.ThrowOnBias = true;

            Assert.Equal(0, decisions.RetryAi());
            Assert.Equal(0, decisions.RetryAi());

            Assert.Equal(2, w.Host.Errors.Count, "reported every time");
            Assert.Equal(FlawResponse.None, ai.Response);
        }

        [Test]
        public void ADispatchedAiFlaw_IsAnsweredByTheDispatch_OfTheMonitor()
        {
            FlawRecord ai = AiWorld.Ai(FlawState.Public, 1, FlawSeverity.Major);
            var w = new AiWorld(ai);
            var host = new ForwardingDecisionHost { Ai = w.Engine };
            var decisions = new FlawDecisionEngine(w.World.Store, w.World.Engine, w.World.Book, host);
            w.Host.Bias = OnlyPatch;

            Assert.Equal(FlawDispatchResult.AiHandled, decisions.Dispatch(ai));

            Assert.Equal(0, host.Inner.Fired.Count, "an AI company has no window");
            Assert.Equal(FlawState.Mitigated, ai.State);
            Assert.Equal(1, w.Host.News.Count);
        }

        [Test]
        public void AiFlawFoundByTheCycle_IsAnsweredWithItsOwnQuote()
        {
            FlawRecord hidden = FlawCycleKit.Record(FlawState.Hidden, 1, baseCpu: 100, company: "ai:3", severity: FlawSeverity.Major);
            var w = new AiWorld(hidden);
            var cycleHost = new WiredCycleHost(w.World);
            cycleHost.Facts[100] = WiredCycleHost.ExampleFacts();
            w.World.SetDay(Day);
            var cycle = new FlawCycle(w.World.Store, cycleHost);
            var host = new ForwardingDecisionHost { Ai = w.Engine };
            var decisions = new FlawDecisionEngine(w.World.Store, w.World.Engine, w.World.Book, host);
            w.Host.Bias = OnlyRecall;

            Assert.True(cycle.Reveal(hidden, DiscoverySource.External), "found by the press");
            Assert.NotNull(hidden.Quote, "the quote is made at the discovery");
            Assert.Equal(FlawDispatchResult.AiHandled, decisions.Dispatch(hidden));

            Assert.Equal(FlawState.Resolved, hidden.State);
            Assert.Equal(hidden.Quote.RecallCost, w.World.Host.Spent[0].Amount, "the company pays the very amount of the quote");
            Assert.Equal(1, w.Host.News.Count);
            Assert.Equal("dfr:news:1", w.Host.News[0].Plan.MailTag);
        }

        // ---- the texts and the identifiers ----

        [Test]
        public void NewsKeys_AreInBothTables_WithTheSamePlaceholders_AndOnlyTheFiveArguments()
        {
            JsonObject ru = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/ru/design-flaw-recalls.json"));
            JsonObject en = FlawTestKit.ParseObject(FlawTestKit.ReadSource("L10n/en/design-flaw-recalls.json"));
            var keys = new List<string>
            {
                FlawNewsPlanner.TitleKey, FlawNewsPlanner.BodyKey, FlawNewsPlanner.LeakedTitleKey, FlawNewsPlanner.LeakedBodyKey, FlawNewsPlanner.NotifyKey, FlawNewsPlanner.NotifyBodyKey, FlawEffectModel.NewsKey,
            };
            foreach (FlawResponse response in new[] { FlawResponse.Recall, FlawResponse.Stepping, FlawResponse.Patch, FlawResponse.Ignore })
            {
                keys.Add(FlawNewsPlanner.ReactionKeyOf(response));
            }

            foreach (string key in keys)
            {
                Assert.True(ru.ContainsKey(key), key + " is in the Russian table");
                Assert.True(en.ContainsKey(key), key + " is in the English table");

                string a = Placeholders(ru.GetString(key));
                string b = Placeholders(en.GetString(key));
                Assert.Equal(a, b, key + ": the same placeholders in both languages");
                foreach (Match m in Regex.Matches(ru.GetString(key) + en.GetString(key), @"\{(\d+)\}"))
                {
                    Assert.True(int.Parse(m.Groups[1].Value) < FlawNewsPlanner.ArgCount, key + ": {" + m.Groups[1].Value + "} is one of the five arguments of the news");
                }
            }
        }

        [Test]
        public void NewsIdentifiers_PassTheRulesOfTheFeaturePrefix()
        {
            JsonObject prefixes = CoreContentTests.LoadCore().GetObject("prefixes");
            try
            {
                ModIds.Load(prefixes);
                Assert.True(ModIds.IsTagAllowed("design-flaw-recalls", FlawNewsPlanner.MailTagOf(17)), "the tag of the letter");
                Assert.Equal("dfr:news:17", FlawNewsPlanner.MailTagOf(17));
                Assert.True(ModIds.IsEventIdAllowed("design-flaw-recalls", FlawNewsPlanner.JournalSourceId), "the source of the journal line");
                foreach (string key in new[] { FlawNewsPlanner.TitleKey, FlawNewsPlanner.BodyKey, FlawNewsPlanner.LeakedTitleKey, FlawNewsPlanner.LeakedBodyKey, FlawNewsPlanner.NotifyKey, FlawNewsPlanner.NotifyBodyKey, FlawEffectModel.NewsKey })
                {
                    Assert.True(ModIds.IsL10nKeyAllowed("design-flaw-recalls", key), key);
                }
            }
            finally
            {
                ModIds.Clear();
            }
        }

        // ---- the game side by the sources ----

        [Test]
        public void NewsSource_SendsALetterANotificationAndAJournalLine_EachUnderItsOwnGuard()
        {
            string code = Code("FlawNews.cs");

            Assert.True(code.Contains("ModMail.Send(new ModMailMessage"), "the letter");
            Assert.True(code.Contains("Priority = MailPriority.Normal"), "Normal priority");
            Assert.True(code.Contains("Tag = plan.MailTag"), "the tag dfr:news:<recordId> of the plan");
            Assert.True(code.Contains("TitleKey = plan.TitleKey") && code.Contains("BodyKey = plan.BodyKey"), "the keys of the plan");
            Assert.False(code.Contains("Choices ="), "a piece of news has no buttons");
            Assert.True(code.Contains("ModNotify.Show(FeatureId, ModL10n.L(plan.NotifyKey, args), ModL10n.L(plan.NotifyBodyKey, args))"), "the notification of a Minor flaw: the title and a second line");
            Assert.True(code.Contains("EventEffects.ApplyDirect(spec, record.CompanyKey, FlawNewsPlanner.JournalSourceId, args)"), "the line of the event journal");
            Assert.True(code.Contains("type = FlawEffectTypes.News"), "through the effect that changes nothing");

            foreach (string site in new[] { "news/args", "news/mail", "news/notify", "news/journal", "news/auto" })
            {
                Assert.True(code.Contains("\"" + site + "\""), "guarded: " + site);
            }
            Assert.False(code.Contains("IsBatchRun") || code.Contains("IsFastForwarding") || code.Contains("DuringFastForward"), "the core holds the showing back; the plan knows the fast-forward");
        }

        [Test]
        public void DispatcherSource_SendsTheAiBranchToTheEngine_AndTheNewsToFlawNews()
        {
            string code = Code("FlawDecisionDispatcher.cs");

            Assert.True(code.Contains("IFlawDecisionHost, IFlawAiHost"), "the game's side of both engines");
            Assert.True(code.Contains("return _ai.Decide(record);"), "DispatchAi hands the flaw to the AI engine");
            Assert.True(code.Contains("_feature.News.OnAiPublic(record, response, plan);"), "the news");
            Assert.True(code.Contains("ModRandom.Stream(FeatureId + \"/ai\")"), "its own random stream");
            Assert.True(code.Contains("ModTicks.IsFastForwarding"), "the fast-forward of a new game");
            Assert.False(code.Contains("not plugged in"), "the placeholder is gone");
        }

        [Test]
        public void TheMoneyOfAnAiCompany_GoesThroughSpendMoney_OfTheCompany()
        {
            string applier = Code("FlawResponseApplier.cs");

            Assert.True(applier.Contains("GameCompany owner = ModCompanies.Resolve(record.CompanyKey);"), "the owner is the company of the key: the player's or an AI company");
            Assert.True(applier.Contains("owner.SpendMoney(amount, kind == FlawSpendKind.Fines ? GameExpenseOrigin.Fines : GameExpenseOrigin.Development);"),
                "ICompany.SpendMoney: AICompany.SpendMoney for a rival (the books and the reports are kept)");
            Assert.False(applier.Contains("AddMoney") || applier.Contains("MoneyAmount ="), "no cheating with the balance");
        }

        [Test]
        public void FeatureSource_BuildsTheNews_AndRegistersTheJournalEffect()
        {
            string feature = Code("DesignFlawRecallsFeature.cs");
            Assert.True(feature.Contains("News = new FlawNews(this);"), "the news object");
            Assert.True(feature.Contains("EventEffects.Register(FlawEffectTypes.News, new FlawNewsEffect());"), "the effect type of the journal line");

            string effect = Code("Effects/FlawNewsEffect.cs");
            Assert.True(effect.Contains(": IEventEffectHandler"));
            Assert.True(effect.Contains("textKey = FlawEffectModel.NewsKey"), "the line of the journal");
            Assert.True(effect.Contains("public void Apply(AppliedEffect effect, EventContext ctx)\r\n        {\r\n        }") || effect.Contains("public void Apply(AppliedEffect effect, EventContext ctx)\n        {\n        }"),
                "the effect changes nothing in the game");
            Assert.False(effect.Contains("SpendMoney") || effect.Contains("ModReputation") || effect.Contains("Applier"), "the journal line has no consequences of its own");
        }

        [Test]
        public void TheJournalEffect_IsNotOneOfTheFourButtons()
        {
            Assert.Equal("FlawNews", FlawEffectTypes.News);
            Assert.False(Array.IndexOf(FlawEffectTypes.All, FlawEffectTypes.News) >= 0, "no event of the pack uses it");

            FlawResponse response;
            Assert.False(FlawEffectTypes.TryResponseOf(FlawEffectTypes.News, out response), "it stands for no response");

            string events = FlawTestKit.ReadSource("Content/design-flaw-recalls/events.json");
            Assert.False(events.Contains("FlawNews"), "events.json does not know it");
        }

        // ---- helpers ----

        private static string Placeholders(string text)
        {
            var found = new SortedSet<string>();
            foreach (Match m in Regex.Matches(text ?? string.Empty, @"\{\d+\}")) found.Add(m.Value);
            return string.Join(",", found);
        }

        private static string Code(string relative)
        {
            string src = SourceScan.SrcDirOrFail();
            string path = Path.Combine(src, "Features", "DesignFlawRecalls", relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "no such file: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }
    }
}
