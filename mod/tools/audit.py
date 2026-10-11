#!/usr/bin/env python3
"""Офлайн-аудит собранного мода: фичи, патчи Harmony, ресурсы (игра не запускается).

  python mod/tools/audit.py [--game-dir "<папка игры>"] [--dll <PTMod.dll>] [--quiet]

Собирает mod/tools/audit/ModAudit.cs в mod/build/tools/ModAudit.exe и запускает его над mod/build/PTMod.dll (сначала python mod/build.py).
Только чтение: из папки игры берутся DLL, на них смотрит рефлексия; код игры и мода не выполняется (кроме конструкторов классов [ModFeature]).
Проверяется:
  - каждый [ModFeature]: id уникален и совпадает с пространством имён, зависимости существуют, нет циклов;
  - каждый [ModPatch] ядра и фич: цель находится тем же поиском, что у ядра, цель не зарезервирована за другим, обработчик годится,
    привязки Harmony (__instance, __result, ___поле, имена и типы параметров цели) сходятся;
  - перекрытия: методы, которые трогает больше одной фичи (два транспайлера или два пропускающих префикса - ошибка/предупреждение);
  - встроенные ресурсы Content/ и L10n/ по фичам;
  - контент (JSON, без сборки): id событий уникальны между паками и носят префикс своей фичи, цепочки FireEvent и записи календаря ссылаются на
    существующие события, каналы объявлены в core.json, источники трат и дохода есть в перечислениях игры.
Код возврата: 0 - проблем нет, 1 - есть ERROR, 2 - не удалось запустить.
"""
import argparse
import contextlib
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.dirname(HERE)
sys.path.insert(0, MOD)
import build  # noqa: E402

# Names of the game's enums (version 0.2.3; ProcessorTycoon.ExpenseOrigin / RevenueOrigin): the content may use only these.
EXPENSE_ORIGINS = {'Factories', 'Outsourcing', 'Royalties', 'Research', 'Development', 'Construction', 'Interest', 'Fines', 'Taxes'}
REVENUE_ORIGINS = {'Sales', 'Contracts', 'Clients', 'Royalties', 'Interest'}
# the packs of the event system: (feature, file with the events)
EVENT_PACKS = (('event-system', 'events'), ('market-shocks', 'shocks'), ('design-flaw-recalls', 'events'),
               ('historical-events', 'events'), ('rival-personalities', 'events'))


def _load(*parts):
    with open(os.path.join(MOD, 'src', 'PTMod', *parts), encoding='utf-8-sig') as f:
        return json.load(f)


def _walk(node, visit, key=None):
    if isinstance(node, dict):
        for k, v in node.items():
            visit(k, v, node)
            _walk(v, visit, k)
    elif isinstance(node, list):
        for v in node:
            _walk(v, visit, key)


