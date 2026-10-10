#!/usr/bin/env python3
"""Проверка ссылок в спеках .kiro/specs/**/*.md (mod-framework, FR-22, TSK-047).

  python tools/check_spec_refs.py                          ссылки `Class.Member` на код игры (pt-modding/code)
  python tools/check_spec_refs.py --code <папка>           другая папка с псевдо-C# игры
  python tools/check_spec_refs.py --specs <папка|файл> ... другие спеки (по умолчанию .kiro/specs); можно без --specs
  python tools/check_spec_refs.py --core-api [design.md]   ссылки `ModX.Член` на API ядра из design.md mod-framework

Режим по умолчанию (AC-22.1): в обратных кавычках ищутся `Class.Member`, где Class - класс из --code.
  Член должен быть методом, свойством (в том числе get_X/set_X), полем, событием, членом перечисления или
  вложенным типом (CpuBase.MarketData.Stock); учитываются базовые типы и интерфейсы из того же кода.
  Классы, которых нет в --code, имена файлов (*.log, *.dll, *.json, *.txt, *.md, *.py, ...) и сущности мода
  (Mod*, PTMod*) пропускаются. Шаблон `Market.Update*` означает «член с таким началом имени».
  Сообщение: файл:строка: метод не найден: Market.Foo (в Market есть: похожие имена).
Режим --core-api (AC-22.3): из блоков ```csharp в design.md собираются публичные имена API ядра (типы, методы,
  свойства, поля, константы, события, члены перечислений, вложенные типы); в спеках фич (каталог mod-framework
  исключён) проверяются ссылки на эти типы и на неизвестные ModX. Пропускаются шаблоны (`ModX.Member`,
  `TickPhase.X`) и типы, которые спека объявляет у себя в блоке csharp (у cpu-architectures свой DesignContext).
  Для известных ошибок есть подсказка: файл:строка: нет в API ядра (используйте FeatureContext.OnTick(TickPhase.DayPassed)).

Блоки кода (```...```) в спеках не проверяются: это примеры, а не ссылки. Цепочки вида `cpu.Market.Foo` (с переменной
в начале) не проверяются. Рядом со спеками тест: mod/tests/installer/test_check_spec_refs.py.
Код возврата: 0 - расхождений нет, 1 - есть расхождения, 2 - ошибка запуска (нет папки, файла).
Инструмент только читает файлы; зависимостей кроме стандартной библиотеки нет.
"""
import argparse
import difflib
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_SPECS = os.path.join(REPO_ROOT, '.kiro', 'specs')
# pt-modding lives next to the repository (not committed); the second candidate is the owner's machine layout.
_CODE_CANDIDATES = (
    os.path.join(os.path.dirname(REPO_ROOT), 'pt-modding', 'code'),
    r'C:\Users\gacha\Desktop\New folder\pt-modding\code',
)
CORE_SPEC_DIR = 'mod-framework'          # excluded from the --core-api check (it defines the API)
DEFAULT_DESIGN = os.path.join(DEFAULT_SPECS, CORE_SPEC_DIR, 'design.md')


def default_code_dir():
    for cand in _CODE_CANDIDATES:
        if os.path.isdir(cand):
            return cand
    return _CODE_CANDIDATES[0]


# --------------------------------------------------------------------------------------------------
# C# subset tokenizer and declaration parser
# (reads both the decompiled pseudo-C# of the game and the ```csharp blocks of design.md)
# --------------------------------------------------------------------------------------------------

_TOKEN = re.compile(r'''
    (?P<ws>\s+)
  | (?P<lc>//[^\n]*)
  | (?P<bc>/\*.*?\*/)
  | (?P<lit>@"(?:[^"]|"")*"|\$?"(?:[^"\\\n]|\\.)*"|'(?:[^'\\\n]|\\.){1,8}')
  | (?P<gen>(?<![\w><])<[\w`.]*>\w*)               # compiler-generated name: <>c, <Start>b__20_0
  | (?P<id>[^\W\d]\w*)
  | (?P<num>\d[\w.]*)
  | (?P<op>=>|::|[{}()\[\];,.<>=:?*&|!+\-/%^~@\#])
  | (?P<other>.)
''', re.X | re.S)

_TYPE_KW = frozenset(('class', 'struct', 'enum', 'interface', 'record'))
# words that are never member names (they show up as "last identifier" in malformed or initializer fragments)
_NOT_NAMES = frozenset((
    'int', 'uint', 'long', 'ulong', 'short', 'ushort', 'byte', 'sbyte', 'float', 'double', 'decimal', 'bool', 'char',
    'string', 'object', 'void', 'var', 'dynamic', 'new', 'null', 'true', 'false', 'default', 'typeof', 'this', 'base',
    'ref', 'out', 'in', 'params', 'return', 'if', 'else', 'for', 'foreach', 'while', 'do', 'switch', 'case', 'break',
    'continue', 'goto', 'try', 'catch', 'finally', 'throw', 'using', 'lock', 'is', 'as', 'where', 'public', 'private',
    'protected', 'internal', 'static', 'readonly', 'const', 'virtual', 'override', 'abstract', 'sealed', 'extern',
    'unsafe', 'volatile', 'async', 'partial', 'namespace', 'class', 'struct', 'enum', 'interface', 'event', 'delegate',
    'operator', 'implicit', 'explicit',
))
_MODIFIERS = frozenset((
    'public', 'private', 'protected', 'internal', 'static', 'readonly', 'const', 'virtual', 'override', 'abstract',
    'sealed', 'extern', 'unsafe', 'volatile', 'async', 'partial', 'new', 'required',
))
_STATEMENT_KW = frozenset(('if', 'for', 'foreach', 'while', 'switch', 'return', 'using', 'lock', 'throw', 'catch', 'try',
                           'else', 'do', 'goto', 'break', 'continue', 'yield'))


