#!/usr/bin/env python3
"""Установка и снятие спайка 0001 (TSK-005): загружает ли игра PTMod.dll по записям в двух JSON Unity.

  python tools/spike_install.py --game-dir "<папка игры>" --install [--register-harmony] [--dll <PTMod.dll>] [--dry-run]
  python tools/spike_install.py --game-dir "<папка игры>" --status [--log <Player.log>]
  python tools/spike_install.py --game-dir "<папка игры>" --remove [--dry-run]
  python tools/spike_install.py --self-test

--install   копирует PTMod.dll спайка (по умолчанию mod/build/spikes/loadspike/PTMod.dll) и 0Harmony.dll (mod/lib)
            в Processor Tycoon_Data/Managed/, делает резервные копии ScriptingAssemblies.json и
            RuntimeInitializeOnLoads.json в _original_en/ (если их там ещё нет) и добавляет записи через ptru/mods.py:
            PTMod.dll (тип 16) и PTMod.Core.ModBootstrap.Init (BeforeSceneLoad). С --register-harmony в
            ScriptingAssemblies.json добавляется и 0Harmony.dll (вопрос спайка: нужна ли такая запись).
            Повторный запуск ничего не дублирует. Манифест: _original_en/ptmod_spike_install.json.
--remove    возвращает игру в ванильное состояние: убирает записи (JSON получают исходный текст), удаляет скопированные
            DLL и манифест. Резервные копии в _original_en/ остаются (это оригиналы).
--status    что сейчас установлено и что спайк написал в Player.log: строки "[PTMod] spike ..." и предварительный
            вердикт по критериям (одна строка Init, Init раньше сцены 0, хост создан и жив, Harmony найден).
--dry-run   показывает, что будет сделано, ничего не меняя (файлы игры только читаются).
--self-test прогоняет установку, повторную установку и снятие на временной копии структуры игры; игру не трогает.

Установка проверяет: DLL похож на DLL спайка (иначе легко поставить настоящий мод под видом спайка), игра не
запущена (tasklist), чужой установки нет (файл PTMod.dll или записи без нашего манифеста), есть _original_en/.
Любая ошибка посередине откатывает уже сделанное. Игру скрипт не запускает. Только стандартная библиотека и ptru/mods.py.
Код возврата: 0 - готово, 1 - self-test не прошёл, 2 - отказ или ошибка окружения (сообщение на русском).
"""
import argparse
import contextlib
import hashlib
import io
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, REPO)
from ptru import mods  # noqa: E402  (needs REPO on sys.path)

SPIKE_DLL = 'PTMod.dll'                 # same file name and assembly name as the real mod: the registration is the real one
HARMONY_DLL = '0Harmony.dll'
DEFAULT_SPIKE_BUILD = os.path.join(REPO, 'mod', 'build', 'spikes', 'loadspike', SPIKE_DLL)
DEFAULT_HARMONY = os.path.join(REPO, 'mod', 'lib', HARMONY_DLL)
BUILD_HINT = ('python mod\\build.py --game-dir "<папка игры>" --src mod\\spikes\\loadspike '
              '--out spikes\\loadspike\\PTMod.dll')
MANIFEST = 'ptmod_spike_install.json'
FULL_MANIFEST = 'ptmod_install.json'    # written by install.py --with-mods (TSK-044); its presence means the real mod is installed
MARKER = '[PTMod] spike Init'           # a string literal of the spike DLL; the real mod's DLL has no such text
LOG_TAG = '[PTMod] spike '
GAME_EXE = 'Processor Tycoon.exe'
DEFAULT_COMPANY, DEFAULT_PRODUCT = 'CoffeeHeaven', 'Processor Tycoon'   # app.info of the current game version


class SpikeError(Exception):
    """A refusal or an environment problem. str(e) is the Russian text shown to the user."""


def say(text=''):
    print(text, flush=True)


# ---------------------------------------------------------------------------------------------------------------
# Game folder and files
# ---------------------------------------------------------------------------------------------------------------

class Paths:
    """Everything the spike touches, relative to the game folder."""

    def __init__(self, root):
        self.root = root
        self.data = os.path.join(root, mods.DATA_DIR)
        self.managed = os.path.join(self.data, 'Managed')
        self.assemblies_json = mods.scripting_assemblies_path(root)
        self.runtime_json = mods.runtime_init_path(root)
        self.backup = os.path.join(root, mods.BACKUP_DIR)
        self.manifest = os.path.join(self.backup, MANIFEST)
        self.full_manifest = os.path.join(self.backup, FULL_MANIFEST)
        self.mod_dll = os.path.join(self.managed, SPIKE_DLL)
        self.harmony_dll = os.path.join(self.managed, HARMONY_DLL)
        self.app_info = os.path.join(self.data, 'app.info')

    def rel(self, path):
        return os.path.relpath(path, self.root)


