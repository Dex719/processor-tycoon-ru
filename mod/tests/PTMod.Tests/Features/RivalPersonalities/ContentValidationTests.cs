using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PTMod.Core;
using PTMod.Core.Logic.Content;
using PTMod.Core.Logic.Ids;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The content of rival-personalities (FR-1, FR-2; TSK-002, TSK-003): the checks of <c>ArchetypeCatalog</c> and <c>RivalConfig</c> on broken
    /// records, and the shipped files against the tables of design.md.
    /// </summary>
    [Covers(typeof(ArchetypeCatalog), typeof(ValidationReport), typeof(Archetype), typeof(ArchetypeBehaviour), typeof(ArchetypeAttitude),
        typeof(ReactionRule), typeof(AssignmentRecord), typeof(RivalConfig), typeof(DesignFocusOverride), typeof(PowerFocusOverride),
        typeof(EventStyle), typeof(TriggerType), typeof(ReactionType), typeof(ReactionKinds))]
    public class ContentValidationTests
    {
        // ---- helpers ----

        private static Archetype Accept(string json, List<string> messages)
        {
            Archetype archetype = ArchetypeCatalog.MapArchetype(RivalTestKit.Obj(json), messages);
            Assert.NotNull(archetype, "the record was not mapped: " + string.Join("; ", messages));
            Assert.True(ArchetypeCatalog.ValidateArchetype(archetype, messages), "the record was rejected: " + string.Join("; ", messages));
            return archetype;
        }

        private static bool Mentions(IEnumerable<string> lines, params string[] fragments)
        {
            return lines.Any(line => fragments.All(f => line.IndexOf(f, StringComparison.Ordinal) >= 0));
        }

        private static string Records(params string[] records)
        {
            return "{ \"schema\": 1, \"records\": [ " + string.Join(", ", records) + " ] }";
        }

        // ---- archetypes: the id ----

        [Test]
        public void ARecordWithoutAnId_IsSkipped_AndTheOthersLoad()
        {
            string file = RivalTestKit.J(Records(
                "{'id':'a'}",
                "{'behaviour':{'approvalBias':1.1}}",
                "{'id':'c'}"));

            ContentLoad<Archetype> load = RivalTestKit.LoadArchetypes(file);

            Assert.Equal(new[] { "a", "c" }, load.Records.Select(r => r.Id).ToArray());
            Assert.Equal(1, load.Report.Errors.Count, string.Join("; ", load.Report.Errors));
        }

        [Test]
        public void MapArchetype_NoIdIsNull_AndSoIsABrokenId()
        {
            var messages = new List<string>();
            Assert.Null(ArchetypeCatalog.MapArchetype(RivalTestKit.Obj("{'behaviour':{}}"), messages));
            Assert.True(Mentions(messages, "id"), string.Join("; ", messages));

            Assert.Null(ArchetypeCatalog.MapArchetype(null, new List<string>()));

            // an id that is not a plain name does not pass the validator
            foreach (string bad in new[] { "''", "'has space'", "'../up'", "'" + new string('x', 65) + "'" })
            {
                var m = new List<string>();
                Archetype archetype = ArchetypeCatalog.MapArchetype(RivalTestKit.Obj("{'id':" + bad + "}"), m);
                if (archetype == null) continue;   // an empty id is refused by the mapper already
                Assert.False(ArchetypeCatalog.ValidateArchetype(archetype, m), "id " + bad + " must be refused");
            }
            Assert.False(ArchetypeCatalog.ValidateArchetype(null, new List<string>()));
        }

        // ---- archetypes: numbers are clipped with a warning ----

        [Test]
        public void TheThreeMultipliers_AreClippedToTheirRanges_WithAWarning()
        {
            var messages = new List<string>();
            Archetype archetype = Accept("{'id':'x','behaviour':{'approvalBias':1.5,'researchFundingMult':0.1,'launchPriceMult':0.5}}", messages);

            Assert.Equal(1.3f, archetype.behaviour.approvalBias);
            Assert.Equal(0.7f, archetype.behaviour.researchFundingMult);
            Assert.Equal(0.85f, archetype.behaviour.launchPriceMult, "launchPriceMult 0.5 gives 0.85");
            Assert.True(Mentions(messages, "behaviour.approvalBias", "1.3"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "behaviour.researchFundingMult", "0.7"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "behaviour.launchPriceMult", "0.85"), string.Join("; ", messages));

            messages = new List<string>();
            archetype = Accept("{'id':'y','behaviour':{'approvalBias':0.2,'researchFundingMult':9,'launchPriceMult':3}}", messages);
            Assert.Equal(0.8f, archetype.behaviour.approvalBias);
            Assert.Equal(1.3f, archetype.behaviour.researchFundingMult);
            Assert.Equal(1.15f, archetype.behaviour.launchPriceMult);
        }

        [Test]
        public void ValuesOnTheEdgesOfTheRanges_AreAcceptedWithoutAWarning()
        {
            var messages = new List<string>();
            Archetype low = Accept("{'id':'low','name':'k','description':'k','behaviour':{'approvalBias':0.8,'researchFundingMult':0.7,'launchPriceMult':0.85},"
                                   + "'attitude':{'baseline':-50,'sensitivity':0.3},'cooldownDays':60,'flawResponseBias':[0.5,0.5,0.5,0.5]}", messages);
            Archetype high = Accept("{'id':'high','name':'k','description':'k','behaviour':{'approvalBias':1.3,'researchFundingMult':1.3,'launchPriceMult':1.15},"
                                    + "'attitude':{'baseline':50,'sensitivity':2.0},'cooldownDays':720,'flawResponseBias':[2.0,2.0,2.0,2.0]}", messages);

            Assert.Equal(0, messages.Count, string.Join("; ", messages));
            Assert.Equal(0.8f, low.behaviour.approvalBias);
            Assert.Equal(1.15f, high.behaviour.launchPriceMult);
            Assert.Equal(720, high.cooldownDays);
        }

        [Test]
        public void BaselineSensitivityAndCooldown_AreClipped()
        {
            var messages = new List<string>();
            Archetype archetype = Accept("{'id':'x','attitude':{'baseline':-90,'sensitivity':5},'cooldownDays':10}", messages);

            Assert.Equal(-50f, archetype.attitude.baseline);
            Assert.Equal(2.0f, archetype.attitude.sensitivity);
            Assert.Equal(60, archetype.cooldownDays);
            Assert.True(Mentions(messages, "attitude.baseline"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "attitude.sensitivity"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "cooldownDays"), string.Join("; ", messages));

            archetype = Accept("{'id':'y','attitude':{'baseline':77,'sensitivity':0.01},'cooldownDays':5000}", new List<string>());
            Assert.Equal(50f, archetype.attitude.baseline);
            Assert.Equal(0.3f, archetype.attitude.sensitivity);
            Assert.Equal(720, archetype.cooldownDays);
        }

        // ---- archetypes: reactions and triggers ----

        [Test]
        public void UnknownReactionTypesAndTriggers_AreDropped()
        {
            var messages = new List<string>();
            Archetype archetype = Accept(
                "{'id':'x','reactions':{"
                + "'StrongLaunch':[{'type':'PriceCut','base':0.4},{'type':'Sabotage','base':0.5},{'type':'Partnership','base':0.3},{'type':'CounterLaunch','base':0.1}],"
                + "'MoonLanding':[{'type':'PriceCut','base':0.4}],"
                + "'ShareGain':[{'type':'Smear','base':2.5,'anyAttitude':true}]}}", messages);

            IReadOnlyList<ReactionRule> strong = archetype.RulesFor(TriggerType.StrongLaunch);
            Assert.Equal(new[] { "PriceCut", "CounterLaunch" }, strong.Select(r => r.type).ToArray(), "unknown type and Partnership are dropped, the order stays");
            Assert.Equal(ReactionType.PriceCut, strong[0].Kind);
            Assert.Equal(ReactionType.CounterLaunch, strong[1].Kind);

            IReadOnlyList<ReactionRule> share = archetype.RulesFor(TriggerType.ShareGain);
            Assert.Equal(1, share.Count);
            Assert.Equal(0.9f, share[0].@base, "a base chance above 0.9 is clipped");
            Assert.True(share[0].anyAttitude);

            Assert.False(archetype.reactions.ContainsKey("MoonLanding"), "the table of an unknown trigger is dropped");
            Assert.Equal(0, archetype.RulesFor(TriggerType.OemLoss).Count, "a trigger without a table gives no rules, not null");

            Assert.True(Mentions(messages, "Sabotage"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "Partnership"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "MoonLanding"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "base", "0.9"), string.Join("; ", messages));
        }

        [Test]
        public void ANullRuleOrANullTable_DoesNotBreakTheRecord()
        {
            var messages = new List<string>();
            Archetype archetype = Accept("{'id':'x','reactions':{'StrongLaunch':null,'OemLoss':[null,{'type':'OemPush','base':0.2}]}}", messages);

            Assert.Equal(0, archetype.RulesFor(TriggerType.StrongLaunch).Count);
            Assert.Equal(1, archetype.RulesFor(TriggerType.OemLoss).Count);
        }

        [Test]
        public void ReactionKinds_ParseExactNamesOnly()
        {
            TriggerType trigger;
            Assert.True(ReactionKinds.TryParseTrigger("TechLead", out trigger));
            Assert.Equal(TriggerType.TechLead, trigger);
            Assert.False(ReactionKinds.TryParseTrigger("techlead", out trigger));
            Assert.False(ReactionKinds.TryParseTrigger("2", out trigger));
            Assert.False(ReactionKinds.TryParseTrigger(null, out trigger));

            ReactionType reaction;
            Assert.True(ReactionKinds.TryParseReaction("Partnership", out reaction));
            Assert.Equal(ReactionType.Partnership, reaction);
            Assert.False(ReactionKinds.TryParseReaction("pricecut", out reaction));
            Assert.False(ReactionKinds.TryParseReaction("", out reaction));

            Assert.Equal(5, ReactionKinds.TriggerNames().Length);
            Assert.Equal(6, ReactionKinds.ReactionNames().Length);
            foreach (TriggerType t in Enum.GetValues(typeof(TriggerType)))
            {
                TriggerType back;
                Assert.True(ReactionKinds.TryParseTrigger(ReactionKinds.NameOf(t), out back) && back == t, "trigger " + t);
            }
            foreach (ReactionType r in Enum.GetValues(typeof(ReactionType)))
            {
                ReactionType back;
                Assert.True(ReactionKinds.TryParseReaction(ReactionKinds.NameOf(r), out back) && back == r, "reaction " + r);
            }
            Assert.True(ReactionKinds.IsHostile(ReactionType.Smear));
            Assert.False(ReactionKinds.IsHostile(ReactionType.Partnership));
            Assert.False(ReactionKinds.IsTableReaction(ReactionType.Partnership));
        }

        [Test]
        public void ReactionParams_UnknownEntriesAreDropped_AndValuesClipped()
        {
            var messages = new List<string>();
            Archetype archetype = Accept(
                "{'id':'x','reactionParams':{'PriceCut':{'depth':0.9,'floorMargin':0.05,'speed':3},'Telepathy':{'depth':0.1},'Poach':{'rivalResearchSpeed':5}}}",
                messages);

            Assert.Equal(0.5f, archetype.ParamOr(ReactionType.PriceCut, "depth", 0.08f), "depth is at most 0.5");
            Assert.Equal(0.05f, archetype.ParamOr(ReactionType.PriceCut, "floorMargin", 0.08f));
            Assert.Equal(7f, archetype.ParamOr(ReactionType.PriceCut, "speed", 7f), "an unknown number is dropped: the default answers");
            Assert.Equal(2f, archetype.ParamOr(ReactionType.Poach, "rivalResearchSpeed", 1.1f));
            Assert.Equal(0.3f, archetype.ParamOr(ReactionType.OemPush, "depth", 0.3f), "no entry: the default answers");
            Assert.False(archetype.reactionParams.ContainsKey("Telepathy"));
            Assert.True(Mentions(messages, "Telepathy"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "speed"), string.Join("; ", messages));
        }

        // ---- archetypes: negotiationDiscount and flawResponseBias ----

        [Test]
        public void NegotiationDiscount_IsABool_AndAcceptedAsIs()
        {
            Assert.True(Accept("{'id':'f','behaviour':{'negotiationDiscount':true}}", new List<string>()).behaviour.negotiationDiscount);
            Assert.False(Accept("{'id':'g','behaviour':{'negotiationDiscount':false}}", new List<string>()).behaviour.negotiationDiscount);
            Assert.False(Accept("{'id':'h'}", new List<string>()).behaviour.negotiationDiscount, "false by default");

            var messages = new List<string>();
            Archetype wrong = Accept("{'id':'i','behaviour':{'negotiationDiscount':'yes'}}", messages);
            Assert.False(wrong.behaviour.negotiationDiscount, "a text is not a bool: the default stays");
            Assert.True(Mentions(messages, "negotiationDiscount"), string.Join("; ", messages));
        }

        [Test]
        public void FlawResponseBias_EachValueIsClippedToHalfAndDouble()
        {
            var messages = new List<string>();
            Archetype archetype = Accept("{'id':'x','flawResponseBias':[0.1,3.0,1.0,2.0]}", messages);

            Assert.Equal(new[] { 0.5f, 2.0f, 1.0f, 2.0f }, archetype.flawResponseBias);
            Assert.True(Mentions(messages, "flawResponseBias[0]"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "flawResponseBias[1]"), string.Join("; ", messages));
            Assert.False(Mentions(messages, "flawResponseBias[2]"), "a value in range is not reported");
        }

        [Test]
        public void FlawResponseBias_WrongLengthGivesAllOnes()
        {
            var messages = new List<string>();
            Archetype archetype = Accept("{'id':'x','flawResponseBias':[1.5,1.5]}", messages);
            Assert.Equal(new[] { 1f, 1f, 1f, 1f }, archetype.flawResponseBias);
            Assert.True(Mentions(messages, "flawResponseBias"), string.Join("; ", messages));

            Archetype missing = Accept("{'id':'y'}", new List<string>());
            Assert.Equal(new[] { 1f, 1f, 1f, 1f }, missing.flawResponseBias, "a missing list is all 1.0 without a complaint");
        }

        // ---- archetypes: the rest of the record ----

        [Test]
        public void AMemberOfTheWrongType_KeepsItsDefault_AndIsReported()
        {
            var messages = new List<string>();
            Archetype archetype = Accept("{'id':'x','behaviour':{'approvalBias':'high'},'cooldownDays':'soon','eventStyle':'wild'}", messages);

            Assert.Equal(1f, archetype.behaviour.approvalBias);
            Assert.Equal(180, archetype.cooldownDays);
            Assert.Equal(EventStyle.Neutral, archetype.eventStyle);
            Assert.True(Mentions(messages, "approvalBias"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "cooldownDays"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "eventStyle"), string.Join("; ", messages));
        }

        [Test]
        public void FocusOverridesAndEventStyle_ReadByName()
        {
            Archetype archetype = Accept(
                "{'id':'x','behaviour':{'designFocusOverride':'LowUnitCost','powerFocusOverride':'LowConsumption'},'eventStyle':'spend'}", new List<string>());
            Assert.Equal(DesignFocusOverride.LowUnitCost, archetype.behaviour.designFocusOverride);
            Assert.Equal(PowerFocusOverride.LowConsumption, archetype.behaviour.powerFocusOverride);
            Assert.Equal(EventStyle.Spend, archetype.eventStyle);

            Archetype none = Accept("{'id':'y','behaviour':{'designFocusOverride':null,'powerFocusOverride':null}}", new List<string>());
            Assert.Equal(DesignFocusOverride.None, none.behaviour.designFocusOverride);
            Assert.Equal(PowerFocusOverride.None, none.behaviour.powerFocusOverride);

            var messages = new List<string>();
            Archetype bad = Accept("{'id':'z','behaviour':{'designFocusOverride':'Inlet'}}", messages);
            Assert.Equal(DesignFocusOverride.None, bad.behaviour.designFocusOverride, "a focus the feature does not know is not applied");
            Assert.True(Mentions(messages, "designFocusOverride"), string.Join("; ", messages));
        }

        [Test]
        public void UnknownMembers_AreWarnings()
        {
            var messages = new List<string>();
            Accept("{'id':'x','colour':'red','behaviour':{'approvalBais':1.1},'attitude':{'mood':3}}", messages);

            Assert.True(Mentions(messages, "colour"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "behaviour.approvalBais"), string.Join("; ", messages));
            Assert.True(Mentions(messages, "attitude.mood"), string.Join("; ", messages));
        }

        [Test]
        public void NameAndDescription_KeyTextOrFallback()
        {
            var messages = new List<string>();
            Archetype bare = Accept("{'id':'zeta'}", messages);
            Assert.Equal("rival.archetype.zeta.name", bare.Name.Key, "no name: the key by the id");
            Assert.Equal("rival.archetype.zeta.desc", bare.Description.Key);
            Assert.True(Mentions(messages, "name"), "a missing name is a warning: " + string.Join("; ", messages));
            Assert.True(Mentions(messages, "description"), string.Join("; ", messages));

            Archetype keyed = Accept("{'id':'a','name':'rival.archetype.a.name','description':'rival.archetype.a.desc'}", new List<string>());
            Assert.Equal("rival.archetype.a.name", keyed.Name.Key);
            Assert.Equal("rival.archetype.a.desc", keyed.Description.Key);

            Archetype inline = Accept("{'id':'b','name':{'ru':'Бета','en':'Beta'},'description':{'key':'rival.custom.desc'}}", new List<string>());
            Assert.Equal("Бета", inline.Name.Ru);
            Assert.Equal("Beta", inline.Name.En);
            Assert.Equal("rival.custom.desc", inline.Description.Key);

            Archetype odd = Accept("{'id':'c','name':42,'description':[]}", new List<string>());
            Assert.Equal("rival.archetype.c.name", odd.Name.Key, "a name of the wrong kind falls back to the key by the id");
        }

        // ---- assignments ----

        [Test]
        public void AnAssignmentOfAnArchetypeThatDoesNotExist_GetsConservative()
        {
            var report = ArchetypeCatalog.Validate(
                new[] { Accept("{'id':'conservative'}", new List<string>()), Accept("{'id':'hegemon'}", new List<string>()) },
                new[]
                {
                    Assignment("{'id':'intel','uniqueId':0,'key':'intel','archetype':'hegemon'}"),
                    Assignment("{'id':'amd','uniqueId':1,'key':'amd','archetype':'time_traveller'}"),
                },
                null);

            ArchetypeCatalog catalog = report.Catalog;
            Assert.Equal("hegemon", catalog.DefaultArchetypeFor(0));
            Assert.Equal("conservative", catalog.DefaultArchetypeFor(1), "the archetype does not exist");
            Assert.Equal("conservative", catalog.AssignmentFor(1).archetype, "the record itself is repaired");
            Assert.True(Mentions(report.Warnings, "time_traveller", "conservative"), string.Join("; ", report.Warnings));
            Assert.True(report.IsClean, "a repaired assignment is a warning, not an error");
        }

        [Test]
        public void AnAssignmentWithoutAKey_IsAWarning_NotAnError()
        {
            var messages = new List<string>();
            AssignmentRecord record = ArchetypeCatalog.MapAssignment(RivalTestKit.Obj("{'id':'intel','uniqueId':0,'archetype':'hegemon'}"), messages);
            Assert.NotNull(record);
            Assert.True(ArchetypeCatalog.ValidateAssignment(record, messages), "accepted");
            Assert.True(Mentions(messages, "key"), string.Join("; ", messages));

            ContentLoad<AssignmentRecord> load = RivalTestKit.LoadAssignments(RivalTestKit.J(Records("{'id':'intel','uniqueId':0,'archetype':'hegemon'}")));
            Assert.Equal(1, load.Records.Count);
            Assert.Equal(0, load.Report.Errors.Count);
            Assert.True(load.Report.Warnings.Count >= 1, "the missing key is reported");
        }

        [Test]
        public void AnAssignmentWithoutAUniqueIdOrAnId_IsRefused()
        {
            var messages = new List<string>();
            AssignmentRecord noUid = ArchetypeCatalog.MapAssignment(RivalTestKit.Obj("{'id':'intel','key':'intel','archetype':'hegemon'}"), messages);
            Assert.NotNull(noUid);
            Assert.False(ArchetypeCatalog.ValidateAssignment(noUid, messages), "uniqueId is required");
            Assert.True(Mentions(messages, "uniqueId"), string.Join("; ", messages));

            Assert.Null(ArchetypeCatalog.MapAssignment(RivalTestKit.Obj("{'uniqueId':3,'key':'ibm','archetype':'engineer'}"), new List<string>()), "no id");

            AssignmentRecord negative = ArchetypeCatalog.MapAssignment(RivalTestKit.Obj("{'id':'x','uniqueId':-4,'key':'x','archetype':'hegemon'}"), new List<string>());
            Assert.False(ArchetypeCatalog.ValidateAssignment(negative, new List<string>()));
            Assert.False(ArchetypeCatalog.ValidateAssignment(null, new List<string>()));
        }

        [Test]
        public void AnAssignmentWithoutAnArchetype_GetsConservativeWithAWarning()
        {
            var messages = new List<string>();
            AssignmentRecord record = ArchetypeCatalog.MapAssignment(RivalTestKit.Obj("{'id':'zilog','uniqueId':2,'key':'zilog'}"), messages);
            Assert.True(ArchetypeCatalog.ValidateAssignment(record, messages));
            Assert.Equal("conservative", record.archetype);
            Assert.True(Mentions(messages, "archetype"), string.Join("; ", messages));
        }

        [Test]
        public void TwoAssignmentsForOneCompany_TheLaterOneWins()
        {
            var report = ArchetypeCatalog.Validate(
                new[] { Accept("{'id':'conservative'}", new List<string>()), Accept("{'id':'hegemon'}", new List<string>()) },
                new[]
                {
                    Assignment("{'id':'intel','uniqueId':0,'key':'intel','archetype':'conservative'}"),
                    Assignment("{'id':'intel-mine','uniqueId':0,'key':'intel','archetype':'hegemon'}"),
                },
                null);

            Assert.Equal(1, report.Catalog.Assignments.Count);
            Assert.Equal("hegemon", report.Catalog.DefaultArchetypeFor(0));
            Assert.True(Mentions(report.Warnings, "uniqueId 0"), string.Join("; ", report.Warnings));
        }

        [Test]
        public void ACompanyWithoutAnAssignment_IsConservative()
        {
            ArchetypeCatalog catalog = ArchetypeCatalog.Validate(
                new[] { Accept("{'id':'conservative'}", new List<string>()), Accept("{'id':'hegemon'}", new List<string>()) },
                new AssignmentRecord[0], null).Catalog;

            Assert.Equal("conservative", catalog.DefaultArchetypeFor(77));
            Assert.Null(catalog.AssignmentFor(77));
        }

        [Test]
        public void WithoutConservative_TheFirstArchetypeStandsIn()
        {
            ArchetypeCatalog catalog = ArchetypeCatalog.Validate(
                new[] { Accept("{'id':'hegemon'}", new List<string>()), Accept("{'id':'engineer'}", new List<string>()) },
                new[] { Assignment("{'id':'amd','uniqueId':1,'key':'amd','archetype':'nope'}") }, null).Catalog;

            Assert.Equal("hegemon", catalog.DefaultArchetypeFor(1));
            Assert.Equal("hegemon", catalog.DefaultArchetypeFor(5));
        }

        [Test]
        public void AnEmptyCatalog_IsNotUsable_AndSaysSo()
        {
            ValidationReport report = ArchetypeCatalog.Validate(new Archetype[0], new[] { Assignment("{'id':'amd','uniqueId':1,'key':'amd','archetype':'x'}") }, null);

            Assert.False(report.Catalog.IsUsable);
            Assert.False(report.IsClean);
            Assert.True(Mentions(report.Errors, "archetypes"), string.Join("; ", report.Errors));
            Assert.Equal(ArchetypeCatalog.DefaultArchetypeId, report.Catalog.DefaultArchetypeFor(1));
            Archetype none;
            Assert.False(report.Catalog.TryGet("conservative", out none));
            Assert.True(ArchetypeCatalog.Empty.Config != null, "the empty catalog has the default numbers");
        }

        [Test]
        public void Validate_RunsTheChecksOnReadyRecords_AndKeepsOnlyTheGoodOnes()
        {
            var good = new Archetype { id = "good", behaviour = new ArchetypeBehaviour { approvalBias = 5f } };
            var noId = new Archetype { id = "" };
            var twin = new Archetype { id = "good" };

            ValidationReport report = ArchetypeCatalog.Validate(new[] { good, noId, null, twin }, null, null);

            Assert.Equal(1, report.Catalog.All.Count);
            Assert.True(ReferenceEquals(good, report.Catalog.All[0]), "the first record of an id stays");
            Assert.Equal(1.3f, good.behaviour.approvalBias, "the record is clipped in place");
            Assert.False(report.IsClean);
            Assert.Equal(3, report.Errors.Count, "no id, empty record, duplicate: " + string.Join("; ", report.Errors));
            Assert.True(Mentions(report.Warnings, "approvalBias"), string.Join("; ", report.Warnings));
        }

        private static AssignmentRecord Assignment(string json)
        {
            var messages = new List<string>();
            AssignmentRecord record = ArchetypeCatalog.MapAssignment(RivalTestKit.Obj(json), messages);
            Assert.NotNull(record, string.Join("; ", messages));
            return record;
        }

        // ---- the config ----

        [Test]
        public void Config_MissingOrEmpty_GivesTheDesignNumbers()
        {
            foreach (JsonObject empty in new JsonObject[] { null, new JsonObject() })
            {
                var messages = new List<string>();
                RivalConfig config = RivalConfig.FromJson(empty, messages);
                Assert.Equal(0, messages.Count);
                Assert.Equal(new[] { 0.4f, 0.7f, 1.0f, 1.2f, 1.4f, 1.6f }, config.difficultyMultipliers);
                Assert.Equal(0.9f, config.chanceCap);
                Assert.Equal(180, config.graceDays);
            }
        }

        [Test]
        public void Config_NumbersOutsideTheirRanges_AreClipped_AndWrongTypesKeepTheDefault()
        {
            var messages = new List<string>();
            RivalConfig config = RivalConfig.FromJson(RivalTestKit.Obj(
                "{'chanceCap':5,'graceDays':-3,'maxConcurrentHostile':'two','telegraphDays':[60,20],'difficultyMultipliers':[1,2],"
                + "'attitude':{'thresholds':[-20,-60,20,60],'monthlyDrift':4},'flawCaution':{'minSeverity':'Huge'},'priceCut':{'externalMaxDays':0}}"), messages);

            Assert.Equal(1f, config.chanceCap);
            Assert.Equal(0, config.graceDays);
            Assert.Equal(2, config.maxConcurrentHostile, "a text is not a number: the default stays");
            Assert.Equal(new[] { 20, 60 }, config.telegraphDays, "the lowest and the highest are swapped");
            Assert.Equal(new[] { 0.4f, 0.7f, 1.0f, 1.2f, 1.4f, 1.6f }, config.difficultyMultipliers, "a table of the wrong length gives the default one");
            Assert.Equal(new[] { -60, -20, 20, 60 }, config.attitude.thresholds, "borders out of order give the default ones");
            Assert.Equal(1f, config.attitude.monthlyDrift);
            Assert.Equal("Major", config.flawCaution.minSeverity);
            Assert.Equal(1, config.priceCut.externalMaxDays);
            Assert.True(messages.Count >= 8, "every repair is reported: " + string.Join("; ", messages));
        }

        [Test]
        public void Config_ReadTwice_GivesTheSameNumbers()
        {
            var first = new List<string>();
            RivalConfig config = RivalConfig.FromJson(RivalTestKit.Obj("{'chanceCap':5,'telegraphDays':[60,20]}"), first);
            var second = new List<string>();
            config.Normalize(second);

            Assert.Equal(0, second.Count, "normalising twice changes nothing: " + string.Join("; ", second));
        }

        [Test]
        public void Config_NullGroupsBecomeDefaultGroups()
        {
            RivalConfig config = RivalConfig.FromJson(RivalTestKit.Obj("{'attitude':null,'reactions':null,'flawCaution':null}"), new List<string>());

            Assert.NotNull(config.attitude);
            Assert.NotNull(config.reactions);
            Assert.NotNull(config.reactions.Poach);
            Assert.Equal(0.85f, config.flawCaution.factor);
        }

        [Test]
        public void Config_DifficultyMultiplierOutsideTheTable_UsesTheNearestEnd()
        {
            RivalConfig config = RivalConfig.Defaults();
            Assert.Equal(0.4f, config.DifficultyMultiplier(0));
            Assert.Equal(1.0f, config.DifficultyMultiplier(2));
            Assert.Equal(1.6f, config.DifficultyMultiplier(5));
            Assert.Equal(0.4f, config.DifficultyMultiplier(-9));
            Assert.Equal(1.6f, config.DifficultyMultiplier(99));
        }

        // ---- the shipped content (TSK-003) ----

        [Test]
        public void Shipped_Archetypes_AreTheSevenOfTheDesign_WithoutAnyRepair()
        {
            ContentLoad<Archetype> load = RivalTestKit.LoadArchetypes(RivalTestKit.ReadContent("archetypes.json"));

            Assert.Equal(0, load.Report.Errors.Count, string.Join("; ", load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, "nothing needed a repair: " + string.Join("; ", load.Report.Warnings));
            Assert.Equal(
                new[] { "hegemon", "price_warrior", "fast_follower", "engineer", "conservative", "niche_specialist", "foundry_partner" },
                load.Records.Select(r => r.Id).ToArray());
        }

        [Test]
        public void Shipped_Archetypes_MatchTheTableOfDesign()
        {
            // design.md, "Полная таблица архетипов (значения по умолчанию в контенте)"
            ArchetypeCatalog catalog = RivalTestKit.LoadShippedCatalog();

            // id: approvalBias, researchFundingMult, launchPriceMult, baseline, sensitivity, cooldownDays, PriceCut depth (NaN = no entry)
            var numbers = new Dictionary<string, float[]>
            {
                { "hegemon",          new[] { 1.05f, 1.10f, 1.05f, -10f, 1.2f, 180f, 0.08f } },
                { "price_warrior",    new[] { 1.00f, 0.95f, 0.92f, -15f, 1.3f, 150f, 0.15f } },
                { "fast_follower",    new[] { 1.12f, 1.00f, 0.96f, 0f, 1.0f, 150f, 0.08f } },
                { "engineer",         new[] { 0.92f, 1.20f, 1.08f, 0f, 0.8f, 240f, 0.08f } },
                { "conservative",     new[] { 0.90f, 0.90f, 1.03f, 10f, 0.6f, 300f, 0.08f } },
                { "niche_specialist", new[] { 1.00f, 0.95f, 1.00f, 5f, 0.8f, 200f, 0.10f } },
                { "foundry_partner",  new[] { 1.00f, 1.00f, 1.00f, 20f, 0.5f, 365f, float.NaN } },
            };
            var styles = new Dictionary<string, EventStyle>
            {
                { "hegemon", EventStyle.Spend }, { "price_warrior", EventStyle.Save }, { "fast_follower", EventStyle.Neutral },
                { "engineer", EventStyle.Spend }, { "conservative", EventStyle.Save }, { "niche_specialist", EventStyle.Save },
                { "foundry_partner", EventStyle.Neutral },
            };
            var bias = new Dictionary<string, float[]>
            {
                { "hegemon", new[] { 0.9f, 1.2f, 1.0f, 1.2f } }, { "price_warrior", new[] { 0.8f, 1.0f, 1.3f, 1.1f } },
                { "fast_follower", new[] { 1.0f, 1.0f, 1.2f, 1.0f } }, { "engineer", new[] { 1.0f, 1.4f, 0.9f, 0.7f } },
                { "conservative", new[] { 1.5f, 1.0f, 0.9f, 0.6f } }, { "niche_specialist", new[] { 1.0f, 1.0f, 1.2f, 1.0f } },
                { "foundry_partner", new[] { 1.0f, 1.0f, 1.0f, 1.0f } },
            };

            Assert.Equal(7, catalog.All.Count);
            foreach (Archetype a in catalog.All)
            {
                float[] n = numbers[a.Id];
                string who = a.Id + ": ";
                Assert.Equal(n[0], a.behaviour.approvalBias, who + "approvalBias");
                Assert.Equal(n[1], a.behaviour.researchFundingMult, who + "researchFundingMult");
                Assert.Equal(n[2], a.behaviour.launchPriceMult, who + "launchPriceMult");
                Assert.Equal(n[3], a.attitude.baseline, who + "baseline");
                Assert.Equal(n[4], a.attitude.sensitivity, who + "sensitivity");
                Assert.Equal((int)n[5], a.cooldownDays, who + "cooldownDays");
                Assert.Equal(n[6], a.ParamOr(ReactionType.PriceCut, "depth", float.NaN), who + "PriceCut depth");
                Assert.Equal(styles[a.Id], a.eventStyle, who + "eventStyle");
                Assert.Equal(bias[a.Id], a.flawResponseBias, who + "flawResponseBias");
                Assert.Equal(a.Id == "foundry_partner", a.behaviour.negotiationDiscount, who + "negotiationDiscount only for the foundry partner");
                Assert.Equal(a.Id == "price_warrior" ? DesignFocusOverride.LowUnitCost : a.Id == "engineer" ? DesignFocusOverride.HighPerformance : DesignFocusOverride.None,
                    a.behaviour.designFocusOverride, who + "designFocusOverride");
                Assert.Equal(a.Id == "niche_specialist" ? PowerFocusOverride.LowConsumption : PowerFocusOverride.None,
                    a.behaviour.powerFocusOverride, who + "powerFocusOverride");
            }
        }

        [Test]
        public void Shipped_ReactionTables_MatchTheTableOfDesign()
        {
            ArchetypeCatalog catalog = RivalTestKit.LoadShippedCatalog();

            // "*" marks anyAttitude. The rows of a trigger are in the order of the table.
            var expected = new Dictionary<string, string>
            {
                { "hegemon", "StrongLaunch=CounterLaunch:0.30,OemPush:0.20|PriceUndercut=PriceCut:0.15|ShareGain=OemPush:0.30,Smear:0.10|OemLoss=OemPush:0.35|TechLead=Poach:0.15" },
                { "price_warrior", "StrongLaunch=PriceCut:0.40*,CounterLaunch:0.10|PriceUndercut=PriceCut:0.50*|ShareGain=PriceCut:0.35*|OemLoss=OemPush:0.10" },
                { "fast_follower", "StrongLaunch=CounterLaunch:0.45|PriceUndercut=PriceCut:0.15|TechLead=Poach:0.10" },
                { "engineer", "StrongLaunch=CounterLaunch:0.25|ShareGain=CounterLaunch:0.15|TechLead=Poach:0.25" },
                { "conservative", "StrongLaunch=CounterLaunch:0.10|PriceUndercut=PriceCut:0.10|OemLoss=OemPush:0.10" },
                { "niche_specialist", "StrongLaunch=CounterLaunch:0.15|PriceUndercut=PriceCut:0.30*|ShareGain=PriceCut:0.25*" },
                { "foundry_partner", "" },
            };

            foreach (Archetype a in catalog.All)
            {
                var parts = new List<string>();
                foreach (TriggerType trigger in Enum.GetValues(typeof(TriggerType)))
                {
                    IReadOnlyList<ReactionRule> rules = a.RulesFor(trigger);
                    if (rules.Count == 0) continue;
                    parts.Add(ReactionKinds.NameOf(trigger) + "="
                              + string.Join(",", rules.Select(r => r.type + ":" + r.@base.ToString("0.00", CultureInfo.InvariantCulture) + (r.anyAttitude ? "*" : ""))));
                }
                Assert.Equal(expected[a.Id], string.Join("|", parts), a.Id);
            }
        }

        [Test]
        public void Shipped_Assignments_AreThirteenCompaniesFromZeroToTwelve()
        {
            ContentLoad<AssignmentRecord> load = RivalTestKit.LoadAssignments(RivalTestKit.ReadContent("assignments.json"));
            Assert.Equal(0, load.Report.Errors.Count, string.Join("; ", load.Report.Errors));
            Assert.Equal(0, load.Report.Warnings.Count, "every record has its key and an archetype: " + string.Join("; ", load.Report.Warnings));
            Assert.Equal(13, load.Records.Count);
            Assert.Equal(Enumerable.Range(0, 13).ToArray(), load.Records.Select(r => r.uniqueId).ToArray());

            // design.md, "assignments.json — назначение 13 компаниям": uniqueId, key, archetype
            string[] table =
            {
                "0 intel hegemon", "1 amd price_warrior", "2 zilog niche_specialist", "3 texas_instruments conservative", "4 ibm engineer",
                "5 fairchild conservative", "6 toshiba fast_follower", "7 motorola engineer", "8 globalfoundries foundry_partner",
                "9 tsmc foundry_partner", "10 cyrix fast_follower", "11 via price_warrior", "12 digital_devices niche_specialist",
            };
            Assert.Equal(table, load.Records.Select(r => r.uniqueId + " " + r.key + " " + r.archetype).ToArray());
            Assert.True(load.Records.All(r => r.id == r.key), "the id of a record is the key of the company");
        }

        [Test]
        public void Shipped_Assignments_AllNameAnExistingArchetype()
        {
            ArchetypeCatalog catalog = RivalTestKit.LoadShippedCatalog();   // no warning means no repaired assignment

            Assert.Equal(13, catalog.Assignments.Count);
            foreach (AssignmentRecord record in catalog.Assignments)
            {
                Archetype archetype;
                Assert.True(catalog.TryGet(record.archetype, out archetype), record.key + " -> " + record.archetype);
                Assert.Equal(record.archetype, catalog.DefaultArchetypeFor(record.uniqueId));
            }
            Assert.Equal("conservative", catalog.DefaultArchetypeFor(40), "an unknown company");
            Assert.True(catalog.IsUsable);
        }

        [Test]
        public void Shipped_Names_AreNotCopiedIntoTheAssignments()
        {
            // design.md: the content has the uniqueId and the machine key only; the names of the companies come from the game
            foreach (AssignmentRecord record in RivalTestKit.LoadAssignments(RivalTestKit.ReadContent("assignments.json")).Records)
            {
                Assert.True(record.key.All(c => (c >= 'a' && c <= 'z') || c == '_'), "the key is a machine name: " + record.key);
            }
            JsonObject root = RivalTestKit.ParseObject(RivalTestKit.ReadContent("assignments.json"));
            foreach (JsonNode node in root.GetArray("records"))
            {
                var record = (JsonObject)node;
                Assert.False(record.ContainsKey("name"), "no display name in the assignments");
            }
        }

        [Test]
        public void Shipped_Config_HasTheNumbersOfDesign_AndNoSegmentPriceBand()
        {
            var messages = new List<string>();
            RivalConfig config = RivalTestKit.LoadConfig(messages);
            Assert.Equal(0, messages.Count, "config.json needs no repair: " + string.Join("; ", messages));

            Assert.Equal(new[] { 0.4f, 0.7f, 1.0f, 1.2f, 1.4f, 1.6f }, config.difficultyMultipliers);
            Assert.Equal(0.9f, config.chanceCap);
            Assert.Equal(180, config.graceDays);
            Assert.Equal(2, config.maxConcurrentHostile);
            Assert.Equal(4, config.maxHostilePerYear);
            Assert.Equal(new[] { 14, 45 }, config.telegraphDays);
            Assert.Equal(30, config.mailWaitDays, "mailWaitDays");

            Assert.Equal(new[] { -60, -20, 20, 60 }, config.attitude.thresholds);
            Assert.Equal(0.10f, config.attitude.monthlyDrift);
            Assert.Equal(20, config.attitude.historySize);
            Assert.Equal(-20, config.attitude.hostileGate);
            Assert.Equal(5, config.attitude.negotiationDivisor);
            Assert.Equal(10, config.attitude.discountDivisor);
            Assert.Equal(10, config.attitude.discountMaxPoints);
            Assert.Equal(20, config.attitude.revealAttitude, "attitude.revealAttitude");

            Assert.Equal(-8f, config.triggerDeltas.StrongLaunch);
            Assert.Equal(-5f, config.triggerDeltas.ShareGain);
            Assert.Equal(-4f, config.triggerDeltas.PriceUndercut);
            Assert.Equal(-6f, config.triggerDeltas.OemLoss);
            Assert.Equal(-3f, config.triggerDeltas.TechLead);
            Assert.Equal(15f, config.triggerDeltas.BusinessSigned);
            Assert.Equal(-25f, config.triggerDeltas.BusinessBrokenByPlayer);
            Assert.Equal(2f, config.triggerDeltas.BusinessActiveMonthly);

            Assert.Equal(1.15f, config.triggerThresholds.strongLaunchIpsRatio);
            Assert.Equal(3.0f, config.triggerThresholds.shareGainPp);
            Assert.Equal(0.85f, config.triggerThresholds.undercutPriceRatio);
            Assert.Equal(0.90f, config.triggerThresholds.undercutIpsRatio);
            Assert.Equal(90, config.triggerThresholds.undercutCooldownDays);
            Assert.Equal(1.05f, config.triggerThresholds.minApprovalForBoost);
            Assert.Equal(new[] { "Lithography", "Frequency", "Multicore" }, config.triggerThresholds.techLeadTypes.ToArray());

            Assert.Equal(0.08f, config.reactions.PriceCut.depth);
            Assert.Equal(0.08f, config.reactions.PriceCut.floorMargin);
            Assert.Equal(1.35f, config.reactions.CounterLaunch.approvalBoost);
            Assert.Equal(1.25f, config.reactions.CounterLaunch.fundingBoost);
            Assert.Equal(1.10f, config.reactions.Poach.rivalResearchSpeed);
            Assert.Equal(120, config.reactions.Poach.rivalBoostDays);
            Assert.Equal(250000, config.reactions.Poach.minCost);
            Assert.Equal(2, config.reactions.Poach.researchMonths);
            Assert.Equal(1.5f, config.reactions.Poach.underfundedChanceMult);
            Assert.Equal(40, config.reactions.Partnership.minAttitude);
            Assert.Equal(0.10f, config.reactions.Partnership.monthlyChance);
            Assert.Equal(6, config.reactions.Partnership.repeatMonths);

            Assert.Equal(400, config.priceCut.externalMaxDays, "priceCut.externalMaxDays");
            Assert.Equal(0.85f, config.flawCaution.factor, "flawCaution");
            Assert.Equal(24, config.flawCaution.months);
            Assert.Equal("Major", config.flawCaution.minSeverity);
            Assert.Equal(5f, config.intelFog.shareStepPp, "intelFog");
            Assert.Equal(2, config.intelFog.ipsSignificantDigits);
            Assert.Equal(1.5f, config.eventChooser.styleWeight);
            Assert.Equal(100, config.eventChooser.priority);
            Assert.Equal(200, config.acquisition.hostilePriceDivisor);
            Assert.Equal(-10, config.acquisition.hostileDealAttitude);
            Assert.Equal(0.05f, config.launchPriceFloorMargin);

            // the file and the defaults of the code say the same
            RivalConfig defaults = RivalConfig.Defaults();
            Assert.Equal(ModJson.Write(ModJson.From(defaults), false), ModJson.Write(ModJson.From(config), false), "config.json differs from the defaults of the code");

            // the segment of a CPU is the core's: segments.priceBand of core.json, never a number of this feature
            JsonObject raw = RivalTestKit.ParseObject(RivalTestKit.ReadContent("config.json"));
            Assert.False(raw.ContainsKey("segmentPriceBand"), "segmentPriceBand belongs to the core");
            Assert.False(raw.ContainsKey("segments"), "no segment rule in config.json");
            Assert.False(RivalTestKit.ReadContent("config.json").IndexOf("priceBand", StringComparison.OrdinalIgnoreCase) >= 0, "no price band in config.json");
        }

        // ---- the events (TSK-003) ----

        private sealed class EventStub : IContentRecord
        {
            public string Id { get; set; }
            public JsonObject Json { get; set; }
        }

        private static List<EventStub> LoadEvents(out ContentReport report)
        {
            ContentLoad<EventStub> load = ContentMerger.MergeRecords<EventStub>(RivalTestKit.Feature, "events.json", RivalTestKit.ReadContent("events.json"), null,
                (json, errors) => new EventStub { Id = json.GetString("id", null), Json = json }, null, 1);
            report = load.Report;
            return load.Records.ToList();
        }

        [Test]
        public void Shipped_Events_AreFiveRivalEvents_WithIdsByTheRule()
        {
            ContentReport report;
            List<EventStub> events = LoadEvents(out report);

            Assert.Equal(0, report.Errors.Count, string.Join("; ", report.Errors));
            Assert.Equal(
                new[] { "rival.priceCut", "rival.counterLaunch", "rival.oemPush", "rival.poach", "rival.smear" },
                events.Select(e => e.Id).ToArray());

            var problems = new List<string>();
            try
            {
                ModIds.Load(RivalTestKit.CorePrefixes(), problems);
                Assert.Equal(0, problems.Count, string.Join("; ", problems));
                foreach (EventStub e in events)
                {
                    Assert.True(ModIds.IsEventIdAllowed(RivalTestKit.Feature, e.Id), ModIds.Explain(IdKind.EventId, RivalTestKit.Feature, e.Id));
                }
            }
            finally
            {
                ModIds.Clear();
            }
        }

        [Test]
        public void Shipped_Events_HaveTheShapeOfTheEventSystem()
        {
            ContentReport report;
            List<EventStub> events = LoadEvents(out report);

            var knownEffects = new HashSet<string>
            {
                "RivalPriceCut", "RivalCounterLaunch", "RivalResearchBoost", "RivalPoachCounterOffer", "RivalAttitude", "ModifierTimed", "Reputation",
            };
            JsonArray channelList = CoreChannels();

            foreach (EventStub e in events)
            {
                JsonObject json = e.Json;
                Assert.Equal("rival", json.GetString("category", null), e.Id + " category");
                Assert.Equal("Manual", json.GetObject("trigger").GetString("type", null), e.Id + " is fired from code");
                // The event system refuses a Specific target without a companyKey, so the definition names the kind of the target and the call
                // (EventFireContext.CompanyKey) names the company: a rival for the blows, the player for the poaching and the smear.
                bool aimedAtThePlayer = e.Id == "rival.poach" || e.Id == "rival.smear";
                Assert.Equal(aimedAtThePlayer ? "Player" : "RandomAI", json.GetObject("targets").GetString("type", null), e.Id + " target");
                Assert.False(json.GetObject("targets").ContainsKey("companyKey"), e.Id + ": the company is given at the call");
                foreach (string text in new[] { "title", "text", "cause" })
                {
                    JsonObject loc = json.GetObject(text);
                    Assert.NotNull(loc, e.Id + " " + text);
                    Assert.True(!string.IsNullOrEmpty(loc.GetString("key", null)), e.Id + " " + text + " is a key of the string tables");
                    Assert.True(loc.GetString("key", "").StartsWith("rival.", StringComparison.Ordinal), e.Id + " " + text + " key has the prefix");
                }

                foreach (JsonObject effect in AllEffects(json))
                {
                    string type = effect.GetString("type", null);
                    Assert.True(knownEffects.Contains(type), e.Id + ": unknown effect type " + type);
                    if (type == "ModifierTimed")
                    {
                        JsonObject p = effect.GetObject("params");
                        Assert.True(ChannelExists(channelList, p.GetString("channel", null)), e.Id + ": no such channel " + p.GetString("channel", null));
                        Assert.Equal("Target", p.GetString("scope", null), e.Id + ": scope");
                        Assert.True(effect.GetInt32("durationDays", 0) >= 1, e.Id + ": a timed effect needs durationDays");
                    }
                }
            }
        }

        [Test]
        public void Shipped_Events_CarryTheNumbersOfDesign()
        {
            ContentReport report;
            Dictionary<string, JsonObject> events = LoadEvents(out report).ToDictionary(e => e.Id, e => e.Json);

            // PriceCut 180 days, CounterLaunch 150, OemPush contract.signChance x1.5 for 365
            JsonObject priceCut = Single(events["rival.priceCut"].GetArray("effects"));
            Assert.Equal("RivalPriceCut", priceCut.GetString("type", null));
            Assert.Equal(180, priceCut.GetInt32("durationDays", 0));
            Assert.True(events["rival.priceCut"].GetBool("notifyPlayer", false));

            JsonObject counter = Single(events["rival.counterLaunch"].GetArray("effects"));
            Assert.Equal("RivalCounterLaunch", counter.GetString("type", null));
            Assert.Equal(150, counter.GetInt32("durationDays", 0));

            JsonObject oem = Single(events["rival.oemPush"].GetArray("effects"));
            Assert.Equal("ModifierTimed", oem.GetString("type", null));
            Assert.Equal("contract.signChance", oem.GetObject("params").GetString("channel", null));
            Assert.Near(1.5, oem.GetObject("params").GetDouble("multiplier", 0), 1e-9);
            Assert.Equal(365, oem.GetInt32("durationDays", 0));

            // Smear: reputation -3
            JsonObject smear = Single(events["rival.smear"].GetArray("effects"));
            Assert.Equal("Reputation", smear.GetString("type", null));
            Assert.Near(-3.0, smear.GetObject("params").GetDouble("delta", 0), 1e-9);

            // Poach: a choice with a timer of 10 days, "let go" by default
            JsonObject poach = events["rival.poach"];
            Assert.True(poach.GetBool("important", false));
            Assert.Equal(10, poach.GetInt32("timerDays", 0));
            Assert.Equal("letGo", poach.GetString("defaultChoice", null));
            JsonArray choices = poach.GetArray("choices");
            Assert.Equal(2, choices.Count);
            JsonObject counterOffer = (JsonObject)choices[0];
            JsonObject letGo = (JsonObject)choices[1];
            Assert.Equal("counterOffer", counterOffer.GetString("id", null));
            Assert.Equal("RivalPoachCounterOffer", Single(counterOffer.GetArray("effects")).GetString("type", null));
            Assert.Equal("letGo", letGo.GetString("id", null));
            JsonArray letGoEffects = letGo.GetArray("effects");
            Assert.Equal(2, letGoEffects.Count);
            JsonObject slow = (JsonObject)letGoEffects[0];
            Assert.Equal("research.speed", slow.GetObject("params").GetString("channel", null));
            Assert.Near(0.85, slow.GetObject("params").GetDouble("multiplier", 0), 1e-9);
            Assert.Equal(120, slow.GetInt32("durationDays", 0));
            Assert.Equal("RivalResearchBoost", ((JsonObject)letGoEffects[1]).GetString("type", null));
        }

        [Test]
        public void Shipped_Texts_ExistInRussianAndEnglish()
        {
            JsonObject ru = RivalTestKit.ReadL10n("ru");
            JsonObject en = RivalTestKit.ReadL10n("en");

            var keys = new List<string> { "rival.feature.name", "rival.feature.desc" };
            foreach (ParamSpec spec in RivalParams.Create(RivalTestKit.Feature))
            {
                keys.Add(spec.LabelKey);
                keys.Add(spec.TooltipKey);
            }
            foreach (Archetype a in RivalTestKit.LoadShippedCatalog().All)
            {
                keys.Add(a.Name.Key);
                keys.Add(a.Description.Key);
            }
            foreach (string level in new[] { "hostile", "rivalry", "neutral", "respect", "partnership" }) keys.Add("rival.level." + level);

            ContentReport report;
            foreach (EventStub e in LoadEvents(out report))
            {
                foreach (string text in new[] { "title", "text", "cause" }) keys.Add(e.Json.GetObject(text).GetString("key", null));
                JsonArray choices = e.Json.GetArray("choices");
                if (choices == null) continue;
                foreach (JsonNode choice in choices) keys.Add(((JsonObject)choice).GetString("textKey", null));
            }

            var missing = new List<string>();
            foreach (string key in keys.Distinct())
            {
                if (!ru.ContainsKey(key)) missing.Add("ru: " + key);
                if (!en.ContainsKey(key)) missing.Add("en: " + key);
            }
            Assert.Equal(0, missing.Count, "keys without a text: " + string.Join(", ", missing));
        }

        // ---- small helpers for the event checks ----

        private static JsonArray CoreChannels()
        {
            JsonArray channels = PTMod.Tests.Architecture.CoreContentTests.LoadCore().GetArray("channels");
            Assert.NotNull(channels, "core.json lacks 'channels'");
            return channels;
        }

        private static bool ChannelExists(JsonArray channels, string name)
        {
            if (name == null) return false;
            foreach (JsonNode node in channels)
            {
                var channel = node as JsonObject;
                if (channel != null && channel.GetString("name", null) == name) return true;
            }
            return false;
        }

        private static JsonObject Single(JsonArray array)
        {
            Assert.NotNull(array);
            Assert.Equal(1, array.Count);
            return (JsonObject)array[0];
        }

        private static IEnumerable<JsonObject> AllEffects(JsonObject eventJson)
        {
            var result = new List<JsonObject>();
            JsonArray own = eventJson.GetArray("effects");
            if (own != null) foreach (JsonNode node in own) result.Add((JsonObject)node);

            JsonArray choices = eventJson.GetArray("choices");
            if (choices != null)
            {
                foreach (JsonNode choice in choices)
                {
                    JsonArray effects = ((JsonObject)choice).GetArray("effects");
                    if (effects != null) foreach (JsonNode node in effects) result.Add((JsonObject)node);
                }
            }
            return result;
        }
    }
}
