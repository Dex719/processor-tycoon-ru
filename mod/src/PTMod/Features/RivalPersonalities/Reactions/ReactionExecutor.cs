using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using PTMod.Features.EventSystem;
using GameFormatter = ProcessorTycoon.StringFormatter;
using GameFinance = ProcessorTycoon.CompanySystem.CompanyFinance;
using GamePlayer = ProcessorTycoon.Player;
using GameInflation = ProcessorTycoon.Bank.Inflation.InflationHandler;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The executor of the reactions in the game (design.md, Component 9; FR-7 items 2-4, 7): the game side of <see cref="ReactionExecutorCore"/>. It
    /// fires the event of a reaction through <c>EventBus.Fire</c> (the target is <c>ai:&lt;UniqueID&gt;</c> for a price cut, a counter-launch and an
    /// OEM push, <c>player</c> for the poaching and the smear; the arguments are <c>[rival name, segment or price, reaction id, rival key]</c>), shows
    /// the player when a reaction starts and ends (<c>ModNotify</c>), reveals the character of the rival on its first reaction and takes the state
    /// of a reaction (the target prices, the boosts) back when it is dropped. The five effect handlers
    /// (<see cref="RivalPriceCutHandler"/> and the others) report to it. Main thread only; the callers guard the calls.
    /// </summary>
    internal sealed class ReactionExecutor : IReactionRuntime
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        private readonly RivalPersonalitiesFeature _feature;
        private readonly ReactionExecutorCore _core;

        public ReactionExecutor(RivalPersonalitiesFeature feature, ReactionBook reactions)
        {
            _feature = feature;
            _core = new ReactionExecutorCore(reactions, this, () => ModTicks.Today, line => ModLog.Info(Feature, "reactions: " + line));
        }

        /// <summary>The rules (shared with the planner and the tests).</summary>
        public ReactionExecutorCore Core
        {
            get { return _core; }
        }

        /// <summary>A reaction of a rival started: <c>(UniqueID, name of the reaction)</c> (the source of the API's <c>ReactionStarted</c>).</summary>
        public event Action<int, string> Started
        {
            add { _core.Started += value; }
            remove { _core.Started -= value; }
        }

        // ---- what the handlers report ----

        public bool OnStarted(string reactionId, int uniqueId, ReactionType type, Segment segment, DateTime end, params string[] details)
        {
            return _core.OnStarted(reactionId, uniqueId, type, segment, end, details);
        }

        public bool Restore(string reactionId, int uniqueId, ReactionType type, Segment segment, DateTime end)
        {
            return _core.Restore(reactionId, uniqueId, type, segment, end);
        }

        public bool OnEnded(string reactionId)
        {
            return _core.OnEnded(reactionId);
        }

        public int ExpireOverdue(DateTime today)
        {
            return _core.ExpireOverdue(today);
        }

        /// <summary><c>EventBus.OnChoiceResolved</c>: only the choices of <c>rival.poach</c> are written to the history.</summary>
        public void OnChoiceResolved(EventInstance instance, string choiceId, bool auto)
        {
            if (instance == null || instance.Data == null || !string.Equals(instance.Data.eventId, ReactionTags.EventPoach, StringComparison.Ordinal)) return;
            EffectArgs args = EffectArgs.FromFire(instance.Data.args);
            _core.OnChoiceResolved(args.ReactionId, choiceId, auto);
        }

        // ---- IReactionRuntime ----

        public FireResult Fire(string eventId, string companyKey, string[] args)
        {
            try
            {
                EventFireResult result = EventBus.Fire(eventId, new EventFireContext { CompanyKey = companyKey, Args = args, IgnoreConditions = true });
                switch (result)
                {
                    case EventFireResult.Fired: return FireResult.Fired;
                    case EventFireResult.Scheduled: return FireResult.Scheduled;
                    case EventFireResult.Skipped: return FireResult.Skipped;
                    case EventFireResult.UnknownEvent: return FireResult.UnknownEvent;
                    case EventFireResult.Disabled: return FireResult.Disabled;
                    default: return FireResult.Failed;
                }
            }
            catch (Exception e)
            {
                ModHooks.ReportFault(Feature, "ReactionExecutor.Fire", e);
                return FireResult.Failed;
            }
        }

        public string NameOf(int uniqueId)
        {
            return AttitudeNotifier.NameOf(uniqueId);
        }

        public string DetailFor(ReactionType type, int uniqueId, int segment)
        {
            if (type != ReactionType.Poach) return segment.ToString(CultureInfo.InvariantCulture);
            return Money(CounterOfferCost());
        }

        public int DurationDays(ReactionType type)
        {
            string eventId = ReactionTags.EventIdOf(type);
            EventDefinition definition = eventId != null ? EventRegistry.Get(eventId) : null;
            if (definition == null) return FallbackDays(type);

            if (type == ReactionType.Poach) return definition.timerDays > 0 ? definition.timerDays : FallbackDays(type);

            int longest = 0;
            if (definition.effects != null)
            {
                foreach (EventEffect effect in definition.effects)
                {
                    if (effect != null && effect.durationDays > longest) longest = effect.durationDays;
                }
            }
            return longest > 0 ? longest : FallbackDays(type);
        }

        public void Reveal(int uniqueId)
        {
            _feature.Registry.Reveal(uniqueId);
        }

        public void NotifyStarted(ActiveReaction reaction, string[] details)
        {
            if (reaction == null) return;
            ReactionType type;
            if (!ReactionKinds.TryParseReaction(reaction.type, out type)) return;

            string name = AttitudeNotifier.NameOf(reaction.uid);
            string segment = SegmentName(reaction.segment);
            string first = details != null && details.Length > 0 ? details[0] : string.Empty;
            string second = details != null && details.Length > 1 ? details[1] : string.Empty;

            switch (type)
            {
                case ReactionType.PriceCut:
                    ModNotify.ShowKey(Feature, "rival.notify.started.priceCut", name, segment, first);
                    break;
                case ReactionType.CounterLaunch:
                    ModNotify.ShowKey(Feature, "rival.notify.started.counterLaunch", name, segment);
                    break;
                case ReactionType.OemPush:
                    ModNotify.ShowKey(Feature, "rival.notify.started.oemPush", name);
                    break;
                case ReactionType.Poach:
                    if (string.Equals(first, ReactionTags.ChoiceCounterOffer, StringComparison.Ordinal)) ModNotify.ShowKey(Feature, "rival.notify.started.poachKept", name, second);
                    else ModNotify.ShowKey(Feature, "rival.notify.started.poach", name);
                    break;
                case ReactionType.Smear:
                    ModNotify.ShowKey(Feature, "rival.notify.started.smear", name);
                    break;
            }
        }

        public void NotifyEnded(ActiveReaction reaction)
        {
            if (reaction == null) return;
            ReactionType type;
            if (!ReactionKinds.TryParseReaction(reaction.type, out type)) return;
            ModNotify.ShowKey(Feature, "rival.notify.ended", AttitudeNotifier.NameOf(reaction.uid), ModL10n.L("rival.reaction." + KeyName(type)));
        }

        public void Cleanup(ActiveReaction reaction)
        {
            if (reaction == null) return;
            ReactionType type;
            if (!ReactionKinds.TryParseReaction(reaction.type, out type)) return;

            if (type == ReactionType.PriceCut) _feature.Prices.Restore(reaction.id);
            else if (type == ReactionType.CounterLaunch) _feature.Modifiers.RemoveBoost(reaction.id);
        }

        // ---- helpers shared with the effect handlers ----

        /// <summary>The name of a reaction as the strings spell it: <c>priceCut</c>, <c>counterLaunch</c>, <c>oemPush</c>, <c>poach</c>, <c>smear</c>.</summary>
        public static string KeyName(ReactionType type)
        {
            string name = ReactionKinds.NameOf(type);
            return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>The name of a segment for the player (<c>High End</c>...); empty for no segment.</summary>
        public static string SegmentName(int index)
        {
            return index >= 0 && index <= 3 ? ModL10n.L("rival.segment." + index.ToString(CultureInfo.InvariantCulture)) : string.Empty;
        }

        /// <summary>An amount of money as the game writes it.</summary>
        public static string Money(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0.0;
            return GameFormatter.FloatToMoney((float)value, "", true, true, true);
        }

        /// <summary>
        /// What keeping the engineers costs the player now (<see cref="PoachMath"/>: <c>max(250 000 × Inflation, 2 × ExpenseByResearch of the last full
        /// month)</c>; the minimum while the company has fewer than two monthly reports).
        /// </summary>
        public static float CounterOfferCost()
        {
            GamePlayer player = GamePlayer.Instance;
            ICompany company = player != null ? player.Company : null;
            return CounterOfferCost(company, InflationNow());
        }

        public static float CounterOfferCost(ICompany company, float inflation)
        {
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            RivalConfig config = feature != null ? feature.Catalog.Config : RivalConfig.Defaults();

            int count = 0;
            float spent = 0f;
            try
            {
                GameFinance finance = company != null ? company.Finance : null;
                List<GameFinance.Report> reports = finance != null ? finance.Reports : null;
                if (reports != null)
                {
                    count = reports.Count;
                    int index = PoachMath.LastFullMonthIndex(count);
                    if (index >= 0 && reports[index] != null) spent = reports[index].ExpenseByResearch;
                }
            }
            catch (Exception e)
            {
                ModLog.Warn(Feature, "poach: the reports of the player could not be read (" + e.GetType().Name + "); the minimum cost is used");
                count = 0;
            }
            return PoachMath.CounterOfferCost(inflation, count, spent, config);
        }

        /// <summary><c>InflationHandler.Instance.Inflation</c>; 1 when it is not there.</summary>
        public static float InflationNow()
        {
            GameInflation handler = GameInflation.Instance;
            if (handler == null) return 1f;
            float value = handler.Inflation;
            return float.IsNaN(value) || value <= 0f ? 1f : value;
        }

        private static int FallbackDays(ReactionType type)
        {
            switch (type)
            {
                case ReactionType.PriceCut: return 180;
                case ReactionType.CounterLaunch: return 150;
                case ReactionType.OemPush: return 365;
                case ReactionType.Poach: return 10;
                default: return 0;
            }
        }
    }
}
