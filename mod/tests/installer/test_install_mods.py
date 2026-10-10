"""Tests of the mod installation: `install.py --with-mods` (TSK-044, FR-20) - install_mods / prepare_install in
ptru/mods.py and the command line of install.py; and its other half (TSK-045): `--remove-mods`, `--restore` and
`--check` - remove_mods and check_mod_sources in ptru/mods.py.

Run from the repository root:  python -m unittest discover -s mod/tests/installer
Everything happens in temporary folders that play the game and the repository: no real game files are read or
written, the translation step of install.py is replaced by a recorder, nothing is built and there is no network.
"""
import contextlib, hashlib, io, json, os, re, shutil, struct, subprocess, sys, tempfile, unittest
from unittest import mock

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
if ROOT not in sys.path:
    sys.path.insert(0, ROOT)

import install  # noqa: E402
from ptru import mods  # noqa: E402

CYRILLIC = re.compile('[А-Яа-яЁё]')

# What the fake game holds. The JSON literals are written by hand in Unity's compact style, not produced by the code.
DATA_UNITY3D = b'data.unity3d placeholder\x00' * 8
ASM_ORIGINAL = b'MZ pristine Assembly-CSharp 0.2.3\x00' * 4
ASM_OTHER = b'MZ some other build of Assembly-CSharp\x00' * 4
SA = '{"names":["UnityEngine.dll","Assembly-CSharp.dll"],"types":[2,16]}'
RI = ('{"root":[{"assemblyName":"Assembly-CSharp","nameSpace":"","className":"GameManager","methodName":"Boot",'
      '"loadTypes":1,"isUnityClass":false}]}\r\n')
MOD_ENTRY = '{"assemblyName":"PTMod","nameSpace":"PTMod.Core","className":"ModBootstrap","methodName":"Init","loadTypes":1,"isUnityClass":false}'


def sha(data):
    return hashlib.sha256(data).hexdigest()


META = {'game': 'Processor Tycoon', 'game_version': '0.2.3', 'unity': '6000.0.65f1',
        'original_sha256': {'data.unity3d': sha(DATA_UNITY3D), 'Assembly-CSharp.dll': sha(ASM_ORIGINAL)}}


def version_resource(product):
    """A VS_VERSIONINFO "ProductVersion" string block the way csc writes it: header, key, padding to 4, value."""
    key = 'ProductVersion'.encode('utf-16-le') + b'\0\0'
    value = (product + '\0').encode('utf-16-le')
    head = 6 + len(key)
    pad = b'\0' * (-head % 4)
    return struct.pack('<HHH', head + len(pad) + len(value), len(product) + 1, 1) + key + pad + value


def fake_mod_dll(product='0.1.0+release', salt=b''):
    return b'MZ\x90\x00 odd prefix' + version_resource(product) + b' tail ' + salt


class Fixture(unittest.TestCase):
    """A fake game folder, a fake repository with ready DLLs in mod/build, and helpers to look at both."""

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
        self.mod_bytes, self.harmony_bytes = fake_mod_dll(), b'MZ fake 0Harmony 2.3.6'
        self.write(os.path.join(self.build, 'PTMod.dll'), self.mod_bytes)
        self.write(os.path.join(self.build, '0Harmony.dll'), self.harmony_bytes)

    def write(self, path, data):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'wb') as f:
            f.write(data if isinstance(data, bytes) else data.encode('utf-8'))
        return path

    def read(self, *parts):
        with open(os.path.join(*parts), 'rb') as f:
            return f.read()

    def snapshot(self, folder=None):
        """{relative path: content hash or 'DIR'}: any change to the folder shows up as a difference."""
        folder = folder or self.game
        shot = {}
        for base, dirs, files in os.walk(folder):
            for name in dirs:
                shot[os.path.relpath(os.path.join(base, name), folder)] = 'DIR'
            for name in files:
                path = os.path.join(base, name)
                shot[os.path.relpath(path, folder)] = sha(self.read(path))
        return shot

    def stamps(self):
        """{path: mtime_ns} of every file in the game: proves that a repeat does not even touch the files."""
        return {os.path.join(b, n): os.stat(os.path.join(b, n)).st_mtime_ns
                for b, _, files in os.walk(self.game) for n in files}

    def count(self, name, text):
        return self.read(self.data, name).decode('utf-8').count(text)

    @contextlib.contextmanager
    def translation(self):
        """Stand-in for the translation half of install.py; yields the list of games it was called for."""
        calls = []
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(install, 'install', side_effect=calls.append))
            stack.enter_context(mock.patch.object(install, 'require_original_dll'))
            yield calls

    def run_main(self, *args):
        """install.main(args) with the fake repository as install.py's own folder: (exit code or message, stdout)."""
        out = io.StringIO()
        with mock.patch.object(install, 'HERE', self.repo), contextlib.redirect_stdout(out):
            try:
                install.main(list(args))
                code = 0
            except SystemExit as e:
                code = e.code
        return code, out.getvalue()

    def plan(self, **kwargs):
        return mods.prepare_install(self.game, self.repo, META, log=lambda text: None, **kwargs)

    def install(self, **kwargs):
        log = []
        manifest = mods.install_mods(self.game, self.plan(**kwargs), log=log.append)
        return manifest, log


class InstallTests(Fixture):
    def test_everything_lands_where_the_design_says(self):
        self.install()
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), self.mod_bytes)
        self.assertEqual(self.read(self.managed, '0Harmony.dll'), self.harmony_bytes)
        self.assertEqual(self.read(self.data, 'ScriptingAssemblies.json').decode(),
                         '{"names":["UnityEngine.dll","Assembly-CSharp.dll","PTMod.dll"],"types":[2,16,16]}')
        self.assertEqual(self.read(self.data, 'RuntimeInitializeOnLoads.json').decode(),
                         RI[:-4] + ',' + MOD_ENTRY + ']}\r\n')
        for sub in ('content', 'l10n'):
            readme = self.read(self.game, 'PTMod', sub, 'README.txt')
            self.assertTrue(readme.startswith(b'\xef\xbb\xbf'))
            self.assertRegex(readme.decode('utf-8'), CYRILLIC)
            self.assertIn(b'\r\n', readme)
        self.assertTrue(os.path.isfile(os.path.join(self.backup, 'ptmod_install.json')))

    def test_game_files_other_than_the_mods_are_not_touched(self):
        before = self.snapshot()
        self.install()
        after = self.snapshot()
        for path, digest in before.items():
            if path not in (os.path.join('Processor Tycoon_Data', 'ScriptingAssemblies.json'),
                            os.path.join('Processor Tycoon_Data', 'RuntimeInitializeOnLoads.json')):
                self.assertEqual(after[path], digest, path)

    def test_installing_twice_gives_one_entry_in_each_json_and_changes_nothing(self):      # AC-20.1
        self.install()
        self.assertEqual((self.count('ScriptingAssemblies.json', '"PTMod.dll"'),
                          self.count('RuntimeInitializeOnLoads.json', '"ModBootstrap"')), (1, 1))
        shot, stamps = self.snapshot(), self.stamps()
        _, log = self.install()
        self.assertEqual((self.count('ScriptingAssemblies.json', '"PTMod.dll"'),
                          self.count('RuntimeInitializeOnLoads.json', '"ModBootstrap"')), (1, 1))
        self.assertEqual(self.snapshot(), shot)
        self.assertEqual(self.stamps(), stamps)                    # not even rewritten
        self.assertTrue(any('изменений нет' in line for line in log), log)

    def test_installing_twice_through_the_command_line(self):                              # AC-20.1 by the book
        dll_dir = self.build
        with self.translation() as calls:
            codes = [self.run_main(self.game, '--with-mods', '--mod-dir', dll_dir)[0] for _ in range(2)]
        self.assertEqual(codes, [0, 0])
        self.assertEqual(calls, [self.game, self.game])
        self.assertEqual((self.count('ScriptingAssemblies.json', '"PTMod.dll"'),
                          self.count('RuntimeInitializeOnLoads.json', '"ModBootstrap"')), (1, 1))

    def test_json_originals_are_saved_once_and_never_overwritten(self):
        self.install()
        self.assertEqual(self.read(self.backup, 'ScriptingAssemblies.json'), SA.encode())
        self.assertEqual(self.read(self.backup, 'RuntimeInitializeOnLoads.json'), RI.encode())
        self.install()
        self.assertEqual(self.read(self.backup, 'ScriptingAssemblies.json'), SA.encode())
        self.assertEqual(self.read(self.backup, 'RuntimeInitializeOnLoads.json'), RI.encode())

    def test_originals_saved_by_an_earlier_translation_install_are_kept(self):
        self.write(os.path.join(self.backup, 'ScriptingAssemblies.json'), 'from the translation install')
        self.install()
        self.assertEqual(self.read(self.backup, 'ScriptingAssemblies.json'), b'from the translation install')
        self.assertEqual(self.read(self.backup, 'RuntimeInitializeOnLoads.json'), RI.encode())

    def test_manifest_lists_files_with_hashes_version_date_and_mode(self):
        manifest, _ = self.install()
        on_disk = json.loads(self.read(self.backup, 'ptmod_install.json').decode('utf-8'))
        self.assertEqual(on_disk, manifest)
        self.assertEqual(manifest['schema'], 1)
        self.assertEqual((manifest['modVersion'], manifest['build']), ('0.1.0', 'release'))
        self.assertRegex(manifest['date'], r'^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d$')
        self.assertIs(manifest['modsOnly'], False)
        self.assertEqual(manifest['gameVersion'], '0.2.3')
        self.assertIs(manifest['gameVersionKnown'], True)
        self.assertEqual({f['path']: f['sha256'] for f in manifest['files']},
                         {'Processor Tycoon_Data/Managed/PTMod.dll': sha(self.mod_bytes),
                          'Processor Tycoon_Data/Managed/0Harmony.dll': sha(self.harmony_bytes)})
        self.assertEqual([j['backup'] for j in manifest['json']],
                         ['_original_en/ScriptingAssemblies.json', '_original_en/RuntimeInitializeOnLoads.json'])
        self.assertEqual(manifest['registeredAssemblies'], ['PTMod.dll'])
        self.assertEqual(manifest['readmes'], ['PTMod/content/README.txt', 'PTMod/l10n/README.txt'])

    def test_repeat_keeps_the_manifest_byte_identical_including_the_date(self):
        self.install()
        first = self.read(self.backup, 'ptmod_install.json')
        with mock.patch.object(mods.time, 'strftime', return_value='2099-01-01T00:00:00'):
            self.install()
        self.assertEqual(self.read(self.backup, 'ptmod_install.json'), first)

    def test_version_is_taken_from_the_dll_and_unknown_when_it_has_none(self):
        self.write(os.path.join(self.build, 'PTMod.dll'), fake_mod_dll('0.2.0-rc1+debug'))
        manifest, log = self.install()
        self.assertEqual((manifest['modVersion'], manifest['build']), ('0.2.0-rc1', 'debug'))
        self.assertTrue(any('Моды установлены (0.2.0-rc1)' in line for line in log), log)

        self.write(os.path.join(self.build, 'PTMod.dll'), b'MZ with no version resource')
        manifest, log = self.install()
        self.assertIsNone(manifest['modVersion'])
        self.assertTrue(any('версия не определена' in line for line in log), log)

    def test_update_replaces_the_files_the_manifest_owns(self):
        self.install()
        self.write(os.path.join(self.build, 'PTMod.dll'), fake_mod_dll('0.2.0+release', b'new'))
        self.write(os.path.join(self.build, '0Harmony.dll'), b'MZ newer harmony')
        manifest, _ = self.install()
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), fake_mod_dll('0.2.0+release', b'new'))
        self.assertEqual(self.read(self.managed, '0Harmony.dll'), b'MZ newer harmony')
        self.assertEqual(manifest['modVersion'], '0.2.0')
        self.assertEqual({f['sha256'] for f in manifest['files']},
                         {sha(self.read(self.managed, 'PTMod.dll')), sha(self.read(self.managed, '0Harmony.dll'))})
        self.assertEqual((self.count('ScriptingAssemblies.json', '"PTMod.dll"'),
                          self.count('RuntimeInitializeOnLoads.json', '"ModBootstrap"')), (1, 1))

    def test_readme_the_player_edited_is_left_alone(self):
        self.install()
        path = os.path.join(self.game, 'PTMod', 'content', 'README.txt')
        self.write(path, 'my own notes')
        self.install()
        self.assertEqual(self.read(path), b'my own notes')

    def test_files_the_player_put_into_ptmod_survive_a_reinstall(self):
        self.install()
        mine = self.write(os.path.join(self.game, 'PTMod', 'content', 'framework-sample', 'samples.json'), '{"entries":[]}')
        self.install()
        self.assertEqual(self.read(mine), b'{"entries":[]}')

    def test_translation_is_installed_after_every_refusal_check_and_before_the_mods(self):
        seen = []

        def translation(game):
            seen.append(self.snapshot())                           # what the translation step finds in the game

        before = self.snapshot()
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(install, 'install', side_effect=translation))
            stack.enter_context(mock.patch.object(install, 'require_original_dll'))
            stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
            install.install_with_mods(self.game, repo_dir=self.repo)
        self.assertEqual(seen, [before])                           # nothing of the mod was written yet
        self.assertEqual(self.count('ScriptingAssemblies.json', '"PTMod.dll"'), 1)

    def test_mods_only_does_not_start_the_translation(self):
        with self.translation() as calls:
            code, out = self.run_main(self.game, '--with-mods', '--mods-only', '--mod-dir', self.build)
        self.assertEqual((code, calls), (0, []))
        self.assertIn('Моды установлены', out)


