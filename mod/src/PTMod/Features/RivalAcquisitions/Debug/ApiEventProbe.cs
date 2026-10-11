#if PTMOD_DEBUG
using System;
using PTMod.Core;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The test subscriber of the events of <c>RivalAcquisitionsApi</c> (TSK-017 acceptance: "the events of the API are raised; the error of one subscriber does not stop the
    /// others"). Debug build only (the whole file is under <c>PTMOD_DEBUG</c>, so a release build has no trace of it) and only when a flag of the <c>debug</c> section of
    /// <c>ptmod_settings.json</c> asks for it:
    /// <list type="bullet">
    /// <item><c>debug.acqApiProbe</c>: every event is written to the log of the feature (<c>debug: api DealCompleted ...</c>).</item>
    /// <item><c>debug.acqApiProbeThrow</c>: a first subscriber throws on every event; the log line of the second one still comes, which shows that a subscriber's error stops
    /// nobody. Each such error is counted against the feature like any other, so switch the flag on for a short test only.</item>
    /// </list>
    /// Nothing here changes the game or the books of the feature.
    /// </summary>
    internal static class ApiEventProbe
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;
        private const string FlagLog = "debug.acqApiProbe";
        private const string FlagThrow = "debug.acqApiProbeThrow";

        private static bool attached;

        /// <summary>Subscribes the two probes once (the throwing one first, so the logging one proves the others still run).</summary>
        internal static void Attach()
        {
            if (attached) return;
            attached = true;

            RivalAcquisitionsApi.DealCompleted += deal => Throw("DealCompleted");
            RivalAcquisitionsApi.StakeChanged += (id, share) => Throw("StakeChanged");
            RivalAcquisitionsApi.RivalDefaulted += id => Throw("RivalDefaulted");
            RivalAcquisitionsApi.RivalBankrupt += id => Throw("RivalBankrupt");

            RivalAcquisitionsApi.DealCompleted += deal => Log("DealCompleted #" + deal.id + " " + deal.type + " " + deal.targetName + " price " + deal.price.ToString("F0"));
            RivalAcquisitionsApi.StakeChanged += (id, share) => Log("StakeChanged ai:" + id + " share " + share.ToString("0.###"));
            RivalAcquisitionsApi.RivalDefaulted += id => Log("RivalDefaulted ai:" + id);
            RivalAcquisitionsApi.RivalBankrupt += id => Log("RivalBankrupt ai:" + id);
        }

        private static void Throw(string name)
        {
            if (ModSettings.Debug(FlagThrow)) throw new InvalidOperationException("debug: the probe subscriber of " + name + " fails on purpose");
        }

        private static void Log(string text)
        {
            if (ModSettings.Debug(FlagLog)) ModLog.Info(Feature, "debug: api " + text);
        }
    }
}
#endif
