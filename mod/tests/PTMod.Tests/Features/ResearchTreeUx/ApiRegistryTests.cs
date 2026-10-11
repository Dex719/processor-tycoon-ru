using System;
using System.Collections.Generic;
using System.Linq;
using PTMod.Features.ResearchTreeUx;

namespace PTMod.Tests.Features.ResearchTreeUx
{
    /// <summary>
    /// The rules of the registry behind <c>ResearchTreeUxApi</c> (TSK-014, FR-7), on the books that hold the providers and the guarded call into them:
    /// a registration before or after the window is built, a duplicate tab id, a provider that throws (switched off and reported on the feature that
    /// owns its code, the others untouched, AC-7.2), a provider of a feature that is switched off (not asked, asked again when it is switched on,
    /// AC-7.3) and the sources of lab occupancy (AC-2.4, AC-9.2). The types of the API itself touch the game and the engine, so the books are tried
    /// with plain stand-ins; the wiring of the real registry is checked by <see cref="ApiWiringSourceTests"/>.
    /// </summary>
    [Covers(typeof(ProviderBook<>), typeof(ProviderEntry<>), typeof(AddOutcome), typeof(ProviderCalls), typeof(LabOccupancy), typeof(LabState), typeof(TabMerge))]
    public class ApiRegistryTests
    {
        private const string ThisFeature = "research-tree-ux";
        private const string ArchFeature = "cpu-architectures";

        // A stand-in for a provider: what it answers, and whether it throws.
        private sealed class Fake
        {
            public string Name;
            public bool Busy;
            public string Text;
            public bool ThrowOnBusy;
            public bool ThrowOnText;
            public int BusyCalls;
            public int TextCalls;
        }

        private sealed class Fault
        {
            public string Owner;
            public string Site;
            public Exception Error;
        }

        private static ProviderBook<Fake> Book(params string[] reserved)
        {
            return new ProviderBook<Fake>(reserved);
        }

        private static Fake F(string name)
        {
            return new Fake { Name = name };
        }

        private static Action<string, string, Exception> Recorder(List<Fault> faults)
        {
            return (owner, site, error) => faults.Add(new Fault { Owner = owner, Site = site, Error = error });
        }

        private static List<string> Names(ProviderBook<Fake> book, Func<string, bool> ownerActive = null)
        {
            var entries = new List<ProviderEntry<Fake>>();
            book.Collect(ownerActive, entries);
            return entries.Select(e => e.Provider.Name).ToList();
        }

        private static TabDefinition Tab(string id, int order)
        {
            return new TabDefinition(id, "rtux.tab." + id, null, order);
        }

        // ---- registering ----

        [Test]
        public void ARegistration_IsKept_BeforeAndAfterTheWindowIsBuilt()
        {
            ProviderBook<Fake> book = Book();
            Fake early = F("early");
            Assert.Equal(AddOutcome.Added, book.Add(early, "early", "t.early", 200, ArchFeature));

            // The window is built here: the strip asks the book. A tab registered afterwards is found by the next ask (FR-7 item 1).
            Assert.Equal(new[] { "early" }, Names(book).ToArray());

            Fake late = F("late");
            Assert.Equal(AddOutcome.Added, book.Add(late, "late", "t.late", 210, "rival-acquisitions"));
            Assert.Equal(new[] { "early", "late" }, Names(book).ToArray());
            Assert.Equal(2, book.Count);
        }

        [Test]
        public void TheOrder_IsTheOrderOfTheProviders_AndTiesKeepTheOrderOfRegistration()
        {
            ProviderBook<Fake> book = Book();
            book.Add(F("c"), "c", null, 300, ArchFeature);
            book.Add(F("a1"), "a1", null, 100, ArchFeature);
            book.Add(F("b"), "b", null, 200, ArchFeature);
            book.Add(F("a2"), "a2", null, 100, ArchFeature);

            Assert.Equal(new[] { "a1", "a2", "b", "c" }, Names(book).ToArray());
        }

