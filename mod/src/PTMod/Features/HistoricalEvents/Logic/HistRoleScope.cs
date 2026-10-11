using System;
using System.Collections.Generic;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// What the second half of the calendar knows besides the world while a condition is evaluated: the cases and export rules that the services
    /// keep (TSK-009, TSK-011), the debug flag, and where to report a predicate that was given bad parameters. The services fill the delegates when
    /// they exist; until then the predicates that ask them answer "no".
    /// </summary>
    public sealed class HistState
    {
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.Ordinal);

        /// <summary><c>(caseId, targetNotPlayer)</c>: the case is open (and, when asked, its target is not the player). Null counts as "no case is open".</summary>
        public Func<string, bool, bool> CaseActive { get; set; }

        /// <summary>The threshold of an export rule in IPS, or null when the rule is not in force. Null function counts as "no rule".</summary>
        public Func<string, float?> ExportThreshold { get; set; }

        /// <summary><c>debugForceLeader</c>: the player passes the share checks of the roles <c>leader</c> and <c>challenger</c> (see <see cref="RoleOptions.ForceLeader"/>).</summary>
        public bool ForceLeader { get; set; }

        /// <summary>Where a line about a bad parameter goes (the log); may be null.</summary>
        public Action<string> Warn { get; set; }

        /// <summary>Reports a problem once per key (a predicate is evaluated for every entry every day it waits).</summary>
        public void WarnOnce(string key, string message)
        {
            if (Warn == null || !_warned.Add(key)) return;
            Warn(message);
        }
    }

    /// <summary>The roles and the world a condition is evaluated in (<see cref="HistRoleScope"/> keeps the current one).</summary>
    public sealed class HistScopeFrame
    {
        public HistScopeFrame(RoleResult roles, IWorldView world, MarketShareCache shares, HistState state)
        {
            Roles = roles ?? new RoleResult();
            World = world;
            Shares = shares ?? new MarketShareCache();
            State = state ?? new HistState();
        }

        /// <summary>The companies of the roles of the entry being judged; never null (an empty result outside an entry).</summary>
        public RoleResult Roles { get; private set; }

        public IWorldView World { get; private set; }

        public MarketShareCache Shares { get; private set; }

        public HistState State { get; private set; }
    }

    /// <summary>
    /// The way the roles of an entry reach the predicates <c>hist.*</c> (design.md, Component 5): the predicates are registered once, as plain
    /// functions of the node and the context (<c>EventConditions.Register</c>), and cannot be handed the roles, so the evaluator pushes a frame around
    /// each evaluation and the predicates read <see cref="Current"/>. A frame is pushed and popped with <c>using</c>, so it goes even when the condition
    /// throws. The <i>ambient</i> frame (set once by the feature) answers when a predicate is evaluated outside an entry, for example by the event
    /// engine for a condition of its own. Main thread only.
    /// </summary>
    public static class HistRoleScope
    {
        private static readonly Stack<HistScopeFrame> Frames = new Stack<HistScopeFrame>();
        private static HistScopeFrame _ambient;

        /// <summary>The frame of the innermost evaluation, else the ambient one; null when nothing was set up (the predicates answer false).</summary>
        public static HistScopeFrame Current
        {
            get { return Frames.Count > 0 ? Frames.Peek() : _ambient; }
        }

        /// <summary>The number of frames pushed and not yet popped.</summary>
        public static int Depth
        {
            get { return Frames.Count; }
        }

        /// <summary>Makes <paramref name="frame"/> the current one until the returned token is disposed.</summary>
        public static IDisposable Push(HistScopeFrame frame)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            Frames.Push(frame);
            return new Popper(frame);
        }

        /// <summary>The frame for evaluations outside an entry (the world and the services, no roles). Null removes it.</summary>
        public static void SetAmbient(HistScopeFrame frame)
        {
            _ambient = frame;
        }

        /// <summary>Forgets every frame, the ambient one too (the tests, a scene change).</summary>
        public static void Reset()
        {
            Frames.Clear();
            _ambient = null;
        }

        private sealed class Popper : IDisposable
        {
            private readonly HistScopeFrame _frame;
            private bool _done;

            public Popper(HistScopeFrame frame)
            {
                _frame = frame;
            }

            public void Dispose()
            {
                if (_done) return;
                _done = true;

                // Normally the top is ours. If something above it was left (a frame never disposed), take those off with it.
                while (Frames.Count > 0)
                {
                    HistScopeFrame top = Frames.Pop();
                    if (ReferenceEquals(top, _frame)) break;
                }
            }
        }
    }
}
