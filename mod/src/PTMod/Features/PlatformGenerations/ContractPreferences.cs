using System;
using System.Collections.Generic;
using PTMod.Core;
using GameContract = ProcessorTycoon.ContractSystem.Contract;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The game side of the OEM preferences of contracts (FR-8 items 3 and 8): gives a contract its preferred generation once
    /// (<see cref="OemAdoptionModel.AssignPreference"/> with the adoption on the day the contract was made and <c>ModRandom.WorldSeed</c>),
    /// stores it in <see cref="OemPreferences"/>, and drops the preferences of contracts that are gone. Used by the provider of
    /// <c>contract.signChance</c> (a contract it meets without a preference, E9), by the handler of <c>ContractManager.OnNewContracts</c> and by the
    /// resynchronization (the contracts of a save or of the fast-forward). Nothing here throws into the game: the callers are guarded.
    /// </summary>
    internal sealed class ContractPreferences
    {
        private readonly PlatformGenerationsFeature _feature;
        private readonly List<GenAdoptionInput> _inputs = new List<GenAdoptionInput>();
        private readonly List<GameContract> _contracts = new List<GameContract>();
        private readonly HashSet<string> _live = new HashSet<string>(StringComparer.Ordinal);

        public ContractPreferences(PlatformGenerationsFeature feature)
        {
            _feature = feature;
        }

        /// <summary>
        /// The contract has a preference (an old one or one given now). A contract whose type is not in <c>contractTypes</c> gets none and stays
        /// without (the provider answers 1 for it); nothing is assigned until the generations are loaded. False = no preference was or could be recorded.
        /// </summary>
        public bool Ensure(GameContract contract, string key, int contractType)
        {
            if (string.IsNullOrEmpty(key)) return false;

            PlatformGenerationsFeature feature = _feature;
            if (feature.Preferences.PreferenceOf(key) != null) return true;
            if (!feature.CatalogReady || !feature.Balance.Oem.IncludesContractType(contractType)) return false;

            GameDate created = GameAdapter.CreatedOn(contract);
            if (!created.IsSet) created = GameAdapter.Today();

            OemAdoptionModel.BuildInputs(feature.Catalog, feature.Timeline, feature.InstalledBase, created, _inputs);
            string genId = OemAdoptionModel.AssignPreference(key, ModRandom.WorldSeed, _inputs);
            return feature.Preferences.Assign(key, genId);
        }

        /// <summary>Gives a preference to every available and active contract that has none. Returns how many were assigned; 0 without a manager.</summary>
        public int AssignMissing()
        {
            if (!_feature.CatalogReady || !GameAdapter.CollectContracts(_contracts)) return 0;

            int assigned = 0;
            for (int i = 0; i < _contracts.Count; i++)
            {
                GameContract contract = _contracts[i];
                if (ReferenceEquals(contract, null)) continue;

                string key = GameAdapter.ContractKey(contract);
                if (string.IsNullOrEmpty(key) || _feature.Preferences.PreferenceOf(key) != null) continue;
                if (Ensure(contract, key, GameAdapter.ContractTypeOf(contract))) assigned++;
            }
            _contracts.Clear();
            return assigned;
        }

        /// <summary>
        /// FR-8 item 8: drops the preferences of contracts that are neither available nor active. Does nothing (nothing is lost) when the
        /// contracts cannot be read. Returns how many were dropped.
        /// </summary>
        public int Prune()
        {
            if (!GameAdapter.CollectContracts(_contracts)) return 0;

            _live.Clear();
            for (int i = 0; i < _contracts.Count; i++)
            {
                if (ReferenceEquals(_contracts[i], null)) continue;
                string key = GameAdapter.ContractKey(_contracts[i]);
                if (key != null) _live.Add(key);
            }
            _contracts.Clear();

            HashSet<string> live = _live;
            return _feature.Preferences.Prune(key => live.Contains(key));
        }
    }
}
