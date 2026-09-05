"""Renders the button faces from Textures.ButtonFace, at the size they are used.

Nine-slicing is simulated: corners kept, middle stretched, which is what Unity
does with these sprites. If the gradient smears or the lit edge vanishes at the
real aspect, it shows up here rather than after a game restart.
"""
from PIL import Image

RADIUS = 8
SRC = RADIUS * 4          # 32, the texture Textures.ButtonFace generates
W, H = 200, 44            # a real button


def lighten(c, a):
    return tuple(max(0, min(255, int(v + a * 255))) for v in c)


def corner_distance(x, y, size, radius):
    cx = min(max(x + 0.5, radius), size - radius)
    cy = min(max(y + 0.5, radius), size - radius)
    return (((x + 0.5 - cx) ** 2) + ((y + 0.5 - cy) ** 2)) ** 0.5


def face(top, bottom, border, border_width=2):
    """The texture, exactly as the C# builds it."""
    img = Image.new('RGBA', (SRC, SRC), (0, 0, 0, 0))
    px = img.load()
    for y in range(SRC):
        up = y / (SRC - 1)
        fill = tuple(int(bottom[i] + (top[i] - bottom[i]) * up) for i in range(3))
        for x in range(SRC):
            d = corner_distance(x, y, SRC, RADIUS)
            alpha = max(0.0, min(1.0, RADIUS - d + 0.5))
            colour = fill
            if border_width > 0 and d > RADIUS - border_width - 0.5:
                colour = border
                if y > SRC - RADIUS:
                    colour = tuple(int(colour[i] + (lighten(border, 0.22)[i] - colour[i]) * 0.85) for i in range(3))
                elif y < RADIUS:
                    colour = tuple(int(colour[i] + (lighten(border, -0.16)[i] - colour[i]) * 0.85) for i in range(3))
            px[x, SRC - 1 - y] = (colour[0], colour[1], colour[2], int(alpha * 255))
    return img


def nine_slice(tex, w, h, r):
    """What Unity draws: corners untouched, edges and middle stretched."""
    out = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    s = tex.size[0]
    boxes = [
        ((0, 0, r, r), (0, 0, r, r)),
        ((s - r, 0, s, r), (w - r, 0, w, r)),
        ((0, s - r, r, s), (0, h - r, r, h)),
        ((s - r, s - r, s, s), (w - r, h - r, w, h)),
        ((r, 0, s - r, r), (r, 0, w - r, r)),
        ((r, s - r, s - r, s), (r, h - r, w - r, h)),
        ((0, r, r, s - r), (0, r, r, h - r)),
        ((s - r, r, s, s - r), (w - r, r, w, h - r)),
        ((r, r, s - r, s - r), (r, r, w - r, h - r)),
    ]
    for src, dst in boxes:
        piece = tex.crop(src).resize((dst[2] - dst[0], dst[3] - dst[1]), Image.BILINEAR)
        out.paste(piece, (dst[0], dst[1]))
    return out


CHIP_TOP, CHIP_BOTTOM, CHIP_EDGE = (43, 46, 48), (23, 26, 28), (77, 71, 61)
BRASS_TOP, BRASS_BOTTOM, BRASS_EDGE = (140, 110, 41), (84, 64, 20), (184, 148, 66)
GOLD = (199, 173, 97)

VARIANTS = [
    ('neutral', CHIP_TOP, CHIP_BOTTOM, CHIP_EDGE),
    ('hover', lighten(CHIP_TOP, 0.09), lighten(CHIP_BOTTOM, 0.07), GOLD),
    ('pressed', lighten(CHIP_BOTTOM, 0.02), lighten(CHIP_BOTTOM, -0.02), GOLD),
    ('brass', BRASS_TOP, BRASS_BOTTOM, BRASS_EDGE),
    ('brass hover', lighten(BRASS_TOP, 0.09), lighten(BRASS_BOTTOM, 0.07), GOLD),
]

pad = 16
sheet = Image.new('RGBA', (W + pad * 2, (H + pad) * len(VARIANTS) + pad), (18, 20, 18, 255))
for i, (name, top, bottom, edge) in enumerate(VARIANTS):
    button = nine_slice(face(top, bottom, edge), W, H, RADIUS)
    sheet.paste(button, (pad, pad + i * (H + pad)), button)

out = r'C:\Users\Hoel\AppData\Local\Temp\claude\C--Users-Hoel\d6391128-2f2f-4bc6-af4e-484a3555a3ed\scratchpad\buttons.png'
sheet = sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST)
sheet.save(out)
print('top to bottom: ' + ', '.join(v[0] for v in VARIANTS))
print('wrote ' + out)
