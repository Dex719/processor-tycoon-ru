using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PTMod.Core.Logic.Events
{
    /// <summary>What <see cref="ReleaseDeduper.Admit"/> decided about one call of the game's <c>AddCpu</c>.</summary>
    public enum ReleaseVerdict
    {
        /// <summary>A new CPU is released: raise <c>ModEvents.CpuReleased</c>.</summary>
        Raise,

        /// <summary>There is no CPU (null): nothing to say.</summary>
        NoCpu,

        /// <summary>A save is being read (the loading screen is up, the new-game fast-forward is not running): the CPU is an old one, no event.</summary>
        SaveLoading,

        /// <summary>The event for this CPU object was already raised (or the object was seen while loading): an event comes at most once per object.</summary>
        Duplicate
    }

    /// <summary>
    /// The rule behind <c>ModEvents.CpuReleased</c> (FR-23 items 2-3, P20): which calls of <c>Company.AddCpu</c> / <c>AICompany.AddCpu</c> are a
    /// release and which are not. The game calls both methods for every CPU of a save while it loads, and nothing says it never calls one twice
    /// for the same object, so five features that each patched <c>AddCpu</c> once processed CPUs twice or skipped them.
    /// <list type="bullet">
    /// <item><b>Loading a save.</b> While the loading screen is up and the new-game fast-forward is not running, the call is a load of an old CPU:
    /// <see cref="ReleaseVerdict.SaveLoading"/>. The new game is different: its fast-forward also runs under the loading screen, and the AI's
    /// releases there are real, so with <c>duringFastForward</c> the CPU is admitted (the event carries <c>duringFastForward = true</c>, AC-23.4).</item>
    /// <item><b>Once per object.</b> The objects are remembered by reference: a look-alike CPU (same name, same numbers) is another CPU, and
    /// whatever <c>Equals</c> or <c>GetHashCode</c> a game class has must not matter. An object seen while loading is remembered too: it came
    /// from the save and is not a new release if the game adds it again.</item>
    /// <item><b>A scene is a session.</b> <see cref="Clear"/> forgets everything; the core calls it when the scene changes, so the set never holds
    /// the CPUs of a game that is gone.</item>
    /// </list>
    /// Pure logic without Unity and the game (the CPU is an <c>object</c> here); main thread only. The set holds the objects until
    /// <see cref="Clear"/>: a few thousand references at most.
    /// </summary>
    public sealed class ReleaseDeduper
    {
        private readonly HashSet<object> _seen = new HashSet<object>(ReferenceComparer.Instance);

        /// <summary>Objects remembered now.</summary>
        public int Count
        {
            get { return _seen.Count; }
        }

        /// <summary>Calls that were admitted (<see cref="ReleaseVerdict.Raise"/>).</summary>
        public int Admitted { get; private set; }

        /// <summary>Calls turned away because a save was loading.</summary>
        public int SkippedLoading { get; private set; }

        /// <summary>Calls turned away because the object was seen before.</summary>
        public int SkippedDuplicates { get; private set; }

        /// <summary>Calls with a null CPU.</summary>
        public int SkippedNoCpu { get; private set; }

        /// <summary>How many times the set was cleared (scene changes).</summary>
        public int Clears { get; private set; }

        /// <summary>
        /// A save is being read: the loading screen is up and the new-game fast-forward does not run. Not a release, and not a transfer either.
        /// </summary>
        public static bool IsSaveLoading(bool isLoading, bool duringFastForward)
        {
            return isLoading && !duringFastForward;
        }

        /// <summary>
        /// Judges one call of <c>AddCpu</c> and remembers the object (except a null). <see cref="ReleaseVerdict.Raise"/> comes at most once
        /// for an object between two <see cref="Clear"/> calls, never for a null and never while a save loads.
        /// </summary>
        /// <param name="cpu">The CPU the game added.</param>
        /// <param name="isLoading"><c>GameManager.IsLoading</c>: the loading screen is up.</param>
        /// <param name="duringFastForward"><c>DateController.IsFastFowardSimulating</c>: the new-game fast-forward runs.</param>
        public ReleaseVerdict Admit(object cpu, bool isLoading, bool duringFastForward)
        {
            if (cpu == null)
            {
                SkippedNoCpu++;
                return ReleaseVerdict.NoCpu;
            }

            bool added = _seen.Add(cpu);

            if (IsSaveLoading(isLoading, duringFastForward))
            {
                SkippedLoading++;
                return ReleaseVerdict.SaveLoading;
            }

            if (!added)
            {
                SkippedDuplicates++;
                return ReleaseVerdict.Duplicate;
            }

            Admitted++;
            return ReleaseVerdict.Raise;
        }

        /// <summary>The object was seen (admitted, or met while a save loaded) since the last <see cref="Clear"/>.</summary>
        public bool Contains(object cpu)
        {
            return cpu != null && _seen.Contains(cpu);
        }

        /// <summary>Forgets every object (the scene changed); the counters stay.</summary>
        public void Clear()
        {
            _seen.Clear();
            Clears++;
        }

        public override string ToString()
        {
            return "ReleaseDeduper(" + _seen.Count + " seen, " + Admitted + " admitted, " + SkippedLoading + " loading, "
                   + SkippedDuplicates + " duplicate)";
        }

        /// <summary>Reference equality for the set: net48 has no <c>ReferenceEqualityComparer</c>.</summary>
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
