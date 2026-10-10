using System;
using System.Collections.Generic;
using UnityEngine;

namespace PTMod.Core
{
    public static partial class ModNewGame
    {
        /// <summary>
        /// Puts a free section of widgets on the panel "Mods..." of the new-game window (FR-18 item 5).
        /// When the panel is drawn <paramref name="build"/> gets the container of
        /// the section (<c>PTModNewGameSection_&lt;featureId&gt;</c>, a vertical list) and fills it with the widgets of <see cref="ModUI"/> - a button "Trials...", a
        /// block of preview texts. The sections stand in the order of <paramref name="order"/>, equal orders by feature id (AC-18.4); they come below the simple
        /// options. The panel is drawn when it is first opened and again when a feature changes its state or the language changes, so <paramref name="build"/> may
        /// run more than once: keep nothing in the widgets that it cannot make again, set the first texts from <see cref="Preview"/> inside it and let them follow
        /// <see cref="PreviewChanged"/> afterwards (the widgets of a panel that was drawn again are new ones: a handler must not touch a widget that is gone). An exception in
        /// <paramref name="build"/> is counted against the feature and costs only its section, which then shows a line about the error
        /// (<c>core.newgame.sectionError</c>); the other sections and the start are not affected. The section is shown while its feature is Enabled.
        /// Ignored, with a message in the log, for a missing feature id or <paramref name="build"/>. Never throws.
        /// </summary>
        /// <param name="featureId">The feature the section belongs to.</param>
        /// <param name="build">Fills the container of the section; called under the guard of <paramref name="featureId"/>.</param>
        /// <param name="order">Smaller first; 10 and 20 are taken by the sections of the scenarios and of the first-year tutorial.</param>
        public static void RegisterSection(string featureId, Action<RectTransform> build, int order = 100)
        {
            try
            {
                if (string.IsNullOrEmpty(featureId))
                {
                    ModLog.Error(CoreId, "newgame: RegisterSection needs a feature id; the section is not registered");
                    return;
                }
                if (build == null)
                {
                    ModLog.Error(featureId, "newgame: RegisterSection(" + featureId + ", null) is ignored: a section needs a build method");
                    return;
                }

                if (featureId != CoreId && ModFeatures.Find(featureId) == null)
                    ModLog.Warn(featureId, "newgame: '" + featureId + "' is not a known feature; its section will not be shown");

                NewGameSections.Add(featureId, build, order);
                NewGamePanel.RegistryChanged();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "newgame: cannot register a section", ex);
            }
        }
    }

    /// <summary>
    /// The sections of features on the new-game panel (TSK-063). <see cref="NewGameSectionList{TContainer}"/> holds the registrations and their order, this
    /// class draws them: one container per section in the order of <c>order</c> then feature id, each filled under the guard of its feature, so a section that
    /// throws shows a line about the error and the next section is built as if nothing had happened (AC-18.4).
    /// </summary>
    internal static class NewGameSections
    {
        /// <summary>The name of a section's container is this plus the feature id (<c>PTModNewGameSection_first-year-onboarding</c>).</summary>
        internal const string ContainerPrefix = "PTModNewGameSection_";

        private const string CoreId = ModLog.CoreFeatureId;
        private const string BuildSite = "ModNewGame.Section/build";
        private const float SectionSpacing = 6f;

        private static readonly NewGameSectionList<RectTransform> Registry = new NewGameSectionList<RectTransform>();

        internal static int Count
        {
            get { return Registry.Count; }
        }

        internal static void Add(string featureId, Action<RectTransform> build, int order)
        {
            Registry.Add(featureId, build, order);
            ModLog.Debug(featureId, "newgame: section registered (order " + order + ")");
        }

        /// <summary>A section of an Enabled feature exists.</summary>
        internal static bool HasActive()
        {
            return Registry.Any(ModFeatures.IsActive);
        }

        /// <summary>The feature has a section.</summary>
        internal static bool Has(string featureId)
        {
            return Registry.Any(id => string.Equals(id, featureId, StringComparison.Ordinal));
        }

        /// <summary>
        /// Draws the sections of the Enabled features under <paramref name="parent"/>, in order. A section whose container cannot be made is skipped with a
        /// warning; a section whose <c>build</c> throws (or is skipped because its feature stopped) keeps only the error line. Returns how many sections were built.
        /// </summary>
        internal static int BuildAll(Transform parent)
        {
            return BuildAll(featureId => MakeContainer(parent, featureId), ShowError);
        }

        /// <summary>
        /// The loop of <see cref="BuildAll(Transform)"/> with the two things that need the engine passed in: <paramref name="makeContainer"/> (the container of the
        /// section of a feature, null when it cannot be made) and <paramref name="showError"/> (what a section that failed shows). The tests of the order and of
        /// the isolation (AC-18.4) give it stand-ins.
        /// </summary>
        internal static int BuildAll(Func<string, RectTransform> makeContainer, Action<RectTransform, string> showError)
        {
            int built = 0;
            foreach (NewGameSectionList<RectTransform>.Entry entry in Registry.Ordered(ModFeatures.IsActive))
            {
                RectTransform box = makeContainer(entry.FeatureId);
                if (ReferenceEquals(box, null))   // made or not made: a plain reference check is enough
                {
                    ModLog.Warn(entry.FeatureId, "newgame: no container could be made for the section of '" + entry.FeatureId + "'");
                    continue;
                }

                NewGameSectionList<RectTransform>.Entry current = entry;
                if (ModHooks.Guard(entry.FeatureId, BuildSite, () => current.Build(box)))
                {
                    built++;
                    continue;
                }

                showError(box, entry.FeatureId);
            }
            return built;
        }

        /// <summary>The name of a feature for the player (the hub's), the feature id when it has none.</summary>
        internal static string TitleOf(string featureId)
        {
            FeatureInfo info = ModFeatures.Find(featureId);
            if (info != null && !string.IsNullOrEmpty(info.NameKey)) return ModUI.TextOfKey(info.NameKey);
            return featureId;
        }

        private static RectTransform MakeContainer(Transform parent, string featureId)
        {
            RectTransform box = ModUI.VerticalList(parent, SectionSpacing);
            if (box != null) box.gameObject.name = ContainerPrefix + featureId;
            return box;
        }

        // What the failed build made is thrown away (it may be half done), and one line says what happened. The log has the exception already (ModHooks.Guard).
        private static void ShowError(RectTransform box, string featureId)
        {
            try
            {
                NewGamePanel.ClearChildren(box);
                ModUI.Text(box, ModL10n.L("core.newgame.sectionError", TitleOf(featureId)));
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "newgame: cannot show the error line of the section of '" + featureId + "'", ex);
            }
        }
    }

    /// <summary>
    /// The registrations of the sections and their order (FR-18 item 5, AC-18.4): by <c>order</c>, equal orders by feature id (ordinal), the same feature and
    /// order by the moment of registration. Generic in the container so that it knows nothing of Unity or the game.
    /// </summary>
    internal sealed class NewGameSectionList<TContainer>
    {
        private readonly List<Entry> _entries = new List<Entry>();

        internal int Count
        {
            get { return _entries.Count; }
        }

        internal Entry Add(string featureId, Action<TContainer> build, int order)
        {
            var entry = new Entry(featureId, order, _entries.Count, build);
            _entries.Add(entry);
            return entry;
        }

        /// <summary>Some section's feature satisfies <paramref name="keep"/>.</summary>
        internal bool Any(Func<string, bool> keep)
        {
            foreach (Entry entry in _entries)
            {
                if (keep(entry.FeatureId)) return true;
            }
            return false;
        }

        /// <summary>The sections whose feature satisfies <paramref name="keep"/> (all when it is null), in the order they are shown.</summary>
        internal List<Entry> Ordered(Func<string, bool> keep)
        {
            var list = new List<Entry>();
            foreach (Entry entry in _entries)
            {
                if (keep == null || keep(entry.FeatureId)) list.Add(entry);
            }
            list.Sort(Compare);
            return list;
        }

        internal static int Compare(Entry a, Entry b)
        {
            int byOrder = a.Order.CompareTo(b.Order);
            if (byOrder != 0) return byOrder;

            int byFeature = string.CompareOrdinal(a.FeatureId, b.FeatureId);
            return byFeature != 0 ? byFeature : a.Sequence.CompareTo(b.Sequence);
        }

        /// <summary>One registered section.</summary>
        internal sealed class Entry
        {
            internal Entry(string featureId, int order, int sequence, Action<TContainer> build)
            {
                FeatureId = featureId;
                Order = order;
                Sequence = sequence;
                Build = build;
            }

            internal string FeatureId { get; }

            internal int Order { get; }

            /// <summary>The place in the order of registration: the last tie-break, so the order is the same on every run.</summary>
            internal int Sequence { get; }

            internal Action<TContainer> Build { get; }
        }
    }
}