        [Test]
        public void ADuplicateTabId_IsRefused_AndTheFirstOneStays()
        {
            ProviderBook<Fake> book = Book();
            Fake first = F("first");
            Fake second = F("second");

            Assert.Equal(AddOutcome.Added, book.Add(first, "cpu-architectures", "t", 200, ArchFeature));
            Assert.Equal(AddOutcome.KeyTaken, book.Add(second, "cpu-architectures", "t", 210, "other-feature"));

            Assert.Equal(1, book.Count);
            Assert.True(ReferenceEquals(first, book.EntryByKey("cpu-architectures").Provider), "the first registration is the one in force");
            Assert.Null(book.EntryOf(second));
        }

        [Test]
        public void ABuiltInTabId_IsNeverTaken()
        {
            ProviderBook<Fake> book = Book("all", "package", "multicore");

            Assert.Equal(AddOutcome.KeyTaken, book.Add(F("x"), "all", "t", 200, ArchFeature));
            Assert.Equal(AddOutcome.KeyTaken, book.Add(F("y"), "multicore", "t", 200, ArchFeature));
            Assert.Equal(AddOutcome.Added, book.Add(F("z"), "mine", "t", 200, ArchFeature));
            Assert.Equal(1, book.Count);
        }

        [Test]
        public void TheKey_IsComparedExactly()
        {
            ProviderBook<Fake> book = Book("all");

            Assert.Equal(AddOutcome.Added, book.Add(F("upper"), "All", "t", 1, ArchFeature), "the saved last tab is compared exactly too");
            Assert.Equal(AddOutcome.KeyTaken, book.Add(F("again"), "All", "t", 2, ArchFeature));
        }

        [Test]
        public void ASwitchedOffProvider_StillHoldsItsKey()
        {
            ProviderBook<Fake> book = Book();
            Fake first = F("first");
            book.Add(first, "tab", "t", 1, ArchFeature);
            book.Disable(first);

            Assert.Equal(AddOutcome.KeyTaken, book.Add(F("second"), "tab", "t", 1, ArchFeature), "a tab does not come back under another object until the game restarts");
        }

        [Test]
        public void TheSameObject_IsAddedOnce()
        {
            ProviderBook<Fake> book = Book();
            Fake source = F("source");

            Assert.Equal(AddOutcome.Added, book.Add(source, null, null, 0, ArchFeature));
            Assert.Equal(AddOutcome.AlreadyRegistered, book.Add(source, null, null, 0, ArchFeature));
            Assert.Equal(1, book.Count);
        }

        [Test]
        public void ProvidersWithoutAKey_NeverCollide()
        {
            ProviderBook<Fake> book = Book();

            Assert.Equal(AddOutcome.Added, book.Add(F("a"), null, null, 0, ArchFeature));
            Assert.Equal(AddOutcome.Added, book.Add(F("b"), null, null, 0, ArchFeature));
            Assert.Equal(2, book.Count);
        }

        [Test]
        public void NothingAndAnEmptyKey_AreInvalid()
        {
            ProviderBook<Fake> book = Book();

            Assert.Equal(AddOutcome.Invalid, book.Add(null, "tab", "t", 1, ArchFeature));
            Assert.Equal(AddOutcome.Invalid, book.Add(F("x"), string.Empty, "t", 1, ArchFeature));
            Assert.Equal(0, book.Count);
            Assert.Null(book.EntryOf(null));
            Assert.Null(book.EntryByKey(null));
        }

        [Test]
        public void TheBook_HoldsAtMostSixtyFourProviders()
        {
            ProviderBook<Fake> book = Book();
            for (int i = 0; i < ProviderBook<Fake>.MaxEntries; i++) Assert.Equal(AddOutcome.Added, book.Add(F("p" + i), null, null, i, ArchFeature));

            Assert.Equal(AddOutcome.Full, book.Add(F("one too many"), null, null, 0, ArchFeature));
            Assert.Equal(ProviderBook<Fake>.MaxEntries, book.Count);
        }

