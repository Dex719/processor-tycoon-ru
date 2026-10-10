"""Tests of tools/check_spec_refs.py (mod-framework TSK-047, FR-22, AC-22.1 and AC-22.3).

Run from the repository root:  python -m unittest discover -s mod/tests/installer
Everything happens on temporary files: a fake "game code" folder and fake specs. Two tests also run the tool on the
real .kiro/specs; the one that needs pt-modding/code is skipped on machines that do not have it.
"""
import importlib.util
import os
import subprocess
import sys
import tempfile
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
TOOL = os.path.join(ROOT, 'tools', 'check_spec_refs.py')
REAL_SPECS = os.path.join(ROOT, '.kiro', 'specs')
REAL_DESIGN = os.path.join(REAL_SPECS, 'mod-framework', 'design.md')
REAL_CODE = os.path.join(os.path.dirname(ROOT), 'pt-modding', 'code')

_spec = importlib.util.spec_from_file_location('check_spec_refs', TOOL)
tool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(tool)

# --- fake game code, in the shape of the decompiled pseudo-C# (method bodies are IL-like, labels and all) ---

MARKET_CS = '''// Namespace: ProcessorTycoon.MarketSystem

public class Market : MonoBehaviour
{
    [Header("Populations")] [SerializeField] private List<Market.Population> populations;
    private float ipsSum;
    public static Market Instance { get; private set; }
    public float[] PotentialSales { get; private set; }
    public event Action OnCpusChanged;

    private void Awake()
    {
        Market.Instance = this;
        s0 = "}{";   // braces inside a string literal must not unbalance the parser
    }

    public void CalculateRatings(Market.Population population, int index)
    {
        // locals: int V_0
        V_0 = (this.cpus.Count - 1);
        try {
        goto IL_0037;
    IL_0018:
        V_1 = new Market.Competitor(V_0.Current);
    IL_0037:
        if (V_0.MoveNext()) goto IL_0018;
        } finally {
        V_0.Dispose();
        }  // end finally
    }

    public float get_MidRangeBudget()
    {
        return 1.0f;
    }

    private void <Start>b__20_0()
    {
    }

    public class Population : Object
    {
        public int Size { get; set; }
        public float GetCurrentSize(int year)
        {
            return 1.0f;
        }
    }

    private sealed class <>c : Object
    {
        public static readonly Market.<>c <>9;
    }
}
'''

CPU_CS = '''// Namespace: ProcessorTycoon.Hardware

public abstract class CpuBase : Object, ISaveID
{
    public string Name { get; set; }
    public float Price { get; set; }
    public CpuBase.MarketData Market { get; set; }

    public class MarketData : Object
    {
        public List<int> Sold { get; set; }
        public int Stock { get; set; }
    }
}

public class Cpu : CpuBase
{
    public int Year;
    public const int MaxYears = 50;
    public float Fail, Rate;

    public void Release()
    {
    }
}

public interface ISaveID
{

    public abstract int get_SaveID();

    public abstract void set_SaveID(int value);
}

public enum RatingType
{
    Overall = 0,
    Gaming = 1,
}
'''

OTHER_CS = '''// Namespace: ProcessorTycoon

public static class Modifiers
{
    public static float GetModifier(ModifierType type, ICompany company)
    {
        return 1.0f;
    }
}

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; set; }

    public bool get_IsLoading()
    {
        return false;
    }

    public void StartNewGame(PlayerInitialData data)
    {
    }
}

public class Player : MonoBehaviour
{
    public void OnNewGame()
    {
    }
}
'''


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    return path


def run_tool(*args):
    # -I: ignore PYTHON* variables and the user site; the tool itself forces UTF-8 on pipes
    p = subprocess.run([sys.executable, '-I', TOOL] + list(args), capture_output=True, cwd=ROOT, timeout=120)
    return p.returncode, p.stdout.decode('utf-8'), p.stderr.decode('utf-8')


class TempCase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.tmp = self._tmp.name

    def path(self, *parts):
        return os.path.join(self.tmp, *parts)


class GameCase(TempCase):
    """A fake pt-modding/code plus a helper that puts a spec next to it and runs the checker on it."""

    def setUp(self):
        super().setUp()
        self.code = self.path('code')
        write(os.path.join(self.code, 'ProcessorTycoon', 'MarketSystem', 'Market.cs'), MARKET_CS)
        write(os.path.join(self.code, 'ProcessorTycoon', 'Hardware', 'Cpu.cs'), CPU_CS)
        write(os.path.join(self.code, 'ProcessorTycoon', 'Other.cs'), OTHER_CS)

    def check(self, spec_text, name='spec.md'):
        spec = write(self.path('specs', 'feature', name), spec_text)
        return run_tool('--code', self.code, '--specs', spec)

    def errors(self, spec_text):
        rc, out, _ = self.check(spec_text)
        return rc, [l for l in out.splitlines() if not l.startswith('Итог')]


# ----------------------------------------------------------------------------------------------
# C# subset parser
# ----------------------------------------------------------------------------------------------

