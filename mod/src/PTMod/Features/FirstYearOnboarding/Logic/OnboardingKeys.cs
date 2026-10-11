using System;
using System.Collections.Generic;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// The string keys (<c>onb.*</c>) the tutorial cannot run without (TSK-016, NFR-4): the fixed ones of the feature, the hub, the new game panel and
    /// the buttons of the mails, and the ones every step of the catalog names (title, body, the bodies of its variants, the hints, the short name, the
    /// "Already done" phrase). The self-check asks the string tables of the Russian language, the main one, for every key of this list. Pure logic.
    /// <para>
    /// The optional texts are not here: a body for one track (<c>&lt;bodyKey&gt;.&lt;track&gt;</c>) is used only when it exists.
    /// </para>
    /// </summary>
    public static class OnboardingKeys
    {
        /// <summary>Keys the code of the feature names itself. A test compares this list with the literals in the sources and with the shipped tables.</summary>
        public static readonly IReadOnlyList<string> Fixed = new[]
        {
            // the feature in the hub and its two settings
            "onb.feature.name", "onb.feature.desc",
            "onb.param.pause", "onb.param.pause.tip", "onb.param.reminders", "onb.param.reminders.tip",

            // the new game panel
            "onb.toggle.label", "onb.toggle.tooltip", "onb.toggle.forced", "onb.toggle.forced.on", "onb.toggle.forced.off",

            // notices
            "onb.start.notify", "onb.notify.title", "onb.hint.none",

            // the buttons of the mails and the lines the mails add
            "onb.button.skipStep", "onb.button.skipStep.tip", "onb.button.skipAll", "onb.button.skipAll.tip",
            "onb.button.hint", "onb.button.hint.tip", "onb.button.openCpu", "onb.button.openCpu.tip",
            "onb.button.openFactory", "onb.button.openFactory.tip",
            "onb.done.prefix", "onb.repeat.suffix",

            // the page of the hub
            "onb.hub.title", "onb.hub.mode", "onb.hub.mode.off", "onb.hub.mode.active", "onb.hub.mode.skipped",
            "onb.hub.mode.completed", "onb.hub.mode.aborted", "onb.hub.track", "onb.track.idm", "onb.track.fabless", "onb.track.foundry",
            "onb.hub.step", "onb.hub.step.none", "onb.hub.status.oldSave",
            "onb.hub.button.repeat", "onb.hub.button.skipStep", "onb.hub.button.skipAll", "onb.hub.button.restart",
            "onb.hub.restart.title", "onb.hub.restart.body",
            "onb.error.content",

            // the closing mail (the texts of the step "graduation" are the same keys; they are here for a content without that step)
            "onb.summary.header", "onb.summary.line", "onb.summary.done", "onb.summary.skipped", "onb.summary.na", "onb.summary.open",
            MailPlanner.DefaultSummaryTitleKey, MailPlanner.DefaultSummaryBodyKey
        };

        /// <summary>The keys one step needs: title, body, the bodies of its variants, the hints; a sequential step also its short name and, when it has a condition, its "Already done" phrase.</summary>
        public static IList<string> ForStep(StepDefinition step)
        {
            var keys = new List<string>();
            if (step == null) return keys;

            MailSpec mail = step.Mail;
            if (mail != null)
            {
                Add(keys, mail.TitleKey);
                Add(keys, mail.BodyKey);
                if (mail.Variants != null)
                {
                    foreach (BodyVariant variant in mail.Variants)
                    {
                        if (variant != null) Add(keys, variant.BodyKey);
                    }
                }
            }

            if (step.Reminders != null)
            {
                foreach (ReminderSpec reminder in step.Reminders)
                {
                    if (reminder != null) Add(keys, reminder.HintKey);
                }
            }

            if (step.Kind == StepKind.Sequential)
            {
                Add(keys, step.NameKey);
                if (step.CompleteWhen != null) Add(keys, step.DoneKey);
            }
            return keys;
        }

        /// <summary>The keys of every step of the catalog, each once, in the order of the steps.</summary>
        public static IList<string> ForCatalog(StepCatalog catalog)
        {
            var keys = new List<string>();
            if (catalog == null) return keys;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (StepDefinition step in catalog.All)
            {
                foreach (string key in ForStep(step))
                {
                    if (seen.Add(key)) keys.Add(key);
                }
            }
            return keys;
        }

        /// <summary>The fixed keys and the keys of the catalog, each once.</summary>
        public static IList<string> All(StepCatalog catalog)
        {
            var keys = new List<string>(Fixed);
            var seen = new HashSet<string>(Fixed, StringComparer.Ordinal);
            foreach (string key in ForCatalog(catalog))
            {
                if (seen.Add(key)) keys.Add(key);
            }
            return keys;
        }

        private static void Add(List<string> keys, string key)
        {
            if (!string.IsNullOrEmpty(key) && !keys.Contains(key)) keys.Add(key);
        }
    }
}
