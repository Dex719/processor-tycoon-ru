using System;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// How a family is named where the game shows an architecture (FR-5 items 4-5): the field "Architecture" of the inspector and the compare
    /// rows. A family of the catalog is shown by its name; a family the catalog does not know any more (removed from the content, a CPU of an old
    /// save) is shown as "Неизвестная (id)", so the id is not lost. Pure: the texts come from the caller's localizer.
    /// </summary>
    public static class ArchDisplay
    {
        /// <summary><c>ModL10n</c> key of "Неизвестная ({0})".</summary>
        public const string UnknownKey = "arch.inspector.unknown";

        /// <summary>The shown name when a CPU has no family at all (the dash of an empty compare column).</summary>
        public const string Empty = "—";

        /// <summary>
        /// The name to show for a family id. <paramref name="localize"/> is <c>ModL10n.L</c> (key and arguments to text). A known family gives the
        /// text of its name key, an unknown id the text of <see cref="UnknownKey"/> with the id; a null or blank id gives <see cref="Empty"/>.
        /// </summary>
        public static string FamilyName(ArchCatalog catalog, string familyId, Func<string, object[], string> localize)
        {
            if (localize == null) throw new ArgumentNullException("localize");
            if (string.IsNullOrEmpty(familyId)) return Empty;

            ArchFamily family = catalog == null ? null : catalog.Get(familyId);
            if (family != null) return localize(family.NameKey, new object[0]);
            return localize(UnknownKey, new object[] { familyId });
        }
    }
}