class VersionGateTests(Fixture):
    def test_unknown_version_is_refused_and_nothing_is_touched(self):                      # AC-20.4
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_OTHER)
        before = self.snapshot()
        with self.translation() as calls:
            code, _ = self.run_main(self.game, '--with-mods', '--mod-dir', self.build)
        self.assertIsInstance(code, str)                           # sys.exit(message): exit code 1 and the text on stderr
        self.assertIn('Версия игры не поддерживается', code)
        self.assertIn('0.2.3', code)
        self.assertIn('--force', code)
        self.assertRegex(code, CYRILLIC)
        self.assertEqual(calls, [])                                # the translation was not started either
        self.assertEqual(self.snapshot(), before)

    def test_unknown_version_with_force_installs_with_a_warning(self):                     # AC-20.4
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_OTHER)
        with self.translation():
            code, out = self.run_main(self.game, '--with-mods', '--force', '--mod-dir', self.build)
        self.assertEqual(code, 0)
        self.assertIn('ВНИМАНИЕ', out)
        self.assertIn('--force', out)
        manifest = json.loads(self.read(self.backup, 'ptmod_install.json').decode('utf-8'))
        self.assertEqual((manifest['gameVersionKnown'], manifest['force']), (False, True))
        self.assertEqual(self.count('ScriptingAssemblies.json', '"PTMod.dll"'), 1)

    def test_known_version_needs_no_force(self):
        manifest, _ = self.install()
        self.assertEqual((manifest['gameVersionKnown'], manifest['force']), (True, False))

    def test_the_copy_in_original_en_decides_when_there_is_one(self):
        # The game holds a translated (different) DLL, the pristine one is in the backup: the version is known.
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_OTHER)
        self.write(os.path.join(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        manifest, _ = self.install()
        self.assertIs(manifest['gameVersionKnown'], True)

    def test_a_foreign_backup_is_not_rescued_by_a_matching_game_file(self):
        self.write(os.path.join(self.backup, 'Assembly-CSharp.dll'), ASM_OTHER)
        with self.assertRaises(mods.ModsInstallError) as cm:
            self.plan()
        self.assertIn('_original_en', str(cm.exception))

    def test_without_a_backup_the_game_file_counts_as_the_original(self):
        path = mods.original_assembly_path(self.game)
        self.assertEqual(os.path.normpath(path), os.path.normpath(os.path.join(self.managed, 'Assembly-CSharp.dll')))
        self.write(os.path.join(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assertEqual(os.path.normpath(mods.original_assembly_path(self.game)),
                         os.path.normpath(os.path.join(self.backup, 'Assembly-CSharp.dll')))

    def test_refusal_in_a_real_process_returns_a_nonzero_exit_code(self):                  # AC-20.4, the process level
        # The real install.py with the real translation/meta.json: the fake game's DLL cannot match its hash.
        before = self.snapshot()
        proc = subprocess.run([sys.executable, os.path.join(ROOT, 'install.py'), self.game, '--with-mods', '--mods-only',
                               '--mod-dir', self.build], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                              env=dict(os.environ, PYTHONIOENCODING='utf-8'), timeout=120)
        self.assertEqual(proc.returncode, 1)
        self.assertIn('Версия игры не поддерживается', proc.stderr.decode('utf-8'))
        self.assertEqual(self.snapshot(), before)

    def test_forced_install_in_a_real_process_works_without_the_translation_dependencies(self):
        proc = subprocess.run([sys.executable, os.path.join(ROOT, 'install.py'), self.game, '--with-mods', '--mods-only',
                               '--force', '--mod-dir', self.build], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                              env=dict(os.environ, PYTHONIOENCODING='utf-8'), timeout=120)
        self.assertEqual(proc.returncode, 0, proc.stderr.decode('utf-8'))
        self.assertIn('Моды установлены', proc.stdout.decode('utf-8'))
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), self.mod_bytes)


class ManualInstallationTests(Fixture):
    """FR-20 p. 6: a PTMod entry in the JSON and no manifest means somebody installed the mod by hand."""

    def register_by_hand(self, assembly=True, runtime=True):
        if assembly:
            mods.register_assembly(os.path.join(self.data, 'ScriptingAssemblies.json'))
        if runtime:
            mods.register_runtime_init(os.path.join(self.data, 'RuntimeInitializeOnLoads.json'))

    def test_entry_without_manifest_gets_a_message_with_the_way_out(self):
        for assembly, runtime in ((True, True), (True, False), (False, True)):
            with self.subTest(assembly=assembly, runtime=runtime):
                self.setUp()
                self.register_by_hand(assembly, runtime)
                before = self.snapshot()
                with self.translation() as calls:
                    code, _ = self.run_main(self.game, '--with-mods', '--mod-dir', self.build)
                self.assertIsInstance(code, str)
                self.assertIn('--remove-mods --force', code)
                self.assertIn('вручную', code)
                self.assertRegex(code, CYRILLIC)
                self.assertEqual(calls, [])
                self.assertEqual(self.snapshot(), before)

    def test_force_does_not_override_it(self):
        self.register_by_hand()
        with self.translation():
            code, _ = self.run_main(self.game, '--with-mods', '--force', '--mod-dir', self.build)
        self.assertIn('--remove-mods --force', code)
        self.assertEqual(self.count('ScriptingAssemblies.json', '"PTMod.dll"'), 1)

    def test_a_loaded_spike_is_pointed_to_its_own_script(self):
        self.register_by_hand()
        self.write(os.path.join(self.backup, 'ptmod_spike_install.json'), '{}')
        with self.assertRaises(mods.ModsInstallError) as cm:
            self.plan()
        self.assertIn('spike_install.py', str(cm.exception))
        self.assertIn('--remove', str(cm.exception))

    def test_with_a_manifest_the_same_entries_are_ours_and_a_reinstall_is_fine(self):
        self.install()
        self.install()
        self.assertEqual(self.count('ScriptingAssemblies.json', '"PTMod.dll"'), 1)

    def test_a_broken_manifest_is_reported_not_guessed(self):
        self.install()
        self.write(os.path.join(self.backup, 'ptmod_install.json'), '{not json')
        with self.assertRaises(mods.ModsInstallError) as cm:
            self.plan()
        self.assertIn('повреждён', str(cm.exception))


class RefusalTests(Fixture):
    def test_broken_json_is_found_before_anything_is_written(self):
        self.write(os.path.join(self.data, 'ScriptingAssemblies.json'), '{"names":["a.dll",],"types":[16]}')
        before = self.snapshot()
        with self.translation() as calls:
            code, _ = self.run_main(self.game, '--with-mods', '--mod-dir', self.build)
        self.assertIsInstance(code, str)
        self.assertIn('ScriptingAssemblies.json', code)
        self.assertRegex(code, CYRILLIC)
        self.assertEqual(calls, [])
        self.assertEqual(self.snapshot(), before)

    def test_a_foreign_0harmony_is_not_replaced_without_force(self):
        self.write(os.path.join(self.managed, '0Harmony.dll'), b'MZ somebody else Harmony')
        before = self.snapshot()
        with self.assertRaises(mods.ModsInstallError) as cm:
            self.plan()
        self.assertIn('0Harmony.dll', str(cm.exception))
        self.assertIn('--force', str(cm.exception))
        self.assertEqual(self.snapshot(), before)

    def test_a_foreign_0harmony_is_replaced_with_force_and_then_belongs_to_the_manifest(self):
        self.write(os.path.join(self.managed, '0Harmony.dll'), b'MZ somebody else Harmony')
        log = []
        mods.prepare_install(self.game, self.repo, META, force=True, log=log.append)
        self.assertTrue(any('ВНИМАНИЕ' in line and '0Harmony.dll' in line for line in log), log)
        self.install(force=True)
        self.assertEqual(self.read(self.managed, '0Harmony.dll'), self.harmony_bytes)
        self.install()                                              # now the manifest owns it: no --force needed

    def test_an_identical_0harmony_already_in_managed_is_fine(self):
        self.write(os.path.join(self.managed, '0Harmony.dll'), self.harmony_bytes)
        self.install()

    def test_a_missing_managed_folder_means_it_is_not_a_game(self):
        shutil.rmtree(self.managed)
        with self.assertRaises(mods.ModsInstallError) as cm:
            self.plan()
        self.assertRegex(str(cm.exception), CYRILLIC)

    def test_every_refusal_is_in_russian(self):
        texts = []
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_OTHER)           # unknown game version
        with self.assertRaises(mods.ModsInstallError) as cm:
            self.plan()
        texts.append(str(cm.exception))
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        with self.assertRaises(mods.ModsInstallError) as cm:                               # no such --mod-dir
            self.plan(mod_dir=os.path.join(self.root, 'nowhere'))
        texts.append(str(cm.exception))
        self.write(os.path.join(self.managed, '0Harmony.dll'), b'MZ other')               # somebody else's Harmony
        with self.assertRaises(mods.ModsInstallError) as cm:
            self.plan()
        texts.append(str(cm.exception))
        self.assertEqual(len(texts), 3)
        for text in texts:
            self.assertRegex(text, CYRILLIC)
            self.assertIn('не тронуты', text)


class RollbackTests(Fixture):
    def test_a_failure_in_the_last_step_leaves_the_game_as_it_was(self):
        before = self.snapshot()
        plan = self.plan()
        with mock.patch.object(mods, 'register_runtime_init', side_effect=mods.ModsJsonError('сбой записи')):
            with self.assertRaises(mods.ModsJsonError):
                mods.install_mods(self.game, plan, log=lambda text: None)
        self.assertEqual(self.snapshot(), before)                  # no DLLs, no _original_en, no PTMod, JSON as they were

    def test_a_failure_while_writing_the_manifest_leaves_the_game_as_it_was(self):
        before = self.snapshot()
        plan = self.plan()
        real = mods.write_atomic

        def failing(path, data):
            if os.path.basename(path) == 'ptmod_install.json':
                raise mods.ModsJsonError('диск полон')
            return real(path, data)

        with mock.patch.object(mods, 'write_atomic', side_effect=failing):
            with self.assertRaises(mods.ModsJsonError):
                mods.install_mods(self.game, plan, log=lambda text: None)
        self.assertEqual(self.snapshot(), before)

    def test_a_failed_update_restores_the_previous_install(self):
        self.install()
        before = self.snapshot()
        self.write(os.path.join(self.build, 'PTMod.dll'), fake_mod_dll('0.2.0+release'))
        plan = self.plan()
        with mock.patch.object(mods, 'register_runtime_init', side_effect=mods.ModsJsonError('сбой')):
            with self.assertRaises(mods.ModsJsonError):
                mods.install_mods(self.game, plan, log=lambda text: None)
        self.assertEqual(self.snapshot(), before)
        self.assertEqual(self.read(self.managed, 'PTMod.dll'), self.mod_bytes)

    def test_the_failure_message_says_that_the_changes_were_taken_back(self):
        log = []
        plan = self.plan()
        with mock.patch.object(mods, 'register_runtime_init', side_effect=mods.ModsJsonError('сбой')):
            with self.assertRaises(mods.ModsJsonError):
                mods.install_mods(self.game, plan, log=log.append)
        self.assertTrue(any('отменены' in line for line in log), log)

    def test_a_locked_dll_is_reported_and_rolled_back(self):
        # Windows keeps the DLL open while the game runs; os.replace then fails with PermissionError.
        before = self.snapshot()
        plan = self.plan()
        real = os.replace

        def locked(src, dst):
            if os.path.basename(dst) == '0Harmony.dll':
                raise PermissionError(13, 'Отказано в доступе')
            return real(src, dst)

        with mock.patch('ptru.mods.os.replace', side_effect=locked):
            with self.assertRaises(mods.ModsJsonError) as cm:
                mods.install_mods(self.game, plan, log=lambda text: None)
        self.assertIn('Закройте игру', str(cm.exception))
        self.assertEqual(self.snapshot(), before)


class HarmonyRegistrationTests(Fixture):
    def test_0harmony_is_not_listed_by_default(self):
        self.assertIs(mods.REGISTER_HARMONY, False)
        self.install()
        self.assertEqual(self.count('ScriptingAssemblies.json', '0Harmony'), 0)

    def test_the_switch_lists_it_and_switching_back_takes_it_out_again(self):
        sa = os.path.join(self.data, 'ScriptingAssemblies.json')
        manifest = mods.install_mods(self.game, self.plan(), register_harmony=True, log=lambda text: None)
        self.assertEqual(json.loads(self.read(sa).decode())['names'][-2:], ['PTMod.dll', '0Harmony.dll'])
        self.assertEqual(manifest['registeredAssemblies'], ['PTMod.dll', '0Harmony.dll'])
        manifest = mods.install_mods(self.game, self.plan(), register_harmony=False, log=lambda text: None)
        self.assertEqual(self.read(sa).decode(), '{"names":["UnityEngine.dll","Assembly-CSharp.dll","PTMod.dll"],"types":[2,16,16]}')
        self.assertEqual(manifest['registeredAssemblies'], ['PTMod.dll'])


class DllVersionTests(unittest.TestCase):
    def test_product_version_with_and_without_a_build_suffix(self):
        self.assertEqual(mods.parse_product_version(fake_mod_dll('0.1.0+debug')), ('0.1.0', 'debug'))
        self.assertEqual(mods.parse_product_version(fake_mod_dll('1.2.3')), ('1.2.3', None))
        self.assertEqual(mods.parse_product_version(fake_mod_dll('1.2.3-rc1+release')), ('1.2.3-rc1', 'release'))
        self.assertEqual(mods.parse_product_version(fake_mod_dll('2.3.6.0+f41da7608c5c')), ('2.3.6.0', 'f41da7608c5c'))

    def test_alignment_of_the_block_inside_the_file_does_not_matter(self):
        for lead in range(8):
            with self.subTest(lead=lead):
                self.assertEqual(mods.parse_product_version(b'x' * lead + version_resource('0.1.0+release')),
                                 ('0.1.0', 'release'))

    def test_garbage_and_non_versions_give_none(self):
        key = 'ProductVersion'.encode('utf-16-le') + b'\0\0'
        for data in (b'', b'MZ nothing here', key, b'xxxxxx' + key, version_resource('not a version'),
                     version_resource('')):
            with self.subTest(data=data[:12]):
                self.assertEqual(mods.parse_product_version(data), (None, None))

    def test_unreadable_file(self):
        self.assertEqual(mods.read_dll_version(os.path.join(tempfile.gettempdir(), 'no-such-dir-ptmod', 'PTMod.dll')),
                         (None, None))

    def test_file_level_read(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, 'PTMod.dll')
            with open(path, 'wb') as f:
                f.write(fake_mod_dll('0.3.1+release'))
            self.assertEqual(mods.read_dll_version(path), ('0.3.1', 'release'))


class CommandLineTests(unittest.TestCase):
    def run_main(self, *args):
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            try:
                install.main(list(args))
            except SystemExit as e:
                return e.code, err.getvalue()
        return 0, err.getvalue()

    def test_mods_flags_need_with_mods(self):
        for flags in (['--mods-only'], ['--mod-dir', 'x'], ['--force']):
            with self.subTest(flags=flags):
                code, err = self.run_main('somewhere', *flags)
                self.assertEqual(code, 2)
                self.assertIn('--with-mods', err)
                self.assertRegex(err, CYRILLIC)

    def test_restore_cannot_be_combined_with_with_mods(self):
        code, err = self.run_main('somewhere', '--with-mods', '--restore')
        self.assertEqual(code, 2)
        self.assertIn('--restore', err)

    def test_the_help_mentions_every_new_flag(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), self.assertRaises(SystemExit):
            install.main(['--help'])
        for flag in ('--with-mods', '--mods-only', '--mod-dir', '--force'):
            self.assertIn(flag, out.getvalue())


class StdlibOnlyTests(unittest.TestCase):
    def test_install_py_imports_nothing_outside_the_standard_library_at_module_level(self):
        import ast
        with open(os.path.join(ROOT, 'install.py'), encoding='utf-8') as f:
            tree = ast.parse(f.read())
        for node in tree.body:
            if isinstance(node, (ast.Import, ast.ImportFrom)):
                names = [a.name for a in node.names] if isinstance(node, ast.Import) else [node.module]
                for name in names:
                    self.assertNotIn(name.split('.')[0], ('pefile', 'fontTools', 'ptru'), name)


# ---------------------------------------------------------------------------------------------------------------
# TSK-045: --remove-mods and --restore (FR-20 items 4, 5 and 6; AC-20.2, AC-20.3)
# ---------------------------------------------------------------------------------------------------------------

TRANSLATED_ASM = b'MZ translated Assembly-CSharp\x00' * 4
TRANSLATED_DATA = b'translated data.unity3d\x00' * 8
GAME_JSON = {'ScriptingAssemblies.json': SA, 'RuntimeInitializeOnLoads.json': RI}


class RemovalFixture(Fixture):
    def remove(self, **kwargs):
        log = []
        return mods.remove_mods(self.game, log=log.append, **kwargs), log

    def translated(self):
        """The game after the translation install: the originals are in _original_en, the game holds other files."""
        self.write(os.path.join(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.write(os.path.join(self.backup, 'data.unity3d'), DATA_UNITY3D)
        self.write(os.path.join(self.managed, 'Assembly-CSharp.dll'), TRANSLATED_ASM)
        self.write(os.path.join(self.data, 'data.unity3d'), TRANSLATED_DATA)

    def hand_install(self, harmony=True):
        """The mod put in by hand: both entries and the DLLs, no manifest, no backups."""
        mods.register_assembly(os.path.join(self.data, 'ScriptingAssemblies.json'))
        mods.register_runtime_init(os.path.join(self.data, 'RuntimeInitializeOnLoads.json'))
        self.write(os.path.join(self.managed, 'PTMod.dll'), b'MZ hand-made PTMod')
        if harmony:
            self.write(os.path.join(self.managed, '0Harmony.dll'), b'MZ somebody else Harmony')

    def assert_json_originals(self):
        for name, original in GAME_JSON.items():
            self.assertEqual(self.read(self.data, name), original.encode('utf-8'), name)

    def dlls_gone(self):
        return not any(os.path.exists(os.path.join(self.managed, n)) for n in ('PTMod.dll', '0Harmony.dll'))

    def put_manifest(self, change):
        manifest = json.loads(self.good_manifest.decode('utf-8'))
        change(manifest)
        self.write(os.path.join(self.backup, 'ptmod_install.json'), json.dumps(manifest, ensure_ascii=False, indent=2))

    def install_for_tampering(self):
        self.install()
        self.good_manifest = self.read(self.backup, 'ptmod_install.json')


class RemoveModsTests(RemovalFixture):
    def test_the_dlls_go_and_both_json_files_are_the_originals_byte_for_byte(self):         # AC-20.2
        self.translated()
        self.install()
        result, log = self.remove()
        self.assertTrue(self.dlls_gone())
        self.assert_json_originals()
        for name in GAME_JSON:                                     # "match the backup"
            self.assertEqual(self.read(self.data, name), self.read(self.backup, name), name)
        self.assertTrue(result.changed)
        self.assertEqual(result.removed, ['Processor Tycoon_Data/Managed/PTMod.dll', 'Processor Tycoon_Data/Managed/0Harmony.dll'])
        self.assertEqual(result.restored, ['Processor Tycoon_Data/ScriptingAssemblies.json',
                                           'Processor Tycoon_Data/RuntimeInitializeOnLoads.json'])
        self.assertIs(result.manual, False)

    def test_the_translation_stays(self):                                                   # AC-20.2: "перевод на месте"
        self.translated()
        self.install()
        self.remove()
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), TRANSLATED_ASM)
        self.assertEqual(self.read(self.data, 'data.unity3d'), TRANSLATED_DATA)
        self.assertEqual(self.read(self.backup, 'Assembly-CSharp.dll'), ASM_ORIGINAL)        # and its backups
        self.assertEqual(self.read(self.backup, 'data.unity3d'), DATA_UNITY3D)

    def test_mods_only_install_and_removal_never_touch_the_game_files(self):                # AC-20.3
        self.install(mods_only=True)
        self.assertEqual(self.read(self.data, 'data.unity3d'), DATA_UNITY3D)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.remove()
        self.assertEqual(self.read(self.data, 'data.unity3d'), DATA_UNITY3D)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assert_json_originals()

    def test_what_stays_behind_is_only_the_json_backups_and_the_players_folder(self):
        before = self.snapshot()
        self.install()
        self.remove()
        after = self.snapshot()
        for path, digest in before.items():
            self.assertEqual(after[path], digest, path)
        left = set(after) - set(before)
        self.assertEqual(left, {'_original_en', os.path.join('_original_en', 'ScriptingAssemblies.json'),
                                os.path.join('_original_en', 'RuntimeInitializeOnLoads.json'),
                                'PTMod', os.path.join('PTMod', 'content'), os.path.join('PTMod', 'l10n'),
                                os.path.join('PTMod', 'content', 'README.txt'), os.path.join('PTMod', 'l10n', 'README.txt')})
        self.assertFalse(os.path.exists(os.path.join(self.backup, 'ptmod_install.json')))        # the manifest is gone

    def test_the_players_content_and_l10n_files_are_not_touched(self):                      # AC-20.2 / FR-20 item 4
        self.install()
        mine = {
            os.path.join('content', 'framework-sample', 'samples.json'): '{"schema":1,"records":[]}',
            os.path.join('content', 'event-system', 'events.json'): '{"schema":1,"records":[{"id":"es.mine"}]}',
            os.path.join('l10n', 'ru.json'): '{"core.hub.title":"Мои моды"}',
            os.path.join('l10n', 'en', 'event-system.json'): '{"es.x":"mine"}',
            os.path.join('content', 'README.txt'): 'my own notes',
        }
        for rel, text in mine.items():
            self.write(os.path.join(self.game, 'PTMod', rel), text)
        shot = self.snapshot(os.path.join(self.game, 'PTMod'))
        stamps = {rel: os.stat(os.path.join(self.game, 'PTMod', rel)).st_mtime_ns for rel in mine}
        self.remove()
        self.assertEqual(self.snapshot(os.path.join(self.game, 'PTMod')), shot)
        for rel, text in mine.items():
            self.assertEqual(self.read(self.game, 'PTMod', rel), text.encode('utf-8'), rel)
            self.assertEqual(os.stat(os.path.join(self.game, 'PTMod', rel)).st_mtime_ns, stamps[rel], rel)

    def test_saves_and_other_game_folders_are_not_touched(self):
        self.install()
        other = {os.path.join(self.data, 'Plugins', 'x.dll'): b'plugin', os.path.join(self.data, 'StreamingAssets', 'a.bin'): b'a',
                 os.path.join(self.game, 'Saves', 'test1.txt'): b'save', os.path.join(self.game, 'Saves', 'test1.ptmod.json'): b'{}'}
        for path, data in other.items():
            self.write(path, data)
        self.remove()
        for path, data in other.items():
            self.assertEqual(self.read(path), data, path)

    def test_removing_twice_changes_nothing_and_says_so(self):
        self.install()
        self.remove()
        shot, stamps = self.snapshot(), self.stamps()
        result, log = self.remove()
        self.assertEqual(log, [mods.NOTHING_TO_REMOVE])
        self.assertFalse(result.changed)
        self.assertEqual((self.snapshot(), self.stamps()), (shot, stamps))

    def test_a_game_without_the_mod_has_nothing_to_remove(self):
        shot, stamps = self.snapshot(), self.stamps()
        result, log = self.remove()
        self.assertEqual(log, [mods.NOTHING_TO_REMOVE])
        self.assertEqual((result.changed, result.removed, result.restored), (False, [], []))
        self.assertEqual((self.snapshot(), self.stamps()), (shot, stamps))

    def test_install_remove_install_gives_a_clean_second_install(self):
        self.install()
        first = self.snapshot()
        self.remove()
        manifest, _ = self.install()
        after = self.snapshot()
        self.assertEqual(set(after), set(first))
        for path in first:
            if path != os.path.join('_original_en', 'ptmod_install.json'):          # holds the date of the install
                self.assertEqual(after[path], first[path], path)
        self.assertEqual((self.count('ScriptingAssemblies.json', '"PTMod.dll"'),
                          self.count('RuntimeInitializeOnLoads.json', '"ModBootstrap"')), (1, 1))
        self.assertEqual(manifest['modVersion'], '0.1.0')

    def test_a_dll_that_is_already_gone_is_skipped(self):
        self.install()
        os.remove(os.path.join(self.managed, '0Harmony.dll'))
        result, _ = self.remove()
        self.assertEqual(result.removed, ['Processor Tycoon_Data/Managed/PTMod.dll'])
        self.assertTrue(self.dlls_gone())
        self.assert_json_originals()

    def test_a_registered_0harmony_leaves_the_json_too(self):
        mods.install_mods(self.game, self.plan(), register_harmony=True, log=lambda text: None)
        self.assertEqual(self.count('ScriptingAssemblies.json', '0Harmony'), 1)
        self.remove()
        self.assert_json_originals()

    def test_the_messages_are_russian_and_name_the_version(self):
        self.install()
        _, log = self.remove()
        for line in log:
            self.assertRegex(line, CYRILLIC, line)
        text = '\n'.join(log)
        self.assertIn('Моды сняты (0.1.0)', text)
        self.assertIn('PTMod\\content', text)                      # tells that the player's folder is left alone
        self.assertIn('PTMod.dll', text)

    def test_a_game_update_that_changed_a_json_keeps_the_new_text_and_renews_the_backup(self):
        self.install()
        sa = os.path.join(self.data, 'ScriptingAssemblies.json')
        updated = mods.add_assembly_text(self.read(sa).decode('utf-8'), 'GameUpdate.dll')     # something the update added
        self.write(sa, updated)
        _, log = self.remove()
        now = self.read(sa)
        self.assertIn(b'GameUpdate.dll', now)
        self.assertNotIn(b'PTMod.dll', now)
        self.assertEqual(self.read(self.backup, 'ScriptingAssemblies.json'), now)                # still "JSON == backup"
        self.assertTrue(any('обновлена' in line for line in log), log)

    def test_the_byte_order_mark_and_line_endings_of_the_json_survive(self):
        for name, text in (('ScriptingAssemblies.json', SA), ('RuntimeInitializeOnLoads.json', RI.replace('\r\n', '\n'))):
            self.write(os.path.join(self.data, name), b'\xef\xbb\xbf' + text.encode('utf-8'))
        before = {n: self.read(self.data, n) for n in GAME_JSON}
        self.install()
        self.remove()
        for name, data in before.items():
            self.assertEqual(self.read(self.data, name), data, name)

    def test_result_carries_the_version_of_the_manifest(self):
        self.write(os.path.join(self.build, 'PTMod.dll'), fake_mod_dll('0.3.1+release'))
        self.install()
        result, _ = self.remove()
        self.assertEqual(result.version, '0.3.1')


class RemovalRefusalTests(RemovalFixture):
    """Every refusal comes before the first change: the game is as it was and the text says what to do."""

    def refused(self, **kwargs):
        before, stamps = self.snapshot(), self.stamps()
        with self.assertRaises(mods.ModsRemoveRefused) as cm:
            self.remove(**kwargs)
        self.assertEqual((self.snapshot(), self.stamps()), (before, stamps))
        text = str(cm.exception)
        self.assertRegex(text, CYRILLIC)
        return text

    def test_a_dll_changed_after_the_install_is_kept_unless_forced(self):
        self.install()
        self.write(os.path.join(self.managed, '0Harmony.dll'), b'MZ another mod updated Harmony')
        text = self.refused()
        self.assertIn('0Harmony.dll', text)
        self.assertIn('--force', text)
        self.assertNotIn('PTMod.dll\n', text)                      # only the changed file is named
        result, _ = self.remove(force=True)
        self.assertTrue(self.dlls_gone())
        self.assert_json_originals()
        self.assertEqual(len(result.removed), 2)

    def test_the_manifest_cannot_name_files_outside_the_game_or_the_players_and_backup_folders(self):
        self.install_for_tampering()
        outside = self.write(os.path.join(self.root, 'precious.dll'), b'do not delete')
        bad_paths = ['../precious.dll', '..\\precious.dll', 'Processor Tycoon_Data/../../precious.dll', outside, '/etc/passwd',
                     'C:/Windows/notepad.exe', 'Processor Tycoon_Data/Managed/PTMod.dll:stream', 'PTMod/content/README.txt',
                     'ptmod/l10n/README.txt', '_original_en/ScriptingAssemblies.json', '_original_en/Assembly-CSharp.dll',
                     'Processor Tycoon_Data//Managed/PTMod.dll', '', None, 42]
        for bad in bad_paths:
            with self.subTest(path=bad):
                self.put_manifest(lambda m: m['files'].append({'path': bad, 'sha256': None}))
                text = self.refused()
                self.assertIn('не годится', text)
                self.assertIn('--remove-mods --force', text)
                self.assertEqual(self.read(outside), b'do not delete')

    def test_force_does_not_make_a_hostile_manifest_trusted(self):
        self.install_for_tampering()
        outside = self.write(os.path.join(self.root, 'precious.dll'), b'do not delete')
        self.put_manifest(lambda m: m['files'].append({'path': '../precious.dll', 'sha256': None}))
        result, log = self.remove(force=True)
        self.assertTrue(result.manual)                             # the manifest is ignored as a whole ...
        self.assertEqual(self.read(outside), b'do not delete')     # ... and what it names is not touched
        self.assertFalse(os.path.exists(os.path.join(self.managed, 'PTMod.dll')))
        self.assertTrue(os.path.exists(os.path.join(self.managed, '0Harmony.dll')))
        self.assertFalse(os.path.exists(os.path.join(self.backup, 'ptmod_install.json')))
        self.assertTrue(any('не годится' in line for line in log), log)
        self.assert_json_originals()

    def make_dir_link(self, link, target):
        """A directory link (a symbolic link, or a junction on Windows without the privilege for symlinks); False if neither works."""
        try:
            os.symlink(target, link, target_is_directory=True)
            return True
        except (OSError, NotImplementedError):
            pass
        if os.name == 'nt':
            done = subprocess.run(['cmd', '/c', 'mklink', '/J', link, target], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return done.returncode == 0 and os.path.isdir(link)
        return False

    def test_a_folder_link_that_leads_out_of_the_game_is_not_followed(self):
        self.install_for_tampering()
        elsewhere = os.path.join(self.root, 'elsewhere')
        os.makedirs(elsewhere)
        precious = self.write(os.path.join(elsewhere, 'precious.dll'), b'keep me')
        if not self.make_dir_link(os.path.join(self.managed, 'link'), elsewhere):
            self.skipTest('cannot create a directory link on this machine')
        self.put_manifest(lambda m: m['files'].append({'path': 'Processor Tycoon_Data/Managed/link/precious.dll', 'sha256': None}))
        self.refused()
        self.assertEqual(self.read(precious), b'keep me')
        self.remove(force=True)
        self.assertEqual(self.read(precious), b'keep me')

    def test_a_manifest_entry_that_is_a_folder_or_not_an_object_is_refused(self):
        self.install_for_tampering()
        os.makedirs(os.path.join(self.data, 'Managed', 'sub'))
        self.put_manifest(lambda m: m['files'].append({'path': 'Processor Tycoon_Data/Managed/sub'}))
        self.refused()
        self.put_manifest(lambda m: m['files'].append('Processor Tycoon_Data/Managed/PTMod.dll'))
        self.refused()

    def test_no_manifest_and_entries_in_the_json_is_a_manual_installation(self):             # FR-20 item 6
        self.hand_install()
        text = self.refused()
        self.assertIn('--remove-mods --force', text)
        self.assertIn('вручную', text)
        self.assertIn('0Harmony.dll', text)

    def test_each_trace_alone_is_enough_to_refuse(self):
        for what in ('assembly', 'runtime', 'dll'):
            with self.subTest(what=what):
                self.setUp()
                if what == 'assembly':
                    mods.register_assembly(os.path.join(self.data, 'ScriptingAssemblies.json'))
                elif what == 'runtime':
                    mods.register_runtime_init(os.path.join(self.data, 'RuntimeInitializeOnLoads.json'))
                else:
                    self.write(os.path.join(self.managed, 'PTMod.dll'), b'MZ hand-made')
                self.assertIn('--remove-mods --force', self.refused())

    def test_force_removes_a_manual_installation_but_not_the_harmony_it_cannot_vouch_for(self):
        self.hand_install()
        result, log = self.remove(force=True)
        self.assertTrue(result.manual)
        self.assertFalse(os.path.exists(os.path.join(self.managed, 'PTMod.dll')))
        self.assertEqual(self.read(self.managed, '0Harmony.dll'), b'MZ somebody else Harmony')
        self.assert_json_originals()
        self.assertTrue(any('0Harmony.dll оставлен' in line for line in log), log)
        self.assertFalse(os.path.exists(self.backup))              # no backups were made up for a hand-made install

    def test_force_with_only_a_json_entry_and_no_dll(self):
        mods.register_runtime_init(os.path.join(self.data, 'RuntimeInitializeOnLoads.json'))
        self.remove(force=True)
        self.assert_json_originals()

    def test_a_spike_is_not_ours_to_remove_even_with_force(self):
        self.hand_install()
        self.write(os.path.join(self.backup, 'ptmod_spike_install.json'), '{}')
        for force in (False, True):
            with self.subTest(force=force):
                text = self.refused(force=force)
                self.assertIn('spike_install.py', text)
                self.assertIn('--remove', text)

    def test_a_spike_manifest_alone_changes_nothing(self):
        self.write(os.path.join(self.backup, 'ptmod_spike_install.json'), '{}')
        result, log = self.remove()
        self.assertEqual((result.changed, log), (False, [mods.NOTHING_TO_REMOVE]))

    def test_a_broken_manifest_is_refused_and_force_clears_it(self):
        self.install()
        self.write(os.path.join(self.backup, 'ptmod_install.json'), '{not json')
        text = self.refused()
        self.assertIn('повреждён', text)
        self.assertIn('--remove-mods --force', text)
        result, log = self.remove(force=True)
        self.assertTrue(result.manual)
        self.assertFalse(os.path.exists(os.path.join(self.backup, 'ptmod_install.json')))
        self.assertFalse(os.path.exists(os.path.join(self.managed, 'PTMod.dll')))
        self.assertTrue(os.path.exists(os.path.join(self.managed, '0Harmony.dll')))      # not provably ours any more
        self.assert_json_originals()
        self.assertTrue(any('ВНИМАНИЕ' in line for line in log), log)

    def test_a_manifest_that_is_not_an_object_with_files_counts_as_broken(self):
        self.install()
        for text in ('[]', '{"files": 3}', '{"schema": 1}', ''):
            with self.subTest(text=text):
                self.write(os.path.join(self.backup, 'ptmod_install.json'), text)
                self.assertIn('повреждён', self.refused())

    def test_a_broken_manifest_alone_is_cleared_by_force(self):
        self.write(os.path.join(self.backup, 'ptmod_install.json'), '{not json')
        result, _ = self.remove(force=True)
        self.assertTrue(result.changed)
        self.assertFalse(os.path.exists(os.path.join(self.backup, 'ptmod_install.json')))


class RemovalRecoveryTests(RemovalFixture):
    """The saved original is the way out of a JSON that cannot be edited."""

    def test_a_broken_json_comes_back_from_the_backup(self):
        self.install()
        sa = os.path.join(self.data, 'ScriptingAssemblies.json')
        self.write(sa, '{"names":["UnityEngine.dll",')
        _, log = self.remove()
        self.assert_json_originals()
        self.assertTrue(self.dlls_gone())
        self.assertTrue(any('восстановлен из резервной копии' in line for line in log), log)

    def test_a_missing_json_comes_back_from_the_backup(self):
        self.install()
        os.remove(os.path.join(self.data, 'RuntimeInitializeOnLoads.json'))
        self.remove()
        self.assert_json_originals()

    def test_a_broken_json_without_a_backup_is_refused_and_nothing_is_changed(self):
        self.install()
        os.remove(os.path.join(self.backup, 'ScriptingAssemblies.json'))
        self.write(os.path.join(self.data, 'ScriptingAssemblies.json'), '{broken')
        before = self.snapshot()
        with self.assertRaises(mods.ModsRemoveRefused) as cm:
            self.remove()
        self.assertIn('ScriptingAssemblies.json', str(cm.exception))
        self.assertRegex(str(cm.exception), CYRILLIC)
        self.assertEqual(self.snapshot(), before)                  # the DLLs are still there too

    def test_a_broken_backup_does_not_replace_a_good_json(self):
        self.install()
        self.write(os.path.join(self.backup, 'RuntimeInitializeOnLoads.json'), '{broken backup')
        self.remove()
        self.assertEqual(self.read(self.data, 'RuntimeInitializeOnLoads.json'), RI.encode('utf-8'))
        self.assertEqual(self.read(self.backup, 'RuntimeInitializeOnLoads.json'), RI.encode('utf-8'))   # renewed from the good text


class RemovalRollbackTests(RemovalFixture):
    def setUp(self):
        super().setUp()
        self.install()
        self.after_install = self.snapshot()

    def test_a_failure_in_the_last_step_puts_everything_back(self):
        real = mods._delete_file

        def failing(path, undo):
            if os.path.basename(path) == 'ptmod_install.json':
                raise mods.ModsInstallError('диск занят')
            return real(path, undo)

        log = []
        with mock.patch.object(mods, '_delete_file', side_effect=failing):
            with self.assertRaises(mods.ModsInstallError):
                mods.remove_mods(self.game, log=log.append)
        self.assertEqual(self.snapshot(), self.after_install)
        self.assertTrue(any('отменены' in line for line in log), log)

    def test_a_failure_writing_the_second_json_puts_everything_back(self):
        real = mods.write_atomic

        def failing(path, data):
            if os.path.basename(path) == 'RuntimeInitializeOnLoads.json':
                raise mods.ModsJsonError('сбой записи')
            return real(path, data)

        with mock.patch.object(mods, 'write_atomic', side_effect=failing):
            with self.assertRaises(mods.ModsJsonError):
                self.remove()
        self.assertEqual(self.snapshot(), self.after_install)

    def test_a_dll_locked_by_the_running_game_is_reported_and_rolled_back(self):
        real = os.remove

        def locked(path):
            if os.path.basename(path) == '0Harmony.dll':
                raise PermissionError(13, 'Отказано в доступе')
            return real(path)

        with mock.patch('ptru.mods.os.remove', side_effect=locked):
            with self.assertRaises(mods.ModsInstallError) as cm:
                self.remove()
        self.assertNotIsInstance(cm.exception, mods.ModsRemoveRefused)      # an environment problem, not a refusal
        self.assertIn('Закройте игру', str(cm.exception))
        self.assertEqual(self.snapshot(), self.after_install)               # PTMod.dll, deleted first, is back

    def test_after_a_failure_a_retry_succeeds(self):
        with mock.patch.object(mods, 'write_atomic', side_effect=mods.ModsJsonError('сбой')):
            with self.assertRaises(mods.ModsJsonError):
                self.remove()
        self.remove()
        self.assertTrue(self.dlls_gone())
        self.assert_json_originals()


class RestoreTests(RemovalFixture):
    """--restore (FR-20 item 5): the four files come back and the files of the mod go."""

    def test_the_four_files_come_back_and_the_mod_goes(self):
        self.translated()
        self.install()
        code, out = self.run_main(self.game, '--restore')
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assertEqual(self.read(self.data, 'data.unity3d'), DATA_UNITY3D)
        self.assert_json_originals()
        self.assertTrue(self.dlls_gone())
        self.assertFalse(os.path.exists(os.path.join(self.backup, 'ptmod_install.json')))
        self.assertIn('Моды сняты', out)
        self.assertIn('Оригинальные файлы восстановлены', out)

    def test_the_players_folder_survives_a_restore(self):
        self.translated()
        self.install()
        mine = self.write(os.path.join(self.game, 'PTMod', 'content', 'event-system', 'events.json'), '{"schema":1,"records":[]}')
        self.run_main(self.game, '--restore')
        self.assertEqual(self.read(mine), b'{"schema":1,"records":[]}')

    def test_without_the_translation_only_the_mod_comes_off(self):                          # --mods-only, then --restore
        self.install(mods_only=True)
        code, out = self.run_main(self.game, '--restore')
        self.assertEqual(code, 0, out)
        self.assertTrue(self.dlls_gone())
        self.assert_json_originals()
        self.assertEqual(self.read(self.data, 'data.unity3d'), DATA_UNITY3D)
        self.assertIn('перевод не устанавливался', out)

    def test_a_game_with_only_the_translation_is_restored_as_before_without_a_word_about_mods(self):
        self.translated()
        code, out = self.run_main(self.game, '--restore')
        self.assertEqual(code, 0)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assertEqual(self.read(self.data, 'data.unity3d'), DATA_UNITY3D)
        self.assertNotIn('мод', out.lower().replace('оригинальные', ''))
        self.assert_json_originals()

    def test_without_a_backup_folder_there_is_nothing_to_restore(self):
        code, _ = self.run_main(self.game, '--restore')
        self.assertIn('Резервная копия не найдена', code)

    def test_after_remove_mods_a_restore_still_works(self):
        self.translated()
        self.install()
        self.run_main(self.game, '--remove-mods')
        code, out = self.run_main(self.game, '--restore')
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)

    def test_restoring_twice_is_harmless(self):
        self.translated()
        self.install()
        self.run_main(self.game, '--restore')
        shot = self.snapshot()
        code, _ = self.run_main(self.game, '--restore')
        self.assertEqual(code, 0)
        self.assertEqual(self.snapshot(), shot)

    def test_a_manual_installation_is_a_warning_and_the_game_files_are_restored_anyway(self):
        self.translated()
        self.hand_install()
        code, out = self.run_main(self.game, '--restore')
        self.assertEqual(code, 0, out)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assertIn('ВНИМАНИЕ', out)
        self.assertIn('--remove-mods --force', out)
        self.assertEqual(self.count('ScriptingAssemblies.json', '"PTMod.dll"'), 1)          # left for the player to decide

    def test_restore_with_force_takes_a_manual_installation_off_too(self):
        self.translated()
        self.hand_install()
        code, out = self.run_main(self.game, '--restore', '--force')
        self.assertEqual(code, 0, out)
        self.assert_json_originals()
        self.assertFalse(os.path.exists(os.path.join(self.managed, 'PTMod.dll')))
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)

    def test_a_changed_dll_is_a_warning_and_force_deletes_it(self):
        self.translated()
        self.install()
        self.write(os.path.join(self.managed, 'PTMod.dll'), b'MZ rebuilt by hand')
        code, out = self.run_main(self.game, '--restore')
        self.assertEqual(code, 0, out)
        self.assertIn('ВНИМАНИЕ', out)
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), ASM_ORIGINAL)
        self.assertTrue(os.path.exists(os.path.join(self.managed, 'PTMod.dll')))
        code, out = self.run_main(self.game, '--restore', '--force')
        self.assertEqual(code, 0, out)
        self.assertTrue(self.dlls_gone())

    def test_a_locked_dll_stops_the_restore_before_any_game_file_is_touched(self):
        self.translated()
        self.install()
        before = self.snapshot()
        real = os.remove

        def locked(path):
            if os.path.basename(path) == '0Harmony.dll':
                raise PermissionError(13, 'Отказано в доступе')
            return real(path)

        with mock.patch('ptru.mods.os.remove', side_effect=locked):
            code, _ = self.run_main(self.game, '--restore')
        self.assertIsInstance(code, str)
        self.assertIn('Закройте игру', code)
        self.assertEqual(self.snapshot(), before)                  # still translated, mod still installed: retry later
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), TRANSLATED_ASM)


