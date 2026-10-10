#!/usr/bin/env python3
"""Пути GameObject шаблонов ModUI в data.unity3d (mod-framework, FR-22, TSK-048, AC-22.2).

  python tools/dump_ui_templates.py "<папка игры>"             пути всех шаблонов таблицы в level0 и level1
  python tools/dump_ui_templates.py "<папка игры>" --check     то же + сверка с таблицей в коде мода (код 1 при расхождении)
  python tools/dump_ui_templates.py "<папка игры>" --components  + компоненты каждого шаблона (всё поддерево)
  python tools/dump_ui_templates.py "<папка игры>" --original  читать _original_en/data.unity3d (английский оригинал)
  python tools/dump_ui_templates.py --bundle <data.unity3d>    явный файл вместо папки игры
  python tools/dump_ui_templates.py --self-test                проверка самого скрипта на синтетической сцене (игра не нужна)

Что делает. Таблица шаблонов хранится в одном месте - mod/src/PTMod/Core/UI/ModUITemplates.cs (строки
`new UiTemplate("Window.Modal", 1, "BreakContractWindow", "", "MainCanvas/BreakContractWindow", true, "PauseTrigger,...")`:
ключ, сцена, окно, элемент, путь, обязательность, ожидаемые компоненты). Скрипт читает оттуда ключи и имена, находит их
в иерархии сцены (`ptru.game.Hierarchy.path`, как ищет мод: сначала окно по имени, затем дочерний элемент по имени -
имена вроде `DifficultyDropdown` в сцене повторяются) и печатает для каждой строки ровно один путь или, если имён
подходит несколько, список кандидатов с пометкой. Сцены: level0 (главное меню) и level1 (игра); путь начинается с
корневого объекта сцены, части разделены «/» (у объекта может быть пробел и стрелка в имени: `DESKTOP -> Bottom`).

--check дополнительно проверяет таблицу: каждый ключ дизайна есть в своей сцене; путь в коде равен найденному (или
входит в список кандидатов - тогда пометка «неоднозначно»); ожидаемые компоненты есть в поддереве шаблона. Если игра
обновилась и путь сдвинулся, скрипт печатает правильный путь: его нужно вписать в ModUITemplates.cs.

По умолчанию читается установленный `<игра>/Processor Tycoon_Data/data.unity3d` (то, что видит мод в игре, с переводом);
имена объектов перевод не меняет, пути из `_original_en/data.unity3d` те же.
Код возврата: 0 - всё найдено (и, с --check, совпадает с кодом), 1 - шаблон не найден или расхождение, 2 - ошибка запуска
(нет игры, файла, таблицы). Игра не запускается, файлы игры только читаются (скрипт ничего не пишет), зависимостей кроме
репозитория (ptru/) нет.
"""
import argparse
import os
import re
import struct
import sys
from collections import defaultdict

sys.dont_write_bytecode = True            # ничего не пишем на диск, даже __pycache__

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(HERE)
sys.path.insert(0, REPO_ROOT)

DEFAULT_TABLE = os.path.join(REPO_ROOT, 'mod', 'src', 'PTMod', 'Core', 'UI', 'ModUITemplates.cs')
DATA_DIR = 'Processor Tycoon_Data'
BACKUP_DIR = '_original_en'
BUNDLE = 'data.unity3d'
SCENES = {0: 'level0', 1: 'level1'}

# Ключи и сцены таблицы design.md («Шаблоны»): что обязано быть в коде. Строки сверх этого - дополнения (required=false).
DESIGN_SCENES = {
    'Window.Modal': (1,),
    'Window.Panel': (1,),
    'Window.MenuPanel': (0,),
    'Button.Taskbar': (1,),
    'Button.MenuFallback': (1,),
    'Button.NewGame': (0,),
    'Widget.Button': (1,),
    'Widget.ButtonCTA': (1,),
    'Widget.Toggle': (0, 1),
    'Widget.Slider': (0, 1),
    'Widget.Dropdown': (1, 0),
    'Widget.InfoText': (1,),
    'Mail.Host': (1,),
}

