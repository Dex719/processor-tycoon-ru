using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// How the intelligence window and the negotiation are wired to the game and the core (TSK-017, TSK-018, TSK-029). The classes that touch the game cannot run
    /// in the unit tests, so these facts are read from the source of the feature, comments aside: the window is made from a template of the game by
    /// <c>ModUI</c>, it opens from the page of the hub and from the action of the mails, it follows the day, the theme, the language and the two parameters, it
    /// reads nothing about the money and the projects of the rivals, and the page keeps the debug section in the debug build only.
    /// </summary>
    public class IntelWiringTests
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

        // ---- the window ----

        [Test]
        public void TheWindow_IsACloneOfThePanelWindowOfTheGame_MadeByModUi()
        {
            string code = Code("UI", "IntelWindow.cs");

            Assert.True(Regex.IsMatch(code, @"ModUI\.CreateWindow\(WindowId,\s*WindowTemplate\.Panel,\s*""rival\.window\.title"",\s*WindowSize\)"),
                "a panel window (the time goes on) with the title of the design");
            Assert.True(Regex.IsMatch(code, @"WindowId\s*=\s*RivalPersonalitiesFeature\.FeatureId\s*\+\s*"".intel"""), "the id carries the id of the feature");
            Assert.True(Regex.IsMatch(code, @"new Vector2\(900f,\s*560f\)"), "the size of the design");
            Assert.True(code.Contains("ModUI.IsReady"), "no window before the templates of the scene are looked up");
            Assert.False(Regex.IsMatch(code, @"WindowTemplate\.Modal"), "the window does not stop the time");
        }

        [Test]
        public void TheWindow_ShowsAListAndTheDetails_WithTheWidgetsOfModUi()
        {
            string code = Code("UI", "IntelWindow.cs");
            string row = Code("UI", "IntelRowView.cs");

            Assert.True(code.Contains("ModUI.Info(") && code.Contains("ModUI.Text("), "the details are pairs and texts");
            Assert.True(code.Contains("ModUI.Tooltip("), "FR-8 item 4: the tip on the number");
            Assert.True(row.Contains("ModUI.Button(") && row.Contains("ModUI.VerticalList(") && row.Contains("ModUI.Text("), "a row: a button and a line under it");
            Assert.True(Regex.IsMatch(row, @"selected \? ButtonStyle\.CallToAction : ButtonStyle\.Standard"), "the chosen company is drawn with the main style");
            Assert.True(code.Contains("IntelBuilder.Compare"), "the bankrupt are at the bottom");
        }

        [Test]
        public void TheWindow_IsMadeAgainWhenTheSceneChanged_AndEveryEntryIsGuarded()
        {
            string code = Code("UI", "IntelWindow.cs");

            Assert.True(code.Contains("_window.IsAlive"), "a window of a scene that was left is made again");
            Assert.True(Regex.IsMatch(code, @"ModHooks\.Guard\(Feature,\s*""IntelWindow/Open"""), "opening is guarded");
            Assert.True(Regex.IsMatch(code, @"ModHooks\.Guard\(Feature,\s*""IntelWindow/Refresh"""), "refreshing is guarded");
            Assert.True(Regex.IsMatch(code, @"if \(!IsOpen\) return;"), "a closed window is not rebuilt");
            Assert.True(Regex.IsMatch(code, @"ModFeatures\.IsActive\(Feature\)"), "the entry does nothing while the feature is off");
        }

        [Test]
        public void TheWindow_FollowsTheFogAndTheHiddenCharacters_AtOnce()
        {
            string code = Code("UI", "IntelWindow.cs");

            Assert.True(Regex.IsMatch(code, @"Fog\s*=\s*RivalSettings\.IntelFog"), "the parameter intelFog is read at every rebuild");
            Assert.True(Regex.IsMatch(code, @"HiddenMode\s*=\s*RivalSettings\.HiddenCharacters"), "and hiddenCharacters too");
            Assert.True(Regex.IsMatch(code, @"RivalParams\.IntelFog") && Regex.IsMatch(code, @"RivalParams\.HiddenCharacters"), "a change of either refreshes the open window");
            Assert.True(Regex.IsMatch(code, @"string\.Equals\(featureId,\s*Feature,\s*StringComparison\.Ordinal\)"), "the parameters of other features do not");
        }

        [Test]
        public void TheFeature_RefreshesTheWindow_OnTheDay_TheTheme_TheLanguage_AndTheSettings()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(feature, @"ModHooks\.Guard\(FeatureId,\s*""DayPassed\.intel"",\s*_intel\.OnDay\)"), "once a game day");
            Assert.True(feature.Contains("ModUI.ThemeChanged += OnThemeChanged;"), "FR-9 item 5: the theme");
            Assert.True(feature.Contains("ModL10n.LanguageChanged += OnLanguageChanged;"));
            Assert.True(feature.Contains("ModSettings.Changed += OnSettingChanged;"), "FR-16 item 3: the parameters");
            Assert.True(Regex.IsMatch(feature, @"_intel\.OnThemeChanged\)") && Regex.IsMatch(feature, @"_intel\.OnLanguageChanged\)"), "guarded");
            Assert.True(Regex.IsMatch(feature, @"_intel\.OnSettingChanged\(featureId,\s*key\)"));
            Assert.True(Regex.IsMatch(feature, @"""feature/disabled\.window"",\s*_intel\.Close"), "switching the feature off closes the window");
        }

        [Test]
        public void TheWindow_RefreshesNoMoreOftenThanOnceADay_ByItself()
        {
            // the only periodic caller is the tick of the day; the window has no Update, no timer and no coroutine of its own
            string window = Code("UI", "IntelWindow.cs");
            Assert.False(Regex.IsMatch(window, @"\bUpdate\(|FixedUpdate\(|Coroutine|StartCoroutine|InvokeRepeating|PTModHost"), "nothing in the window runs by itself");

            string feature = Code("RivalPersonalitiesFeature.cs");
            Assert.Equal(1, Regex.Matches(feature, @"_intel\.OnDay").Count, "one place calls the daily refresh");
        }

        // ---- the entries ----

        [Test]
        public void TheHubPage_HasTheButtonRivals_AndTheDebugSectionInTheDebugBuildOnly()
        {
            string raw = SourceScan.ReadText(Path.Combine(FeatureFolder(), "UI", "IntelHubPage.cs"));
            string code = Code("UI", "IntelHubPage.cs");

            Assert.True(Regex.IsMatch(code, @"ModUI\.Button\(page,\s*ModL10n\.L\(""rival\.hub\.button""\),\s*OpenWindow,\s*ButtonStyle\.CallToAction\)"), "FR-9 item 1: the button Rivals");
            Assert.True(Regex.IsMatch(code, @"ModHub\.Close\(\);[^;]*\r?\n\s*IntelWindow\.Open\(\);"), "the hub is modal: it is closed, then the window opens");
            Assert.True(Regex.IsMatch(raw, @"#if PTMOD_DEBUG\s*\r?\n\s*DebugReactionPage\.Build\(page\);\s*\r?\n#endif"), "the debug section sits in the page, in the debug build only");

            string feature = Code("RivalPersonalitiesFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"ctx\.HubPage\(IntelHubPage\.TitleKey,\s*IntelHubPage\.Build\);"), "the page is registered with ctx.HubPage");
            Assert.False(Regex.IsMatch(SourceScan.ReadText(Path.Combine(FeatureFolder(), "RivalPersonalitiesFeature.cs")), @"#if PTMOD_DEBUG\s*\r?\n\s*ctx\.HubPage"),
                "the page is there in the release build too");
            Assert.Equal(1, Regex.Matches(feature, @"ctx\.HubPage\(").Count, "one page per feature");
        }

        [Test]
        public void TheMailAction_OpensTheWindow_OnTheCompanyOfTheMail()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");
            Assert.True(feature.Contains("IntelMail.OpenIntel = IntelWindow.Open;"), "the action 'Open intel' opens the window");
            Assert.True(Regex.IsMatch(feature, @"ModMail\.RegisterActionHandler\(FeatureId,\s*IntelMail\.OnAction\)"));

            string mail = Code("Reactions", "IntelMail.cs");
            Assert.True(Regex.IsMatch(mail, @"open\(record != null \? UniqueIdOf\(record\.Tag\) : null\)"), "the company comes from the tag of the mail");
            Assert.True(Regex.IsMatch(mail, @"Repeatable\s*=\s*true"), "the mail stays open and the action can be used again");
            Assert.False(Regex.IsMatch(mail, @"Close\("), "the mail is not closed by the action");
        }

        // ---- what the panel must not show ----

        [Test]
        public void ThePanel_ReadsNeitherTheMoneyNorTheProjectsOfARival()
        {
            var forbidden = new Regex(@"\b(Money|Balance|Cash|Debt|Loan|Income|Revenue|Profit|Funding|Budget|Project|Projects|SpendMoney|EarnMoney|Expense\w*|GetMaxFunding)\b");
            var problems = new List<string>();
            string src = SourceScan.SrcDirOrFail();
            foreach (string file in new[] { "IntelWindow.cs", "IntelRowView.cs", "IntelSource.cs", "IntelUi.cs", "IntelHubPage.cs" })
            {
                string code = Code("UI", file);
                foreach (Match match in forbidden.Matches(code)) problems.Add("UI/" + file + ": " + match.Value);
            }
            foreach (string file in new[] { "IntelBuilder.cs", "IntelModels.cs", "ThreatMath.cs" })
            {
                string code = Code("Logic", "Intel", file);
                foreach (Match match in forbidden.Matches(code)) problems.Add("Logic/Intel/" + file + ": " + match.Value);
            }
            Assert.Equal(0, problems.Count, "FR-9 item 6:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void ThePanel_OnlyReads_NeverChangesTheAttitudeOrTheReactions()
        {
            var writes = new Regex(@"\.Change\(|\.Reveal\(|\.AddPending\(|\.RemovePending\(|\.BeginActive\(|\.RemoveActive\(|\.AddHistory\(|\.MarkExecuted\(|\.Plan\w*\(|\.Apply\w*\(");
            foreach (string file in new[] { "IntelWindow.cs", "IntelRowView.cs", "IntelSource.cs" })
            {
                string code = Code("UI", file);
                Assert.False(writes.IsMatch(code), "UI/" + file + " writes: " + writes.Match(code).Value);
            }

            // the builder only reads the books it is given
            string builder = Code("Logic", "Intel", "IntelBuilder.cs");
            Assert.False(writes.IsMatch(builder), "IntelBuilder writes: " + writes.Match(builder).Value);
        }

        [Test]
        public void TheSource_IsReadOnly_AndTakesNoMoneyFromTheGame()
        {
            string code = Code("UI", "IntelSource.cs");
            Assert.True(code.Contains("MarketShare("), "FR-9 item 2: the share is the game's MarketShare");
            Assert.True(code.Contains("ModReputation.Get(") && code.Contains("press-reviews"), "FR-9 item 4: the reputation only while press-reviews is on");
            Assert.True(code.Contains("GetAllCpus("), "one walk over the CPUs");
            Assert.True(Regex.IsMatch(code, @"ModHooks\.ReportFault\("), "a failure of the walk is counted and the other facts stay");
        }

        [Test]
        public void ThePatchesAndTheWindow_NeverTouchTheAiBehaviour()
        {
            // TSK-017 only adds to two score functions; it must not become a second place that changes the AI
            string patches = Code("Negotiation", "NegotiationPatches.cs");
            Assert.False(Regex.IsMatch(patches, @"__instance|\.Change\(|ModModifiers|PriceBook|PersonalityRegistry"), "the negotiation patches only add the bonus");
        }
    }
}
