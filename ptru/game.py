"""In-memory view of the game's data.unity3d: serialized files, scripts, UI hierarchy, texts and fonts."""
import os, struct
from .unityfs import read_bundle
from .serialized import SerializedFile, rstr
from .layout import World, parse_mb

SCRIPTS_FILE = 'globalgamemanagers.assets'


class Hierarchy:
    """GameObject/RectTransform hierarchy of one serialized file."""
    def __init__(self, sf):
        self.gos = {}; self.go_tr = {}; self.tr_go = {}; self.tr_parent = {}
        self.rt = {}; self.kids = {}; self.sd_off = {}
        for o in sf.objs:
            d = sf.data(o)
            if o['cls'] == 1:  # GameObject
                n, = struct.unpack_from('<i', d, 0); p = 4
                comps = []
                for _ in range(n):
                    _, pid = struct.unpack_from('<iq', d, p); p += 12; comps.append(pid)
                p += 4
                name, p = rstr(d, p)
                self.gos[o['pid']] = name
                if comps: self.go_tr[o['pid']] = comps[0]
            elif o['cls'] in (4, 224):  # Transform / RectTransform
                _, gpid = struct.unpack_from('<iq', d, 0); p = 52
                n, = struct.unpack_from('<i', d, p); p += 4
                self.kids[o['pid']] = [struct.unpack_from('<iq', d, p + 12 * k)[1] for k in range(n)]
                p += 12 * n
                _, par = struct.unpack_from('<iq', d, p); p += 12
                self.tr_go[o['pid']] = gpid; self.tr_parent[o['pid']] = par
                if o['cls'] == 224:
                    self.rt[o['pid']] = struct.unpack_from('<10f', d, p)
                    self.sd_off[o['pid']] = p + 24
        self._size_cache = {}
        self.rect = {}
        for g, t in self.go_tr.items():
            if t in self.rt:
                w, h = self._size(t)
                self.rect[g] = (round(w, 1), round(h, 1))
        self._paths = {}

    def _size(self, t, depth=0):
        """Effective rect size, resolving stretched anchors through the parents (serialized layout state)."""
        if t in self._size_cache: return self._size_cache[t]
        v = self.rt.get(t)
        if v is None or depth > 40: return (1920.0, 1080.0)
        amin, amax, sd = v[0:2], v[2:4], v[6:8]
        par = self.tr_parent.get(t)
        if par and par in self.rt:
            pw, ph = self._size(par, depth + 1)
        else:
            res = (sd[0], sd[1]) if amin == amax else (1920.0, 1080.0)
            self._size_cache[t] = res
            return res
        res = ((amax[0] - amin[0]) * pw + sd[0], (amax[1] - amin[1]) * ph + sd[1])
        self._size_cache[t] = res
        return res

    def path(self, gpid):
        if gpid in self._paths: return self._paths[gpid]
        parts = []; t = self.go_tr.get(gpid); guard = 0
        while t and guard < 30:
            parts.append(self.gos.get(self.tr_go.get(t), '?')); t = self.tr_parent.get(t); guard += 1
        p = '/'.join(reversed(parts))
        self._paths[gpid] = p
        return p


