#!/usr/bin/env python3
"""Русификатор Processor Tycoon.

  python install.py "C:\\Games\\Processor Tycoon"                          установить русский перевод
  python install.py "C:\\Games\\Processor Tycoon" --with-mods              перевод + моды (PTMod.dll, 0Harmony.dll)
  python install.py "C:\\Games\\Processor Tycoon" --with-mods --mods-only  только моды, перевод не трогать
  python install.py "C:\\Games\\Processor Tycoon" --remove-mods            снять только моды, перевод остаётся
  python install.py "C:\\Games\\Processor Tycoon" --restore                вернуть оригинальные (английские) файлы и снять моды
  python install.py --check                                                проверить файлы перевода и контент мода (без игры)

Для --with-mods нужны PTMod.dll и 0Harmony.dll. Установщик ищет их по порядку: папка из --mod-dir, mod\\build\\,
папка рядом с install.py (распакованный GitHub Release); если их нет, но есть csc.exe, он собирает мод сам
(python mod\\build.py --release). Сети установщик не использует.

--remove-mods удаляет файлы мода по манифесту (_original_en\\ptmod_install.json) и возвращает два JSON Unity в исходный
вид; перевод, сейвы и ваши файлы в <игра>\\PTMod\\content и PTMod\\l10n не трогаются. Если мод ставили вручную
(манифеста нет), нужен --force. --restore возвращает файлы перевода и заодно снимает моды так же.
"""
import argparse, hashlib, json, os, shutil, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

BACKUP_DIR = '_original_en'
FILES = {'data.unity3d': ('Processor Tycoon_Data', 'data.unity3d'),
         'Assembly-CSharp.dll': ('Processor Tycoon_Data', 'Managed', 'Assembly-CSharp.dll')}


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''): h.update(chunk)
    return h.hexdigest()


def game_paths(game_dir):
    return {k: os.path.join(game_dir, *v) for k, v in FILES.items()}


def find_game_dir(path):
    path = os.path.abspath(path)
    for cand in (path, os.path.dirname(path)):
        if all(os.path.isfile(p) for p in game_paths(cand).values()):
            return cand
    sys.exit('Не найдены файлы игры в "%s".\nУкажите папку, где лежит "Processor Tycoon.exe".' % path)


def remove_mods(game_dir, force=False):
    """--remove-mods: the mod's files by the manifest and its entries in the two Unity JSON files go, the translation and
    the player's <game>/PTMod/ stay (FR-20 items 4 and 6). Returns the mods.RemoveResult; any refusal or failure
    is a Russian message and exit code 1."""
    from ptru import mods
    try:
        return mods.remove_mods(game_dir, force=force)
    except (mods.ModsInstallError, mods.ModsJsonError) as e:
        sys.exit(str(e))


def restore_mods(game_dir, force=False):
    """The mod half of --restore: the same removal, but a refusal that only --force lifts (a hand-made install, a changed
    DLL, a broken manifest) is a warning here - the translation files are restored all the same. A real failure (a DLL
    locked by the running game) stops everything before any game file is touched. Returns True if the mods were touched."""
    from ptru import mods

    def say(text):
        if text != mods.NOTHING_TO_REMOVE:                    # a translation-only game does not need to hear about mods
            print(text)

    try:
        return mods.remove_mods(game_dir, force=force, log=say).changed
    except mods.ModsRemoveRefused as e:
        print('ВНИМАНИЕ: моды не сняты (файлы мода и записи в JSON остались как были), восстанавливаются только файлы перевода.\n'
              'Причина: %s' % e)
        return False
    except (mods.ModsInstallError, mods.ModsJsonError) as e:
        sys.exit(str(e))


def restore(game_dir, force=False):
    """--restore: the mods come off first (FR-20 item 5: their files by the manifest, both JSON files back), then the
    original data.unity3d and Assembly-CSharp.dll are copied back from _original_en."""
    bdir = os.path.join(game_dir, BACKUP_DIR)
    if not os.path.isdir(bdir):
        sys.exit('Резервная копия не найдена (%s) — восстанавливать нечего.' % bdir)
    mods_touched = restore_mods(game_dir, force)
    # After "--with-mods --mods-only" _original_en holds only the JSON copies and the manifest, no game originals.
    present = {n: dst for n, dst in game_paths(game_dir).items() if os.path.isfile(os.path.join(bdir, n))}
    if not present:
        if mods_touched:
            print('Оригиналов data.unity3d и Assembly-CSharp.dll в %s нет — перевод не устанавливался, возвращать нечего.' % bdir)
            return
        sys.exit('В %s нет оригиналов data.unity3d и Assembly-CSharp.dll — перевод не устанавливался, '
                 'восстанавливать нечего.' % bdir)
    for name, dst in present.items():
        shutil.copy2(os.path.join(bdir, name), dst)
    print('Оригинальные файлы восстановлены.')


