namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The "Patch" button: pays the patch (<c>ExpenseOrigin.Development</c>) and lowers the rating of the CPUs for its term.</summary>
    internal sealed class FlawSoftwarePatchEffect : FlawEffectBase
    {
        public FlawSoftwarePatchEffect()
            : base(FlawResponse.Patch)
        {
        }
    }
}
