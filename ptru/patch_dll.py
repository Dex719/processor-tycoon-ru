"""Patch string literals of a .NET assembly per ldstr site.

New strings are appended to a relocated copy of the metadata (grown #US heap) placed in a new PE section,
and each ldstr operand is pointed at its new string. Existing strings keep their offsets."""
import struct
import pefile
from .clr import Meta

def us_blob(s):
    b = s.encode('utf-16-le')
    special = 1 if any(ord(ch) > 0xFF or 0x01 <= ord(ch) <= 0x08 or 0x0E <= ord(ch) <= 0x1F or ord(ch) in (0x27, 0x2D, 0x7F) for ch in s) else 0
    b += bytes([special])
    n = len(b)
    if n < 0x80: pre = bytes([n])
    elif n < 0x4000: pre = struct.pack('>H', 0x8000 | n)
    else: pre = struct.pack('>I', 0xC0000000 | n)
    return pre + b

def align(x, a): return (x + a - 1) // a * a

def patch(src, dst, site_map):
    """site_map: {operand_file_offset: new_string}"""
    m = Meta(src)
    data = bytearray(m.raw)
    # 1. build new #US heap
    us = bytearray(m.us)
    while len(us) % 4: us += b'\0'  # keep 4-alignment of base (existing offsets unchanged)
    new_off = {}
    for s in sorted(set(site_map.values())):
        new_off[s] = len(us)
        us += us_blob(s)
    while len(us) % 4: us += b'\0'
    assert len(us) < (1 << 24), 'US heap too large'
    # 2. patch ldstr operands (original layout)
    for off, s in site_map.items():
        assert data[off - 1] == 0x72, 'not an ldstr at %x' % off
        struct.pack_into('<I', data, off, 0x70000000 | new_off[s])
    # 3. build relocated metadata
    md = bytes(data[m.md_off: m.md_off + m.md_size])
    vlen, = struct.unpack_from('<I', md, 12)
    root_hdr = bytearray(md[:16 + vlen + 4])  # signature..version..flags,nstreams
    nstreams = len(m.stream_order)
    streams = []
    for name in m.stream_order:
        o, sz, _ = m.streams[name]
        content = bytes(us) if name == '#US' else md[o:o + sz]
        streams.append((name, content))
    hdr_len = len(root_hdr)
    for name, _ in streams:
        hdr_len += 8 + align(len(name) + 1, 4)
    out = bytearray(root_hdr)
    pos = align(hdr_len, 4)
    blobs = bytearray()
    for name, content in streams:
        out += struct.pack('<II', pos, len(content))
        nb = name.encode() + b'\0'
        out += nb + b'\0' * (align(len(nb), 4) - len(nb))
        blobs += content + b'\0' * (align(len(content), 4) - len(content))
        pos += align(len(content), 4)
    out += b'\0' * (align(hdr_len, 4) - len(out))
    out += blobs
    new_md = bytes(out)
    # 4. grow headers by one FileAlignment to fit a new section header, shift sections
    pe = pefile.PE(data=bytes(data), fast_load=True)
    fa = pe.OPTIONAL_HEADER.FileAlignment; sa = pe.OPTIONAL_HEADER.SectionAlignment
    nsec = pe.FILE_HEADER.NumberOfSections
    sect_tab = pe.sections[0].get_file_offset()
    soh = pe.OPTIONAL_HEADER.SizeOfHeaders
    need = sect_tab + (nsec + 1) * 40
    shift = 0 if need <= soh else align(need - soh, fa)
    first_raw = min(s.PointerToRawData for s in pe.sections)
    body = bytes(data[first_raw:])
    head = bytearray(data[:soh]) + b'\0' * shift
    newfile = head + bytearray(body)
    if shift:
        # pad between headers and first section if needed
        assert first_raw == soh
    pe2_off_opt = pe.OPTIONAL_HEADER.get_file_offset()
    # update SizeOfHeaders
    struct.pack_into('<I', newfile, pe2_off_opt + 60, soh + shift)
    # update section PointerToRawData
    for i in range(nsec):
        so = sect_tab + i * 40
        ptr, = struct.unpack_from('<I', newfile, so + 20)
        if ptr: struct.pack_into('<I', newfile, so + 20, ptr + shift)
    # update debug directory PointerToRawData entries
    dd_rva = pe.OPTIONAL_HEADER.DATA_DIRECTORY[6].VirtualAddress
    dd_size = pe.OPTIONAL_HEADER.DATA_DIRECTORY[6].Size
    if dd_rva and shift:
        dd_off = pe.get_offset_from_rva(dd_rva) + shift
        for k in range(dd_size // 28):
            e = dd_off + k * 28
            ptr, = struct.unpack_from('<I', newfile, e + 24)
            if ptr: struct.pack_into('<I', newfile, e + 24, ptr + shift)
    # 5. append new section with metadata
    last = pe.sections[-1]
    new_va = align(last.VirtualAddress + max(last.Misc_VirtualSize, last.SizeOfRawData), sa)
    while len(newfile) % fa: newfile += b'\0'
    new_raw = len(newfile)
    raw_size = align(len(new_md), fa)
    newfile += new_md + b'\0' * (raw_size - len(new_md))
    so = sect_tab + nsec * 40
    sh = struct.pack('<8sIIIIIIHHI', b'.ptru\0\0\0', len(new_md), new_va, raw_size, new_raw, 0, 0, 0, 0, 0x40000040)
    newfile[so:so + 40] = sh
    struct.pack_into('<H', newfile, pe.FILE_HEADER.get_file_offset() + 2, nsec + 1)
    struct.pack_into('<I', newfile, pe2_off_opt + 56, align(new_va + len(new_md), sa))  # SizeOfImage
    sid_off = pe2_off_opt + 8  # SizeOfInitializedData
    v, = struct.unpack_from('<I', newfile, sid_off)
    struct.pack_into('<I', newfile, sid_off, v + raw_size)
    # 6. point CLI header to new metadata
    cli_off = m.cli_off + shift
    struct.pack_into('<II', newfile, cli_off + 8, new_va, len(new_md))
    # 7. checksum
    pe3 = pefile.PE(data=bytes(newfile), fast_load=True)
    pe3.OPTIONAL_HEADER.CheckSum = pe3.generate_checksum()
    final = pe3.write()
    open(dst, 'wb').write(final)
    return new_off


def is_patched(path):
    """True if the assembly already carries our relocated metadata section."""
    with open(path, 'rb') as f:
        pe = pefile.PE(data=f.read(), fast_load=True)
    return any(sec.Name.startswith(b'.ptru') for sec in pe.sections)

