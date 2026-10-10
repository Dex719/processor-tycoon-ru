using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PTMod.Core
{
    /// <summary>
    /// Keeps the parts of the mod's windows that have no <c>Themeable*</c> component in the colours of the current theme (FR-16 item 4, AC-16.2).
    /// <list type="bullet">
    /// <item><b>Why it exists.</b> The clones of the game's templates carry the game's own <c>Themeable*</c> components, which <c>ThemeManager.UpdateThemes</c> repaints
    /// (it also reaches hidden objects). What the mod builds from scratch - text, the scroll bars - has none. <c>UpdateThemes</c> ends by raising
    /// <c>ThemeManager.OnThemeChanged</c>; this class listens to it and calls <see cref="ModUI.ReapplyTheme"/>, so an open window turns dark or light without being reopened.</item>
    /// <item><b>Why it re-subscribes.</b> The game clears <c>OnThemeChanged</c> and <c>OnLateThemeChanged</c> of the (persistent) <c>ThemeManager</c> each time a scene transition
    /// starts (<c>ThemeManager.Start</c> subscribes <c>SceneTransitionHandler.OnTransitionsStarted</c> to do it). So the handler is put on again for every scene, one frame
    /// after its load (<c>PTModHost.SceneReady</c>), and taken off when the scene is left. Taking it off first makes the subscription idempotent: a late start, a second
    /// <c>SceneReady</c> or a replaced manager never leaves two handlers (the colours would be applied twice).</item>
    /// <item><b>Faults.</b> The handler runs inside the game's <c>UpdateThemes</c>: it never throws, a failure is counted against the framework through
    /// <see cref="ModHooks.Guard(string, string, Action)"/> (five of them switch the repainting off until restart, the game's own themes go on).</item>
    /// <item><b>Check in the game</b> (owner): open a window of the mod, switch the theme in the settings - the window changes colour at once. With verbose log
    /// <c>Player.log</c> has <c>ui: theme handler bound to ThemeManager (scene N)</c> after each scene load.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    internal static class ThemeRebinder
    {
        private const string CoreId = ModLog.CoreFeatureId;

        // Kept as a plain MonoBehaviour so that this class does not depend on the game's type (the same trick as PTModHost._transitions).
        private static MonoBehaviour _bound;
        private static bool _broken;
        private static int _bindings;

        /// <summary>The handler is on the <c>ThemeManager</c> of the current scene.</summary>
        internal static bool IsBound
        {
            get { return !ReferenceEquals(_bound, null) && _bound != null; }
        }

        /// <summary>How many times the handler was put on in this session (one per scene that had a theme manager).</summary>
        internal static int Bindings
        {
            get { return _bindings; }
        }

        [CoreInit(CoreInitStage.HostReady, 10, Optional = true)]
        internal static void Start()
        {
            PTModHost.SceneReady += OnSceneReady;
            PTModHost.SceneLeaving += OnSceneLeaving;

            // A late start inside a loaded scene: the host has raised SceneReady already.
            if (PTModHost.IsSceneReady) OnSceneReady(PTModHost.CurrentScene);
        }

        private static void OnSceneReady(int index)
        {
            try
            {
                Bind(index);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: cannot follow the theme in scene " + index + "; the mod's own parts keep their colours when the theme changes", ex);
            }
        }

        private static void OnSceneLeaving()
        {
            try
            {
                Unbind();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "ui: dropping the theme handler failed", ex);
            }
        }

        private static void Bind(int scene)
        {
            if (_broken) return;

            try
            {
                if (!Subscribe()) return;   // no ThemeManager in this scene (cannot happen in the two scenes of the game, but a log line is cheap)
            }
            catch (Exception ex)
            {
                _broken = true;
                ModLog.Error(CoreId, "ui: ThemeManager.OnThemeChanged is not available in this game version; the mod's own parts keep their colours when the theme changes", ex);
                return;
            }

            // Nothing is repainted here: what the mod made before this point (a window made on ModUI.Ready) was painted with the current theme when it was made.
            _bindings++;
            ModLog.Debug(CoreId, "ui: theme handler bound to ThemeManager (scene " + scene + ")");
        }

        private static void Unbind()
        {
            if (ReferenceEquals(_bound, null)) return;

            try
            {
                Unsubscribe();
            }
            catch (Exception)
            {
                // A destroyed or mismatched manager: nothing left to remove.
            }
            _bound = null;
        }

        // The game's types are touched only in the NoInlining methods: if they do not match the game, the JIT fails there and the callers catch it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool Subscribe()
        {
            ProcessorTycoon.UI.Themes.ThemeManager manager = ProcessorTycoon.UI.Themes.ThemeManager.Instance;
            if (manager == null)
            {
                ModLog.Warn(CoreId, "ui: there is no ThemeManager in this scene; the mod's own parts will not follow the theme");
                _bound = null;
                return false;
            }

            // Off first, then on: one handler whatever happened before (the same manager again, a late start, a second SceneReady).
            if (!ReferenceEquals(_bound, null)) Unsubscribe();
            manager.OnThemeChanged -= OnThemeChanged;
            manager.OnThemeChanged += OnThemeChanged;
            _bound = manager;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Unsubscribe()
        {
            var manager = _bound as ProcessorTycoon.UI.Themes.ThemeManager;
            _bound = null;
            if (!ReferenceEquals(manager, null)) manager.OnThemeChanged -= OnThemeChanged;
        }

        /// <summary>Called by the game from <c>ThemeManager.UpdateThemes</c>, after every <c>Themeable*</c> was repainted. Must not throw.</summary>
        private static void OnThemeChanged()
        {
            try
            {
                ModHooks.Guard(CoreId, "ThemeRebinder/OnThemeChanged", ModUI.ReapplyTheme);
            }
            catch (Exception)
            {
                // Guard does not throw; this is the last line of defence for a call that comes from the game.
            }
        }
    }
}