def tokenize(text):
    """Return a list of (kind, text): kind 'i' identifier/keyword, 'o' punctuation, 'l' literal or junk."""
    out = []
    for m in _TOKEN.finditer(text):
        k = m.lastgroup
        if k in ('ws', 'lc', 'bc'):
            continue
        if k in ('id', 'gen'):
            out.append(('i', m.group()))
        elif k == 'op':
            out.append(('o', m.group()))
        else:
            out.append(('l', m.group()))
    return out


class TypeInfo:
    """A class/struct/enum/interface: member names, nested types and (raw) base types."""
    __slots__ = ('name', 'kind', 'parent', 'namespace', 'members', 'nested', 'bases')

    def __init__(self, name, kind, parent, namespace):
        self.name = name
        self.kind = kind
        self.parent = parent
        self.namespace = namespace
        self.members = {}        # name -> 'method' | 'property' | 'field' | 'event' | 'enum' | 'delegate'
        self.nested = {}         # simple name -> TypeInfo
        self.bases = []          # each base is a list of identifiers (dotted path, generic arguments dropped)

    def add(self, name, kind):
        if not name or name in _NOT_NAMES:
            return
        self.members.setdefault(name, kind)
        # property/event accessors are visible to reflection under these names and specs cite them (get_Instance)
        if kind == 'property':
            self.members.setdefault('get_' + name, 'method')
            self.members.setdefault('set_' + name, 'method')
        elif kind == 'event':
            self.members.setdefault('add_' + name, 'method')
            self.members.setdefault('remove_' + name, 'method')
        elif kind == 'method':
            for prefix, alias_kind in (('get_', 'property'), ('set_', 'property'), ('add_', 'event'), ('remove_', 'event')):
                if name.startswith(prefix) and len(name) > len(prefix):
                    self.members.setdefault(name[len(prefix):], alias_kind)


class TypeIndex:
    """Top-level types by simple name (nested ones hang off their parents) and the set of known namespaces."""

    def __init__(self):
        self.types = {}
        self.ns_prefixes = set()
        self.unreadable = []

    def add_namespace(self, ns):
        parts = ns.split('.') if ns else []
        for k in range(1, len(parts) + 1):
            self.ns_prefixes.add('.'.join(parts[:k]))

    def merge_text(self, text, namespace=''):
        _Parser(tokenize(text), self, namespace).run()


