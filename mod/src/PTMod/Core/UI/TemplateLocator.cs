using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PTMod.Core
{
    /// <summary>How <see cref="TemplateLocator"/> found a template.</summary>
    public enum TemplateHow
    {
        /// <summary>Not found.</summary>
        None = 0,

        /// <summary>By the exact path of the table (the normal case; the path comes from <c>tools/dump_ui_templates.py</c>).</summary>
        Path = 1,

        /// <summary>By names - the window first, then the element inside it (the path in the table is stale: another game version moved the object).</summary>
        Names = 2
    }

    /// <summary>What <see cref="TemplateLocator.Locate"/> found for one table row.</summary>
    internal sealed class LocateResult
    {
        public GameObject Target;
        public TemplateHow How;

        /// <summary>How many objects matched the names (1 for a path hit; more than 1 means the search was ambiguous and the first in hierarchy order was taken).</summary>
        public int Candidates;

        /// <summary>The path of <see cref="Target"/> in the format of the table (<c>Root/Child/Leaf</c>).</summary>
        public string ActualPath = "";

        /// <summary>Components of <see cref="UiTemplate.Expected"/> that the template's subtree does not have.</summary>
        public List<string> MissingComponents = new List<string>();
    }

    /// <summary>
    /// Finds the templates of <see cref="ModUITemplates"/> in a loaded scene (FR-16 item 1). Two steps, both over the scene's own objects and
    /// including the inactive ones (the game hides its windows a tenth of a second after the scene starts, and <c>GameObject.Find</c> would not see them):
    /// <list type="number">
    /// <item><b>Path.</b> The row's path (<c>MainCanvas/BreakContractWindow</c>) is resolved from the scene's root objects: one <c>Transform.Find</c>, no search.
    /// The paths are printed by <c>tools/dump_ui_templates.py</c> from <c>data.unity3d</c> and checked against the table by <c>--check</c>.</item>
    /// <item><b>Names.</b> If the path leads nowhere (another game version moved the object), the window is searched by name over the whole scene,
    /// then the element by name inside the window; names such as <c>DifficultyDropdown</c> repeat in a scene, so the element is never searched outside its window.
    /// Several candidates are counted and the first in hierarchy order is taken.</item>
    /// </list>
    /// The path format is the one of <c>ptru.game.Hierarchy.path</c>: object names from the root joined by "/", with the names exactly as the game
    /// has them (a name may hold spaces and an arrow, <c>DESKTOP -&gt; Bottom</c>; none holds a slash). Everything here runs on the main thread and never throws
    /// for a missing object; the callers log.
    /// </summary>
    internal static class TemplateLocator
    {
        /// <summary>Separator of the names in a path.</summary>
        internal const char Separator = '/';

        /// <summary>Finds the template of one table row in the scene; <see cref="LocateResult.Target"/> is null when there is none.</summary>
        internal static LocateResult Locate(Scene scene, UiTemplate spec)
        {
            var result = new LocateResult();
            GameObject[] roots = scene.GetRootGameObjects();
            string wanted = spec.Element.Length > 0 ? spec.Element : spec.Window;
            if (wanted.Length == 0) return result;

            GameObject byPath = FindByPath(roots, spec.Path);
            if (byPath != null && string.Equals(byPath.name, wanted, StringComparison.Ordinal))
            {
                result.Target = byPath;
                result.How = TemplateHow.Path;
                result.Candidates = 1;
                result.ActualPath = spec.Path;
                result.MissingComponents = MissingComponents(byPath, spec.Expected);
                return result;
            }

            Transform byNames = FindByNames(roots, spec, out result.Candidates);
            if (byNames == null) return result;

            result.Target = byNames.gameObject;
            result.How = TemplateHow.Names;
            result.ActualPath = PathOf(byNames);
            result.MissingComponents = MissingComponents(result.Target, spec.Expected);
            return result;
        }

        /// <summary>The object at the path from one of the roots, or null. Inactive objects are found.</summary>
        internal static GameObject FindByPath(GameObject[] roots, string path)
        {
            string[] parts = SplitPath(path);
            if (parts.Length == 0) return null;

            string rest = parts.Length > 1 ? string.Join(Separator.ToString(), parts, 1, parts.Length - 1) : null;
            foreach (GameObject root in roots)
            {
                if (root == null || !string.Equals(root.name, parts[0], StringComparison.Ordinal)) continue;
                if (rest == null) return root;

                Transform found = root.transform.Find(rest);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        /// <summary>The window by name, then the element inside it by name; with no window name the element is searched in the whole scene.</summary>
        private static Transform FindByNames(GameObject[] roots, UiTemplate spec, out int candidates)
        {
            var found = new List<Transform>();
            if (spec.Window.Length > 0)
            {
                var windows = new List<Transform>();
                foreach (GameObject root in roots) CollectNamed(root.transform, spec.Window, true, windows);

                if (spec.Element.Length == 0) found = windows;
                else foreach (Transform window in windows) CollectNamed(window, spec.Element, false, found);
            }
            else
            {
                foreach (GameObject root in roots) CollectNamed(root.transform, spec.Element, true, found);
            }

            candidates = found.Count;
            return found.Count == 0 ? null : found[0];
        }

        /// <summary>Depth-first, in hierarchy order (the same order the tool prints candidates in). Skips an object already in the list.</summary>
        private static void CollectNamed(Transform node, string name, bool includeSelf, List<Transform> into)
        {
            if (includeSelf && string.Equals(node.name, name, StringComparison.Ordinal) && !into.Contains(node)) into.Add(node);

            int count = node.childCount;
            for (int i = 0; i < count; i++) CollectNamed(node.GetChild(i), name, true, into);
        }

        /// <summary>The path of an object in the format of the table: names from the root, joined by "/".</summary>
        internal static string PathOf(Transform node)
        {
            var names = new List<string>();
            for (Transform t = node; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join(Separator.ToString(), names.ToArray());
        }

        /// <summary>Splits a path into names; an empty or null path gives no names, empty segments are dropped.</summary>
        internal static string[] SplitPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return new string[0];
            return path.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Names of <paramref name="expected"/> components that nothing in the subtree of <paramref name="target"/> (inactive children included) has.</summary>
        internal static List<string> MissingComponents(GameObject target, IReadOnlyList<string> expected)
        {
            var missing = new List<string>();
            if (target == null || expected == null || expected.Count == 0) return missing;

            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (Component component in target.GetComponentsInChildren<Component>(true))
            {
                if (component != null) present.Add(component.GetType().Name);   // a null entry is a script the game lost
            }
            foreach (string name in expected)
            {
                if (!present.Contains(name)) missing.Add(name);
            }
            return missing;
        }

        /// <summary>The path of a template for the log: "(path)" and, when the object moved, where it really is.</summary>
        internal static string Describe(UiTemplate spec, LocateResult found)
        {
            var text = new StringBuilder(spec.Path);
            if (found != null && found.How == TemplateHow.Names) text.Append(" -> ").Append(found.ActualPath);
            return text.ToString();
        }
    }
}
