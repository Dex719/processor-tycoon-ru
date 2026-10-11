using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.PressReviews
{
    /// <summary>What goes into the letter about the awards of a year: its tag and the arguments of its title and body.</summary>
    public sealed class AwardsMailContent
    {
        /// <summary><c>press:awards:&lt;year&gt;</c>.</summary>
        public string Tag = string.Empty;

        /// <summary>The arguments of the title and of the body (the core fills both with the same list): 0 the year, 1 the winners, one per line, and the player's own result.</summary>
        public string[] Args = new string[0];
    }

    /// <summary>
    /// The letter "Press awards 1995" (FR-8 item 3, design.md "Письма"): the winners as the tab "Awards of the year" shows them (the same lines) and, when the
    /// awards changed the reputation of the player's company, one line about it. The tag lets the letter be found again and lets its button open the tab.
    /// Pure: the texts come in through <see cref="PressText"/>.
    /// </summary>
    public static class AwardsMailText
    {
        /// <summary>The prefix of the tag of the letters about awards; the year follows.</summary>
        public const string TagPrefix = "press:awards:";

        /// <summary>The tag of the letter about the awards of a year.</summary>
        public static string TagOf(int year)
        {
            return TagPrefix + year.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The year a tag <c>press:awards:&lt;year&gt;</c> is about; false for any other tag.</summary>
        public static bool TryParseTag(string tag, out int year)
        {
            year = 0;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(TagPrefix, StringComparison.Ordinal)) return false;
            return int.TryParse(tag.Substring(TagPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out year);
        }

        /// <summary>
        /// The tag and the arguments of the letter. <paramref name="playerDelta"/> is the change of the player's reputation the awards made (0 for none: the
        /// line about it is left out).
        /// </summary>
        public static AwardsMailContent Build(YearAwards awards, int playerDelta, PressTabViews.AwardsLookups lookups, PressText text)
        {
            if (awards == null) throw new ArgumentNullException("awards");
            if (text == null) throw new ArgumentNullException("text");

            var view = new List<YearAwards> { awards };
            List<AwardsYearView> years = PressTabViews.Awards(view, lookups, text);
            var lines = new List<string>(years.Count == 0 ? new List<string>() : years[0].Lines);
            if (playerDelta != 0) lines.Add(text("press.mail.awards.player", playerDelta));

            return new AwardsMailContent
            {
                Tag = TagOf(awards.year),
                Args = new[] { awards.year.ToString(CultureInfo.InvariantCulture), string.Join("\n", lines.ToArray()) },
            };
        }
    }
}