# Не-MonoBehaviour компоненты по классу Unity (для списка компонентов шаблона).
_BUILTIN_CLASSES = {1: 'GameObject', 4: 'Transform', 224: 'RectTransform', 222: 'CanvasRenderer', 223: 'Canvas', 225: 'CanvasGroup',
                    114: 'MonoBehaviour'}

_ROW_RE = re.compile(
    r'new\s+UiTemplate\(\s*"(?P<key>[^"]+)"\s*,\s*(?P<scene>\d+)\s*,\s*"(?P<window>[^"]*)"\s*,\s*"(?P<element>[^"]*)"\s*,'
    r'\s*"(?P<path>[^"]*)"\s*,\s*(?P<required>true|false)\s*,\s*"(?P<expected>[^"]*)"\s*\)')


class ToolError(Exception):
    """Ошибка запуска (код 2): сообщение показывается без трассировки."""


# --------------------------------------------------------------------------------------------------
# таблица из ModUITemplates.cs
# --------------------------------------------------------------------------------------------------

class Row:
    """Строка таблицы шаблонов: то же, что UiTemplate в C#."""
    __slots__ = ('key', 'scene', 'window', 'element', 'path', 'required', 'expected', 'line')

    def __init__(self, key, scene, window, element, path, required, expected, line=0):
        self.key = key
        self.scene = scene
        self.window = window
        self.element = element
        self.path = path
        self.required = required
        self.expected = expected
        self.line = line

    def label(self):
        return '%s (level%d)' % (self.key, self.scene)


def parse_table(text):
    """Строки `new UiTemplate(...)` из текста C#. Комментарии `//` вне строк отбрасываются, чтобы пример в них не считался строкой."""
    rows = []
    for number, line in enumerate(text.splitlines(), 1):
        code = _strip_line_comment(line)
        for m in _ROW_RE.finditer(code):
            expected = [n for n in m.group('expected').split(',') if n]
            rows.append(Row(m.group('key'), int(m.group('scene')), m.group('window'), m.group('element'), m.group('path'),
                            m.group('required') == 'true', expected, number))
    return rows


def _strip_line_comment(line):
    """Обрезает `// ...` вне строковых литералов."""
    in_string = False
    i = 0
    while i < len(line):
        c = line[i]
        if c == '"':
            in_string = not in_string
        elif c == '\\' and in_string:
            i += 1
        elif c == '/' and not in_string and line[i + 1:i + 2] == '/':
            return line[:i]
        i += 1
    return line


def load_table(path):
    try:
        with open(path, encoding='utf-8-sig') as f:
            text = f.read()
    except OSError as e:
        raise ToolError('Не удалось прочитать таблицу шаблонов %s: %s' % (path, e))
    rows = parse_table(text)
    if not rows:
        raise ToolError('В %s нет строк `new UiTemplate("ключ", сцена, "окно", "элемент", "путь", required, "компоненты")`.' % path)
    return rows


# --------------------------------------------------------------------------------------------------
# сцена: иерархия, компоненты, поиск
# --------------------------------------------------------------------------------------------------

def _go_info(data):
    """(компоненты [(file, pathID)], имя, активен) GameObject из сырых данных; None, если данные не разобрать."""
    from ptru.serialized import rstr
    n, = struct.unpack_from('<i', data, 0)
    p = 4
    comps = []
    for _ in range(n):
        fid, pid = struct.unpack_from('<iq', data, p)
        p += 12
        comps.append((fid, pid))
    p += 4                                  # layer
    name, p = rstr(data, p)
    active = None
    if p + 3 <= len(data):                  # tag (2 байта), isActive (1 байт)
        active = bool(data[p + 2])
    return comps, name, active


