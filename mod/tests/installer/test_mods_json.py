"""Tests of ptru/mods.py: registering the mod in Unity's ScriptingAssemblies.json / RuntimeInitializeOnLoads.json.

Run from the repository root:  python -m unittest discover -s mod/tests/installer
Everything happens on temporary files; no game files are read.
"""
import ast, importlib.util, json, os, re, sys, tempfile, unittest
from unittest import mock

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

from ptru import mods  # noqa: E402

BOM = b'\xef\xbb\xbf'
CYRILLIC = re.compile('[А-Яа-яЁё]')

# Hand-written literals in Unity's compact style (not produced by json.dumps, so the tests do not mirror the code).
SA = '{"names":["UnityEngine.dll","Assembly-CSharp.dll"],"types":[0,16]}'
SA_REGISTERED = '{"names":["UnityEngine.dll","Assembly-CSharp.dll","PTMod.dll"],"types":[0,16,16]}'
RI = ('{"root":[{"assemblyName":"Assembly-CSharp","nameSpace":"","className":"GameManager","methodName":"Boot",'
      '"loadTypes":1,"isUnityClass":false}]}')
MOD_ENTRY = ('{"assemblyName":"PTMod","nameSpace":"PTMod.Core","className":"ModBootstrap","methodName":"Init",'
             '"loadTypes":1,"isUnityClass":false}')
RI_REGISTERED = RI[:-2] + ',' + MOD_ENTRY + ']}'


def entry(i):
    return {'assemblyName': 'Asm%d' % i, 'nameSpace': 'Ns%d' % i, 'className': 'Cls%d' % i, 'methodName': 'M%d' % i,
            'loadTypes': i % 4, 'isUnityClass': bool(i % 2)}


def sa_data(n):
    names = ['UnityEngine.dll', 'UnityEngine.CoreModule.dll', 'Unity.TextMeshPro.dll', 'Assembly-CSharp.dll',
             'Zzz.dll'][:n]
    return {'names': names, 'types': [0] * (len(names) - 1) + [16] if names else []}


class TempDirCase(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.dir = self._tmp.name

    def write(self, name, data):
        path = os.path.join(self.dir, name)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'wb') as f:
            f.write(data if isinstance(data, bytes) else data.encode('utf-8'))
        return path

    def read(self, path):
        with open(path, 'rb') as f:
            return f.read()

    def files(self, sub=''):
        return sorted(os.listdir(os.path.join(self.dir, sub)))


