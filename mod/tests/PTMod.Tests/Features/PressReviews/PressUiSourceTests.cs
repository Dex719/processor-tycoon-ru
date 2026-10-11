using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PressReviews;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.PressReviews
{
    /// <summary>
    /// What the windows, the letter and the API of press-reviews must do and the code of the game side cannot show in a test (TSK-010 … TSK-013): the letter goes
    /// through <c>ModMail.Send</c> as a <c>Normal</c> letter with the tag <c>press:review:&lt;id&gt;</c> and the action "Open the review" and sends no
    /// notification; the windows are made from the shared template <c>Window.Panel</c> (not modal); the page in the hub and the API of the reputation exist;
    /// every string the code asks for is in both languages. The sources are read from the tree, comments and the text of strings are not looked at unless a rule is
    /// about a string.
    /// </summary>
    public class PressUiSourceTests
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

        private static List<KeyValuePair<string, string>> AllCode(bool blankStrings)
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
                result.Add(new KeyValuePair<string, string>(SourceScan.Relative(FeatureDir(), path), SourceScan.Strip(SourceScan.ReadText(path), blankStrings)));
            Assert.True(result.Count > 0, "no sources");
            return result;
        }

        // ---- the letter (TSK-010) ----

        [Test]
        public void TheLetter_GoesThroughModMailSend_AsANormalLetterWithTheReviewTagAndTheOpenAction()
        {
            string mail = Code("UI/PressMail.cs");
            Assert.True(Regex.IsMatch(mail, @"ModMail\s*\.\s*Send\s*\("), "the letter is sent with ModMail.Send");
            Assert.True(Regex.IsMatch(mail, @"Priority\s*=\s*MailPriority\s*\.\s*Normal"), "priority Normal");
            Assert.True(mail.Contains("ReviewMailText.TagOf("), "the tag is press:review:<cpuSaveId>");
            Assert.True(Regex.IsMatch(mail, @"new\s+ModMailAction"), "the action 'Open the review' is a ModMailAction");
            Assert.True(Regex.IsMatch(mail, @"Repeatable\s*=\s*true"), "and it can be pressed again");
            Assert.True(Regex.IsMatch(mail, @"record\s*\.\s*mailId\s*=\s*id"), "the id of the letter is kept in ReviewRecord.mailId");
            Assert.False(Regex.IsMatch(mail, @"TimeoutDays|Choices\s*="), "a review letter has no deadline and no choice");
        }

        [Test]
        public void TheLetter_IsForThePlayersCpuOnly_RivalsGetNoLetterAndNoNotification()
        {
            string mail = Code("UI/PressMail.cs");
            Assert.True(Regex.IsMatch(mail, @"record\s*\.\s*companyKey\s*,\s*ModCompanies\s*\.\s*PlayerKey"), "OnReviewPublished lets only the player's CPUs through");

            foreach (KeyValuePair<string, string> file in AllCode(true))
                Assert.False(Regex.IsMatch(file.Value, @"\bModNotify\b"), file.Key + " must not use ModNotify (the rivals' reviews come in the digest)");
        }

        [Test]
        public void TheLetter_OpensNoWindowByItself_ItsActionOpensTheCard()
        {
            string mail = Code("UI/PressMail.cs");
            string send = mail.Substring(mail.IndexOf("internal static int SendReview", StringComparison.Ordinal),
                mail.IndexOf("internal static void OnAction", StringComparison.Ordinal) - mail.IndexOf("internal static int SendReview", StringComparison.Ordinal));
            Assert.False(Regex.IsMatch(send, @"ReviewCardWindow|PressWindow"), "sending the letter does not open a window");

            string action = mail.Substring(mail.IndexOf("internal static void OnAction", StringComparison.Ordinal));
            Assert.True(Regex.IsMatch(action, @"ReviewCardWindow\s*\.\s*Open\s*\("), "the action opens the card");
            Assert.True(action.Contains("ReviewMailText.TryParseTag(mail.Tag"), "the CPU comes from the tag of the letter");
            Assert.True(Regex.IsMatch(action, @"ModHooks\s*\.\s*Guard\s*\("), "the handler is guarded");
        }

        [Test]
        public void TheFeature_SubscribesTheLetterOnce_AndRegistersTheActionHandlerOnce()
        {
            string feature = Code("PressReviewsFeature.cs");
            string register = Between(feature, "public void Register(FeatureContext ctx)", "public void OnEnabled()");
            Assert.Equal(1, Regex.Matches(register, @"scheduler\s*\.\s*Published\s*\+=\s*PressMail\s*\.\s*OnReviewPublished").Count, "one subscription to Published");
            Assert.Equal(1, Regex.Matches(register, @"ModMail\s*\.\s*RegisterActionHandler\s*\(\s*FeatureId\s*,\s*PressMail\s*\.\s*OnAction\s*\)").Count, "one action handler");
            Assert.False(feature.Substring(feature.IndexOf("public void OnEnabled()", StringComparison.Ordinal)).Contains("RegisterActionHandler"), "not touched when the feature is switched");
        }

        // ---- the windows (TSK-011, TSK-012) ----

        [Test]
        public void TheWindows_AreMadeFromTheSharedPanelTemplate_NotModal()
        {
            foreach (string file in new[] { "UI/ReviewCardWindow.cs", "UI/PressWindow.cs" })
            {
                string text = Code(file);
                Assert.True(Regex.IsMatch(text, @"ModUI\s*\.\s*CreateWindow\s*\([^;]*WindowTemplate\s*\.\s*Panel"), file + " uses ModUI.CreateWindow with WindowTemplate.Panel");
                Assert.False(Regex.IsMatch(text, @"WindowTemplate\s*\.\s*Modal"), file + " must not be modal: the card and the window do not stop the game");
                Assert.False(text.Contains("ReviewWindow"), file + " does not use the scene object ReviewWindow (spike: unusable, see the note in the class)");
            }
        }

        [Test]
        public void TheCardAndTheWindow_AreOpenedThroughAGuard_AndOnlyWhileTheFeatureIsActive()
        {
            foreach (string file in new[] { "UI/ReviewCardWindow.cs", "UI/PressWindow.cs" })
            {
                string text = Code(file);
                Assert.True(Regex.IsMatch(text, @"ModHooks\s*\.\s*Guard\s*\("), file + ": the entry is guarded");
                Assert.True(Regex.IsMatch(text, @"ModFeatures\s*\.\s*IsActive\s*\("), file + ": nothing opens while the feature is off");
                Assert.True(Regex.IsMatch(text, @"ModUI\s*\.\s*IsReady"), file + ": nothing is made before the templates of the scene are looked up");
            }
        }

        [Test]
        public void TheWindowPress_HasTheFourTabs_InTheOrderOfTheDesign()
        {
            string text = Code("UI/PressWindow.cs", false);
            Assert.True(Regex.IsMatch(text, @"enum\s+PressTab\s*\{\s*Reviews\s*=\s*0\s*,\s*Reputation\s*=\s*1\s*,\s*Awards\s*=\s*2\s*,\s*Articles\s*=\s*3\s*,?\s*\}"), "PressTab: Reviews, Reputation, Awards, Articles");
            foreach (string tab in new[] { "ReviewsTab", "ReputationTab", "AwardsTab", "ArticlesTab" })
                Assert.True(text.Contains(tab + ".Build("), "the window builds " + tab);
            Assert.True(Regex.IsMatch(text, @"public\s+static\s+void\s+Open\s*\(\s*PressTab\s+tab\s*=\s*PressTab\s*\.\s*Reviews\s*\)"), "Open(PressTab tab = Reviews)");
        }

        [Test]
        public void TheListOfReviews_HasThreeDropdownFilters_APagerAndAClickThatOpensTheCard()
        {
            string text = Code("UI/ReviewsTab.cs");
            Assert.Equal(3, Regex.Matches(text, @"ModUI\s*\.\s*Dropdown\s*\(").Count, "company, segment, year");
            Assert.True(Regex.IsMatch(text, @"ReviewList\s*\.\s*Page\s*\("), "pages come from ReviewList");
            Assert.True(Regex.IsMatch(text, @"ReviewCardWindow\s*\.\s*Open\s*\("), "a row opens the card");
        }

        [Test]
        public void TheReputationTab_ReadsTheHistoryOfTheCore_AndKeepsNoCopyOfIt()
        {
            string text = Code("UI/ReputationTab.cs");
            Assert.True(text.Contains("ModReputation.History"), "the change and the reasons come from ModReputation.History");
            Assert.True(Regex.IsMatch(text, @"ReputationTable\s*\.\s*Build\s*\("), "the rows are made by ReputationTable");
            Assert.False(Regex.IsMatch(text, @"List<ReputationChange>|Dictionary<[^>]*ReputationChange"), "no own copy of the history");

            foreach (KeyValuePair<string, string> file in AllCode(true))
                Assert.False(Regex.IsMatch(file.Value, @"ModReputation\s*\.\s*Drift\s*\("), file.Key + ": the drift is the core's");
        }

        [Test]
        public void TheAwardsAndArticlesTabs_ReadTheState()
        {
            Assert.True(Regex.IsMatch(Code("UI/AwardsTab.cs"), @"PressSaveAdapter\s*\.\s*Instance\s*\.\s*State\s*\.\s*awards"), "the awards come from the state");
            Assert.True(Regex.IsMatch(Code("UI/ArticlesTab.cs"), @"PressSaveAdapter\s*\.\s*Instance\s*\.\s*State\s*\.\s*articles"), "the articles come from the state");
        }

        // ---- the hub and the API (TSK-012, TSK-013) ----

        [Test]
        public void TheHub_GetsAPageWithTheReputationLineAndTheButton()
        {
            string feature = Code("PressReviewsFeature.cs");
            string register = Between(feature, "public void Register(FeatureContext ctx)", "public void OnEnabled()");
            Assert.True(Regex.IsMatch(register, @"ctx\s*\.\s*HubPage\s*\(\s*PressHubPage\s*\.\s*TitleKey\s*,\s*PressHubPage\s*\.\s*Build\s*\)"), "ctx.HubPage(...)");

            string page = Code("UI/PressHubPage.cs", false);
            Assert.True(page.Contains("press.hub.reputation"), "the line 'Reputation: N — level'");
            Assert.True(page.Contains("ModHub.Close()") && page.Contains("PressWindow.Open("), "the hub is closed, then the window opens");
            Assert.True(Regex.IsMatch(page, @"ModUI\s*\.\s*Button\s*\("), "a button opens the window");
        }

        [Test]
        public void TheApi_HasFormatReputationAndTheLevelKey_AndNeedsNothingOutsideItsFolder()
        {
            string api = Code("Api/PressReviewsApi.cs");
            Assert.True(Regex.IsMatch(api, @"public\s+static\s+string\s+FormatReputation\s*\(\s*ICompany\s+company\s*\)"), "FormatReputation(ICompany)");
            Assert.True(Regex.IsMatch(api, @"public\s+static\s+string\s+GetReputationLevelKey\s*\(\s*ICompany\s+company\s*\)"), "GetReputationLevelKey(ICompany)");
            Assert.True(Regex.IsMatch(api, @"public\s+static\s+bool\s+IsEnabled"), "IsEnabled");
            Assert.True(Regex.IsMatch(api, @"public\s+static\s+partial\s+class\s+PressReviewsApi"), "partial: the rest of the API joins it in the file of its task");

            // The dependent features are built with the Api folder alone: it must not name a type of the rest of the feature.
            foreach (string name in new[] { "PressContent", "PressSaveAdapter", "ReviewConfig", "ReputationLevels", "PressWindow", "PressState", "ReviewEffects" })
                Assert.False(Regex.IsMatch(api, @"\b" + name + @"\b"), "Api/PressReviewsApi.cs must not use " + name);
        }

        [Test]
        public void TheApis_BuiltInLevels_AreTheThresholdsAndKeysOfTheDefaultConfiguration()
        {
            // The Api folder cannot see ReviewConfig, so it repeats the thresholds for the time before the feature starts; they must not drift apart.
            string api = Code("Api/PressReviewsApi.cs", false);
            Match mins = Regex.Match(api, @"DefaultMins\s*=\s*\{([^}]*)\}");
            Match keys = Regex.Match(api, @"DefaultKeys\s*=\s*\{([^}]*)\}");
            Assert.True(mins.Success && keys.Success, "DefaultMins and DefaultKeys are in Api/PressReviewsApi.cs");

            var apiMins = new List<string>();
            foreach (string part in mins.Groups[1].Value.Split(',')) if (part.Trim().Length > 0) apiMins.Add(part.Trim());
            var apiKeys = new List<string>();
            foreach (Match key in Regex.Matches(keys.Groups[1].Value, "\"([^\"]+)\"")) apiKeys.Add(key.Groups[1].Value);

            List<ReputationLevel> levels = ReviewConfig.CreateDefault().ReputationLevels;
            Assert.Equal(levels.Count, apiKeys.Count, "one key per level");
            Assert.Equal(levels.Count - 1, apiMins.Count, "a threshold per level but the lowest");
            for (int i = 0; i < levels.Count; i++)
            {
                Assert.Equal(levels[i].Key, apiKeys[i], "key of level " + i);
                if (i < apiMins.Count) Assert.Equal(levels[i].Min.ToString(System.Globalization.CultureInfo.InvariantCulture), apiMins[i], "threshold of level " + i);
            }
        }

        [Test]
        public void TheApi_TakesItsLevelsFromTheFeaturesContent_OnceItHasStarted()
        {
            string feature = Code("PressReviewsFeature.cs");
            string register = Between(feature, "public void Register(FeatureContext ctx)", "public void OnEnabled()");
            Assert.True(Regex.IsMatch(register, @"PressReviewsApi\s*\.\s*LevelKeyProvider\s*=\s*LevelKeyOf"), "the thresholds of the content answer");
            Assert.True(Regex.IsMatch(feature, @"ReputationLevels\s*\.\s*KeyFor\s*\(\s*reputation\s*,\s*PressContent\s*\.\s*Current\s*\.\s*Config\s*\)"), "from the loaded configuration");
        }

        // ---- the strings (NFR-4) ----

        [Test]
        public void EveryStringTheCodeAsksFor_IsInBothLanguages()
        {
            L10nTables ru = PressContentFixture.Tables("ru");
            L10nTables en = PressContentFixture.Tables("en");
            var literal = new Regex("\"(press\\.[A-Za-z0-9_]+(?:\\.[A-Za-z0-9_]+)*)\"");

            // The prefixes of the reasons of the history of the reputation (press.award:<…>) are ids the feature writes, not strings of the tables.
            var reasonPrefixes = new HashSet<string>(StringComparer.Ordinal) { "press.award" };

            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> file in AllCode(false))
            {
                foreach (Match match in literal.Matches(file.Value))
                {
                    string key = match.Groups[1].Value;
                    if (reasonPrefixes.Contains(key) || !seen.Add(key)) continue;
                    if (!Has(ru, "ru", key)) missing.Add(file.Key + ": '" + key + "' is not in the ru table");
                    if (!Has(en, "en", key)) missing.Add(file.Key + ": '" + key + "' is not in the en table");
                }
            }

            Assert.True(seen.Count > 40, "the scan should find the keys of the feature, found " + seen.Count);
            Assert.True(missing.Count == 0, "strings the code asks for and the tables lack:\n  " + string.Join("\n  ", missing));
        }

        /// <summary>The key is in the table, or it is the base of a plural group (<c>key.one</c>, <c>key.few</c>, …) that <c>ModL10n.Plural</c> asks for.</summary>
        private static bool Has(L10nTables tables, string language, string key)
        {
            string text;
            if (tables.TryGet(language, key, out text)) return true;

            foreach (string other in tables.Keys(language))
            {
                if (other.StartsWith(key + ".", StringComparison.Ordinal) && (other.EndsWith(".one", StringComparison.Ordinal) || other.EndsWith(".few", StringComparison.Ordinal)
                    || other.EndsWith(".many", StringComparison.Ordinal) || other.EndsWith(".other", StringComparison.Ordinal))) return true;
            }
            return false;
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "not found: " + from);
            int end = text.IndexOf(to, start, StringComparison.Ordinal);
            Assert.True(end > start, "not found after it: " + to);
            return text.Substring(start, end - start);
        }
    }
}
