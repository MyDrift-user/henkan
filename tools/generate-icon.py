"""Draws the Henkan icon and writes every asset the package and the exe need.

Two pages, the back one plain and the front one in the accent gradient with an
arrow on it: a file that becomes another file. Drawn at eight times the target
size and scaled down, which gives clean edges without a vector renderer.
Small sizes get a simplified drawing, since text lines and a thin arrow turn
to mush at 16 pixels.

Run from the repository root: python tools/generate-icon.py
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

ASSETS = os.path.join(os.path.dirname(__file__), "..", "src", "Henkan.App", "Assets")
SCALE = 8

BACK_FILL = (240, 242, 250, 255)
BACK_EDGE = (190, 196, 222, 255)
BACK_LINES = (196, 202, 226, 255)
FRONT_START = (140, 108, 255)
FRONT_END = (48, 132, 255)
FOLD = (205, 196, 255, 255)
ARROW = (255, 255, 255, 255)


def page_mask(size, box, radius, fold):
    """A page: a rounded rectangle with its top right corner folded away."""
    mask = Image.new("L", (size, size), 0)
    draw = ImageDraw.Draw(mask)
    x0, y0, x1, y1 = box
    draw.rounded_rectangle(box, radius=radius, fill=255)
    draw.polygon([(x1 - fold, y0 - 1), (x1 + 1, y0 - 1), (x1 + 1, y0 + fold)], fill=0)
    return mask


def fold_mask(size, box, fold):
    mask = Image.new("L", (size, size), 0)
    x0, y0, x1, y1 = box
    ImageDraw.Draw(mask).polygon([(x1 - fold, y0), (x1 - fold, y0 + fold), (x1, y0 + fold)], fill=255)
    return mask


def gradient(size, start, end):
    """A diagonal gradient from the top left to the bottom right."""
    small = Image.new("RGBA", (64, 64))
    pixels = small.load()
    for y in range(64):
        for x in range(64):
            t = (x + y) / 126
            pixels[x, y] = tuple(round(a + (b - a) * t) for a, b in zip(start, end)) + (255,)
    return small.resize((size, size), Image.BICUBIC)


def arrow(draw, centre, radius, width, start_deg, end_deg, head):
    """A curved arrow, clockwise from start to end, whose head continues the stroke."""
    cx, cy = centre
    mid = radius - width / 2

    # The stroke stops where the head's base begins, so the two do not overlap.
    back_off = math.degrees(head * 0.55 / mid)
    stroke_end = end_deg - back_off
    box = (cx - radius, cy - radius, cx + radius, cy + radius)
    draw.arc(box, start=start_deg, end=stroke_end, fill=ARROW, width=width)

    # A round tail.
    a = math.radians(start_deg)
    tx, ty = cx + mid * math.cos(a), cy + mid * math.sin(a)
    draw.ellipse((tx - width / 2, ty - width / 2, tx + width / 2, ty + width / 2), fill=ARROW)

    # The head: its base across the stroke, its tip ahead along the direction of travel.
    a = math.radians(stroke_end)
    bx, by = cx + mid * math.cos(a), cy + mid * math.sin(a)
    tangent = (-math.sin(a), math.cos(a))
    normal = (math.cos(a), math.sin(a))
    spread = head * 0.62
    tip = (bx + tangent[0] * head, by + tangent[1] * head)
    outer = (bx + normal[0] * spread, by + normal[1] * spread)
    inner = (bx - normal[0] * spread, by - normal[1] * spread)
    draw.polygon([tip, outer, inner], fill=ARROW)


def draw_icon(size, simple=None):
    """The icon on a transparent square of the given size."""
    if simple is None:
        simple = size <= 24

    canvas = size * SCALE
    u = canvas / 100.0  # the drawing is laid out on a 100 by 100 grid
    image = Image.new("RGBA", (canvas, canvas), (0, 0, 0, 0))

    back = (10 * u, 6 * u, 62 * u, 72 * u)
    front = (32 * u, 24 * u, 92 * u, 95 * u)

    # Back page, with lines of text where there is room to see them.
    back_mask = page_mask(canvas, back, 7 * u, 15 * u)
    edge = Image.new("RGBA", (canvas, canvas), BACK_EDGE)
    image.paste(edge, (0, 0), back_mask)
    inner = page_mask(canvas, tuple(v + d for v, d in zip(back, (1.6 * u, 1.6 * u, -1.6 * u, -1.6 * u))), 5.5 * u, 14 * u)
    image.paste(Image.new("RGBA", (canvas, canvas), BACK_FILL), (0, 0), inner)
    image.paste(Image.new("RGBA", (canvas, canvas), BACK_EDGE), (0, 0), fold_mask(canvas, back, 15 * u))

    if not simple:
        lines = ImageDraw.Draw(image)
        for i, width in enumerate((30, 34, 26, 18)):
            y = (22 + i * 9) * u
            if y > 24 * u + 0.1 and (18 + width) * u > 32 * u:
                width = 12  # stop short of the front page
            lines.rounded_rectangle((18 * u, y, (18 + width) * u, y + 3.2 * u), radius=1.6 * u, fill=BACK_LINES)

    # A soft shadow lifts the front page off the back one.
    front_mask = page_mask(canvas, front, 8 * u, 17 * u)
    shadow = Image.new("RGBA", (canvas, canvas), (20, 20, 60, 0))
    shadow_alpha = front_mask.point(lambda v: v * 0.35).filter(ImageFilter.GaussianBlur(3 * u))
    shadow.putalpha(shadow_alpha)
    image = Image.alpha_composite(image, shadow.transform(shadow.size, Image.AFFINE, (1, 0, -1.2 * u, 0, 1, -2 * u)))

    image.paste(gradient(canvas, FRONT_START, FRONT_END), (0, 0), front_mask)
    image.paste(Image.new("RGBA", (canvas, canvas), FOLD), (0, 0), fold_mask(canvas, front, 17 * u))

    # Two arrows chasing each other round: one thing turning into another.
    draw = ImageDraw.Draw(image)
    centre = (62 * u, 62 * u)
    if simple:
        radius, width, head = 20 * u, round(7.5 * u), 12 * u
    else:
        radius, width, head = 19 * u, round(5.5 * u), 10 * u
    arrow(draw, centre, radius, width, 195, 345, head)
    arrow(draw, centre, radius, width, 15, 165, head)

    return image.resize((size, size), Image.LANCZOS)


def on_canvas(width, height, icon_size):
    """The icon centred on a transparent canvas, for tiles and the splash screen."""
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    icon = draw_icon(icon_size)
    canvas.paste(icon, ((width - icon_size) // 2, (height - icon_size) // 2), icon)
    return canvas


def main():
    os.makedirs(ASSETS, exist_ok=True)

    def save(image, name):
        image.save(os.path.join(ASSETS, name))
        print(name, image.size)

    save(draw_icon(44), "Square44x44Logo.png")

    # Exact sizes for the taskbar, Start and Explorer, which otherwise scale
    # the 44 pixel one. The unplated ones are what Windows 11 actually shows.
    for target in (16, 20, 24, 32, 48, 256):
        save(draw_icon(target), f"Square44x44Logo.targetsize-{target}.png")
        save(draw_icon(target), f"Square44x44Logo.targetsize-{target}_altform-unplated.png")
        save(draw_icon(target), f"Square44x44Logo.targetsize-{target}_altform-lightunplated.png")

    # The title bar shows it at 20 pixels; drawn at twice that for high DPI
    # screens, in the simplified form that stays legible that small.
    save(draw_icon(40, simple=True), "TitleBarIcon.png")
    save(draw_icon(50), "StoreLogo.png")
    save(on_canvas(150, 150, 104), "Square150x150Logo.png")
    save(on_canvas(310, 150, 104), "Wide310x150Logo.png")
    save(on_canvas(620, 300, 200), "SplashScreen.png")

    # The exe icon, which Explorer also shows in the context menu. Each size is
    # drawn for itself rather than scaled from the largest.
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    images = [draw_icon(s) for s in sizes]
    images[-1].save(os.path.join(ASSETS, "Henkan.ico"), format="ICO",
                    sizes=[(s, s) for s in sizes], append_images=images[:-1])
    print("Henkan.ico", sizes)


if __name__ == "__main__":
    main()
