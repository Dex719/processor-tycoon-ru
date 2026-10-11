using System;
using System.Collections.Generic;
using System.Globalization;

namespace PTMod.Features.GpuProductLine
{
    /// <summary>A characteristic of the design as the designer writes it: the name, the value, its tone and the tooltip with the formula in plain words (NFR-5).</summary>
    public sealed class GpuStatLine
    {
        /// <summary>The short id of the line (<c>perf</c>, <c>temp</c>): the tab keeps its widgets by it.</summary>
        public string Key = string.Empty;

        public string Label = string.Empty;
        public string Value = string.Empty;
        public GpuTone Tone;
        public string TipHeader = string.Empty;
        public string TipBody = string.Empty;
    }

    /// <summary>A warning under the characteristics: "Упор в память", "Перегрев", the reason the design cannot be built.</summary>
    public sealed class GpuWarningLine
    {
        public string Text = string.Empty;
        public GpuTone Tone;
    }

    /// <summary>A row of the table "Оценка по сегментам", written out.</summary>
    public sealed class GpuSegmentRow
    {
        public string SegmentId = string.Empty;
        public string Name = string.Empty;
        public string RatingText = string.Empty;
        public GpuTone RatingTone;
        public string FeaturesText = string.Empty;
        public GpuTone FeaturesTone;
        public string VramText = string.Empty;
        public GpuTone VramTone;
        public string BudgetText = string.Empty;

        /// <summary>The numbers the row was written from.</summary>
        public GpuSegmentEstimate Estimate;
    }

    /// <summary>
    /// The designer of the tab "Конструктор" as data (design.md, "Вкладка «Конструктор»"; FR-3, FR-4, FR-17 item "строка скидки"): what can be chosen (the researched
    /// architectures, memory types and coolers, the process nodes of the company, the buses of the memory), the three sliders (die area, clock as a share of the
    /// highest clock of the architecture, video memory in powers of two), the name, and - recomputed on every change of an input and never otherwise (NFR-1) -
    /// the characteristics, the warnings, the table by segment and the state of the button "Разработать". It holds no widgets: the tab calls the setters from the
    /// events of its controls and shows what the properties give. The characteristics come from <see cref="GpuDesigner"/>, the same evaluation the project is
    /// started from, so the price on the screen is the price of the project.
    /// </summary>
    public sealed class GpuDesignerModel
    {
        /// <summary>The slider of the die moves over at most this many positions.</summary>
        public const int DiePositions = 240;

        /// <summary>The video memory slider offers at most this many powers of two (the largest the memory type allows, then smaller ones).</summary>
        public const int VramPositions = 12;

        /// <summary>The smallest video memory the slider offers, MB.</summary>
        public const float VramFloorMB = 0.0625f;

        /// <summary>A die the content gives no largest size for may be this large.</summary>
        public const float FallbackMaxDie = 1000f;

        /// <summary>The clock a new design starts at, as a share of the highest clock (what the generator of the rivals uses).</summary>
        public const float DefaultClockShare = 0.95f;

        private readonly GpuContentDb _content;
        private readonly GpuResearch _research;
        private readonly GpuDesigner _designer;
        private readonly GpuProjects _projects;
        private readonly GpuMarketService _market;
        private readonly IGpuGame _game;
        private readonly IGpuText _text;

        private List<GpuArchitectureDto> _archs = new List<GpuArchitectureDto>();
        private List<NodeSpec> _nodes = new List<NodeSpec>();
        private List<GpuMemoryDto> _mems = new List<GpuMemoryDto>();
        private List<GpuCoolerDto> _coolers = new List<GpuCoolerDto>();
        private List<float> _vramValues = new List<float>();
        private int _arch, _node, _mem, _cooler, _bus, _vramIndex;
        private float _die;
        private int _clockPercent = 100;
        private string _name = string.Empty;
        private GpuSliderRange _dieRange;
        private int _year;

        private GpuDesignInput _input;
        private GpuStats _stats;
        private GpuDesignContext _context;
        private readonly List<GpuStatLine> _lines = new List<GpuStatLine>();
        private readonly List<GpuWarningLine> _warnings = new List<GpuWarningLine>();
        private readonly List<GpuSegmentRow> _segments = new List<GpuSegmentRow>();
        private string _coDevText = string.Empty;
        private string _developBlock = string.Empty;
        private bool _canDevelop;

