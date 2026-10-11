using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using PTMod.Core.Logic.Conditions;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The self-check of the tutorial at start (TSK-016, NFR-2, NFR-4; design.md, "Самопроверка на старте"). The core's <c>ModSelfCheck</c> has no point
    /// where a feature adds its own findings, so the feature checks itself and writes to <c>Player.log</c>: <c>self-check: OK</c> when all is well, otherwise
    /// <c>self-check: 1 error(s), 1 warning(s)</c> and a line per finding. What is looked at:
    /// <list type="bullet">
    /// <item>the members of the game the tutorial uses (<see cref="GameMembers.All"/>: <c>CreatorManager.OpenCreatorWindowOfType</c>, <c>DateController.ManualPause</c>,
    /// the four budget getters of <c>Market</c>, the field <c>BankruptcyHandler.defaultTimeLimit</c> and the rest), by reflection and by name, so a renamed
    /// class is a finding and not a crash;</item>
    /// <item>that the core accepted the twelve predicates <c>onb.*</c> (<c>ModConditions.OwnerOf</c>, the refusals of the core);</item>
    /// <item>the catalog of the steps (<see cref="OnboardingFeature.Catalog"/>): an unusable catalog or a track without steps, which the hub page reports with
    /// <c>onb.error.content</c>, and the steps the checks dropped;</item>
    /// <item>that every key <c>onb.*</c> the tutorial needs (<see cref="OnboardingKeys"/>) is in the Russian table.</item>
    /// </list>
    /// The rules and the report are <see cref="OnboardingSelfCheckRules"/>'s (pure logic with the tests); this class is the reflection, the core calls and the log.
    /// A finding changes nothing in the tutorial: every missing member already has its way out (a fallback, a mail without a number, no pause). Never throws.
    /// </summary>
    internal static class OnboardingSelfCheck
    {
        private const string GameAssemblyName = "Assembly-CSharp";

        /// <summary>The report of the last run; null before the first run.</summary>
        internal static SelfCheckReport Last { get; private set; }

        /// <summary>Runs the check, writes the result to the log and returns it.</summary>
        internal static SelfCheckReport Run()
        {
            SelfCheckReport report;
            try
            {
                report = OnboardingSelfCheckRules.Run(BuildInput());
            }
            catch (Exception ex)
            {
                report = new SelfCheckReport();
                report.Add(SelfCheckSeverity.Error, OnboardingSelfCheckRules.AreaCheck, "the self-check itself failed (" + ex.GetType().Name + ": " + ex.Message + ")");
            }

            Last = report;
            try
            {
                Write(report);
            }
            catch (Exception)
            {
                // the log must never be the reason the feature fails to start
            }
            return report;
        }

        private static OnboardingSelfCheckInput BuildInput()
        {
            // The raw Russian table is the core's private field (see mod/requests/first-year-onboarding.md, item 2); when it cannot be reached, the keys are asked of
            // the language in use and the report says the answer is approximate.
            var texts = new GameStepValidationContext();
            bool exact = texts.CanReadRussian;

            return new OnboardingSelfCheckInput
            {
                Members = new ReflectionProbe(),
                PredicateRefusal = PredicateRefusal,
                Catalog = OnboardingFeature.Catalog,
                HasRussianText = exact ? (Func<string, bool>)texts.HasRussianText : ModL10n.Has,
                RussianIsExact = exact
            };
        }

        /// <summary>Null when the core knows the predicate as the feature's own; otherwise why not (the refusal of the core, or what the registry says of the name).</summary>
        private static string PredicateRefusal(string name)
        {
            string owner = ModConditions.OwnerOf(name);
            if (string.Equals(owner, OnboardingFeature.FeatureId, StringComparison.Ordinal)) return null;

            foreach (RejectedPredicate refused in ModConditions.Rejected)
            {
                if (string.Equals(refused.Type, name, StringComparison.Ordinal)) return "it was refused: " + refused.Reason;
            }
            return owner == null ? "it is not registered" : "it belongs to '" + owner + "'";
        }

        private static void Write(SelfCheckReport report)
        {
            string id = OnboardingFeature.FeatureId;
            if (report.IsOk)
            {
                ModLog.Info(id, report.Summary);
                return;
            }

            if (report.HasErrors) ModLog.Error(id, report.Summary);
            else ModLog.Warn(id, report.Summary);

            foreach (SelfCheckProblem problem in report.Problems)
            {
                if (problem.Severity == SelfCheckSeverity.Error) ModLog.Error(id, "self-check: " + problem);
            }
            foreach (SelfCheckProblem problem in report.Problems)
            {
                if (problem.Severity == SelfCheckSeverity.Warning) ModLog.Warn(id, "self-check: " + problem);
            }
        }

        /// <summary>
        /// The reflection behind <see cref="IGameMemberProbe"/>: the class is looked up by its full name in the game's assembly first (a direct lookup;
        /// <c>AccessTools.TypeByName</c> walks every type of every assembly, which is slow in a Unity game), then among the system types, then everywhere.
        /// The member is looked up with <c>AccessTools</c> (public and private, instance and static, base classes too).
        /// </summary>
        private sealed class ReflectionProbe : IGameMemberProbe
        {
            private const BindingFlags AnyEvent = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.Ordinal);
            private readonly Assembly _game = FindGameAssembly();

            public string Probe(GameMemberSpec member)
            {
                Type type = FindType(member.TypeName);
                if (type == null) return "the class " + member.TypeName + " is not in the game";

                switch (member.Kind)
                {
                    case GameMemberKind.Method:
                        return ProbeMethod(type, member);
                    case GameMemberKind.Field:
                        return ProbeField(type, member);
                    case GameMemberKind.Event:
                        return type.GetEvent(member.Name, AnyEvent) == null ? "no such event" : null;
                    default:
                        return "unknown kind of member";
                }
            }

            private string ProbeMethod(Type type, GameMemberSpec member)
            {
                string[] names = member.ParameterTypes ?? new string[0];
                var parameters = new Type[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    parameters[i] = FindType(names[i]);
                    if (parameters[i] == null) return "the parameter type " + names[i] + " is not in the game";
                }

                MethodInfo method = AccessTools.Method(type, member.Name, parameters);
                if (method == null) return "no such method";
                return TypeProblem(method.ReturnType, member.ValueType, "returns");
            }

            private static string ProbeField(Type type, GameMemberSpec member)
            {
                FieldInfo field = AccessTools.Field(type, member.Name);
                if (field == null) return "no such field";
                return TypeProblem(field.FieldType, member.ValueType, "is of type");
            }

            // The type must match where the tutorial reads the value as it is (null: the existence is enough).
            private static string TypeProblem(Type actual, string expectedName, string what)
            {
                if (expectedName == null || string.Equals(actual.FullName, expectedName, StringComparison.Ordinal)) return null;
                return "it " + what + " " + actual.FullName + ", expected " + expectedName;
            }

            private Type FindType(string name)
            {
                Type type;
                if (_types.TryGetValue(name, out type)) return type;

                type = (_game == null ? null : _game.GetType(name, false)) ?? Type.GetType(name, false) ?? AccessTools.TypeByName(name);
                _types[name] = type;
                return type;
            }

            private static Assembly FindGameAssembly()
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(assembly.GetName().Name, GameAssemblyName, StringComparison.Ordinal)) return assembly;
                }
                return null;
            }
        }
    }
}
