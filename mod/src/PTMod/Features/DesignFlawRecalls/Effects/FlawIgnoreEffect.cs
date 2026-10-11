namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>The "Ignore" button: nothing is paid now; a public flaw costs reputation, clients and perhaps a lawsuit.</summary>
    internal sealed class FlawIgnoreEffect : FlawEffectBase
    {
        public FlawIgnoreEffect()
            : base(FlawResponse.Ignore)
        {
        }
    }
}
