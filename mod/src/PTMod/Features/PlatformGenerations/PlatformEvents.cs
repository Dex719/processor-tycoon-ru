using System;
using PTMod.Core;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The events of the feature for the others (FR-11 item 2; design, Component 7): a generation was announced or launched, a company became an early
    /// adopter, a company received the penalty for changing a socket. Raised once, at the moment it happens, through <c>ModHooks.Raise</c>: a
    /// handler of a feature that is not Enabled is skipped, and a handler that throws is counted against its own feature and does not stop the
    /// others. Never raised while the new game's fast-forward runs or a save loads (the reconstruction at their end writes the chronology without
    /// events, FR-12), so a scenario never sees a launch that happened before the game began.
    /// <para>
    /// Ids: a generation is its <c>id</c> from <c>generations.json</c> ("gen-2000"), a company its key (<c>player</c>, <c>ai:3</c>), a socket its key
    /// (<c>pkg:PGA 1995</c>, <c>custom:5120</c>). The severity of a penalty is 0..1 (1 = the full <c>churnPenalty</c>).
    /// </para>
    /// </summary>
    public static class PlatformEvents
    {
        internal const string AnnouncedEvent = "PlatformEvents.GenerationAnnounced";
        internal const string LaunchedEvent = "PlatformEvents.GenerationLaunched";
        internal const string EarlyAdopterEvent = "PlatformEvents.EarlyAdopterGranted";
        internal const string ChurnEvent = "PlatformEvents.SocketChurnApplied";

        /// <summary>A generation was announced: its id.</summary>
        public static event Action<string> GenerationAnnounced;

        /// <summary>The first CPU of a generation was released: the id of the generation.</summary>
        public static event Action<string> GenerationLaunched;

        /// <summary>A company became an early adopter of a generation: its key, the id of the generation.</summary>
        public static event Action<string, string> EarlyAdopterGranted;

        /// <summary>A company was given the penalty for changing its socket: its key, the key of the new socket, the severity 0..1.</summary>
        public static event Action<string, string, float> SocketChurnApplied;

        internal static void RaiseGenerationAnnounced(string genId)
        {
            if (Quiet()) return;
            ModHooks.Raise(AnnouncedEvent, GenerationAnnounced, genId);
        }

        internal static void RaiseGenerationLaunched(string genId)
        {
            if (Quiet()) return;
            ModHooks.Raise(LaunchedEvent, GenerationLaunched, genId);
        }

        internal static void RaiseEarlyAdopterGranted(string companyKey, string genId)
        {
            if (Quiet()) return;
            ModHooks.Raise(EarlyAdopterEvent, EarlyAdopterGranted, companyKey, genId);
        }

        internal static void RaiseSocketChurnApplied(string companyKey, string socketKey, float severity)
        {
            if (Quiet()) return;
            ModLog.Info(PlatformGenerationsFeature.FeatureId, "events: SocketChurnApplied " + companyKey + " -> " + socketKey + ", severity "
                                                              + severity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            ModHooks.Raise(ChurnEvent, SocketChurnApplied, companyKey, socketKey, severity);
        }

        /// <summary>The fast-forward of a new game and the loading of a save raise nothing (the handlers that call these are not reached then either; this is the second lock).</summary>
        private static bool Quiet()
        {
            return ModTicks.IsFastForwarding || ModTicks.IsLoading;
        }
    }
}
