using System;
using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.EventSystem
{
    /// <summary>
    /// The modal window of a waiting choice (design.md "UI", FR-7 item 3): a clone of the game's modal window (<c>WindowTemplate.Modal</c>), which keeps the pause trigger
    /// of its template, so the time stands while it is open (AC-7.1). It shows the title, the cause and the text of the event, the line "Осталось N из M дн." with a
    /// bar of one division per day, a button for every choice (the exact effects as its hint; a grey button says why) and "Решить позже". It only draws a
    /// <see cref="ChoiceWindowModel"/>: when it opens, in which order and with what numbers is the business of <see cref="EventUiController"/>.
    /// <list type="bullet">
    /// <item>A button reports the choice (<c>onChoice</c>); if the instance is still not answered after that (the choice was refused) the window closes and reports it closed.</item>
    /// <item>"Решить позже" and the cross of the title bar close the window, report it closed, and leave the letter and the timer as they are (FR-7 item 4).</item>
    /// <item><see cref="Hide"/> closes the window without reporting it (the instance was answered elsewhere).</item>
    /// </list>
    /// To check in the game (the owner, M6): an <c>important</c> event stops the time; the window counts the days; "Решить позже" lets the time go on and the letter stays.
    /// Main thread only.
    /// </summary>
    internal sealed class EventChoiceWindow : IChoiceWindowPort
    {
        private const string Feature = EventSystemApi.FeatureId;
        private const string WindowId = Feature + ".choice";

        private static readonly Vector2 WindowSize = new Vector2(640f, 600f);

        private readonly Action<int, string> _onChoice;
        private readonly Action<int> _onClosed;

        private ModWindow _window;
        private ModWindow _hooked;
        private int _instanceId;
        private bool _hiding;

        /// <param name="onChoice">The player pressed a choice: the instance and the choice.</param>
        /// <param name="onClosed">The window was closed without an answer: the instance.</param>
        public EventChoiceWindow(Action<int, string> onChoice, Action<int> onClosed)
        {
            _onChoice = onChoice ?? ((id, choice) => { });
            _onClosed = onClosed ?? (id => { });
        }

        public bool IsOpen
        {
            get { return _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>Opens the window with this content, or draws an open window anew (the day passed, the money changed).</summary>
        public void Show(ChoiceWindowModel model)
        {
            if (model == null) return;

            try
            {
                if (!EnsureWindow()) return;
                _instanceId = model.InstanceId;
                Build(model);
                _window.Open();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(Feature, "ui/choice-window", ex);
            }
        }

        /// <summary>Closes the window without telling the engine.</summary>
        public void Hide()
        {
            if (_window == null || !_window.IsAlive || !_window.IsOpen) return;
            _hiding = true;
            try
            {
                _window.Close();
            }
            finally
            {
                _hiding = false;
            }
        }

        // ---- the window ----

        private bool EnsureWindow()
        {
            if (_window != null && _window.IsAlive) return true;
            _window = null;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(Feature, "ui: the choice window cannot be opened, the templates of the scene are not ready");
                return false;
            }

            ModWindow window = ModUI.CreateWindow(WindowId, WindowTemplate.Modal, "es.ui.choice.title", WindowSize);
            if (window == null) return false;   // ModUI has said why

            if (!ReferenceEquals(_hooked, window))
            {
                window.Closed += OnWindowClosed;
                _hooked = window;
            }
            _window = window;
            return true;
        }

        // The cross, the end of the scene, the feature switched off: the player has not decided.
        private void OnWindowClosed()
        {
            if (_hiding) return;
            int id = _instanceId;
            _instanceId = 0;
            if (id != 0) _onClosed(id);
        }

        // ---- the contents ----

        private void Build(ChoiceWindowModel model)
        {
            RectTransform body = _window.Content;
            EventUiKit.Clear(body);

            EventUiKit.Line(body, model.Title, true);
            if (!string.IsNullOrEmpty(model.Cause)) EventUiKit.Line(body, EventUiKit.Muted(model.Cause));
            if (!string.IsNullOrEmpty(model.Text)) EventUiKit.Line(body, model.Text);
            EventUiKit.Line(body, model.Countdown, true);

            foreach (ChoiceButtonModel choice in model.Choices)
            {
                string choiceId = choice.ChoiceId;
                int instanceId = model.InstanceId;
                EventUiKit.Button(body, choice.Label, () => OnChoicePressed(instanceId, choiceId), choice.Enabled ? null : OrReason(choice), choice.Effects,
                    choice.Enabled ? ButtonStyle.CallToAction : ButtonStyle.Standard);
            }

            int id = model.InstanceId;
            EventUiKit.Button(body, model.DecideLaterLabel, () => OnDecideLater(id), null, null);
        }

        private static string OrReason(ChoiceButtonModel choice)
        {
            return string.IsNullOrEmpty(choice.Reason) ? "—" : choice.Reason;
        }

        // ---- the buttons ----

        private void OnChoicePressed(int instanceId, string choiceId)
        {
            _onChoice(instanceId, choiceId);

            // answered: the engine has hidden the window; refused (the instance is gone): the window must not stay
            if (IsOpen && _instanceId == instanceId) CloseAndReport(instanceId);
        }

        private void OnDecideLater(int instanceId)
        {
            if (IsOpen && _instanceId == instanceId) CloseAndReport(instanceId);
        }

        private void CloseAndReport(int instanceId)
        {
            Hide();
            _instanceId = 0;
            _onClosed(instanceId);
        }
    }
}
