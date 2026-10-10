using System;
using System.Reflection;
using GameContract = ProcessorTycoon.ContractSystem.Contract;
using GameCpu = ProcessorTycoon.Hardware.Cpu;

namespace PTMod.Core
{
    /// <summary>
    /// P8: <c>Contract.CpuOffer.DefineChance(float)</c>, postfix (FR-12 item 13). Channel <c>contract.signChance</c>: the chance that a contract
    /// picks a CPU is the share of its IPS among the best offers, <c>Chance = clamp(round(ipsShare × 100), 1, 100)</c> in the game; the mod
    /// puts the multiplier in: <c>Chance = clamp(round(ipsShare × 100 × M), 1, 100)</c>, with <c>M = M(contract.signChance, ctx)</c> and
    /// <c>ctx = { Company = cpu.Company, Cpu = cpu, Contract = ModContractScope.Current }</c>.
    /// <list type="bullet">
    /// <item><b>One chance for the draw and the window.</b> <c>Contract.ChooseCpu</c> (the draw) and <c>Contract.ChanceToSign</c> (the window)
    /// both read <c>CpuOffer.Chance</c> from <c>GetBestOffers</c>, where this postfix sets it, so they cannot differ.</item>
    /// <item><b>The contract</b> comes from <see cref="ContractScopePatch"/> (P23): <c>CpuOffer</c> does not keep it. Two contracts that share a CPU on
    /// 1 January are asked apart, and the day's cache keeps them apart (<c>ContractID</c> is part of the key, AC-12.7).</item>
    /// <item><b>The type is private</b> (<c>Contract.CpuOffer</c>): the handler takes <c>object __instance</c> and reaches <c>Cpu</c> and the private
    /// setter of <c>Chance</c> through delegates that are built once (<see cref="OfferAccess"/>), not through reflection per call.</item>
    /// <item><b>Nothing registered, nothing done:</b> without records on the channel the postfix returns after one lookup, and the game's chance
    /// stands. A multiplier of exactly 1 leaves it alone as well, so the vanilla rounding is never touched without a reason. A share that is
    /// not a number (a contract whose offers have no IPS) is the game's to handle.</item>
    /// <item>Failure is the core's unit <c>P8</c>: the exception is counted, the chance stays the game's, and after <c>hooks.faultThreshold</c>
    /// failures the patch stops answering until the game restarts.</item>
    /// <item><b>Inlining.</b> <c>DefineChance</c> is 27 bytes of IL. As far as is known Mono's default limit for inlining a callee is about 20 bytes, so a call
    /// from <c>GetBestOffers</c> reaches the patched method; the optimizing JIT of the .NET Framework (which the offline harness runs on, with optimizations
    /// off for this reason) inlines it and the postfix would never run. If the spike S1 counter "DefineChance called inside GetBestOffers" (or a contract
    /// window that ignores a provider) says it does not fire on the game's Mono, the chance has to be set from a postfix on <c>GetBestOffers</c> instead
    /// (from the offers of its result, share = IPS / sum of IPS).</item>
    /// </list>
    /// To check in the game (the owner, with <c>framework-sample</c>, TSK-053): a provider of <c>contract.signChance</c> that returns ×1.25 for
    /// every CPU of the player turns a share of 40 % into 50 % in the contract window and in the draw (a new batch of contracts on 1 January
    /// picks the player's CPU more often); with the feature off the window shows the game's numbers again.
    /// </summary>
    internal static class SignChancePatch
    {
        private const string CoreId = ModLog.CoreFeatureId;
        private const string Site = "P8/postfix";
        private const int MinChance = 1;
        private const int MaxChance = 100;

        private static OfferAccess _access;

        [ModPatch(typeof(GameContract), "DefineChance", PatchKind.Postfix, InnerType = "CpuOffer", ArgTypes = new[] { typeof(float) })]
        private static void DefineChancePostfix(object __instance, float ipsShare)
        {
            if (!ModModifiers.HasAny(ModChannels.ContractSignChance)) return;
            if (!ModHooks.CanRun(CoreId, Site)) return;

            try
            {
                OfferAccess access = _access ?? (_access = OfferAccess.For(__instance));
                GameCpu cpu = access.CpuOf(__instance);
                if (ReferenceEquals(cpu, null)) return;

                var context = new ModifierContext { Company = cpu.Company, Cpu = cpu, Contract = ModContractScope.Current };
                float multiplier = ModModifiers.Evaluate(ModChannels.ContractSignChance, context);
                if (multiplier == 1f) return;

                int chance;
                if (!TryChance(ipsShare, multiplier, out chance)) return;
                access.SetChance(__instance, chance);
            }
            catch (Exception ex)
            {
                ModHooks.ReportFault(CoreId, Site, ex);
            }
        }

        /// <summary>
        /// <c>clamp(round(ipsShare × 100 × multiplier), 1, 100)</c>, rounded like the game's <c>Mathf.RoundToInt</c> (to even on a tie). False for a share
        /// or a multiplier that is not a finite number: the game's own chance for it stays.
        /// </summary>
        internal static bool TryChance(float ipsShare, float multiplier, out int chance)
        {
            chance = 0;
            float scaled = ipsShare * 1e+02f * multiplier;
            if (float.IsNaN(scaled) || float.IsInfinity(scaled)) return false;

            double rounded = Math.Round(scaled);
            chance = rounded < MinChance ? MinChance : rounded > MaxChance ? MaxChance : (int)rounded;
            return true;
        }

        /// <summary>
        /// Reads <c>CpuOffer.Cpu</c> and writes <c>CpuOffer.Chance</c> (a property with a private setter) of the private nested type
        /// <c>Contract.CpuOffer</c>, which no code of the mod can name. One open-instance delegate per accessor, made by a generic helper
        /// over the runtime type, so a call is a delegate call.
        /// </summary>
        private abstract class OfferAccess
        {
            internal abstract GameCpu CpuOf(object offer);

            internal abstract void SetChance(object offer, int chance);

            /// <summary>The accessor for the type of <paramref name="offer"/>; throws when it does not have the members of the game's class (another game version).</summary>
            internal static OfferAccess For(object offer)
            {
                Type type = offer.GetType();
                PropertyInfo cpu = type.GetProperty("Cpu", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo chance = type.GetProperty("Chance", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo getCpu = cpu == null ? null : cpu.GetGetMethod(true);
                MethodInfo setChance = chance == null ? null : chance.GetSetMethod(true);
                if (getCpu == null || setChance == null || cpu.PropertyType != typeof(GameCpu) || chance.PropertyType != typeof(int))
                    throw new MissingMemberException(type.FullName, "Cpu / Chance (the offer of a contract is not the one this patch was written for)");

                Type closed = typeof(OfferAccess<>).MakeGenericType(type);
                return (OfferAccess)Activator.CreateInstance(closed, getCpu, setChance);
            }
        }

        private sealed class OfferAccess<T> : OfferAccess where T : class
        {
            private readonly Func<T, GameCpu> _getCpu;
            private readonly Action<T, int> _setChance;

            public OfferAccess(MethodInfo getCpu, MethodInfo setChance)
            {
                _getCpu = (Func<T, GameCpu>)Delegate.CreateDelegate(typeof(Func<T, GameCpu>), getCpu);
                _setChance = (Action<T, int>)Delegate.CreateDelegate(typeof(Action<T, int>), setChance);
            }

            internal override GameCpu CpuOf(object offer)
            {
                return _getCpu((T)offer);
            }

            internal override void SetChance(object offer, int chance)
            {
                _setChance((T)offer, chance);
            }
        }
    }
}
