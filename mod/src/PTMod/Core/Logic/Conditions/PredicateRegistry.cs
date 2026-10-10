using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Json;

namespace PTMod.Core.Logic.Conditions
{
    /// <summary>A refused <see cref="PredicateRegistry.Register"/> call, kept for the self-check report (AC-19.3).</summary>
    public sealed class RejectedPredicate
    {
        public RejectedPredicate(string type, string ownerFeature, string reason)
        {
            Type = type ?? string.Empty;
            OwnerFeature = ownerFeature ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        /// <summary>The predicate name that was refused.</summary>
        public string Type { get; }

        /// <summary>The feature that tried to register it.</summary>
        public string OwnerFeature { get; }

        /// <summary>English explanation, also the text of the log warning.</summary>
        public string Reason { get; }

        public override string ToString()
        {
            return "predicate \"" + Type + "\" of " + OwnerFeature + " refused: " + Reason;
        }
    }

    /// <summary>
    /// The table of predicates that a JSON condition can name in <c>type</c> (FR-11 items 4 and 6): name, owning feature and
    /// the factory that turns a JSON object into an <see cref="ICondition"/>. Names are case-sensitive and unique;
    /// the keywords <c>all</c>, <c>any</c> and <c>not</c> are the tree structure and cannot be taken.
    /// <para>
    /// Naming rules (D28): a predicate of the framework (<see cref="CoreOwner"/>) has no dot (<c>dateAtLeast</c>); a predicate of a
    /// feature is <c>&lt;prefix&gt;.&lt;name&gt;</c> (<c>es.hasFab</c>), so a feature can never collide with a future core predicate.
    /// The registry enforces the structural part itself. That the prefix is exactly the feature's own (table "feature to prefix" in
    /// <c>core.json</c>, <c>ModIds.IsPredicateNameAllowed</c>) is plugged in through <see cref="NamePolicy"/> by the runtime facade.
    /// A refused registration returns false and is kept in <see cref="Rejected"/>; it never throws.
    /// </para>
    /// Main thread only.
    /// </summary>
    public sealed class PredicateRegistry
    {
        /// <summary>The owner id of built-in predicates: the framework itself.</summary>
        public const string CoreOwner = FeatureGraph.CoreFeatureId;

        public const int MaxNameLength = 64;

        private sealed class Registration
        {
            public string Owner;
            public Func<JsonObject, List<string>, string, ICondition> Factory;
        }

        private readonly Dictionary<string, Registration> byName = new Dictionary<string, Registration>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();
        private readonly List<RejectedPredicate> rejected = new List<RejectedPredicate>();

        /// <param name="namePolicy">
        /// Optional extra check <c>(ownerFeature, predicateName) =&gt; allowed</c>, applied after the structural rules;
        /// the runtime passes <c>ModIds.IsPredicateNameAllowed</c>. Null means "structural rules only".
        /// </param>
        public PredicateRegistry(Func<string, string, bool> namePolicy = null)
        {
            NamePolicy = namePolicy;
        }

        /// <summary>See the constructor. Can be set once the prefix table is loaded; affects later registrations only.</summary>
        public Func<string, string, bool> NamePolicy { get; set; }

        /// <summary>All registered names in registration order (core predicates first, then features in the order they registered).</summary>
        public IReadOnlyList<string> KnownTypes
        {
            get { return order; }
        }

        /// <summary>Refused registrations in the order they happened.</summary>
        public IReadOnlyList<RejectedPredicate> Rejected
        {
            get { return rejected; }
        }

        public bool IsKnown(string type)
        {
            return type != null && byName.ContainsKey(type);
        }

        /// <summary>The feature that registered the predicate; null for an unknown name.</summary>
        public string OwnerOf(string type)
        {
            Registration registration;
            return type != null && byName.TryGetValue(type, out registration) ? registration.Owner : null;
        }

        /// <summary>
        /// Registers a predicate. <paramref name="factory"/> gets the JSON object of the node (it contains <c>type</c> and the parameters),
        /// the shared error list and the path of the node (<c>$.all[1]</c>); it returns the condition, or null after adding
        /// errors (paths of the parameters are <c>path + "." + name</c>). Returns false and records a <see cref="RejectedPredicate"/> on refusal.
        /// </summary>
        public bool Register(string type, string ownerFeature, Func<JsonObject, List<string>, string, ICondition> factory)
        {
            string error;
            return TryRegister(type, ownerFeature, factory, out error);
        }

