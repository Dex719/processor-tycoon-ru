using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The view preferences kept in <c>ModProfile</c> (FR-8 item 2, AC-1.4): the defaults, a round trip through the JSON section, and the
    /// replacement of every bad value by its default with a note. TSK-001.
    /// </summary>
    [Covers(typeof(UxPrefsModel))]
    public class UxPrefsModelTests
    {
        private static JsonObject Parse(string json)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return (JsonObject)node;
        }

        [Test]
        public void Defaults_AreTheOnesOfTheSpec()
        {
            UxPrefsModel prefs = UxPrefsModel.Defaults();

            Assert.Equal("all", prefs.LastTab);
            Assert.Equal("tree", prefs.ViewMode);
            Assert.True(prefs.ListOnlyAvailable);
            Assert.True(prefs.ListHideResearched);
            Assert.False(prefs.ListHideUnnecessary);
            Assert.Equal("year", prefs.ListSort);
            Assert.Equal(1.0f, prefs.SidebarWidthFactor);
        }

        [Test]
        public void Read_NullOrEmptySection_GivesTheDefaultsWithoutProblems()
        {
            var problems = new List<string>();
            UxPrefsModel fromNull = UxPrefsModel.Read(null, problems);
            UxPrefsModel fromEmpty = UxPrefsModel.Read(new JsonObject(), problems);

            Assert.Equal("all", fromNull.LastTab);
            Assert.Equal("tree", fromEmpty.ViewMode);
            Assert.Equal(0, problems.Count);
        }

        [Test]
        public void Read_TheExampleOfTheSpec()
        {
            // design.md, "ModProfile: предпочтения вида".
            JsonObject section = Parse(@"{ ""lastTab"": ""cache"", ""viewMode"": ""list"", ""listOnlyAvailable"": true,
                ""listHideResearched"": true, ""listHideUnnecessary"": false, ""listSort"": ""eta"", ""sidebarWidthFactor"": 1.2 }");
            var problems = new List<string>();

            UxPrefsModel prefs = UxPrefsModel.Read(section, problems);

            Assert.Equal(0, problems.Count, string.Join("; ", problems));
            Assert.Equal("cache", prefs.LastTab);
            Assert.Equal("list", prefs.ViewMode);
            Assert.True(prefs.ListOnlyAvailable);
            Assert.True(prefs.ListHideResearched);
            Assert.False(prefs.ListHideUnnecessary);
            Assert.Equal("eta", prefs.ListSort);
            Assert.Near(1.2, prefs.SidebarWidthFactor, 1e-6);
        }

        [Test]
        public void Write_ThenRead_IsARoundTrip()
        {
            var prefs = new UxPrefsModel
            {
                LastTab = "cpu-architectures",
                ViewMode = UxPrefsModel.ViewList,
                ListOnlyAvailable = false,
                ListHideResearched = false,
                ListHideUnnecessary = true,
                ListSort = UxPrefsModel.SortCost,
                SidebarWidthFactor = 1.6f,
            };
            var section = new JsonObject();
            prefs.Write(section);

            // Through text, as the profile file does it.
            UxPrefsModel back = UxPrefsModel.Read(Parse(ModJson.Write(section, false)), null);

            Assert.Equal("cpu-architectures", back.LastTab);
            Assert.Equal("list", back.ViewMode);
            Assert.False(back.ListOnlyAvailable);
            Assert.False(back.ListHideResearched);
            Assert.True(back.ListHideUnnecessary);
            Assert.Equal("cost", back.ListSort);
            Assert.Near(1.6, back.SidebarWidthFactor, 1e-6);
        }

        [Test]
        public void Write_KeepsTheMembersItDoesNotKnow()
        {
            JsonObject section = Parse(@"{ ""futureOption"": 42, ""lastTab"": ""memory"" }");
            new UxPrefsModel { LastTab = "wafer" }.Write(section);

            Assert.Equal(42, section.GetInt32("futureOption", -1), "a member of a newer version stays");
            Assert.Equal("wafer", section.GetString("lastTab"));
            Assert.Equal(8, section.Count, "the seven preferences and the foreign member");
        }

        [Test]
        public void Write_FixesInvalidValuesBeforeTheyReachTheFile()
        {
            var section = new JsonObject();
            new UxPrefsModel { LastTab = "", ViewMode = "grid", ListSort = "color", SidebarWidthFactor = float.NaN }.Write(section);

            Assert.Equal("all", section.GetString(UxPrefsModel.KeyLastTab));
            Assert.Equal("tree", section.GetString(UxPrefsModel.KeyViewMode));
            Assert.Equal("year", section.GetString(UxPrefsModel.KeySort));
            Assert.Near(1.0, section.GetDouble(UxPrefsModel.KeySidebarWidthFactor, -1), 1e-9);
            UxPrefsModel.Defaults().Write(null);   // a null section is ignored
        }

        [Test]
        public void Read_BadValues_AreReplacedByTheDefaultsWithANote()
        {
            JsonObject section = Parse(@"{ ""lastTab"": 5, ""viewMode"": ""grid"", ""listOnlyAvailable"": ""yes"", ""listHideResearched"": 1,
                ""listHideUnnecessary"": null, ""listSort"": ""color"", ""sidebarWidthFactor"": ""wide"" }");
            var problems = new List<string>();

            UxPrefsModel prefs = UxPrefsModel.Read(section, problems);

            Assert.Equal(7, problems.Count, string.Join("\n", problems));
            Assert.Equal("all", prefs.LastTab);
            Assert.Equal("tree", prefs.ViewMode);
            Assert.True(prefs.ListOnlyAvailable);
            Assert.True(prefs.ListHideResearched);
            Assert.False(prefs.ListHideUnnecessary);
            Assert.Equal("year", prefs.ListSort);
            Assert.Equal(1.0f, prefs.SidebarWidthFactor);
        }

        [Test]
        public void Read_ABadValueDoesNotSpoilTheGoodOnes()
        {
            JsonObject section = Parse(@"{ ""viewMode"": ""list"", ""listSort"": ""color"", ""lastTab"": ""frequency"" }");
            var problems = new List<string>();

            UxPrefsModel prefs = UxPrefsModel.Read(section, problems);

            Assert.Equal(1, problems.Count);
            Assert.Equal("list", prefs.ViewMode);
            Assert.Equal("frequency", prefs.LastTab);
            Assert.Equal("year", prefs.ListSort);
        }

        [Test]
        public void Read_AnEmptyOrVeryLongTabId_FallsBackToAll()
        {
            var problems = new List<string>();
            Assert.Equal("all", UxPrefsModel.Read(Parse(@"{ ""lastTab"": """" }"), problems).LastTab);
            Assert.Equal("all", UxPrefsModel.Read(Parse(@"{ ""lastTab"": """ + new string('x', 65) + @""" }"), problems).LastTab);
            Assert.Equal(2, problems.Count);
            Assert.Equal(new string('x', 64), UxPrefsModel.Read(Parse(@"{ ""lastTab"": """ + new string('x', 64) + @""" }"), problems).LastTab, "64 characters are still fine");
        }

        [Test]
        public void SidebarWidthFactor_IsClampedToTheRangeOfTheSpec()
        {
            Assert.Equal(1.0f, UxPrefsModel.Read(Parse(@"{ ""sidebarWidthFactor"": 0.4 }"), null).SidebarWidthFactor);
            Assert.Equal(2.0f, UxPrefsModel.Read(Parse(@"{ ""sidebarWidthFactor"": 5 }"), null).SidebarWidthFactor);
            Assert.Near(1.37, UxPrefsModel.Read(Parse(@"{ ""sidebarWidthFactor"": 1.37 }"), null).SidebarWidthFactor, 1e-6);

            Assert.Equal(1.0f, UxPrefsModel.ClampWidthFactor(float.NaN));
            Assert.Equal(1.0f, UxPrefsModel.ClampWidthFactor(float.PositiveInfinity));
            Assert.Equal(1.0f, UxPrefsModel.ClampWidthFactor(-3f));
            Assert.Equal(2.0f, UxPrefsModel.ClampWidthFactor(2.5f));
            Assert.Equal(1.5f, UxPrefsModel.ClampWidthFactor(1.5f));
        }

        [Test]
        public void Clone_IsIndependent()
        {
            UxPrefsModel original = UxPrefsModel.Defaults();
            UxPrefsModel copy = original.Clone();
            copy.LastTab = "memory";
            copy.ListHideUnnecessary = true;

            Assert.Equal("all", original.LastTab);
            Assert.False(original.ListHideUnnecessary);
        }
    }
}