class SceneView:
    """Иерархия одного файла сцены: имена, порядок обхода, компоненты. Опирается на ptru.game.Hierarchy."""

    def __init__(self, name, sf, scripts):
        from ptru.game import Hierarchy
        self.name = name
        self.sf = sf
        self.scripts = scripts
        self.h = Hierarchy(sf)
        self.components = {}                # gpid -> [(file id, pathID)]
        self.active = {}
        for o in sf.objs:
            if o['cls'] != 1:
                continue
            try:
                comps, _, active = _go_info(sf.data(o))
            except (struct.error, IndexError):
                continue
            self.components[o['pid']] = comps
            self.active[o['pid']] = active
        self.order = self._document_order()
        self.by_name = defaultdict(list)
        for g in self.order:
            self.by_name[self.h.gos[g]].append(g)

    def _document_order(self):
        h = self.h
        roots = [t for t, par in h.tr_parent.items() if not par and h.tr_go.get(t) in h.gos]
        order = []
        seen = set()

        def walk(t):
            g = h.tr_go.get(t)
            if t in seen or g not in h.gos:
                return
            seen.add(t)
            order.append(g)
            for k in h.kids.get(t, []):
                walk(k)

        for r in roots:
            walk(r)
        return order

    def path(self, gpid):
        return self.h.path(gpid)

    def descendants(self, gpid):
        """Потомки без самого объекта, в порядке обхода в глубину."""
        h = self.h
        out = []

        def walk(t):
            for k in h.kids.get(t, []):
                g = h.tr_go.get(k)
                if g in h.gos:
                    out.append(g)
                    walk(k)

        walk(h.go_tr.get(gpid))
        return out

    def component_names(self, gpid):
        """Имена компонентов объекта: классы скриптов для MonoBehaviour, типы Unity для остальных."""
        names = []
        for fid, pid in self.components.get(gpid, []):
            obj = self.sf.byid.get(pid)
            if obj is None:
                names.append('?')
            elif obj['cls'] == 114:
                names.append(self._script_name(obj))
            else:
                names.append(_BUILTIN_CLASSES.get(obj['cls'], 'class%d' % obj['cls']))
        return names

    def _script_name(self, obj):
        data = self.sf.data(obj)
        try:
            _, pid = struct.unpack_from('<iq', data, 16)
        except struct.error:
            return 'MonoBehaviour'
        script = self.scripts.get(pid)
        return script[0] if script else 'MonoBehaviour'

    def subtree_components(self, gpid):
        """Множество имён компонентов объекта и всех его потомков."""
        names = set(self.component_names(gpid))
        for g in self.descendants(gpid):
            names.update(self.component_names(g))
        return names


def candidates(view, row):
    """GameObject'ы сцены, подходящие под имена строки: окно по имени, затем элемент внутри окна (как в TemplateLocator.cs)."""
    if row.window:
        windows = list(view.by_name.get(row.window, []))
        if not row.element:
            return windows
        found = []
        for w in windows:
            for g in view.descendants(w):
                if view.h.gos[g] == row.element and g not in found:
                    found.append(g)
        return found
    return list(view.by_name.get(row.element, []))


# --------------------------------------------------------------------------------------------------
# чтение data.unity3d (только чтение)
# --------------------------------------------------------------------------------------------------

def load_scenes(bundle_path, wanted=SCENES.values()):
    """{имя файла сцены: SceneView}. Файл бандла только читается."""
    from ptru.unityfs import read_bundle
    from ptru.serialized import SerializedFile, rstr
    from ptru.game import SCRIPTS_FILE

    hdr, nodes, data = read_bundle(bundle_path)
    raw = {name: data[off:off + sz] for off, sz, flags, name in nodes}
    for name in list(wanted) + [SCRIPTS_FILE]:
        if name not in raw:
            raise ToolError('В %s нет файла «%s» (это data.unity3d игры?).' % (bundle_path, name))

    scripts = {}
    sf = SerializedFile(raw[SCRIPTS_FILE], SCRIPTS_FILE)
    for o in sf.objs:
        if o['cls'] != 115:                 # MonoScript
            continue
        d = sf.data(o)
        _, p = rstr(d, 0)
        p += 20
        class_name, p = rstr(d, p)
        namespace, p = rstr(d, p)
        assembly, p = rstr(d, p)
        scripts[o['pid']] = (class_name, namespace, assembly)

    return {name: SceneView(name, SerializedFile(raw[name], name), scripts) for name in wanted}


