using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>
    /// The book of the self-check of the feature (design.md, "Поля для рефлексии", "Самопроверка на старте"; TSK-011), without the game: the game side
    /// (<c>GpuSelfCheck</c>) looks for the two optional patches, the six private fields, the twelve members of the game the feature calls and compares the three
    /// ports of the formulas of the game (F1, F10, F11) on three sets of numbers, and writes every finding here. The book turns the findings into the line of the
    /// log and of the hub page (<c>self-check OK (patches 2/2, fields 6/6, calls 12/12, ports 3/3)</c> once both patches are declared; before their tasks the
    /// count of the patches is of the declared ones) and into the abilities of the feature: a missing thing switches off only the ability that stands on it
    /// (<see cref="GpuCapability"/>), never the whole feature.
    /// </summary>
    public sealed class GpuSelfCheckBook
    {
        /// <summary>
        /// What is expected: six fields, twelve members, three ports. The patches expected are the ones the feature declared (<see cref="PatchesDeclared"/>): GP1 comes
        /// with TSK-021 and GP2 with TSK-041, so the count grows to <see cref="AllPatches"/> as they arrive.
        /// </summary>
        public const int ExpectedFields = 6, ExpectedCalls = 12, ExpectedPorts = 3;

        /// <summary>The optional patches of the design (GP1, GP2).</summary>
        public const int AllPatches = 2;

        /// <summary>The relative tolerance of a port against the game (single precision of the game, double here for F1).</summary>
        public const double PortTolerance = 1e-4;

        private readonly List<string> _problems = new List<string>();
        private readonly bool[] _off = new bool[Enum.GetValues(typeof(GpuCapability)).Length];

        public int PatchesApplied { get; private set; }

        /// <summary>The patches of the feature that are declared (<c>ctx.Patch</c>), applied or not.</summary>
        public int PatchesDeclared { get; private set; }

        public int FieldsFound { get; private set; }
        public int CallsFound { get; private set; }
        public int PortsMatched { get; private set; }

        /// <summary>The ports were compared (the game had a company and a process node to compare them on).</summary>
        public bool PortsRan { get; private set; }

        /// <summary>English lines of what is wrong, for the log; empty when everything was found.</summary>
        public IReadOnlyList<string> Problems
        {
            get { return _problems; }
        }

        /// <summary>Everything was found, applied and matched.</summary>
        public bool Ok
        {
            get
            {
                return PatchesApplied == PatchesDeclared && FieldsFound == ExpectedFields && CallsFound == ExpectedCalls && PortsMatched == ExpectedPorts
                       && _problems.Count == 0;
            }
        }

        /// <summary>
        /// An optional patch of the feature (GP1, GP2). Not declared yet (its task has not come): the ability it serves is off, quietly - nothing failed. Declared and
        /// applied: counted. Declared and not applied (the target is gone, a conflict): the ability is off and it is a problem.
        /// </summary>
        public void Patch(string name, bool declared, bool applied, GpuCapability serves)
        {
            if (!declared)
            {
                Switch(serves);
                return;
            }
            PatchesDeclared++;
            if (applied)
            {
                PatchesApplied++;
                return;
            }
            Switch(serves);
            _problems.Add("patch " + name + " is not applied: " + serves + " is off");
        }

        /// <summary>A private field of the game the feature reads or writes by reflection; a missing one switches off <paramref name="serves"/>.</summary>
        public void Field(string name, bool found, GpuCapability serves)
        {
            if (found)
            {
                FieldsFound++;
                return;
            }
            Switch(serves);
            _problems.Add("field " + name + " is missing: " + serves + " is off");
        }

        /// <summary>A member of the game the feature calls; a missing one switches off <paramref name="serves"/> when it is given.</summary>
        public void Call(string name, bool found, GpuCapability? serves)
        {
            if (found)
            {
                CallsFound++;
                return;
            }
            if (serves.HasValue) Switch(serves.Value);
            _problems.Add("member " + name + " is missing" + (serves.HasValue ? ": " + serves.Value + " is off" : string.Empty));
        }

        /// <summary>
        /// One port (F1, F10, F11) compared on its sets: <paramref name="game"/> and <paramref name="port"/> are the values on the same inputs, set by set. The port
        /// matches when every set agrees within <see cref="PortTolerance"/> (equal for whole numbers). A mismatch is reported; the feature goes on with the game's own
        /// formula (it always calls the game, the port is the check and the preview).
        /// </summary>
        public bool Port(string name, IList<double> game, IList<double> port)
        {
            PortsRan = true;
            int sets = game == null || port == null ? 0 : Math.Min(game.Count, port.Count);
            bool matched = sets > 0 && game.Count == port.Count;
            for (int i = 0; matched && i < sets; i++)
            {
                if (!Matches(game[i], port[i]))
                {
                    _problems.Add("port " + name + " differs from the game on set " + (i + 1).ToString(CultureInfo.InvariantCulture) + ": game "
                                  + game[i].ToString("R", CultureInfo.InvariantCulture) + ", port " + port[i].ToString("R", CultureInfo.InvariantCulture));
                    matched = false;
                }
            }
            if (sets == 0) _problems.Add("port " + name + " was not compared: no values");
            if (matched) PortsMatched++;
            return matched;
        }

        /// <summary>A port could not be compared (no company or no process node yet); it is not counted as matched and says why.</summary>
        public void PortSkipped(string name, string why)
        {
            _problems.Add("port " + name + " was not compared: " + why);
        }

        /// <summary>A finding that is neither a field, a call nor a port (for example, the ids of the companies a vendor binds to).</summary>
        public void Problem(string message)
        {
            if (!string.IsNullOrEmpty(message)) _problems.Add(message);
        }

        /// <summary>Whether the ability survived the check.</summary>
        public bool IsAvailable(GpuCapability capability)
        {
            int index = (int)capability;
            return index < 0 || index >= _off.Length || !_off[index];
        }

        /// <summary>Two values agree: equal, or within <see cref="PortTolerance"/> of the larger magnitude; two infinities of the same sign agree.</summary>
        public static bool Matches(double game, double port)
        {
            if (double.IsNaN(game) || double.IsNaN(port)) return double.IsNaN(game) && double.IsNaN(port);
            if (game == port) return true;
            if (double.IsInfinity(game) || double.IsInfinity(port)) return false;
            double scale = Math.Max(1d, Math.Max(Math.Abs(game), Math.Abs(port)));
            return Math.Abs(game - port) <= PortTolerance * scale;
        }

        /// <summary>The counts, <c>patches 2/2, fields 6/6, calls 12/12, ports 3/3</c>.</summary>
        public string Counts()
        {
            return "patches " + Ratio(PatchesApplied, PatchesDeclared) + ", fields " + Ratio(FieldsFound, ExpectedFields) + ", calls "
                   + Ratio(CallsFound, ExpectedCalls) + ", ports " + Ratio(PortsMatched, ExpectedPorts);
        }

        /// <summary>The line of the log: <c>self-check OK (patches 2/2, fields 6/6, calls 12/12, ports 3/3)</c>, or <c>self-check: N problem(s) (...)</c>.</summary>
        public string Summary()
        {
            if (Ok) return "self-check OK (" + Counts() + ")";
            return "self-check: " + _problems.Count.ToString(CultureInfo.InvariantCulture) + " problem(s) (" + Counts() + ")";
        }

        private void Switch(GpuCapability capability)
        {
            int index = (int)capability;
            if (index >= 0 && index < _off.Length) _off[index] = true;
        }

        private static string Ratio(int found, int expected)
        {
            return found.ToString(CultureInfo.InvariantCulture) + "/" + expected.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// The three sets of numbers the ports are compared on (F1, F10, F11): a die, a wafer, a yield, a material and a failure rate. The first set is the reference
    /// of the CODEMAP, <c>DiesPerWafer(300, 300, 1.0) = 197</c>; the others are a small die on a small wafer and a large die with a low yield.
    /// </summary>
    public static class GpuSelfCheckSamples
    {
        public struct Sample
        {
            public float Die, Wafer, Yield, Material, FailureRate;

            public Sample(float die, float wafer, float yield, float material, float failureRate)
            {
                Die = die;
                Wafer = wafer;
                Yield = yield;
                Material = material;
                FailureRate = failureRate;
            }
        }

        /// <summary>The three sets.</summary>
        public static readonly Sample[] Sets =
        {
            new Sample(300f, 300f, 1.0f, 8f, 0.08f),
            new Sample(80f, 200f, 0.9f, 10f, 0.05f),
            new Sample(600f, 300f, 0.85f, 25f, 0.12f),
        };
    }
}
