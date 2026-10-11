using System;
using PTMod.Core;
using PTMod.Features.EventSystem;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// What the player hears about the flaws of the rivals (FR-11 item 4, design.md Component 13): the game's side of <see cref="FlawNewsPlanner"/>. The rules - whom to tell, how, and
    /// with which keys - are in the pure plan; this class sends it.
    /// <list type="bullet">
    /// <item><c>Major</c> and <c>Critical</c>: a letter, <c>ModMail.Send</c> with <c>MailPriority.Normal</c> and the tag <c>dfr:news:&lt;recordId&gt;</c>.</item>
    /// <item><c>Minor</c>: a notification of two lines, <c>ModNotify.Show</c> (the title and who admitted what and how they answer it).</item>
    /// <item>Every public incident: a line in the event journal, through <c>EventEffects.ApplyDirect</c> with the effect <see cref="FlawEffectTypes.News"/> (nothing happens in the
    /// game; the line is the record).</item>
    /// </list>
    /// The texts carry the company, the CPU, the type, the severity and the reaction. The core holds a letter and a notification back during a fast-forward, a load and a batch run,
    /// so nothing here looks at them; the fast-forward of a new game is checked by the plan.
    /// Every part is guarded on its own: a letter that cannot be sent does not stop the line in the journal.
    /// </summary>
    internal sealed class FlawNews
    {
        private const string FeatureId = DesignFlawRecallsFeature.FeatureId;

        private readonly DesignFlawRecallsFeature _feature;

        public FlawNews(DesignFlawRecallsFeature feature)
        {
            _feature = feature;
        }

        /// <summary>
        /// A public incident of an AI company: the letter or the notification and the line of the journal as <paramref name="plan"/> says. Never throws.
        /// </summary>
        internal void OnAiPublic(FlawRecord record, FlawResponse response, FlawNewsPlan plan)
        {
            if (record == null || plan == null || plan.IsEmpty) return;

            string[] args = ModHooks.Guard<string[]>(FeatureId, "news/args", () => BuildArgs(record, response, plan), null);
            if (args == null) return;

            if (plan.Kind == FlawNewsKind.Mail) ModHooks.Guard(FeatureId, "news/mail", () => SendMail(plan, args));
            else if (plan.Kind == FlawNewsKind.Notify) ModHooks.Guard(FeatureId, "news/notify", () => Notify(plan, args));

            if (plan.Journal) ModHooks.Guard(FeatureId, "news/journal", () => WriteJournal(record, args));
        }

        /// <summary>
        /// The mod answered a flaw of the player itself because the window could not be shown (FR-5 item 7): a line "decided automatically" goes to the log of the mod. The
        /// letter about it is sent by the dispatcher, and the event journal marks a choice made by the timer by itself (<c>AutoResolved</c>); a choice made while the event
        /// system is off cannot be written there.
        /// </summary>
        internal void OnPlayerAutoChoice(FlawRecord record, FlawResponse response)
        {
            if (record == null) return;

            ModHooks.Guard(FeatureId, "news/auto", () =>
            {
                ModLog.Info(FeatureId, ModL10n.L("dfr.log.auto") + " (flaw #" + record.Id + ", " + response + ")");
            });
        }

        // ---- the parts ----

        /// <summary>The five texts: the company, the CPU, the type, the severity, the reaction - in the language of the player.</summary>
        private string[] BuildArgs(FlawRecord record, FlawResponse response, FlawNewsPlan plan)
        {
            GameCompany company = ModCompanies.Resolve(record.CompanyKey);
            string companyName = company == null || string.IsNullOrEmpty(company.Name) ? record.CompanyKey : company.Name;
            string reaction = string.IsNullOrEmpty(plan.ReactionKey) ? FlawText.ChoiceName(response) : ModL10n.L(plan.ReactionKey);

            return new[]
            {
                companyName,
                FlawText.CpuName(record.BaseCpuId),
                FlawText.TypeName(_feature.Balance, record.TypeId),
                FlawText.SeverityName(record.Severity),
                reaction,
            };
        }

        /// <summary>The notification of a <c>Minor</c> flaw: the title ("AMD: defect in Nova 3") and under it who admitted what and how they answer it.</summary>
        private static void Notify(FlawNewsPlan plan, string[] args)
        {
            ModNotify.Show(FeatureId, ModL10n.L(plan.NotifyKey, args), ModL10n.L(plan.NotifyBodyKey, args));
        }

        private static void SendMail(FlawNewsPlan plan, string[] args)
        {
            ModMail.Send(new ModMailMessage
            {
                FeatureId = FeatureId,
                Tag = plan.MailTag,
                TitleKey = plan.TitleKey,
                BodyKey = plan.BodyKey,
                Args = args,
                Priority = MailPriority.Normal,
            });
        }

        /// <summary>
        /// The line of the event journal: a service instance of the event system (<c>Direct</c>, the source <see cref="FlawNewsPlanner.JournalSourceId"/>) with the effect that changes
        /// nothing. The result is <c>null</c> when the event system is off; the journal then has no line and the letter alone tells the player.
        /// </summary>
        private static void WriteJournal(FlawRecord record, string[] args)
        {
            var spec = new EventEffect { type = FlawEffectTypes.News, durationDays = 0 };
            EventEffects.ApplyDirect(spec, record.CompanyKey, FlawNewsPlanner.JournalSourceId, args);
        }
    }
}
