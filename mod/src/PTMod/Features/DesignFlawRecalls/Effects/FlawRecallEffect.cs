namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The "Recall" button: pays the recall (<c>ExpenseOrigin.Fines</c>) and closes the flaw.</summary>
    internal sealed class FlawRecallEffect : FlawEffectBase
    {
        public FlawRecallEffect()
            : base(FlawResponse.Recall)
        {
        }
    }
}