class Game:
    def __init__(self, bundle_path, managed_dir):
        self.bundle_path = bundle_path
        self.hdr, self.nodes, self.data = read_bundle(bundle_path)
        self.raw = {name: self.data[off:off + sz] for off, sz, f, name in self.nodes}
        self.sfs = {}
        for off, sz, f, name in self.nodes:
            if f & 4 and not name.startswith('Resources/'):
                self.sfs[name] = SerializedFile(self.raw[name], name)
        self.world = World(managed_dir)
        self.scripts = self._load_scripts()
        self._layouts = {}
        self._hier = {}

    def _load_scripts(self):
        sf = self.sfs[SCRIPTS_FILE]
        scripts = {}
        for o in sf.objs:
            if o['cls'] == 115:  # MonoScript
                d = sf.data(o)
                _, p = rstr(d, 0); p += 20
                cn, p = rstr(d, p); ns, p = rstr(d, p); asm, p = rstr(d, p)
                scripts[o['pid']] = (cn, ns, asm)
        return scripts

    def files_with_behaviours(self):
        return [n for n, sf in self.sfs.items() if any(o['cls'] == 114 for o in sf.objs)]

    def hierarchy(self, file):
        if file not in self._hier: self._hier[file] = Hierarchy(self.sfs[file])
        return self._hier[file]

    def resolve_file(self, sf, fid):
        return sf.name if fid == 0 else os.path.basename(sf.ext[fid - 1])

    def iter_mbs(self, files=None):
        """yield (file, sf, obj, script, vals, reader) for every MonoBehaviour whose layout parses exactly."""
        for f in (files or self.files_with_behaviours()):
            sf = self.sfs[f]
            for o in sf.objs:
                if o['cls'] != 114: continue
                d = sf.data(o)
                fid, pid = struct.unpack_from('<iq', d, 16)
                if self.resolve_file(sf, fid) != SCRIPTS_FILE or pid not in self.scripts: continue
                sc = self.scripts[pid]
                if sc not in self._layouts:
                    try:
                        self._layouts[sc] = self.world.script_layout(sc[2], sc[1], sc[0])
                    except Exception:
                        self._layouts[sc] = None
                lay = self._layouts[sc]
                if lay is None: continue
                try:
                    vals, r = parse_mb(self.world, lay, d)
                except Exception:
                    continue
                if r.p != len(d): continue
                yield f, sf, o, sc, vals, r

    def strings(self):
        """Every string field of every parsed MonoBehaviour."""
        out = []
        for f, sf, o, sc, vals, r in self.iter_mbs():
            for field, off, s in r.strings:
                if not s.strip(): continue
                out.append(dict(file=f, pid=o['pid'], script=sc[0], field=field, off=off, text=s))
        return out

    def fonts(self):
        """Embedded TTF data of every Font object: {font name: bytes}."""
        res = {}
        for name, sf in self.sfs.items():
            for o in sf.objs:
                if o['cls'] != 128: continue
                d = sf.data(o)
                fname, _ = rstr(d, 0)
                for sig in (b'\x00\x01\x00\x00', b'OTTO', b'true'):
                    i = d.find(sig)
                    found = False
                    while i >= 0:
                        if i >= 4:
                            n, = struct.unpack_from('<i', d, i - 4)
                            if 1000 < n <= len(d) - i:
                                res[fname] = d[i:i + n]; found = True; break
                        i = d.find(sig, i + 1)
                    if found: break
        return res

    def font_asset_sources(self):
        """(file, TMP_FontAsset pathID) -> name of its source Font."""
        res = {}
        for f, sf, o, sc, vals, r in self.iter_mbs():
            if sc[0] != 'TMP_FontAsset': continue
            fid, pid = vals.get('m_SourceFontFile', (0, 0))
            tf = self.resolve_file(sf, fid)
            if tf in self.sfs and pid in self.sfs[tf].byid:
                fo = self.sfs[tf].byid[pid]
                if fo['cls'] == 128:
                    res[(f, o['pid'])] = rstr(self.sfs[tf].data(fo), 0)[0]
        return res

    def tmp_components(self, files=None):
        """Every TextMeshProUGUI with layout-relevant values and field offsets."""
        if not hasattr(self, '_fa'): self._fa = self.font_asset_sources()
        for f, sf, o, sc, vals, r in self.iter_mbs(files):
            if sc[0] != 'TextMeshProUGUI': continue
            h = self.hierarchy(f)
            _, gpid = struct.unpack_from('<iq', sf.data(o), 0)
            fid, fpid = vals['m_fontAsset']
            font = self._fa.get((self.resolve_file(sf, fid), fpid), 'Roboto-Regular')
            yield dict(file=f, pid=o['pid'], gpid=gpid, go=h.path(gpid), text=vals['m_text'], font=font,
                       size=vals['m_fontSize'], auto=bool(vals['m_enableAutoSizing']), smin=vals['m_fontSizeMin'],
                       smax=vals['m_fontSizeMax'], style=vals['m_fontStyle'], spacing=vals['m_characterSpacing'],
                       wrap=vals['m_TextWrappingMode'], margin=vals['m_margin'], rect=h.rect.get(gpid),
                       offs={k: r.offs[k] for k in ('m_enableAutoSizing', 'm_fontSizeMin', 'm_fontSizeMax',
                                                    'm_TextWrappingMode', 'm_fontSize')})
