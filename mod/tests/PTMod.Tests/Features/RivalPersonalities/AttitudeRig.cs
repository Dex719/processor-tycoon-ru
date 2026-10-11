using System;
using System.Collections.Generic;
using PTMod.Features.RivalPersonalities;

namespace PTMod.Tests.Features.RivalPersonalities
{
    /// <summary>
    /// The attitude ledger, the trigger recorder and the monthly pass on the shipped content and a plain state, for the tests of TSK-010..012. Internal,
    /// so the runner never discovers it. The company numbers are the <c>UniqueID</c> of <c>assignments.json</c>.
    /// </summary>
    internal sealed class AttitudeRig
    {
        public const int Intel = 0;          // hegemon: baseline -10, sensitivity 1.2
        public const int Amd = 1;            // price_warrior: baseline -15, sensitivity 1.3
        public const int Ibm = 4;            // engineer: baseline 0, sensitivity 0.8
        public const int Toshiba = 6;        // fast_follower: baseline 0, sensitivity 1.0
        public const int Tsmc = 9;           // foundry_partner: baseline +20, sensitivity 0.5

        public RivalState State = new RivalState();
        public readonly ArchetypeCatalog Catalog;
        public readonly PersonalityBook Book;
        public readonly AttitudeLedger Ledger;
        public readonly TriggerRecorder Recorder;
        public readonly TriggerEvaluator Evaluator;
        public readonly MonthlyEvaluatorCore Monthly;

        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Log = new List<string>();
        public readonly List<string> LevelEvents = new List<string>();
        public readonly List<PlayerTrigger> Triggers = new List<PlayerTrigger>();
        public readonly HashSet<int> Bankrupt = new HashSet<int>();

        public DateTime Today = new DateTime(1990, 5, 1);

        /// <summary>The recorder is blocked (a load, the fast-forward, the game is not ready).</summary>
        public bool Blocked;

        public AttitudeRig()
        {
            Catalog = RivalTestKit.LoadShippedCatalog();
            Book = new PersonalityBook(() => State, () => Catalog, Warnings.Add);
            Ledger = new AttitudeLedger(Book, () => State, () => Catalog, () => Today, uid => Bankrupt.Contains(uid), Warnings.Add);
            Ledger.LevelChanged += (uid, from, to) => LevelEvents.Add(uid + ":" + from + ">" + to);

            Recorder = new TriggerRecorder(Ledger, Book, () => Catalog.Config, () => Blocked, Log.Add);
            Recorder.TriggerRaised += Triggers.Add;
            Evaluator = new TriggerEvaluator(Recorder, () => Catalog.Config, Warnings.Add);
            Monthly = new MonthlyEvaluatorCore(() => State, () => Catalog.Config, Recorder, Ledger, Log.Add);
        }

        /// <summary>Gives the companies their archetypes (the attitude starts at the baseline).</summary>
        public AttitudeRig Assign(params int[] uniqueIds)
        {
            foreach (int uid in uniqueIds) Book.EnsureAssigned(uid);
            return this;
        }

        /// <summary>Gives all thirteen historical companies their archetypes.</summary>
        public AttitudeRig AssignAll()
        {
            for (int uid = 0; uid <= 12; uid++) Book.EnsureAssigned(uid);
            return this;
        }

        /// <summary>Puts a value into the saved attitude of a company.</summary>
        public AttitudeRig SetAttitude(int uid, float value)
        {
            foreach (AttitudeRecord record in State.attitudes)
            {
                if (record.uid == uid)
                {
                    record.value = value;
                    return this;
                }
            }
            State.attitudes.Add(new AttitudeRecord { uid = uid, value = value });
            return this;
        }

        public AttitudeRecord RecordOf(int uid)
        {
            foreach (AttitudeRecord record in State.attitudes)
            {
                if (record.uid == uid) return record;
            }
            return null;
        }
    }
}
