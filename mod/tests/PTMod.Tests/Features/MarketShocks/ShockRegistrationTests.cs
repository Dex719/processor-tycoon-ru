using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Features;
using PTMod.Core.Logic.Ids;
using PTMod.Features.MarketShocks;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.MarketShocks
{
    /// <summary>
    /// market-shocks, TSK-009 (FR-1, FR-14): what the entry class of the feature declares - the id, the one dependency, the keys of the name, the effect
    /// types and the predicates it hands to the event system, the line of the log (AC-1.1) - and the two parameters of the hub (intensity 0.5 ... 2,
    /// frequency 0.5 ... 1.5). The class itself (<c>MarketShocksFeature</c>) talks to the game and the engine and is checked by the build only; it reads
    /// these lists, so what is tested here is what it registers.
    /// </summary>
    [Covers(typeof(ShockRegistration), typeof(ShockParamSpecs))]
    public class ShockRegistrationTests
    {
        // ---- the declaration of the feature ----

        [Test]
        public void TheFeature_IsMarketShocks_DependingOnTheEventSystemOnly()
        {
            Assert.Equal("market-shocks", ShockRegistration.FeatureId, "the slug");
            Assert.Equal("event-system", ShockRegistration.EventSystemId, "the engine");
            string[] depends = ShockRegistration.DependsOn();
            Assert.Equal(1, depends.Length, "one hard dependency");
            Assert.Equal("event-system", depends[0], "DependsOn");
            Assert.True(!ReferenceEquals(depends, ShockRegistration.DependsOn()), "a fresh array each time");
            Assert.Equal("ms.feature.name", ShockRegistration.NameKey, "NameKey");
            Assert.Equal("ms.feature.desc", ShockRegistration.DescriptionKey, "DescriptionKey");
            Assert.Equal("shocks", ShockRegistration.EventsFile, "the events file of the pack");
            Assert.Equal(21, ShockRegistration.ExpectedEvents, "21 records");
            Assert.Equal(3, ShockRegistration.PatchClassCount, "three patch classes");
        }

        [Test]
        public void WithoutTheEventSystem_TheFeatureIsUnavailable_AndSoIsEveryPatchOfIt()
        {
            // AC-1.2: the dependency rule of the core. Register is not called for an Unavailable feature, so no patch is declared and the prices stay vanilla.
            var graph = new FeatureGraph(new[]
            {
                new FeatureNode(ShockRegistration.EventSystemId),
                new FeatureNode(ShockRegistration.FeatureId, ShockRegistration.DependsOn()),
            });
            var machine = new FeatureStateMachine(graph);
            Assert.Equal(FeatureState.Enabled, machine.StateOf(ShockRegistration.FeatureId), "both on: the feature runs");

            machine.MarkUnavailable(ShockRegistration.EventSystemId, "test: the engine failed to start");
            Assert.Equal(FeatureState.Unavailable, machine.StateOf(ShockRegistration.FeatureId), "the engine is gone: the feature is Unavailable");
            Assert.False(machine.IsActive(ShockRegistration.FeatureId), "not active, so its patches are not applied and its handlers do not run");
            FeatureStatus status = machine.StatusOf(ShockRegistration.FeatureId);
            Assert.Equal(FeatureReasonKind.DependencyInactive, status.ReasonKind, "the reason is the dependency");
            Assert.True(new List<string>(status.ReasonFeatures).Contains(ShockRegistration.EventSystemId), "the reason names event-system");
        }

        [Test]
        public void WhenTheEventSystemIsSwitchedOff_TheFeatureStopsWithIt_AndComesBack()
        {
            var graph = new FeatureGraph(new[]
            {
                new FeatureNode(ShockRegistration.EventSystemId),
                new FeatureNode(ShockRegistration.FeatureId, ShockRegistration.DependsOn()),
            });
            var machine = new FeatureStateMachine(graph);

            machine.SetEnabled(ShockRegistration.EventSystemId, false);
            Assert.False(machine.IsActive(ShockRegistration.FeatureId), "event-system off: market-shocks does not run");
            machine.SetEnabled(ShockRegistration.EventSystemId, true);
            Assert.True(machine.IsActive(ShockRegistration.FeatureId), "event-system on again: market-shocks runs");
        }

        [Test]
        public void TheFeature_NeverDeclaresAChannel_TheCoreDoesIt()
        {
            // design.md "Фича": production.memoryCost and production.waferCost are declared by the core (core.json, owner market-shocks) before any Register.
            string root = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "MarketShocks");
            var call = new Regex(@"\bDeclareChannel\s*\(", RegexOptions.CultureInvariant);
            int files = 0;
            foreach (string path in SourceScan.Files(root, ".cs"))
            {
                files++;
                foreach (string line in SourceScan.ReadText(path).Split('\n'))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    Assert.False(call.IsMatch(line), SourceScan.Relative(root, path) + ": calls DeclareChannel: " + line.Trim());
                }
            }
            Assert.True(files >= 10, "the scan found only " + files + " files");

            // and the core does declare both, owned by this feature
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PTMod.Core.Logic.Json.JsonNode node in ShockContentData.Core().GetArray("channels"))
            {
                var channel = (PTMod.Core.Logic.Json.JsonObject)node;
                owners[channel.GetString("name", string.Empty)] = channel.GetString("owner", string.Empty);
            }
            Assert.Equal("market-shocks", owners["production.memoryCost"], "owner of production.memoryCost");
            Assert.Equal("market-shocks", owners["production.waferCost"], "owner of production.waferCost");
        }

        // ---- what Register hands over ----

        [Test]
        public void EffectTypes_AreTheThreeOfThePack_InTheOrderOfRegistration()
        {
            var types = new List<string>(ShockRegistration.EffectTypes);
            Assert.Equal(3, types.Count, "three effect types (AC-1.1)");
            Assert.Equal("ModifierRamp", types[0], "first");
            Assert.Equal("ModifierWave", types[1], "second");
            Assert.Equal("InventoryLoss", types[2], "third");
        }

        [Test]
        public void Predicates_AreTwo_AndTheirNamesPassTheNamingRuleOfTheCore()
        {
            var names = new List<string>(ShockRegistration.PredicateNames);
            Assert.Equal(2, names.Count, "two predicates (AC-1.1)");
            Assert.Equal("ms.sellsBelowCost", names[0], "first");
            Assert.Equal("ms.hasRecentCpu", names[1], "second");

            ShockContentData.WithPrefixes(() =>
            {
                foreach (string name in names)
                    Assert.True(ModIds.IsPredicateNameAllowed(ShockRegistration.FeatureId, name), ModIds.Explain(IdKind.PredicateName, ShockRegistration.FeatureId, name));
                Assert.True(ModIds.IsEventIdAllowed(ShockRegistration.FeatureId, "ms.fab_fire"), "the ids of the records carry the prefix ms");
                Assert.True(ModIds.IsL10nKeyAllowed(ShockRegistration.FeatureId, "ms.feature.name"), "the keys of the strings carry the prefix ms");
            });
        }

        [Test]
        public void ThePackLine_IsTheOneOfAc11()
        {
            Assert.Equal("pack registered: 21 events, 3 effect types, 2 predicates", ShockRegistration.PackRegisteredLine(21), "AC-1.1");
            Assert.Equal("pack registered: 19 events, 3 effect types, 2 predicates", ShockRegistration.PackRegisteredLine(19), "fewer events accepted");
            Assert.Equal("pack registered: 0 events, 3 effect types, 2 predicates", ShockRegistration.PackRegisteredLine(-5), "a negative count is none");
        }

        // ---- the parameters of the hub ----

        [Test]
        public void Params_AreIntensityAndFrequency_WithTheRangesOfDesign()
        {
            ParamSpec[] specs = ShockParamSpecs.Create("market-shocks");
            Assert.Equal(2, specs.Length, "two parameters (FR-14 item 1)");

            ParamSpec intensity = specs[0];
            Assert.Equal("intensity", intensity.Key, "first key");
            Assert.Equal("market-shocks", intensity.FeatureId, "owner");
            Assert.Equal(ParamType.Float, intensity.Type, "a slider");
            Assert.Near(0.5, intensity.Min, 1e-12, "intensity min");
            Assert.Near(2.0, intensity.Max, 1e-12, "intensity max");
            Assert.Near(0.5, intensity.Step, 1e-12, "intensity step");
            Assert.Near(1.0, intensity.Default, 1e-12, "intensity default: Normal");

            ParamSpec frequency = specs[1];
            Assert.Equal("frequency", frequency.Key, "second key");
            Assert.Equal(ParamType.Float, frequency.Type, "a slider");
            Assert.Near(0.5, frequency.Min, 1e-12, "frequency min");
            Assert.Near(1.5, frequency.Max, 1e-12, "frequency max");
            Assert.Near(0.5, frequency.Step, 1e-12, "frequency step");
            Assert.Near(1.0, frequency.Default, 1e-12, "frequency default");

            foreach (ParamSpec spec in specs)
            {
                string problem;
                Assert.True(spec.Clone().Normalize(out problem), spec.Key + ": the core accepts the declaration: " + problem);
                Assert.True(spec.InRange(spec.Default), spec.Key + ": the default is in range");
                Assert.False(spec.RequiresReload, spec.Key + ": a change takes effect at once");
            }
        }

        [Test]
        public void Params_HaveLabelsAndTooltips_InBothLanguages()
        {
            var keys = new List<string>();
            foreach (ParamSpec spec in ShockParamSpecs.Create("market-shocks"))
            {
                Assert.Equal("ms.settings." + spec.Key, spec.LabelKey, spec.Key + ": label key");
                Assert.Equal("ms.settings." + spec.Key + ".tooltip", spec.TooltipKey, spec.Key + ": tooltip key");
                keys.Add(spec.LabelKey);
                keys.Add(spec.TooltipKey);
            }
            foreach (float level in new[] { 0.5f, 1f, 1.5f, 2f }) keys.Add(ShockParamSpecs.IntensityLevelKey(level));

            foreach (string language in new[] { "ru", "en" })
            {
                Dictionary<string, string> strings = ShockContentData.Strings(language);
                foreach (string key in keys) Assert.True(strings.ContainsKey(key), language + ": no string " + key);
            }
        }

        [Test]
        public void Intensity_IsHeldToTheSlider_AndFallsBackOnJunk()
        {
            Assert.Near(1.5, ShockParamSpecs.UsableIntensity(1.5f), 1e-6, "inside");
            Assert.Near(0.5, ShockParamSpecs.UsableIntensity(0.1f), 1e-6, "below the slider");
            Assert.Near(2.0, ShockParamSpecs.UsableIntensity(9f), 1e-6, "above the slider");
            Assert.Near(1.0, ShockParamSpecs.UsableIntensity(float.NaN), 1e-6, "NaN is Normal");
            Assert.Near(1.0, ShockParamSpecs.UsableIntensity(float.PositiveInfinity), 1e-6, "infinity is Normal");

            Assert.Near(1.5, ShockParamSpecs.UsableFrequency(1.5f), 1e-6, "frequency inside");
            Assert.Near(1.5, ShockParamSpecs.UsableFrequency(4f), 1e-6, "frequency above");
            Assert.Near(0.5, ShockParamSpecs.UsableFrequency(0f), 1e-6, "frequency below");
            Assert.Near(1.0, ShockParamSpecs.UsableFrequency(float.NaN), 1e-6, "frequency NaN");
        }

        [Test]
        public void IntensityLevels_AreMildNormalHarshChaos()
        {
            Assert.Equal("ms.settings.intensity.mild", ShockParamSpecs.IntensityLevelKey(0.5f), "x0.5");
            Assert.Equal("ms.settings.intensity.normal", ShockParamSpecs.IntensityLevelKey(1f), "x1");
            Assert.Equal("ms.settings.intensity.harsh", ShockParamSpecs.IntensityLevelKey(1.5f), "x1.5");
            Assert.Equal("ms.settings.intensity.chaos", ShockParamSpecs.IntensityLevelKey(2f), "x2");
            Assert.Equal("ms.settings.intensity.normal", ShockParamSpecs.IntensityLevelKey(float.NaN), "junk is Normal");
            Assert.Equal("ms.settings.intensity.mild", ShockParamSpecs.IntensityLevelKey(0.7f), "between mild and normal, nearer mild");
            Assert.Equal("ms.settings.intensity.chaos", ShockParamSpecs.IntensityLevelKey(1.8f), "between harsh and chaos, nearer chaos");
        }
    }
}