class ParserTests(unittest.TestCase):
    def parse(self, text, namespace=''):
        index = tool.TypeIndex()
        index.merge_text(text, namespace)
        return index

    def test_members_of_every_kind(self):
        idx = self.parse(MARKET_CS)
        m = idx.types['Market']
        for name in ('Awake', 'CalculateRatings', 'get_MidRangeBudget', 'MidRangeBudget', 'Instance', 'get_Instance',
                     'set_Instance', 'PotentialSales', 'OnCpusChanged', 'populations', 'ipsSum'):
            self.assertIn(name, m.members, name)
        self.assertEqual(m.members['OnCpusChanged'], 'event')
        self.assertEqual(m.members['Instance'], 'property')
        self.assertEqual(m.members['populations'], 'field')
        self.assertEqual(m.members['CalculateRatings'], 'method')
        self.assertNotIn('Population', m.members)           # nested types live in .nested, not in members

    def test_nested_types_and_method_bodies_do_not_leak(self):
        idx = self.parse(MARKET_CS)
        m = idx.types['Market']
        self.assertEqual(sorted(m.nested), ['<>c', 'Population'])
        self.assertIn('GetCurrentSize', m.nested['Population'].members)
        self.assertNotIn('GetCurrentSize', m.members)
        self.assertNotIn('V_0', m.members)                  # locals of method bodies are not members
        self.assertNotIn('goto', m.members)

    def test_inheritance_and_interfaces(self):
        idx = self.parse(CPU_CS)
        r = tool.Resolver(idx)
        self.assertIsNone(r.check_chain(['Cpu', 'Price']))              # from CpuBase
        self.assertIsNone(r.check_chain(['Cpu', 'SaveID']))             # property alias of ISaveID.get_SaveID
        self.assertIsNone(r.check_chain(['Cpu', 'get_SaveID']))
        self.assertIsNone(r.check_chain(['Cpu', 'Fail']))               # 'public float Fail, Rate;'
        self.assertIsNone(r.check_chain(['Cpu', 'Rate']))
        self.assertIsNone(r.check_chain(['Cpu', 'MaxYears']))
        self.assertIsNone(r.check_chain(['RatingType', 'Gaming']))      # enum member
        self.assertEqual(r.check_chain(['Cpu', 'Nope'])[0], 'missing')
        self.assertEqual(r.check_chain(['CpuBase', 'Release'])[0], 'missing')   # derived members are not inherited upwards

    def test_nested_type_chain(self):
        r = tool.Resolver(self.parse(CPU_CS))
        self.assertIsNone(r.check_chain(['CpuBase', 'MarketData', 'Stock']))
        self.assertIsNone(r.check_chain(['Cpu', 'MarketData', 'Stock']))      # nested type seen through the base class
        self.assertIsNone(r.check_chain(['CpuBase', 'MarketData']))
        status, owner, name = r.check_chain(['CpuBase', 'MarketData', 'Nope'])
        self.assertEqual((status, owner.name, name), ('missing', 'MarketData', 'Nope'))
        # CpuBase.Market is a property (of type MarketData), not a nested type: the chain stops there
        self.assertIsNone(r.check_chain(['CpuBase', 'Market', 'Anything']))

    def test_unresolved_class_base_makes_the_type_open_but_interfaces_do_not(self):
        idx = self.parse('public class A : SomethingUnknown { public int X; }\n'
                         'public class B : IUnknownInterface { public int Y; }\n')
        r = tool.Resolver(idx)
        self.assertIsNone(r.check_chain(['A', 'Whatever']))
        self.assertEqual(r.check_chain(['B', 'Whatever'])[0], 'missing')

    def test_unity_base_members(self):
        r = tool.Resolver(self.parse('public class A : MonoBehaviour { public int X; }\n'
                                     'public class B : Object { public int Y; }\n'))
        self.assertIsNone(r.check_chain(['A', 'StartCoroutine']))
        self.assertIsNone(r.check_chain(['A', 'gameObject']))
        self.assertEqual(r.check_chain(['A', 'NoSuch'])[0], 'missing')
        self.assertEqual(r.check_chain(['B', 'StartCoroutine'])[0], 'missing')
        self.assertIsNone(r.check_chain(['B', 'ToString']))

    def test_design_style_declarations(self):
        text = '''
namespace PTMod.Core
{
    public sealed class FeatureContext
    {
        public string FeatureId { get; }
        public System.IDisposable OnTick(TickPhase phase, System.Action handler, int priority = 0);
        public void Patch(System.Type patchClass);                 // [ModPatch]-methods; { not a real brace
    }
    [System.Flags] public enum TickOptions { None = 0, RunDuringFastForward = 1, RunDuringLoading = 2 }
    public enum TickPhase
    {
        EarlyTick, Tick,
        DayPassed, FastForwardEnded
    }
    public static class ModTicks
    {
        public static event System.Action<GameReadyReason> GameReady;
        public static bool IsBatchRun { get; }
        public static T Pick<T>(System.Collections.Generic.IList<T> items);
        public static event System.Action Opened, Closed;
        public const int MaxBytes = 8 * 1024 * 1024, MaxDepth = 64;
        public int Priority { get; set; } = 400;
        public static string Name => "x";
        public struct TooltipText { public string Header, Content; }
        public ModTicks(int x) { }
        /* block comment with a { */
        public void Tail() { }
    }
    public sealed class ModMailRecord
    {
        public int MailId; public string FeatureId, Tag;
        public System.DateTime? DeliveredOn;
        public bool IsDelivered { get; }
    }
    public sealed class ModPatchAttribute : System.Attribute
    {
        public ModPatchAttribute(System.Type target, string method, PatchKind kind);
        public System.Type[] ArgTypes { get; set; }
    }
}
namespace PTMod.Core.Logic.Json
{
    public static class ModJson
    {
        public static T Bind<T>(JsonNode node, System.Collections.Generic.List<string> errors) where T : new();
    }
}
'''
        idx = self.parse(text)
        r = tool.Resolver(idx)
        ok = [('FeatureContext', 'OnTick'), ('FeatureContext', 'FeatureId'), ('FeatureContext', 'Patch'),
              ('TickOptions', 'RunDuringLoading'), ('TickPhase', 'FastForwardEnded'), ('TickPhase', 'Tick'),
              ('ModTicks', 'GameReady'), ('ModTicks', 'IsBatchRun'), ('ModTicks', 'Pick'), ('ModTicks', 'Opened'),
              ('ModTicks', 'Closed'), ('ModTicks', 'MaxBytes'), ('ModTicks', 'MaxDepth'), ('ModTicks', 'Priority'),
              ('ModTicks', 'Name'), ('ModTicks', 'TooltipText'), ('ModTicks', 'Tail'),
              ('ModMailRecord', 'MailId'), ('ModMailRecord', 'Tag'), ('ModMailRecord', 'DeliveredOn'),
              ('ModMailRecord', 'IsDelivered'), ('ModPatchAttribute', 'ArgTypes'), ('ModJson', 'Bind')]
        for cls, member in ok:
            self.assertIsNone(r.check_chain([cls, member]), '%s.%s' % (cls, member))
        self.assertIsNone(r.check_chain(['ModTicks', 'TooltipText', 'Header']))
        self.assertIsNone(r.check_chain(['PTMod', 'Core', 'ModTicks', 'IsBatchRun']))        # namespace-qualified
        self.assertIsNone(r.check_chain(['PTMod', 'Core', 'Logic', 'Json', 'ModJson', 'Bind']))
        for cls, member in [('ModTicks', 'OnDayPassed'), ('ModTicks', 'IsBatchRunning'), ('ModTicks', 'int'),
                            ('ModTicks', 'ModTicks'), ('ModPatchAttribute', 'target'), ('TickPhase', 'X')]:
            self.assertEqual(r.check_chain([cls, member])[0], 'missing', '%s.%s' % (cls, member))

    def test_garbage_does_not_hang_or_crash(self):
        for text in ('', '}}}', '{{{', 'class', 'public class', 'public class A', 'public class A {', 'enum E { A = ',
                     'public class A { public int X = new int[] { 1, 2 }; public int Y; }',
                     'ModX.Member(); foo() { } public static class Z { public int Q; }'):
            idx = self.parse(text)                          # must return
            self.assertIsInstance(idx, tool.TypeIndex)
        idx = self.parse('public class A { public int X = new int[] { 1, 2 }; public int Y; }')
        self.assertIn('Y', idx.types['A'].members)
        idx = self.parse('ModX.Member(); foo() { } public static class Z { public int Q; }')
        self.assertIn('Q', idx.types['Z'].members)
        self.assertNotIn('Member', getattr(idx.types.get('Z'), 'members', {}))


