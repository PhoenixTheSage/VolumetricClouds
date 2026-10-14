"""Bake tileable Nubis RGBA volumes (Schneider / Vos, SIGGRAPH 2017).

Shape 128³:
  R  Perlin-Worley. Improved-Perlin remapped into inverted Worley FBM.
  GBA  Worley FBM at the next octaves (cells 4/8/16 and 8/16/32 and 16/32).

Detail 64³: inverted Worley only. The march samples it near 100 m and
uses it as the erosion floor, not as a second shape.

Value noise is not Perlin. It filled the Worley edges and left R in a
narrow high band. Gradients are the 12 cube-edge vectors, quintic fade,
period-wrapped so the tile meets itself.
"""
from pathlib import Path

import numpy as np

OUT = Path(__file__).resolve().parents[1] / "Pack" / "Noise"

# Cube-edge gradients (improved Perlin). Length sqrt(2); the 0–1 stretch
# later removes the amplitude.
_GRADS = np.array(
    [
        [1, 1, 0],
        [-1, 1, 0],
        [1, -1, 0],
        [-1, -1, 0],
        [1, 0, 1],
        [-1, 0, 1],
        [1, 0, -1],
        [-1, 0, -1],
        [0, 1, 1],
        [0, -1, 1],
        [0, 1, -1],
        [0, -1, -1],
        [1, 1, 0],
        [-1, 1, 0],
        [0, -1, 1],
        [0, -1, -1],
    ],
    dtype=np.float64,
)


def _mod(i, period):
    return np.mod(i, period).astype(np.int32)


def _full_hash(x, y, z, seed):
    n = x.astype(np.int64) * np.int64(374761393)
    n += y.astype(np.int64) * np.int64(668265263)
    n += z.astype(np.int64) * np.int64(1274126177)
    n += np.int64(seed) * np.int64(1103515245)
    n = (n ^ (n >> 13)) * np.int64(1274126177)
    n ^= n >> 16
    return n


def _ihash(x, y, z, seed):
    return _full_hash(x, y, z, seed) & np.int64(15)


def hash013(x, y, z, seed):
    return (_full_hash(x, y, z, seed) & np.int64(0x7FFFFFFF)) / 2147483647.0


def _quintic(t):
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0)


def gradient_noise(u, v, w, seed, period):
    """Tileable improved Perlin. Returns roughly [-1, 1]."""
    px = max(int(round(period)), 1)
    x0 = np.floor(u).astype(np.int32)
    y0 = np.floor(v).astype(np.int32)
    z0 = np.floor(w).astype(np.int32)
    tx = u - x0
    ty = v - y0
    tz = w - z0
    sx = _quintic(tx)
    sy = _quintic(ty)
    sz = _quintic(tz)
    total = np.zeros(u.shape, dtype=np.float64)
    for dz in (0, 1):
        wz = sz if dz else (1.0 - sz)
        for dy in (0, 1):
            wy = sy if dy else (1.0 - sy)
            for dx in (0, 1):
                wx = sx if dx else (1.0 - sx)
                ix = _mod(x0 + dx, px)
                iy = _mod(y0 + dy, px)
                iz = _mod(z0 + dz, px)
                gi = _ihash(ix, iy, iz, seed)
                gx = _GRADS[gi, 0]
                gy = _GRADS[gi, 1]
                gz = _GRADS[gi, 2]
                n = gx * (tx - dx) + gy * (ty - dy) + gz * (tz - dz)
                total += n * wx * wy * wz
    return total


def worley_f1(u, v, w, seed, cells):
    p = np.stack((u * cells, v * cells, w * cells), axis=-1)
    cell = np.floor(p).astype(np.int32)
    frac = p - cell
    dmin = np.full(u.shape, 1.0e9, dtype=np.float64)
    for oz in (-1, 0, 1):
        for oy in (-1, 0, 1):
            for ox in (-1, 0, 1):
                cx = _mod(cell[..., 0] + ox, cells)
                cy = _mod(cell[..., 1] + oy, cells)
                cz = _mod(cell[..., 2] + oz, cells)
                hx = hash013(cx, cy, cz, seed)
                hy = hash013(cx, cy, cz, seed + 17)
                hz = hash013(cx, cy, cz, seed + 31)
                delta = np.stack((ox + hx, oy + hy, oz + hz), axis=-1) - frac
                dmin = np.minimum(dmin, np.sum(delta * delta, axis=-1))
    return np.clip(np.sqrt(dmin) / np.sqrt(3.0), 0.0, 1.0)


