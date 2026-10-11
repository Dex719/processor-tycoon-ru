using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PTMod.Core.Logic.Features;
using PTMod.Features.DesignFlawRecalls;
using PTMod.Tests.Architecture;

namespace PTMod.Tests.Features.DesignFlawRecalls
{
    /// <summary>
    /// TSK-026 (FR-18, AC-18.1): the correction of the reviews of the press for a CPU with a known flaw. The rules (<see cref="FlawPressRules"/>): a known, open flaw lowers the total by
    /// <c>press.overallAdjustment[severity]</c> (-8 / -15 / -15) and adds the note; a hidden or closed one changes nothing; the secret of a rival is kept; the draft here is a fake. The
    /// adjuster itself (<c>FlawPressAdjuster : IReviewAdjuster</c>), its registration with <c>PressReviewsApi</c> and the absence of articles are checked on the source.
    /// </summary>
    [Covers(typeof(FlawPressRules), typeof(IFlawReviewDraft))]
    public class FlawPressAdjusterTests
    {
        private sealed class FakeDraft : IFlawReviewDraft
        {
            public float Overall { get; set; }
            public readonly List<string> Notes = new List<string>();

            public float OverallAdjustment
            {
                get { return Overall; }
                set { Overall = value; }
            }

            public void AddNote(string noteKey)
            {
                Notes.Add(noteKey);
            }
        }

        private static readonly FlawBalance Balance = FlawBalance.Defaults();
        private static readonly int Today = FlawCycleKit.MonthStart(30);

        private static FlawRecordView View(FlawState state, FlawSeverity severity = FlawSeverity.Major, string company = "player", int publicSince = 0,
            FlawResponse response = FlawResponse.None)
        {
            FlawRecord record = FlawCycleKit.Record(state, 1, 100, company, severity, response);
            record.Data.publicSinceDay = publicSince;
            return FlawViews.Of(record, Today);
        }

        // ---- the amounts (FR-18 item 2) ----

        [Test]
        public void TheAdjustment_IsThePressTableOfTheContent_PerSeverity()
        {
            Assert.Equal(-8f, FlawPressRules.Adjustment(FlawSeverity.Minor, Balance));
            Assert.Equal(-15f, FlawPressRules.Adjustment(FlawSeverity.Major, Balance));
            Assert.Equal(-15f, FlawPressRules.Adjustment(FlawSeverity.Critical, Balance));

            var custom = FlawBalance.Defaults();
            custom.Press.OverallAdjustment = new[] { -4, -10, -20 };
            Assert.Equal(-4f, FlawPressRules.Adjustment(FlawSeverity.Minor, custom), "the numbers are the content's");
            Assert.Equal(-20f, FlawPressRules.Adjustment(FlawSeverity.Critical, custom));

            Assert.Equal(0f, FlawPressRules.Adjustment(FlawSeverity.Major, null), "no numbers: no correction");
            custom.Press.OverallAdjustment = new int[0];
            Assert.Equal(0f, FlawPressRules.Adjustment(FlawSeverity.Major, custom), "a broken table: no correction");
        }

        [Test]
        public void TheShippedContent_HoldsTheConfirmedNumbers()
        {
            Assert.Equal(new[] { -8, -15, -15 }, Balance.Press.OverallAdjustment, "the owner confirmed -8 / -15 / -15 (the limit of the press is +-20)");
            Assert.True(Balance.Press.OverallAdjustment.All(a => Math.Abs(a) <= 20), "inside the limit of the press");
        }

        // ---- AC-18.1 on a fake draft ----

        [Test]
        public void AC18_1_APublicMajorWithAnActivePatch_LowersTheScoreBy15_AndAddsTheNote()
        {
            var draft = new FakeDraft();

            bool changed = FlawPressRules.Apply(View(FlawState.Mitigated, FlawSeverity.Major, publicSince: Today - 50, response: FlawResponse.Patch), Balance, draft);

            Assert.True(changed);
            Assert.Equal(-15f, draft.Overall, "the metascore is 15 lower");
            Assert.Equal(new[] { "press.note.knownFlaw" }, draft.Notes.ToArray(), "and the card has the note about the defect");
        }

