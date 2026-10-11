using System;
using System.Globalization;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>What the player and a rival did about a business contract between them (FR-5 item 6).</summary>
    public enum ContractMove
    {
        /// <summary>The player signed a business contract with the rival: <c>triggerDeltas.BusinessSigned</c> (+15).</summary>
        Signed = 0,

        /// <summary>The player broke a business contract with the rival: <c>triggerDeltas.BusinessBrokenByPlayer</c> (-25).</summary>
        BrokenByPlayer = 1,

        /// <summary>A contract with the rival is active on the first of the month: <c>triggerDeltas.BusinessActiveMonthly</c> (+2) for each contract.</summary>
        ActiveMonthly = 2,
    }

    /// <summary>
    /// Where a fixed trigger becomes a change of the attitude (FR-5 item 8, design.md Component 7). <see cref="Record"/> applies the delta of the
    /// trigger from <c>config.json</c> (<c>triggerDeltas</c>: StrongLaunch -8, ShareGain -5, PriceUndercut -4, OemLoss -6, TechLead -3) to the
    /// rival through <see cref="AttitudeLedger.Change"/> (with the sensitivity of its archetype, the clip and the history) and then raises
    /// <see cref="TriggerRaised"/> for the planner of the reactions. The moves with the business contracts (<see cref="RecordContract"/>) only
    /// change the attitude: they are not a threat to answer.
    /// <para>
    /// Nothing is recorded while the <c>blocked</c> test of the constructor says so (a save loads, the fast-forward runs, the game is not ready:
    /// <see cref="TriggerRules.IsBlocked"/>), for a company that is put aside (<c>SetDormant</c>) or one the ledger does not keep (bankrupt, no
    /// archetype). The reason in the history is the name of the trigger and the segment: <c>StrongLaunch:1</c>.
    /// </para>
    /// </summary>
    public sealed class TriggerRecorder
    {
        private readonly AttitudeLedger _ledger;
        private readonly PersonalityBook _book;
        private readonly Func<RivalConfig> _config;
        private readonly Func<bool> _blocked;
        private readonly Action<string> _log;
        private readonly Action<string> _debug;

        /// <param name="ledger">The attitudes.</param>
        /// <param name="book">The characters (who is dormant).</param>
        /// <param name="config">The deltas of the triggers; null or a null result gives the defaults.</param>
        /// <param name="blocked">True when no trigger may be recorded now (see <see cref="TriggerRules.IsBlocked"/>); null means never.</param>
        /// <param name="log">One English line per recorded trigger and per failed handler; may be null.</param>
        /// <param name="debug">One English line per move with a business contract (several a month in a long game); may be null.</param>
        public TriggerRecorder(AttitudeLedger ledger, PersonalityBook book, Func<RivalConfig> config, Func<bool> blocked, Action<string> log = null,
                               Action<string> debug = null)
        {
            _ledger = ledger;
            _book = book;
            _config = config;
            _blocked = blocked;
            _log = log;
            _debug = debug;
        }

        /// <summary>
        /// A trigger was recorded: the attitude of the rival has been changed. Raised by <see cref="Record"/> only; a handler that throws is
        /// logged and the others still run.
        /// </summary>
        public event Action<PlayerTrigger> TriggerRaised;

        /// <summary>The number of triggers recorded since the start (for the diagnostics and the tests).</summary>
        public int Recorded { get; private set; }

        /// <summary>No trigger may be recorded now.</summary>
        public bool IsBlocked
        {
            get { return _blocked != null && _blocked(); }
        }

        /// <summary>
        /// The delta of the attitude for a trigger, from the config (before the sensitivity of the archetype).
        /// </summary>
        public float DeltaOf(TriggerType type)
        {
            RivalConfig.TriggerDeltaSettings deltas = Config.triggerDeltas;
            switch (type)
            {
                case TriggerType.StrongLaunch: return deltas.StrongLaunch;
                case TriggerType.PriceUndercut: return deltas.PriceUndercut;
                case TriggerType.ShareGain: return deltas.ShareGain;
                case TriggerType.OemLoss: return deltas.OemLoss;
                case TriggerType.TechLead: return deltas.TechLead;
                default: return 0f;
            }
        }

        /// <summary>The delta of the attitude for a move with a business contract, from the config.</summary>
        public float DeltaOf(ContractMove move)
        {
            RivalConfig.TriggerDeltaSettings deltas = Config.triggerDeltas;
            switch (move)
            {
                case ContractMove.Signed: return deltas.BusinessSigned;
                case ContractMove.BrokenByPlayer: return deltas.BusinessBrokenByPlayer;
                case ContractMove.ActiveMonthly: return deltas.BusinessActiveMonthly;
                default: return 0f;
            }
        }

        /// <summary>
        /// Records a trigger for the rival: changes its attitude and raises <see cref="TriggerRaised"/>. True when the attitude took the change.
        /// False when nothing may be recorded now, the rival is dormant, or the ledger does not keep it (bankrupt, no archetype).
        /// </summary>
        public bool Record(TriggerType type, int uniqueId, Segment segment, string detail)
        {
            if (IsBlocked || _book.IsDormant(uniqueId)) return false;

            string reason = ReasonOf(type, segment);
            if (!_ledger.Change(uniqueId, DeltaOf(type), reason)) return false;

            Recorded++;
            Say("trigger " + reason + " for ai:" + uniqueId.ToString(CultureInfo.InvariantCulture)
                + (string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")"));

            Action<PlayerTrigger> handlers = TriggerRaised;
            if (handlers != null)
            {
                var trigger = new PlayerTrigger(type, uniqueId, segment, detail);
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    try
                    {
                        ((Action<PlayerTrigger>)handler)(trigger);
                    }
                    catch (Exception e)
                    {
                        Say("a handler of TriggerRaised failed (" + e.GetType().Name + ": " + e.Message + ")");
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Records a move with a business contract: changes the attitude of the rival by the delta of the move. No trigger is raised. False when
        /// nothing may be recorded now, the rival is dormant, or the ledger does not keep it.
        /// </summary>
        public bool RecordContract(ContractMove move, int uniqueId)
        {
            if (IsBlocked || _book.IsDormant(uniqueId)) return false;

            string reason = "Business" + move;
            if (!_ledger.Change(uniqueId, DeltaOf(move), reason)) return false;

            if (_debug != null) _debug("contract move " + reason + " for ai:" + uniqueId.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>The reason written to the history: the name of the trigger, and the number of the segment when it has one (<c>StrongLaunch:1</c>).</summary>
        public static string ReasonOf(TriggerType type, Segment segment)
        {
            string name = ReactionKinds.NameOf(type);
            return segment == Segment.Any ? name : name + ":" + ((int)segment).ToString(CultureInfo.InvariantCulture);
        }

        private RivalConfig Config
        {
            get
            {
                RivalConfig config = _config != null ? _config() : null;
                return config ?? RivalConfig.Defaults();
            }
        }

        private void Say(string line)
        {
            if (_log != null) _log(line);
        }
    }
}
