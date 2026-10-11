using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-013, TSK-014, TSK-015, TSK-016, TSK-017, TSK-018: the seams of the code that talks to the game and cannot be loaded here, checked on the source. The
    /// notifier shows nothing in the fast-forward, while loading, with the switch off or the feature off, and sends one letter (tag, priority Normal, never
    /// twice) and one notification for an announcement and for an early adopter, one notification for a launch and for a penalty; the warning of the release
    /// window is the only patch of the feature, a postfix on <c>ProjectReleaseWindow.Open</c> that only reads (<c>Preview</c>); the events are raised through
    /// <c>ModHooks.Raise</c> by the handlers and by nothing else; the predicates are registered with the core under the feature's own id; the window is made from
    /// the game's panel, filled under the guard, and opened from the page of the feature in the hub; the insights break a multiplier into its parts
    /// and the feature has no <c>Explain</c> of its own.
    /// </summary>
    public class PlatformPresentationTests
    {
        private static string FeatureDirectory()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "PlatformGenerations");
        }

        private static List<string> SourceFiles()
        {
            List<string> files = SourceScan.Files(FeatureDirectory(), ".cs");
            Assert.True(files.Count > 10, "the sources of the feature are missing");
            return files;
        }

        private static string Code(string file)
        {
            string path = Path.Combine(FeatureDirectory(), file);
            Assert.True(File.Exists(path), "missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string BodyOf(string file, string signature)
        {
            string path = Path.Combine(FeatureDirectory(), file);
            Assert.True(File.Exists(path), "missing: " + path);
            string raw = SourceScan.ReadText(path);
            string blanked = SourceScan.Strip(raw, true);
            string withStrings = SourceScan.Strip(raw, false);

            int at = blanked.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, file + " has no " + signature);
            int open = blanked.IndexOf('{', at);
            Assert.True(open >= 0, "no body for " + signature);

            int depth = 0;
            for (int i = open; i < blanked.Length; i++)
            {
                if (blanked[i] == '{') depth++;
                else if (blanked[i] == '}' && --depth == 0) return withStrings.Substring(open, i - open + 1);
            }
            Assert.Fail("the body of " + signature + " does not end");
            return null;
        }

        private static string Squeeze(string text)
        {
            return Regex.Replace(text, @"\s+", " ");
        }

        private static int Count(string text, string part)
        {
            int count = 0;
            for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        // ---- the notifier (TSK-013) ----

        [Test]
        public void TheNotifier_ShowsNothing_InTheFastForward_WhileLoading_WithTheSwitchOff_OrTheFeatureOff()
        {
            string allowed = Squeeze(BodyOf("PlatformNotifier.cs", "private static bool IsAllowed()"));

            foreach (string condition in new[] { "ModFeatures.IsActive(Feature)", "PlatformSettings.Notifications", "!ModTicks.IsFastForwarding", "!ModTicks.IsLoading" })
            {
                Assert.True(allowed.Contains(condition), "IsAllowed lacks " + condition);
            }

            string code = Code("PlatformNotifier.cs");
            foreach (string method in new[] { "Announced(GenerationDef generation)", "Launched(GenerationDef generation, string byCompanyKey, bool player)", "EarlyAdopter(GenerationDef generation, int slot)", "Churn(SocketId socket, float penalty, int months, string mainSocketKey, int mainAgeMonths)" })
            {
                string body = Squeeze(BodyOf("PlatformNotifier.cs", "public void " + method));
                Assert.True(body.Contains("!IsAllowed()) return;"), method + " leaves at once when nothing may be shown");
                Assert.True(body.Contains("ModHooks.Guard(Feature,"), method + " keeps a failure inside");
            }
            Assert.Equal(4, Count(code, "ModHooks.Guard(Feature,"), "one guard per kind of message");
        }

        [Test]
        public void TheAnnouncementAndTheEarlyAdopter_GetOneLetterAndOneNotification_TheLaunchAndThePenaltyOnlyANotification()
        {
            string announced = BodyOf("PlatformNotifier.cs", "public void Announced(GenerationDef generation)");
            string early = BodyOf("PlatformNotifier.cs", "public void EarlyAdopter(GenerationDef generation, int slot)");
            string launched = BodyOf("PlatformNotifier.cs", "public void Launched(GenerationDef generation, string byCompanyKey, bool player)");
            string churn = BodyOf("PlatformNotifier.cs", "public void Churn(SocketId socket, float penalty, int months, string mainSocketKey, int mainAgeMonths)");

            Assert.Equal(1, Count(announced, "Send(PlatformNotices.AnnounceMail("));
            Assert.Equal(1, Count(announced, "Show(PlatformNotices.Announced("));
            Assert.Equal(1, Count(early, "Send(PlatformNotices.EarlyMail("));
            Assert.Equal(1, Count(early, "Show(PlatformNotices.EarlyAdopter("));
            Assert.Equal(0, Count(launched, "Send("), "a launch is a notification");
            Assert.Equal(1, Count(launched, "Show(PlatformNotices.Launched("));
            Assert.Equal(0, Count(churn, "Send("), "a penalty is a notification");
            Assert.Equal(1, Count(churn, "Show(PlatformNotices.Churn("));
        }

        [Test]
        public void TheLetters_HaveTheirTag_TheNormalPriority_AndAreNotSentTwice()
        {
            string send = Squeeze(BodyOf("PlatformNotifier.cs", "private static void Send(MailPlan plan)"));

            Assert.True(send.Contains("if (ModMail.Find(Feature, plan.Tag) != null) return;"), "a letter with this tag is in the mail already");
            Assert.True(send.Contains("Tag = plan.Tag"));
            Assert.True(send.Contains("Priority = MailPriority.Normal"));
            Assert.True(send.Contains("FeatureId = Feature"));
            Assert.False(send.Contains("Choices") || send.Contains("Actions") || send.Contains("TimeoutDays"), "a letter to read, with nothing to answer");

            string show = Squeeze(BodyOf("PlatformNotifier.cs", "private static void Show(Notice notice)"));
            Assert.True(show.Contains("ModNotify.ShowKey(Feature, notice.Key, notice.Args);"));
        }

        [Test]
        public void ModNotify_AndModMail_AreUsedOnlyByTheNotifierAndTheReleaseWarning()
        {
            foreach (string file in SourceFiles())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                string name = Path.GetFileName(file);
                bool allowed = name == "PlatformNotifier.cs" || name == "ReleaseWarning.cs";
                if (!allowed)
                {
                    Assert.False(code.Contains("ModNotify.") || code.Contains("ModMail."), name + " shows something itself instead of telling the notifier");
                }
            }
        }

        [Test]
        public void TheFeature_StartsWithThePlatformNotifier_AndFallsBackToNothingNotToNull()
        {
            string feature = Squeeze(Code("PlatformGenerationsFeature.cs"));

            Assert.True(feature.Contains("_notifier = new PlatformNotifier(this);"), "the real notifier is the default");
            Assert.True(feature.Contains("set { _notifier = value ?? NullNotifier.Instance; }"));
        }

        [Test]
        public void TheReleaseHandler_ToldThePlayerOnlyOfHisOwnEarlyAdopterSlotAndPenalty_AndTheEventsOfEveryCompany()
        {
            string tell = Squeeze(BodyOf("PlatformReleaseHandlers.cs", "private static void Tell("));

            Assert.True(tell.Contains("if (player) notifier.EarlyAdopter(gen, outcome.EarlySlot);"));
            Assert.True(tell.Contains("notifier.Launched(gen, companyKey, player);"), "a launch is told for any company (FR-3 item 4)");
            Assert.True(tell.Contains("if (player) { PlatformBalance.RatingBalance rating = feature.Balance.Rating; notifier.Churn(socket, rating.ChurnPenalty * outcome.ChurnSeverity, rating.ChurnDurationMonths, outcome.MainSocketKey, outcome.MainAgeMonths); }")
                          || tell.Contains("notifier.Churn(socket, rating.ChurnPenalty * outcome.ChurnSeverity, rating.ChurnDurationMonths, outcome.MainSocketKey, outcome.MainAgeMonths);"));
            foreach (string raise in new[] { "RaiseGenerationAnnounced(gen.id)", "RaiseGenerationLaunched(gen.id)", "RaiseEarlyAdopterGranted(companyKey, gen.id)", "RaiseSocketChurnApplied(companyKey, socket.Key, outcome.ChurnSeverity)" })
            {
                Assert.Equal(1, Count(tell, "PlatformEvents." + raise), raise + " once");
            }
        }

        // ---- the release warning (TSK-014) ----

        [Test]
        public void TheReleaseWarning_IsTheOnlyPatchOfTheFeature_APostfixOnOpenThatIsOptional()
        {
            int patches = 0;
            foreach (string file in SourceFiles())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), false);
                foreach (Match attribute in Regex.Matches(code, @"\[ModPatch\([^\]]*\]"))
                {
                    patches++;
                    Assert.Equal("[ModPatch(typeof(GameReleaseWindow), \"Open\", PatchKind.Postfix, Optional = true)]", attribute.Value);
                    Assert.Equal("ReleaseWarning.cs", Path.GetFileName(file));
                }
            }
            Assert.Equal(1, patches, "one patch: the rest comes from the core's events and channels");

            string code2 = Code(Path.Combine("Patches", "ReleaseWarning.cs"));
            Assert.True(code2.Contains("using GameReleaseWindow = ProcessorTycoon.ProjectSystem.ProjectReleaseWindow;"));
            Assert.True(code2.Contains("internal static void OpenPostfix(GameCpu cpu)"), "the parameter is named like the game's: Open(Cpu cpu)");

            string register = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public void Register(FeatureContext ctx)"));
            Assert.True(register.Contains("ctx.Patch(typeof(ReleaseWarning));"));
        }

        [Test]
        public void TheReleaseWarning_OnlyReads_TheStateIsTheSameBeforeAndAfter()
        {
            string path = Path.Combine("Patches", "ReleaseWarning.cs");
            string warn = Squeeze(BodyOf(path, "internal static int Warn(PlatformGenerationsFeature feature, GameCpu cpu)"));

            Assert.True(warn.Contains("feature.Timeline.Preview("), "the answer is the preview of the release");
            foreach (string writes in new[] { ".OnCpuReleased(", ".Announce(", ".SetLaunched(", ".AddEarlyAdopter(", ".AddSocketIntro(", "RebuildCache", "UpdateCpu", "Preferences.Assign", "InstalledBase.RebuildFull", "InstalledBase.ApplyMonth" })
            {
                Assert.False(warn.Contains(writes), "the warning must not write: " + writes);
            }
            Assert.True(warn.Contains("!PlatformSettings.Notifications) return 0;"), "the switch of notifications covers the warning");
            Assert.True(warn.Contains("if (GameAdapter.IsVariant(cpu)) return 0;"), "a variant changes nothing");

            string postfix = Squeeze(BodyOf(path, "internal static void OpenPostfix(GameCpu cpu)"));
            Assert.True(postfix.Contains("if (ModTicks.IsFastForwarding) return;"));
            Assert.True(postfix.Contains("!ModFeatures.IsActive(id) || !ModHooks.CanRun(id, Site)"));
            Assert.True(postfix.Contains("catch (Exception e) { ModHooks.ReportFault(id, Site, e); }"), "a failure never reaches the game");
        }

        // ---- the events (TSK-018) ----

        [Test]
        public void TheEvents_AreRaisedThroughModHooksRaise_NotInTheFastForwardOrWhileLoading()
        {
            string code = Code("PlatformEvents.cs");

            Assert.True(Regex.IsMatch(code, @"public static event Action<string> GenerationAnnounced;"));
            Assert.True(Regex.IsMatch(code, @"public static event Action<string> GenerationLaunched;"));
            Assert.True(Regex.IsMatch(code, @"public static event Action<string, string> EarlyAdopterGranted;"));
            Assert.True(Regex.IsMatch(code, @"public static event Action<string, string, float> SocketChurnApplied;"));

            Assert.Equal(4, Count(code, "ModHooks.Raise("), "every event goes through the core's isolation");
            Assert.Equal(4, Count(code, "if (Quiet()) return;"));
            string quiet = Squeeze(BodyOf("PlatformEvents.cs", "private static bool Quiet()"));
            Assert.True(quiet.Contains("ModTicks.IsFastForwarding || ModTicks.IsLoading"));
        }

        [Test]
        public void TheEvents_AreRaisedByTheHandlersOfTheReleaseAndTheYear_AndByNothingElse()
        {
            foreach (string file in SourceFiles())
            {
                string name = Path.GetFileName(file);
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                bool mayRaise = name == "PlatformReleaseHandlers.cs" || name == "PlatformSubscriptions.cs" || name == "PlatformEvents.cs";
                if (!mayRaise) Assert.False(code.Contains("PlatformEvents.Raise"), name + " raises an event");
            }

            string year = Squeeze(BodyOf("PlatformSubscriptions.cs", "private void OnYearPassed()"));
            Assert.Equal(1, Count(year, "PlatformEvents.RaiseGenerationAnnounced(gen.id);"));

            // The resynchronization (the end of the fast-forward, a loaded save, the feature switched on) writes the chronology and says nothing.
            string resync = Squeeze(BodyOf("PlatformSubscriptions.cs", "public void Resync(bool reconstruct, string why)"));
            Assert.False(resync.Contains("PlatformEvents"));
        }

        // ---- the predicates (TSK-018) ----

        [Test]
        public void ThePredicates_AreRegisteredOnceFromRegister_UnderTheIdOfTheFeature()
        {
            string register = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public void Register(FeatureContext ctx)"));
            Assert.True(register.Contains("PlatformConditions.Register();"));

            string conditions = Squeeze(Code("PlatformConditions.cs"));
            Assert.True(conditions.Contains("if (_registered) return 0;"));
            Assert.True(conditions.Contains("ModConditions.Register(pair.Key, PlatformGenerationsFeature.FeatureId, pair.Value)"));
            Assert.True(conditions.Contains("PlatformPredicates.CreateFactories(Facts)"));
            Assert.Equal(1, Count(conditions, "ModConditions.Register("), "nothing else is registered");
        }

        [Test]
        public void TheFacts_AreReadFromTheChronologyAndTheBase_NotFromTheGame()
        {
            string conditions = Code("PlatformConditions.cs");

            Assert.True(conditions.Contains("feature.Timeline.IsLaunched(genId, out date, out by)"));
            Assert.True(conditions.Contains("feature.Timeline.IsEarlyAdopter(companyKey, genId, out since)"));
            Assert.True(conditions.Contains("feature.InstalledBase.BestSocketShareOf(companyKey)"));
        }

        // ---- the insights (TSK-017) ----

        [Test]
        public void TheInsights_BreakTheMultiplierIntoParts_WithTheEngineOfTheProvider_AndTheFeatureHasNoExplainOfItsOwn()
        {
            string insights = Squeeze(Code("PlatformInsights.cs"));

            Assert.True(insights.Contains("public static class PlatformInsights"));
            Assert.True(insights.Contains("public static RatingBreakdown RatingOf(GameCpu cpu)"));
            Assert.True(insights.Contains("public static OemView OemOf(GameCpu cpu)"));
            Assert.True(insights.Contains("public static GenerationDef GenerationOf(GameCpu cpu)"));
            Assert.Equal(2, Count(insights, "Engine.RatingFor(company, socket, gen.id, feature.InstalledBase, feature.Timeline, GameAdapter.Today(), feature.Balance)"),
                "the arguments of the cache of the provider");
            Assert.True(insights.Contains("!ModFeatures.IsActive(PlatformGenerationsFeature.FeatureId)"), "a feature that is off gives the neutral answer");
            Assert.False(insights.Contains("Provider."), "the parts are built on request, never through the cache of the market tick");

            foreach (string file in SourceFiles())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                Assert.False(Regex.IsMatch(code, @"\b(static|public|internal)\b[^;{(=]*\bExplain\s*\("), Path.GetFileName(file) + " declares an Explain of its own");
                Assert.False(code.Contains("ModModifiers.Explain("), Path.GetFileName(file) + " explains the multipliers itself; the core does");
            }
        }

        [Test]
        public void TheProviders_GiveTheCoreTheReasonsOfFR10()
        {
            string provider = Squeeze(Code("PlatformModifierProvider.cs"));

            Assert.True(provider.Contains("RatingReasonKey = \"pg.reason.rating\""));
            Assert.True(provider.Contains("ModModifiers.RegisterProvider(Feature, ModChannels.MarketRatingCpu, Rating, RatingReasonKey)"));
            Assert.True(provider.Contains("ModModifiers.RegisterReasonedProvider(Feature, ModChannels.ContractSignChance, Chance)"));
            Assert.True(provider.Contains("return _reasons.Of(result);"), "the arguments of the reason are the prepared ones");
        }

        // ---- the window and the hub (TSK-015, TSK-016) ----

        [Test]
        public void TheWindow_IsMadeFromThePanelOfTheGame_HasTheThreeTabs_AndBuildsEachUnderTheGuard()
        {
            string window = Squeeze(Code(Path.Combine("UI", "PlatformBrowserWindow.cs")));

            Assert.True(window.Contains("ModUI.CreateWindow(WindowId, WindowTemplate.Panel, \"pg.hub.title\", new Vector2(WindowWidth, WindowHeight))"));
            foreach (string key in new[] { "pg.tab.generations", "pg.tab.installedBase", "pg.tab.myCpus" })
            {
                Assert.True(window.Contains("\"" + key + "\""), key);
            }
            Assert.True(window.Contains("GenerationsTab.Build(body, feature)"));
            Assert.True(window.Contains("InstalledBaseTab.Build(body, feature, Refresh)"));
            Assert.True(window.Contains("MyCpusTab.Build(body, feature)"));
            Assert.True(window.Contains("ModHooks.Guard(Feature, \"ui/tab/\" + tab,"), "a failing tab leaves a line, not an exception");
            Assert.True(window.Contains("if (!ModFeatures.IsActive(Feature)) return;"), "the window does not open for a feature that is off");
            Assert.True(window.Contains("ModUI.Button(_tabBar, caption, () => Select(tab), current ? ButtonStyle.CallToAction : ButtonStyle.Standard)"));
            Assert.True(window.Contains("PlatformUi.Defer("), "a widget is not destroyed inside its own click");
        }

        [Test]
        public void TheFeature_OpensTheWindowFromItsPageInTheHub_AndClosesItWhenSwitchedOff()
        {
            string feature = Code("PlatformGenerationsFeature.cs");
            string register = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public void Register(FeatureContext ctx)"));
            Assert.True(register.Contains("ctx.HubPage(\"pg.hub.title\", BuildHubPage);"));

            string page = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "private void BuildHubPage(RectTransform page)"));
            Assert.True(page.Contains("ModUI.Button(page, ModL10n.L(\"pg.hub.open\"), OpenWindowFromHub, ButtonStyle.CallToAction"));

            string open = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "private void OpenWindowFromHub()"));
            Assert.True(open.Contains("ModHooks.Guard(FeatureId,"));
            int close = open.IndexOf("ModHub.Close();", StringComparison.Ordinal);
            int show = open.IndexOf("PlatformBrowserWindow.Open();", StringComparison.Ordinal);
            Assert.True(close >= 0 && show > close, "the hub holds the time: it is closed before the window opens");

            string disabled = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public void OnDisabled()"));
            Assert.True(disabled.Contains("PlatformBrowserWindow.Close();"));
            Assert.True(feature.Contains("using UnityEngine;"));
        }

        [Test]
        public void TheTabs_ShowWhatTheViewsGive_AndTheSocketGetsTheTooltipOfTheRisk()
        {
            string generations = Squeeze(Code(Path.Combine("UI", "GenerationsTab.cs")));
            Assert.True(generations.Contains("GenerationsView.Build(snapshot.Catalog, snapshot.Timeline, snapshot.InstalledBase, snapshot.Today, text)"));
            Assert.True(generations.Contains("PlatformUi.Bar(row, card.Adoption,"), "the adoption is a bar and a percent");
            Assert.True(generations.Contains("card.AdoptionText"));
            Assert.True(generations.Contains("\"pg.window.empty\""), "no generations: the window says so");

            string ib = Squeeze(Code(Path.Combine("UI", "InstalledBaseTab.cs")));
            Assert.True(ib.Contains("InstalledBaseView.Build(snapshot.InstalledBase, snapshot.Timeline, snapshot.Today, ModCompanies.PlayerKey,"));
            Assert.True(ib.Contains("ModUI.Dropdown(body, ModL10n.L(\"pg.ib.filter\"), options,"), "the filter mine / all");
            Assert.True(ib.Contains("\"pg.ib.filter.mine\"") && ib.Contains("\"pg.ib.filter.all\""));

            string cpus = Squeeze(Code(Path.Combine("UI", "MyCpusTab.cs")));
            Assert.True(cpus.Contains("MyCpusView.Build(cpus, ModCompanies.PlayerKey, snapshot, contracts, text)"));
            Assert.True(cpus.Contains("ModUI.Tooltip(platform.gameObject, () =>"), "the tooltip is made when the pointer comes, from the state of that moment");
            Assert.True(cpus.Contains("SocketRiskView.Build(ModCompanies.PlayerKey, socketKey, PlatformWindowData.Snapshot(feature), text)"));
            Assert.True(cpus.Contains("platform.raycastTarget = true;"), "the tooltip needs something under the pointer");
        }

        [Test]
        public void TheLogic_DoesNotKnowTheUiOrTheServicesThatShowThings()
        {
            foreach (string file in SourceFiles())
            {
                string normal = file.Replace('\\', '/');
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                if (normal.Contains("/Logic/"))
                {
                    Assert.False(Regex.IsMatch(code, @"\bModUI\b|\bModNotify\b|\bModMail\b|\bModL10n\b|\bPlatformUi\b"), normal + " is logic and names the UI or the services of the game");
                }
            }
        }
    }
}
