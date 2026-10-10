using System;
using TMPro;
using UnityEngine;

namespace PTMod.Core
{
    /// <summary>Which window of the game <see cref="ModUI.CreateWindow"/> clones (the keys of <see cref="ModUITemplates"/>).</summary>
    public enum WindowTemplate
    {
        /// <summary>Game scene, <c>Window.Modal</c>: stops the time while open (<c>PauseTrigger</c>); dialogs, the hub.</summary>
        Modal = 0,

        /// <summary>Game scene, <c>Window.Panel</c>: a window that does not stop the time.</summary>
        Panel = 1,

        /// <summary>Main menu, <c>Window.MenuPanel</c>: the panel of the new-game options.</summary>
        MenuPanel = 2
    }

    /// <summary>
    /// A window of the mod (FR-16): a clone of a window of the game with its frame, title bar, theme and close button, and an empty content area for the
    /// widgets of <see cref="ModUI"/>. Made by <see cref="ModUI.CreateWindow"/>, one per id and scene.
    /// <list type="bullet">
    /// <item><b>Open / Close.</b> Shows and hides the clone (<c>SetActive</c>); a window is never destroyed inside a scene, only hidden, because
    /// <c>PauseHandler</c> cannot drop a pause trigger. <see cref="Opened"/> and <see cref="Closed"/> are raised once per real change (a second <see cref="Open"/> only
    /// brings the window to the front); a handler that throws is counted against its feature and the others still run.</item>
    /// <item><b>Pause.</b> A <see cref="WindowTemplate.Modal"/> window keeps the <c>PauseTrigger</c> of its template: the time stands while it is
    /// active in the hierarchy, i.e. between <see cref="Open"/> and <see cref="Close"/> (AC-16.1).</item>
    /// <item><b>Close button.</b> The cross of the title bar calls <see cref="Close"/> (the listeners of the template were removed, the window adds its own).</item>
    /// <item><b>Scenes.</b> The window belongs to the scene that made it. When the scene is left it is closed (<see cref="Closed"/> is raised) and retired:
    /// <see cref="IsAlive"/> is false and <see cref="Open"/> does nothing. Make the windows again on <see cref="ModUI.Ready"/>.</item>
    /// <item><b>Feature off.</b> The windows of a feature that stops being Enabled are closed by <see cref="ModUI"/>.</item>
    /// </list>
    /// Main thread only. Nothing here throws into the caller or into the game.
    /// </summary>
    public sealed class ModWindow
    {
        private readonly UiWindowParts _parts;
        private string _titleKey;
        private bool _open;
        private bool _retired;
        private bool _deadReported;
        private bool _firstOpenLogged;

        internal ModWindow(string id, WindowTemplate template, string owner, UiWindowParts parts, string titleKey)
        {
            Id = id;
            Template = template;
            Owner = owner ?? ModLog.CoreFeatureId;
            _parts = parts;
            _titleKey = titleKey;
        }

        /// <summary>The id the window was made with (unique among the windows of the scene).</summary>
        public string Id { get; }

        /// <summary>The template of the game the window was cloned from.</summary>
        public WindowTemplate Template { get; }

        /// <summary>The window object (the clone's root, a child of the template's canvas). Unity-null after the scene was left.</summary>
        public GameObject Root
        {
            get { return _parts.Root; }
        }

        /// <summary>The emptied content area (<c>Background</c> of the template): the parent of the widgets of <see cref="ModUI"/>.</summary>
        public RectTransform Content
        {
            get { return _parts.Content; }
        }

        /// <summary>Open now: <see cref="Open"/> was called, <see cref="Close"/> was not, and the window is still alive.</summary>
        public bool IsOpen
        {
            get { return _open && IsAlive; }
        }

        /// <summary>False once the scene of the window was left (or its object is gone): <see cref="Open"/> then does nothing.</summary>
        public bool IsAlive
        {
            get { return !_retired && _parts.Root != null; }
        }

        /// <summary>The window was shown (not raised when <see cref="Open"/> finds it open).</summary>
        public event Action Opened;

        /// <summary>The window was hidden: by <see cref="Close"/>, by the cross of the title bar, when its feature stopped or when the scene was left.</summary>
        public event Action Closed;

        /// <summary>The feature whose code made the window (the framework for the hub and the dialogs).</summary>
        internal string Owner { get; }

        /// <summary>The title bar caption, or the empty string when the template has none.</summary>
        public string Title
        {
            get { return _parts.Title != null ? _parts.Title.text : string.Empty; }
        }

