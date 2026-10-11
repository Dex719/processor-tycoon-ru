using System.Collections.Generic;
using System.Linq;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// Who sees the character of a rival (TSK-028; design.md Component 11, formula 14; FR-11 items 2-4, D11): <c>IsVisible = !hiddenMode || revealed ||
    /// a ≥ 20</c>; the record <c>revealed</c> of the section is kept once, survives the save, and neither switching the mode off nor on again touches it.
    /// </summary>
    [Covers(typeof(CharacterVisibility))]
    public class CharacterVisibilityTests
    {
        private const float Reveal = 20f;

        [Test]
        public void WithTheModeOff_EveryCharacterIsVisible()
        {
            Assert.True(CharacterVisibility.IsVisible(false, false, -80f, Reveal));
            Assert.True(CharacterVisibility.IsVisible(false, false, 0f, Reveal));
            Assert.True(CharacterVisibility.IsVisible(false, true, 50f, Reveal));
        }

        [Test]
        public void WithTheModeOn_ACharacterIsHidden_WithoutAReaction_AndBelowTheLevel()
        {
            Assert.False(CharacterVisibility.IsVisible(true, false, 0f, Reveal));
            Assert.False(CharacterVisibility.IsVisible(true, false, -60f, Reveal));
            Assert.False(CharacterVisibility.IsVisible(true, false, 19.99f, Reveal), "just below +20");
        }

        [Test]
        public void WithTheModeOn_ACharacterIsVisible_AfterTheFirstReaction()
        {
            Assert.True(CharacterVisibility.IsVisible(true, true, -80f, Reveal));
            Assert.True(CharacterVisibility.IsVisible(true, true, 0f, Reveal));
        }

        [Test]
        public void WithTheModeOn_ACharacterIsVisible_FromAnAttitudeOf20()
        {
            // the foundry partners start at +20 and have no hostile reaction: AC-11.3
            Assert.True(CharacterVisibility.IsVisible(true, false, 20f, Reveal));
            Assert.True(CharacterVisibility.IsVisible(true, false, 100f, Reveal));
            Assert.False(CharacterVisibility.IsVisible(true, false, 20f, 21f), "the level is a parameter");
            Assert.True(CharacterVisibility.IsVisible(true, false, 21f, 21f));
            Assert.False(CharacterVisibility.IsVisible(true, false, float.NaN, Reveal), "an attitude that is not a number reaches nothing");
            Assert.Near(20.0, CharacterVisibility.DefaultRevealAttitude, 1e-9);
        }

        [Test]
        public void TheFoundryPartner_IsVisibleAtOnce_OnTheShippedContent()
        {
            var rig = new AttitudeRig().AssignAll();
            float reveal = rig.Catalog.Config.attitude.revealAttitude;

            Assert.True(CharacterVisibility.IsVisible(true, false, rig.Ledger.Get(AttitudeRig.Tsmc), reveal), "TSMC starts at +20");
            Assert.False(CharacterVisibility.IsVisible(true, false, rig.Ledger.Get(AttitudeRig.Amd), reveal), "AMD starts at -15");
            Assert.False(CharacterVisibility.IsVisible(true, false, rig.Ledger.Get(AttitudeRig.Intel), reveal));
        }

        // ---- the record ----

        [Test]
        public void Reveal_SavesTheRival_WithoutDuplicates_AndTellsTheFirstTime()
        {
            var rig = new AttitudeRig().AssignAll();

            Assert.False(rig.Book.IsRevealed(AttitudeRig.Amd));
            Assert.True(rig.Book.Reveal(AttitudeRig.Amd), "the first time");
            Assert.False(rig.Book.Reveal(AttitudeRig.Amd), "the notification is shown once");
            Assert.True(rig.Book.Reveal(AttitudeRig.Intel));

            Assert.True(rig.Book.IsRevealed(AttitudeRig.Amd));
            Assert.True(rig.Book.IsRevealed(AttitudeRig.Intel));
            Assert.False(rig.Book.IsRevealed(AttitudeRig.Ibm));
            Assert.Equal(new[] { AttitudeRig.Amd, AttitudeRig.Intel }, rig.State.revealed.ToArray());
            Assert.False(rig.Book.Reveal(-1), "not a company");
            Assert.Equal(2, rig.State.revealed.Count);
        }

        [Test]
        public void TheRecord_SurvivesTheSave()
        {
            var rig = new AttitudeRig().AssignAll();
            rig.Book.Reveal(AttitudeRig.Amd);
            rig.Book.Reveal(AttitudeRig.Toshiba);

            string text = ModJson.Write(RivalSaveCodec.Write(rig.State), false);
            JsonNode node;
            JsonError error;
            Assert.True(ModJson.TryParse(text, out node, out error), "JSON: " + error);
            var loaded = new AttitudeRig();
            loaded.State = RivalSaveCodec.Read((JsonObject)node, null);

            Assert.True(loaded.Book.IsRevealed(AttitudeRig.Amd));
            Assert.True(loaded.Book.IsRevealed(AttitudeRig.Toshiba));
            Assert.False(loaded.Book.IsRevealed(AttitudeRig.Intel));
            Assert.False(loaded.Book.Reveal(AttitudeRig.Amd), "and it is not shown again after a load");
        }

        [Test]
        public void SwitchingTheMode_DoesNotTouchTheRecord_AndARevealedRivalStaysVisible()
        {
            var rig = new AttitudeRig().AssignAll();
            float amd = rig.Ledger.Get(AttitudeRig.Amd);   // -15: hidden unless revealed
            float intel = rig.Ledger.Get(AttitudeRig.Intel);
            rig.Book.Reveal(AttitudeRig.Amd);
            List<int> before = rig.State.revealed.ToList();

            // the mode off: everything is visible, the record is as it was
            Assert.True(CharacterVisibility.IsVisible(false, rig.Book.IsRevealed(AttitudeRig.Amd), amd, Reveal));
            Assert.True(CharacterVisibility.IsVisible(false, rig.Book.IsRevealed(AttitudeRig.Intel), intel, Reveal));
            Assert.Equal(before, rig.State.revealed);

            // the mode on again: the revealed rival is still visible, the other one is hidden again
            Assert.True(CharacterVisibility.IsVisible(true, rig.Book.IsRevealed(AttitudeRig.Amd), amd, Reveal));
            Assert.False(CharacterVisibility.IsVisible(true, rig.Book.IsRevealed(AttitudeRig.Intel), intel, Reveal));
            Assert.Equal(before, rig.State.revealed);
        }

        [Test]
        public void ACharacterVisibleOnlyByItsAttitude_HidesAgain_IfTheAttitudeFalls()
        {
            var rig = new AttitudeRig().AssignAll();
            float reveal = rig.Catalog.Config.attitude.revealAttitude;

            rig.SetAttitude(AttitudeRig.Ibm, 35f);
            Assert.True(CharacterVisibility.IsVisible(true, rig.Book.IsRevealed(AttitudeRig.Ibm), rig.Ledger.Get(AttitudeRig.Ibm), reveal));

            rig.SetAttitude(AttitudeRig.Ibm, 10f);
            Assert.False(CharacterVisibility.IsVisible(true, rig.Book.IsRevealed(AttitudeRig.Ibm), rig.Ledger.Get(AttitudeRig.Ibm), reveal),
                "it was never revealed by a reaction: nothing keeps it visible");
            Assert.Equal(0, rig.State.revealed.Count, "the attitude does not write the record");
        }
    }
}
