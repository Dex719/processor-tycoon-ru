#!/usr/bin/env python3
"""Сборка мода Processor Tycoon: PTMod.dll, тесты чистой логики, релизный пакет.

  python mod/build.py --game-dir "<папка игры>"       отладочная сборка -> mod/build/PTMod.dll
  python mod/build.py --release                       релизная сборка + mod/build/release/PTMod-<версия>.zip
  python mod/build.py --tests [--filter текст]        тесты чистой логики (мини-раннер, .NET Framework 4.8)
  python mod/build.py --tests --coverage-types        + проверка: каждый публичный тип Core/Logic указан в [Covers]
  python mod/build.py --src <папка> --out <имя.dll>   сборка спайка (любая папка с *.cs) -> mod/build/<имя.dll>

Папка игры берётся из --game-dir или переменной PT_GAME_DIR; из неё читаются только DLL для ссылок
(Managed/*.dll и _original_en/Assembly-CSharp.dll), в выход и в репозиторий они не копируются.
Компилятор: Roslyn csc.exe из Visual Studio 2022 Build Tools (vswhere, известные пути, --csc, PT_CSC).
Для --tests нужны только csc и .NET Framework 4.8: ни игра, ни Harmony не требуются.
Только стандартная библиотека Python. Решение: .kiro/decisions/0003-build-toolchain.md.
"""
import argparse
import contextlib
import hashlib
import os
import re
import shutil
import subprocess
import sys
import time
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))

# Фичи, которых нет в релизной сборке (решение D27): папка Features/<Pascal>, папка Content/<slug>,
# файл L10n/<язык>/<slug>.json. Добавлять сюда служебные фичи, нужные только для проверки ядра.
RELEASE_EXCLUDED = [('FrameworkSample', 'framework-sample')]

TESTS_ASSEMBLY = 'PTMod.Tests'
TESTS_MAIN = 'PTMod.Tests.Program'
FRAMEWORK_DLLS = ['mscorlib', 'System', 'System.Core', 'System.Numerics', 'System.Xml', 'System.Xml.Linq']
REQUIRED_GAME_DLLS = ['mscorlib', 'netstandard', 'System', 'System.Core', 'UnityEngine',
                      'UnityEngine.CoreModule', 'UnityEngine.UI', 'Unity.TextMeshPro']
LOCK_TIMEOUT = 600      # сколько ждать чужую сборку, с
TESTS_TIMEOUT = 600     # сколько ждать завершения тестов, с


class BuildError(Exception):
    """Ошибка окружения или сборки: сообщение показывается пользователю без трассировки."""

    def __init__(self, message, code=2):
        Exception.__init__(self, message)
        self.code = code


class Layout:
    """Пути репозитория относительно папки mod/ (можно подменить через --mod-dir)."""

    def __init__(self, mod_dir):
        self.mod = os.path.abspath(mod_dir)
        self.src = os.path.join(self.mod, 'src', 'PTMod')
        self.tests = os.path.join(self.mod, 'tests', 'PTMod.Tests')
        self.lib = os.path.join(self.mod, 'lib')
        self.build = os.path.join(self.mod, 'build')
        self.gen = os.path.join(self.build, 'gen')
        self.harmony = os.path.join(self.lib, '0Harmony.dll')
        self.harmony_license = os.path.join(self.lib, 'Harmony.LICENSE')
        self.version_file = os.path.join(self.mod, 'VERSION')


def say(text):
    print('[build] ' + text, flush=True)


# ---------- поиск компилятора и игры ----------

