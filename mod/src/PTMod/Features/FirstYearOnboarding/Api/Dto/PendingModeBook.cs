using System;

namespace PTMod.Features.FirstYearOnboarding
{
    /// <summary>
    /// The rules behind <see cref="OnboardingService.SetPendingMode"/> (FR-14 item 1), apart from the core so that they can be tested: the mode another
    /// feature asked for the nearest new game, who asked, when it changes, and how it is put together with the toggle of the new game window.
    /// <list type="bullet">
    /// <item>The mode holds for the nearest new game only: <see cref="Reset"/> puts it back to <see cref="PendingMode.Allowed"/> (the service does it
    /// after <c>GameReady(NewGame)</c>, and when a save is loaded instead).</item>
    /// <item><see cref="Set"/> says whether anything changed, so that the event <c>PendingModeChanged</c> is raised only for a real change (the same mode
    /// from the same source is not one).</item>
    /// <item><see cref="ResolveTutorial"/> is the final decision of a new game: a forced mode wins over the toggle; with no request the toggle decides,
    /// and no toggle value at all means no tutorial.</item>
    /// </list>
    /// Pure type: no references to the core, Unity or the game. Main thread only.
    /// </summary>
    internal sealed class PendingModeBook
    {
        private PendingMode _mode = PendingMode.Allowed;
        private string _source = string.Empty;

        /// <summary>The mode asked for the nearest new game; <see cref="PendingMode.Allowed"/> when nobody asked.</summary>
        public PendingMode Mode
        {
            get { return _mode; }
        }

        /// <summary>Who asked (<c>scenario:&lt;id&gt;</c>); empty when nobody did. Never null.</summary>
        public string Source
        {
            get { return _source; }
        }

        /// <summary>The value is one of the three modes (a number cast to the enum may be none of them).</summary>
        public static bool IsValid(PendingMode mode)
        {
            return Enum.IsDefined(typeof(PendingMode), mode);
        }

        /// <summary>
        /// Remembers the request. Returns true when the mode or the source changed; false for the same request again and for a value that is not a mode
        /// (which changes nothing).
        /// </summary>
        public bool Set(PendingMode mode, string source)
        {
            if (!IsValid(mode)) return false;

            string from = source ?? string.Empty;
            if (mode == _mode && string.Equals(from, _source, StringComparison.Ordinal)) return false;

            _mode = mode;
            _source = from;
            return true;
        }

        /// <summary>Takes the mode and its source and puts the book back to the empty request. <paramref name="changed"/> is true when that was a change.</summary>
        public PendingMode Take(out string source, out bool changed)
        {
            PendingMode mode = _mode;
            source = _source;
            changed = Reset();
            return mode;
        }

        /// <summary>Back to <see cref="PendingMode.Allowed"/> with no source. True when that was a change.</summary>
        public bool Reset()
        {
            bool changed = _mode != PendingMode.Allowed || _source.Length > 0;
            _mode = PendingMode.Allowed;
            _source = string.Empty;
            return changed;
        }

        /// <summary>
        /// The final mode of a new game: <see cref="PendingMode.ForcedOn"/> is on and <see cref="PendingMode.ForcedOff"/> is off whatever the toggle says;
        /// <see cref="PendingMode.Allowed"/> follows the toggle (<paramref name="hasToggle"/> false means the player never pressed "Start" through the
        /// window of the feature, so there is no tutorial).
        /// </summary>
        /// <param name="mode">The mode another feature asked for.</param>
        /// <param name="hasToggle">The choice of the toggle was recorded when the player pressed "Start".</param>
        /// <param name="toggleOn">What the toggle said.</param>
        public static bool ResolveTutorial(PendingMode mode, bool hasToggle, bool toggleOn)
        {
            switch (mode)
            {
                case PendingMode.ForcedOn:
                    return true;
                case PendingMode.ForcedOff:
                    return false;
                default:
                    return hasToggle && toggleOn;
            }
        }
    }
}