class RemoveCommandTests(RemovalFixture):
    def test_the_command_line_removes_the_mods_and_leaves_the_translation(self):
        self.translated()
        with self.translation():
            self.run_main(self.game, '--with-mods', '--mod-dir', self.build)
        code, out = self.run_main(self.game, '--remove-mods')
        self.assertEqual(code, 0, out)
        self.assertIn('Моды сняты', out)
        self.assertTrue(self.dlls_gone())
        self.assert_json_originals()
        self.assertEqual(self.read(self.managed, 'Assembly-CSharp.dll'), TRANSLATED_ASM)

    def test_the_hint_after_an_install_names_a_flag_that_now_exists(self):
        with self.translation():
            _, out = self.run_main(self.game, '--with-mods', '--mod-dir', self.build)
        self.assertIn('--remove-mods', out)
        code, _ = self.run_main(self.game, '--remove-mods')
        self.assertEqual(code, 0)

    def test_nothing_to_remove_is_not_an_error(self):
        code, out = self.run_main(self.game, '--remove-mods')
        self.assertEqual(code, 0)
        self.assertIn(mods.NOTHING_TO_REMOVE, out)

    def test_a_refusal_is_a_message_and_a_nonzero_code(self):
        self.hand_install()
        before = self.snapshot()
        code, _ = self.run_main(self.game, '--remove-mods')
        self.assertIsInstance(code, str)
        self.assertIn('--remove-mods --force', code)
        self.assertRegex(code, CYRILLIC)
        self.assertEqual(self.snapshot(), before)

    def test_force_on_the_command_line(self):
        self.hand_install()
        code, out = self.run_main(self.game, '--remove-mods', '--force')
        self.assertEqual(code, 0, out)
        self.assert_json_originals()

    def test_a_locked_file_is_a_message_and_a_nonzero_code(self):
        self.install()
        before = self.snapshot()
        with mock.patch('ptru.mods.os.remove', side_effect=PermissionError(13, 'Отказано в доступе')):
            code, _ = self.run_main(self.game, '--remove-mods')
        self.assertIsInstance(code, str)
        self.assertIn('Закройте игру', code)
        self.assertEqual(self.snapshot(), before)

    def test_in_a_real_process_install_and_removal_round_trip(self):
        env = dict(os.environ, PYTHONIOENCODING='utf-8')
        run = lambda *args: subprocess.run([sys.executable, os.path.join(ROOT, 'install.py'), self.game, *args],
                                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, timeout=120)
        before = self.snapshot()
        done = run('--with-mods', '--mods-only', '--force', '--mod-dir', self.build)       # the fake DLL differs from meta.json
        self.assertEqual(done.returncode, 0, done.stderr.decode('utf-8'))
        done = run('--remove-mods')
        self.assertEqual(done.returncode, 0, done.stderr.decode('utf-8'))
        self.assertIn('Моды сняты', done.stdout.decode('utf-8'))
        self.assertTrue(self.dlls_gone())
        self.assert_json_originals()
        done = run('--remove-mods')                                  # and again: nothing to do, still success
        self.assertEqual(done.returncode, 0, done.stderr.decode('utf-8'))
        self.assertIn('снимать нечего', done.stdout.decode('utf-8'))
        for path, digest in before.items():
            self.assertEqual(self.snapshot()[path], digest, path)

    def test_in_a_real_process_a_refusal_has_exit_code_1(self):
        self.hand_install()
        done = subprocess.run([sys.executable, os.path.join(ROOT, 'install.py'), self.game, '--remove-mods'],
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=dict(os.environ, PYTHONIOENCODING='utf-8'),
                              timeout=120)
        self.assertEqual(done.returncode, 1)
        self.assertIn('--remove-mods --force', done.stderr.decode('utf-8'))


