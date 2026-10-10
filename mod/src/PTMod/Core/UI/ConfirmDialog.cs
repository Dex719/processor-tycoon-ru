using System;
using PTMod.Core.Logic.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PTMod.Core
{
    /// <summary>
    /// The confirmation dialog of <see cref="ModUI"/> (FR-16 item 8): the <c>Confirm</c> half of the <c>partial</c> class, next to the tooltip bridge in
    /// <c>TooltipBridge.cs</c>. The dialog itself is <see cref="ConfirmDialog"/>.
    /// </summary>
    public static partial class ModUI
    {
        /// <summary>
        /// A modal "Yes / No" question on a <c>Window.Modal</c> window of the game, one window for the whole mod: time stands while it is open (the template's
        /// <c>PauseTrigger</c>), the window is closed before the callback runs. Exactly one of the callbacks is called, once, under the guard of the feature it belongs to
        /// (an exception is counted against that feature and never reaches the game).
        /// <list type="bullet">
        /// <item><b>Answers.</b> "Yes" calls <paramref name="onYes"/>. "No", the cross of the title bar, the end of the scene and every case where the question cannot be
        /// shown (no template, scene not ready) call <paramref name="onNo"/>: a question that nobody answered is a refusal, never a "Yes". A second click on a button does nothing.</item>
        /// <item><b>One window.</b> A question asked while another is open waits and is shown when the first is answered (first come, first served), so no window opens
        /// twice. The very same question (same texts and same callbacks) that is already open or waiting is not asked again.</item>
        /// <item><b>Texts.</b> <paramref name="title"/>, <paramref name="message"/>, <paramref name="yesKey"/> and <paramref name="noKey"/> are keys of <see cref="ModL10n"/> or ready
        /// texts (a text no table knows is shown as it is). The window grows with the message.</item>
        /// <item><b>Main menu.</b> There is no <c>Window.Modal</c> there; the dialog then takes <c>Window.MenuPanel</c> (nothing to pause in the menu).</item>
        /// </list>
        /// Main thread only. Does not throw. The callback may run inside this call (when the question cannot be shown); do not rely on it running later.
        /// </summary>
        /// <param name="title">The caption of the window (key or text).</param>
        /// <param name="message">The question (key or text).</param>
        /// <param name="onYes">Called when the player answers "Yes"; may be null.</param>
        /// <param name="onNo">Called on every other way out of the dialog; may be null.</param>
        /// <param name="yesKey">Caption of the "Yes" button; <c>core.common.yes</c> by default (a key or a text, e.g. <c>core.common.ok</c>).</param>
        /// <param name="noKey">Caption of the "No" button; <c>core.common.no</c> by default (e.g. <c>core.common.cancel</c>).</param>
        public static void Confirm(string title, string message, Action onYes, Action onNo = null,
                                   string yesKey = "core.common.yes", string noKey = "core.common.no")
        {
            ConfirmDialog.Ask(title, message, onYes, onNo, yesKey, noKey);
        }
    }

    /// <summary>
    /// The one confirmation window of the mod and the questions waiting for it. The rules (one answer per question, the queue, the duplicate check) are the pure
    /// <see cref="ConfirmFlow"/>; this class makes the window from the game's template and carries the answers out.
    /// <list type="bullet">
    /// <item><b>Window.</b> Made on first use per scene as <c>mod-framework.confirm</c> through <see cref="ModUI.CreateWindow"/> (from here, so the window belongs to the framework and
    /// is not closed when the feature that asked is switched off): the message (a <see cref="ModUI.Text"/>) on top, below it a row with the "Yes" button
    /// (call-to-action) and the "No" button. Each question only refills the texts, resizes and centres the same window. It is never destroyed inside the scene (FR-16 item 6).</item>
    /// <item><b>Answering.</b> The first thing every way out does is <see cref="ConfirmFlow.Take"/>: it hands the question over once, so a repeated click, the cross after a click, or
    /// the close of the scene after an answer find nothing to answer. Then the window is closed (time goes on), the callback runs, and the next waiting question is shown.</item>
    /// <item><b>Scene.</b> The scene being left closes the window (<c>ModWindow.Closed</c>), which answers "No"; the questions still waiting cannot be shown
    /// (<see cref="PTModHost.IsSceneReady"/> is already false) and are answered "No" one by one.</item>
    /// <item><b>Safety.</b> A callback that asks again from inside a refusal is cut after <see cref="MaxCallbackDepth"/> nested calls; a chain of refusals longer than
    /// <see cref="MaxChain"/> drops what is left with an error. A question left open by a window that vanished is answered "No" the next time anything asks.</item>
    /// <item><b>Check in the game</b> (owner; the game is never started from the tools): the hub or <c>framework-sample</c> asks a question; the time stands, the window is in the
    /// theme, "Yes" calls the first callback once, "No" and the cross call the second once and the time goes on; two questions in a row are shown one after the other.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class ConfirmDialog
    {
        internal const string DefaultYesKey = "core.common.yes";
        internal const string DefaultNoKey = "core.common.no";

        private const string CoreId = ModLog.CoreFeatureId;
        private const string WindowId = "mod-framework.confirm";

        // The width is fixed; the height follows the message (plus the title bar, the paddings and the buttons: Chrome is generous, the message takes what is left).
        private const float DialogWidth = 440f;
        private const float MinHeight = 170f;
        private const float MaxHeight = 460f;
        private const float Chrome = 120f;
        private const float SidePadding = 12f;
        private const float ButtonRowHeight = 36f;
        private const float ButtonSpacing = 12f;

        private const int MaxCallbackDepth = 8;
        private const int MaxChain = 256;

        private static readonly ConfirmFlow Flow = new ConfirmFlow();

        private static ModWindow _window;
        private static TMP_Text _message;
        private static TMP_Text _yesLabel;
        private static TMP_Text _noLabel;
        private static bool _buildFailed;   // for _window: its content could not be made, so it is not used
        private static int _callbackDepth;

        // ---- asking ----

        /// <summary>See <see cref="ModUI.Confirm"/>.</summary>
        internal static void Ask(string title, string message, Action onYes, Action onNo, string yesKey, string noKey)
        {
            try
            {
                RecoverIfStale();

                var request = new ConfirmRequest(
                    title, message,
                    string.IsNullOrEmpty(yesKey) ? DefaultYesKey : yesKey,
                    string.IsNullOrEmpty(noKey) ? DefaultNoKey : noKey,
                    onYes, onNo);

                ConfirmArrival arrival = Flow.Enqueue(request);
                if (arrival == ConfirmArrival.Duplicate)
                {
                    ModLog.Debug(CoreId, "ui: the same confirmation is already open or waiting; it is not asked twice");
                    return;
                }
                if (arrival == ConfirmArrival.Queued)
                {
                    ModLog.Debug(CoreId, "ui: a confirmation waits behind " + Flow.Waiting + " other(s)");
                    return;
                }

                if (!TryPresent(request)) Answer(false);   // it cannot be shown: the safe answer
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: Confirm failed", ex);
            }
        }

        // A question that is "open" while its window is not (the window was destroyed behind our back), or waiting questions nobody is going to show: settle them
        // before taking a new one, otherwise every later question would queue behind a dialog that is not there.
        private static void RecoverIfStale()
        {
            if (Flow.Current == null)
            {
                if (Flow.Waiting > 0 && _callbackDepth == 0) ShowNext();
                return;
            }
            if (_window != null && _window.IsOpen) return;

            ModLog.Warn(CoreId, "ui: a confirmation was open without its window; it is answered No");
            Answer(false);
        }

        // ---- answering ----

        private static void OnYesClicked()
        {
            Answer(true);
        }

        private static void OnNoClicked()
        {
            Answer(false);
        }

        // The window was closed by something other than an answer: the cross, the end of the scene.
        private static void OnWindowClosed()
        {
            Answer(false);
        }

        /// <summary>
        /// The player's answer (or the refusal for a way out): takes the open question, closes the window so time goes on, runs the callback, shows the next question.
        /// Does nothing when no question is open - a second click, or the close that follows an answer.
        /// </summary>
        private static void Answer(bool yes)
        {
            ConfirmRequest done = Flow.Take();
            if (done == null) return;

            try
            {
                CloseWindow();   // before the callback: the time goes on, and a callback that asks again finds the window free
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: cannot close the confirmation window", ex);
            }

            Invoke(done, yes);
            ShowNext();
        }

        private static void Invoke(ConfirmRequest request, bool yes)
        {
            Action callback = yes ? request.OnYes : request.OnNo;
            if (callback == null) return;

            if (_callbackDepth >= MaxCallbackDepth)
            {
                ModLog.Error(CoreId, "ui: confirmation callbacks keep asking from inside each other; this callback is skipped");
                return;
            }

            _callbackDepth++;
            try
            {
                ModHooks.Guard(ModFeatures.OwnerOf(callback), yes ? "ModUI.Confirm/yes" : "ModUI.Confirm/no", callback);
            }
            finally
            {
                _callbackDepth--;
            }
        }

        // Shows the next waiting question. One that cannot be shown is refused and the one after it is tried (the loop, not a recursion, so a long queue is no stack).
        private static void ShowNext()
        {
            for (int step = 0; step < MaxChain; step++)
            {
                ConfirmRequest next = Flow.Advance();   // null while a question is open or nothing waits
                if (next == null) return;
                if (TryPresent(next)) return;

                Flow.Take();   // next is the open one; it is refused
                Invoke(next, false);
            }

            foreach (ConfirmRequest dropped in Flow.Drain())
                ModLog.Error(CoreId, "ui: a confirmation chain of " + MaxChain + " refusals was cut; '" + dropped.Title + "' is dropped without an answer");
        }

        // ---- window ----

        private static bool TryPresent(ConfirmRequest request)
        {
            try
            {
                if (!PTModHost.IsSceneReady)
                {
                    ModLog.Debug(CoreId, "ui: no confirmation while the scene is not ready (loading or being left)");
                    return false;
                }
                if (!ModUI.IsReady)
                {
                    ModLog.Warn(CoreId, "ui: the templates of the scene are not looked up; the confirmation cannot be shown");
                    return false;
                }

                ModWindow window = EnsureWindow();
                if (window == null) return false;

                Fill(window, request);
                window.Open();
                return window.IsOpen;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: cannot show the confirmation", ex);
                return false;
            }
        }

        private static ModWindow EnsureWindow()
        {
            if (_window != null && _window.IsAlive) return _buildFailed ? null : _window;

            _window = null;
            _message = null;
            _yesLabel = null;
            _noLabel = null;
            _buildFailed = false;

            // The main menu has no Window.Modal; its panel window does the same job there (and has no time to stop).
            WindowTemplate template = ModUITemplates.Has(ModUITemplates.WindowModal) ? WindowTemplate.Modal : WindowTemplate.MenuPanel;
            ModWindow window = ModUI.CreateWindow(WindowId, template, string.Empty, new Vector2(DialogWidth, MinHeight));   // CreateWindow logs why it is null
            if (window == null) return null;

            _window = window;
            if (!Build(window))
            {
                _buildFailed = true;   // once per window; the next scene makes a new one
                return null;
            }

            window.Closed += OnWindowClosed;
            return window;
        }

        // The content: the message that takes the free height, then a row of two buttons of equal width ("Yes" is the call to action).
        private static bool Build(ModWindow window)
        {
            RectTransform content = window.Content;
            if (content == null) return false;

            try
            {
                TMP_Text message = ModUI.Text(content, string.Empty);
                if (message == null) return false;

                var messageLayout = message.gameObject.AddComponent<LayoutElement>();
                messageLayout.flexibleWidth = 1f;
                messageLayout.flexibleHeight = 1f;

                var row = new GameObject("PTModConfirmButtons", typeof(RectTransform));
                row.transform.SetParent(content, false);
                WidgetKit.AdoptLayer(row, content);

                var group = row.AddComponent<HorizontalLayoutGroup>();
                group.spacing = ButtonSpacing;
                group.childAlignment = TextAnchor.MiddleCenter;
                group.childControlWidth = true;
                group.childControlHeight = true;
                group.childForceExpandWidth = true;
                group.childForceExpandHeight = true;

                var rowLayout = row.AddComponent<LayoutElement>();
                rowLayout.minHeight = ButtonRowHeight;
                rowLayout.preferredHeight = ButtonRowHeight;
                rowLayout.flexibleWidth = 1f;

                var yes = ModUI.Button(row.transform, string.Empty, OnYesClicked, ButtonStyle.CallToAction);
                var no = ModUI.Button(row.transform, string.Empty, OnNoClicked, ButtonStyle.Standard);
                if (yes == null || no == null)
                {
                    ModLog.Error(CoreId, "ui: the button templates are missing; the confirmation window has no buttons and is not used");
                    return false;
                }

                _message = message;
                _yesLabel = yes.GetComponentInChildren<TMP_Text>(true);
                _noLabel = no.GetComponentInChildren<TMP_Text>(true);
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: cannot build the confirmation window", ex);
                return false;
            }
        }

        private static void Fill(ModWindow window, ConfirmRequest request)
        {
            string text = ModUI.TextOfKey(request.Message);

            window.SetTitle(ModUI.TextOfKey(request.Title));
            _message.text = text;
            if (_yesLabel != null) _yesLabel.text = ModUI.TextOfKey(request.YesKey);
            if (_noLabel != null) _noLabel.text = ModUI.TextOfKey(request.NoKey);

            var rect = window.Root.transform as RectTransform;
            if (rect == null) return;

            float height = Mathf.Clamp(MessageHeight(text) + Chrome, MinHeight, MaxHeight);
            rect.sizeDelta = new Vector2(DialogWidth, height);
            var canvas = rect.parent as RectTransform;
            if (canvas != null) ModUI.CentreOn(rect, canvas);   // a modal question appears in the middle, wherever the last one was left
            ModLog.Debug(CoreId, "ui: confirmation window " + Mathf.RoundToInt(DialogWidth) + "x" + Mathf.RoundToInt(height) + " for " + text.Length + " character(s)");
        }

        // How high the message is at the width the window gives it; a rough estimate when TextMeshPro cannot say.
        private static float MessageHeight(string text)
        {
            float width = DialogWidth - 2f * SidePadding;
            try
            {
                return Mathf.Max(20f, _message.GetPreferredValues(text, width, 0f).y);
            }
            catch (Exception)
            {
                int lines = Mathf.CeilToInt(text.Length * 7.5f / width) + 1;
                return Mathf.Max(20f, lines * 20f);
            }
        }

        private static void CloseWindow()
        {
            ModWindow window = _window;
            if (window != null) window.Close();   // idempotent; raises Closed, which finds no open question
        }
    }
}

namespace PTMod.Core.Logic.Ui
{
    // ---------------------------------------------------------------------------------------------------------------------------------
    // The rules of the confirmation dialog without Unity (nothing below this line may touch the engine, the game or the log): at integration
    // this block moves to Core/Logic/Ui/ConfirmFlow.cs unchanged, where the test runner reaches it.
    // ---------------------------------------------------------------------------------------------------------------------------------
    using System;
    using System.Collections.Generic;

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
