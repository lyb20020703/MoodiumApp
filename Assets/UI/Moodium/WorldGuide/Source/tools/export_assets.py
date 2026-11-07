"""Export every world-guide sprite to Unity/Resources/MoodiumWorldGuide/ (transparent PNG, 1.5x).

Usage (macOS, needs Google Chrome, Node and ffmpeg):
    python3 Source/tools/export_assets.py

Card faces come from Source/design/*.dc.html rendered with flat=true (opaque glass, no outer shadow)
and hideControls=true (the buttons are exported separately so Unity can make them clickable).
"""
import json
import os
import shutil
import subprocess
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
DESIGN = os.path.join(ROOT, "Source", "design")
OUT = os.path.join(ROOT, "Unity", "Resources", "MoodiumWorldGuide")
CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
SCALE = 1.5
FONT = "-apple-system, 'SF Pro Text', Inter, 'PingFang SC', 'Noto Sans SC', sans-serif"
FONT_LINK = '<link href="https://fonts.googleapis.com/css2?family=Inter:wght@500;600&amp;family=Noto+Sans+SC:wght@500;700&amp;display=swap" rel="stylesheet">'

WORLDS = {
    "candy": {"rgb": "242,125,182", "deep": "206,86,154"},
    "nature": {"rgb": "85,196,127", "deep": "46,150,90"},
}
CARDS = ["press", "free", "rhythm", "breath"]
LANGS = ["zh", "en"]
# label, fixed width in CSS px (labels never wrap; widths keep the arrow circle on the right)
BUTTONS = {
    "next": {"zh": ("下一张", 140), "en": ("Next", 128)},
    "start": {"zh": ("开始按压", 158), "en": ("Start", 130)},
    "explore": {"zh": ("去探索", 140), "en": ("Explore", 148)},
}
SKIP = {"zh": "跳过", "en": "Skip"}

tmp = tempfile.mkdtemp(prefix="world-guide-")
profile = os.path.join(tmp, "chrome")


def shoot(html, name, w, h):
    page = os.path.join(tmp, name + ".html")
    with open(page, "w") as f:
        f.write(html)
    shot = os.path.join(tmp, name + "_full.png")
    for attempt in range(3):
        try:
            subprocess.run([CHROME, "--headless=old", "--disable-gpu", "--hide-scrollbars",
                            "--default-background-color=00000000", f"--force-device-scale-factor={SCALE}",
                            f"--user-data-dir={profile}", f"--window-size={max(w, 700)},{max(h, 400)}",
                            "--virtual-time-budget=8000", f"--screenshot={shot}", "file://" + page],
                           check=True, capture_output=True, timeout=90)
            break
        except subprocess.TimeoutExpired:
            if attempt == 2:
                raise
    pw, ph = round(w * SCALE), round(h * SCALE)
    out = os.path.join(OUT, name + ".png")
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", shot, "-vf", f"crop={pw}:{ph}:0:0",
                    "-pix_fmt", "rgba", out], check=True)
    print(f"{name}.png  {pw}x{ph}")


def corners_clear(path, w, h):
    """True when the four corner pixels (just inside the rounded corners) are fully transparent.
    Headless Chrome occasionally paints a stray block; this catches it."""
    raw = subprocess.run(["ffmpeg", "-loglevel", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgba", "-"],
                         check=True, capture_output=True).stdout
    alpha = lambda x, y: raw[(y * w + x) * 4 + 3]
    return all(alpha(x, y) == 0 for x, y in ((3, 3), (w - 4, 3), (3, h - 4), (w - 4, h - 4)))


def component(file, props, name, w, h):
    html = os.path.join(tmp, name + "_src.html")
    subprocess.run(["node", os.path.join(os.path.dirname(__file__), "render_component.js"),
                    os.path.join(DESIGN, file), json.dumps(props), html], check=True)
    with open(html) as f:
        page_html = f.read()
    for attempt in range(5):
        shoot(page_html, name, w, h)
        if corners_clear(os.path.join(OUT, name + ".png"), round(w * SCALE), round(h * SCALE)):
            return
        print(f"  {name}: stray paint in a corner, exporting again")
    raise RuntimeError(f"{name}: corners still not transparent after 5 attempts")


def page(body):
    return (f'<!doctype html><html><head><meta charset="utf-8">{FONT_LINK}'
            f'<style>html,body{{margin:0;background:transparent}}</style></head><body>{body}</body></html>')


def action_button(label, width, rgb, deep):
    arrow = ('<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="#FFFFFF" stroke-width="2.4" '
             'stroke-linecap="round" stroke-linejoin="round"><path d="M5 12h13M13 6l6 6-6 6"></path></svg>')
    return page(
        f'<div style="width:{width}px;height:54px;box-sizing:border-box;padding:0 9px 0 24px;border-radius:27px;'
        f'display:flex;align-items:center;justify-content:space-between;font:600 17px {FONT};color:#FFFFFF;'
        f'background:linear-gradient(180deg,rgba({rgb},0.98),rgba({deep},0.98));'
        f'box-shadow:inset 0 1.5px 0 rgba(255,255,255,0.6)">{label}'
        f'<span style="width:36px;height:36px;border-radius:50%;display:inline-flex;align-items:center;'
        f'justify-content:center;background:rgba(255,255,255,0.16)">{arrow}</span></div>')


def skip_button(label):
    return page(
        f'<div style="width:64px;height:34px;border-radius:17px;display:flex;align-items:center;justify-content:center;'
        f'font:500 14px {FONT};color:rgba(255,255,255,0.85);background:rgba(255,255,255,0.10)">{label}</div>')


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for world in WORLDS:
        for lang in LANGS:
            for card in CARDS:
                component("GuideCard.dc.html", {"world": world, "card": card, "lang": lang, "flat": True, "hideControls": True},
                          f"guide_card_{world}_{card}_{lang}", 520, 660)
            component("ExploreCard.dc.html", {"world": world, "lang": lang, "flat": True, "hideControls": True},
                      f"guide_explore_{world}_{lang}", 640, 560)
            for kind, labels in BUTTONS.items():
                label, width = labels[lang]
                shoot(action_button(label, width, WORLDS[world]["rgb"], WORLDS[world]["deep"]), f"guide_btn_{kind}_{world}_{lang}", width, 54)
    for lang in LANGS:
        shoot(skip_button(SKIP[lang]), f"guide_btn_skip_{lang}", 64, 34)
    shutil.rmtree(tmp, ignore_errors=True)
