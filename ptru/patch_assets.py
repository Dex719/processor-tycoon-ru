"""Apply string replacements and byte patches to serialized files and repack data.unity3d."""
import struct
from .serialized import SerializedFile
from .unityfs import write_bundle


def enc_str(s):
    b = s.encode('utf-8')
    return struct.pack('<i', len(b)) + b + b'\0' * ((4 - len(b) % 4) % 4)


def patch_object(d, str_repl, byte_patches):
    """str_repl: [(offset, expected_old, new)]; byte_patches: [(offset, bytes)] (offsets in the original data)."""
    d = bytearray(d)
    for off, b in byte_patches:
        d[off:off + len(b)] = b
    for off, old, new in sorted(str_repl, key=lambda x: -x[0]):
        n, = struct.unpack_from('<i', d, off)
        cur = d[off + 4:off + 4 + n].decode('utf-8')
        if cur != old:
            raise ValueError('string mismatch at %d: %r != %r' % (off, cur, old))
        d[off:off + 4 + n + (4 - n % 4) % 4] = enc_str(new)
    return bytes(d)


def rebuild_sf(raw, patches, name=''):
    """patches: {pathID: (str_repl, byte_patches)} -> (new file bytes, number of changed objects)."""
    sf = SerializedFile(raw, name)
    head = bytearray(raw[:sf.doff])
    out = bytearray()
    changed = 0
    for o in sorted(sf.objs, key=lambda o: o['start']):
        d = sf.data(o)
        if o['pid'] in patches:
            nd = patch_object(d, *patches[o['pid']])
            if nd != d: changed += 1
            d = nd
        while len(out) % 16: out += b'\0'
        struct.pack_into('<qI', head, o['ent'] + 8, len(out), len(d))
        out += d
    struct.pack_into('>q', head, 24, sf.doff + len(out))  # v22 header: file size
    return bytes(head + out), changed


def repack(game, dst, file_patches):
    """Write a new bundle from game.raw with file_patches = {node name: {pathID: (str_repl, byte_patches)}}."""
    files, stats = [], {}
    for off, sz, f, name in game.nodes:
        raw = game.raw[name]
        if name in file_patches:
            raw, stats[name] = rebuild_sf(raw, file_patches[name], name)
        files.append((name, f, raw))
    write_bundle(dst, game.hdr, files)
    return stats
