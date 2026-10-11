using System;
using PTMod.Core;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The implementation of <see cref="IRivalPersonalitiesApi"/> (design.md, Component 12; FR-13, TSK-020): turns the game's <c>ICompany</c> into the
    /// <c>UniqueID</c> of an AI company and hands the call to <see cref="RivalApiCore"/>, which holds the decisions. It also carries them out in the game
    /// (<see cref="IRivalApiEffects"/>): the price book, the planner, the characters. Registered once in <c>Register</c> with
    /// <see cref="RivalPersonalitiesApi.Register"/>; the facade <c>RivalPersonalities.Api</c> calls it only while the feature is active and answers like
    /// the null object otherwise, and it catches an exception of any member, so nothing here needs a guard of its own.
    /// <para>
    /// The name is not <c>RivalPersonalitiesApi</c>: the static class of the <c>Api</c> folder has it.
    /// </para>
    /// </summary>
    internal sealed class RivalApiService : IRivalPersonalitiesApi, IRivalApiEffects
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private readonly RivalPersonalitiesFeature _feature;
        private readonly RivalApiCore _core;

        public RivalApiService(RivalPersonalitiesFeature feature)
        {
            _feature = feature;
            _core = new RivalApiCore(feature.Book, feature.Attitudes, this, line => ModLog.Warn(Feature, line));
            feature.Executor.Started += OnReactionStarted;
        }

        /// <summary>The decisions (for the tests of the wiring and the diagnostics).</summary>
        public RivalApiCore Core
        {
            get { return _core; }
        }

        /// <summary>
        /// The <c>UniqueID</c> of an AI company; null for the player, for null, for a destroyed object and for anything that is not an AI company (the API
        /// treats those as unknown).
        /// </summary>
        internal static int? UidOf(ICompany company)
        {
            if (ReferenceEquals(company, null)) return null;

            var rival = company as AICompany;
            if (ReferenceEquals(rival, null) || rival == null) return null;
            return rival.UniqueID >= 0 ? rival.UniqueID : (int?)null;
        }

        // ---- IRivalPersonalitiesApi ----

        public bool IsEnabled
        {
            get { return true; }
        }

        public float? GetAttitude(ICompany company)
        {
            return _core.GetAttitude(UidOf(company));
        }

        public AttitudeLevel? GetAttitudeLevel(ICompany company)
        {
            return _core.GetAttitudeLevel(UidOf(company));
        }

        public string GetArchetypeId(ICompany company)
        {
            return _core.GetArchetypeId(UidOf(company));
        }

        public void ChangeAttitude(ICompany company, float delta, string reason)
        {
            _core.ChangeAttitude(UidOf(company), delta, reason);
        }

        public void SetDormant(ICompany company, bool dormant)
        {
            if (_core.SetDormant(UidOf(company), dormant))
            {
                ModLog.Info(Feature, "api: " + AttitudeNotifier.NameOf(UidOf(company).Value) + (dormant ? " is put aside (dormant)" : " is back in the game"));
            }
        }

        public event Action<int, string> ReactionStarted;

        public int ApplyPriceCut(string reactionId, ICompany rival, Segment segment, float depth, float floorMargin)
        {
            return _core.ApplyPriceCut(reactionId, UidOf(rival), segment, depth, floorMargin);
        }

        public void RestorePriceCut(string reactionId)
        {
            _core.RestorePriceCut(reactionId);
        }

        /// <summary>
        /// A reaction of a rival started (the hostile ones come from the executor, the friendly offer from <see cref="PartnershipOffer"/>): raises the
        /// event of the API. The facade forwards it through <c>ModHooks.Raise</c>, which isolates the handlers of the other features.
        /// </summary>
        internal void OnReactionStarted(int uniqueId, string reactionType)
        {
            Action<int, string> handlers = ReactionStarted;
            if (handlers == null) return;

            try
            {
                handlers(uniqueId, reactionType);
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, "Api.ReactionStarted", e);
            }
        }

        // ---- IRivalApiEffects ----

        int IRivalApiEffects.ApplyCut(string reactionId, int uniqueId, Segment segment, float depth, float floorMargin)
        {
            AICompany rival = _feature.Registry.FindRival(uniqueId);
            return rival == null ? 0 : _feature.Prices.ApplyCut(reactionId, rival, segment, depth, floorMargin);
        }

        int IRivalApiEffects.RestoreCut(string reactionId)
        {
            return _feature.Prices.Restore(reactionId);
        }

        int IRivalApiEffects.RestoreRivalCuts(int uniqueId)
        {
            return _feature.Prices.RestoreRival(uniqueId);
        }

        int IRivalApiEffects.CancelReactions(int uniqueId, string reason)
        {
            return _feature.Planner.CancelFor(uniqueId, reason);
        }

        void IRivalApiEffects.RevertCharacter(int uniqueId)
        {
            _feature.Registry.Revert(uniqueId);
        }

        void IRivalApiEffects.ApplyCharacter(int uniqueId)
        {
            AICompany rival = _feature.Registry.FindRival(uniqueId);
            if (rival == null) return;
            _feature.Registry.EnsureAssigned(rival);
            _feature.Registry.Apply(rival, "no longer dormant");
        }
    }
}
