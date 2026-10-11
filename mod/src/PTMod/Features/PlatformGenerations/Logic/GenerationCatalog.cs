using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The generations of platforms of the game (FR-1): the validated, ordered list built from <c>generations.json</c> and the lookups the
    /// models use - the generation of a base package, "full platform", the status on a day, declared socket compatibility. Pure logic: the
    /// existence of the game's <c>Package</c> and <c>Memory</c> objects comes in as two functions (the adapter wraps
    /// <c>DataFinder.FindPackage</c> and <c>FindMemory</c>). Built once per session after the game scene is up.
    /// </summary>
    public sealed class GenerationCatalog
    {
        /// <summary>The first year a generation can be announced in (the game starts in 1971).</summary>
        public const int MinAnnounceYear = 1971;

        public const int MaxAnnounceYear = 2035;

        /// <summary>The largest <c>order</c> of a generation.</summary>
        public const int MaxOrder = 99;

        private readonly List<GenerationDef> _generations;
        private readonly Dictionary<string, GenerationDef> _byId = new Dictionary<string, GenerationDef>(StringComparer.Ordinal);
        private readonly Dictionary<string, GenerationDef> _byPackage = new Dictionary<string, GenerationDef>(StringComparer.Ordinal);
        private readonly HashSet<string> _pairs = new HashSet<string>(StringComparer.Ordinal);

        private GenerationCatalog(List<GenerationDef> generations)
        {
            _generations = generations;
            foreach (GenerationDef g in generations)
            {
                _byId[g.id] = g;
                foreach (string socket in g.sockets) _byPackage[socket] = g;
                if (g.compatibleSockets == null) continue;
                foreach (SocketPair pair in g.compatibleSockets) _pairs.Add(PairKey(pair.a, pair.b));
            }
        }

        /// <summary>A catalog with no generations: every CPU is "outside the generations".</summary>
        public static GenerationCatalog Empty
        {
            get { return new GenerationCatalog(new List<GenerationDef>()); }
        }

        /// <summary>The generations by <c>order</c>, oldest first.</summary>
        public IReadOnlyList<GenerationDef> Generations
        {
            get { return _generations; }
        }

        public int Count
        {
            get { return _generations.Count; }
        }

        /// <summary>The generation with this id, or null.</summary>
        public GenerationDef ById(string id)
        {
            GenerationDef g;
            return id != null && _byId.TryGetValue(id, out g) ? g : null;
        }

        /// <summary>The generation that lists this base package among its sockets; null = "outside the generations" (FR-1 item 5).</summary>
        public GenerationDef ByBasePackage(string basePackageName)
        {
            GenerationDef g;
            return basePackageName != null && _byPackage.TryGetValue(basePackageName, out g) ? g : null;
        }

        /// <summary>The generation of a socket, by its base package; null when the socket has none or is outside the generations.</summary>
        public GenerationDef BySocket(SocketId socket)
        {
            return ByBasePackage(socket.BasePackageName);
        }

        /// <summary>
        /// "Full platform" (D11): the memory standard is one of the generation's. False for no generation, no memory name or a standard that
        /// the generation does not list.
        /// </summary>
        public bool IsFullPlatform(GenerationDef g, string memoryName)
        {
            if (g == null || string.IsNullOrEmpty(memoryName) || g.memories == null) return false;
            for (int i = 0; i < g.memories.Count; i++)
            {
                if (string.Equals(g.memories[i], memoryName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// The pair of base packages is listed in <c>compatibleSockets</c> of some generation (FR-6 item 3), in either order. The same name
        /// twice is not a pair; use the equality of the names for that.
        /// </summary>
        public bool IsDeclaredCompatible(string basePackageA, string basePackageB)
        {
            if (string.IsNullOrEmpty(basePackageA) || string.IsNullOrEmpty(basePackageB)) return false;
            return _pairs.Contains(PairKey(basePackageA, basePackageB));
        }

        /// <summary>
        /// The status of a generation on <paramref name="now"/>. Obsolete from 1 January of <c>obsoleteYear</c>. Otherwise launched (the history
        /// knows the first CPU): <see cref="GenerationStatus.EarlyWindow"/> while fewer than <c>earlyWindowMonths</c> calendar months have passed
        /// since the launch, then <see cref="GenerationStatus.Mature"/>. Not launched: <see cref="GenerationStatus.Announced"/> from 1 January of
        /// <c>announceYear</c> (or as soon as the history recorded the announcement), before that <see cref="GenerationStatus.Expected"/>.
        /// A null <paramref name="history"/> knows nothing about launches, so only the dates decide. A launch in the future of <paramref name="now"/>
        /// (a date that does not fit the history) counts as no launch.
        /// </summary>
        public GenerationStatus StatusAt(GenerationDef g, GameDate now, IPlatformHistory history)
        {
            if (g == null) return GenerationStatus.Expected;
            if (g.obsoleteYear > 0 && now.IsSet && now.Year >= g.obsoleteYear) return GenerationStatus.Obsolete;

            GameDate launched;
            string by;
            if (history != null && history.IsLaunched(g.id, out launched, out by) && launched.IsSet && (!now.IsSet || launched <= now))
            {
                int months = now.IsSet ? launched.MonthsTo(now) : 0;
                return months < g.earlyWindowMonths ? GenerationStatus.EarlyWindow : GenerationStatus.Mature;
            }

            bool announced = (history != null && history.IsAnnounced(g.id)) || (now.IsSet && now.Year >= g.announceYear);
            return announced ? GenerationStatus.Announced : GenerationStatus.Expected;
        }

        /// <summary>
        /// How many months of the early adopter window are left on <paramref name="now"/> for a generation launched on <paramref name="launched"/>:
        /// 0 when the window is over (or the dates are not set).
        /// </summary>
        public int EarlyWindowMonthsLeft(GenerationDef g, GameDate launched, GameDate now)
        {
            if (g == null || !launched.IsSet || !now.IsSet) return 0;
            int left = g.earlyWindowMonths - launched.MonthsTo(now);
            return left < 0 ? 0 : left;
        }

        // ---- validation ----

        /// <summary>
        /// Checks one record (FR-1 item 2) and adds one English line to <paramref name="errors"/> for every problem; true when there is none.
        /// The checks: a non-empty id; <c>order</c> 0..<see cref="MaxOrder"/>; <c>name</c> in both languages; <c>announceYear</c> 1971..2035;
        /// <c>obsoleteYear</c> 0 or later than the announce year; a non-empty <c>sockets</c> of distinct names that exist; <c>memories</c> of
        /// distinct names that exist; <c>earlyWindowMonths</c> 6..60; the OEM curve (<c>delayMonths</c> 0..36, <c>rampMonths</c> 6..120,
        /// <c>maxAdoption</c> 0..1, <c>ibAccel</c> 0..2); <c>compatibleSockets</c> pairs of two different packages that exist.
        /// <paramref name="packageExists"/> and <paramref name="memoryExists"/> may be null: the existence is then not checked.
        /// Lines carry no id: the caller (the content loader, <see cref="Build"/>) adds it.
        /// </summary>
        public static bool Validate(GenerationDef g, Func<string, bool> packageExists, Func<string, bool> memoryExists, List<string> errors)
        {
            if (g == null)
            {
                Add(errors, "the record is empty");
                return false;
            }

            bool ok = true;
            if (string.IsNullOrWhiteSpace(g.id)) ok = Fail(errors, "id: is empty");
            if (g.order < 0 || g.order > MaxOrder) ok = Fail(errors, "order: " + Num(g.order) + " is outside 0.." + Num(MaxOrder));

            if (g.name == null || string.IsNullOrWhiteSpace(g.name.Ru) || string.IsNullOrWhiteSpace(g.name.En))
                ok = Fail(errors, "name: needs both 'ru' and 'en'");

            if (g.announceYear < MinAnnounceYear || g.announceYear > MaxAnnounceYear)
                ok = Fail(errors, "announceYear: " + Num(g.announceYear) + " is outside " + Num(MinAnnounceYear) + ".." + Num(MaxAnnounceYear));
            if (g.obsoleteYear != 0 && g.obsoleteYear <= g.announceYear)
                ok = Fail(errors, "obsoleteYear: " + Num(g.obsoleteYear) + " must be 0 (never) or later than announceYear " + Num(g.announceYear));

            ok &= ValidateNames(g.sockets, "sockets", true, "Package", packageExists, errors);
            ok &= ValidateNames(g.memories, "memories", false, "Memory", memoryExists, errors);

            if (g.earlyWindowMonths < 6 || g.earlyWindowMonths > 60)
                ok = Fail(errors, "earlyWindowMonths: " + Num(g.earlyWindowMonths) + " is outside 6..60");

            if (g.oem == null)
            {
                ok = Fail(errors, "oem: is missing");
            }
            else
            {
                if (g.oem.delayMonths < 0 || g.oem.delayMonths > 36) ok = Fail(errors, "oem.delayMonths: " + Num(g.oem.delayMonths) + " is outside 0..36");
                if (g.oem.rampMonths < 6 || g.oem.rampMonths > 120) ok = Fail(errors, "oem.rampMonths: " + Num(g.oem.rampMonths) + " is outside 6..120");
                if (!InRange(g.oem.maxAdoption, 0, 1)) ok = Fail(errors, "oem.maxAdoption: " + Num(g.oem.maxAdoption) + " is outside 0..1");
                if (!InRange(g.oem.ibAccel, 0, 2)) ok = Fail(errors, "oem.ibAccel: " + Num(g.oem.ibAccel) + " is outside 0..2");
            }

            if (g.compatibleSockets != null)
            {
                for (int i = 0; i < g.compatibleSockets.Count; i++)
                {
                    SocketPair pair = g.compatibleSockets[i];
                    string path = "compatibleSockets[" + Num(i) + "]";
                    if (pair == null || string.IsNullOrEmpty(pair.a) || string.IsNullOrEmpty(pair.b))
                    {
                        ok = Fail(errors, path + ": needs both 'a' and 'b'");
                        continue;
                    }
                    if (string.Equals(pair.a, pair.b, StringComparison.Ordinal)) ok = Fail(errors, path + ": '" + pair.a + "' is paired with itself");
                    foreach (string name in new[] { pair.a, pair.b })
                    {
                        if (packageExists != null && !packageExists(name))
                            ok = Fail(errors, path + ": '" + name + "' is not a Package of the game");
                    }
                }
            }
            return ok;
        }

        /// <summary>
        /// Builds the catalog from the loaded records (FR-1 items 2-4): a record that fails <see cref="Validate"/> is skipped; so is a record
        /// whose <c>id</c> or <c>order</c> repeats an earlier one (the first stays). A socket listed by two generations stays in the one with the
        /// smaller <c>order</c> and is removed from the other; a generation left with no socket is skipped. Every problem is one English line in
        /// <paramref name="errors"/> (may be null), starting with <c>generation 'id'</c>; the caller logs them. The records are not changed.
        /// The result is never null; <c>Generations</c> is sorted by <c>order</c>.
        /// </summary>
        public static GenerationCatalog Build(IReadOnlyList<GenerationDef> records, Func<string, bool> packageExists, Func<string, bool> memoryExists,
            List<string> errors)
        {
            var accepted = new List<GenerationDef>();
            if (records == null) return new GenerationCatalog(accepted);

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var orders = new Dictionary<int, string>();
            for (int i = 0; i < records.Count; i++)
            {
                GenerationDef g = records[i];
                if (g == null)
                {
                    Add(errors, "record #" + Num(i + 1) + ": the record is empty, it is skipped");
                    continue;
                }

                string label = "generation '" + g.id + "'";
                var problems = new List<string>();
                if (!Validate(g, packageExists, memoryExists, problems))
                {
                    foreach (string problem in problems) Add(errors, label + ": " + problem);
                    Add(errors, label + ": the generation is skipped");
                    continue;
                }
                if (!ids.Add(g.id))
                {
                    Add(errors, label + ": id: repeats an earlier generation, the generation is skipped");
                    continue;
                }
                string sameOrder;
                if (orders.TryGetValue(g.order, out sameOrder))
                {
                    Add(errors, label + ": order: " + Num(g.order) + " is already the order of '" + sameOrder + "', the generation is skipped");
                    continue;
                }
                orders[g.order] = g.id;
                accepted.Add(g);
            }

            accepted.Sort((x, y) => x.order.CompareTo(y.order));

            // A socket belongs to the generation with the smaller order (FR-1 item 4).
            var owner = new Dictionary<string, GenerationDef>(StringComparer.Ordinal);
            var result = new List<GenerationDef>(accepted.Count);
            foreach (GenerationDef g in accepted)
            {
                var kept = new List<string>(g.sockets.Count);
                foreach (string socket in g.sockets)
                {
                    GenerationDef first;
                    if (owner.TryGetValue(socket, out first))
                    {
                        Add(errors, "generation '" + g.id + "': socket '" + socket + "' is already in generation '" + first.id + "' (smaller order), it stays there");
                        continue;
                    }
                    owner[socket] = g;
                    kept.Add(socket);
                }

                if (kept.Count == 0)
                {
                    Add(errors, "generation '" + g.id + "': no socket is left after the duplicates were removed, the generation is skipped");
                    continue;
                }
                result.Add(kept.Count == g.sockets.Count ? g : WithSockets(g, kept));
            }
            return new GenerationCatalog(result);
        }

        // ---- helpers ----

        private static bool ValidateNames(List<string> names, string member, bool required, string kind, Func<string, bool> exists, List<string> errors)
        {
            if (names == null || names.Count == 0)
            {
                if (!required) return true;
                Add(errors, member + ": needs at least one name");
                return false;
            }

            bool ok = true;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                string path = member + "[" + Num(i) + "]";
                if (string.IsNullOrEmpty(name))
                {
                    Add(errors, path + ": the name is empty");
                    ok = false;
                    continue;
                }
                if (!seen.Add(name))
                {
                    Add(errors, path + ": '" + name + "' is listed twice");
                    ok = false;
                    continue;
                }
                if (exists != null && !exists(name))
                {
                    Add(errors, path + ": '" + name + "' is not a " + kind + " of the game");
                    ok = false;
                }
            }
            return ok;
        }

        private static GenerationDef WithSockets(GenerationDef g, List<string> sockets)
        {
            return new GenerationDef
            {
                id = g.id,
                order = g.order,
                name = g.name,
                description = g.description,
                announceYear = g.announceYear,
                obsoleteYear = g.obsoleteYear,
                sockets = sockets,
                memories = g.memories,
                earlyWindowMonths = g.earlyWindowMonths,
                oem = g.oem,
                compatibleSockets = g.compatibleSockets,
            };
        }

        private static string PairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) <= 0 ? a + "\n" + b : b + "\n" + a;
        }

        private static bool InRange(float value, float min, float max)
        {
            return !float.IsNaN(value) && value >= min && value <= max;
        }

        private static bool Fail(List<string> errors, string message)
        {
            Add(errors, message);
            return false;
        }

        private static void Add(List<string> errors, string message)
        {
            if (errors != null) errors.Add(message);
        }

        private static string Num(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Num(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