def find_bundle(game_dir, original):
    if not os.path.isdir(game_dir):
        raise ToolError('Нет папки игры: %s' % game_dir)
    installed = os.path.join(game_dir, DATA_DIR, BUNDLE)
    backup = os.path.join(game_dir, BACKUP_DIR, BUNDLE)
    if original:
        if not os.path.isfile(backup):
            raise ToolError('Нет %s (резервная копия оригинала делается установщиком русификатора).' % backup)
        return backup
    if os.path.isfile(installed):
        return installed
    if os.path.isfile(backup):
        return backup
    raise ToolError('В «%s» нет ни %s, ни %s.\n  Укажите папку, где лежит "Processor Tycoon.exe".' % (game_dir, installed, backup))


def default_game_dir():
    env = os.environ.get('PT_GAME_DIR')
    if env:
        return env
    sibling = os.path.join(os.path.dirname(REPO_ROOT), 'Processor Tycoon')
    return sibling if os.path.isdir(sibling) else None


# --------------------------------------------------------------------------------------------------
# разбор и сверка
# --------------------------------------------------------------------------------------------------

class Finding:
    """Результат по одной строке таблицы."""

    def __init__(self, row, view, found):
        self.row = row
        self.view = view
        self.found = found                  # список gpid
        self.paths = [view.path(g) for g in found] if view else []
        self.problems = []                  # (уровень, текст): 'ОШИБКА' или 'ВНИМАНИЕ'

    def error(self, text):
        self.problems.append(('ОШИБКА', text))

    def warning(self, text):
        self.problems.append(('ВНИМАНИЕ', text))

    @property
    def failed(self):
        return any(level == 'ОШИБКА' for level, _ in self.problems)

    @property
    def unique(self):
        return len(self.found) == 1


def analyse(rows, scenes):
    """Ищет каждую строку в своей сцене. Ошибки поиска (нет объекта) пишутся в findings; сверка с кодом - в check()."""
    findings = []
    for row in rows:
        view = scenes.get(SCENES.get(row.scene))
        if view is None:
            f = Finding(row, None, [])
            f.error('нет сцены level%d' % row.scene)
        else:
            f = Finding(row, view, candidates(view, row))
            if not f.found:
                f.error('не найден: окно «%s», элемент «%s»' % (row.window, row.element))
        findings.append(f)
    return findings


def check(rows, findings):
    """Сверка таблицы с игрой: ключи дизайна, повторы, путь в коде, компоненты. Дописывает замечания в findings; возвращает общие."""
    general = []
    seen = {}
    for f in findings:
        pair = (f.row.key, f.row.scene)
        if pair in seen:
            f.error('строка (%s, level%d) повторяется: первая на строке %d' % (pair[0], pair[1], seen[pair]))
        seen.setdefault(pair, f.row.line)

    for key, scenes in DESIGN_SCENES.items():
        for scene in scenes:
            if (key, scene) not in seen:
                general.append('в таблице нет строки %s для level%d (она есть в design.md)' % (key, scene))
    known = set(DESIGN_SCENES)
    for f in findings:
        if f.row.key not in known and f.row.required:
            f.warning('ключа нет в design.md; если он нужен, добавьте его в таблицу design.md')

    for f in findings:
        if not f.found:
            continue
        row = f.row
        if row.path not in f.paths:
            f.error('путь в коде «%s» не совпадает с игрой: %s' % (row.path, '; '.join(f.paths)))
        elif not f.unique:
            f.warning('неоднозначно: %d кандидата(ов), в коде выбран «%s»; мод берёт точный путь, но лучше уточнить окно/элемент' % (len(f.found), row.path))
        chosen = f.found[f.paths.index(row.path)] if row.path in f.paths else f.found[0]
        present = f.view.subtree_components(chosen)
        missing = [name for name in row.expected if name not in present]
        if missing:
            f.error('в поддереве нет ожидаемых компонентов: %s' % ', '.join(missing))
    return general


