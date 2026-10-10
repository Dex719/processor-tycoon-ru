#!/usr/bin/env python3
"""Сводка публичного API ядра мода (mod/src/PTMod/Core) в mod/docs/CORE_API.md.

Для авторов фич: публичные типы и члены с их первой строкой <summary>, без тел методов.
  python mod/tools/core_api_digest.py
"""
import os
import re

MOD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CORE = os.path.join(MOD, 'src', 'PTMod', 'Core')
OUT = os.path.join(MOD, 'docs', 'CORE_API.md')

TYPE = re.compile(r'^\s*public\s+(?:(?:static|sealed|abstract|partial|readonly|ref)\s+)*'
                  r'(class|struct|interface|enum|record(?:\s+struct)?|delegate)\s+(.*)$')
MEMBER = re.compile(r'^\s*(public|protected)\s+(?!class\b|struct\b|interface\b|enum\b|record\b)(.+?)\s*(\{.*|=>.*|;)?\s*$')
SUMMARY = re.compile(r'<summary>(.*?)(</summary>|$)')
NS = re.compile(r'^\s*namespace\s+([\w.]+)')


def clean(sig):
    sig = re.sub(r'\s+', ' ', sig).strip()
    return re.sub(r'\s*(\{|=>).*$', '', sig).rstrip(';').strip()


def digest(path):
    ns, out, summary, depth_type = None, [], None, 0
    lines = open(path, encoding='utf-8-sig').read().splitlines()
    for i, line in enumerate(lines):
        m = NS.match(line)
        if m:
            ns = m.group(1)
            continue
        s = line.strip()
        if s.startswith('///'):
            m = SUMMARY.search(s)
            if m and m.group(1).strip():
                summary = m.group(1).strip()
            elif summary is None and not s.startswith('/// <') and len(s) > 4:
                summary = s[3:].strip()
            continue
        if s.startswith('['):
            continue
        m = TYPE.match(line)
        if m:
            out.append(('type', line[:len(line) - len(line.lstrip())], clean(m.group(1) + ' ' + m.group(2)), summary))
            summary = None
            continue
        m = MEMBER.match(line)
        if m and '(' in line or (m and ('{ get' in line or ';' in line or '=>' in line)):
            if re.match(r'^\s*(public|protected)\s+(override\s+)?(string|bool)\s+(ToString|Equals)\b', line):
                summary = None
                continue
            out.append(('member', line[:len(line) - len(line.lstrip())], clean(m.group(2)), summary))
        summary = None
    return ns, out


def main():
    files = []
    for base, dirs, names in os.walk(CORE):
        dirs.sort()
        for n in sorted(names):
            if n.endswith('.cs'):
                files.append(os.path.join(base, n))
    parts = ['# Публичный API ядра PTMod', '',
             'Сгенерировано `mod/tools/core_api_digest.py` из `mod/src/PTMod/Core`. Только публичные типы и члены;',
             'за деталями поведения смотри исходник (путь в заголовке) и `.kiro/specs/mod-framework/design.md`.', '']
    total = 0
    for path in files:
        ns, items = digest(path)
        if not items:
            continue
        rel = os.path.relpath(path, MOD).replace(os.sep, '/')
        parts.append('## `%s`%s' % (rel, ' — `%s`' % ns if ns else ''))
        parts.append('')
        parts.append('```csharp')
        for kind, indent, sig, summary in items:
            pad = '    ' if kind == 'member' else ''
            parts.append('%s%s%s' % (pad, sig, '   // ' + summary if summary else ''))
            total += 1
        parts.append('```')
        parts.append('')
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(parts) + '\n')
    print('%s: %d файл(ов), %d объявлений' % (OUT, len(files), total))


if __name__ == '__main__':
    main()
