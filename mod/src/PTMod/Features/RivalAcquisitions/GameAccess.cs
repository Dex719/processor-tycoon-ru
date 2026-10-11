using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PTMod.Core;
using GameBankruptcy = ProcessorTycoon.Bank.BankruptcyHandler;
using GameBusinessContract = ProcessorTycoon.CompanySystem.Business.BusinessContract;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using GameResearch = ProcessorTycoon.ResearchSystem.ResearchSector;
using ICompany = ProcessorTycoon.CompanySystem.ICompany;

namespace PTMod.Features.RivalAcquisitions
{
    /// <summary>
    /// The private members of the game the feature needs (design.md, "Члены игры через рефлексию"; checked statically against the decompiled code by the
    /// spikes TSK-003 and TSK-004, see <c>mod/requests/rival-acquisitions.md</c>, item 3). Exactly four, all looked up once with <c>AccessTools</c>:
    /// <list type="bullet">
    /// <item><c>ResearchSector.temporarilySupressPopup</c> (<c>bool</c>): no "Research completed" window while the technologies of a deal are unlocked,
    /// as the game's own <c>UnlockNodeTechnology</c> does.</item>
    /// <item><c>Cpu.manufacturer</c> (<c>ICompany</c>) and <c>Cpu.currentContract</c> (<c>BusinessContract</c>): the caches of the maker and of the foundry
    /// contract of a CPU; they are dropped when the maker or the owner of a CPU changes, so the getters look them up again.</item>
    /// <item><c>BankruptcyHandler.OnCompanyWentBankrupt</c>: the field behind the event; its handlers are called when a deal takes its target out
    /// (D2), so <c>LicenseHelper</c> revokes the x86 licence at once.</item>
    /// </list>
    /// A member that is not found makes its operation a no-op that returns false; <see cref="Missing"/> names it for the self-check.
    /// </summary>
    internal static class GameAccess
    {
        private const string Feature = RivalAcquisitionsFeature.FeatureId;

        private static bool _resolved;
        private static FieldInfo _suppressPopup;
        private static FieldInfo _manufacturer;
        private static FieldInfo _currentContract;
        private static FieldInfo _bankruptEvent;

        /// <summary>The members that are not in the game (another game version?); empty when all four are there.</summary>
        internal static List<string> Missing()
        {
            Resolve();
            var missing = new List<string>();
            if (_suppressPopup == null) missing.Add("ResearchSector.temporarilySupressPopup");
            if (_manufacturer == null) missing.Add("Cpu.manufacturer");
            if (_currentContract == null) missing.Add("Cpu.currentContract");
            if (_bankruptEvent == null) missing.Add("BankruptcyHandler.OnCompanyWentBankrupt (field)");
            return missing;
        }

        /// <summary>Sets <c>temporarilySupressPopup</c> of a research sector. False when the member is missing (the windows then appear).</summary>
        internal static bool SetSuppressPopup(GameResearch research, bool value)
        {
            Resolve();
            if (_suppressPopup == null || research == null) return false;
            _suppressPopup.SetValue(research, value);
            return true;
        }

        /// <summary>
        /// Drops the caches of the maker and of the foundry contract of <paramref name="cpu"/>: the next read of <c>Manufacturer</c> takes
        /// <c>OutsourcedID</c> (or the owner), the next <c>MarkupPrice</c> looks the contract up again. False when a member is missing.
        /// </summary>
        internal static bool ResetProductionCaches(GameCpu cpu)
        {
            Resolve();
            if (cpu == null) return false;
            bool ok = true;
            if (_manufacturer != null) _manufacturer.SetValue(cpu, null);
            else ok = false;
            if (_currentContract != null) _currentContract.SetValue(cpu, null);
            else ok = false;
            return ok;
        }

        /// <summary>
        /// Calls the handlers of <c>BankruptcyHandler.OnCompanyWentBankrupt</c> for <paramref name="company"/>, one by one: a handler that throws does not
        /// stop the others (a feature's handler is counted against that feature, a handler of the game is logged). Returns the number of handlers
        /// called, or -1 when the handler object or the member is missing (the consequences then come with the next load of the save).
        /// </summary>
        internal static int NotifyCompanyWentBankrupt(ICompany company)
        {
            Resolve();
            GameBankruptcy handler = GameBankruptcy.Instance;
            if (_bankruptEvent == null || handler == null || company == null) return -1;

            var chain = _bankruptEvent.GetValue(handler) as Action<ICompany>;
            if (chain == null) return 0;

            int called = 0;
            foreach (Delegate each in chain.GetInvocationList())
            {
                var one = each as Action<ICompany>;
                if (one == null) continue;
                called++;
                try
                {
                    one(company);
                }
                catch (Exception e)
                {
                    Type owner = one.Method != null ? one.Method.DeclaringType : null;
                    string feature = owner != null ? ModFeatures.FeatureOfType(owner) : null;
                    if (!string.IsNullOrEmpty(feature)) ModHooks.ReportFault(feature, "BankruptcyHandler.OnCompanyWentBankrupt", e);
                    else ModLog.Warn(Feature, "deal: a handler of OnCompanyWentBankrupt (" + (owner != null ? owner.FullName : "?") + ") threw: " + e.GetType().Name + ": " + e.Message);
                }
            }
            return called;
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _suppressPopup = Field(typeof(GameResearch), "temporarilySupressPopup", typeof(bool));
            _manufacturer = Field(typeof(GameCpu), "manufacturer", typeof(ICompany));
            _currentContract = Field(typeof(GameCpu), "currentContract", typeof(GameBusinessContract));
            _bankruptEvent = Field(typeof(GameBankruptcy), "OnCompanyWentBankrupt", typeof(Action<ICompany>));
        }

        /// <summary>The field, when it exists with the expected type; null otherwise (logged once).</summary>
        private static FieldInfo Field(Type type, string name, Type expected)
        {
            FieldInfo field = null;
            try
            {
                field = AccessTools.Field(type, name);
            }
            catch (Exception)
            {
                field = null;
            }

            if (field != null && expected.IsAssignableFrom(field.FieldType)) return field;
            ModLog.Warn(Feature, "reflection: " + type.Name + "." + name + (field == null ? " is not found" : " has the type " + field.FieldType.Name + ", not " + expected.Name)
                                 + " (another game version?); the step that needs it is skipped");
            return null;
        }
    }
}