class _Parser:
    def __init__(self, toks, index, namespace):
        self.t = toks
        self.n = len(toks)
        self.i = 0
        self.index = index
        self.ns = namespace

    def run(self):
        self.parse_scope(None, self.ns, False)

    # ---- helpers ----
    def skip_group(self, i, opener, closer):
        """i is at an opener; return the index right after its matching closer (or the end)."""
        depth = 0
        t, n = self.t, self.n
        while i < n:
            k, s = t[i]
            if k == 'o':
                if s == opener:
                    depth += 1
                elif s == closer:
                    depth -= 1
                    if depth == 0:
                        return i + 1
            i += 1
        return n

    def skip_to_semicolon(self, i):
        par = 0
        t, n = self.t, self.n
        while i < n:
            k, s = t[i]
            if k == 'o':
                if s in ('(', '[', '{'):
                    par += 1
                elif s in (')', ']'):
                    par -= 1
                elif s == '}':
                    if par == 0:
                        return i              # closer of the enclosing body: leave it to the caller
                    par -= 1
                elif s == ';' and par <= 0:
                    return i + 1
            i += 1
        return n

    # ---- scopes ----
    def parse_scope(self, owner, ns, in_block):
        t = self.t
        while self.i < self.n:
            k, s = t[self.i]
            if k == 'o' and s == '}':
                self.i += 1
                if in_block:
                    return
                continue                      # stray closer in a loose snippet
            if k == 'o' and s == ';':
                self.i += 1
                continue
            before = self.i
            self.parse_decl(owner, ns)
            if self.i == before:
                self.i += 1

    def parse_decl(self, owner, ns):
        t, n = self.t, self.n
        i = self.i
        while i < n and t[i] == ('o', '['):                 # attributes
            i = self.skip_group(i, '[', ']')
        start = i
        par = 0
        seen_eq = False
        block = -1
        term = None
        j = i
        while j < n:
            k, s = t[j]
            if k == 'o':
                if s in ('(', '['):
                    par += 1
                elif s in (')', ']'):
                    par = max(0, par - 1)
                elif par == 0:
                    if s == ';' or s == '}':
                        term = s
                        break
                    if s == '{':
                        if seen_eq:                         # initializer expression, e.g. new int[] { 1 }
                            j = self.skip_group(j, '{', '}')
                            continue
                        block = j
                        break
                    if s in ('=', '=>'):
                        seen_eq = True
            j += 1
        head = t[start:j]
        kw = self.find_type_kw(head)
        if block >= 0:
            if kw >= 0:
                ti = self.make_type(head, kw, owner, ns)
                self.i = block + 1
                if ti is None:
                    self.i = self.skip_group(block, '{', '}')
                elif ti.kind == 'enum':
                    self.parse_enum(ti)
                else:
                    self.parse_scope(ti, ti.namespace, True)
                return
            if owner is None:
                if head and head[0] == ('i', 'namespace'):
                    sub = self.dotted_name(head, 1)
                    self.index.add_namespace(self.join_ns(ns, sub))
                    self.i = block + 1
                    self.parse_scope(None, self.join_ns(ns, sub), True)
                    return
                self.i = self.skip_group(block, '{', '}')   # loose snippet: skip
                return
            self.record_member(owner, head, True)
            self.i = self.skip_group(block, '{', '}')
            if self.i < n and t[self.i] == ('o', '='):       # property initializer: { get; set; } = 400;
                self.i = self.skip_to_semicolon(self.i)
            elif self.i < n and t[self.i] == ('o', ';'):
                self.i += 1
            return
        if owner is None:
            if head and head[0] == ('i', 'namespace'):         # file-scoped namespace
                self.ns = self.join_ns(ns, self.dotted_name(head, 1))
                self.index.add_namespace(self.ns)
                self.i = j + 1 if term == ';' else j
                self.parse_scope(None, self.ns, False)
                return
            self.i = j + 1 if term == ';' else (j if term == '}' else n)
            return
        if head:
            self.record_member(owner, head, False)
        if term == ';':
            self.i = j + 1
        else:
            self.i = j                                      # '}' of the enclosing body or end of input

    @staticmethod
    def join_ns(ns, sub):
        return '%s.%s' % (ns, sub) if ns and sub else (sub or ns)

    @staticmethod
    def dotted_name(head, start):
        parts = []
        for k, s in head[start:]:
            if k == 'i':
                parts.append(s)
            elif s != '.':
                break
        return '.'.join(parts)

    @staticmethod
    def find_type_kw(head):
        for idx, (k, s) in enumerate(head):
            if k == 'o' and s in ('(', '='):
                return -1
            if k == 'i' and s in _TYPE_KW and idx + 1 < len(head) and head[idx + 1][0] == 'i':
                if idx > 0 and head[idx - 1] == ('o', '.'):
                    continue
                return idx
        return -1

    # ---- types ----
    def make_type(self, head, kw, owner, ns):
        kind = head[kw][1]
        k = kw + 1
        while k < len(head) and head[k][0] == 'i' and head[k][1] in _TYPE_KW:
            k += 1
        if k >= len(head) or head[k][0] != 'i':
            return None
        name = head[k][1]
        k += 1
        if k < len(head) and head[k] == ('o', '<'):                 # generic parameters
            depth = 0
            while k < len(head):
                if head[k] == ('o', '<'):
                    depth += 1
                elif head[k] == ('o', '>'):
                    depth -= 1
                    if depth == 0:
                        k += 1
                        break
                k += 1
        bases = []
        if kind != 'enum' and k < len(head) and head[k] == ('o', ':'):
            cur, ang = [], 0
            for kk, s in head[k + 1:]:
                if kk == 'i' and s == 'where' and ang == 0:
                    break
                if kk == 'o' and s == '<':
                    ang += 1
                elif kk == 'o' and s == '>':
                    ang = max(0, ang - 1)
                elif ang == 0:
                    if kk == 'i':
                        cur.append(s)
                    elif kk == 'o' and s == ',':
                        if cur:
                            bases.append(cur)
                        cur = []
            if cur:
                bases.append(cur)
        container = owner.nested if owner is not None else self.index.types
        ti = container.get(name)
        if ti is None:
            ti = TypeInfo(name, kind, owner, ns)
            container[name] = ti
        ti.bases.extend(bases)
        if owner is None:
            self.index.add_namespace(ns)
        return ti

    def parse_enum(self, ti):
        t, n = self.t, self.n
        while self.i < n:
            while self.i < n and t[self.i] == ('o', '['):
                self.i = self.skip_group(self.i, '[', ']')
            if self.i >= n:
                return
            k, s = t[self.i]
            if k == 'o' and s == '}':
                self.i += 1
                return
            if k == 'o' and s == ',':
                self.i += 1
                continue
            if k == 'i':
                ti.add(s, 'enum')
            self.i += 1
            par = 0
            while self.i < n:                                      # skip "= value" up to the next ',' or the closing '}'
                k, s = t[self.i]
                if k == 'o':
                    if s in ('(', '[', '{'):
                        par += 1
                    elif s in (')', ']'):
                        par -= 1
                    elif s == '}':
                        if par <= 0:
                            break
                        par -= 1
                    elif s == ',' and par <= 0:
                        break
                self.i += 1

    # ---- members ----
    @staticmethod
    def name_before(head, lp):
        """Identifier right before the parameter list at head[lp]; handles Name<T>( and explicit interface impls."""
        idx = lp - 1
        if idx >= 0 and head[idx] == ('o', '>'):
            depth = 0
            while idx >= 0:
                if head[idx] == ('o', '>'):
                    depth += 1
                elif head[idx] == ('o', '<'):
                    depth -= 1
                    if depth == 0:
                        idx -= 1
                        break
                idx -= 1
        if idx < 0 or head[idx][0] != 'i':
            return None, False
        qualified = idx > 0 and head[idx - 1] == ('o', '.')
        return head[idx][1], qualified

    def record_member(self, owner, head, has_block):
        if not head:
            return
        lp = next((x for x, (k, s) in enumerate(head) if k == 'o' and s == '('), -1)
        eq = next((x for x, (k, s) in enumerate(head) if k == 'o' and s in ('=', '=>')), -1)
        limit = min([v for v in (lp, eq) if v >= 0] or [len(head)])
        pre = [s for _, s in head[:limit]]
        if 'operator' in pre:
            return
        if 'delegate' in pre:
            if lp > 0:
                name, _ = self.name_before(head, lp)
                owner.add(name, 'delegate')
            return
        if 'event' in pre:
            self.record_declarators(owner, head[pre.index('event') + 1:], 'event', need_type=True)
            return
        if lp >= 0 and (eq < 0 or lp < eq):
            if head[0][0] == 'i' and head[0][1] in _STATEMENT_KW:
                return
            name, qualified = self.name_before(head, lp)
            if name is None or name == owner.name:                     # constructor or garbage
                return
            if qualified and name in ('ctor', 'cctor'):
                return
            owner.add(name, 'method')
            return
        if has_block:
            if 'this' in pre:                                          # indexer
                return
            ids = [s for k, s in head[:limit] if k == 'i']
            if len(ids) >= 2 or (ids and ids[-1] not in _MODIFIERS):
                owner.add(ids[-1], 'property')
            return
        if eq >= 0 and head[eq][1] == '=>':
            ids = [s for k, s in head[:eq] if k == 'i']
            if ids:
                owner.add(ids[-1], 'property')
            return
        self.record_declarators(owner, head, 'field', need_type=True)

    @staticmethod
    def record_declarators(owner, toks, kind, need_type):
        """Split 'T a, b = 1, c' into declarators (commas inside <> () [] {} do not split) and record each name."""
        segs, cur, ang, par, in_init = [], [], 0, 0, False
        for tok in toks:
            k, s = tok
            if k == 'o':
                if not in_init:
                    if s == '<':
                        ang += 1
                    elif s == '>':
                        ang = max(0, ang - 1)
                    elif s in ('=', '=>'):
                        in_init = True
                if s in ('(', '[', '{'):
                    par += 1
                elif s in (')', ']', '}'):
                    par = max(0, par - 1)
                elif s == ',' and par == 0 and ang == 0:
                    segs.append(cur)
                    cur, in_init = [], False
                    continue
            cur.append(tok)
        segs.append(cur)
        for pos, seg in enumerate(segs):
            cut = next((x for x, (k, s) in enumerate(seg) if k == 'o' and s in ('=', '=>')), len(seg))
            ids = [s for k, s in seg[:cut] if k == 'i']
            if not ids:
                continue
            if pos == 0 and need_type and len([s for s in ids if s not in _MODIFIERS]) < 2:
                continue                                              # no type before the name: not a declaration
            owner.add(ids[-1], kind)