        public GpuDesignerModel(GpuContentDb content, GpuResearch research, GpuDesigner designer, GpuProjects projects, GpuMarketService market, IGpuGame game, IGpuText text)
        {
            if (content == null) throw new ArgumentNullException("content");
            if (research == null) throw new ArgumentNullException("research");
            if (designer == null) throw new ArgumentNullException("designer");
            if (projects == null) throw new ArgumentNullException("projects");
            if (market == null) throw new ArgumentNullException("market");
            if (game == null) throw new ArgumentNullException("game");
            if (text == null) throw new ArgumentNullException("text");
            _content = content;
            _research = research;
            _designer = designer;
            _projects = projects;
            _market = market;
            _game = game;
            _text = text;

            LoadParts();
            ChooseDefaults();
            Recompute();
        }

        /// <summary>How many times the characteristics were computed: once at the start and once per change of an input (the tests hold the tab to it).</summary>
        public int Recomputes { get; private set; }

        private GpuBalanceDto.DesignBlock Rules
        {
            get { return (_content.Balance ?? new GpuBalanceDto()).design ?? new GpuBalanceDto.DesignBlock(); }
        }

        // ---------------------------------------------------------------- what can be chosen

        /// <summary>The researched architectures, the earliest first.</summary>
        public IReadOnlyList<GpuArchitectureDto> Archs
        {
            get { return _archs; }
        }

        /// <summary>The process nodes the company can design on, the oldest first.</summary>
        public IReadOnlyList<NodeSpec> Nodes
        {
            get { return _nodes; }
        }

        public IReadOnlyList<GpuMemoryDto> Memories
        {
            get { return _mems; }
        }

        public IReadOnlyList<GpuCoolerDto> Coolers
        {
            get { return _coolers; }
        }

        /// <summary>The bus widths of the chosen memory type, bits.</summary>
        public IReadOnlyList<int> Buses
        {
            get
            {
                GpuMemoryDto mem = CurrentMem;
                return mem != null && mem.busWidths != null ? (IReadOnlyList<int>)mem.busWidths : new int[0];
            }
        }

        /// <summary>The video memory values the slider offers for the chosen memory type, MB, the smallest first.</summary>
        public IReadOnlyList<float> VramValues
        {
            get { return _vramValues; }
        }

        /// <summary>An architecture, a memory type, a cooler and a process node are there to choose from.</summary>
        public bool HasParts
        {
            get { return _archs.Count > 0 && _nodes.Count > 0 && _mems.Count > 0 && _coolers.Count > 0; }
        }

        /// <summary>What is missing, in words (empty when <see cref="HasParts"/>).</summary>
        public string MissingPartsText
        {
            get
            {
                var missing = new List<string>();
                if (_archs.Count == 0) missing.Add(_text.L("gpu.design.missing.arch"));
                if (_nodes.Count == 0) missing.Add(_text.L("gpu.design.missing.node"));
                if (_mems.Count == 0) missing.Add(_text.L("gpu.design.missing.mem"));
                if (_coolers.Count == 0) missing.Add(_text.L("gpu.design.missing.cooler"));
                return missing.Count == 0 ? string.Empty : _text.L("gpu.design.missing", string.Join(", ", missing.ToArray()));
            }
        }

        public List<string> ArchLabels()
        {
            var labels = new List<string>();
            foreach (GpuArchitectureDto arch in _archs) labels.Add(GpuNames.Localized(_text, arch.nameKey, arch.id));
            return labels;
        }

        public List<string> NodeLabels()
        {
            var labels = new List<string>();
            foreach (NodeSpec node in _nodes) labels.Add(node.Name);
            return labels;
        }

        public List<string> MemoryLabels()
        {
            var labels = new List<string>();
            foreach (GpuMemoryDto mem in _mems) labels.Add(GpuNames.Localized(_text, mem.nameKey, mem.id));
            return labels;
        }

