using System.Collections.Generic;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>
    /// The estimate of the possible participants of a contract (FR-11; <c>ContractInspector.BuildPotential</c>): the AI offers that may still come before the review date and the probability of the
    /// analysed CPU if all of them do. Plain data; the panel prints it (<see cref="PotentialLines"/>). Empty (<see cref="IsShown"/> is false) when the AI has no monthly move left before the review.
    /// </summary>
    public sealed class PotentialReport
    {
        /// <summary>The monthly moves of the AI left before the contract is drawn (<see cref="AiMoveCalendar.MovesBefore"/>); 0 hides the block.</summary>
        public int MovesLeft;

        /// <summary>The block is shown: at least one move of the AI is left.</summary>
        public bool IsShown
        {
            get { return MovesLeft > 0; }
        }

        /// <summary>The AI offers that would change the contract (the best qualifying CPU of a company that has no offer yet, or one faster than the company's offer); the largest IPS is not necessarily first.</summary>
        public List<OfferInput> NewBidders = new List<OfferInput>();

        /// <summary>The probability that the draw picks the analysed CPU now (<see cref="OddsReport.CandidateActual"/>).</summary>
        public double CurrentActual;

        /// <summary>The same with every one of <see cref="NewBidders"/> in; <see cref="CurrentActual"/> when there are none.</summary>
        public double PessimisticActual;

        /// <summary><c>Player.DifficultyLevel</c> is 5: the AI takes more kinds of contracts than the estimate assumes (E11).</summary>
        public bool HardDifficulty;
    }
}