# ----------------------------------------------------------------------------------------------
# Markdown scanning
# ----------------------------------------------------------------------------------------------

class CitationScanTests(unittest.TestCase):
    def cites(self, text):
        return [(no, '.'.join(segs), w) for no, segs, w in tool.iter_citations(text)]

    def test_only_inline_code_counts(self):
        self.assertEqual(self.cites('Market.Foo без кавычек и `Market.Bar` в них'), [(1, 'Market.Bar', False)])

    def test_fenced_blocks_are_skipped_and_line_numbers_kept(self):
        text = '`A.B`\n```csharp\n`Market.X`\nMarket.Y\n```\n~~~\n`Market.Z`\n~~~\n  ```\n`Market.Q`\n  ```\n`C.D`\n'
        self.assertEqual(self.cites(text), [(1, 'A.B', False), (12, 'C.D', False)])

    def test_call_syntax_and_double_backticks(self):
        text = '`Company.AddCpu(cpu)` and ``Market.CalculateRatings → `x` `` and `see Market.Foo()`\n'
        self.assertEqual(self.cites(text), [(1, 'Company.AddCpu', False), (1, 'Market.CalculateRatings', False),
                                            (1, 'Market.Foo', False)])

    def test_chains_in_paths_and_versions_are_ignored(self):
        text = '`mod/src/Market.Foo` `C:\\dir\\Market.Foo` `0.2.3` `v0.2` `@Market.Foo`\n'
        self.assertEqual(self.cites(text), [])

    def test_a_chain_is_taken_whole_so_a_receiver_variable_hides_the_class(self):
        self.assertEqual(self.cites('`cpu.Market.Foo`\n'), [(1, 'cpu.Market.Foo', False)])

    def test_file_names_are_ignored(self):
        text = '`Player.log` `PTMod.dll` `ptmod_settings.json` `README.md` `install.py` `notes.txt` `Market.cs` `Market.Foo`\n'
        self.assertEqual(self.cites(text), [(1, 'Market.Foo', False)])

    def test_wildcards_and_duplicates(self):
        text = '`Market.Update*` `Market.Update…` `Market.Foo` `Market.Foo`\n'
        self.assertEqual(self.cites(text), [(1, 'Market.Update', True), (1, 'Market.Foo', False)])

    def test_crlf_text_keeps_line_numbers(self):
        text = 'a\r\n`Market.Foo`\r\n'
        self.assertEqual(self.cites(tool.read_text(self._write_crlf(text))), [(2, 'Market.Foo', False)])

    def _write_crlf(self, text):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        p = os.path.join(tmp.name, 'x.md')
        with open(p, 'wb') as f:
            f.write(text.encode('utf-8'))
        return p


