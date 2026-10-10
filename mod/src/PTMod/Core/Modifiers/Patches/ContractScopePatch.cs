using System;
using System.Collections.Generic;
using GameContract = ProcessorTycoon.ContractSystem.Contract;

namespace PTMod.Core
{
    /// <summary>
    /// The contract whose list of offers the game is building right now (FR-12 item 13, D24): the stack that patch P23 fills around
    /// <c>Contract.GetBestOffers</c> and patch P8 (<see cref="SignChancePatch"/>) reads. <c>Contract.CpuOffer</c> does not know its contract, but
    /// <c>DefineChance</c> only runs inside <c>GetBestOffers</c> of one contract, so the contract of the innermost call is the one the chance is for.
    /// <list type="bullet">
    /// <item><b>Outside <c>GetBestOffers</c> there is no contract:</b> <see cref="Current"/> is null, and so is <c>ModifierContext.Contract</c> in every
    /// patch but P8 (AC-12.7).</item>
    /// <item><b>A stack, not a field:</b> a provider of <c>contract.signChance</c> may call <c>Contract.ChanceToSign</c> of another contract and
    /// enter <c>GetBestOffers</c> again; the outer question gets its own contract back when the inner one is left.</item>
    /// <item>Main thread only (the game's contract code never leaves it), so no locking; the array is reused, a push allocates nothing once
    /// it has grown to the depth the game uses (one or two).</item>
    /// </list>
    /// </summary>
    internal static class ModContractScope
    {
        /// <summary>Deeper nesting than this is a runaway recursion in a provider; the contract is then not pushed (and not popped).</summary>
        internal const int MaxDepth = 32;

        private static GameContract[] _stack = new GameContract[4];
        private static int _depth;

        /// <summary>The contract of the innermost <c>GetBestOffers</c> that is running, or null.</summary>
        internal static GameContract Current
        {
            get { return _depth > 0 ? _stack[_depth - 1] : null; }
        }

        /// <summary>How many <c>GetBestOffers</c> calls are open.</summary>
        internal static int Depth
        {
            get { return _depth; }
        }

        /// <summary>Enters a contract. False (nothing pushed, so nothing to pop) for null or when <see cref="MaxDepth"/> is reached.</summary>
        internal static bool Push(GameContract contract)
        {
            if (ReferenceEquals(contract, null) || _depth >= MaxDepth) return false;

            if (_depth == _stack.Length) Array.Resize(ref _stack, _stack.Length * 2);
            _stack[_depth++] = contract;
            return true;
        }

        /// <summary>Leaves the innermost contract (does nothing when the stack is empty). The reference is dropped, so a contract is not kept alive.</summary>
        internal static void Pop()
        {
            if (_depth == 0) return;

            _depth--;
            _stack[_depth] = null;
        }

        /// <summary>Empties the stack (the offline harness; the game balances it with the finalizer).</summary>
        internal static void Clear()
        {
            while (_depth > 0) Pop();
        }
    }

    /// <summary>
    /// P23: <c>Contract.GetBestOffers(List&lt;int&gt;)</c>, private; prefix + finalizer. The prefix enters the contract (<see cref="ModContractScope.Push"/>)
    /// and the finalizer leaves it, also when the method throws. Both callers of <c>GetBestOffers</c> get the same context, so the chance the
    /// contract window shows (<c>Contract.ChanceToSign</c>) and the chance the draw uses (<c>Contract.ChooseCpu</c>) come out of one formula (AC-12.7).
    /// <list type="bullet">
    /// <item>The method is reserved for the core (<c>ReservedTargets</c>: channel <c>contract.signChance</c>): a feature's patch on it is rejected.
    /// A provider that wants the contract reads <c>ctx.Contract</c> (<c>ContractID</c>, <c>Company</c>).</item>
    /// <item>The prefix goes first among the prefixes and the finalizer last among the finalizers, so other patches of the method see the contract
    /// for the whole call.</item>
    /// <item>The finalizer pops only when its prefix pushed (<c>__state</c>): a prefix skipped by the circuit breaker cannot unbalance the stack,
    /// and a finalizer is never skipped once a contract was entered, so the stack cannot stay deep.</item>
    /// <item>Cost: a unit check and an array store per call, no allocation. The contract is pushed whether or not <c>contract.signChance</c> has
    /// records, so <see cref="ModContractScope.Current"/> is always the truth.</item>
    /// </list>
    /// To check in the game (the owner, with <c>framework-sample</c>, TSK-053): with a provider of <c>contract.signChance</c> that returns ×1.25
    /// for one <c>ContractID</c> and ×1 for the others, the contract window shows for that contract the chance the draw uses, and the other contracts
    /// keep theirs; <c>Player.log</c> has no <c>P23</c> faults.
    /// </summary>
    internal static class ContractScopePatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string PrefixSite = "P23/prefix";
        private const string FinalizerSite = "P23/finalizer";

        [ModPatch(typeof(GameContract), "GetBestOffers", PatchKind.Prefix, ArgTypes = new[] { typeof(List<int>) }, Priority = 800)]
        private static void GetBestOffersPrefix(GameContract __instance, out bool __state)
        {
            __state = false;
            if (!ModHooks.CanRun(CoreId, PrefixSite)) return;

            try
            {
                __state = ModContractScope.Push(__instance);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, PrefixSite, ex);
            }
        }

        [ModPatch(typeof(GameContract), "GetBestOffers", PatchKind.Finalizer, ArgTypes = new[] { typeof(List<int>) }, Priority = 0)]
        private static void GetBestOffersFinalizer(bool __state)
        {
            if (!__state) return;

            try
            {
                ModContractScope.Pop();
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, FinalizerSite, ex);
            }
        }
    }
}
