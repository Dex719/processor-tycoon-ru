using System;
using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using PTMod.Core.Logic.Conditions;
using PTMod.Core.Logic.Json;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// The five predicates of the feature (design.md, Component 5): <c>hist.ShareAtLeast</c>, <c>hist.HasCpuSince</c>, <c>hist.CaseActive</c>,
    /// <c>hist.PlayerHasCpuAbove</c> and <c>hist.IsFabless</c>. <see cref="Evaluate"/> is the function itself, the same one the game registers with
    /// <c>EventConditions.Register</c> and the tests register in a <c>PredicateRegistry</c> (<see cref="Factory"/>), so a parsed condition and a direct
    /// call give the same answer. The company a predicate is about is the one named by the <c>role</c> parameter (every company of the role; the
    /// predicate is true when <i>any</i> of them passes) or, without it, the subject of the context. A predicate whose parameters are wrong answers
    /// false and says so once; none of them throws.
    /// </summary>
    public static class HistPredicates
    {
        public const string ShareAtLeast = "hist.ShareAtLeast";
        public const string HasCpuSince = "hist.HasCpuSince";
        public const string CaseActive = "hist.CaseActive";
        public const string PlayerHasCpuAbove = "hist.PlayerHasCpuAbove";
        public const string IsFabless = "hist.IsFabless";

        // Declared before Names: static fields are initialized in the order of the text.
        private static readonly string[] NameArray = { ShareAtLeast, HasCpuSince, CaseActive, PlayerHasCpuAbove, IsFabless };

        /// <summary>The names, in the order of the design table.</summary>
        public static readonly IReadOnlyList<string> Names = NameArray;

        private const int DefaultMonths = RoleSpec.DefaultMonths;

        public static bool IsKnown(string type)
        {
            return type != null && Array.IndexOf(NameArray, type) >= 0;
        }

        /// <summary>
        /// The truth of predicate <paramref name="type"/> with the parameters of <paramref name="args"/> (the JSON object of the node, <c>type</c> and the
        /// parameters at the top level) for the subject of <paramref name="ctx"/>, in the world and the roles of <paramref name="frame"/> (null: the
        /// predicate is false). An unknown type is false.
        /// </summary>
        public static bool Evaluate(string type, JsonObject args, ConditionContext ctx, HistScopeFrame frame)
        {
            if (frame == null || frame.World == null) return false;
            JsonObject node = args ?? new JsonObject();

            switch (type)
            {
                case ShareAtLeast: return EvalShare(node, ctx, frame);
                case HasCpuSince: return EvalHasCpuSince(node, ctx, frame);
                case CaseActive: return EvalCaseActive(node, frame);
                case PlayerHasCpuAbove: return EvalPlayerHasCpuAbove(node, frame);
                case IsFabless: return EvalIsFabless(node, ctx, frame);
                default: return false;
            }
        }

        /// <summary>
        /// The factory of a predicate for a <c>PredicateRegistry</c>: a condition node that asks <see cref="Evaluate"/> with the frame that
        /// <paramref name="frameOf"/> gives at evaluation time (what <c>EventConditions.Register</c> does in the game).
        /// </summary>
        public static Func<JsonObject, List<string>, string, ICondition> Factory(string type, Func<HistScopeFrame> frameOf)
        {
            return (node, errors, path) => new DelegateCondition(type, ctx => Evaluate(type, node, ctx, frameOf == null ? null : frameOf()));
        }

        // ---- the predicates ----

        private static bool EvalShare(JsonObject node, ConditionContext ctx, HistScopeFrame frame)
        {
            double value;
            if (!node.TryGetDouble("value", out value) || double.IsNaN(value) || double.IsInfinity(value))
                return Bad(frame, ShareAtLeast, "needs a number 'value' (0 to 1)");

            int months;
            if (!Months(node, ShareAtLeast, frame, out months)) return false;

            SegmentMask mask;
            if (!Segments(node, ShareAtLeast, frame, out mask)) return false;

            foreach (string key in CompaniesOf(node, ctx, frame))
            {
                if (frame.State.ForceLeader && CompanyKey.IsPlayer(key)) return true;
                if (frame.Shares.Share(key, mask, months) + 1e-6f >= (float)value) return true;
            }
            return false;
        }

        private static bool EvalHasCpuSince(JsonObject node, ConditionContext ctx, HistScopeFrame frame)
        {
            int months;
            if (!Months(node, HasCpuSince, frame, out months)) return false;

            SegmentMask mask;
            if (!Segments(node, HasCpuSince, frame, out mask)) return false;

            IReadOnlyList<CpuInfo> cpus = frame.World.Cpus;
            if (cpus == null) return false;

            SimpleDate today = frame.World.Today;
            List<string> companies = CompaniesOf(node, ctx, frame);
            for (int i = 0; i < cpus.Count; i++)
            {
                CpuInfo cpu = cpus[i];
                if (cpu == null || cpu.CompanyKey == null || !companies.Contains(cpu.CompanyKey)) continue;
                if (!WorldMath.IsRecent(cpu, today, months)) continue;
                if (mask == SegmentMask.All || (cpu.Segment != Segment.Any && (mask & WorldMath.MaskOf(cpu.Segment)) != 0)) return true;
            }
            return false;
        }

        private static bool EvalCaseActive(JsonObject node, HistScopeFrame frame)
        {
            string caseId;
            if (!node.TryGetString("caseId", out caseId) || string.IsNullOrEmpty(caseId)) return Bad(frame, CaseActive, "needs a text 'caseId'");

            bool targetNotPlayer = node.GetBool("targetNotPlayer", false);
            Func<string, bool, bool> active = frame.State.CaseActive;
            return active != null && active(caseId, targetNotPlayer);
        }

        private static bool EvalPlayerHasCpuAbove(JsonObject node, HistScopeFrame frame)
        {
            string ruleId;
            if (!node.TryGetString("ruleId", out ruleId) || string.IsNullOrEmpty(ruleId)) return Bad(frame, PlayerHasCpuAbove, "needs a text 'ruleId'");

            Func<string, float?> thresholdOf = frame.State.ExportThreshold;
            float? threshold = thresholdOf == null ? null : thresholdOf(ruleId);
            if (!threshold.HasValue) return false;

            bool anyCompany = node.GetBool("anyCompany", false);
            IReadOnlyList<CpuInfo> cpus = frame.World.Cpus;
            for (int i = 0; cpus != null && i < cpus.Count; i++)
            {
                CpuInfo cpu = cpus[i];
                if (!WorldMath.IsOnMarket(cpu) || !(cpu.Ips >= threshold.Value)) continue;
                if (anyCompany || CompanyKey.IsPlayer(cpu.CompanyKey)) return true;
            }
            return false;
        }

        private static bool EvalIsFabless(JsonObject node, ConditionContext ctx, HistScopeFrame frame)
        {
            IReadOnlyList<CompanyInfo> companies = frame.World.Companies;
            if (companies == null) return false;

            List<string> keys = CompaniesOf(node, ctx, frame);
            foreach (CompanyInfo company in companies)
            {
                if (company != null && company.IsFabless && keys.Contains(company.Key)) return true;
            }
            return false;
        }

        // ---- parameters ----

        /// <summary>The companies the predicate is about: the ones of the role, else the subject of the context.</summary>
        private static List<string> CompaniesOf(JsonObject node, ConditionContext ctx, HistScopeFrame frame)
        {
            string role;
            if (node.TryGetString("role", out role) && !string.IsNullOrEmpty(role))
            {
                return new List<string>(frame.Roles.KeysOf(role));
            }

            string subject = ctx == null || string.IsNullOrEmpty(ctx.SubjectKey) ? CompanyKey.Player : ctx.SubjectKey;
            return new List<string> { subject };
        }

        private static bool Months(JsonObject node, string type, HistScopeFrame frame, out int months)
        {
            months = DefaultMonths;
            if (!node.ContainsKey("months") || node["months"].IsNull) return true;

            if (!node.TryGetInt32("months", out months) || months < 1 || months > MarketShareCache.HardMaxMonths)
            {
                months = DefaultMonths;
                return Bad(frame, type, "'months' must be a whole number from 1 to " + MarketShareCache.HardMaxMonths);
            }
            return true;
        }

        /// <summary><c>segments</c> (a name or a list of names) or <c>segment</c> (one name); all of them when absent.</summary>
        private static bool Segments(JsonObject node, string type, HistScopeFrame frame, out SegmentMask mask)
        {
            mask = SegmentMask.All;
            JsonNode member = node["segments"];
            if (member == null || member.IsNull) member = node["segment"];
            if (member == null || member.IsNull) return true;

            var names = new List<string>();
            var single = member as JsonValue;
            var many = member as JsonArray;
            if (single != null && single.Kind == JsonKind.String)
            {
                names.Add(single.StringValue);
            }
            else if (many != null)
            {
                foreach (JsonNode item in many)
                {
                    var name = item as JsonValue;
                    if (name == null || name.Kind != JsonKind.String) return Bad(frame, type, "segments must be names");
                    names.Add(name.StringValue);
                }
            }
            else
            {
                return Bad(frame, type, "segments must be a name or a list of names");
            }

            var segments = new List<Segment>();
            foreach (string name in names)
            {
                Segment segment;
                if (!HistSegments.TryParse(name, out segment)) return Bad(frame, type, "unknown segment \"" + name + "\"");
                segments.Add(segment);
            }
            mask = WorldMath.MaskOf(segments);
            return true;
        }

        private static bool Bad(HistScopeFrame frame, string type, string message)
        {
            if (frame != null && frame.State != null) frame.State.WarnOnce(type + "|" + message, "condition " + type + ": " + message + "; the predicate is false");
            return false;
        }
    }
}