        public List<string> CoolerLabels()
        {
            var labels = new List<string>();
            foreach (GpuCoolerDto cooler in _coolers) labels.Add(GpuNames.Localized(_text, cooler.nameKey, cooler.id));
            return labels;
        }

        public List<string> BusLabels()
        {
            var labels = new List<string>();
            foreach (int bits in Buses) labels.Add(_text.L("gpu.design.val.bits", bits));
            return labels;
        }

        public int ArchIndex
        {
            get { return _arch; }
        }

        public int NodeIndex
        {
            get { return _node; }
        }

        public int MemoryIndex
        {
            get { return _mem; }
        }

        public int CoolerIndex
        {
            get { return _cooler; }
        }

        public int BusIndex
        {
            get { return _bus; }
        }

        private GpuArchitectureDto CurrentArch
        {
            get { return _arch >= 0 && _arch < _archs.Count ? _archs[_arch] : null; }
        }

        private GpuMemoryDto CurrentMem
        {
            get { return _mem >= 0 && _mem < _mems.Count ? _mems[_mem] : null; }
        }

        private GpuCoolerDto CurrentCooler
        {
            get { return _cooler >= 0 && _cooler < _coolers.Count ? _coolers[_cooler] : null; }
        }

        /// <summary>A signature of what can be chosen: it changes when a part is researched or the year moves the limits; the tab rebuilds itself when it does.</summary>
        public string PartsSignature
        {
            get { return SignatureOf(_archs, _mems, _coolers, _nodes, _year); }
        }

        /// <summary>The signature of what can be chosen now, without touching the choices: the tab compares it with <see cref="PartsSignature"/> once a day.</summary>
        public string PeekPartsSignature()
        {
            List<GpuArchitectureDto> archs;
            List<GpuMemoryDto> mems;
            List<GpuCoolerDto> coolers;
            List<NodeSpec> nodes;
            Collect(out archs, out mems, out coolers, out nodes);
            return SignatureOf(archs, mems, coolers, nodes, _game.Today.Year);
        }

        private static string SignatureOf(List<GpuArchitectureDto> archs, List<GpuMemoryDto> mems, List<GpuCoolerDto> coolers, List<NodeSpec> nodes, int year)
        {
            var parts = new System.Text.StringBuilder();
            foreach (GpuArchitectureDto arch in archs) parts.Append(arch.id).Append(',');
            parts.Append('|');
            foreach (GpuMemoryDto mem in mems) parts.Append(mem.id).Append(',');
            parts.Append('|');
            foreach (GpuCoolerDto cooler in coolers) parts.Append(cooler.id).Append(',');
            parts.Append('|');
            foreach (NodeSpec node in nodes) parts.Append(node.Name).Append(',');
            parts.Append('|').Append(year);
            return parts.ToString();
        }

        // ---------------------------------------------------------------- the sliders

        /// <summary>The die area slider (mm2): from <c>design.minDie</c> to the largest die of the year.</summary>
        public GpuSliderRange DieRange
        {
            get { return _dieRange; }
        }

        /// <summary>The die area, mm2.</summary>
        public float Die
        {
            get { return _die; }
        }

        /// <summary>The lowest share of the highest clock the slider allows, in percent (<c>design.clockMinShare</c> rounded up).</summary>
        public int ClockMinPercent
        {
            get { return Math.Min(100, Math.Max(1, (int)Math.Ceiling(Rules.clockMinShare * 100f - 1e-4f))); }
        }

        /// <summary>The clock as a share of the highest clock of the architecture, percent.</summary>
        public int ClockPercent
        {
            get { return _clockPercent; }
        }

        /// <summary>The highest clock of the chosen architecture, MHz (F3: the clock of the company over 1000 times the ratio of the architecture).</summary>
        public float MaxClockMHz
        {
            get
            {
                GpuArchitectureDto arch = CurrentArch;
                return arch == null ? 0f : GpuDesignMath.MaxClockMHz(_game.MaxFrequencyKHz(_game.PlayerKey), arch.clockRatio);
            }
        }

        /// <summary>The clock of the design, MHz: the share of the highest clock, in whole megahertz, never above the highest.</summary>
        public float ClockMHz
        {
            get
            {
                float max = MaxClockMHz;
                if (!(max > 0f)) return 0f;
                if (_clockPercent >= 100) return max;
                return Math.Min(max, (float)Math.Round(max * _clockPercent / 100f));
            }
        }

