using System;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The wiring of the software support that cannot run without the game, checked in the source (FR-10, NFR-2, TSK-022, TSK-023): the monthly handler runs
    /// the support before the AI and the reset of the rating cache; the service charges the upkeep once per company into the article
    /// <c>ExpenseOrigin.Development</c> and nowhere else, turns the tiers of the player in default off before it charges, tells him, skips foundries and
    /// bankrupt companies, counts a failure against the feature and goes on; it runs only for a working feature at stage 2; the self-check names what it stands on;
    /// the section of the panel is made hidden, shown by the same rule, and its rows have the list of the tier and the tooltip with the formula. Comments and strings
    /// are blanked where a word could be mistaken for code.
    /// </summary>
    public class SupportWiringTests
    {
        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "CpuArchitectures");
        }

        private static string Code(params string[] relative)
        {
            string path = FeatureDir();
            foreach (string part in relative) path = Path.Combine(path, part);
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string Blanked(params string[] relative)
        {
            return SourceScan.Strip(Code(relative), true);
        }

        /// <summary>The body of the first member with this signature fragment, from its opening brace to the matching one.</summary>
        private static string BodyOf(string code, string signature)
        {
            int at = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(at >= 0, "no '" + signature + "' in the source");

            string blanked = SourceScan.Strip(code, true);
            int open = blanked.IndexOf('{', at);
            Assert.True(open > 0, "no body after '" + signature + "'");
            int depth = 0;
            for (int i = open; i < blanked.Length; i++)
            {
                if (blanked[i] == '{') depth++;
                else if (blanked[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
            }
            Assert.Fail("the body of '" + signature + "' is not closed");
            return null;
        }

        // ---- the monthly handler ----

        [Test]
        public void TheMonthlyHandler_RunsTheSupportBeforeTheAi_AndTheAiBeforeTheRatingReset()
        {
            string feature = Code("ArchitecturesFeature.cs");
            string month = BodyOf(feature, "private void OnMonthPassed()");

            int neutral = month.IndexOf("if (IsNeutralNow) return;", StringComparison.Ordinal);
            int support = month.IndexOf("_support.OnMonth", StringComparison.Ordinal);
            int ai = month.IndexOf("_ai.OnMonth()", StringComparison.Ordinal);
            int reset = month.IndexOf("_rating.InvalidateAll()", StringComparison.Ordinal);

            Assert.True(neutral >= 0 && support > neutral, "the support does not run while the feature is neutral");
            Assert.True(ai > support, "the support first (design.md, 'Подписки на события'), then the AI");
            Assert.True(reset > ai, "the cache of the rating is dropped last");
            Assert.True(Regex.IsMatch(month, @"ModHooks\.Guard\(FeatureId, MonthSite \+ ""/support"", _support\.OnMonth\)"), "on a guard of its own: the AI still decides when the support fails");
            Assert.True(feature.Contains("_support = new SupportService(this);"), "made once in Register");
            Assert.Equal(1, Regex.Matches(feature, @"TickPhase\.MonthPassed").Count, "one monthly handler for the whole feature (design.md)");
        }

        // ---- the service ----

        [Test]
        public void TheService_ChargesOncePerCompany_IntoTheDevelopmentArticle()
        {
            string code = Code("Services", "SupportService.cs");

            Assert.Equal(1, Regex.Matches(code, @"\.SpendMoney\(").Count, "the one place money is taken");
            Assert.True(code.Contains("company.SpendMoney(month.Upkeep, ExpenseOrigin.Development);"), "the sum of the company's families, article Development (FR-10 item 3)");
            Assert.False(code.Contains("EarnMoney"), "the support never pays out");
            Assert.False(Regex.IsMatch(code, @"ExpenseOrigin\.(Research|Royalties|Factories|Taxes|Fines)"), "no other article");
        }

        [Test]
        public void TheService_RunsOnlyForAWorkingFeatureAtStageTwo()
        {
            string code = Code("Services", "SupportService.cs");
            string canRun = BodyOf(code, "public bool CanRun");

            Assert.True(canRun.Contains("!_feature.IsNeutralNow"), "off or fast-forwarding: no money is spent (FR-9)");
            Assert.True(canRun.Contains("_feature.Balance.IsFullStage"), "stage 1: support gives x1 and costs nothing");
            Assert.True(canRun.Contains("ArchParts.Support") && canRun.Contains("ArchParts.Market"), "the parts the support stands on passed the self-check");

            foreach (string entry in new[] { "public void OnMonth()", "public SupportSetResult SetTier(" })
            {
                string body = BodyOf(code, entry);
                Assert.True(body.Contains("CanRun"), entry + " asks CanRun");
            }
        }

        [Test]
        public void TheService_TurnsThePlayersTiersOffBeforeCharging_AndTellsHimOnce()
        {
            string code = Code("Services", "SupportService.cs");
            string step = BodyOf(code, "private bool StepCompany(");

            int isDefault = step.IndexOf("company.IsInDefault", StringComparison.Ordinal);
            int force = step.IndexOf("SupportCycle.ForceOff(state)", StringComparison.Ordinal);
            int run = step.IndexOf("SupportCycle.RunMonth(", StringComparison.Ordinal);
            int spend = step.IndexOf("company.SpendMoney(", StringComparison.Ordinal);

            Assert.True(isDefault >= 0 && force > isDefault, "FR-10 item 6: the default is checked");
            Assert.True(run > force && spend > run, "the tiers are turned off first, then the month is run, then the money is taken");
            Assert.True(step.Contains("state.IsPlayer && company.IsInDefault"), "only the player has a default");
            Assert.True(Regex.IsMatch(step, @"if \(turnedOff > 0\)[\s\S]*?NotificationsEnabled[\s\S]*?ModNotify\.ShowKey\(FeatureId, ""arch\.notify\.defaultTiers""\)"),
                "the notification only when something was turned off, and only if the player wants notifications");
        }

        [Test]
        public void TheService_SkipsFoundriesAndBankruptCompanies_AndOneFailureDoesNotStopTheOthers()
        {
            string code = Code("Services", "SupportService.cs");
            string month = BodyOf(code, "public void OnMonth()");

            Assert.True(Regex.IsMatch(month, @"company\.IsFoundry \|\| company\.IsBankrupt"), "they design no CPUs (edge cases of the spec)");
            Assert.True(Regex.IsMatch(month, @"catch \(Exception ex\)\s*\{[^}]*ModHooks\.ReportFault\(FeatureId, CompanySite, ex\);"), "a failure is counted against the feature");
            Assert.True(month.Contains("StepCompany(company, state, config, today.Year, inflation, out spent)"), "the date comes from ModTicks, not from the system clock");
            Assert.True(month.Contains("ModTicks.Today"));
            Assert.False(code.Contains("DateTime.Now") || code.Contains("DateTime.UtcNow"));
        }

        [Test]
        public void TheService_DropsTheRatingCache_WhenALevelMoved()
        {
            string month = BodyOf(Code("Services", "SupportService.cs"), "public void OnMonth()");
            Assert.True(Regex.IsMatch(month, @"if \(changed && _feature\.Rating != null\) _feature\.Rating\.InvalidateAll\(\);"), "'кеш канала сбрасывается после обновления'");
            Assert.True(month.Contains("_feature.Ui.OnSupportMonth()"), "the open panel shows the new levels at once");
        }

        [Test]
        public void TheService_PatchesNothing_AndSubscribesToNothingOfTheGame()
        {
            string code = Blanked("Services", "SupportService.cs");
            foreach (string forbidden in new[] { "ModPatch", "ctx.Patch", "DateController", "SaveHandler", "Harmony", "OnNewGame" })
            {
                Assert.False(code.Contains(forbidden), "the support service must not contain " + forbidden);
            }
        }

        // ---- the self-check ----

        [Test]
        public void TheSelfCheck_NamesWhatTheSupportStandsOn()
        {
            string check = Code("ArchSelfCheck.cs");
            foreach (string needed in new[]
            {
                "ArchParts.Support, typeof(ICompany), \"SpendMoney\", typeof(float), typeof(ExpenseOrigin)",
                "ArchParts.Support, typeof(ICompany), \"get_IsInDefault\"",
                "ArchParts.Support, typeof(ICompany), \"get_MoneyAmount\"",
                "ArchParts.Support, typeof(GameInflation), \"get_Inflation\"",
                "ArchParts.LabUi, ArchParts.Support }",
            })
            {
                Assert.True(check.Contains(needed), "the self-check lacks: " + needed);
            }
        }

        // ---- the panel ----

        [Test]
        public void TheSection_IsMadeHidden_ShownByTheModelsRule_AndSitsAfterTheCards()
        {
            string section = Code("UI", "SupportSection.cs");
            Assert.True(section.Contains("_box.gameObject.SetActive(false);"), "stage 1 never shows it");
            Assert.True(section.Contains("SupportSectionModel.IsShown(_feature.Balance, service.CanRun)"), "the same rule as the service");

            string panel = Code("UI", "ArchitecturesPanel.cs");
            int cards = panel.IndexOf("_cards.Add(card);", StringComparison.Ordinal);
            int made = panel.IndexOf("new SupportSection(_feature, _ui)", StringComparison.Ordinal);
            Assert.True(cards >= 0 && made > cards, "the section follows the cards");
            Assert.True(panel.Contains("_support.Refresh(player)"), "and is redrawn with them");
        }

        [Test]
        public void TheRow_HasTheTierListAndTheTooltipWithTheFormula()
        {
            string row = Code("UI", "SupportRow.cs");
            Assert.True(row.Contains("ModUI.Dropdown("), "FR-10 item 7: the list of the tier");
            Assert.True(row.Contains("ThemedBar.Create("), "the bar of the level, 0-100");
            Assert.True(row.Contains("ModUI.Tooltip(_root.gameObject, TooltipOf)"), "NFR-6: the formula in numbers");
            foreach (string key in new[] { "arch.support.tip.rating", "arch.support.tip.tier", "arch.support.tip.off", "arch.support.tip.cost" })
            {
                Assert.True(row.Contains("\"" + key + "\""), "the tooltip writes " + key);
            }
            Assert.True(row.Contains("SupportMath.ForecastMonths") || row.Contains("info.Forecast"), "the forecast of the row comes from the model");
        }

        [Test]
        public void TheTierPick_GoesThroughTheService_NotIntoTheStateDirectly()
        {
            string ui = Code("UI", "ArchitecturesUi.cs");
            string pick = BodyOf(ui, "public void SetSupportTier(");
            Assert.True(pick.Contains("support.SetTier(player, familyId, tier)"), "the rules (default, researched) live in one place");
            Assert.True(pick.Contains("RefreshVisible()"), "the new cost is shown at once, a refused change snaps the list back");

            string row = Code("UI", "SupportRow.cs");
            Assert.True(row.Contains("_ui.SetSupportTier(_family.Id, SupportCycle.Tiers[index])"));
            Assert.False(Regex.IsMatch(row, @"\.Tier\s*=(?!=)"), "the row never writes the tier itself");
        }

        // ---- the rating ----

        [Test]
        public void TheProvider_ReadsTheSupportOfTheOwnerOfTheCpu_AtStageTwoOnly()
        {
            string code = Code("Services", "ArchRatingProvider.cs");
            string contribution = BodyOf(code, "public float ContributionOf(");

            Assert.True(contribution.Contains("if (balance.IsFullStage)"), "stage 1 does not read the support");
            Assert.True(contribution.Contains("ICompany owner = cpu.Company;"), "the owner of the CPU, not the factory that makes it");
            Assert.True(contribution.Contains("familyState.Support"), "the level of the owner for the family of the CPU");
            Assert.True(contribution.Contains("RatingForecast.One("), "one function for the market, the designer and the compare row");
        }
    }
}
