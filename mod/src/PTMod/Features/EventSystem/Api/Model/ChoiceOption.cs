namespace PTMod.Features.EventSystem
{
    /// <summary>One choice of an instance as an AI chooser sees it (<c>IEventAiChooser.Choose</c>).</summary>
    public sealed class ChoiceOption
    {
        public string ChoiceId;

        /// <summary>False when the <c>requires</c> of the choice is false for the company; an unavailable choice is never picked.</summary>
        public bool Available;

        /// <summary>The up-front spending of the choice in dollars, computed by the engine from its spending effects (a larger number is a dearer choice; 0 - a free one).</summary>
        public float Cost;

        /// <summary><c>aiWeight</c> of the choice from the definition.</summary>
        public float AiWeight;
    }
}
