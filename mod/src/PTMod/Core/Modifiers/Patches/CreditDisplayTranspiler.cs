using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using GameMoney = ProcessorTycoon.CompanySystem.Money;
using GameMoneyBalance = ProcessorTycoon.CompanySystem.PlayerUI.MoneyBalance;

namespace PTMod.Core
{
    /// <summary>
    /// P11: the player's credit in the interface, transpiler, Optional, on three methods (FR-24 item 3): the one call of the getter
    /// <c>CentralBank.DebtLimit</c> becomes <c>call ModCreditHooks.PlayerDebtLimit(CentralBank)</c> = <see cref="ModCredit.PlayerDebtLimit"/>, so the
    /// interface shows the threshold the game applies to the player (per company when P10 is in the game, else the global one).
    /// <list type="bullet">
    /// <item><c>MoneyBalance.UpdateUI</c> - the credit left in the finance tooltip of the balance (<c>DebtLimit + MoneyAmount</c> when negative).</item>
    /// <item><c>Money.UpdateMoneyText</c> - the balance turns red below <c>−DebtLimit</c> (the <c>Money</c> component belongs to the player's company only).</item>
    /// <item><c>BankruptcyHandler.UpdateUI</c> - the colour of the bankruptcy countdown.</item>
    /// </list>
    /// The replacement is in place (opcode and operand of the same instruction): the bank the getter would have been called on is the argument of the
    /// hook, the float it would have returned is the hook's result, labels and exception blocks stay. Each target is a patch of its own: a target
    /// where the call is not found exactly once is not patched (the transpiler throws, <see cref="ModHooks"/> logs a warning, <see cref="ModCredit"/>
    /// names the target), there the interface keeps the global limit, and the other targets are patched.
    /// <para>
    /// Verified statically on <c>_original_en/Assembly-CSharp.dll</c> (2026-10-10, <c>mod/spikes/ilprobe/dump_method_il.ps1 -CheckCredit</c>,
    /// reflection-only, the game not started): exactly one <c>callvirt CentralBank::get_DebtLimit()</c> in each target, right after
    /// <c>call CentralBank::get_Instance()</c> - <c>MoneyBalance.UpdateUI(Boolean)</c> at <c>IL_0074</c> (then <c>stloc.1</c>),
    /// <c>Money.UpdateMoneyText()</c> at <c>IL_0035</c> (then <c>neg; blt.s</c>), <c>BankruptcyHandler.UpdateUI()</c> at <c>IL_0014</c> (then
    /// <c>neg; bgt.s</c>). Result: the pattern matches in all three. Not yet checked in the running game: owner, TSK-031.
    /// </para>
    /// </summary>
    internal static class CreditDisplayTranspiler
    {
        [ModPatch(typeof(GameMoneyBalance), "UpdateUI", PatchKind.Transpiler, Optional = true)]
        [ModPatch(typeof(GameMoney), "UpdateMoneyText", PatchKind.Transpiler, Optional = true)]
        [ModPatch(typeof(GameBankruptcyHandler), "UpdateUI", PatchKind.Transpiler, Optional = true)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            string target = ModHooks.NameOf(original);
            var code = new List<CodeInstruction>(instructions);

            MethodInfo getter = ModCreditHooks.DebtLimitGetter;
            MethodInfo hook = ModCreditHooks.PlayerDebtLimitMethod;
            string problem = null;
            int found = -1;
            if (getter == null) problem = "CentralBank.DebtLimit has no getter";
            else if (hook == null) problem = "ModCreditHooks.PlayerDebtLimit(CentralBank) not found";
            else
            {
                int count = 0;
                for (int i = 0; i < code.Count; i++)
                {
                    if (!CreditIl.Calls(code[i], getter)) continue;
                    count++;
                    found = i;
                }
                if (count != 1) problem = count + " call(s) of CentralBank.get_DebtLimit, 1 expected";
            }

            ModCreditHooks.RecordPattern(target, problem);
            if (problem != null)
                throw new InvalidOperationException("P11: the code pattern of " + target + " was not found (" + problem + "); the transpiler is not applied");

            code[found].opcode = OpCodes.Call;   // in place: labels and blocks of the instruction stay
            code[found].operand = hook;
            return code;
        }
    }
}
