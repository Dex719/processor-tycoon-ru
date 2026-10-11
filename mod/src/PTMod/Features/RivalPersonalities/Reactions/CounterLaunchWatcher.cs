using System;
using System.Globalization;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The close of a counter-strike (design.md, Component 9 and "Подписки на события"; FR-7 item 4, TSK-016): listens to
    /// <c>ModEvents.CpuReleased</c> (the feature subscribes once in <c>Register</c>, the core raises it through <c>ModHooks.Raise</c>, never during a
    /// load) and, when a rival releases a CPU in the segment of its <c>CounterLaunch</c> that is in effect, sends the mail "Counter-strike"
    /// (<c>Normal</c>, tag <c>rival:counter:&lt;reactionId&gt;</c>) and writes <c>counterLaunchDone</c> to the history. A variant of a CPU, a release
    /// in the fast-forward of a new game and a transfer between companies (<c>CpuTransferred</c> is not listened to) do not count; the CPU of the
    /// player is not a rival's. The segment of a rival's CPU is the one it was designed for (<c>Strategy.TargetMarket</c>). The rules are
    /// <see cref="CounterLaunchRules"/>'s. Without the event system no <c>CounterLaunch</c> is ever planned, so there is nothing to watch for: the
    /// rest of the feature works on. Main thread only; the caller guards the call.
    /// </summary>
    internal sealed class CounterLaunchWatcher
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private readonly ReactionBook _reactions;

        public CounterLaunchWatcher(ReactionBook reactions)
        {
            _reactions = reactions;
        }

        /// <summary><c>ModEvents.CpuReleased</c>: a CPU of a company was released.</summary>
        public void OnCpuReleased(ICompany owner, GameCpu cpu, bool isVariant, bool duringFastForward)
        {
            var rival = owner as AICompany;
            bool byRival = !ReferenceEquals(rival, null) && rival != null && !rival.IsFoundry;
            if (cpu == null || !CounterLaunchRules.IsRelevantRelease(byRival, isVariant, duringFastForward, ModTicks.IsLoading, ModTicks.IsFastForwarding)) return;

            int market = cpu.Strategy != null ? cpu.Strategy.TargetMarket : -1;
            if (market < 0 || market > 3) return;
            var segment = (Segment)market;

            ActiveReaction reaction = CounterLaunchRules.FindOpen(_reactions, rival.UniqueID, segment);
            if (reaction == null) return;

            // written first: a failure of the mail must not make the same reaction answer twice
            CounterLaunchRules.MarkDone(_reactions, reaction, ModTicks.Today);
            int mailId = IntelMail.SendCounterStrike(reaction, cpu.Name, ReactionExecutor.SegmentName(market));
            ModLog.Info(Feature, "reactions: " + reaction.id + " (CounterLaunch of ai:" + rival.UniqueID.ToString(CultureInfo.InvariantCulture)
                                 + ") answered with a CPU in segment " + market.ToString(CultureInfo.InvariantCulture) + "; mail #"
                                 + mailId.ToString(CultureInfo.InvariantCulture));
        }
    }
}
