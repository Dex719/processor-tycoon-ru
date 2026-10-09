"""Minimal ECMA-335 (.NET) metadata reader."""
import pefile, struct

TABLES = {
 0x00:('Module',['2','s','g','g','g']),
 0x01:('TypeRef',['c:ResolutionScope','s','s']),
 0x02:('TypeDef',['4','s','s','c:TypeDefOrRef','t:04','t:06']),
 0x03:('FieldPtr',['t:04']),
 0x04:('Field',['2','s','b']),
 0x05:('MethodPtr',['t:06']),
 0x06:('MethodDef',['4','2','2','s','b','t:08']),
 0x07:('ParamPtr',['t:08']),
 0x08:('Param',['2','2','s']),
 0x09:('InterfaceImpl',['t:02','c:TypeDefOrRef']),
 0x0A:('MemberRef',['c:MemberRefParent','s','b']),
 0x0B:('Constant',['2','c:HasConstant','b']),
 0x0C:('CustomAttribute',['c:HasCustomAttribute','c:CustomAttributeType','b']),
 0x0D:('FieldMarshal',['c:HasFieldMarshal','b']),
 0x0E:('DeclSecurity',['2','c:HasDeclSecurity','b']),
 0x0F:('ClassLayout',['2','4','t:02']),
 0x10:('FieldLayout',['4','t:04']),
 0x11:('StandAloneSig',['b']),
 0x12:('EventMap',['t:02','t:14']),
 0x13:('EventPtr',['t:14']),
 0x14:('Event',['2','s','c:TypeDefOrRef']),
 0x15:('PropertyMap',['t:02','t:17']),
 0x16:('PropertyPtr',['t:17']),
 0x17:('Property',['2','s','b']),
 0x18:('MethodSemantics',['2','t:06','c:HasSemantics']),
 0x19:('MethodImpl',['t:02','c:MethodDefOrRef','c:MethodDefOrRef']),
 0x1A:('ModuleRef',['s']),
 0x1B:('TypeSpec',['b']),
 0x1C:('ImplMap',['2','c:MemberForwarded','s','t:1A']),
 0x1D:('FieldRVA',['4','t:04']),
 0x1E:('EncLog',['4','4']),
 0x1F:('EncMap',['4']),
 0x20:('Assembly',['4','2','2','2','2','4','b','s','s']),
 0x21:('AssemblyProcessor',['4']),
 0x22:('AssemblyOS',['4','4','4']),
 0x23:('AssemblyRef',['2','2','2','2','4','b','s','s','b']),
 0x24:('AssemblyRefProcessor',['4','t:23']),
 0x25:('AssemblyRefOS',['4','4','4','t:23']),
 0x26:('File',['4','s','b']),
 0x27:('ExportedType',['4','4','s','s','c:Implementation']),
 0x28:('ManifestResource',['4','4','s','c:Implementation']),
 0x29:('NestedClass',['t:02','t:02']),
 0x2A:('GenericParam',['2','2','c:TypeOrMethodDef','s']),
 0x2B:('MethodSpec',['c:MethodDefOrRef','b']),
 0x2C:('GenericParamConstraint',['t:2A','c:TypeDefOrRef']),
}
CODED = {
 'TypeDefOrRef':(2,[0x02,0x01,0x1B]),
 'HasConstant':(2,[0x04,0x08,0x17]),
 'HasCustomAttribute':(5,[0x06,0x04,0x01,0x02,0x08,0x09,0x0A,0x00,0x0E,0x17,0x14,0x11,0x1A,0x1B,0x20,0x23,0x26,0x27,0x28,0x2A,0x2C,0x2B]),
 'HasFieldMarshal':(1,[0x04,0x08]),
 'HasDeclSecurity':(2,[0x02,0x06,0x20]),
 'MemberRefParent':(3,[0x02,0x01,0x1A,0x06,0x1B]),
 'HasSemantics':(1,[0x14,0x17]),
 'MethodDefOrRef':(1,[0x06,0x0A]),
 'MemberForwarded':(1,[0x04,0x06]),
 'Implementation':(2,[0x26,0x23,0x27]),
 'CustomAttributeType':(3,[None,None,0x06,0x0A,None]),
 'ResolutionScope':(2,[0x00,0x1A,0x23,0x01]),
 'TypeOrMethodDef':(1,[0x02,0x06]),
}

