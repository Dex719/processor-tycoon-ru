using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PTMod.Core
{
    /// <summary>
    /// The mod's one persistent object (FR-1 item 2): a <c>DontDestroyOnLoad</c> GameObject created by <see cref="ModBootstrap.Init"/>. It
    /// turns Unity's callbacks into the mod's lifecycle:
    /// <list type="bullet">
    /// <item><see cref="SceneReady"/> - one frame after <c>SceneManager.sceneLoaded</c> of the main menu (0) or the game scene (1), when the
    /// game's <c>Start</c> methods have run (D12). Core services hear it first, then the features.</item>
    /// <item><see cref="SceneLeaving"/> - when <c>SceneTransitionHandler.OnTransitionsStarted</c> fires (new game, load, back to the menu),
    /// before the scene goes away; features hear it first, then the core. If a scene ever changes without that event, it is raised late,
    /// on the next <c>sceneLoaded</c>, so every ready scene gets exactly one. Then the profile is written if it changed and the feature
    /// switches that waited for the scene change take effect.</item>
    /// <item>The pulse: <c>ModLog.Pump</c> and <c>ModProfile.Pump</c> every frame (repeat summaries, the throttled profile write),
    /// <c>ModProfile.Flush</c> on <c>OnApplicationPause</c>, and on <c>OnApplicationQuit</c> a forced <c>ModProfile.Save</c>, the settings
    /// and the log summaries.</item>
    /// <item><see cref="Run"/> - coroutines for the features; an exception inside one is caught and counted against its owner, and a
    /// coroutine of a feature that is no longer active stops at its next step.</item>
    /// </list>
    /// Handlers of a feature that is not active are skipped (a switched-off feature drops its scene references in <c>OnDisabled</c>). Nothing
    /// here throws into Unity or the game. The class is partial so core services can add host-side code next to their own.
    /// </summary>
    public sealed partial class PTModHost : MonoBehaviour
    {
        /// <summary>Build index of the main menu scene (<c>level0</c>).</summary>
        public const int MainMenuScene = 0;

        /// <summary>Build index of the game scene (<c>level1</c>); loading a save reloads it.</summary>
        public const int GameScene = 1;

        private const string CoreId = ModLog.CoreFeatureId;

        private static readonly object HandlerSync = new object();

        private static PTModHost _instance;
        private static Action[] _frameHandlers = new Action[0];
        private static bool _shuttingDown;
        private static bool _quitting;

        private static int _currentScene = -1;
        private static bool _sceneReady;
        private static int _sceneSerial;          // a scene load or leave cancels the SceneReady of the previous load that is still waiting
        private static bool _sawSceneLoad;
        private static bool _hasSceneHandle;
        private static int _lastSceneHandle;
        private static int _loadedFrame;

        // Kept as a plain MonoBehaviour so that the type of this class does not depend on the game's class; see SubscribeTransitions.
        private static MonoBehaviour _transitions;
        private static bool _transitionsBroken;

        /// <summary>The host, or null before the mod started (and after a failed start).</summary>
        public static PTModHost Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// The main menu (0) or the game scene (1) is loaded and its objects have started: (re)subscribe to the scene's singletons, build UI.
        /// Raised one frame after <c>sceneLoaded</c>; also after loading a save, which reloads scene 1. A feature switched on in the middle
        /// of a scene does not get it - check <see cref="CurrentScene"/> and <see cref="IsSceneReady"/> in <c>OnEnabled</c>.
        /// </summary>
        public static event Action<int> SceneReady;

        /// <summary>The scene is about to go (<c>SceneTransitionHandler.OnTransitionsStarted</c>): close the mod's windows, drop references to the scene's objects.</summary>
        public static event Action SceneLeaving;

        /// <summary>Build index of the current scene from its load until <see cref="SceneLeaving"/>; -1 before the first scene and between scenes.</summary>
        public static int CurrentScene
        {
            get { return _currentScene; }
        }

        /// <summary><see cref="SceneReady"/> was raised for <see cref="CurrentScene"/>.</summary>
        public static bool IsSceneReady
        {
            get { return _sceneReady; }
        }

        // ---- core services ----

        /// <summary>
        /// Every frame, after the log and profile pumps. For core services only; keep it cheap. A handler that throws is logged once and
        /// removed (a broken frame handler must not fail every frame).
        /// </summary>
        internal static event Action FrameUpdate
        {
            add
            {
                if (value == null) return;
                lock (HandlerSync)
                {
                    var list = new Action[_frameHandlers.Length + 1];
                    Array.Copy(_frameHandlers, list, _frameHandlers.Length);
                    list[list.Length - 1] = value;
                    _frameHandlers = list;
                }
            }
            remove
            {
                if (value == null) return;
                lock (HandlerSync)
                {
                    int index = Array.LastIndexOf(_frameHandlers, value);
                    if (index < 0) return;
                    var list = new Action[_frameHandlers.Length - 1];
                    Array.Copy(_frameHandlers, 0, list, 0, index);
                    Array.Copy(_frameHandlers, index + 1, list, index, _frameHandlers.Length - index - 1);
                    _frameHandlers = list;
                }
            }
        }

        /// <summary><c>OnApplicationQuit</c>, before the forced write of the profile and the settings.</summary>
        internal static event Action ApplicationQuitting;

        /// <summary><c>OnApplicationPause</c> (true: the game lost focus or was suspended), before the profile is flushed.</summary>
        internal static event Action<bool> ApplicationPaused;

        /// <summary><c>OnApplicationQuit</c> has been received.</summary>
        internal static bool IsQuitting
        {
            get { return _quitting; }
        }

        // ---- coroutines ----

        /// <summary>
        /// Starts a coroutine on the host (it survives scene loads). The owner is the feature of the routine's type (its namespace): an
        /// exception inside the routine stops it and is counted against the owner, and the routine stops at its next step once the owner is not
        /// active. Nested routines it yields are guarded the same way. Returns null when the host cannot run it.
        /// </summary>
        public Coroutine Run(IEnumerator routine)
        {
            if (routine == null) return null;

            string owner = CoreId;
            try
            {
                owner = ModFeatures.FeatureOfType(routine.GetType()) ?? CoreId;
                if (!isActiveAndEnabled)
                {
                    ModLog.Warn(owner, "host: the host is not active, the coroutine " + routine.GetType().Name + " is not started");
                    return null;
                }
                return StartCoroutine(Guard(routine, owner));
            }
            catch (Exception ex)
            {
                ModLog.Error(owner, "host: cannot start the coroutine " + routine.GetType().Name, ex);
                return null;
            }
        }

        /// <summary>Stops a coroutine started by <see cref="Run"/>; null is ignored.</summary>
        public void Stop(Coroutine coroutine)
        {
            if (coroutine == null) return;
            try
            {
                StopCoroutine(coroutine);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: cannot stop a coroutine", ex);
            }
        }

        private static IEnumerator Guard(IEnumerator routine, string owner)
        {
            try
            {
                while (true)
                {
                    if (!ModFeatures.IsActive(owner)) yield break;

                    bool more = false;
                    object current = null;
                    try
                    {
                        more = routine.MoveNext();
                        if (more) current = routine.Current;
                    }
                    catch (Exception ex)
                    {
                        ModFeatures.ReportFault(owner, "coroutine " + routine.GetType().Name, ex);
                        more = false;
                    }
                    if (!more) yield break;

                    // A yielded IEnumerator is a nested coroutine for Unity; guard it too. Unity's own wait objects pass through.
                    IEnumerator nested = current as IEnumerator;
                    if (nested != null && !(current is CustomYieldInstruction)) current = Guard(nested, owner);
                    yield return current;
                }
            }
            finally
            {
                var disposable = routine as IDisposable;
                if (disposable != null)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception)
                    {
                        // The routine's own finally blocks failed; it is over either way.
                    }
                }
            }
        }

        // ---- creation (ModBootstrap) ----

        /// <summary>Creates the single host (Awake runs inside); returns the existing one when called again, null when Unity refused.</summary>
        internal static PTModHost Create()
        {
            if (_instance != null) return _instance;

            var go = new GameObject("PTModHost");
            DontDestroyOnLoad(go);
            go.AddComponent<PTModHost>();
            if (_instance == null)
            {
                Destroy(go);
                return null;
            }
            return _instance;
        }

        /// <summary>The start failed: the host stops listening at once and its object is destroyed (no scene events, no pumps).</summary>
        internal static void Shutdown()
        {
            PTModHost host = _instance;
            if (host == null) return;

            _shuttingDown = true;
            try
            {
                SceneManager.sceneLoaded -= host.OnSceneLoaded;
                UnsubscribeTransitions();
                _instance = null;
                host.enabled = false;
                Destroy(host.gameObject);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: cannot shut down", ex);
            }
        }

        // ---- Unity callbacks ----

        private void Awake()
        {
            try
            {
                if (_instance != null && _instance != this)
                {
                    ModLog.Warn(CoreId, "host: a second PTModHost was created and is destroyed");
                    Destroy(gameObject);
                    return;
                }

                _instance = this;
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: Awake failed", ex);
            }
        }

        private void Start()
        {
            try
            {
                TrySubscribeTransitions();

                // Started after the first scene had loaded (a late Init): treat the current scene as just loaded.
                if (!_sawSceneLoad)
                {
                    Scene active = SceneManager.GetActiveScene();
                    if (active.IsValid() && active.isLoaded)
                    {
                        ModLog.Info(CoreId, "host: started inside scene " + active.buildIndex + ", treating it as loaded");
                        OnSceneLoaded(active, LoadSceneMode.Single);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: Start failed", ex);
            }
        }

        private void Update()
        {
            ModLog.Pump();
            ModProfile.Pump();

            Action[] handlers = _frameHandlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try
                {
                    handlers[i]();
                }
                catch (Exception ex)
                {
                    ModLog.Error(CoreId, "host: frame handler " + NameOf(handlers[i]) + " failed and is removed", ex);
                    FrameUpdate -= handlers[i];
                }
            }
        }

        private void OnApplicationPause(bool paused)
        {
            try
            {
                RaiseCore(ApplicationPaused, paused, "pause");
                if (paused) ModProfile.Flush();
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: OnApplicationPause failed", ex);
            }
        }

        private void OnApplicationQuit()
        {
            _quitting = true;
            try
            {
                Action handlers = ApplicationQuitting;
                if (handlers != null)
                {
                    foreach (Delegate handler in handlers.GetInvocationList())
                    {
                        try
                        {
                            ((Action)handler)();
                        }
                        catch (Exception ex)
                        {
                            ModLog.Error(CoreId, "host: quit handler " + NameOf(handler) + " failed", ex);
                        }
                    }
                }

                ModProfile.Save();
                ModSettings.Flush();
                ModLog.Info(CoreId, "host: quit");
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: OnApplicationQuit failed", ex);
            }
            ModLog.FlushAll();
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            try
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                UnsubscribeTransitions();
                _instance = null;
                if (!_quitting && !_shuttingDown)
                    ModLog.Error(CoreId, "host: PTModHost was destroyed while the game runs; scene events, coroutines and timed profile writes stop");
            }
            catch (Exception)
            {
                // The game may be shutting down; nothing to do.
            }
        }

        // ---- scenes ----

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (_hasSceneHandle && scene.handle == _lastSceneHandle) return;   // the same load seen twice (late start, see Start)
                _sawSceneLoad = true;
                _hasSceneHandle = true;
                _lastSceneHandle = scene.handle;

                if (mode == LoadSceneMode.Additive)
                {
                    ModLog.Debug(CoreId, "host: additive scene " + scene.buildIndex + " (" + scene.name + ") is ignored");
                    return;
                }

                // The game normally announces a scene change through SceneTransitionHandler; if it did not, SceneLeaving is still owed.
                if (_sceneReady) Leave("the scene changed without SceneTransitionHandler");

                TrySubscribeTransitions();

                int serial = ++_sceneSerial;
                int index = scene.buildIndex;
                _currentScene = index;
                _loadedFrame = Time.frameCount;
                if (index != MainMenuScene && index != GameScene)
                {
                    ModLog.Info(CoreId, "host: scene " + index + " (" + scene.name + ") loaded; it gets no SceneReady");
                    return;
                }

                StartCoroutine(ReadyNextFrame(serial, index));
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: sceneLoaded failed", ex);
            }
        }

        private IEnumerator ReadyNextFrame(int serial, int index)
        {
            yield return null;   // the scene's Start methods run in this frame (D12)
            if (serial != _sceneSerial) yield break;

            try
            {
                _sceneReady = true;
                TrySubscribeTransitions();
                ModLog.Info(CoreId, "host: scene " + index + " ready (loaded at frame " + _loadedFrame + ", ready at frame " + Time.frameCount + ")");
                RaiseSceneReady(index);
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: SceneReady failed", ex);
            }
        }

        /// <summary>The game starts a scene transition (called by the game: must not throw).</summary>
        private static void OnTransitionsStarted()
        {
            try
            {
                Leave("transition");
            }
            catch (Exception ex)
            {
                ModLog.Error(CoreId, "host: SceneLeaving failed", ex);
            }
        }

        private static void Leave(string cause)
        {
            bool wasReady = _sceneReady;
            int scene = _currentScene;
            _sceneSerial++;   // a SceneReady still waiting for its frame is cancelled
            _sceneReady = false;
            _currentScene = -1;

            if (wasReady)
            {
                ModLog.Info(CoreId, "host: leaving scene " + scene + " (" + cause + ")");
                RaiseSceneLeaving();
            }

            ModProfile.Flush();
            ModFeatures.ApplyPendingChanges();
        }

        /// <summary>Core handlers first (they subscribe to the scene and find the UI templates), then the features.</summary>
        private static void RaiseSceneReady(int index)
        {
            Action<int> handlers = SceneReady;
            if (handlers == null) return;

            Delegate[] list = handlers.GetInvocationList();
            for (int pass = 0; pass < 2; pass++)
            {
                bool corePass = pass == 0;
                foreach (Delegate handler in list)
                {
                    string owner = ModFeatures.OwnerOf(handler);
                    if ((owner == CoreId) != corePass || !ModFeatures.IsActive(owner)) continue;
                    try
                    {
                        ((Action<int>)handler)(index);
                    }
                    catch (Exception ex)
                    {
                        ModFeatures.ReportFault(owner, "PTModHost.SceneReady handler " + NameOf(handler), ex);
                    }
                }
            }
        }

        /// <summary>Features first (they close their windows), then the core services.</summary>
        private static void RaiseSceneLeaving()
        {
            Action handlers = SceneLeaving;
            if (handlers == null) return;

            Delegate[] list = handlers.GetInvocationList();
            for (int pass = 0; pass < 2; pass++)
            {
                bool corePass = pass == 1;
                foreach (Delegate handler in list)
                {
                    string owner = ModFeatures.OwnerOf(handler);
                    if ((owner == CoreId) != corePass || !ModFeatures.IsActive(owner)) continue;
                    try
                    {
                        ((Action)handler)();
                    }
                    catch (Exception ex)
                    {
                        ModFeatures.ReportFault(owner, "PTModHost.SceneLeaving handler " + NameOf(handler), ex);
                    }
                }
            }
        }

        // ---- SceneTransitionHandler (a game class) ----

        /// <summary>
        /// Subscribes to <c>SceneTransitionHandler.OnTransitionsStarted</c> of the current instance (a <c>DontDestroyOnLoad</c> singleton of
        /// level0; null before it). Called on every scene load, so a replaced instance is followed. If the game's class does not match (another
        /// game version), the error is logged once and SceneLeaving comes only from the sceneLoaded fallback.
        /// </summary>
        private static void TrySubscribeTransitions()
        {
            if (_transitionsBroken) return;
            try
            {
                SubscribeTransitions();
            }
            catch (Exception ex)
            {
                _transitionsBroken = true;
                ModLog.Error(CoreId, "host: cannot subscribe to SceneTransitionHandler.OnTransitionsStarted; SceneLeaving will come late, on the next scene load", ex);
            }
        }

        // The game's types are touched only in these two methods: if they do not match the game, the JIT fails here and the caller catches it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void SubscribeTransitions()
        {
            ProcessorTycoon.Menu.SceneTransitionHandler handler = ProcessorTycoon.Menu.SceneTransitionHandler.Instance;
            if (handler == null || ReferenceEquals(handler, _transitions)) return;

            UnsubscribeTransitions();
            handler.OnTransitionsStarted += OnTransitionsStarted;
            _transitions = handler;
            ModLog.Debug(CoreId, "host: subscribed to SceneTransitionHandler.OnTransitionsStarted");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void UnsubscribeTransitions()
        {
            if (ReferenceEquals(_transitions, null)) return;
            try
            {
                var previous = _transitions as ProcessorTycoon.Menu.SceneTransitionHandler;
                _transitions = null;
                if (!ReferenceEquals(previous, null)) previous.OnTransitionsStarted -= OnTransitionsStarted;
            }
            catch (Exception)
            {
                // A destroyed or mismatched instance: nothing left to remove.
            }
        }

        // ---- helpers ----

        private static void RaiseCore<T>(Action<T> handlers, T argument, string what)
        {
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<T>)handler)(argument);
                }
                catch (Exception ex)
                {
                    ModLog.Error(CoreId, "host: " + what + " handler " + NameOf(handler) + " failed", ex);
                }
            }
        }

        private static string NameOf(Delegate handler)
        {
            if (handler == null || handler.Method == null) return "?";
            Type type = handler.Method.DeclaringType;
            return (type == null ? "" : type.Name + ".") + handler.Method.Name;
        }
    }
}
