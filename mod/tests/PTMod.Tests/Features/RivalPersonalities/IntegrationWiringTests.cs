using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// How the extensions (TSK-020..022, 026, 027, 030) are plugged into the game and the other features. The game does not run in the tests, so the
    /// sources are read as text, comments aside: the one optional patch (<c>AcceptableFailureRate</c>), the flaws that start a caution, the places where the
    /// links are connected and disconnected, and the files that must stay free of Harmony. The pure parts have tests of their own
    /// (<see cref="CautionMathTests"/>, <see cref="FlawBiasTests"/>, <see cref="RivalApiTests"/>, <see cref="AcquisitionLinkTests"/>, <see cref="PartnershipTests"/>,
    /// <see cref="EventChooserTests"/>, <see cref="RandomAssignmentTests"/>, <see cref="RivalPredicateTests"/>).
    /// </summary>
    public class IntegrationWiringTests
    {
        private static string FeatureFolder()
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            Assert.True(Directory.Exists(folder), "no feature folder " + folder);
            return folder;
        }

        private static string Code(params string[] parts)
        {
            string path = Path.Combine(new[] { FeatureFolder() }.Concat(parts).ToArray());
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        // ---- the patch of the caution (TSK-030) ----

        [Test]
        public void TheCautionPatch_IsAnOptionalPostfixOnAcceptableFailureRate()
        {
            string patch = Code("Behaviour", "CautionPatch.cs");

            Assert.Equal(1, Regex.Matches(patch, @"\[ModPatch\(").Count, "one patch");
            Assert.True(Regex.IsMatch(patch, @"\[ModPatch\(typeof\(GameCreation\), ""AcceptableFailureRate"", PatchKind\.Postfix, Optional = true\)\]"), "Optional: the method may be missing");
            Assert.True(Regex.IsMatch(patch, @"using GameCreation = ProcessorTycoon\.AISystem\.AIBehaviourCreation;"));
            Assert.True(Regex.IsMatch(patch, @"private static void Caution\(AICompany ___company, ref float __result\)"), "the company of the designer and the result");
            Assert.True(Regex.IsMatch(patch, @"if \(factor != 1f\) __result \*= factor;"), "the result is multiplied, never replaced");
        }

        [Test]
        public void TheCautionPatch_IsTheOnlyOptionalPatchOfTheFeature_AndIsDeclaredWithCtxPatch()
        {
            var optional = new List<string>();
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                if (Regex.IsMatch(code, @"\[ModPatch\([^\]]*Optional\s*=\s*true")) optional.Add(Path.GetFileName(path));
            }

            Assert.Equal(new List<string> { "CautionPatch.cs" }, optional, "every other target is required");
            Assert.Equal(1, Regex.Matches(Code("RivalPersonalitiesFeature.cs"), @"ctx\.Patch\(typeof\(CautionPatch\)\);").Count, "the registry of the core and its self-check know the target");
        }

        [Test]
        public void TheCautionPatch_ChangesNothing_WhenThereIsNoCaution_OrItDoesNotCount()
        {
            string patch = Code("Behaviour", "CautionPatch.cs");

            Assert.True(Regex.IsMatch(patch, @"CautionMath\.Applies\(ModFeatures\.IsActive\(DesignFlawRecallsApi\.FeatureId\), company\.IsBankrupt, feature\.Book\.IsDormant\(uniqueId\), ModTicks\.IsFastForwarding\)"),
                "design-flaw-recalls off, bankrupt, dormant, fast-forward: the game's value");
            Assert.True(Regex.IsMatch(patch, @"feature\.Cautions\.FactorFor\(uniqueId, ModTicks\.Today\)"));
            Assert.True(Regex.IsMatch(patch, @"if \(!ModHooks\.CanRun\(Feature, Site\)\) return;"));
            Assert.True(Regex.IsMatch(patch, @"catch \(Exception e\)\s*\{\s*ModHooks\.ReportFault\(Feature, Site, e\);"), "a failure is counted and leaves the result");
            Assert.True(Regex.IsMatch(patch, @"!ModFeatures\.IsActive\(Feature\)\) return 1f;"), "a feature that is off");
            Assert.False(Regex.IsMatch(patch, @"\bnew\b"), "no allocation at every design of every AI company");
        }

        [Test]
        public void ThePatchesOfTheFeature_AreTheTwelveTargetsOfTheDesign()
        {
            // 12 targets: AICompany.Initialize, ReformCyrux, AIBehaviourCreation.OnTick, AcceptableFailureRate (optional), GetMaxFunding, AIBehaviourMarket.OnTick (prefix + postfix),
            // AddTechnology, PromoteContractToActive, BreakContract, UpdateScores (prefix + finalizer), GetProviderScores, GetClientScores
            var targets = new HashSet<string>();
            var rule = new Regex(@"\[ModPatch\(\s*typeof\(\s*(\w+)\s*\)\s*,\s*""(\w+)""", RegexOptions.Compiled);
            foreach (string path in SourceScan.Files(FeatureFolder(), ".cs"))
            {
                foreach (Match match in rule.Matches(SourceScan.Strip(SourceScan.ReadText(path), false))) targets.Add(match.Groups[1].Value + "." + match.Groups[2].Value);
            }

            Assert.Equal(12, targets.Count, string.Join(", ", targets.OrderBy(t => t)));
            Assert.True(targets.Contains("GameCreation.AcceptableFailureRate"));
        }

        // ---- the flaws that start a caution (FR-17) ----

        [Test]
        public void OnlyAPublicFlawOfAnAiCompany_StartsACaution_OnceForTheRecord()
        {
            string link = Code("Integration", "FlawLink.cs");

            Assert.True(Regex.IsMatch(link, @"if \(flaw == null \|\| flaw\.State != FlawState\.Public\) return;"), "only a flaw that is public now");
            Assert.True(Regex.IsMatch(link, @"CompanyKey\.TryGetAiId\(flaw\.CompanyKey, out uid\)"), "a flaw of the player is not a rival's business");
            Assert.True(Regex.IsMatch(link, @"_cautions\.OnPublicFlaw\(uid, flaw\.Id, \(int\)flaw\.Severity, since, today\)"), "the record id makes it once, the severity is checked against the config");
            Assert.True(Regex.IsMatch(link, @"RivalDates\.FromDayNumber\(flaw\.PublicSinceDay\)"), "the term starts on the day it first became public");
            Assert.True(Regex.IsMatch(link, @"ModHooks\.Guard\(Feature, ""FlawLink/OnFlawStateChanged"", \(\) => Handle\(flaw\)\)"), "a failure stays here");
            Assert.True(Regex.IsMatch(link, @"DesignFlawRecallsApi\.Registry\.GetAllKnownFlaws\(\)"), "the flaws that are public already are looked at when the link is made");
        }

        [Test]
        public void WithoutDesignFlawRecalls_NoCautionIsMade_AndTheBiasIsNotRegistered()
        {
            string link = Code("Integration", "FlawLink.cs");

            // the only entrances of Handle: the event of the registry (silent while the feature is off) and the look at the known flaws after Attach, which needs it active
            Assert.True(Regex.IsMatch(link, @"if \(!ModFeatures\.IsActive\(Feature\) \|\| !ModFeatures\.IsActive\(DesignFlawRecallsApi\.FeatureId\)\) return;"));
            Assert.Equal(2, Regex.Matches(link, @"\bHandle\(").Count - 1, "the definition and two calls");
            Assert.False(Regex.IsMatch(link, @"\bDesignFlawRecallsFeature\b"), "only the Api of that feature");
        }

        // ---- the places where the links are made and dropped ----

        [Test]
        public void TheLinks_AreMadeOnGameReady_WhenEnabledInAReadyGame_AndWhenTheOtherFeatureChanges_AndDroppedWhenDisabled()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(feature, @"private void AttachLinks\(\)\s*\{\s*_flawLink\.Attach\(\);\s*_acquisition\.Attach\(\);\s*\}"));
            Assert.True(Regex.IsMatch(feature, @"private void DetachLinks\(\)\s*\{\s*_flawLink\.Detach\(\);\s*_acquisition\.Detach\(\);\s*\}"));
            Assert.True(Regex.IsMatch(feature, @"""ModTicks\.GameReady\.links"", AttachLinks"));
            Assert.True(Regex.IsMatch(feature, @"""feature/enabled\.links""[\s\S]*?if \(ModTicks\.IsGameReady\) AttachLinks\(\);"));
            Assert.True(Regex.IsMatch(feature, @"""feature/disabled\.links"", DetachLinks"));
            Assert.True(Regex.IsMatch(feature, @"_flawLink\.OnFeatureStateChanged\(featureId, state\)") && Regex.IsMatch(feature, @"_acquisition\.OnFeatureStateChanged\(featureId, state\)"));
        }

        [Test]
        public void TheFeature_RegistersEverythingExactlyOnce_InRegister()
        {
            string feature = Code("RivalPersonalitiesFeature.cs");
            string register = Between(feature, "public void Register(FeatureContext ctx)", "public void OnEnabled()");

            foreach (string call in new[]
            {
                "ctx.Patch(typeof(CautionPatch));", "RivalPersonalitiesApi.Register(_api);", "RivalConditions.Register();",
                "EventAi.AddChooser(_chooser, RivalEventChooser.DefaultPriority);", "AcquisitionLink.RegisterProvider();", "ModFeatures.StateChanged += OnFeatureStateChanged;",
            })
            {
                Assert.Equal(1, Regex.Matches(register, Regex.Escape(call)).Count, call);
            }
        }

        [Test]
        public void TheLinks_NameOnlyTheApiFoldersOfTheOtherFeatures()
        {
            // the build of a feature takes the Api folders of its dependencies only
            var allowed = new Dictionary<string, string[]>
            {
                { "design-flaw-recalls", new[] { "DesignFlawRecallsApi", "FlawRegistry", "FlawRecordView", "FlawState", "IFlawResponseBias", "FlawSeverity" } },
                { "rival-acquisitions", new[] { "RivalAcquisitionsApi", "DealRecord", "DealType" } },
            };

            foreach (string file in new[] { "FlawLink.cs", "FlawResponseBias.cs", "AcquisitionLink.cs" })
            {
                string code = Code("Integration", file);
                MatchCollection usings = Regex.Matches(code, @"using PTMod\.Features\.(\w+);");
                foreach (Match match in usings)
                {
                    string ns = match.Groups[1].Value;
                    Assert.True(ns == "DesignFlawRecalls" || ns == "RivalAcquisitions" || ns == "RivalPersonalities", file + " uses " + ns);
                }
            }
            Assert.Equal(2, allowed.Count);
        }

        [Test]
        public void TheHandlersOfTheEvents_AreMethodsOfThisFeature_SoTheCoreCanSkipThemWhileItIsOff()
        {
            // ModHooks.Raise attributes a handler to the feature of its declaring type's namespace
            foreach (string file in new[] { "FlawLink.cs", "AcquisitionLink.cs", "FlawResponseBias.cs", "RivalApiService.cs", "RivalConditions.cs" })
            {
                Assert.True(Regex.IsMatch(Code("Integration", file), @"namespace PTMod\.Features\.RivalPersonalities\s*\{"), file);
            }
            Assert.True(Regex.IsMatch(Code("Events", "RivalEventChooser.cs"), @"namespace PTMod\.Features\.RivalPersonalities\s*\{"));
            Assert.True(Regex.IsMatch(Code("Integration", "AcquisitionLink.cs"), @"private void OnDeal\(DealRecord deal\)"));
            Assert.True(Regex.IsMatch(Code("Integration", "FlawLink.cs"), @"private void OnFlawChanged\(FlawRecordView flaw\)"));
        }

        [Test]
        public void TheNewFolders_PatchNothingOfTheGame_ExceptTheCautionPatch()
        {
            foreach (string folder in new[] { "Integration", "Events", "Reactions" })
            {
                foreach (string path in SourceScan.Files(Path.Combine(FeatureFolder(), folder), ".cs"))
                {
                    string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                    Assert.False(Regex.IsMatch(code, @"\[ModPatch\("), Path.GetFileName(path) + " declares a patch");
                    Assert.False(Regex.IsMatch(code, @"HarmonyLib|\.Postfix|\.Prefix"), Path.GetFileName(path) + " touches Harmony");
                }
            }
        }

        [Test]
        public void TheContentOfTheDesign_IsComplete_ForTheExtensions()
        {
            ArchetypeCatalog catalog = RivalTestKit.LoadShippedCatalog();

            Assert.Equal(7, catalog.All.Count);
            Assert.Equal(13, catalog.Assignments.Count);
            Assert.Equal(24, catalog.Config.flawCaution.months);
            Assert.Equal(0.85f, catalog.Config.flawCaution.factor);
            Assert.Equal("Major", catalog.Config.flawCaution.minSeverity);
            Assert.Equal(400, catalog.Config.priceCut.externalMaxDays);
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "no '" + from + "'");
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            Assert.True(end > start, "no '" + to + "' after '" + from + "'");
            return text.Substring(start, end - start);
        }
    }
}
