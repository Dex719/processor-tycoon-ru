using System;
using PTMod.Features.ContractTransparency;

namespace PTMod.Tests.Features.ContractTransparency
{
    /// <summary>
    /// TSK-011...TSK-016: the line the panels are made of. A line is a key of the string table, typed arguments and a look; the formatter turns the arguments into text. The game's formatters are not in
    /// the tests, the plain one stands for them and has the same settings (decimals, decimal comma).
    /// </summary>
    [Covers(typeof(PanelLine), typeof(PanelArg), typeof(ArgKind), typeof(LineTone), typeof(IArgFormatter), typeof(PlainArgFormatter))]
    public class PanelLinesTests
    {
        private static string Show(PanelArg arg)
        {
            return new PlainArgFormatter().Format(arg);
        }

        [Test]
        public void TextAndKeys_AreKept_AndANullTextIsEmpty()
        {
            Assert.Equal("Apple", Show(PanelArg.OfText("Apple")));
            Assert.Equal(string.Empty, Show(PanelArg.OfText(null)));
            Assert.Equal("[ct.req.name.ips]", Show(PanelArg.OfKey("ct.req.name.ips")));
            Assert.Equal("[ct.req.name.ips], [ct.req.name.power]", Show(PanelArg.OfKeys("ct.req.name.ips, ct.req.name.power")));
            Assert.Equal(string.Empty, Show(PanelArg.OfKeys(string.Empty)));
            Assert.Equal(string.Empty, Show(PanelArg.OfKeys(null)));
        }

        [Test]
        public void Numbers_AreRoundedLikeThePlayerExpects()
        {
            Assert.Equal("75", Show(PanelArg.Percent(75.0)));
            Assert.Equal("11", Show(PanelArg.Percent(11.11)));
            Assert.Equal("13", Show(PanelArg.Whole(12.5)), "the midpoint goes away from zero");
            Assert.Equal("52000", Show(PanelArg.Count(52000)));
            Assert.Equal("0.8", Show(PanelArg.Ips(0.8)));
            Assert.Equal("1.25", Show(PanelArg.Multiplier(1.25)));
            Assert.Equal("299.5", Show(PanelArg.Money(299.5)));
        }

        [Test]
        public void SharesAndProbabilities_UseTheirOwnDecimals_AndTheDecimalComma()
        {
            var plain = new PlainArgFormatter();
            var detailed = new PlainArgFormatter(1, 2, true);

            Assert.Equal("35", plain.Format(PanelArg.Fraction(0.35)));
            Assert.Equal("21", plain.Format(PanelArg.Actual(0.2111)));
            Assert.Equal("35,0", detailed.Format(PanelArg.Fraction(0.35)));
            Assert.Equal("21,11", detailed.Format(PanelArg.Actual(0.2111)));
        }

        [Test]
        public void ANumberThatIsNotANumber_BecomesZero()
        {
            Assert.Equal("0", Show(PanelArg.Whole(double.NaN)));
            Assert.Equal("0", Show(PanelArg.Percent(double.PositiveInfinity)));
            Assert.Equal(0.0, PanelArg.Ips(double.NegativeInfinity).Number);
        }

        [Test]
        public void ALine_KeepsItsKeyArgumentsAndLook()
        {
            var line = new PanelLine("ct.req.ips", LineTone.Bad, PanelArg.Ips(0.8), PanelArg.Percent(75)).AsBold().Indented(2);

            Assert.Equal("ct.req.ips", line.Key);
            Assert.Equal(LineTone.Bad, line.Tone);
            Assert.True(line.Bold);
            Assert.Equal(2, line.Indent);
            Assert.Equal(2, line.Args.Length);
            Assert.Equal(new[] { "0.8", "75" }, line.Format(new PlainArgFormatter()));
        }

        [Test]
        public void ALineWithoutArguments_HasNoneAndANegativeIndentIsZero()
        {
            var line = new PanelLine(null, LineTone.Muted);

            Assert.Equal(string.Empty, line.Key);
            Assert.Equal(0, line.Args.Length);
            Assert.Equal(0, new PanelLine("k", LineTone.Normal).Indented(-3).Indent);
            Assert.Equal(0, line.Format(new PlainArgFormatter()).Length);
        }

        [Test]
        public void EveryKindHasAConstructor_AndTheFormatterAnswersForAllOfThem()
        {
            foreach (ArgKind kind in (ArgKind[])Enum.GetValues(typeof(ArgKind)))
            {
                var arg = new PanelArg { Kind = kind, Number = 1, Text = "x" };
                Assert.NotNull(Show(arg), kind.ToString());
            }
        }
    }
}
