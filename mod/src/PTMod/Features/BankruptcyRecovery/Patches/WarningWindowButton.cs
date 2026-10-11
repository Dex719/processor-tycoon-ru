using PTMod.Core;
using UnityEngine;
using UnityEngine.UI;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using GameBankruptcyWarningWindow = ProcessorTycoon.Bank.BankruptcyWarningWindow;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The button "Recovery options" in the game's own warning window (patch P4, FR-2 item 1). The window appears once per loaded scene, on the first default
    /// (the game never resets its flag), so the button is made once per scene: the reference to it is kept, and when the scene is left the window and the
    /// button are destroyed with it and the next window gets a new one. The other doors to the menu (the action of the letter, the page in the hub) do not
    /// depend on this window.
    /// <para>
    /// The button is a clone of the game's own (<c>ModUI.Button</c>), so it has the look and the theme of the game. The layout of the window is not known from
    /// the code (it is in the scene), so when the window has no layout group of its own the button is put at the bottom centre; the place and the size are
    /// to be checked in the game (scenario M2) and adjusted here. If the clone cannot be made the window stays as it was.
    /// </para>
    /// </summary>
    internal static class WarningWindowButton
    {
        private static readonly Vector2 Size = new Vector2(240f, 32f);
        private const float BottomMargin = 16f;

        private static ButtonUI _button;

        /// <summary>Adds the button to the window if this scene has none yet.</summary>
        public static void Ensure(GameBankruptcyWarningWindow window)
        {
            if (window == null) return;
            if (_button != null) return;   // Unity's null: a button of a scene that was left counts as gone

            ButtonUI button = ModUI.Button(window.transform, ModL10n.L("rec.menu.open"), OpenMenu);
            if (button == null) return;
            _button = button;

            var rect = button.transform as RectTransform;
            if (rect == null || window.GetComponent<LayoutGroup>() != null) return;

            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = Size;
            rect.anchoredPosition = new Vector2(0f, BottomMargin);
        }

        private static void OpenMenu()
        {
            RecoveryApi.OpenMenu(RecoveryTab.Summary);
        }
    }
}
