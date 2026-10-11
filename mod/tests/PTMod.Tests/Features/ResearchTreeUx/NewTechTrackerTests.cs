using System.Collections.Generic;
using System.Linq;
using PTMod.Core.Logic.Json;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The marks "new" of FR-3 on the model (TSK-005): which available technologies are unseen, the mark, "everything available is seen" for a
    /// new game and an old save, the section of the save with its defences, and the counters of the tabs and the captions that come from the
    /// same answers. The adapters that draw them (<c>NewBadges</c>, <c>TabStrip</c>) are checked by the owner in the game.
    /// </summary>
    [Covers(typeof(NewTechTracker), typeof(TabBadges))]
    public class NewTechTrackerTests
    {
        // type: 2 memory, 4 cache.
        private static TechSnapshot Tech(int id, int type = 2, bool researched = false, bool locked = false)
        {
            return new TechSnapshot { Id = id, Name = "T" + id, Type = type, Year = 1980 + id, IsResearched = researched, IsLocked = locked };
        }

        private static List<TechSnapshot> Sample()
        {
            return new List<TechSnapshot>
            {
                Tech(0, 2, researched: true),     // done
                Tech(1, 2),                       // available memory
                Tech(2, 2, locked: true),         // locked memory
                Tech(3, 4),                       // available cache
                Tech(4, 4),                       // available cache
                Tech(5, 3),                       // available frequency
            };
        }

        private static TabDefinition TabOf(string id)
        {
            return TabCatalog.Defaults().First(t => t.Id == id);
        }

        // ---- who is new ----

        [Test]
        public void Unseen_AreTheAvailableOnes_NotResearchedNotLocked()
        {
            var tracker = new NewTechTracker();

            Assert.Equal(new[] { 1, 3, 4, 5 }, tracker.Unseen(Sample()).ToArray());
        }

        [Test]
        public void IsNew_NeedsAnAvailableTechnologyThatIsNotSeen()
        {
            var tracker = new NewTechTracker();
            Assert.True(tracker.IsNew(Tech(1)));
            Assert.False(tracker.IsNew(Tech(1, researched: true)), "researched");
            Assert.False(tracker.IsNew(Tech(1, locked: true)), "locked");
            Assert.False(tracker.IsNew(null));

            tracker.MarkSeen(1);
            Assert.False(tracker.IsNew(Tech(1)), "seen");
        }

        [Test]
        public void MarkSeen_RemovesTheMark_AndReportsWhetherItChangedAnything()
        {
            var tracker = new NewTechTracker();

            Assert.True(tracker.MarkSeen(3));
            Assert.False(tracker.MarkSeen(3), "the second time adds nothing");
            Assert.True(tracker.IsSeen(3));
            Assert.Equal(new[] { 1, 4, 5 }, tracker.Unseen(Sample()).ToArray());
        }

        [Test]
        public void MarkSeen_AList_CountsOnlyTheNewIds()
        {
            var tracker = new NewTechTracker();
            tracker.MarkSeen(4);

            Assert.Equal(2, tracker.MarkSeen(new[] { 3, 4, 5, 5 }));
            Assert.Equal(0, tracker.MarkSeen((IEnumerable<int>)null));
            Assert.Equal(new[] { 3, 4, 5 }, tracker.Seen.ToArray());
        }

        [Test]
        public void MarkSeen_AnIdBelowZero_IsNotAnId()
        {
            var tracker = new NewTechTracker();
            Assert.False(tracker.MarkSeen(-1));
            Assert.Equal(0, tracker.SeenCount);
        }

        [Test]
        public void Unseen_NullAndNullEntries_GiveNothingForThem()
        {
            var tracker = new NewTechTracker();
            Assert.Equal(0, tracker.Unseen(null).Count);

            var list = new List<TechSnapshot> { null, Tech(7) };
            Assert.Equal(new[] { 7 }, tracker.Unseen(list).ToArray());
        }

        [Test]
        public void CollectUnseen_FillsTheListWithoutAllocatingANewOne()
        {
            var tracker = new NewTechTracker();
            var into = new List<TechSnapshot> { Tech(99) };

            int count = tracker.CollectUnseen(Sample(), into);

            Assert.Equal(4, count);
            Assert.Equal(new[] { 1, 3, 4, 5 }, into.Select(t => t.Id).ToArray());
            Assert.Equal(0, tracker.CollectUnseen(null, into));
            Assert.Equal(0, into.Count, "cleared even when there is nothing to collect");
            Assert.Throws<System.ArgumentNullException>(() => tracker.CollectUnseen(Sample(), null));
        }

        // ---- everything available is seen ----

        [Test]
        public void InitializeAllAvailableAsSeen_LeavesNoMarks()
        {
            var tracker = new NewTechTracker();
            List<TechSnapshot> techs = Sample();

            int added = tracker.InitializeAllAvailableAsSeen(techs);

            Assert.Equal(4, added);
            Assert.Equal(0, tracker.Unseen(techs).Count, "a new game starts without marks (FR-3 item 7)");
            Assert.Equal(new[] { 1, 3, 4, 5 }, tracker.Seen.ToArray());
            Assert.False(tracker.IsSeen(0), "a researched technology is not an available one");
            Assert.False(tracker.IsSeen(2), "nor a locked one");
        }

        [Test]
        public void InitializeAllAvailableAsSeen_ATechnologyThatOpensLaterIsStillNew()
        {
            var tracker = new NewTechTracker();
            List<TechSnapshot> techs = Sample();
            tracker.InitializeAllAvailableAsSeen(techs);

            techs[2].IsLocked = false;   // the locked memory technology becomes available

            Assert.Equal(new[] { 2 }, tracker.Unseen(techs).ToArray());
        }

        [Test]
        public void InitializeAllAvailableAsSeen_NullAndNullEntries_AreSkipped()
        {
            var tracker = new NewTechTracker();
            Assert.Equal(0, tracker.InitializeAllAvailableAsSeen(null));
            Assert.Equal(1, tracker.InitializeAllAvailableAsSeen(new List<TechSnapshot> { null, Tech(1) }));
        }

        [Test]
        public void Clear_ForgetsEverything()
        {
            var tracker = new NewTechTracker();
            tracker.MarkSeen(new[] { 1, 2, 3 });

            tracker.Clear();

            Assert.Equal(0, tracker.SeenCount);
            Assert.Equal(new[] { 1, 3, 4, 5 }, tracker.Unseen(Sample()).ToArray());
        }

        // ---- the section of the save ----

        [Test]
        public void ToSave_IsAscendingWithoutRepeats()
        {
            var tracker = new NewTechTracker();
            tracker.MarkSeen(new[] { 44, 12, 31, 12 });

            ResearchUxSave save = tracker.ToSave();

            Assert.Equal(new[] { 12, 31, 44 }, save.seenAvailable.ToArray());
        }

        [Test]
        public void ToSave_IsACopy_TheTrackerKeepsItsOwn()
        {
            var tracker = new NewTechTracker();
            tracker.MarkSeen(5);

            ResearchUxSave save = tracker.ToSave();
            save.seenAvailable.Add(6);

            Assert.False(tracker.IsSeen(6));
        }

        [Test]
        public void FromSave_RestoresTheSet_AndATrackerWithoutSectionIsEmpty()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int> { 12, 31, 44, 58, 71 } };

            NewTechTracker tracker = NewTechTracker.FromSave(save, 97);

            Assert.Equal(new[] { 12, 31, 44, 58, 71 }, tracker.Seen.ToArray());
            Assert.Equal(0, NewTechTracker.FromSave(null, 97).SeenCount);
        }

        [Test]
        public void FromSave_DropsIdsOutsideTheTechnologies_RepeatsAndTheNegative()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int> { -3, 5, 5, 96, 97, 4000 } };

            NewTechTracker tracker = NewTechTracker.FromSave(save, 97);

            Assert.Equal(new[] { 5, 96 }, tracker.Seen.ToArray());
            Assert.Equal(6, save.seenAvailable.Count, "the section that was handed in is left as it was");
        }

        [Test]
        public void FromSave_WithoutAKnownCount_KeepsEveryNonNegativeId()
        {
            var save = new ResearchUxSave { seenAvailable = new List<int> { 3, 500 } };

            Assert.Equal(new[] { 3, 500 }, NewTechTracker.FromSave(save, 0).Seen.ToArray());
        }

        [Test]
        public void FromSave_AListOverTheLimit_IsCutAtTheLimit()
        {
            var save = new ResearchUxSave { seenAvailable = Enumerable.Range(0, 600).ToList() };

            NewTechTracker tracker = NewTechTracker.FromSave(save, 0);

            Assert.Equal(ResearchUxSave.MaxSeen, tracker.SeenCount);
            Assert.Equal(ResearchUxSave.MaxSeen, tracker.ToSave().seenAvailable.Count);
        }

        [Test]
        public void FromSave_ANullList_IsAnEmptySet()
        {
            var save = new ResearchUxSave { seenAvailable = null };
            Assert.Equal(0, NewTechTracker.FromSave(save, 97).SeenCount);
        }

        [Test]
        public void SaveRoundTrip_ThroughJson_KeepsTheSet_AndNeverComesBackNew()
        {
            // AC-3.2: after saving and loading the mark does not return.
            var tracker = new NewTechTracker();
            List<TechSnapshot> techs = Sample();
            tracker.MarkSeen(tracker.Unseen(techs));

            JsonObject written = (JsonObject)ModJson.From(tracker.ToSave());
            var bound = ModJson.Bind<ResearchUxSave>(written, new List<string>());
            NewTechTracker loaded = NewTechTracker.FromSave(bound, 97);

            Assert.Equal(0, loaded.Unseen(techs).Count);
            Assert.Equal(1, written.Count, "the section holds the list only, the core writes the version");
        }

        [Test]
        public void OldSaveWithoutSection_AllAvailableCountAsSeen_ThenANewOneShowsUp()
        {
            // FR-3 item 5: no section -> on Loaded everything available is seen; what opens afterwards is new.
            NewTechTracker tracker = NewTechTracker.FromSave(null, 97);
            List<TechSnapshot> techs = Sample();
            tracker.InitializeAllAvailableAsSeen(techs);
            Assert.Equal(0, tracker.Unseen(techs).Count);

            techs.Add(Tech(6, 4));   // L1 64 KB opens after SDR is researched

            Assert.Equal(new[] { 6 }, tracker.Unseen(techs).ToArray());
        }

        // ---- the counters of the tabs ----

        [Test]
        public void CountForTab_AllHoldsEveryNewOne_ATabItsOwnTypes()
        {
            var tracker = new NewTechTracker();
            List<TechSnapshot> unseen = new List<TechSnapshot>();
            tracker.CollectUnseen(Sample(), unseen);   // memory 1, cache 3 and 4, frequency 5

            Assert.Equal(4, TabBadges.CountForTab(unseen, TabOf("all")));
            Assert.Equal(1, TabBadges.CountForTab(unseen, TabOf("memory")));
            Assert.Equal(2, TabBadges.CountForTab(unseen, TabOf("cache")));
            Assert.Equal(1, TabBadges.CountForTab(unseen, TabOf("frequency")));
            Assert.Equal(0, TabBadges.CountForTab(unseen, TabOf("wafer")));
            Assert.Equal(4, TabBadges.CountForTab(unseen, null), "no tab shows everything");
            Assert.Equal(0, TabBadges.CountForTab(null, TabOf("all")));
        }

        [Test]
        public void CountByTab_GivesEveryTab_ZeroIncluded()
        {
            var tracker = new NewTechTracker();
            var unseen = new List<TechSnapshot>();
            tracker.CollectUnseen(Sample(), unseen);

            Dictionary<string, int> counts = TabBadges.CountByTab(unseen, TabCatalog.Defaults());

            Assert.Equal(8, counts.Count);
            Assert.Equal(4, counts["all"]);
            Assert.Equal(2, counts["cache"]);
            Assert.Equal(0, counts["multicore"]);
            Assert.Equal(0, TabBadges.CountByTab(unseen, null).Count);
        }

        [Test]
        public void AnAtypicalType_CountsOnTabAllOnly()
        {
            var unseen = new List<TechSnapshot> { Tech(1, 9) };

            Assert.Equal(1, TabBadges.CountForTab(unseen, TabOf("all")));
            Assert.Equal(0, TabBadges.CountForTab(unseen, TabOf("memory")));
        }

        [Test]
        public void IdsOfTab_AreWhatLeavingTheTabMarksAsSeen()
        {
            var tracker = new NewTechTracker();
            var unseen = new List<TechSnapshot>();
            tracker.CollectUnseen(Sample(), unseen);
            var ids = new List<int> { 99 };

            // AC-3.2: leaving "Cache" - the cache marks go, the others stay.
            Assert.Equal(2, TabBadges.IdsOfTab(unseen, TabOf("cache"), ids));
            tracker.MarkSeen(ids);

            Assert.Equal(new[] { 3, 4 }, ids.ToArray());
            Assert.Equal(new[] { 1, 5 }, tracker.Unseen(Sample()).ToArray());
            Assert.Equal(0, TabBadges.CountForTab(unseen.Where(t => tracker.IsNew(t)).ToList(), TabOf("cache")), "the counter of the tab is gone");
        }

        [Test]
        public void IdsOfTab_LeavingTabAll_MarksEverythingNew()
        {
            var tracker = new NewTechTracker();
            var unseen = new List<TechSnapshot>();
            tracker.CollectUnseen(Sample(), unseen);
            var ids = new List<int>();

            TabBadges.IdsOfTab(unseen, TabOf("all"), ids);
            tracker.MarkSeen(ids);

            Assert.Equal(0, tracker.Unseen(Sample()).Count);
            Assert.Throws<System.ArgumentNullException>(() => TabBadges.IdsOfTab(unseen, TabOf("all"), null));
            Assert.Equal(0, TabBadges.IdsOfTab(null, TabOf("all"), ids));
        }

        // ---- the caption ----

        private static string Loc(string key, object[] args)
        {
            return key == "rtux.tab.badge" ? string.Format("{0} ({1})", args) : "[" + key + "]";
        }

        [Test]
        public void Caption_ShowsTheCounterOnlyWhenThereIsSomethingNew()
        {
            Assert.Equal("Память", TabBadges.Caption("Память", 0, Loc));
            Assert.Equal("Память (2)", TabBadges.Caption("Память", 2, Loc));
            Assert.Equal("Память", TabBadges.Caption("Память", -1, Loc));
        }

        [Test]
        public void Caption_WithoutALookupOrATitle_IsStillSafe()
        {
            Assert.Equal("Кэш", TabBadges.Caption("Кэш", 3, null));
            Assert.Equal(string.Empty, TabBadges.Caption(null, 0, Loc));
            Assert.Equal(" (1)", TabBadges.Caption(null, 1, Loc));
            Assert.Equal("rtux.tab.badge", TabBadges.CaptionKey);
        }
    }
}
