using System;
using ProcessorTycoon.TimeSystem;

namespace PTMod.Core
{
    /// <summary>
    /// P21: the batch run of a month (FR-4 items 8-9). <c>DateController.NextMonth</c> ("next month" button) calls <c>TriggerTicks</c> up to
    /// 31 times in one frame; the prefix enters the run (<see cref="ModTicks.IsBatchRun"/> becomes true), the finalizer, which runs also when
    /// the method throws, leaves it and, after the outer call, raises <see cref="ModTicks.BatchEnded"/> once. The counting is
    /// <c>BatchCounter</c> (pure logic, unit-tested).
    /// <list type="bullet">
    /// <item>The method is reserved for the core (<c>ReservedTargets</c>: service <c>ModTicks</c>): a feature's patch on it is rejected with
    /// the hint to use <c>ModTicks.IsBatchRun</c> / <c>ModTicks.BatchEnded</c> (FR-2 item 6).</item>
    /// <item>The prefix goes first among the prefixes and the finalizer last among the finalizers, so other patches of the method see the
    /// flag for their whole run.</item>
    /// <item>The finalizer leaves only when its prefix entered (<c>__state</c>): a prefix skipped by the breaker cannot unbalance the depth,
    /// and a finalizer is never skipped once the run was entered, so <see cref="ModTicks.IsBatchRun"/> cannot stick at true.</item>
    /// </list>
    /// Found by <c>ModHooks</c>' scan of the core at the Patch stage (Harmony id <c>ptmod.mod-framework</c>). Without the patches (no
    /// 0Harmony.dll, another game version) <see cref="ModTicks.IsBatchRun"/> stays false and the mail is delivered by the day as usual.
    /// </summary>
    internal static class BatchRunPatches
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string PrefixSite = "P21/prefix";
        private const string FinalizerSite = "P21/finalizer";

        [ModPatch(typeof(DateController), "NextMonth", PatchKind.Prefix, Priority = 800)]
        private static void NextMonthPrefix(out bool __state)
        {
            __state = false;
            if (!ModHooks.CanRun(CoreId, PrefixSite)) return;
            try
            {
                __state = true;   // EnterBatch counts first and logs after: whatever happens, the finalizer leaves what was entered
                ModTicks.EnterBatch();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, PrefixSite, ex);
            }
        }

        [ModPatch(typeof(DateController), "NextMonth", PatchKind.Finalizer, Priority = 0)]
        private static void NextMonthFinalizer(bool __state)
        {
            if (!__state) return;
            try
            {
                ModTicks.LeaveBatch();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, FinalizerSite, ex);
            }
        }
    }
}
