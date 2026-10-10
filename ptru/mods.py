"""Mod support for the installer: registering the mod assembly in Unity's JSON files (decision 0001).

Unity reads two files from "<game>/Processor Tycoon_Data/" at startup:
  ScriptingAssemblies.json     {"names": ["Assembly-CSharp.dll", ...], "types": [16, ...]}
  RuntimeInitializeOnLoads.json {"root": [{"assemblyName": ..., "className": ..., "methodName": ..., ...}, ...]}
The mod is "installed" by appending one entry to each. These files belong to the game, so edits are made to the
TEXT, not by re-serializing the whole document: only the inserted (or removed) element changes, and everything else
- key order, indentation, separators, escapes, line endings, BOM, trailing newline - stays byte-for-byte. Removing
an entry therefore restores the original text, and a repeated registration is a no-op that does not touch the file.

Only the standard library is used. The functions come in two layers: *_text work on a str (pure, unit-tested), the
file-level register_*/unregister_* wrap them with reading, a safety check and an atomic write.

The second half of the module installs the mod into a game folder for `install.py --with-mods` (FR-20): where the
DLLs come from (find_mod_dlls, build_with_csc), the game-version gate, prepare_install (every refusal, read-only)
and install_mods (the writes, rolled back as a whole if a step fails), plus the manifest _original_en/ptmod_install.json.

The last two sections are the other half of FR-20 (TSK-045): remove_mods undoes install_mods for `--remove-mods` and for
the mod part of `--restore` (files by the manifest, both JSON back to their original text, the player's PTMod/ folder
untouched), and check_mod_sources is the mod part of `--check` (the content and the string tables in mod/src/PTMod are
well-formed, ids unique, ru and en agree, ids and keys carry the prefix of their feature - no game needed).
"""
import hashlib, json, math, os, re, shutil, struct, subprocess, sys, tempfile, time
from collections import namedtuple

DATA_DIR = 'Processor Tycoon_Data'
SCRIPTING_ASSEMBLIES_JSON = 'ScriptingAssemblies.json'
RUNTIME_INIT_JSON = 'RuntimeInitializeOnLoads.json'

# Assumed Unity format (decision 0001, requirements FR-20 "Assumptions"). The real layout is confirmed by spike 0001
# (TSK-005); everything the code assumes about it lives in this block, so a different layout is a constants change.
ASSEMBLY_NAMES_KEY = 'names'
ASSEMBLY_TYPES_KEY = 'types'
ASSEMBLY_TYPE = 16                      # "user script assembly", as for Assembly-CSharp.dll
RUNTIME_INIT_ROOT_KEY = 'root'
RUNTIME_INIT_MATCH_KEYS = ('assemblyName', 'className')   # identity of a RuntimeInitializeOnLoads entry (design.md)
LOAD_TYPE_BEFORE_SCENE_LOAD = 1         # RuntimeInitializeLoadType.BeforeSceneLoad

MOD_ASSEMBLY = 'PTMod.dll'
HARMONY_ASSEMBLY = '0Harmony.dll'       # not registered by default; spike 0001 decides whether Mono needs it listed
BACKUP_DIR = '_original_en'             # same folder install.py uses for the translation originals

_BOM = b'\xef\xbb\xbf'
_WS = ' \t\r\n'
_DEC = json.JSONDecoder()
_OBJ_HEAD = re.compile(r'\{(\s*)"(?:[^"\\]|\\.)*"(\s*):(\s*)')   # '{', key, ':' - captures the three whitespace runs
_ITEM_GAP = re.compile(r'(\s*),(\s*)"')                          # ',' between two members, up to the next key
_Array = namedtuple('_Array', 'open close elems')                # elems: [(start, end, value)], close = index of ']'


class ModsJsonError(Exception):
    """A Unity JSON file is missing, broken, unexpectedly shaped or cannot be written. str(e) is Russian user text."""


def scripting_assemblies_path(game_dir):
    return os.path.join(game_dir, DATA_DIR, SCRIPTING_ASSEMBLIES_JSON)


def runtime_init_path(game_dir):
    return os.path.join(game_dir, DATA_DIR, RUNTIME_INIT_JSON)


def runtime_init_entry(assembly='PTMod', namespace='PTMod.Core', class_name='ModBootstrap', method='Init',
                       load_types=LOAD_TYPE_BEFORE_SCENE_LOAD):
    """RuntimeInitializeOnLoads.json entry; the defaults are the mod's own (decision 0001)."""
    return {'assemblyName': assembly, 'nameSpace': namespace, 'className': class_name, 'methodName': method,
            'loadTypes': load_types, 'isUnityClass': False}


# ---------------------------------------------------------------------------------------------------------------
# Reading, validation, error messages
# ---------------------------------------------------------------------------------------------------------------

_JSON_ERRORS_RU = (
    ('Expecting value', 'ожидалось значение'),
    ('Expecting property name enclosed in double quotes', 'ожидалось имя поля в двойных кавычках'),
    ("Expecting ':' delimiter", 'ожидалось двоеточие'),
    ("Expecting ',' delimiter", 'ожидалась запятая или закрывающая скобка'),
    ('Unterminated string', 'строка не закрыта'),
    ('Extra data', 'лишние данные после конца JSON'),
    ('Invalid control character', 'недопустимый управляющий символ в строке'),
    ('Invalid \\escape', 'неверная escape-последовательность'),
    ('Invalid \\u', 'неверная escape-последовательность \\u'),
)


def _fail(label, what):
    raise ModsJsonError('Файл "%s" %s Файл не изменён.' % (label, what))


def _load(text, label):
    """Parse `text` (must be a JSON object) with Russian errors that carry the line and column."""
    if not text.strip(_WS):
        _fail(label, 'пуст — это не файл игры или он повреждён. Проверьте целостность файлов игры и повторите.')
    try:
        data = json.loads(text)
    except json.JSONDecodeError as e:
        why = next((ru for en, ru in _JSON_ERRORS_RU if e.msg.startswith(en)), e.msg)
        _fail(label, 'повреждён: некорректный JSON — %s (строка %d, столбец %d). Восстановите оригинал из папки "%s" '
                     'или проверьте целостность файлов игры и повторите.' % (why, e.lineno, e.colno, BACKUP_DIR))
    except (ValueError, RecursionError):
        _fail(label, 'повреждён: некорректный JSON. Проверьте целостность файлов игры и повторите.')
    if not isinstance(data, dict):
        _fail(label, 'имеет неожиданный формат: корневой элемент не объект JSON.')
    return data


def _array_of(data, key, label, kind=None):
    value = data.get(key)
    if not isinstance(value, list):
        _fail(label, 'имеет неожиданный формат: нет массива "%s". Возможно, у этой версии игры другой формат файла.' % key)
    if kind is not None and not all(isinstance(v, kind) for v in value):
        _fail(label, 'имеет неожиданный формат: в массиве "%s" не те значения, что ожидались.' % key)
    return value


def _assembly_data(text, label):
    data = _load(text, label)
    names = _array_of(data, ASSEMBLY_NAMES_KEY, label, str)
    types = _array_of(data, ASSEMBLY_TYPES_KEY, label)
    if len(names) != len(types):
        _fail(label, 'имеет неожиданный формат: массивы "%s" и "%s" разной длины.' % (ASSEMBLY_NAMES_KEY, ASSEMBLY_TYPES_KEY))
    return data


def _runtime_data(text, label):
    data = _load(text, label)
    _array_of(data, RUNTIME_INIT_ROOT_KEY, label)
    return data


def _entry(entry):
    entry = runtime_init_entry() if entry is None else entry
    missing = [k for k in RUNTIME_INIT_MATCH_KEYS if not entry.get(k)]
    if missing:
        raise ValueError('В записи RuntimeInitializeOnLoads не заданы поля: %s.' % ', '.join(missing))
    return entry


def _matches(element, entry):
    return isinstance(element, dict) and all(element.get(k) == entry.get(k) for k in RUNTIME_INIT_MATCH_KEYS)


def _verified(new_text, expected, label):
    """Safety net for the text surgery: the result must parse to exactly the intended document, else nothing is written."""
    try:
        ok = json.loads(new_text) == expected
    except ValueError:
        ok = False
    if not ok:
        _fail(label, 'не удалось отредактировать безопасно (внутренняя ошибка, результат правки не совпал с ожидаемым). '
                     'Сообщите об ошибке разработчикам.')
    return new_text


# ---------------------------------------------------------------------------------------------------------------
# Text surgery on a valid JSON document
# ---------------------------------------------------------------------------------------------------------------

def _skip_ws(text, i):
    n = len(text)
    while i < n and text[i] in _WS:
        i += 1
    return i


def _scan_array(text, i):
    """Spans of an array starting at text[i] == '['. The text is already known to be valid JSON."""
    open_pos = i
    i += 1
    elems = []
    while True:
        i = _skip_ws(text, i)
        if text[i] == ']':
            return _Array(open_pos, i, elems)
        value, end = _DEC.raw_decode(text, i)
        elems.append((i, end, value))
        i = _skip_ws(text, end)
        if text[i] == ',':
            i += 1


def _root_members(text):
    """[(key, _Array or None)] for the members of the root object, in file order."""
    i = _skip_ws(text, 0) + 1                                  # past '{'
    members = []
    while True:
        i = _skip_ws(text, i)
        if text[i] == '}':
            return members
        key, i = _DEC.raw_decode(text, i)
        i = _skip_ws(text, _skip_ws(text, i) + 1)              # past ':'
        if text[i] == '[':
            arr = _scan_array(text, i)
            end = arr.close + 1
        else:
            arr = None
            _, end = _DEC.raw_decode(text, i)
        members.append((key, arr))
        i = _skip_ws(text, end)
        if text[i] == ',':
            i += 1


def _find_array(text, key, label):
    hits = [arr for k, arr in _root_members(text) if k == key]
    if len(hits) > 1:
        _fail(label, 'имеет неожиданный формат: ключ "%s" встречается несколько раз.' % key)
    if not hits or hits[0] is None:
        _fail(label, 'имеет неожиданный формат: нет массива "%s".' % key)
    return hits[0]


def _member_gap(src):
    """Whitespace after the first comma between two members of the object `src`, '' if it has no such comma."""
    head = _OBJ_HEAD.match(src)
    if head is None:
        return ''
    _, value_end = _DEC.raw_decode(src, head.end())
    gap = _ITEM_GAP.match(src, value_end)
    return gap.group(2) if gap else ''