class RemoveFlagsTests(unittest.TestCase):
    def run_main(self, *args):
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            try:
                install.main(list(args))
            except SystemExit as e:
                return e.code, err.getvalue()
        return 0, err.getvalue()

    def test_remove_mods_cannot_be_combined_with_install_or_restore(self):
        for other in ('--with-mods', '--restore'):
            with self.subTest(other=other):
                code, err = self.run_main('somewhere', '--remove-mods', other)
                self.assertEqual(code, 2)
                self.assertIn('--remove-mods', err)
                self.assertRegex(err, CYRILLIC)

    def test_force_is_accepted_with_remove_mods_and_restore(self):
        for flag in ('--remove-mods', '--restore'):
            with self.subTest(flag=flag):
                code, _ = self.run_main(os.path.join(tempfile.gettempdir(), 'no-such-game-ptmod'), flag, '--force')
                self.assertIsInstance(code, str)                   # got past the flags, stopped at "no game files here"
                self.assertIn('Не найдены файлы игры', code)

    def test_force_alone_is_still_refused_and_names_the_commands_it_works_with(self):
        code, err = self.run_main('somewhere', '--force')
        self.assertEqual(code, 2)
        for flag in ('--with-mods', '--remove-mods', '--restore'):
            self.assertIn(flag, err)

    def test_the_help_describes_remove_mods_and_restore(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), self.assertRaises(SystemExit):
            install.main(['--help'])
        text = out.getvalue()
        self.assertIn('--remove-mods', text)
        self.assertIn('--check', text)