class AssemblyRegistrationTests(TempDirCase):
    def test_appends_to_the_end_of_both_arrays(self):
        path = self.write('ScriptingAssemblies.json', SA)
        self.assertTrue(mods.register_assembly(path))
        self.assertEqual(self.read(path).decode(), SA_REGISTERED)

    def test_double_registration_gives_one_entry_and_does_not_touch_the_file(self):
        path = self.write('ScriptingAssemblies.json', SA)
        self.assertTrue(mods.register_assembly(path))
        once = self.read(path)
        mtime = os.stat(path).st_mtime_ns
        self.assertFalse(mods.register_assembly(path))
        self.assertEqual(self.read(path), once)
        self.assertEqual(os.stat(path).st_mtime_ns, mtime)
        data = json.loads(once)
        self.assertEqual(data['names'].count('PTMod.dll'), 1)
        self.assertEqual(len(data['names']), len(data['types']))

    def test_type_is_16_and_order_of_existing_entries_is_kept(self):
        path = self.write('ScriptingAssemblies.json', SA)
        mods.register_assembly(path)
        data = json.loads(self.read(path))
        self.assertEqual(data['names'], ['UnityEngine.dll', 'Assembly-CSharp.dll', 'PTMod.dll'])
        self.assertEqual(data['types'], [0, 16, 16])
        self.assertEqual(mods.ASSEMBLY_TYPE, 16)

    def test_unregister_returns_the_original_text(self):
        path = self.write('ScriptingAssemblies.json', SA)
        mods.register_assembly(path)
        self.assertTrue(mods.unregister_assembly(path))
        self.assertEqual(self.read(path).decode(), SA)

    def test_unregister_when_absent_is_a_no_op(self):
        path = self.write('ScriptingAssemblies.json', SA)
        mtime = os.stat(path).st_mtime_ns
        self.assertFalse(mods.unregister_assembly(path))
        self.assertEqual(self.read(path).decode(), SA)
        self.assertEqual(os.stat(path).st_mtime_ns, mtime)

    def test_other_name_and_type(self):
        path = self.write('ScriptingAssemblies.json', SA)
        mods.register_assembly(path, 'PTModSpike.dll', 16)
        mods.register_assembly(path, mods.HARMONY_ASSEMBLY, 0)
        data = json.loads(self.read(path))
        self.assertEqual(data['names'][-2:], ['PTModSpike.dll', '0Harmony.dll'])
        self.assertEqual(data['types'][-2:], [16, 0])
        mods.unregister_assembly(path, '0Harmony.dll')
        mods.unregister_assembly(path, 'PTModSpike.dll')
        self.assertEqual(self.read(path).decode(), SA)

    def test_removing_a_middle_entry_keeps_names_and_types_aligned(self):
        text = '{"names":["a.dll","PTMod.dll","b.dll"],"types":[1,2,3]}'
        path = self.write('ScriptingAssemblies.json', text)
        self.assertTrue(mods.unregister_assembly(path))
        self.assertEqual(self.read(path).decode(), '{"names":["a.dll","b.dll"],"types":[1,3]}')

    def test_removing_the_first_entry(self):
        path = self.write('ScriptingAssemblies.json', '{"names":["PTMod.dll","b.dll"],"types":[16,3]}')
        mods.unregister_assembly(path)
        self.assertEqual(self.read(path).decode(), '{"names":["b.dll"],"types":[3]}')

    def test_duplicates_of_the_same_name_are_all_removed(self):
        text = '{"names":["PTMod.dll","a.dll","PTMod.dll"],"types":[16,0,16]}'
        path = self.write('ScriptingAssemblies.json', text)
        self.assertFalse(mods.register_assembly(path))
        self.assertTrue(mods.unregister_assembly(path))
        self.assertEqual(self.read(path).decode(), '{"names":["a.dll"],"types":[0]}')

    def test_other_root_keys_and_their_order_survive(self):
        text = '{"version":3,"names":["a.dll"],"extra":{"x":[1,2]},"types":[16],"tail":null}'
        path = self.write('ScriptingAssemblies.json', text)
        mods.register_assembly(path)
        self.assertEqual(self.read(path).decode(),
                         '{"version":3,"names":["a.dll","PTMod.dll"],"extra":{"x":[1,2]},"types":[16,16],"tail":null}')
        mods.unregister_assembly(path)
        self.assertEqual(self.read(path).decode(), text)

    def test_types_before_names_in_the_file(self):
        text = '{"types":[0,16],"names":["a.dll","b.dll"]}'
        path = self.write('ScriptingAssemblies.json', text)
        mods.register_assembly(path)
        self.assertEqual(self.read(path).decode(), '{"types":[0,16,16],"names":["a.dll","b.dll","PTMod.dll"]}')
        mods.unregister_assembly(path)
        self.assertEqual(self.read(path).decode(), text)

    def test_is_assembly_registered(self):
        path = self.write('ScriptingAssemblies.json', SA)
        self.assertFalse(mods.is_assembly_registered(path))
        mods.register_assembly(path)
        self.assertTrue(mods.is_assembly_registered(path))
        self.assertFalse(mods.is_assembly_registered(path, 'Other.dll'))


