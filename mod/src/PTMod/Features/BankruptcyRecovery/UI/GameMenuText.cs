using System;
using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// <see cref="IMenuText"/> on the game: the words come from the string tables of the mod (<c>ModL10n</c>), money and dates are written by the game's own formatter
    /// (<see cref="RecoveryViews"/>). Stateless; the pure models of the windows get one instance and never see the game.
    /// </summary>
    internal sealed class GameMenuText : IMenuText
    {
        public static readonly GameMenuText Instance = new GameMenuText();

        private GameMenuText()
        {
        }

        public string L(string key, params string[] args)
        {
            object[] values = args == null ? new object[0] : (object[])args;
            return ModL10n.L(key, values);
        }

        public string Money(double value)
        {
            return RecoveryViews.Money(value);
        }

        public string MoneyExact(double value)
        {
            return RecoveryViews.MoneyExact(value);
        }

        public string Percent(double fraction)
        {
            return RecoveryViews.Percent(fraction);
        }

        public string Date(DateTime date)
        {
            return RecoveryViews.GameDate(date);
        }
    }
}