# ---------------------------------------------------------------------------------------------------------------
# TSK-045: --check - the content and the string tables of the mod (FR-20 item 7, FR-25)
# ---------------------------------------------------------------------------------------------------------------

PREFIXES = {'mod-framework': ['core'], 'framework-sample': ['smp'], 'event-system': ['es'], 'market-shocks': ['ms'],
            'historical-events': ['hist'], 'scenarios-achievements': ['scn', 'chl', 'ach']}
CORE_JSON = {'schema': 1, 'log': {'repeatLimit': 3}, 'prefixes': PREFIXES,
             'channels': [{'name': 'research.speed', 'min': 0.25, 'max': 4.0}, {'name': 'taxes', 'min': 0.0, 'max': 4.0}]}
RU_TABLE = {'core.hub.title': 'Моды', 'core.mail.daysLeft.one': 'Остался {0} день', 'core.mail.daysLeft.few': 'Осталось {0} дня',
            'core.mail.daysLeft.many': 'Осталось {0} дней'}
EN_TABLE = {'core.hub.title': 'Mods', 'core.mail.daysLeft.one': '{0} day left', 'core.mail.daysLeft.other': '{0} days left'}
EVENTS_ES = {'schema': 1, 'records': [{'id': 'es.patentTroll', 'choices': [{'id': 'settle'}, {'id': 'fight'}]},
                                      {'id': 'es.rivalScandal'}]}


