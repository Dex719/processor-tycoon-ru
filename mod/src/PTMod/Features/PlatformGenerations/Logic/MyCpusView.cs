using System;
using System.Collections.Generic;

namespace PTMod.Features.PlatformGenerations
{
    /// <summary>
    /// The state of the feature on one day, as the views read it: the generations, the chronology, the installed base, the preferences of
    /// the contracts, the numbers, the two switches and today's date. A bundle of references (built by the game side from the feature, by a test from
    /// its own objects), so the views take one argument and a test does not need the feature.
    /// </summary>
    public sealed class PlatformSnapshot
    {
        public GenerationCatalog Catalog = GenerationCatalog.Empty;
        public PlatformTimeline Timeline = new PlatformTimeline();
        public InstalledBaseLedger InstalledBase = new InstalledBaseLedger();
        public OemPreferences Preferences = new OemPreferences();
        public PlatformBalance Balance = PlatformBalance.Defaults();
        public GameDate Today;

        /// <summary>The switch "эффекты на рейтинг" is on: the totals of the window are the multipliers, otherwise ×1.00.</summary>
        public bool RatingEffects = true;

        /// <summary>The switch "эффекты на контракты" is on.</summary>
        public bool OemEffects = true;
    }

    /// <summary>A CPU of the player as the tab "Мои CPU" gets it from the game: the name, the socket and the memory standard.</summary>
    public struct MyCpuInput
    {
        public string Name;

        /// <summary>The socket of the CPU; an invalid one (no key) means the game could not say, and the CPU is outside the generations.</summary>
        public SocketId Socket;

        /// <summary><c>Cpu.Memory.Name</c>; null when the CPU has none.</summary>
        public string MemoryName;
    }

    /// <summary>An offered contract as the OEM view reads it: its key (where the preference is stored) and its type.</summary>
    public struct OemContractInput
    {
        public string Key;
        public int ContractType;
    }

    /// <summary>
    /// What the OEM clients think of a CPU's platform (FR-9 item 4; <c>PlatformInsights.OemOf</c>): in the "per contract" mode how many of the
    /// offered contracts prefer a newer platform than the CPU's and the mean multiplier over them, and the "market" multiplier (c̄ with the
    /// loyalty), which is what the CPU gets when no contract is in the question.
    /// </summary>
    public sealed class OemView
    {
        /// <summary>The setting <c>oem.mode</c> is <c>auto</c>: contracts are looked at one by one.</summary>
        public bool PerContract;

        /// <summary>The CPU belongs to a generation; false means every number below is the neutral one.</summary>
        public bool InGenerations;

        /// <summary>The offered contracts of an included type that have a preference of a known generation.</summary>
        public int Considered;

        /// <summary>Of them, the ones whose preferred generation is newer than the CPU's.</summary>
        public int Lagging;

        /// <summary>The mean chance multiplier over the lagging contracts, loyalty included; 1 when there are none.</summary>
        public float AverageLagging = 1f;

        /// <summary>The "market" multiplier of the CPU (F9b with the loyalty of F9c); 1 for a CPU outside the generations.</summary>
        public float Market = 1f;
    }

    /// <summary>Works out the <see cref="OemView"/> with the very engine the provider of <c>contract.signChance</c> uses, so the window and the game agree.</summary>
    public static class OemViewBuilder
    {
        private static readonly PlatformEffectEngine Engine = new PlatformEffectEngine();

        /// <summary>
        /// The view of one CPU. <paramref name="inputs"/> are the generations on the day, newest first (<see cref="OemAdoptionModel.BuildInputs"/>);
        /// <paramref name="contracts"/> the contracts on offer (may be null). A CPU outside the generations gives the neutral view.
        /// </summary>
        public static OemView Build(EffectCpu cpu, IReadOnlyList<OemContractInput> contracts, OemPreferences prefs, IReadOnlyList<GenAdoptionInput> inputs, PlatformBalance b)
        {
            PlatformBalance balance = b ?? PlatformBalance.Defaults();
            var view = new OemView { PerContract = balance.Oem.Mode == OemMode.Auto, InGenerations = cpu.GenOrder.HasValue };
            if (!cpu.GenOrder.HasValue) return view;

            cpu.IsFastForwarding = false;
            view.Market = Engine.Chance(cpu, default(EffectContract), OemMode.MarketAverage, prefs, inputs, balance).Multiplier;
            if (!view.PerContract || contracts == null) return view;

            double sum = 0;
            for (int i = 0; i < contracts.Count; i++)
            {
                OemContractInput contract = contracts[i];
                if (string.IsNullOrEmpty(contract.Key) || !balance.Oem.IncludesContractType(contract.ContractType)) continue;
                if (prefs == null || string.IsNullOrEmpty(prefs.PreferenceOf(contract.Key))) continue;

                var question = new EffectContract { Present = true, Key = contract.Key, ContractType = contract.ContractType };
                ChanceResult result = Engine.Chance(cpu, question, OemMode.Auto, prefs, inputs, balance);
                view.Considered++;
                if (string.Equals(result.ReasonKey, OemAdoptionModel.ReasonLag, StringComparison.Ordinal))
                {
                    view.Lagging++;
                    sum += result.Multiplier;
                }
            }

            if (view.Lagging > 0) view.AverageLagging = (float)(sum / view.Lagging);
            return view;
        }
    }

