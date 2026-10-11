using System;
using PTMod.Core;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The one object that callers hold as <see cref="RivalPersonalities.Api"/>: a stable front of <see cref="IRivalPersonalitiesApi"/> that
    /// hands every call to the implementation the feature registered, but only while the feature is active (<see cref="ModFeatures.IsActive"/>);
    /// otherwise it answers like <see cref="NullRivalPersonalitiesApi"/> (FR-13 item 7). Because the front never changes, a subscription to
    /// <see cref="ReactionStarted"/> made while the feature is off still works after it is switched on, and a switch needs no cooperation from
    /// the implementation.
    /// <list type="bullet">
    /// <item><b>Boundary.</b> Every call into the implementation runs in try/catch: a failure is counted against <c>rival-personalities</c>
    /// (<see cref="ModHooks.ReportFault"/>) and the caller gets the neutral answer (null, 0, nothing). Nothing is allocated on the normal path.</item>
    /// <item><b>Event.</b> The host subscribes to the event of the implementation and raises its own through <see cref="ModHooks.Raise{T1,T2}"/>,
    /// so handlers of features that are not active are skipped and a failing handler does not stop the others.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal sealed class RivalPersonalitiesApiHost : IRivalPersonalitiesApi
    {
        private const string ReactionStartedEvent = "RivalPersonalities.ReactionStarted";

        private readonly Action<int, string> _forward;
        private IRivalPersonalitiesApi _implementation;

        internal RivalPersonalitiesApiHost()
        {
            _forward = OnImplementationReactionStarted;
        }

        /// <summary>True when the feature has registered its implementation (whether or not the feature is active now).</summary>
        internal bool HasImplementation
        {
            get { return _implementation != null; }
        }

        /// <summary>The registered implementation, or null. For the feature's own code and tests; callers use the host itself.</summary>
        internal IRivalPersonalitiesApi Implementation
        {
            get { return _implementation; }
        }

        // ---- registration (the feature) ----

        /// <summary>
        /// Registers the implementation (replacing an earlier one) and starts forwarding its <see cref="ReactionStarted"/>. A null argument and
        /// the host itself are ignored. Never throws.
        /// </summary>
        internal void Attach(IRivalPersonalitiesApi implementation)
        {
            if (implementation == null || ReferenceEquals(implementation, this) || ReferenceEquals(implementation, NullRivalPersonalitiesApi.Instance)) return;
            if (ReferenceEquals(implementation, _implementation)) return;

            Detach(_implementation);
            _implementation = implementation;
            try
            {
                implementation.ReactionStarted += _forward;
            }
            catch (Exception e)
            {
                Fault("Attach", e);
            }
        }

        /// <summary>
        /// Takes the implementation back if it is the registered one (another object is ignored), and stops forwarding its event. Never throws.
        /// </summary>
        internal void Detach(IRivalPersonalitiesApi implementation)
        {
            if (implementation == null || !ReferenceEquals(implementation, _implementation)) return;

            _implementation = null;
            try
            {
                implementation.ReactionStarted -= _forward;
            }
            catch (Exception e)
            {
                Fault("Detach", e);
            }
        }

        // ---- IRivalPersonalitiesApi ----

        public event Action<int, string> ReactionStarted;

        public bool IsEnabled
        {
            get
            {
                IRivalPersonalitiesApi api = Active();
                if (api == null) return false;
                try
                {
                    return api.IsEnabled;
                }
                catch (Exception e)
                {
                    Fault("IsEnabled", e);
                    return false;
                }
            }
        }

        public float? GetAttitude(ICompany company)
        {
            IRivalPersonalitiesApi api = Active();
            if (api == null) return null;
            try
            {
                return api.GetAttitude(company);
            }
            catch (Exception e)
            {
                Fault("GetAttitude", e);
                return null;
            }
        }

        public AttitudeLevel? GetAttitudeLevel(ICompany company)
        {
            IRivalPersonalitiesApi api = Active();
            if (api == null) return null;
            try
            {
                return api.GetAttitudeLevel(company);
            }
            catch (Exception e)
            {
                Fault("GetAttitudeLevel", e);
                return null;
            }
        }

        public string GetArchetypeId(ICompany company)
        {
            IRivalPersonalitiesApi api = Active();
            if (api == null) return null;
            try
            {
                return api.GetArchetypeId(company);
            }
            catch (Exception e)
            {
                Fault("GetArchetypeId", e);
                return null;
            }
        }

        public void ChangeAttitude(ICompany company, float delta, string reason)
        {
            IRivalPersonalitiesApi api = Active();
            if (api == null) return;
            try
            {
                api.ChangeAttitude(company, delta, reason);
            }
            catch (Exception e)
            {
                Fault("ChangeAttitude", e);
            }
        }

        public void SetDormant(ICompany company, bool dormant)
        {
            IRivalPersonalitiesApi api = Active();
            if (api == null) return;
            try
            {
                api.SetDormant(company, dormant);
            }
            catch (Exception e)
            {
                Fault("SetDormant", e);
            }
        }

        public int ApplyPriceCut(string reactionId, ICompany rival, Segment segment, float depth, float floorMargin)
        {
            IRivalPersonalitiesApi api = Active();
            if (api == null) return 0;
            try
            {
                return api.ApplyPriceCut(reactionId, rival, segment, depth, floorMargin);
            }
            catch (Exception e)
            {
                Fault("ApplyPriceCut", e);
                return 0;
            }
        }

        public void RestorePriceCut(string reactionId)
        {
            IRivalPersonalitiesApi api = Active();
            if (api == null) return;
            try
            {
                api.RestorePriceCut(reactionId);
            }
            catch (Exception e)
            {
                Fault("RestorePriceCut", e);
            }
        }

        // ---- internals ----

        /// <summary>The implementation when it is registered and the feature is active; otherwise null (the null-object answers).</summary>
        private IRivalPersonalitiesApi Active()
        {
            IRivalPersonalitiesApi api = _implementation;
            return api != null && ModFeatures.IsActive(RivalPersonalitiesApi.FeatureId) ? api : null;
        }

        private void OnImplementationReactionStarted(int uniqueId, string reactionType)
        {
            try
            {
                ModHooks.Raise(ReactionStartedEvent, ReactionStarted, uniqueId, reactionType);
            }
            catch (Exception e)
            {
                Fault("ReactionStarted", e);
            }
        }

        private static void Fault(string member, Exception e)
        {
            ModHooks.ReportFault(RivalPersonalitiesApi.FeatureId, "Api." + member, e);
        }
    }
}
