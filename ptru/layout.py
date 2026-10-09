"""Compute Unity serialized layouts for managed script classes from assembly metadata,
and parse MonoBehaviour raw data into field trees with string/field offsets."""
import os, struct
from .clr import Meta, cuint

PRIM = {0x02: ('?', 1), 0x03: ('H', 2), 0x04: ('b', 1), 0x05: ('B', 1), 0x06: ('h', 2), 0x07: ('H', 2),
        0x08: ('i', 4), 0x09: ('I', 4), 0x0a: ('q', 8), 0x0b: ('Q', 8), 0x0c: ('f', 4), 0x0d: ('d', 8)}

# Unity built-in structs: name -> list of (fieldname, fmt)
BUILTIN_STRUCTS = {
    'UnityEngine.Vector2': 'ff', 'UnityEngine.Vector3': 'fff', 'UnityEngine.Vector4': 'ffff',
    'UnityEngine.Quaternion': 'ffff', 'UnityEngine.Color': 'ffff', 'UnityEngine.Color32': 'BBBB',
    'UnityEngine.Rect': 'ffff', 'UnityEngine.Bounds': 'ffffff', 'UnityEngine.Vector2Int': 'ii',
    'UnityEngine.Vector3Int': 'iii', 'UnityEngine.LayerMask': 'I', 'UnityEngine.Matrix4x4': 'f' * 16,
    'UnityEngine.RectInt': 'iiii', 'UnityEngine.BoundsInt': 'iiiiii', 'UnityEngine.Hash128': 'IIII',
    'UnityEngine.RectOffset': 'iiii', 'UnityEngine.RenderingLayerMask': 'I',
}

class Asm:
    def __init__(self, name, path):
        self.name = name
        self.m = m = Meta(path)
        self.types = []
        td = m.tables['TypeDef']
        nf = len(m.tables['Field'])
        nested = {r[0]: r[1] for r in m.tables['NestedClass']}
        for i, r in enumerate(td):
            fe = td[i+1][4] if i + 1 < len(td) else nf + 1
            self.types.append(dict(flags=r[0], name=m.str(r[1]), ns=m.str(r[2]), ext=r[3],
                                   fields=list(range(r[4], fe)), enclosing=nested.get(i+1)))
        for i, t in enumerate(self.types):
            fn = t['name']
            j = t['enclosing']
            while j:
                fn = self.types[j-1]['name'] + '/' + fn
                ns = self.types[j-1]['ns']
                j = self.types[j-1]['enclosing']
                t['ns'] = ns
            t['full'] = (t['ns'] + '.' if t['ns'] else '') + fn
        self.byfull = {t['full']: i + 1 for i, t in enumerate(self.types)}
        # field custom attributes
        self.fattrs = {}
        self.tattrs = {}
        for r in m.tables['CustomAttribute']:
            pt, pi = m.coded('HasCustomAttribute', r[0])
            ct, ci = m.coded('CustomAttributeType', r[1])
            an = None
            if ct == 'MemberRef':
                mr = m.tables['MemberRef'][ci-1]
                kt, ki = m.coded('MemberRefParent', mr[0])
                if kt == 'TypeRef':
                    an = m.str(m.tables['TypeRef'][ki-1][1])
            elif ct == 'MethodDef':
                # find owning typedef
                for k, tr in enumerate(td):
                    ml = tr[5]; me = td[k+1][5] if k + 1 < len(td) else 1 << 30
                    if ml <= ci < me:
                        an = m.str(tr[1]); break
            if pt == 'Field': self.fattrs.setdefault(pi, set()).add(an)
            if pt == 'TypeDef': self.tattrs.setdefault(pi, set()).add(an)

