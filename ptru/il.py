"""IL body parsing: enumerate every ldstr site of an assembly with its owning method."""
import struct

OP1 = {op: 0 for op in range(0x00, 0xE1)}
for op in (0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x1F, 0xDE): OP1[op] = 1
for op in range(0x2B, 0x38): OP1[op] = 1
for op in (0x20, 0x22, 0x27, 0x28, 0x29, 0x6F, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x79, 0x7B, 0x7C, 0x7D, 0x7E,
           0x7F, 0x80, 0x81, 0x8C, 0x8D, 0x8F, 0xA3, 0xA4, 0xA5, 0xC2, 0xC6, 0xD0, 0xDD):
    OP1[op] = 4
for op in range(0x38, 0x45): OP1[op] = 4
OP1[0x21] = 8; OP1[0x23] = 8
OP1[0x45] = 'switch'
OP2 = {0x00: 0, 0x01: 0, 0x02: 0, 0x03: 0, 0x04: 0, 0x05: 0, 0x06: 4, 0x07: 4, 0x09: 2, 0x0A: 2, 0x0B: 2,
       0x0C: 2, 0x0D: 2, 0x0E: 2, 0x0F: 0, 0x11: 0, 0x12: 1, 0x13: 0, 0x14: 0, 0x15: 4, 0x16: 4, 0x17: 0,
       0x18: 0, 0x19: 1, 0x1A: 0, 0x1C: 4, 0x1D: 0, 0x1E: 0}


def body_of(m, rva):
    off = m.rva2off(rva)
    h = m.raw[off]
    if h & 3 == 2:
        return off + 1, h >> 2
    flags_size, = struct.unpack_from('<H', m.raw, off)
    code_size, = struct.unpack_from('<I', m.raw, off + 4)
    return off + (flags_size >> 12) * 4, code_size


def instructions(m, code_off, code_size):
    """[(start, opcode, operand, operand_file_offset)]"""
    d = m.raw
    i = code_off; end = code_off + code_size
    out = []
    while i < end:
        start = i
        op = d[i]; i += 1
        if op == 0xFE:
            op2 = d[i]; i += 1
            sz = OP2[op2]
            out.append((start, 0xFE00 | op2, int.from_bytes(d[i:i + sz], 'little') if sz else None, i)); i += sz
            continue
        sz = OP1[op]
        if sz == 'switch':
            n, = struct.unpack_from('<I', d, i); i += 4 + 4 * n
            out.append((start, op, None, i)); continue
        out.append((start, op, int.from_bytes(d[i:i + sz], 'little') if sz else None, i))
        i += sz
    if i != end:
        raise ValueError('IL decode overrun')
    return out


def type_names(m):
    """TypeDef index (0-based) -> full name with enclosing types, e.g. 'Ns.Outer/<>c'."""
    td = m.tables['TypeDef']
    nested = {r[0]: r[1] for r in m.tables['NestedClass']}
    names = []
    for i, r in enumerate(td):
        n = m.str(r[1]); j = nested.get(i + 1); ns = m.str(r[2])
        while j:
            n = m.str(td[j - 1][1]) + '/' + n
            ns = m.str(td[j - 1][2])
            j = nested.get(j)
        names.append((ns + '.' if ns else '') + n)
    return names


def ldstr_sites(m):
    """All ldstr sites in metadata order: dict(method='Ns.Type.Method', text, us, file_off)."""
    td = m.tables['TypeDef']
    md = m.tables['MethodDef']
    tnames = type_names(m)
    owner = {}
    for k, tr in enumerate(td):
        me = td[k + 1][5] if k + 1 < len(td) else len(md) + 1
        for j in range(tr[5], me): owner[j] = k
    sites = []
    for mi, r in enumerate(md, start=1):
        if r[0] == 0: continue
        co, cs = body_of(m, r[0])
        meth = tnames[owner[mi]] + '::' + m.str(r[3])
        for st, op, operand, opoff in instructions(m, co, cs):
            if op == 0x72:
                us = operand & 0xFFFFFF
                sites.append(dict(method=meth, text=m.usat(us), us=us, file_off=opoff))
    # occurrence index of the same text inside the same method (stable site key)
    seen = {}
    for s in sites:
        k = (s['method'], s['text'])
        s['occ'] = seen.get(k, 0)
        seen[k] = s['occ'] + 1
    return sites