class RuntimeInitRegistrationTests(TempDirCase):
    def test_default_entry_matches_design(self):
        self.assertEqual(json.dumps(mods.runtime_init_entry(), separators=(',', ':')), MOD_ENTRY)

    def test_appends_the_entry_to_root(self):
        path = self.write('RuntimeInitializeOnLoads.json', RI)
        self.assertTrue(mods.register_runtime_init(path))
        self.assertEqual(self.read(path).decode(), RI_REGISTERED)

    def test_double_registration_gives_one_entry(self):
        path = self.write('RuntimeInitializeOnLoads.json', RI)
        mods.register_runtime_init(path)
        once = self.read(path)
        self.assertFalse(mods.register_runtime_init(path))
        self.assertEqual(self.read(path), once)
        root = json.loads(once)['root']
        self.assertEqual(len([e for e in root if e['assemblyName'] == 'PTMod' and e['className'] == 'ModBootstrap']), 1)
        self.assertEqual(len(root), 2)

    def test_unregister_returns_the_original_text(self):
        path = self.write('RuntimeInitializeOnLoads.json', RI)
        mods.register_runtime_init(path)
        self.assertTrue(mods.unregister_runtime_init(path))
        self.assertEqual(self.read(path).decode(), RI)
        self.assertFalse(mods.unregister_runtime_init(path))

    def test_empty_root(self):
        path = self.write('RuntimeInitializeOnLoads.json', '{"root":[]}')
        mods.register_runtime_init(path)
        self.assertEqual(self.read(path).decode(), '{"root":[' + MOD_ENTRY + ']}')
        mods.unregister_runtime_init(path)
        self.assertEqual(self.read(path).decode(), '{"root":[]}')

    def test_existing_entry_with_same_identity_counts_as_registered(self):
        stale = json.loads(MOD_ENTRY)
        stale['loadTypes'] = 2
        text = json.dumps({'root': [stale]}, separators=(',', ':'))
        path = self.write('RuntimeInitializeOnLoads.json', text)
        self.assertTrue(mods.is_runtime_init_registered(path))
        self.assertFalse(mods.register_runtime_init(path))
        self.assertEqual(self.read(path).decode(), text)

    def test_other_entries_of_the_same_assembly_are_untouched(self):
        other = dict(json.loads(MOD_ENTRY), className='Other')
        text = json.dumps({'root': [other]}, separators=(',', ':'))
        path = self.write('RuntimeInitializeOnLoads.json', text)
        self.assertTrue(mods.register_runtime_init(path))
        self.assertEqual(len(json.loads(self.read(path))['root']), 2)
        mods.unregister_runtime_init(path)
        self.assertEqual(self.read(path).decode(), text)

    def test_custom_entry_for_the_spike(self):
        path = self.write('RuntimeInitializeOnLoads.json', RI)
        spike = mods.runtime_init_entry(assembly='PTModSpike')
        mods.register_runtime_init(path, spike)
        mods.register_runtime_init(path)
        self.assertEqual([e['assemblyName'] for e in json.loads(self.read(path))['root']],
                         ['Assembly-CSharp', 'PTModSpike', 'PTMod'])
        mods.unregister_runtime_init(path, spike)
        self.assertEqual(self.read(path).decode(), RI_REGISTERED)

    def test_entry_without_identity_fields_is_a_programming_error(self):
        path = self.write('RuntimeInitializeOnLoads.json', RI)
        with self.assertRaises(ValueError):
            mods.register_runtime_init(path, {'methodName': 'Init'})
        self.assertEqual(self.read(path).decode(), RI)


