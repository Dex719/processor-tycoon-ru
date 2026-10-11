using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using GameContract = ProcessorTycoon.ContractSystem.Contract;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The check of the feature's own ground at the start (design.md, "Самопроверка на старте"; FR-12 item 3; TSK-020). <c>ModSelfCheck</c> of the core keeps its table of members closed, so the feature
    /// checks itself: the six methods the patches are on exist and none is reserved by the core (<c>ModHooks.IsReserved</c> is false for each), the private members of <c>Contract</c>,
    /// <c>ContractWindow</c> and <c>ContractNegotiationWindow</c> exist with the types the code reads them as, <c>Contract.GetBestOffers</c> is only called and never patched. The list is
    /// <see cref="MemberCatalog"/>, the decisions are <see cref="SelfCheckEvaluator"/>; this class only looks the members up. A finding is a line of the log (a warning) and the parts of the feature that depend on
    /// the member are named: they switch themselves off where they meet the missing member (<c>GameMembers</c>), the rest goes on. A required patch target that is gone is the core's business: it makes the
    /// whole feature Unavailable; this check says why in plain words. It changes nothing and never throws.
    /// </summary>
    internal static class ContractSelfCheck
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const BindingFlags AnyDeclared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>Runs the check and writes the result to the log. Null when the check itself failed.</summary>
        internal static SelfCheckResult Run()
        {
            using (PerfTimer.Start(TimingSites.SelfCheck))
            {
                try
                {
                    Assembly game = typeof(GameContract).Assembly;
                    SelfCheckResult result = SelfCheckEvaluator.Evaluate(MemberCatalog.All, row => Probe(game, row));
                    Report(result);
                    return result;
                }
                catch (Exception ex)
                {
                    ModLog.Warn(Feature, "selfcheck: the check of the game's members failed: " + ex.GetType().Name + ": " + ex.Message);
                    return null;
                }
            }
        }

        private static void Report(SelfCheckResult result)
        {
            foreach (SelfCheckFinding finding in result.Findings) ModLog.Warn(Feature, "selfcheck: " + finding.Text);

            if (result.IsClean) ModLog.Info(Feature, "selfcheck: " + result.Summary);
            else ModLog.Warn(Feature, "selfcheck: " + result.Summary);
        }

        // ---- the lookup of one row ----

        private static MemberProbe Probe(Assembly game, GameMember row)
        {
            // The flag hides what the code looks up by reflection; a patch target that only the core resolves cannot be hidden from it.
            if (row.Reflected && GameMembers.IsHidden(row.Key)) return MemberProbe.Of(MemberState.Missing, "hidden by the debug flag " + MissingMemberFlags.FlagOf(row.Key));

            Type type = game.GetType(row.TypeName, false);
            if (type == null) return MemberProbe.Of(MemberState.Missing, "the class " + row.TypeName + " is not in the game");

            switch (row.Kind)
            {
                case GameMemberKind.Method:
                    return ProbeMethod(type, row);
                case GameMemberKind.Field:
                    return ProbeField(type, row);
                case GameMemberKind.Property:
                    return ProbeProperty(type, row);
                default:
                    return AccessTools.Inner(type, row.Name) == null ? MemberProbe.Of(MemberState.Missing, "no nested class " + row.Name) : MemberProbe.Found;
            }
        }

        private static MemberProbe ProbeMethod(Type type, GameMember row)
        {
            MethodInfo method = FindMethod(type, row);
            if (method == null) return MemberProbe.Of(MemberState.Missing, null);
            if (method.IsStatic) return MemberProbe.Of(MemberState.WrongType, "it is static");

            if (row.IsPatchTarget)
            {
                ReservedTarget reserved;
                if (ModHooks.IsReserved(method, out reserved)) return MemberProbe.Of(MemberState.Reserved, reserved == null ? null : reserved.Describe());
            }

            if (row.MustNotBePatched && IsPatchedByFeature(row)) return MemberProbe.Of(MemberState.Patched, null);
            return MemberProbe.Found;
        }

        private static MemberProbe ProbeField(Type type, GameMember row)
        {
            FieldInfo field = GameMembers.Field(row.Key, type, row.Name, null);
            if (field == null) return MemberProbe.Of(MemberState.Missing, null);
            return TypeMatches(field.FieldType, row.ExpectedType) ? MemberProbe.Found : MemberProbe.Of(MemberState.WrongType, "it is " + field.FieldType.FullName + ", expected " + row.ExpectedType);
        }

        private static MemberProbe ProbeProperty(Type type, GameMember row)
        {
            MethodInfo getter = GameMembers.Getter(row.Key, type, row.Name, null);
            if (getter == null) return MemberProbe.Of(MemberState.Missing, null);
            return TypeMatches(getter.ReturnType, row.ExpectedType) ? MemberProbe.Found : MemberProbe.Of(MemberState.WrongType, "it is " + getter.ReturnType.FullName + ", expected " + row.ExpectedType);
        }

        private static bool TypeMatches(Type actual, string expectedFullName)
        {
            return expectedFullName == null || string.Equals(actual.FullName, expectedFullName, StringComparison.Ordinal);
        }

        /// <summary>The method by name and number of parameters, declared on the class (a private method of the game, a patch target).</summary>
        private static MethodInfo FindMethod(Type type, GameMember row)
        {
            foreach (MethodInfo candidate in type.GetMethods(AnyDeclared))
            {
                if (!string.Equals(candidate.Name, row.Name, StringComparison.Ordinal)) continue;
                if (row.ParameterCount >= 0 && candidate.GetParameters().Length != row.ParameterCount) continue;
                return candidate;
            }
            return null;
        }

        /// <summary>A patch of this feature names the method (the core reserves it, so the patch would be refused; the check says it anyway).</summary>
        private static bool IsPatchedByFeature(GameMember row)
        {
            IReadOnlyList<PatchReport> report = ModHooks.Report;
            for (int i = 0; i < report.Count; i++)
            {
                PatchReport patch = report[i];
                if (patch.FeatureId != Feature || patch.Target == null) continue;
                if (string.Equals(patch.Target, row.Key, StringComparison.Ordinal) || patch.Target.EndsWith("." + row.Name, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
