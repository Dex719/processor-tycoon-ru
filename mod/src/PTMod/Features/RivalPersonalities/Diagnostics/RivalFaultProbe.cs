using System;
using System.Diagnostics;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The artificial failure of the debug build (NFR-2, TSK-024): with the flag <see cref="Flag"/> in the <c>debug</c> section of <c>ptmod_settings.json</c> every patch
    /// body of the feature throws at its start, inside its own <c>try</c>. It is how the isolation of failures is tried in a running game: the exception must be
    /// caught at the boundary of the patch (<c>ModHooks.ReportFault</c>: one line in the log with the name of the site, a count against the feature), the game must go
    /// on with its own values, and the fifth failure of the session must make the core switch the feature to <c>Faulted</c> with a notice (<c>hooks.faultThreshold</c>).
    /// The feature keeps no counter of failures of its own. In a release build the calls vanish: <see cref="Trip"/> is <c>[Conditional("PTMOD_DEBUG")]</c>, so the
    /// call and its arguments are not even compiled, and <c>ModSettings.Debug</c> is false there anyway.
    /// </summary>
    internal static class RivalFaultProbe
    {
        /// <summary>The flag of the <c>debug</c> section of <c>ptmod_settings.json</c> (read by <c>ModSettings.Debug</c>; false in a release build).</summary>
        public const string Flag = "rival-personalities.debug.throwInPatch";

        /// <summary>Throws an <see cref="InvalidOperationException"/> when the flag is set. Put it first in the <c>try</c> of a patch body, with the name of the site.</summary>
        [Conditional("PTMOD_DEBUG")]
        public static void Trip(string site)
        {
            if (ModSettings.Debug(Flag)) throw new InvalidOperationException("debug flag " + Flag + " is set: failure injected at " + site);
        }
    }
}
