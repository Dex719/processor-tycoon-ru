using System;
using System.Collections.Generic;
using System.Text;
using PTMod.Core;
using TMPro;
using UnityEngine;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using GameFormatter = ProcessorTycoon.StringFormatter;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The row of one researched family in the section "Программная поддержка" (FR-10 item 7): the name with the support level, a bar of the level (0-100), the
    /// list of the tier the company funds it at, the cost of a month with the level in twelve months, and the multiplier the level gives the rating. The
    /// tooltip of the row writes the formula with the numbers in it (NFR-6): the rating factor from the level, where the tier leads and how fast, the cap of
    /// the family, and the upkeep as the product of its terms. What the row says comes from <see cref="SupportRowInfo"/>; this class only draws it. The row is
    /// built once for a language (a change of language builds it again, which also gives the list its caption), and redrawn by <see cref="Refresh"/>.
    /// </summary>
    internal sealed class SupportRow
    {
        private readonly ArchitecturesFeature _feature;
        private readonly ArchitecturesUi _ui;
        private readonly ArchFamily _family;

        private RectTransform _root;
        private TMP_Text _title;
        private ThemedBar _bar;
        private DropdownUI _dropdown;
        private TMP_Dropdown _tmp;
        private TMP_Text _cost;
        private TMP_Text _effect;
        private SupportRowInfo _info;
        private string _builtLanguage;
        private bool _noListLogged;

        public SupportRow(ArchitecturesFeature feature, ArchitecturesUi ui, ArchFamily family)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            if (ui == null) throw new ArgumentNullException("ui");
            if (family == null) throw new ArgumentNullException("family");
            _feature = feature;
            _ui = ui;
            _family = family;
        }

        public string FamilyId
        {
            get { return _family.Id; }
        }

        public bool IsAlive
        {
            get { return _root != null; }
        }

        /// <summary>The row is built in the language the game speaks now.</summary>
        public bool IsCurrent
        {
            get { return IsAlive && string.Equals(_builtLanguage, ModL10n.Language, StringComparison.Ordinal); }
        }

        /// <summary>The widgets of the row (what the section orders and shows), or null.</summary>
        public RectTransform Root
        {
            get { return _root; }
        }

        /// <summary>What the row shows now (null before the first <see cref="Refresh"/>).</summary>
        public SupportRowInfo Info
        {
            get { return _info; }
        }

        public void Build(Transform parent)
        {
            _root = PanelKit.Card(parent);
            _builtLanguage = ModL10n.Language;

            _title = PanelKit.Line(_root, true, 0f);
            _bar = ThemedBar.Create(_root, 10f);

            _dropdown = ModUI.Dropdown(_root, ModL10n.L("arch.support.tierLabel"), TierNames(), 0, OnPicked, ModL10n.L("arch.support.tierTip"));
            _tmp = _dropdown == null ? null : _dropdown.GetComponent<TMP_Dropdown>();
            if (_dropdown == null && !_noListLogged)
            {
                _noListLogged = true;
                ModLog.Warn(ArchitecturesFeature.FeatureId, "ui: the dropdown template is not available; the support rows show their tier but cannot change it");
            }

            _cost = PanelKit.Line(_root, false, 1f);
            _effect = PanelKit.Line(_root, false, 1f);

            ModUI.Tooltip(_root.gameObject, TooltipOf);
        }

        /// <summary>Takes the widgets away (their scene is going, or the language changed and the row is made again).</summary>
        public void Drop()
        {
            if (_root != null) PanelKit.Destroy(_root.gameObject);
            _root = null;
            _title = null;
            _bar = null;
            _dropdown = null;
            _tmp = null;
            _cost = null;
            _effect = null;
            _info = null;
        }

        public void SetVisible(bool visible)
        {
            if (_root != null && _root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
        }

        /// <summary>Redraws the row for the numbers of <see cref="SupportSectionModel.RowOf"/>.</summary>
        public void Refresh(SupportRowInfo info)
        {
            if (!IsAlive || info == null) return;
            _info = info;

            string language = ModL10n.Language;
            PanelKit.Set(_title, ModL10n.L("arch.support.row", ModL10n.L(_family.NameKey), SupportMath.Score(info.Support)));
            if (_bar != null) _bar.SetFraction(info.Support / SupportMath.MaxSupport);

            SyncList(info);

            PanelKit.Set(_cost, ModL10n.L("arch.support.cost", Money(info.Upkeep), SupportMath.Score(info.Forecast)));
            PanelKit.Set(_effect, ModL10n.L("arch.support.effect", RatingForecast.Format(info.RatingFactor, language)));
        }

        // ---- the list of tiers ----

        private static List<string> TierNames()
        {
            var names = new List<string>(SupportCycle.Tiers.Length);
            foreach (SupportTier tier in SupportCycle.Tiers) names.Add(ModL10n.L(SupportSectionModel.TierKey(tier)));
            return names;
        }

        /// <summary>The list shows the tier of the company and can be used only while the tiers may be changed. No event fires from here.</summary>
        private void SyncList(SupportRowInfo info)
        {
            if (_dropdown == null) return;

            _dropdown.Interactable = info.Editable;
            int index = (int)info.Tier;
            if (_tmp == null || _tmp.value != index) ModUI.SetWithoutNotify(_dropdown, index);
        }

        private void OnPicked(int index)
        {
            if (index < 0 || index >= SupportCycle.Tiers.Length) return;
            _ui.SetSupportTier(_family.Id, SupportCycle.Tiers[index]);
        }

        // ---- the texts ----

        private static string Money(float value)
        {
            return GameFormatter.FloatToMoney(value, string.Empty, true, true, true);
        }

        /// <summary>The formula of the effect with the numbers of this family in it (NFR-6): the rating, the tier, the cap, the upkeep.</summary>
        private ModUI.TooltipText TooltipOf()
        {
            string header = ModL10n.L(_family.NameKey);
            SupportRowInfo info = _info;
            if (info == null) return new ModUI.TooltipText(header, ModL10n.L("arch.support.hint"));

            string language = ModL10n.Language;
            RatingConfig rating = _feature.Balance.rating;
            var body = new StringBuilder();

            body.Append(ModL10n.L("arch.support.tip.rating",
                RatingForecast.Format(rating.supportBase, language),
                RatingForecast.Format(rating.supportSpan, language),
                SupportMath.Score(info.Support),
                RatingForecast.Format(info.RatingFactor, language)));

            body.Append('\n');
            if (info.Tier == SupportTier.Off) body.Append(ModL10n.L("arch.support.tip.off", SupportMath.PercentOf(info.Rate)));
            else body.Append(ModL10n.L("arch.support.tip.tier", ModL10n.L(SupportSectionModel.TierKey(info.Tier)), SupportMath.Score(info.Target), SupportMath.PercentOf(info.Rate)));

            if (info.Capped) body.Append('\n').Append(ModL10n.L("arch.support.tip.cap", SupportMath.Score(info.Cap)));

            body.Append('\n');
            if (info.Upkeep > 0f)
            {
                body.Append(ModL10n.L("arch.support.tip.cost",
                    Money(info.BaseUpkeep),
                    RatingForecast.Format(info.TierFactor, language),
                    RatingForecast.Format(info.EraFactor, language),
                    RatingForecast.Format(info.Inflation, language),
                    Money(info.Upkeep)));
            }
            else
            {
                body.Append(ModL10n.L("arch.support.tip.free"));
            }

            return new ModUI.TooltipText(header, body.ToString());
        }
    }
}
