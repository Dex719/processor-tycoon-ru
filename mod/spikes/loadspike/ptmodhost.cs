// Load spike (spike 0001, TSK-005): the persistent host object. Checks the second half of the question: a
// MonoBehaviour from a DLL that Unity did not know at build time can be added to a GameObject, gets Awake/Start/
// Update, survives scene loads (DontDestroyOnLoad) and receives sceneLoaded, so it can order "Init before level0".
// Same shape as the real PTModHost (design.md, "ModBootstrap, PTModHost"), reduced to log lines.

using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PTMod.Core
{
    public sealed class PTModHost : MonoBehaviour
    {
        // Enough to see level0 (main menu) and level1 (game) and a return to the menu; keeps Player.log short.
        private const int MaxSceneLines = 8;

        private static PTModHost _instance;
        private static int _awakeCalls;

        private int _frames;
        private int _sceneLines;
        private int _scenesSeen;

        public static PTModHost Instance { get { return _instance; } }

        /// <summary>Creates the single host object. Called from ModBootstrap.Init, never throws.</summary>
        internal static void Create()
        {
            try
            {
                if (_instance != null)
                {
                    Debug.Log(ModBootstrap.LogTag + "host: already exists, not created again");
                    return;
                }

                var go = new GameObject("PTModHost");
                DontDestroyOnLoad(go);
                var host = go.AddComponent<PTModHost>();     // Awake runs inside this call
                Debug.Log(ModBootstrap.LogTag + "host: created=" + (host != null) + ", awakeCalls=" + _awakeCalls);
            }
            catch (Exception e)
            {
                try { Debug.LogError(ModBootstrap.LogTag + "host: creation failed: " + e); }
                catch (Exception) { /* nothing left to do */ }
            }
        }

        private void Awake()
        {
            try
            {
                _awakeCalls++;
                if (_instance != null && _instance != this)
                {
                    Debug.Log(ModBootstrap.LogTag + "host: duplicate Awake, destroying the extra object");
                    Destroy(gameObject);
                    return;
                }

                _instance = this;
                SceneManager.sceneLoaded += OnSceneLoaded;
                Debug.Log(ModBootstrap.LogTag + "host: Awake, frame=" + Time.frameCount + ", sceneCount=" + SceneManager.sceneCount);
            }
            catch (Exception e)
            {
                LogError("Awake", e);
            }
        }

        private void Start()
        {
            try { Debug.Log(ModBootstrap.LogTag + "host: Start, frame=" + Time.frameCount); }
            catch (Exception e) { LogError("Start", e); }
        }

        private void Update()
        {
            _frames++;
            if (_frames != 1) return;
            try { Debug.Log(ModBootstrap.LogTag + "host: first Update, frame=" + Time.frameCount); }
            catch (Exception e) { LogError("Update", e); }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                _scenesSeen++;
                if (++_sceneLines > MaxSceneLines) return;
                Debug.Log(ModBootstrap.LogTag + "scene loaded: buildIndex=" + scene.buildIndex + ", name=" + scene.name
                    + ", mode=" + mode + ", hostFrames=" + _frames);
            }
            catch (Exception e)
            {
                LogError("sceneLoaded", e);
            }
        }

        private void OnApplicationQuit()
        {
            try
            {
                Debug.Log(ModBootstrap.LogTag + "host: quit, frames=" + _frames + ", sceneLoadsSeen=" + _scenesSeen);
            }
            catch (Exception e)
            {
                LogError("quit", e);
            }
        }

        private void OnDestroy()
        {
            try
            {
                if (_instance == this)
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    _instance = null;
                }
                // At application quit this is normal; any earlier one means DontDestroyOnLoad did not hold.
                Debug.Log(ModBootstrap.LogTag + "host: destroyed, frames=" + _frames);
            }
            catch (Exception)
            {
                // The game may be shutting down; do not throw from OnDestroy.
            }
        }

        private static void LogError(string where, Exception e)
        {
            try { Debug.LogError(ModBootstrap.LogTag + "host: " + where + " failed: " + e); }
            catch (Exception) { /* nothing left to do */ }
        }
    }
}
