using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PTMod.Core
{
    /// <summary>
    /// The phases of a game day: one per event of <c>DateController</c>, in the order <c>DateController.TriggerTicks</c> raises them
    /// (FR-4). Subscribe with <c>ctx.OnTick(TickPhase.X, handler)</c>.
    /// <list type="bullet">
    /// <item><see cref="EarlyTick"/> … <see cref="DayPassed"/> - every game day, in this order.</item>
    /// <item><see cref="CompanySpawnTick"/>, <see cref="MonthCacheUpdate"/>, <see cref="EarlyMonthPassed"/>, <see cref="MonthPassed"/> - the
    /// 1st of a month, after <see cref="DayPassed"/>; then <see cref="YearPassed"/> (1 January), <see cref="LateMonthPassed"/> (the 1st),
    /// <see cref="LateYearPassed"/> (1 January).</item>
    /// <item><see cref="FirstEverTick"/> - once per new game, at the start of the new-game fast-forward (so only subscribers with
    /// <see cref="TickOptions.RunDuringFastForward"/> hear it).</item>
    /// <item><see cref="FastForwardEnded"/> - once per new game, after the fast-forward, when <c>IsFastFowardSimulating</c> is false again
    /// and before <c>GameReady(NewGame)</c>; it reaches every subscriber although the loading screen is still up: the one place to set up
    /// once the state the AI needs at the start (the owner's policy).</item>
    /// </list>
    /// </summary>
    public enum TickPhase
    {
        EarlyTick, AIBehaviourTick, PreContractSellTick, ClientProductionTick, ContractProductionTick,
        ContractSellTick, ContractTick, ProductionTick, MarketSellTick, Tick, LateTick, DayPassed,
        CompanySpawnTick, MonthCacheUpdate, EarlyMonthPassed, MonthPassed, YearPassed,
        LateMonthPassed, LateYearPassed, FirstEverTick, FastForwardEnded
    }

    /// <summary>
    /// When a tick handler runs although the game is not in its normal course (FR-4 items 2-3). By default (<see cref="None"/>) a handler is
    /// skipped during the new-game fast-forward and while the loading screen is up.
    /// </summary>
    [Flags]
    public enum TickOptions
    {
        None = 0,

        /// <summary>Also during the new-game fast-forward (<c>DateController.IsFastFowardSimulating</c>, ≈9 100 days for a start in 2000).
        /// The fast-forward runs under the loading screen; this flag alone is enough then.</summary>
        RunDuringFastForward = 1,

        /// <summary>Also while the loading screen is up outside the fast-forward (<c>GameManager.IsLoading</c>: its fade after a load or a new game).</summary>
        RunDuringLoading = 2
    }

    /// <summary>
    /// The subscribers of the tick phases (FR-4): an array per phase in calling order - priority (higher first), then the order of
    /// subscription - with no delegate chains. <see cref="Dispatch"/> calls them one by one: the guards first (the flags of the
    /// subscription against the fast-forward and the loading state the caller passes), then the feature's state (a feature that is not active
    /// is skipped, its subscription stays), then the handler through <c>ModHooks.Guard</c> (an exception is counted against the feature,
    /// FR-2 items 3-4, and the next handler runs). The time of the handlers goes into <see cref="Stats"/>.
    /// <para>
    /// Subscribing and disposing are copy-on-write: a dispatch that runs keeps its snapshot, a handler subscribed from inside a handler runs
    /// from the next dispatch on, a disposed one is not called again even inside the current dispatch. Knows nothing of the game (the game's
    /// side is <see cref="ModTicks"/>). Main thread only.
    /// </para>
    /// </summary>
    internal static class TickDispatcher
    {
        /// <summary>Number of <see cref="TickPhase"/> values.</summary>
        public const int PhaseCount = (int)TickPhase.FastForwardEnded + 1;

        private static readonly Subscription[] None = new Subscription[0];
        private static readonly Subscription[][] ByPhase = CreatePhases();
        private static readonly double MsPerTimestamp = 1000.0 / Stopwatch.Frequency;

        /// <summary>Phases of a regular game day (<see cref="TickPhase.EarlyTick"/> … <see cref="TickPhase.LateYearPassed"/>); the rest are one-off.</summary>
        public const int DayPhaseCount = (int)TickPhase.LateYearPassed + 1;

        /// <summary>The handler times of the last 30 game days.</summary>
        internal static readonly TickStatsWindow Stats = new TickStatsWindow(PhaseCount, DayPhaseCount);

        /// <summary>
        /// Adds a subscriber (the checks of the arguments are the caller's, <see cref="ModTicks.Subscribe"/>). The same handler of the same
        /// feature on the same phase twice is one subscription: the second call gets the first token back with a warning.
        /// </summary>
        public static IDisposable Add(string featureId, TickPhase phase, Action handler, int priority, TickOptions options)
        {
            int index = (int)phase;
            Subscription[] current = ByPhase[index];
            foreach (Subscription existing in current)
            {
                if (existing.FeatureId == featureId && existing.Handler.Equals(handler))
                {
                    ModLog.Warn(featureId, "ticks: " + existing.Site + " is already subscribed; the second subscription is ignored");
                    return existing;
                }
            }

            var subscription = new Subscription(featureId, phase, handler, priority, options);

            // Higher priority first; equal priorities keep the order of subscription.
            int at = current.Length;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].Priority < priority)
                {
                    at = i;
                    break;
                }
            }

            var list = new Subscription[current.Length + 1];
            Array.Copy(current, 0, list, 0, at);
            list[at] = subscription;
            Array.Copy(current, at, list, at + 1, current.Length - at);
            ByPhase[index] = list;

            ModLog.Debug(featureId, "ticks: " + subscription.Site + " subscribed" + Describe(priority, options));
            return subscription;
        }

        /// <summary>Some subscriber listens to the phase (active or not): the caller skips reading the game's state otherwise.</summary>
        public static bool HasSubscribers(TickPhase phase)
        {
            int index = (int)phase;
            return index >= 0 && index < PhaseCount && ByPhase[index].Length > 0;
        }

        /// <summary>Subscribers of the phase, or of all phases for null.</summary>
        public static int CountOf(TickPhase? phase)
        {
            if (phase.HasValue)
            {
                int index = (int)phase.Value;
                return index >= 0 && index < PhaseCount ? ByPhase[index].Length : 0;
            }

            int total = 0;
            for (int i = 0; i < PhaseCount; i++) total += ByPhase[i].Length;
            return total;
        }

        /// <summary>
        /// Calls the subscribers of the phase in order. <paramref name="fastForward"/>: only subscribers with
        /// <see cref="TickOptions.RunDuringFastForward"/> run (the loading state does not matter then); otherwise, with
        /// <paramref name="loading"/>, only those with <see cref="TickOptions.RunDuringLoading"/>. Returns how many handlers ran.
        /// Never throws for a handler: each runs inside <c>ModHooks.Guard</c>.
        /// </summary>
        public static int Dispatch(TickPhase phase, bool fastForward, bool loading)
        {
            int index = (int)phase;
            if (index < 0 || index >= PhaseCount) return 0;

            Subscription[] list = ByPhase[index];
            if (list.Length == 0) return 0;

            TickOptions needed = fastForward ? TickOptions.RunDuringFastForward : loading ? TickOptions.RunDuringLoading : TickOptions.None;
            int ran = 0;
            long elapsed = 0;
            for (int i = 0; i < list.Length; i++)
            {
                Subscription subscription = list[i];
                if (subscription.Disposed) continue;
                if (needed != TickOptions.None && (subscription.Options & needed) == 0) continue;
                if (!ModFeatures.IsActive(subscription.FeatureId)) continue;

                long started = Stopwatch.GetTimestamp();
                ModHooks.Guard(subscription.FeatureId, subscription.Site, subscription.Handler);
                elapsed += Stopwatch.GetTimestamp() - started;
                ran++;
            }

            if (ran > 0) Stats.Record(index, elapsed * MsPerTimestamp);
            return ran;
        }

        /// <summary>The subscribers of the phase in calling order, one line each: <c>feature: Type.Method@Phase (priority 0, RunDuringLoading)</c>.</summary>
        public static IReadOnlyList<string> Describe(TickPhase phase)
        {
            var lines = new List<string>();
            int index = (int)phase;
            if (index < 0 || index >= PhaseCount) return lines;

            foreach (Subscription subscription in ByPhase[index])
            {
                lines.Add(subscription.FeatureId + ": " + subscription.Site + Describe(subscription.Priority, subscription.Options));
            }
            return lines;
        }

        private static void Remove(Subscription subscription)
        {
            int index = (int)subscription.Phase;
            Subscription[] current = ByPhase[index];
            int at = Array.IndexOf(current, subscription);
            if (at < 0) return;

            if (current.Length == 1)
            {
                ByPhase[index] = None;
            }
            else
            {
                var list = new Subscription[current.Length - 1];
                Array.Copy(current, 0, list, 0, at);
                Array.Copy(current, at + 1, list, at, current.Length - at - 1);
                ByPhase[index] = list;
            }
            ModLog.Debug(subscription.FeatureId, "ticks: " + subscription.Site + " unsubscribed");
        }

        private static string Describe(int priority, TickOptions options)
        {
            return " (priority " + priority + (options == TickOptions.None ? "" : ", " + options) + ")";
        }

        private static Subscription[][] CreatePhases()
        {
            var phases = new Subscription[PhaseCount][];
            for (int i = 0; i < PhaseCount; i++) phases[i] = None;
            return phases;
        }

        /// <summary>Forgets every subscriber and the statistics (the offline harness only: the game never clears the subscriptions).</summary>
        internal static void ResetForTests()
        {
            for (int i = 0; i < PhaseCount; i++) ByPhase[i] = None;
            Stats.Reset();
        }

        /// <summary>One subscriber; also the token the feature disposes to unsubscribe (idempotent, safe inside a dispatch).</summary>
        private sealed class Subscription : IDisposable
        {
            public Subscription(string featureId, TickPhase phase, Action handler, int priority, TickOptions options)
            {
                FeatureId = featureId;
                Phase = phase;
                Handler = handler;
                Priority = priority;
                Options = options;
                Site = SiteOf(handler, phase);
            }

            public string FeatureId { get; }

            public TickPhase Phase { get; }

            public Action Handler { get; }

            public int Priority { get; }

            public TickOptions Options { get; }

            /// <summary>
            /// <c>Type.Method@Phase</c>: the site of the handler for the fault counter. It has no '/', so for the core the unit the breaker
            /// switches off is this handler alone.
            /// </summary>
            public string Site { get; }

            public bool Disposed { get; private set; }

            public void Dispose()
            {
                if (Disposed) return;
                Disposed = true;
                try
                {
                    Remove(this);
                }
                catch (Exception ex)
                {
                    ModLog.Error(FeatureId, "ticks: cannot unsubscribe " + Site, ex);
                }
            }

            public override string ToString()
            {
                return FeatureId + ": " + Site + (Disposed ? " (disposed)" : "");
            }

            private static string SiteOf(Action handler, TickPhase phase)
            {
                System.Reflection.MethodInfo method = handler.Method;
                string name = method == null ? "?" : (method.DeclaringType == null ? "" : method.DeclaringType.Name + ".") + method.Name;
                return name + "@" + phase;
            }
        }
    }
}