# ----------------------------------------------------------------------------------------------
# Mode 1: Class.Member against the game code (AC-22.1)
# ----------------------------------------------------------------------------------------------

class GameRefsTests(GameCase):
    def test_missing_method_gives_exit_code_1_and_file_line_message(self):
        rc, out, _ = self.check('# Spec\n\nSee `Market.NoSuchMethod` here.\n')       # AC-22.1
        self.assertEqual(rc, 1)
        self.assertIn('spec.md:3: метод не найден: Market.NoSuchMethod', out.replace('\\', '/'))
        self.assertIn('ошибок 1', out)

    def test_valid_references_pass(self):
        rc, errs = self.errors(
            '`Market.CalculateRatings` `Market.Awake` `Market.get_MidRangeBudget` `Market.MidRangeBudget`\n'
            '`Market.Instance` `Market.get_Instance` `Market.PotentialSales` `Market.OnCpusChanged`\n'
            '`Market.populations` `Market.Population.GetCurrentSize` `Cpu.Price` `Cpu.Year` `Cpu.MaxYears`\n'
            '`Cpu.SaveID` `RatingType.Gaming` `GameManager.get_IsLoading` `GameManager.IsLoading`\n'
            '`Modifiers.GetModifier` `Player.OnNewGame(…)` `Company.AddCpu`\n')
        self.assertEqual((rc, errs), (0, []))

    def test_nested_type_member(self):
        rc, errs = self.errors('`CpuBase.MarketData.Stock` `CpuBase.MarketData` `Cpu.MarketData.Sold`\n')
        self.assertEqual((rc, errs), (0, []))
        rc, errs = self.errors('`CpuBase.MarketData.Stok`\n')
        self.assertEqual(rc, 1)
        self.assertIn(':1: метод не найден: CpuBase.MarketData.Stok', errs[0].replace('\\', '/'))
        self.assertIn('Stock', errs[0])                                                  # suggestion

    def test_member_of_nested_type_is_not_a_member_of_the_outer_class(self):
        rc, errs = self.errors('`Market.GetCurrentSize`\n')
        self.assertEqual(rc, 1)

    def test_file_names_mod_entities_and_unknown_classes_are_skipped(self):
        rc, errs = self.errors('`Player.log` `Market.dll` `Market.json` `Market.txt` `Market.md` `Market.py`\n'
                               '`ModTicks.Whatever` `ModMail.Send` `PTMod.Core` `PTModHost.Instance`\n'
                               '`Unknown.Foo` `Class.Method` `Time.timeScale` `Foo.Bar.Baz`\n')
        self.assertEqual((rc, errs), (0, []))

    def test_chain_with_a_variable_receiver_is_not_checked(self):
        rc, errs = self.errors('`cpu.Market.Nope` `this.Market.Nope` `x.Cpu.Nope`\n')
        self.assertEqual((rc, errs), (0, []))

    def test_game_class_starting_with_mod_is_still_checked(self):
        rc, errs = self.errors('`Modifiers.GetModifierX`\n')
        self.assertEqual(rc, 1)
        self.assertIn('Modifiers.GetModifierX', errs[0])

    def test_code_blocks_are_not_checked(self):
        rc, errs = self.errors('```csharp\nMarket.Nope();\n`Market.Nope`\n```\n\n`Market.AlsoNope`\n')
        self.assertEqual(rc, 1)
        self.assertEqual(len(errs), 1)
        self.assertIn(':6: метод не найден: Market.AlsoNope', errs[0].replace('\\', '/'))

    def test_suggestion_for_a_typo(self):
        _, errs = self.errors('`Market.CalculateRating`\n')
        self.assertIn('CalculateRatings', errs[0])

    def test_namespace_qualified_reference(self):
        rc, errs = self.errors('`ProcessorTycoon.MarketSystem.Market.CalculateRatings`\n')
        self.assertEqual((rc, errs), (0, []))
        rc, errs = self.errors('`ProcessorTycoon.MarketSystem.Market.Nope`\n')
        self.assertEqual(rc, 1)

    def test_wildcard_prefix(self):
        rc, errs = self.errors('`Market.Calculate*` `Market.Zzz*`\n')
        self.assertEqual(rc, 1)
        self.assertEqual(len(errs), 1)
        self.assertIn('Market.Zzz*', errs[0])

    def test_several_errors_in_several_files_are_sorted(self):
        write(self.path('specs', 'a', 'design.md'), 'x\n`Market.B1` `Market.Awake` `Cpu.Zzz`\n')
        write(self.path('specs', 'a', 'requirements.md'), '`Market.B2`\n')
        write(self.path('specs', 'b', 'design.md'), '\n\n`Market.B3`\n')
        rc, out, _ = run_tool('--code', self.code, '--specs', self.path('specs'))
        self.assertEqual(rc, 1)
        lines = [l.replace('\\', '/') for l in out.splitlines() if 'метод не найден' in l]
        self.assertEqual(len(lines), 4)
        self.assertEqual([l.split(': метод не найден: ')[1].split(' ')[0] for l in lines],
                         ['Market.B1', 'Cpu.Zzz', 'Market.B2', 'Market.B3'])
        self.assertIn('a/design.md:2:', lines[0])
        self.assertIn('b/design.md:3:', lines[3])

    def test_directory_walk_only_reads_markdown(self):
        write(self.path('specs', 'a', 'notes.txt'), '`Market.Nope`\n')
        write(self.path('specs', 'a', 'design.md'), '`Market.Awake`\n')
        rc, out, _ = run_tool('--code', self.code, '--specs', self.path('specs'))
        self.assertEqual(rc, 0)
        self.assertIn('файлов 1', out)

    def test_paths_can_be_given_without_a_key(self):
        spec = write(self.path('specs', 'x.md'), '`Market.Nope`\n')
        rc, out, _ = run_tool('--code', self.code, spec)
        self.assertEqual(rc, 1)
        self.assertIn('x.md:1: метод не найден: Market.Nope', out.replace('\\', '/'))
        rc, out, _ = run_tool(self.path('specs'), '--code', self.code)
        self.assertEqual(rc, 1)

    def test_quiet_prints_only_errors(self):
        spec = write(self.path('specs', 'x.md'), '`Market.Nope`\n')
        rc, out, _ = run_tool('--code', self.code, '--specs', spec, '-q')
        self.assertEqual(rc, 1)
        self.assertNotIn('Итог', out)
        self.assertEqual(len(out.strip().splitlines()), 1)

    def test_utf8_bom_and_crlf_files(self):
        spec = self.path('specs', 'x.md')
        os.makedirs(os.path.dirname(spec))
        with open(spec, 'wb') as f:
            f.write(b'\xef\xbb\xbf' + 'Привет\r\n`Market.Nope`\r\n'.encode('utf-8'))
        rc, out, _ = run_tool('--code', self.code, '--specs', spec)
        self.assertEqual(rc, 1)
        self.assertIn(':2: метод не найден', out)

    def test_missing_code_folder_is_a_usage_error(self):
        spec = write(self.path('specs', 'x.md'), '`Market.Nope`\n')
        rc, _, err = run_tool('--code', self.path('nope'), '--specs', spec)
        self.assertEqual(rc, 2)
        self.assertIn('--code', err)

    def test_missing_specs_is_a_usage_error(self):
        rc, _, err = run_tool('--code', self.code, '--specs', self.path('no-specs'))
        self.assertEqual(rc, 2)
        self.assertIn('Спеки не найдены', err)

    def test_empty_code_folder_is_a_usage_error(self):
        empty = self.path('empty-code')
        os.makedirs(empty)
        spec = write(self.path('specs', 'x.md'), '`Market.Nope`\n')
        rc, _, err = run_tool('--code', empty, '--specs', spec)
        self.assertEqual(rc, 2)

    @unittest.skipUnless(os.path.isdir(REAL_CODE), 'pt-modding/code is only on the author machine')
    def test_real_specs_have_no_broken_game_references(self):
        rc, out, _ = run_tool('--code', REAL_CODE)
        self.assertEqual(rc, 0, out)
        self.assertIn('ошибок 0', out)

    @unittest.skipUnless(os.path.isdir(REAL_CODE), 'pt-modding/code is only on the author machine')
    def test_real_code_has_the_members_the_specs_rely_on(self):
        idx = tool.build_game_index(REAL_CODE)
        r = tool.Resolver(idx)
        for chain in (('Company', 'AddCpu'), ('AICompany', 'AddCpu'), ('DateController', 'NextMonth'),
                      ('DateController', 'OnFastFowardEnded'), ('CpuBase', 'MarketData', 'Stock'),
                      ('Contract', 'CpuOffer', 'DefineChance'), ('Contract', 'GetBestOffers'),
                      ('Market', 'get_MidRangeBudget'), ('GameManager', 'get_IsLoading'), ('ICompany', 'SaveID'),
                      ('CentralBank', 'DebtLimit'), ('Email', 'EventDate')):
            self.assertIsNone(r.check_chain(list(chain)), '.'.join(chain))
        self.assertEqual(r.check_chain(['Market', 'NoSuchMethodAnywhere'])[0], 'missing')


