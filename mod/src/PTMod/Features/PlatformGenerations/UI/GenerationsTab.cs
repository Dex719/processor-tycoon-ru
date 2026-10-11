using System.Collections.Generic;
using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The tab "Поколения" of the window "Платформы" (FR-9 item 2, AC-9.1): a card for every generation, the newest first - the real name of the platform
    /// and its status, the sockets, the memory, the dates of the announcement and of the launch, the pioneer, the early adopters, and the adoption by OEM
    /// clients on the day as a bar and a percent. The facts are <see cref="GenerationsView"/>'s (pure, tested); this class only lays them out. Without
    /// generations (none could be loaded) it says so.
    /// </summary>
    internal static class GenerationsTab
    {
        private const float AdoptionLabelWidth = 230f;
        private const float BarWidth = 170f;
        private const float BarHeight = 12f;

        /// <summary>Fills the emptied body of the window.</summary>
        public static void Build(RectTransform body, PlatformGenerationsFeature feature)
        {
            PlatformSnapshot snapshot = PlatformWindowData.Snapshot(feature);
            ViewText text = PlatformViewText.Instance;
            List<GenerationCard> cards = GenerationsView.Build(snapshot.Catalog, snapshot.Timeline, snapshot.InstalledBase, snapshot.Today, text);

            if (cards.Count == 0)
            {
                ModUI.Text(body, ModL10n.L("pg.window.empty"));   // none could be loaded, or the game is not up yet: either way there is nothing to list
                return;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                if (i > 0) PlatformUi.Rule(body);
                AddCard(body, cards[i]);
            }
        }

        private static void AddCard(RectTransform body, GenerationCard card)
        {
            RectTransform list = ModUI.VerticalList(body, 2f);
            if (list == null) return;

            ModUI.Text(list, card.Title + "  -  " + card.StatusText, true);
            ModUI.Text(list, string.Join("\n", card.Lines.ToArray()));

            RectTransform row = PlatformUi.Row(list, "PTModPgAdoption", 18f, 8f, false);
            PlatformUi.Cell(row, card.AdoptionText, AdoptionLabelWidth, false, false);
            PlatformUi.Bar(row, card.Adoption, BarWidth, BarHeight);
        }
    }
}