# --------------------------------------------------------------------------------------------------
# вывод
# --------------------------------------------------------------------------------------------------

def render(findings, show_components, check_mode):
    lines = []
    for f in findings:
        row = f.row
        tag = '' if row.required else '  [доп.]'
        if f.unique:
            lines.append('level%d  %-20s %s%s' % (row.scene, row.key, f.paths[0], tag))
        elif not f.found:
            lines.append('level%d  %-20s НЕ НАЙДЕН (окно «%s», элемент «%s»)%s' % (row.scene, row.key, row.window, row.element, tag))
        else:
            lines.append('level%d  %-20s НЕСКОЛЬКО КАНДИДАТОВ (%d):%s' % (row.scene, row.key, len(f.found), tag))
            for p in f.paths:
                lines.append('        - ' + p)
        if show_components and f.found:
            g = f.found[0]
            active = f.view.active.get(g)
            state = '' if active is None else ('включён' if active else 'выключен') + ' в сцене; '
            lines.append('        %sкомпоненты поддерева: %s' % (state, ', '.join(sorted(f.view.subtree_components(g)))))
        if check_mode or any(level == 'ОШИБКА' for level, _ in f.problems):
            for level, text in f.problems:
                lines.append('        %s: %s' % (level, text))
    return lines


def run(rows, scenes, show_components=False, check_mode=False, out=print):
    """Печатает отчёт; возвращает код возврата (0 или 1)."""
    findings = analyse(rows, scenes)
    general = check(rows, findings) if check_mode else []
    for line in render(findings, show_components, check_mode):
        out(line)
    for text in general:
        out('ОШИБКА: ' + text)

    missing = [f for f in findings if not f.found]
    ambiguous = [f for f in findings if f.found and not f.unique]
    failed = [f for f in findings if f.failed] if check_mode else missing
    keys = {f.row.key for f in findings}
    out('')
    out('Строк: %d, ключей: %d; найдено по одному пути: %d, несколько кандидатов: %d, не найдено: %d.'
        % (len(findings), len(keys), sum(1 for f in findings if f.unique), len(ambiguous), len(missing)))
    if check_mode:
        warnings = sum(1 for f in findings for level, _ in f.problems if level == 'ВНИМАНИЕ')
        if failed or general:
            out('Сверка с ModUITemplates.cs: РАСХОЖДЕНИЯ (строк с ошибками: %d, общих: %d, предупреждений: %d).'
                % (len(failed), len(general), warnings))
        else:
            out('Сверка с ModUITemplates.cs: пути в коде совпадают с игрой%s.'
                % ('' if not warnings else ' (предупреждений: %d)' % warnings))
    return 1 if failed or general else 0


# --------------------------------------------------------------------------------------------------
# самопроверка скрипта (синтетическая сцена; игра не нужна)
# --------------------------------------------------------------------------------------------------

def _pack_str(text):
    raw = text.encode('utf-8')
    pad = (4 - len(raw) % 4) % 4
    return struct.pack('<i', len(raw)) + raw + b'\0' * pad


class _FakeSerialized:
    """Минимальный SerializedFile: только то, что читают Hierarchy и SceneView (objs, data, byid, name)."""

    def __init__(self, name='fake'):
        self.name = name
        self.objs = []
        self.byid = {}
        self._data = {}

    def add(self, pid, cls, data):
        o = {'pid': pid, 'cls': cls, 'start': 0, 'size': len(data)}
        self.objs.append(o)
        self.byid[pid] = o
        self._data[pid] = data

    def data(self, o):
        return self._data[o['pid']]


