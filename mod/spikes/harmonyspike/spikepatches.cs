// Harmony spike (spike 0002, TSK-006): the S1 patches of design.md (table of patches, row S1). They change nothing in
// the game: every patch only counts how often it was called (and records a few facts the framework patches P1-P23
// will depend on), so the spike answers "does Harmony 2.3.6 work on Unity 6 Mono for every patch shape we need".
//
// Shapes covered (the framework applies its patches the same way, imperatively through Harmony.Patch with targets
// resolved by AccessTools, design.md "ModHooks"):
//   P1   postfix with parameters by name and ref __result              Modifiers.GetModifier (public static)
//   P5   prefix on a private method + private field injection          Market.UpdatePopularities, ___cpus
//   P4   postfix on a private method whose signature has a private     Market.CalculateRatings(Market.Population, ...)
//        nested type (the patch declares only the parameters it needs)
//   P2   prefix + postfix + finalizer sharing a __state struct         Factory.ProduceWafers
//   P10  transpiler (dummy: counts ldloc.0, injects one call)          BankruptcyHandler.TickBankruptcies (private)
//   P8   postfix on a method of a private nested type                  Contract.CpuOffer.DefineChance (AccessTools.Inner)
//   P23  prefix + finalizer on a private method                        Contract.GetBestOffers
//   P21  prefix + finalizer with a __state struct                      DateController.NextMonth
//   P20  postfix on virtual methods of two classes                     Company.AddCpu, AICompany.AddCpu
//
// Rules the spike follows like the real code: every patch body is in try/catch (a failure is counted and logged, never
// thrown into the game), prefixes return nothing (the original always runs), finalizers return the exception they got.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using ProcessorTycoon;                  // GameManager
using ProcessorTycoon.Bank;             // BankruptcyHandler
using ProcessorTycoon.CompanySystem;    // Modifiers, ModifierType, ICompany, Company, AICompany
using ProcessorTycoon.ContractSystem;   // Contract
using ProcessorTycoon.Hardware;         // Cpu
using ProcessorTycoon.MarketSystem;     // Market
using ProcessorTycoon.Production;       // Factory
using ProcessorTycoon.TimeSystem;       // DateController

namespace PTMod.Spikes.HarmonySpike
{
    /// <summary>Applies the ten S1 patches one by one; a failure of one patch does not stop the others.</summary>
    internal static class SpikePatcher
    {
        public const string HarmonyId = "ptmod.spike.s1";

        // NoInlining and a separate class: PTMod.dll must load and Init must run even when 0Harmony.dll is missing; only this
        // method (and the patch classes) need Harmony types, so a missing assembly fails here, inside Init's try/catch.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void ApplyAll()
        {
            var total = Stopwatch.StartNew();
            Harmony harmony;
            try
            {
                harmony = new Harmony(HarmonyId);
            }
            catch (Exception e)
            {
                foreach (var row in CounterSet.Rows) SpikeCounters.Set.MarkApplied(row.Id, false, "Harmony instance: " + e.GetType().Name);
                SpikeLog.Error("S1 apply: cannot create the Harmony instance: " + e);
                return;
            }

            Apply(harmony, S1.GetModifier,
                () => AccessTools.DeclaredMethod(typeof(Modifiers), "GetModifier"),
                typeof(PatchGetModifier), null, "Postfix", null, null);
            Apply(harmony, S1.UpdatePopularities,
                () => AccessTools.DeclaredMethod(typeof(Market), "UpdatePopularities"),
                typeof(PatchUpdatePopularities), "Prefix", null, null, null);
            Apply(harmony, S1.CalculateRatings,
                () => AccessTools.DeclaredMethod(typeof(Market), "CalculateRatings"),
                typeof(PatchCalculateRatings), null, "Postfix", null, null);
            Apply(harmony, S1.ProduceWafers,
                () => AccessTools.DeclaredMethod(typeof(Factory), "ProduceWafers"),
                typeof(PatchProduceWafers), "Prefix", "Postfix", null, "Finalizer");
            Apply(harmony, S1.TickBankruptcies,
                () => AccessTools.DeclaredMethod(typeof(BankruptcyHandler), "TickBankruptcies"),
                typeof(PatchTickBankruptcies), null, null, "Transpiler", null);
            Apply(harmony, S1.DefineChance,
                () => AccessTools.DeclaredMethod(AccessTools.Inner(typeof(Contract), "CpuOffer"), "DefineChance"),
                typeof(PatchDefineChance), null, "Postfix", null, null);
            Apply(harmony, S1.GetBestOffers,
                () => AccessTools.DeclaredMethod(typeof(Contract), "GetBestOffers"),
                typeof(PatchGetBestOffers), "Prefix", null, null, "Finalizer");
            Apply(harmony, S1.NextMonth,
                () => AccessTools.DeclaredMethod(typeof(DateController), "NextMonth"),
                typeof(PatchNextMonth), "Prefix", null, null, "Finalizer");
            Apply(harmony, S1.CompanyAddCpu,
                () => AccessTools.DeclaredMethod(typeof(Company), "AddCpu"),
                typeof(PatchCompanyAddCpu), null, "Postfix", null, null);
            Apply(harmony, S1.AiCompanyAddCpu,
                () => AccessTools.DeclaredMethod(typeof(AICompany), "AddCpu"),
                typeof(PatchAiCompanyAddCpu), null, "Postfix", null, null);

            var patchedMethods = 0;
            try
            {
                foreach (var method in harmony.GetPatchedMethods())
                {
                    if (method != null) patchedMethods++;
                }
            }
            catch (Exception e)
            {
                SpikeLog.Error("S1 apply: GetPatchedMethods failed: " + e.Message);
            }

            var applied = 0;
            foreach (var row in CounterSet.Rows) if (SpikeCounters.Set.Applied(row.Id)) applied++;
            SpikeLog.Info("S1 apply: done, " + applied + "/" + CounterSet.PatchCount + " patches applied, Harmony reports "
                + patchedMethods + " patched methods, " + total.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                + " ms in total");
        }