        [Test]
        public void TheEntry_RemembersWhatWasReadOnce()
        {
            ProviderBook<Fake> book = Book();
            Fake tab = F("tab");
            book.Add(tab, "cpu-architectures", "arch.tab.title", 200, ArchFeature);

            ProviderEntry<Fake> entry = book.EntryByKey("cpu-architectures");

            Assert.Equal("arch.tab.title", entry.Caption);
            Assert.Equal(200, entry.Order);
            Assert.Equal(ArchFeature, entry.Owner);
            Assert.False(entry.Disabled);
            Assert.True(entry.ToString().Contains("cpu-architectures"), entry.ToString());
        }

        // ---- the feature of the provider is switched off or on (FR-7 items 5 and 6) ----

        [Test]
        public void AProviderOfASwitchedOffFeature_IsNotAsked_AndIsAskedAgainWhenTheFeatureIsSwitchedOn()
        {
            ProviderBook<Fake> book = Book();
            book.Add(F("arch"), "arch", "t", 200, ArchFeature);
            book.Add(F("mine"), "mine", "t", 100, ThisFeature);
            bool archOn = false;
            Func<string, bool> active = owner => owner != ArchFeature || archOn;

            Assert.Equal(new[] { "mine" }, Names(book, active).ToArray());   // AC-7.3: no tab while the feature is off

            archOn = true;
            Assert.Equal(new[] { "mine", "arch" }, Names(book, active).ToArray());   // and it comes back without a reload; nothing was lost
        }

        [Test]
        public void TheRegistrationsMade_WhileTheFeatureIsOff_AreKept()
        {
            ProviderBook<Fake> book = Book();
            bool anyOn = false;
            Func<string, bool> active = owner => anyOn;

            book.Add(F("early"), "early", "t", 1, ArchFeature);
            Assert.Equal(0, Names(book, active).Count);
            Assert.Equal(1, book.Count);

            anyOn = true;
            Assert.Equal(new[] { "early" }, Names(book, active).ToArray());
        }

        // ---- the call into a provider (FR-7 item 3, AC-7.2, D11) ----

        [Test]
        public void AProviderThatThrows_IsSwitchedOff_AndReportedOnTheFeatureThatOwnsIt()
        {
            ProviderBook<Fake> book = Book();
            Fake bad = F("bad");
            Fake good = F("good");
            book.Add(bad, "bad", null, 1, ArchFeature);
            book.Add(good, "good", null, 2, "rival-acquisitions");
            var faults = new List<Fault>();
            var boom = new InvalidOperationException("boom");

            int fromBad = ProviderCalls.Run(book, book.EntryOf(bad), "ICompareRowProvider.GetRows bad", p => { throw boom; }, -1, Recorder(faults));
            int fromGood = ProviderCalls.Run(book, book.EntryOf(good), "ICompareRowProvider.GetRows good", p => 7, -1, Recorder(faults));

            Assert.Equal(-1, fromBad, "the fallback");
            Assert.Equal(7, fromGood, "the other provider is not affected");
            Assert.Equal(1, faults.Count, "one record");
            Assert.Equal(ArchFeature, faults[0].Owner);   // the owner of the provider, not research-tree-ux
            Assert.True(faults[0].Site.Contains("bad"), "the site names the provider: " + faults[0].Site);
            Assert.True(ReferenceEquals(boom, faults[0].Error), "the exception itself");
            Assert.True(book.EntryOf(bad).Disabled, "off until the game restarts");
            Assert.False(book.EntryOf(good).Disabled);
            Assert.Equal(new[] { "good" }, Names(book).ToArray());
        }

        [Test]
        public void ASwitchedOffProvider_IsNotCalledAgain_AndNothingIsReportedTwice()
        {
            ProviderBook<Fake> book = Book();
            Fake bad = F("bad");
            book.Add(bad, "bad", null, 1, ArchFeature);
            var faults = new List<Fault>();
            int calls = 0;

            for (int i = 0; i < 5; i++)
            {
                ProviderCalls.Run(book, book.EntryOf(bad), "site", p => { calls++; throw new InvalidOperationException(); }, 0, Recorder(faults));
            }

            Assert.Equal(1, calls, "the provider was asked once");
            Assert.Equal(1, faults.Count, "and counted once (AC-7.2: one record)");
        }

