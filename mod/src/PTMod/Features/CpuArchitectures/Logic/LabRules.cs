using System;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>What occupies the shared lab of a company (D2): nothing, a technology of the game, an architecture, or an architecture on pause.</summary>
    public enum LabStatus
    {
        /// <summary>No technology and no architecture: the lab stands idle.</summary>
        Free = 0,

        /// <summary>A technology of the game is researched (<c>ResearchSector.CurrentResearch()</c> is set).</summary>
        Technology = 1,

        /// <summary>An architecture is researched; the game's slot is empty.</summary>
        Architecture = 2,

        /// <summary>An architecture is the current research of the company but stands on pause; the game's slot is empty.</summary>
        ArchitecturePaused = 3,
    }

    /// <summary>How the arbiter of the lab settles a lab that holds a technology while an architecture is active (FR-2 item 5, FR-7 item 1).</summary>
    public enum ConflictResolution
    {
        /// <summary>No conflict.</summary>
        None = 0,

        /// <summary>The player chose the technology last: the architecture pauses with its progress (<c>PauseReason.TechnologyChosen</c>).</summary>
        PauseArchitecture = 1,

        /// <summary>An AI company: the architecture keeps the lab, the technology is stopped (its progress stays with the game).</summary>
        StopTechnology = 2,
    }

    /// <summary>How a start (or a resume) of an architecture meets the lab (FR-2 item 1; design.md, "Ошибки": the part <c>LabUi</c>).</summary>
    public enum StartGate
    {
        /// <summary>Nothing stands in the way: the lab is free, or the company is an AI company, or the player has confirmed the interruption.</summary>
        Open = 0,

        /// <summary>A technology is in the player's lab: ask him first (<c>ModUI.Confirm</c>) and ask again with the confirmation.</summary>
        NeedsConfirmation = 1,

        /// <summary>
        /// A technology is in the player's lab and the feature cannot interrupt it safely (the panel of the current research was not found in this version of
        /// the game): the player stops the technology himself. The button is disabled with the hint "Остановите текущее исследование".
        /// </summary>
        StopTechnologyFirst = 2,
    }

    /// <summary>
    /// The rules of the shared lab (D2; FR-2, FR-3 item 5, FR-7): who must confirm the interruption of a technology, when the AI may interrupt one,
    /// how a conflict is settled, the funding of the AI and the limit of the player's slider. Pure: the arbiter and the services
    /// (<c>LabArbiter</c>, <c>ArchResearchService</c>, the AI) ask, the game is touched elsewhere.
    /// </summary>
    public static class LabRules
    {
        /// <summary>The highest funding of the player's slider: 100 %.</summary>
        public const float NormalFundingLimit = 1f;

        /// <summary>The highest funding with <c>ResearchSector.InnovationEffort</c>: 500 %.</summary>
        public const float InnovationFundingLimit = 5f;

        /// <summary>The lowest funding of the slider: 1 % (the game's <c>fundingSlider.Setup(1, 1, 100)</c>).</summary>
        public const float MinFunding = 0.01f;

        /// <summary>The funding of an AI company when the table of <c>balance.json</c> is unusable.</summary>
        public const float DefaultAiFunding = 1f;

        /// <summary>
        /// The state of the lab. An active architecture with an empty slot is <see cref="LabStatus.Architecture"/>; a technology in the slot is
        /// <see cref="LabStatus.Technology"/> (also during a conflict, until the arbiter settles it); a paused architecture with an empty slot is
        /// <see cref="LabStatus.ArchitecturePaused"/>; nothing is <see cref="LabStatus.Free"/>.
        /// </summary>
        /// <param name="hasTechnology"><c>ResearchSector.CurrentResearch()</c> is not null.</param>
        /// <param name="hasArchitecture">The company has a current architecture research (<c>CompanyArchState.CurrentResearch</c>).</param>
        /// <param name="paused">That research is paused.</param>
        public static LabStatus Status(bool hasTechnology, bool hasArchitecture, bool paused)
        {
            if (hasTechnology) return LabStatus.Technology;
            if (hasArchitecture) return paused ? LabStatus.ArchitecturePaused : LabStatus.Architecture;
            return LabStatus.Free;
        }

        /// <summary>The lab is held by an active architecture: the AI must not choose a technology (D2), the panel shows "busy with an architecture".</summary>
        public static bool IsOccupiedByArchitecture(bool hasArchitecture, bool paused)
        {
            return hasArchitecture && !paused;
        }

        /// <summary>FR-2 item 1: only the player confirms, and only when a technology is in the lab (an AI interrupts without asking, a free lab needs no question).</summary>
        public static bool NeedsConfirmation(bool isPlayer, bool hasTechnology)
        {
            return isPlayer && hasTechnology;
        }

        /// <summary>
        /// FR-7 item 1: an AI company interrupts a technology only when more than <paramref name="noInterruptDays"/> are left of it
        /// (<c>ResearchSector.TimeLeftOf</c>); a free lab (a negative number of days) can always be taken.
        /// </summary>
        public static bool CanInterruptForAi(int technologyDaysLeft, int noInterruptDays)
        {
            if (technologyDaysLeft < 0) return true;
            return technologyDaysLeft > Math.Max(0, noInterruptDays);
        }

        /// <summary>
        /// The arbiter on <c>TickPhase.EarlyTick</c> (FR-2 item 5): an active architecture and a technology in one lab. The player chose the technology
        /// after the architecture (the last choice wins): the architecture pauses. An AI company: the technology is stopped.
        /// </summary>
        public static ConflictResolution ResolveConflict(bool isPlayer, bool architectureActive, bool hasTechnology)
        {
            if (!architectureActive || !hasTechnology) return ConflictResolution.None;
            return isPlayer ? ConflictResolution.PauseArchitecture : ConflictResolution.StopTechnology;
        }

        /// <summary>
        /// What happens when the research of an architecture is asked to take the lab (FR-2 item 1): an empty lab and an AI company need no question; the
        /// player's technology needs his confirmation; and when the feature cannot interrupt the technology (<paramref name="labUiWorks"/> is false: the
        /// panel of the current research would keep showing it) the player has to stop it himself, whatever he confirms.
        /// </summary>
        /// <param name="isPlayer">The company is the player's.</param>
        /// <param name="hasTechnology"><c>ResearchSector.CurrentResearch()</c> is not null.</param>
        /// <param name="labUiWorks">The part <c>LabUi</c> of the self-check passed (the private members of <c>CurrentResearchUI</c> are there).</param>
        /// <param name="confirmed">The player has already answered "yes" to the question.</param>
        public static StartGate Gate(bool isPlayer, bool hasTechnology, bool labUiWorks, bool confirmed)
        {
            if (!hasTechnology || !isPlayer) return StartGate.Open;
            if (!labUiWorks) return StartGate.StopTechnologyFirst;
            return confirmed ? StartGate.Open : StartGate.NeedsConfirmation;
        }

        /// <summary>The buttons "Исследовать" and "Продолжить" of the player are disabled with the hint "Остановите текущее исследование" (<see cref="StartGate.StopTechnologyFirst"/>).</summary>
        public static bool TechnologyBlocksStart(bool isPlayer, bool hasTechnology, bool labUiWorks)
        {
            return Gate(isPlayer, hasTechnology, labUiWorks, true) == StartGate.StopTechnologyFirst;
        }

        /// <summary>
        /// The state of the lab as the panel shows it: like <see cref="Status"/>, except that a paused architecture is shown as paused even while a
        /// technology is in the slot (that technology is the reason: "лаборатория занята технологией"), so that the block keeps its progress and its
        /// button "Продолжить".
        /// </summary>
        public static LabStatus StatusForPanel(bool hasTechnology, bool hasArchitecture, bool paused)
        {
            if (hasArchitecture && paused) return LabStatus.ArchitecturePaused;
            return Status(hasTechnology, hasArchitecture, paused);
        }

        /// <summary>
        /// A pause that lifts by itself when its cause is over (the company left the default, FR-2 item 6). Every other pause waits for the button
        /// "Продолжить" - also <see cref="PauseReason.FeatureDisabled"/>: a feature switched on again does not restart the research (FR-9 item 3).
        /// </summary>
        public static bool AutoResumes(PauseReason reason)
        {
            return reason == PauseReason.Default;
        }

        /// <summary>
        /// The arbiter on <c>TickPhase.EarlyTick</c> for one company (FR-2 item 5, AC-2.4): applies <see cref="ResolveConflict"/> to the state. The
        /// player's architecture is paused with <see cref="PauseReason.TechnologyChosen"/> (progress and <c>CurrentResearch</c> stay); for an AI company the
        /// state does not change and the caller stops the technology (<see cref="ConflictResolution.StopTechnology"/>). Only a running architecture can
        /// conflict.
        /// </summary>
        public static ConflictResolution Arbitrate(CompanyArchState state, bool slotHasTechnology)
        {
            if (state == null) return ConflictResolution.None;

            ConflictResolution result = ResolveConflict(state.IsPlayer, state.CurrentResearch != null && !state.Paused, slotHasTechnology);
            if (result == ConflictResolution.PauseArchitecture)
            {
                state.Paused = true;
                state.PauseReason = PauseReason.TechnologyChosen;
            }
            return result;
        }

        /// <summary>
        /// The player's technology the architecture displaces from the lab (FR-2 item 1) is remembered so that the lab can go back to it (FR-2 item 8).
        /// An AI company remembers nothing: the game chooses its next technology itself. A negative id (no technology) changes nothing - a technology
        /// remembered earlier survives a switch from one family to another.
        /// </summary>
        public static void Remember(CompanyArchState state, int technologyId)
        {
            if (state == null || !state.IsPlayer || technologyId < 0) return;
            state.ResumeTechnologyId = technologyId;
        }

        /// <summary>
        /// The lab is freed (FR-2 items 4, 7 and 8): the remembered technology is forgotten and, when it can go back, its id is returned. It goes back
        /// only to the player and only into an empty slot (<paramref name="slotEmpty"/>): a technology he chose himself in the meantime stays. -1 for none.
        /// Whether the technology is still researchable (not researched, not locked) is the game's to say, the caller asks.
        /// </summary>
        public static int TakeReturnTarget(CompanyArchState state, bool slotEmpty)
        {
            if (state == null) return -1;

            int technologyId = state.ResumeTechnologyId;
            state.ResumeTechnologyId = -1;
            if (!state.IsPlayer || !slotEmpty) return -1;
            return technologyId < 0 ? -1 : technologyId;
        }

        /// <summary>
        /// The funding of an AI company that starts an architecture (FR-7 item 2): the entry of <paramref name="table"/> for
        /// <c>Player.DifficultyLevel</c> (0.6 / 0.8 / 1.0 / 1.3 / 1.6 by default); a level out of the table takes the nearest entry, an unusable
        /// table or entry gives <see cref="DefaultAiFunding"/>. The result is never above <see cref="InnovationFundingLimit"/>.
        /// </summary>
        public static float AiFunding(int difficulty, float[] table)
        {
            if (table == null || table.Length == 0) return DefaultAiFunding;
            int index = difficulty < 0 ? 0 : (difficulty >= table.Length ? table.Length - 1 : difficulty);
            float funding = table[index];
            if (float.IsNaN(funding) || float.IsInfinity(funding) || funding <= 0f) return DefaultAiFunding;
            return Math.Min(funding, InnovationFundingLimit);
        }

        /// <summary>The upper end of the player's slider (FR-3 item 5): 1.0 (100 %), or 5.0 (500 %) with <c>ResearchSector.InnovationEffort</c>.</summary>
        public static float PlayerFundingLimit(bool innovationEffort)
        {
            return innovationEffort ? InnovationFundingLimit : NormalFundingLimit;
        }

        /// <summary>A funding the slider may write: within [<see cref="MinFunding"/>, <see cref="PlayerFundingLimit"/>]; NaN gives the limit.</summary>
        public static float ClampPlayerFunding(float funding, bool innovationEffort)
        {
            float limit = PlayerFundingLimit(innovationEffort);
            if (float.IsNaN(funding)) return limit;
            if (funding < MinFunding) return MinFunding;
            return funding > limit ? limit : funding;
        }
    }
}
