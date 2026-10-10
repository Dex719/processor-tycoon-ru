using System;
using System.Collections.Generic;
using GameCompany = ProcessorTycoon.CompanySystem.Company;
using GameAiCompany = ProcessorTycoon.CompanySystem.AICompany;
using GameContractSeller = ProcessorTycoon.ContractSystem.ContractSeller;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameRevenueOrigin = ProcessorTycoon.RevenueOrigin;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Core
{
    /// <summary>
    /// P6 + P7: the money a company earns from the daily delivery of its contracts (FR-12; channel <c>contract.reward</c>).
    /// <c>ContractSeller.SellContracts</c> pays each delivered contract with <c>Company.EarnMoney(PaymentPerUnit × sold, RevenueOrigin.Contracts)</c>,
    /// adds the unmodified payment to <c>Cpu.IncomeLastDayFromContracts</c> (the figure of the monthly report of the CPU) and, for an outsourced CPU,
    /// pays the foundry a 5 % royalty of it. The mod multiplies the payment by <c>M(contract.reward, company)</c> and keeps the report in step:
    /// <list type="bullet">
    /// <item><b>P6</b> (<c>SellContracts</c>, private; prefix + postfix + finalizer) is the <b>scope</b>: the prefix opens it, the finalizer closes it,
    /// whatever happens in between; the postfix scales <c>IncomeLastDayFromContracts</c> of the CPUs that were paid.</item>
    /// <item><b>P7</b> (<c>Company.EarnMoney</c> and <c>AICompany.EarnMoney</c>, prefix) multiplies <c>value</c> only while the scope is open and only
    /// for <c>RevenueOrigin.Contracts</c>. Everything else is left alone: the <c>EarnMoney(…, Contracts)</c> calls of
    /// <c>BusinessContractManager</c> and <c>BusinessHelper</c> (business contracts between companies) happen outside <c>SellContracts</c>, the
    /// royalty is another origin, and the fines (<c>SpendMoney</c>) and the royalty amount are not part of the channel.</item>
    /// <item><b>The report equals the money.</b> P7 notes the multiplier it applied to each company, and the P6 postfix scales the income of a CPU by exactly
    /// that number, not by a second evaluation of the channel. A company whose payment P7 did not scale (the unit <c>P7</c> was off, the channel
    /// failed) keeps its report unscaled too, so <c>IncomeLastDayFromContracts</c> never claims money the company did not get.</item>
    /// <item><b>Nothing registered, nothing done:</b> with no records on <c>contract.reward</c> the prefix does not open the scope, and every
    /// <c>EarnMoney</c> of the game costs one static read in the prefix. Main thread only, nothing is allocated per call once the notes have grown
    /// to the number of companies that have contracts.</item>
    /// <item>Failures are the core's units <c>P6</c> and <c>P7</c>: the exception is counted, the game's value stands, and after
    /// <c>hooks.faultThreshold</c> failures the unit stops answering until the game restarts. The finalizer is never skipped once the scope
    /// was opened (<c>__state</c>), so the scope cannot stay open and scale some other payment.</item>
    /// </list>
    /// Example from the design: 2 000 units × $199 = $398 000 a day; M = 1.1 gives $437 800 in the bank and in the CPU's report; the royalty of an
    /// outsourced CPU stays 5 % of $398 000.
    /// <para>
    /// To check in the game (the owner, with <c>framework-sample</c>, TSK-053): with an entry <c>contract.reward ×1.1</c> for the player, the money
    /// received from the contracts of the day is 1.1 times the payment of the contract window (units per day × payment per unit) and the
    /// monthly report of the CPU shows the same income; a contract with a business partner (<c>BusinessContractManager</c>) pays the same as without
    /// the entry; with the feature off everything is the game's again; <c>Player.log</c> has no <c>P6</c> or <c>P7</c> faults.
    /// </para>
    /// </summary>
    internal static class ContractRewardPatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string PrefixSite = "P6/prefix";
        private const string PostfixSite = "P6/postfix";
        private const string FinalizerSite = "P6/finalizer";
        private const string PlayerEarnSite = "P7/Company.EarnMoney";
        private const string AiEarnSite = "P7/AICompany.EarnMoney";

        // The scope of the running SellContracts, and the multiplier P7 applied to each company in it (reference equality; a handful of companies
        // have contracts, so a linear search over two parallel arrays beats a dictionary).
        private static bool _scopeOpen;
        private static ICompany[] _companies = new ICompany[8];
        private static float[] _multipliers = new float[8];
        private static int _noted;

        /// <summary>True while <c>SellContracts</c> runs and the channel has records (what P7 checks first).</summary>
        internal static bool IsScopeOpen
        {
            get { return _scopeOpen; }
        }

        // ---- P6: the scope ----

        [ModPatch(typeof(GameContractSeller), "SellContracts", PatchKind.Prefix, Priority = 800)]
        private static void SellContractsPrefix(out bool __state)
        {
            __state = false;
            if (!ModModifiers.HasAny(ModChannels.ContractReward)) return;
            if (!ModHooks.CanRun(CoreId, PrefixSite)) return;

            try
            {
                OpenScope();
                __state = true;
            }
            catch (Exception ex)
            {
                CloseScope();
                __state = false;
                ModHooks.ReportFault(CoreId, PrefixSite, ex);
            }
        }

        [ModPatch(typeof(GameContractSeller), "SellContracts", PatchKind.Postfix)]
        private static void SellContractsPostfix(List<GameCpu> ___cpus, bool __state)
        {
            if (!__state || _noted == 0) return;
            if (!ModHooks.CanRun(CoreId, PostfixSite)) return;

            try
            {
                ScaleIncomeOfCpus(___cpus);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, PostfixSite, ex);
            }
        }

        [ModPatch(typeof(GameContractSeller), "SellContracts", PatchKind.Finalizer, Priority = 0)]
        private static void SellContractsFinalizer(bool __state)
        {
            if (!__state) return;

            try
            {
                CloseScope();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, FinalizerSite, ex);
            }
        }

        // ---- P7: the payment ----

        [ModPatch(typeof(GameCompany), "EarnMoney", PatchKind.Prefix, ArgTypes = new[] { typeof(float), typeof(GameRevenueOrigin) })]
        private static void PlayerEarnMoneyPrefix(GameCompany __instance, ref float value, GameRevenueOrigin revenueOrigin)
        {
            if (!_scopeOpen || revenueOrigin != GameRevenueOrigin.Contracts) return;
            Scale(__instance, ref value, PlayerEarnSite);
        }

        [ModPatch(typeof(GameAiCompany), "EarnMoney", PatchKind.Prefix, ArgTypes = new[] { typeof(float), typeof(GameRevenueOrigin) })]
        private static void AiEarnMoneyPrefix(GameAiCompany __instance, ref float value, GameRevenueOrigin revenueOrigin)
        {
            if (!_scopeOpen || revenueOrigin != GameRevenueOrigin.Contracts) return;
            Scale(__instance, ref value, AiEarnSite);
        }

        /// <summary>
        /// The payment of one delivery, times the company's multiplier. The multiplier is read from the channel once per company and run of
        /// <c>SellContracts</c> and noted for the report; the next delivery of the same company takes it from the note.
        /// </summary>
        private static void Scale(ICompany company, ref float value, string site)
        {
            if (!ModHooks.CanRun(CoreId, site)) return;

            try
            {
                float multiplier;
                if (!TryGetNoted(company, out multiplier))
                {
                    multiplier = ModModifiers.Evaluate(ModChannels.ContractReward, new ModifierContext { Company = company });
                    Note(company, multiplier);
                }
                if (multiplier != 1f) value *= multiplier;
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, site, ex);
            }
        }

        // ---- the scope and its notes ----

        private static void OpenScope()
        {
            ClearNotes();
            _scopeOpen = true;
        }

        private static void CloseScope()
        {
            _scopeOpen = false;
            ClearNotes();
        }

        /// <summary>
        /// <c>IncomeLastDayFromContracts</c> of every CPU that was paid, times the multiplier its company's payments got. A CPU with no income, or of
        /// a company with no note, is left as it is.
        /// </summary>
        private static void ScaleIncomeOfCpus(List<GameCpu> cpus)
        {
            if (cpus == null) return;

            for (int i = 0; i < cpus.Count; i++)
            {
                GameCpu cpu = cpus[i];
                if (ReferenceEquals(cpu, null) || cpu.IncomeLastDayFromContracts == 0f) continue;

                float multiplier;
                if (TryGetNoted(cpu.Company, out multiplier) && multiplier != 1f) cpu.IncomeLastDayFromContracts *= multiplier;
            }
        }

        private static bool TryGetNoted(ICompany company, out float multiplier)
        {
            for (int i = 0; i < _noted; i++)
            {
                if (!ReferenceEquals(_companies[i], company)) continue;
                multiplier = _multipliers[i];
                return true;
            }
            multiplier = 1f;
            return false;
        }

        private static void Note(ICompany company, float multiplier)
        {
            if (_noted == _companies.Length)
            {
                Array.Resize(ref _companies, _noted * 2);
                Array.Resize(ref _multipliers, _noted * 2);
            }
            _companies[_noted] = company;
            _multipliers[_noted] = multiplier;
            _noted++;
        }

        private static void ClearNotes()
        {
            for (int i = 0; i < _noted; i++) _companies[i] = null;   // no references to companies of a scene that goes
            _noted = 0;
        }
    }
}
