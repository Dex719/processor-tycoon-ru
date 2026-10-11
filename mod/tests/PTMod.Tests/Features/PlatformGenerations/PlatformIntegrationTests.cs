using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.L10n;
using PTMod.Features.PlatformGenerations;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.PlatformGenerations
{
    /// <summary>
    /// TSK-009, TSK-010, TSK-011: the seams of the integration code that cannot be loaded here (it talks to the game), checked on the source: the
    /// providers of the two channels are registered once in <c>Register</c> and never again, the hot path of the rating has no allocation, every
    /// failure is caught and answers 1, the fast-forward and the switches answer 1, the handlers of the core's events leave at once for the
    /// fast-forward and for variants, the feature has no patch on <c>AddCpu</c>, the ticks are the ordinary ones (not called in the fast-forward or
    /// while a save loads), the end of the fast-forward reconstructs without any mail, and the strings the code names exist.
    /// </summary>
    public class PlatformIntegrationTests
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

        /// <summary>The code of a file of the feature without comments (strings kept).</summary>
        private static string Code(string file)
        {
            string path = Path.Combine(FeatureDirectory(), file);
            Assert.True(File.Exists(path), "missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        /// <summary>The body of the method that starts at <paramref name="signature"/>, braces included; found on the text with the strings blanked, cut from the text with them.</summary>
        private static string BodyOf(string file, string signature)
        {
            string path = Path.Combine(FeatureDirectory(), file);
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

        // ---- the providers (TSK-009) ----

        [Test]
        public void TheProviders_AreRegisteredOnce_InRegister_WithTheirChannelsAndReasons()
        {
            string provider = Squeeze(Code("PlatformModifierProvider.cs"));

            Assert.True(provider.Contains("ModModifiers.RegisterProvider(Feature, ModChannels.MarketRatingCpu, Rating, RatingReasonKey)"), "market.rating.cpu");
            Assert.True(provider.Contains("ModModifiers.RegisterReasonedProvider(Feature, ModChannels.ContractSignChance, Chance)"), "contract.signChance");
            Assert.True(provider.Contains("RatingReasonKey = \"pg.reason.rating\""), "the fixed reason of the rating");
            Assert.True(provider.Contains("if (_registered) return;"), "a second call does nothing");

            // Nobody else registers a provider.
            int registrations = 0;
            foreach (string file in SourceFiles())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                registrations += Count(code, "RegisterProvider(") + Count(code, "RegisterReasonedProvider(");
            }
            Assert.Equal(2, registrations, "exactly the two registrations of the provider class");
        }

        [Test]
        public void TheFeature_RegistersTheProvidersInRegister_NotWhenSwitchedOnOrOff()
        {
            string feature = Code("PlatformGenerationsFeature.cs");
            string register = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public void Register(FeatureContext ctx)"));
            string enabled = BodyOf("PlatformGenerationsFeature.cs", "public void OnEnabled()");
            string disabled = BodyOf("PlatformGenerationsFeature.cs", "public void OnDisabled()");

            Assert.Equal(1, Count(feature, "Provider.Register()"), "the call is made once");
            Assert.True(register.Contains("Provider.Register();"), "in Register");
            Assert.True(register.Contains("Provider.RefreshSettings();"), "the switches are read before the first question");
            foreach (string body in new[] { enabled, disabled })
            {
                Assert.False(body.Contains("Register"), "OnEnabled / OnDisabled do not register again");
                Assert.False(body.Contains("RegisterProvider"));
            }
        }

        [Test]
        public void TheRatingProvider_HasNoAllocationAndNoWalkOnItsHotPath()
        {
            string rating = BodyOf("PlatformModifierProvider.cs", "public float Rating(ModifierContext ctx)");
            string blanked = SourceScan.Strip(rating, true);

            foreach (string forbidden in new[] { "new ", "string", "foreach", "for (", "while", "=>", "ToString", "Format(", "Linq", "$\"", "delegate" })
            {
                Assert.False(blanked.Contains(forbidden), "Rating must not contain '" + forbidden + "'");
            }
            Assert.True(blanked.Contains("TryGet(cpu, out effect)"), "a lookup in the cache");
            Assert.True(blanked.Contains("Segment.Industries"), "the Industries segment has its own multiplier");
        }

        [Test]
        public void TheProviders_AnswerOne_InTheFastForward_WithTheSwitchOff_AndOnAFailure()
        {
            string rating = BodyOf("PlatformModifierProvider.cs", "public float Rating(ModifierContext ctx)");
            string chance = BodyOf("PlatformModifierProvider.cs", "public ProviderResult Chance(ModifierContext ctx)");

            Assert.True(Squeeze(rating).Contains("if (!_ratingOn || ModTicks.IsFastForwarding) return 1f;"), "rating: switch and fast-forward");
            Assert.True(Squeeze(chance).Contains("if (!_oemOn || ModTicks.IsFastForwarding) return new ProviderResult(1f);"), "chance: switch and fast-forward");
            foreach (string body in new[] { rating, chance })
            {
                Assert.True(body.Contains("catch (Exception ex)"), "a failure is caught");
                Assert.True(body.Contains("ModHooks.ReportFault(Feature,"), "and counted against the feature");
            }
        }

        [Test]
        public void TheChanceProvider_PicksTheModeByTheContract_AndUsesThePreparedReasons()
        {
            string chance = Squeeze(BodyOf("PlatformModifierProvider.cs", "public ProviderResult Chance(ModifierContext ctx)"));

            Assert.True(chance.Contains("GameContract offered = ctx.Contract;"), "the contract of the question (P8 + P23)");
            Assert.True(chance.Contains("contract.Present = true;"), "its presence selects the per-contract mode");
            Assert.True(chance.Contains("_feature.ContractPrefs.Ensure(offered,"), "a contract without a preference gets one on the spot");
            Assert.True(chance.Contains("_engine.Chance(info, contract, balance.Oem.Mode,"), "the configured mode goes to the engine");
            Assert.True(chance.Contains("return _reasons.Of(result);"), "the arguments of the reasons are the prepared ones");
            Assert.False(chance.Contains("string.Format"), "no string is made on the call");
            Assert.False(chance.Contains("ReasonArgs ="), "no array is made on the call");
        }

        [Test]
        public void TheProvider_ReadsTheSwitches_AndDropsTheCachedValuesWhenTheyChange()
        {
            string refresh = Squeeze(BodyOf("PlatformModifierProvider.cs", "public void RefreshSettings()"));
            Assert.True(refresh.Contains("PlatformSettings.RatingEffects") && refresh.Contains("PlatformSettings.OemEffects"));
            Assert.True(refresh.Contains("if (ratingChanged) ModModifiers.Invalidate(ModChannels.MarketRatingCpu);"));
            Assert.True(refresh.Contains("if (oemChanged) ModModifiers.Invalidate(ModChannels.ContractSignChance);"));

            string subscriptions = Squeeze(BodyOf("PlatformSubscriptions.cs", "private void OnSettingsChanged(string featureId, string key)"));
            Assert.True(subscriptions.Contains("PlatformParams.RatingEffects") && subscriptions.Contains("PlatformParams.OemEffects"));
            Assert.True(subscriptions.Contains("_feature.Provider.RefreshSettings"));
            Assert.True(Squeeze(Code("PlatformSubscriptions.cs")).Contains("ModSettings.Changed += OnSettingsChanged;"));
        }

        [Test]
        public void TheCacheOfACpu_IsMadeAgain_OnReleaseAndOnTransfer_AndTheCompanyIsInvalidated()
        {
            string update = Squeeze(BodyOf("PlatformModifierProvider.cs", "public void UpdateCpu(GameCpu cpu, string alsoCompanyKey = null)"));
            Assert.True(update.Contains("Fill(cpu, effect, GameAdapter.Today());"));
            Assert.True(update.Contains("InvalidateCompany(key);"));
            Assert.True(update.Contains("InvalidateCompany(alsoCompanyKey);"), "the previous owner of a transferred CPU too");

            string invalidate = Squeeze(BodyOf("PlatformModifierProvider.cs", "private static void InvalidateCompany(string companyKey)"));
            Assert.True(invalidate.Contains("ModModifiers.Invalidate(ModChannels.MarketRatingCpu, companyKey);"));
            Assert.True(invalidate.Contains("ModModifiers.Invalidate(ModChannels.ContractSignChance, companyKey);"));
        }

        [Test]
        public void TheProviders_AreForPlayerAndAiAlike()
        {
            // FR-5 item 6, FR-8 item 7: no branch on who the company is inside the providers or the cache.
            string provider = SourceScan.Strip(Code("PlatformModifierProvider.cs"), true);
            Assert.False(provider.Contains("IsPlayer"));
            Assert.False(provider.Contains("PlayerKey"));
            Assert.False(provider.Contains("ModCompanies.Player"));
        }

        // ---- the handlers of the core's events (TSK-010) ----

        [Test]
        public void TheReleaseHandler_LeavesAtOnce_ForTheFastForward_AndForAVariant()
        {
            string body = Squeeze(BodyOf("PlatformReleaseHandlers.cs", "public void OnCpuReleased(ICompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)"));

            Assert.True(body.StartsWith("{ if (duringFastForward || isVariant) return;"), "the first thing it does");
            Assert.True(body.Contains("feature.Timeline.OnCpuReleased("), "the chronology");
            Assert.True(body.Contains("feature.Provider.UpdateCpu(cpu);"), "the cache of the CPU");
            Assert.True(body.Contains("feature.Provider.RebuildCache();"), "and of the others when the chronology changed");
            Assert.True(body.Contains("Tell(outcome, gen, socket, companyKey, feature);"), "the notifier");
        }

        [Test]
        public void TheTransferHandler_RefreshesTheCpuAndBothCompanies_WithoutWritingAChronology()
        {
            string body = Squeeze(BodyOf("PlatformReleaseHandlers.cs", "public void OnCpuTransferred(ICompany from, ICompany to, GameCpu cpu)"));

            Assert.True(body.Contains("feature.Provider.UpdateCpu(cpu, ModCompanies.KeyOf(from));"));
            Assert.False(body.Contains("Timeline"), "the new owner has not released the CPU");
        }

        [Test]
        public void TheEventsOfTheCore_AreSubscribedInRegister_AndTheFeatureHasNoPatchOnAddCpu()
        {
            string register = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public void Register(FeatureContext ctx)"));
            Assert.True(register.Contains("ModEvents.CpuReleased += _releaseHandlers.OnCpuReleased;"));
            Assert.True(register.Contains("ModEvents.CpuTransferred += _releaseHandlers.OnCpuTransferred;"));
            Assert.True(register.Contains("ModTicks.GameReady += OnGameReady;"));
            Assert.True(register.Contains("_subscriptions.Register(ctx);"));

            // Company.AddCpu and AICompany.AddCpu are reserved by the core (P20): no patch of the feature may name them.
            foreach (string file in SourceFiles())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), false);
                foreach (Match attribute in Regex.Matches(code, @"\[ModPatch\([^\]]*\]"))
                {
                    Assert.False(attribute.Value.Contains("AddCpu"), SourceScan.Relative(FeatureDirectory(), file) + ": " + attribute.Value);
                }
                Assert.False(Regex.IsMatch(code, @"typeof\((AI)?Company\)\s*,\s*""AddCpu"""), SourceScan.Relative(FeatureDirectory(), file));
            }
        }

        // ---- the clock of the game (TSK-011) ----

        [Test]
        public void TheTicks_AreTheOrdinaryOnes_NotCalledInTheFastForwardOrWhileASaveLoads()
        {
            string register = Squeeze(BodyOf("PlatformSubscriptions.cs", "public void Register(FeatureContext ctx)"));

            Assert.True(register.Contains("ctx.OnTick(TickPhase.YearPassed, OnYearPassed);"));
            Assert.True(register.Contains("ctx.OnTick(TickPhase.LateMonthPassed, OnMonthPassed);"));
            Assert.True(register.Contains("ctx.OnTick(TickPhase.FastForwardEnded, OnFastForwardEnded);"));
            Assert.True(register.Contains("PTModHost.SceneReady += OnSceneReady;"), "the contract manager is subscribed again after a scene load");

            foreach (string file in SourceFiles())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                Assert.False(code.Contains("RunDuringFastForward"), SourceScan.Relative(FeatureDirectory(), file));
                Assert.False(code.Contains("RunDuringLoading"), SourceScan.Relative(FeatureDirectory(), file));
            }
        }

        [Test]
        public void TheEndOfTheFastForward_Reconstructs_RebuildsTheBase_AndTheCache_WithoutAnyMail()
        {
            string ended = Squeeze(BodyOf("PlatformSubscriptions.cs", "private void OnFastForwardEnded()"));
            Assert.True(ended.Contains("Resync(true,"), "the reconstruction is asked for");

            string resync = Squeeze(BodyOf("PlatformSubscriptions.cs", "public void Resync(bool reconstruct, string why)"));
            int reconstruct = resync.IndexOf("Reconstruction.Run(", StringComparison.Ordinal);
            int rebuild = resync.IndexOf("InstalledBase.RebuildFull(", StringComparison.Ordinal);
            int invariants = resync.IndexOf("CheckInvariants(", StringComparison.Ordinal);
            int cache = resync.IndexOf("Provider.RebuildCache();", StringComparison.Ordinal);
            Assert.True(reconstruct >= 0 && rebuild > reconstruct && invariants > rebuild && cache > invariants, "reconstruct, full rebuild, invariants, cache - in this order");

            foreach (string forbidden in new[] { "Notifier", "ModMail", "ModNotify", "Announced(" })
            {
                Assert.False(resync.Contains(forbidden), "the resynchronization must not tell the player anything: " + forbidden);
            }
        }

        [Test]
        public void TheGameReady_ReconstructsForANewGameOrAMissingSection_AndTheSwitchOnCatchesUp()
        {
            string ready = Squeeze(BodyOf("PlatformSubscriptions.cs", "public void OnGameReady(GameReadyReason reason)"));
            Assert.True(ready.Contains("reason == GameReadyReason.NewGame || _feature.NeedsReconstruction"));

            string enabled = Squeeze(BodyOf("PlatformSubscriptions.cs", "public void OnEnabled()"));
            Assert.True(enabled.Contains("if (!ModTicks.IsGameReady) return;"));
            Assert.True(enabled.Contains("Resync(true,"));
        }

        [Test]
        public void TheMonthlyPass_AdvancesTheBase_DropsTheOldPreferences_AndRebuildsTheCache()
        {
            string month = Squeeze(BodyOf("PlatformSubscriptions.cs", "private void OnMonthPassed()"));
            int apply = month.IndexOf("InstalledBase.ApplyMonth(", StringComparison.Ordinal);
            int prune = month.IndexOf("ContractPrefs.Prune()", StringComparison.Ordinal);
            int cache = month.IndexOf("Provider.RebuildCache();", StringComparison.Ordinal);
            Assert.True(apply >= 0 && prune > apply && cache > prune, "base, preferences, cache - in this order");
        }

        [Test]
        public void TheYearlyAnnouncement_IsOncePerGeneration_OnItsAnnounceYear()
        {
            string year = Squeeze(BodyOf("PlatformSubscriptions.cs", "private void OnYearPassed()"));
            Assert.True(year.Contains("if (gen.announceYear != today.Year) continue;"));
            Assert.True(year.Contains("if (!feature.Timeline.Announce(gen.id, today)) continue;"), "only a new announcement is told");
            Assert.True(year.Contains("feature.Notifier.Announced(gen); PlatformEvents.RaiseGenerationAnnounced(gen.id);"), "to the player and to the subscribers, once");
        }

        [Test]
        public void TheNewContracts_GetAPreference_OnlyInTheNormalCourseOfTheGame()
        {
            string guard = Squeeze(BodyOf("PlatformSubscriptions.cs", "private void AssignNewContracts()"));
            Assert.True(guard.Contains("ModTicks.IsFastForwarding || ModTicks.IsLoading || !ModTicks.IsGameReady"), "not in the fast-forward, not while loading, not before ready");
            Assert.True(guard.Contains("ContractPrefs.AssignMissing()"), "only the contracts without one");

            string handler = Squeeze(BodyOf("PlatformSubscriptions.cs", "private void OnNewContracts()"));
            Assert.True(handler.Contains("if (!ModFeatures.IsActive(Feature)) return;"), "a switched off feature does not react");
            Assert.True(handler.Contains("ModHooks.Guard(Feature,"), "and a failure stays inside");

            string ensure = Squeeze(BodyOf("ContractPreferences.cs", "public bool Ensure(GameContract contract, string key, int contractType)"));
            Assert.True(ensure.Contains("if (feature.Preferences.PreferenceOf(key) != null) return true;"), "decided once");
            Assert.True(ensure.Contains("IncludesContractType(contractType)"), "the types of the balance");
            Assert.True(ensure.Contains("ModRandom.WorldSeed"), "the roll is deterministic");

            string prune = Squeeze(BodyOf("ContractPreferences.cs", "public int Prune()"));
            Assert.True(prune.Contains("if (!GameAdapter.CollectContracts(_contracts)) return 0;"), "nothing is lost when the contracts cannot be read");
        }

        [Test]
        public void TheSaveParticipant_FoldsTheStateIntoTheSection_AndBuildsItBack()
        {
            string write = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public JsonObject Write()"));
            Assert.True(write.Contains("Timeline.WriteTo(SaveData);") && write.Contains("Preferences.WriteTo(SaveData);"));

            string read = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public void Read(JsonObject section, int schema)"));
            Assert.True(read.Contains("Timeline.ReadFrom(SaveData,") && read.Contains("Preferences.ReadFrom(SaveData,"));
            Assert.True(read.Contains("InstalledBase.Clear();"), "the base is derived from the game, not read");

            string reset = Squeeze(BodyOf("PlatformGenerationsFeature.cs", "public void ResetToDefaults()"));
            Assert.True(reset.Contains("Timeline.Clear();") && reset.Contains("Preferences.Clear();") && reset.Contains("InstalledBase.Clear();") && reset.Contains("Provider.Clear();"));
        }

        // ---- the game side ----

        [Test]
        public void TheGameTypes_AreTouchedOnlyInTheFilesOfTheGameSide()
        {
            // The pure logic never names the game; the adapter reads it behind NoInlining methods and a try/catch.
            string adapter = Code("GameAdapter.cs");
            Assert.True(Regex.Matches(adapter, @"MethodImplOptions\.NoInlining").Count >= 8, "the readers of the game are small methods the JIT cannot inline");
            foreach (string file in SourceFiles())
            {
                if (file.Replace('\\', '/').Contains("/Logic/")) continue;
                string code = SourceScan.Strip(SourceScan.ReadText(file), true);
                string name = Path.GetFileName(file);
                if (name == "GameAdapter.cs" || name == "PlatformContent.cs") continue;

                // Outside the adapter the game's objects appear only as aliases of the CPU, the contract and the company that are handed over.
                Assert.False(Regex.IsMatch(code, @"\bDateController\b|\bDataFinder\b|\bCpuDataProvider\b|\bContractManager\b"), name + " reads the game directly");
            }
        }

        // ---- the strings the code names ----

        [Test]
        public void EveryStringTheFeatureNames_ExistsInBothLanguages()
        {
            var ru = new L10nTables();
            ru.AddJson("ru", "ru", PlatformTestData.ReadStrings("ru"), null);
            var en = new L10nTables();
            en.AddJson("en", "en", PlatformTestData.ReadStrings("en"), null);

            int found = 0;
            foreach (string file in SourceFiles())
            {
                string code = SourceScan.Strip(SourceScan.ReadText(file), false);
                foreach (Match m in Regex.Matches(code, "\"(pg\\.[A-Za-z0-9_.\\-]*[A-Za-z0-9_\\-])\""))
                {
                    if (Array.IndexOf(PlatformPredicateNames.All, m.Groups[1].Value) >= 0) continue;   // a name of a predicate in the JSON of conditions, not a string of the tables
                    found++;
                    Assert.True(ru.Contains("ru", m.Groups[1].Value), SourceScan.Relative(FeatureDirectory(), file) + " names '" + m.Groups[1].Value + "' that ru lacks");
                    Assert.True(en.Contains("en", m.Groups[1].Value), SourceScan.Relative(FeatureDirectory(), file) + " names '" + m.Groups[1].Value + "' that en lacks");
                }
            }
            Assert.True(found >= 5, "the sources name the strings of the feature");
        }
    }
}
