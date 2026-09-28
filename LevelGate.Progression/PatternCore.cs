using System;

namespace LevelGate.Progression
{
    /// <summary>
    /// The maths behind the line patterns of F12 > Graphics > Pattern, kept free of Unity so it can run on a worker thread.
    /// Build() does the heavy part once (the noise field and its slope); Render(t) is cheap and is what the animation repeats:
    /// the contour lines are the field's whole-number heights, and moving t slides every line along the slope (lines flow,
    /// rings grow and shrink); the streaks shimmer column by column and drift slowly up and down.
    /// </summary>
    internal sealed class PatternCore
    {
        public readonly int W, H;
        public readonly string Kind;
        private float[] _v, _inv;          // lines: height in "line" units and 1 / slope (pixels per unit)
        private float[] _col, _speed, _phase, _along; // streaks: per column strength / shimmer, and the soft break-up along them
        private const int Slack = 360;    // extra rows of break-up for the streaks' drift
        private float[] _scan;            // marble: faint horizontal scan lines
        private int[] _cellOf;            // pixels: the LED each texel belongs to (-1 between them)
        private float[] _cellV, _cellHi;  // pixels: each LED's place in the light bands, and its extra glow
        private byte[] _cellA;
        private float[] _height;          // terrain: a seamless height map (HeightN × HeightN)
        private const int HeightN = 256;
        private int[] _top;               // terrain: per column, the highest point drawn so far (hidden lines)
        private float[] _ys;

        public PatternCore(string kind, int w, int h) { Kind = kind; W = w; H = h; }