def _build_fake_scene(tree, scripts):
    """tree: [(имя, активен, [скрипты], [дети])] корни. Возвращает _FakeSerialized. pid: GameObject = 1000+i, RectTransform = 2000+i, MB = 3000+i."""
    sf = _FakeSerialized()
    counter = [0]

    def make(node, parent_rt):
        name, active, comps, kids = node
        i = counter[0]
        counter[0] += 1
        go, rt = 1000 + i, 2000 + i
        comp_pids = [rt]
        for j, script in enumerate(comps):
            mb = 3000 + i * 10 + j
            comp_pids.append(mb)
            script_pid = [pid for pid, s in scripts.items() if s[0] == script][0]
            sf.add(mb, 114, struct.pack('<iq', 0, go) + struct.pack('<i', 1) + struct.pack('<iq', 0, script_pid))
        sf.add(go, 1, struct.pack('<i', len(comp_pids)) + b''.join(struct.pack('<iq', 0, c) for c in comp_pids)
               + struct.pack('<I', 5) + _pack_str(name) + struct.pack('<HB', 0, 1 if active else 0))
        kid_rts = [make(k, rt) for k in kids]
        sf.add(rt, 224, struct.pack('<iq', 0, go) + b'\0' * 40 + struct.pack('<i', len(kid_rts))
               + b''.join(struct.pack('<iq', 0, k) for k in kid_rts) + struct.pack('<iq', 0, parent_rt)
               + struct.pack('<10f', 0, 0, 1, 1, 0, 0, 100, 100, 0.5, 0.5))
        return rt

    for root in tree:
        make(root, 0)
    return sf


