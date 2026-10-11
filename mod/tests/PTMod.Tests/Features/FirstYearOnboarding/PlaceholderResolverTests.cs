using System;
using System.Collections.Generic;
using PTMod.Features.FirstYearOnboarding.Logic;

namespace PTMod.Tests.Features.FirstYearOnboarding
{
    /// <summary>
    /// The values of the placeholders of a mail (TSK-012, FR-5 item 2): every name of the white list, the order of <c>args</c>, the unknown name, a source
    /// that throws. The source here is a table; the game's numbers are read by <c>GamePlaceholderSource</c>.
    /// </summary>
    [Covers(typeof(PlaceholderResolver), typeof(IPlaceholderSource))]
    public class PlaceholderResolverTests
    {
        private sealed class TableSource : IPlaceholderSource
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly List<string> Asked = new List<string>();
            public bool Throws;

            public bool TryGet(string name, out string value)
            {
                Asked.Add(name);
                if (Throws) throw new InvalidOperationException("the game is not there");
                return Values.TryGetValue(name, out value);
            }
        }

        private static TableSource FullTable()
        {
            var source = new TableSource();
            source.Values["highEndBudget"] = "$599";
            source.Values["midRangeBudget"] = "$249";
            source.Values["lowEndBudget"] = "$149";
            source.Values["industriesBudget"] = "$79";
            source.Values["debtLimit"] = "$111M";
            source.Values["defaultDays"] = "120";
            source.Values["money"] = "$5,000";
            source.Values["company"] = "Garage Chips";
            source.Values["founder"] = "Alex";
            return source;
        }

        [Test]
        public void EveryNameOfTheWhiteList_IsFilled()
        {
            TableSource source = FullTable();
            var warnings = new List<string>();

            string[] values = PlaceholderResolver.ResolveArgs(StepValidator.ArgNames, source, warnings.Add);

            Assert.Equal(StepValidator.ArgNames.Count, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                Assert.Equal(source.Values[StepValidator.ArgNames[i]], values[i], StepValidator.ArgNames[i]);
            }
            Assert.Equal(0, warnings.Count, "nothing to warn about: " + string.Join("; ", warnings));
        }

        [Test]
        public void TheValuesFollowTheOrderOfTheNames_NotTheOrderOfTheTable()
        {
            TableSource source = FullTable();

            string[] values = PlaceholderResolver.ResolveArgs(new[] { "lowEndBudget", "highEndBudget", "industriesBudget", "midRangeBudget" }, source);

            Assert.Equal(new[] { "$149", "$599", "$79", "$249" }, values, "{0} is the first name, {1} the second");
        }

        [Test]
        public void AnUnknownName_GivesAQuestionMark_AndAWarning_AndTheOthersStillFill()
        {
            TableSource source = FullTable();
            var warnings = new List<string>();

            string[] values = PlaceholderResolver.ResolveArgs(new[] { "company", "nonsense", "founder" }, source, warnings.Add);

            Assert.Equal(new[] { "Garage Chips", "?", "Alex" }, values);
            Assert.Equal(1, warnings.Count);
            Assert.True(warnings[0].Contains("nonsense"), "the warning names the placeholder: " + warnings[0]);
        }

        [Test]
        public void ANameTheSourceCannotReadNow_IsUnknownToo()
        {
            var source = new TableSource();   // the game is not ready: no value at all
            var warnings = new List<string>();

            string[] values = PlaceholderResolver.ResolveArgs(new[] { "debtLimit" }, source, warnings.Add);

            Assert.Equal(new[] { PlaceholderResolver.Unknown }, values);
            Assert.Equal(1, warnings.Count);
        }

        [Test]
        public void ASourceThatThrows_DoesNotFailTheMail()
        {
            var source = new TableSource { Throws = true };
            var warnings = new List<string>();

            string[] values = PlaceholderResolver.ResolveArgs(new[] { "money", "company" }, source, warnings.Add);

            Assert.Equal(new[] { "?", "?" }, values);
            Assert.Equal(2, warnings.Count);
            Assert.True(warnings[0].Contains("money") && warnings[0].Contains("InvalidOperationException"), warnings[0]);
        }

        [Test]
        public void NoNames_GiveAnEmptyArray_AndNeverAskTheSource()
        {
            TableSource source = FullTable();

            Assert.Equal(0, PlaceholderResolver.ResolveArgs(null, source).Length);
            Assert.Equal(0, PlaceholderResolver.ResolveArgs(new string[0], source).Length);
            Assert.Equal(0, PlaceholderResolver.ResolveArgs(new List<string>(), source).Length);
            Assert.Equal(0, source.Asked.Count);
        }

        [Test]
        public void ANullOrBlankName_IsUnknown()
        {
            TableSource source = FullTable();
            var warnings = new List<string>();

            string[] values = PlaceholderResolver.ResolveArgs(new[] { null, string.Empty, "money" }, source, warnings.Add);

            Assert.Equal(new[] { "?", "?", "$5,000" }, values);
            Assert.Equal(2, warnings.Count);
            Assert.Equal(1, source.Asked.Count, "the source is asked about the real name only");
        }

        [Test]
        public void WithoutASource_EveryNameIsUnknown()
        {
            string[] values = PlaceholderResolver.ResolveArgs(new[] { "money", "company" }, null);

            Assert.Equal(new[] { "?", "?" }, values);
        }

        [Test]
        public void ASourceThatSaysYesWithNoText_IsUnknown()
        {
            var source = new TableSource();
            source.Values["money"] = null;

            Assert.Equal("?", PlaceholderResolver.Resolve("money", source));
        }

        [Test]
        public void TheSameNameTwice_IsFilledTwice()
        {
            TableSource source = FullTable();

            string[] values = PlaceholderResolver.ResolveArgs(new[] { "money", "money" }, source);

            Assert.Equal(new[] { "$5,000", "$5,000" }, values);
        }

        [Test]
        public void TheShippedSteps_UseOnlyNamesTheWhiteListHas_SoNoMailShowsAQuestionMark()
        {
            var context = ShippedContext.Load();
            StepCatalog catalog = context.BuildCatalog();
            TableSource source = FullTable();
            var warnings = new List<string>();

            foreach (StepDefinition step in catalog.All)
            {
                string[] values = PlaceholderResolver.ResolveArgs(step.Mail.Args, source, warnings.Add);
                Assert.False(Array.IndexOf(values, PlaceholderResolver.Unknown) >= 0, step.Id + " has a placeholder the resolver does not know");

                foreach (ReminderSpec reminder in step.Reminders)
                {
                    values = PlaceholderResolver.ResolveArgs(reminder.Args, source, warnings.Add);
                    Assert.False(Array.IndexOf(values, PlaceholderResolver.Unknown) >= 0, step.Id + " hint " + reminder.HintKey + " has a placeholder the resolver does not know");
                }
            }
            Assert.Equal(0, warnings.Count, string.Join("; ", warnings));
        }
    }
}