def _dump_like(value, text, arr):
    """Serialize a new array element. Objects copy the layout of the array's first object (compact `{"a":1,"b":2}`,
    spaced, or indented multi-line); scalars and arrays with no object to copy from are written compactly."""
    compact = dict(separators=(',', ':'), ensure_ascii=False)
    sample = next(((s, e) for s, e, v in arr.elems if isinstance(v, dict) and v), None)
    if not isinstance(value, dict) or not value or sample is None:
        return json.dumps(value, **compact)
    start, end = sample
    src = text[start:end]
    head = _OBJ_HEAD.match(src)
    if head is None:
        return json.dumps(value, **compact)
    lead, before, after = head.groups()                        # ws after '{', before ':', after ':'
    key_sep = before + ':' + after
    if '\n' in lead:                                           # multi-line: indent the element like its neighbours
        own = text[text.rfind('\n', 0, start) + 1:start]       # the element's own line indentation
        if own.strip(_WS):
            own = ''
        inner = lead.rsplit('\n', 1)[-1]                       # indentation of the first key
        unit = inner[len(own):] if inner.startswith(own) else inner
        out = json.dumps(value, indent=unit or '  ', separators=(',', key_sep), ensure_ascii=False)
        return out.replace('\n', '\n' + own)
    return json.dumps(value, separators=(',' + _member_gap(src), key_sep), ensure_ascii=False)


def _append(text, key, value, label):
    """Insert `value` as the last element of the root array `key`, copying the neighbours' separator."""
    arr = _find_array(text, key, label)
    item = _dump_like(value, text, arr)
    if not arr.elems:
        at, ins = arr.open + 1, item
    else:
        last = arr.elems[-1]
        if len(arr.elems) > 1:
            sep = text[arr.elems[-2][1]:last[0]]               # ',' plus whatever whitespace the file uses
        else:                                                  # one element: the padding after '[' or, failing that,
            sep = ',' + (text[arr.open + 1:last[0]] or _member_gap(text[last[0]:last[1]]))   # the object's own gap
        at, ins = last[1], sep + item
    return text[:at] + ins + text[at:]


def _delete(text, key, index, label):
    """Remove element `index` of the root array `key` together with one separator - the inverse of _append."""
    arr = _find_array(text, key, label)
    el = arr.elems
    if len(el) == 1:
        a, b = arr.open + 1, arr.close
    elif index == len(el) - 1:
        a, b = el[index - 1][1], el[index][1]
    else:
        a, b = el[index][0], el[index + 1][0]
    return text[:a] + text[b:]


# ---------------------------------------------------------------------------------------------------------------
# Public API, text level (pure)
# ---------------------------------------------------------------------------------------------------------------

def assembly_registered_text(text, name=MOD_ASSEMBLY, label='ScriptingAssemblies.json'):
    return name in _assembly_data(text, label)[ASSEMBLY_NAMES_KEY]


def add_assembly_text(text, name=MOD_ASSEMBLY, asm_type=ASSEMBLY_TYPE, label='ScriptingAssemblies.json'):
    """`text` with `name`/`asm_type` appended to names/types; `text` itself (same object) if already listed."""
    data = _assembly_data(text, label)
    if name in data[ASSEMBLY_NAMES_KEY]:
        return text
    out = _append(text, ASSEMBLY_NAMES_KEY, name, label)
    out = _append(out, ASSEMBLY_TYPES_KEY, asm_type, label)
    data[ASSEMBLY_NAMES_KEY].append(name)
    data[ASSEMBLY_TYPES_KEY].append(asm_type)
    return _verified(out, data, label)


def remove_assembly_text(text, name=MOD_ASSEMBLY, label='ScriptingAssemblies.json'):
    """`text` without every `name` in names (and the same positions in types); the same object if not listed."""
    data = _assembly_data(text, label)
    hits = [i for i, n in enumerate(data[ASSEMBLY_NAMES_KEY]) if n == name]
    out = text
    for i in reversed(hits):                                   # from the end, so earlier indexes stay valid
        out = _delete(out, ASSEMBLY_NAMES_KEY, i, label)
        out = _delete(out, ASSEMBLY_TYPES_KEY, i, label)
        del data[ASSEMBLY_NAMES_KEY][i], data[ASSEMBLY_TYPES_KEY][i]
    return _verified(out, data, label) if hits else text


def runtime_init_registered_text(text, entry=None, label='RuntimeInitializeOnLoads.json'):
    entry = _entry(entry)
    return any(_matches(el, entry) for el in _runtime_data(text, label)[RUNTIME_INIT_ROOT_KEY])


def add_runtime_init_text(text, entry=None, label='RuntimeInitializeOnLoads.json'):
    """`text` with `entry` appended to root; the same object if an entry with the same identity is already there."""
    entry = _entry(entry)
    data = _runtime_data(text, label)
    root = data[RUNTIME_INIT_ROOT_KEY]
    if any(_matches(el, entry) for el in root):
        return text
    out = _append(text, RUNTIME_INIT_ROOT_KEY, entry, label)
    root.append(entry)
    return _verified(out, data, label)


def remove_runtime_init_text(text, entry=None, label='RuntimeInitializeOnLoads.json'):
    """`text` without every root entry that matches `entry`; the same object if there is none."""
    entry = _entry(entry)
    data = _runtime_data(text, label)
    root = data[RUNTIME_INIT_ROOT_KEY]
    hits = [i for i, el in enumerate(root) if _matches(el, entry)]
    out = text
    for i in reversed(hits):
        out = _delete(out, RUNTIME_INIT_ROOT_KEY, i, label)
        del root[i]
    return _verified(out, data, label) if hits else text


# ---------------------------------------------------------------------------------------------------------------
# Files: read, atomic write, backup
# ---------------------------------------------------------------------------------------------------------------

def _read_json_text(path):
    """(text, bom): the file decoded as UTF-8 with line endings untouched; the BOM is split off to be written back."""
    try:
        with open(path, 'rb') as f:
            data = f.read()
    except FileNotFoundError:
        raise ModsJsonError('Файл "%s" не найден. Проверьте, что указана папка игры, где лежит "Processor Tycoon.exe".' % path)
    except OSError as e:
        raise ModsJsonError('Не удалось прочитать файл "%s": %s.' % (path, e.strerror or e))
    bom = _BOM if data.startswith(_BOM) else b''
    try:
        return data[len(bom):].decode('utf-8'), bom
    except UnicodeDecodeError:
        _fail(path, 'не в кодировке UTF-8 — править его небезопасно.')


def write_atomic(path, data):
    """Write bytes to `path` through a temp file in the same folder and os.replace. Whatever fails, the previous
    content of `path` stays intact and no temp file is left behind."""
    folder = os.path.dirname(os.path.abspath(path))
    tmp = None
    try:
        fd, tmp = tempfile.mkstemp(dir=folder, prefix=os.path.basename(path) + '.', suffix='.tmp')
        with os.fdopen(fd, 'wb') as f:
            f.write(data)
            f.flush()
            os.fsync(f.fileno())
        try:
            shutil.copymode(path, tmp)                         # keep permissions of the file being replaced
        except OSError:
            pass
        os.replace(tmp, path)
    except OSError as e:
        if tmp is not None:
            try:
                os.remove(tmp)
            except OSError:
                pass
        raise ModsJsonError('Не удалось записать файл "%s": %s. Закройте игру и повторите. Файл не изменён.'
                            % (path, e.strerror or e))
    except BaseException:
        if tmp is not None:
            try:
                os.remove(tmp)
            except OSError:
                pass
        raise


def backup_json(json_path, backup_dir):
    """Save the pristine `json_path` into `backup_dir` (created if needed) under its own name, unless a backup is
    already there - an existing backup is the original and is never overwritten. True if a copy was made."""
    dst = os.path.join(backup_dir, os.path.basename(json_path))
    if os.path.exists(dst):
        return False
    try:
        with open(json_path, 'rb') as f:
            data = f.read()
        os.makedirs(backup_dir, exist_ok=True)
    except FileNotFoundError:
        raise ModsJsonError('Файл "%s" не найден — резервную копию сделать не из чего.' % json_path)
    except OSError as e:
        raise ModsJsonError('Не удалось сделать резервную копию "%s": %s.' % (json_path, e.strerror or e))
    write_atomic(dst, data)
    try:
        shutil.copystat(json_path, dst)
    except OSError:
        pass
    return True


def _edit_file(path, edit):
    text, bom = _read_json_text(path)
    new = edit(text, path)
    if new == text:
        return False
    write_atomic(path, bom + new.encode('utf-8'))
    return True


def is_assembly_registered(json_path, name=MOD_ASSEMBLY):
    text, _ = _read_json_text(json_path)
    return assembly_registered_text(text, name, json_path)


def is_runtime_init_registered(json_path, entry=None):
    text, _ = _read_json_text(json_path)
    return runtime_init_registered_text(text, entry, json_path)


def register_assembly(json_path, name=MOD_ASSEMBLY, asm_type=ASSEMBLY_TYPE):
    """Add `name` (type `asm_type`) to ScriptingAssemblies.json. Idempotent. True if the file was changed."""
    return _edit_file(json_path, lambda text, label: add_assembly_text(text, name, asm_type, label))


def unregister_assembly(json_path, name=MOD_ASSEMBLY):
    """Remove `name` from ScriptingAssemblies.json, giving back the text it had before registration. True if changed."""
    return _edit_file(json_path, lambda text, label: remove_assembly_text(text, name, label))


def register_runtime_init(json_path, entry=None):
    """Add the RuntimeInitializeOnLoads.json entry (default: PTMod.Core.ModBootstrap.Init). Idempotent. True if changed."""
    return _edit_file(json_path, lambda text, label: add_runtime_init_text(text, entry, label))


def unregister_runtime_init(json_path, entry=None):
    """Remove the matching RuntimeInitializeOnLoads.json entry, restoring the previous text. True if changed."""
    return _edit_file(json_path, lambda text, label: remove_runtime_init_text(text, entry, label))


# ---------------------------------------------------------------------------------------------------------------
# Installing into a game folder (FR-20, TSK-044 and TSK-068)
# ---------------------------------------------------------------------------------------------------------------

MANAGED_DIR = 'Managed'
ASSEMBLY_CSHARP = 'Assembly-CSharp.dll'
MANIFEST_NAME = 'ptmod_install.json'            # in BACKUP_DIR; tools/spike_install.py reads its presence as "the real mod is installed"
SPIKE_MANIFEST_NAME = 'ptmod_spike_install.json'
MANIFEST_SCHEMA = 1
USER_DIR = 'PTMod'                              # <game>/PTMod/{content,l10n}: the player's overrides, never deleted by the installer
CHECKSUMS_NAME = 'SHA256SUMS.txt'
RELEASES_URL = 'https://github.com/Dex719/processor-tycoon-ru/releases'
REGISTER_HARMONY = False                        # spike 0001 decides whether 0Harmony.dll must be listed in ScriptingAssemblies.json
BUILD_TIMEOUT = 900                             # seconds allowed for mod/build.py
VERSION_SCAN_LIMIT = 32 << 20                   # bytes of a DLL read while looking for its version resource

