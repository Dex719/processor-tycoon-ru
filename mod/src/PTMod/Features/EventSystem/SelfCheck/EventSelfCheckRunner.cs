using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Ids;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The self-check of the engine at start (TSK-026; NFR-2, NFR-4; design.md "Самопроверка на старте"): collects the facts from the running mod, lets
    /// <see cref="EventSelfCheck"/> judge them and writes the result to <c>Player.log</c> in one line, <c>selfcheck OK: 10 events, 9 effect types</c> or
    /// <c>selfcheck found N problems: ...</c>, with one more line per finding. The core's <c>ModSelfCheck</c> has no point where a feature adds findings of its own
    /// (see <c>mod/requests/event-system.md</c>), so the engine looks for itself, like the other features do; the engine has no patches, so no patch target is looked at.
    /// <para>
    /// It runs after the packs are read (the first <c>GameReady</c>, or <c>OnEnabled</c> for a feature that was off then), when every feature has registered its
    /// effect types, predicates and packs, and again when more packs were read since (a pack that registers late). The same result is written once; a repeat is a
    /// debug line. Only looks: it changes nothing and never throws.
    /// </para>
    /// </summary>
    internal sealed class EventSelfCheckRunner
    {
        private const string FeatureId = EventSystemApi.FeatureId;
        private const string Site = "selfcheck";

        private readonly PackCatalog _catalog;
        private readonly Func<IList<string>> _effectTypes;
        private readonly Func<IDictionary<string, int>> _providers;

        private string _signature;
        private string _lastLine;

        /// <param name="catalog">The registry the packs were read into.</param>
        /// <param name="effectTypes">The effect types that have a handler (the engine's table).</param>
        /// <param name="providers">How many providers the aggregator registered per channel (<c>ModModifiersSink.Registrations</c>).</param>
        public EventSelfCheckRunner(PackCatalog catalog, Func<IList<string>> effectTypes, Func<IDictionary<string, int>> providers)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            _catalog = catalog;
            _effectTypes = effectTypes ?? (() => new string[0]);
            _providers = providers ?? (() => new Dictionary<string, int>(StringComparer.Ordinal));
        }

        /// <summary>The verdict of the last run; null before the first run.</summary>
        public EventCheckVerdict Last { get; private set; }

        /// <summary>
        /// Runs the check when the packs are read and something changed since the last run (more packs, more events, more effect types). Guarded: whatever goes wrong
        /// inside is counted against the feature and the game goes on.
        /// </summary>
        public void RunIfNeeded()
        {
            ModHooks.Guard(FeatureId, Site, () =>
            {
                if (_catalog.HasPending || _catalog.Packs.Count == 0) return;
                string signature = Signature();
                if (string.Equals(signature, _signature, StringComparison.Ordinal)) return;
                _signature = signature;
                Run();
            });
        }

        /// <summary>Runs the check now and writes the result. Never throws. Returns the verdict.</summary>
        public EventCheckVerdict Run()
        {
            EventCheckVerdict verdict;
            try
            {
                verdict = EventSelfCheck.Evaluate(Collect());
            }
            catch (Exception ex)
            {
                verdict = EventSelfCheck.Evaluate(null);
                ModLog.Warn(FeatureId, "selfcheck: the check itself failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            Last = verdict;
            try
            {
                Write(verdict);
            }
            catch (Exception)
            {
                // the log must never be the reason the engine fails to start
            }
            return verdict;
        }

        // ---- the facts ----

        private string Signature()
        {
            int loaded = 0;
            foreach (PackInfo pack in _catalog.Packs)
            {
                if (pack.Loaded) loaded++;
            }
            return loaded + "/" + _catalog.Packs.Count + "/" + _catalog.All.Count + "/" + Safe(() => _effectTypes().Count, 0);
        }

        private EventCheckFacts Collect()
        {
            var facts = new EventCheckFacts();
            facts.Definitions = _catalog.All;
            facts.OwnerOf = definition =>
            {
                PackInfo pack = _catalog.PackOf(definition.packId);
                return pack != null && !string.IsNullOrEmpty(pack.Owner) ? pack.Owner : definition.packId;
            };
            facts.EffectTypes = Safe(_effectTypes, new string[0]);
            facts.ChannelDeclared = ModModifiers.IsDeclared;
            facts.EventIdAllowed = ModIds.IsEventIdAllowed;
            facts.KnownPredicates = Safe(() => (ICollection<string>)new List<string>(ModConditions.KnownTypes), new string[0]);
            facts.PredicateNameAllowed = name => ModIds.IsPredicateNameAllowed(FeatureId, name);
            facts.KeyAllowed = key => ModIds.IsL10nKeyAllowed(FeatureId, key);

            var ru = new HashSet<string>(Safe(() => (IEnumerable<string>)ModL10n.Keys("ru"), new string[0]), StringComparer.Ordinal);
            var en = new HashSet<string>(Safe(() => (IEnumerable<string>)ModL10n.Keys("en"), new string[0]), StringComparer.Ordinal);
            facts.HasRu = ru.Contains;
            facts.HasEn = en.Contains;
            facts.ProviderRegistrations = Safe(_providers, new Dictionary<string, int>(StringComparer.Ordinal));
            return facts;
        }

        // A source of facts that fails leaves the default (the check then says what it could not see, or nothing), never an exception.
        private static T Safe<T>(Func<T> read, T fallback)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                ModLog.Warn(FeatureId, "selfcheck: could not read a fact (" + ex.GetType().Name + ": " + ex.Message + ")");
                return fallback;
            }
        }

        // ---- the log ----

        private void Write(EventCheckVerdict verdict)
        {
            string text = verdict.Line;
            if (string.Equals(text, _lastLine, StringComparison.Ordinal))
            {
                ModLog.Debug(FeatureId, text + " (the same as before)");
                return;
            }
            _lastLine = text;

            if (verdict.Ok)
            {
                ModLog.Info(FeatureId, text);
                return;
            }

            if (verdict.HasErrors) ModLog.Error(FeatureId, "selfcheck found " + verdict.Problems.Count + (verdict.Problems.Count == 1 ? " problem" : " problems"));
            else ModLog.Warn(FeatureId, "selfcheck found " + verdict.Problems.Count + (verdict.Problems.Count == 1 ? " problem" : " problems"));
            foreach (EventCheckProblem problem in verdict.Problems)
            {
                if (problem.Level == EventCheckLevel.Error) ModLog.Error(FeatureId, "selfcheck: " + problem);
                else ModLog.Warn(FeatureId, "selfcheck: " + problem);
            }
        }
    }
}