# ----------------------------------------------------------------------------------------------
# Mode 2: ModX.Member against the core API of design.md (AC-22.3)
# ----------------------------------------------------------------------------------------------

DESIGN_MD = '''# Design

## Components

```csharp
namespace PTMod.Core
{
    public enum TickPhase { EarlyTick, Tick, LateTick, DayPassed, MonthPassed, FastForwardEnded }
    public sealed class FeatureContext
    {
        public string FeatureId { get; }
        public System.IDisposable OnTick(TickPhase phase, System.Action handler, int priority = 0);
    }
    public static class ModTicks
    {
        public static event System.Action<GameReadyReason> GameReady;
        public static bool IsFastForwarding { get; }
        public static bool IsBatchRun { get; }
        public static event System.Action BatchEnded;
        public static System.DateTime Today { get; }
    }
    public static class ModMail
    {
        public static int Send(ModMailMessage message);
        public static event System.Action<ModMailRecord> Delivered;
        public const int FirstModMailId = 100000;
    }
    public static class ModUI
    {
        public struct TooltipText { public string Header, Content; }
        public static void Tooltip(UnityEngine.GameObject target, string header, string content);
    }
    public static class ModChannels
    {
        public const string ResearchSpeed = "research.speed", ResearchCost = "research.cost";
    }
    public struct ModifierContext
    {
        public ProcessorTycoon.Hardware.Cpu Cpu; public Segment Segment;
        public ProcessorTycoon.ContractSystem.Contract Contract;
    }
    public enum Segment { Any = -1, HighEnd = 0 }
    public sealed class DesignContext { public float Frequency; }
}
```

Text with `ModTicks.OnDayPassed` outside code (the design itself is not checked).

```json
{ "ModTicks": 1 }
```
'''