def find_game(arg):
    raw = arg or os.environ.get('PT_GAME_DIR')
    if not raw:
        raise SpikeError('Не указана папка игры.\n'
                         '  Передайте её параметром: --game-dir "C:\\Games\\Processor Tycoon"\n'
                         '  или задайте переменную окружения PT_GAME_DIR (папка, где лежит "%s").' % GAME_EXE)
    path = os.path.abspath(raw.strip().strip('"'))
    for base in (path, os.path.dirname(path)):
        paths = Paths(base)
        if (os.path.isdir(paths.managed) and os.path.isfile(paths.assemblies_json)
                and os.path.isfile(paths.runtime_json)):
            return paths
    raise SpikeError('В "%s" не найдена папка игры (нет %s\\ScriptingAssemblies.json и RuntimeInitializeOnLoads.json).\n'
                     '  Укажите папку, где лежит "%s".' % (path, mods.DATA_DIR, GAME_EXE))


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def is_spike_dll(path):
    """True if the DLL contains the spike's marker string (stored UTF-16LE in the metadata #US heap)."""
    with open(path, 'rb') as f:
        return MARKER.encode('utf-16-le') in f.read()


def game_running():
    """True if Processor Tycoon.exe is running (Windows only; elsewhere the check is skipped)."""
    if os.name != 'nt':
        return False
    try:
        out = subprocess.run(['tasklist', '/FI', 'IMAGENAME eq ' + GAME_EXE, '/NH', '/FO', 'CSV'],
                             stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30).stdout
    except (OSError, subprocess.SubprocessError):
        return False
    return GAME_EXE.lower() in out.decode('utf-8', 'replace').lower()


def inspect_state(paths):
    """What is installed now. Reading a broken JSON raises mods.ModsJsonError (turned into SpikeError by main)."""
    return {
        'mod_dll': os.path.isfile(paths.mod_dll),
        'harmony_file': os.path.isfile(paths.harmony_dll),
        'asm_mod': mods.is_assembly_registered(paths.assemblies_json, SPIKE_DLL),
        'asm_harmony': mods.is_assembly_registered(paths.assemblies_json, HARMONY_DLL),
        'runtime': mods.is_runtime_init_registered(paths.runtime_json),
    }


def read_manifest(paths):
    if not os.path.isfile(paths.manifest):
        return None
    try:
        with open(paths.manifest, encoding='utf-8') as f:
            data = json.load(f)
        if data.get('kind') != 'ptmod-load-spike':
            raise ValueError('kind')
        return data
    except (OSError, ValueError, AttributeError):
        raise SpikeError('Манифест спайка "%s" повреждён. Если спайк ставился этим скриптом, снимите его с --force '
                         '(удалит PTMod.dll, 0Harmony.dll и записи в JSON).' % paths.manifest)


def write_manifest(paths, data):
    text = json.dumps(data, ensure_ascii=False, indent=2) + '\n'
    mods.write_atomic(paths.manifest, text.encode('utf-8'))


def same_bytes(a, b):
    try:
        with open(a, 'rb') as fa, open(b, 'rb') as fb:
            return fa.read() == fb.read()
    except OSError:
        return False


# ---------------------------------------------------------------------------------------------------------------
# Install
# ---------------------------------------------------------------------------------------------------------------

def check_install(paths, dll_src, harmony_src, register_harmony, force, dry_run):
    """Read-only preconditions. Returns the manifest (or None) and the list of warnings."""
    warnings = []
    if not os.path.isfile(dll_src):
        raise SpikeError('Не найден PTMod.dll спайка: %s\n  Соберите его командой:\n    %s' % (dll_src, BUILD_HINT))
    if not force and not is_spike_dll(dll_src):
        raise SpikeError('"%s" не похож на DLL спайка (в нём нет строки "%s").\n'
                         '  Спайк ставится только из сборки mod\\spikes\\loadspike:\n    %s\n'
                         '  Чтобы поставить этот файл как есть, добавьте --force.' % (dll_src, MARKER, BUILD_HINT))
    if not os.path.isdir(paths.backup):
        raise SpikeError('Нет папки "%s".\n  Сначала установите русификатор (python install.py "<папка игры>"): он сохраняет '
                         'туда оригиналы, а спайк кладёт туда резервные копии двух JSON.' % paths.backup)
    if not os.path.isfile(harmony_src):
        warnings.append('Нет %s: 0Harmony.dll не будет скопирован, проба Harmony в спайке покажет "NOT loaded".' % harmony_src)
    if game_running():
        message = 'Игра запущена (%s). Закройте её и повторите: Unity держит файлы Managed открытыми.' % GAME_EXE
        if dry_run or force:
            warnings.append(message)
        else:
            raise SpikeError(message)

    manifest = read_manifest(paths)
    if manifest is None:
        state = inspect_state(paths)
        foreign = []
        if state['mod_dll']:
            foreign.append('файл %s' % paths.rel(paths.mod_dll))
        if state['asm_mod']:
            foreign.append('запись %s в ScriptingAssemblies.json' % SPIKE_DLL)
        if state['runtime']:
            foreign.append('запись PTMod.Core.ModBootstrap в RuntimeInitializeOnLoads.json')
        if register_harmony and state['asm_harmony']:
            foreign.append('запись %s в ScriptingAssemblies.json' % HARMONY_DLL)
        if os.path.exists(paths.full_manifest):
            foreign.append('манифест установки мода (%s)' % FULL_MANIFEST)
        if foreign and not force:
            raise SpikeError('Уже есть установка мода, сделанная не этим скриптом:\n  - %s\n'
                             '  Спайк её не трогает. Снимите мод его собственным способом (install.py --remove-mods) '
                             'или, если это остатки ручной установки, повторите с --force.' % '\n  - '.join(foreign))
        if state['harmony_file'] and os.path.isfile(harmony_src) and not same_bytes(harmony_src, paths.harmony_dll) \
                and not force:
            raise SpikeError('В Managed уже лежит другой %s. Спайк его не заменяет; повторите с --force, если это нужно.'
                             % HARMONY_DLL)
    return manifest, warnings


