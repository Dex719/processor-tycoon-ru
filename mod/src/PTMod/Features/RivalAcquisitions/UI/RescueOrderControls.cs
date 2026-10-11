using System;
using PTMod.Core;
using SliderUI = ProcessorTycoon.UI.SliderUI;
using ToggleUI = ProcessorTycoon.UI.ToggleUI;
using UnityEngine;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The switch "Rescue order" and the slider of its limit (TSK-015, TSK-017; FR-6 item 4; design.md "Окно «Сделки»", "Диалог доли"), shared by the tab "My stakes" and the
    /// dialog of a stake. A change goes straight to <c>StakeService.SetRescueOrder</c> (the order is kept in the section of the side save); the slider counts in millions
    /// on a scale that suits the company (<see cref="RescueScale"/>). Switching the order on for the first time starts it at about one credit limit of the company.
    /// </summary>
    internal static class RescueOrderControls
    {
        private sealed class Local
        {
            public bool Active;
            public double Amount;
        }

        /// <summary>
        /// Puts the controls into <paramref name="parent"/>. <paramref name="enabled"/> false (the player is in default) leaves them inactive. <paramref name="onChanged"/>
        /// is called after the switch is flipped (the window refreshes its rows); the slider does not call it, so it is not destroyed under the hand.
        /// </summary>
        internal static void Build(RectTransform parent, int uniqueId, int saveId, double creditLimit, bool active, double maxAmount, bool enabled, Action onChanged)
        {
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            if (feature == null) return;

            RescueScale scale = RescueScale.For(creditLimit, maxAmount);
            var local = new Local { Active = active, Amount = maxAmount > 0.0 ? maxAmount : scale.DefaultAmount };

            ViewFormat f = AcqUi.Format;
            double minInjection = creditLimit > 0.0 ? Math.Max(0.0, creditLimit * (1.0 + feature.Balance.Stake.RescueTargetDebtLimitShare)) : 0.0;
            string hint = ModL10n.L("acq.rescue.hint", f.M(minInjection));

            ToggleUI toggle = ModUI.Toggle(parent, ModL10n.L("acq.rescue.toggle"), active, on =>
            {
                local.Active = on;
                Apply(uniqueId, saveId, local);
                if (onChanged != null) onChanged();
            }, hint);

            SliderUI slider = ModUI.Slider(parent, ModL10n.L("acq.rescue.limit"), 0f, (float)scale.MaxUnits, (float)scale.Step,
                (float)scale.ToUnits(local.Amount), units =>
                {
                    local.Amount = scale.ToAmount(units);
                    Apply(uniqueId, saveId, local);
                }, hint);

            if (toggle != null) toggle.Interactable = enabled;
            if (slider != null) slider.Interactable = enabled;
            ModUI.Text(parent, hint);
        }

        private static void Apply(int uniqueId, int saveId, Local local)
        {
            RivalAcquisitionsFeature feature = RivalAcquisitionsFeature.Instance;
            if (feature == null || feature.Stakes == null) return;

            // A limit moved on a switched-off order that was never set leaves no record behind.
            if (!local.Active && StakeBook.Order(feature.State, uniqueId, saveId) == null) return;
            feature.Stakes.SetRescueOrder(uniqueId, local.Amount, local.Active);
        }
    }
}
