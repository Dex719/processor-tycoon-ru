"""Build the Russian-localized data.unity3d and Assembly-CSharp.dll from the original game files."""
import collections, json, os, re, struct
from .game import Game
from .fit import Fonts, analyse, fix_patch
from .patch_assets import repack
from .patch_dll import patch as patch_dll
from .clr import Meta
from .il import ldstr_sites

# components whose strings are identifiers, technology names, generated company names or editor/debug data
SKIP_SCRIPTS = {'AICompany', 'CpuNameTemplate', 'IconPlaceholder', 'Company', 'ContractIcon', 'TMP_FontAsset',
                'TMP_SpriteAsset', 'TMP_Settings', 'TMP_StyleSheet', 'Text', 'DebugUIHandlerCanvas', 'EventTrigger',
                'StandaloneInputModule', 'ButtonLink', 'ProcessNode', 'Package', 'Frequency', 'CacheSize', 'Memory',
                'WaferSize', 'Multicore', 'ContractNameData', 'AIBehaviourController'}
SKIP_FILES = {'globalgamemanagers.assets', 'resources.assets'}


def translatable(e):
    if e['file'] in SKIP_FILES or e['script'] in SKIP_SCRIPTS: return False
    f = e['field']
    if f == 'm_Name' or 'AnimationTriggers' in f or 'm_OnClick' in f or 'm_Calls' in f: return False
    if 'TargetAssemblyTypeName' in f or 'm_MethodName' in f: return False
    return bool(re.search(r'[A-Za-z]{2}', re.sub(r'<[^>]+>', '', e['text'])))


def brand_target(e, brands):
    m = brands.get(e['script'])
    if not m or e['field'] == 'm_Name': return None
    if e['script'] == 'ContractNameData' and 'FirstNames' not in e['field']: return None
    if e['script'] == 'AIBehaviourController' and 'baseName' not in e['field']: return None
    return m.get(e['text'])


# ---------------- validation of translation files ----------------
PH = re.compile(r'\{\d+(?::[^}]*)?\}')
TOK = re.compile(r'#[A-Za-z_][A-Za-z0-9_]*')
ALLOWED = re.compile('^[\u0000-\u007FЀ-џ«»—–…№₽°²µ'
                     '×→ ³•·]*$')


def problems(src, ru):
    p = []
    if sorted(PH.findall(src)) != sorted(PH.findall(ru)): p.append('placeholders differ')
    if sorted(TOK.findall(src)) != sorted(TOK.findall(ru)): p.append('#tokens differ')
    if sorted(re.findall(r'<[^<>]*>', src)) != sorted(re.findall(r'<[^<>]*>', ru)): p.append('rich-text tags differ')
    if src.count('<') != ru.count('<') or src.count('>') != ru.count('>'): p.append('angle brackets differ')
    if (src[:1] == ' ') != (ru[:1] == ' ') or (src[-1:] == ' ') != (ru[-1:] == ' '): p.append('edge spaces differ')
    if src.count('\n') != ru.count('\n'): p.append('line break count differs')
    if src.count('{') != ru.count('{') or src.count('}') != ru.count('}'): p.append('brace count differs')
    if not ALLOWED.match(ru): p.append('characters outside the font set')
    if ru.strip() == '' and src.strip() != '': p.append('empty translation')
    return p


def load_translation(tr_dir):
    ld = lambda n: json.load(open(os.path.join(tr_dir, n), encoding='utf-8'))
    ui, code, brands = ld('ui.json'), ld('code.json'), ld('brands.json')
    errors = ['ui.json %r: %s' % (en, ', '.join(problems(en, ru))) for en, ru in ui.items() if problems(en, ru)]
    errors += ['code.json %r: %s' % (e['en'], ', '.join(problems(e['en'], e['ru']))) for e in code
               if problems(e['en'], e['ru'])]
    return ui, code, brands, errors


# ---------------- layout fixes ----------------
def auto_widen(game, ui, fonts, files=('level0', 'level1', 'sharedassets1.assets'), log=print):
    """Widen fixed-size buttons that are children of a Horizontal/VerticalLayoutGroup not controlling width, so the
    Russian label fits at its original font size. Returns {(file, text path): (button RectTransform pid, width)}."""
    res = {}
    for file in files:
        if file not in game.sfs: continue
        h = game.hierarchy(file)
        go_of = {t: g for t, g in h.tr_go.items() if t in h.rt}
        groups, ignored = {}, set()
        for f, s, o, sc, vals, r in game.iter_mbs([file]):
            _, g = struct.unpack_from('<iq', s.data(o), 0)
            if sc[0] in ('HorizontalLayoutGroup', 'VerticalLayoutGroup'): groups[g] = (sc[0], vals)
            if sc[0] == 'LayoutElement' and vals.get('m_IgnoreLayout'): ignored.add(g)
        want, texts = {}, collections.defaultdict(list)
        for c in game.tmp_components([file]):
            ru = ui.get(c['text'])
            if not ru or '\n' in ru: continue
            if analyse(c, c['text'], fonts)['status'] != 'fit' or analyse(c, ru, fonts)['status'] != 'overflow':
                continue
            trt = h.go_tr.get(c['gpid'])
            if trt not in h.rt or h.rt[trt][0:4] != (0.0, 0.0, 1.0, 1.0): continue
            P = h.tr_parent.get(trt)
            if P not in h.rt or h.rt[P][0:2] != h.rt[P][2:4]: continue
            grp = groups.get(go_of.get(h.tr_parent.get(P)))
            if not grp or grp[1].get('m_ChildControlWidth'): continue
            need = fonts.width(ru, c['font'], c['size'], c['spacing'], bool(c['style'] & 1) and 'Bold' not in c['font'])
            m = c['margin']
            new_w = need + max(0.0, -h.rt[trt][6]) + m[0] + m[2] + 12
            if new_w > h.rt[P][6]:
                want[P] = max(want.get(P, 0), new_w)
                texts[P].append(c['go'])
        rows = collections.defaultdict(list)
        for P in want: rows[h.tr_parent[P]].append(P)
        for G, Ps in rows.items():
            kind, vals = groups[go_of[G]]
            if kind == 'HorizontalLayoutGroup':
                widths = [want.get(k, h.rt[k][6]) for k in h.kids[G] if k in h.rt and go_of[k] not in ignored]
                total = sum(widths) + vals['m_Spacing'] * max(0, len(widths) - 1) + vals['m_Padding'][0] + vals['m_Padding'][1]
                gw = h.rect.get(go_of[G], (0, 0))[0]
                container = gw if gw > 1 else h.rect.get(go_of.get(h.tr_parent.get(G)), (0, 0))[0]
                if container <= 1 or total > container:
                    log('  row too wide, using auto-size instead: %s' % h.path(go_of[G]))
                    continue
            for P in Ps:
                for tg in texts[P]:
                    res[(file, tg)] = (P, round(want[P], 1))
    return res


