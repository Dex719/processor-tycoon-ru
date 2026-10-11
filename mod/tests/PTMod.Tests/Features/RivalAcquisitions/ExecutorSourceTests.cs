using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalAcquisitions
{
    /// <summary>
    /// The game side of the deals, the hooks and the stakes (TSK-010…TSK-014). <c>AcquisitionExecutor</c>, <c>RivalDistressMonitor</c>, <c>StakeService</c>,
    /// <c>AcquisitionPatches</c> and <c>GameAccess</c> need Unity and the game, so they are not part of the test build; these tests read their source and check the
    /// calls of the Acceptance and of design.md, and what must never be there (a patch of <c>AddCpu</c>, <c>EventBus</c>, a change of the target's identity). That
    /// the steps work in the running game is checked by the owner (M1-M7 of design.md, TSK-022).
    /// </summary>
    public class ExecutorSourceTests
    {
        private static string Folder()
        {
            Assert.NotNull(TestEnv.SrcDir, "source folder not found; run the tests through python mod/build.py --tests");
            return Path.Combine(TestEnv.SrcDir, "Features", "RivalAcquisitions");
        }

        private static string Read(params string[] parts)
        {
            string path = Path.Combine(Folder(), Path.Combine(parts));
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(File.ReadAllText(path), false);   // the code without its comments
        }

        private static string Executor()
        {
            return Read("Services", "AcquisitionExecutor.cs") + Read("Services", "AcquisitionExecutor.Cpus.cs") + Read("Services", "AcquisitionExecutor.Finish.cs");
        }

        private static void HasAll(string code, string what, Dictionary<string, string> calls)
        {
            foreach (KeyValuePair<string, string> call in calls)
            {
                Assert.True(code.Contains(call.Value), what + " does not do: " + call.Key + " (" + call.Value + ")");
            }
        }

        [Test]
        public void TheExecutor_DoesTheStepsOfTheDesign()
        {
            HasAll(Executor(), "the executor", new Dictionary<string, string>
            {
                { "step 1: the target found by SaveID again", "GameDataFinder.FindCompany(request.Target.SaveId)" },
                { "step 1: the UniqueID checked", "_target.UniqueID != request.Target.UniqueId" },
                { "step 1: the status checked", "AcqKeys.ReasonStatusChanged" },
                { "step 1: the money service is there", "CapitalLedger.IsAvailable" },
                { "step 2: the deal's own hooks ignore the target", "_feature.ExecutingTarget = _target" },
                { "step 3: no research windows", "GameAccess.SetSuppressPopup(research, true)" },
                { "step 3: progress 1", "r.Progress = 1f" },
                { "step 3: Update(true)", "research.Update(true)" },
                { "step 3: the passes are limited by the content", "TechUnlockMaxPasses" },
                { "step 4: the node experience", "_player.OptimizeNode(processNode, gap)" },
                { "step 5: the CPUs leave the lists of the AI", "owner.CpusOrderedByProfit().Remove(cpu)" },
                { "step 5: the base lists of the AI", "owner.GetBaseCpus().Remove(cpu)" },
                { "step 5: the CPUs to retire", "cpu.TriggerRetirement()" },
                { "step 6: the lines leave the target", "_target.RemoveProductionLines(lines, from)" },
                { "step 6: the lines go to the player", "_player.ExpandFactory(lines, 0f, to.ID)" },
                { "step 6: the lines are counted after", "_player.Factory.AmountOfLines(to) - before" },
                { "step 6: the sale cost", "GameProductionMath.SaleCost(lines, wafer)" },
                { "step 6: the sale through the ledger as Sales", "RevenueOrigin.Sales" },
                { "step 6: the kinds of the companies", "updater.TriggerUpdate(true)" },
                { "step 7: the supply contracts", "contract.Initialize(_player, contract.Client)" },
                { "step 7: the client contracts", "contract.Initialize(contract.Provider, _player)" },
                { "step 7: the clients' CPUs", "cpu.SetNewManufacturer(_player)" },
                { "step 7: own production", "cpu.OutsourcedID = -1" },
                { "step 7: the foundry flag", "_player.Divisions.FoundryServices = true" },
                { "step 7: the toggle of the production window", "window.SetFoundryServicesToggle(true)" },
                { "step 8: the owner", "cpu.Company = _player" },
                { "step 8: the caches of the maker", "GameAccess.ResetProductionCaches(cpu)" },
                { "step 8: a bankrupt's CPU back on sale", "cpu.IsRetired = false" },
                { "step 8: Player.AddCpu", "_owner.AddCpu(cpu)" },
                { "step 8: the production window", "window.AddCpu(cpu)" },
                { "step 8: OnNewCpu", "data.OnNewCpu" },
                { "step 8: CpuTransferred", "ModEvents.RaiseCpuTransferred(Feature, _target, _player, cpu)" },
                { "step 11: the balance to 0", "_target.MoneyAmount = 0f" },
                { "step 11: the game's bankruptcy", "_target.TriggerBankruptcy()" },
                { "step 11: the game's event", "GameAccess.NotifyCompanyWentBankrupt(_target)" },
                { "step 12: the price out", "CapitalLedger.CapitalOut(Feature, amount, op, details)" },
                { "step 12: the cash in", "CapitalLedger.CapitalIn(Feature, amount, op, details)" },
                { "step 13: the reputation", "ModReputation.Add(_player, delta, reasonKey)" },
                { "step 13: DealCompleted", "RivalAcquisitionsApi.RaiseDealCompleted(record)" },
                { "step 13: the mail", "AcquisitionMail.Deal(record)" },
                { "the pipeline under the guard", "ModHooks.Guard(Feature, site, body)" },
                { "the pipeline", "DealExecution.Run(executor, request, feature.State" },
                { "one deal at a time", "AcqKeys.ReasonBusy" },
            });
        }

        [Test]
        public void TheExecutor_NeverTouchesTheIdentityOfTheTarget_NorTheReservedAddCpu()
        {
            string code = Executor();

            Assert.False(Regex.IsMatch(code, @"_target\s*\.\s*(Name|UniqueID|SaveID|FullName)\s*=[^=]"), "the target's identity is never changed (D1)");
            Assert.False(code.Contains("Destroy("), "the target object is never destroyed (D1)");
            Assert.False(Regex.IsMatch(code, @"_player\s*\.\s*AddCpu\s*\("), "Company.AddCpu would announce a release (P20): Player.AddCpu is used");
            Assert.False(Regex.IsMatch(code, @"_target\s*\.\s*AddCpu\s*\("), "no AICompany.AddCpu");
            Assert.False(code.Contains("EventBus"), "no EventBus (FR-13 item 4)");
            Assert.False(code.Contains("RaiseRivalBankrupt"), "the target of a deal is not a rival's own bankruptcy (FR-10 item 5)");
            Assert.False(Regex.IsMatch(code, @"\.\s*DebtLimit\b"), "only ModCredit reads the bank's limit");
        }

        [Test]
        public void ThePatches_AreP1P2P3_PrefixesReturnTrue_AndNoReservedTarget()
        {
            string code = Read("Patches", "AcquisitionPatches.cs");
            string patches = code.Substring(0, code.IndexOf("internal static bool CheckTargets()", System.StringComparison.Ordinal));

            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameBankruptcy\),\s*""TriggerDefault"",\s*PatchKind\.Postfix"), "P1: postfix of BankruptcyHandler.TriggerDefault");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameAiCompany\),\s*""TriggerBankruptcy"",\s*PatchKind\.Prefix"), "P2: prefix of AICompany.TriggerBankruptcy");
            Assert.True(Regex.IsMatch(code, @"\[ModPatch\(typeof\(GameAiCompany\),\s*""BankruptcyWarning"",\s*PatchKind\.Prefix,\s*Priority\s*=\s*HighPriority"), "P3: prefix of BankruptcyWarning, high priority");
            Assert.True(code.Contains("HighPriority = 600"), "Priority.High");
            Assert.False(Regex.IsMatch(patches, @"return\s+false\s*;"), "a prefix never skips the game's method");
            Assert.True(Regex.Matches(patches, @"ModFeatures\.IsActive\(Feature\)").Count >= 3, "each patch asks whether the feature is active");
            Assert.True(Regex.Matches(patches, @"ModHooks\.Guard\(Feature,").Count >= 3, "each body runs under the guard");
            Assert.True(code.Contains("feature.ExecutingTarget"), "P2 skips the target of a deal");

            foreach (string path in Directory.GetFiles(Folder(), "*.cs", SearchOption.AllDirectories))
            {
                string text = SourceScan.Strip(File.ReadAllText(path), false);
                Assert.False(Regex.IsMatch(text, @"\[ModPatch\([^\]]*""AddCpu"""), path + ": Company.AddCpu / AICompany.AddCpu are reserved by the core");
                Assert.False(Regex.IsMatch(text, @"\bEventBus\b"), path + ": no EventBus");
            }
        }

        [Test]
        public void TheFeature_WiresThePatchesTheMonthAndTheMailActions()
        {
            string code = Read("RivalAcquisitionsFeature.cs");

            Assert.True(code.Contains("ctx.Patch(typeof(AcquisitionPatches))"), "P1-P3 declared");
            Assert.True(code.Contains("ctx.OnTick(TickPhase.LateMonthPassed, OnMonth)"), "the monthly handler");
            Assert.True(code.Contains("ModMail.RegisterActionHandler(FeatureId, AcquisitionMail.OnAction)"), "the actions of the mails");
            Assert.True(code.Contains("Monitor.OnCompanyWentBankrupt(company)"), "the bankruptcy event reaches the monitor");
            Assert.True(code.Contains("Monitor.BackfillAuctions()"), "the backfill on a loaded save");
            Assert.True(code.Contains("State.backfillDone = true"), "a new game gets no backfill");
            Assert.True(code.Contains("AcquisitionPatches.CheckTargets()"), "the self-check of the targets");
        }

        [Test]
        public void TheMonitor_FollowsTheRulesOfTheDesign()
        {
            string code = Read("Services", "RivalDistressMonitor.cs");
            HasAll(code, "the monitor", new Dictionary<string, string>
            {
                { "not while loading", "ModTicks.IsLoading" },
                { "not while fast-forwarding", "ModTicks.IsFastForwarding" },
                { "one mail per episode", "DistressRules.ShouldMailDefault(" },
                { "the default mail", "AcquisitionMail.Default(" },
                { "RivalDefaulted", "RivalAcquisitionsApi.RaiseRivalDefaulted(" },
                { "the snapshot of the CPUs on sale", "!cpu.IsRetired" },
                { "the target of a deal is skipped", "_feature.ExecutingTarget" },
                { "the auction", "DistressRules.OpenAuction(" },
                { "the days of the auction", "Deals.Bankrupt.AuctionDays" },
                { "the auction mail", "AcquisitionMail.Auction(" },
                { "RivalBankrupt", "RivalAcquisitionsApi.RaiseRivalBankrupt(" },
                { "the write-off of a stake", "_feature.Stakes.OnRivalBankrupt(" },
                { "the expired auctions", "DistressRules.CloseExpired(" },
                { "the ended episodes", "DistressRules.EndEpisodes(" },
                { "the backfill", "DistressRules.NeedsBackfill(" },
            });
        }

        [Test]
        public void TheStakeService_FollowsTheRulesOfTheDesign()
        {
            string code = Read("Services", "StakeService.cs");
            HasAll(code, "the stake service", new Dictionary<string, string>
            {
                { "the price out of the player", "CapitalLedger.CapitalOut(Feature, price" },
                { "into the company", "target.MoneyAmount = (float)(target.MoneyAmount + price)" },
                { "+3 for a company in default", "if (inDefault && b.Reputation.Rescue != 0)" },
                { "the dividend through EarnMoney(Interest)", "RevenueOrigin.Interest" },
                { "the report of the month before by Year/Month", "report.Year == year && report.Month == month" },
                { "the money of the company is checked", "StakeMath.Dividend(stake.share, profit, company.MoneyAmount" },
                { "the condition of P3", "StakeBook.ShouldTryRescue(order.active, negativeBalanceLimit, target.IsInDefault, target.MoneyAmount)" },
                { "the threshold of the company through ModCredit", "ModCredit.DebtLimitOf(target)" },
                { "the arithmetic of the rescue", "StakeMath.Rescue(" },
                { "the injection", "target.MoneyAmount = (float)(target.MoneyAmount + decision.Injection)" },
                { "a refusal is a mail", "AcquisitionMail.RescueFailed(target, decision)" },
                { "StakeChanged", "RivalAcquisitionsApi.RaiseStakeChanged(" },
                { "the stake ends with a bankruptcy", "AcquisitionMail.StakeLost(" },
                { "nothing while the feature is off", "ModFeatures.IsActive(Feature)" },
            });
        }

        [Test]
        public void TheMails_HaveTheTagsAndThePriorityOfTheDesign()
        {
            string code = Read("Services", "AcquisitionMail.cs");
            HasAll(code, "the mails", new Dictionary<string, string>
            {
                { "the tag of a default", "MailTags.Default(" },
                { "the tag of an auction", "MailTags.Auction(" },
                { "the tag of a deal", "MailTags.Deal(" },
                { "the tag of a lost stake", "MailTags.StakeLost(" },
                { "the tag of a failed rescue", "MailTags.RescueFailed(" },
                { "Normal priority", "MailPriority.Normal" },
                { "reusable actions", "Repeatable = true" },
                { "the action opens the window", "RivalAcquisitionsApi.OpenWindow(" },
                { "money with the game's formatter", "FloatToMoney(" },
            });
            Assert.False(code.Contains("MailId"), "the id of a mail is never kept");
        }

        [Test]
        public void TheReflection_IsTheFourMembersOfTheDesign()
        {
            string code = Read("GameAccess.cs");
            Assert.Equal(4, Regex.Matches(code, @"Field\(typeof\(").Count, "four members");
            foreach (string member in new[] { "\"temporarilySupressPopup\"", "\"manufacturer\"", "\"currentContract\"", "\"OnCompanyWentBankrupt\"" })
            {
                Assert.True(code.Contains(member), "reflection of " + member);
            }
            Assert.True(code.Contains("GetInvocationList()"), "the handlers of the event are called one by one");
        }
    }
}
