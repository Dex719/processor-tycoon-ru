using System.Text.RegularExpressions;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>The TextMeshPro tags around a line of a panel: none for a plain text, always closed in the reverse order of their opening otherwise.</summary>
    [Covers(typeof(PanelMarkup))]
    public class PanelMarkupTests
    {
        [Test]
        public void APlainText_GetsNoTag()
        {
            Assert.Equal("Apple", PanelMarkup.Wrap("Apple", null, false, false, 0));
            Assert.Equal("Apple", PanelMarkup.Wrap("Apple", string.Empty, false, false, 0));
            Assert.Equal("Apple", PanelMarkup.Wrap("Apple", null, false, false, -2), "a negative indent is none");
        }

        [Test]
        public void AnEmptyText_StaysEmpty_WhateverTheLook()
        {
            Assert.Equal(string.Empty, PanelMarkup.Wrap(null, "#FF0000", true, true, 3));
            Assert.Equal(string.Empty, PanelMarkup.Wrap(string.Empty, "#FF0000", true, true, 3));
        }

        [Test]
        public void AColour_IsOneTagPair()
        {
            Assert.Equal("<color=#C03030>x</color>", PanelMarkup.Wrap("x", "#C03030", false, false, 0));
        }

        [Test]
        public void Bold_WrapsTheColour()
        {
            Assert.Equal("<b><color=#00AA00>x</color></b>", PanelMarkup.Wrap("x", "#00AA00", false, true, 0));
        }

        [Test]
        public void AMutedLine_IsDimmedAndTheDimmingIsEnded()
        {
            Assert.Equal("<alpha=#A0>x<alpha=#FF>", PanelMarkup.Wrap("x", null, true, false, 0));
        }

        [Test]
        public void TheIndent_IsInLetterWidths_AndComesFirst()
        {
            Assert.Equal("<indent=1.5em>x", PanelMarkup.Wrap("x", null, false, false, 1));
            Assert.Equal("<indent=3em><b>x</b>", PanelMarkup.Wrap("x", null, false, true, 2));
        }

        [Test]
        public void EveryLook_ClosesWhatItOpens()
        {
            string[] colours = { null, "#112233" };
            foreach (string colour in colours)
            {
                foreach (bool muted in new[] { false, true })
                {
                    foreach (bool bold in new[] { false, true })
                    {
                        string text = PanelMarkup.Wrap("x", colour, muted, bold, 2);
                        Assert.Equal(CountOpen(text, "<b>"), CountOpen(text, "</b>"), text);
                        Assert.Equal(CountOpen(text, "<color="), CountOpen(text, "</color>"), text);
                        Assert.True(text.Contains(">x<") || text.EndsWith(">x") || text.Contains(">x"), text);
                    }
                }
            }
        }

        private static int CountOpen(string text, string tag)
        {
            return Regex.Matches(text, Regex.Escape(tag)).Count;
        }
    }
}