def cuint(b, i):
    x = b[i]
    if x & 0x80 == 0: return x, i+1
    if x & 0xC0 == 0x80: return ((x & 0x3F) << 8) | b[i+1], i+2
    return ((x & 0x1F) << 24) | (b[i+1] << 16) | (b[i+2] << 8) | b[i+3], i+4

class Meta:
    def __init__(self, path):
        self.path = path
        with open(path, 'rb') as f:
            data = f.read()
        self.raw = data
        self.pe = pe = pefile.PE(data=data, fast_load=True)
        cli = pe.OPTIONAL_HEADER.DATA_DIRECTORY[14]
        self.cli_off = cli_off = pe.get_offset_from_rva(cli.VirtualAddress)
        cb, maj, mnr, md_rva, md_size = struct.unpack_from('<IHHII', data, cli_off)
        self.md_rva, self.md_size = md_rva, md_size
        self.md_off = md_off = pe.get_offset_from_rva(md_rva)
        vlen, = struct.unpack_from('<I', data, md_off+12)
        p = md_off + 16 + vlen
        self.stream_hdr_off = p
        flags, ns = struct.unpack_from('<HH', data, p); p += 4
        self.streams = {}
        self.stream_order = []
        for _ in range(ns):
            hdr_pos = p
            off, size = struct.unpack_from('<II', data, p); p += 8
            e = data.index(b'\0', p); name = data[p:e].decode(); p = (e + 4) & ~3
            self.streams[name] = (off, size, hdr_pos)
            self.stream_order.append(name)
        def S(n):
            if n not in self.streams: return b''
            o, s, _ = self.streams[n]; return data[md_off+o: md_off+o+s]
        self.strings = S('#Strings'); self.us = S('#US'); self.blob = S('#Blob')
        tb = S('#~') if '#~' in self.streams else S('#-')
        hs = tb[6]
        self.ssz = 4 if hs & 1 else 2
        self.gsz = 4 if hs & 2 else 2
        self.bsz = 4 if hs & 4 else 2
        valid, = struct.unpack_from('<Q', tb, 8)
        q = 24
        self.rows = {}
        for t in range(64):
            if valid >> t & 1:
                self.rows[t], = struct.unpack_from('<I', tb, q); q += 4
        if hs & 0x40: q += 4
        self.tables = {}
        for t in range(64):
            if not (valid >> t & 1): continue
            name, cols = TABLES[t]
            sizes = [self._colsize(c) for c in cols]
            rows = []
            for r in range(self.rows[t]):
                vals = []
                for sz in sizes:
                    vals.append(int.from_bytes(tb[q:q+sz], 'little')); q += sz
                rows.append(vals)
            self.tables[name] = rows
        for n in TABLES.values():
            self.tables.setdefault(n[0], [])
    def _colsize(self, c):
        if c == '2': return 2
        if c == '4': return 4
        if c == 's': return self.ssz
        if c == 'g': return self.gsz
        if c == 'b': return self.bsz
        if c.startswith('t:'):
            return 2 if self.rows.get(int(c[2:], 16), 0) < 65536 else 4
        if c.startswith('c:'):
            bits, tabs = CODED[c[2:]]
            mx = max(self.rows.get(t, 0) for t in tabs if t is not None)
            return 2 if mx < (1 << (16 - bits)) else 4
        raise ValueError(c)
    def str(self, i):
        e = self.strings.index(b'\0', i); return self.strings[i:e].decode('utf-8')
    def blobat(self, i):
        n, j = cuint(self.blob, i); return self.blob[j:j+n]
    def usat(self, i):
        n, j = cuint(self.us, i); return self.us[j:j+n-1].decode('utf-16-le') if n else ''
    def coded(self, kind, v):
        bits, tabs = CODED[kind]
        tag = v & ((1 << bits) - 1)
        t = tabs[tag] if tag < len(tabs) else None
        return (TABLES[t][0] if t is not None else None), v >> bits
    def rva2off(self, rva):
        return self.pe.get_offset_from_rva(rva)