        // ------------------------------------------------------------------ noise

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 144269504;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffffff) / 16777215f;
            }
        }

        private static float Noise(float x, float y, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Hash(x0, y0, seed), b = Hash(x0 + 1, y0, seed), c = Hash(x0, y0 + 1, seed), d = Hash(x0 + 1, y0 + 1, seed);
            float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
            return ab + (cd - ab) * fy;
        }

        private static float Fbm(float x, float y, int octaves, int seed)
        {
            float v = 0, amp = .5f, sum = 0;
            for (int o = 0; o < octaves; o++) { v += Noise(x, y, seed + o * 17) * amp; sum += amp; x = x * 2.03f + 1.7f; y = y * 2.03f - 3.1f; amp *= .5f; }
            return v / sum;
        }

        /// <summary>Value noise that repeats every p cells (seamless height map for the terrain).</summary>
        private static float PNoise(float x, float y, int p, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            int xa = ((x0 % p) + p) % p, xb = (xa + 1) % p, ya = ((y0 % p) + p) % p, yb = (ya + 1) % p;
            float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
            float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
            return ab + (cd - ab) * fy;
        }

        // ------------------------------------------------------------------ build (once per pattern)

        public void Build()
        {
            int w = W, h = H;
            float k = w / 1280f; // the designs are tuned at 1280 wide
            if (Kind == "Streaks")
            {
                _col = new float[w]; _speed = new float[w]; _phase = new float[w];
                for (int x = 0; x < w; x++)
                {
                    float n = Fbm(x / k * .035f, 3.1f, 4, 7);
                    _col[x] = Clamp01((n - .42f) * 2.6f);
                    _speed[x] = .35f + Fbm(x / k * .011f, 5.3f, 2, 3) * .9f; // neighbouring columns brighten and dim together
                    _phase[x] = Fbm(x / k * .018f, 1.7f, 2, 4) * 18f;
                }
                int ah = h + Slack;
                _along = new float[w * ah];
                for (int y = 0; y < ah; y++)
                    for (int x = 0; x < w; x++)
                        _along[y * w + x] = Clamp01(.35f + Fbm(x / k * .02f, y / k * .006f, 3, 23) * 1.1f);
                return;
            }

            if (Kind == "Pixels")
            {
                // an LED wall: small squares on a grid, lit by soft diagonal bands of light
                int cell = Math.Max(6, (int)Math.Round(12 * k)), sq = Math.Max(4, (int)Math.Round(8 * k));
                int cw = w / cell + 1, ch = h / cell + 1;
                _cellOf = new int[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        _cellOf[y * w + x] = (x % cell) < sq && (y % cell) < sq ? (y / cell) * cw + x / cell : -1;
                _cellV = new float[cw * ch]; _cellHi = new float[cw * ch]; _cellA = new byte[cw * ch];
                float ca = (float)Math.Cos(-.32), sa = (float)Math.Sin(-.32);
                for (int cy = 0; cy < ch; cy++)
                    for (int cx = 0; cx < cw; cx++)
                    {
                        float px = cx * cell / k, py = cy * cell / k;
                        float warp = (Fbm(px * .0025f, py * .0025f, 3, 71) - .5f) * 260f;
                        _cellV[cy * cw + cx] = (py * ca + px * sa + warp) / 210f;
                        _cellHi[cy * cw + cx] = Clamp01((Fbm(px * .004f + 3, py * .004f, 3, 77) - .52f) * 3.2f);
                    }
                return;
            }
            if (Kind == "Terrain")
            {
                // a seamless height map: the 3D ridge lines fly over it
                _height = new float[HeightN * HeightN];
                for (int y = 0; y < HeightN; y++)
                    for (int x = 0; x < HeightN; x++)
                    {
                        float v = 0, amp = .5f, sum = 0; int per = 4;
                        for (int o = 0; o < 5; o++, per *= 2, amp *= .5f)
                        { v += PNoise(x * per / (float)HeightN, y * per / (float)HeightN, per, 91 + o * 13) * amp; sum += amp; }
                        v /= sum;
                        _height[y * HeightN + x] = (v - .3f) * 1.7f; // rolling hills and valleys
                    }
                _top = new int[w]; _ys = new float[w];
                return;
            }

            _v = new float[w * h]; _inv = new float[w * h];
            if (Kind == "Marble")
            {
                // mirrored marbling: broad banded swirls, reflected down the middle of the screen, with scan lines
                _scan = new float[h];
                for (int y = 0; y < h; y++) _scan[y] = .72f + .28f * Hash(y, 3, 55) * (y % 3 == 0 ? .4f : 1f);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float xm = Math.Abs(x - w * .5f);
                        float fx = xm / k * .0042f, fy = y / k * .0042f;
                        float wx = Fbm(fx * .8f + 2.3f, fy * .8f, 3, 31) - .5f, wy = Fbm(fx * .8f, fy * .8f + 6.7f, 3, 37) - .5f;
                        _v[y * w + x] = Fbm(fx + wx * 2.4f, fy + wy * 2.4f, 4, 43) * 16f;
                    }
            }
            else if (Kind == "Damascus1")
            {
                // busy terrain: many octaves, lots of closed rings and tight bends
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float fx = x / k * .0062f, fy = y / k * .0062f;
                        float wx = Fbm(fx * .9f + 5.2f, fy * .9f, 3, 61) - .5f, wy = Fbm(fx * .9f, fy * .9f + 9.1f, 3, 67) - .5f;
                        _v[y * w + x] = Fbm(fx + wx * 1.6f, fy + wy * 1.6f, 4, 41) * 34f;
                    }
            }
            else if (Kind == "Damascus3")
            {
                // rings: growth rings around a few centres (some off screen), gently warped; where two ring sets meet they
                // fold into each other like pattern-welded steel
                var cx = new[] { .18f * w, .84f * w, .55f * w, -.2f * w }; var cy = new[] { .3f * h, .2f * h, 1.15f * h, .8f * h };
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float fx = x / k * .004f, fy = y / k * .004f;
                        float wx = (Fbm(fx, fy, 3, 71) - .5f) * 90f * k, wy = (Fbm(fx + 4.1f, fy, 3, 73) - .5f) * 90f * k;
                        float best = float.MaxValue;
                        for (int c = 0; c < cx.Length; c++) { float dx = x + wx - cx[c], dy = y + wy - cy[c]; best = Math.Min(best, (float)Math.Sqrt(dx * dx + dy * dy)); }
                        _v[y * w + x] = best / (15f * k);
                    }
            }
            else if (Kind == "Damascus4")
            {
                // mirrored lines: the marbling folded down the middle of the screen, drawn as lines only (chevrons in the centre)
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float xm = Math.Abs(x - w * .5f);
                        float fx = xm / k * .0036f, fy = y / k * .0036f;
                        float wx = Fbm(fx * .8f + 2.3f, fy * .8f, 3, 81) - .5f, wy = Fbm(fx * .8f, fy * .8f + 6.7f, 3, 87) - .5f;
                        _v[y * w + x] = Fbm(fx + wx * 2.2f, fy + wy * 2.2f, 3, 83) * 26f;
                    }
            }
            else
            {
                // Damascus 2: big organic lines flowing across the screen; the warp squeezes them together in places and
                // spreads them apart in others (uneven spacing, like forged steel), drawn thicker
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float fx = x / k * .0017f, fy = y / k * .0017f;
                        // isolines of a smooth, strongly warped field: long meanders, U-bends, lines crowding and opening up
                        float wx = (Fbm(fx * .7f + 3.3f, fy * .7f, 2, 91) - .5f) * 2.6f, wy = (Fbm(fx * .7f, fy * .7f + 8.8f, 2, 97) - .5f) * 2.6f;
                        _v[y * w + x] = Fbm(fx + wx, fy + wy + fx * .35f, 2, 5) * 20f + fy * 1.5f;
                    }
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int xl = x > 0 ? i - 1 : i, xr = x < w - 1 ? i + 1 : i, yd = y > 0 ? i - w : i, yu = y < h - 1 ? i + w : i;
                    float gx = (_v[xr] - _v[xl]) / Math.Max(1, (xr - xl)), gy = (_v[yu] - _v[yd]) / Math.Max(1, (yu - yd) / w);
                    _inv[i] = 1f / Math.Max(1e-4f, (float)Math.Sqrt(gx * gx + gy * gy));
                }
        }

        // ------------------------------------------------------------------ render (every animation step)

        /// <summary>Writes the pattern at time t (in line spacings for the contours, seconds-ish for the streaks) as RGBA32, white with alpha.</summary>
        public void Render(float t, byte[] rgba)
        {
            int w = W, h = H;
            if (Kind == "Streaks")
            {
                var shim = new float[w];
                for (int x = 0; x < w; x++) shim[x] = _col[x] * (.62f + .38f * (float)Math.Sin(_phase[x] + t * _speed[x]));
                int off = (int)((Math.Sin(t * .11) * .5 + .5) * (Slack - 1));
                for (int y = 0; y < h; y++)
                {
                    int row = (y + off) * w, o = y * w * 4;
                    for (int x = 0; x < w; x++, o += 4)
                    {
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = 255;
                        rgba[o + 3] = (byte)(255 * shim[x] * _along[row + x]);
                    }
                }
                return;
            }
            if (Kind == "Pixels") { RenderPixels(t, rgba); return; }
            if (Kind == "Terrain") { RenderTerrain(t, rgba); return; }
            if (Kind == "Marble") { RenderMarble(t, rgba); return; }
            bool topo = Kind == "Damascus1";
            float half = Kind == "Damascus2" ? 2.3f : .55f; // half the line width, px (Damascus 2's lines are bold)
            t %= 5f; // lines repeat every 1, index lines every 5: keeps the numbers small
            if (t < 0) t += 5f;
            var v = _v; var inv = _inv;
            for (int i = 0, o = 0; i < v.Length; i++, o += 4)
            {
                float u = v[i] + t;
                int ri = (int)(u + .5f); // heights are positive, so this rounds
                float du = u - ri;
                if (du < 0) du = -du;
                float d = du * inv[i] - half;
                byte a;
                if (d >= 1f) a = 0;
                else
                {
                    float f = d <= 0 ? 1f : 1 - d;
                    if (topo && ri % 5 != 0) f *= .55f; // every fifth line is an index line, like a real map
                    a = (byte)(255 * f);
                }
                rgba[o] = rgba[o + 1] = rgba[o + 2] = 255;
                rgba[o + 3] = a;
            }
        }

        private void RenderMarble(float t, byte[] rgba)
        {
            int w = W;
            t %= 2f; if (t < 0) t += 2f; // bands alternate every 2
            var v = _v; var inv = _inv;
            for (int i = 0, o = 0; i < v.Length; i++, o += 4)
            {
                float u = v[i] + t;
                int ri = (int)u;
                float fr = u - ri, du = fr < .5f ? fr : 1 - fr;
                float d = du * inv[i] - .9f; // edges ~1.8 px
                float edge = d <= 0 ? 1f : d >= 1 ? 0f : 1 - d;
                float fill = (ri & 1) == 0 ? .34f : .07f;
                float a = (edge > fill ? edge : fill) * _scan[i / w];
                rgba[o] = rgba[o + 1] = rgba[o + 2] = 255;
                rgba[o + 3] = (byte)(255 * a);
            }
        }

        private void RenderPixels(float t, byte[] rgba)
        {
            for (int c = 0; c < _cellV.Length; c++)
            {
                float b = .5f + .5f * (float)Math.Sin(6.2832f * (_cellV[c] - t));
                b = b * b * b;
                float a = .16f + .84f * Clamp01(b * (.55f + .6f * _cellHi[c]));
                _cellA[c] = (byte)(255 * a);
            }
            var of = _cellOf;
            for (int i = 0, o = 0; i < of.Length; i++, o += 4)
            {
                int c = of[i];
                rgba[o] = rgba[o + 1] = rgba[o + 2] = 255;
                rgba[o + 3] = c < 0 ? (byte)0 : _cellA[c];
            }
        }

        private float HeightAt(float x, float z)
        {
            int n = HeightN;
            int x0 = (int)Math.Floor(x), z0 = (int)Math.Floor(z);
            float fx = x - x0, fz = z - z0;
            int xa = ((x0 % n) + n) % n, xb = (xa + 1) % n, za = ((z0 % n) + n) % n, zb = (za + 1) % n;
            var hm = _height;
            float a = hm[za * n + xa], b = hm[za * n + xb], c = hm[zb * n + xa], d = hm[zb * n + xb];
            float ab = a + (b - a) * fx, cd = c + (d - c) * fx;
            return ab + (cd - ab) * fz;
        }

        /// <summary>3D terrain: ridge lines at increasing depth over a height map, nearer ridges hiding the ones behind, flying forward.</summary>
        private void RenderTerrain(float t, byte[] rgba)
        {
            int w = W, h = H;
            for (int o = 0; o < rgba.Length; o += 4) { rgba[o] = rgba[o + 1] = rgba[o + 2] = 255; rgba[o + 3] = 0; }
            var top = _top; var ys = _ys;
            for (int x = 0; x < w; x++) top[x] = int.MaxValue;
            const int lines = 64;
            float horizon = h * .13f, scale = h * .55f, camH = 1.1f;
            for (int k = 0; k < lines; k++) // nearest first
            {
                float f = k / (float)(lines - 1);
                float z = .75f * (float)Math.Pow(12.0, f);
                float thick = .75f + 1.5f * (1 - f), alpha = .3f + .7f * (float)Math.Pow(1 - f, .7);
                for (int x = 0; x < w; x++)
                {
                    float xw = (x - w * .5f) / w * z * 1.7f;
                    float hgt = HeightAt(xw * 28f + 128f, z * 28f + t * 6f) * .95f;
                    ys[x] = horizon + (camH - hgt) * scale / z;
                }
                for (int x = 0; x < w; x++)
                {
                    float y0 = ys[x], y1 = x > 0 ? ys[x - 1] : y0;
                    float lo = Math.Min(y0, y1), hi = Math.Max(y0, y1);
                    int r0 = (int)(lo - thick * 3), r1 = (int)(hi + thick * 3) + 1;
                    int lim = Math.Min(top[x], h - 1);
                    if (r0 < 0) r0 = 0;
                    if (r1 > lim) r1 = lim;
                    for (int r = r0; r <= r1; r++)
                    {
                        float d = r < lo ? lo - r : r > hi ? r - hi : 0;
                        float core = 1 - (d - thick * .5f); core = core < 0 ? 0 : core > 1 ? 1 : core;
                        float glow = 1 - d / (thick * 3); glow = glow < 0 ? 0 : glow * glow * .28f;
                        float a = alpha * (core > glow ? core : glow);
                        int o = ((h - 1 - r) * w + x) * 4 + 3; // rows counted from the top; the texture's from the bottom
                        byte ab = (byte)(255 * (a > 1 ? 1 : a));
                        if (ab > rgba[o]) rgba[o] = ab;
                    }
                    int yi = (int)y0;
                    if (yi < top[x]) top[x] = yi;
                }
            }
        }

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