        /// <summary>Same as <see cref="Register"/> and tells why a registration was refused.</summary>
        public bool TryRegister(string type, string ownerFeature, Func<JsonObject, List<string>, string, ICondition> factory, out string error)
        {
            error = Validate(type, ownerFeature, factory);
            if (error != null)
            {
                rejected.Add(new RejectedPredicate(type, ownerFeature, error));
                return false;
            }

            byName[type] = new Registration { Owner = ownerFeature, Factory = factory };
            order.Add(type);
            return true;
        }

        /// <summary>
        /// Builds the condition for a node with the given <c>type</c>. Returns null and adds at least one error (to the path
        /// <c>path.type</c> for an unknown predicate) when the predicate is unknown, the factory refuses the parameters, returns
        /// nothing or throws; a factory exception never leaves this method.
        /// </summary>
        public ICondition Create(string type, JsonObject node, List<string> errors, string path)
        {
            Registration registration;
            if (type == null || !byName.TryGetValue(type, out registration))
            {
                errors.Add(path + ".type: unknown predicate \"" + type + "\"");
                return null;
            }

            int before = errors.Count;
            ICondition condition;
            try
            {
                condition = registration.Factory(node, errors, path);
            }
            catch (Exception ex)
            {
                errors.Add(path + ": predicate \"" + type + "\" failed while reading its parameters: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }

            if (errors.Count > before) return null;
            if (condition == null)
            {
                errors.Add(path + ": predicate \"" + type + "\" produced no condition");
                return null;
            }
            return condition;
        }

        private string Validate(string type, string ownerFeature, Func<JsonObject, List<string>, string, ICondition> factory)
        {
            if (string.IsNullOrEmpty(ownerFeature)) return "the owner feature is not given";
            if (factory == null) return "no factory given";

            string shape = CheckShape(type);
            if (shape != null) return shape;

            if (type == "all" || type == "any" || type == "not")
                return "\"" + type + "\" is a tree keyword and cannot name a predicate";

            bool hasDot = type.IndexOf('.') >= 0;
            if (string.Equals(ownerFeature, CoreOwner, StringComparison.Ordinal))
            {
                if (hasDot) return "names of framework predicates contain no dot";
            }
            else if (!hasDot)
            {
                return "the name has no feature prefix: a predicate of a feature is named <prefix>.<name> (for example es.hasFab)";
            }

            Func<string, string, bool> policy = NamePolicy;
            if (policy != null)
            {
                bool allowed;
                try
                {
                    allowed = policy(ownerFeature, type);
                }
                catch (Exception ex)
                {
                    return "the name check failed: " + ex.GetType().Name + ": " + ex.Message;
                }
                if (!allowed) return "the name is not allowed for feature " + ownerFeature + " (it must start with the prefix of that feature)";
            }

            Registration existing;
            if (byName.TryGetValue(type, out existing))
                return "the name is already registered by " + existing.Owner;
            return null;
        }

        /// <summary>Letters, digits and underscores in dot-separated segments; each segment starts with a letter; at most <see cref="MaxNameLength"/> characters.</summary>
        private static string CheckShape(string type)
        {
            if (string.IsNullOrEmpty(type)) return "the name is empty";
            if (type.Length > MaxNameLength) return "the name is longer than " + MaxNameLength + " characters";

            bool segmentStart = true;
            for (int i = 0; i < type.Length; i++)
            {
                char c = type[i];
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (segmentStart)
                {
                    if (!letter) return "each part of the name must start with a letter (\"" + type + "\")";
                    segmentStart = false;
                }
                else if (c == '.')
                {
                    segmentStart = true;
                }
                else if (!letter && !(c >= '0' && c <= '9') && c != '_')
                {
                    return "the name may contain only letters, digits, underscores and dots (\"" + type + "\")";
                }
            }
            return segmentStart ? "the name must not end with a dot (\"" + type + "\")" : null;
        }
    }
}