def copy_file(src, dst):
    with open(src, 'rb') as f:
        mods.write_atomic(dst, f.read())


def install(paths, dll_src, harmony_src, register_harmony, dry_run=False, force=False):
    manifest, warnings = check_install(paths, dll_src, harmony_src, register_harmony, force, dry_run)
    owned = dict(manifest) if manifest else {'kind': 'ptmod-load-spike', 'harmonyCopied': False,
                                             'registeredAssemblies': [], 'runtimeInit': False, 'jsonBackups': []}
    have_harmony_src = os.path.isfile(harmony_src)
    copy_harmony = have_harmony_src and (not os.path.isfile(paths.harmony_dll) or owned['harmonyCopied']
                                         or (force and not same_bytes(harmony_src, paths.harmony_dll)))

    if register_harmony and not (copy_harmony or os.path.isfile(paths.harmony_dll)):
        # A registered but missing assembly is exactly the kind of mistake that can stop the game from starting.
        raise SpikeError('С --register-harmony нужен файл 0Harmony.dll, а его нет ни в Managed, ни в %s.\n'
                         '  Как получить файл — в mod\\lib\\README.md.' % harmony_src)

    say('Спайк 0001: установка%s' % (' (пробный прогон, ничего не меняется)' if dry_run else ''))
    say('  игра:        %s' % paths.root)
    say('  PTMod.dll:   %s' % dll_src)
    for text in warnings:
        say('  ВНИМАНИЕ: ' + text)
    plan = ['скопировать %s -> %s' % (os.path.basename(dll_src), paths.rel(paths.mod_dll))]
    if copy_harmony:
        plan.append('скопировать %s -> %s' % (HARMONY_DLL, paths.rel(paths.harmony_dll)))
    plan.append('сохранить в %s копии ScriptingAssemblies.json и RuntimeInitializeOnLoads.json (если их там нет)'
                % mods.BACKUP_DIR)
    plan.append('ScriptingAssemblies.json: добавить %s (тип %d)' % (SPIKE_DLL, mods.ASSEMBLY_TYPE))
    if register_harmony:
        plan.append('ScriptingAssemblies.json: добавить %s (тип %d)' % (HARMONY_DLL, mods.ASSEMBLY_TYPE))
    elif HARMONY_DLL in owned['registeredAssemblies']:
        plan.append('ScriptingAssemblies.json: убрать %s (запись от прошлой установки)' % HARMONY_DLL)
    plan.append('RuntimeInitializeOnLoads.json: добавить PTMod.Core.ModBootstrap.Init (BeforeSceneLoad)')
    plan.append('записать манифест %s' % paths.rel(paths.manifest))
    for i, step in enumerate(plan, 1):
        say('  %d. %s' % (i, step))
    if dry_run:
        say('Пробный прогон завершён: JSON читаются, предусловия выполнены. Для установки уберите --dry-run.')
        return

    undo = []
    try:
        for json_path in (paths.assemblies_json, paths.runtime_json):
            if mods.backup_json(json_path, paths.backup):
                dst = os.path.join(paths.backup, os.path.basename(json_path))
                owned['jsonBackups'].append(os.path.basename(json_path))
                undo.append(lambda dst=dst: os.remove(dst))
        copy_file(dll_src, paths.mod_dll)
        undo.append(lambda: os.remove(paths.mod_dll))
        if copy_harmony:
            copy_file(harmony_src, paths.harmony_dll)
            owned['harmonyCopied'] = True
            undo.append(lambda: os.remove(paths.harmony_dll))

        wanted = [SPIKE_DLL] + ([HARMONY_DLL] if register_harmony else [])
        for name in wanted:
            if mods.register_assembly(paths.assemblies_json, name):
                undo.append(lambda name=name: mods.unregister_assembly(paths.assemblies_json, name))
                if name not in owned['registeredAssemblies']:
                    owned['registeredAssemblies'].append(name)
        if HARMONY_DLL in owned['registeredAssemblies'] and not register_harmony:
            mods.unregister_assembly(paths.assemblies_json, HARMONY_DLL)
            owned['registeredAssemblies'].remove(HARMONY_DLL)
        if mods.register_runtime_init(paths.runtime_json):
            undo.append(lambda: mods.unregister_runtime_init(paths.runtime_json))
            owned['runtimeInit'] = True

        owned.setdefault('date', time.strftime('%Y-%m-%dT%H:%M:%S'))     # date of the first install: a repeat changes nothing
        owned['files'] = {paths.rel(p).replace('\\', '/'): sha256_of(p)
                          for p in (paths.mod_dll, paths.harmony_dll) if os.path.isfile(p)}
        write_manifest(paths, owned)
    except BaseException:
        for action in reversed(undo):
            with contextlib.suppress(Exception):
                action()
        say('Установка не удалась, сделанные изменения отменены.')
        raise

    state = inspect_state(paths)
    say('Готово.')
    say('  PTMod.dll в Managed: %s; в ScriptingAssemblies.json: %s; в RuntimeInitializeOnLoads.json: %s'
        % (yn(state['mod_dll']), yn(state['asm_mod']), yn(state['runtime'])))
    say('  0Harmony.dll в Managed: %s; в ScriptingAssemblies.json: %s'
        % (yn(state['harmony_file']), yn(state['asm_harmony'])))
    say('Дальше: запустите игру, дойдите до главного меню и закройте её, затем выполните')
    say('    python tools\\spike_install.py --game-dir "%s" --status' % paths.root)
    say('Подробная инструкция: mod\\spikes\\loadspike\\readme.txt')


