using System;
using System.Collections.Generic;

namespace PTMod.Features.FirstYearOnboarding.Logic
{
    /// <summary>
    /// Where the values of the placeholders of a mail come from (FR-5 item 2): the game layer answers by name (the budgets of the market segments, the
    /// credit limit of the player, the days to bankruptcy in default, the money, the names of the company and its founder), the tests with a table.
    /// </summary>
    public interface IPlaceholderSource
    {
        /// <summary>
        /// The value of the placeholder, already formatted for the player (money the way the game writes it), or false when the name is unknown or its
        /// source cannot be read now. Must not throw.
        /// </summary>
        bool TryGet(string name, out string value);
    }

    /// <summary>
    /// Turns the list of placeholder names of a step (<c>args</c> in <c>steps.json</c>) into the strings that fill <c>{0}</c>, <c>{1}</c>... of its texts, in
    /// the order of the list (design.md, "OnboardingMailer and PlaceholderResolver"). The values are taken once, when the mail is sent, and stay in the mail.
    /// A name the source does not know gives <see cref="Unknown"/> and a warning, and the other placeholders still fill: a mail never fails because of one.
    /// Pure logic.
    /// </summary>
    public static class PlaceholderResolver
    {
        /// <summary>What stands in the text for a placeholder that could not be filled.</summary>
        public const string Unknown = "?";

        /// <summary>
        /// The values for <paramref name="names"/>, one per name and in the same order. Never null; empty for no names.
        /// </summary>
        /// <param name="names">The names of the placeholders (<see cref="StepValidator.ArgNames"/>); null is the same as no names. A null or blank entry is unknown.</param>
        /// <param name="source">Gives the values; null makes every name unknown.</param>
        /// <param name="warn">Receives one English line for each name that could not be filled; may be null.</param>
        public static string[] ResolveArgs(IReadOnlyList<string> names, IPlaceholderSource source, Action<string> warn = null)
        {
            if (names == null || names.Count == 0) return new string[0];

            var values = new string[names.Count];
            for (int i = 0; i < names.Count; i++)
            {
                values[i] = Resolve(names[i], source, warn);
            }
            return values;
        }

        /// <summary>The value of one placeholder, or <see cref="Unknown"/>.</summary>
        public static string Resolve(string name, IPlaceholderSource source, Action<string> warn = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                Warn(warn, "a placeholder without a name is filled with '" + Unknown + "'");
                return Unknown;
            }

            string value = null;
            bool known = false;
            if (source != null)
            {
                try
                {
                    known = source.TryGet(name, out value);
                }
                catch (Exception ex)
                {
                    // A source must not throw, but a mail is worth more than a clean line: the placeholder becomes unknown.
                    Warn(warn, "the source of the placeholder '" + name + "' failed (" + ex.GetType().Name + ": " + ex.Message + "), it is filled with '" + Unknown + "'");
                    return Unknown;
                }
            }

            if (known && value != null) return value;

            Warn(warn, "the placeholder '" + name + "' has no value, it is filled with '" + Unknown + "'");
            return Unknown;
        }

        private static void Warn(Action<string> warn, string message)
        {
            if (warn != null) warn(message);
        }
    }
}
