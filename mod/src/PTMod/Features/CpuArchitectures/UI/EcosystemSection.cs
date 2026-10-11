using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The section "Экосистема архитектур" of the panel (FR-11 item 4, stage 2; design.md Component 13): for every family of the catalog its share of the CPU sales of the last twelve
    /// months as a whole percent and the change against the year before in percentage points ("x86: 62 % (+4 п. п. за год)"). The percents are rounded so that they add up to 100
    /// (<see cref="EcosystemMath.RoundToHundred"/>) and the total is written under the lines. The tooltip of a line has the formula of the rating with the numbers in it
    /// (<c>0.95 + 0.10 × √E = ×1.045</c>, NFR-6). With no sales in the window the lines say that the whole ecosystem is the proprietary ISA's. The section exists only at stage 2 while the
    /// ecosystem is measured (<see cref="EcosystemService"/>); at stage 1 it is made hidden and stays so. It only draws what the service measured.
    /// </summary>
    internal sealed class EcosystemSection
    {
        private readonly ArchitecturesFeature _feature;
        private readonly Dictionary<string, TMP_Text> _lines = new Dictionary<string, TMP_Text>(StringComparer.Ordinal);

        private RectTransform _box;
        private TMP_Text _title;
        private TMP_Text _hint;
        private TMP_Text _empty;
        private TMP_Text _total;

        public EcosystemSection(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        public bool IsAlive
        {
            get { return _box != null; }
        }

        /// <summary>The container with a line for every family, made hidden: stage 1 never shows it.</summary>
        public void Build(Transform parent)
        {
            _box = PanelKit.Stack(parent, "PTModArchEcosystem", 4f, 0);
            _title = PanelKit.Line(_box, true, 0f);
            _hint = PanelKit.Line(_box, false, 1f);
            foreach (ArchFamily family in _feature.Catalog.Families)
            {
                string id = family.Id;
                TMP_Text line = PanelKit.Line(_box, false, 0f);
                if (line == null) continue;
                _lines[id] = line;
                ModUI.Tooltip(line.gameObject, () => TooltipOf(id));
            }
            _empty = PanelKit.Line(_box, false, 1f);
            _total = PanelKit.Line(_box, false, 1f);
            _box.gameObject.SetActive(false);
        }

        /// <summary>Redraws the section from the last measurement; hides it when the ecosystem is not played or not measured.</summary>
        public void Refresh()
        {
            if (!IsAlive) return;

            EcosystemService service = _feature.Ecosystem;
            bool shown = service != null && service.CanRun && service.Measured;
            if (_box.gameObject.activeSelf != shown) _box.gameObject.SetActive(shown);
            if (!shown) return;

            PanelKit.Set(_title, ModL10n.L("arch.eco.title"));
            RatingConfig rating = _feature.Balance.rating;
            string language = ModL10n.Language;
            PanelKit.Set(_hint, PanelKit.Tinted(ModL10n.L("arch.eco.hint", RatingForecast.Format(rating.ecoBase, language), RatingForecast.Format(rating.ecoSpan, language)), PanelTone.Muted));

            int total = 0;
            foreach (EcosystemRow row in service.Rows())
            {
                total += row.Percent;
                TMP_Text line;
                if (!_lines.TryGetValue(row.FamilyId, out line) || line == null) continue;

                string name = ModL10n.L(_feature.Catalog.Get(row.FamilyId).NameKey);
                string text = ModL10n.L("arch.eco.row", name, row.Percent, EcosystemMath.SignedPoints(row.ChangePoints));
                PanelKit.Set(line, row.Percent > 0 ? text : PanelKit.Tinted(text, PanelTone.Muted));
            }

            PanelKit.Set(_empty, service.HasSales ? string.Empty : PanelKit.Tinted(ModL10n.L("arch.eco.empty"), PanelTone.Muted));
            if (_empty != null) _empty.gameObject.SetActive(!service.HasSales);
            PanelKit.Set(_total, PanelKit.Tinted(ModL10n.L("arch.eco.total", total), PanelTone.Muted));
        }

        /// <summary>The formula of the rating with the numbers of the family in it (NFR-6): <c>0.95 + 0.10 × √0.62 = ×1.03</c>.</summary>
        private ModUI.TooltipText TooltipOf(string familyId)
        {
            ArchFamily family = _feature.Catalog.Get(familyId);
            string header = family == null ? familyId : ModL10n.L(family.NameKey);
            EcosystemService service = _feature.Ecosystem;
            if (family == null || service == null || !service.Measured) return new ModUI.TooltipText(header, ModL10n.L("arch.eco.hint", "0.95", "0.10"));

            RatingConfig rating = _feature.Balance.rating;
            string language = ModL10n.Language;
            float share = Math.Max(0f, service.ShareOf(familyId));
            float factor = ArchRatingMath.EcosystemFactor(share, rating);
            string body = ModL10n.L("arch.eco.tip",
                RatingForecast.Format(rating.ecoBase, language),
                RatingForecast.Format(rating.ecoSpan, language),
                RatingForecast.Format(share, language),
                RatingForecast.Format(factor, language));
            return new ModUI.TooltipText(header, body);
        }

        /// <summary>Forgets the widgets (their scene is going or the panel could not be made).</summary>
        public void Drop()
        {
            _lines.Clear();
            if (_box != null) PanelKit.Destroy(_box.gameObject);
            _box = null;
            _title = null;
            _hint = null;
            _empty = null;
            _total = null;
        }
    }
}