def missing_backups(paths, bdir):
    """Originals not yet saved in _original_en (after --mods-only that is both of them)."""
    return [n for n in paths if not os.path.isfile(os.path.join(bdir, n))]


def require_original_dll(paths, bdir):
    """Read-only: stop if there is no pristine Assembly-CSharp.dll to be had (it is already patched and has no backup)."""
    if 'Assembly-CSharp.dll' in missing_backups(paths, bdir):
        from ptru.patch_dll import is_patched
        if is_patched(paths['Assembly-CSharp.dll']):
            sys.exit('Игра уже пропатчена, но резервной копии оригинала нет. Переустановите игру и запустите снова.')


def backup_originals(paths, bdir):
    require_original_dll(paths, bdir)
    missing = missing_backups(paths, bdir)
    if not missing:
        print('Найдена резервная копия оригинала: %s' % bdir)
        return
    os.makedirs(bdir, exist_ok=True)
    for name in missing:
        shutil.copy2(paths[name], os.path.join(bdir, name))
    print('Оригинальные файлы сохранены в %s' % bdir)


def install(game_dir):
    from ptru.build import build
    paths = game_paths(game_dir)
    bdir = os.path.join(game_dir, BACKUP_DIR)
    backup_originals(paths, bdir)
    src = {name: os.path.join(bdir, name) for name in paths}

    meta = json.load(open(os.path.join(HERE, 'translation', 'meta.json'), encoding='utf-8'))
    known = all(sha256(src[n]) == h for n, h in meta['original_sha256'].items())
    if known:
        print('Версия игры: %s (поддерживается)' % meta['game_version'])
    else:
        print('ВНИМАНИЕ: файлы игры отличаются от версии %s, для которой сделан перевод.\n'
              'Перевод будет применён по совпадению текста; часть строк может остаться на английском.'
              % meta['game_version'])

    managed = os.path.dirname(paths['Assembly-CSharp.dll'])
    with tempfile.TemporaryDirectory(dir=game_dir, prefix='_ru_build_') as tmp:
        rep = build(src['data.unity3d'], src['Assembly-CSharp.dll'], managed,
                    os.path.join(HERE, 'translation'), tmp)
        for name, dst in paths.items():
            shutil.copyfile(os.path.join(tmp, name), dst)
    print('Готово: строк в данных — %d, мест в коде — %d, расширено кнопок — %d, с авто-размером — %d.'
          % (rep['asset_strings'], rep['code_sites'], rep['widened'], rep['autosized']))
    if rep['code_missing'] or rep['ui_unused']:
        print('Не найдено в этой версии игры: строк кода — %d, строк интерфейса — %d.'
              % (len(rep['code_missing']), len(rep['ui_unused'])))
    print('Откат: python install.py "%s" --restore' % game_dir)


def install_with_mods(game_dir, force=False, mods_only=False, mod_dir=None, repo_dir=None):
    """Translation (unless --mods-only) and mods. Every refusal (unknown game version, manual mod installation, no DLLs
    and no csc, SHA256SUMS.txt mismatch) is decided by mods.prepare_install before anything is written to the game
    folder (FR-20, AC-20.4, AC-20.5)."""
    from ptru import mods
    repo_dir = repo_dir or HERE
    with open(os.path.join(repo_dir, 'translation', 'meta.json'), encoding='utf-8') as f:
        meta = json.load(f)
    if not mods_only:
        paths = game_paths(game_dir)
        require_original_dll(paths, os.path.join(game_dir, BACKUP_DIR))
    plan = mods.prepare_install(game_dir, repo_dir, meta, force=force, mods_only=mods_only, mod_dir=mod_dir)
    if not mods_only:
        install(game_dir)
    try:
        mods.install_mods(game_dir, plan)
    except (mods.ModsInstallError, mods.ModsJsonError) as e:
        if not mods_only:
            print('Перевод установлен, моды — нет.')
        sys.exit(str(e))
    print('Снять только моды (перевод останется): python install.py "%s" --remove-mods' % game_dir)


