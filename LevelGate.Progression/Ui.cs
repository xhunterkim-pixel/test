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
            // 0.9.9 Small Text: information text (9 px and up) never smaller than this; decorative micro text (under 9) stays
            float floor = ProgScreen.Polish.SmallText;
            if (floor > 0 && size >= 9 && size < floor) size = floor;
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

        internal static void SetEnumPublic(Component c, string prop, string value) => SetEnum(c, prop, value);

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

        /// <summary>
        /// CoD's NEW tag: bright yellow bold "NEW" in a dark see-through box with a thin yellow outline, a second fainter
        /// outline offset down-right behind it (the "echo"), and a faint yellow glow. The badge rect is the box; place it
        /// where you want it (CoD pins it to a tile's top-right corner, overhanging the edge).
        /// </summary>
        public static RectTransform NewBadge(RectTransform parent, Vector2 anchor, Vector2 pos, float w = 34, float h = 17, float textSize = 11.5f)
        {
            switch (ProgScreen.Polish.NewTag)
            {
                case ProgScreen.NewTagLook.MW4: return NewBadgeMw4(parent, anchor, pos, w, h, textSize);
                case ProgScreen.NewTagLook.Old: return NewBadgeOld(parent, anchor, pos, w, h, textSize);
                default: return NewBadgeMw4Dark(parent, anchor, pos, w, h, textSize);
            }
        }

        /// <summary>
        /// MW4's NEW, from the user's crop (0.9.95): a dark olive see-through box, bright yellow bold NEW, a thin gold outline
        /// (brightest along the top, where the card's own top edge runs into it), a faint gold wash rising from the bottom and a
        /// soft yellow glow around it. The text is the brightest thing; the box only frames it.
        /// </summary>
        private static RectTransform NewBadgeMw4Dark(RectTransform parent, Vector2 anchor, Vector2 pos, float w, float h, float textSize)
        {
            var yellow = Hex("#f2e04a");
            var gold = Hex("#b39a2e");
            var root = Box(parent, "New", anchor, pos, new Vector2(w, h));
            Img(Box(root, "Glow", new Vector2(.5f, .5f), Vector2.zero, new Vector2(w * 2f, h * 2.6f)), new Color(yellow.r, yellow.g, yellow.b, .10f), Radial()).raycastTarget = false;
            Img(Fill(root, "Base"), new Color(.10f, .10f, .04f, .78f)).raycastTarget = false;
            var wash = Img(Fill(root, "Wash"), new Color(gold.r, gold.g, gold.b, .16f), VerticalFade()); wash.raycastTarget = false;
            wash.rectTransform.localScale = new Vector3(1, -1, 1); // brightest at the bottom
            Outline(root, new Color(gold.r, gold.g, gold.b, .8f));
            Img(Rect(root, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero), new Color(yellow.r, yellow.g, yellow.b, .95f)).raycastTarget = false;
            var t = Label(root, "Text", "NEW", textSize, yellow, TextAnchor.MiddleCenter, true, 1.5f);
            ((Graphic)t).raycastTarget = false;
            return root;
        }

        /// <summary>
        /// MW4's NEW (0.9.92): a gold box whose fill is solid on the right and fades out towards the left, a lit edge along its
        /// top and left, a darker one along the bottom and right, and a second box of the same size behind it, 3 px down-right
        /// (the "double box"), dark with a faint gold edge. Bright yellow bold NEW on top. No glow.
        /// </summary>
        private static RectTransform NewBadgeMw4(RectTransform parent, Vector2 anchor, Vector2 pos, float w, float h, float textSize)
        {
            var yellow = Hex("#f7e43a");
            var gold = Hex("#8a7526");
            var lit = Hex("#d9bd4a");
            var root = Box(parent, "New", anchor, pos, new Vector2(w, h));
            // the box behind: same size, down-right, dark with a faint gold rim
            var back = Rect(root, "Back", Vector2.zero, Vector2.one, new Vector2(3, -3), new Vector2(3, -3));
            Img(Fill(back, "Fill"), new Color(.06f, .055f, .03f, .85f)).raycastTarget = false;
            Img(Fill(back, "Gold"), new Color(gold.r, gold.g, gold.b, .35f), HorizontalFade()).raycastTarget = false;
            Outline(back, new Color(gold.r, gold.g, gold.b, .55f));
            // the front box: dark underneath so the text always reads, gold fading from the right to the left
            Img(Fill(root, "Base"), new Color(.07f, .06f, .025f, .9f)).raycastTarget = false;
            Img(Fill(root, "Gold"), new Color(gold.r, gold.g, gold.b, .95f), HorizontalFade()).raycastTarget = false;
            Img(Fill(root, "Floor"), new Color(gold.r, gold.g, gold.b, .28f)).raycastTarget = false; // the left end never goes fully flat
            // edges: lit top / left, shaded bottom / right
            Img(Rect(root, "T", new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero), new Color(lit.r, lit.g, lit.b, .9f)).raycastTarget = false;
            Img(Rect(root, "L", Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(1, 0)), new Color(lit.r, lit.g, lit.b, .95f)).raycastTarget = false;
            Img(Rect(root, "B", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)), new Color(gold.r * .6f, gold.g * .6f, gold.b * .6f, .9f)).raycastTarget = false;
            Img(Rect(root, "R", new Vector2(1, 0), Vector2.one, new Vector2(-1, 1), new Vector2(0, -1)), new Color(gold.r * .8f, gold.g * .8f, gold.b * .8f, .7f)).raycastTarget = false;
            var t = Label(root, "Text", "NEW", textSize, yellow, TextAnchor.MiddleCenter, true, 1.5f);
            ((Graphic)t).raycastTarget = false;
            return root;
        }

        private static RectTransform NewBadgeOld(RectTransform parent, Vector2 anchor, Vector2 pos, float w, float h, float textSize)
        {
            var yellow = Hex("#f4e23c");
            var root = Box(parent, "New", anchor, pos, new Vector2(w, h));
            var glow = Img(Box(root, "Glow", new Vector2(.5f, .5f), Vector2.zero, new Vector2(w * 2.2f, h * 2.6f)), new Color(yellow.r, yellow.g, yellow.b, .12f), Radial());
            glow.raycastTarget = false;
            // the echo: the same outline, 3 px down-right, fainter
            var echo = Rect(root, "Echo", Vector2.zero, Vector2.one, new Vector2(3, -3), new Vector2(3, -3));
            Outline(echo, new Color(yellow.r, yellow.g, yellow.b, .35f));
            // MW's face: dark on the right, fading out towards the left (a faint base keeps the text readable), with a yellow
            // wash rising the same way
            Img(Fill(root, "Base"), new Color(.07f, .07f, .03f, .35f)).raycastTarget = false;
            Img(Fill(root, "Face"), new Color(.05f, .05f, .02f, .92f), HorizontalFade()).raycastTarget = false;
            Img(Fill(root, "Wash"), new Color(yellow.r, yellow.g, yellow.b, .16f), HorizontalFade()).raycastTarget = false;
            Outline(root, new Color(yellow.r, yellow.g, yellow.b, .95f));
            var t = Label(root, "Text", "NEW", textSize, yellow, TextAnchor.MiddleCenter, true, 1.5f);
            ((Graphic)t).raycastTarget = false;
            return root;
        }

        /// <summary>A 1 px outline inside a rect (four thin images).</summary>
        public static void Outline(RectTransform rt, Color c)
        {
            Img(Rect(rt, "T", new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero), c).raycastTarget = false;
            Img(Rect(rt, "B", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)), c).raycastTarget = false;
            Img(Rect(rt, "L", Vector2.zero, new Vector2(0, 1), new Vector2(0, 1), new Vector2(1, -1)), c).raycastTarget = false;
            Img(Rect(rt, "R", new Vector2(1, 0), Vector2.one, new Vector2(-1, 1), new Vector2(0, -1)), c).raycastTarget = false;
        }

        private static Sprite[] _prints = new Sprite[3];

        /// <summary>
        /// Fingerprints and smudges (CoD's handled-glass look): a few partial prints (warped concentric ridges fading out
        /// at their edges) and soft smears, white on transparent, 256 px. Three variants.
        /// </summary>
        public static Sprite Fingerprints(int variant)
        {
            variant = ((variant % 3) + 3) % 3;
            if (_prints[variant] != null) return _prints[variant];
            const int n = 256;
            var rnd = new System.Random(101 + variant * 17);
            var a = new float[n * n];
            int prints = 2 + rnd.Next(2);
            for (int k = 0; k < prints; k++)
            {
                float cx = (float)rnd.NextDouble() * n, cy = (float)rnd.NextDouble() * n, r = 34 + (float)rnd.NextDouble() * 26;
                float rot = (float)rnd.NextDouble() * 6.28f, sq = .62f + (float)rnd.NextDouble() * .2f, ox = (float)rnd.NextDouble() * 50;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x - cx, dy = y - cy;
                        float u = dx * Mathf.Cos(rot) + dy * Mathf.Sin(rot), v = (-dx * Mathf.Sin(rot) + dy * Mathf.Cos(rot)) / sq;
                        float d = Mathf.Sqrt(u * u + v * v);
                        if (d > r * 1.3f) continue;
                        float warp = (Mathf.PerlinNoise(x * .03f + ox, y * .03f) - .5f) * 7f;
                        float ridge = .5f + .5f * Mathf.Sin((d + warp) * 1.25f);          // ~5 px apart
                        float fade = Mathf.Clamp01(1 - d / (r * 1.3f));
                        float broken = Mathf.PerlinNoise(x * .09f + ox, y * .09f + 7) > .38f ? 1 : .2f; // partial, like a real print
                        a[y * n + x] = Mathf.Max(a[y * n + x], ridge * ridge * fade * fade * broken * .8f);
                    }
            }
            // soft smears
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float sm = Mathf.Clamp01((Mathf.PerlinNoise(x * .012f + variant * 9, y * .03f) - .55f) * 2.2f) * .35f;
                    a[y * n + x] = Mathf.Max(a[y * n + x], sm);
                }
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(a[i])));
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return _prints[variant] = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>
        /// "Handled" texture on an important surface: fingerprints / smudges and a faint purple + green colour mottling
        /// (like CoD's cards). Faint; follows F12 UI Detailing.
        /// </summary>
        public static RectTransform Handled(RectTransform parent, int variant, float alpha = .05f)
        {
            var box = Fill(parent, "Handled"); // one container, so a surface can switch its handled look on / off
            var fp = Img(Fill(box, "Prints"), new Color(.9f, .9f, .92f, alpha), Fingerprints(variant));
            fp.raycastTarget = false; fp.preserveAspect = false;
            AddGrit(fp, alpha);
            var rnd = new System.Random(variant * 31 + 5);
            var tints = new[] { new Color(.65f, .35f, .7f), new Color(.45f, .7f, .4f) };
            for (int i = 0; i < 2; i++)
            {
                var anchor = new Vector2((float)rnd.NextDouble() * .8f + .1f, (float)rnd.NextDouble() * .8f + .1f);
                var blob = Img(Box(box, "Mottle", anchor, Vector2.zero, new Vector2(220, 160)), new Color(tints[i].r, tints[i].g, tints[i].b, alpha * .9f), Radial());
                blob.raycastTarget = false;
                AddGrit(blob, alpha * .9f);
            }
            return box;
        }

        private static Sprite _glowFrame, _scan, _vstripes, _grid;

        /// <summary>Fine vertical stripes (1 px on, 2 off), tiled: CoD's "BONUS" bar / barcode texture.</summary>
        public static Sprite VStripes()
        {
            if (_vstripes != null) return _vstripes;
            var tex = new Texture2D(3, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[6];
            px[0] = px[3] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _vstripes = Sprite.Create(tex, new Rect(0, 0, 3, 2), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>A blueprint grid: 1 px lines every 32 px with a fainter line every 8, tiled.</summary>
        public static Sprite BlueprintGrid()
        {
            if (_grid != null) return _grid;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    byte a = x == 0 || y == 0 ? (byte)255 : (x % 8 == 0 && y % 2 == 0) || (y % 8 == 0 && x % 2 == 0) ? (byte)90 : (byte)0;
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _grid = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>A soft glow around a frame (bloom): see-through in the middle, bright at the frame's line, fading outward.
        /// 9-sliced; give it a rect ~16 px bigger than the thing it lights on every side.</summary>
        // GlowFrame's outline (80 px sprite, 16 px pad, 8 px cuts), counter-clockwise from the top edge's cut end
        private static readonly Vector2[] _gfv = { new Vector2(24, 64), new Vector2(64, 64), new Vector2(64, 24), new Vector2(56, 16), new Vector2(16, 16), new Vector2(16, 56) };

        private static float SegDist(float x, float y, Vector2 a, Vector2 b)
        {
            var ab = b - a; var ap = new Vector2(x - a.x, y - a.y);
            float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
            return (ap - ab * t).magnitude;
        }

        public static Sprite GlowFrame()
        {
            if (_glowFrame != null) return _glowFrame;
            // the glow follows the cards' shape: the same 8 px cuts top-left and bottom-right as Chamfer (0.9.69's square
            // corners stuck out past the cut)
            const int n = 80, pad = 16, c = 8, border = pad + c + 4;
            const float r2 = 1.41421356f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x + .5f, fy = y + .5f;
                    // exact distance to the six-sided outline (the card's rect with its two cut corners)
                    float tl = ((fx - pad) + ((n - pad) - fy) - c) / r2, br = (((n - pad) - fx) + (fy - pad) - c) / r2; // > 0: inside
                    bool inside = fx >= pad && fx <= n - pad && fy >= pad && fy <= n - pad && tl >= 0 && br >= 0;
                    float edge = float.MaxValue;
                    for (int k = 0; k < 6; k++) edge = Mathf.Min(edge, SegDist(fx, fy, _gfv[k], _gfv[(k + 1) % 6]));
                    float outD = inside ? 0 : edge, inD = inside ? edge : 0;
                    float a = outD > 0 ? Mathf.Pow(Mathf.Clamp01(1 - outD / pad), 2.2f) : Mathf.Pow(Mathf.Clamp01(1 - inD / 6f), 2f) * .6f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _glowFrame = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        private static Sprite _wave, _waveHot;

        /// <summary>
        /// MW's XP track: a band of thin vertical bars like an audio waveform, mirrored around the line (1 px bars every 3 px,
        /// heights from layered noise with the odd spike, brightest at the line). hot: 360 px, fading out to both ends (a glow
        /// segment); else 384 px and tileable.
        /// </summary>
        public static Sprite Waveform(bool hot)
        {
            if (hot ? _waveHot != null : _wave != null) return hot ? _waveHot : _wave;
            int w = hot ? 360 : 384, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = hot ? TextureWrapMode.Clamp : TextureWrapMode.Repeat, filterMode = FilterMode.Point, name = "LevelGate waveform" };
            var px = new Color32[w * h];
            var rnd = new System.Random(hot ? 5 : 3);
            for (int x = 0; x < w; x += 3)
            {
                // tileable noise: sines whose periods divide the width, plus a random jitter and an occasional spike
                float u = x / (float)w * Mathf.PI * 2;
                float n = .5f + .22f * Mathf.Sin(u * 3 + 1) + .15f * Mathf.Sin(u * 7 + 2) + .1f * Mathf.Sin(u * 16);
                n = Mathf.Clamp01(n * (.55f + .45f * (float)rnd.NextDouble()));
                if (rnd.NextDouble() < .06) n = Mathf.Min(1, n + .45f);
                float fade = hot ? Mathf.Clamp01(1 - Mathf.Abs(x / (float)w * 2 - 1)) : 1;
                fade = fade * fade * (3 - 2 * fade);
                int half = Mathf.RoundToInt(n * (h / 2 - 1));
                for (int dy = 0; dy <= half; dy++)
                {
                    float a = (1 - dy / (float)(h / 2)) * (.45f + .55f * n) * fade;
                    var c = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(a)));
                    px[(h / 2 + dy) * w + x] = c;
                    if (h / 2 - 1 - dy >= 0) px[(h / 2 - 1 - dy) * w + x] = c;
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            if (hot) _waveHot = sp; else _wave = sp;
            return sp;
        }

        private static Sprite _innerGlow;

        /// <summary>
        /// MW's panel edge: a soft glow fading inward from all four edges (strongest right at the edge, gone ~20 px in),
        /// drawn over a panel's background just inside its thin 1 px line. 9-sliced; white (tint it).
        /// </summary>
        public static Sprite InnerGlow()
        {
            if (_innerGlow != null) return _innerGlow;
            const int n = 64, b = 26;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LevelGate innerglow" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Min(Mathf.Min(x, n - 1 - x), Mathf.Min(y, n - 1 - y)) + .5f;
                    float a = Mathf.Exp(-d / 5.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(a)));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _innerGlow = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static readonly Sprite[] _dashFrames = new Sprite[3];

        /// <summary>
        /// MW's selection border: a steady 1 px line on top, left and right; the bottom broken into dashes of random length
        /// with stray dots below it.
        /// 9-sliced with tiled edges (Image.Type.Tiled), so the pattern repeats along any size. Three variants to swap
        /// between (the border "crawls"). White.
        /// </summary>
        public static Sprite DashFrame(int variant)
        {
            variant = ((variant % 3) + 3) % 3;
            if (_dashFrames[variant] != null) return _dashFrames[variant];
            // the cards' / tiles' own shape: the line runs 2 px in from the sprite's edge (use it 2 px outside the outline so
            // they coincide), with the same 8 px cuts top-left and bottom-right as Chamfer (0.9.72–0.9.74 drew a plain
            // rectangle 3–4 px outside: two borders that didn't agree at the cut corners — "broken")
            const int n = 48, L = 2, c = 8, b = L + c + 2;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "LevelGate dashframe" };
            var px = new Color32[n * n];
            var rnd = new System.Random(71 + variant * 13);
            void Put(int x, int y, float a) { if (x >= 0 && x < n && y >= 0 && y < n) px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.Max(px[y * n + x].a, 255 * a)); }
            int top = n - 1 - L, right = n - 1 - L;
            for (int x = L + c; x <= right; x++) Put(x, top, .92f);                       // top (after the top-left cut)
            for (int y = L; y <= top - c; y++) Put(L, y, .92f);                           // left (below the cut)
            for (int y = L + c; y <= top; y++) Put(right, y, .92f);                       // right (above the bottom-right cut)
            for (int k = 0; k <= c; k++) { Put(L + k, top - c + k, .92f); }                // the top-left cut
            for (int k = 0; k <= c; k++) { Put(right - c + k, L + k, .7f); }               // the bottom-right cut (part of the broken bottom)
            for (int x = L; x <= right - c; x++) Put(x, L, .35f);                          // bottom: a faint steady line (the breathing dots live on it)
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _dashFrames[variant] = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        private static Sprite _hstreaks;
        private static readonly Sprite[] _vstreaks = new Sprite[4];

        /// <summary>MW's hover glitch: thin horizontal streaks of random length and brightness, broken up. White.</summary>
        public static Sprite HStreaks()
        {
            if (_hstreaks != null) return _hstreaks;
            const int w = 256, h = 96;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "LevelGate hstreaks" };
            var px = new Color32[w * h];
            var rnd = new System.Random(41);
            for (int k = 0; k < 46; k++)
            {
                int y = rnd.Next(h), x0 = rnd.Next(-40, w), len = 12 + rnd.Next(170), thick = rnd.NextDouble() < .2 ? 2 : 1;
                float a0 = .25f + (float)rnd.NextDouble() * .75f;
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x0 + len); x++)
                {
                    if (rnd.NextDouble() < .12) continue; // broken up
                    float u = (x - x0) / (float)len, a = a0 * Mathf.Sin(u * Mathf.PI) * (.6f + .4f * (float)rnd.NextDouble());
                    for (int t = 0; t < thick && y + t < h; t++) { var c = px[(y + t) * w + x]; px[(y + t) * w + x] = new Color32(255, 255, 255, (byte)Mathf.Max(c.a, 255 * a)); }
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _hstreaks = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// MW's picture load-in: lines hanging down from the top edge, one every 2 px, of uneven length; the longer a line,
        /// the more it breaks up (gaps, brighter flecks). Fades toward each line's end. Tiles sideways. White.
        /// </summary>
        public static Sprite RevealStreaks(int variant = 0)
        {
            variant = ((variant % 4) + 4) % 4;
            if (_vstreaks[variant] != null) return _vstreaks[variant];
            const int w = 256, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, name = "LevelGate reveal" };
            var px = new Color32[w * h];
            var rnd = new System.Random(59 + variant * 101);
            float ph = variant * 1.7f; // each variant's long / short stretches fall elsewhere
            for (int x = 0; x < w; x += 2)
            {
                // lengths: mostly short, some very long (a smooth ridge plus noise, so neighbours roughly agree)
                float ridge = .5f + .3f * Mathf.Sin(x * .045f + ph) + .2f * Mathf.Sin(x * .13f + 1 + ph * 2);
                int len = Mathf.Clamp((int)(h * Mathf.Pow((float)rnd.NextDouble(), 1.6f) * (.4f + .8f * ridge)), 6, h);
                float broken = len / (float)h; // longer = more artifacts
                for (int d = 0; d < len; d++)
                {
                    float u = d / (float)len;
                    if (rnd.NextDouble() < broken * .45f * u) continue;          // gaps, more toward the end of long lines
                    float a = Mathf.Pow(1 - u, .8f) * (.55f + .45f * (float)rnd.NextDouble());
                    if (rnd.NextDouble() < broken * .08f) a = 1;                  // bright flecks
                    int y = h - 1 - d;                                             // hangs down from the top edge
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(a)));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _vstreaks[variant] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        private static Sprite _ruler;

        /// <summary>A measuring scale (tile it along a bottom edge): a tall tick every 50 px, short ones every 10. White.</summary>
        public static Sprite Ruler()
        {
            if (_ruler != null) return _ruler;
            const int w = 50, h = 9;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[w * h];
            for (int x = 0; x < w; x += 10)
            {
                int th = x == 0 ? h : 3;
                for (int y = 0; y < th; y++) px[y * w + x] = new Color32(255, 255, 255, (byte)(x == 0 ? 255 : 170));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _ruler = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Viewfinder corners: an L at each corner of a box, inset, 1 px (texture detail: follows UI Detailing).</summary>
        public static void Brackets(RectTransform box, float inset, float arm, Color color)
        {
            foreach (var c in new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) })
            {
                float sx = c.x == 0 ? 1 : -1, sy = c.y == 0 ? 1 : -1;
                var h = Rect(box, "BracketH", c, c, Vector2.zero, Vector2.zero);
                h.pivot = new Vector2(c.x, c.y); h.sizeDelta = new Vector2(arm, 1); h.anchoredPosition = new Vector2(sx * inset, sy * inset);
                var v = Rect(box, "BracketV", c, c, Vector2.zero, Vector2.zero);
                v.pivot = new Vector2(c.x, c.y); v.sizeDelta = new Vector2(1, arm); v.anchoredPosition = new Vector2(sx * inset, sy * inset);
                Detail(Img(h, color), color.a).raycastTarget = false;
                Detail(Img(v, color), color.a).raycastTarget = false;
            }
        }

        private static Sprite _halftone;

        /// <summary>
        /// A light made of dots (CoD's LED / halftone glow): a soft oval of small square dots on a 5 px grid, each dot as big as
        /// the light is strong there, so the glow itself carries a pattern. 2:1, white (tint it), 3x supersampled.
        /// </summary>
        public static Sprite HalftoneGlow()
        {
            if (_halftone != null) return _halftone;
            const int w = 320, h = 160, pitch = 5, ss = 3;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LevelGate halftone" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // the light's strength at this dot's centre (one value per cell, so every dot is a clean square)
                    int cx = x / pitch * pitch, cy = y / pitch * pitch;
                    float dx = (cx + pitch * .5f) / w * 2 - 1, dy = (cy + pitch * .5f) / h * 2 - 1;
                    float f = Mathf.Clamp01(1 - (dx * dx + dy * dy));
                    f = f * f * (3 - 2 * f); // smooth
                    float half = pitch * .32f * Mathf.Sqrt(f); // at most ~3 of 5 px: always a gap between dots (an LED wall, not a blob)
                    if (half <= .05f) continue;
                    int hit = 0;
                    for (int j = 0; j < ss; j++)
                        for (int i = 0; i < ss; i++)
                        {
                            float sx = x + (i + .5f) / ss - (cx + pitch * .5f), sy = y + (j + .5f) / ss - (cy + pitch * .5f);
                            if (Mathf.Abs(sx) <= half && Mathf.Abs(sy) <= half) hit++;
                        }
                    float a = hit / (float)(ss * ss) * (.35f + .65f * f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _halftone = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// Its own (nested) canvas, so what moves in it every frame only redraws itself, not the whole panel around it.
        /// Keeps the parent's masks and drawing order; takes no clicks.
        /// </summary>
        public static void OwnCanvas(RectTransform rt)
        {
            if (rt.GetComponent<Canvas>() == null) rt.gameObject.AddComponent<Canvas>();
        }

        /// <summary>Fine horizontal scanlines (1 px on, 2 off), tiled.</summary>
        public static Sprite Scanlines()
        {
            if (_scan != null) return _scan;
            var tex = new Texture2D(2, 3, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[6];
            px[4] = px[5] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _scan = Sprite.Create(tex, new Rect(0, 0, 2, 3), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Registration marks: a small "+" just outside each corner of a panel (CoD's HUD framing).</summary>
        public static void CornerMarks(RectTransform frame, Color color, float arm = 5, float gap = 6)
        {
            var corners = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            foreach (var c in corners)
            {
                var off = new Vector2(c.x == 0 ? -gap : gap, c.y == 0 ? -gap : gap);
                var h = Box(frame, "MarkH", c, off, new Vector2(arm * 2 + 1, 1)); Detail(Img(h, color), color.a).raycastTarget = false;
                var v = Box(frame, "MarkV", c, off, new Vector2(1, arm * 2 + 1)); Detail(Img(v, color), color.a).raycastTarget = false;
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

        private static readonly Sprite[] _pipBox = new Sprite[2];

        /// <summary>
        /// MW's rank pip: a small box with its top-left and bottom-right corners cut, solid (filled) or as a 1 px outline
        /// (hollow) — 20 px, drawn at ~10 so it stays crisp; not sliced (the cut stays in proportion).
        /// </summary>
        public static Sprite PipBox(bool filled)
        {
            int k = filled ? 1 : 0;
            if (_pipBox[k] != null) return _pipBox[k];
            const int n = 20; const float c = 6, line = 2.2f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LevelGate pip" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = x + .5f, fy = y + .5f;
                    // distance inside the shape: the four sides and the two cuts (top-left, bottom-right)
                    float d = Mathf.Min(Mathf.Min(fx, n - fx), Mathf.Min(fy, n - fy));
                    d = Mathf.Min(d, (fx + (n - fy) - c) * .7071f);
                    d = Mathf.Min(d, ((n - fx) + fy - c) * .7071f);
                    float a = Mathf.Clamp01(d + .5f);
                    if (!filled) a *= Mathf.Clamp01(line - d + .5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _pipBox[k] = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
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
            AddGrit(img, alpha);
        }

        /// <summary>Every scratch / smudge overlay with its base opacity (F12 > Graphics > UI Detailing scales them).</summary>
        internal static readonly List<(Image Img, float Alpha)> Grits = new List<(Image, float)>();
        /// <summary>The other texture details (corner marks, edge lights, dither, scanlines, sheen, bloom…) with their base opacity.</summary>
        internal static readonly List<(Graphic G, float Alpha)> Details = new List<(Graphic, float)>();

        /// <summary>F12 > Graphics > UI Detailing as 0–1 (0 in Performance Mode: every texture layer off).</summary>
        internal static float DetailK => ProgressionPlugin.Low ? 0f : Mathf.Clamp01((ProgressionPlugin.Detailing?.Value ?? 100) / 100f);
        /// <summary>The scratches / smudges: 100% detailing = twice their original strength (the old Scratches default).</summary>
        internal static float GritK => 2f * DetailK;

        /// <summary>A scratch / smudge / grain layer: registered and shown at the current detailing (culled at 0).</summary>
        internal static Image AddGrit(Image img, float alpha, bool decor = true)
        {
            if (Grits.Count > 3000) Grits.RemoveAll(x => x.Img == null);
            if (decor) alpha *= ProgScreen.Polish.DecorK; // 0.9.9 Decor Noise (panel light has its own dial)
            Grits.Add((img, alpha));
            Fade(img, alpha * GritK);
            return img;
        }

        /// <summary>A texture detail (not information): registered and shown at the current detailing (culled at 0).</summary>
        internal static T Detail<T>(T g, float alpha, bool decor = true) where T : Graphic
        {
            if (Details.Count > 3000) Details.RemoveAll(x => x.G == null); // tiles come and go while browsing
            // 0.9.9 Decor Noise: the decorative layers at a % of 0.9.81 (micro labels have their own dial: decor false)
            float a = decor ? alpha * ProgScreen.Polish.DecorK : alpha;
            Details.Add((g, a));
            Fade(g, a * DetailK);
            return g;
        }

        internal static void Fade(Graphic g, float a)
        {
            if (g == null) return;
            g.canvasRenderer.cullTransparentMesh = true; // alpha 0 = not drawn at all (no overdraw)
            var c = g.color; c.a = Mathf.Clamp01(a); g.color = c;
        }

        /// <summary>Re-applies UI Detailing to every registered layer (and forgets destroyed ones).</summary>
        internal static void ApplyDetail()
        {
            Grits.RemoveAll(x => x.Img == null); Details.RemoveAll(x => x.G == null);
            float gk = GritK, dk = DetailK;
            foreach (var (img, a) in Grits) Fade(img, a * gk);
            foreach (var (g, a) in Details) Fade(g, a * dk);
        }

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

        private static readonly Sprite[] _ramp = new Sprite[2];
        /// <summary>0.9.97: a gentle ramp (30% at one end, full at the other), for fills and lines that should fade without
        /// disappearing. Default: full on the right; fadeRight: full on the left, fading out to the right. (Not flipped
        /// with a negative scale: that would mirror the image's children too.)</summary>
        public static Sprite RampFade(bool fadeRight = false)
        {
            int i = fadeRight ? 1 : 0;
            if (_ramp[i] != null) return _ramp[i];
            const int w = 128;
            var tex = new Texture2D(w, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f); if (fadeRight) u = 1 - u;
                var c = new Color(1, 1, 1, Mathf.Lerp(.3f, 1f, Mathf.SmoothStep(0, 1, u))); tex.SetPixel(x, 0, c); tex.SetPixel(x, 1, c);
            }
            tex.Apply();
            return _ramp[i] = Sprite.Create(tex, new Rect(0, 0, w, 2), new Vector2(.5f, .5f));
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
