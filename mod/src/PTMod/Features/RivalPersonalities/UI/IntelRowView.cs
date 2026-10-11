using System;
using PTMod.Core;
using UnityEngine;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// One company in the list of the intelligence window (design.md, "UI"; FR-9 item 2): a button with the name and the attitude (in the colour of its
    /// level), and under it a dimmed line with the character (or "Character unknown"), the segments, the share and the last CPU. The chosen company is
    /// drawn with the main style of the buttons of the game; a bankrupt one is grey and sits at the bottom. The numbers come ready from
    /// <see cref="IntelBuilder"/>, so the fog is already in them. Main thread only.
    /// </summary>
    internal static class IntelRowView
    {
        /// <summary>Makes the row inside <paramref name="list"/>. <paramref name="onSelect"/> gets the <c>UniqueID</c> of the company.</summary>
        public static void Build(Transform list, IntelRow row, bool selected, Action<int> onSelect)
        {
            RectTransform box = ModUI.VerticalList(list, 2f);
            if (box == null) return;

            int uniqueId = row.UniqueId;
            string caption = Caption(row);
            Action click = () =>
            {
                if (onSelect != null) onSelect(uniqueId);
            };

            ButtonUI button = ModUI.Button(box, caption, click, selected ? ButtonStyle.CallToAction : ButtonStyle.Standard);
            if (button == null && selected) button = ModUI.Button(box, caption, click);
            IntelUi.StyleCaption(button);

            ModUI.Text(box, IntelUi.Paint(row.SummaryText, IntelTone.Muted));
        }

        /// <summary>The caption of the button: the name, then the attitude with its sign in the colour of its level; a bankrupt company is all grey.</summary>
        internal static string Caption(IntelRow row)
        {
            if (row.IsBankrupt) return IntelUi.Paint(row.Name, IntelTone.Muted);
            return IntelUi.Escape(row.Name) + "   " + IntelUi.Paint(row.AttitudeText, row.AttitudeTone);
        }
    }
}
