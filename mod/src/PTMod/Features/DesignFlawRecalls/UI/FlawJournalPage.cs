using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameColors = ProcessorTycoon.Colors;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameDataFinder = ProcessorTycoon.DataFinder;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The page "Flaws and recalls" of the mods hub (FR-12, design.md Component 14), registered with <c>FeatureContext.HubPage</c>. Two tabs, "My CPUs" and "Rivals"; under them
    /// a scrolling list with a line for each record the player may know about (<see cref="FlawJournalModel"/> decides which and in which order): the CPU (and the company on the
    /// rivals' tab) and what is being done about the flaw - "Patch, -12 %, 200 days" - then the kind and severity of the flaw and the money spent. A patched flaw of the player has the
    /// button "Fund a new stepping - $20M, 90 days"; it asks <c>ModUI.Confirm</c> first and then <c>FlawResponseEngine.FundSteppingForMitigated</c> starts the revision with the saved
    /// quote (FR-8 item 5). A hidden flaw is not on the page and nothing on it hints at one (FR-12 item 4).
    /// <para>
    /// The core calls <see cref="Build"/> every time the page is opened, with an empty container, while the feature is on; a tab or a confirmed purchase redraws only the list.
    /// The look is <c>ModUI</c>'s: its widgets repaint with the theme. In a debug build, with <c>debug</c> on in the settings, the page has the extra block "Force a flaw" (a list of
    /// the player's CPUs, a severity and two buttons) for the manual scenarios of the owner; in a release build the block is not compiled.
    /// </para>
    /// </summary>
    internal static class FlawJournalPage
    {
        /// <summary>The key of the heading of the page (also the first argument of <c>HubPage</c>).</summary>
        internal const string TitleKey = "dfr.journal.title";

        private const string Feature = DesignFlawRecallsFeature.FeatureId;
        private const float TabBarHeight = 34f;
        private const float BodyHeight = 330f;
        private const string StatusPrefix = "dfr.journal.status.";

        private static FlawJournalTab _tab = FlawJournalTab.Mine;
        private static RectTransform _tabs;
        private static RectTransform _body;

        /// <summary>Fills the page (an empty vertical list under the parameters of the feature).</summary>
        internal static void Build(RectTransform page)
        {
            if (page == null || DesignFlawRecallsFeature.Instance == null) return;

            _tabs = FlawUi.Row(page, TabBarHeight, 6f);
            _body = ModUI.ScrollView(page, BodyHeight);
            if (_tabs == null || _body == null)
            {
                ModUI.Text(page, ModL10n.L("dfr.journal.unavailable"));
                return;
            }

            RebuildTabs();
            RebuildBody();
#if PTMOD_DEBUG
            BuildDebugBlock(page);
#endif
        }

        // ---- tabs ----

        private static void RebuildTabs()
        {
            FlawUi.Clear(_tabs);
            AddTab(FlawJournalTab.Mine, "dfr.journal.tab.mine");
            AddTab(FlawJournalTab.Rivals, "dfr.journal.tab.rivals");
        }

        private static void AddTab(FlawJournalTab tab, string textKey)
        {
            bool current = tab == _tab;
            string caption = ModL10n.L(textKey);
            ButtonUI button = ModUI.Button(_tabs, caption, () => Select(tab), current ? ButtonStyle.CallToAction : ButtonStyle.Standard);
            if (button == null && current) ModUI.Button(_tabs, caption, () => Select(tab));   // no call-to-action template: the current tab is not marked
        }

        private static void Select(FlawJournalTab tab)
        {
            FlawUi.Defer(() =>
            {
                _tab = tab;
                RebuildTabs();
                RebuildBody();
            });
        }

        // ---- the list ----

        private static void RebuildBody()
        {
            if (_body == null) return;

            FlawUi.Clear(_body);
            _body.anchoredPosition = Vector2.zero;   // a rebuilt list starts at the top

            RectTransform target = _body;
            FlawJournalTab tab = _tab;
            bool built = ModHooks.Guard(Feature, "ui/journal/" + tab, () => FillBody(target, tab));
            if (!built) ModUI.Text(target, ModL10n.L("dfr.journal.error"));
        }

        private static void FillBody(RectTransform body, FlawJournalTab tab)
        {
            DesignFlawRecallsFeature feature = DesignFlawRecallsFeature.Instance;
            List<FlawJournalRow> rows = FlawJournalModel.Rows(feature.Store.All, tab, FlawDays.FromDate(ModTicks.Today));
            if (rows.Count == 0)
            {
                ModUI.Text(body, ModL10n.L(tab == FlawJournalTab.Mine ? "dfr.journal.empty.mine" : "dfr.journal.empty.rivals"));
                return;
            }

            foreach (FlawJournalRow row in rows)
            {
                try
                {
                    AddRow(body, feature, row, tab);
                }
                catch (Exception ex)
                {
                    // One broken line must not take the others with it.
                    ModLog.Warn(Feature, "journal: the line of flaw #" + row.RecordId + " could not be made: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void AddRow(RectTransform body, DesignFlawRecallsFeature feature, FlawJournalRow row, FlawJournalTab tab)
        {
            string title = Title(row, tab);
            string status = Paint(Status(row), row.Waiting || row.CoverUp);
            string details = ModL10n.L("dfr.journal.row.details", FlawText.TypeName(feature.Balance, row.TypeId), FlawText.SeverityName(row.Severity),
                FlawFormat.Instance.Money(row.SpentTotal));

            if (ModUI.Info(body, title, status) == null) ModUI.Text(body, title + ": " + status);   // no info-text template in the scene
            ModUI.Text(body, "<alpha=#B0>" + details + "<alpha=#FF>");

            if (row.CanFundStepping)
            {
                int recordId = row.RecordId;
                string caption = ModL10n.L("dfr.journal.fundStepping", FlawFormat.Instance.Money(row.FundCost), row.FundDays.ToString(CultureInfo.InvariantCulture));
                ModUI.Button(body, caption, () => AskToFund(recordId), ButtonStyle.Standard, ModL10n.L("dfr.journal.fundStepping.tip"));
            }
        }

        // ---- the words of a line ----

        private static string Title(FlawJournalRow row, FlawJournalTab tab)
        {
            string cpu = FlawText.CpuName(row.BaseCpuId);
            if (string.IsNullOrEmpty(cpu)) cpu = ModL10n.L("dfr.journal.cpuUnknown", row.BaseCpuId.ToString(CultureInfo.InvariantCulture));
            if (tab == FlawJournalTab.Mine) return cpu;

            return ModL10n.L("dfr.journal.row.rival", CompanyName(row.CompanyKey), cpu);
        }

        private static string CompanyName(string companyKey)
        {
            var company = ModCompanies.Resolve(companyKey);
            string name = company == null ? null : company.Name;
            return string.IsNullOrEmpty(name) ? companyKey : name;
        }

        /// <summary>What is being done about the flaw: "Patch, -12 %, 200 days" (AC-12.1). Every status text takes the loss of the patch as {0} and the days left as {1}.</summary>
        private static string Status(FlawJournalRow row)
        {
            return ModL10n.L(StatusPrefix + FlawJournalModel.StatusKey(row), FlawFormat.Instance.Percent(row.IpsLoss), row.DaysLeft.ToString(CultureInfo.InvariantCulture));
        }

        private static string Paint(string text, bool attention)
        {
            if (!attention) return text;
            return "<color=" + GameColors.Instance.DynamicNegativeHex() + ">" + text + "</color>";
        }

        // ---- the button "Fund a new stepping" ----

        private static void AskToFund(int recordId)
        {
            DesignFlawRecallsFeature feature = DesignFlawRecallsFeature.Instance;
            FlawRecord record = feature == null ? null : feature.Store.Find(recordId);
            FlawQuote quote = record == null ? null : record.Quote;
            if (record == null || quote == null || record.State != FlawState.Mitigated)
            {
                FlawUi.Defer(RebuildBody);   // the flaw is not patched any more: the list shows what it is now
                return;
            }

            string message = ModL10n.L("dfr.journal.fund.confirm.body", FlawText.CpuName(record.BaseCpuId), FlawFormat.Instance.Money(quote.SteppingCost),
                quote.SteppingDays.ToString(CultureInfo.InvariantCulture));
            ModUI.Confirm("dfr.journal.fund.confirm.title", message, () => Fund(recordId), null, "dfr.journal.fund.confirm.yes", "core.common.cancel");
        }

        private static void Fund(int recordId)
        {
            DesignFlawRecallsFeature feature = DesignFlawRecallsFeature.Instance;
            FlawRecord record = feature == null ? null : feature.Store.Find(recordId);

            bool funded = record != null && feature.Applier.FundSteppingForMitigated(record);
            ModNotify.ShowKey(Feature, funded ? "dfr.journal.fund.done" : "dfr.journal.fund.failed", record == null ? string.Empty : FlawText.CpuName(record.BaseCpuId));
            FlawUi.Defer(RebuildBody);
        }

#if PTMOD_DEBUG
        // ---- the debug block (debug build, setting "debug") ----

        private static void BuildDebugBlock(RectTransform page)
        {
            if (!FlawSettings.Debug) return;

            ModUI.Text(page, ModL10n.L("dfr.journal.debug.heading"), true);

            var cpus = new List<GameCpu>();
            var names = new List<string>();
            List<GameCpu> all = GameDataFinder.FindAllCpus();
            if (all != null)
            {
                foreach (GameCpu cpu in all)
                {
                    if (cpu == null || cpu.Base != null || cpu.IsRetired || !cpu.IsReleased || !ModCompanies.IsPlayer(cpu.Company)) continue;
                    cpus.Add(cpu);
                    names.Add(cpu.Name);
                }
            }

            if (cpus.Count == 0)
            {
                ModUI.Text(page, ModL10n.L("dfr.journal.debug.noCpus"));
                return;
            }

            var severities = new List<string>();
            foreach (FlawSeverity severity in new[] { FlawSeverity.Minor, FlawSeverity.Major, FlawSeverity.Critical }) severities.Add(FlawText.SeverityName(severity));

            int cpuIndex = 0;
            int severityIndex = 1;
            ModUI.Dropdown(page, ModL10n.L("dfr.journal.debug.cpu"), names, 0, index => cpuIndex = index);
            ModUI.Dropdown(page, ModL10n.L("dfr.journal.debug.severity"), severities, severityIndex, index => severityIndex = index);
            ModUI.Button(page, ModL10n.L("dfr.journal.debug.forcePublic"), () => Force(cpus, cpuIndex, severityIndex, DiscoverySource.External));
            ModUI.Button(page, ModL10n.L("dfr.journal.debug.forceInternal"), () => Force(cpus, cpuIndex, severityIndex, DiscoverySource.Internal));
        }

        private static void Force(List<GameCpu> cpus, int cpuIndex, int severityIndex, DiscoverySource discovery)
        {
            if (cpuIndex < 0 || cpuIndex >= cpus.Count) return;

            GameCpu cpu = cpus[cpuIndex];
            var severity = (FlawSeverity)Math.Max(0, Math.Min(severityIndex, FlawBalance.SeverityCount - 1));
            bool done = DesignFlawRecallsApi.Registry.ForceFlaw(cpu.SaveID, null, severity, discovery);
            ModNotify.ShowKey(Feature, done ? "dfr.journal.debug.done" : "dfr.journal.debug.failed", cpu.Name);
            FlawUi.Defer(RebuildBody);
        }
#endif
    }
}
