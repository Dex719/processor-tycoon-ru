using System.Collections.Generic;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The letters of the feature in the game (FR-1, FR-2): <c>ModMail</c> letters of the priority Critical with the reusable action "Recovery options". The letters are not
    /// answered and not closed by the action (AC-1.4); the id of a letter is kept nowhere, <c>ModMail.Find</c> looks it up by the tag when that is needed.
    /// <list type="bullet">
    /// <item><c>rec:prewarning</c> "Default risk": the balance, the threshold and the way to the menu.</item>
    /// <item><c>rec:default</c> "Company in default": the balance, the threshold, the days before the bankruptcy and the way to the menu.</item>
    /// </list>
    /// A letter of a kind that still waits in the queue (a batch run, the loading screen hold letters back) is not sent twice. The mail service delivers the Critical letters
    /// at once on the day, bypassing its daily limits.
    /// </summary>
    internal sealed class RecoveryLetters : IRecoveryLetters
    {
        /// <summary>The id of the reusable action of both letters.</summary>
        internal const string ActionOpenMenu = "open-menu";

        /// <summary>The tag of the "default risk" letter (the prefix <c>rec</c> belongs to the feature).</summary>
        internal const string TagPreWarning = "rec:prewarning";

        /// <summary>The tag of the "company in default" letter.</summary>
        internal const string TagDefault = "rec:default";

        public bool SendPreWarning(double balance, double threshold)
        {
            return Send(TagPreWarning, "rec.mail.prewarning.subject", "rec.mail.prewarning.body", RecoveryViews.Money(balance), RecoveryViews.Money(threshold));
        }

        public bool SendDefault(double balance, double threshold, int daysToBankruptcy)
        {
            return Send(TagDefault, "rec.mail.default.subject", "rec.mail.default.body",
                RecoveryViews.Money(balance), RecoveryViews.Money(threshold), RecoveryUi.Num(daysToBankruptcy));
        }

        private static bool Send(string tag, string titleKey, string bodyKey, params string[] args)
        {
            ModMailRecord waiting = ModMail.Find(RecoveryApi.FeatureId, tag);
            if (waiting != null && !waiting.IsDelivered) return true;   // the same letter is still in the queue

            var message = new ModMailMessage
            {
                FeatureId = RecoveryApi.FeatureId,
                Tag = tag,
                TitleKey = titleKey,
                BodyKey = bodyKey,
                Args = args,
                Priority = MailPriority.Critical,
                Actions = new List<ModMailAction>
                {
                    new ModMailAction { Id = ActionOpenMenu, LabelKey = "rec.mail.action.openMenu", Repeatable = true },
                },
            };

            if (ModMail.Send(message) >= 0) return true;
            ModLog.Warn(RecoveryApi.FeatureId, "mail: the mail service did not accept the letter " + tag + "; it is tried again later");
            return false;
        }
    }

    /// <summary>
    /// The watcher of the player's default in the game (design.md, <c>RecoveryMonitor</c>): the day tick sends the "default risk" letter, the patch P3 sends the "company in
    /// default" letter. The rules, the flags of the episode and the choice of the moment are <see cref="RecoveryMonitorEngine"/>'s (pure logic with the tests); the letters are
    /// <see cref="RecoveryLetters"/>'s. Nothing here reacts to a company that is not the player's.
    /// </summary>
    internal sealed class RecoveryMonitor
    {
        private readonly RecoveryMonitorEngine _engine;

        public RecoveryMonitor(RecoveryEnvironment env)
        {
            _engine = new RecoveryMonitorEngine(env, new RecoveryLetters());
        }

        /// <summary>The rules of the letters.</summary>
        public RecoveryMonitorEngine Engine
        {
            get { return _engine; }
        }

        /// <summary>The day tick (<c>TickPhase.LateTick</c>): the warning of FR-1.</summary>
        public void OnDay()
        {
            _engine.OnDay();
        }

        /// <summary>The player's default has just begun (patch P3, player only): the letter of FR-2.</summary>
        public void OnPlayerDefault()
        {
            _engine.OnPlayerDefault();
        }

        /// <summary>
        /// A click on the action of a letter (<c>ModMail.RegisterActionHandler</c>): opens the menu on the summary. The letter stays as it is.
        /// </summary>
        public static void OnMailAction(ModMailRecord mail, string actionId)
        {
            if (actionId == RecoveryLetters.ActionOpenMenu) RecoveryApi.OpenMenu(RecoveryTab.Summary);
        }
    }
}
