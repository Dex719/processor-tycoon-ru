// Load spike (spike 0001, TSK-005): does Unity 6 (Mono) load a DLL that is only listed in
// ScriptingAssemblies.json and call a method that is only listed in RuntimeInitializeOnLoads.json?
// Built as PTMod.dll together with ptmodhost.cs (assembly name PTMod, as in the real mod, so the registration is the
// real one): python mod/build.py --game-dir "<game dir>" --src mod/spikes/loadspike --out spikes/loadspike/PTMod.dll
// Installed and removed by tools/spike_install.py; the procedure for the owner is in readme.txt. The spike patches
// nothing and writes nothing but lines to Player.log; every line it writes starts with "[PTMod] spike ".

using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PTMod.Core
{
    /// <summary>
    /// Entry point with the same identity as the real mod (PTMod / PTMod.Core / ModBootstrap / Init), so the JSON entry the
    /// installer writes for the spike is exactly the entry the framework will use.
    /// </summary>
    public static class ModBootstrap
    {
        /// <summary>Prefix of every spike line in Player.log; tools/spike_install.py --status greps for it.</summary>
        internal const string LogTag = "[PTMod] spike ";

        private static int _calls;

        // The attribute documents the intent; the call itself comes from RuntimeInitializeOnLoads.json, which is what
        // the spike checks (the attribute alone has no effect for a DLL added after the game was built).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Init()
        {
            try
            {
                _calls++;
                if (_calls > 1)
                {
                    // Not an "Init" line on purpose: a launch must produce exactly one "spike Init" line.
                    Debug.Log(LogTag + "repeat: Init called again (call #" + _calls + "), ignored");
                    return;
                }

                // The line the acceptance criteria look for.
                Debug.Log(LogTag + "Init: unity=" + Application.unityVersion + ", mono=" + Environment.Version);

                // Context for the Evidence section: which assembly and runtime, and that no scene is loaded yet.
                var scene = SceneManager.GetActiveScene();
                Debug.Log(LogTag + "info: runtime=" + RuntimeName()
                    + ", assembly=" + typeof(ModBootstrap).Assembly.GetName().FullName
                    + ", frame=" + Time.frameCount
                    + ", sceneCount=" + SceneManager.sceneCount
                    + ", activeBuildIndex=" + scene.buildIndex
                    + ", activeLoaded=" + scene.isLoaded
                    + ", platform=" + Application.platform);

                ProbeHarmony();
                PTModHost.Create();
            }
            catch (Exception e)
            {
                try { Debug.LogError(LogTag + "Init failed: " + e); }
                catch (Exception) { /* nothing left to do */ }
            }
        }

        /// <summary>
        /// Can Mono find 0Harmony.dll next to PTMod.dll by itself? Loaded by name through reflection, so PTMod.dll has no
        /// reference to Harmony and its own loading does not depend on the answer. Creates a Harmony object (no patches).
        /// </summary>
        private static void ProbeHarmony()
        {
            try
            {
                var assembly = Assembly.Load("0Harmony");
                var type = assembly.GetType("HarmonyLib.Harmony", true);
                var instance = Activator.CreateInstance(type, "ptmod.spike.noop");
                Debug.Log(LogTag + "harmony: loaded " + assembly.GetName().Version + " from \"" + assembly.Location
                    + "\", instance created=" + (instance != null));
            }
            catch (Exception e)
            {
                Debug.Log(LogTag + "harmony: NOT loaded (" + e.GetType().Name + ": " + FirstLine(e.Message) + ")");
            }
        }

        private static string RuntimeName()
        {
            try
            {
                var mono = Type.GetType("Mono.Runtime");
                var method = mono?.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
                return method?.Invoke(null, null) as string ?? "unknown";
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end).TrimEnd();
        }
    }
}