def build(src_bundle, src_dll, managed_dir, tr_dir, out_dir, log=print):
    ui, code, brands, errors = load_translation(tr_dir)
    if errors:
        raise ValueError('translation files contain errors:\n  ' + '\n  '.join(errors[:30]))
    os.makedirs(out_dir, exist_ok=True)
    report = {}

    # ---------------- assets ----------------
    log('Чтение data.unity3d ...')
    game = Game(src_bundle, managed_dir)
    fp = collections.defaultdict(lambda: collections.defaultdict(lambda: ([], [])))
    tmp_new = {}
    used_ui = set()
    n_str = 0
    for e in game.strings():
        new = None
        if translatable(e) and e['text'] in ui:
            new = ui[e['text']]; used_ui.add(e['text'])
        b = brand_target(e, brands)
        if b is not None: new = b
        if new is None or new == e['text']: continue
        fp[e['file']][e['pid']][0].append((e['off'], e['text'], new))
        if e['script'] == 'TextMeshProUGUI' and e['field'] == 'm_text': tmp_new[(e['file'], e['pid'])] = new
        n_str += 1
    report['asset_strings'] = n_str
    report['ui_unused'] = sorted(set(ui) - used_ui)

    fonts = Fonts(game.fonts())
    widen = auto_widen(game, ui, fonts, log=log)
    for (file, path), (P, width) in widen.items():
        fp[file][P][1].append((game.hierarchy(file).sd_off[P], struct.pack('<f', float(width))))
    report['widened'] = len(set((f, P) for (f, _), (P, _) in widen.items()))

    autosize = 0
    for c in game.tmp_components():
        key = (c['file'], c['pid'])
        if key not in tmp_new or (c['file'], c['go']) in widen: continue
        if analyse(c, c['text'], fonts)['status'] != 'fit': continue
        patches, a = fix_patch(c, tmp_new[key], fonts)
        if patches:
            fp[c['file']][c['pid']][1].extend(patches); autosize += 1
    report['autosized'] = autosize

    # dynamic font assets: allow extra atlas pages so all Cyrillic glyphs fit
    for f, sf, o, sc, vals, r in game.iter_mbs():
        if sc[0] == 'TMP_FontAsset' and vals.get('m_AtlasPopulationMode') == 1 \
                and not vals.get('m_IsMultiAtlasTexturesEnabled'):
            fp[f][o['pid']][1].append((r.offs['m_IsMultiAtlasTexturesEnabled'], b'\x01'))

    log('Запись data.unity3d ...')
    report['changed_objects'] = repack(game, os.path.join(out_dir, 'data.unity3d'),
                                       {f: dict(v) for f, v in fp.items()})

    # ---------------- code ----------------
    log('Патч Assembly-CSharp.dll ...')
    sites = ldstr_sites(Meta(src_dll))
    by_text = collections.defaultdict(list)
    for s in sites: by_text[s['text']].append(s)
    site_map, missing = {}, []
    for e in code:
        cand = by_text.get(e['en'], [])
        if e['sites'] == '*':
            chosen = cand
        else:
            keys = set(e['sites'])
            chosen = [s for s in cand if '%s#%d' % (s['method'], s['occ']) in keys]
        if not chosen: missing.append(e['en'])
        for s in chosen: site_map[s['file_off']] = e['ru']
    dst_dll = os.path.join(out_dir, 'Assembly-CSharp.dll')
    patch_dll(src_dll, dst_dll, site_map)
    # verify: every site now holds the expected string
    after = ldstr_sites(Meta(dst_dll))
    bad = sum(1 for a, s in zip(after, sites) if a['text'] != site_map.get(s['file_off'], s['text']))
    if len(after) != len(sites) or bad:
        raise RuntimeError('DLL verification failed (%d mismatches)' % bad)
    report['code_sites'] = len(site_map)
    report['code_missing'] = missing
    return report
