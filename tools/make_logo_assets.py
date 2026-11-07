"""
Builds every Argus logo variant from the original artwork.

    python tools/make_logo_assets.py assets/source/argus-logo-original.webp assets/logo

Needs: Pillow (with WebP) and numpy.

The owl always keeps its original colours. Because it is mostly black it needs a light backing on dark screens,
so the dark-UI variants put it on a light rounded tile.

Variants written to the output folder
  argus-logo.png               full logo (owl + ARGUS), original colours, for light backgrounds
  argus-logo-on-dark.png       full logo for dark backgrounds: owl on a light tile + white ARGUS text
  argus-icon.png               owl only, original colours, transparent background
  argus-icon-tile-light.png    owl on a light rounded tile (sidebar, hero, app icon)
  argus-wordmark.png           text only, black
  argus-wordmark-light.png     text only, white
  argus-wordmark-accent.png    text only, accent blue (matches the UI accent)
  argus.ico                    multi-size app icon (from the light tile)
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ACCENT = (127, 182, 230)      # soft blue, same as the accent in the UI
WHITE = (244, 244, 247)
TILE_LIGHT = (240, 240, 244)
TILE_BORDER = (200, 200, 208)


def clean_alpha(im: Image.Image) -> Image.Image:
    """The source is ~99% opaque but stored as 250-254; snap that to solid and drop faint dust."""
    a = np.array(im.convert("RGBA")).astype(np.float32)
    al = a[..., 3]
    al = np.where(al >= 238, 255, al)
    al = np.where(al <= 6, 0, al)
    a[..., 3] = al
    a[al == 0, :3] = 0
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def bands(im: Image.Image):
    vis = np.array(im)[..., 3] > 40
    rows = vis.sum(axis=1)
    out, start = [], None
    for y, v in enumerate(rows):
        if v > 0 and start is None:
            start = y
        if v == 0 and start is not None:
            out.append((start, y - 1)); start = None
    if start is not None:
        out.append((start, len(rows) - 1))
    return out


def crop_pad(im: Image.Image, pad_ratio=0.04, square=False) -> Image.Image:
    box = im.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
    c = im.crop(box)
    w, h = c.size
    pad = int(max(w, h) * pad_ratio)
    cw, ch = (max(w, h), max(w, h)) if square else (w, h)
    canvas = Image.new("RGBA", (cw + 2 * pad, ch + 2 * pad), (0, 0, 0, 0))
    canvas.paste(c, (pad + (cw - w) // 2, pad + (ch - h) // 2), c)
    return canvas


def recolor(im: Image.Image, color) -> Image.Image:
    a = np.array(im)
    a[..., 0], a[..., 1], a[..., 2] = color
    return Image.fromarray(a, "RGBA")


def stack(icon: Image.Image, word: Image.Image, gap_ratio=0.05) -> Image.Image:
    w = max(icon.width, word.width)
    gap = int(icon.height * gap_ratio)
    out = Image.new("RGBA", (w, icon.height + gap + word.height), (0, 0, 0, 0))
    out.paste(icon, ((w - icon.width) // 2, 0), icon)
    out.paste(word, ((w - word.width) // 2, icon.height + gap), word)
    return out


def tile(icon: Image.Image, bg=TILE_LIGHT, border=TILE_BORDER, size=512, fill=0.80, radius=0.22) -> Image.Image:
    t = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(t)
    r = int(size * radius)
    d.rounded_rectangle((0, 0, size - 1, size - 1), r, fill=bg + (255,), outline=border + (255,), width=max(2, size // 128))
    ic = icon.copy()
    ic.thumbnail((int(size * fill), int(size * fill)), Image.LANCZOS)
    t.paste(ic, ((size - ic.width) // 2, (size - ic.height) // 2), ic)
    return t


def main(src, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    im = clean_alpha(Image.open(src))
    b = bands(im)
    if len(b) != 2:
        raise SystemExit(f"expected owl + text bands, found {b}")
    (iy0, iy1), (ty0, ty1) = b
    mid = (iy1 + ty0) // 2

    icon = crop_pad(im.crop((0, 0, im.width, mid)), square=True)
    word = crop_pad(im.crop((0, mid, im.width, im.height)), pad_ratio=0.03)
    word_dark = recolor(word, (0, 0, 0))
    word_light = recolor(word, WHITE)
    word_accent = recolor(word, ACCENT)

    save = lambda img, name: img.save(os.path.join(out_dir, name))
    save(crop_pad(stack(icon, word_dark)), "argus-logo.png")
    save(crop_pad(stack(tile(icon, size=int(word.width * 0.85), fill=0.86), word_light)), "argus-logo-on-dark.png")
    save(icon, "argus-icon.png")
    save(word_dark, "argus-wordmark.png")
    save(word_light, "argus-wordmark-light.png")
    save(word_accent, "argus-wordmark-accent.png")
    app_tile = tile(icon, fill=0.90, radius=0.20)       # bigger owl so it stays readable at 16-32 px
    save(app_tile, "argus-icon-tile-light.png")
    app_tile.save(os.path.join(out_dir, "argus.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", sorted(os.listdir(out_dir)))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
