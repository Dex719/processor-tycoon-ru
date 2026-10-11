using System;
using PTMod.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The component on a button of the tree that counts how long the cursor stays on it (FR-3 item 4, decision D4): after
    /// <see cref="Seconds"/> it calls <see cref="OnSeen"/> once, and a short pass of the cursor calls nothing. The rule is the pure
    /// <see cref="HoverSeenTimer"/>; this class only feeds it the enter, the exit and the frame time (real time, so a paused game counts too).
    /// It does not touch the tooltip: the tooltip is <c>ModUI.Tooltip</c> on the same object and works alone. A component that is disabled
    /// receives no pointer events in Unity, so it stays enabled and its <c>Update</c> returns at once while the cursor is elsewhere; it is
    /// destroyed with <see cref="Remove"/> when the feature is switched off.
    /// </summary>
    internal sealed class HoverSeenTracker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const string Site = "HoverSeenTracker/seen";

        private HoverSeenTimer _timer;

        /// <summary><c>Technology.ID</c> of the technology on this button.</summary>
        public int TechnologyId { get; private set; }

        /// <summary>How long the cursor must stay (<c>hoverSeenSeconds</c> of <c>ui.json</c>).</summary>
        public float Seconds { get; private set; }

        /// <summary>Called once with the id of the technology when the cursor has stayed long enough; runs under the guard of the feature.</summary>
        public Action<int> OnSeen { get; set; }

        /// <summary>Puts a tracker on the object (or sets the existing one anew). Returns it, or null for a null object.</summary>
        public static HoverSeenTracker Attach(GameObject target, int technologyId, float seconds, Action<int> onSeen)
        {
            if (target == null) return null;

            HoverSeenTracker tracker = target.GetComponent<HoverSeenTracker>();
            if (tracker == null) tracker = target.AddComponent<HoverSeenTracker>();
            tracker.Configure(technologyId, seconds, onSeen);
            return tracker;
        }

        /// <summary>Takes the tracker off the object (the feature is switched off). Does nothing when there is none.</summary>
        public static void Remove(GameObject target)
        {
            if (target == null) return;

            HoverSeenTracker tracker = target.GetComponent<HoverSeenTracker>();
            if (tracker != null) Destroy(tracker);
        }

        private void Configure(int technologyId, float seconds, Action<int> onSeen)
        {
            TechnologyId = technologyId;
            Seconds = seconds;
            OnSeen = onSeen;
            _timer = new HoverSeenTimer(seconds);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_timer != null) _timer.Enter();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_timer != null) _timer.Exit();
        }

        private void OnDisable()
        {
            if (_timer != null) _timer.Exit();   // hidden under the cursor (another tab): no exit event will come
        }

        private void Update()
        {
            if (_timer == null || !_timer.IsHovering) return;
            if (!_timer.Advance(Time.unscaledDeltaTime)) return;

            Action<int> seen = OnSeen;
            if (seen == null) return;

            int id = TechnologyId;
            ModHooks.Guard(ResearchTreeUxFeature.FeatureId, Site, () => seen(id));
        }
    }
}
