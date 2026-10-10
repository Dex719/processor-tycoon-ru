using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using PTMod.Core.Logic.Ui;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PTMod.Core
{
    /// <summary>
    /// The bridge to the game's tooltips (FR-16 item 9): the <c>Tooltip</c> half of the <c>partial</c> class, next to <c>Confirm</c> in <c>ConfirmDialog.cs</c>.
    /// Features never talk to <c>TooltipHandler</c> themselves; they call these four members.
    /// <list type="bullet">
    /// <item><b>On an element.</b> <see cref="Tooltip(GameObject, string, string)"/> (a fixed text) and <see cref="Tooltip(GameObject, Func{TooltipText})"/> (the provider is called each time the
    /// pointer enters the element, so the text can show the state of the moment) hang a hover handler on the object. The game's tooltip appears after its own delay, in its own look
    /// and at the pointer, and goes when the pointer leaves, the object is hidden or destroyed. The object needs a <c>Graphic</c> with Raycast Target in it (a button has one).</item>
    /// <item><b>Without an element.</b> <see cref="ShowTooltip"/> asks for a tooltip at the pointer now (the research tree shows one for a node that is not a UI element of its own),
    /// <see cref="HideTooltip"/> cancels whatever tooltip is waiting or open.</item>
    /// <item><b>Data.</b> The game's <c>TooltipData</c> is a ScriptableObject; one is made per element on its first hover (and one for <see cref="ShowTooltip"/>) and kept, its private
    /// setters set through reflection. The delay and the width stay the game's own unless <see cref="ShowTooltip"/> is given a delay.</item>
    /// <item><b>No tooltip, no problem.</b> Without a <c>TooltipHandler</c> (the main menu), before the game's <c>Tooltip</c> object has started, or when the game's own call fails, nothing is shown and nothing
    /// throws: the failure is a log line, and three failures in a session turn the tooltips of the mod off. A <c>Tooltip</c> that has not started would make the game throw
    /// every frame, so the bridge does not start a trigger then.</item>
    /// <item><b>Check in the game</b> (owner; the game is never started from the tools): in the game scene hover a button given a provider: the provider runs on enter, the tooltip
    /// shows after the usual delay and closes on leave; in the main menu hover the same kind of element: nothing shows and <c>Player.log</c> has no error.</item>
    /// </list>
    /// Main thread only.
    /// </summary>
    public static partial class ModUI
    {
        /// <summary>The text of one tooltip: the header line (may be empty) and the body.</summary>
        public struct TooltipText
        {
            /// <summary>The header line; empty (or null) hides the header line of the game's tooltip.</summary>
            public string Header;

            /// <summary>The body text; the game's own tokens in it (<c>#nodeList</c>, <c>#contractList</c>) are expanded by the game.</summary>
            public string Content;

            /// <summary>Both parts at once.</summary>
            public TooltipText(string header, string content)
            {
                Header = header;
                Content = content;
            }
        }

        /// <summary>
        /// A fixed tooltip on an element. Putting a second one on the same element replaces the first (the <c>TooltipData</c> is kept); with two empty texts nothing is shown.
        /// A null object is a log line, not an exception.
        /// </summary>
        /// <param name="target">The element the pointer hovers; it needs a graphic with Raycast Target in it or below it.</param>
        /// <param name="header">The header line; may be empty.</param>
        /// <param name="content">The body text.</param>
        public static void Tooltip(GameObject target, string header, string content)
        {
            ModTooltipTrigger trigger = TipTrigger(target);
            if (trigger != null) trigger.SetStatic(header, content);
        }

        /// <summary>
        /// A tooltip whose text is made when the pointer enters the element: <paramref name="provider"/> runs under the guard of the feature it belongs to (an exception is counted against it and
        /// nothing is shown). Putting a second tooltip on the same element replaces this one. A null provider is a log line.
        /// </summary>
        /// <param name="target">The element the pointer hovers; it needs a graphic with Raycast Target in it or below it.</param>
        /// <param name="provider">Makes the text each time the pointer enters; runs on the main thread, keep it cheap.</param>
        public static void Tooltip(GameObject target, Func<TooltipText> provider)
        {
            if (provider == null)
            {
                ModLog.Warn(UiCoreId, "ui: Tooltip needs a text provider; nothing is set");
                return;
            }

            ModTooltipTrigger trigger = TipTrigger(target);
            if (trigger != null) trigger.SetProvider(provider, ModFeatures.OwnerOf(provider));
        }

        /// <summary>
        /// Asks the game to show a tooltip at the pointer, after its own delay unless <paramref name="delayOverride"/> says otherwise (seconds, 0 or more). It replaces a tooltip that is waiting.
        /// Two empty texts show nothing. Cancel it with <see cref="HideTooltip"/>. Nothing is shown (and nothing thrown) where the game has no tooltip.
        /// </summary>
        /// <param name="header">The header line; may be empty.</param>
        /// <param name="content">The body text.</param>
        /// <param name="position">Where the game puts the tooltip relative to the pointer (its own <c>PositionPreference</c>).</param>
        /// <param name="delayOverride">Seconds to wait before the tooltip appears; null keeps the game's own delay.</param>
        public static void ShowTooltip(string header, string content,
            ProcessorTycoon.TooltipSystem.Tooltip.PositionPreference position = default(ProcessorTycoon.TooltipSystem.Tooltip.PositionPreference), float? delayOverride = null)
        {
            try
            {
                TooltipBridge.ShowShared(header, content, (int)position, delayOverride);
            }
            catch (Exception ex)
            {
                TooltipBridge.Fail("ShowTooltip", ex);
            }
        }

        /// <summary>Cancels the tooltip that is waiting or open (the game's <c>CancelTrigger</c>). Does nothing, and throws nothing, where there is none.</summary>
        public static void HideTooltip()
        {
            try
            {
                TooltipBridge.Hide();
            }
            catch (Exception ex)
            {
                TooltipBridge.Fail("HideTooltip", ex);
            }
        }

        // The hover handler of the object, made on the first request and reused after it (one TooltipData per element).
        private static ModTooltipTrigger TipTrigger(GameObject target)
        {
            try
            {
                if (target == null)
                {
                    ModLog.Warn(UiCoreId, "ui: Tooltip needs an object to hang on; nothing is set");
                    return null;
                }

                ModTooltipTrigger trigger = target.GetComponent<ModTooltipTrigger>();
                if (trigger != null) return trigger;

                trigger = target.AddComponent<ModTooltipTrigger>();
                if (ModSettings.VerboseLog) TipCheckRaycast(target);
                return trigger;
            }
            catch (Exception ex)
            {
                ModLog.Error(UiCoreId, "ui: cannot put a tooltip on the object", ex);
                return null;
            }
        }

        // The pointer reaches the handler through a graphic that takes raycasts, on the object or below it; without one the tooltip never shows.
        private static void TipCheckRaycast(GameObject target)
        {
            foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.raycastTarget) return;
            }
            ModLog.Debug(UiCoreId, "ui: '" + target.name + "' has a tooltip but no graphic with Raycast Target; the pointer cannot reach it");
        }
    }

    /// <summary>
    /// The game side of the bridge: <c>TooltipHandler</c> and <c>TooltipData</c>. The game's types are touched only in the small <c>NoInlining</c> methods below: if they do not match the
    /// game's code of another version the JIT fails there and the callers catch it; every failure is counted (<see cref="MaxFailures"/> turn the bridge off) and logged.
    /// <see cref="Show"/> and <see cref="Hide"/> are also what the hover tooltip of the widgets (<c>ModHoverTooltip</c> in <c>Widgets.cs</c>) can use instead of a bridge of its own.
    /// </summary>
    internal static class TooltipBridge
    {
        private const int MaxFailures = 3;
        private const string CoreId = ModLog.CoreFeatureId;

        // Reflection handles, resolved on first use (Harmony's AccessTools finds the private members).
        private static MethodInfo _setHeader;
        private static MethodInfo _setContent;
        private static MethodInfo _setDelay;
        private static MethodInfo _setWidth;
        private static bool _settersResolved;
        private static MethodInfo _currentTooltipGetter;
        private static FieldInfo _delayedField;
        private static bool _probed;

        private static float _nativeDelay = 0.15f;   // what a new TooltipData has; read from the first one made
        private static int _failures;
        private static bool _notStartedReported;
        private static ScriptableObject _shared;      // the data of ShowTooltip

        /// <summary>True once <see cref="MaxFailures"/> calls into the game failed: the bridge does nothing until the game restarts.</summary>
        internal static bool IsOff
        {
            get { return _failures >= MaxFailures; }
        }

        /// <summary>
        /// Shows <paramref name="data"/>'s tooltip: makes the data on first use (kept in the caller's slot), sets its texts, delay and width, and starts the game's trigger.
        /// False when nothing was started: the bridge is off, the game has no tooltip here (no handler, its <c>Tooltip</c> not started) or the call failed (counted, logged).
        /// </summary>
        /// <param name="data">The caller's slot for the <c>TooltipData</c> (one per element); empty on the first call, filled by it.</param>
        /// <param name="header">The header line; null counts as empty.</param>
        /// <param name="content">The body text; null counts as empty (the game's tooltip dereferences it).</param>
        /// <param name="position">The game's <c>Tooltip.PositionPreference</c> as a number (0 left, 1 right).</param>
        /// <param name="delay">Seconds before the tooltip appears; null keeps the game's own delay.</param>
        /// <param name="width">Preferred width in canvas units; 0 keeps the tooltip's base width.</param>
        internal static bool Show(ref ScriptableObject data, string header, string content, int position, float? delay, int width = 0)
        {
            if (IsOff) return false;

            try
            {
                if (!GameTooltipReady()) return false;

                if (data == null) data = NewData();
                ApplyData(data, header ?? string.Empty, content ?? string.Empty, delay, width);
                return StartTrigger(data, position);
            }
            catch (Exception ex)
            {
                Fail("show", ex);
                return false;
            }
        }

        /// <summary>The tooltip of <see cref="ModUI.ShowTooltip"/>: one data object for all of them, refilled on each call.</summary>
        internal static void ShowShared(string header, string content, int position, float? delay)
        {
            if (string.IsNullOrEmpty(header) && string.IsNullOrEmpty(content)) return;
            Show(ref _shared, header, content, position, delay);
        }

        /// <summary>Cancels the waiting or open tooltip. Safe to call when none was started: the game's <c>CancelTrigger</c> throws then, so it is not called.</summary>
        internal static void Hide()
        {
            if (IsOff) return;

            try
            {
                CancelTrigger();
            }
            catch (Exception ex)
            {
                Fail("hide", ex);
            }
        }

        /// <summary>Destroys the data an element made (its object is going away) and empties the slot.</summary>
        internal static void Release(ref ScriptableObject data)
        {
            ScriptableObject owned = data;
            data = null;
            if (owned != null) UnityEngine.Object.Destroy(owned);
        }

        /// <summary>A call into the game failed: a log line, and the count that switches the bridge off at <see cref="MaxFailures"/>.</summary>
        internal static void Fail(string site, Exception ex)
        {
            _failures++;
            ModLog.Error(CoreId, "ui: the tooltip of the game failed at " + site + " (" + _failures + " of " + MaxFailures
                                 + (IsOff ? "; the tooltips of the mod are off until the game restarts)" : " before the tooltips of the mod are off)"), ex);
        }

        // ---- the game's side ----

        // The handler exists and its Tooltip object has registered itself: otherwise the game's Update would throw every frame for a trigger we started.
        private static bool GameTooltipReady()
        {
            bool ready = HandlerReady(out bool handlerExists);
            if (!ready && handlerExists && !_notStartedReported)
            {
                _notStartedReported = true;
                ModLog.Debug(CoreId, "ui: the game's Tooltip object has not started yet; no tooltip is asked for until it has");
            }
            return ready;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool HandlerReady(out bool handlerExists)
        {
            ProcessorTycoon.TooltipSystem.TooltipHandler handler = ProcessorTycoon.TooltipSystem.TooltipHandler.Instance;
            handlerExists = handler != null;
            return handlerExists && TooltipRegistered(handler);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ScriptableObject NewData()
        {
            ProcessorTycoon.TooltipSystem.TooltipData data = ScriptableObject.CreateInstance<ProcessorTycoon.TooltipSystem.TooltipData>();
            data.hideFlags = HideFlags.DontUnloadUnusedAsset;   // nothing in the scene refers to it; the cleanup after a scene load must leave it alone
            _nativeDelay = data.Delay;
            return data;
        }

        // Header, Content, Delay and PreferedWidth have private setters (the game fills them from the asset).
        private static void ApplyData(ScriptableObject data, string header, string content, float? delay, int width)
        {
            ResolveSetters();
            float seconds = delay.HasValue && !float.IsNaN(delay.Value) ? Math.Max(0f, delay.Value) : _nativeDelay;

            _setHeader.Invoke(data, new object[] { header });
            _setContent.Invoke(data, new object[] { content });
            _setDelay.Invoke(data, new object[] { seconds });
            _setWidth.Invoke(data, new object[] { Math.Max(0, width) });
        }

        private static void ResolveSetters()
        {
            if (_settersResolved) return;

            Type type = DataType();
            _setHeader = RequireSetter(type, "Header");
            _setContent = RequireSetter(type, "Content");
            _setDelay = RequireSetter(type, "Delay");
            _setWidth = RequireSetter(type, "PreferedWidth");   // sic, the game's spelling
            _settersResolved = true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Type DataType()
        {
            return typeof(ProcessorTycoon.TooltipSystem.TooltipData);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static MethodInfo RequireSetter(Type type, string property)
        {
            MethodInfo setter = HarmonyLib.AccessTools.PropertySetter(type, property);
            if (setter == null) throw new MissingMemberException(type.FullName, property);
            return setter;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool StartTrigger(ScriptableObject data, int position)
        {
            ProcessorTycoon.TooltipSystem.TooltipHandler handler = ProcessorTycoon.TooltipSystem.TooltipHandler.Instance;
            if (handler == null) return false;   // the scene went between the check and now

            handler.StartTrigger((ProcessorTycoon.TooltipSystem.TooltipData)data, (ProcessorTycoon.TooltipSystem.Tooltip.PositionPreference)position, new List<int>());
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CancelTrigger()
        {
            ProcessorTycoon.TooltipSystem.TooltipHandler handler = ProcessorTycoon.TooltipSystem.TooltipHandler.Instance;
            if (handler == null) return;
            if (!TooltipRegistered(handler) || !TriggerStarted(handler)) return;   // CancelTrigger dereferences both

            handler.CancelTrigger();
        }

        // The game's handler keeps its Tooltip in a private property that the Tooltip sets in its own Start. Unknown members (another game version) count as "fine": the call is guarded anyway.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TooltipRegistered(ProcessorTycoon.TooltipSystem.TooltipHandler handler)
        {
            Probe();
            if (_currentTooltipGetter == null) return true;

            var tooltip = _currentTooltipGetter.Invoke(handler, null) as UnityEngine.Object;
            return tooltip != null;
        }

        // The handler's private record of the trigger it last started; null until the first StartTrigger.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TriggerStarted(ProcessorTycoon.TooltipSystem.TooltipHandler handler)
        {
            Probe();
            return _delayedField == null || _delayedField.GetValue(handler) != null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Probe()
        {
            if (_probed) return;
            _probed = true;

            try
            {
                Type type = typeof(ProcessorTycoon.TooltipSystem.TooltipHandler);
                _currentTooltipGetter = HarmonyLib.AccessTools.PropertyGetter(type, "CurrentTooltip");
                _delayedField = HarmonyLib.AccessTools.Field(type, "delayedTooltip");
            }
            catch (Exception ex)
            {
                ModLog.Warn(CoreId, "ui: cannot inspect the game's TooltipHandler (" + ex.GetType().Name + "); the bridge relies on its own error handling");
            }

            if (_currentTooltipGetter == null || _delayedField == null)
                ModLog.Warn(CoreId, "ui: TooltipHandler has no " + (_currentTooltipGetter == null ? "CurrentTooltip" : "delayedTooltip") + " in this game version; the bridge relies on its own error handling");
        }
    }

    /// <summary>
    /// The hover handler <see cref="ModUI.Tooltip(GameObject, string, string)"/> hangs on an element: on pointer enter the text is read (a fixed one, or the provider is called) and the game's tooltip is asked
    /// for, on pointer exit or when the element is hidden it is cancelled. The rules are the pure <see cref="TooltipHover"/>; the <c>TooltipData</c> of the element is made on its first hover and
    /// destroyed with it. Nothing here throws into Unity's event system.
    /// </summary>
    internal sealed class ModTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private ElementHost _host;
        private TooltipHover _hover;
        private string _header = string.Empty;
        private string _content = string.Empty;
        private Func<ModUI.TooltipText> _provider;
        private string _owner = ModLog.CoreFeatureId;

        internal void SetStatic(string header, string content)
        {
            Ensure();
            _provider = null;
            _owner = ModLog.CoreFeatureId;
            _header = header ?? string.Empty;
            _content = content ?? string.Empty;
        }

        internal void SetProvider(Func<ModUI.TooltipText> provider, string owner)
        {
            Ensure();
            _provider = provider;
            _owner = string.IsNullOrEmpty(owner) ? ModLog.CoreFeatureId : owner;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            try
            {
                if (_hover != null) _hover.PointerEntered();
            }
            catch (Exception ex)
            {
                TooltipBridge.Fail("hover", ex);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Leave();
        }

        private void OnDisable()
        {
            Leave();   // hidden while the pointer was over it: no exit event will come
        }

        private void OnDestroy()
        {
            Leave();
            if (_host != null) _host.Release();
        }

        private void Leave()
        {
            try
            {
                if (_hover != null) _hover.PointerExited();
            }
            catch (Exception ex)
            {
                TooltipBridge.Fail("leave", ex);
            }
        }

        private void Ensure()
        {
            if (_hover != null) return;

            _host = new ElementHost();
            _hover = new TooltipHover(_host, ReadText);
        }

        // The text for this hover: the fixed one, or the provider's (under the guard of its feature; a provider that throws or is skipped shows nothing).
        private bool ReadText(out string header, out string content)
        {
            header = _header;
            content = _content;

            Func<ModUI.TooltipText> provider = _provider;
            if (provider == null) return true;

            ModUI.TooltipText text = default(ModUI.TooltipText);
            bool ran = ModHooks.Guard(_owner, "ModUI.Tooltip/provider", () => { text = provider(); });
            header = text.Header;
            content = text.Content;
            return ran;
        }

        /// <summary>The element's side of the game's tooltip: its own <c>TooltipData</c>, made on the first request.</summary>
        private sealed class ElementHost : ITooltipHost
        {
            private ScriptableObject _data;

            public bool Start(string header, string content)
            {
                return TooltipBridge.Show(ref _data, header, content, 0, null);
            }

            public void Cancel()
            {
                TooltipBridge.Hide();
            }

            public void Release()
            {
                TooltipBridge.Release(ref _data);
            }
        }
    }
}

namespace PTMod.Core.Logic.Ui
{
    // ---------------------------------------------------------------------------------------------------------------------------------
    // The hover rules of an element tooltip without Unity (nothing below this line may touch the engine, the game or the log): at integration
    // this block moves to Core/Logic/Ui/TooltipHover.cs unchanged, where the test runner reaches it.
    // ---------------------------------------------------------------------------------------------------------------------------------
    using System;

    /// <summary>The game's tooltip as the hover rules see it.</summary>
    internal interface ITooltipHost
    {
        /// <summary>Asks the game to show the tooltip (after its own delay). False when it could not be asked: no tooltip in this scene, the bridge is off, the call failed.</summary>
        bool Start(string header, string content);

        /// <summary>Cancels the tooltip that was asked for. Called only after a <see cref="Start"/> that returned true.</summary>
        void Cancel();
    }

    /// <summary>Reads the text of one hover; false when there is nothing to show (a provider that failed, for one).</summary>
    internal delegate bool TooltipTextSource(out string header, out string content);

    /// <summary>
    /// What the pointer does to one element's tooltip (FR-16 item 9, AC-16.4): entering reads the text (the provider runs on every enter, not before) and asks the game for the tooltip once;
    /// leaving cancels it, but only if it was asked for, so a hidden element, a second leave or a scene without tooltips never reaches the game's <c>CancelTrigger</c>.
    /// Exceptions of the source or the host propagate to the caller (the Unity handler counts them); the state stays consistent.
    /// </summary>
    internal sealed class TooltipHover
    {
        private readonly ITooltipHost _host;
        private readonly TooltipTextSource _source;

        public TooltipHover(ITooltipHost host, TooltipTextSource source)
        {
            if (host == null) throw new ArgumentNullException("host");
            if (source == null) throw new ArgumentNullException("source");

            _host = host;
            _source = source;
        }

        /// <summary>The tooltip was asked for and not yet cancelled.</summary>
        public bool Showing { get; private set; }

        /// <summary>The pointer entered the element.</summary>
        public void PointerEntered()
        {
            if (Showing) return;   // already asked for; the provider is not called again until the pointer has left

            string header;
            string content;
            if (!_source(out header, out content)) return;

            header = header ?? string.Empty;
            content = content ?? string.Empty;
            if (header.Length == 0 && content.Length == 0) return;   // nothing to say, nothing to show

            Showing = _host.Start(header, content);
        }

        /// <summary>The pointer left the element, or the element was hidden or destroyed.</summary>
        public void PointerExited()
        {
            if (!Showing) return;

            Showing = false;   // first: a failing Cancel must not leave the element believing a tooltip is up
            _host.Cancel();
        }
    }
}
