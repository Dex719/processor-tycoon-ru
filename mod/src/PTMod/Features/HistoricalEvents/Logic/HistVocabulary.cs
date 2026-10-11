using System;
using System.Collections.Generic;

namespace PTMod.Features.HistoricalEvents
{
    /// <summary>
    /// Where an entry of the calendar stands. The numbers are written to the save (<c>EntryStateDto.status</c>), so they must not change; a number
    /// outside this set is read as <see cref="Pending"/>.
    /// </summary>
    public enum EntryStatus
    {
        /// <summary>The anchor date has not come (or the entry is waiting for the first check).</summary>
        Pending = 0,

        /// <summary>The anchor passed but the roles or conditions did not fit; checked again on the 1st of every month until the end of the window.</summary>
        Waiting = 1,

        /// <summary>The first stage was fired.</summary>
        Fired = 2,

        /// <summary>The window ended without a firing; the fallback (<c>news</c> or <c>skip</c>) was carried out.</summary>
        Expired = 3,

        /// <summary>The anchor was in the past when the game (or the mod) started: not fired, not caught up.</summary>
        SkippedBeforeStart = 4,

        /// <summary>The category of the entry was switched off when its date came.</summary>
        Disabled = 5,

        /// <summary>The entry threw while it was checked or fired; it is not run again.</summary>
        Error = 6,
    }

    /// <summary>The twelve categories of the calendar (FR-17 item 1): each has a switch in the settings.</summary>
    public static class HistCategories
    {
        public const string Macro = "macro";
        public const string Boom = "boom";
        public const string Shock = "shock";
        public const string Disaster = "disaster";
        public const string Antitrust = "antitrust";
        public const string Export = "export";
        public const string PriceWar = "priceWar";
        public const string Patent = "patent";
        public const string Flaw = "flaw";
        public const string Race = "race";
        public const string Brand = "brand";
        public const string Oem = "oem";

        private static readonly string[] Names =
        {
            Macro, Boom, Shock, Disaster, Antitrust, Export, PriceWar, Patent, Flaw, Race, Brand, Oem,
        };

        /// <summary>The categories in the order of the settings page. Shared array: do not modify.</summary>
        public static IReadOnlyList<string> All
        {
            get { return Names; }
        }

        /// <summary>The name is one of the twelve (case-sensitive, as the settings keys are).</summary>
        public static bool IsKnown(string category)
        {
            return category != null && Array.IndexOf(Names, category) >= 0;
        }

        /// <summary>The switch of the category when the player has not touched it: on for all but <see cref="Disaster"/> (owner decision: catastrophes and the pandemic are opt-in).</summary>
        public static bool DefaultEnabled(string category)
        {
            return !string.Equals(category, Disaster, StringComparison.Ordinal);
        }
    }

    /// <summary>The kinds of role an entry can ask for (design.md, Component 4) and the role names a fire may use without declaring them.</summary>
    public static class RoleKinds
    {
        public const string Leader = "leader";
        public const string Challenger = "challenger";
        public const string Named = "named";
        public const string FoundryLeader = "foundryLeader";
        public const string BestPerfPerWatt = "bestPerfPerWatt";
        public const string NewestEntrant = "newestEntrant";
        public const string RandomManufacturer = "randomManufacturer";
        public const string Player = "player";
        public const string Global = "global";
        public const string AllCpuCompanies = "allCpuCompanies";
        public const string AllManufacturers = "allManufacturers";
        public const string AllFabless = "allFabless";

        private static readonly string[] Kinds =
        {
            Leader, Challenger, Named, FoundryLeader, BestPerfPerWatt, NewestEntrant, RandomManufacturer,
            Player, Global, AllCpuCompanies, AllManufacturers, AllFabless,
        };

        // The roles that need no declaration: a fire names them directly ("role": "global").
        private static readonly string[] Implicit = { Player, Global, AllCpuCompanies, AllManufacturers, AllFabless };

        public static IReadOnlyList<string> All
        {
            get { return Kinds; }
        }

        public static bool IsKnown(string kind)
        {
            return kind != null && Array.IndexOf(Kinds, kind) >= 0;
        }

        /// <summary>A fire may name this role without the entry declaring it: <c>player</c>, <c>global</c> and the three groups.</summary>
        public static bool IsImplicitRole(string name)
        {
            return name != null && Array.IndexOf(Implicit, name) >= 0;
        }

        /// <summary>The role stands for a group of companies (one firing per company), not for one.</summary>
        public static bool IsGroup(string kind)
        {
            return kind == AllCpuCompanies || kind == AllManufacturers || kind == AllFabless;
        }
    }

    /// <summary>What an entry does when its window ends without a firing (FR-2 item 4).</summary>
    public static class FallbackModes
    {
        /// <summary>A mail with the news and no effects.</summary>
        public const string News = "news";

        /// <summary>Nothing.</summary>
        public const string Skip = "skip";

        public static bool IsKnown(string mode)
        {
            return mode == News || mode == Skip;
        }
    }
}