class CoreApiCase(TempCase):
    def setUp(self):
        super().setUp()
        self.design = write(self.path('mod-framework', 'design.md'), DESIGN_MD)

    def feature(self, text, name='design.md', folder='some-feature'):
        return write(self.path('specs', folder, name), text)

    def check(self, text):
        self.feature(text)
        return run_tool('--core-api', self.design, '--specs', self.path('specs'))

    def errors(self, text):
        rc, out, _ = self.check(text)
        return rc, [l.replace('\\', '/') for l in out.splitlines() if not l.startswith('Итог')]


class CoreApiTests(CoreApiCase):
    def test_nonexistent_tick_event_is_rejected_with_a_hint(self):
        rc, errs = self.errors('# Feature\n\nTicks: `ModTicks.OnDayPassed`.\n')               # AC-22.3
        self.assertEqual(rc, 1)
        self.assertEqual(len(errs), 1)
        self.assertIn('design.md:3: нет в API ядра (используйте FeatureContext.OnTick(TickPhase.DayPassed))', errs[0])
        self.assertIn('ModTicks.OnDayPassed', errs[0])

    def test_hint_for_other_phases_and_the_game_typo(self):
        _, errs = self.errors('`ModTicks.OnMonthPassed` `ModTicks.OnFastFowardEnded` `ModTicks.MonthPassed`\n')
        self.assertEqual(len(errs), 3)
        self.assertIn('(используйте FeatureContext.OnTick(TickPhase.MonthPassed))', errs[1] if 'MonthPassed' in errs[1] else errs[0])
        self.assertTrue(any('TickPhase.FastForwardEnded' in e for e in errs))

    def test_nonexistent_batch_flag_is_rejected_and_the_real_one_accepted(self):
        rc, errs = self.errors('`ModTicks.IsBatchRunning`\n')
        self.assertEqual(rc, 1)
        self.assertIn('нет в API ядра (используйте ModTicks.IsBatchRun)', errs[0])
        rc, errs = self.errors('`ModTicks.IsBatchRun` `ModTicks.BatchEnded` `ModTicks.GameReady(NewGame)`\n')
        self.assertEqual((rc, errs), (0, []))

    def test_hint_points_to_the_service_that_owns_the_name(self):
        _, errs = self.errors('`ModTicks.Send`\n')
        self.assertIn('используйте ModMail.Send', errs[0])

    def test_events_constants_nested_types_and_structs_are_accepted(self):
        rc, errs = self.errors('`ModMail.Delivered` `ModMail.FirstModMailId` `ModUI.TooltipText` `ModUI.Tooltip`\n'
                               '`ModChannels.ResearchSpeed` `ModChannels.ResearchCost` `ModifierContext.Contract`\n'
                               '`ModifierContext.Cpu` `FeatureContext.OnTick` `TickPhase.DayPassed` `Segment.HighEnd`\n'
                               '`PTMod.Core.ModTicks.IsBatchRun`\n')
        self.assertEqual((rc, errs), (0, []))

    def test_mod_framework_folder_is_excluded(self):
        write(self.path('specs', 'mod-framework', 'design.md'), '`ModTicks.OnDayPassed` `ModTicks.IsBatchRunning`\n')
        write(self.path('specs', 'mod-framework', 'sub', 'notes.md'), '`ModNope.X`\n')
        write(self.path('specs', 'ok-feature', 'design.md'), '`ModTicks.IsBatchRun`\n')
        rc, out, _ = run_tool('--core-api', self.design, '--specs', self.path('specs'))
        self.assertEqual(rc, 0, out)
        self.assertIn('файлов 1', out)

    def test_explicit_file_in_the_mod_framework_folder_is_excluded_and_other_files_are_not(self):
        inside = write(self.path('specs', 'mod-framework', 'tasks.md'), '`ModTicks.OnDayPassed`\n')
        outside = write(self.path('specs', 'feature', 'tasks.md'), '`ModTicks.OnDayPassed`\n')
        rc, out, _ = run_tool('--core-api', self.design, inside)
        self.assertEqual(rc, 0, out)
        rc, out, _ = run_tool('--core-api', self.design, outside)
        self.assertEqual(rc, 1)
        self.assertIn('tasks.md:1: нет в API ядра', out.replace('\\', '/'))

    def test_unknown_mod_class_is_rejected(self):
        rc, errs = self.errors('`ModTick.IsBatchRun` `ModFoo.Bar`\n')
        self.assertEqual(rc, 1)
        self.assertEqual(len(errs), 2)
        tick = [e for e in errs if 'ModTick.IsBatchRun' in e][0]
        self.assertIn('нет в API ядра: ModTick.IsBatchRun', tick)
        self.assertIn('похоже: ModTicks', tick)
        self.assertTrue(any('ModFoo.Bar' in e for e in errs))

    def test_unknown_member_of_a_non_mod_core_type_is_rejected(self):
        rc, errs = self.errors('`TickPhase.FastFowardEnded` `FeatureContext.Subscribe` `Segment.Medium`\n')
        self.assertEqual(rc, 1)
        self.assertEqual(len(errs), 3)
        self.assertIn('используйте TickPhase.FastForwardEnded', errs[0])

    def test_placeholders_and_foreign_names_are_skipped(self):
        rc, errs = self.errors('`ModX.Member` `TickPhase.X` `ModХ.Член` `Modifiers.GetModifier` `Market.Foo` '
                               '`ModifierSomething.Foo` `ctx.ModTicks.Nope` `Foo.Bar`\n')
        self.assertEqual((rc, errs), (0, []))

    def test_single_letter_member_of_a_mod_class_is_not_a_placeholder(self):
        rc, errs = self.errors('`ModTicks.X`\n')
        self.assertEqual(rc, 1)

    def test_feature_may_declare_its_own_type_with_a_core_name(self):
        self.feature('```csharp\ninternal static class DesignContext { public static int PlayerSelectedFamily; }\n```\n',
                     name='design.md', folder='cpu-arch')
        write(self.path('specs', 'cpu-arch', 'tasks.md'), '`DesignContext.PlayerSelectedFamily`\n')
        write(self.path('specs', 'other', 'design.md'), '`DesignContext.PlayerSelectedFamily`\n')
        rc, out, _ = run_tool('--core-api', self.design, '--specs', self.path('specs'))
        lines = [l.replace('\\', '/') for l in out.splitlines() if 'нет в API ядра' in l]
        self.assertEqual(rc, 1)
        self.assertEqual(len(lines), 1)
        self.assertIn('other/design.md:1:', lines[0])

    def test_code_blocks_are_not_checked(self):
        rc, errs = self.errors('```csharp\nModTicks.OnDayPassed += x;\n```\n')
        self.assertEqual((rc, errs), (0, []))

    def test_wildcard(self):
        rc, errs = self.errors('`ModTicks.Is*` `ModTicks.Zz*`\n')
        self.assertEqual(rc, 1)
        self.assertEqual(len(errs), 1)

    def test_design_md_not_found_is_a_usage_error(self):
        self.feature('`ModTicks.IsBatchRun`\n')
        rc, _, err = run_tool('--core-api', self.path('nope.md'), '--specs', self.path('specs'))
        self.assertEqual(rc, 2)
        self.assertIn('design.md не найден', err)

    def test_design_without_core_types_is_a_usage_error(self):
        bad = write(self.path('bad', 'design.md'), '# nothing\n\n```csharp\npublic class Foo { }\n```\n')
        self.feature('`ModTicks.IsBatchRun`\n')
        rc, _, err = run_tool('--core-api', bad, '--specs', self.path('specs'))
        self.assertEqual(rc, 2)

    def test_only_csharp_blocks_of_design_define_the_api(self):
        design = write(self.path('d2', 'design.md'),
                       '```text\npublic static class ModGhost { public static int A; }\n```\n'
                       '```csharp\npublic static class ModReal { public static int B; }\n```\n')
        self.feature('`ModReal.B` `ModGhost.A`\n')
        rc, out, _ = run_tool('--core-api', design, '--specs', self.path('specs'))
        self.assertEqual(rc, 1)
        self.assertIn('ModGhost.A', out)
        self.assertNotIn('ModReal.B', out)


