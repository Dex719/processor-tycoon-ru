using System.Collections.Generic;
using PTMod.Core;
using PTMod.Core.Logic.Json;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Features.BankruptcyRecovery
{
    /// <summary>
    /// Loads the content of the feature through <c>ModContent</c> (FR-12 item 1): <c>balance.json</c>, the built-in file with the player's override
    /// (<c>&lt;game folder&gt;/PTMod/content/bankruptcy-recovery/balance.json</c>) on top. The thin game side of <see cref="RecoveryBalance"/>: the
    /// reading and the range checks are its own. Every problem is one line in the log under the feature's id (AC-12.1).
    /// </summary>
    internal static class RecoveryContent
    {
        /// <summary>The content file with the numbers of the feature.</summary>
        public const string BalanceFile = "balance.json";

        /// <summary>The numbers of the feature. A broken file gives the defaults of the code, a broken field gives the default of that field.</summary>
        public static RecoveryBalance LoadBalance()
        {
            JsonObject config = ModContent.LoadConfig(RecoveryApi.FeatureId, BalanceFile, RecoveryBalance.Schema);
            var problems = new List<string>();
            RecoveryBalance balance = RecoveryBalance.FromJson(config, problems);
            foreach (string problem in problems) ModLog.Warn(RecoveryApi.FeatureId, problem);
            return balance;
        }
    }
}
