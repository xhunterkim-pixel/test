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

            _v = new float[w * h]; _inv = new float[w * h];
            if (Kind == "Topo")
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
            else
            {
                // flowing bands across the screen: slightly tilted lines, bent by big soft waves
                float spacing = 12.5f * k, amp = 150f * k;
                float ca = (float)Math.Cos(.21), sa = (float)Math.Sin(.21);
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float fx = x / k * .0021f, fy = y / k * .0021f;
                        float warp = (Fbm(fx, fy, 3, 5) - .5f) * 2f * amp + (Fbm(fx * 3.1f, fy * 3.1f, 2, 13) - .5f) * .35f * amp;
                        float across = y * ca + x * sa;
                        _v[y * w + x] = (across + warp) / spacing + 1000f;
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
            bool topo = Kind == "Topo";
            const float half = .55f; // half the line width, px
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

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
