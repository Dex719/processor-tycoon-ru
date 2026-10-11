namespace PTMod.Features.DesignFlawRecalls
{
    /// <summary>
    /// The public face of the feature class (design.md, Component 1: <c>public static FlawRegistry Registry</c>). This file is only the
    /// API part: it is <c>partial</c> so that the other features, which are built with the <c>Api</c> folder alone, can write
    /// <c>DesignFlawRecallsFeature.Registry.ForceFlaw(...)</c> as the specs say. The implementation of the feature declares the other
    /// part as <c>[ModFeature] public sealed partial class DesignFlawRecallsFeature : IModFeature</c> and must NOT declare
    /// <c>Registry</c> again.
    /// </summary>
    public sealed partial class DesignFlawRecallsFeature
    {
        /// <summary>
        /// The public API entry for other features (FR-13, FR-17). Never <c>null</c>; a null object (<c>false</c>, empty lists, <c>null</c>)
        /// while the feature is not enabled. Same object as <see cref="DesignFlawRecallsApi.Registry"/>.
        /// </summary>
        public static FlawRegistry Registry
        {
            get { return DesignFlawRecallsApi.Registry; }
        }
    }
}
