using System;
using System.Collections.Generic;
using PTMod.Core.Logic.Ui;

namespace PTMod.Tests.Ui
{
    /// <summary>
    /// The rules of the confirmation dialog (FR-16 item 8, TSK-064, AC-16.3) without Unity: at most one open question, the others wait in order, and every
    /// question is handed over for an answer exactly once whichever way out asks first.
    /// </summary>
    [Covers(typeof(ConfirmFlow))]
    public class ConfirmFlowTests
    {
        private static ConfirmRequest Ask(string message, Action onYes = null, Action onNo = null)
        {
            return new ConfirmRequest("title", message, "core.common.yes", "core.common.no", onYes, onNo);
        }

        [Test]
        public void Enqueue_OnAnIdleFlow_OpensTheQuestion()
        {
            var flow = new ConfirmFlow();
            ConfirmRequest a = Ask("a");

            Assert.Equal(ConfirmArrival.ShowNow, flow.Enqueue(a));
            Assert.True(ReferenceEquals(a, flow.Current));
            Assert.Equal(0, flow.Waiting);
        }

        [Test]
        public void Enqueue_WhileOneIsOpen_QueuesInOrder_AndNeverOpensASecondWindow()
        {
            var flow = new ConfirmFlow();
            ConfirmRequest a = Ask("a"), b = Ask("b"), c = Ask("c");

            Assert.Equal(ConfirmArrival.ShowNow, flow.Enqueue(a));
            Assert.Equal(ConfirmArrival.Queued, flow.Enqueue(b));
            Assert.Equal(ConfirmArrival.Queued, flow.Enqueue(c));
            Assert.True(ReferenceEquals(a, flow.Current), "the first question stays open");
            Assert.Equal(2, flow.Waiting);

            Assert.Null(flow.Advance(), "nothing is advanced while a question is open");
            Assert.True(ReferenceEquals(a, flow.Current));
        }

        [Test]
        public void Take_HandsTheQuestionOverExactlyOnce()
        {
            var flow = new ConfirmFlow();
            ConfirmRequest a = Ask("a");
            flow.Enqueue(a);

            Assert.True(ReferenceEquals(a, flow.Take()));
            Assert.Null(flow.Take(), "a second click, or the close that follows the answer, finds nothing to answer");
            Assert.Null(flow.Take());
            Assert.Null(flow.Current);
        }

        [Test]
        public void Take_OnAnIdleFlow_IsNull()
        {
            Assert.Null(new ConfirmFlow().Take());
        }

        [Test]
        public void Advance_AfterTheAnswer_OpensTheWaitingOnesOneByOne_InTheOrderAsked()
        {
            var flow = new ConfirmFlow();
            ConfirmRequest a = Ask("a"), b = Ask("b"), c = Ask("c");
            flow.Enqueue(a);
            flow.Enqueue(b);
            flow.Enqueue(c);

            flow.Take();
            Assert.True(ReferenceEquals(b, flow.Advance()));
            Assert.True(ReferenceEquals(b, flow.Current));
            Assert.Equal(1, flow.Waiting);

            flow.Take();
            Assert.True(ReferenceEquals(c, flow.Advance()));
            flow.Take();
            Assert.Null(flow.Advance());
            Assert.Null(flow.Current);
            Assert.Equal(0, flow.Waiting);
        }

        [Test]
        public void Enqueue_AskedFromACallback_WhileOthersWait_DoesNotJumpTheQueue()
        {
            // The window is closed and the question taken, then the callback runs and asks again; the question that was waiting is still first.
            var flow = new ConfirmFlow();
            ConfirmRequest a = Ask("a"), b = Ask("b"), fromCallback = Ask("c");
            flow.Enqueue(a);
            flow.Enqueue(b);

            flow.Take();
            Assert.Equal(ConfirmArrival.Queued, flow.Enqueue(fromCallback), "b was due first");
            Assert.True(ReferenceEquals(b, flow.Advance()));
            flow.Take();
            Assert.True(ReferenceEquals(fromCallback, flow.Advance()));
        }

        [Test]
        public void Enqueue_AskedFromACallback_OnAnEmptyFlow_OpensAtOnce()
        {
            var flow = new ConfirmFlow();
            flow.Enqueue(Ask("a"));
            flow.Take();

            ConfirmRequest again = Ask("again");
            Assert.Equal(ConfirmArrival.ShowNow, flow.Enqueue(again));
            Assert.Null(flow.Advance(), "the dialog's own advance finds the window taken and does nothing");
            Assert.True(ReferenceEquals(again, flow.Current));
        }