class World:
    def __init__(self, managed):
        self.managed = managed
        self.asms = {}
        self.layout_cache = {}
    def asm(self, name):
        if name not in self.asms:
            p = os.path.join(self.managed, name + '.dll')
            self.asms[name] = Asm(name, p) if os.path.exists(p) else None
        return self.asms[name]
    def find_type(self, full):
        # search loaded + common assemblies
        for a in list(self.asms.values()):
            if a and full in a.byfull: return a, a.byfull[full]
        for n in os.listdir(self.managed):
            if n.endswith('.dll'):
                a = self.asm(n[:-4])
                if a and full in a.byfull: return a, a.byfull[full]
        return None, None
    def resolve_typeref(self, a, idx):
        m = a.m
        r = m.tables['TypeRef'][idx-1]
        name, ns = m.str(r[1]), m.str(r[2])
        st, si = m.coded('ResolutionScope', r[0])
        if st == 'TypeRef':
            pa, pi, _pf = self.resolve_typeref(a, si)
            if pa is None or pi is None: return None, None, None
            full = pa.types[pi-1]['full'] + '/' + name
            return pa, pa.byfull.get(full), full
        full = (ns + '.' if ns else '') + name
        if st == 'AssemblyRef':
            an = m.str(m.tables['AssemblyRef'][si-1][6])
            ta = self.asm(an)
            if ta and full in ta.byfull: return ta, ta.byfull[full], full
            # type forwarding: search all
            ta, ti = self.find_type(full)
            return ta, ti, full
        return a, a.byfull.get(full), full
    # ---------- signatures ----------
    def parse_type(self, a, b, i, gargs):
        while b[i] in (0x1f, 0x20):
            i += 1; _, i = cuint(b, i)
        e = b[i]; i += 1
        if e in PRIM: return ('prim', PRIM[e][0]), i
        if e == 0x0e: return ('string',), i
        if e in (0x11, 0x12):
            v, i = cuint(b, i)
            return self.typeref_to_t(a, v, e == 0x11, []), i
        if e == 0x1d:
            el, i = self.parse_type(a, b, i, gargs)
            return ('array', el), i
        if e == 0x15:
            kind = b[i]; i += 1
            v, i = cuint(b, i)
            n, i = cuint(b, i)
            args = []
            for _ in range(n):
                t, i = self.parse_type(a, b, i, gargs); args.append(t)
            return self.typeref_to_t(a, v, kind == 0x11, args), i
        if e == 0x13:
            n, i = cuint(b, i)
            return (gargs[n] if n < len(gargs) else ('none', 'var')), i
        if e == 0x1e:
            n, i = cuint(b, i); return ('none', 'mvar'), i
        if e == 0x14:
            el, i = self.parse_type(a, b, i, gargs)
            rank, i = cuint(b, i)
            ns, i = cuint(b, i)
            for _ in range(ns): _, i = cuint(b, i)
            nl, i = cuint(b, i)
            for _ in range(nl): _, i = cuint(b, i)
            return ('none', 'mdarray'), i
        if e in (0x1c, 0x18, 0x19, 0x0f, 0x10, 0x1b, 0x16, 0x01):
            if e == 0x0f or e == 0x10:
                _, i = self.parse_type(a, b, i, gargs)
            return ('none', 'e%x' % e), i
        raise ValueError('sig elem %x' % e)
    def typeref_to_t(self, a, coded, isval, args):
        tag, idx = coded & 3, coded >> 2
        if tag == 0:
            return ('ref', a, idx, isval, tuple(args))
        if tag == 1:
            ta, ti, full = self.resolve_typeref(a, idx)
            if ta is None or ti is None:
                return ('ext', full, isval, tuple(args))
            return ('ref', ta, ti, isval, tuple(args))
        # TypeSpec
        b = a.m.blobat(a.m.tables['TypeSpec'][idx-1][0])
        t, _ = self.parse_type(a, b, 0, [])
        return t
    # ---------- classification ----------
    def full(self, a, ti): return a.types[ti-1]['full']
    def base_of(self, a, ti):
        ext = a.types[ti-1]['ext']
        if ext == 0: return None
        return self.typeref_to_t(a, ext, False, [])
    def chain_names(self, t):
        names = []
        while t and t[0] == 'ref':
            _, a, ti, _, args = t
            names.append(self.full(a, ti))
            t = self.base_of(a, ti)
            if t and t[0] == 'ref' and t[4] == () and args:
                pass
        if t and t[0] == 'ext': names.append(t[1])
        return names
    def resolve(self, t, depth=0):
        """Return a layout node for type t, or None if not serialized."""
        if t[0] in ('prim', 'string'): return t
        if t[0] == 'none': return None
        if t[0] == 'array':
            el = t[1]
            if el[0] == 'array': return None
            r = self.resolve(el, depth)
            if r is None: return None
            return ('array', r)
        if t[0] == 'ext':
            full = t[1]
            if full in BUILTIN_STRUCTS: return ('struct', BUILTIN_STRUCTS[full])
            return ('unknown', full)
        _, a, ti, isval, args = t
        full = self.full(a, ti)
        if full == 'System.String': return ('string',)
        if full in BUILTIN_STRUCTS: return ('struct', BUILTIN_STRUCTS[full])
        if full in ('System.Collections.Generic.List`1',):
            if not args: return None
            el = args[0]
            if el[0] == 'array': return None
            r = self.resolve(el, depth)
            return ('array', r) if r else None
        prim_map = {'System.Boolean': '?', 'System.Byte': 'B', 'System.SByte': 'b', 'System.Int16': 'h',
                    'System.UInt16': 'H', 'System.Int32': 'i', 'System.UInt32': 'I', 'System.Int64': 'q',
                    'System.UInt64': 'Q', 'System.Single': 'f', 'System.Double': 'd', 'System.Char': 'H'}
        if full in prim_map: return ('prim', prim_map[full])
        if full == 'UnityEngine.AnimationCurve': return ('curve',)
        if full == 'UnityEngine.Gradient': return ('gradient',)
        chain = self.chain_names(t)
        if 'UnityEngine.Object' in chain: return ('pptr',)
        if any(c.startswith('UnityEngine.Events.UnityEvent') for c in chain): return ('event',)
        if 'System.Enum' in chain:
            # underlying type
            for fi in a.types[ti-1]['fields']:
                fr = a.m.tables['Field'][fi-1]
                if not (fr[0] & 0x10):
                    b = a.m.blobat(fr[2])
                    ft, _ = self.parse_type(a, b, 1, [])
                    return ft
            return ('prim', 'i')
        if 'System.Delegate' in chain or 'System.MulticastDelegate' in chain: return None
        tflags = a.types[ti-1]['flags']
        if tflags & 0x20: return None  # interface
        if not (tflags & 0x2000): return None  # not [Serializable]
        if tflags & 0x80 and not isval: return None  # abstract
        if a.name in ('mscorlib', 'netstandard', 'System', 'System.Core'): return None
        if depth > 8: return None
        return ('class', full, self.class_fields(t, depth + 1))
    def class_fields(self, t, depth=0):
        """Serialized fields of class t (including base classes), list of (name, node)."""
        _, a, ti, isval, args = t
        key = (a.name, ti, args)
        if key in self.layout_cache: return self.layout_cache[key]
        out = []
        base = self.base_of(a, ti)
        if base and base[0] == 'ref':
            bfull = self.full(base[1], base[2])
            if bfull not in ('UnityEngine.MonoBehaviour', 'UnityEngine.ScriptableObject', 'UnityEngine.Behaviour',
                             'UnityEngine.Component', 'UnityEngine.Object', 'System.Object', 'System.ValueType'):
                # substitute generic args of base: base args may reference our args via VAR
                bt = base
                if bt[4]:
                    bt = ('ref', bt[1], bt[2], bt[3], tuple(self.subst(x, args) for x in bt[4]))
                out.extend(self.class_fields(bt, depth))
        m = a.m
        for fi in a.types[ti-1]['fields']:
            fr = m.tables['Field'][fi-1]
            fl = fr[0]
            if fl & (0x10 | 0x40 | 0x80): continue  # static, literal, notserialized
            if fl & 0x20: continue  # readonly
            attrs = a.fattrs.get(fi, set())
            public = (fl & 7) == 6
            if not public and 'SerializeField' not in attrs and 'SerializeReference' not in attrs: continue
            if 'SerializeReference' in attrs:
                out.append((m.str(fr[1]), ('serref',))); continue
            b = m.blobat(fr[2])
            ft, _ = self.parse_type(a, b, 1, list(args))
            node = self.resolve(ft, depth)
            if node is None: continue
            out.append((m.str(fr[1]), node))
        self.layout_cache[key] = out
        return out
    def subst(self, x, args):
        return x
    def script_layout(self, asm_name, ns, cls):
        a = self.asm(asm_name.replace('.dll', ''))
        full = (ns + '.' if ns else '') + cls
        ti = a.byfull.get(full)
        if ti is None: return None
        return self.class_fields(('ref', a, ti, False, ()))