        /// <summary>The video memory of the design, MB.</summary>
        public float VramMB
        {
            get { return _vramIndex >= 0 && _vramIndex < _vramValues.Count ? _vramValues[_vramIndex] : 0f; }
        }

        public int VramIndex
        {
            get { return _vramIndex; }
        }

        /// <summary>The name the player typed (cleaned; empty gives the project a name of its own).</summary>
        public string Name
        {
            get { return _name; }
        }

        public int NameMaxLength
        {
            get { return Math.Max(1, Rules.nameMaxLength); }
        }

        // ---------------------------------------------------------------- the changes (each recomputes once)

        public void SelectArch(int index)
        {
            if (_archs.Count == 0) return;
            _arch = Math.Min(_archs.Count - 1, Math.Max(0, index));
            Recompute();
        }

        public void SelectNode(int index)
        {
            if (_nodes.Count == 0) return;
            _node = Math.Min(_nodes.Count - 1, Math.Max(0, index));
            Recompute();
        }

        /// <summary>A new memory type changes the buses and the video memory it can carry: the bus and the memory are put right.</summary>
        public void SelectMemory(int index)
        {
            if (_mems.Count == 0) return;
            int keepBits = CurrentBusBits;
            float keepVram = VramMB;
            _mem = Math.Min(_mems.Count - 1, Math.Max(0, index));
            PrepareMemory(keepVram, false, keepBits);
            Recompute();
        }

        public void SelectCooler(int index)
        {
            if (_coolers.Count == 0) return;
            _cooler = Math.Min(_coolers.Count - 1, Math.Max(0, index));
            Recompute();
        }

        public void SelectBus(int index)
        {
            int count = Buses.Count;
            if (count == 0) return;
            _bus = Math.Min(count - 1, Math.Max(0, index));
            Recompute();
        }

        public void SetDie(float mm2)
        {
            _die = _dieRange.Snap(mm2);
            Recompute();
        }

        public void SetClockPercent(int percent)
        {
            _clockPercent = Math.Min(100, Math.Max(ClockMinPercent, percent));
            Recompute();
        }

        public void SetVramIndex(int index)
        {
            if (_vramValues.Count == 0) return;
            _vramIndex = Math.Min(_vramValues.Count - 1, Math.Max(0, index));
            Recompute();
        }

        public void SetName(string name)
        {
            _name = GpuState.CleanName(name, NameMaxLength);
            // the name is no input of the characteristics: nothing to recompute
            _input.Name = _name;
        }

        /// <summary>The parts and the limits are read again (a node was researched, a year went by): the choices are kept where they are still possible.</summary>
        public void RefreshParts()
        {
            string arch = CurrentArch != null ? CurrentArch.id : null;
            string node = _node >= 0 && _node < _nodes.Count ? _nodes[_node].Name : null;
            string mem = CurrentMem != null ? CurrentMem.id : null;
            string cooler = CurrentCooler != null ? CurrentCooler.id : null;
            int keepBits = CurrentBusBits;
            float vram = VramMB;

            LoadParts();
            if (!HasParts)
            {
                Recompute();
                return;
            }
            _arch = arch == null ? _archs.Count - 1 : Math.Max(0, _archs.FindIndex(a => a.id == arch));
            _node = node == null ? _nodes.Count - 1 : Math.Max(0, _nodes.FindIndex(n => n.Name == node));
            _mem = mem == null ? _mems.Count - 1 : Math.Max(0, _mems.FindIndex(m => m.id == mem));
            _cooler = cooler == null ? _coolers.Count - 1 : Math.Max(0, _coolers.FindIndex(c => c.id == cooler));
            PrepareMemory(vram, false, keepBits);
            _die = _dieRange.Snap(_die);
            Recompute();
        }

        // ---------------------------------------------------------------- parts and defaults

