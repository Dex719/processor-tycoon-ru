using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// The checked steps of the tutorial, by track (design.md, "StepCatalog"). <see cref="Build"/> runs <see cref="StepValidator"/> over every
    /// step, throws out the broken ones with a reason, and checks what only the whole set can show: duplicate ids, two sequential steps with the
    /// same order in a track, a track left without sequential steps. The result is immutable; the machine of the tutorial reads it and never
    /// meets an unchecked step.
    /// <para>
    /// A track without sequential steps is switched off (<see cref="HasTrack"/> is false) and described in <see cref="Errors"/>; a catalog
    /// with no track at all (<see cref="IsEmpty"/>) means the content is broken as a whole and the tutorial does not start (the hub shows
    /// <c>onb.error.content</c>).
    /// </para>
    /// </summary>
    public sealed class StepCatalog
    {
        private readonly List<StepDefinition> _all;
        private readonly Dictionary<string, StepDefinition> _byId;
        private readonly Dictionary<string, ReadOnlyCollection<StepDefinition>> _sequential;
        private readonly Dictionary<string, ReadOnlyCollection<StepDefinition>> _situational;
        private readonly List<string> _errors;
        private readonly List<string> _warnings;

        private StepCatalog(OnboardingConfig config, List<StepDefinition> steps, List<string> errors, List<string> warnings)
        {
            Config = config;
            _all = steps;
            _errors = errors;
            _warnings = warnings;

            _byId = new Dictionary<string, StepDefinition>(StringComparer.Ordinal);
            foreach (StepDefinition step in steps) _byId[step.Id] = step;

            _sequential = Group(steps, StepKind.Sequential);
            _situational = Group(steps, StepKind.Situational);
        }

        /// <summary>The rhythm of the tutorial (<c>config.json</c>). Never null.</summary>
        public OnboardingConfig Config { get; private set; }

        /// <summary>The steps that passed the checks, in the order of the content file.</summary>
        public IList<StepDefinition> All
        {
            get { return new ReadOnlyCollection<StepDefinition>(_all); }
        }

        /// <summary>Problems that dropped a step or switched a track off, one English line each (<c>step 'x' skipped: ...</c>); empty when all is well.</summary>
        public IList<string> Errors
        {
            get { return new ReadOnlyCollection<string>(_errors); }
        }

        /// <summary>Findings that dropped nothing (a text that is too long, a missing short name, a reference to an unknown step).</summary>
        public IList<string> Warnings
        {
            get { return new ReadOnlyCollection<string>(_warnings); }
        }

        /// <summary>No track has a sequential step: the content is unusable and the tutorial does not start.</summary>
        public bool IsEmpty
        {
            get { return _sequential.Count == 0; }
        }

        /// <summary>The track has at least one sequential step, so the tutorial can run on it.</summary>
        public bool HasTrack(string track)
        {
            return track != null && _sequential.ContainsKey(track);
        }

        /// <summary>
        /// The tracks (of <see cref="StepValidator.Tracks"/>) that have no sequential step, so the tutorial is off for them; empty when every track has
        /// steps. A catalog with all of them missing is <see cref="IsEmpty"/>. A new list each time.
        /// </summary>
        public IList<string> MissingTracks
        {
            get
            {
                var missing = new List<string>();
                foreach (string track in StepValidator.Tracks)
                {
                    if (!HasTrack(track)) missing.Add(track);
                }
                return missing;
            }
        }

        /// <summary>
        /// The steps of a track of one kind. Sequential steps come by <see cref="StepDefinition.Order"/>; situational ones in the order of the
        /// content file. Empty (never null) for an unknown track or one without such steps. Read-only; the same list each time.
        /// </summary>
        public IList<StepDefinition> StepsFor(string track, StepKind kind)
        {
            Dictionary<string, ReadOnlyCollection<StepDefinition>> source = kind == StepKind.Sequential ? _sequential : _situational;
            ReadOnlyCollection<StepDefinition> steps;
            if (track != null && source.TryGetValue(track, out steps)) return steps;
            return Empty;
        }

        /// <summary>The step with the id, or null.</summary>
        public StepDefinition Find(string stepId)
        {
            StepDefinition step;
            return stepId != null && _byId.TryGetValue(stepId, out step) ? step : null;
        }

        /// <summary>The number of sequential steps of the track ("Step N of M": M); 0 for a track without any.</summary>
        public int SequentialCount(string track)
        {
            return StepsFor(track, StepKind.Sequential).Count;
        }

        private static readonly ReadOnlyCollection<StepDefinition> Empty = new ReadOnlyCollection<StepDefinition>(new StepDefinition[0]);

        // ---- build ----

        /// <summary>
        /// Checks the steps and builds the catalog. Never throws and never returns null: what cannot be used is left out and explained in
        /// <see cref="Errors"/> (<c>step 'research' skipped: completeWhen.type: unknown predicate 'researchActve'</c>). A step is kept only when it
        /// passes <see cref="StepValidator.Validate"/> whole; among steps with one id the first one stays; among sequential steps of a track with
        /// one order the first one stays.
        /// </summary>
        /// <param name="steps">The steps in the order of the content file; null entries are reported and skipped.</param>
        /// <param name="config">The rhythm; null gives the defaults.</param>
        /// <param name="context">The game layer's parser of conditions and string tables; null skips those checks.</param>
        public static StepCatalog Build(IList<StepDefinition> steps, OnboardingConfig config, IStepValidationContext context)
        {
            var accepted = new List<StepDefinition>();
            var errors = new List<string>();
            var warnings = new List<string>();

            try
            {
                if (steps != null)
                {
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    var orders = new Dictionary<string, string>(StringComparer.Ordinal);   // "track/order" -> id of the step that holds it

                    for (int i = 0; i < steps.Count; i++)
                    {
                        StepDefinition step = steps[i];
                        if (step == null)
                        {
                            errors.Add("step #" + (i + 1) + " skipped: the record is empty");
                            continue;
                        }

                        string label = "step '" + step.Id + "' skipped: ";
                        var problems = new List<string>();
                        if (!StepValidator.Validate(step, context, problems, warnings))
                        {
                            errors.Add(label + string.Join("; ", problems));
                            continue;
                        }

                        if (ids.Contains(step.Id))
                        {
                            errors.Add(label + "duplicate id, the first step with this id is kept");
                            continue;
                        }

                        string clash = FindOrderClash(step, orders);
                        if (clash != null)
                        {
                            errors.Add(label + clash);
                            continue;
                        }

                        ids.Add(step.Id);
                        ClaimOrders(step, orders);
                        accepted.Add(step);
                    }
                }

                CheckReferences(accepted, warnings);
            }
            catch (Exception ex)
            {
                errors.Add("the catalog could not be built completely (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            var catalog = new StepCatalog(config ?? new OnboardingConfig(), accepted, errors, warnings);
            foreach (string track in StepValidator.Tracks)
            {
                if (!catalog.HasTrack(track))
                    errors.Add("track '" + track + "': no sequential steps are left, the tutorial is off for this track");
            }
            return catalog;
        }

        // ---- internals ----

        /// <summary>Two sequential steps of one track must not share an order; returns the reason, or null.</summary>
        private static string FindOrderClash(StepDefinition step, Dictionary<string, string> orders)
        {
            if (step.Kind != StepKind.Sequential) return null;
            foreach (string track in step.Tracks)
            {
                string holder;
                if (orders.TryGetValue(OrderKey(track, step.Order), out holder))
                    return "order " + step.Order + " in track '" + track + "' is already used by step '" + holder + "'";
            }
            return null;
        }

        private static void ClaimOrders(StepDefinition step, Dictionary<string, string> orders)
        {
            if (step.Kind != StepKind.Sequential) return;
            foreach (string track in step.Tracks) orders[OrderKey(track, step.Order)] = step.Id;
        }

        private static string OrderKey(string track, int order)
        {
            return track + "/" + order.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary><c>onb.stepDone</c> names a step by id: an id that no step has can never be true, which is a mistake in the content.</summary>
        private static void CheckReferences(List<StepDefinition> steps, List<string> warnings)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (StepDefinition step in steps) ids.Add(step.Id);

            foreach (StepDefinition step in steps)
            {
                CheckReferences(step, step.ShowWhen, "showWhen", ids, warnings);
                CheckReferences(step, step.CompleteWhen, "completeWhen", ids, warnings);
                if (step.Mail == null || step.Mail.Variants == null) continue;
                foreach (BodyVariant variant in step.Mail.Variants)
                {
                    if (variant != null) CheckReferences(step, variant.When, "mail.variants.when", ids, warnings);
                }
            }
        }

        private static void CheckReferences(StepDefinition step, ConditionRef condition, string where, HashSet<string> ids, List<string> warnings)
        {
            if (condition == null) return;
            foreach (string id in StepDoneReferences(condition.Node, 0))
            {
                if (!ids.Contains(id))
                    warnings.Add("step '" + step.Id + "': " + where + " asks whether step '" + id + "' is done, but there is no such step");
            }
        }

        /// <summary>The <c>id</c> of every <c>onb.stepDone</c> node inside a condition tree.</summary>
        private static List<string> StepDoneReferences(JsonNode node, int depth)
        {
            var result = new List<string>();
            if (node == null || depth > 32) return result;

            var obj = node as JsonObject;
            if (obj != null)
            {
                string type;
                string id;
                if (obj.TryGetString("type", out type) && string.Equals(type, "onb.stepDone", StringComparison.Ordinal) && obj.TryGetString("id", out id))
                    result.Add(id);

                foreach (KeyValuePair<string, JsonNode> member in obj)
                {
                    if (member.Value != null && (member.Value.IsObject || member.Value.IsArray))
                        result.AddRange(StepDoneReferences(member.Value, depth + 1));
                }
                return result;
            }

            var array = node as JsonArray;
            if (array != null)
            {
                foreach (JsonNode item in array) result.AddRange(StepDoneReferences(item, depth + 1));
            }
            return result;
        }

        private static Dictionary<string, ReadOnlyCollection<StepDefinition>> Group(List<StepDefinition> steps, StepKind kind)
        {
            // A stable sort: by order for the sequential steps (the order is unique inside a track), by the file order for the tips.
            var perTrack = new Dictionary<string, List<StepDefinition>>(StringComparer.Ordinal);
            foreach (StepDefinition step in steps)
            {
                if (step.Kind != kind) continue;
                foreach (string track in step.Tracks)
                {
                    List<StepDefinition> list;
                    if (!perTrack.TryGetValue(track, out list))
                    {
                        list = new List<StepDefinition>();
                        perTrack[track] = list;
                    }
                    list.Add(step);
                }
            }

            var result = new Dictionary<string, ReadOnlyCollection<StepDefinition>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, List<StepDefinition>> pair in perTrack)
            {
                List<StepDefinition> ordered = pair.Value;
                if (kind == StepKind.Sequential) ordered = StableByOrder(ordered);
                result[pair.Key] = new ReadOnlyCollection<StepDefinition>(ordered);
            }
            return result;
        }

        private static List<StepDefinition> StableByOrder(List<StepDefinition> list)
        {
            var indexed = new List<KeyValuePair<int, StepDefinition>>(list.Count);
            for (int i = 0; i < list.Count; i++) indexed.Add(new KeyValuePair<int, StepDefinition>(i, list[i]));
            indexed.Sort((a, b) =>
            {
                int byOrder = a.Value.Order.CompareTo(b.Value.Order);
                return byOrder != 0 ? byOrder : a.Key.CompareTo(b.Key);
            });

            var result = new List<StepDefinition>(list.Count);
            foreach (KeyValuePair<int, StepDefinition> pair in indexed) result.Add(pair.Value);
            return result;
        }
    }
}