def yn(value):
    return 'да' if value else 'нет'


# ---------------------------------------------------------------------------------------------------------------
# Remove
# ---------------------------------------------------------------------------------------------------------------

def remove(paths, dry_run=False, force=False):
    manifest = read_manifest(paths)
    state = inspect_state(paths)
    present = state['mod_dll'] or state['asm_mod'] or state['runtime'] or state['asm_harmony']
    if manifest is None and not present:
        say('Спайк не установлен: убирать нечего.')
        return
    if manifest is None and not force:
        raise SpikeError('Манифест спайка (%s) не найден, но в игре есть следы PTMod:\n'
                         '  PTMod.dll в Managed: %s; записи: PTMod.dll - %s, PTMod.Core.ModBootstrap - %s, 0Harmony.dll - %s.\n'
                         '  Похоже на ручную установку или на мод, поставленный install.py. Чтобы всё равно снять эти записи '
                         'и удалить PTMod.dll и 0Harmony.dll, повторите с --force.'
                         % (paths.rel(paths.manifest), yn(state['mod_dll']), yn(state['asm_mod']), yn(state['runtime']),
                            yn(state['asm_harmony'])))
    if game_running() and not force:
        if dry_run:
            say('ВНИМАНИЕ: игра запущена (%s); при настоящем снятии её нужно закрыть.' % GAME_EXE)
        else:
            raise SpikeError('Игра запущена (%s). Закройте её и повторите: Unity держит файлы Managed открытыми.' % GAME_EXE)

    forced = manifest is None
    recorded = (manifest or {}).get('files', {})
    registered = list((manifest or {}).get('registeredAssemblies', [SPIKE_DLL, HARMONY_DLL]))
    files = [paths.mod_dll]
    if forced or manifest.get('harmonyCopied'):
        files.append(paths.harmony_dll)
    for path in files:
        key = paths.rel(path).replace('\\', '/')
        if os.path.isfile(path) and key in recorded and sha256_of(path) != recorded[key] and not force:
            raise SpikeError('Файл %s изменён после установки спайка (хэш не совпал), поэтому не удаляется.\n'
                             '  Если это настоящий мод, снимайте его install.py --remove-mods. Чтобы удалить файл всё равно, '
                             'повторите с --force.' % paths.rel(path))

    say('Спайк 0001: снятие%s' % (' (пробный прогон, ничего не меняется)' if dry_run else ''))
    say('  игра: %s' % paths.root)
    plan = ['RuntimeInitializeOnLoads.json: убрать запись PTMod.Core.ModBootstrap']
    plan += ['ScriptingAssemblies.json: убрать %s' % name for name in registered]
    plan += ['удалить %s' % paths.rel(p) for p in files if os.path.isfile(p)]
    plan.append('удалить манифест %s' % paths.rel(paths.manifest))
    for i, step in enumerate(plan, 1):
        say('  %d. %s' % (i, step))
    if dry_run:
        say('Пробный прогон завершён. Для снятия уберите --dry-run.')
        return

    # JSON first: a registered but missing DLL is worse than an unregistered leftover file.
    mods.unregister_runtime_init(paths.runtime_json)
    for name in registered:
        mods.unregister_assembly(paths.assemblies_json, name)
    failed = []
    for path in files:
        try:
            if os.path.isfile(path):
                os.remove(path)
        except OSError as e:
            failed.append('%s (%s)' % (paths.rel(path), e.strerror or e))
    with contextlib.suppress(FileNotFoundError):
        os.remove(paths.manifest)

    say('Готово: записи сняты.')
    for json_path in (paths.assemblies_json, paths.runtime_json):
        backup = os.path.join(paths.backup, os.path.basename(json_path))
        if not os.path.isfile(backup):
            say('  %s: резервной копии нет, сверить не с чем' % os.path.basename(json_path))
        elif same_bytes(json_path, backup):
            say('  %s: совпадает с оригиналом из %s' % (os.path.basename(json_path), mods.BACKUP_DIR))
        else:
            say('  %s: отличается от копии в %s (после неё файл правили, например другой мод; для самого спайка это нормально)'
                % (os.path.basename(json_path), mods.BACKUP_DIR))
    if failed:
        raise SpikeError('Записи сняты, но не удалось удалить файлы: %s.\n  Закройте игру и удалите их вручную '
                         '(без записей в JSON они игре не мешают).' % '; '.join(failed))
    say('Резервные копии JSON остаются в %s: это оригиналы.' % mods.BACKUP_DIR)


