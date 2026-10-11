namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The "Revision" button: starts a new stepping paid by the day (<c>ExpenseOrigin.Development</c>).</summary>
    internal sealed class FlawSteppingEffect : FlawEffectBase
    {
        public FlawSteppingEffect()
            : base(FlawResponse.Stepping)
        {
        }
    }
}