class StylePreservationTests(TempDirCase):
    def test_non_ascii_and_escapes_are_kept_byte_for_byte(self):
        text = ('{"root":[{"assemblyName":"Игра","nameSpace":"Про\\u0441транство","className":"\\u00e9\\n",'
                '"methodName":"Старт","loadTypes":1,"isUnityClass":false}]}')
        path = self.write('RuntimeInitializeOnLoads.json', text)
        mods.register_runtime_init(path)
        self.assertTrue(self.read(path).decode().startswith(text[:-2] + ','))
        mods.unregister_runtime_init(path)
        self.assertEqual(self.read(path), text.encode('utf-8'))

    def test_bom_and_trailing_newline(self):
        data = BOM + (SA + '\n').encode()
        path = self.write('ScriptingAssemblies.json', data)
        mods.register_assembly(path)
        got = self.read(path)
        self.assertTrue(got.startswith(BOM))
        self.assertEqual(got[3:].decode(), SA_REGISTERED + '\n')
        mods.unregister_assembly(path)
        self.assertEqual(self.read(path), data)

    def test_crlf_pretty_printed_files(self):
        text = json.dumps(sa_data(4), indent=2).replace('\n', '\r\n')
        path = self.write('ScriptingAssemblies.json', text)
        mods.register_assembly(path)
        got = self.read(path).decode()
        self.assertEqual(got, json.dumps(dict(names=sa_data(4)['names'] + ['PTMod.dll'],
                                              types=sa_data(4)['types'] + [16]), indent=2).replace('\n', '\r\n'))
        self.assertNotIn('\n', got.replace('\r\n', ''))
        mods.unregister_assembly(path)
        self.assertEqual(self.read(path).decode(), text)

    def test_pretty_printed_runtime_init_entry_copies_neighbour_layout(self):
        base = {'root': [entry(1), entry(2)]}
        for style in (dict(indent=2), dict(indent=4), dict(indent='\t')):
            with self.subTest(style=style):
                text = json.dumps(base, **style)
                path = self.write('RuntimeInitializeOnLoads.json', text)
                mods.register_runtime_init(path)
                expected = json.dumps({'root': base['root'] + [mods.runtime_init_entry()]}, **style)
                self.assertEqual(self.read(path).decode(), expected)
                mods.unregister_runtime_init(path)
                self.assertEqual(self.read(path).decode(), text)

    def test_spaced_separators(self):
        text = '{"root": [{"assemblyName": "A", "className": "B", "loadTypes": 1}]}'
        path = self.write('RuntimeInitializeOnLoads.json', text)
        mods.register_runtime_init(path)
        self.assertIn('}, {"assemblyName": "PTMod", "nameSpace": "PTMod.Core", "className": "ModBootstrap"',
                      self.read(path).decode())
        mods.unregister_runtime_init(path)
        self.assertEqual(self.read(path).decode(), text)

    def test_hand_edited_whitespace(self):
        text = '  { "names" : [ "a.dll" ,\n\t"b.dll" ] , "types" :[ 0 , 16 ]  }\n'
        path = self.write('ScriptingAssemblies.json', text)
        mods.register_assembly(path)
        self.assertEqual(json.loads(self.read(path))['names'], ['a.dll', 'b.dll', 'PTMod.dll'])
        mods.unregister_assembly(path)
        self.assertEqual(self.read(path).decode(), text)

    def test_compact_output_equals_json_dumps(self):
        ri = {'root': [entry(i) for i in range(5)]}
        sa = sa_data(4)
        for name, doc, register, extra in (
                ('ScriptingAssemblies.json', sa, mods.register_assembly,
                 {'names': sa['names'] + ['PTMod.dll'], 'types': sa['types'] + [16]}),
                ('RuntimeInitializeOnLoads.json', ri, mods.register_runtime_init,
                 {'root': ri['root'] + [mods.runtime_init_entry()]})):
            path = self.write(name, json.dumps(doc, separators=(',', ':')))
            register(path)
            self.assertEqual(self.read(path).decode(), json.dumps(extra, separators=(',', ':')))

    LAYOUTS = [dict(separators=(',', ':')), dict(), dict(indent=0), dict(indent=1), dict(indent=2), dict(indent=4),
               dict(indent='\t'), dict(indent=2, separators=(',', ' : '))]

    @staticmethod
    def cases(n_names, n_root):
        """(file name, document, intended document after registration, add, remove, is_registered) for both files."""
        sa, ri = sa_data(n_names), {'root': [entry(i) for i in range(n_root)]}
        return (('ScriptingAssemblies.json', sa, {'names': sa['names'] + ['PTMod.dll'], 'types': sa['types'] + [16]},
                 mods.add_assembly_text, mods.remove_assembly_text, mods.assembly_registered_text),
                ('RuntimeInitializeOnLoads.json', ri, {'root': ri['root'] + [mods.runtime_init_entry()]},
                 mods.add_runtime_init_text, mods.remove_runtime_init_text, mods.runtime_init_registered_text))

    def test_text_round_trip_over_many_layouts(self):
        """Layout x size x line ending x trailing newline: adding parses to the intended document, adding again
        returns the very same text, removing restores the exact original."""
        count = 0
        for style in self.LAYOUTS:
            for n_names in (0, 1, 2, 5):
                for n_root in (0, 1, 3):
                    for eol in ('\n', '\r\n'):
                        for tail in ('', '\n'):
                            for fname, doc, want, add, remove, registered in self.cases(n_names, n_root):
                                original = json.dumps(doc, **style).replace('\n', eol) + tail
                                label = (style, n_names, n_root, eol, tail, fname)
                                self.assertFalse(registered(original), label)
                                added = add(original)
                                self.assertEqual(json.loads(added), want, label)
                                self.assertTrue(registered(added), label)
                                self.assertIs(add(added), added, label)
                                self.assertEqual(remove(added), original, label)
                                self.assertIs(remove(original), original, label)
                                count += 1
        self.assertEqual(count, len(self.LAYOUTS) * 4 * 3 * 2 * 2 * 2)

    def test_file_round_trip_with_bom_and_line_endings(self):
        for style in (dict(separators=(',', ':')), dict(indent=2)):
            for eol in ('\n', '\r\n'):
                for bom in (b'', BOM):
                    for fname, doc, want, *_ in self.cases(3, 2):
                        reg, unreg = ((mods.register_assembly, mods.unregister_assembly) if fname.startswith('Scripting')
                                      else (mods.register_runtime_init, mods.unregister_runtime_init))
                        original = bom + json.dumps(doc, **style).replace('\n', eol).encode('utf-8')
                        path = self.write(fname, original)
                        label = (style, eol, bom, fname)
                        self.assertTrue(reg(path), label)
                        got = self.read(path)
                        self.assertEqual(got.startswith(BOM), bool(bom), label)
                        self.assertEqual(json.loads(got[len(bom):].decode()), want, label)
                        self.assertFalse(reg(path), label)
                        self.assertEqual(self.read(path), got, label)
                        self.assertTrue(unreg(path), label)
                        self.assertEqual(self.read(path), original, label)