        private void LoadParts()
        {
            _year = _game.Today.Year;
            Collect(out _archs, out _mems, out _coolers, out _nodes);

            float maxDie = _content.MaxDie(_year);
            if (!(maxDie > 0f)) maxDie = FallbackMaxDie;
            float minDie = Math.Max(1f, Rules.minDie);
            _dieRange = GpuSliderRange.Nice(minDie, Math.Max(minDie, maxDie), DiePositions);
        }

        // The researched parts and the nodes of the company, each list the earliest first.
        private void Collect(out List<GpuArchitectureDto> archs, out List<GpuMemoryDto> mems, out List<GpuCoolerDto> coolers, out List<NodeSpec> nodes)
        {
            archs = new List<GpuArchitectureDto>();
            foreach (GpuArchitectureDto arch in _content.Architectures)
            {
                if (IsResearched(GpuContentDb.BranchArchitecture, arch.id)) archs.Add(arch);
            }
            archs.Sort((a, b) => a.year != b.year ? a.year.CompareTo(b.year) : string.CompareOrdinal(a.id, b.id));

            mems = new List<GpuMemoryDto>();
            foreach (GpuMemoryDto mem in _content.Memories)
            {
                if (IsResearched(GpuContentDb.BranchMemory, mem.id)) mems.Add(mem);
            }
            mems.Sort((a, b) => a.year != b.year ? a.year.CompareTo(b.year) : string.CompareOrdinal(a.id, b.id));

            coolers = new List<GpuCoolerDto>();
            foreach (GpuCoolerDto cooler in _content.Coolers)
            {
                if (IsResearched(GpuContentDb.BranchCooling, cooler.id)) coolers.Add(cooler);
            }
            coolers.Sort((a, b) => a.year != b.year ? a.year.CompareTo(b.year) : string.CompareOrdinal(a.id, b.id));

            nodes = new List<NodeSpec>(_game.PlayerNodes());
            nodes.Sort((a, b) => a.Density != b.Density ? a.Density.CompareTo(b.Density) : string.CompareOrdinal(a.Name, b.Name));
        }

        // A part is there to design with when a node that opens it is finished.
        private bool IsResearched(string branch, string partId)
        {
            foreach (GpuResearchNodeDto node in _content.ResearchNodes)
            {
                if (node.branch == branch && node.unlocks == partId && _research.IsResearched(node.id)) return true;
            }
            return false;
        }

        private void ChooseDefaults()
        {
            _arch = Math.Max(0, _archs.Count - 1);
            _node = Math.Max(0, _nodes.Count - 1);
            _mem = Math.Max(0, _mems.Count - 1);
            _cooler = Math.Max(0, _coolers.Count - 1);
            _clockPercent = Math.Max(ClockMinPercent, (int)Math.Round(DefaultClockShare * 100f));
            _die = _dieRange.Snap(_dieRange.Max * FirstSegmentDieShare());
            PrepareMemory(FirstSegmentVramMB(), true, 0);
        }

        // The die a new design starts with: the share of the largest die the rivals of the reference segment use (0.5 for the shipped consumer segment of the single mode).
        private float FirstSegmentDieShare()
        {
            GpuSegmentDto reference = _market.ReferenceSegmentAt(_game.Today);
            return reference != null && reference.rivalDieShare > 0f ? Math.Min(1f, reference.rivalDieShare) : 0.5f;
        }

        // The video memory a new design starts with: what the reference segment asks for today.
        private float FirstSegmentVramMB()
        {
            GpuSegmentDto reference = _market.ReferenceSegmentAt(_game.Today);
            if (reference == null) return 0f;
            foreach (GpuSegmentView view in _market.OpenSegments)
            {
                if (view.Id == reference.id) return view.VramRequirementMB;
            }
            return 0f;
        }

        private int CurrentBusBits
        {
            get
            {
                IReadOnlyList<int> buses = Buses;
                return _bus >= 0 && _bus < buses.Count ? buses[_bus] : 0;
            }
        }

