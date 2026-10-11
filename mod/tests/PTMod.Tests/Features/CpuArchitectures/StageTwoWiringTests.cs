using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.CpuArchitectures
{
    /// <summary>
    /// The wiring of stage 2 that cannot run without the game, checked in the source (TSK-024 ... TSK-028, TSK-035; NFR-2): the monthly handler runs the ecosystem, the support, the
    /// royalty and the AI in this order, each on a guard of its own; the ecosystem reads the series of the CPUs and nothing else; the royalty takes money once per payer into the
    /// article Royalties and pays once per holder into the revenue Royalties, never during the fast-forward and never when the licence is revoked; the AI reads the segment of a CPU
    /// from the classifier of the core and has no thresholds of its own; the letters carry the tag of the milestone and the arguments their text asks for; the designer, the
    /// rating and the tooltips of the technologies use the shared functions. Comments and strings are blanked where a word could be mistaken for code.
    /// </summary>
    public class StageTwoWiringTests
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

        private static int Placeholders(string language, string key)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "L10n", language, "cpu-architectures.json");
            string text = SourceScan.ReadText(path);
            Match line = Regex.Match(text, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            Assert.True(line.Success, language + " lacks " + key);
            int needed = 0;
            foreach (Match match in Regex.Matches(line.Groups[1].Value, @"\{(\d+)"))
            {
                needed = Math.Max(needed, int.Parse(match.Groups[1].Value) + 1);
            }
            return needed;
        }

        // ---- the monthly handler ----

        [Test]
        public void TheMonthlyHandler_RunsEcosystem_Support_Royalty_Ai_AndTheRatingReset_InThisOrder_EachOnItsOwnGuard()
        {
            string feature = Code("ArchitecturesFeature.cs");
            string month = BodyOf(feature, "private void OnMonthPassed()");

            int neutral = month.IndexOf("if (IsNeutralNow) return;", StringComparison.Ordinal);
            int ecosystem = month.IndexOf("_ecosystem.OnMonth", StringComparison.Ordinal);
            int support = month.IndexOf("_support.OnMonth", StringComparison.Ordinal);
            int royalty = month.IndexOf("_royalty.OnMonth", StringComparison.Ordinal);
            int ai = month.IndexOf("_ai.OnMonth()", StringComparison.Ordinal);
            int reset = month.IndexOf("_rating.InvalidateAll()", StringComparison.Ordinal);

            Assert.True(neutral >= 0 && ecosystem > neutral, "nothing runs while the feature is neutral");
            Assert.True(support > ecosystem, "design.md: the shares first, then the support");
            Assert.True(royalty > support, "then the royalty");
            Assert.True(ai > royalty, "then the AI, which reads the shares and the levels");
            Assert.True(reset > ai, "the cache of the rating is dropped last");

            Assert.True(Regex.IsMatch(month, @"ModHooks\.Guard\(FeatureId, MonthSite \+ ""/ecosystem"", _ecosystem\.OnMonth\)"), "the ecosystem is on a guard of its own");
            Assert.True(Regex.IsMatch(month, @"ModHooks\.Guard\(FeatureId, MonthSite \+ ""/royalty"", _royalty\.OnMonth\)"), "so is the royalty");
            Assert.Equal(1, Regex.Matches(feature, @"TickPhase\.MonthPassed").Count, "one monthly handler for the whole feature");
        }

        [Test]
        public void TheFeature_MakesTheServicesOnce_AndTellsThemAboutTheGameAndTheScene()
        {
            string feature = Code("ArchitecturesFeature.cs");
            foreach (string made in new[] { "_ecosystem = new EcosystemService(this);", "_royalty = new RoyaltyService(this);", "_notifier = new ArchNotifier(this);" })
            {
                Assert.True(feature.Contains(made), "made once in Register: " + made);
                Assert.Equal(1, Regex.Matches(feature, Regex.Escape(made)).Count);
            }

            string ready = BodyOf(feature, "private void OnGameReady(GameReadyReason reason)");
            Assert.True(ready.IndexOf("_seeder.OnGameReady(reason)", StringComparison.Ordinal) < ready.IndexOf("_ecosystem.OnGameReady", StringComparison.Ordinal),
                "the first measurement comes after the seed: every CPU has its family");

            string scene = BodyOf(feature, "private void OnSceneLeaving()");
            Assert.True(scene.Contains("_ecosystem.Forget()") && scene.Contains("_royalty.Forget()"), "what belonged to a game that is gone is forgotten");

            string disabled = BodyOf(feature, "public void OnDisabled()");
            Assert.True(disabled.Contains("_ecosystem.Forget()"), "switched off: the measurement is stale when the feature comes back");
            Assert.True(feature.Contains("ctx.Patch(typeof(ExtensionPatches));"), "the patch of the designer for the gates of the extensions is declared");
            Assert.True(feature.Contains("ResearchTreeUxApi.RegisterTechTooltipLines(new ArchTechTooltipLines(this));"), "FR-13 item 3");
            Assert.True(feature.Contains("_ai.OnActiveChanged += _notifier.OnActiveChanged;"), "FR-15 item 2");
            Assert.True(feature.Contains("_research.OnFamilyResearched += ArchitectureApi.RaiseFamilyResearched;") && feature.Contains("_ai.OnActiveChanged += ArchitectureApi.RaiseActiveChanged;"),
                "the events of the public API");
        }

        // ---- the ecosystem ----

        [Test]
        public void TheEcosystemService_RunsOnlyAtStageTwo_ReadsTheSeriesOfTheCpus_AndPatchesNothing()
        {
            string code = Code("Services", "EcosystemService.cs");
            string canRun = BodyOf(code, "public bool CanRun");
            Assert.True(canRun.Contains("!_feature.IsNeutralNow") && canRun.Contains("_feature.Balance.IsFullStage") && canRun.Contains("ArchParts.Ecosystem"),
                "a working feature, stage 2, the part of the self-check");

            string refresh = BodyOf(code, "public bool Refresh(bool announce)");
            Assert.True(refresh.Contains("if (!CanRun) return false;"));
            Assert.True(refresh.Contains("EcosystemMath.WindowSum(market.Sold, market.SoldByContracts, config.windowMonths)"), "FR-11 item 1: market and contracts, the window of balance.json");
            Assert.True(refresh.Contains("EcosystemMath.WindowSum(market.Sold, market.SoldByContracts, config.windowMonths, EcosystemMath.YearMonths)"), "the window of a year ago for the change");
            Assert.True(refresh.Contains("registry.ResolveCpu(cpu)"), "the family of a CPU is its binding");
            Assert.True(refresh.Contains("!catalog.Contains(familyId)"), "a family the catalog lost is left out");
            Assert.True(refresh.Contains("EcosystemMath.Shares(_now, config.fallbackFamily)"), "FR-11 item 3: no sales, the fallback family has it all");
            Assert.True(Regex.IsMatch(refresh, @"catch \(Exception ex\)\s*\{\s*ModHooks\.ReportFault\(FeatureId, RefreshSite, ex\);"), "a failure is counted, the game goes on");

            string blank = Blanked("Services", "EcosystemService.cs");
            foreach (string forbidden in new[] { "ModPatch", "ctx.Patch", "DateController", "SaveHandler", "Harmony", "SpendMoney", "EarnMoney", "DateTime.Now" })
            {
                Assert.False(blank.Contains(forbidden), "the ecosystem service must not contain " + forbidden);
            }
        }

        [Test]
        public void TheRating_TheDesignerAndTheCompareRows_TakeTheEcosystemFromOnePlace()
        {
            string provider = Code("Services", "ArchRatingProvider.cs");
            Assert.True(BodyOf(provider, "public float ContributionOf(").Contains("_feature.EcosystemOf(family.Id)"), "f_eco from the measured shares, x1 before them");

            string designer = Code("Designer", "DesignerIntegration.cs");
            Assert.True(designer.Contains("RatingForecast.All(family, year, balance, support, _feature.EcosystemOf(family.Id), DesignContext.CoreCount, DesignContext.Smt,"),
                "the forecast of the designer: the same shares and the cores of the design on the screen");

            string feature = Code("ArchitecturesFeature.cs");
            string of = BodyOf(feature, "internal float EcosystemOf(string familyId)");
            Assert.True(of.Contains("!Balance.IsFullStage") && of.Contains("-1f"), "stage 1 or not measured: the factor is x1");
        }

        // ---- the royalty ----

        [Test]
        public void TheRoyaltyService_ChargesOncePerPayer_PaysOncePerHolder_IntoTheRoyaltiesArticles()
        {
            string code = Code("Services", "RoyaltyService.cs");

            Assert.Equal(1, Regex.Matches(code, @"\.SpendMoney\(").Count, "the one place money is taken");
            Assert.Equal(1, Regex.Matches(code, @"\.EarnMoney\(").Count, "the one place money is paid");
            Assert.True(code.Contains("payer.SpendMoney(charge.Amount, ExpenseOrigin.Royalties);"), "FR-16 item 2: the article Royalties");
            Assert.True(code.Contains("holder.EarnMoney(payout.Amount, RevenueOrigin.Royalties);"), "FR-16 item 3: the revenue Royalties");
            Assert.False(Regex.IsMatch(code, @"ExpenseOrigin\.(Development|Research|Factories|Taxes|Fines)"), "no other article");
            Assert.True(code.Contains("RoyaltyMath.Plan(companies, license, Revoked())"), "who owes what is decided by the pure function");
            Assert.True(code.Contains("EcosystemMath.LastMonth(market.Income)"), "the income of the last full month, Count − 2");
        }

        [Test]
        public void TheRoyaltyService_SkipsAFastForward_ARevokedLicence_AndAnUnsoundFeature()
        {
            string code = Code("Services", "RoyaltyService.cs");
            string canRun = BodyOf(code, "public bool CanRun");
            Assert.True(canRun.Contains("!_feature.IsNeutralNow"), "FR-16 item 5: no royalty during the fast-forward of a new game");
            Assert.True(canRun.Contains("_feature.Balance.IsFullStage"), "stage 2 only");
            Assert.True(canRun.Contains("ArchParts.Royalty | ArchParts.Ecosystem"), "the parts it stands on passed the self-check");

            Assert.True(BodyOf(code, "public void OnMonth()").Contains("if (!CanRun) return;"));
            string revoked = BodyOf(code, "private static bool Revoked()");
            Assert.True(revoked.Contains("helper.X86LicenseHasBeenRevoked"), "the flag of the game");
            Assert.True(Regex.IsMatch(BodyOf(code, "public void OnMonth()"), @"catch \(Exception ex\)\s*\{\s*ModHooks\.ReportFault\(FeatureId, MonthSite, ex\);"), "a failure is counted");

            string blank = Blanked("Services", "RoyaltyService.cs");
            foreach (string forbidden in new[] { "ModPatch", "ctx.Patch", "DateController", "SaveHandler", "Harmony", "DateTime.Now" })
            {
                Assert.False(blank.Contains(forbidden), "the royalty service must not contain " + forbidden);
            }
        }

        [Test]
        public void TheRoyaltyService_TakesThePlayerAsAHolderNever_AndTheHoldersFromTheConfig()
        {
            string code = Code("Services", "RoyaltyService.cs");
            Assert.True(code.Contains("UniqueId = company.IsPlayer ? -1 : ArchRegistry.AiUniqueIdOf(company)"), "the player has no licence, an AI company by its UniqueID");
            Assert.True(code.Contains("_feature.Balance.x86License"), "the rate and the holders are in balance.json");
            Assert.True(code.Contains("string.Equals(family.Isa, license.isa, StringComparison.Ordinal)"), "the licensed ISA of the content");
        }

        // ---- the notifications ----

        [Test]
        public void TheNotifier_ShowsTheSwitchOfAnAiCompany_OnlyAtStageTwo_AndWhenTheHubSwitchIsOn()
        {
            string code = Code("Services", "ArchNotifier.cs");
            string show = BodyOf(code, "private void ShowSwitch(");
            Assert.True(show.Contains("!Works || !_feature.NotificationsEnabled"), "FR-15 item 4: the switch of the hub");
            Assert.True(show.Contains("company.IsPlayer"), "the player's own choice is no news");
            Assert.True(show.Contains("previous == null"), "the first family the seed gives is no switch");
            Assert.True(show.Contains("ModNotify.ShowKey(FeatureId, \"arch.notify.switch\", company.Name, family)"), "FR-15 item 2");
            Assert.True(BodyOf(code, "private bool Works").Contains("_feature.Balance.IsFullStage"), "FR-15 is stage 2");
        }

        [Test]
        public void TheMilestoneLetters_HaveTheTagThePriorityAndTheArgumentsTheirTextAsksFor()
        {
            string code = Code("Services", "ArchNotifier.cs");
            string send = BodyOf(code, "private void SendLetter(");
            Assert.True(send.Contains("Tag = EcosystemMilestones.Tag(family.Id, threshold),"), "FR-15 item 3: arch:eco:<familyId>:<threshold>");
            Assert.True(send.Contains("Priority = MailPriority.Normal,"), "priority Normal");
            Assert.True(send.Contains("TitleKey = \"arch.mail.eco.subject\"") && send.Contains("BodyKey = \"arch.mail.eco.body\""));

            Match args = Regex.Match(send, @"Args = new\[\] \{([^}]*)\}");
            Assert.True(args.Success, "the arguments are listed");
            int passed = args.Groups[1].Value.Split(',').Length;
            foreach (string language in new[] { "ru", "en" })
            {
                foreach (string key in new[] { "arch.mail.eco.subject", "arch.mail.eco.body" })
                {
                    Assert.True(Placeholders(language, key) <= passed, language + " " + key + " asks for more than the " + passed + " arguments the letter carries");
                }
            }
            Assert.Equal(3, Math.Max(Placeholders("ru", "arch.mail.eco.body"), Placeholders("en", "arch.mail.eco.body")), "the family, the threshold and the market");
        }

        [Test]
        public void TheMilestones_AreRecordedOncePerGame_AndTheFallbackFamilyHasNone()
        {
            string code = Code("Services", "ArchNotifier.cs");
            string check = BodyOf(code, "private void CheckMilestones(");
            Assert.True(check.Contains("!hasSales"), "without sales the shares are the fallback of the early game, not a market");
            Assert.True(check.Contains("string.Equals(id, config.fallbackFamily, StringComparison.Ordinal)) continue;"), "the proprietary ISA is every company's own: no letters");
            Assert.True(check.Contains("foreach (int threshold in thresholds) Record(recorded, id, threshold);"), "recorded in the save section, so a load does not repeat them");
            Assert.True(check.Contains("if (silent) continue;"), "the first look at a game is silent");
            Assert.True(check.Contains("thresholds[thresholds.Count - 1]"), "two thresholds in a month: one letter, for the higher");
            Assert.True(check.Contains("if (_feature.NotificationsEnabled) SendLetter"), "silenced letters are still recorded");

            string announced = BodyOf(code, "private static bool IsAnnounced(");
            Assert.True(announced.Contains("ModMail.Find(FeatureId, EcosystemMilestones.Tag(familyId, threshold)) != null"), "the mail service is asked for the tag as well");
        }

        // ---- the AI ----

        [Test]
        public void TheAi_ReadsTheSegmentFromTheClassifierOfTheCore_AndHasNoPriceThresholdsOfItsOwn()
        {
            string ai = Code("Services", "ArchitectureAi.cs");
            Assert.True(ai.Contains("ModSegments.Classify(cpu)"), "the segment of the price of a CPU is the core's answer");
            foreach (string series in new[] { "SoldByHighEnd", "SoldByMidRange", "SoldByLowEnd", "SoldByIndustries" })
            {
                Assert.True(ai.Contains("market." + series), "FR-14 item 1 reads " + series);
            }
            Assert.True(ai.Contains("SegmentShares.Resolve(high, mid, low, industries, votes)"), "sales, else votes, else a quarter each");

            // No rule of "the main segment" anywhere in the feature: no budgets of the market, no price band.
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string text = SourceScan.Strip(SourceScan.ReadText(path), true);
                foreach (string forbidden in new[] { "MidRangeBudget", "LowEndBudget", "IndustriesBudget", "PriceBand", "SegmentRule" })
                {
                    Assert.False(text.Contains(forbidden), Path.GetFileName(path) + " has its own classifier of segments (" + forbidden + ")");
                }
            }
        }

        [Test]
        public void TheAi_DecidesFullAtStageTwo_OnTheRecordWithTheOverrides_AndSetsTheTiers()
        {
            string ai = Code("Services", "ArchitectureAi.cs");
            string decide = BodyOf(ai, "private void DecideFor(");
            Assert.True(decide.Contains("AiOverrides.Apply(Registry.PreferenceOf(uniqueId), company.SaveID, Registry.AiOverrides)"), "the weights of other features over the record");
            Assert.True(decide.Contains("ArchAiPolicy.DecideFull(view, preference, _feature.Catalog, config, shares, _feature.Balance.rating, _feature.Balance.support)"));
            Assert.True(decide.Contains("ArchAiPolicy.DecideMvp(view, preference, _feature.Catalog, config)"), "stage 1 is unchanged");
            Assert.True(decide.Contains("ecosystem.Measured ? ecosystem.Current : null"), "not measured: x1");
            Assert.True(decide.Contains("view.Inflation = SupportService.InflationNow();"));
            Assert.True(BodyOf(ai, "private void Apply(").Contains("ApplyTiers(state, decision.Tiers)"));
            Assert.True(BodyOf(ai, "private void ApplyTiers(").Contains("SupportCycle.SetTier(state, _feature.Catalog, pair.Key, pair.Value, false)"), "the same rules of a tier as the player's");

            string researched = BodyOf(ai, "public void OnFamilyResearched(");
            Assert.True(researched.Contains("if (_feature.Balance.IsFullStage) return;"), "stage 2: no jump at the moment of the research, the monthly decision waits for the support");
        }

        // ---- the designer ----

        [Test]
        public void TheDesigner_WarnsAboutTheGates_AndAboutTheImmatureTools_FromTheSharedFunctions()
        {
            string designer = Code("Designer", "DesignerIntegration.cs");
            Assert.True(designer.Contains("ImmatureTools.Surcharge(state, today, balance.migration)"), "FR-12 item 4: the same function as the price");
            Assert.True(designer.Contains("ExtensionWarnings.For(balance.extensions, support, DesignContext.CoreCount, DesignContext.Smt)"), "FR-13 item 2");
            Assert.True(designer.Contains("balance.IsFullStage"), "stage 2 only");
            Assert.True(designer.Contains("public void OnDesignChanged()"));

            string patch = Code("Patches", "ExtensionPatches.cs");
            Assert.True(patch.Contains("[ModPatch(typeof(CreatorCpu), \"UpdateNumbers\", PatchKind.Postfix, ArgTypes = new Type[0], Optional = true)]"), "optional: losing it costs the warning only");
            Assert.True(patch.Contains("int ___currentCoreCount, bool ___smtEnabled"), "the private fields come in by Harmony's injection");
            Assert.True(patch.Contains("NoteDesign(___currentCoreCount, ___smtEnabled);"), "the postfix hands the cores and SMT on");
            Assert.True(patch.Contains("if (!DesignContext.NoteDesign(cores, smt)) return;"), "the lines are written only when the design changed");
        }

        [Test]
        public void TheCostProvider_AndTheModifierPanel_UseTheImmatureSurchargeOfTheDesignContext()
        {
            string provider = Code("Designer", "DesignCostProvider.cs");
            Assert.True(provider.Contains("DesignMath.CostFactor(family.Design, DesignContext.ImmatureSurcharge(company, familyId))"), "AC-12.2: devCost × (1 + surcharge)");

            string lines = Code("UI", "ArchModifierLines.cs");
            Assert.True(lines.Contains("DesignMath.CostFactor(family.Design, surcharge)"), "the panel shows what the provider gives");

            string context = Code("Designer", "DesignContext.cs");
            string surcharge = BodyOf(context, "public static float ImmatureSurcharge(");
            Assert.True(surcharge.Contains("feature.IsNeutralNow || !feature.Balance.IsFullStage"), "stage 1 and a neutral feature: 0");
            Assert.True(surcharge.Contains("ImmatureTools.Surcharge(state.GetFamily(familyId), ModTicks.Today, feature.Balance.migration)"));
        }

        [Test]
        public void TheStartingSupport_OfAFinishedFamily_IsTheInheritedOne()
        {
            string research = Code("Services", "ArchResearchService.cs");
            string complete = BodyOf(research, "private void Complete(");
            Assert.True(complete.Contains("SupportMath.StartSupport(family, state.Families, _feature.Catalog, _feature.Balance.support)"), "FR-12 item 2");
            string days = BodyOf(research, "private float EffectiveDays(");
            Assert.True(days.Contains("ArchResearchMath.IsaFactor(family, state.Families, _feature.Catalog, balance.research.isaExperienceFactor, balance.IsFullStage)"), "FR-12 item 1");
        }

        [Test]
        public void TheTooltipLines_AreAskedForMulticoreTechnologiesAtStageTwoOnly()
        {
            string code = Code("UI", "ArchTechTooltipLines.cs");
            string lines = BodyOf(code, "public IEnumerable<string> GetLines(Technology technology)");
            Assert.True(lines.Contains("!balance.IsFullStage || technology.Type != TechnologyType.Multicore"), "stage 2, and only the kind multicore");
            Assert.True(lines.Contains("ExtensionWarnings.ForTechnology(balance.extensions, cores)"));
            Assert.True(lines.Contains("ModL10n.L(LineKey, name,"), "one line per extension the technology brings");

            Assert.True(code.Contains("LineKey = \"arch.techtip.ext\""));
            foreach (string language in new[] { "ru", "en" })
            {
                Assert.Equal(5, Placeholders(language, "arch.techtip.ext"), language + ": the call passes the name, the support needed, the multiplier, the family and the level");
            }
        }

        // ---- the self-check and the panel ----

        [Test]
        public void TheSelfCheck_NamesWhatTheNewPartsStandOn()
        {
            string check = Code("ArchSelfCheck.cs");
            foreach (string needed in new[]
            {
                "ArchParts.Ecosystem, typeof(CpuDataProvider), \"GetAllCpus\", typeof(List<GameCpu>)",
                "ArchParts.Ecosystem, typeof(CpuBase), \"get_Market\"",
                "ArchParts.Ecosystem, typeof(CpuBase.MarketData), \"get_\" + series",
                "ArchParts.Royalty, typeof(ICompany), \"EarnMoney\", typeof(float), typeof(RevenueOrigin)",
                "ArchParts.Royalty, typeof(ICompany), \"SpendMoney\", typeof(float), typeof(ExpenseOrigin)",
                "ArchParts.Royalty, typeof(LicenseHelper), \"get_X86LicenseHasBeenRevoked\"",
                "ArchParts.Extensions, typeof(CreatorCpu), \"UpdateNumbers\", Postfix",
                "ArchParts.Extensions, typeof(CreatorCpu), \"currentCoreCount\", typeof(int)",
                "ArchParts.Extensions, typeof(CreatorCpu), \"smtEnabled\", typeof(bool)",
                "ArchParts.Ecosystem, ArchParts.Royalty, ArchParts.Extensions, ArchParts.LabUi, ArchParts.Support }",
            })
            {
                Assert.True(check.Contains(needed), "the self-check lacks: " + needed);
            }
        }

        [Test]
        public void ThePanel_AddsTheNewSectionsAfterTheSupport_EachRedrawnOnAGuardOfItsOwn()
        {
            string panel = Code("UI", "ArchitecturesPanel.cs");
            int support = panel.IndexOf("new SupportSection(_feature, _ui)", StringComparison.Ordinal);
            int ecosystem = panel.IndexOf("new EcosystemSection(_feature)", StringComparison.Ordinal);
            int royalty = panel.IndexOf("new RoyaltyLine(_feature)", StringComparison.Ordinal);
            int rivals = panel.IndexOf("new RivalsSection(_feature)", StringComparison.Ordinal);
            Assert.True(support >= 0 && ecosystem > support && royalty > ecosystem && rivals > royalty, "design.md: the support, the ecosystem, the royalty under it, the rivals");

            foreach (string call in new[] { "_ecosystem.Refresh()", "_royalty.Refresh(player)", "_rivals.Refresh()" })
            {
                Assert.True(Regex.IsMatch(panel, @"ModHooks\.Guard\(ArchitecturesFeature\.FeatureId, RefreshSite \+ ""/\w+"", \(\) => \{ if \(\w+ != null\) " + Regex.Escape(call) + @"; \}\);"), "guarded: " + call);
            }

            string eco = Code("UI", "EcosystemSection.cs");
            Assert.True(eco.Contains("_box.gameObject.SetActive(false);"), "stage 1 never shows it");
            Assert.True(eco.Contains("service.CanRun && service.Measured"), "shown by the rule of the service");
            Assert.True(eco.Contains("ModUI.Tooltip(line.gameObject"), "NFR-6: the formula in numbers");

            string rivalsSection = Code("UI", "RivalsSection.cs");
            Assert.True(rivalsSection.Contains("RivalsModel.Rows(Inputs())"), "the rows come from the model");
            Assert.True(rivalsSection.Contains("_feature.Balance.IsFullStage"), "stage 2 only");

            string line = Code("UI", "RoyaltyLine.cs");
            Assert.True(line.Contains("service.LastMonthRoyaltyOf(player)") && line.Contains("service.IsFree"), "FR-16 item 6");
        }

        // ---- the public API ----

        [Test]
        public void TheApi_IsSafeWithoutTheFeature_AndHasTheMembersOfTheDesign()
        {
            string api = Code("Api", "ArchitectureApi.cs");
            foreach (string member in new[]
            {
                "public static bool IsActive", "public static string GetArchitectureId(GameCpu cpu)", "public static string GetActiveArchitectureId(ICompany company)",
                "public static float GetRatingMultiplier(GameCpu cpu, Segment segment)", "public static void TransferCompanyState(ICompany from, ICompany to, bool mergeKeepBest)",
                "public static void SetAiWeightOverride(ICompany company, string familyId, float? weight)",
                "public static event Action<ICompany, string> OnFamilyResearched;", "public static event Action<ICompany, string, string> OnActiveChanged;",
            })
            {
                Assert.True(api.Contains(member), "the API lacks: " + member);
            }

            Assert.True(Regex.IsMatch(api, @"if \(ReferenceEquals\(company, null\) \|\| company\.IsPlayer \|\| !IsActive\) return;"), "no override for the player, none while the feature is off");
            Assert.True(api.Contains("AiOverrides.Set(registry.AiOverrides, company.SaveID, familyId, weight, ArchRegistry.MaxOverrides)"));
            Assert.True(api.Contains("StateTransfer.Merge(source, target, mergeKeepBest)"));
            Assert.True(Regex.Matches(api, @"catch \(Exception ex\)").Count >= 7, "every call and every handler of an event has its own catch");
            Assert.True(Regex.Matches(api, @"ModHooks\.ReportFault\(").Count >= 7);
            Assert.False(api.Contains("throw "), "the API never throws at another feature");
        }
    }
}