class BrokenFileTests(TempDirCase):
    SA_BROKEN = [
        ('empty', b''),
        ('blank', b'  \r\n'),
        ('truncated', b'{"names":["a.dll"],"types":[16]'),
        ('trailing comma', b'{"names":["a.dll",],"types":[16]}'),
        ('missing comma', b'{"names":["a.dll"] "types":[16]}'),
        ('single quotes', b"{'names':['a.dll'],'types':[16]}"),
        ('extra data', b'{"names":["a.dll"],"types":[16]} x'),
        ('not json', b'MZ\x90\x00 definitely not json'),
        ('root is an array', b'[1,2]'),
        ('no types', b'{"names":["a.dll"]}'),
        ('no names', b'{"types":[16]}'),
        ('lengths differ', b'{"names":["a.dll"],"types":[16,16]}'),
        ('names not an array', b'{"names":"a.dll","types":16}'),
        ('names not strings', b'{"names":[1],"types":[16]}'),
        ('duplicate key', b'{"names":["a.dll"],"types":[16],"names":["b.dll"]}'),
        ('not utf-8', b'{"names":["\xff\xfe.dll"],"types":[16]}'),
    ]

    def check(self, func, name, data, *args):
        path = self.write(name, data)
        with self.assertRaises(mods.ModsJsonError) as cm:
            func(path, *args)
        msg = str(cm.exception)
        self.assertRegex(msg, CYRILLIC)
        self.assertIn(name, msg)
        self.assertEqual(self.read(path), data)
        self.assertEqual(self.files(), [name])          # no leftover temp files
        return msg

    def test_broken_scripting_assemblies_register_and_unregister(self):
        for label, data in self.SA_BROKEN:
            for func in (mods.register_assembly, mods.unregister_assembly):
                with self.subTest(case=label, op=func.__name__):
                    if label == 'duplicate key' and func is mods.unregister_assembly:
                        continue                         # last key wins in the parse; nothing to remove, so no edit
                    self.check(func, 'ScriptingAssemblies.json', data)

    def test_broken_runtime_init(self):
        cases = [('truncated', b'{"root":[{"assemblyName":"A"'), ('no root', b'{"entries":[]}'),
                 ('root is an object', b'{"root":{}}'), ('empty', b''), ('bom only', BOM)]
        for label, data in cases:
            for func in (mods.register_runtime_init, mods.unregister_runtime_init):
                with self.subTest(case=label, op=func.__name__):
                    self.check(func, 'RuntimeInitializeOnLoads.json', data)

    def test_syntax_error_message_has_position_and_russian_reason(self):
        msg = self.check(mods.register_assembly, 'ScriptingAssemblies.json', b'{\n  "names": [\n    "a.dll",\n  ],\n}')
        self.assertIn('строка 4', msg)
        self.assertIn('столбец', msg)
        self.assertIn('не изменён', msg)
        self.assertNotIn('Expecting', msg)

    def test_missing_file(self):
        path = os.path.join(self.dir, 'ScriptingAssemblies.json')
        for func in (mods.register_assembly, mods.unregister_assembly, mods.register_runtime_init,
                     mods.unregister_runtime_init, mods.is_assembly_registered, mods.is_runtime_init_registered):
            with self.subTest(op=func.__name__):
                with self.assertRaises(mods.ModsJsonError) as cm:
                    func(path)
                self.assertRegex(str(cm.exception), CYRILLIC)
        self.assertEqual(self.files(), [])

    def test_text_level_errors_use_the_given_label(self):
        with self.assertRaises(mods.ModsJsonError) as cm:
            mods.add_assembly_text('{"names":', mods.MOD_ASSEMBLY, 16, 'X.json')
        self.assertIn('X.json', str(cm.exception))

    def test_error_is_a_plain_exception_not_a_sys_exit(self):
        self.assertTrue(issubclass(mods.ModsJsonError, Exception))
        self.assertFalse(issubclass(mods.ModsJsonError, SystemExit))


