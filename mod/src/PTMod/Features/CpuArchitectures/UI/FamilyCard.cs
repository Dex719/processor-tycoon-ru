using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameFormatter = ProcessorTycoon.StringFormatter;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The card of one family in the panel "Архитектуры" (FR-3 item 3): the name, the ISA and the year, the status with its reason (researched, available,
    /// researching with a percent, paused with the reason, locked with the missing families), the time and the monthly cost of the research at the current
    /// funding, the multipliers of the family (IPC, power, cost of a project), its affinity with the four segments, and the button "Исследовать"
    /// ("Продолжить" on a paused research). What the card says comes from <see cref="FamilyCardModel"/>; this class only draws it. The card is made once
    /// and redrawn by <see cref="Refresh"/> (every text is set there, so a change of language needs nothing more). The tooltip of the card is made when
    /// the pointer comes: the description of the family and, when it is locked, what it needs.
    /// </summary>
    internal sealed class FamilyCard
    {
        private readonly ArchitecturesFeature _feature;
        private readonly ArchitecturesUi _ui;
        private readonly ArchFamily _family;

        private RectTransform _root;
        private TMP_Text _title;
        private TMP_Text _meta;
        private TMP_Text _status;
        private TMP_Text _numbers;
        private TMP_Text _effects;
        private TMP_Text _affinity;
        private ButtonUI _button;
        private FamilyCardInfo _info;
        private ICompany _player;

        public FamilyCard(ArchitecturesFeature feature, ArchitecturesUi ui, ArchFamily family)
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

        /// <summary>The state the card shows now (null before the first <see cref="Refresh"/>).</summary>
        public FamilyCardInfo Info
        {
            get { return _info; }
        }

        public void Build(Transform parent)
        {
            _root = PanelKit.Card(parent);

            _title = PanelKit.Line(_root, true, 0f);
            _meta = PanelKit.Line(_root, false, 2f);
            _status = PanelKit.Line(_root, false, 0f);
            _numbers = PanelKit.Line(_root, false, 1f);
            _effects = PanelKit.Line(_root, false, 1f);
            _affinity = PanelKit.Line(_root, false, 1f);

            RectTransform row = PanelKit.Row(_root, 34f, 8f);
            _button = ModUI.Button(row, ModL10n.L("arch.card.research"), OnPressed);
            PanelKit.SetWidth(_button, 200f, 0f);

            ModUI.Tooltip(_root.gameObject, TooltipOf);
        }

        /// <summary>Redraws the card for the state of the player's company.</summary>
        /// <param name="info">What <see cref="FamilyCardModel.Of"/> says about this family now.</param>
        /// <param name="player">The player's company, for the numbers of the research; null before the game is ready.</param>
        public void Refresh(FamilyCardInfo info, ICompany player)
        {
            if (!IsAlive || info == null) return;
            _info = info;
            _player = player;

            string language = ModL10n.Language;
            ArchFamily family = _family;

            PanelKit.Set(_title, ModL10n.L(family.NameKey));
            PanelKit.Set(_meta, ModL10n.L("arch.card.meta", IsaText(family.Isa), family.Year));
            PanelKit.Set(_status, StatusText(info));
            PanelKit.Set(_numbers, NumbersText(info, player));
            PanelKit.Set(_effects, ModL10n.L("arch.effects",
                RatingForecast.Format(family.Design.Ipc, language),
                RatingForecast.Format(family.Design.Power, language),
                RatingForecast.Format(family.Design.DevCost, language)));
            PanelKit.Set(_affinity, ModL10n.L("arch.card.affinity", ModL10n.L("arch.segments", RatingForecast.FormatAll(family.Affinity, language))));

            if (_numbers != null) _numbers.gameObject.SetActive(info.ShowsResearchNumbers);

            if (_button != null)
            {
                _button.Text = ButtonCaption(info);
                _button.Interactable = info.ActionEnabled;
            }
        }

        // ---- the texts ----

        /// <summary>The name of an ISA group (<c>arch.isa.x86</c>…); the id itself for a group of the player's own content.</summary>
        private static string IsaText(string isa)
        {
            if (string.IsNullOrEmpty(isa)) return ArchDisplay.Empty;
            string key = "arch.isa." + isa;
            return ModL10n.Has(key) ? ModL10n.L(key) : isa;
        }

        private string StatusText(FamilyCardInfo info)
        {
            switch (info.Status)
            {
                case CardStatus.Researched:
                    return PanelKit.Tinted(ModL10n.L("arch.status.researched"), PanelTone.Good);

                case CardStatus.InProgress:
                    return PanelKit.Tinted(ModL10n.L("arch.status.inProgress", info.ProgressPercent), PanelTone.Warn);

                case CardStatus.Paused:
                    string reasonKey = LabBlockModel.PauseKey(info.PauseReason);
                    string reason = reasonKey == null ? ArchDisplay.Empty : ModL10n.L(reasonKey);
                    return PanelKit.Tinted(ModL10n.L("arch.status.paused", info.ProgressPercent, reason), PanelTone.Bad);

                case CardStatus.Locked:
                    return PanelKit.Tinted(ModL10n.L("arch.status.locked", NamesOf(info.Missing)), PanelTone.Muted);

                default:
                    return info.ProgressPercent > 0
                        ? ModL10n.L("arch.status.availableProgress", info.ProgressPercent)
                        : ModL10n.L("arch.status.available");
            }
        }

        /// <summary>"Время: …, стоимость: …/мес" at the current funding of the lab; empty for a researched family.</summary>
        private string NumbersText(FamilyCardInfo info, ICompany player)
        {
            ArchResearchService research = _feature.Research;
            if (!info.ShowsResearchNumbers || research == null || ReferenceEquals(player, null)) return string.Empty;

            ResearchForecast forecast = research.Forecast(player, _family.Id);
            string eta = ArchResearchMath.FormatEta(forecast.EtaDays, GameFormatter.DaysToDate);
            string cost = GameFormatter.FloatToMoney(forecast.MonthlyCost, string.Empty, true, true, true);
            return ModL10n.L("arch.card.numbers", eta, cost);
        }

        private string ButtonCaption(FamilyCardInfo info)
        {
            if (info.Status == CardStatus.Researched) return ModL10n.L("arch.card.done");
            if (info.Action == CardAction.Resume) return ModL10n.L("arch.card.resume");
            return ModL10n.L("arch.card.research");
        }

        private string NamesOf(List<string> ids)
        {
            var names = new List<string>();
            foreach (string id in ids) names.Add(ArchDisplay.FamilyName(_feature.Catalog, id, ModL10n.L));
            return string.Join(", ", names.ToArray());
        }

        // ---- the button and the tooltip ----

        private void OnPressed()
        {
            FamilyCardInfo info = _info;
            if (info == null || !info.ActionEnabled) return;

            if (info.Action == CardAction.Resume) _ui.ResumeResearch();
            else _ui.StartResearch(_family.Id);
        }

        private ModUI.TooltipText TooltipOf()
        {
            string header = ModL10n.L(_family.NameKey);
            string body = ModL10n.L(_family.DescKey);
            FamilyCardInfo info = _info;
            if (info != null && info.Status == CardStatus.Locked) body += "\n" + ModL10n.L("arch.status.locked", NamesOf(info.Missing));
            if (info != null && info.BlockedByTechnology) body += "\n" + ModL10n.L("arch.lab.stopTechFirst");

            string rating = RatingText();
            if (rating.Length > 0) body += "\n\n" + rating;
            return new ModUI.TooltipText(header, body);
        }

        /// <summary>
        /// The formula of the effect on the rating for this family (NFR-6): "Высокий сегмент: ×1,05 сродство ×1,04 поддержка ×1,03 экосистема = ×1,12" for the four segments, with the
        /// support of the player for the family (the one he would start with when the family is not researched yet) and the ecosystem of the family now. Empty while the rating of the
        /// feature does not work (the part <c>Market</c> of the self-check) or the feature is off.
        /// </summary>
        private string RatingText()
        {
            if (_feature.IsNeutralNow || (_feature.Parts & ArchParts.Market) == 0 || _feature.Registry == null) return string.Empty;

            BalanceConfig balance = _feature.Balance;
            DateTime today = ModTicks.Today;
            int year = today == default(DateTime) ? _family.Year : today.Year;

            CompanyArchState state = ReferenceEquals(_player, null) ? null : _feature.Registry.GetOrNull(_player.SaveID);
            IReadOnlyDictionary<string, FamilyState> families = state == null ? null : state.Families;
            FamilyState own = state == null ? null : state.GetFamily(_family.Id);

            float support = 0f;
            bool assumed = false;
            if (balance.IsFullStage)
            {
                if (ArchResearchMath.CountsAsResearched(_family, families)) support = own == null ? 0f : own.Support;
                else
                {
                    support = SupportMath.StartSupport(_family, families, _feature.Catalog, balance.support);
                    assumed = true;
                }
            }

            float strength = _feature.RatingStrength;
            RatingTerms[] terms = RatingBreakdown.All(_family, year, balance, support, _feature.EcosystemOf(_family.Id), 1, false, strength);
            string text = ModL10n.L("arch.card.tip.rating") + "\n" + RatingTooltipText.Lines(terms, strength, ModL10n.Language);
            if (assumed) text += "\n" + ModL10n.L("arch.card.tip.assumed", SupportMath.Score(support));
            return text;
        }
    }
}