class SourceFixture(unittest.TestCase):
    """A tiny mod/src/PTMod in a temporary folder that passes the check; each test spoils one thing."""

    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.src = os.path.join(os.path.realpath(self._tmp.name), 'mod', 'src', 'PTMod')
        self.put('Content/mod-framework/core.json', CORE_JSON)
        self.put('Content/event-system/events.json', EVENTS_ES)
        self.put('L10n/ru.json', RU_TABLE)
        self.put('L10n/en.json', EN_TABLE)

    def put(self, rel, data):
        path = os.path.join(self.src, *rel.split('/'))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        if not isinstance(data, (str, bytes)):
            data = json.dumps(data, ensure_ascii=False, indent=2)
        with open(path, 'wb') as f:
            f.write(data if isinstance(data, bytes) else data.encode('utf-8'))
        return path

    def delete(self, rel):
        os.remove(os.path.join(self.src, *rel.split('/')))

    def check(self):
        return mods.check_mod_sources(self.src)

    def assert_error(self, report, *fragments):
        """Some error mentions every fragment (and is Russian)."""
        for error in report.errors:
            if all(f in error for f in fragments):
                self.assertRegex(error, CYRILLIC)
                return error
        self.fail('no error with %r in:\n  %s' % (fragments, '\n  '.join(report.errors) or '(no errors)'))

    def assert_clean(self, report):
        self.assertEqual(report.errors, [])
        self.assertEqual(report.warnings, [])


class CheckValidTreeTests(SourceFixture):
    def test_a_valid_tree_passes_with_nothing_to_say(self):
        report = self.check()
        self.assert_clean(report)
        self.assertTrue(report.ok)
        self.assertEqual((report.content_files, report.l10n_files), (2, 2))
        self.assertEqual(report.l10n_keys, {'en': 3, 'ru': 4})

    def test_the_summary_line_is_russian_and_counts_files_and_problems(self):
        lines = self.check().lines()
        self.assertEqual(len(lines), 1)
        self.assertRegex(lines[0], CYRILLIC)
        self.assertIn('файлов контента — 2', lines[0])
        self.assertIn('ошибок: 0', lines[0])
        self.assertIn('предупреждений: 0', lines[0])

    def test_errors_and_warnings_are_listed_before_the_summary(self):
        self.put('Content/event-system/oops.json', '{')
        self.put('Content/nobody/x.json', {'schema': 1})
        lines = self.check().lines()
        self.assertTrue(lines[0].startswith('ОШИБКА '), lines)
        self.assertTrue(lines[1].startswith('ВНИМАНИЕ '), lines)
        self.assertIn('ошибок: 1', lines[-1])

    def test_a_missing_source_folder_is_not_an_error(self):                                  # a release has no sources
        report = mods.check_mod_sources(os.path.join(self.src, 'nowhere'))
        self.assertFalse(report.found)
        self.assertTrue(report.ok)
        self.assertRegex(report.lines()[0], 'пропущена')

    def test_the_foundation_files_of_the_real_mod_pass(self):
        # core.json and the two core string tables of the repository (not the files of other packages that are being written).
        real = mods.mod_sources_dir(ROOT)
        if not os.path.isfile(os.path.join(real, 'Content', 'mod-framework', 'core.json')):
            self.skipTest('no mod sources next to the installer')
        for rel in ('Content/mod-framework/core.json', 'L10n/ru.json', 'L10n/en.json'):
            self.put(rel, self.read_real(real, rel))
        self.delete('Content/event-system/events.json')
        report = self.check()
        self.assert_clean(report)
        self.assertEqual(len(json.loads(self.read_real(real, 'Content/mod-framework/core.json').decode('utf-8'))['prefixes']), 17)
        self.assertGreaterEqual(report.l10n_keys['ru'], 100)

    @staticmethod
    def read_real(real, rel):
        with open(os.path.join(real, *rel.split('/')), 'rb') as f:
            return f.read()

    def test_the_whole_real_tree_does_not_crash_the_checker(self):
        report = mods.check_mod_sources(mods.mod_sources_dir(ROOT))
        for line in report.lines():
            self.assertIsInstance(line, str)
        self.assertFalse(any('внутренняя ошибка' in e for e in report.errors), report.errors)


class CheckJsonTests(SourceFixture):
    def test_broken_json_in_a_content_file_is_found_with_line_and_column(self):
        self.put('Content/event-system/events.json', '{\n  "schema": 1,\n  "records": [ {"id": "es.a"}, ]\n}')
        error = self.assert_error(self.check(), 'Content/event-system/events.json', 'некорректный JSON', 'строка 3')
        self.assertIn('столбец', error)

    def test_broken_json_in_a_string_table_is_found(self):
        self.put('L10n/en.json', '{"core.hub.title": "Mods",}')
        self.assert_error(self.check(), 'L10n/en.json', 'некорректный JSON')

    def test_broken_json_does_not_hide_the_other_files(self):
        self.put('Content/event-system/events.json', '{broken')
        self.put('Content/historical-events/calendar.json', {'schema': 7})
        report = self.check()
        self.assert_error(report, 'events.json', 'некорректный JSON')
        self.assert_error(report, 'calendar.json', 'неизвестная версия схемы 7')

    def test_the_dialect_is_the_strict_one_of_the_mod(self):
        for name, text in (('comment', '{"schema": 1, /* c */ "records": []}'), ('single quotes', "{'schema': 1}"),
                           ('NaN', '{"schema": 1, "x": NaN}'), ('Infinity', '{"schema": 1, "x": Infinity}'),
                           ('huge number', '{"schema": 1, "x": 1e999}'), ('empty file', ''), ('two documents', '{} {}'),
                           ('leading zero', '{"schema": 01}'), ('raw tab in a string', '{"schema": 1, "x": "a\tb"}')):
            with self.subTest(name):
                self.put('Content/event-system/events.json', text)
                self.assert_error(self.check(), 'events.json', 'JSON')

    def test_a_duplicate_key_in_one_object_is_an_error(self):
        self.put('Content/event-system/events.json', '{"schema": 1, "records": [], "schema": 1}')
        self.assert_error(self.check(), 'events.json', 'повторяется', 'schema')

    def test_nesting_deeper_than_the_mod_reads_is_an_error(self):
        self.put('Content/event-system/events.json', '{"schema": 1, "x": ' + '[' * 70 + ']' * 70 + '}')
        self.assert_error(self.check(), 'events.json', 'вложенность')
        self.put('Content/event-system/events.json', '{"schema": 1, "x": ' + '[' * 62 + ']' * 62 + '}')      # 63 levels with the root
        self.assert_clean(self.check())
        self.put('Content/event-system/events.json', '[' * 200000)                           # must not crash the checker
        self.assert_error(self.check(), 'events.json', 'JSON')

    def test_a_file_that_is_not_utf8_is_an_error_and_a_bom_is_fine(self):
        self.put('Content/event-system/events.json', b'\xef\xbb\xbf{"schema": 1, "records": []}')
        self.assert_clean(self.check())
        self.put('Content/event-system/events.json', '{"schema": 1, "x": "привет"}'.encode('cp1251'))
        self.assert_error(self.check(), 'events.json', 'UTF-8')

    def test_a_file_over_the_limit_is_an_error(self):
        with mock.patch.object(mods, 'JSON_MAX_BYTES', 100):
            self.put('Content/event-system/events.json', '{"schema": 1, "x": "' + 'a' * 200 + '"}')
            self.assert_error(self.check(), 'events.json', 'больше')

    def test_the_root_must_be_an_object_with_a_known_schema(self):
        cases = (('[]', 'корень'), ('{}', 'schema'), ('{"schema": "1"}', 'schema'), ('{"schema": true}', 'schema'),
                 ('{"schema": 1.5}', 'schema'), ('{"schema": 2}', 'неизвестная версия схемы 2'),
                 ('{"schema": 0}', 'неизвестная версия схемы 0'))
        for text, fragment in cases:
            with self.subTest(text):
                self.put('Content/historical-events/calendar.json', text)
                self.assert_error(self.check(), 'calendar.json', fragment)
        self.put('Content/historical-events/calendar.json', '{"schema": 1.0}')               # 1.0 is the integer 1
        self.assert_clean(self.check())

    def test_the_known_schemas_are_one_constant(self):
        self.put('Content/historical-events/calendar.json', {'schema': 2})
        with mock.patch.object(mods, 'CONTENT_SCHEMAS', (1, 2)):
            self.assert_clean(self.check())


