using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The Dogtag rank-up (after Tarkov's own dogtags): a brushed, scratched metal tag with a pressed rim and a punched
    /// hole, hanging on a bead chain that loops up out of view. Everything on it is embossed — dark letters with a light
    /// lower edge. It drops in on the chain and swings to rest with a slight turn; on the emblem sound's peak the new rank
    /// emblem is stamped in (a jolt, a small flash) and a glint crosses the metal.
    /// </summary>
    internal static partial class ProgScreen
    {
        private const float TagW = 250, TagH = 390;
        private static Image _tagSheen;
        private static Component[] _tagName, _tagLine, _tagLv;
        private static RectTransform _tagEmblemBox;

        private static Sprite _tagPlate, _tagBall;

        /// <summary>The tag: rounded, brushed cool metal, a light gradient, scratches, a pressed rim, the hole. 250x390, own colours.</summary>
        private static Sprite TagPlate()
        {
            if (_tagPlate != null) return _tagPlate;
            const int w = 250, h = 390, ss = 2;
            const float r = 46, rim = 9;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "LevelGate dogtag" };
            var px = new Color32[w * h];
            var rnd = new System.Random(5);
            // scratches: short straight lines, a few long ones
            var scratch = new float[w * h];
            for (int k = 0; k < 90; k++)
            {
                float x0 = (float)rnd.NextDouble() * w, y0 = (float)rnd.NextDouble() * h;
                float ang = (float)(rnd.NextDouble() * Mathf.PI), len = k < 12 ? 60 + (float)rnd.NextDouble() * 120 : 6 + (float)rnd.NextDouble() * 30;
                float s = ((float)rnd.NextDouble() < .5 ? .06f : -.05f) * (.4f + .6f * (float)rnd.NextDouble()); // fine, uneven
                for (float d = 0; d < len; d += .7f)
                {
                    int x = (int)(x0 + Mathf.Cos(ang) * d), y = (int)(y0 + Mathf.Sin(ang) * d);
                    if (x >= 0 && x < w && y >= 0 && y < h) scratch[y * w + x] += s;
                }
            }
            float Dist(float x, float y) // signed distance to the rounded rect's edge (negative inside)
            {
                float qx = Mathf.Abs(x - w / 2f) - (w / 2f - r), qy = Mathf.Abs(y - h / 2f) - (h / 2f - r);
                return Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            }
            float hx = w / 2f, hy = h - 30, hr = 12;
            for (int y = 0; y < h; y++)
            {
                float brush = (Mathf.PerlinNoise(0, y * .9f) - .5f) * .06f; // brushed: fine horizontal grain
                for (int x = 0; x < w; x++)
                {
                    float cover = 0;
                    for (int j = 0; j < ss; j++) for (int i = 0; i < ss; i++)
                    {
                        float fx = x + (i + .5f) / ss, fy = y + (j + .5f) / ss;
                        float dh = Mathf.Sqrt((fx - hx) * (fx - hx) + (fy - hy) * (fy - hy));
                        if (Dist(fx, fy) <= 0 && dh > hr) cover += 1f / (ss * ss);
                    }
                    if (cover <= 0) continue;
                    float d = -Dist(x + .5f, y + .5f);          // depth inside the edge
                    float dHole = Mathf.Sqrt((x + .5f - hx) * (x + .5f - hx) + (y + .5f - hy) * (y + .5f - hy)) - hr;
                    float v = .74f + .14f * (y / (float)h) - .07f * (x / (float)w) + brush + scratch[y * w + x];
                    v += (Mathf.PerlinNoise(x * .05f, y * .05f) - .5f) * .08f; // wear
                    // the pressed rim: a dark groove then a light ridge, like a stamped tag
                    if (d < rim) v *= d < 2 ? .55f : d < 4 ? .8f : d < 6.5f ? 1.12f : .9f;
                    if (dHole < 4) v *= dHole < 1.5f ? .45f : .75f; // the hole's edge
                    v = Mathf.Clamp01(v);
                    px[y * w + x] = new Color32((byte)(255 * v * .93f), (byte)(255 * v * .95f), (byte)(255 * v * .97f), (byte)(255 * cover));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return _tagPlate = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
        }

        /// <summary>One bead of the chain: a shaded metal ball (light top-left). 16x16.</summary>
        private static Sprite TagBall()
        {
            if (_tagBall != null) return _tagBall;
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1, rr = dx * dx + dy * dy;
                    if (rr > 1) continue;
                    float hl = Mathf.Clamp01(1 - ((dx + .35f) * (dx + .35f) + (dy - .35f) * (dy - .35f)) * 1.6f);
                    float v = .35f + .45f * Mathf.Sqrt(1 - rr) + .45f * hl * hl;
                    float a = Mathf.Clamp01((1 - rr) * 6);
                    px[y * n + x] = new Color32((byte)(255 * Mathf.Clamp01(v * .92f)), (byte)(255 * Mathf.Clamp01(v * .95f)), (byte)(255 * Mathf.Clamp01(v)), (byte)(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _tagBall = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>Embossed text: a light copy 1 px down-right under dark letters.</summary>
        private static Component[] Emboss(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, float size, float spacing)
        {
            var hiRt = Ui.Rect(parent, name + "Hi", aMin, aMax, oMin + new Vector2(1, -1), oMax + new Vector2(1, -1));
            var hi = Ui.Label(hiRt, "Text", "", size, new Color(1, 1, 1, .45f), TextAnchor.MiddleCenter, true, spacing);
            var lo = Ui.Label(Ui.Rect(parent, name, aMin, aMax, oMin, oMax), "Text", "", size, Ui.Hex("#23282a", .92f), TextAnchor.MiddleCenter, true, spacing);
            return new[] { lo, hi };
        }

        private static void SetEmboss(Component[] c, string text) { foreach (var x in c) Ui.SetText(x, text); }

        private static void BuildDogtag(RankView v, RectTransform root)
        {
            // hangs from a point high above: the holder's pivot is up the chain, so the swing turns about it
            var holder = Ui.Box(root, "Tag", new Vector2(.5f, .5f), Vector2.zero, new Vector2(TagW, TagH));
            v.Body = holder;
            holder.pivot = new Vector2(.5f, 2.1f);
            // the bead chain: from the hole up and around in a loop, off the top of the screen
            var pts = new List<Vector2> { new Vector2(0, TagH / 2 - 30), new Vector2(-10, TagH / 2 + 40), new Vector2(40, TagH / 2 + 130), new Vector2(120, TagH / 2 + 150), new Vector2(110, TagH / 2 + 70),
                                          new Vector2(30, TagH / 2 + 110), new Vector2(-30, TagH / 2 + 250), new Vector2(-10, TagH / 2 + 480), new Vector2(0, TagH / 2 + 900) };
            var ballSp = TagBall();
            float acc = 0; Vector2 last = pts[0];
            for (int seg = 0; seg < pts.Count - 1; seg++)
            {
                Vector2 p0 = pts[Mathf.Max(0, seg - 1)], p1 = pts[seg], p2 = pts[seg + 1], p3 = pts[Mathf.Min(pts.Count - 1, seg + 2)];
                for (float u = 0; u < 1; u += .01f)
                {
                    // Catmull-Rom through the points
                    float u2 = u * u, u3 = u2 * u;
                    var p = .5f * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3);
                    acc += (p - last).magnitude; last = p;
                    if (acc < 6.5f) continue;
                    acc = 0;
                    var b = Ui.Img(Ui.Box(holder, "Bead", new Vector2(.5f, .5f), p, new Vector2(7, 7)), Color.white, ballSp);
                    b.transform.SetAsFirstSibling(); // behind the tag
                }
            }
            // a soft shadow under the tag, then the tag
            var sh = Ui.Img(Ui.Box(holder, "Shadow", new Vector2(.5f, .5f), new Vector2(10, -14), new Vector2(TagW + 40, TagH + 40)), new Color(0, 0, 0, .55f), Ui.Radial());
            var plate = Ui.Img(Ui.Fill(holder, "Plate"), Color.white, TagPlate());
            // the glint: a light band crossing the metal, only on the tag (masked by its shape)
            var maskRt = Ui.Fill(holder, "GlintMask");
            var mi = Ui.Img(maskRt, Color.white, TagPlate());
            maskRt.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _tagSheen = Ui.Img(Ui.Rect(maskRt, "Glint", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-50, -60), new Vector2(50, 60)), new Color(1, 1, 1, 0), Ui.Radial());
            _tagSheen.rectTransform.localEulerAngles = new Vector3(0, 0, -18);
            // stamped on it: PROMOTED, the emblem (pressed into a shallow well), the rank, a rule, level and date
            SetEmboss(Emboss(holder, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -80), new Vector2(-20, -56), 13, 7), "PROMOTED");
            var well = Ui.Box(holder, "Well", new Vector2(.5f, .58f), Vector2.zero, new Vector2(150, 130));
            Ui.Img(well, new Color(0, 0, 0, .12f), Ui.Radial());
            _tagEmblemBox = well;
            v.Emblem = Ui.Img(Ui.Fill(well, "Emblem", 6), Color.white); v.Emblem.preserveAspect = true;
            v.Flash = Ui.Img(Ui.Box(well, "Flash", new Vector2(.5f, .5f), Vector2.zero, new Vector2(190, 170)), new Color(1, 1, 1, 0), Ui.Radial());
            _tagName = Emboss(holder, "Name", new Vector2(0, .3f), new Vector2(1, .3f), new Vector2(14, -18), new Vector2(-14, 18), 26, 4);
            var rule = Ui.Rect(holder, "Rule", new Vector2(.2f, .23f), new Vector2(.8f, .23f), new Vector2(0, 0), new Vector2(0, 1));
            Ui.Img(rule, Ui.Hex("#23282a", .6f));
            Ui.Img(Ui.Rect(holder, "RuleHi", new Vector2(.2f, .23f), new Vector2(.8f, .23f), new Vector2(1, -1), new Vector2(1, 0)), new Color(1, 1, 1, .35f));
            _tagLv = Emboss(holder, "Lv", new Vector2(0, .16f), new Vector2(1, .16f), new Vector2(14, -14), new Vector2(-14, 14), 18, 5);
            _tagLine = Emboss(holder, "Date", new Vector2(0, .09f), new Vector2(1, .09f), new Vector2(14, -10), new Vector2(-14, 10), 12, 4);
        }

        private static void DrawDogtag(RankView v, int level, float t, float dur, float after, bool still, string name)
        {
            v.Veil.color = new Color(0, 0, 0, .5f);
            SetEmboss(_tagName, name);
            SetEmboss(_tagLv, $"LV {level:000}");
            SetEmboss(_tagLine, System.DateTime.Now.ToString("dd.MM.yyyy"));
            // drops in on its chain, swings to rest with a slight turn (the tag isn't flat to the screen while it swings)
            const float lift = (2.1f - .5f) * TagH; // the pivot sits up the chain
            float drop = still ? 1 : Motion.Eval(Motion.Ease.OutCubic, t / .5f);
            v.Body.anchoredPosition = new Vector2(0, Mathf.Lerp(700, 0, drop) + lift - 10);
            float swing = still ? 0 : 7 * Mathf.Exp(-t * 1.8f) * Mathf.Sin(t * 5.2f + .4f);
            v.Body.localEulerAngles = new Vector3(0, 0, swing);
            float turn = still ? 1 : 1 - .12f * Mathf.Exp(-t * 1.6f) * Mathf.Abs(Mathf.Sin(t * 3.1f));
            v.Body.localScale = new Vector3(turn, 1, 1);
            // the stamp on the peak: the emblem is pressed in (from a little big, with a jolt)
            float st = still || after < 0 ? 1 : Mathf.Lerp(1.3f, 1f, Motion.Eval(Motion.Ease.OutQuint, after / .16f));
            _tagEmblemBox.localScale = new Vector3(st, st, 1);
            v.Emblem.color = new Color(.92f, .94f, .96f, still || after >= 0 ? 1 : .55f); // the old one faint, the new one pressed in
            if (!still && after >= 0 && after < .15f) v.Body.anchoredPosition += new Vector2(0, -4 * (1 - after / .15f));
            // the glint crosses the metal once, just after the stamp
            float g = still ? -1 : (after - .12f) / .6f;
            if (g >= 0 && g <= 1)
            {
                var r = _tagSheen.rectTransform;
                float x = Mathf.Lerp(-.3f, 1.3f, Motion.Eval(Motion.Ease.InOutSine, g));
                r.anchorMin = new Vector2(x, 0); r.anchorMax = new Vector2(x, 1);
                _tagSheen.color = new Color(1, 1, 1, .45f * Motion.Eval(Motion.Ease.Pulse, g));
            }
            else _tagSheen.color = new Color(1, 1, 1, 0);
        }
    }
}
