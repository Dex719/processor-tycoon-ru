"""Tests of tools/spike_install.py (mod-framework TSK-005, the preparation part: install/remove of the load spike).

Run from the repository root:  python -m unittest discover -s mod/tests/installer
The script brings its own self-test (a fake game folder in a temporary directory: dry run, install, repeat install,
removal back to the original bytes, rollback after a failure in the middle, refusals, Player.log parsing); here it is
wired into the suite. Nothing outside the temporary directory is touched, the real game folder is never read.
"""
import contextlib
import importlib.util
import io
import os
import sys
import unittest
from unittest import mock

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
TOOL = os.path.join(ROOT, 'tools', 'spike_install.py')

_spec = importlib.util.spec_from_file_location('spike_install', TOOL)
tool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(tool)


class SpikeInstallTests(unittest.TestCase):
    def test_built_in_self_test_passes(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            code = tool.self_test()
        self.assertEqual(0, code, out.getvalue())

    def test_game_folder_is_required(self):
        # Without --game-dir and PT_GAME_DIR the script must stop at argument parsing, before touching anything.
        env = {k: v for k, v in os.environ.items() if k != 'PT_GAME_DIR'}
        err = io.StringIO()
        with mock.patch.dict(os.environ, env, clear=True), contextlib.redirect_stderr(err):
            with self.assertRaises(SystemExit) as caught:
                tool.main(['--install'])
        self.assertEqual(2, caught.exception.code)

    def test_missing_game_folder_is_a_russian_error_not_a_traceback(self):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = tool.main(['--game-dir', os.path.join(ROOT, 'no', 'such', 'game'), '--status'])
        self.assertEqual(2, code)
        self.assertIn('ОШИБКА', err.getvalue())

    def test_default_spike_dll_is_not_the_real_mod_build(self):
        # The real PTMod.dll is mod/build/PTMod.dll; the spike must come from the spikes subfolder.
        self.assertTrue(tool.DEFAULT_SPIKE_BUILD.replace('\\', '/').endswith('mod/build/spikes/loadspike/PTMod.dll'))


if __name__ == '__main__':
    unittest.main()
