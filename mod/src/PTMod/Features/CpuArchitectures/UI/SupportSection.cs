using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using GameFormatter = ProcessorTycoon.StringFormatter;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The section "Программная поддержка" at the end of the panel "Архитектуры" (FR-10 item 7; design.md, "UI"): for every family the player has researched,
    /// a <see cref="SupportRow"/> with the bar of the support level, the list of the tier, the cost of a month with the forecast for a year, and the formula
    /// in the tooltip (NFR-6). The section exists only at stage 2 of <c>balance.json</c> while the feature works (<see cref="SupportSectionModel.IsShown"/>); at
    /// stage 1 it is hidden and its rows are never made. A row is made when its family is first listed (the families are researched during the game) and
    /// placed by the order of the catalog. While the company is in default the tiers are locked and a line says why (FR-10 item 6).
    /// A change of tier is the player's command (<see cref="ArchitecturesUi.SetSupportTier"/>); the section only draws the state.
    /// </summary>
    internal sealed class SupportSection
    {
        private const string RowSite = "ui/support.row";

        private readonly ArchitecturesFeature _feature;
        private readonly ArchitecturesUi _ui;
        private readonly Dictionary<string, SupportRow> _rows = new Dictionary<string, SupportRow>(StringComparer.Ordinal);
        private readonly HashSet<string> _failed = new HashSet<string>(StringComparer.Ordinal);

        private RectTransform _box;
        private RectTransform _host;
        private TMP_Text _title;
        private TMP_Text _hint;
        private TMP_Text _defaultLine;
        private TMP_Text _total;

        public SupportSection(ArchitecturesFeature feature, ArchitecturesUi ui)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            if (ui == null) throw new ArgumentNullException("ui");
            _feature = feature;
            _ui = ui;
        }

        public bool IsAlive
        {
            get { return _box != null; }
        }

        /// <summary>The rows made so far, by family id.</summary>
        public IReadOnlyDictionary<string, SupportRow> Rows
        {
            get { return _rows; }
        }

        /// <summary>The container is made hidden: stage 1 never shows it.</summary>
        public void Build(Transform parent)
        {
            _box = PanelKit.Stack(parent, "PTModArchSupport", 6f, 0);
            _title = PanelKit.Line(_box, true, 0f);
            _hint = PanelKit.Line(_box, false, 1f);
            _defaultLine = PanelKit.Line(_box, false, 0f);
            _host = PanelKit.Stack(_box, "PTModArchSupportRows", 6f, 0);
            _total = PanelKit.Line(_box, false, 1f);
            _box.gameObject.SetActive(false);
        }

        /// <summary>Redraws the section for the player's company; hides it when it is not to be shown.</summary>
        public void Refresh(ICompany player)
        {
            if (!IsAlive) return;

            SupportService service = _feature.Support;
            bool shown = service != null && SupportSectionModel.IsShown(_feature.Balance, service.CanRun) && !ReferenceEquals(player, null);
            if (_box.gameObject.activeSelf != shown) _box.gameObject.SetActive(shown);
            if (!shown) return;

            PanelKit.Set(_title, ModL10n.L("arch.support.title"));
            PanelKit.Set(_hint, PanelKit.Tinted(ModL10n.L("arch.support.hint"), PanelTone.Muted));

            CompanyArchState state = _feature.Registry.GetOrNull(player.SaveID);
            bool inDefault = service.InDefault(player, state);
            PanelKit.Set(_defaultLine, inDefault ? PanelKit.Tinted(ModL10n.L("arch.notify.defaultTiers"), PanelTone.Bad) : string.Empty);
            if (_defaultLine != null) _defaultLine.gameObject.SetActive(inDefault);

            List<SupportRowInfo> infos = service.RowsFor(player);
            bool added = false;
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (SupportRowInfo info in infos)
            {
                listed.Add(info.FamilyId);
                SupportRow row = RowFor(info.FamilyId, ref added);
                if (row == null) continue;
                row.Refresh(info);
                row.SetVisible(true);
            }
            foreach (KeyValuePair<string, SupportRow> pair in _rows)
            {
                if (!listed.Contains(pair.Key)) pair.Value.SetVisible(false);
            }
            if (added) Reorder();

            PanelKit.Set(_total, ModL10n.L("arch.support.total", GameFormatter.FloatToMoney(SupportSectionModel.TotalUpkeep(infos), string.Empty, true, true, true)));
        }

        /// <summary>The row of a family: the one made, or a new one; a row built in another language is made again. Null when it could not be made.</summary>
        private SupportRow RowFor(string familyId, ref bool added)
        {
            SupportRow row;
            if (_rows.TryGetValue(familyId, out row) && row.IsCurrent) return row;

            ArchFamily family = _feature.Catalog.Get(familyId);
            if (family == null || _failed.Contains(familyId)) return null;

            if (row != null) row.Drop();
            row = new SupportRow(_feature, _ui, family);
            bool ok = ModHooks.Guard(ArchitecturesFeature.FeatureId, RowSite, () => row.Build(_host));
            if (!ok || !row.IsAlive)
            {
                row.Drop();
                _rows.Remove(familyId);
                _failed.Add(familyId);   // not tried again every day; the next panel (a new scene) starts clean
                return null;
            }

            _rows[familyId] = row;
            added = true;
            return row;
        }

        /// <summary>The rows stand in the order of the catalog, whenever they were made.</summary>
        private void Reorder()
        {
            int index = 0;
            foreach (ArchFamily family in _feature.Catalog.Families)
            {
                SupportRow row;
                if (!_rows.TryGetValue(family.Id, out row) || !row.IsAlive || row.Root == null) continue;
                row.Root.SetSiblingIndex(index++);
            }
        }

        /// <summary>Forgets the widgets (their scene is going or the panel could not be made).</summary>
        public void Drop()
        {
            foreach (SupportRow row in _rows.Values) row.Drop();
            _rows.Clear();
            _failed.Clear();
            if (_box != null) PanelKit.Destroy(_box.gameObject);
            _box = null;
            _host = null;
            _title = null;
            _hint = null;
            _defaultLine = null;
            _total = null;
        }
    }
}
