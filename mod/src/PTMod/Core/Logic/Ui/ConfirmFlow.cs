using System;
using System.Collections.Generic;

namespace PTMod.Core.Logic.Ui
{
    /// <summary>One question for the player: the texts as the caller gave them (keys or ready text) and the two ways out.</summary>
    internal sealed class ConfirmRequest
    {
        public ConfirmRequest(string title, string message, string yesKey, string noKey, Action onYes, Action onNo)
        {
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            YesKey = yesKey ?? string.Empty;
            NoKey = noKey ?? string.Empty;
            OnYes = onYes;
            OnNo = onNo;
        }

        public string Title { get; }

        public string Message { get; }

        public string YesKey { get; }

        public string NoKey { get; }

        public Action OnYes { get; }

        public Action OnNo { get; }

        /// <summary>Same texts and same callbacks (a delegate is equal when its target and method are): asking it twice is asking it once.</summary>
        public bool IsSameQuestion(ConfirmRequest other)
        {
            return other != null
                   && Title == other.Title && Message == other.Message && YesKey == other.YesKey && NoKey == other.NoKey
                   && OnYes == other.OnYes && OnNo == other.OnNo;
        }
    }

    /// <summary>What <see cref="ConfirmFlow.Enqueue"/> decided for a new question.</summary>
    internal enum ConfirmArrival
    {
        /// <summary>Nothing was open or waiting: the caller shows it now.</summary>
        ShowNow = 0,

        /// <summary>Another question is open (or others wait): it will be shown, in order, when the earlier ones are answered.</summary>
        Queued = 1,

        /// <summary>The same question is already open or waiting: this one is dropped.</summary>
        Duplicate = 2
    }

    /// <summary>
    /// The state of the confirmation dialog (FR-16 item 8, AC-16.3): at most one question is open, the others wait in order, and every question is handed over
    /// for an answer exactly once - <see cref="Take"/> returns it the first time and null after that, whichever way out (a button, the cross, the end of the scene) asks first.
    /// </summary>
    internal sealed class ConfirmFlow
    {
        private readonly Queue<ConfirmRequest> _waiting = new Queue<ConfirmRequest>();
        private ConfirmRequest _current;

        /// <summary>The open question, or null.</summary>
        public ConfirmRequest Current
        {
            get { return _current; }
        }

        /// <summary>How many questions wait behind the open one.</summary>
        public int Waiting
        {
            get { return _waiting.Count; }
        }

        /// <summary>
        /// A new question. Nothing open and nothing waiting makes it the open one (<see cref="ConfirmArrival.ShowNow"/>); otherwise it joins the end of the line, so a callback
        /// that asks while the next waiting question is still due does not jump the queue. The same question as one already open or waiting is dropped.
        /// </summary>
        public ConfirmArrival Enqueue(ConfirmRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");

            if (request.IsSameQuestion(_current)) return ConfirmArrival.Duplicate;
            foreach (ConfirmRequest waiting in _waiting)
            {
                if (request.IsSameQuestion(waiting)) return ConfirmArrival.Duplicate;
            }

            if (_current == null && _waiting.Count == 0)
            {
                _current = request;
                return ConfirmArrival.ShowNow;
            }

            _waiting.Enqueue(request);
            return ConfirmArrival.Queued;
        }

        /// <summary>The answer comes: hands over the open question (it is no longer open) - once. Null when none is open: a second click, or the close after an answer.</summary>
        public ConfirmRequest Take()
        {
            ConfirmRequest request = _current;
            _current = null;
            return request;
        }

        /// <summary>Makes the first waiting question the open one and returns it; null while one is open or none waits.</summary>
        public ConfirmRequest Advance()
        {
            if (_current != null || _waiting.Count == 0) return null;

            _current = _waiting.Dequeue();
            return _current;
        }

        /// <summary>Empties the dialog and returns what was in it, the open question first. The caller answers or drops them.</summary>
        public List<ConfirmRequest> Drain()
        {
            var all = new List<ConfirmRequest>();
            if (_current != null) all.Add(_current);
            all.AddRange(_waiting);
            _current = null;
            _waiting.Clear();
            return all;
        }
    }
}
