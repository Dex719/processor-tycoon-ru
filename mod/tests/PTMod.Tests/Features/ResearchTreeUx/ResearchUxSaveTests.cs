using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The section <c>research-tree-ux</c> of the side save (NFR-4): the DTO binds from and writes to JSON without a version field, and
    /// <c>Sanitize</c> drops what the spec says (ids outside the technologies, repeats, more than 512). TSK-001; the tracker that uses
    /// the list is TSK-005, the full compatibility suite TSK-019.
    /// </summary>
    [Covers(typeof(ResearchUxSave))]
    public class ResearchUxSaveTests
    {
        private static ResearchUxSave Bind(string json, List<string> errors)
        {
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(json, out node, out error), "test JSON is invalid: " + error);
            return ModJson.Bind<ResearchUxSave>(node, errors);
        }

        [Test]
        public void Bind_TheExampleOfTheSpec()
        {
            var errors = new List<string>();
            ResearchUxSave save = Bind(@"{ ""seenAvailable"": [12, 31, 44, 58, 71] }", errors);

            Assert.Equal(0, errors.Count, string.Join("; ", errors));
            Assert.Equal(new[] { 12, 31, 44, 58, 71 }, save.seenAvailable.ToArray());
        }

        [Test]
        public void Bind_AnEmptySection_GivesAnEmptyList()
        {
            ResearchUxSave save = Bind("{}", new List<string>());
            Assert.NotNull(save.seenAvailable);
            Assert.Equal(0, save.seenAvailable.Count);
        }

        [Test]
        public void Write_HasNoVersionField_AndTheListOnly()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int> { 3, 9 } };
            var errors = new List<string>();

            var node = ModJson.From(save, errors) as JsonObject;

            Assert.NotNull(node);
            Assert.Equal(0, errors.Count);
            Assert.Equal(1, node.Count, "no _schema, no schema, no version: the core writes the version of the section (D20)");
            Assert.True(node.ContainsKey("seenAvailable"));
            Assert.Equal("{\"seenAvailable\":[3,9]}", ModJson.Write(node, false));
        }

        [Test]
        public void Sanitize_ASanitySection_IsLeftAlone()
        {
            ResearchUxSave save = new ResearchUxSave { seenAvailable = new List<int> { 0, 5, 96 } };
            var problems = new List<string>();

            int dropped = save.Sanitize(97, problems);

            Assert.Equal(0, dropped);
            Assert.Equal(0, problems.Count);
            Assert.Equal(new[] { 0, 5, 96 }, save.seenAvailable.ToArray());
        }

        [Test]
        public void Sanitize_DropsIdsOutsideTheTechnologies()
        {
            // 97 technologies: the valid ids are 0...96.
            var save = new ResearchUxSave { seenAvailable = new List<int> { -3, 0, 96, 97, 5000 } };
            var problems = new List<string>();

            int dropped = save.Sanitize(97, problems);

            Assert.Equal(3, dropped);
            Assert.Equal(new[] { 0, 96 }, save.seenAvailable.ToArray());
            Assert.Equal(1, problems.Count);
            Assert.True(problems[0].Contains("3"), problems[0]);
        }

        [Test]
        public void Sanitize_WithAnUnknownCount_OnlyDropsNegativeIds()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int> { -1, 5000, 7 } };
            save.Sanitize(0, null);
            Assert.Equal(new[] { 7, 5000 }, save.seenAvailable.ToArray());
        }

        [Test]
        public void Sanitize_MergesRepeats_AndSorts()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int> { 44, 12, 44, 31, 12, 12 } };
            var problems = new List<string>();

            int dropped = save.Sanitize(97, problems);

            Assert.Equal(3, dropped);
            Assert.Equal(new[] { 12, 31, 44 }, save.seenAvailable.ToArray());
            Assert.Equal(1, problems.Count);
        }

        [Test]
        public void Sanitize_KeepsAtMost512Ids()
        {
            var ids = new List<int>();
            for (int i = 0; i < 600; i++) ids.Add(i);
            var save = new ResearchUxSave { seenAvailable = ids };
            var problems = new List<string>();

            int dropped = save.Sanitize(0, problems);

            Assert.Equal(88, dropped);
            Assert.Equal(512, save.seenAvailable.Count);
            Assert.Equal(0, save.seenAvailable[0]);
            Assert.Equal(511, save.seenAvailable[511], "the smallest ids stay");
            Assert.Equal(1, problems.Count);
            Assert.Equal(512, ResearchUxSave.MaxSeen);
        }

        [Test]
        public void Sanitize_ReportsEachKindOfDefectOnce()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int> { -1, -2, 200, 5, 5 } };
            var problems = new List<string>();

            save.Sanitize(97, problems);

            Assert.Equal(2, problems.Count, "out of range and repeats: " + string.Join("; ", problems));
        }

        [Test]
        public void Sanitize_ANullList_BecomesAnEmptyOne()
        {
            var save = new ResearchUxSave { seenAvailable = null };
            Assert.Equal(0, save.Sanitize(97, null));
            Assert.NotNull(save.seenAvailable);
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int> { 1, 2 } };
            ResearchUxSave copy = save.Clone();
            copy.seenAvailable.Add(3);

            Assert.Equal(2, save.seenAvailable.Count);
            Assert.Equal(3, copy.seenAvailable.Count);
            Assert.NotNull(new ResearchUxSave { seenAvailable = null }.Clone().seenAvailable);
        }
    }
}
