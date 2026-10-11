using System;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// What a deal asks of the two features that keep state about a company (FR-16, design.md step 9): <c>cpu-architectures</c> (the families a company
    /// has researched) and <c>gpu-product-line</c> (its GPU lineup and vendor). The game side is <c>GameRelatedFeatures</c>, the tests use a fake. The
    /// features are soft: <c>rival-acquisitions</c> has no <c>DependsOn</c> on them, it calls them only while they are active and only if their API is there.
    /// </summary>
    public interface IRelatedFeatures
    {
        /// <summary>The feature is switched on and healthy (<c>ModFeatures.IsActive</c>).</summary>
        bool IsActive(string featureId);

        /// <summary>The public API of <c>cpu-architectures</c> that the deal calls is in the build (<c>ArchitectureApi.TransferCompanyState</c>).</summary>
        bool ArchitecturesApiFound { get; }

        /// <summary>The public API of <c>gpu-product-line</c> that the deal calls is in the build (<c>GpuApi.IsDivisionFounded</c>, <c>GpuApi.TransferVendorProducts</c>).</summary>
        bool GpuApiFound { get; }

        /// <summary><c>ArchitectureApi.TransferCompanyState(target, player, true)</c>: the researched families are merged, the support is the larger of the two.</summary>
        void TransferArchitectures();

        /// <summary><c>GpuApi.IsDivisionFounded(target)</c>: the target has a GPU division (an AI company: it has a vendor bound to it).</summary>
        bool IsGpuDivisionFounded();

        /// <summary><c>GpuApi.TransferVendorProducts(target, player)</c>; false: the lineup was not transferred.</summary>
        bool TransferGpuProducts();
    }

    /// <summary>
    /// What step 9 will pass on, as far as it can be said before the deal (the preview shows it). Built by the same rules <see cref="RelatedStateTransfer"/>
    /// follows, from the same <see cref="IRelatedFeatures"/>, so the preview does not promise what the deal will not do. Never throws.
    /// </summary>
    public sealed class RelatedStatePlan
    {
        /// <summary>The researched families and the support of the target pass to the player.</summary>
        public bool Architectures;

        /// <summary>The GPU lineup of the target passes to the player (it has one, the deal takes the target out of the game while it is alive).</summary>
        public bool GpuLineup;

        /// <summary>The assets of a bankrupt company are bought and <c>gpu-product-line</c> is on: a GPU lineup, if the company had one, does not pass (its vendor left the market).</summary>
        public bool GpuNotPassed;

        public bool Any
        {
            get { return Architectures || GpuLineup || GpuNotPassed; }
        }

        public static RelatedStatePlan Of(DealType type, IRelatedFeatures features)
        {
            var plan = new RelatedStatePlan();
            if (features == null) return plan;

            try
            {
                plan.Architectures = features.IsActive(RelatedStateTransfer.ArchitecturesFeature) && features.ArchitecturesApiFound;

                if (features.IsActive(RelatedStateTransfer.GpuFeature))
                {
                    if (!DealExecution.TakesTargetOut(type)) plan.GpuNotPassed = true;
                    else plan.GpuLineup = features.GpuApiFound && features.IsGpuDivisionFounded();
                }
            }
            catch (Exception)
            {
                // A related feature that fails while the player reads the preview must not break the preview: it shows what it could tell.
                plan.GpuLineup = false;
            }
            return plan;
        }
    }

    /// <summary>
    /// Step 9 of a deal (FR-16; design.md "AcquisitionExecutor"): the state of the related features passes to the player BEFORE the target leaves the game
    /// (the pipeline puts this step before step 11), because the GPU feature takes a vendor off the market together with a bankrupt company.
    /// <list type="bullet">
    /// <item><b>Architectures</b> (all three purchases, FR-3…FR-5): <c>ArchitectureApi.TransferCompanyState(target, player, true)</c> while <c>cpu-architectures</c> is active.</item>
    /// <item><b>GPU lineup</b> (a company in default or healthy, FR-4/FR-5): while <c>gpu-product-line</c> is active and the target has a GPU division,
    /// <c>GpuApi.TransferVendorProducts(target, player)</c>; a result of <c>false</c> is a note in the record of the deal ("GPU lineup was not transferred").
    /// The assets of a bankrupt company (FR-3) pass no GPU lineup and do not call <c>GpuApi</c> at all: the record says so.</item>
    /// <item><b>Soft.</b> A feature that is off, or whose API is not in the build, is skipped and the deal goes on. An exception of a call marks the step failed
    /// (the deal is "partial"), the other call still runs, and the payment is not held back by it (only the steps 3 to 8 hold the payment back).</item>
    /// </list>
    /// </summary>
    public static class RelatedStateTransfer
    {
        public const string ArchitecturesFeature = "cpu-architectures";
        public const string GpuFeature = "gpu-product-line";

        /// <summary>The guard sites of the two calls (<c>AcquisitionExecutor.step9.*</c>).</summary>
        public const string SiteArchitectures = "AcquisitionExecutor.step9.architectures";
        public const string SiteGpu = "AcquisitionExecutor.step9.gpu";

        /// <summary>
        /// Runs step 9. <paramref name="guard"/> runs one call: <c>(site, body) =&gt; true</c> when the body ran to the end, false when it threw (the game
        /// passes <c>ModHooks.Guard</c>). What happened goes into <paramref name="log"/>: notes for the record, a failure for a call that threw.
        /// </summary>
        public static void Run(IRelatedFeatures features, DealType type, DealLog log, Func<string, Action, bool> guard)
        {
            if (features == null || log == null || guard == null) return;

            TransferArchitectures(features, log, guard);
            TransferGpu(features, type, log, guard);
        }

        private static void TransferArchitectures(IRelatedFeatures features, DealLog log, Func<string, Action, bool> guard)
        {
            if (!features.IsActive(ArchitecturesFeature)) return;

            if (!features.ArchitecturesApiFound)
            {
                log.Note("architectures: cpu-architectures is on but its API was not found, the state is not transferred");
                return;
            }

            if (guard(SiteArchitectures, features.TransferArchitectures)) log.Note("architectures: researched families merged into the player's, support kept at the larger of the two");
            else log.Fail("architectures: the transfer failed, see the log");
        }

        private static void TransferGpu(IRelatedFeatures features, DealType type, DealLog log, Func<string, Action, bool> guard)
        {
            if (!features.IsActive(GpuFeature)) return;

            if (!DealExecution.TakesTargetOut(type))
            {
                // AC-16.4: the vendor left the market when the company went bankrupt; the lineup does not come with its assets, and GpuApi is not asked.
                log.Note("gpu: the GPU lineup, if the company had one, is not transferred with the assets of a bankrupt company (its vendor left the market)");
                return;
            }

            if (!features.GpuApiFound)
            {
                log.Note("gpu: gpu-product-line is on but its API was not found, the lineup is not transferred");
                return;
            }

            bool founded = false;
            bool passed = false;
            bool ran = guard(SiteGpu, () =>
            {
                founded = features.IsGpuDivisionFounded();
                if (founded) passed = features.TransferGpuProducts();
            });

            if (!ran) log.Fail("gpu: the transfer failed, see the log");
            else if (!founded) log.Note("gpu: the target has no GPU division, nothing to transfer");
            else if (passed) log.Note("gpu: the GPU lineup of the target passed to the player");
            else log.Note("GPU lineup was not transferred (GpuApi.TransferVendorProducts returned false)");
        }
    }
}
