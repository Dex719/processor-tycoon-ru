using System;
using PTMod.Core;
using PTMod.Core.Logic.Companies;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// What the parts of the feature that touch the game share (the scheduler, the dispatcher, the catch-up, later the services): the view of the world,
    /// the share cache, the role resolver, the state that the predicates ask, and the evaluator of conditions. One object per session, made by
    /// <see cref="HistoricalEventsFeature.Register"/>. It owns no rule of its own: every decision is made by a type of <c>Logic/</c>; this is the glue to
    /// <c>ModConditions</c>, <c>ModRandom</c> and <c>ModSettings</c>.
    /// </summary>
    internal sealed class HistRuntime
    {
        private readonly HistoricalEventsFeature _feature;
        private bool _forceLeader;

        internal HistRuntime(HistoricalEventsFeature feature)
        {
            _feature = feature;
            World = GameWorldView.Instance;
            Shares = new MarketShareCache();
            Resolver = new RoleResolver();
            State = new HistState { Warn = message => ModLog.Warn(HistIds.Feature, message) };
            Evaluator = new ConditionEvaluator(RunCondition, message => ModLog.Warn(HistIds.Feature, message));
        }

        internal GameWorldView World { get; private set; }

        internal MarketShareCache Shares { get; private set; }

        internal RoleResolver Resolver { get; private set; }

        /// <summary>What the predicates <c>hist.*</c> ask the services (the cases, the export rules) and the debug flag.</summary>
        internal HistState State { get; private set; }

        internal ConditionEvaluator Evaluator { get; private set; }

        /// <summary>The numbers of <c>params.json</c> (the defaults until the calendar is loaded).</summary>
        internal HistContentParams Params
        {
            get { return _feature.Params; }
        }

        /// <summary>
        /// <c>debugForceLeader</c> of the chronicle page: the player is the leader and the first challenger of every entry. Only works while the
        /// <c>debug</c> setting is on; switching <c>debug</c> off puts it down.
        /// </summary>
        internal bool ForceLeader
        {
            get { return _forceLeader && HistSettings.Debug; }
            set
            {
                _forceLeader = value && HistSettings.Debug;
                State.ForceLeader = _forceLeader;
            }
        }

        /// <summary>A setting changed: the debug flag does not outlive the <c>debug</c> setting.</summary>
        internal void OnSettingChanged(string key)
        {
            if (key == HistParamSpecs.Debug && !HistSettings.Debug) ForceLeader = false;
        }

        /// <summary>The roles of the entry in the game as it is now. Brings the share cache up to date first (O(1) within a month).</summary>
        internal RoleResult ResolveRoles(CalendarEntry entry)
        {
            Shares.EnsureFresh(World, Params.ShareCacheMonths);
            var options = new RoleOptions { Mode = HistSettings.Mode, ForceLeader = ForceLeader };
            return Resolver.Resolve(entry, World, Shares, options, ModRandom.Stream(HistIds.Feature));
        }

        /// <summary>The conditions of the entry (a conjunction) hold with these roles.</summary>
        internal bool ConditionsHold(CalendarEntry entry, RoleResult roles)
        {
            Shares.EnsureFresh(World, Params.ShareCacheMonths);
            return Evaluator.EvaluateAll(entry.Conditions, roles, World, Shares, State);
        }

        /// <summary>The frame the predicates use outside an entry (a condition of the event engine's own that names a <c>hist.*</c> predicate).</summary>
        internal HistScopeFrame AmbientFrame()
        {
            return new HistScopeFrame(null, World, Shares, State);
        }

        /// <summary>Runs a condition for the company with this key. A company that is not in the game is not "the player": the answer is false.</summary>
        private static bool RunCondition(PTMod.Core.ICondition condition, string subjectKey)
        {
            if (CompanyKey.IsPlayer(subjectKey)) return ModConditions.Evaluate(condition, null);

            ProcessorTycoon.CompanySystem.ICompany company = ModCompanies.Resolve(subjectKey);
            return company != null && ModConditions.Evaluate(condition, company);
        }
    }
}
