"""Génère le logo de SpaceNotch : la grille hypnotique 3×3 sur fond noir OLED.

Chaque taille est dessinée pour elle-même : aux petites tailles, les pixels de la
grille tombent sur des pixels entiers de l'écran (une icône de 16 px réduite
depuis 256 px serait floue). Rendu sur-échantillonné puis réduit.

    python tools/brand/make_icons.py

Dépend de Pillow et numpy. Écrit dans src/SpaceNotch.App/Assets et docs/assets/brand.
"""
from __future__ import annotations

import pathlib

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = pathlib.Path(__file__).resolve().parents[2]
ASSETS = ROOT / "src" / "SpaceNotch.App" / "Assets"
BRAND = ROOT / "docs" / "assets" / "brand"

CYAN = (0x7F, 0xE6, 0xFF)
WHITE = (0xFF, 0xFF, 0xFF)

# Motif de repos du logo : centre blanc, croix cyan, coins en veille.
PATTERN = [
    (CYAN, 0.30), (CYAN, 0.78), (CYAN, 0.30),
    (CYAN, 0.78), (WHITE, 1.00), (CYAN, 0.78),
    (CYAN, 0.30), (CYAN, 0.78), (CYAN, 0.30),
]

# Grilles calées sur les pixels : (côté de pixel, écart, retrait du fond).
SNAPPED = {
    16: (4, 1, 0),
    20: (4, 2, 0),
    24: (6, 1, 0),
    32: (6, 2, 1),
    40: (8, 2, 1),
    48: (10, 2, 2),
    64: (12, 4, 2),
}

SS = 8  # sur-échantillonnage


def layout(size: int) -> tuple[float, float, float]:
    if size in SNAPPED:
        return SNAPPED[size]
    # Grandes tailles : proportions du dessin de référence en 256.
    k = size / 256
    return 50 * k, 14 * k, 16 * k


def render(size: int, plate: bool = True) -> Image.Image:
    cell, gap, inset = layout(size)
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    if plate:
        radius = (size - 2 * inset) * 0.26
        d = ImageDraw.Draw(img)
        box = [inset * SS, inset * SS, (size - inset) * SS - 1, (size - inset) * SS - 1]
        d.rounded_rectangle(box, radius=radius * SS, fill=(0, 0, 0, 255))
        if size >= 32:
            # Liseré discret : le noir reste lisible sur une barre des tâches sombre.
            d.rounded_rectangle(box, radius=radius * SS, outline=(255, 255, 255, 40), width=max(1, round(SS * size / 128)))
        # Voile de nuit, à peine visible, en haut du fond.
        if size >= 48:
            grad = np.zeros((S, S, 4), dtype=np.float32)
            y = np.linspace(0, 1, S)[:, None]
            grad[..., 0] = 0x1B
            grad[..., 1] = 0x22
            grad[..., 2] = 0x36
            grad[..., 3] = np.clip(1 - y / 0.7, 0, 1) * 110
            mask = np.array(img.split()[3], dtype=np.float32) / 255
            grad[..., 3] *= mask
            img = Image.alpha_composite(img, Image.fromarray(grad.astype(np.uint8), "RGBA"))

    total = 3 * cell + 2 * gap
    origin = (size - total) / 2
    lit = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    dl = ImageDraw.Draw(lit)
    rr = cell * (0.26 if size >= 32 else 0.2)

    for i in range(3):
        for j in range(3):
            color, alpha = PATTERN[i * 3 + j]
            x = (origin + j * (cell + gap)) * SS
            y = (origin + i * (cell + gap)) * SS
            dl.rounded_rectangle([x, y, x + cell * SS - 1, y + cell * SS - 1], radius=rr * SS,
                                 fill=(*color, round(255 * alpha)))

    # Halo : la forme exacte des pixels allumés, floutée (le « bloom » de l'app).
    glow_radius = max(1.0, size * 0.045) * SS if size >= 32 else 0.6 * SS
    glow = lit.filter(ImageFilter.GaussianBlur(glow_radius))
    ga = np.array(glow, dtype=np.float32)
    ga[..., 3] *= 0.9 if size >= 32 else 0.45
    glow = Image.fromarray(ga.clip(0, 255).astype(np.uint8), "RGBA")

    img = Image.alpha_composite(img, glow)
    img = Image.alpha_composite(img, lit)
    return img.resize((size, size), Image.LANCZOS)


def canvas(w: int, h: int, logo: int) -> Image.Image:
    """Logo centré sur un fond transparent (tuiles larges, écran de démarrage)."""
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    mark = render(logo)
    out.alpha_composite(mark, ((w - logo) // 2, (h - logo) // 2))
    return out


def main() -> None:
    BRAND.mkdir(parents=True, exist_ok=True)

    sizes = [16, 20, 24, 32, 40, 48, 64, 256]
    frames = [render(s) for s in sizes]
    frames[-1].save(ASSETS / "AppIcon.ico", format="ICO", sizes=[(s, s) for s in sizes],
                    append_images=frames[:-1])

    render(88).save(ASSETS / "Square44x44Logo.scale-200.png")
    render(24).save(ASSETS / "Square44x44Logo.targetsize-24_altform-unplated.png")
    render(48).save(ASSETS / "Square44x44Logo.targetsize-48_altform-lightunplated.png")
    render(300).save(ASSETS / "Square150x150Logo.scale-200.png")
    render(50).save(ASSETS / "StoreLogo.png")
    render(48).save(ASSETS / "LockScreenLogo.scale-200.png")
    canvas(620, 300, 220).save(ASSETS / "Wide310x150Logo.scale-200.png")
    canvas(1240, 600, 320).save(ASSETS / "SplashScreen.scale-200.png")

    for s in (128, 512, 1024):
        render(s).save(BRAND / f"spacenotch-logo-{s}.png", optimize=True)


if __name__ == "__main__":
    main()
