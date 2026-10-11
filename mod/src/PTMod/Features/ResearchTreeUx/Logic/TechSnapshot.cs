using System;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// A frozen, game-free picture of one technology (design: Component 4): what the filters, the list model, the tooltip builder and the
    /// "new" tracker work with. <c>Tree/TechSnapshotReader</c> fills it from <c>Technology</c> and <c>ResearchSector</c>; the logic never
    /// touches the game. Plain fields so an object initializer builds one in a test. <see cref="Type"/> is the value of the game's
    /// <c>TechnologyType</c> (see <see cref="TechCategory"/>).
    /// </summary>
    public sealed class TechSnapshot
    {
        public int Id;
        public string Name;
        public int Type;
        public int Year;

        /// <summary>Research time in days with no year bonus or penalty (<c>Technology.BaseTime</c>).</summary>
        public float BaseTime;

        /// <summary>Research time in days now (<c>Technology.Time</c>, already scaled by the year).</summary>
        public float Time;

        /// <summary>The monthly cost of the team that researches it (<c>Technology.Cost</c>).</summary>
        public float MonthlyCost;

        public bool IsResearched;
        public bool IsLocked;
        public bool IsCurrent;

        /// <summary>The game's grey-out for this company type (see <see cref="UnnecessaryRule"/>).</summary>
        public bool IsUnnecessary;

        /// <summary>Progress 0..1 of the research in play.</summary>
        public float Progress;

        /// <summary>Days left at the current funding; -1 stands for "never" (shown as the infinity sign).</summary>
        public int EtaDays;

        /// <summary>Ids of the technologies this one requires (<c>ResearchSector.GetDependencies</c>). Never null.</summary>
        public int[] DependencyIds = NoIds;

        /// <summary>Ids of the technologies this one opens, the inverse of <see cref="DependencyIds"/> (<see cref="UnlocksIndex"/>). Never null.</summary>
        public int[] UnlocksIds = NoIds;

        private static readonly int[] NoIds = new int[0];

        /// <summary>
        /// Not researched and not locked: the player can start it (FR-1 item 4 "available"). The technology in progress is available too;
        /// the "unnecessary" grey-out does not make a technology unavailable.
        /// </summary>
        public bool IsAvailable
        {
            get { return !IsResearched && !IsLocked; }
        }

        public override string ToString()
        {
            return "Tech#" + Id + " " + (Name ?? "?") + " type " + Type + ", " + Year + (IsResearched ? " researched" : IsLocked ? " locked" : " available");
        }
    }
}