def audit_content():
    """Cross-checks of the shipped JSON content. Returns the list of ERROR lines (printed by the caller)."""
    problems = []
    core = _load('Content', 'mod-framework', 'core.json')
    owner_of_prefix = {}
    for feature, prefixes in core['prefixes'].items():
        for prefix in prefixes:
            owner_of_prefix[prefix] = feature
    channels = {c['name'] for c in core['channels']}

    def channel_declared(name):
        return name in channels or any(c.endswith('.*') and name.startswith(c[:-1]) for c in channels)

    ids = {}
    chains = []
    documents = []
    for feature, file in EVENT_PACKS:
        document = _load('Content', feature, file + '.json')
        documents.append((feature + '/' + file, document))
        for record in document['records']:
            event_id = record.get('id', '')
            if event_id in ids:
                problems.append('content: the event id %s is in %s and in %s/%s' % (event_id, ids[event_id], feature, file))
            ids[event_id] = feature + '/' + file
            if owner_of_prefix.get(event_id.split('.')[0]) != feature:
                problems.append('content: %s/%s: the id %s does not carry a prefix of %s (%s)' % (
                    feature, file, event_id, feature, '/'.join(core['prefixes'].get(feature, []))))

            def collect(key, value, parent, source=event_id):
                if key == 'type' and value == 'FireEvent':
                    params = parent.get('params', {})
                    chains.append((source, params.get('eventId')))
            _walk(record, collect)

    calendar = _load('Content', 'historical-events', 'calendar.json')
    documents.append(('historical-events/calendar', calendar))
    for entry in calendar['records']:
        for stage in entry.get('stages', []):
            for fire in stage.get('fires', []):
                chains.append((entry['id'], fire.get('eventId')))
        for extra in ('fallback', ):
            value = entry.get(extra)
            if isinstance(value, dict) and value.get('eventId'):
                chains.append((entry['id'] + ' (fallback)', value['eventId']))

    for source, target in chains:
        if target and target not in ids:
            problems.append('content: %s fires %s, which is not an event of any pack' % (source, target))

    challenges = _load('Content', 'scenarios-achievements', 'challenges.json')
    documents.append(('scenarios-achievements/challenges', challenges))
    for name, document in documents:
        def check(key, value, parent, where=name):
            if key == 'channel' and isinstance(value, str) and not channel_declared(value):
                problems.append('content: %s uses the channel %s, which core.json does not declare' % (where, value))
            if key == 'origin' and isinstance(value, str) and value not in EXPENSE_ORIGINS | REVENUE_ORIGINS:
                problems.append('content: %s uses the origin %s, which the game does not have' % (where, value))
        _walk(document, check)
    return problems


def main(argv=None):
    for stream in (sys.stdout, sys.stderr):
        if not stream.isatty():
            with contextlib.suppress(Exception):
                stream.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description='Офлайн-аудит PTMod.dll: фичи, патчи, ресурсы.')
    ap.add_argument('--game-dir', metavar='ПАПКА', help='папка игры (или PT_GAME_DIR); только чтение DLL')
    ap.add_argument('--dll', metavar='ПУТЬ', help='PTMod.dll для проверки (по умолчанию mod/build/PTMod.dll)')
    ap.add_argument('--csc', metavar='ПУТЬ', help='путь к Roslyn csc.exe (или PT_CSC)')
    ap.add_argument('-q', '--quiet', action='store_true', help='печатать только ERROR, WARN и итог')
    args = ap.parse_args(argv)

    layout = build.Layout(MOD)
    dll = os.path.abspath(args.dll) if args.dll else os.path.join(layout.build, 'PTMod.dll')
    try:
        if not os.path.isfile(dll):
            raise build.BuildError('Нет %s: сначала python mod/build.py.' % dll)
        csc, version = build.find_csc(args.csc)
        game = build.find_game(args.game_dir)
        build.require_harmony(layout)

        out_dir = os.path.join(layout.build, 'tools')
        os.makedirs(out_dir, exist_ok=True)
        exe = os.path.join(out_dir, 'ModAudit.exe')
        build._remove(exe)
        compile_args = ['-nologo', '-utf8output', '-codepage:65001', '-fullpaths', '-nostdlib', '-langversion:latest', '-optimize+',
                        '-target:exe', '-out:' + exe]
        compile_args += ['-r:' + r for r in build.framework_references()]
        compile_args.append(os.path.join(HERE, 'audit', 'ModAudit.cs'))
        build.compile_or_fail(csc, compile_args, build.Layout(MOD), 'ModAudit')
    except build.BuildError as e:
        print('\n[audit] ОШИБКА: %s' % e, file=sys.stderr)
        return 2

    proc = subprocess.run([exe, dll, game.managed, game.original, layout.harmony], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    text = proc.stdout.decode('utf-8', 'replace')
    for line in text.splitlines():
        if args.quiet and line.lstrip().startswith('info'):
            continue
        print(line)

    problems = audit_content()
    for line in problems:
        print('  ERROR ' + line)
    print('content: %d problem(s)' % len(problems))
    return 1 if (proc.returncode != 0 or problems) else 0


if __name__ == '__main__':
    sys.exit(main())
