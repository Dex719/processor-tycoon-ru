using System;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The close of a counter-strike (design.md, Component 9 and "Подписки на события"; FR-7 item 4): while a <c>CounterLaunch</c> of a rival is in
    /// effect, the first CPU the rival releases in the same segment is its answer to the player's launch, and the player gets the mail
    /// "Counter-strike" (tag <c>rival:counter:&lt;reactionId&gt;</c>) and the line <c>counterLaunchDone</c> in the history. Which releases count
    /// and which reaction they answer is decided here; the game side (<c>CounterLaunchWatcher</c>) reads the CPU and sends the mail. Pure.
    /// </summary>
    public static class CounterLaunchRules
    {
        /// <summary>
        /// A release is worth looking at: a CPU of an AI company, not a variant of another CPU, not released in the fast-forward of a new game and
        /// not while a save loads (the core does not raise <c>CpuReleased</c> then, but the watcher checks again). A transfer between companies is
        /// not a release at all (<c>ModEvents.CpuTransferred</c> is not listened to).
        /// </summary>
        public static bool IsRelevantRelease(bool ownedByRival, bool isVariant, bool duringFastForward, bool loading, bool fastForwarding)
        {
            return ownedByRival && !isVariant && !duringFastForward && !loading && !fastForwarding;
        }

        /// <summary>
        /// The <c>CounterLaunch</c> of the rival in effect in <paramref name="segment"/> that has not been answered yet: it has started (its
        /// handler announced it) and no <c>counterLaunchDone</c> was written since. Null for <see cref="Segment.Any"/> and when there is none.
        /// </summary>
        public static ActiveReaction FindOpen(ReactionBook reactions, int uniqueId, Segment segment)
        {
            if (reactions == null || segment == Segment.Any) return null;

            string type = ReactionKinds.NameOf(ReactionType.CounterLaunch);
            foreach (ActiveReaction active in reactions.ActiveOf(uniqueId))
            {
                if (!string.Equals(active.type, type, StringComparison.Ordinal) || active.segment != (int)segment) continue;

                DateTime start;
                if (!RivalDates.TryParse(active.start, out start)) start = DateTime.MinValue;
                if (!reactions.HasHistory(uniqueId, type, HistoryOutcomes.Started, start, active.segment)) continue;
                if (reactions.HasHistory(uniqueId, type, HistoryOutcomes.CounterLaunchDone, start, active.segment)) continue;
                return active;
            }
            return null;
        }

        /// <summary>The counter-strike was answered: writes <c>counterLaunchDone</c>, so the same reaction does not send the mail again.</summary>
        public static void MarkDone(ReactionBook reactions, ActiveReaction active, DateTime today)
        {
            if (reactions == null || active == null) return;
            reactions.AddHistory(active.uid, today, active.type, active.segment, HistoryOutcomes.CounterLaunchDone);
        }
    }
}
