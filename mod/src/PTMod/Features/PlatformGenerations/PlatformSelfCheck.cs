using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The self-check of the feature (TSK-020; design.md, "Самопроверка на старте"; NFR-2). The core's own self-check has no slot for the
    /// members of a single feature, so this class looks by itself, once per session when the feature starts: every member of the game that the
    /// feature reads, calls or patches (<see cref="PlatformSelfCheckTable.Members"/>: <c>CustomSocket.CustomHardwareID</c>, <c>Cpu.Memory</c>,
    /// <c>Memory.Name</c>, <c>CpuBase.Market</c>, <c>DateController.MonthsSinceStart</c>, <c>ContractManager.OnNewContracts</c>, the target of the
    /// patch <c>ProjectReleaseWindow.Open</c> ...), and the hooks of the core the feature stands on - the one patch is applied and is not reserved,
    /// <c>Company.AddCpu</c> and <c>AICompany.AddCpu</c> are reserved by the core (so <c>ModEvents.CpuReleased</c> comes and the feature need not
    /// patch them), the handlers of <c>CpuReleased</c> and <c>CpuTransferred</c> are subscribed, both providers are registered on channels that are
    /// declared. The members are found by name with reflection, never by the shape the code was compiled against, so a game version that moved
    /// one costs a line in <c>Player.log</c>, not an exception. It only LOOKS: nothing is changed, the feature goes on whatever it finds - the adapter
    /// answers "unknown" for what is missing.
    /// </summary>
    internal static class PlatformSelfCheck
    {
        private const string Feature = PlatformGenerationsFeature.FeatureId;
        private const BindingFlags AnyMember = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The last result, or null before the first run.</summary>
        public static PlatformSelfCheckBook Last { get; private set; }

        /// <summary>
        /// Looks at everything, writes the summary and the problems to the log (<c>Player.log</c>) and returns the result. Never throws: a check
        /// that cannot be made is a problem of its own, not a failure of the feature.
        /// </summary>
        public static PlatformSelfCheckBook Run(PlatformGenerationsFeature feature)
        {
            var book = new PlatformSelfCheckBook();
            try
            {
                Assembly game = typeof(GameCpu).Assembly;
                IReadOnlyList<GameMemberSpec> members = PlatformSelfCheckTable.Members;
                for (int i = 0; i < members.Count; i++) book.Member(members[i], Look(game, members[i]));

                CheckHooks(book, game, feature);
            }
            catch (Exception ex)
            {
                ModLog.Error(Feature, "self-check: the check itself failed; what it found so far is logged", ex);
                book.Hook("the self-check", ex.GetType().Name + ": " + ex.Message);
            }

            Last = book;
            Write(book);
            return book;
        }

        private static void Write(PlatformSelfCheckBook book)
        {
            if (book.Ok) ModLog.Info(Feature, book.Summary());
            else ModLog.Warn(Feature, book.Summary());

            foreach (string line in book.Problems) ModLog.Warn(Feature, "self-check: " + line);
            foreach (string line in book.Notes) ModLog.Info(Feature, "self-check: note: " + line);
        }

        // ---- the members of the game ----

        /// <summary>Null when the member is there as described, otherwise what is wrong.</summary>
        private static string Look(Assembly game, GameMemberSpec spec)
        {
            try
            {
                Type type = game.GetType(spec.TypeName, false);
                if (type == null) return "the class " + spec.TypeName + " is not in the game";

                switch (spec.Kind)
                {
                    case GameMemberKind.Property:
                        return LookProperty(type, spec);
                    case GameMemberKind.Field:
                        return LookField(type, spec);
                    case GameMemberKind.Event:
                        return type.GetEvent(spec.Name, AnyMember) == null ? "missing" : null;
                    default:
                        return LookMethod(type, spec);
                }
            }
            catch (Exception ex)
            {
                return "cannot be looked at (" + ex.GetType().Name + ")";
            }
        }

        private static string LookProperty(Type type, GameMemberSpec spec)
        {
            PropertyInfo property = FindProperty(type, spec.Name);
            Type valueType;
            if (property != null)
            {
                valueType = property.PropertyType;
            }
            else
            {
                MethodInfo getter = FindMethod(type, "get_" + spec.Name);   // a build in which the member is a plain getter
                if (getter == null) return "missing";
                valueType = getter.ReturnType;
            }
            return CheckValueType(spec, valueType);
        }

        private static string LookField(Type type, GameMemberSpec spec)
        {
            FieldInfo field = type.GetField(spec.Name, AnyMember);
            return field == null ? "missing" : CheckValueType(spec, field.FieldType);
        }

        private static string LookMethod(Type type, GameMemberSpec spec)
        {
            MethodInfo[] candidates = type.GetMethods(AnyMember);
            bool named = false;
            for (int i = 0; i < candidates.Length; i++)
            {
                MethodInfo method = candidates[i];
                if (!string.Equals(method.Name, spec.Name, StringComparison.Ordinal)) continue;
                named = true;
                if (spec.ParameterTypes == null || ParametersMatch(method, spec.ParameterTypes)) return null;
            }

            if (named) return "no overload with the parameters (" + string.Join(", ", spec.ParameterTypes ?? new string[0]) + ")";
            if (type.IsInterface) return LookInterfaceMethod(type, spec);
            return "missing";
        }

        private static string LookInterfaceMethod(Type type, GameMemberSpec spec)
        {
            foreach (Type parent in type.GetInterfaces())
            {
                string problem = LookMethod(parent, spec);
                if (problem == null) return null;
            }
            return "missing";
        }

        private static bool ParametersMatch(MethodInfo method, string[] expected)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != expected.Length) return false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!string.Equals(parameters[i].ParameterType.FullName, expected[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static string CheckValueType(GameMemberSpec spec, Type actual)
        {
            if (spec.ValueType == null || actual == null) return null;
            string name = actual.FullName ?? actual.Name;
            return name.StartsWith(spec.ValueType, StringComparison.Ordinal) ? null : "has the type " + name + " instead of " + spec.ValueType;
        }

        /// <summary>A property of the class, or of an interface it extends (reflection does not look into the base interfaces of an interface).</summary>
        private static PropertyInfo FindProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(name, AnyMember);
            if (property != null || !type.IsInterface) return property;

            foreach (Type parent in type.GetInterfaces())
            {
                property = parent.GetProperty(name, AnyMember);
                if (property != null) return property;
            }
            return null;
        }

        private static MethodInfo FindMethod(Type type, string name)
        {
            MethodInfo method = type.GetMethod(name, AnyMember);
            if (method != null || !type.IsInterface) return method;

            foreach (Type parent in type.GetInterfaces())
            {
                method = parent.GetMethod(name, AnyMember);
                if (method != null) return method;
            }
            return null;
        }

        // ---- the hooks of the core ----

        private static void CheckHooks(PlatformSelfCheckBook book, Assembly game, PlatformGenerationsFeature feature)
        {
            CheckReleasePatch(book, game);
            CheckReservedAddCpu(book, game);

            book.Hook("ModEvents.CpuReleased", SubscriptionProblem("CpuReleased"));
            book.Hook("ModEvents.CpuTransferred", SubscriptionProblem("CpuTransferred"));

            string[] channels = PlatformSelfCheckTable.Channels;
            for (int i = 0; i < channels.Length; i++)
            {
                string channel = channels[i];
                string problem = null;
                if (!ModModifiers.IsDeclared(channel)) problem = "the channel is not declared by the core";
                else if (!ModModifiers.HasAny(channel)) problem = "the provider of the feature is not registered on it";
                book.Hook("provider of " + channel, problem);
            }
            book.Hook("providers of the feature", feature != null && feature.Provider.IsRegistered ? null : "Register did not register them");
        }

        /// <summary>The patch of the release window is declared and applied (an optional patch: the warning is off without it) and the core does not reserve its target.</summary>
        private static void CheckReleasePatch(PlatformSelfCheckBook book, Assembly game)
        {
            string problem = "no such patch is declared";
            IReadOnlyList<PatchReport> report = ModHooks.Report;
            for (int i = 0; i < report.Count; i++)
            {
                PatchReport row = report[i];
                if (row.FeatureId != Feature || !string.Equals(row.Target, PlatformSelfCheckTable.ReleaseWindowTarget, StringComparison.Ordinal)) continue;

                problem = row.Status == PatchStatus.Applied ? null : "the patch is " + row.Status + (row.Detail == null ? string.Empty : " (" + row.Detail + ")");
                break;
            }
            book.Hook("patch " + PlatformSelfCheckTable.ReleaseWindowTarget + "; without it the release window shows no warning", problem, true);

            string reserved = null;
            Type window = game.GetType("ProcessorTycoon.ProjectSystem.ProjectReleaseWindow", false);
            MethodInfo open = window == null ? null : window.GetMethod("Open", AnyMember);
            ReservedTarget info;
            if (open != null && ModHooks.IsReserved(open, out info)) reserved = "the core reserves it (" + info.Describe() + ")";
            book.Hook(PlatformSelfCheckTable.ReleaseWindowTarget + " is free to patch", reserved, true);
        }

        /// <summary><c>Company.AddCpu</c> and <c>AICompany.AddCpu</c> belong to the core (P20): it raises <c>CpuReleased</c> from them and the feature must not patch them.</summary>
        private static void CheckReservedAddCpu(PlatformSelfCheckBook book, Assembly game)
        {
            foreach (string typeName in PlatformSelfCheckTable.ReservedAddCpu)
            {
                string problem = null;
                Type type = game.GetType(typeName, false);
                MethodInfo[] methods = type == null ? new MethodInfo[0] : type.GetMethods(AnyMember);

                bool found = false;
                bool reserved = false;
                for (int i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name != "AddCpu" || methods[i].DeclaringType != type) continue;
                    found = true;
                    ReservedTarget info;
                    if (ModHooks.IsReserved(methods[i], out info)) reserved = true;
                }

                if (!found) problem = "the method is not in the game";
                else if (!reserved) problem = "the core does not reserve it, so ModEvents.CpuReleased may never come";
                book.Hook(typeName.Substring(typeName.LastIndexOf('.') + 1) + ".AddCpu is reserved by the core", problem);
            }
        }

        /// <summary>
        /// The handlers of the feature are on the event of the core. A C# event cannot be looked into from outside, so the backing field of the
        /// event is read; a core that keeps it some other way gives "cannot be looked at" in the log of debugging and no problem.
        /// </summary>
        private static string SubscriptionProblem(string eventName)
        {
            FieldInfo field = typeof(ModEvents).GetField(eventName, AnyMember);
            if (field == null)
            {
                ModLog.Debug(Feature, "self-check: ModEvents." + eventName + " cannot be looked into; the subscription is not verified");
                return null;
            }

            var handlers = field.GetValue(null) as Delegate;
            if (handlers == null) return "nobody listens";

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                if (handler.Target is PlatformReleaseHandlers) return null;
            }
            return "the handler of the feature is not subscribed";
        }
    }
}
