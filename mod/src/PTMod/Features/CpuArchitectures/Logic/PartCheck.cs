using System;
using System.Collections.Generic;
using System.Text;

namespace PTMod.Features.CpuArchitectures
{
    /// <summary>
    /// The verdicts of the self-check on the members of the game the feature stands on (NFR-2, TSK-030), kept per part: a part is off when one of its members that is not
    /// optional is missing, wrong or reserved, and only that part - the others go on. The class holds no game types: <c>ArchSelfCheck</c> looks the members up with reflection and
    /// hands the verdict in as a word, the tests hand in a made-up one. The line of a part is the one written to <c>Player.log</c>:
    /// <c>CreatorCpu.ipc ok; CreatorCpu.coreCountDropdown missing</c>.
    /// <para>
    /// In the debug build a member can be hidden on purpose, to see the feature degrade without a game that lacks it: the flag <see cref="HideFlag"/> of a member
    /// (<c>"cpu-architectures.hide.CreatorCpu.coreCountDropdown": true</c> in the <c>debug</c> section of <c>ptmod_settings.json</c>) makes the self-check treat the member as missing
    /// (<see cref="HiddenState"/>). A release build never reads the section.
    /// </para>
    /// </summary>
    public sealed class PartCheck
    {
        /// <summary>The verdict "the member is there and fits".</summary>
        public const string Ok = "ok";

        /// <summary>The verdict of a member hidden by its debug flag; it counts as missing.</summary>
        public const string HiddenState = "hidden by the debug flag";

        /// <summary>The prefix of the debug flags that hide a member.</summary>
        public const string HidePrefix = "cpu-architectures.hide.";

        private sealed class Entry
        {
            public readonly StringBuilder Line = new StringBuilder();
            public int Members;
            public bool Off;
        }

        private readonly Dictionary<string, Entry> _parts = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        /// <summary>The debug flag that hides a member: <c>cpu-architectures.hide.&lt;type&gt;.&lt;member&gt;</c>, the type without its namespace.</summary>
        public static string HideFlag(string typeName, string member)
        {
            return HidePrefix + (typeName ?? string.Empty) + "." + (member ?? string.Empty);
        }

        /// <summary>The verdict is "ok".</summary>
        public static bool IsOk(string state)
        {
            return string.Equals(state, Ok, StringComparison.Ordinal);
        }

        /// <summary>Members looked at so far.</summary>
        public int Members { get; private set; }

        /// <summary>
        /// Records a member of a part. <paramref name="display"/> is how the line names it, <paramref name="state"/> the verdict (<see cref="Ok"/> or the reason). A missing member that is
        /// <paramref name="optional"/> is written down as "missing (optional)" and does not switch the part off.
        /// </summary>
        public void Add(string part, string display, string state, bool optional)
        {
            if (string.IsNullOrEmpty(part)) throw new ArgumentException("part");

            Entry entry;
            if (!_parts.TryGetValue(part, out entry))
            {
                entry = new Entry();
                _parts[part] = entry;
                _order.Add(part);
            }

            bool ok = IsOk(state);
            if (entry.Line.Length > 0) entry.Line.Append("; ");
            entry.Line.Append(display).Append(' ').Append(ok ? Ok : (optional ? "missing (optional)" : state));
            entry.Members++;
            Members++;
            if (!ok && !optional) entry.Off = true;
        }

        /// <summary>The part has something that is missing and not optional. A part nothing was recorded for works.</summary>
        public bool IsOff(string part)
        {
            Entry entry;
            return part != null && _parts.TryGetValue(part, out entry) && entry.Off;
        }

        /// <summary>The members of a part with their verdicts, as the line of the log shows them; empty for a part with none.</summary>
        public string Line(string part)
        {
            Entry entry;
            return part != null && _parts.TryGetValue(part, out entry) ? entry.Line.ToString() : string.Empty;
        }

        /// <summary>The parts that are off, in the order they were first recorded.</summary>
        public List<string> OffParts()
        {
            var off = new List<string>();
            foreach (string part in _order)
            {
                if (_parts[part].Off) off.Add(part);
            }
            return off;
        }

        /// <summary>The parts that have been recorded, in the order they were first recorded.</summary>
        public IReadOnlyList<string> Parts
        {
            get { return _order; }
        }
    }
}
