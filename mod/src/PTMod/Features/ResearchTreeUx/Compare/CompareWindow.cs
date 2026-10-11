using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PTMod.Core;
using TMPro;
using ButtonUI = ProcessorTycoon.UI.ButtonUI;
using DropdownUI = ProcessorTycoon.UI.DropdownUI;
using GameCompany = ProcessorTycoon.CompanySystem.ICompany;
using GameCpu = ProcessorTycoon.Hardware.Cpu;
using UnityRectTransform = UnityEngine.RectTransform;
using UnityVector2 = UnityEngine.Vector2;

namespace PTMod.Features.ResearchTreeUx
{
    /// <summary>
    /// The window "CPU comparison" (FR-6, design: Component 8): two processors side by side, the column of differences and the better side
    /// painted with the colours of the game. A window of the mod that does not stop the game (a clone of the game's panel window); it is opened
    /// by the button "Compare" next to "Detailed / Simplified" in the inspector (A is the processor the inspector shows) and from the page of the
    /// feature in the hub "Mods" (A is the player's latest processor).
    /// <list type="bullet">
    /// <item><b>The choice.</b> Each side is a pair of dropdowns, "Company" and "CPU" (the first entry of the second is "—", no processor). The
    /// companies are those the inspector lists: the player and the AI companies that are not foundries, minus the bankrupt ones. Choosing a
    /// company picks its newest processor. "Swap" swaps the sides (item 6).</item>
    /// <item><b>What is shown.</b> The table of <see cref="CompareModel"/> from <see cref="CpuFactsReader"/>, which reads only what the inspector
    /// and the market windows show for any company (item 10), plus the rows other features registered (item 8). The colour goes to the better side
    /// of a row with a direction when the difference reaches the threshold (item 4).</item>
    /// <item><b>What is kept.</b> Only the key of a company and the <c>SaveID</c> of a processor (<see cref="CompareSelection"/>), never an object of
    /// the game: every redraw looks the pair up again. A company that went bankrupt, or a processor the game dropped, resets its side to "—" when
    /// the window is opened or redrawn, without an exception (item 7, AC-6.3).</item>
    /// <item><b>When it is redrawn.</b> On opening, on a pick, on "Swap", every game day while it is open, and when the language or the theme changes.</item>
    /// </list>
    /// The dropdowns and the rows of the table are made once and reused. Main thread only. Every entry point is called under a guard of the feature.
    /// </summary>
    internal sealed class CompareWindow
    {
        private const string WindowId = ResearchTreeUxFeature.FeatureId + ".compare";
        private const float BodyHeight = 400f;
        private const float BodyMinHeight = 160f;
        private const int MaxNameLength = 24;
        private const string Ellipsis = "…";

        // The columns of the table as tab stops, in percent of the width: label, CPU A, CPU B, difference.
        private static readonly float[] ColumnPositions = { 0f, 34f, 58f, 82f };
        private static readonly UnityVector2 WindowSize = new UnityVector2(880f, 720f);

        private sealed class CompanyEntry
        {
            public string Key;
            public string Name;
        }

        // The widgets and the choice of one side of the comparison.
        private sealed class Side
        {
            public bool IsA;
            public string CompanyKey;
            public TMP_Text Caption;
            public DropdownUI CompanyDropdown;
            public DropdownUI CpuDropdown;
            public List<GameCpu> Cpus = new List<GameCpu>();
        }

        private readonly ResearchTreeUxFeature _feature;
        private readonly CompareSelection _selection = new CompareSelection();
        private readonly List<CompanyEntry> _companies = new List<CompanyEntry>();
        private readonly List<TMP_Text> _rows = new List<TMP_Text>();
        private readonly Side _a = new Side { IsA = true };
        private readonly Side _b = new Side { IsA = false };

        private ModWindow _window;
        private UnityRectTransform _body;
        private TMP_Text _header;
        private ButtonUI _swapButton;
        private bool _subscribed;
        private bool _destroyed;
        private bool _widgetsBroken;

        public CompareWindow(ResearchTreeUxFeature feature)
        {
            _feature = feature;
        }

        /// <summary>The window is on the screen.</summary>
        public bool IsOpen
        {
            get { return !_destroyed && _window != null && _window.IsAlive && _window.IsOpen; }
        }