        private static void Apply(Harmony harmony, S1 id, Func<MethodBase> resolve, Type patch,
            string prefix, string postfix, string transpiler, string finalizer)
        {
            var row = CounterSet.Rows[(int)id];
            var watch = Stopwatch.StartNew();
            try
            {
                var original = resolve();
                if (original == null) throw new MissingMethodException("target not found: " + row.Target);

                harmony.Patch(original, Hm(patch, prefix), Hm(patch, postfix), Hm(patch, transpiler), Hm(patch, finalizer));

                // Trust Harmony's own registry, not only the absence of an exception.
                var info = Harmony.GetPatchInfo(original);
                if (info == null || !info.Owners.Contains(HarmonyId))
                    throw new InvalidOperationException("Harmony.GetPatchInfo does not list our patch after Patch()");

                SpikeCounters.Set.MarkApplied(id, true, null);
                SpikeLog.Info("S1 apply: " + row.Design + " " + row.Target + " [" + row.Kinds + "] OK, " + Describe(original)
                    + ", " + watch.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms");
            }
            catch (Exception e)
            {
                SpikeCounters.Set.MarkApplied(id, false, e.GetType().Name + ": " + FirstLine(e.Message));
                SpikeLog.Error("S1 apply: " + row.Design + " " + row.Target + " [" + row.Kinds + "] FAILED: " + e);
            }
        }

        private static HarmonyMethod Hm(Type patch, string method)
        {
            if (method == null) return null;
            var info = AccessTools.DeclaredMethod(patch, method);
            if (info == null) throw new MissingMethodException(patch.FullName + "." + method);
            return new HarmonyMethod(info);
        }

        private static string Describe(MethodBase m)
        {
            var access = m.IsPublic ? "public" : (m.IsPrivate ? "private" : "non-public");
            return access + (m.IsStatic ? " static" : "") + (m.IsVirtual ? " virtual" : "") + " " + m.DeclaringType.FullName;
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var end = text.IndexOf('\n');
            return end < 0 ? text : text.Substring(0, end).TrimEnd();
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P1. Postfix with parameters by name and ref __result.
    // ---------------------------------------------------------------------------------------------------------------
    public static class PatchGetModifier
    {
        public static void Postfix(ModifierType type, ICompany company, ref float __result)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Hit(S1.GetModifier);
                if (company == null) c.Add(S1.GetModifier, 0, 1);
                var t = (int)type;
                if (t >= 0 && t < 63) c.SetBits(S1.GetModifier, 1, 1L << t);
                // Proves that __result is the real return value: a game multiplier is a finite positive number.
                if (float.IsNaN(__result) || float.IsInfinity(__result) || __result <= 0f) c.Add(S1.GetModifier, 2, 1);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.GetModifier, e);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P5. Prefix on a private method, with injection of the private field "cpus" (the framework does the same with
    // ___playerInitialData in P22).
    // ---------------------------------------------------------------------------------------------------------------
    public static class PatchUpdatePopularities
    {
        public static void Prefix(Market __instance, List<Cpu> ___cpus)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Hit(S1.UpdatePopularities);
                if (___cpus == null)
                {
                    c.Add(S1.UpdatePopularities, 2, 1);
                    return;
                }
                c.Add(S1.UpdatePopularities, 0, ___cpus.Count);
                c.Max(S1.UpdatePopularities, 1, ___cpus.Count);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.UpdatePopularities, e);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P4. Postfix on a private method whose first parameter has the private nested type Market.Population: the patch
    // names only __instance and populationIndex, Harmony matches the rest of the signature by itself.
    // ---------------------------------------------------------------------------------------------------------------
    public static class PatchCalculateRatings
    {
        public static void Postfix(Market __instance, int populationIndex)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Hit(S1.CalculateRatings);
                if (populationIndex >= 0 && populationIndex < 63) c.SetBits(S1.CalculateRatings, 0, 1L << populationIndex);
                if (__instance == null) c.Add(S1.CalculateRatings, 1, 1);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.CalculateRatings, e);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P2. Prefix + postfix + finalizer sharing a __state struct (state flows only between methods of the SAME class).
    // The prefix snapshots values, the postfix and the finalizer check that the snapshot arrived unchanged.
    // ---------------------------------------------------------------------------------------------------------------
    public struct WaferState
    {
        public float UnitCost;
        public float FailureRate;
        public int Produced;
        public int Check;
    }

    public static class PatchProduceWafers
    {
        private static int CheckOf(Cpu cpu) { return unchecked(cpu.SaveID * 31 + 7); }

        public static void Prefix(Cpu cpu, out WaferState __state)
        {
            __state = default(WaferState);
            try
            {
                SpikeCounters.Set.Hit(S1.ProduceWafers);
                __state.UnitCost = cpu.UnitCost;
                __state.FailureRate = cpu.FailureRate;
                __state.Produced = cpu.ProducedLastDay;
                __state.Check = CheckOf(cpu);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.ProduceWafers, e);
            }
        }

        public static void Postfix(Cpu cpu, WaferState __state)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Add(S1.ProduceWafers, 0, 1);
                if (__state.Check != CheckOf(cpu)) c.Add(S1.ProduceWafers, 3, 1);
                var delta = cpu.ProducedLastDay - __state.Produced;
                if (delta > 0) c.Add(S1.ProduceWafers, 2, delta);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.ProduceWafers, e);
            }
        }

