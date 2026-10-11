using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The answer of a rival to a design flaw (FR-13 item 5, TSK-020): the multipliers of the archetypes handed to <c>design-flaw-recalls</c>
    /// (<see cref="FlawBiasMath"/>) and, read as text, the class that hands them over and the link that registers it (<c>FlawResponseBias</c>, <c>FlawLink</c>).
    /// </summary>
    [Covers(typeof(FlawBiasMath))]
    public class FlawBiasTests
    {
        // Recall, Stepping, Patch, Ignore - the table of design.md
        private static readonly Dictionary<string, float[]> Table = new Dictionary<string, float[]>
        {
            { "hegemon", new[] { 0.9f, 1.2f, 1.0f, 1.2f } },
            { "price_warrior", new[] { 0.8f, 1.0f, 1.3f, 1.1f } },
            { "fast_follower", new[] { 1.0f, 1.0f, 1.2f, 1.0f } },
            { "engineer", new[] { 1.0f, 1.4f, 0.9f, 0.7f } },
            { "conservative", new[] { 1.5f, 1.0f, 0.9f, 0.6f } },
            { "niche_specialist", new[] { 1.0f, 1.0f, 1.2f, 1.0f } },
            { "foundry_partner", new[] { 1.0f, 1.0f, 1.0f, 1.0f } },
        };

        private static Archetype Find(string id)
        {
            return RivalTestKit.LoadShippedCatalog().All.First(a => a.id == id);
        }

        // ---- the numbers ----

        [Test]
        public void EveryShippedArchetype_GivesTheNumbersOfTheDesignTable_InTheOrderOfThePolicy()
        {
            ArchetypeCatalog catalog = RivalTestKit.LoadShippedCatalog();
            Assert.Equal(Table.Count, catalog.All.Count);

            foreach (Archetype archetype in catalog.All)
            {
                float[] multipliers = FlawBiasMath.For(archetype);

                Assert.NotNull(multipliers, archetype.id);
                Assert.Equal(Table[archetype.id], multipliers, archetype.id + ": Recall, Stepping, Patch, Ignore");
            }
        }

        [Test]
        public void TheConservative_PrefersTheRecall_AndAvoidsTheIgnore()
        {
            float[] multipliers = FlawBiasMath.For(Find("conservative"));

            Assert.Equal(1.5f, multipliers[0], "Recall");
            Assert.Equal(0.6f, multipliers[3], "Ignore");
        }

        [Test]
        public void TheEngineer_PrefersTheRevision_ThePriceWarriorThePatch_TheHegemonSitsItOut()
        {
            Assert.Equal(1.4f, FlawBiasMath.For(Find("engineer"))[1], "Stepping");
            Assert.Equal(1.3f, FlawBiasMath.For(Find("price_warrior"))[2], "Patch");
            Assert.Equal(1.2f, FlawBiasMath.For(Find("hegemon"))[3], "Ignore");
        }

        [Test]
        public void EveryAnswer_MeetsTheContractOfTheRegistry()
        {
            // FlawRegistry.SanitizeMultipliers: four numbers, none negative, NaN or infinite - otherwise the whole answer counts as "all 1.0"
            foreach (Archetype archetype in RivalTestKit.LoadShippedCatalog().All)
            {
                float[] multipliers = FlawBiasMath.For(archetype);
                Assert.Equal(FlawBiasMath.Count, multipliers.Length, archetype.id);
                foreach (float value in multipliers)
                {
                    Assert.True(!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f, archetype.id);
                    Assert.True(value >= FlawBiasMath.Min && value <= FlawBiasMath.Max, archetype.id + " is within 0.5..2.0");
                }
            }
        }

        [Test]
        public void NoArchetype_IsNoBias()
        {
            Assert.Null(FlawBiasMath.For(null));
            Assert.True(FlawBiasMath.IsNeutral(null));
        }

        [Test]
        public void TheNumbers_AreHeldToTheRange_AndNaNIsOne()
        {
            var archetype = new Archetype { id = "x", flawResponseBias = new[] { 0.1f, 5f, float.NaN, float.PositiveInfinity } };

            Assert.Equal(new[] { 0.5f, 2.0f, 1.0f, 1.0f }, FlawBiasMath.For(archetype));
            Assert.Equal(0.5f, FlawBiasMath.Min);
            Assert.Equal(2.0f, FlawBiasMath.Max);
            Assert.Equal(1f, FlawBiasMath.Clamp(float.NegativeInfinity));
            Assert.Equal(1.7f, FlawBiasMath.Clamp(1.7f));
        }

        [Test]
        public void ATableOfTheWrongLength_IsFilledWithOnes()
        {
            Assert.Equal(new[] { 1.4f, 1f, 1f, 1f }, FlawBiasMath.For(new Archetype { id = "x", flawResponseBias = new[] { 1.4f } }));
            Assert.Equal(new[] { 1f, 1f, 1f, 1f }, FlawBiasMath.For(new Archetype { id = "x", flawResponseBias = null }));
            Assert.Equal(new[] { 1.1f, 1.2f, 1.3f, 1.4f }, FlawBiasMath.For(new Archetype { id = "x", flawResponseBias = new[] { 1.1f, 1.2f, 1.3f, 1.4f, 1.5f } }), "the extra number is dropped");
        }

        [Test]
        public void EveryCallGivesANewArray_SoTheCallerMayChangeIt()
        {
            Archetype archetype = Find("conservative");

            float[] first = FlawBiasMath.For(archetype);
            first[0] = 99f;

            Assert.Equal(1.5f, FlawBiasMath.For(archetype)[0], "the archetype keeps its numbers");
            Assert.Equal(1.5f, archetype.flawResponseBias[0]);
        }

        [Test]
        public void IsNeutral_IsTrueOnlyWhenEveryMultiplierIsOne()
        {
            Assert.True(FlawBiasMath.IsNeutral(FlawBiasMath.For(Find("foundry_partner"))));
            Assert.False(FlawBiasMath.IsNeutral(FlawBiasMath.For(Find("hegemon"))));
            Assert.True(FlawBiasMath.IsNeutral(new float[0]));
        }

        // ---- the class and the link (read as text) ----

        private static string Code(params string[] parts)
        {
            string path = Path.Combine(new[] { SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities" }.Concat(parts).ToArray());
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        [Test]
        public void TheOrderOfThePolicy_IsRecallSteppingPatchIgnore()
        {
            string path = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "DesignFlawRecalls", "Api", "Model", "FlawEnums.cs");
            string code = SourceScan.Strip(SourceScan.ReadText(path), false);

            Assert.True(Regex.IsMatch(code, @"Recall = 0;[\s\S]*Stepping = 1;[\s\S]*Patch = 2;[\s\S]*Ignore = 3;[\s\S]*Length = 4;"), "FlawResponseBiasOrder");
        }

        [Test]
        public void TheBias_IsAnIFlawResponseBias_OfThisFeature_ForAiCompaniesOnly()
        {
            string bias = Code("Integration", "FlawResponseBias.cs");

            Assert.True(Regex.IsMatch(bias, @"namespace PTMod\.Features\.RivalPersonalities\s*\{"), "the registry counts a failure against the namespace's feature");
            Assert.True(Regex.IsMatch(bias, @"class FlawResponseBias : IFlawResponseBias"));
            Assert.True(Regex.IsMatch(bias, @"public float\[\] Multipliers\(ICompany company, bool isPublic\)"));
            Assert.True(Regex.IsMatch(bias, @"RivalApiService\.UidOf\(company\)"), "null for the player and for what is no AI company");
            Assert.True(Regex.IsMatch(bias, @"IsDormant\(uniqueId\.Value\)\) return null;"), "a rival that is put aside decides nothing");
            Assert.True(Regex.IsMatch(bias, @"FlawBiasMath\.For\(feature\.Book\.ArchetypeOf\(uniqueId\.Value\)\)"), "the numbers of the archetype");
        }

        [Test]
        public void TheLink_RegistersTheBias_OnGameReadyAndOnEnable_AndTakesItBackOnDisable()
        {
            string link = Code("Integration", "FlawLink.cs");
            string feature = Code("RivalPersonalitiesFeature.cs");

            Assert.Equal(1, Regex.Matches(link, @"registry\.SetResponseBias\(_bias\)").Count, "set once");
            Assert.Equal(1, Regex.Matches(link, @"registry\.SetResponseBias\(null\)").Count, "cleared once");
            Assert.True(Regex.IsMatch(link, @"if \(!ModFeatures\.IsActive\(Feature\) \|\| !ModFeatures\.IsActive\(DesignFlawRecallsApi\.FeatureId\)\) return;"), "both features must be active");

            // Attach: on every GameReady, when the feature is switched on in a ready game, when design-flaw-recalls is switched on
            Assert.True(Regex.IsMatch(feature, @"ModHooks\.Guard\(FeatureId, ""ModTicks\.GameReady\.links"", AttachLinks\)"), "GameReady");
            Assert.True(Regex.IsMatch(feature, @"if \(ModTicks\.IsGameReady\) AttachLinks\(\);"), "enabled in a ready game");
            Assert.True(Regex.IsMatch(feature, @"ModFeatures\.StateChanged \+= OnFeatureStateChanged;"), "a state change of design-flaw-recalls");
            Assert.True(Regex.IsMatch(link, @"state == FeatureState\.Enabled[\s\S]*?Attach\(\)[\s\S]*?else[\s\S]*?Detach\(\)"), "on: attach, off: detach");

            // Detach: when this feature is switched off
            Assert.True(Regex.IsMatch(feature, @"""feature/disabled\.links"", DetachLinks\)"), "OnDisabled");
        }

        [Test]
        public void TheLink_ListensToTheFlaws_OnceAndLetsGo()
        {
            string link = Code("Integration", "FlawLink.cs");

            Assert.Equal(1, Regex.Matches(link, @"OnFlawStateChanged \+= OnFlawChanged;").Count);
            Assert.Equal(1, Regex.Matches(link, @"OnFlawStateChanged -= OnFlawChanged;").Count);
            Assert.True(Regex.IsMatch(link, @"if \(!_attached\)\s*\{\s*FlawRegistry registry"), "the subscription is made once");
            Assert.True(Regex.IsMatch(link, @"if \(!_attached\) return;"), "detach is idempotent");
            Assert.True(Regex.IsMatch(link, @"ModHooks\.Guard\(Feature, ""FlawLink/sync"", \(\) => SyncKnown\(\)\);"), "every Attach looks at the flaws that are public already");
        }
    }
}