def check(repo_dir=None):
    """--check: the translation files, then the mod's content and string tables (FR-20 item 7). No game needed.
    Exit code 1 if either has an error; a repository without mod/src/PTMod (an unpacked release) skips the second part."""
    from ptru import mods
    from ptru.build import load_translation
    repo_dir = repo_dir or HERE
    ui, code, brands, errors = load_translation(os.path.join(repo_dir, 'translation'))
    for e in errors: print(e)
    print('ui.json: %d строк, code.json: %d строк, ошибок: %d' % (len(ui), len(code), len(errors)))
    report = mods.check_mod_sources(mods.mod_sources_dir(repo_dir))
    for line in report.lines(): print(line)
    sys.exit(1 if errors or report.errors else 0)


def keep_output_alive():
    """Output to a file or a pipe uses the system code page: a character missing from it must not kill the installer."""
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors='replace')
        except (AttributeError, ValueError):
            pass


def main(argv=None):
    keep_output_alive()
    ap = argparse.ArgumentParser(description='Русификатор Processor Tycoon')
    ap.add_argument('game_dir', nargs='?', help='папка игры (где лежит "Processor Tycoon.exe")')
    ap.add_argument('--restore', action='store_true',
                    help='вернуть оригинальные файлы из резервной копии (data.unity3d, Assembly-CSharp.dll) и снять моды')
    ap.add_argument('--check', action='store_true',
                    help='только проверить файлы перевода и контент мода (JSON, id, ключи ru/en, префиксы); игра не нужна')
    ap.add_argument('--with-mods', action='store_true',
                    help='установить и моды: PTMod.dll и 0Harmony.dll в Managed, записи в два JSON Unity')
    ap.add_argument('--remove-mods', action='store_true',
                    help='снять только моды: удалить PTMod.dll и 0Harmony.dll по манифесту, вернуть два JSON Unity; '
                         'перевод, сейвы и папка PTMod остаются')
    ap.add_argument('--mods-only', action='store_true',
                    help='с --with-mods: только моды; data.unity3d и Assembly-CSharp.dll не изменяются')
    ap.add_argument('--mod-dir', metavar='ПАПКА',
                    help='с --with-mods: папка с готовыми PTMod.dll и 0Harmony.dll (иначе mod\\build, затем папка '
                         'рядом с install.py, затем сборка через csc)')
    ap.add_argument('--force', action='store_true',
                    help='с --with-mods: продолжить на неизвестной версии игры, при расхождении с SHA256SUMS.txt '
                         'и поверх чужого 0Harmony.dll (на свой риск); с --remove-mods и --restore: снять моды, '
                         'поставленные без манифеста (вручную), и удалить файлы, изменённые после установки')
    a = ap.parse_args(argv)
    if a.check:
        check()
    if a.with_mods and a.restore:
        ap.error('--restore нельзя сочетать с --with-mods')
    if a.remove_mods and (a.with_mods or a.restore):
        ap.error('--remove-mods нельзя сочетать с --with-mods и --restore (--restore снимает моды сам)')
    for flag, used in (('--mods-only', a.mods_only), ('--mod-dir', a.mod_dir)):
        if used and not a.with_mods:
            ap.error('%s работает только вместе с --with-mods' % flag)
    if a.force and not (a.with_mods or a.remove_mods or a.restore):
        ap.error('--force работает только вместе с --with-mods, --remove-mods или --restore')
    game_dir = a.game_dir or input('Путь к папке игры: ').strip().strip('"')
    game_dir = find_game_dir(game_dir)
    if a.with_mods:
        from ptru import mods
        try:
            install_with_mods(game_dir, force=a.force, mods_only=a.mods_only, mod_dir=a.mod_dir)
        except (mods.ModsInstallError, mods.ModsJsonError) as e:
            sys.exit(str(e))
    elif a.remove_mods:
        remove_mods(game_dir, force=a.force)
    elif a.restore:
        restore(game_dir, force=a.force)
    else:
        install(game_dir)


if __name__ == '__main__':
    main()
