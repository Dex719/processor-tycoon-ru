using System;
using System.Collections.Generic;
using PTMod.Core;
using UnityEngine;
using InfoText = ProcessorTycoon.UI.InfoText;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The window "Intel: Rivals" (design.md, Component 11 and "UI"; FR-9, FR-16, FR-17; NFR-5): the list of the AI companies on the left, the details of the
    /// chosen one on the right. A clone of the panel window of the game (<see cref="WindowTemplate.Panel"/>, the time goes on), made by
    /// <c>ModUI.CreateWindow</c>; the contents are made by <see cref="IntelBuilder"/> and drawn with the widgets of <c>ModUI</c>.
    /// <list type="bullet">
    /// <item><b>Opened from</b> the button "Rivals" on the page of the feature in the hub (<see cref="IntelHubPage"/>) and the action "Open intel" of the mails
    /// of the feature (<see cref="IntelMail.OpenIntel"/>, the mail stays open); <see cref="Open"/> is the one entry for both. A company can be chosen at the
    /// opening (the mail names the rival).</item>
    /// <item><b>List.</b> Per company: the name, the attitude with its colour, the character (or "Character unknown" while it is hidden), the segments, the
    /// share, the last CPU. The bankrupt are at the bottom, grey. <b>Details:</b> the character and its description, the attitude and what it does in a
    /// negotiation (a tip on the number), the market numbers, the threats with the range of weeks, the effects in force with their end, the line about the
    /// caution after a flaw, ten lines of the history. No money and no projects of the rivals.</item>
    /// <item><b>Fog.</b> The share and the <c>Ips</c> are rounded when the parameter <c>intelFog</c> is on (<see cref="IntelFog"/>); the rest is exact.</item>
    /// <item><b>Refresh</b> once a game day while open (<see cref="OnDay"/>), when the theme or the language changes, and when the parameters
    /// <c>intelFog</c> and <c>hiddenCharacters</c> change. The scroll positions are kept.</item>
    /// </list>
    /// The window belongs to the scene that made it: after a scene change it is made again at the next opening. Main thread only; every entry is guarded.
    /// </summary>
    internal sealed class IntelWindow
    {
        internal const string WindowId = RivalPersonalitiesFeature.FeatureId + ".intel";

        private const string Feature = RivalPersonalitiesFeature.FeatureId;
        private const float ListWidth = 310f;
        private const float ColumnsHeight = 440f;
        private static readonly Vector2 WindowSize = new Vector2(900f, 560f);

        private readonly RivalPersonalitiesFeature _feature;
        private readonly IntelBuilder _builder;
        private readonly IntelSource _source = new IntelSource();

        private ModWindow _window;
        private RectTransform _list;
        private RectTransform _details;
        private int? _selected;

        public IntelWindow(RivalPersonalitiesFeature feature, IntelBuilder builder)
        {
            _feature = feature;
            _builder = builder;
        }

        /// <summary>The window is on the screen.</summary>
        public bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>The company the details are for (null before the first opening).</summary>
        public int? Selected
        {
            get { return _selected; }
        }

        /// <summary>
        /// Opens the window, on the company <paramref name="selectUniqueId"/> when it is given (the window is made on first use). Does nothing while the
        /// feature is not active. The one entry of the hub button and of the mail action.
        /// </summary>
        public static void Open(int? selectUniqueId = null)
        {
            RivalPersonalitiesFeature feature = RivalPersonalitiesFeature.Instance;
            if (feature == null || !ModFeatures.IsActive(Feature)) return;
            feature.Intel.Show(selectUniqueId);
        }

        /// <summary>Opens the window (<see cref="Open"/> without the lookup of the feature).</summary>
        public void Show(int? selectUniqueId)
        {
            ModHooks.Guard(Feature, "IntelWindow/Open", () =>
            {
                if (!EnsureWindow()) return;
                if (selectUniqueId.HasValue) _selected = selectUniqueId;
                Rebuild(true);
                _window.Open();
            });
        }

        /// <summary>Closes the window when it is open.</summary>
        public void Close()
        {
            if (_window != null && _window.IsAlive) _window.Close();
        }

        /// <summary>Makes the contents again from the state of the game now (on opening, once a day, when the theme or a setting changes). Does nothing while the window is closed.</summary>
        public void Refresh()
        {
            if (!IsOpen) return;
            ModHooks.Guard(Feature, "IntelWindow/Refresh", () => Rebuild(false));
        }

        /// <summary><c>TickPhase.DayPassed</c>: the numbers change with the days.</summary>
        public void OnDay()
        {
            Refresh();
        }

        /// <summary><c>ModUI.ThemeChanged</c>: the colours are in the text, so the contents are made again (FR-9 item 5).</summary>
        public void OnThemeChanged()
        {
            Refresh();
        }

        /// <summary><c>ModL10n.LanguageChanged</c>.</summary>
        public void OnLanguageChanged()
        {
            Refresh();
        }

        /// <summary><c>ModSettings.Changed</c>: a change of the fog or of the hidden characters shows in the open window at once (FR-16 item 3).</summary>
        public void OnSettingChanged(string featureId, string key)
        {
            if (!string.Equals(featureId, Feature, StringComparison.Ordinal)) return;
            if (string.Equals(key, RivalParams.IntelFog, StringComparison.Ordinal) || string.Equals(key, RivalParams.HiddenCharacters, StringComparison.Ordinal)
                || string.Equals(key, ModSettings.EnabledKey, StringComparison.Ordinal))
            {
                Refresh();
            }
        }

        // ---- the frame ----

        private bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _list != null && _details != null) return true;
            _window = null;
            _list = null;
            _details = null;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the intelligence window cannot be opened, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "rival.window.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            IntelUi.Clear(window.Content);   // the window of an earlier attempt that failed halfway is the same object: start from an empty frame
            RectTransform columns = IntelUi.Columns(window.Content, ColumnsHeight, 10f);
            RectTransform list = IntelUi.Column(columns, ListWidth, 0f, ColumnsHeight);
            RectTransform details = IntelUi.Column(columns, 0f, 1f, ColumnsHeight);
            if (columns == null || list == null || details == null)
            {
                ModLog.Warn(Feature, "ui: the frame of the intelligence window could not be made");
                return false;
            }

            _window = window;
            _list = list;
            _details = details;
            return true;
        }

        // ---- the contents ----

        /// <summary>Fills both columns from the game. <paramref name="resetDetailsScroll"/>: show the details from the top (another company was chosen).</summary>
        private void Rebuild(bool resetDetailsScroll)
        {
            if (_list == null || _details == null) return;

            IntelSettings settings = new IntelSettings
            {
                Today = ModTicks.Today,
                HiddenMode = RivalSettings.HiddenCharacters,
                Fog = RivalSettings.IntelFog,
            };

            List<IntelCompanyFacts> facts = _source.Collect(_feature.Registry.Rivals());
            var byId = new Dictionary<int, IntelCompanyFacts>();
            var rows = new List<IntelRow>(facts.Count);
            foreach (IntelCompanyFacts company in facts)
            {
                byId[company.UniqueId] = company;
                rows.Add(_builder.BuildRow(company, settings));
            }
            rows.Sort(IntelBuilder.Compare);

            if (!_selected.HasValue || !byId.ContainsKey(_selected.Value)) _selected = rows.Count > 0 ? rows[0].UniqueId : (int?)null;

            float listOffset = IntelUi.ScrollOffset(_list);
            float detailsOffset = resetDetailsScroll ? 0f : IntelUi.ScrollOffset(_details);
            IntelUi.Clear(_list);
            IntelUi.Clear(_details);

            if (rows.Count == 0)
            {
                ModUI.Text(_list, IntelUi.Paint(ModL10n.L("rival.window.empty"), IntelTone.Muted));
                return;
            }

            foreach (IntelRow row in rows) IntelRowView.Build(_list, row, row.UniqueId == _selected.Value, OnSelect);
            BuildDetails(_builder.BuildDetails(byId[_selected.Value], settings));

            IntelUi.RestoreScroll(_list, listOffset);
            IntelUi.RestoreScroll(_details, detailsOffset);
        }

        private void OnSelect(int uniqueId)
        {
            if (_selected.HasValue && _selected.Value == uniqueId) return;
            _selected = uniqueId;
            Rebuild(true);
        }

        private void BuildDetails(IntelDetails details)
        {
            foreach (IntelSection section in details.Sections)
            {
                if (!string.IsNullOrEmpty(section.Title)) ModUI.Text(_details, IntelUi.Escape(section.Title), true);
                foreach (IntelLine line in section.Lines) AddLine(line);
            }
        }

        private void AddLine(IntelLine line)
        {
            string painted = IntelUi.Paint(line.Text, line.Tone);
            if (line.Label != null)
            {
                InfoText info = ModUI.Info(_details, IntelUi.Escape(line.Label), painted);
                if (info != null)
                {
                    // the tip on the number: what it is and what it does in a negotiation (FR-8 item 4)
                    if (!string.IsNullOrEmpty(line.Tooltip)) ModUI.Tooltip(info.gameObject, line.Label, IntelUi.Escape(line.Tooltip));
                    return;
                }
                painted = IntelUi.Escape(line.Label) + ": " + painted;   // no template of the pair in this scene: a plain line
            }
            ModUI.Text(_details, painted, line.Bold);
        }
    }
}
