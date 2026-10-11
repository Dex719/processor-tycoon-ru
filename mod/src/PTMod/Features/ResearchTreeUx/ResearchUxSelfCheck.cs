using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using PTMod.Core;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCurrentResearchUi = ProcessorTycoon.ResearchSystem.CurrentResearchUI;
using GameResearchButton = ProcessorTycoon.ResearchSystem.ResearchButton;
using UiImage = UnityEngine.UI.Image;
using UnityGameObject = UnityEngine.GameObject;
using UnityRectTransform = UnityEngine.RectTransform;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The self-check of the feature at start (TSK-001, NFR-2): it looks in the game for every method the feature patches and every private
    /// field it reads, writes what it found to the log and turns <see cref="UxParts"/> on or off part by part
    /// (<see cref="UxPartsRule"/> holds the rule, so a missing <c>InspectorWindow.currentCpu</c> switches off the comparison and leaves the
    /// tabs alone). The patches of the feature are optional (<c>ModPatch.Optional</c>), so a target that is gone costs one part, not the
    /// feature; a patch the core could not apply counts as a missing member too.
    /// <para>
    /// A member is a method or a field of a game class named <c>Type.member</c>; the types are looked up by their full names, so a renamed
    /// or removed class is a missing member and not a crash. For a field the type is checked as well, because the adapters cast it.
    /// </para>
    /// </summary>
    internal static class ResearchUxSelfCheck
    {
        private const BindingFlags AnyMethod =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        // The short class names of UxPartsRule's members and the full names the game uses.
        private static readonly Dictionary<string, string> GameTypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "ResearchTreeWindow", "ProcessorTycoon.ResearchSystem.ResearchTreeWindow" },
            { "ResearchTreeController", "ProcessorTycoon.ResearchSystem.ResearchTreeController" },
            { "InspectorWindow", "ProcessorTycoon.Desktop.Inspector.InspectorWindow" },
            { "SideWindow", "ProcessorTycoon.Desktop.SideWindow.SideWindow" },
        };

        // The type a field must have, where the feature casts or iterates it (null: the existence is enough).
        private static readonly Dictionary<string, Type> FieldTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            { UxPartsRule.ResearchButtons, typeof(List<GameResearchButton>) },
            { UxPartsRule.DependencyLines, typeof(List<UiImage>) },
            { UxPartsRule.CpuTab, typeof(UnityGameObject) },
            { UxPartsRule.LinesParent, typeof(UnityGameObject) },
            { UxPartsRule.CurrentResearchUi, typeof(GameCurrentResearchUi) },
            { UxPartsRule.ControllerRect, typeof(UnityRectTransform) },
            { UxPartsRule.ControllerScaler, typeof(UnityGameObject) },
            { UxPartsRule.CurrentCpu, typeof(GameCpu) },
            { UxPartsRule.SideRectTransform, typeof(UnityRectTransform) },
            { UxPartsRule.SideVisiblePosition, typeof(float) },
            { UxPartsRule.SideHiddenPosition, typeof(float) },
        };

        // The members that are methods (the rest are fields).
        private static readonly HashSet<string> Methods = new HashSet<string>(StringComparer.Ordinal)
        {
            UxPartsRule.InitializeUi, UxPartsRule.UpdateButtonStates, UxPartsRule.InspectorStart,
        };

        /// <summary>
        /// Runs the check and returns the parts that can run. Writes one line with what was found, one per part that is off, and one with the
        /// result. Never throws: a failure of the check itself leaves every part that needs the game switched off.
        /// </summary>
        public static UxParts Run()
        {
            var found = new Dictionary<string, bool>(StringComparer.Ordinal);
            var details = new Dictionary<string, string>(StringComparer.Ordinal);
            IReadOnlyList<PatchReport> patches = PatchReports();

            foreach (string member in UxPartsRule.AllMembers())
            {
                string detail;
                bool present;
                try
                {
                    present = Probe(member, patches, out detail);
                }
                catch (Exception e)
                {
                    present = false;
                    detail = "check failed: " + e.GetType().Name + ": " + e.Message;
                }
                found[member] = present;
                if (detail != null) details[member] = detail;
            }

            Func<string, bool> isPresent = member => { bool ok; return found.TryGetValue(member, out ok) && ok; };
            UxParts parts = UxPartsRule.Resolve(isPresent);

            Log(found, details, parts, isPresent);
            return parts;
        }

        private static bool Probe(string member, IReadOnlyList<PatchReport> patches, out string detail)
        {
            detail = null;
            int dot = member.IndexOf('.');
            string typeKey = member.Substring(0, dot);
            string name = member.Substring(dot + 1);

            string fullName;
            Type type = GameTypeNames.TryGetValue(typeKey, out fullName) ? AccessTools.TypeByName(fullName) : null;
            if (type == null)
            {
                detail = "the class " + (fullName ?? typeKey) + " is not in the game";
                return false;
            }

            if (Methods.Contains(member))
            {
                if (!HasMethod(type, name))
                {
                    detail = "no such method";
                    return false;
                }

                PatchReport patch = FindPatch(patches, member);
                if (patch != null && (patch.Status == PatchStatus.Failed || patch.Status == PatchStatus.Rejected))
                {
                    detail = "the patch was not applied: " + (patch.Detail ?? patch.Status.ToString());
                    return false;
                }
                return true;
            }

            FieldInfo field = AccessTools.Field(type, name);
            if (field == null)
            {
                detail = "no such field";
                return false;
            }

            Type expected;
            if (FieldTypes.TryGetValue(member, out expected) && !expected.IsAssignableFrom(field.FieldType))
            {
                detail = "the field has the type " + field.FieldType.Name + ", the feature reads it as " + expected.Name;
                return false;
            }
            return true;
        }

        private static bool HasMethod(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                foreach (MethodInfo method in t.GetMethods(AnyMethod))
                {
                    if (string.Equals(method.Name, name, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        private static IReadOnlyList<PatchReport> PatchReports()
        {
            try
            {
                var mine = new List<PatchReport>();
                foreach (PatchReport report in ModHooks.Report)
                {
                    if (string.Equals(report.FeatureId, ResearchTreeUxFeature.FeatureId, StringComparison.Ordinal)) mine.Add(report);
                }
                return mine;
            }
            catch (Exception)
            {
                return new PatchReport[0];
            }
        }

        private static PatchReport FindPatch(IReadOnlyList<PatchReport> patches, string member)
        {
            foreach (PatchReport patch in patches)
            {
                if (string.Equals(patch.Target, member, StringComparison.Ordinal)) return patch;
            }
            return null;
        }

        private static void Log(Dictionary<string, bool> found, Dictionary<string, string> details, UxParts parts, Func<string, bool> isPresent)
        {
            string id = ResearchTreeUxFeature.FeatureId;

            var present = new StringBuilder();
            var absent = new StringBuilder();
            foreach (string member in UxPartsRule.AllMembers())
            {
                StringBuilder target = found[member] ? present : absent;
                if (target.Length > 0) target.Append(", ");
                target.Append(member);
                string detail;
                if (!found[member] && details.TryGetValue(member, out detail)) target.Append(" (").Append(detail).Append(')');
            }
            ModLog.Info(id, "self-check: found " + (present.Length == 0 ? "nothing" : present.ToString()));
            if (absent.Length > 0) ModLog.Warn(id, "self-check: missing " + absent);

            foreach (UxParts part in UxPartsRule.AllParts())
            {
                if ((parts & part) != 0) continue;
                IReadOnlyList<string> missing = UxPartsRule.Missing(part, isPresent);
                ModLog.Warn(id, "self-check: the part " + part + " is off, it needs " + string.Join(", ", missing));
            }

            ModLog.Info(id, "self-check: parts on: " + (parts == UxParts.None ? "none" : parts.ToString()));
        }
    }
}