        // The video memory the chosen memory type can carry: the value nearest to what was wanted (at least that much for a new design, so the first card meets the
        // requirement of the year), and the bus the new type has that is nearest to the old one - the middle one when there is none.
        private void PrepareMemory(float wantedMB, bool atLeast, int keepBits)
        {
            GpuMemoryDto mem = CurrentMem;
            _vramValues = mem != null ? GpuUiMath.PowersOfTwo(mem.maxVramMB, VramPositions, VramFloorMB) : new List<float>();
            if (_vramValues.Count == 0) _vramIndex = 0;
            else if (!(wantedMB > 0f)) _vramIndex = _vramValues.Count - 1;
            else _vramIndex = atLeast ? GpuUiMath.FirstAtLeast(_vramValues, wantedMB) : GpuUiMath.NearestIndex(_vramValues, wantedMB);

            IReadOnlyList<int> buses = Buses;
            int at = -1;
            for (int i = 0; i < buses.Count; i++)
            {
                if (buses[i] == keepBits) at = i;
            }
            _bus = at >= 0 ? at : (buses.Count > 0 ? buses.Count / 2 : 0);
        }

        // ---------------------------------------------------------------- the evaluation

        /// <summary>What the player chose, as the designer and the project take it.</summary>
        public GpuDesignInput Input
        {
            get { return _input; }
        }

        /// <summary>The characteristics of the design (F1-F13); every number is finite, also when the design is not valid.</summary>
        public GpuStats Stats
        {
            get { return _stats; }
        }

        /// <summary>The context of the evaluation (the wafer, the yield, the modifiers).</summary>
        public GpuDesignContext Context
        {
            get { return _context; }
        }

        public IReadOnlyList<GpuStatLine> Lines
        {
            get { return _lines; }
        }

        public IReadOnlyList<GpuWarningLine> Warnings
        {
            get { return _warnings; }
        }

        public IReadOnlyList<GpuSegmentRow> Segments
        {
            get { return _segments; }
        }

        /// <summary>"Совместная разработка: −15 %" when the discount of the co-development acts, otherwise empty (FR-17).</summary>
        public string CoDevText
        {
            get { return _coDevText; }
        }

        /// <summary>The button "Разработать" works.</summary>
        public bool CanDevelop
        {
            get { return _canDevelop; }
        }

        /// <summary>Why the button does not work (empty when it does).</summary>
        public string DevelopBlockText
        {
            get { return _developBlock; }
        }

        /// <summary>Recomputes everything the screen shows from the inputs. Called by the setters; the tab never calls it on a timer.</summary>
        public void Recompute()
        {
            Recomputes++;
            _lines.Clear();
            _warnings.Clear();
            _segments.Clear();
            _coDevText = string.Empty;
            _input = BuildInput();

            if (!HasParts)
            {
                _stats = new GpuStats { Features = new string[0], BandwidthRatio = 1f };
                _context = default(GpuDesignContext);
                _canDevelop = false;
                _developBlock = MissingPartsText;
                return;
            }

            GpuDesignContext context = _designer.ContextFor(_input);
            _stats = _designer.Evaluate(_input, context);
            _context = context;

            BuildLines();
            BuildWarnings();
            if (context.CoDevFactor > 0f && context.CoDevFactor < 0.9999f)
                _coDevText = _text.L("gpu.design.codev", PercentText(1f - context.CoDevFactor));

            BuildSegments();
            BuildDevelopState();
        }

        private GpuDesignInput BuildInput()
        {
            GpuArchitectureDto arch = CurrentArch;
            GpuMemoryDto mem = CurrentMem;
            GpuCoolerDto cooler = CurrentCooler;
            IReadOnlyList<int> buses = Buses;
            return new GpuDesignInput
            {
                ArchId = arch != null ? arch.id : string.Empty,
                MemId = mem != null ? mem.id : string.Empty,
                CoolerId = cooler != null ? cooler.id : string.Empty,
                NodeName = _node >= 0 && _node < _nodes.Count ? _nodes[_node].Name : string.Empty,
                Name = _name,
                DieMm2 = _die,
                ClockMHz = ClockMHz,
                VramMB = VramMB,
                BusWidthBits = _bus >= 0 && _bus < buses.Count ? buses[_bus] : 0,
            };
        }

        // A whole-number percentage with the sign of the game ("12 %").
        private string PercentText(float fraction)
        {
            return Math.Round(fraction * 100f).ToString("0", CultureInfo.InvariantCulture) + " %";
        }

