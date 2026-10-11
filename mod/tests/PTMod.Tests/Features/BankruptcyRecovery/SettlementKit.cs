using System;
using System.Collections.Generic;
using PTMod.Features.BankruptcyRecovery.Logic;

namespace PTMod.Tests.Features.BankruptcyRecovery
{
    /// <summary>
    /// The flags of a save as a table (<see cref="ISettlementFlags"/>): the stand-in of <c>ModSave.Flags</c>, which the core keeps in its own section of the side
    /// file. <see cref="Clone"/> is a copy of the table, as a save and a load make one. Internal, so the runner never discovers it.
    /// </summary>
    internal sealed class FakeSettlementFlags : ISettlementFlags
    {
        public readonly Dictionary<string, int> Values = new Dictionary<string, int>(StringComparer.Ordinal);

        public int GetInt(string key)
        {
            int value;
            return Values.TryGetValue(key, out value) ? value : 0;
        }

        public void SetInt(string key, int value)
        {
            Values[key] = value;
        }

        public FakeSettlementFlags Clone()
        {
            var copy = new FakeSettlementFlags();
            foreach (KeyValuePair<string, int> pair in Values) copy.Values[pair.Key] = pair.Value;
            return copy;
        }
    }

    /// <summary>
    /// The states and rigs the tests of the safe switch-off start from: the player with every kind of obligation of the feature. Internal.
    /// </summary>
    internal static class SettlementKit
    {
        /// <summary>The sum <see cref="PopulatedRig"/> owes: two loans, the drawn part of the line, what the investor may still take.</summary>
        public const double PopulatedTotal = 6527277.0 + 36075000.0 + 2660000.0 + (108225000.0 - 2400000.0);

        /// <summary>
        /// A rig whose player owes through every part of the feature (<see cref="RecoveryTestData.PopulatedState"/>): the bridge loan <c>bridge-1</c> (6 527 277), the
        /// rescue loan <c>rescue-2</c> (36 075 000), 2 660 000 drawn on the open line, an investor contract with 105 825 000 left to pay; one rescue used, the
        /// last bridge loan closed on 1974-02-01. The balance is 200 000 000 and the threshold 24 300 000, the date 1977-06-01.
        /// </summary>
        public static RecoveryRig PopulatedRig()
        {
            var rig = new RecoveryRig();
            rig.State = RecoveryTestData.PopulatedState();
            rig.Finance.MoneyValue = 200000000.0;
            rig.Finance.DebtLimit = 24300000.0;
            rig.Finance.TodayValue = new DateTime(1977, 6, 1);
            return rig;
        }

        /// <summary>A rig with one bridge loan of 5 000 000 left and 2 000 000 in the bank: the example AC-12.2.</summary>
        public static RecoveryRig OneBridgeRig()
        {
            var rig = new RecoveryRig();
            rig.State.loans.Add(new LoanData
            {
                id = "bridge-1", kind = LoanData.KindBridge, principal = 8400000.0, outstanding = 5000000.0, annualRate = 0.30,
                monthsTotal = 12, monthsLeft = 7, payment = 818892.0, openedOn = "1976-03-01",
            });
            rig.State.nextLoanSeq = 2;
            rig.Finance.MoneyValue = 2000000.0;
            rig.Finance.DebtLimit = 8400000.0;
            rig.Finance.TodayValue = new DateTime(1976, 9, 1);
            return rig;
        }

        /// <summary>The state as the JSON of the section: what a save holds and what the core hands back.</summary>
        public static string Describe(RecoveryState state)
        {
            return PTMod.Core.Logic.Json.ModJson.Write(RecoveryStateCodec.Write(state), false);
        }
    }
}