# --------------------------------------------------------------------------------------------------
# Name resolution with inheritance
# --------------------------------------------------------------------------------------------------

_SYSTEM_OBJECT = frozenset('ToString Equals GetHashCode GetType ReferenceEquals MemberwiseClone Finalize'.split())
_UNITY_OBJECT = frozenset(
    'name hideFlags GetInstanceID Destroy DestroyImmediate DontDestroyOnLoad Instantiate FindObjectOfType '
    'FindObjectsOfType FindFirstObjectByType FindAnyObjectByType FindObjectsByType FindObjectsOfTypeAll '
    'CreateInstance'.split())
_UNITY_COMPONENT = frozenset(
    'gameObject transform tag GetComponent GetComponents GetComponentInChildren GetComponentsInChildren '
    'GetComponentInParent GetComponentsInParent TryGetComponent CompareTag SendMessage SendMessageUpwards '
    'BroadcastMessage'.split())
_UNITY_BEHAVIOUR = frozenset('enabled isActiveAndEnabled'.split())
_UNITY_MONO = frozenset(
    'useGUILayout runInEditMode StartCoroutine StopCoroutine StopAllCoroutines Invoke InvokeRepeating CancelInvoke '
    'IsInvoking print destroyCancellationToken'.split())
_ENUM_BASE = frozenset('CompareTo HasFlag GetTypeCode GetName GetNames GetValues GetUnderlyingType IsDefined Parse '
                       'TryParse Format ToObject'.split())

