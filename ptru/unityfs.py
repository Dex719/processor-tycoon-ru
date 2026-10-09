"""Reading and writing UnityFS archives (data.unity3d)."""
import struct


def lz4_decompress(src, usize):
    dst = bytearray()
    i = 0
    n = len(src)
    while i < n:
        token = src[i]; i += 1
        lit = token >> 4
        if lit == 15:
            while True:
                b = src[i]; i += 1
                lit += b
                if b != 255: break
        dst += src[i:i + lit]; i += lit
        if i >= n: break
        off = src[i] | (src[i + 1] << 8); i += 2
        ml = token & 15
        if ml == 15:
            while True:
                b = src[i]; i += 1
                ml += b
                if b != 255: break
        ml += 4
        start = len(dst) - off
        if off >= ml:
            dst += dst[start:start + ml]
        else:
            chunk = dst[start:]
            dst += (bytes(chunk) * (ml // off + 1))[:ml]
    if len(dst) != usize:
        raise ValueError('LZ4: size mismatch %d != %d' % (len(dst), usize))
    return bytes(dst)


def _cstr(b, p):
    e = b.index(b'\0', p)
    return b[p:e].decode('utf-8'), e + 1


def read_bundle(path):
    """Return (header, nodes, data); nodes = [(offset, size, flags, name)] into the decompressed data."""
    b = open(path, 'rb').read()
    p = 0
    sig, p = _cstr(b, p)
    if sig != 'UnityFS':
        raise ValueError('not a UnityFS archive: %r' % sig)
    ver, = struct.unpack_from('>I', b, p); p += 4
    uver, p = _cstr(b, p)
    urev, p = _cstr(b, p)
    size, csize, usize, flags = struct.unpack_from('>qIII', b, p); p += 20
    hdr = dict(ver=ver, uver=uver, urev=urev, flags=flags)
    if ver >= 7:
        p = (p + 15) & ~15
    if flags & 0x80:
        bi = b[len(b) - csize:]
    else:
        bi = b[p:p + csize]; p += csize
    comp = flags & 0x3F
    if comp == 0:
        binfo = bi
    elif comp in (2, 3):
        binfo = lz4_decompress(bi, usize)
    else:
        raise ValueError('unsupported block-info compression %d' % comp)
    if flags & 0x200:
        p = (p + 15) & ~15
    q = 16
    nblocks, = struct.unpack_from('>i', binfo, q); q += 4
    blocks = []
    for _ in range(nblocks):
        blocks.append(struct.unpack_from('>IIH', binfo, q)); q += 10
    nnodes, = struct.unpack_from('>i', binfo, q); q += 4
    nodes = []
    for _ in range(nnodes):
        off, sz, f = struct.unpack_from('>qqI', binfo, q); q += 20
        name, q = _cstr(binfo, q)
        nodes.append((off, sz, f, name))
    data = bytearray()
    for (u, c, f) in blocks:
        raw = b[p:p + c]; p += c
        bc = f & 0x3F
        if bc == 0:
            data += raw
        elif bc in (2, 3):
            data += lz4_decompress(raw, u)
        else:
            raise ValueError('unsupported block compression %d' % bc)
    return hdr, nodes, bytes(data)


def write_bundle(path, hdr, files, block_size=0x20000):
    """files: [(name, flags, bytes)] -> uncompressed UnityFS archive."""
    data = bytearray()
    nodes = []
    for name, f, d in files:
        nodes.append((len(data), len(d), f, name))
        data += d
    blocks = [(min(block_size, len(data) - i),) * 2 + (0x40,) for i in range(0, len(data), block_size)] or [(0, 0, 0x40)]
    binfo = bytearray(16)
    binfo += struct.pack('>i', len(blocks))
    for u, c, f in blocks:
        binfo += struct.pack('>IIH', u, c, f)
    binfo += struct.pack('>i', len(nodes))
    for off, sz, f, name in nodes:
        binfo += struct.pack('>qqI', off, sz, f) + name.encode('utf-8') + b'\0'
    flags = 0x40 | 0x200  # block+directory info combined, padding at start, no compression
    head = bytearray(b'UnityFS\0' + struct.pack('>I', hdr['ver']) + hdr['uver'].encode() + b'\0'
                     + hdr['urev'].encode() + b'\0')
    size_pos = len(head)
    head += struct.pack('>qIII', 0, len(binfo), len(binfo), flags)
    if hdr['ver'] >= 7:
        while len(head) % 16: head += b'\0'
    head += binfo
    while len(head) % 16: head += b'\0'
    struct.pack_into('>q', head, size_pos, len(head) + len(data))
    with open(path, 'wb') as fo:
        fo.write(head)
        fo.write(data)