        [Test]
        public void AKnownOpenFlaw_ChangesTheDraft_ForEverySeverityAndState()
        {
            foreach (FlawState state in new[] { FlawState.InternalKnown, FlawState.Public, FlawState.Stepping, FlawState.Mitigated })
            {
                foreach (FlawSeverity severity in new[] { FlawSeverity.Minor, FlawSeverity.Major, FlawSeverity.Critical })
                {
                    var draft = new FakeDraft();
                    Assert.True(FlawPressRules.Apply(View(state, severity), Balance, draft), "the player's flaw in " + state + "/" + severity);
                    Assert.Equal(FlawPressRules.Adjustment(severity, Balance), draft.Overall);
                    Assert.Equal(1, draft.Notes.Count);
                }
            }
        }

        [Test]
        public void TheAdjustment_IsAddedToWhatTheOtherAdjustersDid()
        {
            var draft = new FakeDraft { Overall = 3f };
            draft.Notes.Add("press.note.other");

            FlawPressRules.Apply(View(FlawState.Public, FlawSeverity.Minor), Balance, draft);

            Assert.Equal(-5f, draft.Overall, "the earlier correction stays, ours is added (the press holds the sum to +-20)");
            Assert.Equal(new[] { "press.note.other", "press.note.knownFlaw" }, draft.Notes.ToArray());
        }

        // ---- what does not change the review (FR-18 item 3) ----

        [Test]
        public void AHiddenOrClosedFlaw_LeavesTheDraftAlone()
        {
            foreach (FlawState state in new[] { FlawState.Hidden, FlawState.Resolved, FlawState.Expired, FlawState.Closed })
            {
                var draft = new FakeDraft { Overall = 1f };
                Assert.False(FlawPressRules.Apply(View(state), Balance, draft), "a record in " + state);
                Assert.Equal(1f, draft.Overall, state + ": the total is as it was");
                Assert.Equal(0, draft.Notes.Count, state + ": no note");
            }

            var none = new FakeDraft();
            Assert.False(FlawPressRules.Apply(null, Balance, none), "a CPU without a record (the registry gives null for it and for a hidden one)");
            Assert.Equal(0f, none.Overall);
            Assert.Throws<ArgumentNullException>(() => FlawPressRules.Apply(View(FlawState.Public), Balance, null));
        }

        [Test]
        public void TheSecretOfARival_IsKept_UntilItIsPublic()
        {
            Assert.False(FlawPressRules.Applies(View(FlawState.InternalKnown, company: "ai:4")), "an internal flaw of a rival: the press cannot know (FR-11 item 5: no rumours)");
            Assert.False(FlawPressRules.Applies(View(FlawState.Stepping, company: "ai:4", response: FlawResponse.Stepping)), "a quiet revision of a rival");
            Assert.True(FlawPressRules.Applies(View(FlawState.Public, company: "ai:4", publicSince: Today - 1)), "a public flaw of a rival");
            Assert.True(FlawPressRules.Applies(View(FlawState.Mitigated, company: "ai:4", publicSince: Today - 30, response: FlawResponse.Patch)), "and a patch after it became public");
            Assert.True(FlawPressRules.Applies(View(FlawState.InternalKnown, company: "player")), "the player's own flaw is 'known' in the sense of the spec (not hidden)");
            Assert.False(FlawPressRules.Applies(null));
        }

        [Test]
        public void TheIdAndTheNote_AreThoseOfTheSpec_AndTheNoteExistsInThePressTables()
        {
            Assert.Equal("design-flaw-recalls", FlawPressRules.AdjusterId, "AC-18.1: Id = design-flaw-recalls");
            Assert.Equal("press.note.knownFlaw", FlawPressRules.NoteKey);
            Assert.True(FlawPressRules.NoteKey.StartsWith("press.", StringComparison.Ordinal), "the press accepts only its own prefix");

            // The press drops a note that its tables do not have: the key must be in both of its languages.
            Assert.Equal("Пресса знает о дефекте.", FlawApiKit.Strings("ru", "press-reviews")[FlawPressRules.NoteKey]);
            Assert.True(FlawApiKit.Strings("en", "press-reviews").ContainsKey(FlawPressRules.NoteKey));
        }

        // ---- the registration with the press (FR-18 item 1) and no articles (item 4), checked on the source ----

