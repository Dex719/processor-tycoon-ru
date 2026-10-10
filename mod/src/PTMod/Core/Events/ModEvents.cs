using System;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Events;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using Cpu = ProcessorTycoon.Hardware.Cpu;
using GameDates = ProcessorTycoon.TimeSystem.DateController;
using GameManagerType = ProcessorTycoon.GameManager;

namespace PTMod.Core
{
    /// <summary>
    /// The events of the game that more than one feature needs (FR-23): a CPU is released, a CPU moves to another company. The core raises the
    /// first one from the single postfix P20 on <c>Company.AddCpu</c> and <c>AICompany.AddCpu</c> (<c>CpuEventPatches</c>), so a feature never
    /// patches those methods itself (it would be rejected, FR-2 item 6) and never sees a CPU twice or a CPU of a loading save.
    /// <list type="bullet">
    /// <item><b><see cref="CpuReleased"/>.</b> Not while a save loads (the game calls <c>AddCpu</c> for every CPU of the save), not for a null
    /// CPU, and at most once per CPU object in a scene (<see cref="ReleaseDeduper"/>). The new game's fast-forward runs under the loading
    /// screen, but its releases are real: they come with <c>duringFastForward = true</c> and a feature that wants a quiet fast-forward
    /// ignores them by that flag (the owner's policy: no events, mail or reactions then).</item>
    /// <item><b><see cref="CpuTransferred"/>.</b> The only event a feature raises, with <see cref="RaiseCpuTransferred"/>: the feature that moves
    /// the CPU (a takeover in <c>rival-acquisitions</c>) knows it, the core just hands it to the subscribers. A transfer is never a release, and is
    /// not raised while a save loads.</item>
    /// <item><b>Handlers</b> are called one by one through <see cref="ModHooks"/> in the order of subscription: only for features that are
    /// active, each in try/catch, a failure counted against the feature of the handler and the next handler still runs (FR-2 item 8).</item>
    /// </list>
    /// Without Harmony (or when the P20 patches are not applied) <see cref="CpuReleased"/> is never raised; <see cref="RaiseCpuTransferred"/> works
    /// anyway. Main thread only. Never throws into the game.
    /// </summary>
    public static class ModEvents
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string ReleasedEvent = "ModEvents.CpuReleased";
        private const string TransferredEvent = "ModEvents.CpuTransferred";
        private const string TransferSite = "ModEvents/transfer";

        private static readonly ReleaseDeduper Deduper = new ReleaseDeduper();

        private static bool _started;
        private static int _transfersRaised;
        private static int _transfersIgnored;

        /// <summary>The test seam for <c>GameManager.IsLoading</c> (null: read the game).</summary>
        internal static Func<bool> LoadingProbe;

        /// <summary>The test seam for <c>DateController.IsFastFowardSimulating</c> (null: read the game).</summary>
        internal static Func<bool> FastForwardProbe;

        // ---- public API ----

        /// <summary>
        /// A CPU was released (FR-23 item 1): <c>owner</c> is the company that owns it, <c>isVariant</c> is <c>cpu.Base != null</c> (a variant
        /// of an earlier design), <c>duringFastForward</c> is <c>DateController.IsFastFowardSimulating</c> (the new game's fast-forward). Raised
        /// after the game's own <c>AddCpu</c> finished, so the CPU is already in the company's lists. Not raised while a save loads and at most
        /// once per CPU object.
        /// </summary>
        public static event Action<ICompany, Cpu, bool, bool> CpuReleased;

        /// <summary>
        /// A CPU moved from one company to another (FR-23 item 4): <c>from</c>, <c>to</c>, <c>cpu</c>. <see cref="CpuReleased"/> does not
        /// come for it. Raised only by <see cref="RaiseCpuTransferred"/>.
        /// </summary>
        public static event Action<ICompany, ICompany, Cpu> CpuTransferred;

