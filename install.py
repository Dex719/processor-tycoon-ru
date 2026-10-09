#!/usr/bin/env python3
"""Русификатор Processor Tycoon.

  python install.py "C:\\Games\\Processor Tycoon"            установить русский перевод
  python install.py "C:\\Games\\Processor Tycoon" --restore  вернуть оригинальные (английские) файлы
  python install.py --check                                  проверить файлы перевода (без игры)
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


def restore(game_dir):
    bdir = os.path.join(game_dir, BACKUP_DIR)
    if not os.path.isdir(bdir):
        sys.exit('Резервная копия не найдена (%s) — восстанавливать нечего.' % bdir)
    for name, dst in game_paths(game_dir).items():
        shutil.copy2(os.path.join(bdir, name), dst)
    print('Оригинальные файлы восстановлены.')


def install(game_dir):
    from ptru.patch_dll import is_patched
    from ptru.build import build
    paths = game_paths(game_dir)
    bdir = os.path.join(game_dir, BACKUP_DIR)
    if os.path.isdir(bdir):
        print('Найдена резервная копия оригинала: %s' % bdir)
    else:
        if is_patched(paths['Assembly-CSharp.dll']):
            sys.exit('Игра уже пропатчена, но резервной копии оригинала нет. Переустановите игру и запустите снова.')
        os.makedirs(bdir)
        for name, src in paths.items():
            shutil.copy2(src, os.path.join(bdir, name))
        print('Оригинальные файлы сохранены в %s' % bdir)
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


def check():
    from ptru.build import load_translation
    ui, code, brands, errors = load_translation(os.path.join(HERE, 'translation'))
    for e in errors: print(e)
    print('ui.json: %d строк, code.json: %d строк, ошибок: %d' % (len(ui), len(code), len(errors)))
    sys.exit(1 if errors else 0)


def main():
    ap = argparse.ArgumentParser(description='Русификатор Processor Tycoon')
    ap.add_argument('game_dir', nargs='?', help='папка игры (где лежит "Processor Tycoon.exe")')
    ap.add_argument('--restore', action='store_true', help='вернуть оригинальные файлы из резервной копии')
    ap.add_argument('--check', action='store_true', help='только проверить файлы перевода')
    a = ap.parse_args()
    if a.check:
        check()
    game_dir = a.game_dir or input('Путь к папке игры: ').strip().strip('"')
    game_dir = find_game_dir(game_dir)
    restore(game_dir) if a.restore else install(game_dir)


if __name__ == '__main__':
    main()