def _stretch01(x):
    lo = np.percentile(x, 3.0)
    hi = np.percentile(x, 97.0)
    return np.clip((x - lo) / max(hi - lo, 1e-4), 0.0, 1.0)


def bake_shape(dim=128, seed=1109):
    g = np.linspace(0.0, 1.0, dim, endpoint=False, dtype=np.float64)
    u, v, w = np.meshgrid(g, g, g, indexing="ij")
    # One Perlin octave at the same 4-cell frequency as the coarsest Worley.
    perlin = gradient_noise(u * 4.0, v * 4.0, w * 4.0, seed, 4.0)
    perlin01 = np.clip(perlin * 0.85 + 0.5, 0.0, 1.0)
    w0 = 1.0 - worley_f1(u, v, w, seed + 3, 4)
    w1 = 1.0 - worley_f1(u, v, w, seed + 11, 8)
    w2 = 1.0 - worley_f1(u, v, w, seed + 19, 16)
    w3 = 1.0 - worley_f1(u, v, w, seed + 29, 32)
    worley_fbm = w0 * 0.625 + w1 * 0.25 + w2 * 0.125
    # remap(perlin, 0, 1, worleyFbm, 1) — Worley owns the billow edge.
    pw = worley_fbm + perlin01 * (1.0 - worley_fbm)
    gch = w0 * 0.625 + w1 * 0.25 + w2 * 0.125
    bch = w1 * 0.625 + w2 * 0.25 + w3 * 0.125
    ach = w2 * 0.75 + w3 * 0.25
    stacked = np.stack((_stretch01(pw), _stretch01(gch), _stretch01(bch), _stretch01(ach)), axis=-1)
    return np.clip(stacked, 0.0, 1.0)


def bake_detail(dim=64, seed=2207):
    g = np.linspace(0.0, 1.0, dim, endpoint=False, dtype=np.float64)
    u, v, w = np.meshgrid(g, g, g, indexing="ij")
    w0 = 1.0 - worley_f1(u, v, w, seed, 4)
    w1 = 1.0 - worley_f1(u, v, w, seed + 7, 8)
    w2 = 1.0 - worley_f1(u, v, w, seed + 13, 16)
    w3 = 1.0 - worley_f1(u, v, w, seed + 23, 32)
    r = w0 * 0.625 + w1 * 0.25 + w2 * 0.125
    gch = w1 * 0.625 + w2 * 0.25 + w3 * 0.125
    bch = w2 * 0.75 + w3 * 0.25
    ach = w3
    stacked = np.stack((_stretch01(r), _stretch01(gch), _stretch01(bch), _stretch01(ach)), axis=-1)
    return np.clip(stacked, 0.0, 1.0)


def write_rgba(path, volume):
    path.parent.mkdir(parents=True, exist_ok=True)
    # D3D upload and the CPU sampler index as ((z * dim + y) * dim + x).
    bytes_ = np.rint(volume.transpose(2, 1, 0, 3) * 255.0).astype(np.uint8)
    path.write_bytes(bytes_.tobytes())
    r = bytes_[..., 0].astype(np.float64)
    print(
        path.name,
        bytes_.shape,
        "R min/mean/max/std",
        int(r.min()),
        round(float(r.mean()), 1),
        int(r.max()),
        round(float(r.std()), 1),
    )


def _assert_tiles():
    p = gradient_noise(np.array([0.25]), np.array([1.5]), np.array([2.75]), 1109, 4)
    q = gradient_noise(np.array([4.25]), np.array([5.5]), np.array([6.75]), 1109, 4)
    err = float(np.max(np.abs(p - q)))
    if err > 1e-5:
        raise SystemExit(f"gradient noise does not tile ({err})")
    print("gradient noise tiles, period error", err)


def main():
    _assert_tiles()
    write_rgba(OUT / "cloud_noise_shape_128_v5.bin", bake_shape())
    write_rgba(OUT / "cloud_noise_detail_64_v5.bin", bake_detail())


if __name__ == "__main__":
    main()
