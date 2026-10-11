using PTMod.Core;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// The flags of the save the marks of a settlement live in (<see cref="SettlementMarks"/>): <c>ModSave.Flags</c> under the id of the feature. The core keeps them in
    /// its own section of the side file, so they are written with every save and read back whether the feature is on or off, and a new game starts without them.
    /// </summary>
    internal sealed class SaveMarks : ISettlementFlags
    {
        public int GetInt(string key)
        {
            return ModSave.Flags.GetInt(RecoveryApi.FeatureId, key, 0);
        }

        public void SetInt(string key, int value)
        {
            ModSave.Flags.SetInt(RecoveryApi.FeatureId, key, value);
        }
    }
}
