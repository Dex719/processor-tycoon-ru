using System;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// One dependency line of the tree matched to its pair of technologies (design: Component 2): <see cref="LineIndex"/> is the index in the
    /// game's <c>ResearchTreeWindow.dependencyLines</c>, <see cref="FromTechId"/> the technology that is required (the dependency),
    /// <see cref="ToTechId"/> the technology that requires it. A line is shown only while both ends are shown (<see cref="CategoryFilter.IsLineVisible"/>).
    /// </summary>
    public readonly struct LineLink : IEquatable<LineLink>
    {
        public readonly int LineIndex;
        public readonly int FromTechId;
        public readonly int ToTechId;

        public LineLink(int lineIndex, int fromTechId, int toTechId)
        {
            LineIndex = lineIndex;
            FromTechId = fromTechId;
            ToTechId = toTechId;
        }

        public bool Equals(LineLink other)
        {
            return LineIndex == other.LineIndex && FromTechId == other.FromTechId && ToTechId == other.ToTechId;
        }

        public override bool Equals(object obj)
        {
            return obj is LineLink && Equals((LineLink)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = LineIndex;
                hash = hash * 31 + FromTechId;
                hash = hash * 31 + ToTechId;
                return hash;
            }
        }

        public override string ToString()
        {
            return "line " + LineIndex + ": " + FromTechId + " -> " + ToTechId;
        }
    }
}
