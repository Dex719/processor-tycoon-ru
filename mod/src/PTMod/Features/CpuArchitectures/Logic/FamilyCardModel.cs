using System;
using System.Collections.Generic;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>The status line of a family card (FR-3 item 3).</summary>
    public enum CardStatus
    {
        /// <summary>Researched (also a family researched at once, like the default one).</summary>
        Researched = 0,

        /// <summary>Can be started: its requirements are researched.</summary>
        Available = 1,

        /// <summary>The current research of the lab, with a progress.</summary>
        InProgress = 2,

        /// <summary>The current research, on pause; the reason is in <see cref="FamilyCardInfo.PauseReason"/>.</summary>
        Paused = 3,

        /// <summary>A required family is not researched; <see cref="FamilyCardInfo.Missing"/> names them.</summary>
        Locked = 4,
    }

    /// <summary>What the button of a card does.</summary>
    public enum CardAction
    {
        /// <summary>"Исследовать" (disabled for a researched, a locked and the running family).</summary>
        Research = 0,

        /// <summary>"Продолжить" for a paused research.</summary>
        Resume = 1,
    }

    /// <summary>What the panel shows about one family: the status, the button and the numbers behind them. Plain data made by <see cref="FamilyCardModel"/>.</summary>
    public sealed class FamilyCardInfo
    {
        public string FamilyId;
        public CardStatus Status;
        public CardAction Action;

        /// <summary>The button can be pressed.</summary>
        public bool ActionEnabled;

        /// <summary>
        /// The button would work but a technology of the player is in the lab and the feature cannot interrupt it (the part <c>LabUi</c> is off): the button is
        /// disabled and the tooltip says "Остановите текущее исследование" (<see cref="LabRules.StartGate"/>).
        /// </summary>
        public bool BlockedByTechnology;

        /// <summary>The share of the research done, 0..100 (rounded down; 100 only for a researched family).</summary>
        public int ProgressPercent;

        /// <summary>Why a <see cref="CardStatus.Paused"/> research stands.</summary>
        public PauseReason PauseReason;

        /// <summary>The ids of the required families that are not researched yet (catalog order); empty unless <see cref="CardStatus.Locked"/>.</summary>
        public readonly List<string> Missing = new List<string>();

        /// <summary>The line "time and cost of the research" is meaningful: the family is not researched.</summary>
        public bool ShowsResearchNumbers
        {
            get { return Status != CardStatus.Researched; }
        }
    }

    /// <summary>
    /// The state of the cards of the panel "Архитектуры" (FR-3 items 3 and 7; design.md Component 13), pure: <c>FamilyCard</c> draws what this says. The
    /// rules: a family is researched when the company has it (or it comes at once, <c>baseDays</c> 0, like <c>proprietary</c>); it is the current research
    /// (running or paused) when the lab holds it; otherwise it is locked while a requirement is missing and available when none is. The button is
    /// "Исследовать" (enabled only for an available family, and only while the research part works), "Продолжить" for a paused research.
    /// </summary>
    public static class FamilyCardModel
    {
        /// <param name="family">The family of the card.</param>
        /// <param name="state">The state of the player's company; null before the seed (the families researched at once only).</param>
        /// <param name="researchUsable">The feature is on and the research part passed the self-check: the buttons may work.</param>
        /// <param name="technologyBlocks">A technology of the player is in the lab and cannot be interrupted (<see cref="LabRules.TechnologyBlocksStart"/>): "Исследовать" and "Продолжить" are disabled.</param>
        public static FamilyCardInfo Of(ArchFamily family, CompanyArchState state, bool researchUsable, bool technologyBlocks = false)
        {
            if (family == null) throw new ArgumentNullException("family");

            IReadOnlyDictionary<string, FamilyState> families = state == null ? null : state.Families;
            var info = new FamilyCardInfo { FamilyId = family.Id, Action = CardAction.Research };

            FamilyState own = state == null ? null : state.GetFamily(family.Id);
            if (ArchResearchMath.CountsAsResearched(family, families))
            {
                info.Status = CardStatus.Researched;
                info.ProgressPercent = 100;
                return info;
            }

            info.ProgressPercent = Percent(own == null ? 0f : own.Progress);

            if (state != null && state.CurrentResearch == family.Id)
            {
                if (state.Paused)
                {
                    info.Status = CardStatus.Paused;
                    info.Action = CardAction.Resume;
                    info.PauseReason = state.PauseReason;
                    info.ActionEnabled = researchUsable && !technologyBlocks;
                    info.BlockedByTechnology = researchUsable && technologyBlocks;
                }
                else
                {
                    info.Status = CardStatus.InProgress;   // already running: nothing to press
                }
                return info;
            }

            if (!ArchResearchMath.RequirementsMet(family, families))
            {
                info.Status = CardStatus.Locked;
                info.Missing.AddRange(ArchResearchMath.MissingRequirements(family, families));
                return info;
            }

            info.Status = CardStatus.Available;
            info.ActionEnabled = researchUsable && !technologyBlocks;
            info.BlockedByTechnology = researchUsable && technologyBlocks;
            return info;
        }

        /// <summary>The cards in the order of the catalog.</summary>
        public static List<FamilyCardInfo> All(ArchCatalog catalog, CompanyArchState state, bool researchUsable, bool technologyBlocks = false)
        {
            var cards = new List<FamilyCardInfo>();
            if (catalog == null) return cards;
            foreach (ArchFamily family in catalog.Families) cards.Add(Of(family, state, researchUsable, technologyBlocks));
            return cards;
        }

        /// <summary>
        /// The families that count as "new and available" for the counter on the tab (FR-3 item 7): not researched, requirements researched, the year of
        /// the family has come (<paramref name="year"/>) and it is not researched at once. A family of a future year can be researched too (at a time
        /// penalty) but it is not news yet.
        /// </summary>
        public static List<string> Newcomers(ArchCatalog catalog, CompanyArchState state, int year)
        {
            var ids = new List<string>();
            if (catalog == null) return ids;

            IReadOnlyDictionary<string, FamilyState> families = state == null ? null : state.Families;
            foreach (ArchFamily family in catalog.Families)
            {
                if (family.Year > year || ArchResearchMath.IsInstant(family.Research)) continue;
                if (ArchResearchMath.IsAvailable(family, families)) ids.Add(family.Id);
            }
            return ids;
        }

        /// <summary>The share of a research as a whole percent, rounded down; an unusable number is 0.</summary>
        public static int Percent(float progress)
        {
            if (float.IsNaN(progress) || float.IsInfinity(progress) || progress <= 0f) return 0;
            if (progress >= 1f) return 100;
            return (int)Math.Floor(progress * 100f);
        }
    }

    /// <summary>
    /// The counter on the tab "Архитектуры" (FR-3 item 7): how many families became available and have not been looked at. The first set it sees is the
    /// baseline (what was available when the game started or the save loaded is not news); a family that appears after that counts until the tab has
    /// been shown. In memory only: a new game or a load starts over.
    /// </summary>
    public sealed class NewFamilyTracker
    {
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _last = new List<string>();
        private bool _baselined;
        private int _count;

        /// <summary>The families that are new and not looked at yet.</summary>
        public int Count
        {
            get { return _count; }
        }

        /// <summary>
        /// Takes the families available now (<see cref="FamilyCardModel.Newcomers"/>). <paramref name="viewing"/> is true while the tab is on the screen:
        /// everything shown is looked at. Returns <see cref="Count"/>.
        /// </summary>
        public int Update(IEnumerable<string> available, bool viewing)
        {
            _last.Clear();
            if (available != null) _last.AddRange(available);

            if (!_baselined)
            {
                _baselined = true;
                _seen.UnionWith(_last);
            }
            else if (viewing)
            {
                _seen.UnionWith(_last);
            }

            int unseen = 0;
            foreach (string id in _last)
            {
                if (!_seen.Contains(id)) unseen++;
            }
            _count = unseen;
            return _count;
        }

        /// <summary>The tab was shown: whatever is available now is looked at.</summary>
        public void MarkSeen()
        {
            _seen.UnionWith(_last);
            _count = 0;
        }

        /// <summary>A new game, a load or a new scene: the next set is the baseline again.</summary>
        public void Reset()
        {
            _seen.Clear();
            _last.Clear();
            _baselined = false;
            _count = 0;
        }
    }
}
