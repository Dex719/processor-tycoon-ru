using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>What a company's first CPU on a socket recorded (FR-6 item 1): the day, the main socket it left and the strength of the penalty.</summary>
    public struct SocketIntro
    {
        /// <summary>False = the company has no record for the socket; the other members are empty then.</summary>
        public bool Exists;

        /// <summary>The day of the first CPU of the company on the socket.</summary>
        public GameDate Date;

        /// <summary>The company's main socket at that moment, or null when it had none (or it is not known).</summary>
        public string MainSocketKey;

        /// <summary>The age of the main socket in months at that moment.</summary>
        public int MainAgeMonths;

        /// <summary>The strength 0..1 of the socket-change penalty (<see cref="ChurnDetector.Severity"/>).</summary>
        public float Severity;
    }

    /// <summary>An early adopter of a generation: the company and the day it became one.</summary>
    public struct EarlyAdopterRecord
    {
        public string CompanyKey;
        public GameDate Date;
    }

    /// <summary>What a release of a CPU changed in the chronology (or would change, for <see cref="PlatformTimeline.Preview"/>).</summary>
    public struct ReleaseOutcome
    {
        /// <summary>The generation was not announced before and is now (the release came before the announce day, or the tick was missed).</summary>
        public bool Announced;

        /// <summary>The CPU is the first one of the generation: the generation is launched, the company is the pioneer.</summary>
        public bool Launched;

        /// <summary>The company became an early adopter of the generation with this release.</summary>
        public bool EarlyAdopter;

        /// <summary>1-based number of the early adopter slot taken (1 for the pioneer); 0 when <see cref="EarlyAdopter"/> is false.</summary>
        public int EarlySlot;

        /// <summary>The company's first CPU on this socket.</summary>
        public bool NewSocket;

        /// <summary>The strength 0..1 of the socket-change penalty; 0 when there is none (or the socket is not new).</summary>
        public float ChurnSeverity;

        /// <summary>The main socket the company leaves (only for a new socket that has one), or null.</summary>
        public string MainSocketKey;

        /// <summary>The age in months of <see cref="MainSocketKey"/>; 0 without a main socket.</summary>
        public int MainAgeMonths;
    }

    /// <summary>
    /// The chronology of the generations (FR-2, FR-3, FR-6): when each was announced and launched, who the early adopters are, and when each
    /// company first shipped a CPU on each socket (with the penalty that came with it). Pure logic: dates are <see cref="GameDate"/>, companies
    /// are keys, sockets are <see cref="SocketId"/>. It implements <see cref="IPlatformHistory"/>, so <see cref="GenerationCatalog.StatusAt"/>
    /// reads launches from it. State lives in the save through <see cref="WriteTo"/> / <see cref="ReadFrom"/>; records of generations that the
    /// content no longer has are kept and written back untouched (FR-12 item 6). Not thread-safe.
    /// </summary>
    public sealed class PlatformTimeline : IPlatformHistory
    {
        private sealed class GenState
        {
            public string Id;
            public GameDate Announced;
            public GameDate Launched;
            public string LaunchedBy = string.Empty;
            public readonly List<EarlyAdopterRecord> Adopters = new List<EarlyAdopterRecord>();
        }

        private sealed class IntroState
        {
            public string Company;
            public string Socket;
            public GameDate Date;
            public string Main;
            public int MainAgeMonths;
            public float Severity;
        }

        private readonly Dictionary<string, GenState> _gens = new Dictionary<string, GenState>(StringComparer.Ordinal);
        private readonly List<GenState> _genList = new List<GenState>();
        private readonly Dictionary<CompanySocketKey, IntroState> _intros = new Dictionary<CompanySocketKey, IntroState>();
        private readonly List<IntroState> _introList = new List<IntroState>();

        /// <summary>How many generations have a record (announced or launched), the ones the content no longer has included.</summary>
        public int GenerationCount
        {
            get { return _genList.Count; }
        }

        /// <summary>How many socket appearances are recorded.</summary>
        public int IntroCount
        {
            get { return _introList.Count; }
        }

        /// <summary>Forgets everything (a new game, leaving to the menu, a section that cannot be used).</summary>
        public void Clear()
        {
            _gens.Clear();
            _genList.Clear();
            _intros.Clear();
            _introList.Clear();
        }

        // ---- generations ----

        public bool IsAnnounced(string genId)
        {
            GenState state = Find(genId);
            return state != null && state.Announced.IsSet;
        }

        /// <summary>The day the generation was announced; <see cref="GameDate.None"/> when it was not.</summary>
        public GameDate AnnouncedOn(string genId)
        {
            GenState state = Find(genId);
            return state == null ? GameDate.None : state.Announced;
        }

        public bool IsLaunched(string genId, out GameDate date, out string byCompanyKey)
        {
            GenState state = Find(genId);
            if (state == null || !state.Launched.IsSet)
            {
                date = GameDate.None;
                byCompanyKey = null;
                return false;
            }
            date = state.Launched;
            byCompanyKey = state.LaunchedBy;
            return true;
        }

        /// <summary>
        /// FR-2: records the announcement of the generation. An announced generation stays as it is (the first date stands): the call is safe to
        /// repeat. False for an empty id or a date that is not set, and when the generation was announced already.
        /// </summary>
        public bool Announce(string genId, GameDate date)
        {
            if (string.IsNullOrEmpty(genId) || !date.IsSet) return false;

            GenState state = GetOrCreate(genId);
            if (state.Announced.IsSet) return false;
            state.Announced = date;
            return true;
        }

        /// <summary>
        /// Records the launch of the generation (the first CPU on it) by the company. The first record stands. Used by the release itself and by
        /// the reconstruction (FR-12 item 3). False for an empty id, no date, or when the generation is launched already.
        /// </summary>
        public bool SetLaunched(string genId, GameDate date, string byCompanyKey)
        {
            if (string.IsNullOrEmpty(genId) || !date.IsSet) return false;

            GenState state = GetOrCreate(genId);
            if (state.Launched.IsSet) return false;
            state.Launched = date;
            state.LaunchedBy = byCompanyKey ?? string.Empty;
            return true;
        }

        /// <summary>
        /// Records an early adopter of the generation. <paramref name="maxSlots"/> is the number of slots (<c>earlyAdopterSlots</c>); the call
        /// refuses a company that is an adopter already and a full list. For the reconstruction and the release.
        /// </summary>
        public bool AddEarlyAdopter(string genId, string companyKey, GameDate date, int maxSlots)
        {
            if (string.IsNullOrEmpty(genId) || string.IsNullOrEmpty(companyKey) || !date.IsSet) return false;

            GenState state = GetOrCreate(genId);
            if (state.Adopters.Count >= maxSlots) return false;
            if (IndexOfAdopter(state, companyKey) >= 0) return false;
            state.Adopters.Add(new EarlyAdopterRecord { CompanyKey = companyKey, Date = date });
            return true;
        }

        /// <summary>The early adopters of the generation in the order they joined (empty for an unknown generation).</summary>
        public IReadOnlyList<EarlyAdopterRecord> EarlyAdoptersOf(string genId)
        {
            GenState state = Find(genId);
            return state == null ? (IReadOnlyList<EarlyAdopterRecord>)new EarlyAdopterRecord[0] : state.Adopters;
        }

        /// <summary>The company is an early adopter of the generation: since when.</summary>
        public bool IsEarlyAdopter(string companyKey, string genId, out GameDate since)
        {
            GenState state = Find(genId);
            int index = state == null ? -1 : IndexOfAdopter(state, companyKey);
            since = index >= 0 ? state.Adopters[index].Date : GameDate.None;
            return index >= 0;
        }

        // ---- socket appearances ----

        /// <summary>
        /// The record of the company's first CPU on the socket. A record that exists stays (FR-6 item 1: once per company x socket). False for
        /// empty keys or a missing date, and when the pair is recorded already. <paramref name="severity"/> is kept in [0, 1].
        /// </summary>
        public bool AddSocketIntro(string companyKey, string socketKey, GameDate date, string mainSocketKey, int mainAgeMonths, float severity)
        {
            if (string.IsNullOrEmpty(companyKey) || string.IsNullOrEmpty(socketKey) || !date.IsSet) return false;

            var key = new CompanySocketKey(companyKey, socketKey);
            if (_intros.ContainsKey(key)) return false;

            var intro = new IntroState
            {
                Company = companyKey,
                Socket = socketKey,
                Date = date,
                Main = string.IsNullOrEmpty(mainSocketKey) ? null : mainSocketKey,
                MainAgeMonths = Math.Max(0, mainAgeMonths),
                Severity = ClampSeverity(severity),
            };
            _intros[key] = intro;
            _introList.Add(intro);
            return true;
        }

        /// <summary>What the company recorded for the socket (<see cref="SocketIntro.Exists"/> = false when nothing).</summary>
        public SocketIntro IntroOf(string companyKey, string socketKey)
        {
            IntroState intro = FindIntro(companyKey, socketKey);
            if (intro == null) return default(SocketIntro);
            return new SocketIntro { Exists = true, Date = intro.Date, MainSocketKey = intro.Main, MainAgeMonths = intro.MainAgeMonths, Severity = intro.Severity };
        }

        /// <summary>
        /// The age in months of the company's socket on <paramref name="now"/>: calendar months since its first CPU there (0 on the first day;
        /// never negative). -1 when the company has no record for the socket or <paramref name="now"/> is not set.
        /// </summary>
        public int SocketAgeMonths(string companyKey, string socketKey, GameDate now)
        {
            IntroState intro = FindIntro(companyKey, socketKey);
            if (intro == null || !now.IsSet) return -1;
            return Math.Max(0, intro.Date.MonthsTo(now));
        }

        // ---- the release of a CPU ----

        /// <summary>
        /// FR-3, FR-6: books the release of a CPU (the handler of <c>ModEvents.CpuReleased</c> calls it for a CPU that is not a variant and not
        /// from the fast-forward) and returns what it changed. In order: the generation is announced if it was not (on its announce day when the
        /// release is on or after it, otherwise on the day of the release - the generation cannot launch unannounced); the first CPU launches it;
        /// the company's first CPU on this socket within <c>earlyWindowMonths</c> of the launch takes a free early adopter slot (the pioneer
        /// is the first adopter) and records the appearance of the socket with the penalty strength against the company's main socket.
        /// A second CPU on a known socket changes nothing. A CPU outside the generations (<paramref name="gen"/> null, a socket without a key)
        /// changes nothing. <paramref name="ib"/> and <paramref name="cat"/> may be null (no main socket is found then).
        /// </summary>
        public ReleaseOutcome OnCpuReleased(string companyKey, SocketId socket, GenerationDef gen, GameDate date,
            InstalledBaseLedger ib, GenerationCatalog cat, PlatformBalance b)
        {
            return Process(companyKey, socket, gen, date, ib, cat, b, true);
        }

        /// <summary>FR-7: the same answer as <see cref="OnCpuReleased"/> and no change of the state (the warning in the release window).</summary>
        public ReleaseOutcome Preview(string companyKey, SocketId socket, GenerationDef gen, GameDate date,
            InstalledBaseLedger ib, GenerationCatalog cat, PlatformBalance b)
        {
            return Process(companyKey, socket, gen, date, ib, cat, b, false);
        }

        private ReleaseOutcome Process(string companyKey, SocketId socket, GenerationDef gen, GameDate date,
            InstalledBaseLedger ib, GenerationCatalog cat, PlatformBalance b, bool commit)
        {
            var outcome = new ReleaseOutcome();
            if (string.IsNullOrEmpty(companyKey) || !socket.IsValid || gen == null || string.IsNullOrEmpty(gen.id) || !date.IsSet) return outcome;
            if (b == null) b = PlatformBalance.Defaults();

            GenState state = Find(gen.id);
            bool wasAnnounced = state != null && state.Announced.IsSet;
            bool wasLaunched = state != null && state.Launched.IsSet;

            // FR-2 item 3 / E1: no launch without an announcement.
            GameDate announceDate = GameDate.None;
            if (!wasAnnounced)
            {
                outcome.Announced = true;
                announceDate = date.Year >= gen.announceYear ? new GameDate(gen.announceYear, 1, 1) : date;
            }

            // FR-3 item 1: the first CPU launches the generation.
            GameDate launchDate = wasLaunched ? state.Launched : date;
            if (!wasLaunched) outcome.Launched = true;

            // FR-6 item 1: once per company x socket.
            bool newSocket = FindIntro(companyKey, socket.Key) == null;
            outcome.NewSocket = newSocket;

            // FR-3 item 2 and 6: the first CPU of the company in the generation, inside the window, while a slot is free.
            int adopterCount = state == null ? 0 : state.Adopters.Count;
            bool alreadyAdopter = state != null && IndexOfAdopter(state, companyKey) >= 0;
            if (newSocket && !alreadyAdopter && adopterCount < b.Rating.EarlyAdopterSlots && launchDate.MonthsTo(date) < gen.earlyWindowMonths)
            {
                outcome.EarlyAdopter = true;
                outcome.EarlySlot = adopterCount + 1;
            }

            // FR-6 item 2: the penalty against the main socket.
            if (newSocket) ReadChurn(companyKey, socket, date, ib, cat, b, ref outcome);

            if (!commit) return outcome;

            if (outcome.Announced) Announce(gen.id, announceDate);
            if (outcome.Launched) SetLaunched(gen.id, date, companyKey);
            if (outcome.EarlyAdopter) AddEarlyAdopter(gen.id, companyKey, date, int.MaxValue);
            if (newSocket) AddSocketIntro(companyKey, socket.Key, date, outcome.MainSocketKey, outcome.MainAgeMonths, outcome.ChurnSeverity);
            return outcome;
        }

        /// <summary>Finds the company's main socket and fills the severity, the main socket and its age into the outcome.</summary>
        private void ReadChurn(string companyKey, SocketId socket, GameDate date, InstalledBaseLedger ib, GenerationCatalog cat, PlatformBalance b,
            ref ReleaseOutcome outcome)
        {
            if (ib == null) return;

            string mainKey = ib.MainSocketOf(companyKey, socket.Key);
            if (mainKey == null) return;

            // A main socket without a record of its own (the CPUs came with a purchase, or the save is older than the mod) counts as old enough:
            // a penalty is never given for what the chronology cannot show (Migration and Compatibility).
            int age = SocketAgeMonths(companyKey, mainKey, date);
            if (age < 0) age = b.Rating.MinSocketLifeMonths;

            double share = ib.Total > 0 ? ib.Of(companyKey, mainKey) / ib.Total : 0;
            bool compatible = SocketIdentity.Compatible(socket, ib.SocketOf(mainKey), cat, b);

            outcome.MainSocketKey = mainKey;
            outcome.MainAgeMonths = age;
            outcome.ChurnSeverity = ChurnDetector.Severity(true, age, share, compatible, b);
        }

        // ---- the save ----

        /// <summary>
        /// Writes the chronology into <paramref name="save"/> (<c>generations</c> and <c>socketIntros</c>; the contract preferences are not touched).
        /// The order of the lists is the order the records were made, so a write of a read section is the same section.
        /// </summary>
        public void WriteTo(PlatformGenerationsSave save)
        {
            if (save == null) return;

            var generations = new List<GenerationStateDto>(_genList.Count);
            foreach (GenState state in _genList)
            {
                var dto = new GenerationStateDto
                {
                    id = state.Id,
                    announced = state.Announced.ToString(),
                    launched = state.Launched.ToString(),
                    launchedBy = state.Launched.IsSet ? state.LaunchedBy : string.Empty,
                };
                foreach (EarlyAdopterRecord adopter in state.Adopters)
                {
                    dto.earlyAdopters.Add(new AdopterDto { company = adopter.CompanyKey, date = adopter.Date.ToString() });
                }
                generations.Add(dto);
            }
            save.generations = generations;

            var intros = new List<SocketIntroDto>(_introList.Count);
            foreach (IntroState intro in _introList)
            {
                intros.Add(new SocketIntroDto
                {
                    company = intro.Company,
                    socket = intro.Socket,
                    date = intro.Date.ToString(),
                    main = intro.Main ?? string.Empty,
                    mainAgeMonths = intro.MainAgeMonths,
                    severity = intro.Severity,
                });
            }
            save.socketIntros = intros;
        }

        /// <summary>
        /// Replaces the chronology with the one in <paramref name="save"/> (NFR-8, design "Validation Rules"). A date must be <c>yyyy-MM-dd</c>
        /// and, when <paramref name="today"/> is set, not later than it; a record with a bad key or date is dropped (the date of a generation is
        /// dropped alone, its other facts stay), a repeated record is dropped, a severity outside [0, 1] is cut to it. Generations that the content does not
        /// have are kept. One English line per problem goes to <paramref name="problems"/> (may be null). Never throws.
        /// </summary>
        public void ReadFrom(PlatformGenerationsSave save, GameDate today, List<string> problems)
        {
            Clear();
            if (save == null) return;

            if (save.generations != null)
            {
                for (int i = 0; i < save.generations.Count; i++) ReadGeneration(save.generations[i], i, today, problems);
            }
            if (save.socketIntros != null)
            {
                for (int i = 0; i < save.socketIntros.Count; i++) ReadIntro(save.socketIntros[i], i, today, problems);
            }
        }

        private void ReadGeneration(GenerationStateDto dto, int index, GameDate today, List<string> problems)
        {
            string path = "generations[" + index + "]";
            if (dto == null) return;
            if (string.IsNullOrEmpty(dto.id))
            {
                Report(problems, path + ": the id is empty, the record is dropped");
                return;
            }
            if (_gens.ContainsKey(dto.id))
            {
                Report(problems, path + ": generation '" + dto.id + "' repeats an earlier record, the record is dropped");
                return;
            }

            GenState state = GetOrCreate(dto.id);

            GameDate date;
            if (ReadDate(dto.announced, today, path + ".announced", problems, out date)) state.Announced = date;
            if (ReadDate(dto.launched, today, path + ".launched", problems, out date))
            {
                state.Launched = date;
                state.LaunchedBy = dto.launchedBy ?? string.Empty;
            }

            if (dto.earlyAdopters == null) return;
            for (int i = 0; i < dto.earlyAdopters.Count; i++)
            {
                AdopterDto adopter = dto.earlyAdopters[i];
                string adopterPath = path + ".earlyAdopters[" + i + "]";
                if (adopter == null) continue;
                if (string.IsNullOrEmpty(adopter.company))
                {
                    Report(problems, adopterPath + ": the company is empty, the record is dropped");
                    continue;
                }
                if (!ReadDate(adopter.date, today, adopterPath + ".date", problems, out date) || !date.IsSet) continue;
                if (IndexOfAdopter(state, adopter.company) >= 0)
                {
                    Report(problems, adopterPath + ": company '" + adopter.company + "' is listed twice, the record is dropped");
                    continue;
                }
                state.Adopters.Add(new EarlyAdopterRecord { CompanyKey = adopter.company, Date = date });
            }
        }

        private void ReadIntro(SocketIntroDto dto, int index, GameDate today, List<string> problems)
        {
            string path = "socketIntros[" + index + "]";
            if (dto == null) return;
            if (string.IsNullOrEmpty(dto.company) || string.IsNullOrEmpty(dto.socket))
            {
                Report(problems, path + ": the company or the socket is empty, the record is dropped");
                return;
            }

            GameDate date;
            if (!ReadDate(dto.date, today, path + ".date", problems, out date) || !date.IsSet)
            {
                if (string.IsNullOrEmpty(dto.date)) Report(problems, path + ".date: the date is missing, the record is dropped");
                return;
            }

            float severity = dto.severity;
            if (float.IsNaN(severity) || severity < 0f || severity > 1f)
            {
                Report(problems, path + ".severity: " + severity.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is outside 0..1, it is cut to the range");
            }

            if (!AddSocketIntro(dto.company, dto.socket, date, dto.main, dto.mainAgeMonths, severity))
            {
                Report(problems, path + ": company '" + dto.company + "' on socket '" + dto.socket + "' repeats an earlier record, the record is dropped");
            }
        }

        /// <summary>
        /// An empty text is "no date" (true, <see cref="GameDate.None"/>). A text that is not a date or lies after <paramref name="today"/> is a
        /// problem (false). A set date is true.
        /// </summary>
        private static bool ReadDate(string text, GameDate today, string path, List<string> problems, out GameDate date)
        {
            date = GameDate.None;
            if (string.IsNullOrEmpty(text)) return true;

            if (!GameDate.TryParse(text, out date))
            {
                date = GameDate.None;
                Report(problems, path + ": '" + text + "' is not a date yyyy-MM-dd, the date is dropped");
                return false;
            }
            if (today.IsSet && date > today)
            {
                Report(problems, path + ": " + text + " is later than today (" + today + "), the date is dropped");
                date = GameDate.None;
                return false;
            }
            return true;
        }

        // ---- helpers ----

        private GenState Find(string genId)
        {
            GenState state;
            return genId != null && _gens.TryGetValue(genId, out state) ? state : null;
        }

        private GenState GetOrCreate(string genId)
        {
            GenState state;
            if (_gens.TryGetValue(genId, out state)) return state;

            state = new GenState { Id = genId };
            _gens[genId] = state;
            _genList.Add(state);
            return state;
        }

        private IntroState FindIntro(string companyKey, string socketKey)
        {
            if (companyKey == null || socketKey == null) return null;
            IntroState intro;
            return _intros.TryGetValue(new CompanySocketKey(companyKey, socketKey), out intro) ? intro : null;
        }

        private static int IndexOfAdopter(GenState state, string companyKey)
        {
            for (int i = 0; i < state.Adopters.Count; i++)
            {
                if (string.Equals(state.Adopters[i].CompanyKey, companyKey, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        private static float ClampSeverity(float severity)
        {
            if (float.IsNaN(severity) || severity < 0f) return 0f;
            return severity > 1f ? 1f : severity;
        }

        private static void Report(List<string> problems, string line)
        {
            if (problems != null) problems.Add(line);
        }
    }
}
