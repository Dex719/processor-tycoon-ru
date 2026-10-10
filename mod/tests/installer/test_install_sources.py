"""Tests of where `install.py --with-mods` gets its DLLs and how --mods-only behaves (TSK-068, FR-20 p. 9-10):
find_mod_dlls (--mod-dir, mod/build, next to install.py), SHA256SUMS.txt, the build with csc through mod/build.py,
the instruction when there is nothing to install, and --mods-only leaving data.unity3d and Assembly-CSharp.dll alone.

Run from the repository root:  python -m unittest discover -s mod/tests/installer
Temporary folders play the game and the repository. The "mod/build.py" of the fake repository is a few lines of Python
that records how it was called and writes dummy DLLs: no csc is started, the real game and the network are not used.
"""
import ast, contextlib, hashlib, io, json, os, re, shutil, sys, tempfile, types, unittest
from unittest import mock

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

import install  # noqa: E402
from ptru import mods  # noqa: E402

CYRILLIC = re.compile('[А-Яа-яЁё]')

DATA_UNITY3D = b'data.unity3d placeholder\x00' * 8
ASM_ORIGINAL = b'MZ pristine Assembly-CSharp 0.2.3\x00' * 4
ASM_TRANSLATED = b'MZ the same assembly after the translation\x00' * 4
SA = '{"names":["UnityEngine.dll","Assembly-CSharp.dll"],"types":[2,16]}'
RI = ('{"root":[{"assemblyName":"Assembly-CSharp","nameSpace":"","className":"GameManager","methodName":"Boot",'
      '"loadTypes":1,"isUnityClass":false}]}\r\n')


def sha(data):
    return hashlib.sha256(data).hexdigest()


META = {'game': 'Processor Tycoon', 'game_version': '0.2.3', 'unity': '6000.0.65f1',
        'original_sha256': {'data.unity3d': sha(DATA_UNITY3D), 'Assembly-CSharp.dll': sha(ASM_ORIGINAL)}}

# The stand-in for mod/build.py. mod/mode.txt picks the behaviour: ok (default), fail, nodll, sleep.
FAKE_BUILD_PY = r'''
import json, os, sys, time
mod = os.path.dirname(os.path.abspath(__file__))
build = os.path.join(mod, 'build')
mode = 'ok'
if os.path.exists(os.path.join(mod, 'mode.txt')):
    mode = open(os.path.join(mod, 'mode.txt')).read().strip()
with open(os.path.join(mod, 'call.json'), 'w') as f:
    json.dump({'argv': sys.argv[1:], 'cwd': os.getcwd()}, f)
if mode == 'fail':
    sys.exit(3)
if mode == 'sleep':
    time.sleep(60)
if mode == 'ok':
    os.makedirs(build, exist_ok=True)
    open(os.path.join(build, 'PTMod.dll'), 'wb').write(b'MZ PTMod built by the fake build.py')
    open(os.path.join(build, '0Harmony.dll'), 'wb').write(b'MZ Harmony copied by the fake build.py')
'''
BUILT_MOD, BUILT_HARMONY = b'MZ PTMod built by the fake build.py', b'MZ Harmony copied by the fake build.py'