        [Test]
        public void Enqueue_TheSameQuestionAgain_IsDropped_WhetherOpenOrWaiting()
        {
            Action yes = () => { };
            Action no = () => { };
            var flow = new ConfirmFlow();

            Assert.Equal(ConfirmArrival.ShowNow, flow.Enqueue(Ask("same", yes, no)));
            Assert.Equal(ConfirmArrival.Duplicate, flow.Enqueue(Ask("same", yes, no)), "the open one");
            Assert.Equal(ConfirmArrival.Queued, flow.Enqueue(Ask("other", yes, no)));
            Assert.Equal(ConfirmArrival.Duplicate, flow.Enqueue(Ask("other", yes, no)), "a waiting one");
            Assert.Equal(1, flow.Waiting);
        }

        [Test]
        public void Enqueue_SameTextsDifferentCallbacks_IsAnotherQuestion()
        {
            var flow = new ConfirmFlow();
            int callsA = 0, callsB = 0;

            Assert.Equal(ConfirmArrival.ShowNow, flow.Enqueue(Ask("same", () => callsA++, null)));
            Assert.Equal(ConfirmArrival.Queued, flow.Enqueue(Ask("same", () => callsB++, null)), "two features that ask the same words each get their answer");
            Assert.Equal(1, flow.Waiting);
            Assert.Equal(0, callsA + callsB, "the flow only decides, it calls nothing");
        }

        [Test]
        public void Enqueue_SameCallbacksDifferentButtonTexts_IsAnotherQuestion()
        {
            Action yes = () => { };
            var flow = new ConfirmFlow();
            flow.Enqueue(new ConfirmRequest("t", "m", "core.common.yes", "core.common.no", yes, null));

            Assert.Equal(ConfirmArrival.Queued, flow.Enqueue(new ConfirmRequest("t", "m", "core.common.ok", "core.common.cancel", yes, null)));
        }

        [Test]
        public void AnAnsweredQuestion_CanBeAskedAgain()
        {
            Action yes = () => { };
            var flow = new ConfirmFlow();
            flow.Enqueue(Ask("q", yes, null));
            flow.Take();

            Assert.Equal(ConfirmArrival.ShowNow, flow.Enqueue(Ask("q", yes, null)), "the first one is answered, this is a new question");
        }

        [Test]
        public void Drain_ReturnsTheOpenQuestionFirst_ThenTheWaitingOnes_AndEmptiesTheFlow()
        {
            var flow = new ConfirmFlow();
            ConfirmRequest a = Ask("a"), b = Ask("b"), c = Ask("c");
            flow.Enqueue(a);
            flow.Enqueue(b);
            flow.Enqueue(c);

            List<ConfirmRequest> all = flow.Drain();

            Assert.Equal(3, all.Count);
            Assert.True(ReferenceEquals(a, all[0]) && ReferenceEquals(b, all[1]) && ReferenceEquals(c, all[2]));
            Assert.Null(flow.Current);
            Assert.Equal(0, flow.Waiting);
            Assert.Null(flow.Take());
            Assert.Equal(0, flow.Drain().Count);
        }

        [Test]
        public void ARefusalRound_EveryQuestionIsRefusedOnce_AndNothingIsLeft()
        {
            // What the dialog does when it cannot show anything (scene being left): take, refuse, advance - each callback exactly once.
            var flow = new ConfirmFlow();
            var refused = new List<string>();
            foreach (string name in new[] { "a", "b", "c", "d" })
            {
                string n = name;
                flow.Enqueue(Ask(n, null, () => refused.Add(n)));
            }

            ConfirmRequest open = flow.Current;
            while (open != null)
            {
                Assert.True(ReferenceEquals(open, flow.Take()));
                open.OnNo();
                open = flow.Advance();
            }

            Assert.Equal(new List<string> { "a", "b", "c", "d" }, refused);
            Assert.Null(flow.Take());
        }

        [Test]
        public void Request_NullTextsBecomeEmpty_AndNullRequestIsRefused()
        {
            var request = new ConfirmRequest(null, null, null, null, null, null);
            Assert.Equal(string.Empty, request.Title);
            Assert.Equal(string.Empty, request.Message);
            Assert.Equal(string.Empty, request.YesKey);
            Assert.Equal(string.Empty, request.NoKey);
            Assert.False(request.IsSameQuestion(null));
            Assert.True(request.IsSameQuestion(new ConfirmRequest("", "", "", "", null, null)));

            Assert.Throws<ArgumentNullException>(() => new ConfirmFlow().Enqueue(null));
        }
    }
}
