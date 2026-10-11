using System;
using System.Globalization;
using PTMod.Core;
using PTMod.Core.Logic.Companies;
using AICompany = ProcessorTycoon.CompanySystem.AICompany;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// The game side of <see cref="AttitudeLedger"/> (TSK-010; FR-4 items 4 and 5): shows the player the notification when the attitude of a rival
    /// crosses the border of a level (<see cref="AttitudeLedger.LevelChanged"/>, <c>rival.notify.level</c>: "AMD: attitude towards you is now
    /// "Rivalry""), and tells the ledger whether a company is bankrupt. Main thread only; the ledger isolates the handler.
    /// </summary>
    internal static class AttitudeNotifier
    {
        private const string Feature = RivalPersonalitiesFeature.FeatureId;

        /// <summary><see cref="AttitudeLedger.LevelChanged"/>: one notification and one log line.</summary>
        public static void OnLevelChanged(int uniqueId, AttitudeLevel from, AttitudeLevel to)
        {
            if (!ModFeatures.IsActive(Feature)) return;

            string name = NameOf(uniqueId);
            ModLog.Info(Feature, "attitude of " + name + " (ai:" + uniqueId.ToString(CultureInfo.InvariantCulture) + ") crossed a border: " + from + " -> " + to);
            ModNotify.ShowKey(Feature, "rival.notify.level", name, ModL10n.L("rival.level." + AttitudeLedger.LevelKeyName(to)));
        }

        /// <summary>The AI company with this <c>UniqueID</c> is bankrupt (<see cref="AttitudeLedger"/> freezes its attitude). False for a company the core does not know.</summary>
        public static bool IsBankrupt(int uniqueId)
        {
            ICompany company = Resolve(uniqueId);
            return company != null && company.IsBankrupt;
        }

        /// <summary>The name of the AI company for the player; <c>ai:&lt;UniqueID&gt;</c> when the company is not found.</summary>
        public static string NameOf(int uniqueId)
        {
            var company = Resolve(uniqueId) as AICompany;
            string name = null;
            if (!ReferenceEquals(company, null) && company != null)
            {
                try
                {
                    name = company.Name;
                }
                catch (Exception)
                {
                    name = null;
                }
            }
            return string.IsNullOrEmpty(name) ? "ai:" + uniqueId.ToString(CultureInfo.InvariantCulture) : name;
        }

        private static ICompany Resolve(int uniqueId)
        {
            string key = CompanyKey.ForAi(uniqueId);
            return key != null ? ModCompanies.Resolve(key) : null;
        }
    }
}
