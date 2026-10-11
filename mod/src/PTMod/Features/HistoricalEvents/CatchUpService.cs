using System;
using System.Collections.Generic;
using System.Text;
using PTMod.Core;
using PTMod.Features.EventSystem;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// A game that starts late still feels the past (FR-4; design.md, Component 9; TSK-007): the standard timed effects of the stages of earlier entries that
    /// would still be running at the start become entries of <c>ModModifiers</c> (<see cref="ModModifiers.Add"/>) with the days they have left and the
    /// reason <c>hist.catchup</c>, and one letter "State of the industry" lists them. What is caught up, and why, is <see cref="CatchUpPlanner"/>; this
    /// class reads the world for it and does the writing.
    /// <list type="bullet">
    /// <item><b>Channels.</b> <c>MarketDemand</c> acts on <c>market.demand.&lt;segment&gt;</c> for the whole market; <c>ModifierTimed</c> on its channel (the
    /// whole market for <c>scope: Global</c>, <c>gpu.demand</c> of a crypto boom among them, otherwise the companies of the fire's role); <c>FactoryOutput</c> on
    /// <c>production.output</c> of those companies. The multiplier has the intensity of the settings applied, like the effects the engine resolves.</item>
    /// <item><b>Not caught up:</b> choices, the effects of the feature's own types (cases, rules, wars), entries of a category that is off (<c>disaster</c>
    /// by default) and entries whose conditions are false (the crypto booms without <c>gpu-product-line</c>).</item>
    /// <item><b>The ids</b> of the entries made are kept in the section of the save (<c>catchUpEntryIds</c>), so they are known after a load.</item>
    /// </list>
    /// </summary>
    internal sealed class CatchUpService : ICatchUpWorld
    {
        private readonly HistoricalEventsFeature _feature;
        private readonly HistRuntime _runtime;

        internal CatchUpService(HistoricalEventsFeature feature, HistRuntime runtime)
        {
            _feature = feature;
            _runtime = runtime;
        }

        /// <summary>
        /// Catches up at <paramref name="startDay"/> (days from 1971-01-01). False when the events of the pack are not available yet (the event system reads
        /// them on the first <c>GameReady</c> of the session): the caller tries again later. True when it is done, also when there was nothing to catch up
        /// or a failure was logged.
        /// </summary>
        internal bool Run(int startDay)
        {
            if (CalendarContent.LoadedPacks() == null) return false;

            try
            {
                CatchUpPlan plan = CatchUpPlanner.Plan(startDay, _feature.Calendar, this);
                int made = Apply(plan, startDay);
                if (made > 0) SendLetter(plan, startDay);
                ModLog.Info(HistIds.Feature, "catch-up: " + made + " effects of " + plan.EntryIds.Count + " entries still running on day " + startDay);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(HistIds.Feature, "catchUp/run", ex);
                ModLog.Error(HistIds.Feature, "catch-up failed; the game goes on without it", ex);
            }
            return true;
        }

        // ---- writing ----

        private int Apply(CatchUpPlan plan, int startDay)
        {
            HistSaveData data = _feature.Save.Data;
            var titles = new Dictionary<string, string>(StringComparer.Ordinal);
            var marked = new HashSet<string>(StringComparer.Ordinal);
            int made = 0;

            foreach (CatchUpItem item in plan.Items)
            {
                string title;
                if (!titles.TryGetValue(item.EntryId, out title))
                {
                    title = TitleOf(item.EntryId);
                    titles[item.EntryId] = title;
                }

                string channel = item.Channel ?? ModChannels.MarketDemand(item.DemandSegment);
                if (channel == null) continue;

                ModifierScope scope = item.Global ? ModifierScope.Global() : ModifierScope.Company(item.CompanyKey);
                string id = ModModifiers.Add(HistIds.Feature, channel, scope, item.Multiplier, item.RemainingDays, HistIds.CatchUpReasonKey, new[] { title });
                if (id == null) continue;

                data.catchUpEntryIds.Add(id);
                made++;

                // The chronicle lists what is still running of a caught-up entry: it needs to know that the entry was caught up and from which day (CatchUpMarks).
                if (marked.Add(item.EntryId) && _feature.Scheduler != null) _feature.Scheduler.SetFlag(item.EntryId, CatchUpMarks.FlagFor(startDay));
            }
            return made;
        }

        /// <summary>One letter for the whole catch-up: a line per entry with the longest time any of its effects has left.</summary>
        private void SendLetter(CatchUpPlan plan, int startDay)
        {
            var list = new StringBuilder();
            foreach (string entryId in plan.EntryIds)
            {
                int days = plan.LongestRemaining(entryId);
                if (list.Length > 0) list.Append('\n');
                list.Append(ModL10n.L("hist.catchup.line", TitleOf(entryId), ModL10n.Plural("hist.catchup.days", days, days)));
            }

            ModMail.Send(new ModMailMessage
            {
                FeatureId = HistIds.Feature,
                Tag = HistIds.CatchUpMailTag,
                TitleKey = "hist.catchup.title",
                BodyKey = "hist.catchup.body",
                Args = new[] { list.ToString() },
                Priority = MailPriority.Normal,
            });
        }

        private string TitleOf(string entryId)
        {
            foreach (CalendarEntry entry in _feature.Calendar)
            {
                if (entry.Id == entryId) return entry.History != null && !string.IsNullOrEmpty(entry.History.TitleKey) ? ModL10n.L(entry.History.TitleKey) : entryId;
            }
            return entryId;
        }

        // ---- ICatchUpWorld ----

        bool ICatchUpWorld.IsCategoryEnabled(string category)
        {
            return HistSettings.IsCategoryEnabled(category);
        }

        IReadOnlyList<CatchUpEffect> ICatchUpWorld.EffectsOf(string eventId)
        {
            return PackEffects.Of(eventId);
        }

        RoleResult ICatchUpWorld.ResolveRoles(CalendarEntry entry)
        {
            return _runtime.ResolveRoles(entry);
        }

        bool ICatchUpWorld.ConditionsHold(CalendarEntry entry, RoleResult roles)
        {
            return _runtime.ConditionsHold(entry, roles);
        }

        bool ICatchUpWorld.OnlyIfHolds(FireSpec fire, RoleResult roles, string companyKey)
        {
            return _runtime.Evaluator.Evaluate(fire.OnlyIf, roles, _runtime.World, _runtime.Shares, _runtime.State, companyKey);
        }

        float ICatchUpWorld.Intensity
        {
            get { return HistSettings.Intensity; }
        }
    }
}
