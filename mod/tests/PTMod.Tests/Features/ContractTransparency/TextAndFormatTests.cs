using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>NFR-7 and NFR-4: strings from the game are cut and cleaned of markup before they are kept or drawn, and a chance is printed with the decimals and the separator the player wants.</summary>
    [Covers(typeof(TextSafe), typeof(ChanceFormat))]
    public class TextAndFormatTests
    {
        [Test]
        public void Clean_CutsTo128Characters_AndNeverReturnsNull()
        {
            Assert.Equal(string.Empty, TextSafe.Clean(null));
            Assert.Equal(string.Empty, TextSafe.Clean(string.Empty));
            Assert.Equal("Apple", TextSafe.Clean("Apple"));
            Assert.Equal(128, TextSafe.Clean(new string('a', 129)).Length);
            Assert.Equal(128, TextSafe.Clean(new string('a', 128)).Length);
            Assert.Equal("ab", TextSafe.Clean("abcdef", 2));
            Assert.Equal(string.Empty, TextSafe.Clean("abc", -4));
        }

        [Test]
        public void Clean_ReplacesLineBreaksAndControlCharacters()
        {
            Assert.Equal("a b c d", TextSafe.Clean("a\nb\rc\td"));
        }

        [Test]
        public void Clean_DoesNotCutASurrogatePairInHalf()
        {
            string pair = "😀";   // one character outside the BMP, two chars
            string cut = TextSafe.Clean("ab" + pair + "cd", 3);

            Assert.Equal("ab", cut, "the half pair is dropped");
        }

        [Test]
        public void EscapeMarkup_TakesTheAngleBracketsOut()
        {
            string tag = "<size=500>Big</size>";
            string escaped = TextSafe.EscapeMarkup(tag);

            Assert.False(escaped.Contains("<"));
            Assert.False(escaped.Contains(">"));
            Assert.Equal(tag.Length, escaped.Length);
            Assert.Equal(string.Empty, TextSafe.EscapeMarkup(null));
            Assert.Equal("Intel i386", TextSafe.EscapeMarkup("Intel i386"));
            Assert.Equal(TextSafe.OpenReplacement + "b" + TextSafe.CloseReplacement, TextSafe.EscapeMarkup("<b>"));
        }

        [Test]
        public void Percent_RoundsAwayFromZero_WithTheWantedDecimals()
        {
            Assert.Equal("21", ChanceFormat.Percent(0.2111, 0, false));
            Assert.Equal("21.1", ChanceFormat.Percent(0.2111, 1, false));
            Assert.Equal("21,1", ChanceFormat.Percent(0.2111, 1, true));
            Assert.Equal("78.95", ChanceFormat.Percent(0.78951, 2, false));
            Assert.Equal("13", ChanceFormat.Percent(0.125, 0, false), "the midpoint (12.5 is exact in binary) goes up");
            Assert.Equal("0", ChanceFormat.Percent(0.0, 0, false));
            Assert.Equal("100", ChanceFormat.Percent(1.0, 0, false));
            Assert.Equal("0.000", ChanceFormat.Percent(0.0, 3, false));
        }

        [Test]
        public void Percent_HoldsOddInputsInRange()
        {
            Assert.Equal("0", ChanceFormat.Percent(double.NaN, 0, false));
            Assert.Equal("0", ChanceFormat.Percent(-0.4, 0, false));
            Assert.Equal("100", ChanceFormat.Percent(7.5, 0, false));
            Assert.Equal("100", ChanceFormat.Percent(double.PositiveInfinity, 0, false));
            Assert.Equal("21", ChanceFormat.Percent(0.2123, -2, false), "decimals below 0 count as 0");
            Assert.Equal("21.234", ChanceFormat.Percent(0.21234, 9, false), "decimals above 3 count as 3");
        }

        [Test]
        public void Whole_IsCutTo0And100()
        {
            Assert.Equal("40", ChanceFormat.Whole(40));
            Assert.Equal("0", ChanceFormat.Whole(-3));
            Assert.Equal("100", ChanceFormat.Whole(250));
        }
    }
}
