"""Unity SerializedFile (format 22) reader: object table and raw object data."""
import struct


def cstr(b, p):
    e = b.index(b'\0', p)
    return b[p:e].decode('utf-8', 'replace'), e + 1


def rstr(d, p):
    """Read an aligned length-prefixed UTF-8 string from object data."""
    n, = struct.unpack_from('<i', d, p); p += 4
    s = d[p:p + n].decode('utf-8', 'replace'); p += n
    return s, (p + 3) & ~3


class SerializedFile:
    def __init__(self, raw, name=''):
        self.name = name
        b = self.b = raw
        ver, = struct.unpack_from('>I', b, 8)
        self.ver = ver
        if ver < 22:
            raise ValueError('%s: serialized file version %d is not supported' % (name, ver))
        p = 16
        if b[p] != 0:
            raise ValueError('%s: big-endian serialized files are not supported' % name)
        p += 4
        self.msize, self.fsize, self.doff, _ = struct.unpack_from('>IqqQ', b, p); p += 28
        self.uver, p = cstr(b, p)
        p += 4  # target platform
        self.tt = b[p]; p += 1
        n, = struct.unpack_from('<i', b, p); p += 4
        self.types = []
        for _ in range(n):
            t, p = self._read_type(p)
            self.types.append(t)
        n, = struct.unpack_from('<i', b, p); p += 4
        self.objs = []
        for _ in range(n):
            p = (p + 3) & ~3
            ent = p
            pid, start, size, tidx = struct.unpack_from('<qqIi', b, p); p += 24
            self.objs.append(dict(pid=pid, start=start, size=size, cls=self.types[tidx]['cls'], ent=ent))
        n, = struct.unpack_from('<i', b, p); p += 4
        for _ in range(n):
            p += 4; p = (p + 3) & ~3; p += 8
        n, = struct.unpack_from('<i', b, p); p += 4
        self.ext = []
        for _ in range(n):
            _, p = cstr(b, p); p += 20
            name_, p = cstr(b, p)
            self.ext.append(name_)
        self.byid = {o['pid']: o for o in self.objs}

    def _read_type(self, p):
        b = self.b
        cls, = struct.unpack_from('<i', b, p); p += 4
        p += 1  # stripped
        p += 2  # script type index
        if cls == 114:
            p += 16
        p += 16
        if self.tt:
            nn, sbs = struct.unpack_from('<ii', b, p); p += 8
            p += nn * 32 + sbs
            if self.ver >= 21:
                k, = struct.unpack_from('<i', b, p); p += 4 + 4 * k
        return dict(cls=cls), p

    def data(self, o):
        s = self.doff + o['start']
        return self.b[s:s + o['size']]
