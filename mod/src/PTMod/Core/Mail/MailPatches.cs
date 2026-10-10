using System;
using System.Collections.Generic;
using GameEmail = ProcessorTycoon.Desktop.Email.Email;
using GameEmailWindow = ProcessorTycoon.Desktop.Email.EmailWindow;

namespace PTMod.Core
{
    /// <summary>
    /// P15: <c>EmailWindow.GetEmails</c>, postfix. Keeps the letters of the mod out of the game's own save (FR-14 item 10, AC-14.3). The game
    /// builds the <c>Emails</c> of its save from this method alone (<c>SaveHandler.Save</c> is the only caller) and the method returns a copy of
    /// its list, so the mod's letters (<c>EmailID</c> of 100000 and above) are removed from that copy; the window's own list is not touched. The
    /// letters are saved in the side file (<c>MailStore</c>) instead, so a save made with the mod loads in the vanilla game without them and
    /// the mod does not depend on the vanilla save format.
    /// <list type="bullet">
    /// <item>The method is reserved for the core (<c>ReservedTargets</c>: service <c>ModMail</c>): a feature's patch on it is rejected.</item>
    /// <item>While the patch is not in force (no Harmony, another game version, the breaker of unit <c>P15</c> tripped) <see cref="IsInEffect"/>
    /// is false and <c>ModMail</c> does not put any letter into the window: a letter that the game could write into its save is worse than
    /// a letter that waits in the queue.</item>
    /// </list>
    /// Found by <c>ModHooks</c>' scan of the core at the Patch stage (Harmony id <c>ptmod.mod-framework</c>). Do not call <c>GetEmails</c> to
    /// read the letters of the mod: the answer has none of them by design.
    /// </summary>
    internal static class MailPatches
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string Site = "P15/postfix";

        private static readonly Predicate<GameEmail> IsModMail = IsModMailLetter;

        /// <summary>The patch is applied to the game and its breaker has not tripped: letters may be put into the window.</summary>
        internal static bool IsInEffect
        {
            get { return ModHooks.IsApplied(typeof(MailPatches)) && ModHooks.CanRun(CoreId, Site); }
        }

        /// <summary>
        /// Removes the letters with an id of 100000 and above from a copy of the game's list of letters; returns how many. A null list and a
        /// null letter (never seen in the game) are tolerated.
        /// </summary>
        internal static int RemoveModMails(List<GameEmail> emails)
        {
            return emails == null ? 0 : emails.RemoveAll(IsModMail);
        }

        private static bool IsModMailLetter(GameEmail email)
        {
            return email != null && email.EmailID >= ModMail.FirstModMailId;
        }

        [ModPatch(typeof(GameEmailWindow), "GetEmails", PatchKind.Postfix)]
        private static void GetEmailsPostfix(List<GameEmail> __result)
        {
            try
            {
                if (!ModHooks.CanRun(CoreId, Site)) return;
                RemoveModMails(__result);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, Site, ex);
            }
        }
    }
}