_MONO = _SYSTEM_OBJECT | _UNITY_OBJECT | _UNITY_COMPONENT | _UNITY_BEHAVIOUR | _UNITY_MONO
# Bases that live outside the analysed code but whose members are known. Any other unresolved class base makes the
# type "open" (members cannot be verified, anything is accepted); unresolved interfaces (IFoo) are ignored.
FRAMEWORK_BASES = {
    'Object': _SYSTEM_OBJECT,
    'ValueType': _SYSTEM_OBJECT,
    'Attribute': _SYSTEM_OBJECT,
    'EventArgs': _SYSTEM_OBJECT,
    'Exception': _SYSTEM_OBJECT | frozenset(('Message', 'StackTrace', 'InnerException', 'Data', 'Source', 'HelpLink')),
    'Enum': _SYSTEM_OBJECT | _ENUM_BASE,
    'ScriptableObject': _SYSTEM_OBJECT | _UNITY_OBJECT,
    'Component': _SYSTEM_OBJECT | _UNITY_OBJECT | _UNITY_COMPONENT,
    'Behaviour': _SYSTEM_OBJECT | _UNITY_OBJECT | _UNITY_COMPONENT | _UNITY_BEHAVIOUR,
    'MonoBehaviour': _MONO,
}
_INTERFACE_NAME = re.compile(r'^I[A-Z]')


class Resolver:
    def __init__(self, index):
        self.index = index
        self._closure = {}

    # -- types by path --
    def find_type(self, path, scope=None):
        """Resolve a dotted type path relative to scope (a TypeInfo) or from the top level."""
        if not path:
            return None
        if len(path) == 1:
            p = scope
            while p is not None:
                if path[0] in p.nested:
                    return p.nested[path[0]]
                p = p.parent
            return self.index.types.get(path[0])
        segs = self.strip_namespace(path)
        ti = self.index.types.get(segs[0])
        if ti is None:
            return self.index.types.get(path[-1]) if len(segs) == len(path) else None
        for name in segs[1:]:
            ti = ti.nested.get(name)
            if ti is None:
                return None
        return ti

    def strip_namespace(self, segs):
        k = 0
        while k < len(segs) - 1 and '.'.join(segs[:k + 1]) in self.index.ns_prefixes and segs[k] not in self.index.types:
            k += 1
        return segs[k:]

    # -- inheritance --
    def closure(self, ti):
        """(types in lookup order, framework member names, is_open)."""
        key = id(ti)
        cached = self._closure.get(key)
        if cached is not None:
            return cached
        chain, fw, is_open = [], set(), False
        seen = set()
        stack = [ti]
        while stack:
            cur = stack.pop(0)
            if id(cur) in seen:
                continue
            seen.add(id(cur))
            chain.append(cur)
            for path in cur.bases:
                base = self.find_type(path, cur.parent)
                if base is cur:                       # nested "Market : Market" means the top-level class
                    base = self.index.types.get(path[-1])
                    if base is cur:
                        base = None
                if base is not None:
                    stack.append(base)
                elif path[-1] in FRAMEWORK_BASES:
                    fw |= FRAMEWORK_BASES[path[-1]]
                elif not _INTERFACE_NAME.match(path[-1]):
                    is_open = True
        fw |= _SYSTEM_OBJECT
        result = (chain, fw, is_open)
        self._closure[key] = result
        return result

    def find_member(self, ti, name):
        """('type', TypeInfo) | ('member', kind) | ('open', None) | None."""
        chain, fw, is_open = self.closure(ti)
        for c in chain:
            if name in c.nested:
                return 'type', c.nested[name]
        for c in chain:
            if name in c.members:
                return 'member', c.members[name]
        if name in fw:
            return 'member', 'method'
        if is_open:
            return 'open', None
        return None

    def has_prefix(self, ti, prefix):
        chain, fw, is_open = self.closure(ti)
        if is_open:
            return True
        return any(m.startswith(prefix) for c in chain for m in list(c.members) + list(c.nested)) or \
            any(m.startswith(prefix) for m in fw)

    def member_names(self, ti):
        """Names to offer as suggestions: declared members and nested types, without accessor aliases."""
        chain, _, _ = self.closure(ti)
        names = []
        for c in chain:
            for m in list(c.members) + list(c.nested):
                if m.startswith(('get_', 'set_', 'add_', 'remove_', '<')) or '<' in m or m in names:
                    continue
                names.append(m)
        return names

    def check_chain(self, segs, wildcard=False):
        """Check `Cls.A.B...`: returns None when fine, else (status, owner TypeInfo or None, missing name)."""
        segs = self.strip_namespace(segs)
        cur = self.index.types.get(segs[0])
        if cur is None:
            return 'no-class', None, segs[0]
        for pos in range(1, len(segs)):
            name = segs[pos]
            last = pos == len(segs) - 1
            if last and wildcard:
                return None if self.has_prefix(cur, name) else ('missing', cur, name + '*')
            found = self.find_member(cur, name)
            if found is None:
                return 'missing', cur, name
            kind, payload = found
            if kind == 'type' and not last:
                nxt = payload
                if self.find_member(nxt, segs[pos + 1]) is None:
                    # also a plain member of the same name (property of that nested type) - accept
                    chain, _, _ = self.closure(cur)
                    if any(name in c.members for c in chain):
                        return None
                cur = nxt
                continue
            return None            # member, nested type as the last segment, or open type
        return None


