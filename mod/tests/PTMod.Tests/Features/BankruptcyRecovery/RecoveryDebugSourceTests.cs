using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-019 and TSK-014, read from the sources: the debug actions of the hub page exist in a debug build only and only behind the flag of <c>ptmod_settings.json</c>
    /// ("without the flag and in a release build there are no actions"), and the early repayment of the "Loans" tab takes the share of the debt from the slider (FR-10 item 3).
    /// These tests do not need <c>PTMOD_DEBUG</c> themselves: they read the files, so a release run of the tests checks the same thing.
    /// </summary>
    public class RecoveryDebugSourceTests
    {
        private static readonly string[] DebugFiles = { "Debug/RecoveryDebugActions.cs", "Logic/RecoveryDebugEngine.cs" };

        private static string FeatureDir()
        {
            string dir = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery");
            Assert.True(Directory.Exists(dir), "the folder of the feature is missing: " + dir);
            return dir;
        }

        private static string Raw(string relative)
        {
            string path = Path.Combine(FeatureDir(), relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "missing: " + path);
            return SourceScan.ReadText(path).Replace("\r\n", "\n");
        }

        private static string Code(string relative)
        {
            return SourceScan.Strip(Raw(relative), false);
        }

        // ---- the debug actions exist in a debug build only ----

        [Test]
        public void TheDebugFiles_AreWhollyInsideOneDebugSection_SoAReleaseBuildHasNoTraceOfThem()
        {
            foreach (string file in DebugFiles)
            {
                string text = Raw(file).Trim();
                Assert.True(text.StartsWith("#if PTMOD_DEBUG\n", StringComparison.Ordinal), file + " must start with '#if PTMOD_DEBUG'");
                Assert.True(text.EndsWith("\n#endif", StringComparison.Ordinal), file + " must end with '#endif'");
                Assert.Equal(1, Regex.Matches(text, @"^\s*#if\b", RegexOptions.Multiline).Count, file + ": one section, not several");
                Assert.False(Regex.IsMatch(text, @"^\s*#else\b", RegexOptions.Multiline), file + ": no #else branch");
            }
        }

        [Test]
        public void NothingOutsideADebugSection_ReachesTheDebugActions()
        {
            var debugName = new Regex(@"\b(?:RecoveryDebugActions|RecoveryDebugEngine|DebugOutcome|DebugStatus|DebugKind)\b|""debug\.(?:balance|days|recovery)""");
            var offenders = new List<string>();
            int inside = 0;

            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                string relative = SourceScan.Relative(FeatureDir(), path).Replace('\\', '/');
                if (Array.IndexOf(DebugFiles, relative) >= 0) continue;

                var open = new Stack<bool>();   // one entry per #if: is it the debug one
                string[] lines = SourceScan.ReadText(path).Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("#if", StringComparison.Ordinal))
                    {
                        open.Push(line == "#if PTMOD_DEBUG");
                        continue;
                    }
                    if (line.StartsWith("#endif", StringComparison.Ordinal))
                    {
                        if (open.Count > 0) open.Pop();
                        continue;
                    }
                    if (line.StartsWith("//", StringComparison.Ordinal) || !debugName.IsMatch(line)) continue;

                    bool debugSection = false;
                    foreach (bool entry in open) debugSection |= entry;
                    if (debugSection) inside++;
                    else offenders.Add(relative + ":" + (i + 1) + ": " + line);
                }
            }

            Assert.True(offenders.Count == 0, "the debug actions are used outside '#if PTMOD_DEBUG':\n  " + string.Join("\n  ", offenders));
            Assert.True(inside >= 2, "the hub page and the table of the journal names reach the actions inside debug sections (found " + inside + ")");
        }

        [Test]
        public void TheHubPage_BuildsTheBlock_InsideADebugSection_OnceAtTheEnd()
        {
            string page = Raw("UI/RecoveryHubPage.cs");

            Assert.Equal(1, Regex.Matches(page, @"RecoveryDebugActions\s*\.\s*Build\(page\)").Count, "one call");
            Assert.True(Regex.IsMatch(page, @"#if PTMOD_DEBUG\s*\n\s*RecoveryDebugActions\.Build\(page\);[^\n]*\n#endif"), "and it is inside the debug section");
            Assert.True(page.IndexOf("RecoveryDebugActions.Build", StringComparison.Ordinal) > page.IndexOf("rec.menu.open", StringComparison.Ordinal),
                "the block comes after the button of the menu: the page of a release build is the same page");
        }

        // ---- only with the flag ----

        [Test]
        public void TheBlock_IsBuiltOnlyWithTheFlag_AndEveryButtonChecksItAgain()
        {
            string code = Code("Debug/RecoveryDebugActions.cs");

            Assert.True(code.Contains("internal const string Flag = \"debug.recovery\";"), "the flag of the section debug of ptmod_settings.json");
            Assert.True(Regex.IsMatch(code, @"get\s*\{\s*return\s+ModSettings\.Debug\(Flag\);\s*\}"), "read through the core, which answers false in a release build");

            string build = Between(code, "internal static void Build(RectTransform page)", "private static void SetBalance(");
            Assert.True(build.Contains("!IsOn"), "the block is not built without the flag");
            Assert.True(build.IndexOf("!IsOn", StringComparison.Ordinal) < build.IndexOf("ModUI.", StringComparison.Ordinal), "and the check comes before the first widget");

            string run = Between(code, "private static void Run(", "private static string Describe(");
            Assert.True(run.Contains("!IsOn"), "a button of a page older than the flag does nothing");
            Assert.True(run.Contains("ModFeatures.IsActive(BankruptcyRecoveryFeature.FeatureId)"), "and only while the feature is on");
            Assert.True(run.Contains("ModHooks.Guard(BankruptcyRecoveryFeature.FeatureId"), "under the guard of the feature");
        }

        [Test]
        public void TheBlock_HasTheSevenButtonsOfTheAcceptance_AndNoOtherAction()
        {
            string code = Code("Debug/RecoveryDebugActions.cs");

            Assert.True(code.Contains("foreach (double share in RecoveryDebugEngine.BalanceShares)"), "'Balance = k × D' from the table of the engine");
            Assert.True(code.Contains("foreach (int count in RecoveryDebugEngine.DefaultDays)"), "'DaysInDefault = N' from the table of the engine");
            Assert.Equal(2, Regex.Matches(code, @"ModUI\.Button\(").Count, "two loops, two kinds of buttons");
            Assert.True(code.Contains("engine.SetBalanceShare(share)") && code.Contains("engine.SetDaysInDefault(days)"), "each button calls its action of the engine");
            Assert.False(Regex.IsMatch(code, @"\.(?:AddMoney|SpendMoney|EarnMoney)\s*\(|\.DaysInDefault\s*="), "the page moves nothing itself: the engine does, through the ledger and the finances");
        }

        // ---- the early repayment (TSK-014, FR-10 item 3) ----

        [Test]
        public void TheEarlyRepayment_TakesTheShareFromTheSlider_AndAsksBeforeItPays()
        {
            string loans = Code("UI/RecoveryWindow.Loans.cs");

            Assert.True(Regex.IsMatch(loans, @"ModUI\.Slider\(_body,\s*L\(""rec\.loans\.prepay\.share""\),\s*AmountSteps\.PrepayMinPercent,\s*AmountSteps\.MaxPercent"), "the slider of the share of the debt");
            Assert.True(loans.Contains("AmountSteps.PrepayAmount(loan.Outstanding, _prepayPercent)"), "the sum of a button is the share of the debt of its loan");

            string onPrepay = Between(loans, "private static void OnPrepay(string loanId)", "private static bool FindLoan(");
            Assert.True(onPrepay.Contains("AmountSteps.PrepayAmount(loan.Outstanding, _prepayPercent)"), "the same sum is paid that the button showed");
            Assert.True(onPrepay.Contains("Prepay(RecoveryViews.KindName(loan.Kind.ToString()), amount, loan.Outstanding - amount)"), "the question shows what is still owed afterwards");
            Assert.True(onPrepay.IndexOf("Ask(plan,", StringComparison.Ordinal) < onPrepay.IndexOf("now.Loans.Prepay(loanId, amount)", StringComparison.Ordinal), "and the money moves only after the 'Yes'");
            Assert.False(onPrepay.Contains("Prepay(RecoveryViews.KindName(loan.Kind.ToString()), amount, 0.0)"), "no longer 'all of it' only");
        }

        [Test]
        public void TheEarlyRepayment_Slider_RedrawsEveryButtonOfTheTable()
        {
            string loans = Code("UI/RecoveryWindow.Loans.cs");
            string share = Between(loans, "private static void OnPrepayShare(float value)", "private static void OnPrepay(");

            Assert.True(share.Contains("AmountSteps.ClampPercent((int)Math.Round(value), AmountSteps.PrepayMinPercent)"), "a share between the lowest and the whole");
            Assert.True(share.Contains("RecoveryUi.SetCaption(") && share.Contains("RecoveryUi.SetEnabled(") && share.Contains("PrepayReason(feature, amount)"), "caption and reason follow the share");
            Assert.True(loans.Contains("PrepayButtons.Clear();"), "the buttons of a rebuilt tab are forgotten with it");
        }

        // Text between the first occurrence of 'from' and the next 'to' after it; fails with a clear message when a marker is missing.
        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal);
            Assert.True(start >= 0, "not found: " + from);
            int end = text.IndexOf(to, start + from.Length, StringComparison.Ordinal);
            Assert.True(end > start, "not found after '" + from + "': " + to);
            return text.Substring(start, end - start);
        }
    }
}
