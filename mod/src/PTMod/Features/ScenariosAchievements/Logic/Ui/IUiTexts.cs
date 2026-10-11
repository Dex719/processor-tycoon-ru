using System;

namespace PTMod.Features.ScenariosAchievements.Logic
{
    /// <summary>How a line of a window is painted: the colour is the window's business, the choice is the model's (a done goal is good, a failed one is bad).</summary>
    public enum UiTone
    {
        Normal,
        Good,
        Bad,
        Warn,
        Muted,
    }

    /// <summary>One line of text of a window with its tone.</summary>
    public sealed class UiLine
    {
        public UiLine(string text, UiTone tone = UiTone.Normal)
        {
            Text = text ?? string.Empty;
            Tone = tone;
        }

        public string Text { get; private set; }

        public UiTone Tone { get; private set; }
    }

    /// <summary>
    /// The words and the numbers of the windows of the feature (TSK-022 .. TSK-027): the texts of <c>ModL10n</c> and the formats of the game's
    /// <c>StringFormatter</c> behind one interface, so the models of the windows are pure logic that a test can run with a table of strings and plain formats.
    /// The game-side implementation is <c>GameUiTexts</c>; a test brings its own over the real string tables.
    /// </summary>
    public interface IUiTexts
    {
        /// <summary>The text of an L10n key with <c>{0}..{n}</c> filled in (<c>[key]</c> for a key no table has).</summary>
        string Text(string key, params object[] args);

        /// <summary>The form of the key that fits <paramref name="n"/> (<c>key.one/.few/.many</c> in Russian).</summary>
        string Plural(string key, long n, params object[] args);

        /// <summary>An amount of money (<c>StringFormatter.FloatToMoney</c>).</summary>
        string Money(double amount);

        /// <summary>A share as a percentage; <paramref name="fraction"/> is 0..1 (<c>StringFormatter.FloatToPercentageString</c>).</summary>
        string Percent(double fraction);

        /// <summary>A plain count, rounded to a whole number.</summary>
        string Number(double value);

        /// <summary>A frequency given in kHz (<c>StringFormatter.FloatToFrequency</c>).</summary>
        string Frequency(double kilohertz);

        /// <summary>A calendar date as the player reads it.</summary>
        string Date(DateTime date);
    }
}
