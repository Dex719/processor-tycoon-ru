using System;
using System.Collections.Generic;
using System.Reflection;
using PTMod.Core;
using CurrentResearchUI = ProcessorTycoon.ResearchSystem.CurrentResearchUI;
using ResearchTreeWindow = ProcessorTycoon.ResearchSystem.ResearchTreeWindow;
using Technology = ProcessorTycoon.ResearchSystem.Technology;
using UnityObject = UnityEngine.Object;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The panel "current research" of the game (<c>CurrentResearchUI</c>, part <c>LabUi</c> of the self-check; design.md Component 4). The panel holds the
    /// technology the player chose in its private <c>targetTechnology</c> and, as long as it holds one, <c>FixedUpdate</c> writes the position of its own
    /// slider into <c>ResearchSector.Funding</c> every frame - it would fight the slider of the lab block and keep showing a technology the architecture
    /// has taken out of the lab. So when an architecture takes the player's lab the stale panel is emptied and hidden (<see cref="HideStalePanel"/>:
    /// <c>targetTechnology = null</c> and the private <c>Hide</c>), and when the lab goes back to a technology the panel gets it again
    /// (<see cref="SyncFunding"/> with the optional private <c>LoadFunding</c>, then the public <c>SetTechnology</c>) and the research tree redraws its
    /// buttons (<see cref="RefreshTree"/>: the public <c>ResearchTreeWindow.ForceUIUpdate</c>).
    /// <para>
    /// The private members are found by name with reflection once and kept. Every method here is safe to call at any time: it does nothing and answers false
    /// when the panel is not in the scene (the main menu) or a member is missing, and an exception is written to the log once per call kind and never
    /// thrown (this is a best-effort sync of the game's own UI, so a failure here is not counted towards the circuit breaker of the feature). The calls of
    /// the game's public members sit in methods of their own so that a game version without one costs that call, not the caller.
    /// </para>
    /// </summary>
    internal static class LabUiSync
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;

        private static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);
        private static bool _resolved;
        private static FieldInfo _targetTechnology;
        private static MethodInfo _hide;
        private static MethodInfo _loadFunding;
        private static CurrentResearchUI _panel;
        private static ResearchTreeWindow _tree;

        /// <summary>The private members of the panel exist (<c>targetTechnology</c> and <c>Hide</c>): the technology of the player can be taken out of the lab.</summary>
        public static bool HasMembers
        {
            get
            {
                Resolve();
                return _targetTechnology != null && _hide != null;
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                Type type = typeof(CurrentResearchUI);
                _targetTechnology = type.GetField("targetTechnology", Private);
                if (_targetTechnology != null && !typeof(Technology).IsAssignableFrom(_targetTechnology.FieldType)) _targetTechnology = null;
                _hide = type.GetMethod("Hide", Private, null, Type.EmptyTypes, null);
                _loadFunding = type.GetMethod("LoadFunding", Private, null, Type.EmptyTypes, null);
            }
            catch (Exception ex)
            {
                Failed("resolve", ex);
            }
        }

        private static void Failed(string what, Exception ex)
        {
            if (Warned.Add(what)) ModLog.Warn(ArchitecturesFeature.FeatureId, "lab ui: " + what + " failed (" + ex.GetType().Name + ": " + ex.Message + "); the panel of the current research may be out of date");
        }

        /// <summary>The panel of the scene, found again when the scene changed; null in the main menu.</summary>
        private static CurrentResearchUI Panel()
        {
            if (_panel == null) _panel = UnityObject.FindFirstObjectByType<CurrentResearchUI>(UnityEngine.FindObjectsInactive.Include);
            return _panel;
        }

        /// <summary>A new scene: the panels and windows of the old one are gone.</summary>
        public static void Forget()
        {
            _panel = null;
            _tree = null;
        }

        // ---- the lab goes to the architecture ----

        /// <summary>
        /// Empties the panel and hides it (<c>targetTechnology = null</c>, <c>Hide()</c>), so that it neither shows the technology that left the lab
        /// nor writes <c>ResearchSector.Funding</c> any more. False when the members or the panel are not there.
        /// </summary>
        public static bool HideStalePanel()
        {
            try
            {
                if (!HasMembers) return false;
                CurrentResearchUI panel = Panel();
                if (panel == null) return false;

                _targetTechnology.SetValue(panel, null);
                _hide.Invoke(panel, null);
                return true;
            }
            catch (Exception ex)
            {
                Failed("hide", ex);
                return false;
            }
        }

        // ---- the lab goes back to a technology ----

        /// <summary>
        /// The slider of the panel is put on <c>ResearchSector.Funding</c> (the private <c>LoadFunding</c>, optional): the funding the player set in the lab
        /// block must survive the technology coming back, as the panel writes its slider into the funding every frame. Call before <see cref="ShowTechnology"/>.
        /// </summary>
        public static bool SyncFunding()
        {
            try
            {
                Resolve();
                if (_loadFunding == null) return false;
                CurrentResearchUI panel = Panel();
                if (panel == null) return false;

                _loadFunding.Invoke(panel, null);
                return true;
            }
            catch (Exception ex)
            {
                Failed("syncFunding", ex);
                return false;
            }
        }

        /// <summary>The panel shows the technology the lab went back to (the public <c>CurrentResearchUI.SetTechnology</c>, as the buttons of the tree call it).</summary>
        public static bool ShowTechnology(Technology technology)
        {
            if (technology == null) return false;
            try
            {
                return SetTechnologyOnPanel(technology);
            }
            catch (Exception ex)
            {
                Failed("showTechnology", ex);
                return false;
            }
        }

        private static bool SetTechnologyOnPanel(Technology technology)
        {
            CurrentResearchUI panel = Panel();
            if (panel == null) return false;
            panel.SetTechnology(technology);
            return true;
        }

        /// <summary>The research tree redraws the states of its buttons now (the public <c>ResearchTreeWindow.ForceUIUpdate</c>) instead of at the next day.</summary>
        public static void RefreshTree()
        {
            try
            {
                if (_tree == null) _tree = UnityObject.FindFirstObjectByType<ResearchTreeWindow>(UnityEngine.FindObjectsInactive.Include);
                if (_tree != null) ForceTreeUpdate(_tree);
            }
            catch (Exception ex)
            {
                Failed("refreshTree", ex);
            }
        }

        private static void ForceTreeUpdate(ResearchTreeWindow tree)
        {
            tree.ForceUIUpdate();
        }
    }
}