class RealDesignTests(unittest.TestCase):
    @unittest.skipUnless(os.path.isfile(REAL_DESIGN), 'mod-framework design.md is missing')
    def test_core_api_is_collected_from_the_real_design(self):
        r = tool.Resolver(tool.build_core_index(REAL_DESIGN))
        present = [
            ('ModTicks', 'IsBatchRun'), ('ModTicks', 'BatchEnded'), ('ModTicks', 'GameReady'), ('ModTicks', 'IsLoading'),
            ('ModTicks', 'IsFastForwarding'), ('ModTicks', 'Today'), ('ModMail', 'Delivered'), ('ModMail', 'Send'),
            ('ModMail', 'SetStatusLine'), ('ModMail', 'FirstModMailId'), ('ModEvents', 'CpuReleased'),
            ('ModEvents', 'CpuTransferred'), ('ModUI', 'TooltipText'), ('ModUI', 'Confirm'), ('ModUI', 'CreateWindow'),
            ('ModChannels', 'ResearchSpeed'), ('ModChannels', 'GpuDemand'), ('ModChannels', 'MarketDemand'),
            ('ModReputation', 'Min'), ('ModReputation', 'Drift'), ('ModModifiers', 'Explain'),
            ('ModModifiers', 'RegisterReasonedProvider'), ('ModifierContext', 'Contract'), ('ModifierContext', 'Design'),
            ('ModSegments', 'Classify'), ('ModCredit', 'ThresholdOf'), ('ModCredit', 'PlayerDebtLimit'),
            ('FeatureContext', 'OnTick'), ('FeatureContext', 'ConfirmBeforeDisable'), ('TickPhase', 'FastForwardEnded'),
            ('ModFeatures', 'StateChanged'), ('ModHooks', 'Guard'), ('ModHooks', 'IsReserved'),
            ('ModNewGame', 'PreviewChanged'), ('ModNewGame', 'RegisterSection'), ('ModSave', 'FileSchema'),
            ('ModJson', 'MaxDepth'), ('SegmentRule', 'ByPrice'), ('CreditMath', 'Threshold'), ('ModIds', 'PrefixesOf'),
            ('ModLog', 'RecentProblems'), ('ModL10n', 'LanguageChanged'), ('ModSettings', 'Debug'),
            ('ModWindow', 'Opened'), ('ModWindow', 'Closed'), ('ModConditions', 'Register'),
            ('PTModHost', 'SceneReady'), ('ModMailMessage', 'DefaultChoiceId'), ('ModMailRecord', 'IsDelivered'),
        ]
        for cls, member in present:
            self.assertIsNone(r.check_chain([cls, member]), '%s.%s' % (cls, member))
        for cls, member in (('ModTicks', 'OnDayPassed'), ('ModTicks', 'IsBatchRunning'), ('ModMail', 'Deliver'),
                            ('ModUI', 'Templates')):
            self.assertEqual(r.check_chain([cls, member])[0], 'missing', '%s.%s' % (cls, member))

    @unittest.skipUnless(os.path.isfile(REAL_DESIGN), 'mod-framework design.md is missing')
    def test_real_specs_have_no_wrong_core_api_names(self):
        rc, out, _ = run_tool('--core-api', REAL_DESIGN)
        self.assertEqual(rc, 0, out)
        self.assertIn('ошибок 0', out)

    @unittest.skipUnless(os.path.isfile(REAL_DESIGN), 'mod-framework design.md is missing')
    def test_core_api_without_a_value_uses_the_design_of_the_repository(self):
        # AC-22.3 is written as "check_spec_refs.py --core-api": the design.md of mod-framework is the default
        with tempfile.TemporaryDirectory() as tmp:
            spec = write(os.path.join(tmp, 'feature', 'design.md'), 'Ticks: `ModTicks.OnDayPassed`.\n')
            rc, out, _ = run_tool('--core-api', tmp)
            self.assertEqual(rc, 1)
            self.assertIn('design.md:1: нет в API ядра (используйте FeatureContext.OnTick(TickPhase.DayPassed))',
                          out.replace('\\', '/'))
            rc, out, _ = run_tool('--core-api', '--specs', spec)
            self.assertEqual(rc, 1)
            self.assertIn('нет в API ядра', out)
        rc, out, _ = run_tool('--core-api')
        self.assertEqual(rc, 0, out)

    @unittest.skipUnless(os.path.isfile(REAL_DESIGN), 'mod-framework design.md is missing')
    def test_default_mode_needs_no_design_and_core_mode_needs_no_game_code(self):
        # --core-api must not require --code (pt-modding exists only on the author machine)
        rc, out, err = run_tool('--core-api', REAL_DESIGN, '--code', os.path.join(tempfile.gettempdir(), 'no-such-dir-xyz'))
        self.assertEqual(rc, 0, out + err)


if __name__ == '__main__':
    unittest.main()
