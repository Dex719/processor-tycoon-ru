namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The shape of the identifiers the feature reads from content and saves (design.md, "Security"): a family id is <c>[a-z0-9_]{1,32}</c>
    /// (<c>x86_64</c>), an ISA group <c>[a-z0-9_]{1,16}</c>, the slug of an AI preference record <c>[a-z0-9_-]{1,32}</c> (<c>texas-instruments</c>).
    /// Anything else is refused before it reaches a dictionary, a log line or a save.
    /// </summary>
    internal static class ArchIds
    {
        public const int MaxFamilyIdLength = 32;
        public const int MaxIsaIdLength = 16;

        public static bool IsFamilyId(string id)
        {
            return Matches(id, MaxFamilyIdLength, false);
        }

        public static bool IsIsaId(string id)
        {
            return Matches(id, MaxIsaIdLength, false);
        }

        /// <summary>The slug of a record of <c>ai-preferences.json</c>: like a family id, hyphens allowed.</summary>
        public static bool IsRecordSlug(string id)
        {
            return Matches(id, MaxFamilyIdLength, true);
        }

        private static bool Matches(string id, int maxLength, bool allowHyphen)
        {
            if (string.IsNullOrEmpty(id) || id.Length > maxLength) return false;
            foreach (char ch in id)
            {
                bool ok = (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || (allowHyphen && ch == '-');
                if (!ok) return false;
            }
            return true;
        }
    }
}
