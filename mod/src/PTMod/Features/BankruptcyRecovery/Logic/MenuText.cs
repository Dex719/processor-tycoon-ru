using System;

namespace PTMod.Features.BankruptcyRecovery.Logic
{
    /// <summary>How a line of the rescue menu is painted (the game side maps it to the colours of the theme; <see cref="Normal"/> keeps the colour of the text).</summary>
    internal enum MenuTone
    {
        Normal,
        Good,
        Bad,
        Warn,
        Muted,
    }

    /// <summary>
    /// How the pure models of the windows get their words and numbers (design.md, "UI"): the string tables by key, and the game's own way to write money and dates.
    /// The game side is <c>GameMenuText</c> (<c>ModL10n</c> and <c>StringFormatter</c>); a test uses a fake that prints the keys and the numbers, so the choice of a
    /// key and of a number is checked without the game. Every method returns a text and never throws.
    /// </summary>
    internal interface IMenuText
    {
        /// <summary>The text of a key with <c>{0}..{n}</c> filled in (<c>ModL10n.L</c>); the arguments are already written.</summary>
        string L(string key, params string[] args);

        /// <summary>Money the way the game writes it in a list: short, with the currency (<c>$2.6M</c>); a negative sum has its sign.</summary>
        string Money(double value);

        /// <summary>Money in full, for the question before an operation and the balance after it.</summary>
        string MoneyExact(double value);

        /// <summary>A yearly rate or a share as the number of percent without the sign (<c>0.3</c> gives <c>30</c>).</summary>
        string Percent(double fraction);

        /// <summary>A game date the way the game writes dates.</summary>
        string Date(DateTime date);
    }
}