# ---------------------------------------------------------------------------------------------------------------
# Status and Player.log analysis
# ---------------------------------------------------------------------------------------------------------------

def player_log_path(paths):
    """%USERPROFILE%\\AppData\\LocalLow\\<company>\\<product>\\Player.log; company and product are in app.info."""
    company, product = DEFAULT_COMPANY, DEFAULT_PRODUCT
    try:
        with open(paths.app_info, encoding='utf-8-sig') as f:
            lines = [line.strip() for line in f.read().splitlines() if line.strip()]
        if len(lines) >= 2:
            company, product = lines[0], lines[1]
    except OSError:
        pass
    home = os.environ.get('USERPROFILE') or os.path.expanduser('~')
    return os.path.join(home, 'AppData', 'LocalLow', company, product, 'Player.log')


def analyze_log(lines):
    """Pure: the spike lines of a Player.log and the verdict on each acceptance point (True / False / None = unknown)."""
    tagged = [(i, line.rstrip()) for i, line in enumerate(lines) if LOG_TAG in line]
    texts = [t for _, t in tagged]

    def first(part):
        return next((i for i, t in tagged if part in t), None)

    inits = [i for i, t in tagged if 'spike Init:' in t]
    scene0 = first('scene loaded: buildIndex=0')
    quit_at, destroyed_at = first('host: quit'), first('host: destroyed')
    harmony = next((t for t in texts if 'spike harmony:' in t), None)
    verdict = {
        'init_once': (len(inits) == 1) if tagged else None,
        'init_before_scene0': (inits[0] < scene0) if inits and scene0 is not None else None,
        'host_created': True if first('host: created=True') is not None
        else (False if first('host: created=') is not None or first('host: creation failed') is not None else None),
        'host_alive': (False if destroyed_at is not None and (quit_at is None or destroyed_at < quit_at)
                       else (True if first('scene loaded: buildIndex=1') is not None else None)),
        'harmony_loaded': (('harmony: loaded' in harmony) if harmony else None),
    }
    return {'lines': texts, 'verdict': verdict, 'init_count': len(inits)}


VERDICT_TEXT = [
    ('init_once', 'ровно одна строка "spike Init" за запуск'),
    ('init_before_scene0', 'Init раньше загрузки сцены level0 (buildIndex=0)'),
    ('host_created', 'PTModHost создан (AddComponent из загруженной DLL сработал)'),
    ('host_alive', 'PTModHost пережил смену сцены (DontDestroyOnLoad)'),
    ('harmony_loaded', '0Harmony.dll найден Mono без дополнительных действий'),
]