        [Test]
        public void ACallThatReturnsNothing_IsGuardedToo()
        {
            ProviderBook<Fake> book = Book();
            Fake tab = F("tab");
            book.Add(tab, "tab", null, 1, ArchFeature);
            var faults = new List<Fault>();

            bool ran = ProviderCalls.Run(book, book.EntryOf(tab), "OnShown", p => p.Name.ToString(), Recorder(faults));
            Assert.True(ran);

            bool failed = ProviderCalls.Run(book, book.EntryOf(tab), "OnHidden", p => { throw new NullReferenceException(); }, Recorder(faults));
            Assert.False(failed);
            Assert.Equal(1, faults.Count);
            Assert.True(book.EntryOf(tab).Disabled);
        }

        [Test]
        public void AFailure_NotAnException_CanSwitchTheProviderOffToo()
        {
            ProviderBook<Fake> book = Book();
            Fake tab = F("tab");
            book.Add(tab, "tab", null, 1, ArchFeature);
            var faults = new List<Fault>();

            Assert.True(ProviderCalls.Fail(book, book.EntryOf(tab), "BuildContent", new InvalidOperationException("no root"), Recorder(faults)));
            Assert.False(ProviderCalls.Fail(book, book.EntryOf(tab), "BuildContent", new InvalidOperationException("no root"), Recorder(faults)), "the second time is not reported");
            Assert.Equal(1, faults.Count);
            Assert.Equal(ArchFeature, faults[0].Owner);
        }

        [Test]
        public void AReporterThatThrows_DoesNotStopTheCaller()
        {
            ProviderBook<Fake> book = Book();
            Fake bad = F("bad");
            book.Add(bad, "bad", null, 1, ArchFeature);

            int result = ProviderCalls.Run(book, book.EntryOf(bad), "site", p => { throw new InvalidOperationException(); }, 5,
                (owner, site, error) => { throw new ApplicationException("the counter failed"); });

            Assert.Equal(5, result);
            Assert.True(book.EntryOf(bad).Disabled);
        }

        [Test]
        public void NothingToCall_GivesTheFallback_WithoutAnException()
        {
            ProviderBook<Fake> book = Book();

            Assert.Equal(9, ProviderCalls.Run<Fake, int>(book, null, "site", p => 1, 9, null));
            Assert.Equal(9, ProviderCalls.Run<Fake, int>(null, null, "site", p => 1, 9, null));
            Assert.False(ProviderCalls.Run<Fake>(book, null, "site", p => { }, null));
            Assert.False(ProviderCalls.Fail(book, null, "site", new Exception(), null));
        }

        [Test]
        public void Materialize_CatchesAnIteratorThatThrowsInsideTheGuardedCall()
        {
            ProviderBook<Fake> book = Book();
            Fake bad = F("bad");
            book.Add(bad, "bad", null, 1, ArchFeature);
            var faults = new List<Fault>();

            List<string> rows = ProviderCalls.Run(book, book.EntryOf(bad), "GetRows", p => ProviderCalls.Materialize(Walk(), 10), (List<string>)null, Recorder(faults));

            Assert.Null(rows, "a lazily walked sequence fails here, under the guard, not later in the table");
            Assert.Equal(1, faults.Count);
            Assert.Equal(ArchFeature, faults[0].Owner);
        }

        private static IEnumerable<string> Walk()
        {
            yield return "first";
            throw new InvalidOperationException("the second item cannot be made");
        }

        [Test]
        public void Materialize_SkipsNothings_AndKeepsAtMostTheLimit()
        {
            var items = new[] { "a", null, "b", "c", "d" };

            Assert.Equal(new[] { "a", "b", "c" }, ProviderCalls.Materialize(items, 3).ToArray());
            Assert.Equal(0, ProviderCalls.Materialize((IEnumerable<string>)null, 3).Count);
            Assert.Equal(0, ProviderCalls.Materialize(items, 0).Count);
        }

        // ---- the lab (FR-7 item 7, AC-2.4, AC-9.2) ----

