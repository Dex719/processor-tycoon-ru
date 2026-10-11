using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PTMod.Features.ContractTransparency
{
    /// <summary>What the check found out about one member: its state and, when it is not as expected, a few words for the log (what was found instead, who holds the method).</summary>
    public struct MemberProbe
    {
        public MemberState State;
        public string Detail;

        public static MemberProbe Of(MemberState state, string detail)
        {
            return new MemberProbe { State = state, Detail = detail };
        }

        public static MemberProbe Found
        {
            get { return new MemberProbe { State = MemberState.Found }; }
        }
    }

    /// <summary>One thing the check did not like.</summary>
    public sealed class SelfCheckFinding
    {
        internal SelfCheckFinding(GameMember member, MemberProbe probe)
        {
            Member = member;
            State = probe.State;
            Detail = probe.Detail;
        }

        public GameMember Member { get; private set; }

        public MemberState State { get; private set; }

        /// <summary>A few words from the check (what was found instead, who holds the method); may be null.</summary>
        public string Detail { get; private set; }

        /// <summary>One English line for the log: the member, what is wrong with it and what stops because of that.</summary>
        public string Text
        {
            get
            {
                var sb = new StringBuilder();
                sb.Append(Member.Key);
                switch (State)
                {
                    case MemberState.Missing: sb.Append(" is not found in the game"); break;
                    case MemberState.WrongType: sb.Append(" is not as expected"); break;
                    case MemberState.Reserved: sb.Append(" is reserved by the core, a patch of it is refused"); break;
                    case MemberState.Patched: sb.Append(" is patched by this feature, but it may only be called (it is reserved by the core)"); break;
                    default: sb.Append(" is fine"); break;
                }
                if (!string.IsNullOrEmpty(Detail)) sb.Append(" (").Append(Detail).Append(')');
                sb.Append(Consequence);
                return sb.ToString();
            }
        }

        private string Consequence
        {
            get
            {
                if (Member.Advisory) return "; nothing is switched off, the layout of the window may have changed";
                if (Member.IsPatchTarget && Member.RequiredPatch) return "; the core makes the feature Unavailable";
                if (Member.MustNotBePatched && State == MemberState.Patched) return string.Empty;
                var sb = new StringBuilder("; off:");
                for (int i = 0; i < Member.Parts.Length; i++)
                {
                    sb.Append(i == 0 ? " " : ", ").Append(MemberCatalog.Describe(Member.Parts[i]));
                }
                return sb.ToString();
            }
        }
    }

    /// <summary>The outcome of the check at the start: what was looked at, what is wrong and which parts of the feature that turns off.</summary>
    public sealed class SelfCheckResult
    {
        private readonly List<SelfCheckFinding> _findings = new List<SelfCheckFinding>();
        private readonly HashSet<FeaturePart> _off = new HashSet<FeaturePart>();

        internal SelfCheckResult(int checkedCount)
        {
            Checked = checkedCount;
        }

        /// <summary>How many members were looked at.</summary>
        public int Checked { get; private set; }

        /// <summary>The findings, in the order of the catalog.</summary>
        public IReadOnlyList<SelfCheckFinding> Findings
        {
            get { return _findings; }
        }

        /// <summary>A required patch target is missing or reserved: the core refuses the patches and makes the whole feature Unavailable.</summary>
        public bool FeatureUnavailable { get; private set; }

        /// <summary>Nothing is wrong (an advisory finding counts as wrong: the log mentions it).</summary>
        public bool IsClean
        {
            get { return _findings.Count == 0; }
        }

        /// <summary>The part works as far as the check can tell.</summary>
        public bool IsPartOn(FeaturePart part)
        {
            return !_off.Contains(part);
        }

        /// <summary>The parts that are off, in the order of <see cref="FeaturePart"/>.</summary>
        public IReadOnlyList<FeaturePart> PartsOff
        {
            get
            {
                var list = new List<FeaturePart>(_off);
                list.Sort();
                return list;
            }
        }

        /// <summary>One English line for the log.</summary>
        public string Summary
        {
            get
            {
                if (IsClean)
                {
                    return "all " + Checked.ToString(CultureInfo.InvariantCulture) + " members of the game the feature relies on are as expected";
                }

                var sb = new StringBuilder();
                sb.Append(_findings.Count.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(Checked.ToString(CultureInfo.InvariantCulture)).Append(" members are not as expected");
                if (FeatureUnavailable) sb.Append("; a required patch target is gone, the feature is Unavailable");
                IReadOnlyList<FeaturePart> off = PartsOff;
                if (off.Count > 0)
                {
                    sb.Append("; off:");
                    for (int i = 0; i < off.Count; i++) sb.Append(i == 0 ? " " : ", ").Append(off[i]);
                }
                return sb.ToString();
            }
        }

        internal void Add(SelfCheckFinding finding)
        {
            _findings.Add(finding);

            GameMember member = finding.Member;
            if (member.Advisory) return;
            if (finding.State == MemberState.Patched) return;   // a warning only: the call goes on

            if (member.IsPatchTarget && member.RequiredPatch) FeatureUnavailable = true;
            foreach (FeaturePart part in member.Parts) _off.Add(part);
        }
    }

    /// <summary>
    /// The decision part of the check at the start (FR-12 item 3; TSK-020): takes the answers about the members and says what is wrong and which parts of the feature depend on it. The question
    /// "is it in the game" is the adapter's (<c>ContractSelfCheck</c> looks the member up and asks <c>ModHooks.IsReserved</c>); everything that follows from the answer is here, so a test can walk
    /// through every member being gone.
    /// </summary>
    public static class SelfCheckEvaluator
    {
        /// <summary>
        /// Evaluates <paramref name="rows"/> with <paramref name="probe"/>. A probe that throws counts as <see cref="MemberState.Missing"/> with the exception in the detail: the check never throws.
        /// A required patch target that is missing or reserved makes <see cref="SelfCheckResult.FeatureUnavailable"/> true; a missing private member turns off the parts of its row; an advisory member
        /// only gets a finding.
        /// </summary>
        public static SelfCheckResult Evaluate(IReadOnlyList<GameMember> rows, Func<GameMember, MemberProbe> probe)
        {
            if (rows == null) throw new ArgumentNullException("rows");
            if (probe == null) throw new ArgumentNullException("probe");

            var result = new SelfCheckResult(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                GameMember row = rows[i];
                MemberProbe answer;
                try
                {
                    answer = probe(row);
                }
                catch (Exception ex)
                {
                    answer = MemberProbe.Of(MemberState.Missing, "the check failed: " + ex.GetType().Name);
                }

                if (answer.State == MemberState.Found) continue;
                result.Add(new SelfCheckFinding(row, answer));
            }
            return result;
        }
    }
}
