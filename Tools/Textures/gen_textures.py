# Генератор минимальных текстур арены и эффектов для AAAA_game.
# Запуск: python Tools/Textures/gen_textures.py  (нужны numpy и Pillow)
# Всё маленькое (64–512 px), без альфы там, где она не нужна: на мобилках это копейки памяти и выборок.

import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ENV = os.path.join(ROOT, "Assets", "_Project", "Art", "Textures", "Environment")
FX = os.path.join(ROOT, "Assets", "_Project", "Art", "Textures", "Fx")
rng = np.random.default_rng(7)


def noise(size, scale, octaves=4):
    """Тайлящийся value-noise (сумма октав), 0..1."""
    h, w = size
    out = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        cells = max(1, int(scale * (2 ** o)))
        grid = rng.random((cells, cells)).astype(np.float32)
        ys = np.linspace(0, cells, h, endpoint=False)
        xs = np.linspace(0, cells, w, endpoint=False)
        y0 = np.floor(ys).astype(int); x0 = np.floor(xs).astype(int)
        fy = (ys - y0)[:, None]; fx = (xs - x0)[None, :]
        fy = fy * fy * (3 - 2 * fy); fx = fx * fx * (3 - 2 * fx)
        y1 = (y0 + 1) % cells; x1 = (x0 + 1) % cells
        a = grid[y0][:, x0]; b = grid[y0][:, x1]; c = grid[y1][:, x0]; d = grid[y1][:, x1]
        out += amp * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy)
        total += amp
        amp *= 0.5
    return out / total


def save(img, folder, name):
    os.makedirs(folder, exist_ok=True)
    img.save(os.path.join(folder, name))
    print("wrote", name, img.size)


def slabs(size, tiles_x, tiles_y, base, var, grout, gw, bevel, stagger=False, seed_noise=6):
    """Каменные плиты/блоки: швы, лёгкая фаска, разброс тона по плитам, шум."""
    h, w = size
    img = np.zeros((h, w, 3), np.float32)
    n = noise((h, w), seed_noise)
    fine = noise((h, w), 32, 2)
    tw, th = w / tiles_x, h / tiles_y
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    row = np.floor(yy / th)
    shift = np.where((row % 2 == 1) & stagger, tw * 0.5, 0.0)
    lx = (xx + shift) % tw
    ly = yy % th
    col = np.floor((xx + shift) / tw) % tiles_x
    tone = rng.uniform(-var, var, (tiles_y, tiles_x)).astype(np.float32)
    t = tone[row.astype(int) % tiles_y, col.astype(int)]
    d = np.minimum(np.minimum(lx, tw - lx), np.minimum(ly, th - ly))  # до шва
    edge = np.clip((d - gw) / bevel, 0, 1)
    light = np.where((lx < gw + bevel) | (ly < gw + bevel), 1.06, 0.94)  # свет сверху-слева на фаске
    light = np.where(edge >= 1, 1.0, light)
    for k in range(3):
        c = base[k] * (1 + t) * (0.9 + 0.15 * n) * (0.96 + 0.08 * fine) * light
        img[..., k] = np.where(d < gw, grout[k] * (0.9 + 0.2 * n), c)
    # трещинки и сколы — тёмные пятна
    cracks = noise((h, w), 14, 3)
    img *= (1 - 0.18 * np.clip((cracks - 0.68) * 8, 0, 1))[..., None]
    return Image.fromarray(np.clip(img * 255, 0, 255).astype(np.uint8), "RGB")


def radial(size, fn):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    c = (size - 1) / 2
    r = np.hypot(xx - c, yy - c) / c
    a = np.clip(fn(r), 0, 1)
    return a


def gray_alpha(a):
    """Белая текстура с альфой (цвет задаётся частицами/материалом)."""
    rgba = np.zeros(a.shape + (4,), np.uint8)
    rgba[..., :3] = 255
    rgba[..., 3] = (a * 255).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def env():
    # Пол: тёплые каменные плиты 4×4 на текстуру (в игре — 1 плита = 1 м).
    save(slabs((512, 512), 4, 4, (0.40, 0.37, 0.335), 0.05, (0.24, 0.22, 0.2), 3, 5), ENV, "T_Floor_Stone.png")
    # Стены и препятствия: крупная кладка вразбежку, холоднее и темнее пола — фон не спорит с бойцами.
    save(slabs((256, 256), 2, 4, (0.33, 0.34, 0.38), 0.08, (0.10, 0.10, 0.12), 3, 4, stagger=True), ENV, "T_Wall_Blocks.png")

    # Разметка арены (альфа поверх пола): кайма, центральный круг и линия. 32 px на метр, арена 22×14 м.
    ppm = 32
    w, h = 22 * ppm, 14 * ppm
    im = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(im)
    lw = 5
    inset = int(0.45 * ppm)
    d.rectangle([inset, inset, w - inset, h - inset], outline=200, width=lw)
    cx, cy = w // 2, h // 2
    r = int(2.0 * ppm)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=170, width=lw)
    r2 = int(0.35 * ppm)
    d.ellipse([cx - r2, cy - r2, cx + r2, cy + r2], fill=150)
    d.line([cx, inset, cx, cy - r], fill=110, width=3)
    d.line([cx, cy + r, cx, h - inset], fill=110, width=3)
    # точки старта бойцов (±3 м по X от центра, MatchRules.Spawns)
    for sx in (-3, 3):
        px = cx + sx * ppm
        rr = int(0.9 * ppm)
        d.ellipse([px - rr, cy - rr, px + rr, cy + rr], outline=120, width=3)
    im = im.filter(ImageFilter.GaussianBlur(0.8))
    # износ: разметка местами стёрта
    wear = noise((h, w), 10, 3)
    a = np.asarray(im, np.float32) / 255 * np.clip(0.55 + (wear - 0.5) * 1.6, 0.25, 1)
    save(gray_alpha(a), ENV, "T_Arena_Markings.png")


def fx():
    save(gray_alpha(radial(64, lambda r: (1 - r) ** 2)), FX, "T_Fx_Glow.png")
    save(gray_alpha(radial(128, lambda r: np.exp(-((r - 0.82) / 0.07) ** 2))), FX, "T_Fx_Ring.png")
    # четырёхлучевая звезда-вспышка (попадание, парирование)
    s = 128
    yy, xx = np.mgrid[0:s, 0:s].astype(np.float32)
    c = (s - 1) / 2
    dx, dy = np.abs(xx - c) / c, np.abs(yy - c) / c
    star = np.clip(1 - dx * 8 - dy, 0, 1) ** 1.5 + np.clip(1 - dy * 8 - dx, 0, 1) ** 1.5
    core = np.clip(1 - np.hypot(dx, dy) * 2.2, 0, 1) ** 2
    save(gray_alpha(np.clip(star + core, 0, 1)), FX, "T_Fx_Star.png")
    # клуб пыли: мягкий круг с шумом по краю
    n = noise((64, 64), 4, 3)
    puff = radial(64, lambda r: 1 - r) * (0.55 + 0.6 * n)
    save(gray_alpha(np.clip(puff * 1.4, 0, 1) ** 1.3), FX, "T_Fx_Puff.png")
    # мягкая тень под бойцом
    save(gray_alpha(radial(64, lambda r: np.clip(1 - r, 0, 1) ** 1.2)), FX, "T_Fx_Shadow.png")


if __name__ == "__main__":
    env()
    fx()