# ---------- reader ----------
class Reader:
    def __init__(self, d, pos=0):
        self.d = d; self.p = pos; self.strings = []; self.offs = {}
    def align(self):
        self.p = (self.p + 3) & ~3
    def prim(self, f):
        v, = struct.unpack_from('<' + f, self.d, self.p); self.p += struct.calcsize(f); return v
    def string(self, path):
        n = self.prim('i')
        if n < 0 or self.p + n > len(self.d): raise ValueError('bad string len %d at %d' % (n, self.p))
        s = self.d[self.p:self.p+n]
        self.strings.append((path, self.p - 4, s.decode('utf-8', 'replace')))
        self.p += n; self.align(); return s.decode('utf-8', 'replace')
    def pptr(self):
        f = self.prim('i'); pid = self.prim('q'); return (f, pid)
    def node(self, nd, path):
        k = nd[0]
        self.offs[path] = self.p
        if k == 'prim':
            v = self.prim(nd[1])
            if struct.calcsize(nd[1]) < 4: self.align()
            return v
        if k == 'string': return self.string(path)
        if k == 'pptr': return self.pptr()
        if k == 'struct':
            v = [self.prim(c) for c in nd[1]]
            return v
        if k == 'array':
            n = self.prim('i')
            if n < 0 or n > 1000000: raise ValueError('bad array len %d at %d (%s)' % (n, self.p - 4, path))
            out = [self.node(nd[1], '%s[%d]' % (path, i)) for i in range(n)]
            el = nd[1]
            if el[0] == 'prim' and struct.calcsize(el[1]) < 4: self.align()
            return out
        if k == 'class':
            return {fn: self.node(fnd, path + '.' + fn) for fn, fnd in nd[2]}
        if k == 'event':
            n = self.prim('i')
            calls = []
            for i in range(n):
                pp = '%s.m_Calls[%d]' % (path, i)
                c = dict(target=self.pptr(), tatn=self.string(pp + '.m_TargetAssemblyTypeName'),
                         method=self.string(pp + '.m_MethodName'), mode=self.prim('i'),
                         objarg=self.pptr(), objargtype=self.string(pp + '.m_ObjectArgumentAssemblyTypeName'),
                         intarg=self.prim('i'), floatarg=self.prim('f'),
                         strarg=self.string(pp + '.m_StringArgument'), boolarg=self.prim('?'))
                self.align(); c['state'] = self.prim('i')
                calls.append(c)
            return calls
        if k == 'curve':
            n = self.prim('i')
            self.p += n * 28
            self.p += 12
            return 'curve'
        if k == 'gradient':
            self.p += 16 * 8 + 2 * 8 + 2 * 8 + 4 + 4 + 1 + 1
            self.align()
            return 'gradient'
        if k == 'serref':
            raise ValueError('SerializeReference not supported (%s)' % path)
        if k == 'unknown':
            raise ValueError('unknown type %s at %s' % (nd[1], path))
        raise ValueError(k)

def parse_mb(world, layout, d):
    r = Reader(d)
    go = r.pptr(); en = r.prim('B'); r.align(); scr = r.pptr(); name = r.string('m_Name')
    vals = {}
    for fn, nd in layout:
        vals[fn] = r.node(nd, fn)
    return vals, r
