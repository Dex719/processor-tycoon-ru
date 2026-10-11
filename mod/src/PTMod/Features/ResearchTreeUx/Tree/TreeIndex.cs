using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using GameCurrentResearchUi = ProcessorTycoon.ResearchSystem.CurrentResearchUI;
using GamePlayer = ProcessorTycoon.Player;
using GameResearchButton = ProcessorTycoon.ResearchSystem.ResearchButton;
using GameResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using GameTechnology = ProcessorTycoon.ResearchSystem.Technology;
using GameTreeWindow = ProcessorTycoon.ResearchSystem.ResearchTreeWindow;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using UiImage = UnityEngine.UI.Image;
using UnityGameObject = UnityEngine.GameObject;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityTransform = UnityEngine.Transform;
using UnityVector3 = UnityEngine.Vector3;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A picture of the buttons and the dependency lines of the game's research tree, taken right after <c>ResearchTreeWindow.InitializeUI</c>
    /// (design: Component 2): the private lists of the window read with <c>AccessTools</c>, the button of each technology and the match of
    /// every line to the pair of technologies it connects. The tab filter and the focus work from it; nothing here changes the game.
    /// <para>
    /// <b>Matching the lines.</b> The game does not remember which pair a line belongs to, so <see cref="LineMatcher"/> finds the pair by
    /// geometry: the middle of the line is the middle of the segment between the two buttons, up to one constant shift (the lines live
    /// under another object, <c>linesParent</c>; the game also moves the entry and exit nodes of the buttons after it has drawn the lines,
    /// so the buttons themselves are the reliable reference points, and <see cref="LineMatcher.MatchAuto"/> finds the offset between the
    /// two by itself). All points are taken in the coordinate system of the tree content (<c>cpuTab</c>). A line that cannot be matched
    /// keeps its place in <see cref="UnmatchedLineIndices"/> and is left visible in every tab (NFR-2).
    /// </para>
    /// </summary>
    internal sealed class TreeIndex
    {
        private static FieldInfo _buttonsField;
        private static FieldInfo _linesField;
        private static FieldInfo _cpuTabField;
        private static FieldInfo _currentResearchField;
        private static bool _fieldsResolved;

        private readonly List<GameResearchButton> _buttons;
        private readonly List<UiImage> _lines;
        private readonly Dictionary<int, GameResearchButton> _buttonById = new Dictionary<int, GameResearchButton>();

        private IReadOnlyList<LineLink> _lineLinks = new LineLink[0];
        private IReadOnlyList<int> _unmatchedLines = new int[0];

        private TreeIndex(GameTreeWindow window, List<GameResearchButton> buttons, List<UiImage> lines, UnityRectTransform content,
            GameCurrentResearchUi currentResearchUi)
        {
            Window = window;
            _buttons = buttons;
            _lines = lines;
            TreeContent = content;
            CurrentResearchUi = currentResearchUi;
        }

        /// <summary>The window the picture was taken from.</summary>
        public GameTreeWindow Window { get; private set; }

        /// <summary>The buttons of the window, in the order of the game (<c>ResearchTreeWindow.researchButtons</c>; the live list, do not change it).</summary>
        public IReadOnlyList<GameResearchButton> Buttons
        {
            get { return _buttons; }
        }

        /// <summary>The dependency lines of the window, in the order of the game (<c>dependencyLines</c>; the live list, do not change it).</summary>
        public IReadOnlyList<UiImage> Lines
        {
            get { return _lines; }
        }

        /// <summary>The area the tree lives in (<c>cpuTab</c>), or null when the window has none: the coordinate system of the matching and of the view.</summary>
        public UnityRectTransform TreeContent { get; private set; }

        /// <summary>The panel of the current research (<c>currentResearchUI</c>), or null.</summary>
        public GameCurrentResearchUi CurrentResearchUi { get; private set; }

        /// <summary>The lines matched to their pair of technologies (<c>FromTechId</c> is the dependency, <c>ToTechId</c> the technology), by line index.</summary>
        public IReadOnlyList<LineLink> LineLinks
        {
            get { return _lineLinks; }
        }

        /// <summary>The indices (in <see cref="Lines"/>) of the lines no pair was found for: they stay visible in every tab.</summary>
        public IReadOnlyList<int> UnmatchedLineIndices
        {
            get { return _unmatchedLines; }
        }

        /// <summary>How many dependency pairs had no line (a line that is not in the window, e.g. destroyed).</summary>
        public int UnmatchedPairCount { get; private set; }

        /// <summary>The offset the matcher found between the plane of the buttons and the plane of the lines (0 when they agree).</summary>
        public float LineShiftX { get; private set; }

        public float LineShiftY { get; private set; }

        /// <summary>The button of the technology with this <c>Technology.ID</c>, or null.</summary>
        public GameResearchButton ButtonOf(int technologyId)
        {
            GameResearchButton button;
            return _buttonById.TryGetValue(technologyId, out button) ? button : null;
        }

        /// <summary>True when the window has built its buttons (<c>InitializeUI</c> ran): the lists are filled only then.</summary>
        public static bool IsBuilt(GameTreeWindow window)
        {
            if (window == null) return false;
            ResolveFields();
            var buttons = Read<List<GameResearchButton>>(_buttonsField, window);
            return buttons != null && buttons.Count > 0;
        }

        /// <summary>
        /// Takes the picture. Null when the window is null or the private lists cannot be read (the self-check has switched the tabs off then).
        /// Call it after <c>InitializeUI</c>; it only reads, but it matches every line against every pair, so once per scene is the intended use.
        /// </summary>
        public static TreeIndex Capture(GameTreeWindow window)
        {
            if (window == null) return null;

            ResolveFields();
            var buttons = Read<List<GameResearchButton>>(_buttonsField, window);
            var lines = Read<List<UiImage>>(_linesField, window);
            if (buttons == null || lines == null) return null;

            UnityGameObject cpuTab = Read<UnityGameObject>(_cpuTabField, window);
            UnityRectTransform content = cpuTab == null ? null : cpuTab.GetComponent<UnityRectTransform>();
            var currentResearchUi = Read<GameCurrentResearchUi>(_currentResearchField, window);

            var index = new TreeIndex(window, buttons, lines, content, currentResearchUi);
            foreach (GameResearchButton button in buttons)
            {
                if (button == null || button.TargetTechnology == null) continue;
                int id = button.TargetTechnology.ID;
                if (!index._buttonById.ContainsKey(id)) index._buttonById[id] = button;
            }

            index.MatchLines();
            return index;
        }

        private void MatchLines()
        {
            UnityTransform space = TreeContent != null ? (UnityTransform)TreeContent : Window.transform;
            GameResearchSector sector = PlayerSector();

            var pairs = new List<DependencyPair>();
            foreach (GameResearchButton button in _buttons)
            {
                if (button == null || button.TargetTechnology == null) continue;
                GameTechnology technology = button.TargetTechnology;

                float toX, toY;
                CenterIn(space, button.transform, out toX, out toY);
                foreach (GameTechnology dependency in DependenciesOf(sector, technology))
                {
                    GameResearchButton from;
                    if (dependency == null || !_buttonById.TryGetValue(dependency.ID, out from)) continue;

                    float fromX, fromY;
                    CenterIn(space, from.transform, out fromX, out fromY);
                    pairs.Add(new DependencyPair(dependency.ID, technology.ID, fromX, fromY, toX, toY));
                }
            }

            var geometry = new List<LineGeom>(_lines.Count);
            for (int i = 0; i < _lines.Count; i++)
            {
                UiImage line = _lines[i];
                if (line == null) continue;   // a destroyed line has no place to match
                float x, y;
                CenterIn(space, line.transform, out x, out y);
                geometry.Add(new LineGeom(i, x, y));
            }

            LineMatchResult result = LineMatcher.MatchAuto(pairs, geometry, LineMatcher.DefaultTolerance);
            _lineLinks = result.Links;
            _unmatchedLines = result.UnmatchedLineIndices;
            UnmatchedPairCount = result.UnmatchedPairs.Count;
            LineShiftX = result.ShiftX;
            LineShiftY = result.ShiftY;
        }

        // The centre of an element, in the coordinate system of "space". The pivot of the rect is not the centre of a button, so the centre of the rect is used.
        private static void CenterIn(UnityTransform space, UnityTransform element, out float x, out float y)
        {
            var rect = element as UnityRectTransform;
            UnityVector3 world = rect != null ? rect.TransformPoint(rect.rect.center) : element.position;
            UnityVector3 local = space.InverseTransformPoint(world);
            x = local.x;
            y = local.y;
        }

        private static IEnumerable<GameTechnology> DependenciesOf(GameResearchSector sector, GameTechnology technology)
        {
            if (sector != null)
            {
                try
                {
                    List<GameTechnology> fromSector = sector.GetDependencies(technology);
                    if (fromSector != null) return fromSector;
                }
                catch (Exception)
                {
                    // The sector knows nothing about this technology: the dependencies of the technology itself are the same data.
                }
            }
            return technology.Dependencies ?? new List<GameTechnology>();
        }

        private static GameResearchSector PlayerSector()
        {
            try
            {
                GamePlayer player = GamePlayer.Instance;
                if (player == null) return null;
                ICompany company = player.Company;
                return company == null ? null : company.ResearchSector;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static T Read<T>(FieldInfo field, object instance) where T : class
        {
            if (field == null) return null;
            try
            {
                return field.GetValue(instance) as T;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void ResolveFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;
            Type window = typeof(GameTreeWindow);
            _buttonsField = AccessTools.Field(window, "researchButtons");
            _linesField = AccessTools.Field(window, "dependencyLines");
            _cpuTabField = AccessTools.Field(window, "cpuTab");
            _currentResearchField = AccessTools.Field(window, "currentResearchUI");
        }
    }
}
