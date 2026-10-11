using System;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Features.BankruptcyRecovery.Logic;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// TSK-022 (FR-12, D9): the seams of the safe switch-off on the game side, which cannot be loaded here, read from the sources. The hub is given the question; the
    /// settlement runs from <c>OnDisabled</c> under the guard of the feature, before the state is kept for the switch-on, and looks at nothing that says whether the feature
    /// is active (it is not, by then); the money moves through the ledger only; the texts are keys of the tables; the marks live in the flags of the save and the state kept
    /// for the switch-on belongs to the game it was kept in.
    /// </summary>
    public class DisableWiringTests
    {
        private static string Code(params string[] relative)
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery", Path.Combine(relative));
            Assert.True(File.Exists(path), "the file is missing: " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        // The text of a method: from its signature to the closing brace at the same indent.
        private static string Body(string code, string signature, string indent)
        {
            int start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "the method is missing: " + signature);
            int end = code.IndexOf("\n" + indent + "}", start, StringComparison.Ordinal);
            Assert.True(end > start, "the end of " + signature);
            return code.Substring(start, end - start);
        }

        [Test]
        public void Register_GivesTheHubTheQuestion()
        {
            string code = Code("BankruptcyRecoveryFeature.cs");
            string register = Body(code, "public void Register(FeatureContext ctx)", "        ");

            Assert.True(register.Contains("ctx.ConfirmBeforeDisable(_settlement.BuildConfirmation);"), "FR-12 item 3: the hub asks before it switches the feature off");
            Assert.True(code.Contains("_settlement = new DisableSettlementService(_env);"), "the service is made with the environment of the feature");
        }

        [Test]
        public void OnDisabled_SettlesFirst_ThenKeepsTheState_UnderTheGuard()
        {
            string code = Code("BankruptcyRecoveryFeature.cs");
            string body = Body(code, "public void OnDisabled()", "        ");

            Assert.True(body.Contains("ModHooks.Guard(FeatureId, \"feature/disabled\""), "the guard of the core lets a switched-off feature run its own cleanup");
            int settle = body.IndexOf("_settlement.Settle(\"disabled\");", StringComparison.Ordinal);
            int freeze = body.IndexOf("_participant.Freeze();", StringComparison.Ordinal);
            Assert.True(settle >= 0 && freeze > settle, "the state is kept after the settlement, not before");
            Assert.False(body.Contains("ModFeatures.IsActive"), "when OnDisabled runs the feature is not active any more");
        }

        [Test]
        public void TheSettlement_NeverAsksWhetherTheFeatureIsActive()
        {
            foreach (string file in new[] { "Services/DisableSettlementService.cs", "Logic/DisableSettlementEngine.cs", "Logic/SettlementMarks.cs" })
            {
                string code = Code(file.Split('/'));
                Assert.False(code.Contains("ModFeatures"), file + ": it runs from OnDisabled, when the feature is already off, and from a switch from code");
                Assert.False(code.Contains("ConfirmBeforeDisable") && file.EndsWith("Engine.cs"), file + ": a switch from code asks nothing");
            }
        }

        [Test]
        public void TheQuestion_IsBuiltFromTheStringsOfTheDesign()
        {
            string code = Code("Services", "DisableSettlementService.cs");
            string body = Body(code, "public DisableConfirmation BuildConfirmation()", "        ");

            Assert.True(body.Contains("if (plan == null) return null;"), "nothing owed: no question");
            Assert.True(body.Contains("MessageKey = \"rec.disable.confirm\""));
            Assert.True(body.Contains("ConfirmLabelKey = \"rec.disable.confirmLabel\""));
            Assert.True(body.Contains("ModL10n.L(\"rec.disable.defaultRisk\")"), "the warning about the default");
            Assert.True(Regex.IsMatch(body, @"Args\s*=\s*new\[\]\s*\{\s*RecoveryViews\.MoneyExact\(plan\.Total\),\s*RecoveryViews\.MoneyExact\(plan\.BalanceAfter\),\s*risk\s*\}"),
                "the sum, the balance after, the warning: {0}, {1}, {2} of the string (the money as the game writes it)");
            Assert.True(body.Contains("plan.WouldDefault ?"), "the warning only when the balance after is below the threshold");
        }

        [Test]
        public void TheNotification_IsShownWhenMoneyWasTaken()
        {
            string code = Code("Services", "DisableSettlementService.cs");

            Assert.True(Regex.IsMatch(code, @"if \(result\.Converted > 0\.0\) ModNotify\.ShowKey\(RecoveryApi\.FeatureId, ""rec\.notify\.converted"", RecoveryViews\.Money\(result\.Converted\)\);"),
                "FR-12: ModNotify with the sum");
            Assert.True(code.Contains("SettleStatus.Unavailable"), "a settlement that could not be made is a warning, not silence");
        }

        [Test]
        public void TheRecordOfTheConversion_IsTheCodeOfTheDesign_WithAName()
        {
            Assert.Equal("disable.convert", DisableSettlementEngine.Operation);
            Assert.True(new System.Collections.Generic.List<string>(JournalLines.KnownOps).Contains("disable.convert"), "the tab Journal has a name for it (TSK-014)");
            Assert.True(Code("Logic", "DisableSettlementEngine.cs").Contains("_env.Money.CapitalOut(parts.Total, Operation,"), "one CapitalOut for the whole sum, through the ledger");
        }

        [Test]
        public void TheMarks_LiveInTheFlagsOfTheSave_AndTheGameIsRecognisedByTheHeaderOfTheFile()
        {
            string marks = Code("SaveMarks.cs");
            Assert.True(marks.Contains("ModSave.Flags.GetInt(RecoveryApi.FeatureId, key, 0)"));
            Assert.True(marks.Contains("ModSave.Flags.SetInt(RecoveryApi.FeatureId, key, value)"));

            string participant = Code("RecoverySaveParticipant.cs");
            Assert.True(participant.Contains("_section.UseMarks(new SaveMarks(), () => ModFeatures.IsActive(RecoveryApi.FeatureId)"), "the state kept for a switch-on is used only when the feature is active");
            Assert.True(participant.Contains("() => ModSave.FileHeader"), "a new header object for every side file that is read: the identity of the game in play");
        }

        [Test]
        public void TheGameEvents_ForgetTheKeptState_AndApplyTheMarks()
        {
            string code = Code("BankruptcyRecoveryFeature.cs");

            string ready = Body(code, "private void OnGameReady(GameReadyReason reason)", "        ");
            Assert.True(ready.IndexOf("_participant.DropFrozen();", StringComparison.Ordinal) >= 0, "a new game or a load: the kept state is of a game that is gone");
            Assert.True(ready.Contains("_participant.Reconcile();"));
            Assert.False(ready.Contains("Settle"), "an inactive feature is not called at all; an active one has nothing to settle at the start of a game");

            Assert.True(code.Contains("ModSave.AfterRead += OnSaveRead;"));
            Assert.True(Body(code, "private void OnSaveRead()", "        ").Contains("_participant.Reconcile();"));
        }

        [Test]
        public void TheFeature_DoesNotKeepADiskCopyOfTheSectionOfItsOwn()
        {
            // the section is the core's business (FR-5): no file access in the feature for the sake of the settlement
            var io = new Regex(@"\bSystem\.IO\b|\bFile\s*\.\s*(?:ReadAll|WriteAll|Open|Create)|\bStreamReader\b|\bStreamWriter\b");
            foreach (string path in SourceScan.Files(Path.Combine(SourceScan.SrcDirOrFail(), "Features", "BankruptcyRecovery"), ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), true);
                Assert.False(io.IsMatch(code), SourceScan.Relative(SourceScan.SrcDirOrFail(), path) + " touches the disk; files belong to the core");
            }
        }

        [Test]
        public void TheStringsOfTheQuestion_ExistWithTheirPlaceholders_InBothLanguages()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                string table = RecoveryTestData.ReadStrings(language);
                Assert.True(Regex.IsMatch(table, "\"rec\\.disable\\.confirm\": \"[^\"]*\\{0\\}[^\"]*\\{1\\}[^\"]*\\{2\\}"), language + ": the question has the sum, the balance after and the warning");
                Assert.True(Regex.IsMatch(table, "\"rec\\.notify\\.converted\": \"[^\"]*\\{0\\}"), language + ": the notification has the sum");
                Assert.True(table.Contains("\"rec.disable.defaultRisk\"") && table.Contains("\"rec.disable.confirmLabel\"") && table.Contains("\"rec.journal.op.disable.convert\""), language);
            }
        }
    }
}
