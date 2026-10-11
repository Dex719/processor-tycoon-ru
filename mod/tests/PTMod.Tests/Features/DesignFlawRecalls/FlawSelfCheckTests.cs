using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-023 (NFR-2, NFR-3, NFR-8, FR-15; design.md, "Самопроверка на старте" and "Error Handling"): the self-check at start and the isolation of errors. The rules
    /// (<see cref="FlawSelfCheckRules"/>) run on fake facts: what a missing patch target or field costs (one ability, one warning), what the missing required parts cost
    /// (the feature: fatal), what only gets a line in the log. The game side (<c>FlawSelfCheck.cs</c>) and the guards of every boundary are checked on the sources, and
    /// the switching of the feature off in the middle of a game (manual scenario 10) is played on the pure engines.
    /// </summary>
    [Covers(typeof(FlawSelfCheckRules), typeof(FlawSelfCheckReport), typeof(FlawSelfCheckInput), typeof(FlawCheckFinding), typeof(FlawCheckSeverity), typeof(FlawCapability),
        typeof(FlawPatchFact), typeof(FlawPatchTarget), typeof(FlawMemberSpec), typeof(FlawChannelFact))]
    public class FlawSelfCheckTests
    {
        // ---- a healthy world ----

        private static FlawSelfCheckInput Healthy()
        {
            return new FlawSelfCheckInput
            {
                Patches = FlawSelfCheckRules.PatchTargets.Select(t => new FlawPatchFact(t.Target, true, false, "Applied", null)).ToList(),
                MemberProblem = member => null,
                ReleaseSubscribed = true,
                TransferSubscribed = true,
                ReleaseTracked = true,
                EventsLoaded = true,
                HasEvent = id => true,
                EffectTypes = new List<string> { "Money", "Popularity", FlawEffectTypes.Recall, FlawEffectTypes.Stepping, FlawEffectTypes.SoftwarePatch, FlawEffectTypes.Ignore, FlawEffectTypes.News },
                Channels = new List<FlawChannelFact>
                {
                    new FlawChannelFact(FlawChannelNames.MarketRatingCpu, true, true),
                    new FlawChannelFact(FlawChannelNames.ContractSignChance, true, true),
                },
                EventIdProblem = id => null,
                Keys = new List<string> { "dfr.feature.name", "dfr.feature.desc" },
                KeyProblem = key => null,
            };
        }

        private static FlawSelfCheckReport Run(Action<FlawSelfCheckInput> change)
        {
            FlawSelfCheckInput input = Healthy();
            change(input);
            return FlawSelfCheckRules.Run(input);
        }

        private static void Drop(FlawSelfCheckInput input, string target)
        {
            input.Patches = input.Patches.Where(p => p.Target != target).ToList();
        }

        private static void Break(FlawSelfCheckInput input, string target, string status = "Failed", string detail = "the target was not found")
        {
            input.Patches = input.Patches.Select(p => p.Target == target ? new FlawPatchFact(p.Target, false, status == "Rejected", status, detail) : p).ToList();
        }

        private static readonly FlawCapability[] AllAbilities = { FlawCapability.CardMark, FlawCapability.RiskHint, FlawCapability.RushProject };

        // ---- a clean start ----

        [Test]
        public void ACleanWorld_GivesNoFinding_AndEveryAbilityIsOn()
        {
            FlawSelfCheckReport report = FlawSelfCheckRules.Run(Healthy());

            Assert.True(report.IsOk, string.Join("; ", report.Lines()));
            Assert.False(report.IsFatal);
            Assert.False(report.HasErrors);
            Assert.True(report.EventsChecked, "the six events were looked for");
            Assert.Equal(FlawSelfCheckReport.OkLine, report.Summary);
            Assert.Equal("self-check: OK", report.Summary);
            Assert.Equal(0, report.Lines().Count);
            foreach (FlawCapability ability in AllAbilities) Assert.True(report.IsOn(ability), ability.ToString());
            Assert.Equal(string.Empty, report.FatalReason);
        }

        [Test]
        public void ANullInput_GivesOneWarning_AndNothingIsFatal()
        {
            FlawSelfCheckReport report = FlawSelfCheckRules.Run(null);

            Assert.Equal(1, report.Warnings);
            Assert.False(report.IsFatal);
            Assert.True(report.Has(FlawCheckSeverity.Warning, FlawSelfCheckRules.AreaCheck));
        }

        [Test]
        public void FactsThatCouldNotBeCollected_AreNotesAndNotFindings()
        {
            // null means "could not be looked at": the check says so (in the debug log) and does not invent a problem.
            FlawSelfCheckReport report = FlawSelfCheckRules.Run(new FlawSelfCheckInput { EventsLoaded = true, HasEvent = id => true });

            Assert.True(report.IsOk, string.Join("; ", report.Lines()));
            Assert.True(report.Notes.Count >= 6, "one note per part that was not looked at: " + report.Notes.Count);
            Assert.False(report.IsFatal);
        }

        // ---- the patches: the design's list ----

        [Test]
        public void TheTargets_AreTheFiveOfTheDesign_PlusTheSwitchRefresh_AllOptionalPatchesOfTheSources()
        {
            string[] targets = FlawSelfCheckRules.PatchTargets.Select(t => t.Target).OrderBy(t => t, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[]
            {
                "CpuSalesUI.UpdateDailyTexts", "CreatorCpu.CreateCpu", "CreatorCpu.Project", "CreatorCpu.UpdateInfoTexts", "CreatorCpu.UpdateUI", "ProjectCompletedHandler.AddCpu",
            }, targets);

            // The list is the list of the [ModPatch] declarations of the feature, no more and no fewer.
            var declared = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> file in FlawApiKit.AllCode())
            {
                foreach (Match m in Regex.Matches(file.Value, @"\[ModPatch\(typeof\((\w+)\),\s*""(\w+)"""))
                {
                    declared.Add(m.Groups[1].Value + "." + m.Groups[2].Value);
                }
            }
            Assert.Equal(declared.OrderBy(t => t, StringComparer.Ordinal).ToArray(), targets, "the patches of the sources and the table of the self-check");

            foreach (KeyValuePair<string, string> file in FlawApiKit.AllCode())
            {
                foreach (Match m in Regex.Matches(file.Value, @"\[ModPatch\(.*?\)\]"))
                {
                    Assert.True(m.Value.Contains("Optional = true"), file.Key + ": " + m.Value + " must be Optional (a miss costs only its ability)");
                }
            }
        }

        [Test]
        public void TheAbilities_AreTheMark_TheHint_AndTheRushSwitch_WithTheirTargets()
        {
            Func<FlawCapability, string[]> targetsOf = ability => FlawSelfCheckRules.PatchTargets.Where(t => t.Capability == ability).Select(t => t.Target).OrderBy(t => t, StringComparer.Ordinal).ToArray();

            Assert.Equal(new[] { "CpuSalesUI.UpdateDailyTexts" }, targetsOf(FlawCapability.CardMark));
            Assert.Equal(new[] { "CreatorCpu.UpdateInfoTexts" }, targetsOf(FlawCapability.RiskHint));
            Assert.Equal(new[] { "CreatorCpu.CreateCpu", "CreatorCpu.Project", "CreatorCpu.UpdateUI", "ProjectCompletedHandler.AddCpu" }, targetsOf(FlawCapability.RushProject));
        }

        [Test]
        public void AMissingTarget_SwitchesOffOnlyItsOwnAbility_WithOneWarning_NotFatal()
        {
            foreach (FlawPatchTarget target in FlawSelfCheckRules.PatchTargets)
            {
                FlawSelfCheckReport report = Run(input => Drop(input, target.Target));

                foreach (FlawCapability ability in AllAbilities)
                {
                    Assert.Equal(ability != target.Capability, report.IsOn(ability), target.Target + " / " + ability);
                }
                Assert.False(report.IsFatal, target.Target + " is optional");
                Assert.False(report.HasErrors, target.Target + " is a warning");
                Assert.Equal(1, report.Warnings, target.Target);
                Assert.True(report.Has(FlawCheckSeverity.Warning, FlawSelfCheckRules.AreaAbilities));
                Assert.True(report.Findings.Single().Message.Contains(target.Target), "the line names the target: " + report.Findings.Single().Message);
            }
        }

        [Test]
        public void APatchThatFailed_IsReportedWithTheReasonOfTheCore()
        {
            FlawSelfCheckReport report = Run(input => Break(input, "CpuSalesUI.UpdateDailyTexts", "Failed", "Parameter \"___popularityText\" not found"));

            Assert.False(report.IsOn(FlawCapability.CardMark));
            Assert.True(report.IsOn(FlawCapability.RiskHint) && report.IsOn(FlawCapability.RushProject));
            Assert.True(report.Findings.Single().Message.Contains("failed") && report.Findings.Single().Message.Contains("___popularityText"), report.Findings.Single().Message);
            Assert.False(report.IsFatal);
        }

        [Test]
        public void APatchTheCoreRefused_IsAnError_AndStillOnlyCostsItsAbility()
        {
            FlawSelfCheckReport report = Run(input => Break(input, "CreatorCpu.Project", "Rejected", "the target is reserved by the core"));

            Assert.True(report.Has(FlawCheckSeverity.Error, FlawSelfCheckRules.AreaPatches), "a patch the feature should never have made");
            Assert.False(report.IsOn(FlawCapability.RushProject));
            Assert.True(report.IsOn(FlawCapability.CardMark) && report.IsOn(FlawCapability.RiskHint));
            Assert.False(report.IsFatal, "the patches are optional");
        }

        [Test]
        public void SeveralMissingPartsOfOneAbility_AreOneWarning()
        {
            FlawSelfCheckReport report = Run(input =>
            {
                Drop(input, "CreatorCpu.Project");
                Drop(input, "CreatorCpu.CreateCpu");
                input.MemberProblem = member => member.Member == "projectCost" || member.Member == "createCpuButton" ? "the class has no such field" : null;
            });

            Assert.False(report.IsOn(FlawCapability.RushProject));
            Assert.Equal(1, report.Findings.Count(f => f.Area == FlawSelfCheckRules.AreaAbilities), "one warning for the ability");
            string line = report.Findings.Single(f => f.Area == FlawSelfCheckRules.AreaAbilities).Message;
            Assert.True(line.Contains("CreatorCpu.Project") && line.Contains("CreatorCpu.CreateCpu") && line.Contains("projectCost") && line.Contains("createCpuButton"), line);
            Assert.True(line.Contains("Rush project"), "it says which ability is off: " + line);
        }

        [Test]
        public void TheFeatureDeclaringNoPatchAtAll_SwitchesOffTheThreeAbilities_ButWorksOtherwise()
        {
            FlawSelfCheckReport report = Run(input => input.Patches = new List<FlawPatchFact>());

            foreach (FlawCapability ability in AllAbilities) Assert.False(report.IsOn(ability), ability.ToString());
            Assert.Equal(3, report.Warnings, "one warning per ability");
            Assert.False(report.IsFatal, "the roll, the monitor, the windows and the journal need none of the patches");
        }

        [Test]
        public void APatchOnTheAddCpuOfTheCore_IsAnError()
        {
            foreach (string forbidden in new[] { "Company.AddCpu", "AICompany.AddCpu" })
            {
                FlawSelfCheckReport report = Run(input => input.Patches.Add(new FlawPatchFact(forbidden, false, true, "Rejected", "reserved")));
                Assert.True(report.HasErrors, forbidden);
                Assert.True(report.Findings.Any(f => f.Message.Contains(forbidden) && f.Message.Contains("owns")), forbidden);
            }
        }

        // ---- the fields of the game ----

        [Test]
        public void TheFieldsTheCodeLooksUp_AreTheFieldsOfTheTable()
        {
            // card: injected as ___cpu and ___popularityText
            string badge = FlawApiKit.Code("Patches/CpuBadgePatch.cs");
            foreach (FlawMemberSpec member in FlawSelfCheckRules.Members.Where(m => m.TypeName == "CpuSalesUI"))
            {
                Assert.True(badge.Contains("___" + member.Member), "CpuBadgePatch injects " + member.Member);
            }

            // hint: AccessTools.Field(typeof(CreatorCpu), "name")
            string hint = FlawApiKit.Code("Patches/DesignerRiskHint.cs");
            foreach (FlawMemberSpec member in FlawSelfCheckRules.Members.Where(m => m.Capability == FlawCapability.RiskHint))
            {
                Assert.True(hint.Contains("AccessTools.Field(typeof(CreatorCpu), \"" + member.Member + "\")"), "DesignerRiskHint looks up " + member.Member);
            }

            // rush: injected as ___projectTime, ___projectCost, ___nameInput; the button is looked up by name
            string patches = FlawApiKit.Code("Patches/RushProjectPatches.cs");
            string option = FlawApiKit.Code("RushProjectOption.cs");
            foreach (FlawMemberSpec member in FlawSelfCheckRules.Members.Where(m => m.Capability == FlawCapability.RushProject))
            {
                Assert.True(patches.Contains("___" + member.Member) || option.Contains("\"" + member.Member + "\""), "the rush switch uses " + member.Member);
            }
        }

        [Test]
        public void TheOnlyOptionalField_IsTheTermOfTheProjectForTheHint_AndItsMissWorksInAWeakerWay()
        {
            Assert.Equal(new[] { "projectTime" }, FlawSelfCheckRules.Members.Where(m => !m.Required).Select(m => m.Member).ToArray());

            FlawSelfCheckReport report = Run(input => input.MemberProblem = member => member.Member == "projectTime" && member.Capability == FlawCapability.RiskHint ? "the class has no such field" : null);

            Assert.True(report.IsOn(FlawCapability.RiskHint), "the hint goes on, with the year of today");
            Assert.Equal(1, report.Warnings);
            Assert.True(report.Findings.Single().Message.Contains("CreatorCpu.projectTime"), report.Findings.Single().Message);
            Assert.False(report.IsFatal);
        }

        [Test]
        public void AMissingRequiredField_SwitchesOffItsAbility_AndTheTermFieldIsRequiredForTheRushSwitch()
        {
            FlawSelfCheckReport hint = Run(input => input.MemberProblem = member => member.Member == "failureRateText" ? "the class has no such field" : null);
            Assert.False(hint.IsOn(FlawCapability.RiskHint));
            Assert.True(hint.IsOn(FlawCapability.RushProject) && hint.IsOn(FlawCapability.CardMark));

            FlawSelfCheckReport rush = Run(input => input.MemberProblem = member => member.Member == "projectTime" ? "the class has no such field" : null);
            Assert.False(rush.IsOn(FlawCapability.RushProject), "the switch needs the term to shorten");
            Assert.True(rush.IsOn(FlawCapability.RiskHint), "the hint does not");

            FlawSelfCheckReport card = Run(input => input.MemberProblem = member => member.Member == "popularityText" ? "the class has no such field" : null);
            Assert.False(card.IsOn(FlawCapability.CardMark));
            Assert.False(card.IsFatal);
        }

        [Test]
        public void AProbeThatThrows_IsAMissingField_NotACrash()
        {
            FlawSelfCheckReport report = Run(input => input.MemberProblem = member => { throw new InvalidOperationException("no reflection today"); });

            foreach (FlawCapability ability in AllAbilities) Assert.False(report.IsOn(ability), ability.ToString());
            Assert.False(report.IsFatal);
            Assert.True(report.Findings.Any(f => f.Message.Contains("no reflection today")), "the reason is kept");
        }

        // ---- the subscriptions ----

        [Test]
        public void MissingSubscriptions_AreErrors_ButNotFatal()
        {
            FlawSelfCheckReport release = Run(input => input.ReleaseSubscribed = false);
            Assert.True(release.Has(FlawCheckSeverity.Error, FlawSelfCheckRules.AreaSubscriptions));
            Assert.True(release.Findings.Single().Message.Contains("ModEvents.CpuReleased"));
            Assert.False(release.IsFatal, "the feature goes on: the flaws that exist are still handled");

            FlawSelfCheckReport transfer = Run(input => input.TransferSubscribed = false);
            Assert.True(transfer.Findings.Single().Message.Contains("ModEvents.CpuTransferred"));
            Assert.False(transfer.IsFatal);

            FlawSelfCheckReport untracked = Run(input => input.ReleaseTracked = false);
            Assert.True(untracked.Has(FlawCheckSeverity.Error, FlawSelfCheckRules.AreaSubscriptions), "the core never raises the release");
            Assert.False(untracked.IsFatal);
        }

        [Test]
        public void ASubscriptionThatCannotBeLookedInto_IsNotAProblem()
        {
            FlawSelfCheckReport report = Run(input =>
            {
                input.ReleaseSubscribed = null;
                input.TransferSubscribed = null;
                input.ReleaseTracked = null;
            });

            Assert.True(report.IsOk);
            Assert.Equal(2, report.Notes.Count);
        }

        // ---- the required parts: fatal ----

        [Test]
        public void ASixthOfTheDecisionEvents_IsAsRequiredAsTheFirst()
        {
            Assert.Equal(6, FlawDecisionPlanner.EventIds.Length);
            foreach (string missing in FlawDecisionPlanner.EventIds)
            {
                FlawSelfCheckReport report = Run(input => input.HasEvent = id => id != missing);

                Assert.True(report.IsFatal, missing);
                Assert.True(report.FatalReason.Contains(missing), report.FatalReason);
                Assert.Equal(1, report.Errors, missing);
                Assert.True(report.Summary.EndsWith("the feature is unavailable"), report.Summary);
            }
        }

        [Test]
        public void APackWithNoDecisionEvent_IsFatal_AndIsToldAsThePack()
        {
            FlawSelfCheckReport report = Run(input => input.HasEvent = id => false);

            Assert.True(report.IsFatal);
            Assert.True(report.FatalReason.Contains("none of the 6 decision events"), report.FatalReason);
            Assert.Equal(1, report.Errors, "one finding, not six");
        }

        [Test]
        public void EventDefinitionsNotLoadedYet_AreAWarning_UntilTheyAre()
        {
            FlawSelfCheckReport early = Run(input => input.EventsLoaded = false);
            Assert.False(early.IsFatal, "nothing can be said yet");
            Assert.False(early.EventsChecked, "to be checked on the first ready game");
            Assert.Equal(1, early.Warnings);

            FlawSelfCheckReport later = Run(input => input.EventsLoaded = true);
            Assert.True(later.EventsChecked);
            Assert.True(later.IsOk);
        }

        [Test]
        public void AMissingEffectType_IsFatal_EachOfTheFour()
        {
            Assert.Equal(new[] { "FlawRecall", "FlawStepping", "FlawSoftwarePatch", "FlawIgnore" }, FlawSelfCheckRules.RequiredEffectTypes.ToArray(), "the four effect types of the design");
            foreach (string type in FlawEffectTypes.All)
            {
                FlawSelfCheckReport report = Run(input => input.EffectTypes = input.EffectTypes.Where(t => t != type).ToList());

                Assert.True(report.IsFatal, type);
                Assert.True(report.FatalReason.Contains(type), report.FatalReason);
            }
        }

        [Test]
        public void TheNewsEffect_IsOnlyAWarning_AndAnUnreadableTableIsANote()
        {
            FlawSelfCheckReport news = Run(input => input.EffectTypes = input.EffectTypes.Where(t => t != FlawEffectTypes.News).ToList());
            Assert.False(news.IsFatal, "the line of the rivals' news is a nicety");
            Assert.Equal(1, news.Warnings);

            FlawSelfCheckReport unknown = Run(input => input.EffectTypes = null);
            Assert.True(unknown.IsOk, "no table, no verdict");
            Assert.True(unknown.Notes.Count > 0);
        }

        [Test]
        public void TheChannels_MustBeDeclared_AndTheRatingChannelPerCpu()
        {
            FlawSelfCheckReport noRating = Run(input => input.Channels = new List<FlawChannelFact> { new FlawChannelFact(FlawChannelNames.ContractSignChance, true, true) });
            Assert.True(noRating.IsFatal && noRating.FatalReason.Contains("market.rating.cpu"), noRating.FatalReason);

            FlawSelfCheckReport notPerCpu = Run(input => input.Channels = new List<FlawChannelFact>
            {
                new FlawChannelFact(FlawChannelNames.MarketRatingCpu, true, false),
                new FlawChannelFact(FlawChannelNames.ContractSignChance, true, false),
            });
            Assert.True(notPerCpu.IsFatal && notPerCpu.FatalReason.Contains("PerCpu = false"), notPerCpu.FatalReason);
            Assert.Equal(1, notPerCpu.Errors, "the sign chance channel needs no PerCpu: it is per company");

            FlawSelfCheckReport undeclared = Run(input => input.Channels = new List<FlawChannelFact>
            {
                new FlawChannelFact(FlawChannelNames.MarketRatingCpu, true, true),
                new FlawChannelFact(FlawChannelNames.ContractSignChance, false, false),
            });
            Assert.True(undeclared.IsFatal && undeclared.FatalReason.Contains("contract.signChance"), undeclared.FatalReason);

            FlawSelfCheckReport unknown = Run(input => input.Channels = null);
            Assert.True(unknown.IsOk, "a registry that cannot be asked gives no verdict");
        }

        [Test]
        public void SeveralRequiredPartsMissing_AreAllInTheReasonOfTheFeature()
        {
            FlawSelfCheckReport report = Run(input =>
            {
                input.HasEvent = id => false;
                input.EffectTypes = new List<string> { "Money" };
                input.Channels = new List<FlawChannelFact>();
            });

            Assert.True(report.IsFatal);
            Assert.True(report.FatalReason.Contains("decision events") && report.FatalReason.Contains("FlawRecall") && report.FatalReason.Contains("market.rating.cpu") && report.FatalReason.Contains("contract.signChance"), report.FatalReason);
        }

        // ---- identifiers and content ----

        [Test]
        public void TheIdsOfTheEventsAndTheKeys_AreCheckedByThePrefixRules()
        {
            FlawSelfCheckReport badEvent = Run(input => input.EventIdProblem = id => id == "dfr.decide.public" ? "the id has no prefix of the feature" : null);
            Assert.True(badEvent.Has(FlawCheckSeverity.Error, FlawSelfCheckRules.AreaIds));
            Assert.True(badEvent.Findings.Single().Message.Contains("dfr.decide.public"));
            Assert.False(badEvent.IsFatal, "the pack's own loading is what decides whether the event exists");

            FlawSelfCheckReport badKeys = Run(input => input.KeyProblem = key => "no string table has the key");
            Assert.Equal(1, badKeys.Errors, "one finding for all the keys");
            Assert.True(badKeys.Findings.Single().Message.Contains("2 string key(s)") && badKeys.Findings.Single().Message.Contains("dfr.feature.name"), badKeys.Findings.Single().Message);
            Assert.False(badKeys.IsFatal);
        }

        [Test]
        public void ARejectedValueOfTheContent_IsAWarning_BecauseTheBuiltInValueStands()
        {
            FlawSelfCheckReport report = Run(input =>
            {
                input.ContentErrors = 2;
                input.FirstContentError = "risk.pMax out of range [0,1]: 3; the default 0.5 is used";
            });

            Assert.Equal(1, report.Warnings);
            Assert.False(report.IsFatal);
            Assert.True(report.Findings.Single().Message.Contains("2 value(s)") && report.Findings.Single().Message.Contains("pMax"), report.Findings.Single().Message);
        }

        // ---- the report ----

        [Test]
        public void TheLines_AreErrorsFirst_WithTheAreaOfEach_AndTheSummaryCountsThem()
        {
            FlawSelfCheckReport report = Run(input =>
            {
                Drop(input, "CreatorCpu.Project");   // a warning
                input.ReleaseSubscribed = false;       // an error
            });

            IList<string> lines = report.Lines();
            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].StartsWith("self-check: [subscriptions] "), lines[0]);
            Assert.True(lines[1].StartsWith("self-check: [abilities] "), lines[1]);
            Assert.Equal("self-check: 1 error(s), 1 warning(s)", report.Summary);
            Assert.False(report.IsOk);
        }

        [Test]
        public void ThePartsOfTheCheck_AreIsolated_AThrowingPartIsAWarningAndTheNextOnesStillRun()
        {
            FlawSelfCheckReport report = Run(input =>
            {
                input.HasEvent = id => { throw new InvalidOperationException("the registry broke"); };
                input.EventIdProblem = id => { throw new InvalidOperationException("the table broke"); };
                input.Channels = new List<FlawChannelFact>();   // still checked after the failures above: fatal
            });

            Assert.True(report.Findings.Any(f => f.Area == FlawSelfCheckRules.AreaCheck && f.Message.Contains("events")), "the events part failed on its own");
            Assert.True(report.Findings.Any(f => f.Message.Contains("the table broke")), "a failing identifier check is told");
            Assert.True(report.FatalReason.Contains("market.rating.cpu"), "and the channels were still checked: " + report.FatalReason);
        }

        // ---- the game side, by the sources ----

        [Test]
        public void TheGameSide_AsksTheCoreForTheFacts_AndOnlyAFatalFindingMakesTheFeatureUnavailable()
        {
            string code = FlawApiKit.Code("FlawSelfCheck.cs");

            Assert.True(code.Contains("ModHooks.Report"), "the patches as the core reports them");
            Assert.True(code.Contains("EventRegistry.All") && code.Contains("EventEffects.RegisteredTypes"), "the events and the effect types of event-system");
            Assert.True(code.Contains("ModModifiers.SpecOf(") && code.Contains("ModModifiers.IsDeclared(") && code.Contains("spec.PerCpu"), "the channels");
            Assert.True(code.Contains("ModIds.IsEventIdAllowed(") && code.Contains("ModIds.IsL10nKeyAllowed(") && code.Contains("ModL10n.Has("), "the prefix and the existence of the ids");
            Assert.True(code.Contains("typeof(ModEvents).GetField(eventName") && code.Contains("typeof(FlawRoller)") && code.Contains("typeof(FlawTransferHandler)"), "the two subscriptions");
            Assert.True(code.Contains("ModEvents.IsReleaseTracked"), "the release event is raised by the core");
            Assert.True(code.Contains("AccessTools.Field(type, member.Member)"), "the fields by name");

            Assert.Equal(1, FlawApiKit.Occurrences(code, "ModFeatures.MarkUnavailable("), "the one verdict");
            Assert.True(code.Contains("if (report.IsFatal) MakeUnavailable(report);"), "and only for a fatal finding");
            string unavailable = FlawApiKit.Between(code, "private static void MakeUnavailable(", "private static void Write(");
            Assert.True(unavailable.Contains("ModLog.Error(FeatureId") && unavailable.Contains("report.FatalReason"), "with a line in the log (Error) that says why");
        }

        [Test]
        public void TheGameSide_OnlyLooks_NeverChangesTheGameOrTheCore()
        {
            string code = FlawApiKit.Code("FlawSelfCheck.cs");

            foreach (string forbidden in new[] { "ModModifiers.Add(", "ModModifiers.Remove", ".Invoke(", "SetValue(", "SpendMoney", "ModHooks.RemoveFeature", "ModHooks.ApplyFeature", "Harmony(", "EventBus.Fire" })
            {
                Assert.False(code.Contains(forbidden), "the self-check does not call " + forbidden);
            }
            Assert.False(code.Contains("ModSettings."), "and does not touch the settings");
        }

        [Test]
        public void TheFeature_DeclaresNoChannel_AndNamesOnlyTheTwoChannelsOfTheCoreThatItWritesTo()
        {
            // NFR-8: the channels of the registry of the core only - market.rating.cpu (the patch) and contract.signChance (waiting for the answer, ignoring).
            var allowed = new HashSet<string>(StringComparer.Ordinal) { FlawChannelNames.MarketRatingCpu, FlawChannelNames.ContractSignChance };
            var core = CoreContentTests.LoadCore().GetArray("channels").Cast<PTMod.Core.Logic.Json.JsonNode>().Select(n => ((PTMod.Core.Logic.Json.JsonObject)n).GetString("name")).ToList();
            Assert.True(core.Contains(FlawChannelNames.MarketRatingCpu) && core.Contains(FlawChannelNames.ContractSignChance), "both are channels of the core");

            foreach (KeyValuePair<string, string> file in FlawApiKit.AllCode())
            {
                Assert.False(file.Value.Contains("DeclareChannel") || file.Value.Contains("new ChannelSpec"), file.Key + " declares a channel");
                foreach (string channel in core.Where(c => !allowed.Contains(c)))
                {
                    Assert.False(file.Value.Contains("\"" + channel + "\""), file.Key + " names the channel " + channel + " (popularity is lowered once, not through a channel)");
                }
            }
        }

        [Test]
        public void TheGameSide_NeverThrows_ItsFactsAreCollectedOneByOne_AndTheFailureOfTheWholeCheckIsAWarning()
        {
            string code = FlawApiKit.Code("FlawSelfCheck.cs");

            string run = FlawApiKit.Between(code, "internal static FlawSelfCheckReport Run(", "internal static void OnGameReady(");
            Assert.True(run.Contains("try") && run.Contains("catch (Exception ex)") && run.Contains("the self-check itself failed"), "the whole check");
            Assert.True(run.Contains("Write(report, stage);") && FlawApiKit.Occurrences(run, "catch (Exception") >= 2, "even the writing of the log is under its own try");

            Assert.True(code.Contains("private static T Collect<T>(") && code.Contains("private static bool? CollectFlag("), "every fact is collected under its own try");
            Assert.True(FlawApiKit.Occurrences(code, "Collect(\"") >= 6, "events, effect types, channels, keys, content, patches");
        }

        [Test]
        public void TheFeature_RunsTheCheckAtTheEndOfOnEnabled_AndOnTheFirstReadyGameWhenTheEventsWereNotThere()
        {
            string feature = FlawApiKit.Code("DesignFlawRecallsFeature.cs");
            string onEnabled = FlawApiKit.Between(feature, "public void OnEnabled()", "public void OnDisabled()");
            Assert.True(onEnabled.Contains("ModHooks.Guard(FeatureId, \"feature/selfCheck\", () => FlawSelfCheck.Run(this, \"start\"));"), "guarded, after the work of OnEnabled");
            Assert.True(onEnabled.IndexOf("FlawSelfCheck.Run", StringComparison.Ordinal) > onEnabled.IndexOf("AttachBackend", StringComparison.Ordinal), "the backend is attached first");

            string ready = FlawApiKit.Between(feature, "private void OnGameReady(", "private void RememberLastActiveDay()");
            Assert.True(ready.Contains("ModHooks.Guard(FeatureId, \"ticks/selfCheck\", () => FlawSelfCheck.OnGameReady(this));"), "the second look at the events");

            string selfCheck = FlawApiKit.Code("FlawSelfCheck.cs");
            string onReady = FlawApiKit.Between(selfCheck, "internal static void OnGameReady(", "private static void MakeUnavailable(");
            Assert.True(onReady.Contains("last.EventsChecked") && onReady.Contains("Run(feature, \"ready\")"), "only when the first look could not see the events");
        }

        // ---- the isolation of errors, by the sources ----

        [Test]
        public void EveryPatchBody_IsGuarded_SoThatAFailureNeverReachesTheGame()
        {
            int patches = 0;
            foreach (KeyValuePair<string, string> file in FlawApiKit.AllCode())
            {
                foreach (Match m in Regex.Matches(file.Value, @"\[ModPatch\(.*?\)\]\s*(?:private|internal|public)\s+static\s+\w+\s+(\w+)\s*\("))
                {
                    patches++;
                    string body = BodyOf(file.Value, m.Groups[1].Value + "(", m.Index);
                    Assert.True(body.Contains("ModHooks.Guard(") || body.Contains("ModHooks.ReportFault("), file.Key + ": " + m.Groups[1].Value + " runs unguarded");
                    Assert.False(body.Contains("throw "), file.Key + ": " + m.Groups[1].Value + " must not throw into the game");
                }
            }
            Assert.Equal(FlawSelfCheckRules.PatchTargets.Count + 1, patches, "the six targets, with the two patches of CreateCpu");
        }

        [Test]
        public void EveryHandlerTheFeatureSubscribes_IsGuarded()
        {
            var handlers = new[]
            {
                new[] { "FlawRoller.cs", "internal void OnCpuReleased(" },
                new[] { "FlawTransferHandler.cs", "internal void OnCpuTransferred(" },
                new[] { "FlawDecisionDispatcher.cs", "internal void OnChoiceResolved(" },
                new[] { "FlawMonitor.cs", "internal void OnMonth(" },
                new[] { "FlawMonitor.cs", "internal void OnDay(" },
                new[] { "FlawMonitor.cs", "private void OnCompanyWentBankrupt(" },
                new[] { "DesignFlawRecallsFeature.cs", "private void OnSaveRead(" },
                new[] { "DesignFlawRecallsFeature.cs", "private void OnGameReady(" },
                new[] { "Press/FlawPressBridge.cs", "internal void OnFeatureStateChanged(" },
            };

            foreach (string[] handler in handlers)
            {
                string code = FlawApiKit.Code(handler[0]);
                string body = BodyOf(code, handler[1], 0);
                Assert.True(body.Contains("ModHooks.Guard(") || body.Contains("Refresh();"), handler[0] + ": " + handler[1] + " is not guarded");
            }
        }

        [Test]
        public void EveryEffectHandler_CatchesItsOwnFailure_SoTheRecordStaysWaitingAndTheEngineMarksTheEffectFailed()
        {
            string code = FlawApiKit.Code("Effects/FlawEffectBase.cs");

            Assert.True(FlawApiKit.Occurrences(code, "catch (Exception e)") >= 3, "Resolve, Apply and Describe each have their own catch");
            string apply = BodyOf(code, "public void Apply(", 0);
            Assert.True(apply.Contains("throw;") || apply.Contains("throw "), "Apply lets the engine know (Failed) after it logged the failure");
            Assert.True(apply.Contains("ModLog.Error(") || apply.Contains("ReportFault("), "and logs it");
        }

        [Test]
        public void TheTicksOfTheMonitor_AreGuardedInTheFeatureNotJustInTheCore()
        {
            string code = FlawApiKit.Code("FlawMonitor.cs");

            string month = BodyOf(code, "internal void OnMonth(", 0);
            Assert.True(month.Contains("ModHooks.Guard(DesignFlawRecallsFeature.FeatureId, \"monitor/month\", _cycle.OnMonth);"), "the cycle");
            Assert.True(month.Contains("_feature.Dispatcher.Sweep();"), "and the sweep runs even if the cycle failed");
            Assert.True(BodyOf(code, "internal void OnDay(", 0).Contains("\"monitor/day\""), "the day");
        }

        [Test]
        public void TheCycle_SkipsAFailingRecord_AndClosesItAfterThreeInARow()
        {
            // design.md, Error Handling: an exception for one record is skipped, the others go on, three in a row close the record.
            FlawRecord broken = FlawCycleKit.Record(FlawState.Hidden, id: 1, baseCpu: 100);
            FlawRecord fine = FlawCycleKit.Record(FlawState.Hidden, id: 2, baseCpu: 200);
            var host = new FakeCycleHost { Day = FlawCycleKit.MonthStart(1) };
            host.Facts[200] = FakeCycleHost.Selling(1000000);
            host.ThrowOnRead.Add(1);
            host.Rng_ = FakeRng.Always(0.999f);
            var cycle = new FlawCycle(FlawCycleKit.Store(broken, fine), host);

            for (int month = 1; month <= FlawCycle.MaxFailStreak; month++)
            {
                host.Day = FlawCycleKit.MonthStart(month);
                cycle.OnMonth();
                Assert.Equal(FlawState.Hidden, fine.State, "the other record goes on, month " + month);
            }

            Assert.Equal(FlawState.Closed, broken.State, "three failures in a row close the record");
            Assert.Equal(FlawState.Hidden, fine.State);
            Assert.Equal(FlawCycle.MaxFailStreak, host.Errors.Count, "and every failure is in the log");
        }

        // ---- manual scenario 10, on the pure engines ----

        [Test]
        public void ManualScenario10_TheFeatureOffInTheMiddleOfAGame_APatchAndARevisionRunning()
        {
            // A revision and a patch are running; the feature is switched off for ten days and on again. While it is off nothing is paid and nothing in the core is
            // touched; when it is on again the revision goes on with its term moved by the pause, and the patch keeps its calendar term (FR-7 item 7, NFR-2, D12, D13).
            FlawRecord revision = ResponseWorld.Known(FlawState.Public, id: 1);
            FlawRecord patched = ResponseWorld.Known(FlawState.Public, id: 2, typeId: "security", year: 2018);   // a type and a year with a patch
            patched.Data.baseCpuId = 200;
            patched.Data.cpuIds = new List<int> { 200, 201 };
            var world = new ResponseWorld(revision, patched);
            var host = new WiredCycleHost(world);
            var cycle = new FlawCycle(world.Store, host);
            int start = world.Host.Day;

            world.Engine.Apply(revision, FlawResponse.Stepping, false);
            world.Engine.Apply(patched, FlawResponse.Patch, false);
            Assert.Equal(FlawState.Stepping, revision.State);
            Assert.Equal(FlawState.Mitigated, patched.State);
            int steppingEnd = revision.Data.steppingEndDay;
            int patchEnd = patched.Data.mitigationEndDay;
            string[] entryIds = patched.Data.modifierEntryIds.ToArray();
            int entryEnd = world.Gateway.Entries[entryIds[0]].EndDay;
            int added = world.Gateway.AddCount;
            Assert.True(entryIds.Length > 0 && steppingEnd == start + 90, "a patch entry and a 90-day revision");

            // ten days of play: the revision is paid every day
            for (int day = start + 1; day <= start + 10; day++)
            {
                world.SetDay(day);
                cycle.OnDay();
            }
            float paidBeforeOff = world.Host.Total(FlawSpendKind.Development);
            Assert.True(paidBeforeOff > 0f, "the revision was paid while the feature was on");

            // OnDisabled: the day is remembered; then ten days with no tick of the feature at all
            world.Store.LastActiveDay = start + 10;
            world.SetDay(start + 20);

            Assert.Equal(paidBeforeOff, world.Host.Total(FlawSpendKind.Development), "nothing is paid while the feature is off");
            Assert.Equal(0, world.Gateway.RemoveCount, "no entry of the core is removed (D13)");

            // OnEnabled: the pause is counted; nothing is made again
            cycle.OnResume();

            Assert.Equal(steppingEnd + 9, revision.Data.steppingEndDay, "the revision is moved by the pause (today - lastActiveDay - 1)");
            Assert.Equal(patchEnd, patched.Data.mitigationEndDay, "the term of the patch is the calendar's: it did not move");
            Assert.Equal(entryEnd, world.Gateway.Entries[entryIds[0]].EndDay, "and its entry in the core has the old end date");
            Assert.Equal(added, world.Gateway.AddCount, "no entry was made again (in the diagnostics: the same end date)");
            Assert.Equal(0, world.Gateway.RemoveCount);
            Assert.Equal(paidBeforeOff, world.Host.Total(FlawSpendKind.Development), "and the resume itself pays nothing");

            // the revision goes on and is paid in full, 90 days' worth, not a day more
            for (int day = start + 21; day <= steppingEnd + 9 + 1; day++)
            {
                world.SetDay(day);
                cycle.OnDay();
            }
            Assert.Equal(FlawState.Resolved, revision.State, "the revision ends, nine days later than it would have");
            float quoted = revision.Quote.SteppingCost;
            float paidForTheRevision = world.Host.Spent.Where(x => x.RecordId == revision.Id && x.Kind == FlawSpendKind.Development).Sum(x => x.Amount);
            Assert.Near(quoted, paidForTheRevision, quoted * 0.001f, "exactly the cost of the revision was paid: the paused days are free");
            Assert.Equal(FlawState.Mitigated, patched.State, "the patch is still in force");
        }

        // ---- helpers ----

        /// <summary>The body of the first method whose signature contains <paramref name="signature"/>, at or after <paramref name="from"/> (brace to brace).</summary>
        private static string BodyOf(string code, string signature, int from)
        {
            int at = code.IndexOf(signature, from, StringComparison.Ordinal);
            Assert.True(at >= 0, "not found: " + signature);
            int open = code.IndexOf('{', at);
            Assert.True(open > 0, "no body: " + signature);

            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) return code.Substring(at, i - at + 1);
            }
            return code.Substring(at);
        }
    }
}