# --------------------------------------------------------------------------------------------------
# Loading the indexes
# --------------------------------------------------------------------------------------------------

_NS_HEADER = re.compile(r'^\s*//\s*Namespace:\s*(\S+)', re.M)


def read_text(path):
    with open(path, 'rb') as f:
        data = f.read()
    text = data.decode('utf-8-sig', errors='replace')
    return text.replace('\r\n', '\n').replace('\r', '\n')


def build_game_index(code_dir):
    """Parse every *.cs under code_dir (decompiled pseudo-C#) into a TypeIndex."""
    index = TypeIndex()
    for root, dirs, files in os.walk(code_dir):
        dirs.sort()
        for name in sorted(files):
            if not name.endswith('.cs'):
                continue
            path = os.path.join(root, name)
            try:
                text = read_text(path)
            except OSError as e:
                index.unreadable.append('%s: %s' % (path, e))
                continue
            m = _NS_HEADER.search(text)
            ns = m.group(1) if m else ''
            if ns == '(global)':
                ns = ''
            index.merge_text(text, ns)
    return index


_FENCE_OPEN = re.compile(r'^\s*(`{3,}|~{3,})([^`]*)$')
CSHARP_LANGS = ('csharp', 'cs', 'c#')


def iter_code_blocks(text, langs):
    """Yield the bodies of fenced blocks whose info string is in langs."""
    lines = text.split('\n')
    fence = None
    buf = []
    take = False
    for line in lines:
        if fence is None:
            m = _FENCE_OPEN.match(line)
            if m:
                fence = m.group(1)
                info = m.group(2).split()
                take = bool(info) and info[0].lower() in langs
                buf = []
            continue
        stripped = line.strip()
        if stripped.startswith(fence[0] * len(fence)) and set(stripped) == {fence[0]}:
            if take:
                yield '\n'.join(buf)
            fence = None
            continue
        if take:
            buf.append(line)


def build_core_index(design_path):
    """Public API of the core: types declared in the ```csharp blocks of design.md."""
    index = TypeIndex()
    text = read_text(design_path)
    for block in iter_code_blocks(text, CSHARP_LANGS):
        index.merge_text(block)
    return index


# --------------------------------------------------------------------------------------------------
# Markdown scanning
# --------------------------------------------------------------------------------------------------

_SPAN = re.compile(r'(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)')
_CHAIN = re.compile(r'(?<![\w.\\/@#$%~])([^\W\d]\w*(?:\.[^\W\d]\w*)+)(\*|…|\.\.\.)?')
# extensions of file names that look like Class.Member
FILE_EXTENSIONS = frozenset((
    'log', 'dll', 'json', 'txt', 'md', 'py', 'cs', 'csv', 'exe', 'png', 'jpg', 'jpeg', 'gif', 'xml', 'yaml', 'yml',
    'bak', 'ini', 'cfg', 'html', 'js', 'ps1', 'bat', 'sh', 'zip', 'unity3d', 'assets', 'tmp', 'lock', 'probe',
    'toml', 'resS', 'resource',
))
_MOD_ENTITY = re.compile(r'^(PTMod|Mod[A-Z])')


def iter_citations(text):
    """Yield (line_no, segments, wildcard) for every dotted chain inside inline code spans (fenced blocks skipped)."""
    fence = None
    for no, raw in enumerate(text.split('\n'), 1):
        line = raw.rstrip('\r')
        if fence is not None:
            stripped = line.strip()
            if stripped.startswith(fence[0] * len(fence)) and set(stripped) == {fence[0]}:
                fence = None
            continue
        m = _FENCE_OPEN.match(line)
        if m:
            fence = m.group(1)
            continue
        seen = set()
        for span in _SPAN.finditer(line):
            body = span.group(2)
            for ch in _CHAIN.finditer(body):
                segs = ch.group(1).split('.')
                if segs[-1] in FILE_EXTENSIONS:
                    continue
                key = (ch.group(1), bool(ch.group(2)))
                if key in seen:
                    continue
                seen.add(key)
                yield no, segs, bool(ch.group(2))


def collect_spec_files(paths, exclude_dir=None):
    """Expand files and folders into a sorted list of .md files; exclude_dir skips folders with that name."""
    result = []
    for p in paths:
        if os.path.isfile(p):
            if exclude_dir and os.path.basename(os.path.dirname(os.path.abspath(p))) == exclude_dir:
                continue
            result.append(p)
            continue
        for root, dirs, files in os.walk(p):
            dirs[:] = sorted(d for d in dirs if d != exclude_dir)
            for name in sorted(files):
                if name.lower().endswith('.md'):
                    result.append(os.path.join(root, name))
    return result


def display_path(path):
    full = os.path.abspath(path)
    try:
        rel = os.path.relpath(full, REPO_ROOT)
        if not rel.startswith('..') and not os.path.isabs(rel):
            full = rel
    except ValueError:
        pass
    return full.replace('\\', '/')


