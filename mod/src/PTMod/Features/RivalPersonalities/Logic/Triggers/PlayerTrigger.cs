using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>
    /// A move of the player that a rival noticed (FR-5, design.md Component 7): what it was, which rival noticed it and in which segment. Raised by
    /// <see cref="TriggerRecorder"/> after the attitude of the rival was changed; the planner of the reactions (TSK-013) listens to it.
    /// </summary>
    public struct PlayerTrigger
    {
        /// <param name="type">What the player did.</param>
        /// <param name="uniqueId">The <c>UniqueID</c> of the rival that noticed it.</param>
        /// <param name="segment">The segment of the move; <see cref="Segment.Any"/> when it has none (a technology) or it is not known.</param>
        /// <param name="detail">A short text for the log and the panel: the name of the CPU, of the technology, of the customer; may be null.</param>
        public PlayerTrigger(TriggerType type, int uniqueId, Segment segment, string detail)
        {
            Type = type;
            UniqueId = uniqueId;
            Segment = segment;
            Detail = detail;
        }

        public TriggerType Type;

        /// <summary>The <c>UniqueID</c> of the rival.</summary>
        public int UniqueId;

        /// <summary>The segment of the move; <see cref="Segment.Any"/> when there is none.</summary>
        public Segment Segment;

        /// <summary>For example the name of the CPU; may be null.</summary>
        public string Detail;
    }
}
