using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-007 to TSK-012 (FR-2, FR-11, NFR-2): the seams of the game-facing code that cannot be loaded here, read from the sources. The patches are the four of the
    /// design with the right kinds, each guarded and checked for an active feature; the feature registers the ledger first and wires the ticks and the events;
    /// the money of the player moves in one place; nothing touches the AI companies or the channels of <c>ModModifiers</c> except to read one; no text is typed
    /// into the code. The logic behind these seams has its own tests.
    /// </summary>
    public class RecoveryWiringTests
    {
        private static string FeatureDir()
        {
            string dir = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery");
            Assert.True(Directory.Exists(dir), "the folder of the feature is missing: " + dir);
            return dir;
        }

        private static string Code(params string[] relative)
        {
            string path = Path.Combine(FeatureDir(), Path.Combine(relative));
            Assert.True(File.Exists(path), "the file is missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static List<KeyValuePair<string, string>> AllCode(bool stripStrings)
        {
            var found = new List<KeyValuePair<string, string>>();
            foreach (string path in SourceScan.Files(FeatureDir(), ".cs"))
            {
                found.Add(new KeyValuePair<string, string>(SourceScan.Relative(FeatureDir(), path).Replace('\\', '/'), SourceScan.Strip(SourceScan.ReadText(path), stripStrings)));
            }
            Assert.True(found.Count > 20, "too few sources found: " + found.Count);
            return found;
        }

        // ---- the patches ----

        [Test]
        public void ThePatches_AreTheFourOfTheDesign_WithTheirKinds()
        {
            string code = Code("Patches", "BankruptcyPatches.cs");

            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameBankruptcyHandler\),\s*""TickBankruptcies"",\s*PatchKind\.Prefix,\s*Priority\s*=\s*HighPriority\)\]"), "P1: prefix of TickBankruptcies, high priority");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameBankruptcyHandler\),\s*""TickBankruptcies"",\s*PatchKind\.Postfix\)\]"), "P2: postfix of TickBankruptcies");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameBankruptcyHandler\),\s*""TriggerDefault"",\s*PatchKind\.Postfix\)\]"), "P3: postfix of TriggerDefault");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameBankruptcyWarningWindow\),\s*""Open"",\s*PatchKind\.Postfix,\s*Optional\s*=\s*true\)\]"), "P4: optional postfix of the warning window");
            Assert.Equal(4, Regex.Matches(code, @"\[ModPatch\(").Count, "four patches and no other");
            Assert.False(code.Contains("PatchKind.Transpiler"), "the transpiler of TickBankruptcies is the core's (P10)");
            Assert.False(code.Contains("PatchKind.Finalizer"));
            Assert.True(Regex.IsMatch(code, @"HighPriority\s*=\s*600"), "Harmony's Priority.High");
        }

        [Test]
        public void EveryPatch_ChecksTheFeatureIsActive_AndRunsUnderTheGuard()
        {
            string code = Code("Patches", "BankruptcyPatches.cs");
            string[] bodies =
            {
                "private static bool TickPrefix()", "private static void TickPostfix()",
                "private static void TriggerPostfix(ICompany company)", "private static void WarningOpenPostfix(GameBankruptcyWarningWindow __instance)",
            };
            foreach (string signature in bodies)
            {
                int start = code.IndexOf(signature, StringComparison.Ordinal);
                Assert.True(start >= 0, "the patch method is missing: " + signature);
                int end = code.IndexOf("\n        }", start, StringComparison.Ordinal);
                string body = code.Substring(start, end - start);
                Assert.True(body.Contains("ModFeatures.IsActive(FeatureId)"), signature + " must check the state of the feature first");
                Assert.True(body.Contains("ModHooks.Guard(FeatureId,"), signature + " must run its work under ModHooks.Guard");
            }
        }

        [Test]
        public void TheFirstPatch_AlwaysLetsTheOriginalRun_AndTheThirdIsForThePlayerOnly()
        {
            string code = Code("Patches", "BankruptcyPatches.cs");
            int start = code.IndexOf("private static bool TickPrefix()", StringComparison.Ordinal);
            int end = code.IndexOf("\n        }", start, StringComparison.Ordinal);
            string prefix = code.Substring(start, end - start);
            Assert.Equal(1, Regex.Matches(prefix, @"\breturn\b").Count, "one return in the prefix");
            Assert.True(prefix.Contains("return true;"), "the prefix never skips the original (a failure is the vanilla way)");

            int third = code.IndexOf("private static void TriggerPostfix(ICompany company)", StringComparison.Ordinal);
            int thirdEnd = code.IndexOf("\n        }", third, StringComparison.Ordinal);
            string player = code.Substring(third, thirdEnd - third);
            Assert.True(player.Contains("company.IsPlayer"), "P3 reacts to the player's default only (FR-11)");
        }

        [Test]
        public void TheWarningButton_IsMadeOncePerScene()
        {
            string code = Code("Patches", "WarningWindowButton.cs");
            Assert.True(code.Contains("if (_button != null) return;"), "a button that is alive is not made twice");
            Assert.True(code.Contains("ModUI.Button("), "the button is a clone of the game's own, made by ModUI");
            Assert.True(code.Contains("\"rec.menu.open\""));
        }

        // ---- the entry point ----

        [Test]
        public void TheConstructor_RegistersTheLedgerBackend_BeforeAnythingElse()
        {
            string code = Code("BankruptcyRecoveryFeature.cs");
            int ctor = code.IndexOf("public BankruptcyRecoveryFeature()", StringComparison.Ordinal);
            Assert.True(ctor >= 0);
            int register = code.IndexOf("CapitalLedger.SetBackend(new CapitalLedgerBackend(", ctor, StringComparison.Ordinal);
            int services = code.IndexOf("_loans = new LoanService(", ctor, StringComparison.Ordinal);
            Assert.True(register > ctor && register < services, "the ledger works with the feature switched off, so the backend is registered in the constructor");

            int registerMethod = code.IndexOf("public void Register(FeatureContext ctx)", StringComparison.Ordinal);
            Assert.False(code.Substring(registerMethod).Contains("SetBackend"), "Register does not run for a feature the player switched off");
            Assert.False(code.Contains("SetBackend(null"), "the backend is never taken back");
        }

        [Test]
        public void Register_WiresThePatchesTheTicksAndTheEvents()
        {
            string code = Code("BankruptcyRecoveryFeature.cs");

            Assert.True(code.Contains("ctx.Patch(typeof(BankruptcyPatches))"));
            Assert.True(code.Contains("ctx.OnTick(TickPhase.LateTick, _creditLine.OnDay)"), "the auto draw runs on the day, before DayPassed");
            Assert.True(code.Contains("ctx.OnTick(TickPhase.LateMonthPassed, _loans.OnMonth)"));
            Assert.True(code.Contains("ctx.OnTick(TickPhase.LateMonthPassed, _creditLine.OnMonth)"));
            Assert.True(code.Contains("ctx.OnTick(TickPhase.LateMonthPassed, _bailout.OnMonth)"));
            Assert.False(code.Contains("TickOptions."), "no tick runs during the fast-forward or the loading screen (FR-7 item 9, edge case 8)");
            Assert.True(code.Contains("ModTicks.GameReady += OnGameReady"));
            Assert.True(code.Contains("ModTicks.BatchEnded += OnBatchEnded"));

            int loans = code.IndexOf("_loans.OnMonth", StringComparison.Ordinal);
            int line = code.IndexOf("_creditLine.OnMonth", StringComparison.Ordinal);
            int bailout = code.IndexOf("_bailout.OnMonth", StringComparison.Ordinal);
            Assert.True(loans < line && line < bailout, "the order of the month: the loans, the line, the investor");
        }

        [Test]
        public void Register_EndsWithTheSelfCheck_ThatStopsTheFeatureWhenAMemberIsMissing()
        {
            string code = Code("BankruptcyRecoveryFeature.cs");
            int registerMethod = code.IndexOf("public void Register(FeatureContext ctx)", StringComparison.Ordinal);
            int check = code.IndexOf("RecoverySelfCheck.Run()", registerMethod, StringComparison.Ordinal);
            int thrown = code.IndexOf("throw new InvalidOperationException(", check, StringComparison.Ordinal);
            int tick = code.IndexOf("ctx.OnTick(", registerMethod, StringComparison.Ordinal);
            Assert.True(check > tick && thrown > check, "the check comes last; a miss throws, the core then marks the feature unavailable");
            Assert.True(code.Contains("report.AllFound"));
        }

        // ---- the money of the player moves in one place ----

        [Test]
        public void TheBalanceOfThePlayer_IsChangedByTheAdapterAndTheLedgerRules_Only()
        {
            var call = new Regex(@"\.\s*(?:AddMoney|SpendMoney|EarnMoney)\s*\(");
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "PlayerFinanceAdapter.cs", "Logic/CapitalLedgerCore.cs" };
            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode(true))
            {
                if (allowed.Contains(file.Key)) continue;
                foreach (Match m in call.Matches(file.Value)) offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index) + ": " + m.Value.Trim());
            }
            Assert.True(offenders.Count == 0,
                "money moves through CapitalLedger (design: CapitalLedger); only the adapter and the ledger's rules call the company:\n  " + string.Join("\n  ", offenders));
        }

        // ---- FR-11: the player only, the channels read only ----

        [Test]
        public void TheFeature_NeverReachesTheCompaniesOfTheAi()
        {
            var ai = new Regex(@"\b(?:AICompany|FindAllCompanies|ModCompanies|AiCompanyKeys|CompanyKey\.ForAi|GetAiCompan\w*)\b");
            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode(true))
            {
                foreach (Match m in ai.Matches(file.Value)) offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index) + ": " + m.Value);
            }
            Assert.True(offenders.Count == 0, "the feature changes the player's company only (FR-11):\n  " + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheChannels_AreOnlyRead()
        {
            var write = new Regex(@"\bModModifiers\s*\.\s*(?:Add|Remove|RemoveAll|RegisterProvider|RegisterReasonedProvider|DeclareChannel|Invalidate)\b");
            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode(true))
            {
                foreach (Match m in write.Matches(file.Value)) offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index) + ": " + m.Value);
            }
            Assert.True(offenders.Count == 0, "the feature registers no entries and no providers (design: only reads bank.interest):\n  " + string.Join("\n  ", offenders));
            Assert.True(Code("PlayerFinanceAdapter.cs").Contains("ModModifiers.Get(ModChannels.BankInterest, ModifierContext.For(company))"), "the multiplier is read for the player");
        }

        [Test]
        public void TheThreshold_IsReadFromModCredit()
        {
            string code = Code("PlayerFinanceAdapter.cs");
            Assert.True(code.Contains("ModCredit.PlayerDebtLimit()"));
            Assert.True(code.Contains("ModCredit.ThresholdOf(company)"));
            Assert.False(Regex.IsMatch(code, @"\bCentralBank\b"), "the bank is not read here (FR-11 item 2)");
        }

        // ---- no typed text ----

        [Test]
        public void NotificationsAndWindows_TakeTheirTextFromTheStringTables()
        {
            var literalShow = new Regex(@"\bModNotify\s*\.\s*Show\s*\(");
            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> file in AllCode(false))
            {
                foreach (Match m in literalShow.Matches(file.Value)) offenders.Add(file.Key + ":" + SourceScan.LineOf(file.Value, m.Index));
            }
            Assert.True(offenders.Count == 0, "ModNotify.ShowKey takes a key of the string tables; Show takes a ready text:\n  " + string.Join("\n  ", offenders));
        }
    }
}