README_CONTENT = """Папка PTMod\\content: ваши правки контента мода Processor Tycoon (PTMod).

Как это работает
  Положите сюда JSON-файл по тому же пути, что и у встроенного: <фича>\\<файл>.json
  (например, framework-sample\\samples.json). Запись с тем же "id" заменяет встроенную,
  запись с новым "id" добавляется, запись с "disabled": true убирается.
  Файлы читаются при запуске игры. Неверная запись пропускается, остальные работают;
  причина записывается в журнал мода.

Что не трогается
  Установщик (install.py) и команда --remove-mods эту папку не удаляют и не меняют,
  поэтому ваши файлы переживают переустановку и снятие модов.
"""

README_L10N = """Папка PTMod\\l10n: ваши правки текстов мода Processor Tycoon (PTMod).

Как это работает
  Положите сюда JSON-таблицу строк в том же формате, что и встроенные (ru.json, en.json,
  <язык>\\<фича>.json). Ключ из этой папки заменяет встроенный; если ключа здесь нет,
  берётся встроенный текст. Файлы читаются при запуске игры.

Что не трогается
  Установщик (install.py) и команда --remove-mods эту папку не удаляют и не меняют,
  поэтому ваши файлы переживают переустановку и снятие модов.
"""


class ModsInstallError(Exception):
    """The mod cannot be installed: a refusal or an environment problem. str(e) is Russian user text. It is raised
    before anything is written, or after the failed step has been rolled back."""


ModDlls = namedtuple('ModDlls', 'mod harmony folder label checksums')    # checksums: path of SHA256SUMS.txt or None
InstallPlan = namedtuple('InstallPlan', 'dlls version build mods_only game_version known force')


def managed_dir(game_dir):
    return os.path.join(game_dir, DATA_DIR, MANAGED_DIR)


def backup_dir(game_dir):
    return os.path.join(game_dir, BACKUP_DIR)


def manifest_path(game_dir):
    return os.path.join(backup_dir(game_dir), MANIFEST_NAME)


def user_dir(game_dir):
    return os.path.join(game_dir, USER_DIR)


def rel_path(game_dir, path):
    """`path` relative to the game folder, with '/' - the form used in the manifest."""
    return os.path.relpath(path, game_dir).replace(os.sep, '/')


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def _read_bytes(path):
    """File content, None if there is no such file."""
    try:
        with open(path, 'rb') as f:
            return f.read()
    except FileNotFoundError:
        return None


# ---------------------------------------------------------------------------------------------------------------
# Where the DLLs come from: --mod-dir, mod/build/, next to install.py, else a build with csc (FR-20 p. 10)
# ---------------------------------------------------------------------------------------------------------------

def _dlls_in(folder, label):
    """(ModDlls, []) when `folder` holds both DLLs, else (None, [names of the missing ones])."""
    mod, harmony = os.path.join(folder, MOD_ASSEMBLY), os.path.join(folder, HARMONY_ASSEMBLY)
    missing = [os.path.basename(p) for p in (mod, harmony) if not os.path.isfile(p)]
    if missing:
        return None, missing
    sums = os.path.join(folder, CHECKSUMS_NAME)
    return ModDlls(mod, harmony, folder, label, sums if os.path.isfile(sums) else None), []


def find_mod_dlls(repo_dir, mod_dir=None):
    """The first folder that holds both PTMod.dll and 0Harmony.dll, in the order of FR-20 p. 10: the --mod-dir
    argument, `repo_dir`/mod/build, `repo_dir` itself (a GitHub Release unpacked next to install.py). None if there is
    none. A --mod-dir that does not fit is an error, not a reason to quietly take a different build. No network."""
    if mod_dir:
        folder = os.path.abspath(mod_dir.strip().strip('"'))
        found, missing = _dlls_in(folder, '--mod-dir') if os.path.isdir(folder) else (None, None)
        if found is None:
            what = ('папка не существует' if missing is None else 'в ней нет: %s' % ', '.join(missing))
            raise ModsInstallError(
                'Папка --mod-dir "%s" не подходит: %s.\n'
                'Укажите папку, где лежат PTMod.dll и 0Harmony.dll (распакованный релиз или mod\\build). '
                'Файлы игры не тронуты.' % (folder, what))
        return found
    for folder, label in ((os.path.join(repo_dir, 'mod', 'build'), 'mod\\build'),
                          (repo_dir, 'папка установщика')):
        found, _ = _dlls_in(folder, label)
        if found is not None:
            return found
    return None


def csc_candidates():
    """Roslyn csc.exe candidates, best first: PT_CSC, then vswhere, then the usual Visual Studio folders (the same
    places mod/build.py looks in)."""
    found = []

    def add(path):
        if path and path not in found:
            found.append(path)

    add(os.environ.get('PT_CSC'))
    pf86 = os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')
    pf = os.environ.get('ProgramFiles', r'C:\Program Files')
    vswhere = os.path.join(pf86, 'Microsoft Visual Studio', 'Installer', 'vswhere.exe')
    if os.path.isfile(vswhere):
        try:
            out = subprocess.run([vswhere, '-products', '*', '-requires', 'Microsoft.Component.MSBuild',
                                  '-find', r'MSBuild\**\Bin\Roslyn\csc.exe'],
                                 stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30).stdout
            for line in out.decode('utf-8', 'replace').splitlines():
                add(line.strip())
        except (OSError, subprocess.SubprocessError):
            pass
    for year in ('2022', '2019'):
        for base in (pf86, pf):
            for edition in ('BuildTools', 'Community', 'Professional', 'Enterprise'):
                add(os.path.join(base, 'Microsoft Visual Studio', year, edition,
                                 'MSBuild', 'Current', 'Bin', 'Roslyn', 'csc.exe'))
    on_path = shutil.which('csc')
    if on_path and 'roslyn' in on_path.lower():            # the old csc of .NET Framework knows only C# 5
        add(on_path)
    return found