        [Test]
        public void TheBridgeToThePress_RegistersWhileBothFeaturesAreOn_AndTakesTheAdjusterOutWhenOneIsOff()
        {
            string bridge = FlawApiKit.Code("Press/FlawPressBridge.cs");

            Assert.True(bridge.Contains("ModFeatures.IsActive(PressFeatureId) && ModFeatures.IsActive(DesignFlawRecallsFeature.FeatureId)"), "wanted: both are on");
            Assert.True(bridge.Contains("PressReviewsApi.RegisterAdjuster(_adjuster);") && bridge.Contains("PressReviewsApi.UnregisterAdjuster(_adjuster.Id);"), "PressReviewsApi.RegisterAdjuster and UnregisterAdjuster, by the same id");
            Assert.True(bridge.Contains("PressFeatureId = \"press-reviews\""), "the press-reviews feature");
            Assert.False(bridge.Contains("Reflection"), "the API of the press is called directly: the full build compiles both features into one assembly");

            string refresh = FlawApiKit.Between(bridge, "internal void Refresh()", "internal void OnFeatureStateChanged");
            Assert.True(refresh.Contains("if (IsWanted) Register();") && refresh.Contains("else Unregister();"), "register when wanted, take out otherwise");
            Assert.True(refresh.Contains("ModHooks.Guard("), "never throws");

            string adjuster = FlawApiKit.Code("Press/FlawPressAdjuster.cs");
            Assert.True(adjuster.Contains("internal sealed class FlawPressAdjuster : IReviewAdjuster"), "a plain class of the contract of the press");
            Assert.True(adjuster.Contains("get { return FlawPressRules.AdjusterId; }"), "the id of the adjuster is the id of the feature");
        }

        [Test]
        public void TheLinkToThePress_FollowsTheStateOfBothFeatures_TheGameReady_AndTheSwitching()
        {
            string feature = FlawApiKit.Code("DesignFlawRecallsFeature.cs");

            Assert.True(feature.Contains("ModFeatures.StateChanged += Press.OnFeatureStateChanged;"), "a change of the state of the press or of this feature");
            Assert.True(FlawApiKit.Between(feature, "private void OnGameReady(GameReadyReason reason)", "private void RememberLastActiveDay()").Contains("Press.Refresh();"), "the game is ready");
            Assert.True(FlawApiKit.Between(feature, "public void OnEnabled()", "public void OnDisabled()").Contains("Press.Refresh();"), "switched on");
            Assert.True(feature.Substring(feature.IndexOf("public void OnDisabled()", StringComparison.Ordinal)).Contains("Press.Detach();"), "switched off: the adjuster leaves the press");
            Assert.True(FlawApiKit.Code("Press/FlawPressBridge.cs").Contains("if (featureId != PressFeatureId && featureId != DesignFlawRecallsFeature.FeatureId) return;"),
                "only these two features matter");
        }

        [Test]
        public void TheFeature_NeverWritesAnArticleOfThePress()
        {
            foreach (KeyValuePair<string, string> file in FlawApiKit.AllCode())
            {
                Assert.False(file.Value.Contains("PublishArticle"), file.Key + " publishes an article: that is the job of press-reviews, on OnFlawStateChanged (FR-18 item 4)");
                Assert.False(file.Value.Contains("PressArticle"), file.Key + " builds an article");
            }
        }

        [Test]
        public void TheAdjuster_HasNoSideEffects_AndOnlyReadsTheRegistry()
        {
            string code = FlawApiKit.Code("Press/FlawPressAdjuster.cs");
            string adjust = FlawApiKit.Between(code, "public void Adjust(GameCpu cpu, ReviewDraft draft)", "private sealed class DraftOfThePress");

            Assert.True(adjust.Contains("DesignFlawRecallsApi.Registry.FindByCpu(cpu.SaveID)"), "the record comes from the public registry: a hidden flaw is not seen");
            Assert.True(adjust.Contains("FlawPressRules.Apply(view, _feature.Balance, new DraftOfThePress(draft))"), "the rules are the pure ones");
            foreach (string effect in new[] { "SpendMoney", "ModReputation", "ModMail", "ModNotify", "ModModifiers", "ForceFlaw", "TryReveal", ".Store." })
            {
                Assert.False(adjust.Contains(effect), "an adjuster has no side effects: " + effect);
            }
        }
    }
}
