using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// 0.9.9's polish pass — every change is a dial in F12 > CURRENTLY TESTING, all behind one switch (Polish 0.9.9 on / off =
    /// exactly 0.9.81), so before / after can be compared live and the picked values become the defaults next patch:
    ///   · Decor Noise: the decorative layers (scanlines, rulers, corner marks, scratches, dither, inner glows) in % of 0.9.81
    ///   · Micro Labels: the tiny system labels (ID - …, STATS —, LEVEL_ACTIVE) in % of 0.9.81
    ///   · Ambient Motion: every idle mover (drifting lights, breathing emblems, background pattern) in % of 0.9.81,
    ///     and Quiet During Effects: they rest while a level up / XP fill / picture load-in plays (one thing moves at a time)
    ///   · Flourish: the one-off show effects (select shine, hover glitch, title glitch) in % of their F12 values
    ///   · Border Fade: frames drawn MW4-style — full strength in the middle of each side, fading out toward the ends
    ///   · Card Rest / Card Locked: how far the bottom cards that are neither yours nor viewed step back
    ///   · Small Text: information text smaller than this is raised to it (decorative micro text isn't)
    ///   · Neutral Pips: the hero's rank pips in the screen's state colours instead of loot colours (red means unmet)
    ///   · Hero Subtitle: a line under the big name (MW4: the description in grey italic)
    ///   · Tooltip Delay: the full name shows only after resting on a tile
    ///   · Name Glow (0.9.91): MW4's soft light halo behind the big name
    ///   · Ambient Light / Light Colours: the picked card's soft lights stronger, with a pink / purple accent (Mixed)
    /// </summary>
    internal static partial class ProgScreen
    {
        internal static class Polish
        {
            public static bool On => ProgressionPlugin.TestPolish?.Value ?? true;
            private static float Pct(BepInEx.Configuration.ConfigEntry<int> e, int fallback) => (e?.Value ?? fallback) / 100f;

            public static float DecorK => On ? Pct(ProgressionPlugin.TestDecorNoise, 55) : 1f;
            public static float MicroK => On ? Pct(ProgressionPlugin.TestMicroLabels, 60) : 1f;
            public static float AmbientK => On ? Pct(ProgressionPlugin.TestAmbient, 50) : 1f; // 0.9.91: your pick (was 40)
            public static float FlourishK => On ? Pct(ProgressionPlugin.TestFlourish, 65) : 1f;
            public static float BorderFade => On ? Pct(ProgressionPlugin.TestBorderFade, 70) : 0f;
            // 0.9.92: back to 0.9.81's panels and cards (you preferred them): new keys so the saved 90 / 72 don't stick
            public static float CardRest => On ? Pct(ProgressionPlugin.TestCardOpacity, 100) : 1f;
            public static float CardLocked => On ? Pct(ProgressionPlugin.TestCardLockedOpacity, 100) : .85f;
            /// <summary>The panels' and cards' soft light layers (inner glow, lit edge, glass, sheen, gloss); 100 = 0.9.81.</summary>
            public static float PanelLight => On ? Pct(ProgressionPlugin.TestPanelLight, 100) : 1f;
            public static float SmallText => On ? (ProgressionPlugin.TestSmallText?.Value ?? 10.5f) : 0f;
            public static bool NeutralPips => On && (ProgressionPlugin.TestNeutralPips?.Value ?? true);
            public static bool QuietDuringFx => On && (ProgressionPlugin.TestQuietFx?.Value ?? true);
            // 0.9.91: off — the glow behind the name was wanted, not a line (fixed: a saved 0.9.9 value doesn't bring it back)
            public static HeroLine Subtitle => HeroLine.Off;
            // 0.9.92: the name's glow became a light reflection falling down (MW4 "HAN 86"); new keys, so the 0.9.91 values don't carry over
            public static float NameGlowK => On ? Pct(ProgressionPlugin.TestNameShadow, 29) : 0f;
            public static float NameGlowSoft => Pct(ProgressionPlugin.TestNameShadowSoftness, 26);
            public static float NameGlowDrop => Pct(ProgressionPlugin.TestNameShadowDistance, 47);
            public static NewTagLook NewTag => ProgressionPlugin.TestNewTag?.Value ?? NewTagLook.MW4; // 0.9.96: your pick
            // 0.9.95 (read live): the sweep's +XP
            public static float SweepXpBacking => On ? Pct(ProgressionPlugin.TestWallXpBacking, 100) : 0f;
            public static float SweepXpHeight => On ? (ProgressionPlugin.TestWallXpLift?.Value ?? 44) : 44f;
            // 0.9.97: left / right fades (built with the screen)
            public static bool FadeCardLine => On && (ProgressionPlugin.TestFadeCardLine?.Value ?? true);
            public static bool FadeSectionHead => On && (ProgressionPlugin.TestFadeSectionHead?.Value ?? true);
            public static bool FadeMeters => On && (ProgressionPlugin.TestFadeMeters?.Value ?? true);
            public static bool FadeXpBar => On && (ProgressionPlugin.TestFadeXpBar?.Value ?? true);
            public static float XpStartDelay => Mathf.Clamp(ProgressionPlugin.TestXpStartDelay?.Value ?? .6f, 0f, 2f); // 1.0.4
            public static bool DirectionalBorders => On && (ProgressionPlugin.TestDirectionalBorders?.Value ?? true); // 1.0.9
            public static bool CardAccentLight => !On || (ProgressionPlugin.TestCardAccentLight?.Value ?? false);   // 1.0.9
            public static bool BigXpDuringSweep => !On || (ProgressionPlugin.TestBigXpDuringSweep?.Value ?? true);
            public static float SubtitleAlpha => Pct(ProgressionPlugin.TestHeroSubtitleOpacity, 70);
            public static float AmbientLightK => On ? Pct(ProgressionPlugin.TestAmbientLight, 140) : 1f;
            public static bool MixedLight => On && (ProgressionPlugin.TestLightHue?.Value ?? LightHue.Mixed) == LightHue.Mixed;

            // ---- quiet during effects: the ambient movers ease out while a big moment plays, and back in after
            private static float _quiet = 1f;
            public static float Quiet(bool busy, float dt)
            {
                float target = busy && QuietDuringFx ? 0f : 1f;
                _quiet = Mathf.MoveTowards(_quiet, target, dt * (target > _quiet ? .8f : 4f)); // out fast, back in gently
                return _quiet;
            }
        }

        public enum HeroLine { Description, FullName, Off }
        public enum LightHue { Mixed, Rank }
        public enum NewTagLook { Mw4Dark, MW4, Old }

        // ---------------------------------------------------------------- MW4 frames: strong in the middle, fading at the ends

        /// <summary>Frames drawn as four fading edges: the frame graphic itself stays (hover hits it) but is invisible.</summary>
        private static readonly Dictionary<Graphic, Image[]> _edges = new Dictionary<Graphic, Image[]>();
        private static Sprite _fadeV, _fadeH, _fadeEnd, _rampUpV, _rampDownV, _rampRightH, _rampLeftH, _rampLow;
        private static float _fadeBuiltFor = -1;

        private static void BuildFadeSprites()
        {
            float fade = Polish.BorderFade;
            if (_fadeV != null && Mathf.Abs(_fadeBuiltFor - fade) < .001f) return;
            _fadeBuiltFor = fade;
            const int n = 64;
            float Curve(int i)
            {
                // 30% of the length at each end fades; the ends keep (1 - fade) of the strength
                float u = (i + .5f) / n, d = Mathf.Min(u, 1 - u) / .3f;
                float s = Mathf.Clamp01(d); s = s * s * (3 - 2 * s);
                return Mathf.Lerp(1 - fade, 1f, s);
            }
            var tv = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var th = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++) { var c = new Color(1, 1, 1, Curve(i)); tv.SetPixel(0, i, c); th.SetPixel(i, 0, c); }
            tv.Apply(); th.Apply();
            _fadeV = Sprite.Create(tv, new Rect(0, 0, 1, n), new Vector2(.5f, .5f));
            _fadeH = Sprite.Create(th, new Rect(0, 0, n, 1), new Vector2(.5f, .5f));
            // 1.0.9: the cut corners' diagonals: the strength the edges have at their ends, so the line runs on unbroken
            var te = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            te.SetPixel(0, 0, new Color(1, 1, 1, Curve(0))); te.Apply();
            _fadeEnd = Sprite.Create(te, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f));
            // 1.0.9, MW4's cards: each edge a one-way ramp toward the two square corners (top: brighter right, right: brighter
            // up, bottom: brighter left, left: brighter down); the cut corners are the faint ends, at a third of the strength
            float lo = Mathf.Lerp(1f, .3f, Mathf.Clamp01(fade * 1.4f));
            float Ramp(int i) { float u = (i + .5f) / n; return Mathf.Lerp(lo, 1f, Mathf.Pow(u, 1.25f)); }
            var up = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var down = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var right = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var left = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++)
            {
                up.SetPixel(0, i, new Color(1, 1, 1, Ramp(i)));
                down.SetPixel(0, i, new Color(1, 1, 1, Ramp(n - 1 - i)));
                right.SetPixel(i, 0, new Color(1, 1, 1, Ramp(i)));
                left.SetPixel(i, 0, new Color(1, 1, 1, Ramp(n - 1 - i)));
            }
            up.Apply(); down.Apply(); right.Apply(); left.Apply();
            _rampUpV = Sprite.Create(up, new Rect(0, 0, 1, n), new Vector2(.5f, .5f));
            _rampDownV = Sprite.Create(down, new Rect(0, 0, 1, n), new Vector2(.5f, .5f));
            _rampRightH = Sprite.Create(right, new Rect(0, 0, n, 1), new Vector2(.5f, .5f));
            _rampLeftH = Sprite.Create(left, new Rect(0, 0, n, 1), new Vector2(.5f, .5f));
            Sprite Solid(float a) { var t = new Texture2D(1, 1, TextureFormat.RGBA32, false); t.SetPixel(0, 0, new Color(1, 1, 1, a)); t.Apply(); return Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f)); }
            _rampLow = Solid(lo); // both cuts: the faint ends meet there
        }

        /// <summary>Gives a frame graphic fading edges (thickness px) — only with Polish on and Border Fade above 0.</summary>
        private static void FadeEdges(Graphic frame, float thickness, float cut = 0, bool directional = false)
        {
            if (frame == null || Polish.BorderFade <= .001f) return;
            BuildFadeSprites();
            var rt = frame.rectTransform;
            Image Edge(string name, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, Sprite sp)
            {
                var img = Ui.Img(Ui.Rect(rt, name, aMin, aMax, oMin, oMax), frame.color, sp);
                img.type = Image.Type.Simple; img.preserveAspect = false; img.raycastTarget = false;
                return img;
            }
            // cut: frames with the cards' / tiles' cut corners (top-left, bottom-right) — the edges stop where the cut starts and
            // a diagonal of the same thickness joins them (1.0.9; the four straight edges squared the corners off)
            var list = new List<Image>
            {
                Edge("EdgeL", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(thickness, -cut), directional ? _rampDownV : _fadeV),
                Edge("EdgeR", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-thickness, cut), Vector2.zero, directional ? _rampUpV : _fadeV),
                Edge("EdgeT", new Vector2(0, 1), new Vector2(1, 1), new Vector2(cut, -thickness), Vector2.zero, directional ? _rampRightH : _fadeH),
                Edge("EdgeB", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(-cut, thickness), directional ? _rampLeftH : _fadeH),
            };
            if (cut > 0)
            {
                float len = cut * 1.41421f + thickness * .8f, inset = cut / 2 + thickness * .3536f; // centred half a line in from the cut
                foreach (var (anchor, pos, sp) in new[] { (new Vector2(0, 1), new Vector2(inset, -inset), directional ? _rampLow : _fadeEnd), (new Vector2(1, 0), new Vector2(-inset, inset), directional ? _rampLow : _fadeEnd) })
                {
                    var d = Ui.Box(rt, "EdgeCut", anchor, pos, new Vector2(len, thickness));
                    d.localEulerAngles = new Vector3(0, 0, 45);
                    var img = Ui.Img(d, frame.color, sp);
                    img.type = Image.Type.Simple; img.raycastTarget = false;
                    list.Add(img);
                }
            }
            var edges = list.ToArray();
            _edges[frame] = edges;
            frame.canvasRenderer.cullTransparentMesh = false; // still drawn (invisibly): hover hit-testing keeps working on it
            PaintFrame(frame, frame.color);
        }

        /// <summary>The colour a frame is meant to have (its edges carry it when it has them).</summary>
        private static Color FrameColor(Graphic g) => _edges.TryGetValue(g, out var e) && e[0] != null ? e[0].color : g.color;

        /// <summary>Sets a graphic's colour — a frame with fading edges passes it on to them and stays see-through itself.</summary>
        private static void PaintFrame(Graphic g, Color c)
        {
            if (g == null) return;
            if (_edges.TryGetValue(g, out var e) && e[0] != null)
            {
                foreach (var img in e) if (img != null) img.color = c;
                g.color = new Color(c.r, c.g, c.b, .004f); // not 0: an invisible but live frame (pointer hover)
                return;
            }
            g.color = c;
        }

        // ---------------------------------------------------------------- MW4's soft glow behind the hero's name (0.9.91)

        /// <summary>
        /// MW4's "HAN 86": each letter casts a soft light-grey reflection that falls DOWN (a touch to the right) — not a halo
        /// all round. Drawn by the text's own underlay pass on an instance of its material (no extra graphics), offset
        /// downwards. Shaders without an underlay get a faint soft strip under the name instead.
        /// </summary>
        private static void NameGlow(Component label)
        {
            float k = Polish.NameGlowK;
            if (label == null || k <= 0) return;
            float drop = Mathf.Clamp01(Polish.NameGlowDrop);
            try
            {
                var mat = Refl.Get(label, "fontMaterial") as Material; // the label's own copy: other text keeps its look
                if (mat != null && mat.HasProperty("_UnderlayColor"))
                {
                    mat.EnableKeyword("UNDERLAY_ON");
                    mat.SetColor("_UnderlayColor", new Color(.86f, .85f, .80f, Mathf.Clamp01(.6f * k)));
                    if (mat.HasProperty("_UnderlaySoftness")) mat.SetFloat("_UnderlaySoftness", Mathf.Clamp01(Polish.NameGlowSoft));
                    if (mat.HasProperty("_UnderlayDilate")) mat.SetFloat("_UnderlayDilate", .05f);
                    if (mat.HasProperty("_UnderlayOffsetX")) mat.SetFloat("_UnderlayOffsetX", .18f * drop);
                    if (mat.HasProperty("_UnderlayOffsetY")) mat.SetFloat("_UnderlayOffsetY", -drop);
                    L.Info($"name reflection: text underlay on ({mat.shader?.name}), {k * 100:0}%, softness {Polish.NameGlowSoft * 100:0}%, drop {drop * 100:0}%");
                    return;
                }
                L.Info($"name reflection: the text shader ({mat?.shader?.name ?? "none"}) has no underlay — a soft strip under the name instead");
            }
            catch (System.Exception e) { L.ErrorOnce("name glow", e); }
            var rt = ((Component)label).transform as RectTransform;
            if (rt == null) return;
            var glow = Ui.Img(Ui.Rect(rt, "Glow", new Vector2(0, 0), new Vector2(.5f, .6f), new Vector2(-6, -6 * drop - 4), new Vector2(20, -2)), new Color(.86f, .85f, .80f, .07f * k), Ui.Radial());
            glow.raycastTarget = false;
            glow.transform.SetAsFirstSibling();
        }

        // ---------------------------------------------------------------- the line under the hero's name

        private static Component _heroSub;

        private static void BuildHeroSubtitle(RectTransform face)
        {
            _heroSub = null;
            if (Polish.Subtitle == HeroLine.Off) return;
            _heroSub = Ui.Label(Ui.Rect(face, "Subtitle", new Vector2(0, 1), new Vector2(.55f, 1), new Vector2(24, -112), new Vector2(0, -72)),
                "Text", "", TBody, Ui.Hex("#b9bfc2", Polish.SubtitleAlpha), TextAnchor.UpperLeft, false, 0, true);
            Ui.SetWrap(_heroSub, true);
            try { Ui.SetEnumPublic(_heroSub, "fontStyle", "Italic"); } catch { }
        }

        private static void HeroSubtitleFor(ProgItem it)
        {
            if (_heroSub == null) return;
            if (it == null) { Ui.SetText(_heroSub, ""); return; }
            string text = Polish.Subtitle == HeroLine.FullName ? (it.Name != it.Short ? it.Name : "") : FirstSentences(ProgData.DescriptionOf(it.Tpl), 150);
            Ui.SetText(_heroSub, text ?? "");
        }

        /// <summary>The description's first sentence(s) up to max characters (MW4 keeps it to 2–3 short lines).</summary>
        private static string FirstSentences(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= max) return text;
            int cut = -1;
            for (int i = 0; i < text.Length && i <= max; i++) if (text[i] == '.' && (i + 1 == text.Length || text[i + 1] == ' ')) cut = i + 1;
            if (cut > 40) return text.Substring(0, cut);
            int sp = text.LastIndexOf(' ', max);
            return text.Substring(0, sp > 40 ? sp : max).TrimEnd(',', ';', ' ') + "…";
        }
    }
}
