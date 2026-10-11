using System.Collections.Generic;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The plain data types of the public API of the research tree (<c>Api/Dto</c>), compiled by the test builds together with <c>Logic</c>: the values
    /// other features fill in (<see cref="CompareRow"/>, <see cref="ModifierLine"/>, <see cref="ModifierLineGroup"/>) and the direction of a compare row.
    /// A provider of another feature relies on the defaults below: it sets only the fields it knows, the window fills in the rest.
    /// </summary>
    [Covers(typeof(Better), typeof(CompareRow), typeof(ModifierLine), typeof(ModifierLineGroup))]
    public class ApiDtoTests
    {
        [Test]
        public void Better_KeepsTheNumbersOfTheSpec()
        {
            Assert.Equal(0, (int)Better.None);
            Assert.Equal(1, (int)Better.Higher);
            Assert.Equal(2, (int)Better.Lower);
        }

        [Test]
        public void CompareRow_NewRow_HasNeutralDefaults()
        {
            var row = new CompareRow();

            Assert.Null(row.Id);
            Assert.Null(row.ValueA);
            Assert.Null(row.ValueB);
            Assert.Null(row.NumA, "a text row has no numbers");
            Assert.Null(row.NumB);
            Assert.Equal(Better.None, row.Better, "no highlight unless the provider asks for it");
            Assert.Equal(0, row.Order);
            Assert.Null(row.DeltaText, "the window fills in the difference");
            Assert.Equal(0, row.Winner, "no side is highlighted before the window compares the numbers");
        }

        [Test]
        public void ModifierLine_NewLine_HasNoMultiplierAndIsNotCapped()
        {
            var line = new ModifierLine { LabelKey = "x.label", Args = new object[] { "a", 1 } };

            Assert.Null(line.Multiplier);
            Assert.False(line.Capped);
            Assert.Null(line.SourceFeatureId, "a line of the game itself");
            Assert.Equal(2, line.Args.Length);
        }

        [Test]
        public void ModifierLineGroup_NewGroup_HasAnEmptyListOfLines_NeverNull()
        {
            var group = new ModifierLineGroup { TitleKey = "x.title" };

            Assert.NotNull(group.Lines);
            Assert.Equal(0, group.Lines.Count);

            group.Lines.Add(new ModifierLine { LabelKey = "x.a", Multiplier = 1.25f });
            Assert.Equal(1, group.Lines.Count);
            Assert.Equal(1.25f, group.Lines[0].Multiplier.Value);

            var other = new ModifierLineGroup();
            Assert.False(ReferenceEquals(group.Lines, other.Lines), "every group has its own list");
            Assert.True(other.Lines.GetType() == typeof(List<ModifierLine>), "a plain list");
        }
    }
}