        public static Exception Finalizer(Exception __exception, Cpu cpu, WaferState __state)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Add(S1.ProduceWafers, 1, 1);
                // The postfix does not run when the original throws; the finalizer always does. State must still be intact.
                if (__state.Check != CheckOf(cpu)) c.Add(S1.ProduceWafers, 3, 1);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.ProduceWafers, e);
            }
            return __exception;     // pass through: the spike never swallows a game exception
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P10. Dummy transpiler on a private method. It (1) counts the loads of local 0 (V_0 = -DebtLimit) in the loop of
    // TickBankruptcies, which the real P10 will replace: the design expects exactly 3; (2) injects one call at the start
    // of the method so the "called" counter proves that the rewritten IL runs on Unity's Mono. Nothing else changes.
    // ---------------------------------------------------------------------------------------------------------------
    internal struct LocalUse
    {
        public int Instructions;
        public int Loads;           // ldloc / ldloc.s / ldloc.N of the local, whole method
        public int Stores;          // stloc* of the local
        public int LoadsInLoop;     // loads between the target of the first backward branch and the last backward branch
        public int BackwardBranches;
        public int LoopStart;       // instruction index, -1 when there is no loop
        public int LoopEnd;
    }

    internal static class LocalUseAnalyzer
    {
        public static LocalUse Analyze(IList<CodeInstruction> code, int local)
        {
            var use = new LocalUse { Instructions = code.Count, LoopStart = -1, LoopEnd = -1 };

            var labelAt = new Dictionary<Label, int>();
            for (var i = 0; i < code.Count; i++)
            {
                foreach (var label in code[i].labels) labelAt[label] = i;
            }

            var loopStart = int.MaxValue;
            var loopEnd = -1;
            for (var i = 0; i < code.Count; i++)
            {
                var operand = code[i].operand;
                if (operand is Label)
                {
                    NoteBranch(labelAt, (Label)operand, i, ref use.BackwardBranches, ref loopStart, ref loopEnd);
                }
                else if (operand is Label[])
                {
                    foreach (var label in (Label[])operand) NoteBranch(labelAt, label, i, ref use.BackwardBranches, ref loopStart, ref loopEnd);
                }
            }
            if (loopEnd >= 0)
            {
                use.LoopStart = loopStart;
                use.LoopEnd = loopEnd;
            }

            for (var i = 0; i < code.Count; i++)
            {
                if (IsLoad(code[i], local))
                {
                    use.Loads++;
                    if (use.LoopStart >= 0 && i >= use.LoopStart && i <= use.LoopEnd) use.LoadsInLoop++;
                }
                else if (IsStore(code[i], local))
                {
                    use.Stores++;
                }
            }
            return use;
        }

        private static void NoteBranch(Dictionary<Label, int> labelAt, Label target, int at, ref int count, ref int loopStart, ref int loopEnd)
        {
            int to;
            if (!labelAt.TryGetValue(target, out to) || to > at) return;
            count++;
            if (to < loopStart) loopStart = to;
            if (at > loopEnd) loopEnd = at;
        }

        public static bool IsLoad(CodeInstruction instruction, int local)
        {
            var op = instruction.opcode;
            if (op == OpCodes.Ldloc_0) return local == 0;
            if (op == OpCodes.Ldloc_1) return local == 1;
            if (op == OpCodes.Ldloc_2) return local == 2;
            if (op == OpCodes.Ldloc_3) return local == 3;
            if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S) return LocalIndex(instruction.operand) == local;
            return false;
        }

        public static bool IsStore(CodeInstruction instruction, int local)
        {
            var op = instruction.opcode;
            if (op == OpCodes.Stloc_0) return local == 0;
            if (op == OpCodes.Stloc_1) return local == 1;
            if (op == OpCodes.Stloc_2) return local == 2;
            if (op == OpCodes.Stloc_3) return local == 3;
            if (op == OpCodes.Stloc || op == OpCodes.Stloc_S) return LocalIndex(instruction.operand) == local;
            return false;
        }

        // Harmony keeps the operand of ldloc.s as a LocalBuilder; a raw index is accepted too.
        private static int LocalIndex(object operand)
        {
            var builder = operand as LocalBuilder;
            if (builder != null) return builder.LocalIndex;
            if (operand is int) return (int)operand;
            if (operand is short) return (short)operand;
            if (operand is ushort) return (ushort)operand;
            if (operand is byte) return (byte)operand;
            if (operand is sbyte) return (sbyte)operand;
            return -1;
        }
    }

    public static class PatchTickBankruptcies
    {
        /// <summary>Called by the injected instruction at every entry into TickBankruptcies.</summary>
        public static void Entered()
        {
            try
            {
                SpikeCounters.Set.Hit(S1.TickBankruptcies);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.TickBankruptcies, e);
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            try
            {
                var use = LocalUseAnalyzer.Analyze(code, 0);
                var c = SpikeCounters.Set;
                c.SetExtra(S1.TickBankruptcies, 0, use.Loads);
                c.SetExtra(S1.TickBankruptcies, 1, use.LoadsInLoop);
                c.SetExtra(S1.TickBankruptcies, 2, use.Stores);
                c.SetExtra(S1.TickBankruptcies, 3, use.Instructions);
                SpikeLog.Info("S1 transpiler: TickBankruptcies has " + use.Instructions + " instructions, local 0: " + use.Loads + " loads ("
                    + use.LoadsInLoop + " in the loop at " + use.LoopStart + ".." + use.LoopEnd + ", " + use.BackwardBranches
                    + " backward branches), " + use.Stores + " stores");
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.TickBankruptcies, e);
            }

            // The injected call proves that the rewritten method runs. Labels move to the new first instruction so every
            // jump to the old start still lands at the beginning of the method.
            var entry = new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(PatchTickBankruptcies), "Entered"));
            if (code.Count > 0)
            {
                entry.labels.AddRange(code[0].labels);
                code[0].labels.Clear();
                entry.blocks.AddRange(code[0].blocks);
                code[0].blocks.Clear();
            }
            code.Insert(0, entry);
            return code;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P8 + P23. DefineChance is a method of the private nested type Contract.CpuOffer; GetBestOffers is the private
    // method that calls it. The prefix/finalizer pair keeps a scope depth, so the spike can show that DefineChance is
    // only ever called inside GetBestOffers (the framework's ModContractScope relies on that).
    // ---------------------------------------------------------------------------------------------------------------
    public static class PatchGetBestOffers
    {
        private static int _depth;

        public static int Depth { get { return _depth; } }

        public static void Prefix(Contract __instance)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Hit(S1.GetBestOffers);
                _depth++;
                c.Max(S1.GetBestOffers, 1, _depth);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.GetBestOffers, e);
            }
        }

        public static Exception Finalizer(Exception __exception)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Add(S1.GetBestOffers, 0, 1);
                if (__exception != null) c.Add(S1.GetBestOffers, 2, 1);
                if (_depth > 0) _depth--;
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.GetBestOffers, e);
            }
            return __exception;
        }
    }

    public static class PatchDefineChance
    {
        // __instance is declared as object: the real type Contract.CpuOffer is private and cannot be named.
        public static void Postfix(object __instance, float ipsShare)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Hit(S1.DefineChance);
                if (PatchGetBestOffers.Depth > 0) c.Add(S1.DefineChance, 0, 1);
                else c.Add(S1.DefineChance, 1, 1);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.DefineChance, e);
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P21. Prefix + finalizer around the monthly fast-forward ("Next month" button). The state carries the start date and
    // the start time; the finalizer logs one line per use, which is the evidence the owner copies.
    // ---------------------------------------------------------------------------------------------------------------
    public struct NextMonthState
    {
        public DateTime Start;
        public long Timestamp;
    }

    public static class PatchNextMonth
    {
        public static void Prefix(DateController __instance, out NextMonthState __state)
        {
            __state = default(NextMonthState);
            try
            {
                SpikeCounters.Set.Hit(S1.NextMonth);
                __state.Start = __instance.CurrentDate;
                __state.Timestamp = Stopwatch.GetTimestamp();
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.NextMonth, e);
            }
        }

        public static Exception Finalizer(Exception __exception, DateController __instance, NextMonthState __state)
        {
            try
            {
                var c = SpikeCounters.Set;
                var days = (__instance.CurrentDate - __state.Start).Days;
                var ms = (Stopwatch.GetTimestamp() - __state.Timestamp) * 1000.0 / Stopwatch.Frequency;
                c.Add(S1.NextMonth, 0, 1);
                c.Add(S1.NextMonth, 1, days);
                c.Add(S1.NextMonth, 2, (long)ms);
                SpikeLog.Info("S1 NextMonth: " + days + " game days in " + ms.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
                    + " ms, now " + __instance.CurrentDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                    + (__exception == null ? ", no exception" : ", exception " + __exception.GetType().Name));
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(S1.NextMonth, e);
            }
            return __exception;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // P20. Postfix on the two virtual AddCpu methods. Besides the call count it splits the calls by situation, so the
    // owner's run shows which are "live" releases (the only ones ModEvents.CpuReleased fires for) and how many distinct
    // CPU objects there were (the framework fires at most once per CPU object).
    // ---------------------------------------------------------------------------------------------------------------
    public static class PatchCompanyAddCpu
    {
        public static void Postfix(Company __instance, Cpu cpu)
        {
            AddCpuProbe.Record(S1.CompanyAddCpu, cpu);
        }
    }

    public static class PatchAiCompanyAddCpu
    {
        public static void Postfix(AICompany __instance, Cpu cpu)
        {
            AddCpuProbe.Record(S1.AiCompanyAddCpu, cpu);
        }
    }

    internal static class AddCpuProbe
    {
        private static readonly HashSet<int>[] Seen = { new HashSet<int>(), new HashSet<int>() };

        public static void Record(S1 id, Cpu cpu)
        {
            try
            {
                var c = SpikeCounters.Set;
                c.Hit(id);

                var loading = IsLoading();
                var fastForward = IsFastForward();
                if (loading) c.Add(id, 1, 1);
                if (fastForward) c.Add(id, 2, 1);
                if (!loading && !fastForward) c.Add(id, 0, 1);

                var seen = Seen[id == S1.CompanyAddCpu ? 0 : 1];
                if (cpu != null && seen.Add(RuntimeHelpers.GetHashCode(cpu))) c.Add(id, 3, 1);
            }
            catch (Exception e)
            {
                SpikeCounters.Fault(id, e);
            }
        }

        private static bool IsLoading()
        {
            try
            {
                var manager = GameManager.Instance;
                return manager != null && manager.IsLoading;
            }
            catch (Exception)
            {
                return false;   // LoadingScreen.Instance can be missing outside the game scene
            }
        }

        private static bool IsFastForward()
        {
            try
            {
                var dates = DateController.Instance;
                return dates != null && dates.IsFastFowardSimulating;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
