using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Core.Logic.Save;
using PTMod.Features.RivalPersonalities;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// Switching the feature off in the middle of a game and on again (FR-10 item 4, NFR-3, TSK-023). The core hands a feature that is switched on again the copy
    /// of its section from the last save (or none), never the state of the moment it went off; so the section freezes the state in <c>OnDisabled</c> and takes
    /// it back for the first hand-over after the switch-on, in the same game only. These tests drive that with the core's own <c>SideFileModel</c> (a participant that
    /// can be made inactive, restored, read and reset), then check the pieces <c>OnDisabled</c> calls on plain objects (the price book, the multipliers of the
    /// behaviour), and read the sources for what cannot run without the game: the order of the steps, that they run in any state of the feature, and that no entry
    /// of <c>ModModifiers</c> is removed or made again.
    /// </summary>
    public class RivalDisableFlowTests
    {
        private const string Id = "rival-personalities";

        // ---- one run of the game around the section ----

        private sealed class SaveFile
        {
            public string Text;
        }

        private sealed class Game
        {
            public bool Active = true;
            public readonly SideFileModel Model;
            public readonly RivalSaveSection Section;

            public Game(bool freezeConnected = true)
            {
                Section = new RivalSaveSection(Id);
                Model = new SideFileModel(id => id != Id || Active);
                string problem;
                Assert.True(Model.TryRegister(Section, out problem), problem);
                if (freezeConnected) Section.UseFreeze(() => Active, () => Model.Header);
            }

            public RivalState State
            {
                get { return Section.State; }
            }

            /// <summary>OnDisabled: the feature is no longer active when the core calls it; the feature freezes its state.</summary>
            public void Disable()
            {
                Active = false;
                Section.Freeze();
            }

            /// <summary>The core: the state of the feature becomes Enabled, ModSave hands the section to the participant.</summary>
            public void Enable()
            {
                Active = true;
                Model.RestoreParticipant(Id);
            }

            public SaveFile Save()
            {
                SideFileBuildReport report = Model.Build(new SideFileHeader { modVersion = "0.1.0", gameDate = "1985-03-01" });
                Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
                return new SaveFile { Text = ModJson.Write(report.Document, true) };
            }

            public void Load(SaveFile file)
            {
                SideFileReadReport report = Model.Apply(file.Text);
                Assert.Equal(0, report.Warnings.Count, string.Join("; ", report.Warnings));
            }

            public void NewGame()
            {
                Model.StartNewGame();
            }

            public void Populate()
            {
                Section.Read(RivalSaveCodec.Write(PlayedState()), 1);
            }
        }

        private static RivalState PlayedState()
        {
            var state = new RivalState { graceStart = "1980-01-01", nextReactionSeq = 19 };
            state.assignments.Add(new AssignmentEntry { uid = 1, archetype = "price_warrior" });
            state.attitudes.Add(new AttitudeRecord
            {
                uid = 1,
                value = -25.4f,
                history = new List<AttitudeEntry> { new AttitudeEntry { date = "1984-03-12", delta = -10.4f, reason = "StrongLaunch:1" } },
            });
            state.pending.Add(new PendingReaction { id = "r-17", uid = 1, type = "PriceCut", segment = 1, trigger = "StrongLaunch", plannedOn = "1984-03-12", mailId = 100231, telegraphDays = 33, fireOn = "1984-04-20" });
            state.active.Add(new ActiveReaction { id = "r-12", uid = 1, type = "CounterLaunch", segment = 0, start = "1983-11-02", end = "1984-04-01" });
            state.priceBook.Add(new PriceBookEntry { cpuSaveId = 4711, uid = 1, reactionId = "r-12", baseTargetPrice = 239f, cutTarget = 203.15f, startedOn = "1984-04-20" });
            state.counters.executed.Add(new ExecutedCount { uid = 1, count = 2 });
            return state;
        }

        /// <summary>What the game does to the state after the load: the attitude moves, a line is written, a reaction ends.</summary>
        private static void Play(RivalState state)
        {
            state.attitudes[0].value = 61f;
            state.attitudes[0].history.Add(new AttitudeEntry { date = "1984-05-01", delta = 86.4f, reason = "PartnershipBonus" });
            state.pending.Clear();
            state.counters.executed[0].count = 3;
            state.nextReactionSeq = 30;
        }

        // ---- the same game ----

        [Test]
        public void SwitchedOffAndOn_InOneGame_TheStateContinuesWhereItWas()
        {
            var game = new Game();
            game.Populate();
            Play(game.State);
            RivalState before = game.State;

            game.Disable();
            game.Enable();

            RivalState state = game.State;
            Assert.True(ReferenceEquals(before, state), "the very same object: what OnEnabled applied the characters to is the section in play");
            Assert.Equal(61f, state.attitudes[0].value, "the attitude it left with");
            Assert.Equal(2, state.attitudes[0].history.Count);
            Assert.Equal(0, state.pending.Count, "the reaction that was carried out does not come back");
            Assert.Equal(1, state.active.Count);
            Assert.Equal(1, state.priceBook.Count, "the way back to the price is still there");
            Assert.Equal(3, state.counters.executed[0].count);
            Assert.Equal(30, state.nextReactionSeq);
            Assert.Equal("1980-01-01", state.graceStart);
        }

        [Test]
        public void TheFrozenState_IsNewerThanTheCopyTheCoreKeepsFromTheLastSave()
        {
            var game = new Game();
            game.Populate();
            game.Save();   // the copy the core keeps: the attitude is -25.4, the reaction r-17 is planned
            Play(game.State);

            game.Disable();
            game.Enable();

            Assert.Equal(61f, game.State.attitudes[0].value);
            Assert.Equal(0, game.State.pending.Count, "not the planned r-17 of the old copy");
        }

        [Test]
        public void WithoutTheFreeze_TheCoreHandsBackTheOldCopy_OrNone_WhichIsWhyTheSectionFreezes()
        {
            // the behaviour of the core (SideFileModel.RestoreParticipant), written down: mod mod-framework could change it (mod/requests/rival-personalities.md 13.1)
            var withCopy = new Game(freezeConnected: false);
            withCopy.Populate();
            withCopy.Save();
            Play(withCopy.State);
            withCopy.Disable();
            withCopy.Enable();
            Assert.Equal(-25.4f, withCopy.State.attitudes[0].value, "the copy of the last save, not the state it left with");

            var noCopy = new Game(freezeConnected: false);
            noCopy.Populate();
            Play(noCopy.State);
            noCopy.Disable();
            noCopy.Enable();
            Assert.Equal(0, noCopy.State.attitudes.Count, "no save since the load: nothing at all");
        }

        [Test]
        public void TheFrozenState_IsUsedOnce()
        {
            var game = new Game();
            game.Populate();
            Play(game.State);
            game.Disable();
            Assert.True(game.Section.IsFrozen);

            game.Enable();
            Assert.False(game.Section.IsFrozen, "taken");

            SaveFile fresh = SaveOf(new RivalState { graceStart = "1999-09-09" });
            game.Load(fresh);
            Assert.Equal("1999-09-09", game.State.graceStart, "a later load reads the file as always");
        }

        [Test]
        public void SwitchedOffTwice_TheLatestStateIsTheOneThatComesBack()
        {
            var game = new Game();
            game.Populate();
            game.Disable();
            game.Enable();
            game.State.attitudes[0].value = 10f;

            game.Disable();
            game.Enable();

            Assert.Equal(10f, game.State.attitudes[0].value);
        }

        // ---- another game ----

        [Test]
        public void ANewGame_WhileOff_DropsTheFrozenState()
        {
            var game = new Game();
            game.Populate();
            game.Disable();

            game.NewGame();   // every participant is reset, active or not
            Assert.False(game.Section.IsFrozen, "a hand-over to an inactive participant is a reset of the whole mod");
            game.Enable();

            Assert.Equal(string.Empty, game.State.graceStart);
            Assert.Equal(0, game.State.attitudes.Count);
            Assert.Equal(1, game.State.nextReactionSeq);
        }

        [Test]
        public void ALoadOfAnotherSave_WhileOff_GivesTheFeatureThatSavesSection_NotTheFrozenState()
        {
            var game = new Game();
            game.Populate();
            RivalState before = game.State;
            game.Disable();

            game.Load(SaveOf(new RivalState { graceStart = "1999-09-09", nextReactionSeq = 4 }));   // the participant is off: the core keeps the section back
            game.Enable();

            Assert.False(ReferenceEquals(before, game.State), "another section: the feature applies its characters again from it (OnOwnSwitchOn)");
            Assert.Equal("1999-09-09", game.State.graceStart);
            Assert.Equal(0, game.State.attitudes.Count, "nothing of the game that is gone");
            Assert.Equal(4, game.State.nextReactionSeq);
        }

        [Test]
        public void TheSameSaveLoadedAgain_WhileOff_IsATimelineOfItsOwn()
        {
            var game = new Game();
            game.Populate();
            SaveFile saved = game.Save();
            Play(game.State);

            game.Disable();
            game.Load(saved);   // a new side file is read: even the same save is another timeline than the frozen state
            game.Enable();

            Assert.Equal(-25.4f, game.State.attitudes[0].value, "the file, not the state the feature left with");
            Assert.Equal(1, game.State.pending.Count);
        }

        [Test]
        public void ALoad_WhileTheFeatureIsOn_NeverUsesAFrozenState()
        {
            var game = new Game();
            game.Populate();
            Play(game.State);
            game.Disable();
            game.Active = true;   // on again, but the core has not handed anything over (the menu, no side file)

            game.Load(SaveOf(new RivalState { graceStart = "2001-01-01" }));

            Assert.Equal("2001-01-01", game.State.graceStart, "the game that was loaded is not the game that was frozen");
        }

        [Test]
        public void ASectionThatWasNeverConnected_UsesTheCoresHandOver()
        {
            var section = new RivalSaveSection(Id);   // UseFreeze never called
            section.Read(RivalSaveCodec.Write(PlayedState()), 1);
            section.Freeze();

            section.Read(RivalSaveCodec.Write(new RivalState { graceStart = "1999-09-09" }), 1);

            Assert.Equal("1999-09-09", section.State.graceStart);
        }

        [Test]
        public void TheFile_KeepsTheSectionOfTheLastSave_WhileTheFeatureIsOff()
        {
            // what the core does with the section of a switched-off feature: the copy of the last save stays in the file (FR-10 item 4: "saved unchanged")
            var game = new Game();
            game.Populate();
            SaveFile before = game.Save();
            Play(game.State);

            game.Disable();
            SaveFile whileOff = game.Save();

            Assert.Equal(SectionText(before), SectionText(whileOff), "unchanged by the switch-off");
            Assert.True(SectionText(whileOff).Contains("price_warrior"));
        }

        private static SaveFile SaveOf(RivalState state)
        {
            var other = new Game();
            other.Section.Read(RivalSaveCodec.Write(state), 1);
            return other.Save();
        }

        private static string SectionText(SaveFile file)
        {
            JsonObject features = RivalTestKit.ParseObject(file.Text).GetObject("features");
            Assert.NotNull(features, "features");
            JsonObject section = features.GetObject(Id);
            Assert.NotNull(section, "the section is in the file");
            return ModJson.Write(section, false);
        }

        // ---- what OnDisabled calls: the prices ----

        private sealed class FakeCpu
        {
            public int SaveId;
            public float Price;
            public float TargetPrice;
            public float RealUnitCost;
            public int Market;
        }

        private sealed class FakeWorld : IPriceCpuAccess<FakeCpu>
        {
            public readonly List<FakeCpu> All = new List<FakeCpu>();

            public int SaveIdOf(FakeCpu cpu) { return cpu.SaveId; }
            public float PriceOf(FakeCpu cpu) { return cpu.Price; }
            public void SetPrice(FakeCpu cpu, float price) { cpu.Price = price; }
            public float TargetPriceOf(FakeCpu cpu) { return cpu.TargetPrice; }
            public void SetTargetPrice(FakeCpu cpu, float targetPrice) { cpu.TargetPrice = targetPrice; }
            public float RealUnitCostOf(FakeCpu cpu) { return cpu.RealUnitCost; }
            public int TargetMarketOf(FakeCpu cpu) { return cpu.Market; }
            public bool IsSoldOnMarket(FakeCpu cpu) { return true; }
            public FakeCpu FindCpu(int saveId) { return All.FirstOrDefault(c => c.SaveId == saveId); }
        }

        [Test]
        public void OnDisabled_GivesTheTargetPricesBack_AndTheCutGoesOnWhenSwitchedOnAgain()
        {
            var state = new RivalState();
            var world = new FakeWorld();
            var cpu = new FakeCpu { SaveId = 10, Price = 300f, TargetPrice = 300f, RealUnitCost = 100f, Market = (int)Segment.MidRange };
            world.All.Add(cpu);
            var book = new PriceBookCore<FakeCpu>(() => state, world);
            book.ApplyCut("r-1", 1, world.All, Segment.MidRange, 0.15f, 0.08f, false, new DateTime(1990, 5, 1));
            Assert.True(cpu.TargetPrice < 300f, "cut");

            // OnDisabled: ReleaseAll
            int released = book.ReleaseAll();

            Assert.Equal(1, released);
            Assert.Equal(300f, cpu.TargetPrice, "the original target price is the game's again");
            Assert.Equal(1, state.priceBook.Count, "the entry stays: the cut goes on when the feature is switched on again");

            // the section goes through a save while the feature is off
            RivalState loaded = RivalSaveCodec.Read(RivalSaveCodec.Write(state), 1, null);
            var again = new PriceBookCore<FakeCpu>(() => loaded, world);
            Assert.Equal(1, again.Clamp(1, world.All), "switched on again: the hold of the target price goes on");
            Assert.True(cpu.TargetPrice < 300f);

            // and the way back at the end of the reaction still ends at the price before any cut
            again.Restore("r-1");
            Assert.Equal(300f, cpu.TargetPrice);
            Assert.Equal(0, loaded.priceBook.Count);
        }

        [Test]
        public void ReleaseAll_OnAnEmptyBook_DoesNothing()
        {
            var state = new RivalState();
            var book = new PriceBookCore<FakeCpu>(() => state, new FakeWorld());
            Assert.Equal(0, book.ReleaseAll());
        }

        // ---- what OnDisabled calls: the multipliers ----

        [Test]
        public void OnDisabled_StopsTheMultipliers_AndTheyComeBackWithTheCharacterAndTheBoost()
        {
            var model = new BehaviourModel();
            model.SetApplied(1, 1.2f, 1.1f, 0.95f);
            model.AddBoost(new ReactionBoost("r-5", 1, Segment.MidRange, 1.35f, 1.25f));
            Assert.True(model.ApprovalMultiplier(1, Segment.MidRange, false) > 1.5f);
            Assert.True(model.ResearchFundingMultiplier(1, false) > 1.3f);

            // OnDisabled: the characters are taken off (RevertAll), the boosts are cleared
            model.ClearAllApplied();
            model.ClearBoosts();

            Assert.Equal(1f, model.ApprovalMultiplier(1, Segment.MidRange, false));
            Assert.Equal(1f, model.ResearchFundingMultiplier(1, false));
            Assert.Equal(1f, model.LaunchPriceMultiplier(1, false));
            Assert.Equal(0, model.BoostCount);

            // switched on again: the character is applied to every company, the event system restores the boost of the reaction that is still in effect
            model.SetApplied(1, 1.2f, 1.1f, 0.95f);
            model.AddBoost(new ReactionBoost("r-5", 1, Segment.MidRange, 1.35f, 1.25f));
            Assert.Near(1.2 * 1.35, model.ApprovalMultiplier(1, Segment.MidRange, false), 1e-5);
            Assert.True(model.HasBoost("r-5"));
        }

        [Test]
        public void ThePriorities_OfTheDesigners_GoBackToTheirSnapshot()
        {
            // OnDisabled -> PersonalityRegistry.RevertAll -> CreatorRecord.PlanRevert: the designer gets what it had before the character
            var record = new CreatorRecord();
            var before = new CreatorPriorities(PriorityMath.LowUnitCost, PriorityMath.PowerLowConsumption, 1);
            CreatorPriorities target;
            Assert.True(record.Plan(before, DesignFocusOverride.HighPerformance, PowerFocusOverride.Irrelevant, out target));
            record.MarkApplied(target);
            Assert.NotEqual(before, target);

            CreatorPriorities back;
            Assert.True(record.PlanRevert(target, out back), "the designer still has what the character set");
            Assert.Equal(before, back);
            record.MarkReverted();

            Assert.True(record.Plan(before, DesignFocusOverride.HighPerformance, PowerFocusOverride.Irrelevant, out target), "applied again when the feature returns");
        }

        // ---- the sources: the order, the states, the modifiers ----

        private static string FeatureCode(params string[] parts)
        {
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            string path = Path.Combine(new[] { folder }.Concat(parts).ToArray());
            Assert.True(File.Exists(path), "missing " + path);
            return SourceScan.Strip(SourceScan.ReadText(path), false);
        }

        private static string Body(string code, string signature)
        {
            int start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, "no " + signature);
            int open = code.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
            }
            Assert.Fail("no end of " + signature);
            return null;
        }

        [Test]
        public void OnDisabled_RunsEveryStep_InAnyStateOfTheFeature()
        {
            string feature = FeatureCode("RivalPersonalitiesFeature.cs");
            string body = Body(feature, "public void OnDisabled()");

            // ModHooks.Guard does not run the body of a Faulted or Unavailable feature; the prices and the designers must go back all the same
            Assert.False(body.Contains("ModHooks.Guard"), "no ModHooks.Guard in OnDisabled");
            foreach (string step in new[]
            {
                "Always(\"feature/disabled.freeze\", _save.Freeze)",
                "Always(\"feature/disabled.links\", DetachLinks)",
                "Always(\"feature/disabled.triggers\", _monitor.Detach)",
                "Always(\"feature/disabled.window\", _intel.Close)",
                "Always(\"feature/disabled.characters\", _registry.OnDisabled)",
                "Always(\"feature/disabled.boosts\", _modifiers.Model.ClearBoosts)",
                "Always(\"feature/disabled.prices\"",
            })
            {
                Assert.True(body.Contains(step), "a step: " + step);
            }
            Assert.True(body.Contains("_prices.ReleaseAll()"), "the target prices go back");
            Assert.True(Regex.IsMatch(body, @"OnDisabled: restored \"" \+ released\.ToString\(CultureInfo\.InvariantCulture\) \+ \"" prices"), "the line of the log of scenario 7");

            string always = Body(feature, "private static void Always(string site, Action step)");
            Assert.True(always.Contains("try") && always.Contains("catch (Exception e)") && always.Contains("ModHooks.ReportFault(FeatureId, site, e)"), "a failing step is counted and the next one runs");

            int freeze = body.IndexOf("feature/disabled.freeze", StringComparison.Ordinal);
            int characters = body.IndexOf("feature/disabled.characters", StringComparison.Ordinal);
            Assert.True(freeze >= 0 && freeze < characters, "the state is kept before anything else is undone");
        }

        [Test]
        public void TheFeature_NeverRemovesOrMakesAgainAnEntryOfModModifiers()
        {
            // the core keeps the entry of a switched-off feature without applying it, and its term runs on the calendar (NFR-3, design.md "Error Handling")
            string folder = Path.Combine(SourceScan.SrcDirOrFail(), "Features", "RivalPersonalities");
            var offenders = new List<string>();
            foreach (string path in SourceScan.Files(folder, ".cs"))
            {
                string code = SourceScan.Strip(SourceScan.ReadText(path), false);
                foreach (Match match in Regex.Matches(code, @"ModModifiers\.(Remove\w*|Clear\w*|Add\w*)\("))
                {
                    offenders.Add(SourceScan.Relative(folder, path) + ": " + match.Value);
                }
            }

            // the one Add is the bonus of the poached team (RivalResearchBoostHandler), made when the reaction starts and never undone by the feature
            Assert.Equal(1, offenders.Count, string.Join(", ", offenders));
            Assert.True(offenders[0].Contains("RivalResearchBoostHandler") && offenders[0].EndsWith("ModModifiers.Add("), offenders[0]);

            string feature = FeatureCode("RivalPersonalitiesFeature.cs");
            Assert.False(feature.Contains("ModModifiers."), "neither OnEnabled nor OnDisabled touches the entries");
        }

        [Test]
        public void TheSection_IsFrozenOnDisable_AndTheFreezeKnowsWhoIsActiveAndWhichGameIsInPlay()
        {
            string adapter = FeatureCode("State", "RivalSaveAdapter.cs");
            string feature = FeatureCode("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(adapter, @"UseFreeze\(\(\) => ModFeatures\.IsActive\(featureId\), \(\) => ModSave\.FileHeader\)"), "active now, and a new object for every side file read");
            Assert.True(Regex.IsMatch(adapter, @"public void Freeze\(\)\s*\{\s*_section\.Freeze\(\);"));
            Assert.True(Regex.IsMatch(feature, @"_save\.DropFrozen\(\)"), "a load drops what was kept for a game that is gone");
            Assert.True(Regex.IsMatch(Body(feature, "private void OnAfterRead()"), @"_save\.DropFrozen\(\);\s*ModHooks\.Guard"), "before the characters are applied");
        }

        [Test]
        public void IfTheCoreHandsOverAnotherSection_AfterOnEnabled_TheCharactersAreAppliedAgainFromIt()
        {
            string feature = FeatureCode("RivalPersonalitiesFeature.cs");

            Assert.True(Regex.IsMatch(Body(feature, "public void OnEnabled()"), @"_stateWhenEnabled = _save\.State;"), "the section OnEnabled worked on");
            string changed = Body(feature, "private void OnFeatureStateChanged(string featureId, FeatureState state)");
            Assert.True(Regex.IsMatch(changed, @"featureId, FeatureId, StringComparison\.Ordinal\)[\s\S]*?FeatureState\.Enabled\) ModHooks\.Guard\(FeatureId, ""ModFeatures\.StateChanged\.handOver"", OnOwnSwitchOn\)"),
                "its own switch-on, after the core (ModSave reacts to the same event first)");

            string handler = Body(feature, "private void OnOwnSwitchOn()");
            Assert.True(handler.Contains("if (ReferenceEquals(_save.State, _stateWhenEnabled)) return;"), "the frozen section is the same object: nothing to do");
            Assert.True(handler.Contains("_characters.StartGrace(ModTicks.Today, false)"), "a section without a grace period starts it");
            Assert.True(handler.Contains("_registry.ApplyAll(\"section handed over\")"), "ApplyAll keeps the snapshots of the designers, OnEnabled would drop them");
            Assert.False(handler.Contains("_registry.OnEnabled"), "OnEnabled clears the snapshots: the designers' own priorities would be lost");
        }

        [Test]
        public void SwitchingOn_StartsFromTheState_WhichTheCoreHandsOverAfterOnEnabled()
        {
            string feature = FeatureCode("RivalPersonalitiesFeature.cs");
            string body = Body(feature, "public void OnEnabled()");

            // OnEnabled runs before the core's hand-over (ModSave reacts to StateChanged, which is raised after OnEnabled): it works on the frozen state, which is the same object
            Assert.True(body.Contains("_registry.OnEnabled(ModTicks.IsGameReady)"), "every company gets its character at once in a ready game");
            Assert.False(body.Contains("State = ") || body.Contains("ResetToDefaults"), "OnEnabled does not touch the section");
        }
    }
}
