using System;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// The tags of the mails of the tutorial (FR-5, FR-13 item 2): <c>onb:&lt;stepId&gt;</c> for the mail of a step or a tip and
    /// <c>onb:&lt;stepId&gt;:repeat</c> for the copy the player asked for. The prefix <c>onb</c> is the one the identifier table of the core gives the
    /// feature; the tag is how the feature knows its own mail in <c>ModMail.Delivered</c> and in the handler of the buttons. Pure functions.
    /// </summary>
    public static class MailTags
    {
        /// <summary>The prefix of every tag of the tutorial, with the separator.</summary>
        public const string Prefix = "onb:";

        /// <summary>The suffix of the tag of a repeated mail, with its separator.</summary>
        public const string RepeatSuffix = ":repeat";

        /// <summary>The tag of the mail of a step, or of its repeat.</summary>
        public static string For(string stepId, bool isRepeat)
        {
            return Prefix + stepId + (isRepeat ? RepeatSuffix : string.Empty);
        }

        /// <summary>
        /// Reads a tag: true when it is a tag of the tutorial with a step id, whatever the step is (the content may have changed since the mail was
        /// sent). <paramref name="isRepeat"/> tells the copy from the mail of the step.
        /// </summary>
        public static bool TryParse(string tag, out string stepId, out bool isRepeat)
        {
            stepId = null;
            isRepeat = false;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            string rest = tag.Substring(Prefix.Length);
            if (rest.EndsWith(RepeatSuffix, StringComparison.Ordinal))
            {
                isRepeat = true;
                rest = rest.Substring(0, rest.Length - RepeatSuffix.Length);
            }

            if (rest.Length == 0 || rest.IndexOf(':') >= 0)
            {
                isRepeat = false;
                return false;
            }

            stepId = rest;
            return true;
        }
    }
}