class CheckIdTests(SourceFixture):
    def records(self, *records, name='Content/historical-events/calendar.json'):
        self.put(name, {'schema': 1, 'records': list(records)})
        return self.check()

    def test_a_duplicate_id_in_records_is_an_error_naming_both_places(self):
        report = self.records({'id': 'a'}, {'id': 'b'}, {'id': 'a'})
        self.assert_error(report, 'calendar.json', 'records[2]', '«a»', 'records[0]')

    def test_unique_ids_pass(self):
        self.assert_clean(self.records({'id': 'a'}, {'id': 'b'}, {'id': 'A'}))

    def test_a_record_without_a_usable_id(self):
        for bad in ({}, {'id': ''}, {'id': '  '}, {'id': 5}, {'id': None}, {'id': ['a']}, 'text', 7, None, []):
            with self.subTest(bad=bad):
                report = self.records(bad)
                self.assertTrue(report.errors, bad)
                self.assert_error(report, 'records[0]')

    def test_records_must_be_an_array(self):
        self.put('Content/historical-events/calendar.json', {'schema': 1, 'records': {'id': 'a'}})
        self.assert_error(self.check(), 'calendar.json', 'records', 'массивом')

    def test_disabled_must_be_a_boolean(self):
        self.assert_clean(self.records({'id': 'a', 'disabled': True}, {'id': 'b', 'disabled': False}, {'id': 'c', 'disabled': None}))
        self.assert_error(self.records({'id': 'a', 'disabled': 'yes'}), 'records[0]', 'disabled')

    def test_a_config_file_without_records_needs_no_ids(self):
        self.put('Content/historical-events/params.json', {'schema': 1, 'dailyChance': 0.01, 'categories': [{'name': 'x'}, {'name': 'x'}]})
        self.assert_clean(self.check())

    def test_the_same_id_among_the_siblings_of_a_nested_array_is_an_error(self):
        self.put('Content/event-system/events.json', {'schema': 1, 'records': [
            {'id': 'es.a', 'choices': [{'id': 'yes'}, {'id': 'no'}, {'id': 'yes'}]}]})
        self.assert_error(self.check(), 'events.json', 'records[0].choices[2]', '«yes»', 'choices[0]')

    def test_the_same_id_in_different_arrays_is_fine(self):
        self.put('Content/event-system/events.json', {'schema': 1, 'records': [
            {'id': 'es.a', 'choices': [{'id': 'yes'}, {'id': 'no'}]}, {'id': 'es.b', 'choices': [{'id': 'yes'}, {'id': 'no'}]}]})
        self.assert_clean(self.check())

    def test_the_same_id_in_a_nested_array_of_a_config_is_an_error(self):
        self.put('Content/historical-events/params.json', {'schema': 1, 'categories': [{'id': 'x'}, {'id': 'x'}]})
        self.assert_error(self.check(), 'params.json', 'categories[1]', '«x»')

    def test_ids_in_other_files_are_not_compared_with_each_other(self):
        self.put('Content/historical-events/a.json', {'schema': 1, 'records': [{'id': 'same'}]})
        self.put('Content/historical-events/b.json', {'schema': 1, 'records': [{'id': 'same'}]})
        self.assert_clean(self.check())

    def test_a_channel_declared_twice_in_core_json(self):
        core = dict(CORE_JSON, channels=[{'name': 'taxes'}, {'name': 'bank.interest'}, {'name': 'taxes'}])
        self.put('Content/mod-framework/core.json', core)
        self.assert_error(self.check(), 'core.json', 'channels[2]', '«taxes»')
        self.put('Content/mod-framework/core.json', dict(CORE_JSON, channels=[{'name': ''}, {}, 'x']))
        self.assertEqual(len([e for e in self.check().errors if 'channels[' in e]), 3)


class CheckEventPrefixTests(SourceFixture):
    def events(self, feature, *ids, name='events.json'):
        self.put('Content/%s/%s' % (feature, name), {'schema': 1, 'records': [{'id': i} for i in ids]})
        return self.check()

    def test_only_the_own_prefix_is_allowed_for_an_event_id(self):                          # AC-25.1
        report = self.events('market-shocks', 'ms.earthquake', 'earthquake', 'es.earthquake', name='shocks.json')
        self.assertEqual(len(report.errors), 2, report.errors)
        self.assert_error(report, 'shocks.json', 'records[1]', '«earthquake»', 'без префикса', 'ms.')
        self.assert_error(report, 'shocks.json', 'records[2]', '«es.earthquake»', 'чужим префиксом', 'event-system', 'ms.')

    def test_a_foreign_prefix_names_the_owner_and_the_expected_one(self):
        error = self.assert_error(self.events('historical-events', 'hist.ok', 'ms.shock'), 'events.json', '«ms.shock»')
        self.assertIn('market-shocks', error)
        self.assertIn('«hist.»', error)

    def test_a_feature_with_several_prefixes_allows_each(self):
        report = self.events('scenarios-achievements', 'scn.a', 'chl.b', 'ach.c', 'xyz.d')
        self.assertEqual(len(report.errors), 1, report.errors)
        self.assert_error(report, '«xyz.d»', 'scn.')

    def test_the_prefix_alone_is_not_an_id(self):
        for bad in ('es.', 'es', 'ES.x', 'es:x', 'es_x', '.es.x', ' es.x'):
            with self.subTest(bad):
                self.assertTrue(self.events('event-system', bad).errors)

    def test_the_default_event_file_is_events_json_and_market_shocks_has_shocks_json(self):
        self.assertTrue(self.events('market-shocks', 'wrong', name='shocks.json').errors)
        self.assertTrue(self.events('market-shocks', 'wrong', name='events.json').errors)
        self.delete('Content/market-shocks/shocks.json')
        self.assert_clean(self.events('market-shocks', 'ms.fine', name='events.json'))

    def test_other_files_are_not_event_packs(self):
        self.put('Content/market-shocks/config.json', {'schema': 1, 'records': [{'id': 'supply'}]})
        self.put('Content/event-system/shocks.json', {'schema': 1, 'records': [{'id': 'whatever'}]})
        self.assert_clean(self.check())

    def test_an_event_pack_of_a_feature_missing_from_the_table_is_an_error(self):
        self.put('Content/nobody/events.json', {'schema': 1, 'records': [{'id': 'x.a'}, {'id': 'x.b'}]})
        report = self.check()
        self.assertEqual(len([e for e in report.errors if 'nobody' in e]), 1, report.errors)     # said once per file
        self.assert_error(report, 'nobody/events.json', 'prefixes')

    def test_without_a_prefix_table_the_events_are_not_judged_but_the_table_is(self):
        self.put('Content/mod-framework/core.json', {'schema': 1})
        report = self.check()
        self.assert_error(report, 'core.json', 'prefixes')
        self.assertEqual(len(report.errors), 1, report.errors)


class CheckPrefixTableTests(SourceFixture):
    def test_a_missing_core_json_is_an_error(self):
        self.delete('Content/mod-framework/core.json')
        self.assert_error(self.check(), 'core.json', 'файла нет')

    def test_a_broken_core_json_is_reported_once(self):
        self.put('Content/mod-framework/core.json', '{"schema": 1,')
        report = self.check()
        self.assertEqual(len([e for e in report.errors if 'core.json' in e]), 1, report.errors)

    def test_bad_rows_of_the_table(self):
        core = dict(CORE_JSON, prefixes={'a': 'x', 'b': [], 'c': ['OK'], 'd': ['fine'], 'e': ['fine'], 'f': [1]})
        self.put('Content/mod-framework/core.json', core)
        report = self.check()
        for feature in 'abcf':
            self.assert_error(report, 'prefixes["%s"]' % feature)
        self.assert_error(report, '«fine»', '«d»', '«e»')

    def test_a_new_feature_folder_that_is_not_in_the_table_is_only_a_warning(self):
        self.put('Content/new-feature/balance.json', {'schema': 1})
        report = self.check()
        self.assertEqual(report.errors, [])
        self.assertEqual(len(report.warnings), 1, report.warnings)
        self.assertIn('new-feature', report.warnings[0])
        self.assertIn('prefixes', report.warnings[0])


class CheckLayoutTests(SourceFixture):
    def test_a_content_file_outside_a_feature_folder_is_a_warning(self):
        self.put('Content/stray.json', {'schema': 1})
        self.put('Content/event-system/deeper/x.json', {'schema': 1})
        report = self.check()
        self.assertEqual(report.errors, [])
        self.assertEqual(len(report.warnings), 2, report.warnings)
        self.assertTrue(any('Content/stray.json' in w for w in report.warnings), report.warnings)
        self.assertTrue(any('Content/event-system/deeper/x.json' in w for w in report.warnings), report.warnings)

    def test_files_that_are_not_json_are_ignored(self):
        self.put('Content/event-system/README.md', '# not json {')
        self.put('L10n/notes.txt', 'x')
        self.assert_clean(self.check())

    def test_an_unsafe_file_name_is_an_error(self):
        for name in ('bad name.json', 'ключ.json', 'a$b.json', '.hidden.json'):
            with self.subTest(name):
                self.put('Content/event-system/' + name, {'schema': 1})
                self.assert_error(self.check(), name, 'недопустимо')
                os.remove(os.path.join(self.src, 'Content', 'event-system', name))

    def test_the_folders_must_exist(self):
        shutil.rmtree(os.path.join(self.src, 'Content'))
        self.assert_error(self.check(), 'Content', 'папки нет')
        self.setUp()
        shutil.rmtree(os.path.join(self.src, 'L10n'))
        self.assert_error(self.check(), 'L10n', 'папки нет')

    def test_the_checker_never_raises(self):
        with mock.patch.object(mods, '_check_l10n', side_effect=RuntimeError('boom')):
            report = self.check()
        self.assert_error(report, 'внутренняя ошибка', 'RuntimeError')


class CheckL10nTests(SourceFixture):
    def test_a_key_in_ru_only_and_a_key_in_en_only(self):
        self.put('L10n/ru.json', dict(RU_TABLE, **{'core.only.ru': 'Только ру'}))
        self.put('L10n/en.json', dict(EN_TABLE, **{'core.only.en': 'English only'}))
        report = self.check()
        self.assertEqual(len(report.errors), 2, report.errors)
        self.assert_error(report, 'L10n/ru.json', '«core.only.ru»', 'есть в ru', 'нет в en')
        self.assert_error(report, 'L10n/en.json', '«core.only.en»', 'есть в en', 'нет в ru')

    def test_plural_groups_are_compared_as_groups(self):
        # ru has one/few/many, en has one/other: the same logical key, no difference (the real core tables do exactly this).
        self.assert_clean(self.check())

    def test_a_plural_group_in_one_language_only_is_a_missing_key(self):
        self.put('L10n/en.json', {k: v for k, v in EN_TABLE.items() if 'daysLeft' not in k})
        self.assert_error(self.check(), '«core.mail.daysLeft»', 'нет в en')

    def test_a_plural_group_that_lacks_a_form_its_language_needs(self):
        self.put('L10n/ru.json', {k: v for k, v in RU_TABLE.items() if not k.endswith('.few')})
        self.assert_error(self.check(), 'core.mail.daysLeft', 'ru', 'few')
        self.put('L10n/ru.json', RU_TABLE)
        self.put('L10n/en.json', {k: v for k, v in EN_TABLE.items() if not k.endswith('.one')})
        self.assert_error(self.check(), 'core.mail.daysLeft', 'en', 'one')

    def test_many_and_other_serve_each_other(self):
        self.put('L10n/ru.json', dict(RU_TABLE, **{'core.mail.daysLeft.other': 'x'}))
        self.put('L10n/ru.json', {k: v for k, v in dict(RU_TABLE, **{'core.mail.daysLeft.other': 'x'}).items() if not k.endswith('.many')})
        self.assert_clean(self.check())

    def test_a_lone_one_or_other_suffix_is_an_ordinary_key(self):
        self.put('L10n/ru.json', dict(RU_TABLE, **{'core.item.other': 'Другое'}))
        self.put('L10n/en.json', dict(EN_TABLE, **{'core.item.other': 'Other'}))
        self.assert_clean(self.check())

    def test_the_feature_tables_are_merged_per_language_like_the_game_does(self):
        self.put('L10n/ru/framework-sample.json', {'smp.title': 'Проверка'})
        self.put('L10n/en/framework-sample.json', {'smp.title': 'Check'})
        report = self.check()
        self.assert_clean(report)
        self.assertEqual(report.l10n_files, 4)
        self.put('L10n/en/framework-sample.json', {'smp.other': 'Check'})
        report = self.check()
        self.assert_error(report, 'L10n/ru/framework-sample.json', '«smp.title»', 'нет в en')
        self.assert_error(report, 'L10n/en/framework-sample.json', '«smp.other»', 'нет в ru')

    def test_a_missing_language_table_is_an_error(self):
        self.delete('L10n/en.json')
        self.assert_error(self.check(), 'нет таблицы en')

    def test_a_broken_table_is_reported_once_and_does_not_flood_the_comparison(self):
        self.put('L10n/ru.json', '{"core.hub.title": ')
        report = self.check()
        self.assertEqual(len(report.errors), 1, report.errors)
        self.assert_error(report, 'L10n/ru.json', 'некорректный JSON')

    def test_entries_must_be_non_empty_strings_with_a_key(self):
        self.put('L10n/ru.json', dict(RU_TABLE, **{'core.number': 5, 'core.empty': '  ', '': 'x', 'core.list': ['a']}))
        self.put('L10n/en.json', dict(EN_TABLE, **{'core.number': 'n', 'core.empty': 'e', 'core.list': 'l'}))
        report = self.check()
        self.assert_error(report, 'L10n/ru.json', '«core.number»', 'строкой')
        self.assert_error(report, 'L10n/ru.json', '«core.empty»', 'пустой текст')
        self.assert_error(report, 'L10n/ru.json', 'пустым ключом')
        self.assert_error(report, 'L10n/ru.json', '«core.list»', 'строкой')

    def test_a_table_that_is_not_an_object(self):
        self.put('L10n/ru.json', '["a"]')
        self.assert_error(self.check(), 'L10n/ru.json', 'объектом')

    def test_names_that_are_not_tables(self):
        for rel in ('L10n/x.json', 'L10n/ru/a/b.json', 'L10n/-ru.json', 'L10n/ru/.hidden.json', 'L10n/ru/a b.json', 'L10n/русский.json'):
            with self.subTest(rel):
                self.put(rel, {})
                self.assert_error(self.check(), rel, 'не имя таблицы')
                os.remove(os.path.join(self.src, *rel.split('/')))

    def test_other_languages_are_not_required_to_match(self):
        self.put('L10n/de.json', {'core.hub.title': 'Mods'})
        self.assert_clean(self.check())

    def test_a_key_defined_in_two_files_of_one_language_is_a_warning(self):
        self.put('L10n/ru/mod-framework.json', {'core.hub.title': 'Мои моды'})
        self.put('L10n/en/mod-framework.json', {'core.hub.title': 'My mods'})
        report = self.check()
        self.assertEqual(report.errors, [])
        self.assertEqual(len(report.warnings), 2, report.warnings)
        self.assertIn('«core.hub.title»', report.warnings[0])