class Finding:
    __slots__ = ('path', 'line', 'message')

    def __init__(self, path, line, message):
        self.path = path
        self.line = line
        self.message = message

    def __str__(self):
        return '%s:%d: %s' % (display_path(self.path), self.line, self.message)


def _suggest(name, candidates, limit=3):
    low = {c.lower(): c for c in candidates}
    out = []
    if name.lower() in low and low[name.lower()] != name:
        out.append(low[name.lower()])
    for c in difflib.get_close_matches(name, candidates, n=limit, cutoff=0.7):
        if c not in out:
            out.append(c)
    return out[:limit]


# --------------------------------------------------------------------------------------------------
# Mode 1: Class.Member against the game code (AC-22.1)
# --------------------------------------------------------------------------------------------------

def check_game_refs(spec_paths, index, stats=None):
    resolver = Resolver(index)
    findings = []
    for path in collect_spec_files(spec_paths):
        text = read_text(path)
        if stats is not None:
            stats['files'] += 1
        for line, segs, wildcard in iter_citations(text):
            if _MOD_ENTITY.match(segs[0]):
                continue
            stripped = resolver.strip_namespace(segs)
            if stripped[0] not in index.types or len(stripped) < 2:
                continue                                           # not a game class: out of scope
            if stats is not None:
                stats['refs'] += 1
            bad = resolver.check_chain(segs, wildcard)
            if bad is None:
                continue
            _, owner, name = bad
            shown = '.'.join(stripped) + ('*' if wildcard else '')
            msg = 'метод не найден: %s' % shown
            hints = _suggest(name.rstrip('*'), resolver.member_names(owner)) if owner is not None else []
            if hints:
                msg += ' (в %s есть: %s)' % (owner.name, ', '.join(hints))
            findings.append(Finding(path, line, msg))
    return findings


# --------------------------------------------------------------------------------------------------
# Mode 2: ModX.Member against the core API in design.md (AC-22.3)
# --------------------------------------------------------------------------------------------------

_FALLBACK_PHASES = (
    'EarlyTick', 'AIBehaviourTick', 'PreContractSellTick', 'ClientProductionTick', 'ContractProductionTick',
    'ContractSellTick', 'ContractTick', 'ProductionTick', 'MarketSellTick', 'Tick', 'LateTick', 'DayPassed',
    'CompanySpawnTick', 'MonthCacheUpdate', 'EarlyMonthPassed', 'MonthPassed', 'YearPassed', 'LateMonthPassed',
    'LateYearPassed', 'FirstEverTick', 'FastForwardEnded')


def tick_phase_hint(member, phases):
    """ModTicks.OnDayPassed / ModTicks.DayPassed -> FeatureContext.OnTick(TickPhase.DayPassed)."""
    cand = member[2:] if re.match(r'On[A-Z]', member) else member
    if cand in phases:
        phase = cand
    else:
        close = difflib.get_close_matches(cand, list(phases), n=1, cutoff=0.85)   # OnFastFowardEnded (game typo)
        if not close:
            return None
        phase = close[0]
    return 'FeatureContext.OnTick(TickPhase.%s)' % phase


def core_hint(resolver, index, cls, member):
    """Text after 'используйте' for a name that is not in the core API, or None."""
    ti = index.types.get(cls)
    if cls == 'ModTicks':
        phases = list(index.types['TickPhase'].members) if 'TickPhase' in index.types else _FALLBACK_PHASES
        hint = tick_phase_hint(member, phases)
        if hint:
            return hint
    if ti is not None:
        close = _suggest(member, resolver.member_names(ti), limit=2)
        if close:
            return ', '.join('%s.%s' % (cls, c) for c in close)
    # the member may live in a sibling service (ModTicks.Send -> ModMail.Send)
    owners = []
    for name, other in sorted(index.types.items()):
        if name != cls and _MOD_ENTITY.match(name):
            found = resolver.find_member(other, member)
            if found is not None and found[0] != 'open':
                owners.append('%s.%s' % (name, member))
    if 0 < len(owners) <= 3:
        return ', '.join(owners)
    return None


def is_placeholder(segs):
    """ModX.Member, TickPhase.X: names used in the specs to describe a format, not to refer to the API."""
    if re.fullmatch(r'Mod[A-Z]', segs[0]):
        return True
    return not segs[0].startswith('Mod') and any(len(x) == 1 and x.isupper() for x in segs[1:])


def types_declared_beside(path, cache):
    """Names of types declared in the ```csharp blocks of the spec that owns this file (same folder).

    A feature may declare its own class that happens to share a name with a core type (cpu-architectures has an
    internal DesignContext); references to it are the feature's own business and are not checked against the core.
    """
    folder = os.path.dirname(os.path.abspath(path))
    if folder not in cache:
        names = set()
        for name in sorted(os.listdir(folder)):
            if name.lower().endswith('.md'):
                local = TypeIndex()
                for block in iter_code_blocks(read_text(os.path.join(folder, name)), CSHARP_LANGS):
                    local.merge_text(block)
                names |= set(local.types)
        cache[folder] = names
    return cache[folder]