    /// <summary>One CPU of the tab "Мои CPU": the lines to draw and the numbers behind them.</summary>
    public sealed class MyCpuRow
    {
        public string Name;
        public string SocketKey;
        public bool InGenerations;
        public bool FullPlatform;
        public RatingBreakdown Rating;
        public OemView Oem;

        /// <summary>The name of the CPU, drawn bold.</summary>
        public string Title;

        /// <summary>"Платформа: LGA 775 / Socket 939, сокет LGA 2000" - the line that carries the tooltip of the risk of changing the socket.</summary>
        public string PlatformLine;

        /// <summary>The rest: the full platform, the three components, the totals, the OEM lines.</summary>
        public readonly List<string> Lines = new List<string>();
    }

    /// <summary>The model of the tab "Мои CPU" (FR-9 item 4, FR-5): the parts of the rating multiplier and the OEM view of every CPU of the player.</summary>
    public static class MyCpusView
    {
        private static readonly PlatformEffectEngine Engine = new PlatformEffectEngine();

        /// <summary>
        /// One row per CPU, in the order given. <paramref name="contracts"/> are the offered contracts (may be null). With the rating switch off the
        /// totals are ×1.00 at once (the components are still shown: they say what the switch is holding back).
        /// </summary>
        public static List<MyCpuRow> Build(IReadOnlyList<MyCpuInput> cpus, string companyKey, PlatformSnapshot s, IReadOnlyList<OemContractInput> contracts, ViewText text)
        {
            var rows = new List<MyCpuRow>();
            if (cpus == null || s == null) return rows;
            if (text == null) text = new ViewText();

            List<GenAdoptionInput> inputs = OemAdoptionModel.BuildInputs(s.Catalog, s.Timeline, s.InstalledBase, s.Today, null);
            for (int i = 0; i < cpus.Count; i++) rows.Add(Row(cpus[i], companyKey, s, contracts, inputs, text));
            return rows;
        }

        private static MyCpuRow Row(MyCpuInput cpu, string companyKey, PlatformSnapshot s, IReadOnlyList<OemContractInput> contracts,
            IReadOnlyList<GenAdoptionInput> inputs, ViewText text)
        {
            PlatformBalance b = s.Balance;
            GenerationDef gen = cpu.Socket.IsValid ? s.Catalog.BySocket(cpu.Socket) : null;
            string socketName = cpu.Socket.IsValid ? text.Socket(cpu.Socket) : text.L("pg.gen.none");

            var row = new MyCpuRow
            {
                Name = cpu.Name,
                SocketKey = cpu.Socket.Key,
                InGenerations = gen != null,
                Title = string.IsNullOrEmpty(cpu.Name) ? text.L("pg.gen.none") : cpu.Name,
                Rating = new RatingBreakdown { ConsumerMult = 1f, IndustriesMult = 1f },
                Oem = new OemView { PerContract = b.Oem.Mode == OemMode.Auto },
            };

            if (gen == null)
            {
                row.PlatformLine = text.L("pg.cpu.noGeneration", socketName);
                return row;
            }

            row.PlatformLine = text.L("pg.cpu.platform", text.Gen(gen), socketName);
            row.FullPlatform = s.Catalog.IsFullPlatform(gen, cpu.MemoryName);
            row.Rating = Engine.RatingFor(companyKey, cpu.Socket, gen.id, s.InstalledBase, s.Timeline, s.Today, b);

            string memory = string.IsNullOrEmpty(cpu.MemoryName) ? text.L("pg.gen.none") : cpu.MemoryName;
            row.Lines.Add(text.L(row.FullPlatform ? "pg.cpu.fullPlatform" : "pg.cpu.notFullPlatform", memory));
            row.Lines.Add(text.L("pg.cpu.components",
                PlatformNumbers.SignedPercent(row.Rating.Bib),
                PlatformNumbers.SignedPercent(row.Rating.Bearly),
                PlatformNumbers.SignedPercent(-row.Rating.Pchurn)));

            if (s.RatingEffects)
                row.Lines.Add(text.L("pg.cpu.total", PlatformNumbers.Mult(row.Rating.ConsumerMult), PlatformNumbers.Mult(row.Rating.IndustriesMult)));
            else
                row.Lines.Add(text.L("pg.cpu.totalOff"));

            var effect = new EffectCpu
            {
                GenOrder = gen.order,
                GenId = gen.id,
                FullPlatform = row.FullPlatform,
                EffShare = row.Rating.EffShare,
            };
            row.Oem = OemViewBuilder.Build(effect, contracts, s.Preferences, inputs, b);

            if (!s.OemEffects)
            {
                row.Lines.Add(text.L("pg.cpu.oem.off"));
                return row;
            }

            if (row.Oem.PerContract)
            {
                if (row.Oem.Considered == 0)
                    row.Lines.Add(text.L("pg.cpu.oem.noContracts"));
                else
                    row.Lines.Add(text.L("pg.cpu.oem.contracts", PlatformNumbers.Int(row.Oem.Lagging), PlatformNumbers.Int(row.Oem.Considered), PlatformNumbers.Mult(row.Oem.AverageLagging)));
            }
            row.Lines.Add(text.L("pg.cpu.oem.market", PlatformNumbers.Mult(row.Oem.Market)));
            return row;
        }
    }

