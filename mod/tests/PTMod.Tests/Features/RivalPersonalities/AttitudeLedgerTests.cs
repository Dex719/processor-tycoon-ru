using System.Collections.Generic;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The attitude of the rivals (TSK-010; design.md Component 6; FR-4): the start at the baseline, the change with the sensitivity and the clip, the
    /// history of twenty, the level and its notification event, the monthly drift, the frozen bankrupt, and the round trip through the side save file.
    /// The notification itself (<c>ModNotify</c>) is the game side's: here the event it hangs on.
    /// </summary>
    [Covers(typeof(AttitudeLedger))]
    public class AttitudeLedgerTests
    {
        // ---- the start ----

        [Test]
        public void NewCompany_StartsAtTheBaselineOfItsArchetype()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();

            Assert.Near(-10f, rig.Ledger.Get(AttitudeRig.Intel), 1e-5, "hegemon");
            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5, "price_warrior");
            Assert.Near(0f, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-5, "fast_follower");
            Assert.Near(20f, rig.Ledger.Get(AttitudeRig.Tsmc), 1e-5, "foundry_partner");
            Assert.Equal(AttitudeLevel.Neutral, rig.Ledger.GetLevel(AttitudeRig.Amd));
            Assert.Equal(AttitudeLevel.Respect, rig.Ledger.GetLevel(AttitudeRig.Tsmc), "+20 is Respect");
        }

        [Test]
        public void ACompanyWithAnArchetypeButNoSavedValue_IsAtTheBaseline()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);
            rig.State.attitudes.Clear();   // an old save: the archetype is there, the value is not

            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5);

            rig.State.attitudes.Add(new AttitudeRecord { uid = AttitudeRig.Amd });   // the value is NaN until set
            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5, "NaN means the baseline");
        }

        [Test]
        public void ACompanyWithoutAnArchetype_IsUnknown()
        {
            AttitudeRig rig = new AttitudeRig();

            Assert.False(rig.Ledger.IsKnown(AttitudeRig.Amd));
            Assert.Equal(0f, rig.Ledger.Get(AttitudeRig.Amd));
            Assert.Equal(AttitudeLevel.Neutral, rig.Ledger.GetLevel(AttitudeRig.Amd));
            Assert.False(rig.Ledger.Change(AttitudeRig.Amd, -8f, "StrongLaunch:1"), "nothing is kept for an unknown company");
            Assert.Equal(0, rig.State.attitudes.Count, "and nothing is created");
            Assert.Equal(0, rig.LevelEvents.Count);
            Assert.Equal(0, rig.Ledger.MonthlyDrift());
        }

        // ---- the change ----

        [Test]
        public void Change_AppliesTheSensitivity_AndTheLevelChangesWithANotification()
        {
            // AC-4.1: AMD at -15 (baseline -15, sensitivity 1.3), a strong launch of -8 -> -25.4, and the level becomes "Rivalry"
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);

            Assert.True(rig.Ledger.Change(AttitudeRig.Amd, -8f, "StrongLaunch:1"));

            Assert.Near(-25.4, rig.Ledger.Get(AttitudeRig.Amd), 1e-4);
            Assert.Equal(AttitudeLevel.Rivalry, rig.Ledger.GetLevel(AttitudeRig.Amd));
            Assert.Equal(1, rig.LevelEvents.Count);
            Assert.Equal("1:Neutral>Rivalry", rig.LevelEvents[0]);
        }

        [Test]
        public void Change_WritesTheHistory_WithTheRealChangeTheDateAndTheReason()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);
            rig.Today = new System.DateTime(1984, 3, 12);

            rig.Ledger.Change(AttitudeRig.Amd, -8f, "StrongLaunch:1");

            IReadOnlyList<AttitudeEntry> history = rig.Ledger.History(AttitudeRig.Amd);
            Assert.Equal(1, history.Count);
            Assert.Equal("1984-03-12", history[0].date);
            Assert.Near(-10.4, history[0].delta, 1e-4, "the delta times the sensitivity, as in the example of the design");
            Assert.Equal("StrongLaunch:1", history[0].reason);
        }

        [Test]
        public void Change_ClipsToTheScale()
        {
            // AC-4.3: +98, +15 -> +100 (Toshiba: sensitivity 1.0)
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba).SetAttitude(AttitudeRig.Toshiba, 98f);

            Assert.True(rig.Ledger.Change(AttitudeRig.Toshiba, 15f, "BusinessSigned"));

            Assert.Equal(100f, rig.Ledger.Get(AttitudeRig.Toshiba));
            Assert.Near(2.0, rig.Ledger.History(AttitudeRig.Toshiba)[0].delta, 1e-4, "the history keeps what really happened");

            Assert.True(rig.Ledger.Change(AttitudeRig.Toshiba, 15f, "BusinessSigned"), "the change is accepted at the edge of the scale...");
            Assert.Equal(100f, rig.Ledger.Get(AttitudeRig.Toshiba));
            Assert.Equal(1, rig.Ledger.History(AttitudeRig.Toshiba).Count, "...but a change of nothing is not written down");

            AttitudeRig low = new AttitudeRig().Assign(AttitudeRig.Toshiba).SetAttitude(AttitudeRig.Toshiba, -95f);
            low.Ledger.Change(AttitudeRig.Toshiba, -25f, "BusinessBrokenByPlayer");
            Assert.Equal(-100f, low.Ledger.Get(AttitudeRig.Toshiba));
        }

        [Test]
        public void Change_AcrossTwoBorders_RaisesOneEvent_WithTheFirstAndTheLastLevel()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);

            rig.Ledger.Change(AttitudeRig.Toshiba, -70f, "test");

            Assert.Equal(AttitudeLevel.Hostile, rig.Ledger.GetLevel(AttitudeRig.Toshiba));
            Assert.Equal(1, rig.LevelEvents.Count);
            Assert.Equal("6:Neutral>Hostile", rig.LevelEvents[0]);
        }

        [Test]
        public void Change_WithinALevel_RaisesNoEvent()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);

            rig.Ledger.Change(AttitudeRig.Toshiba, -5f, "test");
            rig.Ledger.Change(AttitudeRig.Toshiba, 8f, "test");

            Assert.Equal(0, rig.LevelEvents.Count);
        }

        [Test]
        public void LevelBorders_AreMinus60_Minus20_Plus20_Plus60()
        {
            AttitudeRig rig = new AttitudeRig();

            Assert.Equal(AttitudeLevel.Hostile, rig.Ledger.LevelOf(-60f));
            Assert.Equal(AttitudeLevel.Rivalry, rig.Ledger.LevelOf(-59f));
            Assert.Equal(AttitudeLevel.Rivalry, rig.Ledger.LevelOf(-20f));
            Assert.Equal(AttitudeLevel.Neutral, rig.Ledger.LevelOf(-19f));
            Assert.Equal(AttitudeLevel.Neutral, rig.Ledger.LevelOf(19f));
            Assert.Equal(AttitudeLevel.Respect, rig.Ledger.LevelOf(20f));
            Assert.Equal(AttitudeLevel.Respect, rig.Ledger.LevelOf(59f));
            Assert.Equal(AttitudeLevel.Partnership, rig.Ledger.LevelOf(60f));
        }

        [Test]
        public void Change_NotANumber_IsIgnored()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);

            Assert.False(rig.Ledger.Change(AttitudeRig.Amd, float.NaN, "test"));
            Assert.False(rig.Ledger.Change(AttitudeRig.Amd, float.PositiveInfinity, "test"));

            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5);
            Assert.Equal(0, rig.Ledger.History(AttitudeRig.Amd).Count);
        }

        [Test]
        public void ALongReason_IsCutInTheHistory_AndAMissingOneIsEmpty()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);

            rig.Ledger.Change(AttitudeRig.Toshiba, 1f, new string('x', 200));
            rig.Ledger.Change(AttitudeRig.Toshiba, 1f, null);

            IReadOnlyList<AttitudeEntry> history = rig.Ledger.History(AttitudeRig.Toshiba);
            Assert.Equal(AttitudeLedger.MaxReasonLength, history[0].reason.Length);
            Assert.Equal(string.Empty, history[1].reason);
        }

        // ---- the history of twenty ----

        [Test]
        public void History_KeepsTheTwentyLatest()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);

            for (int i = 0; i < 25; i++) rig.Ledger.Change(AttitudeRig.Toshiba, 1f, "r" + i);

            IReadOnlyList<AttitudeEntry> history = rig.Ledger.History(AttitudeRig.Toshiba);
            Assert.Equal(20, history.Count);
            Assert.Equal("r5", history[0].reason, "the five oldest are gone");
            Assert.Equal("r24", history[19].reason);
            Assert.Near(25f, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-4, "the value still counts every change");
        }

        [Test]
        public void History_IsACopy_AndEmptyForACompanyWithoutRecord()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);
            rig.Ledger.Change(AttitudeRig.Toshiba, 1f, "a");

            IReadOnlyList<AttitudeEntry> copy = rig.Ledger.History(AttitudeRig.Toshiba);
            copy[0].reason = "changed";

            Assert.Equal("a", rig.Ledger.History(AttitudeRig.Toshiba)[0].reason);
            Assert.Equal(0, rig.Ledger.History(99).Count);
        }

        // ---- the drift ----

        [Test]
        public void MonthlyDrift_MovesTenPercentOfTheWayToTheBaseline()
        {
            // AC-4.2: -40 at baseline 0 -> -36
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba).SetAttitude(AttitudeRig.Toshiba, -40f);

            int changed = rig.Ledger.MonthlyDrift();

            Assert.Equal(1, changed);
            Assert.Near(-36.0, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-4);
        }

        [Test]
        public void MonthlyDrift_GoesToTheBaselineOfTheArchetype_FromAbove_AndFromBelow()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd, AttitudeRig.Tsmc);
            rig.SetAttitude(AttitudeRig.Amd, -25f);    // baseline -15
            rig.SetAttitude(AttitudeRig.Tsmc, 60f);    // baseline +20

            rig.Ledger.MonthlyDrift();

            Assert.Near(-24.0, rig.Ledger.Get(AttitudeRig.Amd), 1e-4, "-25 + (−15 − (−25)) × 0.1");
            Assert.Near(56.0, rig.Ledger.Get(AttitudeRig.Tsmc), 1e-4, "60 + (20 − 60) × 0.1");
        }

        [Test]
        public void MonthlyDrift_AtTheBaseline_ChangesNothing_AndDoesNotWriteTheHistory()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();

            Assert.Equal(0, rig.Ledger.MonthlyDrift());

            rig.SetAttitude(AttitudeRig.Toshiba, -40f);
            for (int month = 0; month < 12; month++) rig.Ledger.MonthlyDrift();
            Assert.Equal(0, rig.Ledger.History(AttitudeRig.Toshiba).Count, "the drift is not a reason the player reads");
        }

        [Test]
        public void MonthlyDrift_RaisesTheLevelEvent_WhenItCrossesABorder()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba).SetAttitude(AttitudeRig.Toshiba, -21f);

            rig.Ledger.MonthlyDrift();   // -18.9

            Assert.Equal(AttitudeLevel.Neutral, rig.Ledger.GetLevel(AttitudeRig.Toshiba));
            Assert.Equal(1, rig.LevelEvents.Count);
            Assert.Equal("6:Rivalry>Neutral", rig.LevelEvents[0]);
        }

        [Test]
        public void MonthlyDrift_ForAValueNeverSet_GivesTheBaseline()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);
            rig.RecordOf(AttitudeRig.Amd).value = float.NaN;

            rig.Ledger.MonthlyDrift();

            Assert.Near(-15.0, rig.RecordOf(AttitudeRig.Amd).value, 1e-5);
            Assert.Equal(0, rig.LevelEvents.Count);
        }

        [Test]
        public void MonthlyDrift_LeavesRecordsOfUnknownCompaniesAlone()
        {
            AttitudeRig rig = new AttitudeRig();
            rig.SetAttitude(77, -40f);   // a record of a company that has no archetype (kept, not used)

            Assert.Equal(0, rig.Ledger.MonthlyDrift());
            Assert.Equal(-40f, rig.RecordOf(77).value);
        }

        // ---- the bankrupt ----

        [Test]
        public void ABankruptCompany_IsFrozen()
        {
            // FR-4 item 5
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba, AttitudeRig.Amd).SetAttitude(AttitudeRig.Toshiba, -40f);
            rig.Bankrupt.Add(AttitudeRig.Toshiba);

            Assert.True(rig.Ledger.IsFrozen(AttitudeRig.Toshiba));
            Assert.False(rig.Ledger.Change(AttitudeRig.Toshiba, -8f, "StrongLaunch:1"), "no change");
            rig.Ledger.MonthlyDrift();

            Assert.Equal(-40f, rig.Ledger.Get(AttitudeRig.Toshiba), "no drift either");
            Assert.Equal(0, rig.Ledger.History(AttitudeRig.Toshiba).Count);
            Assert.Equal(0, rig.LevelEvents.Count);
            Assert.Near(-15.0, rig.Ledger.Get(AttitudeRig.Amd), 1e-5, "the others are not frozen");
        }

        [Test]
        public void AFrozenCompany_ThatIsHealthyAgain_ChangesAgain()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);
            rig.Bankrupt.Add(AttitudeRig.Toshiba);
            rig.Ledger.Change(AttitudeRig.Toshiba, 10f, "x");
            rig.Bankrupt.Clear();

            Assert.True(rig.Ledger.Change(AttitudeRig.Toshiba, 10f, "x"));
            Assert.Near(10f, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-5);
        }

        // ---- the handlers of the event ----

        [Test]
        public void AFailingHandlerOfLevelChanged_DoesNotStopTheChange_OrTheOtherHandlers()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Toshiba);
            var calls = new List<string>();
            rig.Ledger.LevelChanged += (uid, from, to) => { throw new System.InvalidOperationException("boom"); };
            rig.Ledger.LevelChanged += (uid, from, to) => calls.Add("second");

            Assert.True(rig.Ledger.Change(AttitudeRig.Toshiba, -30f, "x"));

            Assert.Near(-30f, rig.Ledger.Get(AttitudeRig.Toshiba), 1e-5, "the value was saved before the handlers ran");
            Assert.Equal(1, calls.Count, "the next handler still ran");
            Assert.True(rig.Warnings.Exists(w => w.Contains("boom")), "and the failure was reported: " + string.Join("; ", rig.Warnings));
        }

        // ---- the save ----

        [Test]
        public void TheValuesAndTheHistory_SurviveSavingAndLoading()
        {
            AttitudeRig rig = new AttitudeRig().AssignAll();
            rig.Today = new System.DateTime(1984, 3, 12);
            rig.Ledger.Change(AttitudeRig.Amd, -8f, "StrongLaunch:1");
            rig.Ledger.Change(AttitudeRig.Tsmc, 15f, "BusinessSigned");
            rig.Ledger.MonthlyDrift();
            float amd = rig.Ledger.Get(AttitudeRig.Amd);
            float tsmc = rig.Ledger.Get(AttitudeRig.Tsmc);

            string text = ModJson.Write(RivalSaveCodec.Write(rig.State), false);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "the saved text is JSON: " + error);
            var loaded = new AttitudeRig();
            loaded.State = RivalSaveCodec.Read((JsonObject)node, null);

            Assert.Near(amd, loaded.Ledger.Get(AttitudeRig.Amd), 1e-4);
            Assert.Near(tsmc, loaded.Ledger.Get(AttitudeRig.Tsmc), 1e-4);
            Assert.Equal(1, loaded.Ledger.History(AttitudeRig.Amd).Count);
            Assert.Equal("StrongLaunch:1", loaded.Ledger.History(AttitudeRig.Amd)[0].reason);
            Assert.Equal("1984-03-12", loaded.Ledger.History(AttitudeRig.Amd)[0].date);
            Assert.Equal("BusinessSigned", loaded.Ledger.History(AttitudeRig.Tsmc)[0].reason);
            Assert.Equal(AttitudeLevel.Rivalry, loaded.Ledger.GetLevel(AttitudeRig.Amd));
        }

        [Test]
        public void TheLedgerReadsTheStateAgainAtEveryCall_SoALoadReplacesIt()
        {
            AttitudeRig rig = new AttitudeRig().Assign(AttitudeRig.Amd);
            rig.Ledger.Change(AttitudeRig.Amd, -8f, "x");

            rig.State = new RivalState();   // a load replaced the object
            rig.Book.EnsureAssigned(AttitudeRig.Amd);

            Assert.Near(-15f, rig.Ledger.Get(AttitudeRig.Amd), 1e-5, "the new state's value, not a cached one");
            Assert.Equal(0, rig.Ledger.History(AttitudeRig.Amd).Count);
        }

        [Test]
        public void LevelKeyNames_MatchTheKeysOfTheStringTables()
        {
            Assert.Equal("hostile", AttitudeLedger.LevelKeyName(AttitudeLevel.Hostile));
            Assert.Equal("rivalry", AttitudeLedger.LevelKeyName(AttitudeLevel.Rivalry));
            Assert.Equal("neutral", AttitudeLedger.LevelKeyName(AttitudeLevel.Neutral));
            Assert.Equal("respect", AttitudeLedger.LevelKeyName(AttitudeLevel.Respect));
            Assert.Equal("partnership", AttitudeLedger.LevelKeyName(AttitudeLevel.Partnership));

            foreach (string language in new[] { "ru", "en" })
            {
                JsonObject table = RivalTestKit.ReadL10n(language);
                foreach (AttitudeLevel level in new[] { AttitudeLevel.Hostile, AttitudeLevel.Rivalry, AttitudeLevel.Neutral, AttitudeLevel.Respect, AttitudeLevel.Partnership })
                {
                    string key = "rival.level." + AttitudeLedger.LevelKeyName(level);
                    Assert.True(table.ContainsKey(key), language + " lacks " + key);
                }
                Assert.True(table.ContainsKey("rival.notify.level"), language + " lacks rival.notify.level");
            }
        }
    }
}
