using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Ui;

namespace PTMod.Tests.Ui
{
    /// <summary>
    /// The hover rules of an element tooltip (FR-16 item 9, TSK-064, AC-16.4) without Unity: the provider runs on enter, the game's tooltip is asked for once
    /// and cancelled only if it was asked for; a scene without tooltips and a failing provider cost no exception here.
    /// </summary>
    [Covers(typeof(TooltipHover))]
    public class TooltipHoverTests
    {
        private sealed class FakeHost : ITooltipHost
        {
            public bool Available = true;
            public bool ThrowOnStart;
            public bool ThrowOnCancel;
            public readonly List<string> Calls = new List<string>();

            public bool Start(string header, string content)
            {
                Calls.Add("start:" + header + "|" + content);
                if (ThrowOnStart) throw new InvalidOperationException("start");
                return Available;
            }

            public void Cancel()
            {
                Calls.Add("cancel");
                if (ThrowOnCancel) throw new InvalidOperationException("cancel");
            }
        }

        private static TooltipTextSource Fixed(string header, string content)
        {
            return (out string h, out string c) =>
            {
                h = header;
                c = content;
                return true;
            };
        }

        [Test]
        public void Enter_ReadsTheText_AndAsksForTheTooltipOnce()
        {
            var host = new FakeHost();
            var hover = new TooltipHover(host, Fixed("Head", "Body"));

            hover.PointerEntered();

            Assert.True(hover.Showing);
            Assert.Equal(new List<string> { "start:Head|Body" }, host.Calls);
        }

        [Test]
        public void Enter_WhileShowing_DoesNotAskAgain_AndDoesNotCallTheProvider()
        {
            var host = new FakeHost();
            int reads = 0;
            var hover = new TooltipHover(host, (out string h, out string c) => { reads++; h = "h"; c = "c"; return true; });

            hover.PointerEntered();
            hover.PointerEntered();

            Assert.Equal(1, reads);
            Assert.Equal(1, host.Calls.Count);
        }

        [Test]
        public void TheProvider_RunsOnEnter_NotBefore_AndOnEveryNewEnter()
        {
            var host = new FakeHost();
            int reads = 0;
            var hover = new TooltipHover(host, (out string h, out string c) => { reads++; h = ""; c = "n=" + reads; return true; });

            Assert.Equal(0, reads, "nothing is read before the pointer comes");
            hover.PointerEntered();
            hover.PointerExited();
            hover.PointerEntered();

            Assert.Equal(2, reads);
            Assert.Equal(new List<string> { "start:|n=1", "cancel", "start:|n=2" }, host.Calls, "each hover shows the text of its moment");
        }

        [Test]
        public void Exit_CancelsWhatWasAskedFor_Once()
        {
            var host = new FakeHost();
            var hover = new TooltipHover(host, Fixed("h", "c"));

            hover.PointerEntered();
            hover.PointerExited();
            hover.PointerExited();

            Assert.False(hover.Showing);
            Assert.Equal(new List<string> { "start:h|c", "cancel" }, host.Calls);
        }

        [Test]
        public void Exit_WithoutEnter_DoesNotReachTheGame()
        {
            var host = new FakeHost();
            var hover = new TooltipHover(host, Fixed("h", "c"));

            hover.PointerExited();

            Assert.Equal(0, host.Calls.Count, "the game's CancelTrigger throws when nothing was started");
        }

        [Test]
        public void NoTooltipInTheScene_NothingIsShown_AndLeavingDoesNotCancel()
        {
            // TooltipHandler.Instance == null (the main menu): the host answers false.
            var host = new FakeHost { Available = false };
            var hover = new TooltipHover(host, Fixed("h", "c"));

            hover.PointerEntered();
            Assert.False(hover.Showing);
            hover.PointerExited();

            Assert.Equal(new List<string> { "start:h|c" }, host.Calls, "asked once, never cancelled");
        }

        [Test]
        public void NoTooltipInTheScene_TheNextEnterTries_Again()
        {
            var host = new FakeHost { Available = false };
            var hover = new TooltipHover(host, Fixed("h", "c"));

            hover.PointerEntered();
            host.Available = true;
            hover.PointerEntered();

            Assert.True(hover.Showing);
            Assert.Equal(2, host.Calls.Count);
        }

        [Test]
        public void ASourceThatHasNothing_ShowsNothing()
        {
            var host = new FakeHost();
            var hover = new TooltipHover(host, (out string h, out string c) => { h = "x"; c = "y"; return false; });

            hover.PointerEntered();

            Assert.False(hover.Showing);
            Assert.Equal(0, host.Calls.Count);
        }

        [Test]
        public void EmptyAndNullTexts_ShowNothing_AndNullBecomesEmpty()
        {
            var host = new FakeHost();
            new TooltipHover(host, Fixed(null, null)).PointerEntered();
            new TooltipHover(host, Fixed("", "")).PointerEntered();
            Assert.Equal(0, host.Calls.Count);

            new TooltipHover(host, Fixed(null, "only body")).PointerEntered();
            new TooltipHover(host, Fixed("only header", null)).PointerEntered();
            Assert.Equal(new List<string> { "start:|only body", "start:only header|" }, host.Calls, "the game's Tooltip.SetText dereferences the content, so null is never passed on");
        }

        [Test]
        public void AFailingProvider_Propagates_AndLeavesNoTooltipShowing()
        {
            var host = new FakeHost();
            var hover = new TooltipHover(host, (out string h, out string c) => { throw new InvalidOperationException("provider"); });

            Assert.Throws<InvalidOperationException>(() => hover.PointerEntered());

            Assert.False(hover.Showing);
            hover.PointerExited();
            Assert.Equal(0, host.Calls.Count);
        }

        [Test]
        public void AFailingStart_Propagates_AndLeavesNoTooltipShowing()
        {
            var host = new FakeHost { ThrowOnStart = true };
            var hover = new TooltipHover(host, Fixed("h", "c"));

            Assert.Throws<InvalidOperationException>(() => hover.PointerEntered());

            Assert.False(hover.Showing);
            host.ThrowOnStart = false;
            hover.PointerEntered();
            Assert.True(hover.Showing, "the next hover can try again");
        }

        [Test]
        public void AFailingCancel_StillEndsTheShowing()
        {
            var host = new FakeHost { ThrowOnCancel = true };
            var hover = new TooltipHover(host, Fixed("h", "c"));
            hover.PointerEntered();

            Assert.Throws<InvalidOperationException>(() => hover.PointerExited());

            Assert.False(hover.Showing);
            hover.PointerExited();
            Assert.Equal(2, host.Calls.Count, "no second cancel for the same tooltip");
        }

        [Test]
        public void Constructor_RefusesNulls()
        {
            Assert.Throws<ArgumentNullException>(() => new TooltipHover(null, Fixed("h", "c")));
            Assert.Throws<ArgumentNullException>(() => new TooltipHover(new FakeHost(), null));
        }
    }
}
