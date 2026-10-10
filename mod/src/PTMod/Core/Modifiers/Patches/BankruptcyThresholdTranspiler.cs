using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using GameBankruptcyHandler = ProcessorTycoon.Bank.BankruptcyHandler;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// P10: <c>BankruptcyHandler.TickBankruptcies</c>, transpiler, Optional (FR-12 item 19, FR-24, D17). The game computes one threshold for the
    /// whole world at the top of the method, <c>V_0 = −CentralBank.Instance.DebtLimit</c>, and compares every company with it in the loop over
    /// <c>companies</c> (<c>V_2</c>). The transpiler leaves every load of <c>V_0</c> in place and puts
    /// <c>ldloc V_2; call ModCreditHooks.Threshold(float, ICompany)</c> right after it, so each comparison gets
    /// <c>V_0 × M(bank.creditLimit, company)</c> - the company's own threshold, the same number <see cref="ModCredit.ThresholdOf"/> returns.
    /// <list type="bullet">
    /// <item><b>The three loads</b> are: the AI's <c>BankruptcyWarning(V_0, false)</c> on its last day in default, "still below at the end of the
    /// default: bankrupt" and "below: enter default". The player's own colour in <c>BankruptcyHandler.UpdateUI</c> is P11.</item>
    /// <item><b>Pattern</b> (checked before anything is changed): <c>V_0</c> is a float stored once, right after
    /// <c>call CentralBank::get_DebtLimit; neg</c>; it is loaded exactly 3 times (<c>ldloc</c>, never by address), all after that store;
    /// <c>V_2</c> is the <c>ICompany</c> of the loop and is loaded somewhere (its load is copied). Anything else - a game update that moved the
    /// code - and the transpiler throws: Harmony does not apply it, <see cref="ModHooks"/> logs a warning (Optional patch), <see cref="ModCredit"/>
    /// logs the consequence once, <see cref="ModCredit.IsPerCompanyActive"/> is false and the threshold stays global for everybody (AC-24.2).</item>
    /// <item><b>Labels</b> stay on the original instructions (nothing is replaced, only inserted after a load), so the jumps of the loop are untouched.</item>
    /// <item><b>Reserved:</b> only the transpiler is the core's (one per method, FR-2 item 5); <c>bankruptcy-recovery</c> uses a prefix and a postfix
    /// on this method.</item>
    /// </list>
    /// <para>
    /// Verified statically on <c>_original_en/Assembly-CSharp.dll</c> (2026-10-10, <c>mod/spikes/ilprobe/dump_method_il.ps1 -CheckCredit</c>,
    /// reflection-only, the game not started): locals <c>V_0 System.Single</c>, <c>V_1 List&lt;ICompany&gt;.Enumerator</c>,
    /// <c>V_2 ICompany</c>, <c>V_3 System.Int32</c>; <c>IL_0000 call CentralBank::get_Instance</c>, <c>IL_0005 callvirt CentralBank::get_DebtLimit</c>,
    /// <c>IL_000A neg</c>, <c>IL_000B stloc.0</c>; <c>V_0</c> stored 1x and loaded 3x, all <c>ldloc.0</c>: <c>IL_0064</c> (argument of
    /// <c>AICompany::BankruptcyWarning(Single, Boolean)</c>), <c>IL_0071</c> (<c>bge.un.s</c> after <c>MoneyAmount</c>: bankrupt or cancel the
    /// default), <c>IL_00AC</c> (<c>bge.un.s</c> after <c>MoneyAmount</c>: trigger the default), every one inside the <c>try</c> of the loop
    /// (IL_0018..IL_00C4); no jump targets a load of <c>V_0</c>. Result: the pattern matches - 3 loads, as design.md expects. Not yet checked in the
    /// running game (Harmony on Unity Mono): owner, TSK-031.
    /// </para>
    /// </summary>
    internal static class BankruptcyThresholdTranspiler
    {
        /// <summary>Loads of <c>V_0</c> the pattern expects (design.md, P10).</summary>
        internal const int ExpectedLoads = 3;

        private const int ThresholdLocal = 0;   // V_0: float, −CentralBank.DebtLimit
        private const int CompanyLocal = 2;     // V_2: ICompany, the company of the loop

        [ModPatch(typeof(GameBankruptcyHandler), "TickBankruptcies", PatchKind.Transpiler, Optional = true)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            string target = ModHooks.NameOf(original);
            var code = new List<CodeInstruction>(instructions);

            List<int> loads;
            CodeInstruction loadCompany;
            string problem = Match(code, original, out loads, out loadCompany);
            ModCreditHooks.RecordPattern(target, problem);
            if (problem != null)
                throw new InvalidOperationException("P10: the code pattern of " + target + " was not found (" + problem + "); the transpiler is not applied");

            MethodInfo hook = ModCreditHooks.ThresholdMethod;
            if (hook == null) throw new MissingMethodException("P10: ModCreditHooks.Threshold(float, ICompany) not found");

            // From the end, so the indexes of the earlier loads stay valid.
            for (int i = loads.Count - 1; i >= 0; i--)
            {
                int at = loads[i] + 1;
                code.Insert(at, new CodeInstruction(loadCompany.opcode, loadCompany.operand));
                code.Insert(at + 1, new CodeInstruction(OpCodes.Call, hook));
            }
            return code;
        }

        /// <summary>
        /// Checks the pattern described on the class; null when it matches (<paramref name="loads"/> - the indexes of the loads of <c>V_0</c>,
        /// <paramref name="loadCompany"/> - a load of <c>V_2</c> to copy), otherwise the English reason.
        /// </summary>
        private static string Match(List<CodeInstruction> code, MethodBase original, out List<int> loads, out CodeInstruction loadCompany)
        {
            loads = new List<int>();
            loadCompany = null;

            MethodBody body = original == null ? null : original.GetMethodBody();
            if (body == null) return "the method has no body";
            IList<LocalVariableInfo> locals = body.LocalVariables;
            if (locals.Count <= CompanyLocal) return locals.Count + " local(s), " + (CompanyLocal + 1) + " or more expected";
            if (locals[ThresholdLocal].LocalType != typeof(float)) return "V_0 is " + locals[ThresholdLocal].LocalType + ", float expected";
            if (locals[CompanyLocal].LocalType != typeof(ICompany)) return "V_2 is " + locals[CompanyLocal].LocalType + ", ICompany expected";

            MethodInfo getter = ModCreditHooks.DebtLimitGetter;
            if (getter == null) return "CentralBank.DebtLimit has no getter";

            int store = -1;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i];
                if (CreditIl.StoredLocal(instruction) == ThresholdLocal)
                {
                    if (store >= 0) return "V_0 is stored more than once";
                    store = i;
                }
                else if (CreditIl.LoadedLocal(instruction) == ThresholdLocal)
                {
                    loads.Add(i);
                }
                else if (CreditIl.AddressedLocal(instruction) == ThresholdLocal)
                {
                    return "V_0 is loaded by address at #" + i;
                }
                else if (loadCompany == null && CreditIl.LoadedLocal(instruction) == CompanyLocal)
                {
                    loadCompany = instruction;
                }
            }

            if (store < 0) return "V_0 is never stored";
            if (store < 2 || code[store - 1].opcode != OpCodes.Neg || !CreditIl.Calls(code[store - 2], getter))
                return "V_0 is not '-CentralBank.DebtLimit' (call get_DebtLimit; neg; stloc)";
            if (loads.Count != ExpectedLoads) return "V_0 is loaded " + loads.Count + " time(s), " + ExpectedLoads + " expected";
            if (loads[0] < store) return "V_0 is loaded before it is stored";
            if (loadCompany == null) return "V_2 is never loaded";
            return null;
        }
    }

    /// <summary>The IL questions of the credit transpilers P10 and P11 (local loads and stores in every encoding, a call of a given method).</summary>
    internal static class CreditIl
    {
        /// <summary>The local an instruction loads by value (<c>ldloc.0</c>-<c>ldloc.3</c>, <c>ldloc.s</c>, <c>ldloc</c>), or -1.</summary>
        internal static int LoadedLocal(CodeInstruction instruction)
        {
            OpCode op = instruction.opcode;
            if (op == OpCodes.Ldloc_0) return 0;
            if (op == OpCodes.Ldloc_1) return 1;
            if (op == OpCodes.Ldloc_2) return 2;
            if (op == OpCodes.Ldloc_3) return 3;
            if (op == OpCodes.Ldloc_S || op == OpCodes.Ldloc) return IndexOf(instruction.operand);
            return -1;
        }

        /// <summary>The local an instruction loads by address (<c>ldloca.s</c>, <c>ldloca</c>), or -1.</summary>
        internal static int AddressedLocal(CodeInstruction instruction)
        {
            OpCode op = instruction.opcode;
            return op == OpCodes.Ldloca_S || op == OpCodes.Ldloca ? IndexOf(instruction.operand) : -1;
        }

        /// <summary>The local an instruction stores (<c>stloc.0</c>-<c>stloc.3</c>, <c>stloc.s</c>, <c>stloc</c>), or -1.</summary>
        internal static int StoredLocal(CodeInstruction instruction)
        {
            OpCode op = instruction.opcode;
            if (op == OpCodes.Stloc_0) return 0;
            if (op == OpCodes.Stloc_1) return 1;
            if (op == OpCodes.Stloc_2) return 2;
            if (op == OpCodes.Stloc_3) return 3;
            if (op == OpCodes.Stloc_S || op == OpCodes.Stloc) return IndexOf(instruction.operand);
            return -1;
        }

        /// <summary>The instruction is a <c>call</c> or <c>callvirt</c> of <paramref name="method"/> (same name and declaring type).</summary>
        internal static bool Calls(CodeInstruction instruction, MethodInfo method)
        {
            if (method == null || (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt)) return false;
            var called = instruction.operand as MethodInfo;
            return called != null && called.Name == method.Name && called.DeclaringType == method.DeclaringType;
        }

        /// <summary>
        /// The index behind a local operand: Harmony gives a <c>LocalBuilder</c> (a <c>LocalVariableInfo</c>) for the copied body; a raw number is
        /// accepted too.
        /// </summary>
        private static int IndexOf(object operand)
        {
            var local = operand as LocalVariableInfo;
            if (local != null) return local.LocalIndex;
            if (operand is byte) return (byte)operand;
            if (operand is sbyte) return (sbyte)operand;
            if (operand is short) return (short)operand;
            if (operand is ushort) return (ushort)operand;
            if (operand is int) return (int)operand;
            return -1;
        }
    }
}
