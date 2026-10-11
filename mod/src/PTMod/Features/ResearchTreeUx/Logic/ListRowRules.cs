namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>The state of a row of the research list (FR-2 item 2): the four statuses of the spec.</summary>
    public enum RowStatus
    {
        /// <summary>Not researched and not locked: the player can start it.</summary>
        Available = 0,

        /// <summary>The technology in progress.</summary>
        Current = 1,

        /// <summary>The requirements are not researched yet.</summary>
        Locked = 2,

        /// <summary>Done.</summary>
        Researched = 3
    }

    /// <summary>What the button "Research" of a row does (FR-2 items 5 and 6), the same as the button of the tree in the game.</summary>
    public enum ChooseAction
    {
        /// <summary>The button is not active: the technology is researched, locked or already in progress.</summary>
        None = 0,

        /// <summary>Start the research at once.</summary>
        Start = 1,

        /// <summary>The company does not need the technology: ask the player through the game's own warning first.</summary>
        StartAfterWarning = 2
    }

    /// <summary>
    /// The rules of a row that both the text and the button follow (FR-2 items 2, 5 and 6), without the game. The button of the tree is
    /// interactable unless the technology is researched, locked or the current one, and on a click opens the warning for an unnecessary
    /// technology and starts the research otherwise (<c>ResearchButton.UpdateState</c> and its click handler); the list does the same.
    /// </summary>
    public static class ListRowRules
    {
        /// <summary>
        /// The status of a technology. The order is the one of the tooltip: researched first, then the one in progress, then locked, then
        /// available. A null technology is locked.
        /// </summary>
        public static RowStatus StatusOf(TechSnapshot tech)
        {
            if (tech == null) return RowStatus.Locked;
            if (tech.IsResearched) return RowStatus.Researched;
            if (tech.IsCurrent) return RowStatus.Current;
            if (tech.IsLocked) return RowStatus.Locked;
            return RowStatus.Available;
        }

        /// <summary>What a click on "Research" does; <see cref="ChooseAction.None"/> where the button is not active.</summary>
        public static ChooseAction ActionOf(TechSnapshot tech)
        {
            if (StatusOf(tech) != RowStatus.Available) return ChooseAction.None;
            return tech.IsUnnecessary ? ChooseAction.StartAfterWarning : ChooseAction.Start;
        }

        /// <summary>The button "Research" is active for this technology.</summary>
        public static bool CanChoose(TechSnapshot tech)
        {
            return ActionOf(tech) != ChooseAction.None;
        }

        /// <summary>The row carries the mark "not needed": the company type does not need the technology and it is not done (the game's grey-out).</summary>
        public static bool IsMarkedUnnecessary(TechSnapshot tech)
        {
            return tech != null && tech.IsUnnecessary && !tech.IsResearched;
        }
    }
}
