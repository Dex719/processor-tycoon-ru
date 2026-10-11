using System;
using System.Reflection;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The game side of <see cref="IRelatedFeatures"/> (TSK-024; FR-16): the calls of <c>ArchitectureApi.TransferCompanyState</c> of <c>cpu-architectures</c> and of
    /// <c>GpuApi.IsDivisionFounded</c> / <c>GpuApi.TransferVendorProducts</c> of <c>gpu-product-line</c>.
    /// <para>
    /// The two features are SOFT dependencies of <c>rival-acquisitions</c>: no <c>DependsOn</c>, and no compile-time reference either. Their public classes are looked
    /// up by name in the assembly of the mod and called only while <c>ModFeatures.IsActive</c> says the feature is on; a class or method that is not there makes the
    /// call a no-op that the deal notes (<see cref="IRelatedFeatures.ArchitecturesApiFound"/> / <see cref="IRelatedFeatures.GpuApiFound"/>). The names and signatures
    /// are the ones of the specs of those features: <c>ArchitectureApi.TransferCompanyState(ICompany from, ICompany to, bool mergeKeepBest)</c>,
    /// <c>GpuApi.IsDivisionFounded(ICompany)</c>, <c>GpuApi.TransferVendorProducts(ICompany from, ICompany to)</c>.
    /// </para>
    /// One object per deal (or per preview): it holds the target and the player. Main thread only.
    /// </summary>
    internal sealed class GameRelatedFeatures : IRelatedFeatures
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;

        internal const string ArchitectureApiType = "PTMod.Features.CpuArchitectures.ArchitectureApi";
        internal const string GpuApiType = "PTMod.Features.GpuProductLine.GpuApi";

        private static bool _resolved;
        private static MethodInfo _transferArchitectures;
        private static MethodInfo _isDivisionFounded;
        private static MethodInfo _transferVendorProducts;

        private readonly ICompany _target;
        private readonly ICompany _player;

        public GameRelatedFeatures(ICompany target, ICompany player)
        {
            _target = target;
            _player = player;
        }

        public bool IsActive(string featureId)
        {
            return ModFeatures.IsActive(featureId);
        }

        public bool ArchitecturesApiFound
        {
            get
            {
                Resolve();
                return _transferArchitectures != null;
            }
        }

        public bool GpuApiFound
        {
            get
            {
                Resolve();
                return _isDivisionFounded != null && _transferVendorProducts != null;
            }
        }

        public void TransferArchitectures()
        {
            Resolve();
            if (_transferArchitectures == null) throw new InvalidOperationException("ArchitectureApi.TransferCompanyState was not found");
            Call(_transferArchitectures, _target, _player, true);
        }

        public bool IsGpuDivisionFounded()
        {
            Resolve();
            if (_isDivisionFounded == null) throw new InvalidOperationException("GpuApi.IsDivisionFounded was not found");
            object result = Call(_isDivisionFounded, _target);
            return result is bool && (bool)result;
        }

        public bool TransferGpuProducts()
        {
            Resolve();
            if (_transferVendorProducts == null) throw new InvalidOperationException("GpuApi.TransferVendorProducts was not found");
            object result = Call(_transferVendorProducts, _target, _player);
            return result is bool && (bool)result;
        }

        /// <summary>One line for the log at the start: which of the three calls are in this build (the deal skips a call that is not).</summary>
        internal static string Describe()
        {
            Resolve();
            return "related features: ArchitectureApi.TransferCompanyState " + (_transferArchitectures != null ? "found" : "not found")
                   + ", GpuApi.IsDivisionFounded " + (_isDivisionFounded != null ? "found" : "not found")
                   + ", GpuApi.TransferVendorProducts " + (_transferVendorProducts != null ? "found" : "not found");
        }

        // ---- lookup ----

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            try
            {
                Assembly mod = typeof(GameRelatedFeatures).Assembly;
                Type iCompany = typeof(ICompany);

                Type architectures = mod.GetType(ArchitectureApiType, false);
                if (architectures != null)
                {
                    _transferArchitectures = architectures.GetMethod("TransferCompanyState",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { iCompany, iCompany, typeof(bool) }, null);
                }

                Type gpu = mod.GetType(GpuApiType, false);
                if (gpu != null)
                {
                    _isDivisionFounded = gpu.GetMethod("IsDivisionFounded", BindingFlags.Public | BindingFlags.Static, null, new[] { iCompany }, null);
                    _transferVendorProducts = gpu.GetMethod("TransferVendorProducts", BindingFlags.Public | BindingFlags.Static, null, new[] { iCompany, iCompany }, null);
                }
            }
            catch (Exception e)
            {
                ModLog.Warn(Feature, "related features: the lookup of the APIs failed (" + e.GetType().Name + ": " + e.Message + "); the state of other features is not transferred");
            }
        }

        /// <summary>Calls a static method; the exception of the callee, not the reflection wrapper, goes up (the guard counts the real one).</summary>
        private static object Call(MethodInfo method, params object[] args)
        {
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException e)
            {
                Exception inner = e.InnerException ?? e;
                throw new InvalidOperationException(method.DeclaringType.Name + "." + method.Name + " threw " + inner.GetType().Name + ": " + inner.Message, inner);
            }
        }
    }
}