        private static LabState Poll(ProviderBook<Fake> book, bool withText, List<Fault> faults, Func<string, bool> active = null)
        {
            return LabOccupancy.Poll(book, active,
                s => { s.BusyCalls++; if (s.ThrowOnBusy) throw new InvalidOperationException("IsLabBusy"); return s.Busy; },
                withText ? (Func<Fake, string>)(s => { s.TextCalls++; if (s.ThrowOnText) throw new InvalidOperationException("BusyText"); return s.Text; }) : null,
                Recorder(faults));
        }

        [Test]
        public void WithoutSources_TheLabIsFree()
        {
            Assert.False(Poll(Book(), true, new List<Fault>()).Busy);
            Assert.False(LabOccupancy.Poll<Fake>(null, null, s => true, s => "x", null).Busy);
            Assert.False(LabOccupancy.Poll(Book(), null, null, null, null).Busy);
        }

        [Test]
        public void ABusySource_MakesTheLabBusy_WithItsText()
        {
            ProviderBook<Fake> book = Book();
            Fake arch = new Fake { Name = "arch", Busy = true, Text = "x86 architecture - 43 %" };
            book.Add(arch, null, null, 0, ArchFeature);

            LabState state = Poll(book, true, new List<Fault>());

            Assert.True(state.Busy);
            Assert.Equal("x86 architecture - 43 %", state.Text);
        }

        [Test]
        public void TheText_IsAskedOnlyWhenTheLabIsBusyAndTheTextIsWanted()
        {
            ProviderBook<Fake> book = Book();
            Fake idle = new Fake { Name = "idle", Busy = false, Text = "never" };
            Fake busy = new Fake { Name = "busy", Busy = true, Text = "now" };
            book.Add(idle, null, null, 0, ArchFeature);
            book.Add(busy, null, null, 1, ArchFeature);

            LabState yesNo = Poll(book, false, new List<Fault>());   // the reminder of an idle lab wants no text
            Assert.True(yesNo.Busy);
            Assert.Null(yesNo.Text);
            Assert.Equal(0, idle.TextCalls + busy.TextCalls, "no text was asked");

            Poll(book, true, new List<Fault>());
            Assert.Equal(0, idle.TextCalls, "a free source is not asked for its text");
            Assert.Equal(1, busy.TextCalls);
        }

        [Test]
        public void TheFirstBusySource_Decides_AndTheRestAreNotAsked()
        {
            ProviderBook<Fake> book = Book();
            Fake first = new Fake { Name = "first", Busy = true, Text = "one" };
            Fake second = new Fake { Name = "second", Busy = true, Text = "two" };
            book.Add(first, null, null, 0, ArchFeature);
            book.Add(second, null, null, 1, ArchFeature);

            Assert.Equal("one", Poll(book, true, new List<Fault>()).Text);
            Assert.Equal(0, second.BusyCalls);
        }

        [Test]
        public void AFreeSource_LeavesTheLabFree()
        {
            ProviderBook<Fake> book = Book();
            book.Add(new Fake { Name = "arch", Busy = false, Text = "unused" }, null, null, 0, ArchFeature);

            LabState state = Poll(book, true, new List<Fault>());

            Assert.False(state.Busy);
            Assert.Null(state.Text);
        }

        [Test]
        public void ASourceThatThrows_IsSwitchedOff_AndTheNextSourceIsStillAsked()
        {
            ProviderBook<Fake> book = Book();
            Fake bad = new Fake { Name = "bad", ThrowOnBusy = true };
            Fake good = new Fake { Name = "good", Busy = true, Text = "fine" };
            book.Add(bad, null, null, 0, ArchFeature);
            book.Add(good, null, null, 1, "rival-acquisitions");
            var faults = new List<Fault>();

            LabState state = Poll(book, true, faults);

            Assert.True(state.Busy, "one broken source never hides the state of the others");
            Assert.Equal("fine", state.Text);
            Assert.Equal(1, faults.Count);
            Assert.Equal(ArchFeature, faults[0].Owner);
            Assert.True(faults[0].Site.Contains("IsLabBusy"), faults[0].Site);

            Poll(book, true, faults);
            Assert.Equal(1, bad.BusyCalls, "off until the game restarts");
            Assert.Equal(1, faults.Count);
        }

