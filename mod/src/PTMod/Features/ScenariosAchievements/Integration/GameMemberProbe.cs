using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Features.ScenariosAchievements.Logic;

namespace PTMod.Features.ScenariosAchievements.Integration
{
    /// <summary>
    /// Looks for the members of the game that the feature reaches by name (<see cref="GameMemberSpec"/>; TSK-029): by reflection and by the full name of the class, so a class or a
    /// field that a game update renamed is a finding in the log and not a crash. The class is looked up in the game's assembly first (a direct lookup; <c>AccessTools.TypeByName</c>
    /// walks every type of every assembly, which is slow in a Unity game), then among the system types, then everywhere. A member is looked up the way <c>AccessTools</c> does (public and
    /// private, instance and static, base classes too), which is how the feature itself reads the private fields. Reads only; never throws.
    /// </summary>
    internal sealed class GameMemberProbe
    {
        private const string GameAssemblyName = "Assembly-CSharp";
        private const BindingFlags AnyEvent = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly Assembly _game = FindGameAssembly();

        /// <summary>What is wrong with the member (English), or null when it is there as expected.</summary>
        internal string Probe(GameMemberSpec spec)
        {
            try
            {
                Type type = FindType(spec.TypeName);
                if (type == null) return "the class " + spec.TypeName + " is not in the game";

                switch (spec.Kind)
                {
                    case ProbeKind.Field:
                        return ProbeField(type, spec);
                    case ProbeKind.Method:
                        return ProbeMethod(type, spec);
                    case ProbeKind.Event:
                        return type.GetEvent(spec.Name, AnyEvent) == null ? "no such event" : null;
                    default:
                        return "unknown kind of member";
                }
            }
            catch (Exception ex)
            {
                return "the lookup failed (" + ex.GetType().Name + ": " + ex.Message + ")";
            }
        }

        private static string ProbeField(Type type, GameMemberSpec spec)
        {
            FieldInfo field = AccessTools.Field(type, spec.Name);
            if (field == null) return "no such field";
            if (spec.ValueType == null || string.Equals(field.FieldType.FullName, spec.ValueType, StringComparison.Ordinal)) return null;
            return "it is of type " + field.FieldType.FullName + ", expected " + spec.ValueType;
        }

        private string ProbeMethod(Type type, GameMemberSpec spec)
        {
            string[] names = spec.ParameterTypes ?? new string[0];
            var parameters = new Type[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                parameters[i] = FindType(names[i]);
                if (parameters[i] == null) return "the parameter type " + names[i] + " is not in the game";
            }

            MethodInfo method = AccessTools.Method(type, spec.Name, parameters);
            return method == null ? "no such method" : null;
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
