using System;
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

        public static void SetWrap(Component c, bool wrap)
        {
            if (c is Text t) t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            else Refl.Set(c, "enableWordWrapping", wrap);
        }

        // ---------------------------------------------------------------- generated pictures

        private static Sprite _diamond, _white, _vgrad, _hgrad, _radial, _dots;

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

        /// <summary>A filled diamond (square turned 45°) — the menu button icon.</summary>
        public static Sprite DiamondSprite()
        {
            if (_diamond != null) return _diamond;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Abs(x - n / 2f + .5f) + Mathf.Abs(y - n / 2f + .5f);
                    float outer = Mathf.Clamp01(n / 2f - 2 - d), ring = Mathf.Clamp01(d - (n / 2f - 12)) * outer;
                    float inner = Mathf.Clamp01(n / 2f - 18 - d);
                    float a = Mathf.Max(ring, inner);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            _diamond = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
            return _diamond;
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
}
