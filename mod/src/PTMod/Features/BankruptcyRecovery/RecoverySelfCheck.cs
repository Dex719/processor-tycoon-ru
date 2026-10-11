using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;
using GamePlayer = ProcessorTycoon.Player;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The self-check of the feature at start (design.md, "Самопроверка на старте"; NFR-2): it looks in the game for every member the feature depends on
    /// (<see cref="RecoveryTargets"/>: the methods it patches, the private members it calls by name, the functions of the asset sale), writes the result to the
    /// log and tells the caller whether the feature can run. The core's own check covers every <c>[ModPatch]</c> target of the registry; this one is about the
    /// rest, and about the pair prefix and postfix on a method the core also holds a transpiler on.
    /// <para>
    /// The members are looked up by the full name of their class, with the types of their parameters, through <c>AccessTools</c>, so a class that was
    /// renamed or removed in a game update is a line in the report and not a crash. The pure part (the list, the report) is
    /// <see cref="TargetCheck"/>'s and has the tests; this class is the reflection behind <see cref="ITargetResolver"/> and the log. In the log:
    /// <c>self-check: 20 of 20 game members found</c>, and a warning line per missing member.
    /// </para>
    /// </summary>
    internal static class RecoverySelfCheck
    {
        /// <summary>The report of the last run; null before the first run.</summary>
        public static TargetReport Last { get; private set; }

        /// <summary>
        /// Runs the check over <see cref="RecoveryTargets.All"/> with the real game, writes the result to the log (one line, and one warning per missing
        /// member) and returns it. Never throws. A report with <see cref="TargetReport.AllFound"/> false means the feature must not run.
        /// </summary>
        public static TargetReport Run()
        {
            return Run(new ReflectionResolver());
        }

        /// <summary>The same with another resolver (the log lines are the same).</summary>
        internal static TargetReport Run(ITargetResolver resolver)
        {
            TargetReport report = TargetCheck.Run(RecoveryTargets.All, resolver);
            Last = report;

            // The targets the design names (the patches P1 to P4 and the members called by name) are listed one by one at the normal level, the rest at the debug level
            // (the verbose log of the settings). What is missing is a warning below.
            foreach (TargetEntry entry in report.Entries)
            {
                if (!entry.Found) continue;
                if (entry.IsPatchOrReflection) ModLog.Info(RecoveryApi.FeatureId, "self-check: " + entry.Line);
                else ModLog.Debug(RecoveryApi.FeatureId, "self-check: " + entry.Line);
            }

            if (report.AllFound && report.OptionalMissing.Count == 0)
            {
                ModLog.Info(RecoveryApi.FeatureId, report.Summary);
                return report;
            }

            ModLog.Warn(RecoveryApi.FeatureId, report.Summary + "; " + report.Missing.Count + " needed, " + report.OptionalMissing.Count + " optional missing");
            foreach (string line in report.Missing) ModLog.Warn(RecoveryApi.FeatureId, "self-check: missing " + line);
            foreach (string line in report.OptionalMissing) ModLog.Warn(RecoveryApi.FeatureId, "self-check: missing (optional) " + line);
            return report;
        }

        /// <summary>
        /// The second half of the check, once the feature runs (<c>OnEnabled</c>: the core has applied the patches by then): are the patches P1 to P4 in the game.
        /// The core's own check reports a patch that failed; this puts the answer for the feature's four into its line of the log. A missing P4 (the button) is the
        /// only one the feature can live without. Never throws.
        /// </summary>
        public static bool VerifyPatches()
        {
            bool applied = ModHooks.IsApplied(typeof(BankruptcyPatches));
            if (applied) ModLog.Info(RecoveryApi.FeatureId, "self-check: the patches P1-P4 are applied");
            else ModLog.Warn(RecoveryApi.FeatureId, "self-check: not every patch of P1-P4 is applied; the Diagnostics tab of the hub names them (P4, the button, is optional)");
            return applied;
        }

        /// <summary>Looks for a member with reflection (<c>AccessTools</c>: public and private, instance and static, also on the base classes).</summary>
        private sealed class ReflectionResolver : ITargetResolver
        {
            // The classes are looked up by name in the game's assembly first (a direct lookup; AccessTools.TypeByName walks every type of every assembly,
            // which is slow in a Unity game), then among the system types, and only then everywhere. Each name once.
            private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.Ordinal);
            private readonly Assembly _game = typeof(GamePlayer).Assembly;

            public string Probe(RecoveryTarget target)
            {
                Type type = FindType(target.TypeName);
                if (type == null) return "the class " + target.TypeName + " is not in the game";

                switch (target.Kind)
                {
                    case TargetKind.Method:
                        return ProbeMethod(type, target);
                    case TargetKind.Field:
                        return ProbeField(type, target);
                    case TargetKind.Property:
                        return ProbeProperty(type, target);
                    default:
                        return "unknown kind of target";
                }
            }

            private Type FindType(string name)
            {
                Type type;
                if (_types.TryGetValue(name, out type)) return type;

                type = _game.GetType(name, false) ?? Type.GetType(name, false) ?? AccessTools.TypeByName(name);
                _types[name] = type;
                return type;
            }

            private string ProbeMethod(Type type, RecoveryTarget target)
            {
                string[] names = target.ParameterTypes ?? new string[0];
                var parameters = new Type[names.Length];
                for (int i = 0; i < names.Length; i++)
                {
                    parameters[i] = FindType(names[i]);
                    if (parameters[i] == null) return "the parameter type " + names[i] + " is not in the game";
                }

                MethodInfo method = AccessTools.Method(type, target.Member, parameters);
                return method == null ? "no such method" : null;
            }

            private static string ProbeField(Type type, RecoveryTarget target)
            {
                FieldInfo field = AccessTools.Field(type, target.Member);
                if (field == null) return "no such field";
                return TypeProblem(field.FieldType, target.ValueType, "field");
            }

            private static string ProbeProperty(Type type, RecoveryTarget target)
            {
                PropertyInfo property = AccessTools.Property(type, target.Member);
                if (property == null) return "no such property";
                return TypeProblem(property.PropertyType, target.ValueType, "property");
            }

            // The type must match where the feature casts or reads the value (null: the existence is enough).
            private static string TypeProblem(Type actual, string expectedName, string what)
            {
                if (expectedName == null || actual.FullName == expectedName) return null;
                return what + " is " + actual.FullName + ", expected " + expectedName;
            }
        }
    }
}
