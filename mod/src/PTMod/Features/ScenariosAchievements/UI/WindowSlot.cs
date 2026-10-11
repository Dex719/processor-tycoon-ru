using System;
using PTMod.Core;
using UnityEngine;

namespace PTMod.Features.ScenariosAchievements.UI
{
    /// <summary>
    /// The place in a window class where its <see cref="ModWindow"/> lives (TSK-022 .. TSK-026). The window is made from a template of the scene on first use and kept
    /// until the scene is left (<c>ModUI</c> then retires it and <see cref="ModWindow.IsAlive"/> turns false), so the next use makes a new one; asking for it while the templates
    /// of the scene are not looked up yet answers null and writes one line in the log. <see cref="Closed"/> is raised whichever way the window closes (the cross, <c>Close</c>,
    /// the end of the scene, the feature switched off). Main thread only.
    /// </summary>
    internal sealed class WindowSlot
    {
        private readonly string _id;
        private readonly string _titleKey;
        private readonly Vector2 _size;
        private ModWindow _window;
        private ModWindow _hooked;

        /// <param name="id">The id of the window among the windows of <c>ModUI</c>; it carries the id of the feature.</param>
        /// <param name="titleKey">The key of the caption of the title bar; <see cref="ModWindow.SetTitle"/> replaces it for a caption that is data.</param>
        internal WindowSlot(string id, string titleKey, Vector2 size)
        {
            _id = id;
            _titleKey = titleKey;
            _size = size;
        }

        /// <summary>The window closed (by anyone). Raised through <c>ModHooks.Raise</c>: a handler that throws costs only itself.</summary>
        internal event Action Closed;

        /// <summary>The window, when it was made in this scene and is alive; otherwise null.</summary>
        internal ModWindow Window
        {
            get { return _window != null && _window.IsAlive ? _window : null; }
        }

        /// <summary>The window is on the screen.</summary>
        internal bool IsOpen
        {
            get
            {
                ModWindow window = Window;
                return window != null && window.IsOpen;
            }
        }

        /// <summary>The window of the template, made now when it does not exist in this scene; null when it cannot be made (the log of the core says why).</summary>
        internal ModWindow Ensure(WindowTemplate template)
        {
            ModWindow alive = Window;
            if (alive != null) return alive;
            _window = null;

            if (!ModUI.IsReady)
            {
                ModLog.Warn(ScenariosFeature.FeatureId, "ui: the window '" + _id + "' cannot be made, the templates of the scene are not looked up yet");
                return null;
            }

            ModWindow made = ModUI.CreateWindow(_id, template, _titleKey, _size);
            if (made == null) return null;

            if (!ReferenceEquals(_hooked, made))
            {
                made.Closed += OnWindowClosed;
                _hooked = made;
            }
            _window = made;
            return made;
        }

        /// <summary>Closes the window if it is open.</summary>
        internal void Close()
        {
            ModWindow window = Window;
            if (window != null) window.Close();
        }

        private void OnWindowClosed()
        {
            ModHooks.Raise("WindowSlot.Closed", Closed);
        }
    }
}
