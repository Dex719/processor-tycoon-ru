using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-007...TSK-010: what can be said about the patches of the feature without the game, read from the sources (the patch classes touch Unity and the game, so no test loads them): the
    /// table of the patches of design.md is the table in the code, the feature patches none of the methods the core keeps for itself, every body runs through the guard, the prefix of
    /// <c>AddOffer</c> goes first and falls back to the game, and the finalizer of the automation lowers its flag before anything that can throw.
    /// </summary>
    public class PatchSourceTests
    {
        private static readonly Regex PatchAttribute = new Regex(
            @"\[ModPatch\(\s*typeof\(\s*(?<type>\w+)\s*\)\s*,\s*""(?<method>\w+)""\s*,\s*PatchKind\.(?<kind>\w+)(?<rest>[^\]]*)\]", RegexOptions.Compiled);

        private static string FeatureDir()
        {
            return Path.Combine(SourceScan.SrcDirOrFail(), "Features", "ContractTransparency");
        }

        private static string Source(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static List<string> AllPatches()
        {
            var found = new List<string>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match m in PatchAttribute.Matches(code)) found.Add(m.Groups["method"].Value + "/" + m.Groups["kind"].Value);
            }
            found.Sort(StringComparer.Ordinal);
            return found;
        }

        [Test]
        public void TheFeaturePatches_ExactlyTheTenOfTheDesignTable()
        {
            // design.md, "Патчи Harmony": AddOffer (prefix, postfix), SendOffers (prefix, finalizer), ChooseCpu (prefix, postfix) - the flow of the game (TSK-007...TSK-009);
            // ContractWindow.Start and UpdateProbabilityText (postfixes, TSK-011); ContractNegotiationWindow.SignContract (prefix, postfix, TSK-016).
            List<string> patches = AllPatches();

            Assert.Equal(
                new[]
                {
                    "AddOffer/Postfix", "AddOffer/Prefix", "ChooseCpu/Postfix", "ChooseCpu/Prefix", "SendOffers/Finalizer", "SendOffers/Prefix",
                    "SignContract/Postfix", "SignContract/Prefix", "Start/Postfix", "UpdateProbabilityText/Postfix",
                },
                patches.ToArray());
        }

        [Test]
        public void NoPatchNamesAMethodTheCoreKeepsForItself()
        {
            // GetBestOffers is reserved by the core (P23): the feature only calls it. ChanceToSign is only called, DefineChance is the core's patch P8.
            foreach (string forbidden in new[] { "GetBestOffers", "ChanceToSign", "DefineChance", "FailsAtAnyRequirement" })
            {
                Assert.False(AllPatches().Any(p => p.StartsWith(forbidden + "/", StringComparison.Ordinal)), "a patch on " + forbidden);
            }
        }

        [Test]
        public void TheSourcesOfThePatches_AreInThePatchesFolder_AndEachFileGuardsEveryBody()
        {
            string patches = Path.Combine(FeatureDir(), "Patches");
            Assert.True(Directory.Exists(patches), "Patches/ is the folder of the patch classes");

            foreach (string path in SourceScan.Files(patches, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                int handlers = PatchAttribute.Matches(code).Count;
                int guards = Regex.Matches(code, @"ContractTransparencyFeature\.Guarded\(").Count;
                Assert.True(guards >= handlers, SourceScan.Relative(FeatureDir(), path) + ": " + handlers + " patch(es) but " + guards + " guarded body(ies)");
            }
        }

        [Test]
        public void ThePrefixOfAddOffer_GoesFirst_AndFallsBackToTheGame()
        {
            string code = Source("Patches/OfferGate.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModPatch\([^\]]*""AddOffer""[^\]]*PatchKind\.Prefix[^\]]*Priority\s*=\s*800\b"), "Priority.First (800) on the prefix");
            Assert.True(Regex.IsMatch(code, @"bool\s+run\s*=\s*true\s*;"), "the result starts as true: the original runs");
            Assert.True(Regex.IsMatch(code, @"if\s*\(\s*!ok\s*\)\s*\{[^}]*run\s*=\s*true\s*;"), "a failed prefix gives true (NFR-2)");
            Assert.True(Regex.IsMatch(code, @"track\s*=\s*default\(OfferTrack\)\s*;\s*run\s*=\s*true"), "and records nothing");
        }

        [Test]
        public void TheGate_SendsTheConfirmedOfferUnderTheBypass_AndRefreshesTheWindow()
        {
            string code = Source("Patches/OfferGate.cs");

            int bypassOn = code.IndexOf("BypassScope = true;", StringComparison.Ordinal);
            int add = code.IndexOf("contract.AddOffer(cpuId);", StringComparison.Ordinal);
            int bypassOff = code.IndexOf("BypassScope = wasBypass;", StringComparison.Ordinal);
            int refresh = code.IndexOf("RefreshContractWindow();", StringComparison.Ordinal);

            Assert.True(bypassOn > 0 && add > bypassOn && bypassOff > add, "AddOffer is called between raising and lowering the bypass");
            Assert.True(Regex.IsMatch(code, @"finally\s*\{\s*BypassScope\s*=\s*wasBypass\s*;"), "the bypass is lowered in a finally: a throw cannot leave it up");
            Assert.True(refresh > bypassOff, "the window is redrawn after the call");
            Assert.True(code.Contains("TriggerUIUpdate()"), "the public method of the window");
        }

        [Test]
        public void ThePostfixOfAddOffer_RecordsOnlyWhenTheListGrew()
        {
            string code = Source("Patches/OfferGate.cs");

            // The comparison is OfferEventRules.WasSent(before, after) (tested on its own, TSK-018): the list must have grown.
            Assert.True(Regex.IsMatch(code, @"!OfferEventRules\.WasSent\(\s*track\.CountBefore\s*,\s*contract\.OffersIDs\.Count\s*\)\s*\)\s*return"), "nothing is recorded when OffersIDs did not grow");
        }

        [Test]
        public void TheFinalizerOfTheAutomation_LowersTheFlagFirst_AndHandsTheExceptionOn()
        {
            string code = Source("Patches/AutomationLogger.cs");
            Match finalizer = Regex.Match(code, @"Exception\s+SendOffersFinalizer\([^)]*\)\s*\{(?<body>.*?)\n        \}", RegexOptions.Singleline);

            Assert.True(finalizer.Success, "SendOffersFinalizer is in the file");
            string body = finalizer.Groups["body"].Value.TrimStart();
            Assert.True(body.StartsWith("OfferGate.AutomationScope = false;", StringComparison.Ordinal), "the first statement lowers the flag, outside any guard:\n" + body);
            Assert.True(Regex.IsMatch(body, @"return\s+__exception\s*;"), "the exception of the game goes on unchanged (the mod hides nothing)");
        }

        [Test]
        public void TheFlagOfTheAutomation_IsRaisedInThePrefix_OnlyAfterTheOldOneIsGone()
        {
            string code = Source("Patches/AutomationLogger.cs");

            Assert.True(code.Contains("OfferGate.AutomationScope = true;"));
            Assert.Equal(1, Regex.Matches(code, @"OfferGate\.AutomationScope\s*=\s*true").Count, "one place raises it");
        }

        [Test]
        public void TheRecorder_AsksTheGameForTheChances_WithTheMethodTheInspectorHolds_AndSkipsFastForwardAndLoading()
        {
            string code = Source("Patches/TenderRecorder.cs");

            Assert.True(code.Contains("GameChances("), "the chances of the draw come from a second call of GetBestOffers through the inspector");
            Assert.True(Regex.Matches(code, @"ModTicks\.IsFastForwarding\s*\|\|\s*ModTicks\.IsLoading").Count >= 2, "both the prefix and the postfix leave a fast-forward and a load alone");
            Assert.False(code.Contains("AccessTools"), "the private method is reached in one place only, the inspector");
        }

        [Test]
        public void TheMailer_IsSubscribedToTheDayWithoutOptions_AndRegistersItsAction()
        {
            string feature = Source("ContractTransparencyFeature.cs");

            Assert.True(Regex.IsMatch(feature, @"ctx\.OnTick\(\s*TickPhase\.DayPassed\s*,\s*_mailer\.OnDayPassed\s*\)"), "no RunDuringFastForward, no RunDuringLoading");
            Assert.True(Regex.IsMatch(feature, @"ModMail\.RegisterActionHandler\(\s*FeatureId\s*,\s*_mailer\.OnMailAction\s*\)"));
            Assert.True(Regex.IsMatch(feature, @"ctx\.SaveParticipant\(\s*_log\s*\)"), "the log is a section of the side file");
            foreach (string patchClass in new[] { "OfferGate", "TenderRecorder", "AutomationLogger" })
            {
                Assert.True(Regex.IsMatch(feature, @"ctx\.Patch\(\s*typeof\(\s*" + patchClass + @"\s*\)\s*\)"), patchClass + " is declared");
            }
        }

        [Test]
        public void TheLetter_HasTheTagPriorityAndAction_OfTheSpec()
        {
            string code = Source("TenderMailer.cs");

            Assert.True(code.Contains("\"ct:tenders:\""), "Tag = ct:tenders:<yyyy-MM-dd>");
            Assert.True(code.Contains("LogDates.Write(today)"), "the date in the tag is the day of the draws");
            Assert.True(Regex.IsMatch(code, @"Priority\s*=\s*MailPriority\.Normal"), "priority Normal");
            Assert.True(Regex.IsMatch(code, @"new\s+ModMailAction\s*\{[^}]*Id\s*=\s*ActionOpenLog[^}]*Repeatable\s*=\s*true"), "the action open-log is repeatable");
            Assert.True(code.Contains("\"open-log\""));
            Assert.False(code.Contains("Choices"), "the letter has no choices");
            Assert.True(Regex.IsMatch(code, @"lost\s*>\s*0\s*&&\s*ContractTransparencySettings\.NotifyTenderLoss"), "one notification, only for a loss and only when the setting is on");
            Assert.Equal(1, Regex.Matches(code, @"ModNotify\.Show\(").Count);
        }
    }
}