def self_test():
    """Проверяет разбор таблицы, поиск окна/элемента, кандидатов, пути с пробелом и стрелкой, сверку и коды возврата."""
    failures = []

    def expect(cond, what):
        if not cond:
            failures.append(what)

    scripts = {7: ('ButtonUI', 'ProcessorTycoon.UI', 'Assembly-CSharp'), 8: ('PauseTrigger', 'ProcessorTycoon.TimeSystem', 'Assembly-CSharp'),
               9: ('ThemeableTopBar', 'ProcessorTycoon.UI.Themes', 'Assembly-CSharp')}
    tree = [
        ('Canvas', True, [], [
            ('Win', True, ['PauseTrigger'], [
                ('Body', True, [], [('Btn', True, ['ButtonUI'], [])]),
                ('TopBar', True, ['ThemeableTopBar'], [])]),
            ('Other', True, [], [('Btn', True, ['ButtonUI'], [])]),
            ('DESKTOP -> Bottom', True, [], [('Door', False, ['ButtonUI'], [])]),
            ('Twin', True, [], []), ('Twin', True, [], [])]),
        ('Side Root', True, [], [('Lone', True, [], [])]),
    ]
    sf = _build_fake_scene(tree, scripts)
    scenes = {'level0': SceneView('level0', _FakeSerialized(), scripts), 'level1': SceneView('level1', sf, scripts)}

    view = scenes['level1']
    expect(view.path(view.by_name['Door'][0]) == 'Canvas/DESKTOP -> Bottom/Door', 'путь со пробелом и стрелкой: %r' % view.path(view.by_name['Door'][0]))
    expect(view.path(view.by_name['Lone'][0]) == 'Side Root/Lone', 'второй корень')
    expect(view.active[view.by_name['Door'][0]] is False and view.active[view.by_name['Win'][0]] is True, 'флаг активности')
    expect(view.component_names(view.by_name['Win'][0]) == ['RectTransform', 'PauseTrigger'], 'компоненты окна: %r' % view.component_names(view.by_name['Win'][0]))
    expect(view.subtree_components(view.by_name['Win'][0]) >= {'ButtonUI', 'ThemeableTopBar', 'PauseTrigger'}, 'поддерево окна')

    table = '''
        // new UiTemplate("Ghost", 1, "", "Nowhere", "x", true, "")   <- пример в комментарии не строка
        new UiTemplate("Window.Modal", 1, "Win", "", "Canvas/Win", true, "PauseTrigger,ThemeableTopBar"),
        new UiTemplate("Widget.Button", 1, "Win", "Btn", "Canvas/Win/Body/Btn", true, "ButtonUI"),   // комментарий после строки
        new UiTemplate("Button.Taskbar", 1, "", "Door", "Canvas/DESKTOP -> Bottom/Door", true, "ButtonUI"),
        new UiTemplate("Widget.Toggle", 1, "Win", "Missing", "Canvas/Win/Missing", false, ""),
        new UiTemplate("Widget.Slider", 1, "", "Btn", "Canvas/Other/Btn", true, ""),
        new UiTemplate("Mail.Host", 1, "", "Twin", "Canvas/Twin", true, ""),
        new UiTemplate("Widget.Dropdown", 1, "Win", "Btn", "Canvas/Wrong/Btn", true, ""),
        new UiTemplate("Widget.InfoText", 1, "Win", "Btn", "Canvas/Win/Body/Btn", true, "InfoText"),
    '''
    rows = parse_table(table)
    expect([r.key for r in rows] == ['Window.Modal', 'Widget.Button', 'Button.Taskbar', 'Widget.Toggle', 'Widget.Slider', 'Mail.Host',
                                     'Widget.Dropdown', 'Widget.InfoText'], 'разбор таблицы: %r' % [r.key for r in rows])
    expect(rows[2].path == 'Canvas/DESKTOP -> Bottom/Door' and rows[3].required is False and rows[1].expected == ['ButtonUI'], 'поля строки')

    findings = analyse(rows, scenes)
    by_key = {f.row.key: f for f in findings}
    expect(by_key['Window.Modal'].paths == ['Canvas/Win'], 'окно по имени')
    expect(by_key['Widget.Button'].paths == ['Canvas/Win/Body/Btn'], 'элемент внутри своего окна, а не одноимённый снаружи: %r' % by_key['Widget.Button'].paths)
    expect(by_key['Widget.Slider'].paths == ['Canvas/Win/Body/Btn', 'Canvas/Other/Btn'], 'элемент без окна: все совпадения в порядке обхода: %r' % by_key['Widget.Slider'].paths)
    expect(not by_key['Widget.Toggle'].found, 'несуществующий элемент')
    expect(len(by_key['Mail.Host'].found) == 2, 'два одноимённых окна')

    general = check(rows, findings)
    expect(any('Window.Modal' not in g and 'нет строки' in g for g in general), 'ключи дизайна, которых нет в таблице теста, перечислены')
    expect(not by_key['Window.Modal'].failed, 'верный путь и компоненты: %r' % by_key['Window.Modal'].problems)
    expect(by_key['Widget.Slider'].failed is False, 'путь входит в список кандидатов: %r' % by_key['Widget.Slider'].problems)
    expect(any(l == 'ВНИМАНИЕ' for l, _ in by_key['Widget.Slider'].problems), 'неоднозначный выбор помечен')
    expect(by_key['Widget.Dropdown'].failed and any('не совпадает' in t for _, t in by_key['Widget.Dropdown'].problems), 'устаревший путь замечен')
    expect(by_key['Widget.InfoText'].failed and any('InfoText' in t for _, t in by_key['Widget.InfoText'].problems), 'нет ожидаемого компонента замечено')
    expect(by_key['Button.Taskbar'].failed is False, 'путь со стрелкой совпал')

    # коды возврата: нужна полная таблица дизайна, чтобы «общих» ошибок не было
    full = []
    for i, (key, key_scenes) in enumerate(DESIGN_SCENES.items()):
        for scene in key_scenes:
            full.append(Row(key, scene, 'Win', '', 'Canvas/Win', True, [], i))
    scenes_ok = {'level0': SceneView('level0', _build_fake_scene([('Canvas', True, [], [('Win', True, [], [])])], scripts), scripts),
                 'level1': scenes['level1']}
    lines = []
    expect(run(full, scenes_ok, check_mode=True, out=lines.append) == 0, 'полная верная таблица даёт код 0: %s' % '\n'.join(lines))
    lines = []
    full[0] = Row(full[0].key, full[0].scene, 'Win', '', 'Canvas/Old', True, [], 0)
    expect(run(full, scenes_ok, check_mode=True, out=lines.append) == 1, 'устаревший путь даёт код 1')
    lines = []
    expect(run(full, scenes_ok, check_mode=False, out=lines.append) == 0, 'без --check путь в коде не проверяется')
    lines = []
    full[1] = Row(full[1].key, full[1].scene, 'Absent', '', 'Canvas/Absent', True, [], 1)
    expect(run(full, scenes_ok, check_mode=False, out=lines.append) == 1, 'ненайденный шаблон даёт код 1 и без --check')

    # таблица мода в репозитории разбирается и покрывает дизайн (если файл есть)
    if os.path.isfile(DEFAULT_TABLE):
        real = load_table(DEFAULT_TABLE)
        pairs = {(r.key, r.scene) for r in real}
        expect(len(pairs) == len(real), 'в ModUITemplates.cs нет повторов (ключ, сцена)')
        for key, key_scenes in DESIGN_SCENES.items():
            for scene in key_scenes:
                expect((key, scene) in pairs, 'ModUITemplates.cs: нет %s для level%d' % (key, scene))

    for text in failures:
        print('самопроверка: ПРОВАЛ: ' + text)
    print('самопроверка: %s' % ('все проверки пройдены' if not failures else 'провалов: %d' % len(failures)))
    return 1 if failures else 0


