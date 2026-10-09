"""Text-fit analysis for TextMeshProUGUI: measure a text against its rect using the real font metrics and
produce byte patches (NoWrap / auto-size) for components that would overflow."""
import io, re, struct
from fontTools.ttLib import TTFont

LINE_H = {'Roboto': 1.172, 'RobotoMono': 1.319}
TAG = re.compile(r'<[^>]*>')


def visible(s):
    return TAG.sub('', s)


class Fonts:
    def __init__(self, ttf_by_name):
        self.f = {}
        for name, data in ttf_by_name.items():
            tt = TTFont(io.BytesIO(data))
            self.f[name] = (tt.getBestCmap(), tt['hmtx'], tt['head'].unitsPerEm)

    def width(self, s, font, size, spacing=0.0, synth_bold=False):
        cmap, hmtx, upem = self.f.get(font, self.f['Roboto-Regular'])
        w = 0.0
        for ch in s:
            g = cmap.get(ord(ch))
            adv = hmtx[g][0] if g else upem * 0.6
            w += adv * size / upem + spacing * size * 0.01 + (0.07 * size if synth_bold else 0)
        return w


def wrap_lines(text, maxw, measure):
    """Greedy word wrap; returns (number of lines, widest line)."""
    lines = 0; widest = 0.0
    for para in visible(text).split('\n'):
        if not para:
            lines += 1; continue
        cur = ''
        for w in para.split(' '):
            cand = (cur + ' ' + w) if cur else w
            if measure(cand) <= maxw or not cur:
                cur = cand
            else:
                lines += 1; widest = max(widest, measure(cur)); cur = w
        lines += 1; widest = max(widest, measure(cur))
    return lines, widest


def analyse(comp, text, fonts):
    if not comp['rect']: return dict(status='unknown')
    w, h = comp['rect']
    m = comp['margin']
    w -= (m[0] + m[2]); h -= (m[1] + m[3])
    if w <= 2 or h <= 2: return dict(status='unknown')
    size = comp['smax'] if comp['auto'] else comp['size']
    font, style = comp['font'], comp['style']
    txt = text
    if style & 16: txt = txt.upper()
    if style & 8: txt = txt.lower()
    synth = bool(style & 1) and 'Bold' not in font
    meas = lambda s: fonts.width(s, font, size, comp['spacing'], synth)
    lh = size * LINE_H['RobotoMono' if 'Mono' in font else 'Roboto']
    cap = max(1, int((h + 0.25 * lh) // lh))
    if comp['wrap'] in (0, 3):
        parts = visible(txt).split('\n')
        lines = len(parts); widest = max(meas(x) for x in parts)
        fits = widest <= w * 1.02 and lines <= cap
    else:
        lines, widest = wrap_lines(txt, w * 1.02, meas)
        fits = lines <= cap and widest <= w * 1.02
    return dict(status='fit' if fits else 'overflow', need_lines=lines, cap_lines=cap, width=round(widest),
                box=(round(w), round(h)), size=size)


def fix_patch(comp, text, fonts, min_ratio=0.62):
    """Byte patches making an overflowing component fit: single-line boxes -> NoWrap + auto-size,
    multi-line boxes -> auto-size. Returns (patches, info)."""
    a = analyse(comp, text, fonts)
    if a['status'] != 'overflow': return [], a
    size = a['size']
    o = comp['offs']
    patches = []
    single = a['cap_lines'] == 1 and '\n' not in text
    if single and comp['wrap'] == 1:
        patches.append((o['m_TextWrappingMode'], struct.pack('<i', 0)))
    smin = max(8.0, round(size * min_ratio, 1))
    patches.append((o['m_enableAutoSizing'], b'\x01'))
    patches.append((o['m_fontSizeMin'], struct.pack('<f', smin)))
    patches.append((o['m_fontSizeMax'], struct.pack('<f', size)))
    need = size
    if single:
        synth = bool(comp['style'] & 1) and 'Bold' not in comp['font']
        width = fonts.width(visible(text), comp['font'], size, comp['spacing'], synth)
        need = size * min(1.0, a['box'][0] / max(1.0, width))
    a['est_size'] = round(max(smin, need), 1)
    return patches, a
