using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// What the game side of the yearly awards, the quarterly notes and the public API must do and the unit build cannot run (TSK-014 … TSK-016): the ticks they
    /// listen to, the letters they send (priority, tag, action), the reputation they change, the order of the events of a published review, the gate of the API
    /// by the state of the feature, the soft link to <c>design-flaw-recalls</c> (made on <c>GameReady</c> and on a change of state, not in <c>Register</c>), and
    /// that the folder <c>Api</c> names nothing outside itself. The sources are read from the tree; comments and the text of strings are not looked at unless a
    /// rule is about a string.
    /// </summary>
    public class PressApiSourceTests
    {
        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PressReviews");
        }

        private static string Code(string relative, bool blankStrings = true)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing source file: " + relative);
            return SourceScan.Strip(SourceScan.ReadText(path), blankStrings);
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "not found: " + from);
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            Assert.True(end > start, "not found after it: " + to);
            return text.Substring(start, end - start);
        }

        private static string Register()
        {
            return Between(Code("PressReviewsFeature.cs"), "public void Register(FeatureContext ctx)", "public void OnEnabled()");
        }

        // ---- the awards (TSK-014) ----

        [Test]
        public void TheAwards_AreMadeOnTheLateYearTick_AfterTheGamesOwnLetter()
        {
            Assert.True(Regex.IsMatch(Register(), @"ctx\s*\.\s*OnTick\s*\(\s*TickPhase\s*\.\s*LateYearPassed\s*,\s*awards\s*\.\s*OnNewYear\s*\)"), "AwardsService.OnNewYear on LateYearPassed");
            Assert.True(Register().IndexOf("awards.OnNewYear", StringComparison.Ordinal) < Register().IndexOf("notes.YearlySnapshot", StringComparison.Ordinal),
                "the awards come first, the snapshot of the market after them");
        }

        [Test]
        public void TheAwardsService_SumsUpTheYearBefore_PaysTheReputation_FilesThemAndSendsTheLetter()
        {
            string text = Code("Game/AwardsService.cs");

            Assert.True(Regex.IsMatch(text, @"today\s*\.\s*Year\s*-\s*1"), "the year that has just ended");
            Assert.True(Regex.IsMatch(text, @"AwardsCalculator\s*\.\s*Compute\s*\("), "the awards are the pure calculator's");
            Assert.True(Regex.IsMatch(text, @"ModReputation\s*\.\s*Add\s*\(\s*company\s*,\s*payout\s*\.\s*Delta\s*,\s*payout\s*\.\s*Reason\s*\)"), "ModReputation.Add with the reason press.award:*");
            Assert.True(Regex.IsMatch(text, @"state\s*\.\s*awards\s*\.\s*Add\s*\("), "the awards go to the archive");
            Assert.True(Regex.IsMatch(text, @"PressMail\s*\.\s*SendAwards\s*\("), "and the letter is sent");
            Assert.True(Regex.IsMatch(text, @"state\s*\.\s*awards\s*\.\s*Exists\s*\("), "a year that has its awards is not done again");
            Assert.True(Regex.IsMatch(text, @"if\s*\(\s*result\s*\.\s*IsEmpty\s*\)\s*\{[^}]*return\s*;"), "a year with nothing to award has no letter and no entry");
            Assert.False(Regex.IsMatch(text, @"\bModNotify\b"), "no notification");
            Assert.True(text.IndexOf("state.awards.Add(", StringComparison.Ordinal) < text.IndexOf("PressMail.SendAwards(", StringComparison.Ordinal),
                "the archive first, then the letter");
        }

        [Test]
        public void TheLetterOfTheAwards_IsNormal_TaggedByTheYear_AndHasTheOpenAction()
        {
            string mail = Code("UI/PressMail.cs");
            string send = Between(mail, "internal static int SendAwards", "internal static int SendNotes");

            Assert.True(Regex.IsMatch(send, @"ModMail\s*\.\s*Send\s*\("), "ModMail.Send");
            Assert.True(Regex.IsMatch(send, @"Priority\s*=\s*MailPriority\s*\.\s*Normal"), "Normal");
            Assert.True(send.Contains("AwardsMailText.TagOf(awards.year)") && send.Contains("Tag = content.Tag"), "the tag press:awards:<year>");
            Assert.True(Regex.IsMatch(send, @"Id\s*=\s*ActionOpenAwards"), "the action");
            Assert.True(Regex.IsMatch(send, @"Repeatable\s*=\s*true"), "that can be pressed again");
            Assert.True(Regex.IsMatch(send, @"ModMail\s*\.\s*Find\s*\(\s*Feature\s*,"), "a year that has its letter is not sent again");
            Assert.False(Regex.IsMatch(send, @"TimeoutDays|Choices\s*=|ModNotify"), "no deadline, no choice, no notification");
        }

        [Test]
        public void TheActionOpenAwards_OpensTheTab_AndTheLetterStaysOpen()
        {
            string mail = Code("UI/PressMail.cs");
            string action = Between(mail, "internal static void OnAction", "internal static int SendAwards");

            Assert.True(action.Contains("ActionOpenAwards"), "the action id is handled");
            Assert.True(Regex.IsMatch(action, @"PressWindow\s*\.\s*Open\s*\(\s*PressWindow\s*\.\s*PressTab\s*\.\s*Awards\s*\)"), "the tab Awards of the window");
            Assert.True(Regex.IsMatch(action, @"ModHooks\s*\.\s*Guard\s*\("), "guarded");
            Assert.False(Regex.IsMatch(action, @"Resolve|Close"), "the core does not close a letter on an action and neither does the handler");
        }

        // ---- the notes (TSK-015) ----

        [Test]
        public void TheNotes_ComeOnTheMonthTick_AndTheSnapshotOnTheLateYearTick()
        {
            string register = Register();
            Assert.True(Regex.IsMatch(register, @"ctx\s*\.\s*OnTick\s*\(\s*TickPhase\s*\.\s*MonthPassed\s*,\s*notes\s*\.\s*OnQuarter\s*\)"), "PressNotesService.OnQuarter on MonthPassed");
            Assert.True(Regex.IsMatch(register, @"ctx\s*\.\s*OnTick\s*\(\s*TickPhase\s*\.\s*LateYearPassed\s*,\s*notes\s*\.\s*YearlySnapshot\s*\)"), "YearlySnapshot on LateYearPassed");
        }

        [Test]
        public void TheNotesService_PicksUpToTwo_Rests_AndSendsOneLetter()
        {
            string text = Code("Game/PressNotesService.cs");
            string quarter = Between(text, "public void OnQuarter()", "public void YearlySnapshot()");

            Assert.True(quarter.Contains("NoteSelection.IsQuarterMonth(today.Month)"), "only January, April, July and October");
            Assert.True(Regex.IsMatch(quarter, @"NoteSelection\s*\.\s*Pick\s*\([^;]*NoteSelection\s*\.\s*MaxPerQuarter"), "up to two");
            Assert.True(Regex.IsMatch(quarter, @"NoteCooldowns\s*\.\s*Rest\s*\("), "the cooldown is set");
            Assert.True(Regex.IsMatch(quarter, @"NoteCooldowns\s*\.\s*UntilOf\s*\([^;]*CooldownQuarters"), "by the quarters of the note");
            Assert.Equal(1, Regex.Matches(quarter, @"PressMail\s*\.\s*SendNotes\s*\(").Count, "one letter for the notes");
            Assert.True(Regex.IsMatch(quarter, @"ModMail\s*\.\s*Find\s*\(\s*Feature\s*,\s*NotesMailText\s*\.\s*TagOf"), "a quarter that has its letter is not done again");
            Assert.True(quarter.Contains("EnsureSnapshot(state, today.Year - 1)"), "January takes the snapshot of the year before it reads it (the tick of the month is earlier than the late tick of the year)");
            Assert.True(text.Contains("ModRandom.Stream(StreamId)") && Regex.IsMatch(Code("Game/PressNotesService.cs", false), "StreamId\\s*=\\s*Feature\\s*\\+\\s*\"/notes\""), "the dice are a stream of their own");
        }

        [Test]
        public void TheNotesLetter_IsLow_TaggedByTheMonth_WithoutActions()
        {
            string mail = Code("UI/PressMail.cs");
            string send = Between(mail, "internal static int SendNotes", "internal static int SendArticle");

            Assert.True(Regex.IsMatch(send, @"Priority\s*=\s*MailPriority\s*\.\s*Low"), "Low");
            Assert.True(send.Contains("NotesMailText.Build(") && send.Contains("Tag = content.Tag"), "the tag press:notes:<yyyy-MM>");
            Assert.False(Regex.IsMatch(send, @"Actions\s*=|Choices\s*=|TimeoutDays"), "no buttons");
        }

        [Test]
        public void TheNotesFacts_AreCollectedByThePressItself_NotByTheConditionsOfTheCore()
        {
            string text = Code("Game/PressNotesService.cs");

            Assert.True(text.Contains("MarketEventHandler") && text.Contains("HasSeenPgaEvent") && text.Contains("PgaDate"), "PGA: the market events of the game");
            Assert.True(Regex.IsMatch(text, @"ReputationTable\s*\.\s*ChangeOverYear\s*\("), "the reputation: the history of the core");
            Assert.True(Regex.IsMatch(text, @"MarketStatsBook\s*\.\s*FindNewEntrant\s*\("), "the new company: the archive of reviews");
            Assert.True(Regex.IsMatch(text, @"MostSoldCpus\s*\("), "the market: the best sellers of the segment");
            foreach (KeyValuePair<string, string> file in AllCode())
                Assert.False(Regex.IsMatch(file.Value, @"ModConditions\s*\.\s*Register"), file.Key + ": the conditions of the notes are not registered in ModConditions");
        }

        // ---- the articles and the API (TSK-016) ----

        [Test]
        public void TheArticleLetter_IsNormal_TaggedByANumber_AndTitledByTheArticle()
        {
            string mail = Code("UI/PressMail.cs");
            string send = Between(mail, "internal static int SendArticle", "private static int Accepted");

            Assert.True(Regex.IsMatch(send, @"Priority\s*=\s*MailPriority\s*\.\s*Normal"), "Normal");
            Assert.True(Regex.IsMatch(send, @"Tag\s*=\s*ArticleLog\s*\.\s*TagOf\s*\(\s*ArticleLog\s*\.\s*NextNumber\s*\("), "the tag press:article:<n>");
            Assert.True(Regex.IsMatch(send, @"TitleKey\s*=\s*article\s*\.\s*titleKey"), "the subject is the title of the article");
            Assert.True(Regex.IsMatch(send, @"ModMail\s*\.\s*FindAll\s*\(\s*Feature\s*,\s*ArticleLog\s*\.\s*TagPrefix"), "the numbers are kept by the letters themselves");
        }

        [Test]
        public void TheApi_IsGatedByTheStateOfTheFeature_AndCatchesWhatTheStoreThrows()
        {
            string api = Code("Api/PressReviewsApi.cs");

            Assert.True(Regex.IsMatch(api, @"IsEnabled\s*\{\s*get\s*\{\s*return\s+ModFeatures\s*\.\s*IsActive\s*\(\s*FeatureId\s*\)\s*;"), "IsEnabled is ModFeatures.IsActive");
            foreach (string method in new[] { "PublishArticle", "RegisterAdjuster", "GetReview" })
            {
                string body = Between(api, "public static " + (method == "GetReview" ? "ReviewRecord " : "void ") + method + "(", "public static ");
                Assert.True(Regex.IsMatch(body, @"ActiveBackend\s*\(\s*\)"), method + " is a no-op unless the feature is enabled");
                Assert.True(Regex.IsMatch(body, @"catch\s*\(\s*Exception\s+ex\s*\)") && body.Contains("Fault("), method + " catches and counts what the store throws");
            }
            string unregister = Between(api, "public static void UnregisterAdjuster(", "public static ReviewRecord GetReview(");
            Assert.False(Regex.IsMatch(unregister, @"ActiveBackend"), "an adjuster can always be taken out");
            Assert.True(Regex.IsMatch(api, @"return\s+store\s*!=\s*null\s*&&\s*ModFeatures\s*\.\s*IsActive\s*\(\s*FeatureId\s*\)\s*\?\s*store\s*:\s*null"), "ActiveBackend is the store while the feature is enabled");
        }

        [Test]
        public void TheEventOfAPublishedReview_IsRaisedThroughModHooks_WithACopy()
        {
            string api = Code("Api/PressReviewsApi.cs", false);
            Assert.True(Regex.IsMatch(api, @"public\s+static\s+event\s+Action<ReviewRecord>\s+ReviewPublished"), "event Action<ReviewRecord> ReviewPublished");
            Assert.True(Regex.IsMatch(api, @"ModHooks\s*\.\s*Raise\s*\(\s*""PressReviewsApi\.ReviewPublished""\s*,\s*ReviewPublished\s*,\s*copy\s*\)"), "raised through ModHooks.Raise");

            string backend = Code("Game/PressApiBackend.cs");
            Assert.True(Regex.IsMatch(backend, @"PressReviewsApi\s*\.\s*RaiseReviewPublished\s*\(\s*ReviewRecordCopy\s*\.\s*Of\s*\(\s*record\s*\)\s*\)"), "the subscribers get a copy");
            Assert.True(Regex.IsMatch(backend, @"ReviewRecordCopy\s*\.\s*Of\s*\(\s*ReviewArchive\s*\.\s*Find\s*\("), "and so does GetReview");
        }

        [Test]
        public void TheEventOfAPublishedReview_ComesAfterTheRecordIsInTheArchive_AndAfterTheLetter()
        {
            string scheduler = Code("Game/ReviewScheduler.cs", false);
            string review = Between(scheduler, "private ReviewRecord Review(", "private AdjusterSet AdjustersOf");
            Assert.True(review.Contains("ReviewQueue.Publish(state, entry, record"), "Review files the record in the archive");

            string due = Between(scheduler, "private void PublishDue(", "private ReviewRecord Review(");
            Assert.True(due.IndexOf("record = Review(", StringComparison.Ordinal) < due.IndexOf("AfterPublished(record)", StringComparison.Ordinal), "the archive first, the events after");

            string after = Between(scheduler, "private void AfterPublished(ReviewRecord record)", "private void LogVerbose");
            Assert.True(after.IndexOf("effects.OnReviewPublished", StringComparison.Ordinal) < after.IndexOf("ModHooks.Raise(\"ReviewScheduler.Published\"", StringComparison.Ordinal),
                "the effects are in before the subscribers hear");

            string register = Register();
            int letter = register.IndexOf("scheduler.Published += PressMail.OnReviewPublished", StringComparison.Ordinal);
            int api = register.IndexOf("scheduler.Published += api.OnReviewPublished", StringComparison.Ordinal);
            int flaws = register.IndexOf("scheduler.Published += flaws.OnReviewPublished", StringComparison.Ordinal);
            Assert.True(letter > 0 && api > letter && flaws > api, "the letter (it writes mailId), then the API, then the reveal of a flaw");
        }

        [Test]
        public void TheAdjusters_AreAskedBeforeTheMarks_ByTheComposer()
        {
            string scheduler = Code("Game/ReviewScheduler.cs", false);
            Assert.True(Regex.IsMatch(scheduler, @"ReviewComposer\s*\.\s*Compose\s*\([^;]*adjusters\s*\)"), "the scheduler hands the adjusters to the composer");
            Assert.True(scheduler.Contains("scheduler/adjusters") && scheduler.Contains("ModHooks.Guard"), "asking for them is guarded");

            string composer = Code("Logic/ReviewComposer.cs");
            Assert.True(composer.IndexOf("AdjusterPipeline.Apply(", StringComparison.Ordinal) < composer.IndexOf("OutletPanel.ScoreBy(", StringComparison.Ordinal), "before the outlets mark");
            Assert.True(Regex.IsMatch(composer, @"AdjusterPipeline\s*\.\s*Apply\s*\(\s*draft\s*,\s*adjusters\s*,\s*config\s*\.\s*AdjusterLimits"), "with the limits of the content");
        }

        [Test]
        public void TheBackend_AsksOnlyAdjustersOfFeaturesThatAreOn_AndCountsAFailureAgainstTheirOwner()
        {
            string backend = Code("Game/PressApiBackend.cs");

            Assert.True(Regex.IsMatch(backend, @"ModFeatures\s*\.\s*FeatureOfType\s*\(\s*adjuster\s*\.\s*GetType\s*\(\s*\)\s*\)"), "the owner is the feature of the class");
            Assert.True(Regex.IsMatch(backend, @"if\s*\(\s*!ModFeatures\s*\.\s*IsActive\s*\(\s*owner\s*\)\s*\)\s*continue"), "an adjuster of a feature that is off is skipped");
            Assert.True(Regex.IsMatch(backend, @"ModHooks\s*\.\s*ReportFault\s*\(\s*owner\s*,"), "its failure counts against it");
            Assert.True(Regex.IsMatch(backend, @"IsNoteKnown\s*=\s*ModL10n\s*\.\s*Has"), "a note the tables lack is dropped");
            Assert.True(Regex.IsMatch(backend, @"new\s+AdjusterBook<IReviewAdjuster>"), "the book of adjusters is the pure one");
        }

        [Test]
        public void TheFeature_AttachesTheStoreOfTheApiOnce_InRegister()
        {
            string feature = Code("PressReviewsFeature.cs");
            Assert.Equal(1, Regex.Matches(feature, @"PressReviewsApi\s*\.\s*AttachBackend\s*\(").Count);
            Assert.True(Register().Contains("PressReviewsApi.AttachBackend(api)"), "in Register");
            Assert.True(Regex.IsMatch(Register(), @"scheduler\s*\.\s*AdjusterSource\s*=\s*api\s*\.\s*AdjustersFor"), "the scheduler asks the store for the adjusters");
        }

        // ---- the link to design-flaw-recalls ----

        [Test]
        public void TheFlawBridge_IsNotMadeInRegister_ItIsAttachedOnGameReadyAndOnAChangeOfState()
        {
            string register = Register();
            Assert.False(Regex.IsMatch(register, @"\.\s*Attach\s*\(|DesignFlawRecallsApi|OnFlawStateChanged"), "Register does not touch the registry of the other feature");
            Assert.True(Regex.IsMatch(register, @"ModTicks\s*\.\s*GameReady\s*\+=\s*flaws\s*\.\s*OnGameReady"), "on GameReady");
            Assert.True(Regex.IsMatch(register, @"ModFeatures\s*\.\s*StateChanged\s*\+=\s*flaws\s*\.\s*OnFeatureStateChanged"), "and on a change of state");

            string feature = Code("PressReviewsFeature.cs");
            string enabled = Between(feature, "public void OnEnabled()", "public void OnDisabled()");
            Assert.True(Regex.IsMatch(enabled, @"ModTicks\s*\.\s*IsGameReady") && enabled.Contains("flaws.Refresh()"), "a feature switched on later looks at the link at once");
            Assert.True(Regex.IsMatch(feature.Substring(feature.IndexOf("public void OnDisabled()", StringComparison.Ordinal)), @"flaws\s*\.\s*Detach\s*\(\s*\)"), "switched off, it lets go");
        }

        [Test]
        public void TheFlawBridge_AttachesOnlyWhenBothFeaturesAreOnAndTheGameIsReady()
        {
            string text = Code("Game/FlawBridge.cs");
            string refresh = Between(text, "internal void Refresh()", "internal void Attach()");

            Assert.True(Regex.IsMatch(refresh, @"ModTicks\s*\.\s*IsGameReady\s*&&\s*ModFeatures\s*\.\s*IsActive\s*\(\s*Feature\s*\)\s*&&\s*ModFeatures\s*\.\s*IsActive\s*\(\s*FlawFeatureId\s*\)"), "both enabled and the game ready");
            Assert.True(Regex.IsMatch(text, @"OnFlawStateChanged\s*-=\s*OnFlawStateChanged\s*;\s*DesignFlawRecallsApi\s*\.\s*Registry\s*\.\s*OnFlawStateChanged\s*\+=\s*OnFlawStateChanged"), "one subscription, however often it is attached");
            Assert.True(text.Contains("if (attached) return;") && text.Contains("if (!attached) return;"), "attach and detach are idempotent");
        }

        [Test]
        public void TheReveal_RollsTheChanceOfTheContent_AndAsksTheOtherFeatureForAnExternalDiscovery()
        {
            string text = Code("Game/FlawBridge.cs");
            string reveal = Between(text, "internal void OnReviewPublished(ReviewRecord record)", "internal void OnFlawStateChanged(FlawRecordView view)");

            Assert.True(Regex.IsMatch(reveal, @"!attached"), "nothing is rolled while the bridge is not attached");
            Assert.True(Regex.IsMatch(reveal, @"PressContent\s*\.\s*Current\s*\.\s*Config\s*\.\s*FlawReveal\s*\.\s*Chance"), "flawReveal.chance of the content");
            Assert.True(Regex.IsMatch(reveal, @"ModRandom\s*\.\s*Stream\s*\(\s*StreamId\s*\)\s*\.\s*Chance\s*\("), "from a stream of its own");
            Assert.True(Regex.IsMatch(reveal, @"DesignFlawRecallsApi\s*\.\s*Registry\s*\.\s*TryReveal\s*\(\s*record\s*\.\s*cpuSaveId\s*,\s*DiscoverySource\s*\.\s*External\s*\)"), "FlawRegistry.TryReveal(id, DiscoverySource.External)");
            Assert.True(Regex.IsMatch(Code("Game/FlawBridge.cs", false), "StreamId\\s*=\\s*PressReviewsFeature\\s*\\.\\s*FeatureId\\s*\\+\\s*\"/flaw\""), "the stream press-reviews/flaw");
            Assert.False(Regex.IsMatch(reveal, @"\bModNotify\b|ModMail"), "the player is not told that a flaw exists: the other feature decides what comes of it");
        }

        [Test]
        public void APublicFlaw_GetsOneArticle_AndANoteInTheReviewOfItsCpu()
        {
            string text = Code("Game/FlawBridge.cs");
            string changed = Between(text, "internal void OnFlawStateChanged(FlawRecordView view)", "private static void AddNote");

            Assert.True(Regex.IsMatch(changed, @"view\s*\.\s*State\s*!=\s*FlawState\s*\.\s*Public"), "only a flaw that is public");
            Assert.True(Regex.IsMatch(changed, @"ArticleLog\s*\.\s*Has\s*\(\s*state\s*,\s*Feature\s*,\s*ArticleTitleKey\s*,\s*flawId\s*\)"), "once per flaw, also after a load");
            Assert.True(Regex.IsMatch(changed, @"publisher\s*\.\s*Publish\s*\(\s*Feature\s*,\s*ArticleTitleKey\s*,\s*ArticleBodyKey"), "an article like PublishArticle");
            Assert.True(Regex.IsMatch(changed, @"AddNote\s*\(\s*state\s*,\s*view\s*\.\s*BaseCpuId\s*\)"), "the note goes to the review of the base CPU");
            Assert.True(Regex.IsMatch(Between(text, "private static void AddNote", "private static string CpuLabelOf"), @"ReviewArchive\s*\.\s*Find\s*\("), "if the CPU has a review");
        }

        // ---- the folder Api ----

        private static List<KeyValuePair<string, string>> AllCode()
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
                result.Add(new KeyValuePair<string, string>(SourceScan.Relative(FeatureDir(), path), SourceScan.Strip(SourceScan.ReadText(path), true)));
            Assert.True(result.Count > 0, "no sources");
            return result;
        }

        [Test]
        public void TheApiFolder_NamesNoTypeOfTheRestOfTheFeature()
        {
            // The dependent features are built with the folder Api alone (build_feature.py): it must compile without the rest of the feature.
            var inApi = new HashSet<string>(StringComparer.Ordinal);
            var outside = new HashSet<string>(StringComparer.Ordinal);
            var declaration = new Regex(@"\b(?:class|struct|enum|interface)\s+(\w+)");
            var apiCode = new List<KeyValuePair<string, string>>();

            foreach (KeyValuePair<string, string> file in AllCode())
            {
                bool api = file.Key.StartsWith("Api/", StringComparison.Ordinal);
                if (api) apiCode.Add(file);
                foreach (Match match in declaration.Matches(file.Value)) (api ? inApi : outside).Add(match.Groups[1].Value);
            }
            outside.ExceptWith(inApi);   // a type that the API declares is not "outside" even when the same name is used in Logic
            Assert.True(apiCode.Count >= 5 && outside.Count > 40, "the scan found the files: " + apiCode.Count + " in Api, " + outside.Count + " types outside");

            var violations = new List<string>();
            foreach (KeyValuePair<string, string> file in apiCode)
            {
                foreach (string name in outside)
                {
                    Match match = Regex.Match(file.Value, @"(?<![\w.])" + Regex.Escape(name) + @"\b");
                    if (match.Success) violations.Add(file.Key + ":" + SourceScan.LineOf(file.Value, match.Index) + ": " + name);
                }
            }
            Assert.True(violations.Count == 0, "the folder Api names types outside itself:\n  " + string.Join("\n  ", violations));
        }

        [Test]
        public void TheApiModel_IsFreeOfUnityAndTheGame()
        {
            string model = Code("Api/Model/ReviewModel.cs");
            var engine = new Regex(@"\busing\s+(?:static\s+)?(?:global::)?(?:UnityEngine|UnityEditor|Unity|TMPro|ProcessorTycoon|HarmonyLib)\b"
                + @"|(?<![\w.])(?:global::)?(?:UnityEngine|UnityEditor|TMPro|HarmonyLib|ProcessorTycoon)(?=\s*\.\s*\w)");
            Assert.False(engine.IsMatch(model), "Api/Model is shared with the unit build");
        }
    }
}