def report_log(path, limit=60):
    try:
        with open(path, encoding='utf-8', errors='replace') as f:
            lines = f.read().splitlines()
    except OSError:
        say('Player.log: не найден (%s). Он появляется после запуска игры; для сохранённой копии укажите --log <файл>.' % path)
        return
    mtime = time.strftime('%Y-%m-%d %H:%M:%S', time.localtime(os.path.getmtime(path)))
    result = analyze_log(lines)
    say('Player.log: %s (изменён %s, строк %d)' % (path, mtime, len(lines)))
    if not result['lines']:
        say('  Строк "%s..." нет. После снятия спайка так и должно быть. Если спайк установлен, значит игра не запускалась'
            ' после установки или Unity не вызвал Init (mod\\spikes\\loadspike\\readme.txt, раздел "Если что-то пошло не так").'
            % LOG_TAG.strip())
        return
    say('Строки спайка (вставьте их в Evidence решения 0001):')
    for text in result['lines'][:limit]:
        say('  ' + text)
    if len(result['lines']) > limit:
        say('  ... ещё %d' % (len(result['lines']) - limit))
    say('Предварительный вердикт (сверьте с самим логом):')
    for key, title in VERDICT_TEXT:
        value = result['verdict'][key]
        say('  [%s] %s' % ({True: 'ДА ', False: 'НЕТ', None: ' ? '}[value], title))


def status(paths, log_path):
    state = inspect_state(paths)
    manifest = read_manifest(paths)
    say('Спайк 0001: состояние')
    say('  игра: %s' % paths.root)
    say('  манифест спайка: %s' % (('есть (от %s)' % manifest.get('date', '?')) if manifest else 'нет'))
    say('  Managed\\PTMod.dll: %s; в ScriptingAssemblies.json: %s; в RuntimeInitializeOnLoads.json: %s'
        % (yn(state['mod_dll']), yn(state['asm_mod']), yn(state['runtime'])))
    say('  Managed\\0Harmony.dll: %s; в ScriptingAssemblies.json: %s' % (yn(state['harmony_file']), yn(state['asm_harmony'])))
    if state['mod_dll']:
        say('  PTMod.dll похож на DLL спайка: %s' % yn(is_spike_dll(paths.mod_dll)))
    say('  игра запущена: %s' % yn(game_running()))
    say()
    report_log(log_path or player_log_path(paths))


# ---------------------------------------------------------------------------------------------------------------
# Self-test
# ---------------------------------------------------------------------------------------------------------------

FAKE_ASSEMBLIES = '{"names":["UnityEngine.dll","Assembly-CSharp.dll"],"types":[0,16]}'
FAKE_RUNTIME = ('{"root":[{"assemblyName":"Unity.Burst","nameSpace":"","className":"$BurstDirectCallInitializer",'
                '"methodName":"Initialize","loadTypes":2,"isUnityClass":true}]}')
GOOD_LOG = ['Initialize engine version: 6000.0.65f1', '[PTMod] spike Init: unity=6000.0.65f1, mono=4.0.30319.42000',
            '[PTMod] spike info: runtime=Mono', '[PTMod] spike harmony: loaded 2.3.6.0 from "x", instance created=True',
            '[PTMod] spike host: Awake, frame=0', '[PTMod] spike host: created=True, awakeCalls=1',
            '[PTMod] spike scene loaded: buildIndex=0, name=level0, mode=Single, hostFrames=0',
            '[PTMod] spike scene loaded: buildIndex=1, name=level1, mode=Single, hostFrames=400',
            '[PTMod] spike host: quit, frames=900, sceneLoadsSeen=2', '[PTMod] spike host: destroyed, frames=900']


def _tree(root):
    """{relative path: sha256} of every file under root, to prove that a dry run changed nothing."""
    result = {}
    for base, _, names in os.walk(root):
        for name in names:
            path = os.path.join(base, name)
            result[os.path.relpath(path, root)] = sha256_of(path)
    return result


