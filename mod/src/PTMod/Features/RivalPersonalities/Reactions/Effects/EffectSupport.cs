using System;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Features.EventSystem;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// What the five effect handlers of the feature share (design.md, Component 9). The handlers keep no state of their own (the numbers live in the
    /// saved <see cref="AppliedEffect"/>, D1): the rival, the segment and the id of the reaction are read from what <c>Resolve</c> put into the
    /// effect's <c>textArgs</c> (and <c>Describe</c> hands back to it, see <see cref="EffectArgs"/>), and only when that is gone from the arguments of
    /// the live event. The reaction ids and the arguments are the executor's: <see cref="ReactionExecutor"/>.
    /// </summary>
    internal static class EffectSupport
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        public static RivalPersonalitiesFeature Instance
        {
            get { return RivalPersonalitiesFeature.Instance; }
        }

        /// <summary>The numbers of the content (the defaults of the design when the feature has not read them yet).</summary>
        public static RivalConfig Config
        {
            get
            {
                RivalPersonalitiesFeature feature = Instance;
                return feature != null ? feature.Catalog.Config : RivalConfig.Defaults();
            }
        }

        /// <summary>The archetype of the rival, or null when the feature or the rival is not known.</summary>
        public static Archetype ArchetypeOf(int uniqueId)
        {
            RivalPersonalitiesFeature feature = Instance;
            return feature != null && uniqueId >= 0 ? feature.Book.ArchetypeOf(uniqueId) : null;
        }

        /// <summary>
        /// The arguments of the live event for <c>Resolve</c>: <c>[rival name, detail, reaction id, rival key]</c> as the executor fired them. A gap
        /// is filled as far as it can be: the rival from the target of the event (when it is a rival), its name from the game, the reaction id from the
        /// number of the instance (an event fired by hand, not by the executor, has no id of its own).
        /// </summary>
        public static EffectArgs ResolveArgs(EventContext ctx)
        {
            EffectArgs args = EffectArgs.FromFire(ctx != null && ctx.Instance != null && ctx.Instance.Data != null ? ctx.Instance.Data.args : null);
            if (string.IsNullOrEmpty(args.RivalKey) && ctx != null && CompanyKey.IsAi(ctx.CompanyKey)) args.RivalKey = ctx.CompanyKey;

            int uniqueId = args.UniqueId;
            if (string.IsNullOrEmpty(args.RivalName) && uniqueId >= 0) args.RivalName = AttitudeNotifier.NameOf(uniqueId);
            if (string.IsNullOrEmpty(args.ReactionId))
            {
                int instance = ctx != null && ctx.Instance != null && ctx.Instance.Data != null ? ctx.Instance.Data.id : 0;
                args.ReactionId = "rival:event-" + instance.ToString(CultureInfo.InvariantCulture);
            }
            return args;
        }

        /// <summary>The arguments of an effect that was resolved already: what <c>Resolve</c> saved, otherwise the live event's.</summary>
        public static EffectArgs SavedArgs(AppliedEffect effect, EventContext ctx)
        {
            EffectArgs saved = EffectArgs.FromSaved(effect != null ? effect.textArgs : null);
            return saved.IsComplete ? saved : ResolveArgs(ctx);
        }

        /// <summary>The rival of a reaction: its company in the game, or null (gone, not started).</summary>
        public static AICompany RivalOf(EffectArgs args)
        {
            if (args == null || args.UniqueId < 0) return null;
            var rival = ModCompanies.Resolve(args.RivalKey) as AICompany;
            return ReferenceEquals(rival, null) || rival == null ? null : rival;
        }

        /// <summary>The segment of the saved index; <see cref="Segment.Any"/> for none.</summary>
        public static Segment SegmentOf(int index)
        {
            return index >= 0 && index <= 3 ? (Segment)index : Segment.Any;
        }

        /// <summary>The day the effect ends: the saved <c>endDay</c> of a timed effect, otherwise today plus its duration.</summary>
        public static DateTime EndOf(AppliedEffect effect, EventContext ctx)
        {
            if (effect != null && effect.endDay > 0) return RivalDates.FromDayNumber(effect.endDay);
            int today = ctx != null && ctx.Today > 0 ? ctx.Today : RivalDates.ToDayNumber(ModTicks.Today);
            int days = effect != null && effect.durationDays > 0 ? effect.durationDays : 0;
            return RivalDates.FromDayNumber(today + days);
        }

        /// <summary>The feature is Enabled (<c>ModFeatures.IsActive</c>). A handler that is asked to <c>Remove</c> an effect because its pack froze with the feature leaves the record alone.</summary>
        public static bool FeatureIsActive
        {
            get { return ModFeatures.IsActive(Feature); }
        }

        /// <summary>Marks the effect as not applied (nothing to apply it to) and says why in the log.</summary>
        public static void Skip(AppliedEffect effect, string handler, string why)
        {
            if (effect != null) effect.state = AppliedEffectStates.Skipped;
            ModLog.Warn(Feature, handler + ": not applied - " + why);
        }
    }
}