class CheckL10nPrefixTests(SourceFixture):
    def test_a_key_of_a_feature_file_must_carry_a_prefix_of_that_feature(self):
        self.put('L10n/ru/event-system.json', {'es.title': 'Заголовок', 'ms.title': 'Чужой', 'title': 'Без префикса'})
        self.put('L10n/en/event-system.json', {'es.title': 'Title', 'ms.title': 'Foreign', 'title': 'No prefix'})
        report = self.check()
        self.assertEqual(len(report.errors), 4, report.errors)                      # two keys in each language
        error = self.assert_error(report, 'L10n/ru/event-system.json', '«ms.title»', 'чужим префиксом')
        self.assertIn('market-shocks', error)
        self.assertIn('«es.»', error)
        self.assert_error(report, 'L10n/en/event-system.json', '«title»', 'без префикса', 'es.')

    def test_the_shared_language_files_take_the_keys_of_any_feature_of_the_table(self):
        # The specs of the features keep their strings in L10n/ru.json and L10n/en.json: the file does not say whose a key is,
        # so what is checked there is that the prefix is one the table knows.
        ru = dict(RU_TABLE, **{'hist.1.title': 'Событие', 'smp.title': 'Проверка', 'ach.first.name': 'Первый'})
        en = dict(EN_TABLE, **{'hist.1.title': 'Event', 'smp.title': 'Check', 'ach.first.name': 'First'})
        self.put('L10n/ru.json', ru)
        self.put('L10n/en.json', en)
        self.assert_clean(self.check())

    def test_the_shared_language_files_refuse_a_prefix_nobody_owns(self):
        self.put('L10n/ru.json', dict(RU_TABLE, **{'zzz.title': 'a', 'nodot': 'b', 'smp.': 'c', 'hist': 'd', 'Core.x': 'e'}))
        self.put('L10n/en.json', dict(EN_TABLE, **{'zzz.title': 'a', 'nodot': 'b', 'smp.': 'c', 'hist': 'd', 'Core.x': 'e'}))
        report = self.check()
        self.assertEqual(len(report.errors), 10, report.errors)                       # five keys in each language
        error = self.assert_error(report, 'L10n/ru.json', '«zzz.title»', 'неизвестным префиксом «zzz.»', 'prefixes')
        self.assertRegex(error, 'core')
        self.assert_error(report, 'L10n/en.json', '«nodot»', 'без префикса')
        for key in ('smp.', 'hist', 'Core.x'):
            self.assert_error(report, 'L10n/en.json', '«%s»' % key)

    def test_a_feature_file_does_not_take_the_keys_of_the_core(self):
        self.put('L10n/ru/event-system.json', {'core.sneaky': 'x'})
        self.put('L10n/en/event-system.json', {'core.sneaky': 'x'})
        self.assert_error(self.check(), 'L10n/ru/event-system.json', '«core.sneaky»', 'чужим префиксом', 'mod-framework')

    def test_a_key_of_another_feature_in_a_feature_file_says_where_it_belongs(self):
        self.put('L10n/ru/event-system.json', {'hist.1.title': 'x'})
        self.put('L10n/en/event-system.json', {'hist.1.title': 'x'})
        error = self.assert_error(self.check(), 'L10n/ru/event-system.json', '«hist.1.title»', 'historical-events')
        self.assertIn('L10n/ru/historical-events.json', error)

    def test_a_feature_with_several_prefixes_may_use_each(self):                            # AC-25.3
        keys = {'chl.tightCredit.title': 'a', 'ach.first.name': 'b', 'scn.briefing': 'c'}
        self.put('L10n/ru/scenarios-achievements.json', keys)
        self.put('L10n/en/scenarios-achievements.json', keys)
        self.assert_clean(self.check())
        self.put('L10n/ru/scenarios-achievements.json', dict(keys, **{'onb.step1': 'd'}))
        self.put('L10n/en/scenarios-achievements.json', dict(keys, **{'onb.step1': 'd'}))
        self.assert_error(self.check(), '«onb.step1»', 'scn.', 'chl.', 'ach.')

    def test_a_prefix_needs_a_name_after_the_dot(self):
        self.put('L10n/ru/framework-sample.json', {'smp.': 'x', 'smp': 'y', 'smpx.a': 'z', 'smp.ok': 'ok'})
        self.put('L10n/en/framework-sample.json', {'smp.': 'x', 'smp': 'y', 'smpx.a': 'z', 'smp.ok': 'ok'})
        report = self.check()
        self.assertEqual(len([e for e in report.errors if 'ru/framework-sample' in e]), 3, report.errors)

    def test_a_feature_file_of_a_feature_that_is_not_in_the_table(self):
        self.put('L10n/ru/nobody.json', {'x.a': 'a'})
        self.put('L10n/en/nobody.json', {'x.a': 'a'})
        report = self.check()
        self.assert_error(report, 'L10n/ru/nobody.json', '«nobody»', 'prefixes')
        self.assertEqual(len([e for e in report.errors if 'ru/nobody' in e]), 1, report.errors)   # said once, not per key

    def test_the_core_may_also_have_a_file_of_its_own(self):
        self.put('L10n/ru/mod-framework.json', {'core.extra': 'x'})
        self.put('L10n/en/mod-framework.json', {'core.extra': 'x'})
        self.assert_clean(self.check())

    def test_the_prefix_check_is_per_file_not_per_language_table(self):
        self.put('L10n/ru/event-system.json', {'es.a': 'a'})
        self.put('L10n/en/event-system.json', {'es.a': 'a'})
        self.put('L10n/ru/market-shocks.json', {'es.b': 'b'})                         # es. is not ms.
        self.put('L10n/en/market-shocks.json', {'es.b': 'b'})
        report = self.check()
        self.assert_error(report, 'L10n/ru/market-shocks.json', '«es.b»', 'event-system')


class CheckCommandTests(SourceFixture):
    """install.py --check: the translation check, then the mod check, one exit code."""

    def setUp(self):
        super().setUp()
        self.repo = os.path.dirname(os.path.dirname(os.path.dirname(self.src)))               # <tmp>
        os.makedirs(os.path.join(self.repo, 'translation'), exist_ok=True)

    def run_check(self, translation_errors=()):
        fake = mock.Mock()
        fake.load_translation = mock.Mock(return_value=({'a': 1, 'b': 2}, {'c': 3}, {}, list(translation_errors)))
        out = io.StringIO()
        with mock.patch.dict(sys.modules, {'ptru.build': fake}), mock.patch.object(install, 'HERE', self.repo), \
                contextlib.redirect_stdout(out):
            try:
                install.main(['--check'])
                code = 0
            except SystemExit as e:
                code = e.code
        return code, out.getvalue()

    def test_a_good_mod_and_good_translation_exit_0(self):
        code, out = self.run_check()
        self.assertEqual(code, 0, out)
        self.assertIn('ui.json: 2 строк, code.json: 1 строк, ошибок: 0', out)
        self.assertIn('Мод (mod\\src\\PTMod)', out)
        self.assertIn('ошибок: 0', out.splitlines()[-1])

    def test_a_mod_error_exits_1_and_is_printed(self):
        self.put('Content/event-system/events.json', {'schema': 1, 'records': [{'id': 'a'}, {'id': 'a'}]})
        code, out = self.run_check()
        self.assertEqual(code, 1)
        self.assertIn('ОШИБКА Content/event-system/events.json', out)
        self.assertIn('ошибок: 2', out)                            # the duplicate, and the id without a prefix

    def test_a_translation_error_exits_1_even_if_the_mod_is_fine(self):
        code, out = self.run_check(['ui.json: строка 5 без перевода'])
        self.assertEqual(code, 1)
        self.assertIn('ui.json: строка 5 без перевода', out)
        self.assertIn('ошибок: 0', out.splitlines()[-1])           # the mod part has none

    def test_warnings_do_not_fail_the_check(self):
        self.put('Content/new-feature/x.json', {'schema': 1})
        code, out = self.run_check()
        self.assertEqual(code, 0, out)
        self.assertIn('ВНИМАНИЕ', out)

    def test_without_the_sources_the_mod_part_is_skipped(self):
        shutil.rmtree(os.path.join(self.repo, 'mod'))
        code, out = self.run_check()
        self.assertEqual(code, 0, out)
        self.assertIn('проверка контента и строк мода пропущена', out)

    def test_the_four_things_the_task_names_are_caught_through_the_command_line(self):
        self.put('Content/historical-events/calendar.json', '{"schema": 1,')                                  # broken JSON
        self.put('Content/historical-events/params.json', {'schema': 1, 'records': [{'id': 'x'}, {'id': 'x'}]})   # duplicate id
        self.put('L10n/ru.json', dict(RU_TABLE, **{'core.only.ru': 'x'}))                                    # ru/en mismatch
        self.put('L10n/ru/event-system.json', {'ms.stolen': 'x'})                                            # foreign key prefix
        self.put('L10n/en/event-system.json', {'ms.stolen': 'x'})
        self.put('Content/market-shocks/shocks.json', {'schema': 1, 'records': [{'id': 'es.stolen'}]})        # foreign event id
        code, out = self.run_check()
        self.assertEqual(code, 1)
        for fragment in ('calendar.json: некорректный JSON', '«x» уже есть', '«core.only.ru»', '«ms.stolen» с чужим префиксом',
                         '«es.stolen» с чужим префиксом'):
            self.assertIn(fragment, out)

    def test_in_a_real_process_the_check_runs_on_the_repository(self):
        # The repository's own files: the translation (as before) and the mod sources. Only the exit code and the shape of
        # the output are asserted here; the foundation files are asserted above, the rest belongs to the packages writing them.
        import importlib.util
        if importlib.util.find_spec('pefile') is None or importlib.util.find_spec('fontTools') is None:
            self.skipTest('the translation check needs pefile and fonttools (requirements.txt)')
        done = subprocess.run([sys.executable, os.path.join(ROOT, 'install.py'), '--check'], stdout=subprocess.PIPE,
                              stderr=subprocess.PIPE, env=dict(os.environ, PYTHONIOENCODING='utf-8'), timeout=300)
        out = done.stdout.decode('utf-8')
        self.assertIn('ui.json:', out)
        self.assertIn('Мод (mod\\src\\PTMod)', out)
        self.assertEqual(done.returncode, 0 if 'ОШИБКА' not in out else 1, out)


if __name__ == '__main__':
    unittest.main()