        /// <summary>
        /// Announces a transfer of <paramref name="cpu"/> from <paramref name="from"/> to <paramref name="to"/> to the subscribers of
        /// <see cref="CpuTransferred"/>. Called by the feature that did the transfer, after it did it, with its own id (a failure of a handler
        /// is counted against the handler's feature, not the caller). Ignored with a warning when a company or the CPU is null, when both
        /// companies are the same, when <paramref name="featureId"/> is not a known feature, and silently while a save loads or when the
        /// feature is Faulted or Unavailable. Never throws.
        /// </summary>
        public static void RaiseCpuTransferred(string featureId, ICompany from, ICompany to, Cpu cpu)
        {
            try
            {
                if (string.IsNullOrEmpty(featureId))
                {
                    Ignored(CoreId, "RaiseCpuTransferred without a feature id is ignored");
                    return;
                }
                if (featureId != CoreId && ModFeatures.Find(featureId) == null)
                {
                    Ignored(CoreId, "RaiseCpuTransferred from '" + featureId + "', which is not a known feature, is ignored");
                    return;
                }
                if (!ModHooks.CanRun(featureId, TransferSite))
                {
                    _transfersIgnored++;
                    return;
                }
                if (ReferenceEquals(cpu, null) || ReferenceEquals(from, null) || ReferenceEquals(to, null))
                {
                    Ignored(featureId, "RaiseCpuTransferred with a missing " + (ReferenceEquals(cpu, null) ? "CPU" : "company") + " is ignored");
                    return;
                }
                if (ReferenceEquals(from, to))
                {
                    Ignored(featureId, "RaiseCpuTransferred from a company to itself is ignored");
                    return;
                }

                if (ReleaseDeduper.IsSaveLoading(ReadLoading(), ReadFastForward()))
                {
                    _transfersIgnored++;
                    ModLog.Debug(featureId, "events: CpuTransferred during the load of a save is not raised");
                    return;
                }

                _transfersRaised++;
                ModLog.Debug(featureId, "events: CpuTransferred raised by " + featureId);
                ModHooks.Raise(TransferredEvent, CpuTransferred, from, to, cpu);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, TransferSite, ex);
            }
        }

        // ---- diagnostics ----

        /// <summary>The P20 patches are in the game (without them <see cref="CpuReleased"/> never comes).</summary>
        internal static bool IsReleaseTracked
        {
            get { return ModHooks.IsApplied(typeof(CpuEventPatches)); }
        }

        /// <summary>The "once per CPU" memory and its counters (the Diagnostics tab).</summary>
        internal static ReleaseDeduper Releases
        {
            get { return Deduper; }
        }

        /// <summary>Transfers that were handed to the subscribers.</summary>
        internal static int TransfersRaised
        {
            get { return _transfersRaised; }
        }

        /// <summary>Transfers that were not raised (loading, a feature that is off, a bad call).</summary>
        internal static int TransfersIgnored
        {
            get { return _transfersIgnored; }
        }

        // ---- P20 (CpuEventPatches) ----

        /// <summary>At least one handler listens: the postfix of P20 does nothing else without it (the budget of 20 microseconds with no subscribers).</summary>
        internal static bool HasReleaseSubscribers
        {
            get { return CpuReleased != null; }
        }

        /// <summary>
        /// The game added <paramref name="cpu"/> to <paramref name="owner"/> (the postfix of <c>Company.AddCpu</c> or <c>AICompany.AddCpu</c>):
        /// decides whether it is a release and, if it is, raises <see cref="CpuReleased"/>. May throw: the patch counts it as a fault of its unit.
        /// </summary>
        internal static void CpuAdded(ICompany owner, Cpu cpu)
        {
            bool fastForward = ReadFastForward();
            bool loading = ReadLoading();

            ReleaseVerdict verdict = Deduper.Admit(cpu, loading, fastForward);
            if (verdict != ReleaseVerdict.Raise)
            {
                if (verdict == ReleaseVerdict.Duplicate) ModLog.Debug(CoreId, "events: CpuReleased for an object that was seen before is not raised again");
                return;
            }

            bool isVariant = cpu.Base != null;
            ModLog.Debug(CoreId, "events: CpuReleased '" + cpu.Name + "', variant " + (isVariant ? "yes" : "no") + ", fast-forward " + (fastForward ? "yes" : "no"));
            ModHooks.Raise(ReleasedEvent, CpuReleased, owner, cpu, isVariant, fastForward);
        }

        // ---- scenes ----

        /// <summary>HostReady stage: the memory of released CPUs lives for one scene, so it is cleared when the scene goes and when the next one is ready.</summary>
        [CoreInit(CoreInitStage.HostReady, Optional = true)]
        internal static void Start()
        {
            if (_started) return;
            _started = true;

            PTModHost.SceneLeaving += OnSceneLeaving;
            PTModHost.SceneReady += OnSceneReady;
        }

        private static void OnSceneLeaving()
        {
            ClearScene();
        }

        private static void OnSceneReady(int scene)
        {
            ClearScene();   // a SceneLeaving that never came must not leave the old scene's CPUs in the set
        }

        private static void ClearScene()
        {
            try
            {
                Deduper.Clear();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "events: cannot clear the memory of released CPUs", ex);
            }
        }

        /// <summary>Forgets the memory of released CPUs and the probes (the offline harness only: the game never resets the service).</summary>
        internal static void ResetForTests()
        {
            Deduper.Clear();
            LoadingProbe = null;
            FastForwardProbe = null;
            _transfersRaised = 0;
            _transfersIgnored = 0;
        }

        // ---- reading the game's state ----

        private static void Ignored(string featureId, string what)
        {
            _transfersIgnored++;
            ModLog.Warn(featureId, "events: " + what);
        }

        /// <summary>
        /// <c>GameManager.IsLoading</c>: the loading screen is up. When the game cannot say (no manager, no loading screen yet) the answer
        /// is "loading": better no event than an event for a CPU of a save that is being read.
        /// </summary>
        private static bool ReadLoading()
        {
            Func<bool> probe = LoadingProbe;
            if (probe != null) return probe();

            try
            {
                return LoadingNow();
            }
            catch (NullReferenceException)
            {
                return true;   // GameManager.IsLoading reads LoadingScreen.Instance, which may not exist
            }
        }

        /// <summary><c>DateController.IsFastFowardSimulating</c>; false when there is no <c>DateController</c>.</summary>
        private static bool ReadFastForward()
        {
            Func<bool> probe = FastForwardProbe;
            if (probe != null) return probe();

            return FastForwardNow();
        }

        // The game's classes are touched only here; if they do not match the game, the JIT fails in the method and the caller catches it.

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool LoadingNow()
        {
            GameManagerType manager = GameManagerType.Instance;
            return manager == null || manager.IsLoading;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool FastForwardNow()
        {
            GameDates dates = GameDates.Instance;
            return dates != null && dates.IsFastFowardSimulating;
        }
    }
}