def check_core_api(spec_paths, index, stats=None):
    resolver = Resolver(index)
    findings = []
    mod_classes = sorted(n for n in index.types if _MOD_ENTITY.match(n))
    local_cache = {}
    for path in collect_spec_files(spec_paths, exclude_dir=CORE_SPEC_DIR):
        text = read_text(path)
        if stats is not None:
            stats['files'] += 1
        for line, segs, wildcard in iter_citations(text):
            stripped = resolver.strip_namespace(segs)
            cls = stripped[0]
            if len(stripped) < 2 or is_placeholder(stripped):
                continue
            known = cls in index.types
            if not known and not re.match(r'^Mod[A-Z]', cls):
                continue                                           # not a core name: out of scope
            if cls in types_declared_beside(path, local_cache):
                continue                                           # the spec declares its own type of that name
            if stats is not None:
                stats['refs'] += 1
            shown = '.'.join(stripped) + ('*' if wildcard else '')
            if not known:
                close = _suggest(cls, mod_classes)
                msg = 'нет в API ядра: %s (класс %s не объявлен в design.md' % (shown, cls)
                msg += '; похоже: %s)' % ', '.join(close) if close else ')'
                findings.append(Finding(path, line, msg))
                continue
            bad = resolver.check_chain(segs, wildcard)
            if bad is None:
                continue
            _, owner, _ = bad
            hint = core_hint(resolver, index, cls, stripped[1]) if owner is index.types[cls] else None
            if hint:
                findings.append(Finding(path, line, 'нет в API ядра (используйте %s): %s' % (hint, shown)))
            else:
                findings.append(Finding(path, line, 'нет в API ядра: %s' % shown))
    return findings


# --------------------------------------------------------------------------------------------------
# CLI
# --------------------------------------------------------------------------------------------------

def _configure_output():
    for stream in (sys.stdout, sys.stderr):
        try:
            if stream.isatty():
                stream.reconfigure(errors='replace')
            else:
                stream.reconfigure(encoding='utf-8', errors='replace')
        except (AttributeError, ValueError):
            pass


def build_parser():
    ap = argparse.ArgumentParser(
        prog='check_spec_refs.py',
        description='Проверка ссылок `Class.Member` и `ModX.Член` в спеках .kiro/specs (FR-22).')
    ap.add_argument('--code', metavar='DIR', default=None,
                    help='папка псевдо-C# игры (по умолчанию pt-modding/code рядом с репозиторием)')
    ap.add_argument('--specs', metavar='PATH', nargs='+', default=None,
                    help='папки или .md-файлы со спеками (по умолчанию .kiro/specs)')
    ap.add_argument('paths', metavar='PATH', nargs='*',
                    help='то же, что --specs (можно указать без ключа)')
    ap.add_argument('--core-api', metavar='DESIGN_MD', nargs='?', const=DEFAULT_DESIGN, default=None,
                    help='design.md mod-framework (по умолчанию .kiro/specs/mod-framework/design.md): проверить '
                         'ссылки на API ядра вместо ссылок на код игры')
    ap.add_argument('-q', '--quiet', action='store_true', help='не печатать итоговую строку')
    return ap


def main(argv=None):
    _configure_output()
    args = build_parser().parse_args(argv)
    core_api, paths = args.core_api, list(args.paths)
    if core_api is not None and os.path.isdir(core_api):         # "--core-api <specs folder>": the design is the default
        paths.insert(0, core_api)
        core_api = DEFAULT_DESIGN
    specs = (args.specs or []) + paths or [DEFAULT_SPECS]
    for p in specs:
        if not os.path.exists(p):
            print('Спеки не найдены: %s' % p, file=sys.stderr)
            return 2
    stats = {'files': 0, 'refs': 0}
    if core_api is not None:
        if not os.path.isfile(core_api):
            print('design.md не найден: %s' % core_api, file=sys.stderr)
            return 2
        index = build_core_index(core_api)
        if not any(_MOD_ENTITY.match(n) for n in index.types):
            print('В %s не найдено ни одного публичного типа Mod* в блоках csharp.' % core_api, file=sys.stderr)
            return 2
        findings = check_core_api(specs, index, stats)
        what = 'ссылок на API ядра'
    else:
        code = args.code or default_code_dir()
        if not os.path.isdir(code):
            print('Папка с кодом игры не найдена: %s\nУкажите её через --code (это pt-modding/code, '
                  'есть только локально).' % code, file=sys.stderr)
            return 2
        index = build_game_index(code)
        for problem in index.unreadable:
            print('Предупреждение: не прочитан файл %s' % problem, file=sys.stderr)
        if not index.types:
            print('В %s не найдено ни одного класса (*.cs).' % code, file=sys.stderr)
            return 2
        findings = check_game_refs(specs, index, stats)
        what = 'ссылок на классы игры'
    findings.sort(key=lambda f: (display_path(f.path), f.line))          # stable: keeps the order inside a line
    for f in findings:
        print(f)
    if not args.quiet:
        print('Итог: файлов %d, %s %d, ошибок %d.' % (stats['files'], what, stats['refs'], len(findings)))
    return 1 if findings else 0


if __name__ == '__main__':
    sys.exit(main())
