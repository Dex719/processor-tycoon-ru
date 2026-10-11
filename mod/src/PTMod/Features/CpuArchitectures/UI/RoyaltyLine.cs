using System;
using PTMod.Core;
using TMPro;
using UnityEngine;
using GameFormatter = ProcessorTycoon.StringFormatter;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The line "Роялти x86: N/мес" under the support of the panel (FR-16 item 6, stage 2; design.md, "UI"): what the player's company paid last month for the x86 CPUs it sold
    /// without holding the licence (<see cref="RoyaltyService.LastMonthRoyaltyOf"/>). When the licence is revoked or no holder is alive it says that x86 is free; its tooltip
    /// gives the rate. The player never holds the licence, so the line is for him whenever the royalty is played at all. Hidden at stage 1; it only draws what the service worked out.
    /// </summary>
    internal sealed class RoyaltyLine
    {
        private readonly ArchitecturesFeature _feature;
        private TMP_Text _line;

        public RoyaltyLine(ArchitecturesFeature feature)
        {
            if (feature == null) throw new ArgumentNullException("feature");
            _feature = feature;
        }

        public bool IsAlive
        {
            get { return _line != null; }
        }

        /// <summary>The line is made hidden; the tooltip is made when the pointer comes.</summary>
        public void Build(Transform parent)
        {
            _line = PanelKit.Line(parent, false, 0f);
            if (_line == null) return;
            ModUI.Tooltip(_line.gameObject, TooltipOf);
            _line.gameObject.SetActive(false);
        }

        /// <summary>Redraws the line for the player's company; hides it when the royalty is not played.</summary>
        public void Refresh(ICompany player)
        {
            if (!IsAlive) return;

            RoyaltyService service = _feature.Royalty;
            bool shown = service != null && service.CanRun && !ReferenceEquals(player, null);
            if (_line.gameObject.activeSelf != shown) _line.gameObject.SetActive(shown);
            if (!shown) return;

            if (service.IsFree)
            {
                PanelKit.Set(_line, PanelKit.Tinted(ModL10n.L("arch.royalty.free"), PanelTone.Muted));
                return;
            }

            float paid = service.LastMonthRoyaltyOf(player);
            string text = ModL10n.L("arch.royalty.line", GameFormatter.FloatToMoney(paid, string.Empty, true, true, true));
            PanelKit.Set(_line, paid > 0f ? PanelKit.Tinted(text, PanelTone.Warn) : PanelKit.Tinted(text, PanelTone.Muted));
        }

        private ModUI.TooltipText TooltipOf()
        {
            float rate = _feature.Balance.x86License.royaltyRate;
            return new ModUI.TooltipText(ModL10n.L("arch.royalty.title"), ModL10n.L("arch.royalty.tip", SupportMath.PercentOf(rate)));
        }

        /// <summary>Forgets the widget (its scene is going or the panel could not be made).</summary>
        public void Drop()
        {
            if (_line != null) PanelKit.Destroy(_line.gameObject);
            _line = null;
        }
    }
}