def _csc_works(path):
    if not os.path.isfile(path):
        return False
    try:
        return subprocess.run([path, '-version'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                              timeout=60).returncode == 0
    except (OSError, subprocess.SubprocessError):
        return False


def find_csc():
    """Path of a working Roslyn csc.exe, None if the machine has none."""
    for path in csc_candidates():
        if _csc_works(path):
            return path
    return None


def no_dlls_message(repo_dir, why):
    """The instruction shown when there are neither ready DLLs nor a way to build them (AC-20.5)."""
    return ('Не найдены готовые PTMod.dll и 0Harmony.dll, а собрать мод на этом компьютере нечем (%s).\n'
            'Что сделать:\n'
            '  1. Скачайте готовые файлы: архив PTMod-<версия>.zip со страницы GitHub Release проекта\n'
            '     %s\n'
            '  2. Распакуйте PTMod.dll и 0Harmony.dll в папку рядом с install.py (%s)\n'
            '     или в любую другую и укажите её: --mod-dir "<папка>".\n'
            '  3. Повторите установку.\n'
            'Можно и собрать мод самому: установите Build Tools for Visual Studio 2022 (достаточно компонента MSBuild), '
            'тогда установщик соберёт его сам.\n'
            'Файлы игры не тронуты.' % (why, RELEASES_URL, repo_dir))


def build_with_csc(repo_dir, game_dir, csc=None, release=True, log=print):
    """Run `python mod/build.py --game-dir <game_dir> --csc <csc> [--release]` in `repo_dir` and return the ModDlls it
    leaves in mod/build. The installer asks for the release build (no framework-sample); whoever wants the debug build
    runs mod/build.py by hand first, and the installer then finds it in mod/build."""
    build_py = os.path.join(repo_dir, 'mod', 'build.py')
    if not os.path.isfile(build_py):
        raise ModsInstallError(no_dlls_message(repo_dir, 'нет файла mod\\build.py'))
    csc = csc or find_csc()
    if not csc:
        raise ModsInstallError(no_dlls_message(repo_dir, 'не найден компилятор csc.exe'))
    cmd = [sys.executable, build_py, '--game-dir', game_dir, '--csc', csc] + (['--release'] if release else [])
    log('Сборка мода: %s' % subprocess.list2cmdline(cmd))
    try:
        sys.stdout.flush()                                  # our lines must come out before the build's own output
    except (AttributeError, OSError, ValueError):
        pass
    try:
        code = subprocess.run(cmd, cwd=repo_dir, timeout=BUILD_TIMEOUT).returncode
    except subprocess.TimeoutExpired:
        raise ModsInstallError('Сборка мода не завершилась за %d с и остановлена. Файлы игры не тронуты.' % BUILD_TIMEOUT)
    except OSError as e:
        raise ModsInstallError('Не удалось запустить сборку мода (%s): %s. Файлы игры не тронуты.' % (build_py, e.strerror or e))
    if code != 0:
        raise ModsInstallError(
            'Сборка мода не удалась (код %d, подробности выше). Файлы игры не тронуты.\n'
            'Можно не собирать, а скачать готовые PTMod.dll и 0Harmony.dll со страницы GitHub Release (%s) и указать '
            'их папку: --mod-dir "<папка>".' % (code, RELEASES_URL))
    found, missing = _dlls_in(os.path.join(repo_dir, 'mod', 'build'), 'mod\\build')
    if found is None:
        raise ModsInstallError('Сборка завершилась без ошибок, но в mod\\build нет: %s. Файлы игры не тронуты.'
                               % ', '.join(missing))
    return found


def locate_mod_dlls(repo_dir, game_dir, mod_dir=None, log=print):
    """find_mod_dlls, and when there are no DLLs but a csc and mod/build.py, a build. Otherwise the instruction."""
    found = find_mod_dlls(repo_dir, mod_dir)
    if found is not None:
        return found
    if not os.path.isfile(os.path.join(repo_dir, 'mod', 'build.py')):
        raise ModsInstallError(no_dlls_message(repo_dir, 'нет файла mod\\build.py'))
    csc = find_csc()
    if csc is None:
        raise ModsInstallError(no_dlls_message(repo_dir, 'не найден компилятор csc.exe'))
    log('Готовых PTMod.dll и 0Harmony.dll нет (искал в mod\\build и рядом с install.py), но найден csc.exe: собираю мод.')
    return build_with_csc(repo_dir, game_dir, csc=csc, log=log)


# ---------------------------------------------------------------------------------------------------------------
# SHA256SUMS.txt beside the DLLs
# ---------------------------------------------------------------------------------------------------------------

_SUM_LINE = re.compile(r'^([0-9A-Fa-f]{64})[ \t]+\*?(.+?)\s*$')


def parse_checksums(text):
    """{file name: lowercase sha256} from sha256sum-style lines ("<hash>  <name>" or "<hash> *<name>"); other lines
    are ignored, a folder in the name is dropped."""
    sums = {}
    for line in text.lstrip('\ufeff').splitlines():
        m = _SUM_LINE.match(line.strip())
        if m:
            sums[m.group(2).replace('\\', '/').rsplit('/', 1)[-1]] = m.group(1).lower()
    return sums


def verify_checksums(dlls, force=False):
    """Compare both DLLs with the SHA256SUMS.txt that lies beside them, if there is one. Returns (verified names,
    warnings). A DLL that differs from its line is a refusal (ModsInstallError), with `force` a warning."""
    if dlls.checksums is None:
        return [], []
    try:
        with open(dlls.checksums, encoding='utf-8-sig') as f:
            sums = {k.lower(): v for k, v in parse_checksums(f.read()).items()}
    except (OSError, UnicodeDecodeError):
        sums = {}
    if not sums:
        return [], ['Рядом с DLL лежит %s, но прочитать из него контрольные суммы не удалось: целостность файлов не проверена.'
                    % CHECKSUMS_NAME]
    verified, warnings, bad = [], [], []
    for path in (dlls.mod, dlls.harmony):
        name = os.path.basename(path)
        want = sums.get(name.lower())
        if want is None:
            warnings.append('В %s нет строки для %s: его целостность не проверена.' % (CHECKSUMS_NAME, name))
            continue
        got = sha256_of(path)
        if got == want:
            verified.append(name)
        else:
            bad.append('%s: в %s %s..., на диске %s...' % (name, CHECKSUMS_NAME, want[:12], got[:12]))
    if bad:
        text = 'Контрольная сумма не совпала с %s (%s).' % (CHECKSUMS_NAME, dlls.folder)
        if not force:
            raise ModsInstallError(
                text + '\n  ' + '\n  '.join(bad) + '\n'
                'Файл повреждён при скачивании или заменён. Скачайте архив релиза заново. Файлы игры не тронуты.\n'
                'Если вы уверены в этих файлах (например, собрали их сами), повторите команду с --force.')
        warnings.append(text + ' Установка продолжается из-за --force: ' + '; '.join(bad) + '.')
    return verified, warnings


# ---------------------------------------------------------------------------------------------------------------
# Game version gate (FR-20 p. 3 and 9) and the version of the mod DLL
# ---------------------------------------------------------------------------------------------------------------

def original_assembly_path(game_dir):
    """The file that stands for the pristine Assembly-CSharp.dll: the copy in _original_en, and when there is none
    (no translation installed yet, or --mods-only on an untouched game) the one in the game itself."""
    backup = os.path.join(backup_dir(game_dir), ASSEMBLY_CSHARP)
    return backup if os.path.isfile(backup) else os.path.join(managed_dir(game_dir), ASSEMBLY_CSHARP)


def check_game_version(game_dir, meta, force=False, log=print):
    """True if the pristine Assembly-CSharp.dll is the version the mod was made for (translation/meta.json). An
    unknown version is a refusal (ModsInstallError), with `force` a warning and False."""
    path = original_assembly_path(game_dir)
    expected = (meta.get('original_sha256') or {}).get(ASSEMBLY_CSHARP)
    try:
        got = sha256_of(path)
    except OSError as e:
        raise ModsInstallError('Не удалось прочитать "%s": %s. Проверьте, что указана папка игры.' % (path, e.strerror or e))
    if expected and got == expected:
        log('Версия игры: %s (поддерживается).' % meta.get('game_version', '?'))
        return True
    version = meta.get('game_version', '?')
    where = os.path.relpath(path, game_dir)
    if os.path.dirname(path) != backup_dir(game_dir):
        where += ' (копии оригинала в %s нет, файл взят как оригинал)' % BACKUP_DIR
    if not force:
        raise ModsInstallError(
            'Версия игры не поддерживается: хэш Assembly-CSharp.dll (%s...) не совпал с версией %s, для которой сделан мод.\n'
            'Проверенный файл: %s.\n'
            'Моды подменяют методы игры и на другой версии могут не работать или вызывать ошибки. Файлы игры не тронуты.\n'
            '  - Дождитесь версии мода для вашей игры или обновите русификатор;\n'
            '  - если вы понимаете риск, повторите команду с --force.' % (got[:12], version, where))
    log('ВНИМАНИЕ: хэш Assembly-CSharp.dll (%s...) не совпал с версией %s, для которой сделан мод (файл: %s).\n'
        'Моды ставятся из-за --force, на ваш риск: часть фич может оказаться недоступной, а игра - работать нестабильно. '
        'Сохранения мод не меняет.' % (got[:12], version, where))
    return False


def parse_product_version(data):
    """(version, build) from the ProductVersion string of a PE version resource ("0.1.0+release" gives
    ("0.1.0", "release")), (None, None) if there is none. `data` is the file content."""
    key = 'ProductVersion'.encode('utf-16-le') + b'\0\0'
    pos = data.find(key)
    while pos >= 0:
        start = pos - 6                                                      # VS_VERSIONINFO String: wLength, wValueLength, wType, key
        if start >= 0:
            _, value_len, kind = struct.unpack_from('<HHH', data, start)
            first = start + ((pos + len(key) - start + 3) & ~3)              # the value is aligned to 4 bytes from the header
            if kind == 1 and 1 < value_len <= 128:
                raw = data[first:first + value_len * 2]
                try:
                    text = raw.decode('utf-16-le').rstrip('\0')
                except UnicodeDecodeError:
                    text = ''
                version, _, build = text.partition('+')
                if re.match(r'^\d+(\.\d+){1,3}([-.][0-9A-Za-z.]+)?$', version):
                    return version, build or None
        pos = data.find(key, pos + 1)
    return None, None


def read_dll_version(path):
    """(version, build) of a mod DLL built by mod/build.py (from mod/VERSION), (None, None) if it cannot be read."""
    try:
        with open(path, 'rb') as f:
            return parse_product_version(f.read(VERSION_SCAN_LIMIT))
    except OSError:
        return None, None


# ---------------------------------------------------------------------------------------------------------------
# Manifest _original_en/ptmod_install.json
# ---------------------------------------------------------------------------------------------------------------

def read_manifest(game_dir):
    """The install manifest as a dict, None if the mod was not installed by install.py (no file)."""
    path = manifest_path(game_dir)
    data = _read_bytes(path)
    if data is None:
        return None
    try:
        manifest = json.loads(data.decode('utf-8-sig'))
        if not isinstance(manifest, dict) or not isinstance(manifest.get('files'), list):
            raise ValueError('shape')
        return manifest
    except (ValueError, UnicodeDecodeError):
        raise ModsInstallError('Манифест установки модов "%s" повреждён. Снимите моды вручную '
                               '(python install.py "<папка игры>" --remove-mods --force) и установите заново.' % path)


def write_manifest(game_dir, manifest):
    write_atomic(manifest_path(game_dir), (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))


# ---------------------------------------------------------------------------------------------------------------
# Preparation (read-only: every refusal happens here) and installation (the writes)
# ---------------------------------------------------------------------------------------------------------------

def _manual_install_message(game_dir, in_assemblies, in_runtime):
    spike = os.path.isfile(os.path.join(backup_dir(game_dir), SPIKE_MANIFEST_NAME))
    where = ' и '.join(n for n, f in (('ScriptingAssemblies.json', in_assemblies), ('RuntimeInitializeOnLoads.json', in_runtime)) if f)
    head = 'В игре уже есть запись мода PTMod (%s), а манифеста установки (%s\\%s) нет. ' % (where, BACKUP_DIR, MANIFEST_NAME)
    if spike:
        return (head + 'Это спайк загрузки, поставленный tools\\spike_install.py.\n'
                'Снимите его: python tools\\spike_install.py --game-dir "%s" --remove\n'
                'и повторите установку. Файлы игры не тронуты.' % game_dir)
    return (head + 'Значит, мод ставили вручную или другим способом.\n'
            'Поверх такой установки он не ставится: резервные копии JSON уже содержали бы записи мода.\n'
            'Снимите ручную установку командой\n'
            '    python install.py "%s" --remove-mods --force\n'
            'и повторите установку. Файлы игры не тронуты.' % game_dir)


def _check_foreign_files(game_dir, dlls, manifest, force, log):
    """A different PTMod.dll / 0Harmony.dll that this installer did not put into Managed is not ours to replace."""
    owned = {f.get('path') for f in (manifest or {}).get('files', []) if isinstance(f, dict)}
    for src in (dlls.mod, dlls.harmony):
        dst = os.path.join(managed_dir(game_dir), os.path.basename(src))
        if not os.path.isfile(dst) or rel_path(game_dir, dst) in owned or sha256_of(dst) == sha256_of(src):
            continue
        text = ('В %s\\%s уже лежит файл %s, который поставил не этот установщик, и он отличается от нового.'
                % (DATA_DIR, MANAGED_DIR, os.path.basename(src)))
        if not force:
            raise ModsInstallError(
                text + '\nУстановщик его не заменяет: он мог понадобиться другому моду. Если это не так, удалите его '
                'или повторите команду с --force (файл будет заменён, а при снятии модов удалён). Файлы игры не тронуты.')
        log('ВНИМАНИЕ: ' + text + ' Заменяю из-за --force.')


def prepare_install(game_dir, repo_dir, meta, force=False, mods_only=False, mod_dir=None, log=print):
    """Everything that can stop the installation, without writing anything to the game: the JSON files parse, no
    manual installation in them, the game version is known (or `force`), the DLLs are found or built (csc), they match
    SHA256SUMS.txt, they do not overwrite foreign files. Returns the InstallPlan for install_mods. A build, if one is
    needed, touches only mod/build in the repository."""
    if not os.path.isdir(managed_dir(game_dir)):
        raise ModsInstallError('В "%s" нет папки %s\\%s: это не папка игры.' % (game_dir, DATA_DIR, MANAGED_DIR))
    in_assemblies = is_assembly_registered(scripting_assemblies_path(game_dir))
    in_runtime = is_runtime_init_registered(runtime_init_path(game_dir))
    manifest = read_manifest(game_dir)
    if manifest is None and (in_assemblies or in_runtime):
        raise ModsInstallError(_manual_install_message(game_dir, in_assemblies, in_runtime))

    known = check_game_version(game_dir, meta, force, log)
    dlls = locate_mod_dlls(repo_dir, game_dir, mod_dir, log)
    verified, warnings = verify_checksums(dlls, force)
    if verified:
        log('Контрольные суммы %s совпали (%s).' % (CHECKSUMS_NAME, ', '.join(verified)))
    for text in warnings:
        log('ВНИМАНИЕ: ' + text)
    _check_foreign_files(game_dir, dlls, manifest, force, log)

    version, build = read_dll_version(dlls.mod)
    return InstallPlan(dlls, version, build, bool(mods_only), meta.get('game_version'), known, bool(force))


class _Undo:
    """Steps that take back what install_mods has already done, run newest first."""

    def __init__(self):
        self._steps = []

    def add(self, func, *args):
        self._steps.append((func, args))

    def run(self):
        for func, args in reversed(self._steps):
            try:
                func(*args)
            except Exception:
                pass
        self._steps = []


def _restore_bytes(path, data):
    if data is None:
        if os.path.exists(path):
            os.remove(path)
    elif _read_bytes(path) != data:
        write_atomic(path, data)


def _install_file(src, dst, undo):
    """Copy `src` over `dst` atomically unless `dst` is already identical. True if `dst` changed."""
    with open(src, 'rb') as f:
        data = f.read()
    before = _read_bytes(dst)
    if before == data:
        return False
    write_atomic(dst, data)
    undo.add(_restore_bytes, dst, before)
    return True


def _make_dirs(folder, undo):
    missing = []
    while not os.path.isdir(folder):
        missing.append(folder)
        folder = os.path.dirname(folder)
    for path in reversed(missing):
        os.mkdir(path)
        undo.add(os.rmdir, path)
    return bool(missing)


def _crlf(text):
    return '\ufeff' + text.replace('\r\n', '\n').replace('\n', '\r\n')


def install_mods(game_dir, plan, register_harmony=None, log=print):
    """Install the mod from `plan` (prepare_install) into `game_dir`: originals of the two JSON files into
    _original_en (once), PTMod.dll and 0Harmony.dll into Managed, the entries in both JSON files, <game>/PTMod/
    {content,l10n}/README.txt, the manifest. Idempotent: a repeat changes nothing. If a step fails, everything done is
    taken back and the error is raised. data.unity3d and Assembly-CSharp.dll are never touched. Returns the manifest."""
    if register_harmony is None:
        register_harmony = REGISTER_HARMONY
    sa_path, ri_path = scripting_assemblies_path(game_dir), runtime_init_path(game_dir)
    bdir, managed = backup_dir(game_dir), managed_dir(game_dir)
    old = read_manifest(game_dir)
    undo = _Undo()
    changed = False
    log('Установка модов%s.' % (' (режим --mods-only: перевод и файлы игры data.unity3d, Assembly-CSharp.dll не трогаются)'
                                if plan.mods_only else ''))
    try:
        changed = _make_dirs(bdir, undo)
        for path in (sa_path, ri_path):                       # the originals first: they must be taken before the first edit
            if backup_json(path, bdir):
                undo.add(_restore_bytes, os.path.join(bdir, os.path.basename(path)), None)
                changed = True
                log('  Оригинал %s сохранён в %s.' % (os.path.basename(path), BACKUP_DIR))

        files = []
        for src in (plan.dlls.mod, plan.dlls.harmony):
            dst = os.path.join(managed, os.path.basename(src))
            if _install_file(src, dst, undo):
                changed = True
                log('  %s скопирован в %s\\%s.' % (os.path.basename(src), DATA_DIR, MANAGED_DIR))
            files.append({'path': rel_path(game_dir, dst), 'sha256': sha256_of(dst)})

        wanted = [MOD_ASSEMBLY] + ([HARMONY_ASSEMBLY] if register_harmony else [])
        stale = [n for n in (old or {}).get('registeredAssemblies', []) if n not in wanted]
        undo.add(_restore_bytes, sa_path, _read_bytes(sa_path))
        undo.add(_restore_bytes, ri_path, _read_bytes(ri_path))
        for name in wanted:
            if register_assembly(sa_path, name):
                changed = True
                log('  В ScriptingAssemblies.json добавлено %s.' % name)
        for name in stale:                                     # an earlier install registered more than this one wants
            changed = unregister_assembly(sa_path, name) or changed
        if register_runtime_init(ri_path):
            changed = True
            log('  В RuntimeInitializeOnLoads.json добавлено PTMod.Core.ModBootstrap.Init.')

        readmes = []
        for sub, text in (('content', README_CONTENT), ('l10n', README_L10N)):
            folder = os.path.join(user_dir(game_dir), sub)
            changed = _make_dirs(folder, undo) or changed
            readme = os.path.join(folder, 'README.txt')
            if not os.path.exists(readme):                     # a README the player has edited stays as it is
                write_atomic(readme, _crlf(text).encode('utf-8'))
                undo.add(_restore_bytes, readme, None)
                changed = True
            readmes.append(rel_path(game_dir, readme))

        manifest = {
            'schema': MANIFEST_SCHEMA,
            'modVersion': plan.version,
            'build': plan.build,
            'date': time.strftime('%Y-%m-%dT%H:%M:%S'),
            'modsOnly': plan.mods_only,
            'gameVersion': plan.game_version,
            'gameVersionKnown': plan.known,
            'force': plan.force,
            'files': files,
            'json': [{'path': rel_path(game_dir, p), 'backup': rel_path(game_dir, os.path.join(bdir, os.path.basename(p)))}
                     for p in (sa_path, ri_path)],
            'registeredAssemblies': wanted,
            'runtimeInit': True,
            'readmes': readmes,
        }
        if old is not None and all(old.get(k) == v for k, v in manifest.items() if k != 'date'):
            manifest['date'] = old.get('date', manifest['date'])       # the same install again: the manifest stays byte-identical
        text = (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
        before = _read_bytes(manifest_path(game_dir))
        if before != text:
            write_atomic(manifest_path(game_dir), text)
            undo.add(_restore_bytes, manifest_path(game_dir), before)
            changed = True
    except BaseException:
        undo.run()
        log('Установка модов не удалась, сделанные изменения отменены.')
        raise

    version = plan.version or 'версия не определена'
    if changed:
        log('Моды установлены (%s).' % version)
    else:
        log('Моды уже установлены (%s), изменений нет.' % version)
    log('  Ваши правки контента и текстов кладите в %s\\content и %s\\l10n.' % (USER_DIR, USER_DIR))
    return manifest


# ---------------------------------------------------------------------------------------------------------------
# Removing the mods: --remove-mods and the mod half of --restore (FR-20 items 4, 5 and 6, TSK-045)
# ---------------------------------------------------------------------------------------------------------------

class ModsRemoveRefused(ModsInstallError):
    """Removal refused before anything was changed, for a reason that is not about the machine: the state needs the
    player's decision (--force), belongs to another tool (the load spike), or cannot be analysed (broken manifest or
    JSON). `--remove-mods` stops with it; `--restore` shows it as a warning and carries on with the game files.
    Trouble with the machine itself (a DLL locked by a running game) stays a plain ModsInstallError."""


RemoveResult = namedtuple('RemoveResult', 'changed version removed restored manual')
# changed: anything on disk was touched; version: modVersion of the manifest; removed / restored: game-relative paths of the
# deleted files and of the JSON files that were edited; manual: no usable manifest, the removal was forced by hand.

_JsonEdit = namedtuple('_JsonEdit', 'path old new backup backup_old via_backup')
_Removal = namedtuple('_Removal', 'files json manifest_file version manual')

_BAD_PATH_CHARS = '<>:"|?*\\'
NOTHING_TO_REMOVE = 'Модов в игре нет, снимать нечего.'     # also how --restore knows to stay quiet for a translation-only game


def _shown(game_dir, path):
    """A path for messages: relative to the game folder, with the backslashes of Windows."""
    return rel_path(game_dir, path).replace('/', '\\')


def _safe_manifest_path(game_dir, rel):
    """Absolute path for a path from the manifest, None if it is not a plain file path below the game folder: absolute,
    '..', a drive or stream marker, or inside PTMod/ (the player's) or _original_en/ (the backups). The manifest is a
    file on the player's disk: what it says is checked before anything is deleted because of it."""
    if not isinstance(rel, str) or not rel or rel.startswith('/'):
        return None
    parts = rel.split('/')
    if any(p in ('', '.', '..') or any(c in p for c in _BAD_PATH_CHARS) or p != p.strip() or p.endswith('.')
           for p in parts):
        return None
    if parts[0].lower() in (USER_DIR.lower(), BACKUP_DIR.lower()):
        return None
    full = os.path.join(game_dir, *parts)
    root = os.path.normcase(os.path.realpath(game_dir))
    real = os.path.normcase(os.path.realpath(full))
    return full if real.startswith(root + os.sep) else None


class _BadManifest(Exception):
    """The manifest lists something that is not a plain file of the game; str(e) says what."""


def _manifest_files(game_dir, manifest):
    """[(absolute path, recorded sha256 or None)] of the files the manifest lists that still exist. _BadManifest if an
    entry is not an object, is not a plain file path of the game, or is not a file."""
    found = []
    for entry in manifest['files']:
        if not isinstance(entry, dict):
            raise _BadManifest('запись в "files" не объект')
        full = _safe_manifest_path(game_dir, entry.get('path'))
        if full is None:
            raise _BadManifest('путь %r выходит за допустимое (только файлы игры вне %s и %s)'
                               % (entry.get('path'), USER_DIR, BACKUP_DIR))
        if not os.path.lexists(full):
            continue                                           # already gone: nothing to delete
        if not os.path.isfile(full):
            raise _BadManifest('"%s" не файл' % entry.get('path'))
        found.append((full, entry.get('sha256')))
    return found


def _unusable_manifest(game_dir, what):
    return ModsRemoveRefused('Манифест установки модов "%s" не годится для снятия: %s. Файлы игры не тронуты.\n'
                             'Если вы уверены, что в игре только то, что поставил установщик, снимите моды без манифеста '
                             '(будет удалён PTMod.dll и его записи): python install.py "%s" --remove-mods --force'
                             % (manifest_path(game_dir), what, game_dir))


def _remove_assemblies(names):
    def run(text, label):
        for name in names:
            text = remove_assembly_text(text, name, label)
        return text
    return run


def _remove_runtime_entry(text, label):
    return remove_runtime_init_text(text, None, label)


def _plan_json_edit(path, backup, remove):
    """Compute the text the file gets without the mod's entries. A file that cannot be edited (broken, not UTF-8,
    missing) is replaced by the saved original when there is a usable one - the point of the backup - else it is an error."""
    label = os.path.basename(path)
    old = _read_bytes(path)
    try:
        text, bom = _read_json_text(path)
        new_text, via_backup = remove(text, label), False
    except ModsJsonError as problem:
        if not os.path.isfile(backup):
            raise
        try:
            text, bom = _read_json_text(backup)
            new_text, via_backup = remove(text, label), True
        except ModsJsonError:
            raise problem
    new = bom + new_text.encode('utf-8')
    return _JsonEdit(path, old, new, backup, _read_bytes(backup), via_backup)


def _plan_removal(game_dir, force, log):
    """Everything that can stop the removal, read-only. None when there is nothing to remove. Raises ModsRemoveRefused
    for a refusal and ModsJsonError (turned into a refusal by remove_mods) for JSON that cannot be read."""
    sa_path, ri_path = scripting_assemblies_path(game_dir), runtime_init_path(game_dir)
    manifest_file = manifest_path(game_dir)
    manifest = None
    try:
        manifest = read_manifest(game_dir)
    except ModsInstallError as e:
        if not force:
            raise ModsRemoveRefused(str(e))
        log('ВНИМАНИЕ: манифест установки повреждён, из-за --force он не используется (будет удалён).')
    listed = []
    if manifest is not None:
        try:
            listed = _manifest_files(game_dir, manifest)
        except _BadManifest as e:
            if not force:
                raise _unusable_manifest(game_dir, str(e))
            log('ВНИМАНИЕ: манифест установки не годится (%s), из-за --force он не используется (будет удалён).' % e)
            manifest = None

    files, names, version, manual = [], [MOD_ASSEMBLY], None, manifest is None
    if manifest is None:
        in_assemblies = is_assembly_registered(sa_path)
        in_runtime = is_runtime_init_registered(ri_path)
        mod_dll = os.path.join(managed_dir(game_dir), MOD_ASSEMBLY)
        has_dll = os.path.isfile(mod_dll)
        if not (in_assemblies or in_runtime or has_dll):
            if not os.path.exists(manifest_file):
                return None                                    # nothing of the mod anywhere
        else:
            if os.path.isfile(os.path.join(backup_dir(game_dir), SPIKE_MANIFEST_NAME)):
                raise ModsRemoveRefused(
                    'В игре стоит спайк загрузки, поставленный tools\\spike_install.py, а не мод из install.py: '
                    'манифеста установки (%s\\%s) нет.\nСнимите спайк его скриптом: '
                    'python tools\\spike_install.py --game-dir "%s" --remove\nФайлы игры не тронуты.'
                    % (BACKUP_DIR, MANIFEST_NAME, game_dir))
            if not force:
                what = ' и '.join(n for n, f in (('запись в ScriptingAssemblies.json', in_assemblies),
                                                  ('запись в RuntimeInitializeOnLoads.json', in_runtime),
                                                  ('файл %s\\%s\\%s' % (DATA_DIR, MANAGED_DIR, MOD_ASSEMBLY), has_dll)) if f)
                raise ModsRemoveRefused(
                    'В игре есть следы мода PTMod (%s), а манифеста установки (%s\\%s) нет. Значит, мод ставили вручную '
                    'или другим способом, и установщик не знает, какие файлы его.\n'
                    'Чтобы снять записи мода и %s, повторите команду с --force:\n'
                    '    python install.py "%s" --remove-mods --force\n'
                    '%s и его записи при этом не трогаются: им могут пользоваться другие моды. Файлы игры не тронуты.'
                    % (what, BACKUP_DIR, MANIFEST_NAME, MOD_ASSEMBLY, game_dir, HARMONY_ASSEMBLY))
            if has_dll:
                files.append(mod_dll)
    else:
        version = manifest.get('modVersion')
        stale = []
        for full, want in listed:
            if isinstance(want, str) and not force:
                try:
                    got = sha256_of(full)
                except OSError as e:
                    raise ModsInstallError('Не удалось прочитать "%s": %s. Закройте игру и повторите. Файлы игры не тронуты.'
                                           % (full, e.strerror or e))
                if got != want.lower():
                    stale.append(_shown(game_dir, full))
            files.append(full)
        if stale:
            raise ModsRemoveRefused(
                'Файлы изменены после установки модов (контрольная сумма не совпала с манифестом):\n  %s\n'
                'Возможно, вы заменили их вручную или ими пользуется другой мод. Файлы игры не тронуты.\n'
                'Чтобы снять моды и удалить и эти файлы, повторите команду с --force.' % '\n  '.join(stale))
        registered = manifest.get('registeredAssemblies')
        names += [n for n in (registered if isinstance(registered, list) else []) if isinstance(n, str) and n not in names]

    bdir = backup_dir(game_dir)
    edits = [_plan_json_edit(sa_path, os.path.join(bdir, SCRIPTING_ASSEMBLIES_JSON), _remove_assemblies(names)),
             _plan_json_edit(ri_path, os.path.join(bdir, RUNTIME_INIT_JSON), _remove_runtime_entry)]
    return _Removal(files, edits, manifest_file if os.path.exists(manifest_file) else None, version, manual)


def _delete_file(path, undo):
    data = _read_bytes(path)
    try:
        os.remove(path)
    except FileNotFoundError:
        return
    except OSError as e:
        raise ModsInstallError('Не удалось удалить файл "%s": %s. Закройте игру и повторите: пока игра запущена, '
                               'Unity держит файлы Managed открытыми. Проверьте и то, что файл не помечен как «только '
                               'чтение». Сделанные изменения отменены.' % (path, e.strerror or e))
    undo.add(_restore_bytes, path, data)


def remove_mods(game_dir, force=False, log=print):
    """Take the mods out of `game_dir` - the inverse of install_mods (FR-20 items 4 and 6). The files listed in the
    manifest are deleted, the entries of the mod leave ScriptingAssemblies.json and RuntimeInitializeOnLoads.json (both
    get back the exact text they had before, which equals the copy in _original_en unless the game was updated since),
    the manifest is deleted. Left alone: the translation (data.unity3d, Assembly-CSharp.dll), the saves and the player's
    <game>/PTMod/ folder. The copies of the two JSON files in _original_en stay as backups (refreshed if the original
    changed under them).

    Without a manifest (the mod was put in by hand) it refuses; with `force` it removes only PTMod.dll and the entries
    of PTMod, never 0Harmony.dll, which other mods may use. `force` also deletes files whose hash differs from the
    manifest and ignores a broken manifest. Idempotent: with nothing to remove it says so and changes nothing. If a step
    fails everything done is taken back. Raises ModsRemoveRefused before any change, ModsInstallError / ModsJsonError
    for a failed step. Returns a RemoveResult."""
    try:
        plan = _plan_removal(game_dir, force, log)
    except ModsJsonError as e:
        raise ModsRemoveRefused(str(e))
    if plan is None:
        log(NOTHING_TO_REMOVE)
        return RemoveResult(False, None, [], [], False)

    log('Снятие модов%s.' % (' (без манифеста, из-за --force)' if plan.manual else ''))
    undo = _Undo()
    removed, restored = [], []
    try:
        for path in plan.files:
            _delete_file(path, undo)
            removed.append(rel_path(game_dir, path))
            log('  Удалён файл %s.' % _shown(game_dir, path))
        for edit in plan.json:
            name = os.path.basename(edit.path)
            if edit.new != edit.old:
                write_atomic(edit.path, edit.new)
                undo.add(_restore_bytes, edit.path, edit.old)
                restored.append(rel_path(game_dir, edit.path))
                if edit.via_backup:
                    log('  %s был повреждён: восстановлен из резервной копии в %s.' % (name, BACKUP_DIR))
                else:
                    log('  %s: записи мода убраны.' % name)
            else:
                log('  %s: записей мода уже нет.' % name)
            if edit.backup_old is not None and edit.backup_old != edit.new:
                write_atomic(edit.backup, edit.new)             # the original moved on (a game update): keep the backup current
                undo.add(_restore_bytes, edit.backup, edit.backup_old)
                log('  Копия %s в %s обновлена: оригинал изменился с момента установки.' % (name, BACKUP_DIR))
        if plan.manifest_file is not None:
            _delete_file(plan.manifest_file, undo)
            log('  Удалён манифест установки.')
    except BaseException:
        undo.run()
        log('Снятие модов не удалось, сделанные изменения отменены.')
        raise

    changed = bool(removed or restored or plan.manifest_file is not None)
    log('Моды сняты%s. Перевод, сейвы и ваши файлы в %s\\content и %s\\l10n не тронуты.'
        % (' (%s)' % plan.version if plan.version else '', USER_DIR, USER_DIR))
    if plan.manual and os.path.isfile(os.path.join(managed_dir(game_dir), HARMONY_ASSEMBLY)):
        log('  %s оставлен: манифеста нет, и неизвестно, нужен ли он другим модам. Если нет, удалите его вручную.'
            % HARMONY_ASSEMBLY)
    return RemoveResult(changed, plan.version, removed, restored, plan.manual)


# ---------------------------------------------------------------------------------------------------------------
# --check: the mod's content and string tables, without the game (FR-20 item 7, FR-25, TSK-045)
# ---------------------------------------------------------------------------------------------------------------
#
# The rules are those of the C# side, restated in Python because the installer must not need the game, a compiler or
# the test runner: the strict JSON of ModJson (no comments, no trailing commas, no NaN, no duplicate keys, 8 MiB, depth 64),
# ContentMerger (record files, unique ids, "schema"), L10nTables (flat tables, plural groups) and the prefix table of
# core.json (ModIds). The unit tests of the mod (ArchitectureTests) check the same on the build machine; this is the
# version that runs from install.py --check, so that a broken file is found before it reaches a release.

MOD_SRC = ('mod', 'src', 'PTMod')               # the sources of the mod, relative to the repository
CONTENT_SCHEMAS = (1,)                          # "schema" values a content file may carry (core.json and every feature's files today)
CORE_CONTENT = 'Content/mod-framework/core.json'
EVENT_FILES = ('events.json',)                  # the event pack of a feature (PackOptions.EventsFile default) ...
EVENT_FILES_OF = {'market-shocks': ('shocks.json',)}    # ... and the packs that name their file differently
JSON_MAX_BYTES = 8 << 20                        # ModJson.MaxBytes
JSON_MAX_DEPTH = 64                             # ModJson.MaxDepth
_PLURAL_SUFFIXES = ('one', 'few', 'many', 'other')
_SLAVIC = ('ru', 'uk', 'be')                    # languages with one/few/many (PluralRules.IsSlavic)
_PREFIX_RE = re.compile(r'^[a-z]+$')


def mod_sources_dir(repo_dir):
    """mod/src/PTMod in the repository (it may be missing: a GitHub Release has no sources)."""
    return os.path.join(repo_dir, *MOD_SRC)


class CheckReport:
    """What check_mod_sources found. Errors fail `install.py --check`, warnings do not. Messages are Russian; `label` is
    the path of the file relative to mod/src/PTMod (with '/')."""

    def __init__(self, src_dir):
        self.src_dir = src_dir
        self.errors, self.warnings = [], []
        self.content_files = 0
        self.l10n_files = 0
        self.l10n_keys = {}                     # language -> number of keys
        self.found = os.path.isdir(src_dir)

    def error(self, label, text):
        self.errors.append('%s: %s' % (label, text))

    def warn(self, label, text):
        self.warnings.append('%s: %s' % (label, text))

    @property
    def ok(self):
        return not self.errors

    def lines(self):
        """The text for the console: every error and warning, then one summary line."""
        shown = '\\'.join(MOD_SRC)
        if not self.found:
            return ['Исходников мода (%s) нет — проверка контента и строк мода пропущена.' % shown]
        out = ['ОШИБКА %s' % e for e in self.errors] + ['ВНИМАНИЕ %s' % w for w in self.warnings]
        tables = ', '.join('%s: %d' % (lang, n) for lang, n in sorted(self.l10n_keys.items()))
        out.append('Мод (%s): файлов контента — %d, таблиц строк — %d%s, ошибок: %d, предупреждений: %d'
                   % (shown, self.content_files, self.l10n_files, ' (ключей %s)' % tables if tables else '',
                      len(self.errors), len(self.warnings)))
        return out


class _BadJson(Exception):
    """str(e) is the Russian reason a file is not the strict JSON the mod reads."""


def _depth(value):
    deepest, stack = 0, [(value, 1)]
    while stack:
        node, level = stack.pop()
        if isinstance(node, dict):
            children = node.values()
        elif isinstance(node, list):
            children = node
        else:
            continue
        deepest = max(deepest, level)
        stack.extend((child, level + 1) for child in children)
    return deepest


def _strict_json(text):
    """Parse like ModJson does: RFC 8259, finite numbers, no duplicate keys, nesting up to JSON_MAX_DEPTH."""
    def constant(name):
        raise ValueError('значение %s недопустимо в JSON' % name)

    def number(literal):
        value = float(literal)
        if math.isinf(value) or math.isnan(value):
            raise ValueError('число %s вне допустимого диапазона' % literal[:24])
        return value

    def members(pairs):
        out = {}
        for key, value in pairs:
            if key in out:
                raise ValueError('ключ «%s» повторяется в одном объекте' % key)
            out[key] = value
        return out

    try:
        value = json.loads(text, parse_constant=constant, parse_float=number, object_pairs_hook=members)
    except json.JSONDecodeError as e:
        why = next((ru for en, ru in _JSON_ERRORS_RU if e.msg.startswith(en)), e.msg)
        raise _BadJson('некорректный JSON — %s (строка %d, столбец %d)' % (why, e.lineno, e.colno))
    except RecursionError:
        raise _BadJson('некорректный JSON — слишком глубокая вложенность')
    except ValueError as e:
        raise _BadJson('некорректный JSON — %s' % e)
    if _depth(value) > JSON_MAX_DEPTH:
        raise _BadJson('вложенность глубже %d уровней: мод такой файл не прочитает' % JSON_MAX_DEPTH)
    return value


def _read_source_json(path, label, report):
    """The parsed JSON of a source file, None (after reporting why) when it cannot be used."""
    try:
        with open(path, 'rb') as f:
            data = f.read(JSON_MAX_BYTES + 1)
    except OSError as e:
        report.error(label, 'не удалось прочитать файл: %s.' % (e.strerror or e))
        return None
    if len(data) > JSON_MAX_BYTES:
        report.error(label, 'файл больше %d МиБ: мод такой файл не прочитает.' % (JSON_MAX_BYTES >> 20))
        return None
    try:
        text = data.decode('utf-8-sig')                        # a BOM is allowed and ignored, as in ModJson
    except UnicodeDecodeError as e:
        report.error(label, 'файл не в кодировке UTF-8 (байт %d).' % e.start)
        return None
    try:
        return _strict_json(text)
    except _BadJson as e:
        report.error(label, '%s.' % e)
        return None


def _json_files(folder):
    """[(path relative to `folder` with '/', full path)] of every .json file below it, sorted."""
    found = []
    for base, dirs, files in os.walk(folder):
        dirs.sort()
        for name in files:
            if name.lower().endswith('.json'):
                full = os.path.join(base, name)
                found.append((os.path.relpath(full, folder).replace(os.sep, '/'), full))
    return sorted(found)


def _is_safe_name(name):
    """ContentMerger.IsSafeName: letters, digits, '-', '_', '.', at most 100 characters, no leading dot, no '..'."""
    return (0 < len(name) <= 100 and name[0] != '.' and '..' not in name
            and all(c.isascii() and (c.isalnum() or c in '-_.') for c in name))


def _prefix_table(core, report):
    """{feature: [prefixes]} from the `prefixes` section of core.json (FR-25 item 1); rows that are wrong are reported and
    left out. None when there is no usable table."""
    table = core.get('prefixes') if isinstance(core, dict) else None
    if not isinstance(table, dict) or not table:
        report.error(CORE_CONTENT, 'нет раздела «prefixes» (таблица «фича → префиксы»): префиксы ключей и ID проверить не по чему.')
        return None
    result, owner = {}, {}
    for feature, row in table.items():
        if not isinstance(row, list) or not row or not all(isinstance(p, str) and _PREFIX_RE.match(p) for p in row):
            report.error(CORE_CONTENT, 'prefixes["%s"] должно быть непустым списком префиксов из строчных латинских букв.' % feature)
            continue
        for prefix in row:
            if prefix in owner:
                report.error(CORE_CONTENT, 'префикс «%s» записан и у «%s», и у «%s»: префикс принадлежит одной фиче.'
                             % (prefix, owner[prefix], feature))
        owner.update((p, feature) for p in row)
        result[feature] = list(row)
    return result


def _owner_of(prefixes, name):
    """The feature whose prefix starts `name` ("hist.x" -> historical-events), None if no prefix of the table does."""
    if '.' not in name:
        return None
    head = name.split('.', 1)[0]
    for feature, row in prefixes.items():
        if head in row:
            return feature
    return None


def _has_prefix(name, row):
    return any(name.startswith(p + '.') and len(name) > len(p) + 1 for p in row)


def _foreign_prefix_text(what, name, feature, prefixes, where):
    """The sentence for an id or key that does not carry a prefix of its feature."""
    expected = ' / '.join('«%s.»' % p for p in prefixes[feature])
    owner = _owner_of(prefixes, name)
    if owner is not None and owner != feature:
        return ('%s «%s» с чужим префиксом: «%s.» принадлежит фиче «%s», а %s относится к «%s» (префикс %s).'
                % (what, name, name.split('.', 1)[0], owner, where, feature, expected))
    return '%s «%s» без префикса своей фичи: %s относится к «%s», ожидался префикс %s.' % (what, name, where, feature, expected)


def _check_sibling_ids(node, path, label, report, skip):
    """Elements of one array that carry the same "id" are one record twice: report it (the top-level records array
    `skip` is checked with better messages by _check_records)."""
    if isinstance(node, dict):
        for key, child in node.items():
            _check_sibling_ids(child, '%s.%s' % (path, key) if path else key, label, report, skip)
    elif isinstance(node, list):
        if node is not skip:
            seen = {}
            for i, element in enumerate(node):
                if isinstance(element, dict) and isinstance(element.get('id'), str):
                    first = seen.setdefault(element['id'], i)
                    if first != i:
                        report.error(label, '%s[%d]: «id» «%s» уже есть в %s[%d] — в одном массиве id не повторяются.'
                                     % (path, i, element['id'], path, first))
        for i, element in enumerate(node):
            _check_sibling_ids(element, '%s[%d]' % (path, i), label, report, skip)


def _check_records(records, label, feature, event_file, prefixes, report):
    """`records` of a record file (ContentMerger.MergeRecords): objects with a unique non-blank string "id", a boolean
    "disabled" if any; in an event pack the id also carries a prefix of the feature."""
    seen = {}
    event_known = True
    for i, record in enumerate(records):
        where = 'records[%d]' % i
        if not isinstance(record, dict):
            report.error(label, '%s: запись должна быть объектом JSON.' % where)
            continue
        rid = record.get('id')
        if not isinstance(rid, str) or not rid.strip():
            report.error(label, '%s: поле «id» отсутствует или не является непустой строкой.' % where)
            continue
        if rid in seen:
            report.error(label, '%s: «id» «%s» уже есть в records[%d] — id записей в файле уникальны.' % (where, rid, seen[rid]))
            continue
        seen[rid] = i
        disabled = record.get('disabled')
        if disabled is not None and not isinstance(disabled, bool):
            report.error(label, '%s («%s»): поле «disabled» должно быть true или false.' % (where, rid))
        if event_file and prefixes is not None:
            if feature not in prefixes:
                if event_known:
                    report.error(label, 'фичи «%s» нет в таблице prefixes файла core.json: префикс ID событий проверить не по чему.' % feature)
                    event_known = False
            elif not _has_prefix(rid, prefixes[feature]):
                report.error(label, '%s: %s' % (where, _foreign_prefix_text('ID события', rid, feature, prefixes, 'файл событий')))


def _check_content_doc(doc, label, feature, file, prefixes, report):
    if not isinstance(doc, dict):
        report.error(label, 'корень файла должен быть объектом JSON { "schema": %d, ... }.' % CONTENT_SCHEMAS[-1])
        return
    schema = doc.get('schema')
    if isinstance(schema, float) and schema.is_integer():
        schema = int(schema)
    if isinstance(schema, bool) or not isinstance(schema, int):
        report.error(label, 'поле «schema» отсутствует или не целое число (мод читает версии: %s).'
                     % ', '.join(map(str, CONTENT_SCHEMAS)))
    elif schema not in CONTENT_SCHEMAS:
        report.error(label, 'неизвестная версия схемы %d (мод читает версии: %s): такой файл будет отвергнут целиком.'
                     % (schema, ', '.join(map(str, CONTENT_SCHEMAS))))

    records = doc.get('records')
    if 'records' in doc:
        if isinstance(records, list):
            event_file = file in EVENT_FILES or file in EVENT_FILES_OF.get(feature, ())
            _check_records(records, label, feature, event_file, prefixes, report)
        else:
            report.error(label, 'поле «records» должно быть массивом.')
            records = None
    _check_sibling_ids(doc, '', label, report, records if isinstance(records, list) else None)

    if label == CORE_CONTENT and isinstance(doc.get('channels'), list):
        names = {}
        for i, channel in enumerate(doc['channels']):
            name = channel.get('name') if isinstance(channel, dict) else None
            if not isinstance(name, str) or not name:
                report.error(label, 'channels[%d]: поле «name» отсутствует или не строка.' % i)
            elif names.setdefault(name, i) != i:
                report.error(label, 'channels[%d]: канал «%s» уже объявлен в channels[%d].' % (i, name, names[name]))


def _check_content(src_dir, prefixes, report, preloaded):
    root = os.path.join(src_dir, 'Content')
    if not os.path.isdir(root):
        report.error('Content', 'папки нет: без неё в PTMod.dll не будет контента.')
        return
    unknown = set()
    for rel, path in _json_files(root):
        label = 'Content/' + rel
        parts = rel.split('/')
        if len(parts) != 2:
            report.warn(label, 'файл лежит не в Content/<фича>/<файл>.json — ModContent его не прочитает.')
            continue
        feature, file = parts
        report.content_files += 1
        if not (_is_safe_name(feature) and _is_safe_name(file)):
            report.error(label, 'имя папки или файла недопустимо (только латинские буквы, цифры, «-», «_», «.»): ModContent его не прочитает.')
            continue
        if prefixes is not None and feature not in prefixes and feature not in unknown:
            unknown.add(feature)
            report.warn(label, 'папки контента «%s» нет в таблице prefixes файла core.json (новая фича добавляет строку в таблицу).' % feature)
        doc = preloaded[label] if label in preloaded else _read_source_json(path, label, report)
        if doc is not None:
            _check_content_doc(doc, label, feature, file, prefixes, report)


def _parse_table_path(rel):
    """(language, feature or None) of a table file path relative to L10n - L10nTables.TryParseTablePath: ru.json or
    ru/<feature>.json. None for anything else."""
    text = rel.strip().replace('\\', '/')
    if not text.lower().endswith('.json'):
        return None
    parts = text[:-5].split('/')
    if len(parts) > 2:
        return None
    lang = parts[0].strip().lower()
    if not (2 <= len(lang) <= 12 and all(c.isascii() and (c.isalnum() or c == '-') for c in lang)
            and lang[0] != '-' and lang[-1] != '-'):
        return None
    if len(parts) == 1:
        return lang, None
    feature = parts[1]
    ok = 0 < len(feature) <= 64 and feature[0] != '.' and '..' not in feature \
        and all(c.isascii() and (c.isalnum() or c in '-_.') for c in feature)
    return (lang, feature) if ok else None


def _split_plural(key):
    """("core.mail.daysLeft", "few") for "core.mail.daysLeft.few", None for a key without a plural suffix."""
    dot = key.rfind('.')
    if dot <= 0 or dot == len(key) - 1 or key[dot + 1:] not in _PLURAL_SUFFIXES:
        return None
    return key[:dot], key[dot + 1:]


def _forms_of(language):
    return ('one', 'few', 'many') if re.split('[-_]', language)[0] in _SLAVIC else ('one', 'other')


def _plural_bases(table):
    """Bases that are plural groups: two or more forms, or a few/many form (a lone one/other is an ordinary key)."""
    forms = {}
    for key in table:
        split = _split_plural(key)
        if split:
            forms.setdefault(split[0], set()).add(split[1])
    return {base for base, found in forms.items() if len(found) > 1 or found & {'few', 'many'}}


def _logical_keys(table, bases):
    """{logical key: label of the file it came from}: the forms of a plural group count as one key (the base)."""
    logical = {}
    for key, (_, label) in table.items():
        split = _split_plural(key)
        logical.setdefault(split[0] if split and split[0] in bases else key, label)
    return logical


def _compare_tables(ru, en, report):
    """L10nTables.Compare for ru and en: keys one language has and the other lacks, plural forms a language needs."""
    bases = _plural_bases(ru) | _plural_bases(en)
    logical = {'ru': _logical_keys(ru, bases), 'en': _logical_keys(en, bases)}
    for a, b in (('ru', 'en'), ('en', 'ru')):
        for key in sorted(set(logical[a]) - set(logical[b])):
            report.error(logical[a][key], 'ключ «%s» есть в %s, но нет в %s (наборы ключей ru и en должны совпадать).' % (key, a, b))
    for language, table in (('ru', ru), ('en', en)):
        for base in sorted(bases):
            present = {s for s in _PLURAL_SUFFIXES if base + '.' + s in table}
            if not present:
                continue                                       # absent group (reported above) or a text with '|' under the bare key
            for form in _forms_of(language):
                if form in present or (form == 'many' and 'other' in present) or (form == 'other' and 'many' in present):
                    continue
                label = next(table[base + '.' + s][1] for s in _PLURAL_SUFFIXES if s in present)
                report.error(label, 'у плюрального ключа «%s» в %s не хватает формы «%s» (нужны: %s).'
                             % (base, language, form, ', '.join(_forms_of(language))))


def _check_l10n(src_dir, prefixes, report):
    root = os.path.join(src_dir, 'L10n')
    if not os.path.isdir(root):
        report.error('L10n', 'папки нет: без неё в PTMod.dll не будет строк.')
        return
    files = []
    for rel, path in _json_files(root):
        parsed = _parse_table_path(rel)
        if parsed is None:
            report.error('L10n/' + rel, 'это не имя таблицы строк (ожидается L10n/<язык>.json или L10n/<язык>/<фича>.json).')
            continue
        files.append((parsed[1] is not None, rel, path, parsed[0], parsed[1]))
    files.sort(key=lambda f: (f[0], f[1]))                     # language files first, then the feature files, as ModL10n loads them

    tables, broken = {}, set()
    for _, rel, path, language, feature in files:
        label = 'L10n/' + rel
        report.l10n_files += 1
        doc = _read_source_json(path, label, report)
        if doc is None:
            broken.add(language)
            continue
        if not isinstance(doc, dict):
            report.error(label, 'таблица строк должна быть объектом JSON { "ключ": "текст" }.')
            broken.add(language)
            continue
        table = tables.setdefault(language, {})
        # L10n/<lang>/<feature>.json belongs to its feature: every key carries one of that feature's prefixes. The shared
        # L10n/<lang>.json holds the keys of the core and of the features that keep their strings there (the specs of the
        # features do): the file does not say whose a key is, so the prefix must at least be one of the table (FR-25 item 2).
        row = None
        if prefixes is not None and feature is not None:
            row = prefixes.get(feature)
            if row is None:
                report.error(label, 'фичи «%s» нет в таблице prefixes файла core.json: префикс ключей проверить не по чему.' % feature)
        for key, text in doc.items():
            if key == '':
                report.error(label, 'запись с пустым ключом.')
                continue
            if not isinstance(text, str):
                report.error(label, 'ключ «%s»: значение должно быть строкой.' % key)
                continue
            if not text.strip():
                report.error(label, 'ключ «%s»: пустой текст.' % key)
            if row is not None and not _has_prefix(key, row):
                hint = ''
                other = _owner_of(prefixes, key)
                if other is not None and other != feature:
                    hint = ' Ключи фичи «%s» кладут в L10n/%s/%s.json.' % (other, language, other)
                report.error(label, _foreign_prefix_text('ключ', key, feature, prefixes, 'файл') + hint)
            elif prefixes is not None and feature is None and not any(_has_prefix(key, r) for r in prefixes.values()):
                head = key.split('.', 1)[0]
                report.error(label, 'ключ «%s» %s: такого префикса нет в таблице prefixes файла core.json (ключ фичи начинается '
                             'с её префикса и точки, ключ ядра — с «core.»).'
                             % (key, 'с неизвестным префиксом «%s.»' % head if '.' in key else 'без префикса фичи'))
            if key in table:
                report.warn(label, 'ключ «%s» уже задан в %s: этот текст его заменит.' % (key, table[key][1]))
            table[key] = (text, label)

    for language, table in sorted(tables.items()):
        report.l10n_keys[language] = len(table)
    for language in ('ru', 'en'):
        if language not in tables and language not in broken:
            report.error('L10n', 'нет таблицы %s (L10n/%s.json): мод ждёт строки на обоих языках.' % (language, language))
    if all(lang in tables and lang not in broken for lang in ('ru', 'en')):
        _compare_tables(tables['ru'], tables['en'], report)


def check_mod_sources(src_dir):
    """Check the content and the string tables of the mod in `src_dir` (mod/src/PTMod) - `install.py --check`, no game
    needed (FR-20 item 7). Content/<feature>/<file>.json: strict JSON, an object with a known "schema"; record files have
    unique ids ("records[].id", and siblings in any array); the event packs carry the prefix of their feature; core.json
    declares each channel once. L10n/<lang>.json and L10n/<lang>/<feature>.json: flat tables of non-empty strings; a key
    of a feature file carries a prefix of that feature, a key of the shared language file a prefix of the table; the key
    sets of ru and en are equal, plural groups compared by group. The prefixes come from the table in
    Content/mod-framework/core.json.
    Never raises; returns a CheckReport (a missing `src_dir` is reported as not found, not as an error)."""
    report = CheckReport(src_dir)
    if not report.found:
        return report
    try:
        core, preloaded = None, {}
        core_path = os.path.join(src_dir, *CORE_CONTENT.split('/'))
        if os.path.isfile(core_path):
            core = _read_source_json(core_path, CORE_CONTENT, report)
            preloaded[CORE_CONTENT] = core                     # read once; _check_content must not report its problems again
        else:
            report.error(CORE_CONTENT, 'файла нет: без него не объявлены каналы и не проверить префиксы.')
        prefixes = _prefix_table(core, report) if core is not None else None
        _check_content(src_dir, prefixes, report, preloaded)
        _check_l10n(src_dir, prefixes, report)
    except Exception as e:                                     # a checker that crashes must say so, not hide the rest
        report.error('проверка', 'внутренняя ошибка проверки: %s: %s. Сообщите об ошибке разработчикам.' % (type(e).__name__, e))
    return report
