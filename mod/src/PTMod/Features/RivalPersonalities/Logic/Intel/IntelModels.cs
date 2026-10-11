using System;
using System.Collections.Generic;
using PTMod.Core;

namespace PTMod.Features.RivalPersonalities
{
    /// <summary>The colour family of a line of the panel: the window paints it with the colours of the theme of the game.</summary>
    public enum IntelTone
    {
        /// <summary>The text colour of the theme.</summary>
        Normal = 0,

        /// <summary>The positive colour of the theme (a friend, a rising attitude).</summary>
        Good = 1,

        /// <summary>The negative colour of the theme (an enemy, a falling attitude, a bankrupt company).</summary>
        Bad = 2,

        /// <summary>The warning colour of the theme (a threat, a caution).</summary>
        Warn = 3,

        /// <summary>A dimmed text (an unknown value, a company out of the game).</summary>
        Muted = 4,
    }

    /// <summary>
    /// What the builder of the panel needs from the language and the game's formatters (design.md, Component 11). The game's implementation reads
    /// <c>ModL10n</c> and <c>StringFormatter</c>; a test gives its own, so the texts of the panel can be checked without the game.
    /// </summary>
    public interface IIntelText
    {
        /// <summary>The text of a key in the current language with <c>{0}..{n}</c> filled in.</summary>
        string L(string key, params object[] args);

        /// <summary>A text of the content (the name and the description of an archetype) in the current language.</summary>
        string Resolve(LocText text);

        /// <summary>A game date the way the game writes dates.</summary>
        string Date(DateTime date);

        /// <summary>An <c>Ips</c> value the way the game writes it (without the mark of the fog).</summary>
        string Ips(float ips);
    }

    /// <summary>
    /// What the game says about one AI company at the moment the panel is built (FR-9 item 2). Plain data read by the game side
    /// (<c>UI/IntelSource</c>). There is no money and no project of the rival here on purpose (FR-9 item 6): the panel cannot show what it is not given.
    /// </summary>
    public sealed class IntelCompanyFacts
    {
        /// <summary>The <c>UniqueID</c> of the company.</summary>
        public int UniqueId;

        /// <summary>The name from the game.</summary>
        public string Name = string.Empty;

        public bool IsBankrupt;

        /// <summary>The segments the company has a CPU on sale in, one flag per segment (index = <c>(int)Segment</c>).</summary>
        public readonly bool[] Segments = new bool[4];

        /// <summary>The share of the market is known (<see cref="Share"/> is valid).</summary>
        public bool HasShare;

        /// <summary>The share of all the CPUs sold, 0..1 (<c>MarketDataProvider.MarketShare</c>).</summary>
        public float Share;

        /// <summary>The company has a released CPU (the next four fields are valid).</summary>
        public bool HasLastCpu;

        public string LastCpuName = string.Empty;
        public int LastCpuYear;
        public float LastCpuIps;

        /// <summary>The brand reputation is shown (only while <c>press-reviews</c> is on).</summary>
        public bool HasReputation;

        /// <summary>The brand reputation, 0..100 (<c>ModReputation</c>).</summary>
        public float Reputation;
    }

    /// <summary>The state of the game and of the settings the panel is built for.</summary>
    public struct IntelSettings
    {
        /// <summary>The game date.</summary>
        public DateTime Today;

        /// <summary>The parameter <c>hiddenCharacters</c>: a character is shown only after the first reaction of the rival or from an attitude of +20.</summary>
        public bool HiddenMode;

        /// <summary>The parameter <c>intelFog</c>: the share and the <c>Ips</c> are rounded and marked with "≈".</summary>
        public bool Fog;
    }

    /// <summary>One company in the list on the left of the panel (FR-9 item 2).</summary>
    public sealed class IntelRow
    {
        public int UniqueId;
        public string Name = string.Empty;
        public bool IsBankrupt;