def self_test():
    failures = []

    def check(condition, text):
        if not condition:
            failures.append(text)

    def run(args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            code = main(args)
        return code, out.getvalue()

    # The check must not depend on whether the real game happens to be running on this machine.
    real_game_running = globals()['game_running']
    globals()['game_running'] = lambda: False
    try:
        with tempfile.TemporaryDirectory(prefix='ptmod_spike_selftest_') as tmp:
            _self_test_in(tmp, check, run)
    finally:
        globals()['game_running'] = real_game_running

    if failures:
        say('Self-test: ПРОВАЛЕН (%d)' % len(failures))
        for text in failures:
            say('  - ' + text)
        return 1
    say('Self-test: всё прошло (установка, повторная установка, снятие, отказы, разбор Player.log).')
    return 0


def _self_test_in(tmp, check, run):
    game = os.path.join(tmp, 'game')
    data = os.path.join(game, mods.DATA_DIR)
    os.makedirs(os.path.join(data, 'Managed'))
    os.makedirs(os.path.join(game, mods.BACKUP_DIR))
    files = {'ScriptingAssemblies.json': FAKE_ASSEMBLIES, 'RuntimeInitializeOnLoads.json': FAKE_RUNTIME}
    for name, text in files.items():
        with open(os.path.join(data, name), 'w', encoding='utf-8', newline='') as f:
            f.write(text)
    with open(os.path.join(data, 'app.info'), 'w', encoding='utf-8') as f:
        f.write('TestCompany\nTest Product\n')
    spike = os.path.join(tmp, 'spike', SPIKE_DLL)
    harmony = os.path.join(tmp, 'lib', HARMONY_DLL)
    real = os.path.join(tmp, 'real', SPIKE_DLL)
    for path, body in ((spike, b'MZ' + MARKER.encode('utf-16-le') + b'\0\0'), (harmony, b'MZ-harmony'),
                       (real, b'MZ-the-real-mod')):
        os.makedirs(os.path.dirname(path))
        with open(path, 'wb') as f:
            f.write(body)
    paths = Paths(game)
    base = ['--game-dir', game, '--dll', spike, '--harmony', harmony]
    original = {n: open(os.path.join(data, n), 'rb').read() for n in files}

    def jsons_equal_original():
        return all(open(os.path.join(data, n), 'rb').read() == original[n] for n in files)

    # 1. dry run changes nothing
    before = _tree(tmp)
    code, text = run(base + ['--install', '--dry-run'])
    check(code == 0 and _tree(tmp) == before, 'dry-run install must succeed and change nothing: %s' % text)

    # 2. install, then install again (idempotent)
    code, text = run(base + ['--install'])
    state = inspect_state(paths)
    check(code == 0 and state['mod_dll'] and state['asm_mod'] and state['runtime'], 'install failed: %s' % text)
    check(state['harmony_file'] and not state['asm_harmony'], 'Harmony must be copied but not registered by default')
    check(read_manifest(paths) is not None, 'manifest must exist after install')
    after = _tree(tmp)
    code, text = run(base + ['--install'])
    check(code == 0 and _tree(tmp) == after, 'second install must change nothing: %s' % text)

    # 3. status with a Player.log
    log = os.path.join(tmp, 'Player.log')
    with open(log, 'w', encoding='utf-8') as f:
        f.write('\n'.join(GOOD_LOG) + '\n')
    code, text = run(['--game-dir', game, '--status', '--log', log])
    check(code == 0 and 'spike Init' in text and '[ДА ] ровно одна строка' in text, 'status must report the log: %s' % text)

    # 4. remove restores the original bytes; repeated remove is a no-op
    code, text = run(['--game-dir', game, '--remove', '--dry-run'])
    check(code == 0 and inspect_state(paths)['asm_mod'], 'dry-run remove must change nothing: %s' % text)
    code, text = run(['--game-dir', game, '--remove'])
    state = inspect_state(paths)
    check(code == 0 and jsons_equal_original(), 'remove must give back the original JSON bytes: %s' % text)
    check(not state['mod_dll'] and not state['harmony_file'] and read_manifest(paths) is None, 'remove must delete files')
    code, text = run(['--game-dir', game, '--remove'])
    check(code == 0 and 'не установлен' in text, 'second remove must be a no-op: %s' % text)

    # 5. install with Harmony registration, remove
    code, text = run(base + ['--install', '--register-harmony'])
    check(code == 0 and inspect_state(paths)['asm_harmony'], 'register-harmony failed: %s' % text)
    code, text = run(base + ['--install'])      # without the flag: the earlier Harmony entry goes away
    check(code == 0 and not inspect_state(paths)['asm_harmony'], 'install without the flag must unregister Harmony: %s' % text)
    code, text = run(['--game-dir', game, '--remove'])
    check(code == 0 and jsons_equal_original(), 'remove after register-harmony must restore the JSON: %s' % text)

    # 6. a failure in the middle of an install rolls everything back
    before = _tree(tmp)
    real_register = mods.register_runtime_init

    def failing_register(*args, **kwargs):
        raise OSError('simulated failure')

    mods.register_runtime_init = failing_register
    try:
        code, text = run(base + ['--install', '--register-harmony'])
    finally:
        mods.register_runtime_init = real_register
    check(code == 2 and _tree(tmp) == before, 'a failed install must be rolled back: %s' % text)

    # 7. refusals leave the game untouched
    before = _tree(tmp)
    code, text = run(['--game-dir', game, '--dll', real, '--harmony', harmony, '--install'])
    check(code == 2 and _tree(tmp) == before, 'a non-spike DLL must be refused: %s' % text)
    mods.register_runtime_init(paths.runtime_json)       # a hand-made registration
    before = _tree(tmp)
    code, text = run(base + ['--install'])
    check(code == 2 and _tree(tmp) == before, 'foreign registration must be refused: %s' % text)
    code, text = run(['--game-dir', game, '--remove'])
    check(code == 2 and _tree(tmp) == before, 'remove without a manifest must be refused: %s' % text)
    code, text = run(['--game-dir', game, '--remove', '--force'])
    check(code == 0 and jsons_equal_original(), 'forced remove must clean the hand-made entry: %s' % text)
    before = _tree(tmp)
    missing_harmony = ['--game-dir', game, '--dll', spike, '--harmony', os.path.join(tmp, 'no_such', HARMONY_DLL)]
    code, text = run(missing_harmony + ['--install', '--register-harmony'])
    check(code == 2 and _tree(tmp) == before, 'registering a Harmony that does not exist must be refused: %s' % text)
    shutil.rmtree(paths.backup)
    code, text = run(base + ['--install'])
    check(code == 2 and 'русификатор' in text, 'missing _original_en must be refused: %s' % text)

    # 8. Player.log analysis
    bad = [line for line in GOOD_LOG if 'scene loaded: buildIndex=0' not in line]
    check(analyze_log(GOOD_LOG)['verdict']['init_before_scene0'] is True, 'good log: Init before scene 0')
    check(analyze_log(GOOD_LOG + [GOOD_LOG[1]])['verdict']['init_once'] is False, 'two Init lines must be detected')
    check(analyze_log(bad)['verdict']['init_before_scene0'] is None, 'no scene 0 line: unknown')
    check(analyze_log(['nothing here'])['verdict']['init_once'] is None, 'empty log: unknown')
    early = ['[PTMod] spike Init: unity=x, mono=y', '[PTMod] spike host: destroyed, frames=3']
    check(analyze_log(early)['verdict']['host_alive'] is False, 'early destroy must be detected')


# ---------------------------------------------------------------------------------------------------------------
# Command line
# ---------------------------------------------------------------------------------------------------------------

def parse_args(argv):
    ap = argparse.ArgumentParser(prog='python tools/spike_install.py',
                                 description='Установка и снятие спайка 0001 (игра загружает PTMod.dll).')
    ap.add_argument('--game-dir', metavar='ПАПКА', help='папка игры, где лежит "%s" (или переменная PT_GAME_DIR)' % GAME_EXE)
    action = ap.add_mutually_exclusive_group(required=True)
    action.add_argument('--install', action='store_true', help='установить спайк')
    action.add_argument('--remove', action='store_true', help='снять спайк, вернуть игру в ванильное состояние')
    action.add_argument('--status', action='store_true', help='показать состояние и разобрать Player.log')
    action.add_argument('--self-test', action='store_true', help='проверить сам скрипт на временной копии структуры игры')
    ap.add_argument('--register-harmony', action='store_true',
                    help='с --install: добавить 0Harmony.dll в ScriptingAssemblies.json (прогон C спайка)')
    ap.add_argument('--dll', metavar='ФАЙЛ', default=DEFAULT_SPIKE_BUILD, help='DLL спайка (по умолчанию %(default)s)')
    ap.add_argument('--harmony', metavar='ФАЙЛ', default=DEFAULT_HARMONY, help='0Harmony.dll (по умолчанию %(default)s)')
    ap.add_argument('--log', metavar='ФАЙЛ', help='с --status: разобрать этот Player.log (копию), а не текущий')
    ap.add_argument('--dry-run', action='store_true', help='с --install/--remove: только показать план, ничего не менять')
    ap.add_argument('--force', action='store_true', help='обойти проверки (чужая установка, запущенная игра, не-спайковая DLL)')
    args = ap.parse_args(argv)
    if args.register_harmony and not args.install:
        ap.error('--register-harmony относится к --install')
    if args.log and not args.status:
        ap.error('--log относится к --status')
    if args.dry_run and not (args.install or args.remove):
        ap.error('--dry-run относится к --install и --remove')
    if not args.self_test and not (args.game_dir or os.environ.get('PT_GAME_DIR')):
        ap.error('нужен --game-dir (или переменная PT_GAME_DIR)')
    return args


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        # Output piped to another tool is always UTF-8, otherwise Cyrillic is garbled.
        if not stream.isatty():
            with contextlib.suppress(Exception):
                stream.reconfigure(encoding='utf-8', errors='replace')
    args = parse_args(argv if argv is not None else sys.argv[1:])
    if args.self_test:
        return self_test()
    try:
        paths = find_game(args.game_dir)
        if args.install:
            install(paths, os.path.abspath(args.dll), os.path.abspath(args.harmony), args.register_harmony,
                    args.dry_run, args.force)
        elif args.remove:
            remove(paths, args.dry_run, args.force)
        else:
            status(paths, args.log)
        return 0
    except (SpikeError, mods.ModsJsonError) as e:
        print('\n[spike_install] ОШИБКА: %s' % e, file=sys.stderr, flush=True)
        return 2
    except OSError as e:
        print('\n[spike_install] ОШИБКА ввода-вывода: %s. Закройте игру и повторите; незавершённая установка отменена.' % e,
              file=sys.stderr, flush=True)
        return 2


if __name__ == '__main__':
    sys.exit(main())
