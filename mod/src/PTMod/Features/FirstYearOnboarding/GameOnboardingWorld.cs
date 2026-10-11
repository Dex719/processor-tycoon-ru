using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.FirstYearOnboarding.Logic;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The game side of <see cref="IOnboardingWorld"/> (TSK-003): the blocking states and the date come from <c>ModTicks</c>, the conditions of the
    /// steps from <c>ModConditions</c>, the delivery of a mail from <c>ModMail</c>, the track from the player's company. Nothing here patches the game.
    /// A condition is parsed once (the parsed object is kept in <see cref="ConditionRef.Compiled"/>); the first failure of a condition is one
    /// warning, after that it is false without a sound. Main thread only; never throws.
    /// </summary>
    internal sealed class GameOnboardingWorld : IOnboardingWorld
    {
        private const string SitePrefix = "world/";

        public bool IsBlocked
        {
            get { return ModTicks.IsLoading || ModTicks.IsFastForwarding || !ModTicks.IsGameReady; }
        }

        public bool IsBatchRun
        {
            get { return ModTicks.IsBatchRun; }
        }

        public DateTime GameDate
        {
            get { return ModTicks.Today; }
        }

        /// <summary>
        /// <c>foundry</c>, <c>fabless</c> or <c>idm</c> by the flags of the player's company (<c>get_IsFoundry</c>, <c>get_IsFabless</c>,
        /// <c>get_IsIdmCompany</c>; the three exclude each other); null when the company cannot be read.
        /// </summary>
        public string DetectTrack()
        {
            try
            {
                ICompany player = ModCompanies.Resolve(CompanyKey.Player);
                return ReferenceEquals(player, null) ? null : TrackOf(player);
            }
            catch (Exception ex)
            {
                if (!(ex is NullReferenceException)) ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + "DetectTrack", ex);
                return null;
            }
        }

        public bool Evaluate(ConditionRef condition)
        {
            if (condition == null || condition.Node == null || condition.CompileFailed) return false;

            try
            {
                var compiled = condition.Compiled as ICondition;
                if (compiled == null)
                {
                    var errors = new List<string>();
                    compiled = ModConditions.Parse(condition.Node, errors, "$");
                    if (compiled == null)
                    {
                        condition.CompileFailed = true;
                        ModLog.Warn(OnboardingFeature.FeatureId, "a condition of a step cannot be parsed, it is false: "
                                                                  + (errors.Count > 0 ? string.Join("; ", errors.ToArray()) : "no details"));
                        return false;
                    }
                    condition.Compiled = compiled;
                }
                return ModConditions.Evaluate(compiled);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + "Evaluate", ex);
                return false;
            }
        }

        public bool IsMailDelivered(int mailId)
        {
            try
            {
                ModMailRecord record = ModMail.Get(mailId);
                return record != null && record.IsDelivered;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(OnboardingFeature.FeatureId, SitePrefix + "IsMailDelivered", ex);
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string TrackOf(ICompany company)
        {
            if (company.IsFoundry) return "foundry";
            if (company.IsFabless) return "fabless";
            if (company.IsIdmCompany) return "idm";
            return null;
        }
    }
}