        /// <summary>The choice of the two sides; for tests of the feature's glue and for the log.</summary>
        public CompareSelection Selection
        {
            get { return _selection; }
        }

        /// <summary>
        /// Opens the window with <paramref name="a"/> on the left and <paramref name="b"/> on the right (both may be null: that side is "—"). The
        /// window is made on first use. Does nothing when the feature or the part is off or the templates of the game are not looked up yet.
        /// </summary>
        public static void Open(GameCpu a, GameCpu b = null)
        {
            ResearchTreeUxFeature feature = ResearchTreeUxFeature.Instance;
            if (feature != null) feature.OpenCompare(a, b);
        }

        /// <summary>Opens this window on a pair of processors (see the static <see cref="Open(GameCpu, GameCpu)"/>).</summary>
        public void Show(GameCpu a, GameCpu b)
        {
            if (_destroyed || !EnsureWindow()) return;

            Choose(_a, a);
            Choose(_b, b);
            RefreshAll();
            _window.Open();
        }

        /// <summary>"Swap": A becomes B and B becomes A (FR-6 item 6).</summary>
        public void Swap()
        {
            if (!IsOpen && _window == null) return;

            string key = _a.CompanyKey;
            _a.CompanyKey = _b.CompanyKey;
            _b.CompanyKey = key;
            _selection.Swap();
            RefreshAll();
        }

        /// <summary>Reads the game again and redraws everything; does nothing while the window is shut.</summary>
        public void Refresh()
        {
            if (IsOpen) RefreshAll();
        }

        /// <summary>Closes the window when it is open.</summary>
        public void Close()
        {
            if (_window != null && _window.IsAlive) _window.Close();
        }

        /// <summary>The language changed: the captions, the options and the table are written again.</summary>
        public void OnLanguageChanged()
        {
            if (!IsOpen) return;
            RefreshCaptions();
            RefreshAll();
        }

        /// <summary>The theme changed: the colours of the better side are read again.</summary>
        public void OnThemeChanged()
        {
            if (IsOpen) WriteTable();
        }

        /// <summary>The feature is switched off or the scene is left: the window is closed and every reference dropped (the window stays in its scene).</summary>
        public void Destroy()
        {
            _destroyed = true;
            if (_subscribed)
            {
                ModL10n.LanguageChanged -= OnLanguageChanged;
                ModUI.ThemeChanged -= OnThemeChanged;
                _subscribed = false;
            }

            Close();
            _rows.Clear();
            _companies.Clear();
            _body = null;
            _header = null;
            _swapButton = null;
            _window = null;
            Forget(_a);
            Forget(_b);
            _selection.ClearA();
            _selection.ClearB();
        }

        // ---- the window ----

        private bool EnsureWindow()
        {
            if (_widgetsBroken) return false;
            if (_window != null && _window.IsAlive && _body != null) return true;

            _window = null;
            _body = null;
            _rows.Clear();
            Forget(_a);
            Forget(_b);

            if (!ModUI.IsReady)
            {
                ModLog.Warn(ResearchTreeUxFeature.FeatureId, "compare: the window cannot be made yet, the interface templates of the game are not looked up");
                return false;
            }

            ModWindow made = ModUI.CreateWindow(WindowId, WindowTemplate.Panel, "rtux.compare.title", WindowSize);
            if (made == null) return false;   // ModUI has logged why

            try
            {
                UxWidgets.Clear(made.Content);   // the widgets of an earlier life of the window (the feature was off and on)
                BuildControls(made.Content);

                _window = made;
                if (!_subscribed)
                {
                    ModL10n.LanguageChanged += OnLanguageChanged;
                    ModUI.ThemeChanged += OnThemeChanged;
                    _subscribed = true;
                }
                return true;
            }
            catch (Exception e)
            {
                _widgetsBroken = true;
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "compare: cannot build the window", e);
                UxWidgets.Clear(made.Content);
                return false;
            }
        }

