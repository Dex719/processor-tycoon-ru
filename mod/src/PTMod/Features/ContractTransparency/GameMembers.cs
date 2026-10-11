using System;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The one door to the private members of the game's classes (design.md, "Самопроверка на старте"; FR-12 item 3; TSK-020): <c>ContractInspector</c>, <c>ContractWindowAugmenter</c> and
    /// <c>BusinessSignGate</c> look their fields and methods up here, not with <c>AccessTools</c> directly. A member is found only when it exists with the expected type, and not when the debug flag
    /// of <see cref="MissingMemberFlags"/> says it is gone, so the part of the feature that depends on it behaves exactly as in a game that lacks the member (the test of "the absence of a member
    /// switches off only the dependent part") while the rest goes on. The keys are those of <see cref="MemberCatalog"/>, which <c>ContractSelfCheck</c> checks at the start. Never throws.
    /// </summary>
    internal static class GameMembers
    {
        /// <summary>True when the debug flag hides the member (always false in a release build).</summary>
        internal static bool IsHidden(string key)
        {
            return MissingMemberFlags.IsHidden(key, ModSettings.Debug);
        }

        /// <summary>The text to add to a line of the log that says a member is missing: names the flag when it is the flag that hides it.</summary>
        internal static string HiddenNote(string key)
        {
            return IsHidden(key) ? " [hidden by the debug flag " + MissingMemberFlags.FlagOf(key) + "]" : string.Empty;
        }

        /// <summary>The field, or null when it is missing, hidden by the flag, or not of the expected type (<paramref name="expected"/> null: any).</summary>
        internal static FieldInfo Field(string key, Type declaring, string name, Type expected)
        {
            if (IsHidden(key)) return null;

            FieldInfo field = AccessTools.Field(declaring, name);
            if (field == null) return null;
            return expected == null || field.FieldType == expected ? field : null;
        }

        /// <summary>The method by name and the exact types of its parameters (<c>new Type[0]</c> for none), or null.</summary>
        internal static MethodInfo Method(string key, Type declaring, string name, Type[] parameters)
        {
            if (IsHidden(key)) return null;
            return AccessTools.Method(declaring, name, parameters);
        }

        /// <summary>The nested type, or null.</summary>
        internal static Type Inner(string key, Type declaring, string name)
        {
            if (IsHidden(key)) return null;
            return AccessTools.Inner(declaring, name);
        }

        /// <summary>The getter of a property, or null when it is missing, hidden or does not return <paramref name="expectedReturn"/>.</summary>
        internal static MethodInfo Getter(string key, Type declaring, string name, Type expectedReturn)
        {
            if (IsHidden(key)) return null;

            MethodInfo getter = AccessTools.PropertyGetter(declaring, name);
            if (getter == null) return null;
            return expectedReturn == null || getter.ReturnType == expectedReturn ? getter : null;
        }
    }
}
