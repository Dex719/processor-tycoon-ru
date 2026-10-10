"""Tests of tools/dump_ui_templates.py (mod-framework TSK-048): the template table of ModUITemplates.cs and the script that checks it.

Run from the repository root:  python -m unittest discover -s mod/tests/installer
The script brings its own self-test (a synthetic scene, no game needed); here it is wired into the suite together with checks of the
real table (19 rows, the keys of design.md, scene roots). One test reads the installed game's data.unity3d, read-only, and is skipped
when the game folder is not at hand (PT_GAME_DIR or the usual place next to the repository).
"""
import contextlib
import importlib.util
import io
import os
import sys
import unittest

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
TOOL = os.path.join(ROOT, 'tools', 'dump_ui_templates.py')
TABLE = os.path.join(ROOT, 'mod', 'src', 'PTMod', 'Core', 'UI', 'ModUITemplates.cs')

sys.dont_write_bytecode = True
_spec = importlib.util.spec_from_file_location('dump_ui_templates', TOOL)
tool = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(tool)


def _game_bundle():
    """data.unity3d of the installed game, or None. Never written, never copied."""
    candidates = []
    if os.environ.get('PT_GAME_DIR'):
        candidates.append(os.environ['PT_GAME_DIR'])
    candidates.append(os.path.join(os.path.dirname(ROOT), 'Processor Tycoon'))
    for folder in candidates:
        path = os.path.join(folder, tool.DATA_DIR, tool.BUNDLE)
        if os.path.isfile(path):
            return path
    return None


class TemplateTableTests(unittest.TestCase):
    def test_built_in_self_test_passes(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            code = tool.self_test()
        self.assertEqual(0, code, out.getvalue())

    def test_table_has_19_rows_and_every_design_key_and_scene(self):
        rows = tool.load_table(TABLE)
        self.assertEqual(19, len(rows))
        pairs = {(r.key, r.scene) for r in rows}
        self.assertEqual(len(rows), len(pairs), 'a (key, scene) pair is listed twice')
        for key, scenes in tool.DESIGN_SCENES.items():
            for scene in scenes:
                self.assertIn((key, scene), pairs, 'design.md needs %s for level%d' % (key, scene))
        self.assertEqual(13, len({r.key for r in rows}))

    def test_extra_rows_are_the_three_optional_ones_of_the_main_menu(self):
        rows = tool.load_table(TABLE)
        optional = sorted((r.key, r.scene) for r in rows if not r.required)
        self.assertEqual([('Widget.Button', 0), ('Widget.ButtonCTA', 0), ('Widget.InfoText', 0)], optional)

    def test_paths_start_at_the_root_of_their_scene_and_end_with_their_element(self):
        for r in tool.load_table(TABLE):
            segments = r.path.split('/')
            self.assertEqual('Main Canvas' if r.scene == 0 else 'MainCanvas', segments[0], r.label())
            self.assertEqual(r.element or r.window, segments[-1], r.label())
            if r.window:
                self.assertIn(r.window, segments, r.label())

    def test_a_comment_with_a_row_inside_is_not_a_row(self):
        text = '// new UiTemplate("X.Y", 0, "W", "E", "A/B", true, "C")\nnew UiTemplate("K", 1, "", "E", "MainCanvas/E", true, "")\n'
        rows = tool.parse_table(text)
        self.assertEqual(['K'], [r.key for r in rows])

    @unittest.skipUnless(_game_bundle(), 'the installed game (data.unity3d) is not available')
    def test_table_matches_the_installed_game_read_only(self):
        bundle = _game_bundle()
        before = os.stat(bundle)
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            code = tool.main(['--bundle', bundle, '--check'])
        self.assertEqual(0, code, out.getvalue())
        self.assertIn('пути в коде совпадают с игрой', out.getvalue())
        after = os.stat(bundle)
        self.assertEqual((before.st_size, before.st_mtime_ns), (after.st_size, after.st_mtime_ns), 'the script must only read')


if __name__ == '__main__':
    unittest.main()