    /// <summary>The risk of changing the socket as the tooltip shows it (FR-9 item 5): the company's main socket, how long it has been supported and what leaving it now would cost.</summary>
    public sealed class SocketRisk
    {
        /// <summary>The company has a main socket (a socket with a base).</summary>
        public bool HasMain;

        public string MainSocketKey;
        public string MainName;

        /// <summary>How long the company has supported the main socket, in months (<c>minSocketLifeMonths</c> when the chronology has no record).</summary>
        public int AgeMonths;

        /// <summary>How many months are left until <c>minSocketLifeMonths</c>; 0 when the age is reached.</summary>
        public int MonthsLeft;

        /// <summary>The strength 0..1 of the penalty if the company moved to an incompatible socket today.</summary>
        public float Severity;

        /// <summary>The penalty in that case, as a fraction of the rating (0.067 = 6.7 %).</summary>
        public float Penalty;

        /// <summary>The header of the tooltip: "Риск смены сокета".</summary>
        public string Header;

        /// <summary>The body of the tooltip.</summary>
        public string Content;
    }

    /// <summary>Works out the <see cref="SocketRisk"/> of a company on the day: age of the main socket against <c>minSocketLifeMonths</c>, the penalty for leaving it now (F5, FR-6 item 2).</summary>
    public static class SocketRiskView
    {
        /// <summary>
        /// The risk for <paramref name="companyKey"/>; <paramref name="hoveredSocketKey"/> is the socket under the pointer (may be null): when it is not
        /// the main one, the body also says how long the company has had it. The penalty assumes the new socket is not compatible with the main one
        /// (the worst case; a socket on the same base is free, FR-6 item 3).
        /// </summary>
        public static SocketRisk Build(string companyKey, string hoveredSocketKey, PlatformSnapshot s, ViewText text)
        {
            if (text == null) text = new ViewText();
            var risk = new SocketRisk { Header = text.L("pg.risk.title") };
            if (s == null || string.IsNullOrEmpty(companyKey))
            {
                risk.Content = text.L("pg.risk.none");
                return risk;
            }

            PlatformBalance.RatingBalance rating = s.Balance.Rating;
            string main = s.InstalledBase.MainSocketOf(companyKey, null);
            if (main == null)
            {
                risk.Content = text.L("pg.risk.none");
                return risk;
            }

            int age = s.Timeline.SocketAgeMonths(companyKey, main, s.Today);
            if (age < 0) age = rating.MinSocketLifeMonths;   // no record: old enough, as the chronology treats it

            double share = s.InstalledBase.Total > 0 ? s.InstalledBase.Of(companyKey, main) / s.InstalledBase.Total : 0;
            float severity = ChurnDetector.Severity(true, age, share, false, s.Balance);

            risk.HasMain = true;
            risk.MainSocketKey = main;
            risk.MainName = text.Socket(s.InstalledBase.SocketOf(main).IsValid ? s.InstalledBase.SocketOf(main) : new SocketId { Key = main });
            risk.AgeMonths = age;
            risk.MonthsLeft = Math.Max(0, rating.MinSocketLifeMonths - age);
            risk.Severity = severity;
            risk.Penalty = rating.ChurnPenalty * severity;

            string ages = PlatformNumbers.Int(age);
            string life = PlatformNumbers.Int(rating.MinSocketLifeMonths);
            if (severity > 0)
            {
                risk.Content = text.L("pg.risk.penalty", risk.MainName, ages, life, PlatformNumbers.Int(risk.MonthsLeft),
                    PlatformNumbers.Percent(risk.Penalty), PlatformNumbers.Int(rating.ChurnDurationMonths));
            }
            else
            {
                risk.Content = text.L("pg.risk.safe", risk.MainName, ages, life);
            }

            if (!string.IsNullOrEmpty(hoveredSocketKey) && !string.Equals(hoveredSocketKey, main, StringComparison.Ordinal))
            {
                int own = s.Timeline.SocketAgeMonths(companyKey, hoveredSocketKey, s.Today);
                if (own >= 0)
                {
                    SocketId hovered = s.InstalledBase.SocketOf(hoveredSocketKey);
                    if (!hovered.IsValid) hovered = new SocketId { Key = hoveredSocketKey };
                    risk.Content += "\n" + text.L("pg.risk.thisSocket", text.Socket(hovered), PlatformNumbers.Int(own));
                }
            }
            return risk;
        }
    }
}
