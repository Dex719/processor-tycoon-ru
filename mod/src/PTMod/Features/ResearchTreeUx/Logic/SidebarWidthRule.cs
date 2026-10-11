using System;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The arithmetic of the width of the side panel (FR-10, design: Component 11), without the game. The panel (<c>SideWindow</c>) docks to the
    /// right edge of the screen and slides between two positions, <c>visiblePosition</c> and <c>hiddenPosition</c>, which are the horizontal
    /// positions of its pivot. A wider panel must keep its right edge where it was when it is open (it grows to the left, which is the edge the
    /// player drags) and must hide exactly as far as before when it is shut (the closed panel is not allowed to peek out further than the game made
    /// it, FR-10 item 1). Both follow from one rule: the edges of a rectangle of width <c>W</c> whose pivot (0 = left edge, 1 = right edge) stands at
    /// <c>x</c> are <c>x - pivot * W</c> and <c>x + (1 - pivot) * W</c>, so a growth of <c>g</c> moves the open position by <c>-(1 - pivot) * g</c>
    /// and the shut position by <c>+pivot * g</c>.
    /// <para>
    /// Every width and every position here is in the units of the parent of the panel (the units the positions are written in); the adapter
    /// converts from the units of the panel when the panel is scaled. Nothing throws: a number that is none (NaN, infinity) counts as "no change".
    /// </para>
    /// </summary>
    public static class SidebarWidthRule
    {
        /// <summary>The factor of the original width.</summary>
        public const float NeutralFactor = 1.0f;

        /// <summary>The range of <c>ui.json</c> (<c>sidebar.widthMin</c>, <c>widthMax</c>) when the file says nothing: from the original width to 1.6 times it.</summary>
        public const float DefaultMin = 1.0f;

        public const float DefaultMax = 1.6f;

        /// <summary>The bounds no file may widen: the panel is never narrower than the game made it and never above twice its width (the design's validation).</summary>
        public const float AbsoluteMin = 1.0f;

        public const float AbsoluteMax = 2.0f;

        /// <summary>A factor this close to 1 is the original width: the panel is left exactly as the game made it.</summary>
        public const float NeutralTolerance = 0.001f;

        /// <summary>
        /// The factor inside <paramref name="min"/>...<paramref name="max"/> (themselves held inside <see cref="AbsoluteMin"/>...<see cref="AbsoluteMax"/>;
        /// a pair in the wrong order is swapped). A factor that is none gives the original width, 1, held inside the range.
        /// </summary>
        public static float Clamp(float factor, float min, float max)
        {
            float low = Bound(min, DefaultMin);
            float high = Bound(max, DefaultMax);
            if (high < low)
            {
                float swap = low;
                low = high;
                high = swap;
            }

            float value = IsNumber(factor) ? factor : NeutralFactor;
            return Math.Max(low, Math.Min(high, value));
        }

        /// <summary>The width of the panel after the factor <paramref name="factor"/> (already inside the range): <c>original * factor</c>. An original that is not positive gives 0.</summary>
        public static float Width(float originalWidth, float factor)
        {
            if (!IsNumber(originalWidth) || originalWidth <= 0f) return 0f;
            return originalWidth * (IsNumber(factor) ? Math.Max(0f, factor) : NeutralFactor);
        }

        /// <summary>How much wider (positive) or narrower the panel became: <see cref="Width"/> minus the original.</summary>
        public static float Growth(float originalWidth, float factor)
        {
            float original = IsNumber(originalWidth) && originalWidth > 0f ? originalWidth : 0f;
            return Width(originalWidth, factor) - original;
        }

        /// <summary>
        /// The position of the open panel: the original one moved so that the right edge stays where it was. For a pivot on the right edge (1, the
        /// position names the edge itself) nothing moves.
        /// </summary>
        public static float VisiblePosition(float originalVisible, float originalWidth, float factor, float pivot)
        {
            return originalVisible - (1f - Pivot(pivot)) * Growth(originalWidth, factor);
        }

        /// <summary>The same for a pivot on the right edge.</summary>
        public static float VisiblePosition(float originalVisible, float originalWidth, float factor)
        {
            return VisiblePosition(originalVisible, originalWidth, factor, 1f);
        }

        /// <summary>
        /// The position of the shut panel: the original one moved so that the left edge stays where it was, that is, the panel hides as far as
        /// before and no further part of it shows. A panel the game hid completely stays completely hidden.
        /// </summary>
        public static float HiddenPosition(float originalHidden, float originalWidth, float factor, float pivot)
        {
            return originalHidden + Pivot(pivot) * Growth(originalWidth, factor);
        }

        /// <summary>The same for a pivot on the right edge: the shut position moves by the whole growth.</summary>
        public static float HiddenPosition(float originalHidden, float originalWidth, float factor)
        {
            return HiddenPosition(originalHidden, originalWidth, factor, 1f);
        }

        /// <summary>The left edge of a panel of the given width whose pivot stands at <paramref name="position"/>.</summary>
        public static float LeftEdge(float position, float width, float pivot)
        {
            return position - Pivot(pivot) * width;
        }

        /// <summary>The right edge of a panel of the given width whose pivot stands at <paramref name="position"/>.</summary>
        public static float RightEdge(float position, float width, float pivot)
        {
            return position + (1f - Pivot(pivot)) * width;
        }

        /// <summary>
        /// The factor after the left edge was dragged: <paramref name="dragLeft"/> (positive to the left, in parent units) widens the panel from the
        /// factor it had when the drag began. The result is held inside the range, so a drag past the limit stops at the limit.
        /// </summary>
        public static float FactorAfterDrag(float startFactor, float originalWidth, float dragLeft, float min, float max)
        {
            float start = Clamp(startFactor, min, max);
            if (!IsNumber(originalWidth) || originalWidth <= 0f || !IsNumber(dragLeft)) return start;
            return Clamp(start + dragLeft / originalWidth, min, max);
        }

        /// <summary>The factor is the original width: the adapter then leaves the panel as the game made it.</summary>
        public static bool IsNeutral(float factor)
        {
            return !IsNumber(factor) || Math.Abs(factor - NeutralFactor) < NeutralTolerance;
        }

        /// <summary>
        /// Whether a panel standing at <paramref name="current"/> is open, judged by which of the two positions is nearer (a tie counts as open). Used only
        /// when the game does not say (its <c>isOpen</c> flag is read first).
        /// </summary>
        public static bool LooksOpen(float current, float visible, float hidden)
        {
            return Math.Abs(current - visible) <= Math.Abs(current - hidden);
        }

        private static float Bound(float value, float fallback)
        {
            float v = IsNumber(value) ? value : fallback;
            return Math.Max(AbsoluteMin, Math.Min(AbsoluteMax, v));
        }

        private static float Pivot(float pivot)
        {
            if (!IsNumber(pivot)) return 1f;
            return Math.Max(0f, Math.Min(1f, pivot));
        }

        private static bool IsNumber(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
