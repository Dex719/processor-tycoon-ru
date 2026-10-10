using System;
using System.Reflection;
using PTMod.Core.Logic.Credit;
using GameCentralBank = ProcessorTycoon.Bank.CentralBank;

namespace PTMod.Core
{
    /// <summary>
    /// P9: <c>CentralBank.UpdateCreditLimit</c>, postfix (FR-12, channel <c>bank.creditLimit</c>, global part). The game recomputes the one debt limit
    /// of the world every day (<c>DateController.OnTick</c>) and after a load (<c>SaveHandler.OnLateLoaded</c>):
    /// <c>DebtLimit = max(1e6, base × I² × (1.5 + clamp(year − 1971, 0, 60) / 25) − 1.75e7)</c>; the postfix multiplies the result by the channel's
    /// global value, <c>DebtLimit = max(1e6, DebtLimit × M(global))</c> (<see cref="CreditMath.GlobalDebtLimit"/>). The game starts from its base again each
    /// time, so the multiplier never compounds.
    /// <list type="bullet">
    /// <item><b>Who sees it.</b> Every reader of <c>DebtLimit</c>: the default threshold of every company (and through P10 the per-company part on
    /// top), the player's interface, and the AI's own reserves in <c>AIBehaviour*</c> (they read the global limit; the per-company part never
    /// reaches them, FR-24 item 5).</item>
    /// <item><b>Timing.</b> An entry added or removed during a day reaches <c>DebtLimit</c> at the next <c>UpdateCreditLimit</c> (the next day's
    /// tick). <see cref="ModCredit"/> reads <c>DebtLimit</c> as it is, so it always agrees with the game.</item>
    /// <item><b>Fast path.</b> Nothing registered on the channel: the postfix returns after one flag read and the game's value stands.</item>
    /// <item><b>Failure</b> is the core's unit <c>P9</c>: the exception is counted, the game's value stays, and after <c>hooks.faultThreshold</c>
    /// failures the patch stops answering until the game restarts.</item>
    /// </list>
    /// The property has a private setter; it is called through a delegate made once (the getter and setter are the auto-property's).
    /// To check in the game (owner, with <c>framework-sample</c>): a global entry <c>bank.creditLimit ×0.5</c> halves the credit line in the finance
    /// tooltip of the balance the next day (never below 1 million); removing it restores the vanilla value the day after.
    /// </summary>
    internal static class CreditLimitPatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string Site = "P9/postfix";

        private static Action<GameCentralBank, float> _setDebtLimit;

        [ModPatch(typeof(GameCentralBank), "UpdateCreditLimit", PatchKind.Postfix)]
        private static void Postfix(GameCentralBank __instance)
        {
            if (!ModModifiers.HasAny(ModChannels.BankCreditLimit)) return;
            if (ReferenceEquals(__instance, null)) return;
            if (!ModHooks.CanRun(CoreId, Site)) return;

            try
            {
                float multiplier = ModModifiers.Evaluate(ModChannels.BankCreditLimit, default(ModifierContext));
                float current = __instance.DebtLimit;
                float next = CreditMath.GlobalDebtLimit(current, multiplier);
                if (next != current) Setter()(__instance, next);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, Site, ex);
            }
        }

        /// <summary>The private setter of <c>CentralBank.DebtLimit</c> as an open delegate; throws when the game has no such setter (another version).</summary>
        private static Action<GameCentralBank, float> Setter()
        {
            Action<GameCentralBank, float> setter = _setDebtLimit;
            if (setter != null) return setter;

            PropertyInfo property = typeof(GameCentralBank).GetProperty("DebtLimit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo method = property == null ? null : property.GetSetMethod(true);
            if (method == null || method.IsStatic || method.GetParameters().Length != 1 || method.GetParameters()[0].ParameterType != typeof(float))
                throw new MissingMethodException("CentralBank.DebtLimit has no setter (float); the global credit multiplier cannot be applied");

            setter = (Action<GameCentralBank, float>)Delegate.CreateDelegate(typeof(Action<GameCentralBank, float>), method);
            _setDebtLimit = setter;
            return setter;
        }
    }
}