        [Test]
        public void ASourceWhoseTextThrows_IsStillBusy_ButHasNoText_AndIsSwitchedOff()
        {
            ProviderBook<Fake> book = Book();
            Fake source = new Fake { Name = "source", Busy = true, ThrowOnText = true };
            book.Add(source, null, null, 0, ArchFeature);
            var faults = new List<Fault>();

            LabState state = Poll(book, true, faults);

            Assert.True(state.Busy);
            Assert.Null(state.Text);
            Assert.Equal(1, faults.Count);
            Assert.True(faults[0].Site.Contains("BusyText"), faults[0].Site);
            Assert.False(Poll(book, true, faults).Busy, "the source is off now");
        }

        [Test]
        public void ABlankText_IsNoText()
        {
            ProviderBook<Fake> book = Book();
            book.Add(new Fake { Name = "s", Busy = true, Text = "   " }, null, null, 0, ArchFeature);

            LabState state = Poll(book, true, new List<Fault>());

            Assert.True(state.Busy);
            Assert.Null(state.Text);
        }

        [Test]
        public void ALabBusyForAFeatureThatIsOff_IsNotBusy()
        {
            // AC-9.2 needs the architecture feature to be on; with it off its research is not going on and nobody is asked.
            ProviderBook<Fake> book = Book();
            Fake arch = new Fake { Name = "arch", Busy = true, Text = "x86" };
            book.Add(arch, null, null, 0, ArchFeature);

            Assert.False(Poll(book, true, new List<Fault>(), owner => owner != ArchFeature).Busy);
            Assert.Equal(0, arch.BusyCalls, "a source of a feature that is off is not asked");
            Assert.True(Poll(book, true, new List<Fault>(), owner => true).Busy);
        }

        // ---- the tabs of the strip (FR-7 items 1 and 5, AC-7.1) ----

        [Test]
        public void TheTabOfAnotherFeature_StandsAfterTheBuiltInOnes_AfterCores()
        {
            IReadOnlyList<TabDefinition> builtIn = TabCatalog.Defaults();
            var external = new List<TabDefinition> { Tab("cpu-architectures", 200) };

            List<TabDefinition> tabs = TabMerge.Combine(builtIn, external);

            Assert.Equal(builtIn.Count + 1, tabs.Count);
            Assert.Equal("multicore", tabs[tabs.Count - 2].Id);   // "Cores" is the last built-in tab
            Assert.Equal("cpu-architectures", tabs[tabs.Count - 1].Id);
            Assert.Equal("all", tabs[0].Id);
        }

        [Test]
        public void TheDebugTab_StandsBetweenCoresAndTheArchitectures()
        {
            var external = new List<TabDefinition> { Tab("cpu-architectures", 200), Tab("rtux-debug", 100) };

            List<string> ids = TabMerge.Combine(TabCatalog.Defaults(), external).Select(t => t.Id).ToList();

            Assert.Equal(new[] { "multicore", "rtux-debug", "cpu-architectures" }, ids.Skip(ids.Count - 3).ToArray());
        }

        [Test]
        public void AnExternalTab_OfTheSameOrderAsABuiltInOne_GoesAfterIt()
        {
            var external = new List<TabDefinition> { Tab("mine", 10) };   // the order of "package"

            List<string> ids = TabMerge.Combine(TabCatalog.Defaults(), external).Select(t => t.Id).ToList();

            Assert.Equal(ids.IndexOf("package") + 1, ids.IndexOf("mine"));
        }

        [Test]
        public void ExternalTabsOfTheSameOrder_KeepTheOrderTheyWereGivenIn()
        {
            var external = new List<TabDefinition> { Tab("b", 200), Tab("a", 200), Tab("c", 200) };

            List<string> ids = TabMerge.Combine(TabCatalog.Defaults(), external).Select(t => t.Id).ToList();

            Assert.Equal(new[] { "b", "a", "c" }, ids.Skip(ids.Count - 3).ToArray());
        }