        /// <summary>The rival is put aside (<c>SetDormant</c>): the feature does not act against the player through it.</summary>
        public bool IsDormant;

        /// <summary>The player may see the character (<see cref="CharacterVisibility"/>): the mode is off, or the rival has reacted, or it likes the player.</summary>
        public bool CharacterVisible;

        /// <summary>The id of the archetype; empty when the character is not visible (the panel does not leak it) or the rival has none.</summary>
        public string ArchetypeId = string.Empty;

        /// <summary>The name of the archetype, or "Character unknown" while it is hidden.</summary>
        public string CharacterText = string.Empty;

        /// <summary>The description of the archetype; empty while it is hidden.</summary>
        public string Description = string.Empty;

        /// <summary>The attitude to the player, -100..+100 (exact whatever the fog says).</summary>
        public float Attitude;

        /// <summary>The attitude as the panel writes it: <c>+12</c>, <c>-15</c>, <c>0</c>.</summary>
        public string AttitudeText = string.Empty;

        public AttitudeLevel Level;
        public string LevelText = string.Empty;
        public IntelTone AttitudeTone;

        /// <summary>The segments served, as names ("High End, Mid Range"), or a dash.</summary>
        public string SegmentsText = string.Empty;

        /// <summary>The share of the market: "≈ 15 %" with the fog, "17 %" without, or a dash.</summary>
        public string ShareText = string.Empty;

        /// <summary>The last CPU: name, year, <c>Ips</c> (rounded with the fog), or a dash.</summary>
        public string LastCpuText = string.Empty;

        /// <summary>The second line of the row: character, segments, share, last CPU.</summary>
        public string SummaryText = string.Empty;

        /// <summary>The tone of the whole row: a company that is out is muted.</summary>
        public IntelTone Tone;
    }

    /// <summary>
    /// One line of the right side of the panel. With a <see cref="Label"/> the window draws it as a pair "label: value" (<c>ModUI.Info</c>), without one
    /// as a plain text. <see cref="Tooltip"/> is the text of a hover tip.
    /// </summary>
    public sealed class IntelLine
    {
        public IntelLine(string label, string text, IntelTone tone = IntelTone.Normal, bool bold = false, string tooltip = null)
        {
            Label = label;
            Text = text ?? string.Empty;
            Tone = tone;
            Bold = bold;
            Tooltip = tooltip;
        }

        /// <summary>The caption of a pair, or null for a plain text.</summary>
        public string Label { get; private set; }

        public string Text { get; private set; }
        public IntelTone Tone { get; private set; }
        public bool Bold { get; private set; }

        /// <summary>The hover tip, or null.</summary>
        public string Tooltip { get; private set; }
    }

    /// <summary>A block of lines under a heading (<see cref="Title"/> is null for the first block, which has no heading).</summary>
    public sealed class IntelSection
    {
        public IntelSection(string id, string title)
        {
            Id = id;
            Title = title;
        }

        /// <summary>A stable name for the tests and the log: <c>header</c>, <c>attitude</c>, <c>market</c>, <c>threats</c>, <c>active</c>, <c>history</c>.</summary>
        public string Id { get; private set; }

        public string Title { get; private set; }
        public List<IntelLine> Lines { get; } = new List<IntelLine>();

        public IntelSection Add(IntelLine line)
        {
            if (line != null) Lines.Add(line);
            return this;
        }
    }

    /// <summary>The right side of the panel for one company (FR-9 item 3).</summary>
    public sealed class IntelDetails
    {
        public IntelDetails(IntelRow row)
        {
            Row = row;
        }

        public IntelRow Row { get; private set; }
        public List<IntelSection> Sections { get; } = new List<IntelSection>();

        /// <summary>The section with this id, or null.</summary>
        public IntelSection Find(string id)
        {
            foreach (IntelSection section in Sections)
            {
                if (string.Equals(section.Id, id, StringComparison.Ordinal)) return section;
            }
            return null;
        }
    }
}