        private void BuildControls(UnityRectTransform content)
        {
            UnityRectTransform selectors = UxWidgets.Row(content, "PTMod_RTUX_Selectors", 16f);
            BuildSide(_a, UxWidgets.Column(selectors, "PTMod_RTUX_SideA", 4f, 1f), "rtux.compare.cpuA");
            BuildSide(_b, UxWidgets.Column(selectors, "PTMod_RTUX_SideB", 4f, 1f), "rtux.compare.cpuB");

            _swapButton = ModUI.Button(content, ModL10n.L("rtux.compare.swap"), Swap);
            if (_swapButton == null) throw new InvalidOperationException("the button template is missing");

            _header = ModUI.Text(content, string.Empty);
            _body = UxWidgets.ScrollBody(content, BodyHeight, BodyMinHeight);
            if (_header == null || _body == null) throw new InvalidOperationException("the table could not be made");
            StyleRow(_header);
            UxWidgets.Clear(_body);
        }

        private void BuildSide(Side side, UnityRectTransform column, string captionKey)
        {
            side.Caption = ModUI.Text(column, ModL10n.L(captionKey), true);
            side.CompanyDropdown = ModUI.Dropdown(column, ModL10n.L("rtux.compare.company"), new List<string>(), 0, index => OnCompanyPicked(side, index));
            side.CpuDropdown = ModUI.Dropdown(column, ModL10n.L("rtux.compare.cpu"), new List<string>(), 0, index => OnCpuPicked(side, index));
            if (side.Caption == null || side.CompanyDropdown == null || side.CpuDropdown == null)
                throw new InvalidOperationException("a dropdown template is missing");
        }

        private void RefreshCaptions()
        {
            if (_a.Caption != null) _a.Caption.text = ModL10n.L("rtux.compare.cpuA");
            if (_b.Caption != null) _b.Caption.text = ModL10n.L("rtux.compare.cpuB");
        }

        private static void Forget(Side side)
        {
            side.Caption = null;
            side.CompanyDropdown = null;
            side.CpuDropdown = null;
            side.Cpus.Clear();
        }

        // ---- the choice ----

        // The processor the caller passed becomes a side: its company and its SaveID. A null one leaves the side without a processor.
        private void Choose(Side side, GameCpu cpu)
        {
            string key = cpu == null ? null : ModCompanies.KeyOf(cpu.Company);
            if (key == null)
            {
                SetCpu(side, CpuRef.None);
                return;
            }

            side.CompanyKey = key;
            SetCpu(side, new CpuRef(key, cpu.SaveID));
        }

        private void SetCpu(Side side, CpuRef cpu)
        {
            if (side.IsA) _selection.SetA(cpu.CompanyKey, cpu.SaveId);
            else _selection.SetB(cpu.CompanyKey, cpu.SaveId);
        }

        private CpuRef RefOf(Side side)
        {
            return side.IsA ? _selection.A : _selection.B;
        }

        private void OnCompanyPicked(Side side, int index)
        {
            if (_destroyed || index < 0 || index >= _companies.Count) return;

            side.CompanyKey = _companies[index].Key;
            List<GameCpu> list = CpuFactsReader.CpusOf(ModCompanies.Resolve(side.CompanyKey));
            SetCpu(side, list.Count > 0 ? new CpuRef(side.CompanyKey, list[0].SaveID) : CpuRef.None);   // the newest of the company, as the inspector does

            FillCpuDropdown(side);
            WriteTable();
        }

        private void OnCpuPicked(Side side, int index)
        {
            if (_destroyed) return;

            if (index <= 0 || index > side.Cpus.Count) SetCpu(side, CpuRef.None);
            else SetCpu(side, new CpuRef(side.CompanyKey, side.Cpus[index - 1].SaveID));
            WriteTable();
        }

        // ---- redrawing ----

        private void RefreshAll()
        {
            if (_destroyed || _window == null) return;

            ReadCompanies();
            bool reset = _selection.Validate(CompanyAvailable, CpuAvailable);
            if (reset) ModLog.Debug(ResearchTreeUxFeature.FeatureId, "compare: a company or a processor is gone, its side is reset");

            foreach (Side side in new[] { _a, _b })
            {
                if (!HasCompany(side.CompanyKey)) side.CompanyKey = DefaultCompanyKey(side);
                FillCompanyDropdown(side);
                FillCpuDropdown(side);
            }
            WriteTable();
        }