        [Test]
        public void AnExternalTab_ThatRepeatsAnIdOrLacksOne_IsLeftOut()
        {
            var external = new List<TabDefinition>
            {
                Tab("cache", 5),                                   // a built-in id
                Tab("dup", 200),
                Tab("dup", 201),                                   // repeats an earlier tab of another feature
                new TabDefinition(null, "rtux.tab.x", null, 1),    // no id
                new TabDefinition("notitle", null, null, 1),       // no caption
                null,
            };

            List<TabDefinition> tabs = TabMerge.Combine(TabCatalog.Defaults(), external);

            Assert.Equal(TabCatalog.Defaults().Count + 1, tabs.Count);
            Assert.Equal(1, tabs.Count(t => t.Id == "dup"));
            Assert.Equal(200, tabs.First(t => t.Id == "dup").Order, "the first one wins");
            Assert.Equal(1, tabs.Count(t => t.Id == "cache"));
            Assert.Equal(50, tabs.First(t => t.Id == "cache").Order, "the built-in tab is the one that stays");
        }

        [Test]
        public void TheStrip_HoldsAtMostThirtyTwoTabs_TheBuiltInOnesFirst()
        {
            var external = new List<TabDefinition>();
            for (int i = 0; i < 50; i++) external.Add(Tab("t" + i, 200 + i));

            List<TabDefinition> tabs = TabMerge.Combine(TabCatalog.Defaults(), external);

            Assert.Equal(TabMerge.MaxTabs, tabs.Count);
            foreach (TabDefinition builtIn in TabCatalog.Defaults()) Assert.True(tabs.Any(t => t.Id == builtIn.Id), "built-in " + builtIn.Id + " stays");
        }

        [Test]
        public void NoListsAtAll_GiveNoTabs_AndOneListGivesItself()
        {
            Assert.Equal(0, TabMerge.Combine(null, null).Count);
            Assert.Equal(TabCatalog.Defaults().Count, TabMerge.Combine(TabCatalog.Defaults(), null).Count);
            Assert.Equal(1, TabMerge.Combine(null, new List<TabDefinition> { Tab("only", 5) }).Count);
        }

        [Test]
        public void SameTabs_SeesAChangeInTheIdsTheCaptionsAndTheOrder()
        {
            IReadOnlyList<TabDefinition> a = TabCatalog.Defaults();
            List<TabDefinition> b = TabMerge.Combine(TabCatalog.Defaults(), null);

            Assert.True(TabMerge.SameTabs(a, b), "equal by content, not by reference");
            Assert.True(TabMerge.SameTabs(null, null));
            Assert.True(TabMerge.SameTabs(a, a));

            List<TabDefinition> more = TabMerge.Combine(TabCatalog.Defaults(), new List<TabDefinition> { Tab("extra", 200) });
            Assert.False(TabMerge.SameTabs(a, more), "a tab came");

            List<TabDefinition> renamed = TabMerge.Combine(TabCatalog.Defaults(), null);
            renamed[1] = new TabDefinition(renamed[1].Id, "rtux.tab.other", null, renamed[1].Order);
            Assert.False(TabMerge.SameTabs(a, renamed), "a caption changed");

            List<TabDefinition> swapped = TabMerge.Combine(TabCatalog.Defaults(), null);
            TabDefinition first = swapped[1];
            swapped[1] = swapped[2];
            swapped[2] = first;
            Assert.False(TabMerge.SameTabs(a, swapped), "the order changed");
        }

        [Test]
        public void Find_ReturnsTheTabWithTheId()
        {
            IReadOnlyList<TabDefinition> tabs = TabCatalog.Defaults();

            Assert.Equal("cache", TabMerge.Find(tabs, "cache").Id);
            Assert.Null(TabMerge.Find(tabs, "nope"));
            Assert.Null(TabMerge.Find(tabs, null));
            Assert.Null(TabMerge.Find(null, "cache"));
        }
    }
}
