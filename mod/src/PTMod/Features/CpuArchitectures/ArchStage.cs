namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The stage the feature is released in (<c>balance.json</c>, "stage"): the formulas of stage 2 are in the code from the start, but they are off
    /// until the owner has checked the MVP (TSK-020). At <see cref="Mvp"/> support, ecosystem and extensions give ×1 and there is no royalty.
    /// </summary>
    public enum ArchStage
    {
        Mvp = 1,
        Full = 2,
    }
}
