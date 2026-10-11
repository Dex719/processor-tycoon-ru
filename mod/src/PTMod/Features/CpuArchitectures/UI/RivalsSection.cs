using System;
using System.Collections.Generic;
using System.Globalization;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The table "Соперники" of the panel (FR-15 item 1, stage 2; design.md Component 13): for every AI company that designs CPUs its name, the architecture it designs on and the
    /// architecture it is researching with the progress ("x86-64: 40 %", with "пауза" when it stands still). The rows come from <see cref="RivalsModel"/> (sorted by name, foundries and
    /// bankrupt companies left out); what is shown is what the inspector of the game shows for any company, nothing more. Made once with three columns and a header; rows are added as
    /// companies appear and hidden when they go. Hidden at stage 1. It only draws what the model gives.
    /// </summary>
    internal sealed class RivalsSection
    {
        private const float NameWidth = 150f;
        private const float ActiveWidth = 170f;

        private readonly ArchitecturesFeature _feature;
        private readonly List<RivalRowWidgets> _rows = new List<RivalRowWidgets>();

        private RectTransform _box;
        private RectTransform _host;
        private TMP_Text _title;
        private TMP_Text _hint;
        private TMP_Text _empty;
        private TMP_Text _headCompany;
        private TMP_Text _headActive;
        private TMP_Text _headResearch;

        private sealed class RivalRowWidgets
        {
            public RectTransform Root;
            public TMP_Text Name;
            public TMP_Text Active;
            public TMP_Text Research;
        }

        public RivalsSection(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        public bool IsAlive
        {
            get { return _box != null; }
        }

        /// <summary>The container with the header, made hidden: stage 1 never shows it.</summary>
        public void Build(Transform parent)
        {
            _box = PanelKit.Stack(parent, "PTModArchRivals", 4f, 0);
            _title = PanelKit.Line(_box, true, 0f);
            _hint = PanelKit.Line(_box, false, 1f);

            RectTransform header = PanelKit.Row(_box, 0f, 8f);
            _headCompany = Cell(header, true, NameWidth, 0f);
            _headActive = Cell(header, true, ActiveWidth, 0f);
            _headResearch = Cell(header, true, 0f, 1f);

            _host = PanelKit.Stack(_box, "PTModArchRivalsRows", 2f, 0);
            _empty = PanelKit.Line(_box, false, 1f);
            _box.gameObject.SetActive(false);
        }

        private static TMP_Text Cell(Transform row, bool bold, float width, float flexible)
        {
            TMP_Text text = PanelKit.Line(row, bold, bold ? 1f : 0f);
            if (text != null) PanelKit.SetWidth(text, width, flexible);
            return text;
        }

        /// <summary>Redraws the table from the state of the AI companies; hides the section when it is not to be shown.</summary>
        public void Refresh()
        {
            if (!IsAlive) return;

            bool shown = _feature.Balance.IsFullStage && !_feature.IsNeutralNow && _feature.Registry != null && _feature.Catalog != null;
            if (_box.gameObject.activeSelf != shown) _box.gameObject.SetActive(shown);
            if (!shown) return;

            PanelKit.Set(_title, ModL10n.L("arch.rivals.title"));
            PanelKit.Set(_hint, PanelKit.Tinted(ModL10n.L("arch.rivals.hint"), PanelTone.Muted));
            PanelKit.Set(_headCompany, PanelKit.Tinted(ModL10n.L("arch.rivals.colCompany"), PanelTone.Muted));
            PanelKit.Set(_headActive, PanelKit.Tinted(ModL10n.L("arch.rivals.colActive"), PanelTone.Muted));
            PanelKit.Set(_headResearch, PanelKit.Tinted(ModL10n.L("arch.rivals.colResearch"), PanelTone.Muted));

            List<RivalRow> rows = RivalsModel.Rows(Inputs());
            PanelKit.Set(_empty, rows.Count == 0 ? PanelKit.Tinted(ModL10n.L("arch.rivals.empty"), PanelTone.Muted) : string.Empty);
            if (_empty != null) _empty.gameObject.SetActive(rows.Count == 0);

            for (int i = 0; i < rows.Count; i++)
            {
                RivalRowWidgets widgets = RowAt(i);
                if (widgets == null) break;
                Show(widgets, rows[i]);
            }
            for (int i = rows.Count; i < _rows.Count; i++)
            {
                if (_rows[i].Root != null && _rows[i].Root.gameObject.activeSelf) _rows[i].Root.gameObject.SetActive(false);
            }
        }

        private void Show(RivalRowWidgets widgets, RivalRow row)
        {
            if (!widgets.Root.gameObject.activeSelf) widgets.Root.gameObject.SetActive(true);

            PanelKit.Set(widgets.Name, row.Name);
            PanelKit.Set(widgets.Active, row.ActiveId == null ? ArchDisplay.Empty : ArchDisplay.FamilyName(_feature.Catalog, row.ActiveId, ModL10n.L));

            string research = ArchDisplay.Empty;
            if (row.ResearchId != null)
            {
                string name = ArchDisplay.FamilyName(_feature.Catalog, row.ResearchId, ModL10n.L);
                string percent = row.ResearchPercent.ToString(CultureInfo.InvariantCulture);
                research = row.Paused ? ModL10n.L("arch.rivals.paused", name, percent) : ModL10n.L("arch.rivals.research", name, percent);
            }
            PanelKit.Set(widgets.Research, research);
        }

        /// <summary>The row widgets at an index; new ones are made as the table grows. Null when a row could not be made.</summary>
        private RivalRowWidgets RowAt(int index)
        {
            while (_rows.Count <= index)
            {
                var widgets = new RivalRowWidgets();
                try
                {
                    widgets.Root = PanelKit.Row(_host, 0f, 8f);
                    widgets.Name = Cell(widgets.Root, false, NameWidth, 0f);
                    widgets.Active = Cell(widgets.Root, false, ActiveWidth, 0f);
                    widgets.Research = Cell(widgets.Root, false, 0f, 1f);
                }
                catch (Exception ex)
                {
                    ModHooks.ReportFault(ArchitecturesFeature.FeatureId, "ui/rivals.row", ex);
                    if (widgets.Root != null) PanelKit.Destroy(widgets.Root.gameObject);
                    return null;
                }
                _rows.Add(widgets);
            }
            return _rows[index];
        }

        /// <summary>The AI companies as the model reads them: name, foundry and bankruptcy from the game, the state of architectures from the registry.</summary>
        private List<RivalInput> Inputs()
        {
            var inputs = new List<RivalInput>();
            IReadOnlyList<ICompany> companies = ModCompanies.All;
            for (int i = 0; i < companies.Count; i++)
            {
                ICompany company = companies[i];
                if (ReferenceEquals(company, null) || company.IsPlayer) continue;

                inputs.Add(new RivalInput
                {
                    CompanyId = company.SaveID,
                    Name = company.Name,
                    IsFoundry = company.IsFoundry,
                    IsBankrupt = company.IsBankrupt,
                    State = _feature.Registry.GetOrNull(company.SaveID),
                });
            }
            return inputs;
        }

        /// <summary>Forgets the widgets (their scene is going or the panel could not be made).</summary>
        public void Drop()
        {
            _rows.Clear();
            if (_box != null) PanelKit.Destroy(_box.gameObject);
            _box = null;
            _host = null;
            _title = null;
            _hint = null;
            _empty = null;
            _headCompany = null;
            _headActive = null;
            _headResearch = null;
        }
    }
}
