using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// Small helpers to build uGUI in code. Text uses the game's TextMeshPro (looked up by
    /// name, so no compile-time dependency) with its font, or Unity's plain Text if TMP
    /// can't be found.
    /// </summary>
    internal static class Ui
    {
        public static object GameFont;          // TMPro.TMP_FontAsset of the game
        private static Type _tmpType;
        private static bool _tmpTried, _fontSearched;
        private static Font _legacyFont;

        public static Type TmpType
        {
            get
            {
                if (!_tmpTried)
                {
                    _tmpTried = true;
                    _tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
                    L.Info(_tmpType != null ? "text: TextMeshPro (" + _tmpType.Assembly.GetName().Name + ")" : "text: TextMeshPro not found — using Unity's plain Text");
                }
                return _tmpType;
            }
        }

        public static bool IsTmp(Component c) => c != null && c.GetType().Name.Contains("TextMeshPro");

        public static Color Hex(string hex, float alpha = 1f)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) c = Color.gray;
            // Tarkov's UI is neutral grey: the near-greys (panels, borders, text) lose their slight blue tint; real colours
            // (orange, red, green, the blue of a bonus) stay as they are
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            if (max - min < .12f) { float grey = c.r * .3f + c.g * .5f + c.b * .2f; c.r = c.g = c.b = grey; }
            c.a = alpha;
            return c;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return rt;
        }

        /// <summary>A rect of a fixed size, placed by its center relative to an anchor point.</summary>
        public static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = Rect(parent, name, anchor, anchor, Vector2.zero, Vector2.zero);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        public static RectTransform Fill(Transform parent, string name, float pad = 0) =>
            Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(pad, pad), new Vector2(-pad, -pad));

        public static Image Img(RectTransform rt, Color color, Sprite sprite = null, bool raycast = false)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null) img.sprite = sprite;
            img.raycastTarget = raycast;
            return img;
        }

        public static Component Label(Transform parent, string name, string text, float size, Color color, TextAnchor align, bool bold = false, float spacing = 0, bool ellipsis = false)
        {
            var rt = Fill(parent, name);
            return AddText(rt.gameObject, text, size, color, align, bold, spacing, ellipsis);
        }

        /// <summary>ellipsis: cut long text with "…" (tiles); otherwise text may run past its box (titles).</summary>
        public static Component AddText(GameObject go, string text, float size, Color color, TextAnchor align, bool bold = false, float spacing = 0, bool ellipsis = false)
        {
            // F12 Text Size: the small text grows (labels, tags, names); big numbers and titles stay as designed
            int pct = ProgressionPlugin.TextSize?.Value ?? 100;
            if (pct != 100 && size <= 15) size = Mathf.Round(size * pct / 100f * 2) / 2;
            var tmp = TmpType;
            if (tmp != null)
            {
                try
                {
                    var c = go.AddComponent(tmp);
                    if (GameFont == null && !_fontSearched)
                    {
                        _fontSearched = true;
                        var any = UnityEngine.Object.FindObjectsOfType(tmp).OfType<Component>().FirstOrDefault(x => Refl.Get(x, "font") != null);
                        GameFont = Refl.Get(any, "font");
                        L.Info("game font for the screen: " + ((GameFont as UnityEngine.Object)?.name ?? "none found, TMP default"));
                    }
                    if (GameFont != null) Refl.Set(c, "font", GameFont);
                    Refl.Set(c, "richText", true);
                    Refl.Set(c, "raycastTarget", false);
                    Refl.Set(c, "enableWordWrapping", false);
                    SetEnum(c, "overflowMode", ellipsis ? "Ellipsis" : "Overflow");
                    SetEnum(c, "alignment", TmpAlign(align));
                    if (bold) SetEnum(c, "fontStyle", "Bold");
                    Refl.Set(c, "fontSize", size);
                    Refl.Set(c, "characterSpacing", spacing);
                    Refl.Set(c, "color", color);
                    Refl.Set(c, "text", text);
                    return c;
                }
                catch (Exception e) { L.ErrorOnce("TextMeshPro text", e); }
            }
            var t = go.AddComponent<Text>();
            if (_legacyFont == null)
            {
                _legacyFont = TryFont("LegacyRuntime.ttf") ?? TryFont("Arial.ttf");
                L.Info("plain Text font: " + (_legacyFont?.name ?? "none!"));
            }
            t.font = _legacyFont;
            t.fontSize = Mathf.RoundToInt(size);
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.alignment = align;
            t.color = color;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.text = text;
            return t;
        }

        private static Font TryFont(string name) { try { return Resources.GetBuiltinResource<Font>(name); } catch { return null; } }

        private static void SetEnum(Component c, string prop, string value)
        {
            var p = c.GetType().GetProperty(prop);
            if (p == null) return;
            try { p.SetValue(c, Enum.Parse(p.PropertyType, value), null); }
            catch (Exception e) { L.Debug($"TMP {prop}={value}: {e.GetBaseException().Message}"); }
        }

        private static string TmpAlign(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return "TopLeft";
                case TextAnchor.UpperCenter: return "Top";
                case TextAnchor.UpperRight: return "TopRight";
                case TextAnchor.MiddleLeft: return "Left";
                case TextAnchor.MiddleRight: return "Right";
                case TextAnchor.LowerLeft: return "BottomLeft";
                case TextAnchor.LowerCenter: return "Bottom";
                case TextAnchor.LowerRight: return "BottomRight";
                default: return "Center";
            }
        }

        public static void SetText(Component c, string text)
        {
            if (c is Text t) t.text = text;
            else Refl.Set(c, "text", text);
        }

        public static void SetColor(Component c, Color color)
        {
            if (c is Graphic g) g.color = color;
            else Refl.Set(c, "color", color);
        }

        /// <summary>
        /// One of the game's own UI sprites by name: an exact match of the usual names for `what`, else null. All loaded
        /// sprites with `what` in their name are logged once (verbose) so the right one can be picked.
        /// </summary>
        public static Sprite GameSprite(string what)
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Sprite>();
                var hits = all.Where(sp => sp != null && sp.name.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (L.Verbose) L.Debug($"game sprites with '{what}': {string.Join(", ", hits.Select(h => $"{h.name} {h.rect.width:0}x{h.rect.height:0}").Distinct().Take(40).ToArray())}");
                string[] wanted = what == "exp" ? new[] { "icon_experience_big", "icon_experience", "icon_exp_small", "exp" } : new[] { what }; // the big one: the small 25x17 looked squashed
                foreach (var w in wanted)
                {
                    var sp = hits.FirstOrDefault(h => string.Equals(h.name, w, StringComparison.OrdinalIgnoreCase));
                    if (sp != null) { L.Info($"game sprite for '{what}': {sp.name}"); return sp; }
                }
            }
            catch (Exception e) { L.Debug($"game sprite '{what}': {e.Message}"); }
            return null;
        }

        public static void SetSize(Component c, float size)
        {
            if (c is Text t) t.fontSize = Mathf.RoundToInt(size);
            else Refl.Set(c, "fontSize", size);
        }

        public static void SetWrap(Component c, bool wrap)
        {
            if (c is Text t) t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            else Refl.Set(c, "enableWordWrapping", wrap);
        }

        // ---------------------------------------------------------------- generated pictures

        private static Sprite _white, _vgrad, _hgrad, _radial, _dots, _cut, _lock, _vignette;
        private static Sprite[] _grime;
        private static Sprite _dotGrid, _cutTR, _tabIcon, _tick, _cornerGlow;

        /// <summary>A glow from the right edge that also fades out toward the bottom (no hard edge anywhere). White: tint it.</summary>
        private static Sprite _hatch;

        /// <summary>Diagonal stripes like the game's empty slots (a 16 px tile, white; tint it faint and tile it).</summary>
        private static Sprite _chamfer, _plate, _leader, _grain, _hollowTick, _dither;

        /// <summary>
        /// CoD's pixel dissolve: white dots on an ordered (Bayer) dither, dense on the left and thinning out to nothing on the
        /// right. 64 x 8, point-filtered; stretch it along an edge.
        /// </summary>
        public static Sprite Dither()
        {
            if (_dither != null) return _dither;
            const int w = 64, h = 8;
            int[,] bayer = { { 0, 8, 2, 10 }, { 12, 4, 14, 6 }, { 3, 11, 1, 9 }, { 15, 7, 13, 5 } };
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float density = Mathf.Pow(1 - x / (float)(w - 1), 1.4f) * (1 - y / (float)h * .6f);
                    bool on = (bayer[y % 4, x % 4] + .5f) / 16f < density;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(on ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _dither = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            return _dither;
        }

        /// <summary>Registration marks: a small "+" just outside each corner of a panel (CoD's HUD framing).</summary>
        public static void CornerMarks(RectTransform frame, Color color, float arm = 5, float gap = 6)
        {
            var corners = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            foreach (var c in corners)
            {
                var off = new Vector2(c.x == 0 ? -gap : gap, c.y == 0 ? -gap : gap);
                var h = Box(frame, "MarkH", c, off, new Vector2(arm * 2 + 1, 1)); Img(h, color).raycastTarget = false;
                var v = Box(frame, "MarkV", c, off, new Vector2(1, arm * 2 + 1)); Img(v, color).raycastTarget = false;
            }
        }

        /// <summary>A hollow check mark (outline only), white, 256 px, antialiased: the unlock stamp.</summary>
        public static Sprite HollowTick()
        {
            if (_hollowTick != null) return _hollowTick;
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[n * n];
            // the check's centre line: short leg down-right, long leg up-right
            Vector2 a = new Vector2(.16f, .52f) * n, b = new Vector2(.40f, .26f) * n, c = new Vector2(.86f, .78f) * n;
            float half = .075f * n, edge = .022f * n; // stroke half-width, outline thickness
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + .5f, y + .5f);
                    float d = Mathf.Min(SegDist(p, a, b), SegDist(p, b, c));
                    float ring = Mathf.Abs(d - half);                       // distance to the shape's outline
                    float al = Mathf.Clamp01(1 - (ring - edge) / 1.2f);     // a thin antialiased line along it
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * al));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            _hollowTick = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
            return _hollowTick;
        }

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>
        /// A white shape with its top-left and bottom-right corners cut at 45° (Tarkov's prestige tiles, its tabs): 9-sliced,
        /// so any size keeps the same cut. Two of them (frame + face 1 px in) make a cut-corner outline.
        /// </summary>
        public static Sprite Chamfer()
        {
            if (_chamfer != null) return _chamfer;
            const int n = 32, c = 8;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float tl = x + (n - 1 - y) - c + 1, br = (n - 1 - x) + y - c + 1; // distance past each cut line
                    float a = Mathf.Clamp01(Mathf.Min(tl, br) / 1.2f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _chamfer = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(c + 1, c + 1, c + 1, c + 1));
            return _chamfer;
        }

        /// <summary>
        /// The worn pale plate of Tarkov's selected sub-tabs (SKILLS, ACTIVE TASKS…): off-white paint with ragged, chipped
        /// edges, dirt and faint scratches. Its own colours (tint it white); 9-sliced so it stretches to any label.
        /// </summary>
        public static Sprite WornPlate()
        {
            if (_plate != null) return _plate;
            const int w = 192, h = 48;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            var rnd = new System.Random(7);
            for (int y = 0; y < h; y++)
            {
                float scratch = rnd.NextDouble() < .12 ? .9f : 1f; // a few faint horizontal scuffs
                for (int x = 0; x < w; x++)
                {
                    float d = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                    // lighter wear than before (VIEWING / "41–45" read as smudges): ragged edges, a few chips, a solid middle
                    float erode = .8f + 2.2f * Mathf.PerlinNoise(x * .19f + 3.1f, y * .19f + 7.7f) * Mathf.PerlinNoise(x * .05f, y * .05f + 11f) * 1.6f;
                    float a = Mathf.Clamp01((d - erode) / 1.2f);
                    if (d < 4 && Mathf.PerlinNoise(x * .6f + 40, y * .6f) > .82f) a *= .35f; // chips near the edge
                    float dirt = .86f + .14f * Mathf.PerlinNoise(x * .08f + 20, y * .08f + 5) - .04f * Mathf.PerlinNoise(x * .5f, y * .5f + 30);
                    float v = dirt * scratch;
                    px[y * w + x] = new Color32((byte)(232 * v), (byte)(229 * v), (byte)(219 * v), (byte)(255 * a));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _plate = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(16, 12, 16, 12));
            return _plate;
        }

        /// <summary>A dotted leader line (1 px dots, 3 px apart), tiled: "Character level ........ 25/25".</summary>
        public static Sprite Leader()
        {
            if (_leader != null) return _leader;
            var tex = new Texture2D(4, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[8];
            px[0] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _leader = Sprite.Create(tex, new Rect(0, 0, 4, 2), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            return _leader;
        }

        /// <summary>Film grain: fine random specks, white with varying alpha, tiled over the whole screen at a very low strength.</summary>
        public static Sprite Grain()
        {
            if (_grain != null) return _grain;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[n * n];
            var rnd = new System.Random(3);
            for (int i = 0; i < px.Length; i++)
            {
                int r = rnd.Next(256);
                bool light = rnd.Next(2) == 0;
                byte c = light ? (byte)255 : (byte)0;
                px[i] = new Color32(c, c, c, (byte)(r * r / 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _grain = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            return _grain;
        }

        public static Sprite Hatch()
        {
            if (_hatch != null) return _hatch;
            const int n = 16;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int d = (x + y) % n; // 45° stripes, 3 px wide every 16
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(d < 3 ? 255 : 0));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _hatch = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            return _hatch;
        }

        private static Sprite _hexSpinner;

        /// <summary>A hexagon ring in three parts (like the game's loading mark), white on transparent, 128 px.</summary>
        public static Sprite HexSpinner()
        {
            if (_hexSpinner != null) return _hexSpinner;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
                    float ax = Mathf.Abs(u), ay = Mathf.Abs(v);
                    float d = Mathf.Max(ax, ax * .5f + ay * .866f); // pointy-top hexagon "radius"
                    float ring = Mathf.Clamp01((d - .58f) / .02f) * Mathf.Clamp01((.86f - d) / .02f);
                    // three gaps, 120° apart, cut at a slant (the blades look like they interlock)
                    float ang = (Mathf.Atan2(v, u) * Mathf.Rad2Deg + 360f + (d - .58f) * 60f) % 120f;
                    float gap = Mathf.Clamp01((ang - 8f) / 2f) * Mathf.Clamp01((118f - ang) / 2f);
                    byte a = (byte)(255 * ring * gap);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            _hexSpinner = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
            return _hexSpinner;
        }

        public static Sprite CornerGlow()
        {
            if (_cornerGlow != null) return _cornerGlow;
            const int w = 128, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fx = Mathf.Pow(x / (w - 1f), 2.2f);                       // strongest at the right edge
                    float fy = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.25f, .85f, y / (h - 1f))); // gone before the bottom
                    tex.SetPixel(x, y, new Color(1, 1, 1, fx * fy));
                }
            tex.Apply();
            return _cornerGlow = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
        }

        /// <summary>A check mark (white, tint it), anti-aliased.</summary>
        public static Sprite Tick()
        {
            if (_tick != null) return _tick;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            float Seg(Vector2 p, Vector2 a, Vector2 b)
            {
                var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                return (p - (a + ab * t)).magnitude;
            }
            Vector2 p0 = new Vector2(6, 16), p1 = new Vector2(13, 9), p2 = new Vector2(26, 24);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + .5f, y + .5f);
                    float d = Mathf.Min(Seg(p, p0, p1), Seg(p, p1, p2));
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(2.6f - d)));
                }
            tex.Apply();
            return _tick = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>A block with its top-right corner cut off (the EXPANSIONS icon shape). White: tint it.</summary>
        public static Sprite CutCornerTopRight()
        {
            if (_cutTR != null) return _cutTR;
            const int w = 84, h = 112, cut = 22;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = (x + .5f) + (y + .5f) - (w + h - cut); // past the diagonal near the top-right
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(.5f - d / 1.414f)));
                }
            tex.Apply();
            return _cutTR = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
        }

        /// <summary>The menu tab icon: a red block with its top-right corner cut and a dark diamond in it (coloured, not white).</summary>
        public static Sprite TabIcon()
        {
            if (_tabIcon != null) return _tabIcon;
            const int n = 64, cut = 16, ss = 3;
            var red = Hex("#d8412f");
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int inBlock = 0, inDiamond = 0;
                    for (int j = 0; j < ss; j++)
                        for (int i = 0; i < ss; i++)
                        {
                            float px = x + (i + .5f) / ss, py = y + (j + .5f) / ss;
                            bool block = px >= 6 && px <= n - 6 && py >= 2 && py <= n - 2 && (px + py) <= (2 * n - 8 - cut);
                            if (!block) continue;
                            inBlock++;
                            float d = Mathf.Abs(px - n / 2f + 2) + Mathf.Abs(py - n / 2f + 2);
                            if (d <= 17 && d >= 9 || d <= 4) inDiamond++;
                        }
                    float a = inBlock / (float)(ss * ss), dia = inBlock == 0 ? 0 : inDiamond / (float)inBlock;
                    var c = Color.Lerp(red, new Color(.08f, .05f, .05f), dia);
                    c.a = a;
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return _tabIcon = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>A repeating tile: a fine dot grid with a faint line every 4th dot (the Arena background pattern).</summary>
        public static Sprite DotGrid()
        {
            if (_dotGrid != null) return _dotGrid;
            const int n = 24, step = 6;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = 0;
                    if (x % step == 0 && y % step == 0) a = 1;            // dots
                    else if (x == 0 || y == 0) a = .35f;                   // the faint line through every 4th dot
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            return _dotGrid = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Clear in the middle, darkening toward the edges and corners (tint it black).</summary>
        public static Sprite Vignette()
        {
            if (_vignette != null) return _vignette;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx * .8f + dy * dy * 1.1f);
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.55f, 1.35f, d));
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            return _vignette = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>Grit, smudges and a few scratches, mostly along the edges (a few variants; tint light grey, low alpha).</summary>
        public static Sprite Grime(int variant)
        {
            if (_grime == null) _grime = new Sprite[4];
            variant = Mathf.Abs(variant) % _grime.Length;
            if (_grime[variant] != null) return _grime[variant];
            const int n = 128;
            var rnd = new System.Random(1234 + variant * 77);
            float ox = (float)rnd.NextDouble() * 100, oy = (float)rnd.NextDouble() * 100;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var a = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n, v = y / (float)n;
                    // how close to an edge (1 at the border, 0 in the middle)
                    float edge = Mathf.Clamp01(1 - Mathf.Min(Mathf.Min(u, 1 - u), Mathf.Min(v, 1 - v)) / .22f);
                    float big = Mathf.PerlinNoise(ox + u * 4, oy + v * 4), fine = Mathf.PerlinNoise(ox + u * 22, oy + v * 22);
                    float smudge = Mathf.Clamp01((big - .5f) * 3.2f) * edge;
                    float grit = fine > .72f ? (fine - .72f) * 3f * (.35f + edge) : 0;
                    a[y * n + x] = Mathf.Clamp01(smudge * .8f + grit);
                }
            // a few thin scratches
            for (int k = 0; k < 5; k++)
            {
                float x0 = (float)rnd.NextDouble() * n, y0 = (float)rnd.NextDouble() * n, ang = (float)rnd.NextDouble() * Mathf.PI, len = 10 + (float)rnd.NextDouble() * 26;
                for (float t = 0; t < len; t += .5f)
                {
                    int x = (int)(x0 + Mathf.Cos(ang) * t), y = (int)(y0 + Mathf.Sin(ang) * t);
                    if (x < 0 || y < 0 || x >= n || y >= n) break;
                    a[y * n + x] = Mathf.Max(a[y * n + x], .55f * (1 - t / len));
                }
            }
            for (int i = 0; i < a.Length; i++) tex.SetPixel(i % n, i / n, new Color(1, 1, 1, a[i]));
            tex.Apply();
            return _grime[variant] = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>Lays grit and smudges over a box (variant picks the pattern; rotated/flipped for variety).</summary>
        public static void Grit(RectTransform box, int variant, float alpha = .13f)
        {
            var rt = Fill(box, "Grit");
            rt.localEulerAngles = new Vector3(0, 0, 90 * ((variant / 4 + variant) % 4));
            if ((variant / 2) % 2 == 1) rt.localScale = new Vector3(-1, 1, 1);
            var img = Img(rt, new Color(.82f, .85f, .86f, alpha), Grime(variant));
            img.raycastTarget = false;
            Grits.Add((img, alpha));
        }

        /// <summary>Every scratch / smudge overlay with its base opacity (F12 > Graphics > Scratches scales them).</summary>
        internal static readonly List<(Image Img, float Alpha)> Grits = new List<(Image, float)>();

        /// <summary>A small padlock (white, tint it), drawn with 4x supersampling for smooth edges.</summary>
        public static Sprite Lock()
        {
            if (_lock != null) return _lock;
            const int n = 48, ss = 4;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            bool Inside(float x, float y)
            {
                // body: rounded box
                float bx0 = 9, bx1 = 39, by0 = 4, by1 = 26, r = 4;
                float qx = Mathf.Max(bx0 + r - x, 0, x - (bx1 - r)), qy = Mathf.Max(by0 + r - y, 0, y - (by1 - r));
                bool body = x >= bx0 && x <= bx1 && y >= by0 && y <= by1 && qx * qx + qy * qy <= r * r;
                // keyhole
                float kx = x - 24, ky = y - 16;
                bool hole = kx * kx + ky * ky <= 9 || (Mathf.Abs(kx) <= 1.5f && y >= 9 && y <= 16);
                if (body) return !hole;
                // shackle: a thick ring, top half, with legs down into the body
                float sx = x - 24, sy = y - 31, d = Mathf.Sqrt(sx * sx + sy * sy);
                bool ring = d >= 7 && d <= 11.5f && sy >= 0;
                bool legs = y >= 25 && y <= 31 && ((x >= 12.5f && x <= 17) || (x >= 31 && x <= 35.5f));
                return ring || legs;
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int hit = 0;
                    for (int j = 0; j < ss; j++) for (int i = 0; i < ss; i++) if (Inside(x + (i + .5f) / ss, y + (j + .5f) / ss)) hit++;
                    tex.SetPixel(x, y, new Color(1, 1, 1, hit / (float)(ss * ss)));
                }
            tex.Apply();
            return _lock = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>Darkens a box's edges toward the inside (the soft fade around Arena's item previews).</summary>
        public static void EdgeFade(RectTransform box, float depth = .16f, float alpha = .6f)
        {
            var c = new Color(0, 0, 0, alpha);
            var top = Rect(box, "FadeT", new Vector2(0, 1 - depth), Vector2.one, Vector2.zero, Vector2.zero);
            Img(top, c, VerticalFade());
            var bottom = Rect(box, "FadeB", Vector2.zero, new Vector2(1, depth), Vector2.zero, Vector2.zero);
            bottom.localScale = new Vector3(1, -1, 1);
            Img(bottom, c, VerticalFade());
            var right = Rect(box, "FadeR", new Vector2(1 - depth, 0), Vector2.one, Vector2.zero, Vector2.zero);
            Img(right, c, HorizontalFade());
            var left = Rect(box, "FadeL", Vector2.zero, new Vector2(depth, 1), Vector2.zero, Vector2.zero);
            left.localScale = new Vector3(-1, 1, 1);
            Img(left, c, HorizontalFade());
        }

        /// <summary>A square with its bottom-right corner cut off at 45° (the Arena level box).</summary>
        public static Sprite CutCorner()
        {
            if (_cut != null) return _cut;
            const int n = 128, cut = 30;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // distance past the diagonal x - y = n - cut (texture y grows upward: bottom-right is x high, y low)
                    float d = (x + .5f) - (y + .5f) - (n - cut);
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(.5f - d / 1.414f)));
                }
            tex.Apply();
            return _cut = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>How wide this text would be in the label (TextMeshPro GetPreferredValues, or the legacy Text's).</summary>
        public static float PreferredWidth(Component c, string text)
        {
            try
            {
                if (c is Text t) { var old = t.text; t.text = text; var w = t.preferredWidth; t.text = old; return w; }
                var m = c.GetType().GetMethod("GetPreferredValues", new[] { typeof(string) });
                if (m?.Invoke(c, new object[] { text }) is Vector2 v) return v.x;
            }
            catch { }
            return text.Length * 12;
        }

        /// <summary>See-through on the left, white on the right, eased so the glow hugs the right edge — tint it for bloom.</summary>
        public static Sprite HorizontalFade()
        {
            if (_hgrad != null) return _hgrad;
            const int w = 256;
            var tex = new Texture2D(w, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < w; x++) { float a = Mathf.Pow(x / (w - 1f), 2.2f); var c = new Color(1, 1, 1, a); tex.SetPixel(x, 0, c); tex.SetPixel(x, 1, c); }
            tex.Apply();
            return _hgrad = Sprite.Create(tex, new Rect(0, 0, w, 2), new Vector2(.5f, .5f));
        }

        /// <summary>A soft round glow (white in the middle, see-through at the edge) — tint it for corner bloom.</summary>
        public static Sprite Radial()
        {
            if (_radial != null) return _radial;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1 - d);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
            tex.Apply();
            return _radial = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>A dotted line that fades out toward its right end (flip it with a -1 x scale for the left side).</summary>
        public static Sprite FadingDots()
        {
            if (_dots != null) return _dots;
            const int w = 240, h = 2;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            for (int x = 0; x < w; x++)
            {
                float a = (x % 6) < 3 ? Mathf.Pow(1 - x / (float)w, 1.4f) : 0;
                for (int y = 0; y < h; y++) tex.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            tex.Apply();
            return _dots = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
        }

        public static Sprite White()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) tex.SetPixel(x, y, Color.white);
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
            return _white;
        }

        /// <summary>White at the top fading to see-through at the bottom (tint it with the Image color).</summary>
        public static Sprite VerticalFade()
        {
            if (_vgrad != null) return _vgrad;
            var tex = new Texture2D(2, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) { var c = new Color(1, 1, 1, y / 63f); tex.SetPixel(0, y, c); tex.SetPixel(1, y, c); }
            tex.Apply();
            _vgrad = Sprite.Create(tex, new Rect(0, 0, 2, 64), new Vector2(.5f, .5f));
            return _vgrad;
        }

        /// <summary>A picture file next to the plugin (e.g. the Tarkov logo), or null.</summary>
        public static Sprite LoadPng(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) { L.Debug("no picture at " + path); return null; }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(path))) { L.Warn("couldn't read picture " + path); return null; }
                L.Debug($"picture {System.IO.Path.GetFileName(path)}: {tex.width}x{tex.height}");
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f));
            }
            catch (Exception e) { L.Error("loading " + path, e); return null; }
        }
    }

    /// <summary>An Arena-style dotted line: short dashes, bright by the label and fading out toward a small tick
    /// at the far end. Drawn as its own mesh, so the dashes stay evenly spaced at any size (a stretched sprite didn't).</summary>
    internal sealed class DottedLine : MaskableGraphic
    {
        public bool FadeToRight = true;
        public float Dash = 4, Gap = 3, Thickness = 2, Tick = 14, Margin = 8;

        public static DottedLine Add(RectTransform rt, bool fadeToRight)
        {
            var d = rt.gameObject.AddComponent<DottedLine>();
            d.FadeToRight = fadeToRight;
            d.raycastTarget = false;
            return d;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float w = r.width - Margin, cy = r.center.y;
            if (w <= Dash) return;
            // x measured from the label end outward
            float X(float d) => FadeToRight ? r.xMin + Margin + d : r.xMax - Margin - d;
            void Quad(float d0, float d1, float h, float a)
            {
                float x0 = X(d0), x1 = X(d1);
                if (x0 > x1) { var t = x0; x0 = x1; x1 = t; }
                var c = color; c.a *= a;
                int i = vh.currentVertCount;
                vh.AddVert(new Vector3(x0, cy - h / 2), c, Vector2.zero);
                vh.AddVert(new Vector3(x0, cy + h / 2), c, Vector2.zero);
                vh.AddVert(new Vector3(x1, cy + h / 2), c, Vector2.zero);
                vh.AddVert(new Vector3(x1, cy - h / 2), c, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i, i + 2, i + 3);
            }
            float end = w - 2;
            for (float d = 0; d + Dash <= end - Gap; d += Dash + Gap)
            {
                float t = d / end;
                Quad(d, d + Dash, Thickness, Mathf.Lerp(.95f, .12f, Mathf.Pow(t, .7f)));
            }
            Quad(end, end + 2, Tick, .45f); // the end tick |
        }
    }

    /// <summary>Calls back on pointer enter / exit (hover states).</summary>
    internal sealed class HoverHook : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Action<bool> On;
        public static HoverHook Add(Component c, Action<bool> on)
        {
            var h = c.gameObject.GetComponent<HoverHook>() ?? c.gameObject.AddComponent<HoverHook>();
            h.On = on;
            return h;
        }
        // while the card row is being dragged, nothing under the pointer lights up or plays its hover sound (it was a flood of sounds)
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { if (ProgScreen.Dragging) return; try { On?.Invoke(true); } catch (Exception x) { L.ErrorOnce("hover", x); } }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { try { On?.Invoke(false); } catch (Exception x) { L.ErrorOnce("hover", x); } }
        private void OnDisable() { try { On?.Invoke(false); } catch { } }
    }
}