class AtomicWriteTests(TempDirCase):
    def test_no_temp_files_remain_after_success(self):
        path = self.write('ScriptingAssemblies.json', SA)
        mods.register_assembly(path)
        mods.unregister_assembly(path)
        self.assertEqual(self.files(), ['ScriptingAssemblies.json'])

    def test_temp_file_is_in_the_same_folder_and_replaced_atomically(self):
        path = self.write('ScriptingAssemblies.json', SA)
        calls = []
        real = os.replace

        def spy(src, dst):
            calls.append((os.path.dirname(os.path.abspath(src)), os.path.abspath(dst), self.read(src)))
            return real(src, dst)

        with mock.patch('ptru.mods.os.replace', side_effect=spy):
            mods.register_assembly(path)
        self.assertEqual(len(calls), 1)
        src_dir, dst, content = calls[0]
        self.assertEqual(src_dir, os.path.dirname(os.path.abspath(path)))
        self.assertEqual(dst, os.path.abspath(path))
        self.assertEqual(content.decode(), SA_REGISTERED)       # the temp file already held the complete new text

    def test_failed_replace_keeps_the_original_and_cleans_up(self):
        path = self.write('ScriptingAssemblies.json', SA)
        with mock.patch('ptru.mods.os.replace', side_effect=PermissionError(13, 'Отказано в доступе')):
            with self.assertRaises(mods.ModsJsonError) as cm:
                mods.register_assembly(path)
        self.assertRegex(str(cm.exception), CYRILLIC)
        self.assertIn('Закройте игру', str(cm.exception))
        self.assertEqual(self.read(path).decode(), SA)
        self.assertEqual(self.files(), ['ScriptingAssemblies.json'])

    def test_failed_write_keeps_the_original(self):
        path = self.write('ScriptingAssemblies.json', SA)
        with mock.patch('ptru.mods.os.fsync', side_effect=OSError(28, 'No space left on device')):
            with self.assertRaises(mods.ModsJsonError):
                mods.register_assembly(path)
        self.assertEqual(self.read(path).decode(), SA)
        self.assertEqual(self.files(), ['ScriptingAssemblies.json'])

    def test_write_atomic_creates_and_replaces(self):
        path = os.path.join(self.dir, 'x.bin')
        mods.write_atomic(path, b'one')
        self.assertEqual(self.read(path), b'one')
        mods.write_atomic(path, b'two\r\n')
        self.assertEqual(self.read(path), b'two\r\n')
        self.assertEqual(self.files(), ['x.bin'])


