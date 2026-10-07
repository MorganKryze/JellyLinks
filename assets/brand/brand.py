"""Composes the JellyLinks images (concept C): the name on the left, a large gradient chain link cut by the right edge.
Writes assets/brand/{catalog,banner,social,mark}.svg from lettering.json; build.sh renders them."""
import json
import pathlib

HERE = pathlib.Path(__file__).parent
LETTERING = json.loads((HERE / "lettering.json").read_text())
INK, PURPLE, BLUE, WHITE, MUTED = "#0D1124", "#AA5CC3", "#00A4DC", "#FFFFFF", "#B9BFD6"

# Per image: canvas, name position (left x, baseline y, cap height), link centre, ring length / height / stroke, tagline.
LAYOUTS = {
    "catalog": dict(w=1536, h=1024, x=200, base=562, cap=100, cx=1180, cy=512, length=620, height=270, stroke=64, tagline=None),
    "banner": dict(w=1531, h=520, x=150, base=308, cap=96, cx=1150, cy=260, length=520, height=225, stroke=54, tagline=None),
    "social": dict(w=1280, h=640, x=110, base=328, cap=84, cx=1080, cy=320, length=470, height=200, stroke=48,
                   tagline=dict(base=396, cap=21)),
}


def ring(x0, length, r):
    """A stadium (rounded rectangle with round ends) centred on (x0, 0)."""
    x1, x2 = x0 - length / 2 + r, x0 + length / 2 - r
    return f"M{x1:.1f},{-r:.1f} H{x2:.1f} A{r:.1f},{r:.1f} 0 0 1 {x2:.1f},{r:.1f} H{x1:.1f} A{r:.1f},{r:.1f} 0 0 1 {x1:.1f},{-r:.1f} Z"


def link(uid, cx, cy, length, height, stroke, angle=-45, gap=None, overlap=0.6):
    """Two rings that really interlock: A passes over B at the top crossing and under it at the bottom one.
    Each ring is masked where the other passes over it, so the gaps show the background whatever it is."""
    r = height / 2
    gap = stroke * 0.18 if gap is None else gap
    d = length - height * overlap
    a, b = ring(-d / 2, length, r), ring(d / 2, length, r)
    left, right = -d / 2 - length / 2, d / 2 + length / 2
    x1, x2 = d / 2 - length / 2 - stroke, -d / 2 + length / 2 + stroke
    box = f'x="{left - stroke:.1f}" y="{-r - stroke:.1f}" width="{right - left + 2 * stroke:.1f}" height="{height + 2 * stroke:.1f}"'
    paint = f"url(#{uid}-g)"
    return f"""<defs>
  <linearGradient id="{uid}-g" gradientUnits="userSpaceOnUse" x1="{left:.1f}" y1="0" x2="{right:.1f}" y2="0">
    <stop offset="0" stop-color="{PURPLE}"/><stop offset="1" stop-color="{BLUE}"/>
  </linearGradient>
  <clipPath id="{uid}-top"><rect x="{x1:.1f}" y="{-r - stroke:.1f}" width="{x2 - x1:.1f}" height="{r + stroke:.1f}"/></clipPath>
  <clipPath id="{uid}-bottom"><rect x="{x1:.1f}" y="0" width="{x2 - x1:.1f}" height="{r + stroke:.1f}"/></clipPath>
  <mask id="{uid}-under-a" maskUnits="userSpaceOnUse" {box}>
    <rect {box} fill="#fff"/><path d="{a}" fill="none" stroke="#000" stroke-width="{stroke + 2 * gap:.1f}" clip-path="url(#{uid}-top)"/>
  </mask>
  <mask id="{uid}-under-b" maskUnits="userSpaceOnUse" {box}>
    <rect {box} fill="#fff"/><path d="{b}" fill="none" stroke="#000" stroke-width="{stroke + 2 * gap:.1f}" clip-path="url(#{uid}-bottom)"/>
  </mask>
</defs>
<g transform="translate({cx:.1f},{cy:.1f}) rotate({angle})" fill="none" stroke="{paint}" stroke-width="{stroke}">
  <path d="{b}" mask="url(#{uid}-under-a)"/>
  <path d="{a}" mask="url(#{uid}-under-b)"/>
</g>"""


def lettering(key, x, base, cap, fill):
    """The outlined text, scaled so its capitals are `cap` px tall."""
    item = LETTERING[key]
    s = cap / item["capHeight"]
    return f'<path transform="translate({x:.1f},{base:.1f}) scale({s:.4f})" d="{item["d"]}" fill="{fill}"/>'


def svg(w, h, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">\n'
            f'<rect width="{w}" height="{h}" fill="{INK}"/>\n{body}\n</svg>\n')


def compose(name, p):
    body = [link(name, p["cx"], p["cy"], p["length"], p["height"], p["stroke"]), lettering("wordmark", p["x"], p["base"], p["cap"], WHITE)]
    if p["tagline"]:
        body.append(lettering("tagline", p["x"] + 4, p["tagline"]["base"], p["tagline"]["cap"], MUTED))
    return svg(p["w"], p["h"], "\n".join(body))


def mark():
    """The square mark: the whole link, not cut, on an ink tile; legible at 32 px."""
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512">\n'
            f'<rect width="512" height="512" rx="112" fill="{INK}"/>\n'
            f'{link("mark", 256, 256, 312, 142, 36)}\n</svg>\n')


for name, layout in LAYOUTS.items():
    (HERE / f"{name}.svg").write_text(compose(name, layout))
(HERE / "mark.svg").write_text(mark())
print("composed:", ", ".join([*LAYOUTS, "mark"]))
