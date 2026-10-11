using System;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The clock behind "the cursor stayed on a new technology long enough" (FR-3 item 4): it counts the time the cursor is on the object and
    /// says once, when <c>hoverSeenSeconds</c> have passed, that the technology counts as seen. A short pass of the cursor over the button
    /// (leaving before the time is up) fires nothing and the count starts again at the next entry. The Unity side
    /// (<c>HoverSeenTracker</c>) only feeds it the enter, exit and frame times.
    /// </summary>
    public sealed class HoverSeenTimer
    {
        /// <summary>The time of <c>ui.json</c> when the file gives none (seconds).</summary>
        public const float DefaultSeconds = 0.5f;

        public HoverSeenTimer(float seconds)
        {
            Seconds = float.IsNaN(seconds) || seconds < 0f ? DefaultSeconds : seconds;
        }

        /// <summary>How long the cursor must stay (seconds, zero or more).</summary>
        public float Seconds { get; private set; }

        /// <summary>The cursor is on the object now.</summary>
        public bool IsHovering { get; private set; }

        /// <summary>The time has passed during the present stay (it fires once per stay).</summary>
        public bool HasFired { get; private set; }

        /// <summary>Seconds counted during the present stay.</summary>
        public float Elapsed { get; private set; }

        /// <summary>The cursor came onto the object: the count starts from zero.</summary>
        public void Enter()
        {
            IsHovering = true;
            HasFired = false;
            Elapsed = 0f;
        }

        /// <summary>The cursor left (or the object was hidden): nothing fires for a stay that was too short.</summary>
        public void Exit()
        {
            IsHovering = false;
            HasFired = false;
            Elapsed = 0f;
        }

        /// <summary>
        /// A frame passed. True exactly once per stay, in the call that makes the elapsed time reach <see cref="Seconds"/>; false when the
        /// cursor is not on the object, when it has fired already, and for a time that is negative or not a number.
        /// </summary>
        public bool Advance(float deltaSeconds)
        {
            if (!IsHovering || HasFired) return false;
            if (float.IsNaN(deltaSeconds) || deltaSeconds < 0f) return false;

            Elapsed += deltaSeconds;
            if (Elapsed < Seconds) return false;

            HasFired = true;
            return true;
        }
    }
}