        // The player first, then the AI companies; no foundries (the inspector leaves them out: they design nothing) and no bankrupt ones.
        private void ReadCompanies()
        {
            _companies.Clear();
            IReadOnlyList<GameCompany> all = ModCompanies.All;
            for (int i = 0; i < all.Count; i++)
            {
                GameCompany company = all[i];
                if (ReferenceEquals(company, null)) continue;

                try
                {
                    if (company.IsBankrupt || company.IsFoundry) continue;

                    string key = ModCompanies.KeyOf(company);
                    if (key == null) continue;
                    _companies.Add(new CompanyEntry { Key = key, Name = company.Name ?? key });
                }
                catch (Exception e)
                {
                    ModLog.Debug(ResearchTreeUxFeature.FeatureId, "compare: a company cannot be listed (" + e.GetType().Name + ")");
                }
            }
        }

        private bool HasCompany(string key)
        {
            return IndexOfCompany(key) >= 0;
        }

        private int IndexOfCompany(string key)
        {
            if (key == null) return -1;
            for (int i = 0; i < _companies.Count; i++)
            {
                if (string.Equals(_companies[i].Key, key, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        // A is the player; B is the first rival, so the first view already has two different companies to choose from.
        private string DefaultCompanyKey(Side side)
        {
            if (_companies.Count == 0) return null;
            if (side.IsA || _companies.Count == 1) return _companies[0].Key;
            return _companies[1].Key;
        }

        private bool CompanyAvailable(string key)
        {
            if (IndexOfCompany(key) < 0) return false;

            GameCompany company = ModCompanies.Resolve(key);
            return !ReferenceEquals(company, null) && !company.IsBankrupt;
        }

        private bool CpuAvailable(string companyKey, int saveId)
        {
            return FindCpu(companyKey, saveId) != null;
        }

        private static GameCpu FindCpu(string companyKey, int saveId)
        {
            foreach (GameCpu cpu in CpuFactsReader.CpusOf(ModCompanies.Resolve(companyKey)))
            {
                if (cpu.SaveID == saveId) return cpu;
            }
            return null;
        }

        private GameCpu ResolveCpu(CpuRef reference)
        {
            return reference.IsSet ? FindCpu(reference.CompanyKey, reference.SaveId) : null;
        }

        // ---- the dropdowns ----

        private void FillCompanyDropdown(Side side)
        {
            var names = new List<string>(_companies.Count);
            foreach (CompanyEntry entry in _companies) names.Add(entry.Name);
            SetOptions(side.CompanyDropdown, names, Math.Max(0, IndexOfCompany(side.CompanyKey)));
        }

        private void FillCpuDropdown(Side side)
        {
            side.Cpus = CpuFactsReader.CpusOf(ModCompanies.Resolve(side.CompanyKey));

            var names = new List<string>(side.Cpus.Count + 1) { side.Cpus.Count == 0 ? ModL10n.L("rtux.compare.noCpu") : CompareModel.Dash };
            int chosen = 0;
            CpuRef reference = RefOf(side);
            for (int i = 0; i < side.Cpus.Count; i++)
            {
                names.Add(side.Cpus[i].Name ?? "?");
                if (reference.IsSet && side.Cpus[i].SaveID == reference.SaveId) chosen = i + 1;
            }
            SetOptions(side.CpuDropdown, names, chosen);
        }

        // The options and the pick are written without calling the callback of the dropdown (the pick of the player is the only call).
        private static void SetOptions(DropdownUI dropdown, List<string> names, int index)
        {
            if (dropdown == null) return;

            TMP_Dropdown inner = dropdown.GetComponent<TMP_Dropdown>();
            if (inner == null) return;

            inner.ClearOptions();
            inner.AddOptions(names);
            inner.SetValueWithoutNotify(names.Count == 0 ? 0 : Math.Max(0, Math.Min(index, names.Count - 1)));
            inner.RefreshShownValue();
        }

        // ---- the table ----

        private void WriteTable()
        {
            if (_destroyed || _body == null || _header == null) return;

            long started = PerfProbe.Begin();
            GameCpu cpuA = ResolveCpu(_selection.A);
            GameCpu cpuB = ResolveCpu(_selection.B);

            List<CompareLine> lines;
            CpuFacts factsA = null;
            CpuFacts factsB = null;
            try
            {
                factsA = CpuFactsReader.Read(cpuA);
                factsB = CpuFactsReader.Read(cpuB);
                lines = CompareModel.Build(factsA, factsB, _feature.CompareConfig, GameFormatter.Instance, _feature.CompareLinesFor(cpuA, cpuB));
            }
            catch (Exception e)
            {
                ModLog.Error(ResearchTreeUxFeature.FeatureId, "compare: the table cannot be built", e);
                lines = null;
            }

            int used = 0;
            _header.text = HeaderText(factsA, factsB);
            if (lines == null)
            {
                Put(used++, ModL10n.L("rtux.compare.error"));
            }
            else
            {
                if (!_selection.B.IsSet) Put(used++, ModL10n.L("rtux.compare.hint"));

                string good = UxWidgets.PositiveHex();
                string bad = UxWidgets.NegativeHex();
                foreach (CompareLine line in lines) Put(used++, RowText(line, good, bad));
            }

            for (int i = used; i < _rows.Count; i++)
            {
                if (_rows[i] != null && _rows[i].gameObject.activeSelf) _rows[i].gameObject.SetActive(false);
            }

            if (started != 0L) PerfProbe.End(PerfOp.CompareTable, started, "compare: table written (" + used + " rows)");
        }

        private static string HeaderText(CpuFacts a, CpuFacts b)
        {
            return Columns(
                "<b>" + ModL10n.L("rtux.compare.column.param") + "</b>",
                "<b>" + Shorten(a == null ? CompareModel.Dash : a.Name) + "</b>",
                "<b>" + Shorten(b == null ? CompareModel.Dash : b.Name) + "</b>",
                "<b>" + ModL10n.L("rtux.compare.column.delta") + "</b>");
        }

        // One row: the label, the two values (the better one in the colour of good news, the worse one in the colour of bad news), the difference.
        private static string RowText(CompareLine line, string good, string bad)
        {
            string label = ModL10n.Has(line.LabelKey) ? ModL10n.L(line.LabelKey) : line.LabelKey;
            string valueA = line.ValueA;
            string valueB = line.ValueB;
            string delta = line.DeltaText;

            if (line.Winner < 0)
            {
                valueA = UxWidgets.Colored(valueA, good);
                valueB = UxWidgets.Colored(valueB, bad);
                delta = UxWidgets.Colored(delta, bad);   // B is behind
            }
            else if (line.Winner > 0)
            {
                valueA = UxWidgets.Colored(valueA, bad);
                valueB = UxWidgets.Colored(valueB, good);
                delta = UxWidgets.Colored(delta, good);
            }
            return Columns(label, valueA, valueB, delta);
        }

        private static string Columns(params string[] cells)
        {
            var text = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) text.Append("<pos=").Append(ColumnPositions[i].ToString("0.#", CultureInfo.InvariantCulture)).Append("%>");
                text.Append(cells[i] ?? string.Empty);
            }
            return text.ToString();
        }

        private static string Shorten(string name)
        {
            if (string.IsNullOrEmpty(name)) return CompareModel.Dash;
            return name.Length <= MaxNameLength ? name : name.Substring(0, MaxNameLength - 1) + Ellipsis;
        }

        // The text at a position of the body: made when the list is shorter, switched on when it was hidden, written when it differs.
        private void Put(int index, string text)
        {
            TMP_Text target;
            if (index < _rows.Count)
            {
                target = _rows[index];
            }
            else
            {
                target = ModUI.Text(_body, text);
                if (target == null) return;   // ModUI has logged why
                StyleRow(target);
                _rows.Add(target);
            }

            if (target == null) return;   // destroyed behind our back
            if (!target.gameObject.activeSelf) target.gameObject.SetActive(true);
            if (!string.Equals(target.text, text, StringComparison.Ordinal)) target.text = text;
        }

        // A row is one line: the columns are tab stops, a wrapped value would run into the next column.
        private static void StyleRow(TMP_Text text)
        {
            if (text == null) return;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = true;
        }
    }
}