        private void BuildLines()
        {
            GpuStats s = _stats;
            GpuArchitectureDto arch = CurrentArch;
            GpuMemoryDto mem = CurrentMem;
            GpuBalanceDto.DesignBlock rules = Rules;
            float companyMhz = _game.MaxFrequencyKHz(_game.PlayerKey) / 1000f;
            bool hot = s.TempC > rules.safeTempC;
            float loss = GpuUiMath.BandwidthLoss(s.BandwidthRatio, rules.bandwidthRatioMin, rules.bandwidthExponent);

            Add("transistors", _text.Compact(s.Transistors), GpuTone.Normal);
            Add("units", _text.Integer(s.Units), GpuTone.Normal, _text.Compact(arch != null ? arch.transistorsPerUnit : 0f));
            Add("maxClock", _text.Frequency(s.MaxClockMHz), GpuTone.Normal, arch != null ? PercentText(arch.clockRatio) : string.Empty, _text.Frequency(companyMhz));
            Add("compute", _text.L("gpu.design.val.mops", _text.Integer(s.ComputeMops)), GpuTone.Normal, s.Units, _text.Frequency(_input.ClockMHz), arch != null ? GpuUiMath.Plain(arch.opsPerClock) : "0");
            Add("bandwidth", _text.L("gpu.design.val.mbs", _text.Integer(s.BandwidthMBs)), loss > 0.0049f ? GpuTone.Warn : GpuTone.Normal,
                _input.BusWidthBits, mem != null ? GpuUiMath.Plain(mem.dataRateMTs) : "0", GpuUiMath.Plain((float)Math.Round(s.BandwidthRatio, 2)));
            Add("perf", _text.L("gpu.design.val.mops", _text.Integer(s.EffectivePerf)), loss > 0.0049f ? GpuTone.Warn : GpuTone.Normal,
                GpuUiMath.Plain((float)Math.Round(s.BandwidthRatio, 2)), PercentText(loss), GpuUiMath.Plain(rules.bandwidthExponent));
            Add("power", _text.L("gpu.design.val.watts", _text.Number(s.PowerW, string.Empty)), GpuTone.Normal);
            Add("temp", _text.L("gpu.design.val.celsius", _text.Number(s.TempC, string.Empty)), hot ? GpuTone.Bad : GpuTone.Normal,
                GpuUiMath.Plain(rules.ambientC), GpuUiMath.Plain(rules.safeTempC));
            Add("failure", _text.Percent(s.FailureRate), hot ? GpuTone.Bad : GpuTone.Normal);
            Add("dies", _text.L("gpu.design.val.dies", s.DpwYield, s.DpwFull), s.DpwFull < 1 ? GpuTone.Bad : GpuTone.Normal,
                GpuUiMath.Plain(_context.WaferDiameter), PercentText(_context.Yield));
            Add("unitCost", _text.Money(s.UnitCost), GpuTone.Normal);
            Add("projectCost", _text.Money(s.ProjectCost), GpuTone.Normal);
            Add("projectDays", GpuUiMath.Span(_text, s.ProjectDays), GpuTone.Normal);
        }

        private void Add(string key, string value, GpuTone tone, params object[] tipArgs)
        {
            _lines.Add(new GpuStatLine
            {
                Key = key,
                Label = _text.L("gpu.design.stat." + key),
                Value = value,
                Tone = tone,
                TipHeader = _text.L("gpu.design.stat." + key),
                TipBody = _text.L("gpu.design.stat." + key + ".tip", tipArgs),
            });
        }

        private void BuildWarnings()
        {
            GpuStats s = _stats;
            GpuBalanceDto.DesignBlock rules = Rules;
            if (!s.Valid && !string.IsNullOrEmpty(s.InvalidReasonKey))
                _warnings.Add(new GpuWarningLine { Text = _text.L(s.InvalidReasonKey), Tone = GpuTone.Bad });

            float loss = GpuUiMath.BandwidthLoss(s.BandwidthRatio, rules.bandwidthRatioMin, rules.bandwidthExponent);
            if (Math.Round(loss * 100f) >= 1d)
                _warnings.Add(new GpuWarningLine { Text = _text.L("gpu.design.warn.bandwidth", Math.Round(loss * 100f).ToString("0", CultureInfo.InvariantCulture)), Tone = GpuTone.Warn });

            if (s.TempC > rules.safeTempC)
                _warnings.Add(new GpuWarningLine { Text = _text.L("gpu.design.warn.temp"), Tone = GpuTone.Bad });
        }