        /// <summary>Shows the window in front of the others. A <see cref="WindowTemplate.Modal"/> window stops the time. Open already: only brings it to the front.</summary>
        public void Open()
        {
            if (!CheckAlive("Open")) return;

            if (_open)
            {
                BringToFront();
                return;
            }

            _open = true;   // before the side effects: a handler that calls Open or Close again sees the new state
            try
            {
                if (!_firstOpenLogged) CentreOnCanvas();   // the screen may have changed since the window was made; later opens keep where the player dragged it
                BringToFront();
                _parts.Root.SetActive(true);
                LogFirstOpen();
            }
            catch (Exception ex)
            {
                _open = false;
                ModLog.Error(ModLog.CoreFeatureId, "ui: cannot open the window '" + Id + "'", ex);
                return;
            }

            ModHooks.Raise("ModWindow.Opened", Opened);
        }

        /// <summary>Hides the window (and lets the time go on if it was modal). Closed already: does nothing.</summary>
        public void Close()
        {
            if (!_open) return;

            _open = false;
            try
            {
                if (_parts.Root != null) _parts.Root.SetActive(false);
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "ui: cannot close the window '" + Id + "'", ex);
            }

            ModHooks.Raise("ModWindow.Closed", Closed);
        }

        /// <summary>Opens a closed window, closes an open one.</summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>
        /// Sets the caption of the title bar to a ready text; from now on the caption no longer follows the language (use it for a window whose
        /// title is data, not a key). A template without a title bar ignores it.
        /// </summary>
        public void SetTitle(string text)
        {
            _titleKey = null;
            ApplyTitle(text);
        }

        // ---- for ModUI ----

        /// <summary>The caption comes from this key (or text, if no table has it) and is redrawn when the language changes.</summary>
        internal void SetTitleKey(string key)
        {
            _titleKey = key;
            RefreshTitle();
        }

        /// <summary>The language changed: the caption is looked up again.</summary>
        internal void RefreshTitle()
        {
            if (_titleKey == null) return;
            ApplyTitle(ModUI.TextOfKey(_titleKey));
        }

        /// <summary>The scene is being left (or the window must go): closes it, raising <see cref="Closed"/>, and marks it dead.</summary>
        internal void Retire()
        {
            if (_retired) return;

            try
            {
                Close();
            }
            finally
            {
                _retired = true;
            }
        }

        private void ApplyTitle(string text)
        {
            try
            {
                TMP_Text title = _parts.Title;
                if (title != null) title.text = text ?? string.Empty;
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "ui: cannot set the title of the window '" + Id + "'", ex);
            }
        }

        private void CentreOnCanvas()
        {
            var window = _parts.Root.transform as RectTransform;
            var canvas = _parts.Root.transform.parent as RectTransform;
            if (window != null && canvas != null) ModUI.CentreOn(window, canvas);
        }

        private void BringToFront()
        {
            try
            {
                if (_parts.Root != null) _parts.Root.transform.SetAsLastSibling();
            }
            catch (Exception ex)
            {
                ModLog.Error(ModLog.CoreFeatureId, "ui: cannot bring the window '" + Id + "' to the front", ex);
            }
        }

        private bool CheckAlive(string operation)
        {
            if (IsAlive) return true;

            // A feature that kept the window over a scene change: say so once, then stay quiet.
            if (!_deadReported)
            {
                _deadReported = true;
                ModLog.Debug(ModLog.CoreFeatureId, "ui: " + operation + " on the window '" + Id + "' is ignored; its scene was left, make the window again on ModUI.Ready");
            }
            return false;
        }

        // For the check in the game (owner): the first open writes where the frame really is, so a wrong size or a covered first row shows in Player.log.
        private void LogFirstOpen()
        {
            if (_firstOpenLogged) return;
            _firstOpenLogged = true;

            RectTransform root = _parts.Root.transform as RectTransform;
            RectTransform content = _parts.Content;
            ModLog.Debug(ModLog.CoreFeatureId, "ui: window '" + Id + "' opened for the first time: root "
                + (root == null ? "?" : Size(root)) + ", content " + (content == null ? "?" : Size(content)) + ", template " + Template);
        }

        private static string Size(RectTransform rect)
        {
            Rect r = rect.rect;
            return Mathf.RoundToInt(r.width) + "x" + Mathf.RoundToInt(r.height);
        }
    }
}