def _csc_candidates(explicit=None):
    """Кандидаты по приоритету: PT_CSC, затем найденные (новая Visual Studio выше старой). Явный --csc проверяет find_csc."""
    chosen = [os.environ['PT_CSC']] if os.environ.get('PT_CSC') else []
    found = []

    def add(path):
        if path and path not in found:
            found.append(path)

    pf86 = os.environ.get('ProgramFiles(x86)', r'C:\Program Files (x86)')
    pf = os.environ.get('ProgramFiles', r'C:\Program Files')
    vswhere = os.path.join(pf86, 'Microsoft Visual Studio', 'Installer', 'vswhere.exe')
    if os.path.isfile(vswhere):
        try:
            out = subprocess.run(
                [vswhere, '-products', '*', '-requires', 'Microsoft.Component.MSBuild',
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

    found.sort(key=lambda p: ('2022' not in p, '2019' not in p))    # sort стабилен: порядок внутри года сохраняется

    # csc из PATH годится только если это Roslyn (старый csc из .NET Framework знает лишь C# 5).
    on_path = shutil.which('csc')
    if on_path and 'roslyn' in on_path.lower():
        found.append(on_path)
    return chosen + [p for p in found if p not in chosen]


def _csc_version(path):
    """Версия csc или None, если файла нет или он не запускается."""
    if not os.path.isfile(path):
        return None
    try:
        proc = subprocess.run([path, '-version'], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=60)
    except (OSError, subprocess.SubprocessError):
        return None
    return proc.stdout.decode('utf-8', 'replace').strip() if proc.returncode == 0 else None


def find_csc(explicit=None):
    """Возвращает (путь к csc.exe, версия). Явно заданный --csc обязан работать; остальное ищется само."""
    if explicit:
        version = _csc_version(explicit)
        if version is None:
            raise BuildError('Указанный компилятор не запускается: %s\n'
                             '  Проверьте путь к Roslyn csc.exe или уберите --csc, чтобы искать автоматически.' % explicit)
        return explicit, version
    for path in _csc_candidates(None):
        version = _csc_version(path)
        if version is not None:
            return path, version
    raise BuildError(
        'Не найден компилятор Roslyn (csc.exe).\n'
        '  Установите «Build Tools for Visual Studio 2022» (https://visualstudio.microsoft.com/downloads/,\n'
        '  раздел «Инструменты для Visual Studio»; достаточно компонента «MSBuild»)\n'
        '  или укажите путь: --csc "<...>\\Roslyn\\csc.exe" либо переменная окружения PT_CSC.\n'
        '  Если собирать нечем, скачайте готовые PTMod.dll и 0Harmony.dll из GitHub Release\n'
        '  и установите их: python install.py "<папка игры>" --with-mods --mods-only')


class Game:
    """Что нужно от папки игры для ссылок компилятора (только чтение)."""

    def __init__(self, root, managed, original):
        self.root = root
        self.managed = managed
        self.original = original


def find_game(game_dir_arg):
    raw = game_dir_arg or os.environ.get('PT_GAME_DIR')
    if not raw:
        raise BuildError(
            'Не указана папка игры.\n'
            '  Передайте её параметром: python mod/build.py --game-dir "C:\\Games\\Processor Tycoon"\n'
            '  или задайте переменную окружения PT_GAME_DIR.\n'
            '  Нужна папка, где лежит "Processor Tycoon.exe" (из неё читаются только DLL для ссылок).')
    path = os.path.abspath(raw.strip().strip('"'))

    def managed_of(base):
        if os.path.isfile(os.path.join(base, 'mscorlib.dll')) and os.path.basename(base).lower() == 'managed':
            return base                                    # передана сама папка Managed
        for data in sorted(os.listdir(base)) if os.path.isdir(base) else []:
            cand = os.path.join(base, data, 'Managed')
            if data.lower().endswith('_data') and os.path.isfile(os.path.join(cand, 'mscorlib.dll')):
                return cand                                # папка игры
        cand = os.path.join(base, 'Managed')
        if os.path.isfile(os.path.join(cand, 'mscorlib.dll')):
            return cand                                    # папка Processor Tycoon_Data
        return None

    managed = None
    for base in (path, os.path.dirname(path)):
        managed = managed_of(base)
        if managed:
            break
    if not managed:
        raise BuildError(
            'В "%s" не найдена папка игры (нет Processor Tycoon_Data\\Managed\\mscorlib.dll).\n'
            '  Укажите папку, где лежит "Processor Tycoon.exe".' % path)

    root = os.path.dirname(os.path.dirname(managed))
    original = os.path.join(root, '_original_en', 'Assembly-CSharp.dll')
    if not os.path.isfile(original):
        fallback = os.path.join(managed, 'Assembly-CSharp.dll')
        if not os.path.isfile(fallback):
            raise BuildError('Не найден Assembly-CSharp.dll ни в "%s", ни в "%s".\n'
                             '  Проверьте папку игры.' % (os.path.dirname(original), managed))
        say('ВНИМАНИЕ: нет _original_en\\Assembly-CSharp.dll (английский оригинал); для ссылок взят %s.\n'
            '         Для сборки этого достаточно, но лучше запустить install.py, который делает резервную копию оригинала.'
            % fallback)
        original = fallback
    return Game(root, managed, original)


def game_references(game, layout):
    managed = game.managed
    names = sorted(n for n in os.listdir(managed) if n.lower().endswith('.dll'))
    present = {n[:-4].lower() for n in names}
    missing = [n for n in REQUIRED_GAME_DLLS if n.lower() not in present]
    if missing:
        raise BuildError('В "%s" не хватает: %s.\n  Проверьте, что указана папка полной установки игры.'
                         % (managed, ', '.join(m + '.dll' for m in missing)))

    refs = [os.path.join(managed, n) for n in ('mscorlib.dll', 'netstandard.dll')]
    refs += [os.path.join(managed, n) for n in names if n.lower().startswith('system')]
    refs += [os.path.join(managed, n) for n in names if n.lower().startswith('unityengine')]
    refs.append(os.path.join(managed, 'Unity.TextMeshPro.dll'))
    refs.append(game.original)
    return refs + [require_harmony(layout)]


def require_harmony(layout):
    if not os.path.isfile(layout.harmony):
        raise BuildError('Не найден %s.\n  Это Harmony 2.3.6 (net48), он лежит вне git; как получить файл — в mod/lib/README.md.'
                         % layout.harmony)
    return layout.harmony


def framework_references():
    windir = os.environ.get('SystemRoot') or os.environ.get('WINDIR') or r'C:\Windows'
    fwdir = os.path.join(windir, 'Microsoft.NET', 'Framework64', 'v4.0.30319')
    paths = [os.path.join(fwdir, n + '.dll') for n in FRAMEWORK_DLLS]
    missing = [p for p in paths[:3] if not os.path.isfile(p)]
    if missing:
        raise BuildError('Не найден .NET Framework 4.8 (нет %s).\n'
                         '  Тесты собираются под .NET Framework 4.8 из C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\;\n'
                         '  на Windows 10/11 он встроен, проверьте установку.' % ', '.join(missing))
    return [p for p in paths if os.path.isfile(p)]


# ---------- исходники, ресурсы, версия ----------

def read_version(layout):
    if not os.path.isfile(layout.version_file):
        raise BuildError('Нет файла %s (одна строка вида 0.1.0).' % layout.version_file)
    with open(layout.version_file, encoding='utf-8-sig') as f:
        version = f.read().strip()
    m = re.match(r'^(\d+)\.(\d+)\.(\d+)(?:[-+][0-9A-Za-z.+-]+)?$', version)
    if not m:
        raise BuildError('Версия в %s должна быть вида 1.2.3 или 1.2.3-rc1, а не "%s".' % (layout.version_file, version))
    return version, '%s.%s.%s.0' % m.groups()


def _walk(root, suffix):
    """Файлы с расширением suffix под root в стабильном порядке (относительные пути через '/')."""
    found = []
    if not os.path.isdir(root):
        return found
    for base, dirs, files in os.walk(root):
        dirs.sort()
        for name in sorted(files):
            if name.lower().endswith(suffix):
                rel = os.path.relpath(os.path.join(base, name), root).replace(os.sep, '/')
                found.append(rel)
    return found


def is_release_excluded(rel):
    """rel - путь под mod/src/PTMod через '/'. Служебные фичи не входят в релиз."""
    low = rel.lower()
    parts = low.split('/')
    for pascal, slug in RELEASE_EXCLUDED:
        if low.startswith('features/%s/' % pascal.lower()):
            return True
        if low.startswith('content/%s/' % slug):
            return True
        if parts[0] == 'l10n' and len(parts) == 3 and parts[2] == slug + '.json':
            return True
    return False


def collect_sources(layout, release):
    files = [f for f in _walk(layout.src, '.cs') if not (release and is_release_excluded(f))]
    return [os.path.join(layout.src, *f.split('/')) for f in files]


def collect_resources(layout, release):
    """[(файл, LogicalName)]: Content/<фича>/<файл>.json -> content/..., L10n/... -> l10n/..."""
    result = {}
    for folder in ('Content', 'L10n'):
        for rel in _walk(os.path.join(layout.src, folder), '.json'):
            full_rel = folder + '/' + rel
            if release and is_release_excluded(full_rel):
                continue
            path = os.path.join(layout.src, folder, *rel.split('/'))
            if ',' in path:
                raise BuildError('В пути ресурса есть запятая, csc такой путь не примет: %s' % path)
            name = folder.lower() + '/' + rel
            if name.lower() in {k.lower() for k in result}:
                raise BuildError('Два ресурса с одним именем "%s".' % name)
            result[name] = path
    return [(result[n], n) for n in sorted(result)]


def write_build_info(layout, version, assembly_version, release):
    """mod/build/gen/BuildInfo.g.cs: версия из mod/VERSION попадает в AssemblyVersion и в BuildInfo.Version."""
    os.makedirs(layout.gen, exist_ok=True)
    config = 'release' if release else 'debug'
    text = (
        '// <auto-generated>\n'
        '// Generated by mod/build.py from mod/VERSION. Do not edit.\n'
        '// </auto-generated>\n'
        'using System.Reflection;\n'
        '\n'
        '[assembly: AssemblyTitle("PTMod")]\n'
        '[assembly: AssemblyVersion("{av}")]\n'
        '[assembly: AssemblyFileVersion("{av}")]\n'
        '[assembly: AssemblyInformationalVersion("{v}+{cfg}")]\n'
        '\n'
        'namespace PTMod.Core\n'
        '{{\n'
        '    internal static class BuildInfo\n'
        '    {{\n'
        '        /// <summary>Contents of mod/VERSION.</summary>\n'
        '        public const string Version = "{v}";\n'
        '\n'
        '        /// <summary>"debug" or "release".</summary>\n'
        '        public const string Configuration = "{cfg}";\n'
        '\n'
        '        /// <summary>True in the debug build (PTMOD_DEBUG): framework-sample and debug.* flags exist.</summary>\n'
        '        public const bool IsDebug = {dbg};\n'
        '    }}\n'
        '}}\n'
    ).format(av=assembly_version, v=version, cfg=config, dbg='false' if release else 'true')
    path = os.path.join(layout.gen, 'BuildInfo.g.cs')
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    return path


# ---------- csc ----------

def _quote(arg):
    return '"%s"' % arg if re.search(r'\s', arg) else arg


def run_csc(csc, args, rsp_path):
    """Передаёт аргументы через response-файл (пути с пробелами, сотни файлов). Возвращает (код, вывод)."""
    os.makedirs(os.path.dirname(rsp_path), exist_ok=True)
    with open(rsp_path, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(_quote(a) for a in args) + '\n')
    # -noconfig (не подключать стандартный csc.rsp) csc принимает только в командной строке, не в response-файле.
    proc = subprocess.run([csc, '-noconfig', '@' + rsp_path], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    return proc.returncode, proc.stdout.decode('utf-8', 'replace')


def common_args(release, defines):
    args = ['-nologo', '-utf8output', '-codepage:65001', '-fullpaths', '-nostdlib', '-langversion:latest',
            '-nowarn:1701,1702', '-deterministic', '-warn:4']
    args += ['-optimize+', '-debug-'] if release else ['-optimize-', '-debug:portable']
    if defines:
        args.append('-define:' + ';'.join(defines))
    return args


def compile_or_fail(csc, args, layout, what):
    rsp = os.path.join(layout.gen, what + '.rsp')
    code, output = run_csc(csc, args, rsp)
    output = output.strip()
    if output:
        print(output, flush=True)
    if code != 0:
        raise BuildError('Компиляция %s не удалась (код %d).' % (what, code), 1)


def _remove(*paths):
    for p in paths:
        with contextlib.suppress(FileNotFoundError):
            os.remove(p)


def _kb(path):
    return '%.0f КБ' % (os.path.getsize(path) / 1024.0)


# ---------- режимы сборки ----------

def build_mod(layout, game, csc, release):
    version, assembly_version = read_version(layout)
    if not os.path.isdir(layout.src):
        raise BuildError('Нет папки исходников %s.' % layout.src)
    sources = collect_sources(layout, release)
    resources = collect_resources(layout, release)
    sources.append(write_build_info(layout, version, assembly_version, release))

    dll = os.path.join(layout.build, 'PTMod.dll')
    _remove(dll, os.path.join(layout.build, 'PTMod.pdb'))
    args = common_args(release, [] if release else ['PTMOD_DEBUG'])
    args += ['-target:library', '-out:' + dll]
    args += ['-r:' + r for r in game_references(game, layout)]
    args += ['-resource:%s,%s' % (path, name) for path, name in resources]
    args += sources

    say('%s %s: %d файл(ов), %d ресурс(ов)' % ('release' if release else 'debug', version, len(sources) - 1, len(resources)))
    compile_or_fail(csc, args, layout, 'PTMod')
    shutil.copyfile(layout.harmony, os.path.join(layout.build, '0Harmony.dll'))
    say('готово: %s (%s), рядом 0Harmony.dll' % (dll, _kb(dll)))
    return dll, version


def build_spike(layout, game, csc, src_dir, out, release):
    src_dir = os.path.abspath(src_dir)
    if not os.path.isdir(src_dir):
        raise BuildError('Нет папки исходников спайка: %s' % src_dir)
    files = [os.path.join(src_dir, *f.split('/')) for f in _walk(src_dir, '.cs')]
    if not files:
        raise BuildError('В %s нет файлов *.cs.' % src_dir)
    dll = out if os.path.isabs(out) else os.path.join(layout.build, out)
    os.makedirs(os.path.dirname(dll), exist_ok=True)
    _remove(dll, os.path.splitext(dll)[0] + '.pdb')

    args = common_args(release, [] if release else ['PTMOD_DEBUG'])
    args += ['-target:library', '-out:' + dll]
    args += ['-r:' + r for r in game_references(game, layout)]
    args += files
    say('спайк: %d файл(ов) из %s' % (len(files), src_dir))
    compile_or_fail(csc, args, layout, 'spike-' + os.path.splitext(os.path.basename(dll))[0])
    say('готово: %s (%s)' % (dll, _kb(dll)))
    return dll


def build_tests(layout, csc, release):
    """Консольный exe под .NET Framework 4.8: Core/Logic/**, Core/Compat/** и tests/PTMod.Tests/**. Игра не нужна."""
    if not os.path.isdir(layout.tests):
        raise BuildError('Нет папки тестов %s.' % layout.tests)
    files = []
    for folder in (os.path.join(layout.src, 'Core', 'Logic'), os.path.join(layout.src, 'Core', 'Compat'), layout.tests):
        files += [os.path.join(folder, *f.split('/')) for f in _walk(folder, '.cs')]
    exe = os.path.join(layout.build, TESTS_ASSEMBLY + '.exe')
    _remove(exe, os.path.join(layout.build, TESTS_ASSEMBLY + '.pdb'))
    os.makedirs(layout.build, exist_ok=True)

    defines = ['PTMOD_TESTS'] + ([] if release else ['PTMOD_DEBUG'])
    args = common_args(False, defines)      # без оптимизации и с pdb: в трассировках падений видны строки
    args += ['-target:exe', '-main:' + TESTS_MAIN, '-out:' + exe]
    args += ['-r:' + r for r in framework_references()]
    args += files
    say('тесты: %d файл(ов)' % len(files))
    compile_or_fail(csc, args, layout, 'tests')
    return exe


def run_tests(layout, exe, tests, coverage, filters, verbose):
    cmd = [exe]
    if tests:
        cmd.append('--tests')
    if coverage:
        cmd.append('--coverage-types')
    for text in filters:
        cmd += ['--filter', text]
    if verbose:
        cmd.append('--verbose')
    env = dict(os.environ, PTMOD_MOD_DIR=layout.mod, PTMOD_SRC_DIR=layout.src, PTMOD_BUILD_DIR=layout.build)
    sys.stdout.flush()
    try:
        return subprocess.run(cmd, env=env, cwd=layout.build, timeout=TESTS_TIMEOUT).returncode
    except subprocess.TimeoutExpired:
        raise BuildError('Тесты не завершились за %d с и остановлены.' % TESTS_TIMEOUT, 1)


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def package_release(layout, dll, version):
    """mod/build/release/PTMod-<версия>/ и .zip: PTMod.dll, 0Harmony.dll, Harmony.LICENSE, SHA256SUMS.txt (без DLL игры)."""
    if not os.path.isfile(layout.harmony_license):
        raise BuildError('Нет %s: лицензию Harmony нужно класть в релиз.' % layout.harmony_license)
    release_dir = os.path.join(layout.build, 'release')
    stage = os.path.join(release_dir, 'PTMod-' + version)
    if os.path.isdir(stage):
        shutil.rmtree(stage)
    os.makedirs(stage)
    shutil.copyfile(dll, os.path.join(stage, 'PTMod.dll'))
    shutil.copyfile(layout.harmony, os.path.join(stage, '0Harmony.dll'))
    shutil.copyfile(layout.harmony_license, os.path.join(stage, 'Harmony.LICENSE'))

    # Формат sha256sum: «<хэш>  <имя>»; installer сверяет по нему файлы рядом с DLL.
    sums = ''.join('%s  %s\n' % (sha256_of(os.path.join(stage, n)), n) for n in ('PTMod.dll', '0Harmony.dll'))
    with open(os.path.join(stage, 'SHA256SUMS.txt'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(sums)

    archive = os.path.join(release_dir, 'PTMod-%s.zip' % version)
    _remove(archive)
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for name in ('PTMod.dll', '0Harmony.dll', 'Harmony.LICENSE', 'SHA256SUMS.txt'):
            info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))     # воспроизводимый архив
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            with open(os.path.join(stage, name), 'rb') as f:
                z.writestr(info, f.read())
    say('релизный пакет: %s (%s)' % (archive, _kb(archive)))
    return archive


# ---------- блокировка параллельных сборок ----------

@contextlib.contextmanager
def build_lock(layout):
    """Параллельные запуски (несколько разработчиков, CI) не должны затирать PTMod.dll и PTMod.Tests.exe друг друга."""
    os.makedirs(layout.build, exist_ok=True)
    handle = open(os.path.join(layout.build, '.build.lock'), 'a+b')
    if os.name == 'nt':
        import msvcrt

        def acquire():
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)

        def release():
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
    else:
        import fcntl

        def acquire():
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)

        def release():
            fcntl.flock(handle.fileno(), fcntl.LOCK_UN)

    started = time.time()
    announced = False
    try:
        while True:
            try:
                acquire()
                break
            except OSError:
                if time.time() - started > LOCK_TIMEOUT:
                    raise BuildError('Другая сборка не освободила mod/build за %d с.' % LOCK_TIMEOUT)
                if not announced:
                    say('другая сборка ещё идёт, жду...')
                    announced = True
                time.sleep(0.5)
        try:
            yield
        finally:
            with contextlib.suppress(OSError):
                release()
    finally:
        handle.close()


# ---------- командная строка ----------

def parse_args(argv):
    ap = argparse.ArgumentParser(
        prog='python mod/build.py',
        description='Сборка мода Processor Tycoon (PTMod.dll), тестов чистой логики и релизного пакета.')
    ap.add_argument('--game-dir', metavar='ПАПКА', help='папка игры (или переменная PT_GAME_DIR); только чтение DLL для ссылок')
    ap.add_argument('--release', action='store_true',
                    help='релизная сборка без PTMOD_DEBUG и без framework-sample + mod/build/release/PTMod-<версия>.zip')
    ap.add_argument('--tests', action='store_true', help='собрать и запустить тесты чистой логики (мини-раннер)')
    ap.add_argument('--filter', action='append', default=[], metavar='ТЕКСТ',
                    help='запустить только тесты, в полном имени которых есть ТЕКСТ (можно повторять)')
    ap.add_argument('--coverage-types', action='store_true',
                    help='проверить, что каждый публичный тип Core/Logic указан в [Covers] '
                         '(без --tests тесты не запускаются)')
    ap.add_argument('-v', '--verbose', action='store_true', help='с --tests: печатать каждый пройденный тест')
    ap.add_argument('--src', metavar='ПАПКА', help='собрать все *.cs из папки (спайк); нужен --out')
    ap.add_argument('--out', metavar='ИМЯ.dll', help='с --src: имя результата в mod/build/ (или абсолютный путь)')
    ap.add_argument('--csc', metavar='ПУТЬ', help='путь к Roslyn csc.exe (или переменная PT_CSC)')
    ap.add_argument('--mod-dir', metavar='ПАПКА', default=HERE, help=argparse.SUPPRESS)
    args = ap.parse_args(argv)

    testing = args.tests or args.coverage_types
    if bool(args.src) != bool(args.out):
        ap.error('--src и --out используются вместе')
    if args.src and (testing or args.filter):
        ap.error('--src нельзя сочетать с --tests, --coverage-types и --filter')
    if (args.filter or args.verbose) and not args.tests and not args.coverage_types:
        ap.error('--filter и --verbose относятся к --tests')
    if args.filter and args.coverage_types and not args.tests:
        ap.error('--filter не применяется к --coverage-types; добавьте --tests')
    return args


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        # Вывод в канал (другой инструмент, CI) - всегда UTF-8, иначе кириллица портится.
        if not stream.isatty():
            with contextlib.suppress(Exception):
                stream.reconfigure(encoding='utf-8', errors='replace')

    args = parse_args(argv if argv is not None else sys.argv[1:])
    layout = Layout(args.mod_dir)
    try:
        csc, csc_version = find_csc(args.csc)
        say('csc %s (%s)' % (csc_version, csc))
        with build_lock(layout):
            if args.tests or args.coverage_types:
                exe = build_tests(layout, csc, args.release)
                return run_tests(layout, exe, args.tests, args.coverage_types, args.filter, args.verbose)

            game = find_game(args.game_dir)
            if args.src:
                build_spike(layout, game, csc, args.src, args.out, args.release)
                return 0
            dll, version = build_mod(layout, game, csc, args.release)
            if args.release:
                package_release(layout, dll, version)
            return 0
    except BuildError as e:
        print('\n[build] ОШИБКА: %s' % e, file=sys.stderr, flush=True)
        return e.code


if __name__ == '__main__':
    sys.exit(main())