        private void BuildSegments()
        {
            List<GpuSegmentEstimate> estimates = GpuSegmentEstimator.Estimate(_content, _market, _game, _stats, VramMB, _text);
            foreach (GpuSegmentEstimate e in estimates)
            {
                var row = new GpuSegmentRow { SegmentId = e.SegmentId, Name = e.Name, Estimate = e, BudgetText = _text.Money(e.Budget) };

                if (!_stats.Valid || e.RatingPercent <= 0f)
                {
                    row.RatingText = _text.L("gpu.design.seg.noBuyers");
                    row.RatingTone = GpuTone.Bad;
                }
                else if (!e.HasCompetitors)
                {
                    row.RatingText = _text.L("gpu.design.seg.alone");
                    row.RatingTone = GpuTone.Good;
                }
                else
                {
                    row.RatingText = Math.Round(e.RatingPercent).ToString("0", CultureInfo.InvariantCulture) + " %";
                    row.RatingTone = e.RatingPercent >= 100f ? GpuTone.Good : e.RatingPercent >= 60f ? GpuTone.Normal : GpuTone.Warn;
                }

                if (e.MissingFeatures.Length == 0)
                {
                    row.FeaturesText = _text.L("gpu.design.seg.featuresOk");
                    row.FeaturesTone = GpuTone.Normal;
                }
                else
                {
                    row.FeaturesText = _text.L("gpu.design.seg.featuresMissing", GpuNames.Features(_text, e.MissingFeatures), GpuUiMath.Plain((float)Math.Round(e.FeatureFactor, 2)));
                    row.FeaturesTone = GpuTone.Bad;
                }

                if (e.VramFactor >= 0.9999f)
                {
                    row.VramText = e.VramRequirementMB > 0f
                        ? _text.L("gpu.design.seg.vramOk", _text.L("gpu.design.val.mb", GpuUiMath.Plain(e.VramRequirementMB)))
                        : _text.L("gpu.design.seg.vramNone");
                    row.VramTone = GpuTone.Normal;
                }
                else
                {
                    row.VramText = _text.L("gpu.design.seg.vramShort", GpuUiMath.Plain((float)Math.Round(e.VramFactor, 2)), _text.L("gpu.design.val.mb", GpuUiMath.Plain(e.VramRequirementMB)));
                    row.VramTone = GpuTone.Warn;
                }
                _segments.Add(row);
            }
        }

        /// <summary>The button "Разработать" is looked at again without computing the design: a project of the player finished or was cancelled since.</summary>
        public void RefreshDevelopState()
        {
            if (HasParts) BuildDevelopState();
        }

        private void BuildDevelopState()
        {
            string reasonKey;
            if (!_stats.Valid)
            {
                _canDevelop = false;
                _developBlock = _text.L(_stats.InvalidReasonKey ?? GpuDesignMath.ReasonNumbers);
                return;
            }
            if (!_projects.CanStart(out reasonKey))
            {
                _canDevelop = false;
                _developBlock = _text.L(reasonKey);
                return;
            }
            _canDevelop = true;
            _developBlock = string.Empty;
        }

        // ---------------------------------------------------------------- developing

        /// <summary>
        /// FR-3 item 7: starts the project of the design (the snapshot of the inputs and of the characteristics is kept by <see cref="GpuProjects.Start"/>). The id of the
        /// project, or null with the reason in words. The screen is recomputed afterwards: the limit of projects may now be reached.
        /// </summary>
        public string Develop(out string reasonText)
        {
            reasonText = string.Empty;
            string reasonKey;
            string id = _projects.Start(_input, out reasonKey);
            if (id == null)
            {
                reasonText = string.IsNullOrEmpty(reasonKey) ? string.Empty : _text.L(reasonKey);
                BuildDevelopState();
                return null;
            }
            BuildDevelopState();
            return id;
        }
    }
}
