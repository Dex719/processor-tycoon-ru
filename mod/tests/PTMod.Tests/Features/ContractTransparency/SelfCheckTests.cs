using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PTMod.Features.ContractTransparency;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-020 (FR-12 item 3): the check of the game's members at the start. The catalog is the table of design.md ("Самопроверка на старте"): the six methods the patches are on and the private members the
    /// code reads. The evaluator says what a missing member switches off; the test flag hides a member from the code that looks it up, so only the part that depends on it goes. What needs the game
    /// (that the members really exist in Assembly-CSharp.dll, that the core really lets the six methods go) is checked in the sources: the patches in the code are the patches of the catalog, the
    /// table of the core does not hold them, every private member is looked up through the one door that the flag guards.
    /// </summary>
    [Covers(typeof(MemberCatalog), typeof(GameMember), typeof(GameMemberKind), typeof(FeaturePart), typeof(MemberState), typeof(MemberKeys), typeof(MemberProbe),
        typeof(SelfCheckEvaluator), typeof(SelfCheckResult), typeof(SelfCheckFinding), typeof(MissingMemberFlags))]
    public class SelfCheckTests
    {
        private static readonly string[] SixTargets =
        {
            "Contract.AddOffer", "Contract.ChooseCpu", "ContractAutomation.SendOffers", "ContractWindow.Start", "ContractWindow.UpdateProbabilityText",
            "ContractNegotiationWindow.SignContract",
        };

        // design.md, "Самопроверка на старте": the private members.
        private static readonly string[] PrivateMembers =
        {
            "Contract.mipsToleranceMultiplier", "Contract.GetBestOffers", "Contract.CpuOffer", "Contract.CpuOffer.Cpu", "Contract.CpuOffer.Chance",
            "ContractWindow.currentContract", "ContractWindow.currentCpu", "ContractWindow.probabilityText", "ContractWindow.cpuFailedAtAnyRequirement", "ContractWindow.sendOfferButton",
            "ContractNegotiationWindow.currentProvider", "ContractNegotiationWindow.currentClient", "ContractNegotiationWindow.duration", "ContractNegotiationWindow.productionCapacity",
            "ContractNegotiationWindow.markupPrice", "ContractNegotiationWindow.providerFines", "ContractNegotiationWindow.clientFines", "ContractNegotiationWindow.exclusivity",
            "ContractNegotiationWindow.acceptanceScore",
        };

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency");
        }

        private static string Code(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string ClassOf(GameMember row)
        {
            string type = row.TypeName.Substring(row.TypeName.LastIndexOf('.') + 1);
            return type.Replace('+', '.');
        }

        private static Func<GameMember, MemberProbe> AllFound = row => MemberProbe.Found;

        private static Func<GameMember, MemberProbe> Missing(params string[] keys)
        {
            return row => keys.Contains(row.Key) ? MemberProbe.Of(MemberState.Missing, null) : MemberProbe.Found;
        }

        private static SelfCheckResult Evaluate(Func<GameMember, MemberProbe> probe)
        {
            return SelfCheckEvaluator.Evaluate(MemberCatalog.All, probe);
        }

        // ---- the catalog is the table of the design ----

        [Test]
        public void TheCatalog_HasTheSixTargetsAndTheNineteenMembersOfTheDesign()
        {
            Assert.Equal(SixTargets, MemberCatalog.PatchTargets.Select(r => r.Key).ToArray());
            Assert.Equal(PrivateMembers.OrderBy(k => k, StringComparer.Ordinal).ToArray(), MemberCatalog.PrivateMembers.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.Equal(25, MemberCatalog.All.Count);
            Assert.True(MemberCatalog.PatchTargets.All(r => r.Kind == GameMemberKind.Method), "a patch target is a method");
        }

        [Test]
        public void TheKeys_AreUnique_AndAreTheClassTheMemberAndNothingElse()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameMember row in MemberCatalog.All)
            {
                Assert.True(seen.Add(row.Key), "twice in the catalog: " + row.Key);
                Assert.Equal(ClassOf(row) + "." + row.Name, row.Key);
                Assert.True(row.Parts != null && row.Parts.Length > 0, row.Key + " names no part of the feature");
                Assert.NotNull(MemberCatalog.Find(row.Key), row.Key);
            }
            Assert.Null(MemberCatalog.Find("Contract.NoSuchThing"));
        }

        [Test]
        public void TheKeyConstants_AreTheKeysOfTheCatalog_AndTheOtherWayRound()
        {
            var constants = typeof(MemberKeys).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToList();

            Assert.Equal(constants.Count, constants.Distinct().Count());
            Assert.Equal(MemberCatalog.All.Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray(), constants.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        }

        [Test]
        public void TheRequiredPatchTargets_AreTheThreeOfTheFlow_AndTheOtherThreeAreOptional()
        {
            Assert.Equal(
                new[] { "Contract.AddOffer", "Contract.ChooseCpu", "ContractAutomation.SendOffers" },
                MemberCatalog.PatchTargets.Where(r => r.RequiredPatch).Select(r => r.Key).ToArray());
            Assert.True(MemberCatalog.PrivateMembers.All(r => !r.RequiredPatch), "only a patch can be required");
        }

        [Test]
        public void TheTargetsOfTheCatalog_AreTheTargetsOfThePatchesInTheCode_WithTheSameOptionalFlag()
        {
            var alias = new Dictionary<string, string>
            {
                { "GameContract", "Contract" }, { "GameContractAutomation", "ContractAutomation" }, { "GameContractWindow", "ContractWindow" },
                { "GameNegotiationWindow", "ContractNegotiationWindow" },
            };
            var attribute = new Regex(@"\[ModPatch\(\s*typeof\(\s*(?<type>\w+)\s*\)\s*,\s*""(?<method>\w+)""\s*,\s*PatchKind\.(?<kind>\w+)(?<rest>.*?)\)\]", RegexOptions.Compiled);

            var inCode = new Dictionary<string, bool>(StringComparer.Ordinal);   // key -> every patch of the method is Optional
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in attribute.Matches(code))
                {
                    string type = m.Groups["type"].Value;
                    Assert.True(alias.ContainsKey(type), "a patch on a class the catalog does not know: " + type + " in " + SourceScan.Relative(FeatureDir(), path));
                    string key = alias[type] + "." + m.Groups["method"].Value;
                    bool optional = Regex.IsMatch(m.Groups["rest"].Value, @"Optional\s*=\s*true");
                    bool all;
                    inCode[key] = inCode.TryGetValue(key, out all) ? all && optional : optional;
                }
            }

            Assert.Equal(SixTargets.OrderBy(k => k, StringComparer.Ordinal).ToArray(), inCode.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            foreach (GameMember row in MemberCatalog.PatchTargets)
            {
                Assert.Equal(!row.RequiredPatch, inCode[row.Key], row.Key + ": the Optional flag of the patches and RequiredPatch of the catalog disagree");
            }
        }

        [Test]
        public void NoTargetOfThePatches_IsInTheTableOfTheCore_SoModHooksIsReservedIsFalseForAllSix()
        {
            // ModHooks.IsReserved answers from ReservedTargets: a row per class and method. Read from the source of the core, so the test needs no game.
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Core", "Hooks", "ReservedTargets.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);
            var reserved = new List<KeyValuePair<string, string>>();
            foreach (Match m in Regex.Matches(code, @"(?:Channel|Service|Formula)\(\s*""(?<patch>[PF]\d+)""\s*,\s*""(?<type>[^""]+)""\s*,\s*""(?<method>[^""]+)"""))
            {
                reserved.Add(new KeyValuePair<string, string>(m.Groups["type"].Value, m.Groups["method"].Value));
            }
            Assert.True(reserved.Count >= 20, "the table of the core was read: " + reserved.Count + " rows");

            foreach (GameMember row in MemberCatalog.PatchTargets)
            {
                Assert.False(reserved.Any(r => r.Key == row.TypeName && r.Value == row.Name), row.Key + " is reserved by the core: the patch would be refused");
            }

            // GetBestOffers is the one the core keeps (P23): the feature calls it and never patches it.
            GameMember best = MemberCatalog.Find(MemberKeys.GetBestOffers);
            Assert.True(best.MustNotBePatched);
            Assert.True(reserved.Any(r => r.Key == best.TypeName && r.Value == best.Name), "the core's table still reserves GetBestOffers");
            Assert.True(MemberCatalog.All.Count(r => r.MustNotBePatched) == 1);
        }

        // ---- the evaluation ----

        [Test]
        public void WhenEverythingIsThere_TheCheckIsClean_AndNothingIsOff()
        {
            SelfCheckResult result = Evaluate(AllFound);

            Assert.True(result.IsClean);
            Assert.Equal(25, result.Checked);
            Assert.False(result.FeatureUnavailable);
            Assert.Equal(0, result.PartsOff.Count);
            foreach (FeaturePart part in (FeaturePart[])Enum.GetValues(typeof(FeaturePart))) Assert.True(result.IsPartOn(part), part.ToString());
            Assert.True(result.Summary.Contains("all 25 members"), result.Summary);
        }

        [Test]
        public void GetBestOffersMissing_SwitchesOffTheAnalysisAndTheChancesOfTheTenders_AndNothingElse()
        {
            SelfCheckResult result = Evaluate(Missing(MemberKeys.GetBestOffers));

            Assert.False(result.IsPartOn(FeaturePart.Analysis));
            Assert.False(result.IsPartOn(FeaturePart.TenderChances));
            foreach (FeaturePart part in new[] { FeaturePart.OfferGate, FeaturePart.TenderLog, FeaturePart.AutomationLog, FeaturePart.WindowUi, FeaturePart.SignQuestion })
            {
                Assert.True(result.IsPartOn(part), part + " must go on without GetBestOffers");
            }
            Assert.False(result.FeatureUnavailable, "the core does not refuse the feature for a private method");
            Assert.Equal(1, result.Findings.Count);
        }

        [Test]
        public void AFieldOfTheContractWindowMissing_SwitchesOffOnlyTheButtonsAndTheLabel()
        {
            foreach (string key in new[] { MemberKeys.WindowProbabilityText, MemberKeys.WindowContract, MemberKeys.WindowCpu, MemberKeys.WindowCpuFailed, MemberKeys.WindowUpdateProbability, MemberKeys.WindowStart })
            {
                SelfCheckResult result = Evaluate(Missing(key));
                Assert.Equal(new[] { FeaturePart.WindowUi }, result.PartsOff.ToArray(), key);
                Assert.False(result.FeatureUnavailable, key);
            }
        }

        [Test]
        public void AFieldOfTheNegotiationWindowOrItsSignMethodMissing_SwitchesOffOnlyTheQuestion()
        {
            foreach (GameMember row in MemberCatalog.All.Where(r => ClassOf(r) == "ContractNegotiationWindow"))
            {
                SelfCheckResult result = Evaluate(Missing(row.Key));
                Assert.Equal(new[] { FeaturePart.SignQuestion }, result.PartsOff.ToArray(), row.Key);
                Assert.False(result.FeatureUnavailable, row.Key);
            }
        }

        [Test]
        public void ARequiredTargetMissing_MakesTheFeatureUnavailable_LikeTheCoreDoes()
        {
            SelfCheckResult result = Evaluate(Missing(MemberKeys.AddOffer));

            Assert.True(result.FeatureUnavailable);
            Assert.False(result.IsPartOn(FeaturePart.OfferGate));
            Assert.True(result.Summary.Contains("Unavailable"), result.Summary);
            Assert.True(result.Findings[0].Text.Contains("the core makes the feature Unavailable"), result.Findings[0].Text);
        }

        [Test]
        public void ATargetReservedByTheCore_IsReportedAsSuch()
        {
            SelfCheckResult result = SelfCheckEvaluator.Evaluate(MemberCatalog.All,
                row => row.Key == MemberKeys.ChooseCpu ? MemberProbe.Of(MemberState.Reserved, "P99 channel x") : MemberProbe.Found);

            Assert.True(result.FeatureUnavailable);
            Assert.False(result.IsPartOn(FeaturePart.TenderLog));
            Assert.True(result.Findings[0].Text.Contains("reserved by the core"), result.Findings[0].Text);
            Assert.True(result.Findings[0].Text.Contains("P99 channel x"), result.Findings[0].Text);
        }

        [Test]
        public void ATypeThatChanged_IsAFindingLikeAMissingMember()
        {
            SelfCheckResult result = SelfCheckEvaluator.Evaluate(MemberCatalog.All,
                row => row.Key == MemberKeys.OfferChance ? MemberProbe.Of(MemberState.WrongType, "it is System.Double, expected System.Int32") : MemberProbe.Found);

            Assert.False(result.IsPartOn(FeaturePart.Analysis));
            Assert.False(result.IsPartOn(FeaturePart.TenderChances));
            Assert.True(result.Findings[0].Text.Contains("is not as expected") && result.Findings[0].Text.Contains("System.Double"), result.Findings[0].Text);
        }

        [Test]
        public void TheAdvisoryField_IsMentionedAndSwitchesNothingOff()
        {
            SelfCheckResult result = Evaluate(Missing(MemberKeys.WindowSendButton));

            Assert.False(result.IsClean);
            Assert.Equal(0, result.PartsOff.Count);
            Assert.True(result.Findings[0].Text.Contains("nothing is switched off"), result.Findings[0].Text);
        }

        [Test]
        public void APatchOnTheMethodThatMayOnlyBeCalled_IsAWarningAndSwitchesNothingOff()
        {
            SelfCheckResult result = SelfCheckEvaluator.Evaluate(MemberCatalog.All,
                row => row.Key == MemberKeys.GetBestOffers ? MemberProbe.Of(MemberState.Patched, null) : MemberProbe.Found);

            Assert.False(result.IsClean);
            Assert.Equal(0, result.PartsOff.Count);
            Assert.False(result.FeatureUnavailable);
            Assert.True(result.Findings[0].Text.Contains("may only be called"), result.Findings[0].Text);
        }

        [Test]
        public void AProbeThatThrows_IsAMissingMember_AndTheCheckGoesOn()
        {
            SelfCheckResult result = SelfCheckEvaluator.Evaluate(MemberCatalog.All,
                row =>
                {
                    if (row.Key == MemberKeys.DealScore) throw new InvalidOperationException("boom");
                    return MemberProbe.Found;
                });

            Assert.Equal(25, result.Checked);
            Assert.Equal(1, result.Findings.Count);
            Assert.False(result.IsPartOn(FeaturePart.SignQuestion));
            Assert.True(result.Findings[0].Text.Contains("InvalidOperationException"), result.Findings[0].Text);
        }

        [Test]
        public void EveryMemberAloneMissing_SwitchesOffExactlyTheParts_ItsRowNames()
        {
            foreach (GameMember row in MemberCatalog.All)
            {
                SelfCheckResult result = Evaluate(Missing(row.Key));
                FeaturePart[] expected = row.Advisory ? new FeaturePart[0] : row.Parts.Distinct().OrderBy(p => p).ToArray();

                Assert.Equal(expected, result.PartsOff.ToArray(), row.Key);
                Assert.Equal(1, result.Findings.Count, row.Key);
                Assert.Equal(row.IsPatchTarget && row.RequiredPatch, result.FeatureUnavailable, row.Key);
                Assert.True(result.Findings[0].Text.StartsWith(row.Key, StringComparison.Ordinal), result.Findings[0].Text);
            }
        }

        [Test]
        public void TheFindingsKeepTheOrderOfTheCatalog_AndThePartsOffAreSorted()
        {
            SelfCheckResult result = Evaluate(Missing(MemberKeys.DealScore, MemberKeys.WindowCpu, MemberKeys.AddOffer, MemberKeys.OfferChance));

            Assert.Equal(
                new[] { MemberKeys.AddOffer, MemberKeys.OfferChance, MemberKeys.WindowCpu, MemberKeys.DealScore },
                result.Findings.Select(f => f.Member.Key).ToArray());
            Assert.Equal(
                new[] { FeaturePart.OfferGate, FeaturePart.WindowUi, FeaturePart.SignQuestion, FeaturePart.Analysis, FeaturePart.TenderChances },
                result.PartsOff.ToArray());
            Assert.True(result.Summary.StartsWith("4 of 25 members", StringComparison.Ordinal), result.Summary);
        }

        [Test]
        public void TheEvaluator_RefusesNoArguments()
        {
            Assert.Throws<ArgumentNullException>(() => SelfCheckEvaluator.Evaluate(null, AllFound));
            Assert.Throws<ArgumentNullException>(() => SelfCheckEvaluator.Evaluate(MemberCatalog.All, null));
            Assert.Equal(0, SelfCheckEvaluator.Evaluate(new GameMember[0], AllFound).Checked);
        }

        [Test]
        public void EveryPartIsDescribed_ForTheLog()
        {
            foreach (FeaturePart part in (FeaturePart[])Enum.GetValues(typeof(FeaturePart)))
            {
                string text = MemberCatalog.Describe(part);
                Assert.True(!string.IsNullOrWhiteSpace(text) && text != part.ToString(), part + " has no description");
            }
        }

        // ---- the test flag ----

        [Test]
        public void TheFlag_IsNamedByTheKey_AndReadsThroughTheSourceOfTheFlags()
        {
            Assert.Equal("ct.missing.Contract.GetBestOffers", MissingMemberFlags.FlagOf(MemberKeys.GetBestOffers));

            var flags = new HashSet<string> { MissingMemberFlags.FlagOf(MemberKeys.GetBestOffers) };
            Func<string, bool> source = flags.Contains;
            Assert.True(MissingMemberFlags.IsHidden(MemberKeys.GetBestOffers, source));
            Assert.False(MissingMemberFlags.IsHidden(MemberKeys.OfferCpu, source));
            Assert.False(MissingMemberFlags.IsHidden(null, source));
            Assert.False(MissingMemberFlags.IsHidden(MemberKeys.GetBestOffers, null), "no source of flags (a release build): nothing is hidden");
            Assert.False(MissingMemberFlags.IsHidden(string.Empty, source));
        }

        [Test]
        public void HidingAMemberWithTheFlag_SwitchesOffOnlyThePartsThatDependOnIt()
        {
            // The check as the game runs it: a probe that asks the flag first (GameMembers.IsHidden) for the rows the code looks up by reflection.
            foreach (GameMember row in MemberCatalog.All.Where(r => r.Reflected))
            {
                var hidden = new HashSet<string> { MissingMemberFlags.FlagOf(row.Key) };
                SelfCheckResult result = SelfCheckEvaluator.Evaluate(MemberCatalog.All,
                    r => r.Reflected && MissingMemberFlags.IsHidden(r.Key, hidden.Contains) ? MemberProbe.Of(MemberState.Missing, "hidden by the debug flag") : MemberProbe.Found);

                Assert.Equal(row.Parts.Distinct().OrderBy(p => p).ToArray(), result.PartsOff.ToArray(), row.Key);
                var others = ((FeaturePart[])Enum.GetValues(typeof(FeaturePart))).Except(row.Parts);
                foreach (FeaturePart part in others) Assert.True(result.IsPartOn(part), row.Key + " must not switch off " + part);
                Assert.False(result.FeatureUnavailable, row.Key + ": a hidden member never makes the feature Unavailable");
            }
        }

        [Test]
        public void TheTargetsOnlyTheCoreResolves_CannotBeHidden_SoTheFlagDoesNotLieAboutThem()
        {
            foreach (string key in new[] { MemberKeys.AddOffer, MemberKeys.ChooseCpu, MemberKeys.SendOffers, MemberKeys.WindowStart })
            {
                Assert.False(MemberCatalog.Find(key).Reflected, key);
            }
            // The two the mod also calls itself (the refresh of the label, the signature after "Sign") are looked up by reflection.
            Assert.True(MemberCatalog.Find(MemberKeys.WindowUpdateProbability).Reflected);
            Assert.True(MemberCatalog.Find(MemberKeys.SignContract).Reflected);
        }

        // ---- the code: one door to the members ----

        [Test]
        public void TheMembers_AreLookedUpOnlyThroughGameMembers_AndTheSelfCheck()
        {
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string relative = SourceScan.Relative(FeatureDir(), path);
                if (relative == "GameMembers.cs" || relative == "ContractSelfCheck.cs") continue;

                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                Assert.False(Regex.IsMatch(code, @"\bAccessTools\s*\."), relative + " reaches into the game with AccessTools: use GameMembers, so the check knows the member and the flag can hide it");
                Assert.False(Regex.IsMatch(code, @"\.(GetField|GetMethod|GetProperty|GetNestedType)\("), relative + " looks a member up by reflection itself");
            }
        }

        [Test]
        public void EveryMemberTheCodeLooksUp_HasItsRowInTheCatalog_AndEveryReflectedRowIsLookedUp()
        {
            var keyOfConstant = typeof(MemberKeys).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue());

            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (string file in new[] { "ContractInspector.cs", "UI/ContractWindowAugmenter.cs", "Patches/BusinessSignGate.cs" })
            {
                string code = Code(file);
                Assert.True(code.Contains("GameMembers."), file + " looks its members up through GameMembers");
                foreach (Match m in Regex.Matches(code, @"MemberKeys\.(\w+)"))
                {
                    Assert.True(keyOfConstant.ContainsKey(m.Groups[1].Value), file + " names a key that does not exist: " + m.Groups[1].Value);
                    used.Add(keyOfConstant[m.Groups[1].Value]);
                }
            }

            string[] expected = MemberCatalog.All.Where(r => r.Reflected).Select(r => r.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, used.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        }

        [Test]
        public void TheMembersAreChecked_WhenTheFeatureStarts_AndTheOldCheckIsGone()
        {
            string feature = Code("ContractTransparencyFeature.cs");
            Assert.True(Regex.IsMatch(feature, @"_selfCheck\s*=\s*ContractSelfCheck\.Run\(\)"), "OnEnabled runs the check");
            Assert.False(File.Exists(Path.Combine(FeatureDir(), "PatchTargetsCheck.cs")), "the check of three targets was replaced by the one of the catalog");

            string check = Code("ContractSelfCheck.cs");
            Assert.True(check.Contains("SelfCheckEvaluator.Evaluate(MemberCatalog.All"), "the whole catalog is checked");
            Assert.True(check.Contains("ModHooks.IsReserved("), "the patch targets are asked of the core");
            Assert.True(Regex.IsMatch(check, @"catch\s*\(\s*Exception"), "and the check never throws");
        }
    }
}