# --------------------------------------------------------------------------------------------------

def parse_args(argv):
    ap = argparse.ArgumentParser(
        prog='python tools/dump_ui_templates.py',
        description='Пути GameObject шаблонов ModUI в data.unity3d (только чтение).')
    ap.add_argument('game_dir', nargs='?', metavar='ПАПКА_ИГРЫ',
                    help='папка, где лежит "Processor Tycoon.exe" (или переменная PT_GAME_DIR)')
    ap.add_argument('--bundle', metavar='ФАЙЛ', help='data.unity3d вместо папки игры')
    ap.add_argument('--original', action='store_true', help='читать _original_en/data.unity3d (английский оригинал)')
    ap.add_argument('--table', metavar='ФАЙЛ', default=DEFAULT_TABLE, help='C#-файл с таблицей (по умолчанию ModUITemplates.cs)')
    ap.add_argument('--check', action='store_true', help='сверить таблицу в коде с игрой; код 1 при расхождении')
    ap.add_argument('--components', action='store_true', help='печатать компоненты поддерева каждого шаблона')
    ap.add_argument('--self-test', action='store_true', help='проверить сам скрипт на синтетической сцене (игра не нужна)')
    return ap.parse_args(argv)


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        if not stream.isatty():             # вывод в канал - всегда UTF-8, иначе кириллица портится
            try:
                stream.reconfigure(encoding='utf-8', errors='replace')
            except Exception:
                pass

    args = parse_args(argv if argv is not None else sys.argv[1:])
    if args.self_test:
        return self_test()

    try:
        rows = load_table(args.table)
        if args.bundle:
            bundle = args.bundle
            if not os.path.isfile(bundle):
                raise ToolError('Нет файла %s.' % bundle)
        else:
            game_dir = args.game_dir or default_game_dir()
            if not game_dir:
                raise ToolError('Не указана папка игры.\n'
                                '  python tools/dump_ui_templates.py "C:\\Games\\Processor Tycoon"\n'
                                '  или задайте переменную окружения PT_GAME_DIR (или --bundle <data.unity3d>).')
            bundle = find_bundle(os.path.abspath(game_dir.strip().strip('"')), args.original)

        table_name = os.path.relpath(args.table, REPO_ROOT) if os.path.abspath(args.table).startswith(REPO_ROOT) else args.table
        print('Таблица: %s' % table_name)
        print('Данные:  %s (только чтение)' % bundle)
        print('')
        scenes = load_scenes(bundle)
        return run(rows, scenes, show_components=args.components, check_mode=args.check)
    except ToolError as e:
        print('\nОШИБКА: %s' % e, file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
