using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// The library of conditions (FR-11): a JSON description such as <c>{"all":[{"type":"dateAtLeast","year":1980},{"type":"oemContractsActiveAtLeast","count":1}]}</c>
    /// is parsed once into a tree of <see cref="ICondition"/> and evaluated against the game whenever a feature asks. The grammar, the 32 predicates of the
    /// framework and the rules of evaluation are the pure logic in <c>Core/Logic/Conditions</c>; this class is its one instance and its place in the game:
    /// <list type="bullet">
    /// <item><b>Parse.</b> <see cref="Parse"/> never throws: every problem is a line <c>path: message</c> in the error list (<c>$.all[1].year: ...</c>) and the result
    /// is null when there is any. A condition is parsed when its content is loaded, not on every evaluation.</item>
    /// <item><b>Evaluate.</b> <see cref="Evaluate"/> answers a plain bool and never throws (AC-11.3). The subject is the player unless a company is given; the date
    /// is <see cref="ModTicks.Today"/>. A data source that is not available - a game singleton that is null, an unknown company, fewer than two finance
    /// reports - makes its predicate false (and <c>not</c> of it true: conditions are two-valued); a singleton is reported once per scene in the log. A predicate
    /// of a feature that throws makes the whole tree false and leaves a warning. The game is read through <see cref="GameStateAdapter"/>, whose lists
    /// (CPUs, AI companies, the CPU ids of a company) are cached for one game day and dropped when a company goes bankrupt or spawns
    /// (<see cref="ModCompanies.Changed"/>) or by <see cref="InvalidateCaches"/>.</item>
    /// <item><b>Register.</b> A feature adds a predicate with <see cref="Register"/> in its <c>Register</c> method, under its own prefix:
    /// <c>ModConditions.Register("es.hasFab", "event-system", factory)</c>; the factory reads the parameters of the JSON object (<see cref="ConditionArgs"/> does
    /// the typed reading and the error paths) and returns an <see cref="ICondition"/>, for example a <see cref="DelegateCondition"/>. A name that is taken, that has no dot
    /// while the owner is a feature, or that does not start with the prefix of its feature is refused: <c>false</c>, a warning, and the refusal is kept in
    /// <see cref="Rejected"/> for the self-check (AC-19.3). The predicates of the framework are registered at the Declare stage and have no dot in the name.
    /// A condition a feature's factory returns is wrapped: it is false while its feature is not active, and an exception it throws is counted against that
    /// feature (FR-2) and then makes the whole tree false.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public static class ModConditions
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string EvaluateSite = "conditions/evaluate";
        private const string ParseSite = "conditions/parse";
        private const string RegisterSite = "conditions/register";

        /// <summary>The subject key of a company object that is no company of the game (a key that nothing resolves: every company predicate is false).</summary>
        private const string UnknownSubject = "unknown";

        private static readonly Action<string> ErrorSink = OnConditionError;

        private static PredicateRegistry _registry;
        private static ConditionParser _parser;
        private static IGameState _stateOverride;
        private static ConditionContext _context;       // the last context: reused while the game state, the day and the subject stay the same
        private static bool _started;

        // ---- public API ----

        /// <summary>
        /// Parses a condition (<c>type</c> with its parameters, or <c>all</c> / <c>any</c> with a list, or <c>not</c> with a node). Errors are added to
        /// <paramref name="errors"/> as <c>path: message</c>, the path starting at <paramref name="path"/> (the place of the node in its document); the result is null
        /// when there is any error, so a half-built condition is never used. Never throws.
        /// </summary>
        public static ICondition Parse(JsonNode node, List<string> errors, string path = "$")
        {
            try
            {
                return Parser.Parse(node, errors, path);
            }
            catch (Exception ex)
            {
                // The parser reports bad input as errors itself; this is a defect, and the caller is still told that the condition is unusable.
                ModHooks.ReportFault(CoreId, ParseSite, ex);
                if (errors != null) errors.Add((path ?? "$") + ": the condition could not be parsed (" + ex.GetType().Name + ")");
                return null;
            }
        }

        /// <summary>
        /// Evaluates a condition now, from the point of view of <paramref name="subject"/> (the player when null): the predicates without an explicit
        /// <c>company</c> look at it. A null condition (a parse that failed) is false. Never throws.
        /// </summary>
        public static bool Evaluate(ICondition condition, ICompany subject = null)
        {
            if (condition == null) return false;
            if (!ModHooks.CanRun(CoreId, EvaluateSite)) return false;

            try
            {
                return ConditionEvaluator.Evaluate(condition, MakeContext(subject), ErrorSink);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, EvaluateSite, ex);
                return false;
            }
        }

        /// <summary>
        /// Makes the predicate <paramref name="type"/> available in JSON. <paramref name="type"/> of a feature is <c>&lt;prefix&gt;.&lt;name&gt;</c> (D28); the names of the
        /// framework have no dot. <paramref name="factory"/> gets the JSON object of the node, the error list and the path of the node, and returns the condition (or
        /// null after adding errors). Returns false - with a warning in the log and an entry in <see cref="Rejected"/> - when the name is taken, is not allowed for
        /// the owner (no prefix of its feature), is not a valid name, or when the owner or the factory is missing. Never throws.
        /// </summary>
        public static bool Register(string type, string ownerFeature, Func<JsonObject, List<string>, string, ICondition> factory)
        {
            string owner = string.IsNullOrEmpty(ownerFeature) ? CoreId : ownerFeature;
            try
            {
                // The prefix of a feature is checked by the registry's NamePolicy (ModIds.IsPredicateNameAllowed); the structural rules are its own.
                string error;
                Func<JsonObject, List<string>, string, ICondition> registered = factory;
                if (factory != null && owner != CoreId) registered = (node, errors, path) => OwnByFeature(owner, type, factory(node, errors, path));

                if (Registry.TryRegister(type, ownerFeature, registered, out error))
                {
                    ModLog.Debug(owner, "conditions: predicate \"" + type + "\" registered");
                    return true;
                }

                ModLog.Warn(owner, "conditions: predicate \"" + type + "\" is refused: " + error);
                return false;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, RegisterSite, ex);
                return false;
            }
        }

        /// <summary>The names of all registered predicates: those of the framework first, then those of the features in the order they registered.</summary>
        public static IReadOnlyCollection<string> KnownTypes
        {
            get { return Registry.KnownTypes; }
        }

        /// <summary>
        /// Drops the cached lists of the adapter (CPUs, AI companies, CPU ids). The cache lives for one game day and ends by itself when a company goes bankrupt
        /// or spawns, so this is only for a feature that has just changed the world in a way the cache cannot see and evaluates a condition right away.
        /// </summary>
        public static void InvalidateCaches()
        {
            try
            {
                GameStateAdapter.Instance.Invalidate();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, EvaluateSite, ex);
            }
        }

        // ---- core services ----

        /// <summary>Refused registrations in the order they happened (the self-check reports them: AC-19.3, FR-11 item 6).</summary>
        internal static IReadOnlyList<RejectedPredicate> Rejected
        {
            get { return Registry.Rejected; }
        }

        /// <summary>The feature that registered the predicate (<c>mod-framework</c> for the core ones); null for an unknown name.</summary>
        internal static string OwnerOf(string type)
        {
            return Registry.OwnerOf(type);
        }

        /// <summary>The table of predicates (the prefix rule is plugged into its <see cref="PredicateRegistry.NamePolicy"/>).</summary>
        internal static PredicateRegistry Registry
        {
            get
            {
                PredicateRegistry registry = _registry;
                if (registry != null) return registry;

                try
                {
                    registry = Predicates.CreateRegistry();
                }
                catch (Exception ex)
                {
                    // The core predicates are plain data; this is a defect. Features can still register theirs, and conditions with a core predicate fail to parse.
                    ModLog.Error(CoreId, "conditions: the predicates of the framework could not be registered", ex);
                    registry = new PredicateRegistry();
                }
                _registry = registry;
                return registry;
            }
        }

        /// <summary>
        /// The game state used by <see cref="Evaluate"/> instead of the game: a fake world for the offline harness. Null puts the adapter back.
        /// </summary>
        internal static void UseStateForTests(IGameState state)
        {
            _stateOverride = state;
            _context = null;
        }

        /// <summary>Declare stage: the predicates of the framework are registered, and the cache of the adapter follows the companies of the world.</summary>
        [CoreInit(CoreInitStage.Declare)]
        internal static void Declare()
        {
            PredicateRegistry registry = Registry;   // creates it with the core predicates
            ModCompanies.Changed += GameStateAdapter.OnCompaniesChanged;
            ModLog.Debug(CoreId, "conditions: " + registry.KnownTypes.Count + " predicates of the framework registered");
        }

        /// <summary>HostReady stage: a new scene starts with an empty cache and lets every missing source be reported once again.</summary>
        [CoreInit(CoreInitStage.HostReady, 700, Optional = true)]
        internal static void Start()
        {
            if (_started) return;
            _started = true;

            PTModHost.SceneReady += OnSceneReady;
            PTModHost.SceneLeaving += OnSceneLeaving;
        }

        // ---- internals ----

        private static ConditionParser Parser
        {
            get
            {
                ConditionParser parser = _parser;
                if (parser != null && ReferenceEquals(parser.Registry, Registry)) return parser;

                parser = new ConditionParser(Registry);
                _parser = parser;
                return parser;
            }
        }

        private static ConditionContext MakeContext(ICompany subject)
        {
            IGameState state = _stateOverride ?? GameStateAdapter.Instance;

            DateTime today = ModTicks.Today;
            if (today == default(DateTime)) GameStateAdapter.MissingSource("DateController.Instance");   // outside the game scene: the date predicates are false

            string key = CompanyKey.Player;
            if (!ReferenceEquals(subject, null))
            {
                key = ModCompanies.KeyOf(subject);
                if (key == null)
                {
                    ModLog.Debug(CoreId, "conditions: the subject is no company of the game; the predicates about a company are false");
                    key = UnknownSubject;
                }
            }

            ConditionContext context = _context;
            if (context != null && ReferenceEquals(context.State, state) && context.Today == today
                && string.Equals(context.SubjectKey, key, StringComparison.Ordinal))
            {
                return context;
            }

            context = new ConditionContext(state, today, key);
            _context = context;
            return context;
        }

        /// <summary>Puts the condition a feature's factory built under the feature's account (null stays null: the factory has reported its errors).</summary>
        private static ICondition OwnByFeature(string owner, string type, ICondition condition)
        {
            return condition == null ? null : new FeatureCondition(owner, type, condition);
        }

        /// <summary>
        /// A condition of a feature, as the registry holds it: false while the feature is not active (switched off, Faulted, Unavailable - its code does not
        /// run), and an exception from it is counted against the feature, not the core, before it goes on and makes the tree false.
        /// </summary>
        private sealed class FeatureCondition : ICondition
        {
            private readonly string _owner;
            private readonly string _site;
            private readonly ICondition _inner;

            public FeatureCondition(string owner, string type, ICondition inner)
            {
                _owner = owner;
                _site = "conditions/" + type;
                _inner = inner;
            }

            public bool Evaluate(ConditionContext ctx)
            {
                if (!ModFeatures.IsActive(_owner)) return false;

                try
                {
                    return _inner.Evaluate(ctx);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(_owner, _site, ex);
                    throw;   // ConditionEvaluator makes the whole tree false and writes the line for the log
                }
            }

            public string Describe()
            {
                return _inner.Describe();
            }
        }

        /// <summary>A condition threw while it was evaluated (a predicate of a feature, a failing source): the tree was false, this is the line for the log.</summary>
        private static void OnConditionError(string message)
        {
            ModLog.Warn(CoreId, "conditions: " + message + "; the condition is false");
        }

        private static void OnSceneReady(int scene)
        {
            ModHooks.Guard(CoreId, EvaluateSite, ResetScene);
        }

        private static void OnSceneLeaving()
        {
            ModHooks.Guard(CoreId, EvaluateSite, ResetScene);
        }

        private static void ResetScene()
        {
            GameStateAdapter.Instance.ResetScene();
            _context = null;
        }
    }
}
