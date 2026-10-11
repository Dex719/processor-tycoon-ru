using System;
using PTMod.Core;
using TMPro;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameFormatter = ProcessorTycoon.StringFormatter;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;
using ResearchSector = ProcessorTycoon.ResearchSystem.ResearchSector;
using SliderUI = ProcessorTycoon.UI.SliderUI;
using Technology = ProcessorTycoon.ResearchSystem.Technology;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The block of the lab at the top of the panel "Архитектуры" (FR-3 items 4 and 5): a line that says what the shared lab is busy with (free, a
    /// technology, an architecture, an architecture on pause and why), and, while an architecture is the research of the lab, its progress bar, the
    /// percent done, the time left and the cost per month at the current funding, the slider of the funding of the lab, and the buttons "Остановить" and
    /// "Продолжить". The slider writes <c>ResearchSector.Funding</c> of the player (1 … 100 %, up to 500 % with <c>InnovationEffort</c>) and the numbers
    /// are redrawn at once. What is shown is decided by <see cref="LabBlockModel"/>; the numbers are the research service's
    /// (<see cref="ArchResearchService.Forecast"/>). Every text is set by <see cref="Refresh"/>, so the block follows the language and the day.
    /// </summary>
    internal sealed class LabStatusBlock
    {
        private readonly ArchitecturesFeature _feature;
        private readonly ArchitecturesUi _ui;

        private RectTransform _box;
        private RectTransform _group;
        private TMP_Text _status;
        private ThemedBar _bar;
        private TMP_Text _numbers;
        private SliderUI _slider;
        private int _sliderMax;
        private ButtonUI _stop;
        private ButtonUI _resume;

        public LabStatusBlock(ArchitecturesFeature feature, ArchitecturesUi ui)
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

        public void Build(Transform parent)
        {
            _box = PanelKit.Card(parent);
            _status = PanelKit.Line(_box, true, 0f);

            _group = PanelKit.Stack(_box, "PTModArchLab", 4f, 0);
            _bar = ThemedBar.Create(_group, 12f);
            _numbers = PanelKit.Line(_group, false, 1f);
            CreateSlider(LabBlockModel.NormalMaxPercent, LabBlockModel.NormalMaxPercent, -1);

            RectTransform row = PanelKit.Row(_group, 34f, 8f);
            _stop = ModUI.Button(row, ModL10n.L("arch.card.stop"), OnStop);
            _resume = ModUI.Button(row, ModL10n.L("arch.card.resume"), OnResume);
            PanelKit.SetWidth(_stop, 200f, 0f);
            PanelKit.SetWidth(_resume, 200f, 0f);
        }

        /// <summary>Redraws the block for the player's company.</summary>
        /// <param name="player">The player's company; null before the game is ready.</param>
        /// <param name="state">His architecture state; null before the seed.</param>
        /// <param name="researchUsable">The feature is on and the research part works: the buttons may work.</param>
        /// <param name="technologyBlocks">A technology of the player is in the lab and cannot be interrupted: "Продолжить" is disabled.</param>
        public void Refresh(ICompany player, CompanyArchState state, bool researchUsable, bool technologyBlocks = false)
        {
            if (!IsAlive) return;

            LabStatus status = ReferenceEquals(player, null) || _feature.Lab == null ? LabStatus.Free : _feature.Lab.StatusOf(player);
            ArchFamily family = state == null || state.CurrentResearch == null ? null : _feature.Catalog.Get(state.CurrentResearch);
            string name = family == null ? ArchDisplay.Empty : ModL10n.L(family.NameKey);

            PanelKit.Set(_status, StatusText(player, state, status, name));

            bool has = LabBlockModel.HasArchitecture(status) && family != null;
            if (_group != null) _group.gameObject.SetActive(has);
            if (!has) return;

            ResearchSector sector = player.ResearchSector;
            FamilyState progress = state.GetFamily(family.Id);
            float done = progress == null ? 0f : progress.Progress;

            if (_bar != null) _bar.SetFraction(done);

            ResearchForecast forecast = _feature.Research == null ? new ResearchForecast { EtaDays = ArchResearchMath.InfiniteEta } : _feature.Research.Forecast(player, family.Id);
            string eta = ArchResearchMath.FormatEta(forecast.EtaDays, GameFormatter.DaysToDate);
            string cost = GameFormatter.FloatToMoney(forecast.MonthlyCost, string.Empty, true, true, true);
            PanelKit.Set(_numbers, ModL10n.L("arch.lab.numbers", FamilyCardModel.Percent(done), eta, cost));

            SyncSlider(sector);

            if (_stop != null)
            {
                _stop.Text = ModL10n.L("arch.card.stop");
                _stop.Interactable = LabBlockModel.CanStop(status, researchUsable);
            }
            if (_resume != null)
            {
                _resume.Text = ModL10n.L("arch.card.resume");
                _resume.Interactable = LabBlockModel.CanResume(status, researchUsable, technologyBlocks);
            }
        }

        // ---- the line ----

        private static string StatusText(ICompany player, CompanyArchState state, LabStatus status, string familyName)
        {
            switch (status)
            {
                case LabStatus.Technology:
                    return ModL10n.L("arch.lab.tech", TechnologyNameOf(player));

                case LabStatus.Architecture:
                    return ModL10n.L("arch.lab.arch", familyName);

                case LabStatus.ArchitecturePaused:
                    string key = LabBlockModel.PauseKey(state == null ? PauseReason.None : state.PauseReason);
                    return PanelKit.Tinted(ModL10n.L("arch.lab.paused", familyName, key == null ? ArchDisplay.Empty : ModL10n.L(key)), PanelTone.Bad);

                default:
                    return ModL10n.L("arch.lab.free");
            }
        }

        private static string TechnologyNameOf(ICompany player)
        {
            try
            {
                ResearchSector sector = ReferenceEquals(player, null) ? null : player.ResearchSector;
                Technology technology = sector == null ? null : sector.CurrentResearch();
                return technology == null ? ArchDisplay.Empty : technology.Name;
            }
            catch (Exception)
            {
                return ArchDisplay.Empty;
            }
        }

        // ---- the slider ----

        /// <summary>
        /// Puts the slider on the funding the game has now. When the limit changed (<c>InnovationEffort</c> was switched) the slider is made anew in the same
        /// place, as <c>ModUI.Slider</c> cannot change its range.
        /// </summary>
        private void SyncSlider(ResearchSector sector)
        {
            if (sector == null) return;

            bool innovation = sector.InnovationEffort;
            int max = LabBlockModel.MaxPercent(innovation);
            int percent = LabBlockModel.FundingToPercent(sector.Funding, innovation);

            if (_slider == null || _sliderMax != max)
            {
                int index = _slider == null ? -1 : _slider.transform.GetSiblingIndex();
                if (_slider != null) PanelKit.Destroy(_slider.gameObject);
                _slider = null;
                CreateSlider(max, percent, index);
            }
            else
            {
                ModUI.SetWithoutNotify(_slider, percent);
            }

            if (_slider != null) _slider.Title = ModL10n.L("arch.lab.funding");
        }

        private void CreateSlider(int max, int percent, int siblingIndex)
        {
            if (_group == null) return;

            _slider = ModUI.Slider(_group, ModL10n.L("arch.lab.funding"), LabBlockModel.MinPercent, max, 1f, percent, OnFundingChanged, ModL10n.L("arch.lab.fundingTip"));
            _sliderMax = max;
            if (_slider != null && siblingIndex >= 0) _slider.transform.SetSiblingIndex(siblingIndex);
        }

        // ---- the commands ----

        private void OnFundingChanged(float percent)
        {
            _ui.SetFunding(percent);
        }

        private void OnStop()
        {
            _ui.StopResearch();
        }

        private void OnResume()
        {
            _ui.ResumeResearch();
        }
    }
}