class BackupTests(TempDirCase):
    def test_copies_once_into_a_new_folder(self):
        src = self.write('Processor Tycoon_Data/ScriptingAssemblies.json', SA)
        bdir = os.path.join(self.dir, '_original_en')
        self.assertTrue(mods.backup_json(src, bdir))
        self.assertEqual(self.read(os.path.join(bdir, 'ScriptingAssemblies.json')), SA.encode())
        self.assertFalse(mods.backup_json(src, bdir))
        self.assertEqual(self.files('_original_en'), ['ScriptingAssemblies.json'])

    def test_existing_backup_is_never_overwritten(self):
        src = self.write('Processor Tycoon_Data/RuntimeInitializeOnLoads.json', RI)
        bdir = os.path.join(self.dir, '_original_en')
        mods.backup_json(src, bdir)
        mods.register_runtime_init(src)                          # source is now modified
        self.assertFalse(mods.backup_json(src, bdir))
        self.assertEqual(self.read(os.path.join(bdir, 'RuntimeInitializeOnLoads.json')), RI.encode())

    def test_backup_is_byte_exact_even_for_odd_files(self):
        data = BOM + b'{"names":[],"types":[]}\r\n\x00'
        src = self.write('a/ScriptingAssemblies.json', data)
        bdir = os.path.join(self.dir, 'b')
        mods.backup_json(src, bdir)
        self.assertEqual(self.read(os.path.join(bdir, 'ScriptingAssemblies.json')), data)

    def test_missing_source(self):
        with self.assertRaises(mods.ModsJsonError) as cm:
            mods.backup_json(os.path.join(self.dir, 'nope.json'), os.path.join(self.dir, 'b'))
        self.assertRegex(str(cm.exception), CYRILLIC)
        self.assertEqual(self.files(), [])


class GameFolderScenarioTests(TempDirCase):
    """Install -> install again -> remove on a fake game folder (the JSON half of AC-20.1 / AC-20.2)."""

    def test_install_twice_then_remove_gives_back_the_originals(self):
        sa_path = self.write('Processor Tycoon_Data/ScriptingAssemblies.json', SA)
        ri_path = self.write('Processor Tycoon_Data/RuntimeInitializeOnLoads.json', RI)
        self.assertEqual(os.path.normpath(mods.scripting_assemblies_path(self.dir)), os.path.normpath(sa_path))
        self.assertEqual(os.path.normpath(mods.runtime_init_path(self.dir)), os.path.normpath(ri_path))
        bdir = os.path.join(self.dir, mods.BACKUP_DIR)
        for _ in range(2):
            mods.backup_json(sa_path, bdir)
            mods.backup_json(ri_path, bdir)
            mods.register_assembly(sa_path)
            mods.register_runtime_init(ri_path)
        self.assertEqual(self.read(sa_path).decode().count('PTMod.dll'), 1)
        self.assertEqual(self.read(ri_path).decode().count('ModBootstrap'), 1)
        self.assertEqual(self.read(os.path.join(bdir, 'ScriptingAssemblies.json')), SA.encode())
        self.assertEqual(self.read(os.path.join(bdir, 'RuntimeInitializeOnLoads.json')), RI.encode())
        mods.unregister_assembly(sa_path)
        mods.unregister_runtime_init(ri_path)
        self.assertEqual(self.read(sa_path), self.read(os.path.join(bdir, 'ScriptingAssemblies.json')))
        self.assertEqual(self.read(ri_path), self.read(os.path.join(bdir, 'RuntimeInitializeOnLoads.json')))


class StdlibOnlyTests(unittest.TestCase):
    def test_mods_py_imports_only_the_standard_library(self):
        with open(os.path.join(ROOT, 'ptru', 'mods.py'), encoding='utf-8') as f:
            tree = ast.parse(f.read())
        names = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                names.update(a.name.split('.')[0] for a in node.names)
            elif isinstance(node, ast.ImportFrom):
                self.assertEqual(node.level, 0, 'relative import of a ptru module')
                names.add(node.module.split('.')[0])
        self.assertTrue(names)
        stdlib_dir = os.path.dirname(os.__file__)
        for name in sorted(names):
            with self.subTest(module=name):
                known = getattr(sys, 'stdlib_module_names', None)
                if known is not None:
                    self.assertIn(name, known)
                else:
                    spec = importlib.util.find_spec(name)
                    self.assertTrue(spec.origin in ('built-in', 'frozen') or
                                    os.path.abspath(spec.origin).startswith(stdlib_dir), spec.origin)


if __name__ == '__main__':
    unittest.main()
