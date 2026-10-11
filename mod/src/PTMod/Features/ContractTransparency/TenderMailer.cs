using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The letter and the notification about the tenders of the day (design.md, Component 10, D6; FR-5 items 4-6): ONE letter a day "Tender results", a line per contract, and one
    /// notification when something was lost, instead of a window for every contract. Almost all draws fall on 1 January, so in practice the letter comes once a year.
    /// <list type="bullet">
    /// <item><b>Cost of an ordinary day:</b> <see cref="OnDayPassed"/> looks at one counter and returns (NFR-1). The handler is subscribed with <c>ctx.OnTick(TickPhase.DayPassed, ...)</c> and no
    /// option, so the core does not call it in the new-game fast-forward or under the loading screen; the recorder does not write results there anyway.</item>
    /// <item><b>The letter</b>: tag <c>ct:tenders:&lt;yyyy-MM-dd&gt;</c>, priority Normal, no block of ids, the action <c>open-log</c> "Open the log" (reusable, the letter stays open). A batch run of a
    /// month (<c>ModTicks.IsBatchRun</c>) plays the days in one frame: this handler runs for each of them and sends its letter as usual; the core holds letters and notifications back
    /// and shows them after <c>BatchEnded</c>, one per day.</item>
    /// <item><b>The notification</b> when at least one tender was lost and the setting "notify about lost tenders" is on: "Tenders lost: N".</item>
    /// <item><b>The action</b> <c>open-log</c> opens the log window on the filter "Tender results" through <see cref="LogWindowLauncher"/> (the window itself is TSK-014).</item>
    /// </list>
    /// </summary>
    internal sealed class TenderMailer
    {
        /// <summary>The id of the action of the letter.</summary>
        internal const string ActionOpenLog = "open-log";

        /// <summary>The prefix of the tag of the letter; the date follows.</summary>
        internal const string TagPrefix = "ct:tenders:";

        private const string Feature = ContractParamSpecs.FeatureId;
        private const string DaySite = "TenderMailer/DayPassed";
        private const string ActionSite = "TenderMailer/OnMailAction";

        private readonly ContractTransparencyFeature _feature;

        internal TenderMailer(ContractTransparencyFeature feature)
        {
            _feature = feature;
        }

        /// <summary><c>TickPhase.DayPassed</c>: the letter of the day, if the recorder added any result since yesterday.</summary>
        internal void OnDayPassed()
        {
            // The metric of NFR-1 (0.05 ms a day) is measured here, the empty day included.
            using (PerfTimer.Start(TimingSites.MailerDay))
            {
                ContractLog log = _feature.Log;
                if (log == null || log.Model.PendingTenderCount == 0) return;   // the whole cost of a day without tenders

                ContractTransparencyFeature.Guarded(DaySite, () => SendSummary(log.Model));
            }
        }

        /// <summary><c>ModMail.RegisterActionHandler</c>: a click on "Open the log" in the letter. The letter is not closed (the core keeps it open).</summary>
        internal void OnMailAction(ModMailRecord mail, string actionId)
        {
            if (actionId != ActionOpenLog) return;
            ContractTransparencyFeature.Guarded(ActionSite, () => LogWindowLauncher.Open(LogFilter.Tenders));
        }

        // ---- the letter ----

        private void SendSummary(ContractLogModel model)
        {
            IReadOnlyList<TenderResultDto> tenders = model.DrainTodayTenders();
            if (tenders.Count == 0) return;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) return;

            bool comma = string.Equals(ModL10n.Language, "ru", StringComparison.OrdinalIgnoreCase);
            var lines = new StringBuilder();
            int lost = 0;
            for (int i = 0; i < tenders.Count; i++)
            {
                TenderResultDto tender = tenders[i];
                if (tender.outcome == TenderOutcomeClassifier.OutcomeLost) lost++;
                if (i > 0) lines.Append('\n');
                lines.Append(ModL10n.L(TenderLines.KeyOf(tender), TenderLines.Args(tender, _feature.Rules, comma)));
            }

            var message = new ModMailMessage
            {
                FeatureId = Feature,
                Tag = TagPrefix + LogDates.Write(today),
                TitleKey = "ct.tender.mail.title",
                BodyKey = "ct.tender.mail.body",
                Args = new[] { today.Year.ToString(CultureInfo.InvariantCulture), lines.ToString() },
                Priority = MailPriority.Normal,
                Actions = new List<ModMailAction>
                {
                    new ModMailAction { Id = ActionOpenLog, LabelKey = "ct.mail.openLog", Repeatable = true },
                },
            };
            int id = ModMail.Send(message);
            if (id < 0) ModLog.Warn(Feature, "tender: the letter of " + LogDates.Write(today) + " was not accepted by the mail service; the results are in the log");

            if (lost > 0 && ContractTransparencySettings.NotifyTenderLoss)
            {
                ModNotify.Show(Feature, ModL10n.L("ct.tender.notify.lost", lost.ToString(CultureInfo.InvariantCulture)));
            }
        }
    }

    /// <summary>
    /// The way to the window of the log (FR-8; the window is TSK-014). The pieces that want to show the log (the action of the letter, the button in the contract window, the hub page) call
    /// <see cref="Open"/> and do not know whether the window exists yet. The window calls <see cref="SetOpener"/> when it is built; until then <see cref="Open"/> only says so in the log of the mod.
    /// </summary>
    internal static class LogWindowLauncher
    {
        private static Action<LogFilter> _opener;

        /// <summary>The window of the log tells how to open it (null: there is no window).</summary>
        internal static void SetOpener(Action<LogFilter> opener)
        {
            _opener = opener;
        }

        /// <summary>Opens the log on this filter; false when there is no window to open (the reason goes to the log of the mod).</summary>
        internal static bool Open(LogFilter filter)
        {
            Action<LogFilter> opener = _opener;
            if (opener == null)
            {
                ModLog.Info(ContractParamSpecs.FeatureId, "log: the window of the log is not built yet; the filter " + filter + " is not shown");
                return false;
            }

            opener(filter);
            return true;
        }
    }
}
