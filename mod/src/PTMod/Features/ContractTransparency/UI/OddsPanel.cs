using System;
using System.Collections.Generic;
using PTMod.Core;
using TMPro;
using UnityEngine;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameCpuProvider = ProcessorTycoon.Hardware.CpuDataProvider;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The window "Signing chance: breakdown" (design.md, Component 11; FR-1, FR-2, FR-12; NFR-5): for the contract and the CPU chosen in the contract window, why the CPU passes or fails the three
    /// requirements, and what the chance is made of.
    /// <list type="bullet">
    /// <item><b>Requirements</b>: three lines with the numbers (<see cref="RequirementLines"/>), red or green; a red line says how much is missing; the verdict is the game's. A CPU that fails gets no table of
    /// chances: the game turns the offer down and the chance is 0 %.</item>
    /// <item><b>Chances</b>: the summary (the chance of the game and the probability the draw picks the CPU), then the table of the best offer of each company in the order of the draw (company, CPU, IPS,
    /// the vanilla chance, the chance of the game, the probability), the CPU of the player marked; then the notes and the hints (<see cref="OddsLines"/>): the mods behind the chance with their reasons,
    /// the own CPUs the review will not count, the IPS needed for 50 % and for the first place; at the end the block "possible participants" (<see cref="PotentialLines"/>, FR-11): the AI companies that may
    /// still bid before the review date with their best fitting CPU, the probability if all of them do, and the notes "estimate" and, on difficulty 5, "the AI is more active". It is left out when the AI has no
    /// monthly move left before the review.</item>
    /// <item><b>Window</b>: <c>WindowTemplate.Panel</c> (the time goes on), 560 x 520, one scrolling area that is cleared and filled at every opening; redrawn when the theme changes.</item>
    /// </list>
    /// The numbers come from <see cref="ContractInspector"/>, which has checked them against the game (FR-12). A missing private member of the game shows a line saying so instead of a breakdown.
    /// To check in the game (the owner, M1, M4, M14): the panel opens in one click from "Why?"; the chance of the game equals the percent of the contract window; for two offers the probability matches the
    /// formula of design.md; with <c>platform-generations</c> on, the line "mod modifiers" appears with the reason of that feature.
    /// </summary>
    internal static class OddsPanel
    {
        private const string Feature = ContractParamSpecs.FeatureId;
        private const string WindowId = Feature + ".odds";
        private const string OpenSite = "OddsPanel/Open";
        private const string RenderSite = "OddsPanel/Render";
        private const string ThemeSite = "OddsPanel/ThemeChanged";

        private static readonly Vector2 WindowSize = new Vector2(560f, 520f);

        // The columns of the table: position, company, CPU (the two names share the free width), IPS, vanilla chance, the chance of the game, the probability.
        private const float PositionWidth = 22f;
        private const float NumberWidth = 62f;

        private static ModWindow _window;
        private static RectTransform _body;
        private static GameContract _contract;
        private static GameCpu _cpu;
        private static bool _themeHooked;

        /// <summary>The window is on the screen.</summary>
        internal static bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>
        /// Opens the breakdown for <paramref name="contract"/> and <paramref name="cpu"/> (null for either: the window asks the player to choose). Does nothing while the feature is not active or the
        /// templates of the scene are not ready.
        /// </summary>
        internal static void Open(GameContract contract, GameCpu cpu)
        {
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (feature == null || !feature.IsActive) return;

            ContractTransparencyFeature.Guarded(OpenSite, () =>
            {
                if (!EnsureWindow()) return;
                _contract = contract;
                _cpu = cpu;
                Render();
                _window.Open();
            });
        }

        /// <summary>Closes the window when it is open.</summary>
        internal static void Close()
        {
            if (_window != null && _window.IsAlive) _window.Close();
        }

        // ---- the window ----

        private static bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive && _body != null) return true;
            _window = null;
            _body = null;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the breakdown cannot be opened, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "ct.odds.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            UiKit.Clear(window.Content);   // the window of an earlier attempt that failed halfway is the same object: start from an empty frame
            RectTransform body = UiKit.Scroll(window.Content, 200f, WindowSize.y - 90f, 1f);
            if (body == null)
            {
                ModLog.Warn(Feature, "ui: the scroll area of the breakdown could not be made");
                return false;
            }

            _window = window;
            _body = body;
            if (!_themeHooked)
            {
                _themeHooked = true;
                ModUI.ThemeChanged += OnThemeChanged;   // static: the handler stays for the session and looks at the window of the moment
            }
            return true;
        }

        private static void OnThemeChanged()
        {
            if (!IsOpen) return;
            ContractTransparencyFeature.Guarded(ThemeSite, Render);
        }

        // ---- the contents ----

        private static void Render()
        {
            if (_body == null) return;
            UiKit.Clear(_body);

            GameArgFormatter formatter = GameArgFormatter.Current();
            ContractTransparencyFeature feature = ContractTransparencyFeature.Instance;
            if (feature == null) return;

            if (_contract == null || _cpu == null)
            {
                UiKit.AddLine(_body, new PanelLine("ct.odds.pick", LineTone.Muted), formatter);
                return;
            }

            ContractInspector inspector = feature.Inspector;
            UiKit.AddLine(_body, new PanelLine("ct.odds.subject", LineTone.Normal, PanelArg.OfText(ContractInspector.ContractLabel(_contract)), PanelArg.OfText(_cpu.Name)).AsBold(), formatter);

            if (!inspector.IsAvailable)
            {
                UiKit.AddLine(_body, new PanelLine("ct.odds.unavailable", LineTone.Warn, PanelArg.OfText(inspector.UnavailableReason)), formatter);
                return;
            }

            // Requirements (FR-1).
            UiKit.AddLine(_body, new PanelLine("ct.odds.section.requirements", LineTone.Normal).AsBold(), formatter);
            RequirementReport requirements = inspector.BuildRequirements(_contract, _cpu);
            UiKit.AddLines(_body, RequirementLines.Build(new RequirementPanelInput
            {
                Ips = requirements.Ips,
                Power = requirements.Power,
                Package = requirements.Package,
                Tolerance = requirements.Tolerance,
                RequiredPackageText = requirements.RequiredPackageText,
                CpuPackageText = requirements.CpuPackageText,
                FailsByGame = requirements.FailsByGame,
                MismatchWithGame = requirements.MismatchWithGame,
            }), formatter);
            if (requirements.Fails) return;   // the game turns the offer down: there is no chance to explain

            // The chance (FR-2).
            UiKit.AddLine(_body, new PanelLine("ct.odds.section.chance", LineTone.Normal).AsBold(), formatter);
            OddsReport odds = inspector.BuildOdds(_contract, _cpu);
            List<PanelLine> lines = OddsLines.Build(InputOf(feature, odds));

            if (lines.Count > 0) AddSummary(_body, lines[0], formatter);
            AddTable(_body, odds, formatter);
            for (int i = 1; i < lines.Count; i++) UiKit.AddLine(_body, lines[i], formatter);

            // Possible participants (FR-11): the AI companies that may still bid before the review date, the probability if all of them do, and the notes that this is an estimate.
            PotentialReport potential = inspector.BuildPotential(_contract, _cpu, odds);
            UiKit.AddLines(_body, PotentialLines.Build(PotentialInputOf(feature, potential)), formatter);
        }

        private static PotentialPanelInput PotentialInputOf(ContractTransparencyFeature feature, PotentialReport potential)
        {
            var input = new PotentialPanelInput
            {
                MovesLeft = potential.MovesLeft,
                CurrentActual = potential.CurrentActual,
                PessimisticActual = potential.PessimisticActual,
                LowActualChance = feature.Rules.LowActualChance,
                HardDifficulty = potential.HardDifficulty,
                Rows = new List<PotentialRow>(),
            };

            foreach (OfferInput bidder in potential.NewBidders)
            {
                GameCpu cpu = CpuOf(bidder.CpuId);
                input.Rows.Add(new PotentialRow
                {
                    Company = cpu == null || cpu.Company == null ? bidder.CompanyKey ?? string.Empty : cpu.Company.Name,
                    Cpu = cpu == null ? "#" + bidder.CpuId.ToString(System.Globalization.CultureInfo.InvariantCulture) : cpu.Name,
                    Ips = bidder.Ips,
                });
            }
            return input;
        }

        private static OddsPanelInput InputOf(ContractTransparencyFeature feature, OddsReport odds)
        {
            var input = new OddsPanelInput
            {
                NoOffersYet = odds.NoOffersYet,
                GameChancesAvailable = odds.GameChancesAvailable,
                ChanceDiffersFromVanilla = odds.ChanceDiffersFromVanilla,
                ModsCapped = odds.ModsCapped,
                Mismatch = odds.MismatchWithGame,
                VanillaChance = odds.CandidateVanillaChance,
                GameChance = odds.CandidateGameChance,
                Actual = odds.CandidateActual,
                ModMultiplier = odds.ModMultiplier,
                LowActualChance = feature.Rules.LowActualChance,
                CandidateCpuId = odds.CandidateCpuId,
                Rows = odds.Rows,
                IpsFor50 = odds.IpsFor50,
                IpsForFirstInQueue = odds.IpsForFirstInQueue,
                ShadowedCount = odds.ShadowedOwnCpuIds == null ? 0 : odds.ShadowedOwnCpuIds.Count,
                StrongestOwnName = NameOfCpu(odds.StrongestOwnCpuId),
                ReviewDateText = ReviewDateText(_contract),
                Sources = new List<ModSourceText>(),
            };

            foreach (ModifierSourceRow source in odds.ModSources)
            {
                input.Sources.Add(new ModSourceText { Feature = source.FeatureId, Reason = source.ReasonText, Multiplier = source.Multiplier });
            }
            return input;
        }

        /// <summary>The summary line, with the formula of the probability in its tooltip.</summary>
        private static void AddSummary(RectTransform parent, PanelLine line, GameArgFormatter formatter)
        {
            TMP_Text text = UiKit.AddLine(parent, line, formatter);
            if (text == null) return;

            text.raycastTarget = true;   // a tooltip needs something to hover
            ModUI.Tooltip(text.gameObject, ModL10n.L("ct.odds.summary.tipTitle"), ModL10n.L("ct.odds.summary.tip"));
        }

        /// <summary>The table of the best offers in the order of the draw; the row of the CPU that is analysed is bold and green.</summary>
        private static void AddTable(RectTransform parent, OddsReport odds, GameArgFormatter formatter)
        {
            if (odds.Rows == null || odds.Rows.Count == 0) return;

            AddTableRow(parent, new[] { "#", ModL10n.L("ct.odds.col.company"), ModL10n.L("ct.odds.col.cpu"), ModL10n.L("ct.odds.col.ips"),
                                        ModL10n.L("ct.odds.col.vanilla"), ModL10n.L("ct.odds.col.game"), ModL10n.L("ct.odds.col.actual") }, LineTone.Muted, false);

            for (int i = 0; i < odds.Rows.Count; i++)
            {
                OfferOdds row = odds.Rows[i];
                bool own = row.CpuId == odds.CandidateCpuId;
                GameCpu cpu = CpuOf(row.CpuId);
                string company = cpu == null || cpu.Company == null ? row.CompanyKey ?? string.Empty : cpu.Company.Name;
                string cpuName = cpu == null ? "#" + row.CpuId.ToString(System.Globalization.CultureInfo.InvariantCulture) : cpu.Name;

                AddTableRow(parent, new[]
                {
                    (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    TextSafe.EscapeMarkup(company),
                    TextSafe.EscapeMarkup(cpuName),
                    formatter.Format(PanelArg.Ips(row.Ips)),
                    formatter.Format(PanelArg.Whole(row.VanillaChance)) + " %",
                    formatter.Format(PanelArg.Whole(row.GameChance)) + " %",
                    formatter.Format(PanelArg.Actual(row.ActualProbability)) + " %",
                }, own ? LineTone.Good : LineTone.Normal, own);
            }
        }

        private static void AddTableRow(RectTransform parent, string[] cells, LineTone tone, bool bold)
        {
            RectTransform row = UiKit.Row(parent, 0f, 6f, TextAnchor.UpperLeft);
            for (int i = 0; i < cells.Length; i++)
            {
                string text = UiKit.Decorate(cells[i], tone, bold, 0);
                if (i == 0) UiKit.Cell(row, text, PositionWidth, 0f);
                else if (i == 1 || i == 2) UiKit.Cell(row, text, 90f, 1f);
                else UiKit.Cell(row, text, NumberWidth, 0f, TextAlignmentOptions.TopRight);
            }
        }

        // ---- names ----

        private static GameCpu CpuOf(int cpuId)
        {
            return GameCpuProvider.Instance == null ? null : GameCpuProvider.Instance.GetCpu(cpuId);
        }

        private static string NameOfCpu(int cpuId)
        {
            GameCpu cpu = CpuOf(cpuId);
            return cpu == null ? string.Empty : cpu.Name;
        }

        /// <summary>The review date of the contract for people ("January 1987"): when it is decided, the end of the AI's monthly bids.</summary>
        private static string ReviewDateText(GameContract contract)
        {
            if (contract == null || contract.AcceptanceDate == null) return string.Empty;
            return UiKit.MonthYearText((DateTime)contract.AcceptanceDate);
        }
    }
}
