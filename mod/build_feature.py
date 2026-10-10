#!/usr/bin/env python3
"""Сборка одной фичи мода отдельно от остальных: ядро + Features/<Pascal> + Api-папки зависимостей.

Нужна, когда над разными фичами работают параллельно: недописанный код соседней фичи не ломает
сборку, а выход у каждой фичи свой (mod/build/features/<slug>/), так что сборки не мешают друг другу.

  python mod/build_feature.py event-system            # PTMod.dll фичи
  python mod/build_feature.py event-system --tests    # тесты ядра и чистой логики фичи

Зависимости фич - mod/feature_deps.json ({"<slug>": ["<slug зависимости>", ...]}).
Чистая логика фичи (Features/<Pascal>/Logic/**) не ссылается на Unity и игру: её собирают тесты.
Полная сборка всего мода - по-прежнему mod/build.py.
"""
import argparse
import contextlib
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build  # noqa: E402


def pascal(slug):
    return ''.join(part[:1].upper() + part[1:] for part in slug.split('-'))


def load_deps():
    path = os.path.join(HERE, 'feature_deps.json')
    if not os.path.exists(path):
        return {}
    with open(path, encoding='utf-8') as f:
        return json.load(f)


class FeatureLayout(build.Layout):
    """Тот же репозиторий, но свой каталог результата и сгенерированных файлов."""

    def __init__(self, mod_dir, slug):
        build.Layout.__init__(self, mod_dir)
        self.build = os.path.join(self.build, 'features', slug)
        self.gen = os.path.join(self.build, 'gen')


def _files(root):
    return [os.path.join(root, *f.split('/')) for f in build._walk(root, '.cs')]


def feature_sources(layout, slug, deps):
    src = layout.src
    files = _files(os.path.join(src, 'Core'))
    own = os.path.join(src, 'Features', pascal(slug))
    if not os.path.isdir(own):
        raise build.BuildError('Нет папки фичи %s.' % own)
    files += _files(own)
    for dep in deps:
        api = os.path.join(src, 'Features', pascal(dep), 'Api')
        files += _files(api)
    return files


def feature_resources(layout, slug):
    keep = []
    for path, name in build.collect_resources(layout, release=False):
        parts = name.lower().split('/')
        if parts[0] == 'content' and parts[1] not in ('mod-framework', slug):
            continue
        if parts[0] == 'l10n' and len(parts) == 3 and parts[2] != slug + '.json':
            continue
        keep.append((path, name))
    return keep


def build_feature(layout, game, csc, slug, deps):
    version, assembly_version = build.read_version(layout)
    sources = feature_sources(layout, slug, deps)
    resources = feature_resources(layout, slug)
    sources.append(build.write_build_info(layout, version, assembly_version, False))
    os.makedirs(layout.build, exist_ok=True)
    dll = os.path.join(layout.build, 'PTMod.dll')
    build._remove(dll, os.path.join(layout.build, 'PTMod.pdb'))
    args = build.common_args(False, ['PTMOD_DEBUG'])
    args += ['-target:library', '-out:' + dll]
    args += ['-r:' + r for r in build.game_references(game, layout)]
    args += ['-resource:%s,%s' % (path, name) for path, name in resources]
    args += sources
    build.say('фича %s (+api: %s): %d файл(ов), %d ресурс(ов)' % (slug, ', '.join(deps) or '-', len(sources) - 1, len(resources)))
    build.compile_or_fail(csc, args, layout, 'PTMod-' + slug)
    build.say('готово: %s (%s)' % (dll, build._kb(dll)))
    return dll


def build_feature_tests(layout, csc, slug):
    src = layout.src
    files = []
    for folder in (os.path.join(src, 'Core', 'Logic'), os.path.join(src, 'Core', 'Compat'),
                   os.path.join(src, 'Features', pascal(slug), 'Logic')):
        files += _files(folder)
    for f in build._walk(layout.tests, '.cs'):
        parts = f.split('/')
        if parts[0] == 'Features' and len(parts) > 2 and parts[1] != pascal(slug):
            continue   # тесты других фич: их логика в эту сборку не входит
        files.append(os.path.join(layout.tests, *parts))
    os.makedirs(layout.build, exist_ok=True)
    exe = os.path.join(layout.build, build.TESTS_ASSEMBLY + '.exe')
    build._remove(exe, os.path.join(layout.build, build.TESTS_ASSEMBLY + '.pdb'))
    args = build.common_args(False, ['PTMOD_TESTS', 'PTMOD_DEBUG'])
    args += ['-target:exe', '-main:' + build.TESTS_MAIN, '-out:' + exe]
    args += ['-r:' + r for r in build.framework_references()]
    args += files
    build.say('тесты фичи %s: %d файл(ов)' % (slug, len(files)))
    build.compile_or_fail(csc, args, layout, 'tests-' + slug)
    return exe


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        if not stream.isatty():
            with contextlib.suppress(Exception):
                stream.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description='Сборка одной фичи: ядро + фича + Api зависимостей.')
    ap.add_argument('slug', help='slug фичи, например event-system')
    ap.add_argument('--tests', action='store_true', help='собрать и запустить тесты ядра и чистой логики фичи')
    ap.add_argument('--filter', action='append', default=[], metavar='ТЕКСТ')
    ap.add_argument('-v', '--verbose', action='store_true')
    ap.add_argument('--game-dir', metavar='ПАПКА')
    ap.add_argument('--csc', metavar='ПУТЬ')
    args = ap.parse_args(argv if argv is not None else sys.argv[1:])

    layout = FeatureLayout(HERE, args.slug)
    deps = load_deps().get(args.slug, [])
    try:
        csc, csc_version = build.find_csc(args.csc)
        build.say('csc %s (%s)' % (csc_version, csc))
        if args.tests:
            exe = build_feature_tests(layout, csc, args.slug)
            return build.run_tests(layout, exe, True, False, args.filter, args.verbose)
        game = build.find_game(args.game_dir)
        build_feature(layout, game, csc, args.slug, deps)
        return 0
    except build.BuildError as e:
        print('\n[build_feature] ОШИБКА: %s' % e, file=sys.stderr, flush=True)
        return e.code


if __name__ == '__main__':
    sys.exit(main())