class Fixture(unittest.TestCase):
    """A fake game and a fake repository. The repository starts without any DLL."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.root = os.path.realpath(self._tmp.name)
        self.game = os.path.join(self.root, 'Game')
        self.repo = os.path.join(self.root, 'Repo')
        self.build = os.path.join(self.repo, 'mod', 'build')
        self.data = os.path.join(self.game, 'Processor Tycoon_Data')
        self.managed = os.path.join(self.data, 'Managed')
        self.backup = os.path.join(self.game, '_original_en')
        self.write(os.path.join(self.data, 'data.unity3d'), DATA_UNITY3D)
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.write(os.path.join(self.data, 'ScriptingAssemblies.json'), SA)
        self.write(os.path.join(self.data, 'RuntimeInitializeOnLoads.json'), RI)
        self.write(os.path.join(self.repo, 'translation', 'meta.json'), json.dumps(META))

    def write(self, path, data):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'wb') as f:
            f.write(data if isinstance(data, bytes) else data.encode('utf-8'))
        return path

    def read(self, *parts):
        with open(os.path.join(*parts), 'rb') as f:
            return f.read()

    def put_dlls(self, folder, tag, sums=None):
        """PTMod.dll and 0Harmony.dll whose content says where they are; SHA256SUMS.txt if `sums` is given."""
        mod, harmony = ('MZ PTMod from %s' % tag).encode(), ('MZ Harmony from %s' % tag).encode()
        self.write(os.path.join(folder, 'PTMod.dll'), mod)
        self.write(os.path.join(folder, '0Harmony.dll'), harmony)
        if sums is not None:
            self.write(os.path.join(folder, 'SHA256SUMS.txt'), sums)
        return mod, harmony

    def snapshot(self, folder=None):
        folder = folder or self.game
        shot = {}
        for base, dirs, files in os.walk(folder):
            for name in dirs:
                shot[os.path.relpath(os.path.join(base, name), folder)] = 'DIR'
            for name in files:
                path = os.path.join(base, name)
                shot[os.path.relpath(path, folder)] = sha(self.read(path))
        return shot

    def with_build_py(self, mode='ok'):
        self.write(os.path.join(self.repo, 'mod', 'build.py'), FAKE_BUILD_PY)
        self.write(os.path.join(self.repo, 'mod', 'mode.txt'), mode)

    def build_call(self):
        """How the fake mod/build.py was called, None if it was not."""
        path = os.path.join(self.repo, 'mod', 'call.json')
        return json.loads(self.read(path).decode()) if os.path.exists(path) else None

    @contextlib.contextmanager
    def translation(self):
        calls = []
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(install, 'install', side_effect=calls.append))
            stack.enter_context(mock.patch.object(install, 'require_original_dll'))
            yield calls

    def run_main(self, *args):
        out = io.StringIO()
        with mock.patch.object(install, 'HERE', self.repo), contextlib.redirect_stdout(out):
            try:
                install.main(list(args))
                code = 0
            except SystemExit as e:
                code = e.code
        return code, out.getvalue()


class FindModDllsTests(Fixture):
    def setUp(self):
        super().setUp()
        self.arg_dir = os.path.join(self.root, 'Downloaded release')

    def test_order_is_mod_dir_then_mod_build_then_next_to_install_py(self):
        self.assertIsNone(mods.find_mod_dlls(self.repo))
        beside = self.put_dlls(self.repo, 'beside install.py')
        found = mods.find_mod_dlls(self.repo)
        self.assertEqual((self.read(found.mod), found.label), (beside[0], 'папка установщика'))
        self.assertEqual(os.path.dirname(found.mod), self.repo)

        built = self.put_dlls(self.build, 'mod/build')
        found = mods.find_mod_dlls(self.repo)
        self.assertEqual((self.read(found.mod), self.read(found.harmony)), built)
        self.assertEqual(found.label, 'mod\\build')

        given = self.put_dlls(self.arg_dir, '--mod-dir')
        found = mods.find_mod_dlls(self.repo, self.arg_dir)
        self.assertEqual((self.read(found.mod), self.read(found.harmony)), given)
        self.assertEqual((found.label, found.folder), ('--mod-dir', self.arg_dir))

    def test_without_the_argument_the_arguments_folder_is_not_consulted(self):
        self.put_dlls(self.arg_dir, '--mod-dir')
        self.assertIsNone(mods.find_mod_dlls(self.repo))

    def test_a_folder_with_only_one_of_the_two_dlls_does_not_count(self):
        self.write(os.path.join(self.build, 'PTMod.dll'), b'MZ lonely')
        beside = self.put_dlls(self.repo, 'beside install.py')
        found = mods.find_mod_dlls(self.repo)
        self.assertEqual(os.path.dirname(found.mod), self.repo)         # never a pair made of two different folders
        self.assertEqual(self.read(found.harmony), beside[1])

    def test_only_a_wrong_mod_dir_is_an_error_naming_what_is_missing(self):
        self.write(os.path.join(self.arg_dir, 'PTMod.dll'), b'MZ lonely')
        self.put_dlls(self.build, 'mod/build')                          # a good build must not hide the mistake
        with self.assertRaises(mods.ModsInstallError) as cm:
            mods.find_mod_dlls(self.repo, self.arg_dir)
        text = str(cm.exception)
        self.assertIn('0Harmony.dll', text)
        self.assertIn('--mod-dir', text)
        self.assertIn('не тронуты', text)
        self.assertRegex(text, CYRILLIC)

        with self.assertRaises(mods.ModsInstallError) as cm:
            mods.find_mod_dlls(self.repo, os.path.join(self.root, 'no such folder'))
        self.assertIn('не существует', str(cm.exception))

    def test_quotes_around_the_argument_are_ignored(self):
        self.put_dlls(self.arg_dir, '--mod-dir')
        found = mods.find_mod_dlls(self.repo, '"%s"' % self.arg_dir)
        self.assertEqual(found.folder, self.arg_dir)

    def test_a_checksum_file_beside_the_dlls_is_noticed(self):
        self.put_dlls(self.arg_dir, '--mod-dir')
        self.assertIsNone(mods.find_mod_dlls(self.repo, self.arg_dir).checksums)
        self.write(os.path.join(self.arg_dir, 'SHA256SUMS.txt'), 'x')
        self.assertEqual(mods.find_mod_dlls(self.repo, self.arg_dir).checksums,
                         os.path.join(self.arg_dir, 'SHA256SUMS.txt'))


class ChecksumTests(Fixture):
    def sums_for(self, folder, eol='\n', star=False, bom=False):
        lines = ['%s %s%s' % (sha(self.read(folder, n)), '*' if star else ' ', n) for n in ('PTMod.dll', '0Harmony.dll')]
        return ('\ufeff' if bom else '') + eol.join(lines) + eol

    def test_parse_accepts_sha256sum_output_in_both_modes_and_any_line_ending(self):
        h1, h2 = 'a' * 64, 'B' * 64
        for text in ('%s  PTMod.dll\n%s *0Harmony.dll\n' % (h1, h2), '%s  PTMod.dll\r\n%s *0Harmony.dll\r\n' % (h1, h2),
                     '\ufeff%s  PTMod.dll\n\n# comment\n%s  .\\build\\0Harmony.dll' % (h1, h2)):
            with self.subTest(text=text):
                self.assertEqual(mods.parse_checksums(text), {'PTMod.dll': h1, '0Harmony.dll': h2.lower()})

    def test_parse_ignores_lines_that_are_not_checksums(self):
        self.assertEqual(mods.parse_checksums('hello\n' + 'a' * 63 + '  short.dll\n' + 'g' * 64 + '  nothex.dll\n'), {})

    def test_matching_sums_are_verified_and_reported(self):
        for kwargs in ({}, {'eol': '\r\n'}, {'star': True}, {'bom': True}):
            with self.subTest(**kwargs):
                self.put_dlls(self.build, 'release')
                folder = self.build
                self.write(os.path.join(folder, 'SHA256SUMS.txt'), self.sums_for(folder, **kwargs))
                dlls = mods.find_mod_dlls(self.repo)
                self.assertEqual(mods.verify_checksums(dlls), (['PTMod.dll', '0Harmony.dll'], []))

    def test_a_dll_that_differs_from_its_line_is_refused(self):
        self.put_dlls(self.build, 'release')
        self.write(os.path.join(self.build, 'SHA256SUMS.txt'), self.sums_for(self.build))
        self.write(os.path.join(self.build, 'PTMod.dll'), b'MZ tampered with')
        dlls = mods.find_mod_dlls(self.repo)
        with self.assertRaises(mods.ModsInstallError) as cm:
            mods.verify_checksums(dlls)
        text = str(cm.exception)
        self.assertIn('PTMod.dll', text)
        self.assertNotIn('0Harmony.dll:', text)
        self.assertIn('--force', text)
        self.assertRegex(text, CYRILLIC)

    def test_force_turns_the_mismatch_into_a_warning(self):
        self.put_dlls(self.build, 'release')
        self.write(os.path.join(self.build, 'SHA256SUMS.txt'), self.sums_for(self.build))
        self.write(os.path.join(self.build, 'PTMod.dll'), b'MZ tampered with')
        verified, warnings = mods.verify_checksums(mods.find_mod_dlls(self.repo), force=True)
        self.assertEqual(verified, ['0Harmony.dll'])
        self.assertEqual(len(warnings), 1)
        self.assertIn('--force', warnings[0])

    def test_a_missing_line_or_an_unreadable_file_only_warns(self):
        self.put_dlls(self.build, 'release')
        self.write(os.path.join(self.build, 'SHA256SUMS.txt'), '%s  PTMod.dll\n' % sha(self.read(self.build, 'PTMod.dll')))
        verified, warnings = mods.verify_checksums(mods.find_mod_dlls(self.repo))
        self.assertEqual(verified, ['PTMod.dll'])
        self.assertIn('0Harmony.dll', warnings[0])

        self.write(os.path.join(self.build, 'SHA256SUMS.txt'), 'this is not a checksum file')
        verified, warnings = mods.verify_checksums(mods.find_mod_dlls(self.repo))
        self.assertEqual(verified, [])
        self.assertEqual(len(warnings), 1)
        self.assertRegex(warnings[0], CYRILLIC)

    def test_without_a_sums_file_there_is_nothing_to_check(self):
        self.put_dlls(self.build, 'built here')
        self.assertEqual(mods.verify_checksums(mods.find_mod_dlls(self.repo)), ([], []))

    def test_a_release_folder_made_by_build_py_passes(self):
        # The layout mod/build.py --release writes: PTMod.dll, 0Harmony.dll, SHA256SUMS.txt in sha256sum format.
        folder = self.arg_dir_release()
        dlls = mods.find_mod_dlls(self.repo, folder)
        self.assertEqual(os.path.basename(dlls.checksums), 'SHA256SUMS.txt')
        self.assertEqual(mods.verify_checksums(dlls)[0], ['PTMod.dll', '0Harmony.dll'])

    def arg_dir_release(self):
        folder = os.path.join(self.root, 'release', 'PTMod-0.1.0')
        if not os.path.exists(os.path.join(folder, 'SHA256SUMS.txt')):
            self.put_dlls(folder, 'release')
            self.write(os.path.join(folder, 'SHA256SUMS.txt'), self.sums_for(folder))
        return folder

    def test_install_refuses_on_a_mismatch_and_leaves_the_game_alone(self):
        folder = self.arg_dir_release()
        self.write(os.path.join(folder, '0Harmony.dll'), b'MZ replaced after the sums were made')
        before = self.snapshot()
        with self.translation() as calls:
            code, _ = self.run_main(self.game, '--with-mods', '--mod-dir', folder)
        self.assertIsInstance(code, str)
        self.assertIn('0Harmony.dll', code)
        self.assertIn('SHA256SUMS.txt', code)
        self.assertEqual(calls, [])
        self.assertEqual(self.snapshot(), before)

    def test_install_goes_on_with_force_and_says_so(self):
        folder = self.arg_dir_release()
        self.write(os.path.join(folder, '0Harmony.dll'), b'MZ replaced after the sums were made')
        with self.translation():
            code, out = self.run_main(self.game, '--with-mods', '--force', '--mod-dir', folder)
        self.assertEqual(code, 0)
        self.assertIn('ВНИМАНИЕ', out)
        self.assertEqual(self.read(self.managed, '0Harmony.dll'), b'MZ replaced after the sums were made')

    def test_install_reports_matching_sums(self):
        folder = self.arg_dir_release()
        with self.translation():
            code, out = self.run_main(self.game, '--with-mods', '--mod-dir', folder)
        self.assertEqual(code, 0)
        self.assertIn('Контрольные суммы SHA256SUMS.txt совпали', out)


class CscTests(Fixture):
    def test_pt_csc_comes_first_and_the_known_visual_studio_places_follow(self):
        with mock.patch.dict(os.environ, {'PT_CSC': r'D:\tools\csc.exe'}):
            candidates = mods.csc_candidates()
        self.assertEqual(candidates[0], r'D:\tools\csc.exe')
        self.assertEqual(len(candidates), len(set(candidates)))
        self.assertTrue(any('BuildTools' in c and '2022' in c and c.endswith('csc.exe') for c in candidates))

    def test_the_first_working_candidate_wins(self):
        with mock.patch.object(mods, 'csc_candidates', return_value=['a.exe', 'b.exe', 'c.exe']), \
                mock.patch.object(mods, '_csc_works', side_effect=lambda p: p != 'a.exe'):
            self.assertEqual(mods.find_csc(), 'b.exe')

    def test_none_when_the_machine_has_no_compiler(self):
        with mock.patch.object(mods, 'csc_candidates', return_value=['a.exe', 'b.exe']), \
                mock.patch.object(mods, '_csc_works', return_value=False):
            self.assertIsNone(mods.find_csc())
        self.assertFalse(mods._csc_works(os.path.join(self.root, 'no', 'csc.exe')))


class BuildWithCscTests(Fixture):
    def test_runs_mod_build_py_for_this_game_with_the_found_compiler(self):
        self.with_build_py()
        log = []
        dlls = mods.build_with_csc(self.repo, self.game, csc=r'C:\x\csc.exe', log=log.append)
        call = self.build_call()
        self.assertEqual(call['argv'], ['--game-dir', self.game, '--csc', r'C:\x\csc.exe', '--release'])
        self.assertEqual(os.path.realpath(call['cwd']), self.repo)
        self.assertEqual((self.read(dlls.mod), self.read(dlls.harmony)), (BUILT_MOD, BUILT_HARMONY))
        self.assertEqual(os.path.dirname(dlls.mod), self.build)
        self.assertTrue(any('mod' in line and 'build.py' in line for line in log), log)

    def test_the_debug_build_can_be_asked_for(self):
        self.with_build_py()
        mods.build_with_csc(self.repo, self.game, csc='csc', release=False, log=lambda text: None)
        self.assertNotIn('--release', self.build_call()['argv'])

    def test_the_compiler_is_searched_when_not_given(self):
        self.with_build_py()
        with mock.patch.object(mods, 'find_csc', return_value='found-csc.exe'):
            mods.build_with_csc(self.repo, self.game, log=lambda text: None)
        self.assertEqual(self.build_call()['argv'][3], 'found-csc.exe')
        with mock.patch.object(mods, 'find_csc', return_value=None):
            with self.assertRaises(mods.ModsInstallError) as cm:
                mods.build_with_csc(self.repo, self.game, log=lambda text: None)
        self.assertIn('GitHub Release', str(cm.exception))

    def test_a_failed_build_is_an_error_with_the_way_out(self):
        self.with_build_py('fail')
        with self.assertRaises(mods.ModsInstallError) as cm:
            mods.build_with_csc(self.repo, self.game, csc='csc', log=lambda text: None)
        text = str(cm.exception)
        self.assertIn('код 3', text)
        self.assertIn('GitHub Release', text)
        self.assertIn('не тронуты', text)

    def test_a_build_that_leaves_no_dlls_is_an_error(self):
        self.with_build_py('nodll')
        with self.assertRaises(mods.ModsInstallError) as cm:
            mods.build_with_csc(self.repo, self.game, csc='csc', log=lambda text: None)
        self.assertIn('PTMod.dll', str(cm.exception))

    def test_a_build_that_hangs_is_stopped(self):
        self.with_build_py('sleep')
        with mock.patch.object(mods, 'BUILD_TIMEOUT', 1):
            with self.assertRaises(mods.ModsInstallError) as cm:
                mods.build_with_csc(self.repo, self.game, csc='csc', log=lambda text: None)
        self.assertIn('остановлена', str(cm.exception))

    def test_without_build_py_the_answer_is_the_instruction(self):
        with self.assertRaises(mods.ModsInstallError) as cm:
            mods.build_with_csc(self.repo, self.game, csc='csc', log=lambda text: None)
        self.assertIn('GitHub Release', str(cm.exception))


class NothingToInstallTests(Fixture):
    """AC-20.5: no DLLs and no csc: stop with the instruction, the game folder untouched."""

    def assert_instruction(self, text):
        self.assertRegex(text, CYRILLIC)
        self.assertIn('GitHub Release', text)
        self.assertIn(mods.RELEASES_URL, text)
        self.assertIn('--mod-dir', text)
        self.assertIn('PTMod.dll', text)
        self.assertIn('0Harmony.dll', text)
        self.assertIn('не тронуты', text)

    def test_no_dlls_and_no_csc(self):
        self.with_build_py()
        before = self.snapshot()
        with self.translation() as calls, mock.patch.object(mods, 'find_csc', return_value=None):
            code, _ = self.run_main(self.game, '--with-mods')
        self.assertIsInstance(code, str)
        self.assert_instruction(code)
        self.assertIn('csc.exe', code)
        self.assertEqual(calls, [])                                    # the translation was not started
        self.assertIsNone(self.build_call())                           # and no build was attempted
        self.assertEqual(self.snapshot(), before)

    def test_no_dlls_and_no_build_py_even_with_csc(self):
        before = self.snapshot()
        with self.translation() as calls, mock.patch.object(mods, 'find_csc', return_value='csc.exe'):
            code, _ = self.run_main(self.game, '--with-mods', '--mods-only')
        self.assert_instruction(code)
        self.assertIn('build.py', code)
        self.assertEqual((calls, self.snapshot()), ([], before))

    def test_in_a_real_process_the_exit_code_is_not_zero_and_the_game_is_untouched(self):
        import subprocess
        # A copy of install.py with nothing around it (no mod/build, no build.py, no DLLs): the real command line.
        # --force passes the version gate, because the fake game cannot match the hashes of the real meta.json.
        installer = os.path.join(self.root, 'Installer')
        shutil.copytree(os.path.join(ROOT, 'ptru'), os.path.join(installer, 'ptru'), ignore=shutil.ignore_patterns('__pycache__'))
        shutil.copy(os.path.join(ROOT, 'install.py'), installer)
        os.makedirs(os.path.join(installer, 'translation'))
        shutil.copy(os.path.join(ROOT, 'translation', 'meta.json'), os.path.join(installer, 'translation'))
        before = self.snapshot()
        proc = subprocess.run([sys.executable, os.path.join(installer, 'install.py'), self.game, '--with-mods', '--mods-only',
                               '--force'], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                              env=dict(os.environ, PYTHONIOENCODING='utf-8'), timeout=120)
        self.assertEqual(proc.returncode, 1)
        self.assert_instruction(proc.stderr.decode('utf-8'))
        self.assertEqual(self.snapshot(), before)


class AutoBuildTests(Fixture):
    """AC-20.5: mod/build is empty, nothing beside install.py, csc is there: install.py runs mod/build.py first."""

    def test_the_mod_is_built_and_then_installed(self):
        self.with_build_py()
        with self.translation() as calls, mock.patch.object(mods, 'find_csc', return_value='fake-csc.exe'):
            code, out = self.run_main(self.game, '--with-mods')
        self.assertEqual(code, 0, out)
        self.assertEqual(calls, [self.game])                           # the full mode: the translation too
        self.assertEqual(self.build_call()['argv'], ['--game-dir', self.game, '--csc', 'fake-csc.exe', '--release'])
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), BUILT_MOD)
        self.assertEqual(self.read(self.managed, '0Harmony.dll'), BUILT_HARMONY)
        self.assertIn('собираю мод', out)
        self.assertIn('Моды установлены', out)

    def test_the_translation_comes_only_after_a_successful_build(self):
        self.with_build_py('fail')
        before = self.snapshot()
        with self.translation() as calls, mock.patch.object(mods, 'find_csc', return_value='fake-csc.exe'):
            code, _ = self.run_main(self.game, '--with-mods')
        self.assertIn('Сборка мода не удалась', code)
        self.assertEqual(calls, [])
        self.assertEqual(self.snapshot(), before)

    def test_no_build_when_the_dlls_are_there(self):
        self.with_build_py()
        built = self.put_dlls(self.build, 'mod/build')
        with self.translation(), mock.patch.object(mods, 'find_csc', return_value='fake-csc.exe'):
            code, _ = self.run_main(self.game, '--with-mods', '--mods-only')
        self.assertEqual(code, 0)
        self.assertIsNone(self.build_call())
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), built[0])

    def test_no_build_when_mod_dir_is_given(self):
        self.with_build_py()
        folder = os.path.join(self.root, 'release')
        given = self.put_dlls(folder, '--mod-dir')
        with self.translation(), mock.patch.object(mods, 'find_csc', return_value='fake-csc.exe'):
            code, _ = self.run_main(self.game, '--with-mods', '--mods-only', '--mod-dir', folder)
        self.assertEqual(code, 0)
        self.assertIsNone(self.build_call())
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), given[0])

    def test_an_unknown_game_version_is_refused_before_the_long_build(self):
        self.with_build_py()
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_TRANSLATED)
        with self.translation() as calls, mock.patch.object(mods, 'find_csc', return_value='fake-csc.exe'):
            code, _ = self.run_main(self.game, '--with-mods', '--mods-only')
        self.assertIn('Версия игры не поддерживается', code)
        self.assertIsNone(self.build_call())
        self.assertEqual(calls, [])

    def test_the_order_found_by_install_py_is_mod_dir_then_build_then_beside(self):
        beside = self.put_dlls(self.repo, 'beside install.py')
        with self.translation(), mock.patch.object(mods, 'find_csc', return_value=None):
            self.assertEqual(self.run_main(self.game, '--with-mods', '--mods-only')[0], 0)
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), beside[0])
        built = self.put_dlls(self.build, 'mod/build')
        with self.translation():
            self.assertEqual(self.run_main(self.game, '--with-mods', '--mods-only')[0], 0)
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), built[0])
        given = self.put_dlls(os.path.join(self.root, 'given'), '--mod-dir')
        with self.translation():
            self.assertEqual(self.run_main(self.game, '--with-mods', '--mods-only', '--mod-dir',
                                           os.path.join(self.root, 'given'))[0], 0)
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), given[0])


class ModsOnlyTests(Fixture):
    def setUp(self):
        super().setUp()
        self.put_dlls(self.build, 'mod/build')

    def stamps(self, *names):
        return {n: (self.read(self.managed if n.endswith('.dll') else self.data, n),
                    os.stat(os.path.join(self.managed if n.endswith('.dll') else self.data, n)).st_mtime_ns)
                for n in names}

    def mods_only(self, *extra):
        with self.translation() as calls:
            code, out = self.run_main(self.game, '--with-mods', '--mods-only', *extra)
        return code, out, calls

    def test_data_unity3d_and_assembly_csharp_stay_byte_for_byte(self):                   # AC-20.3
        before = self.stamps('data.unity3d', 'Assembly-CSharp.dll')
        code, out, calls = self.mods_only()
        self.assertEqual(code, 0, out)
        self.assertEqual(calls, [])
        self.assertEqual(self.stamps('data.unity3d', 'Assembly-CSharp.dll'), before)
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), self.read(self.build, 'PTMod.dll'))
        self.assertEqual(self.read(self.managed, '0Harmony.dll'), self.read(self.build, '0Harmony.dll'))
        self.assertEqual(self.read(self.data, 'ScriptingAssemblies.json').decode().count('"PTMod.dll"'), 1)

    def test_the_translation_code_is_never_reached(self):
        with mock.patch.object(install, 'install', side_effect=AssertionError('translation started')), \
                mock.patch.object(install, 'backup_originals', side_effect=AssertionError('originals copied')), \
                mock.patch.object(install, 'require_original_dll', side_effect=AssertionError('originals read')):
            code, out = self.run_main(self.game, '--with-mods', '--mods-only')
        self.assertEqual(code, 0, out)

    def test_original_en_gets_only_the_json_originals_and_the_manifest(self):
        self.mods_only()
        self.assertEqual(sorted(os.listdir(self.backup)),
                         ['RuntimeInitializeOnLoads.json', 'ScriptingAssemblies.json', 'ptmod_install.json'])
        manifest = json.loads(self.read(self.backup, 'ptmod_install.json').decode('utf-8'))
        self.assertIs(manifest['modsOnly'], True)

    def test_removing_the_entries_gives_back_the_two_json_originals(self):                # what --remove-mods will do
        self.mods_only()
        mods.unregister_assembly(os.path.join(self.data, 'ScriptingAssemblies.json'))
        mods.unregister_runtime_init(os.path.join(self.data, 'RuntimeInitializeOnLoads.json'))
        self.assertEqual(self.read(self.data, 'ScriptingAssemblies.json'), SA.encode())
        self.assertEqual(self.read(self.data, 'RuntimeInitializeOnLoads.json'), RI.encode())
        self.assertEqual(self.read(self.data, 'ScriptingAssemblies.json'), self.read(self.backup, 'ScriptingAssemblies.json'))

    def test_a_game_with_the_translation_keeps_its_translated_files(self):
        # Translation installed earlier: the game holds the patched files, the pristine ones are in _original_en.
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_TRANSLATED)
        self.write(os.path.join(self.data, 'data.unity3d'), b'translated data')
        self.write(os.path.join(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.write(os.path.join(self.backup, 'data.unity3d'), DATA_UNITY3D)
        before = self.stamps('data.unity3d', 'Assembly-CSharp.dll')
        code, out, _ = self.mods_only()
        self.assertEqual(code, 0, out)
        self.assertEqual(self.stamps('data.unity3d', 'Assembly-CSharp.dll'), before)
        self.assertEqual(self.read(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)

    def test_the_version_is_judged_by_the_copy_in_original_en_when_there_is_one(self):     # FR-20 p. 9
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_TRANSLATED)         # would not match on its own
        self.write(os.path.join(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assertEqual(self.mods_only()[0], 0)

    def test_without_original_en_the_current_file_is_the_original(self):                  # FR-20 p. 9
        self.assertFalse(os.path.exists(self.backup))
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_TRANSLATED)         # does not match the version
        before = self.snapshot()
        code, _, calls = self.mods_only()
        self.assertIn('Версия игры не поддерживается', code)
        self.assertIn('копии оригинала', code)                                              # says which file was taken
        self.assertEqual(self.snapshot(), before)

    def test_the_backup_wins_over_a_matching_game_file(self):
        self.write(os.path.join(self.backup, 'Assembly-CSharp.dll'), ASM_TRANSLATED)          # the backup is not the version
        code, _, _ = self.mods_only()
        self.assertIn('Версия игры не поддерживается', code)

    def test_unknown_version_with_force_installs_the_mods_only(self):
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_TRANSLATED)
        before = self.stamps('data.unity3d', 'Assembly-CSharp.dll')
        code, out, calls = self.mods_only('--force')
        self.assertEqual(code, 0, out)
        self.assertIn('ВНИМАНИЕ', out)
        self.assertEqual(self.stamps('data.unity3d', 'Assembly-CSharp.dll'), before)
        self.assertEqual(calls, [])

    def test_mods_only_twice_changes_nothing(self):
        self.mods_only()
        shot = self.snapshot()
        code, out, _ = self.mods_only()
        self.assertEqual(code, 0)
        self.assertIn('изменений нет', out)
        self.assertEqual(self.snapshot(), shot)


class OriginalsAfterModsOnlyTests(Fixture):
    """--mods-only leaves _original_en without the game originals; the translation steps must cope with that."""

    def setUp(self):
        super().setUp()
        self.put_dlls(self.build, 'mod/build')
        with self.translation():
            self.assertEqual(self.run_main(self.game, '--with-mods', '--mods-only')[0], 0)
        self.paths = install.game_paths(self.game)

    @contextlib.contextmanager
    def patched(self, answer):
        module = types.ModuleType('ptru.patch_dll')
        module.is_patched = lambda path: answer
        with mock.patch.dict(sys.modules, {'ptru.patch_dll': module}):
            yield

    def test_the_translation_install_saves_the_missing_originals_and_keeps_the_json_copies(self):
        json_copies = {n: self.read(self.backup, n) for n in ('ScriptingAssemblies.json', 'RuntimeInitializeOnLoads.json',
                                                              'ptmod_install.json')}
        out = io.StringIO()
        with self.patched(False), contextlib.redirect_stdout(out):
            install.backup_originals(self.paths, self.backup)
        self.assertEqual(self.read(self.backup, 'data.unity3d'), DATA_UNITY3D)
        self.assertEqual(self.read(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assertEqual({n: self.read(self.backup, n) for n in json_copies}, json_copies)
        self.assertIn('сохранены', out.getvalue())

    def test_a_complete_backup_is_just_recognised(self):
        with self.patched(False), contextlib.redirect_stdout(io.StringIO()):
            install.backup_originals(self.paths, self.backup)
        out = io.StringIO()
        with self.patched(True), contextlib.redirect_stdout(out):
            install.backup_originals(self.paths, self.backup)               # a patched file must not matter now
        self.assertIn('Найдена резервная копия', out.getvalue())

    def test_a_patched_assembly_without_its_original_is_still_refused(self):
        with self.patched(True), self.assertRaises(SystemExit) as cm:
            install.backup_originals(self.paths, self.backup)
        self.assertIn('уже пропатчена', cm.exception.code)
        self.assertFalse(os.path.exists(os.path.join(self.backup, 'Assembly-CSharp.dll')))

    def test_restore_after_mods_only_takes_the_mods_off_and_explains_there_is_no_translation(self):
        # FR-20 p. 5 (TSK-045): --restore removes the mods first; with no game originals in _original_en the translation
        # step is only explained, it does not fail and it touches neither data.unity3d nor Assembly-CSharp.dll.
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            install.restore(self.game)
        text = out.getvalue()
        self.assertIn('перевод не устанавливался', text)
        self.assertRegex(text, CYRILLIC)
        self.assertFalse(os.path.exists(os.path.join(self.managed, 'PTMod.dll')))
        self.assertFalse(os.path.exists(os.path.join(self.managed, '0Harmony.dll')))
        self.assertEqual(self.read(self.data, 'ScriptingAssemblies.json').decode(), SA)
        self.assertEqual(self.read(self.data, 'RuntimeInitializeOnLoads.json').decode(), RI)
        self.assertEqual(self.read(self.data, 'data.unity3d'), DATA_UNITY3D)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)

    def test_restore_still_restores_what_the_backup_holds(self):
        self.write(os.path.join(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.write(os.path.join(self.backup, 'data.unity3d'), DATA_UNITY3D)
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_TRANSLATED)
        self.write(os.path.join(self.data, 'data.unity3d'), b'translated')
        with contextlib.redirect_stdout(io.StringIO()):
            install.restore(self.game)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assertEqual(self.read(self.data, 'data.unity3d'), DATA_UNITY3D)

    def test_the_full_mode_after_mods_only_runs_the_translation_and_the_mods_again(self):
        with self.translation() as calls:
            code, out = self.run_main(self.game, '--with-mods')
        self.assertEqual((code, calls), (0, [self.game]))
        self.assertIn('Моды установлены', out)
        manifest = json.loads(self.read(self.backup, 'ptmod_install.json').decode('utf-8'))
        self.assertIs(manifest['modsOnly'], False)                  # the manifest follows the latest mode
        self.assertEqual(self.read(self.data, 'ScriptingAssemblies.json').decode().count('"PTMod.dll"'), 1)


class NoNetworkTests(unittest.TestCase):
    """FR-20 p. 10: the installer does not touch the network."""

    NETWORK = {'socket', 'urllib', 'http', 'ftplib', 'smtplib', 'ssl', 'requests', 'xmlrpc', 'telnetlib', 'webbrowser'}

    def test_neither_install_py_nor_mods_py_imports_a_network_module(self):
        for name in ('install.py', os.path.join('ptru', 'mods.py')):
            with open(os.path.join(ROOT, name), encoding='utf-8') as f:
                tree = ast.parse(f.read())
            imported = set()
            for node in ast.walk(tree):
                if isinstance(node, ast.Import):
                    imported.update(a.name.split('.')[0] for a in node.names)
                elif isinstance(node, ast.ImportFrom) and node.module:
                    imported.add(node.module.split('.')[0])
            self.assertEqual(imported & self.NETWORK, set(), name)


if __name__ == '__main__':
    unittest.main()
