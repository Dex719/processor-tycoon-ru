using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// A CPU extension that does not give its full effect yet (FR-13 item 2): the CPU carries it, the owner's support of the family is below what the extension
    /// needs, and the rating pays for it (<see cref="Factor"/> is under 1).
    /// </summary>
    public sealed class ExtensionWarning
    {
        /// <summary><c>ExtensionDef.id</c>: <c>multicore</c>, <c>smt</c>.</summary>
        public string Id;

        /// <summary>The support the extension needs for its full effect (<c>requiredSupport</c>).</summary>
        public float Required;

        /// <summary>The support of the owner for the family now.</summary>
        public float Support;

        /// <summary>The multiplier the gate gives the rating now (<see cref="ArchRatingMath.ExtensionFactor"/>), under 1.</summary>
        public float Factor;
    }

    /// <summary>
    /// The words about the gates of the extensions (FR-13 items 2-3, stage 2): which of them hold a CPU back in the designer, and which extensions the tooltip of
    /// a technology of the research tree has to tell about. The numbers are the ones <see cref="ArchRatingMath"/> uses for the rating, so a warning and the
    /// multiplier it explains cannot disagree. Pure.
    /// </summary>
    public static class ExtensionWarnings
    {
        /// <summary>
        /// The extensions the CPU carries whose gate is not open: <c>coreCount</c> cores or SMT of the design, with the support of the owner below the required
        /// level (AC-13.1: four cores at a support of 20 are held to 0.96). Empty when every gate is open or the CPU carries nothing.
        /// </summary>
        public static List<ExtensionWarning> For(IReadOnlyList<ExtensionDef> extensions, float support, int coreCount, bool smt)
        {
            var warnings = new List<ExtensionWarning>();
            if (extensions == null) return warnings;

            float level = float.IsNaN(support) ? 0f : Math.Max(0f, Math.Min(SupportMath.MaxSupport, support));
            for (int i = 0; i < extensions.Count; i++)
            {
                ExtensionDef extension = extensions[i];
                if (!ArchRatingMath.Carries(extension, coreCount, smt)) continue;

                float factor = ArchRatingMath.ExtensionFactor(level, extension);
                if (!(factor < 1f) || !(extension.requiredSupport > 0f)) continue;

                warnings.Add(new ExtensionWarning { Id = extension.id, Required = extension.requiredSupport, Support = level, Factor = factor });
            }
            return warnings;
        }

        /// <summary>
        /// The extensions a technology of the kind "multicore" brings (FR-13 item 3): a technology of <paramref name="technologyCores"/> cores (2, 4, 6, 8) brings the
        /// core-count extensions that start at or below that number, the technology of SMT (no cores, 0) brings the SMT extension.
        /// </summary>
        public static List<ExtensionDef> ForTechnology(IReadOnlyList<ExtensionDef> extensions, int technologyCores)
        {
            var found = new List<ExtensionDef>();
            if (extensions == null) return found;

            for (int i = 0; i < extensions.Count; i++)
            {
                ExtensionDef extension = extensions[i];
                if (extension == null) continue;

                if (technologyCores <= 0)
                {
                    if (extension.trigger == ExtensionDef.TriggerSmt) found.Add(extension);
                }
                else if (extension.trigger == ExtensionDef.TriggerCoreCount && technologyCores >= Math.Max(1, extension.minCores))
                {
                    found.Add(extension);
                }
            }
            return found;
        }

        /// <summary>The <c>ModL10n</c> key of the name of an extension ("Многоядерность", "SMT"); null for an extension the feature has no name for (its id is shown then).</summary>
        public static string NameKey(string extensionId)
        {
            switch (extensionId)
            {
                case "multicore": return "arch.ext.multicore";
                case "smt": return "arch.ext.smt";
                default: return null;
            }
        }
    }
}
