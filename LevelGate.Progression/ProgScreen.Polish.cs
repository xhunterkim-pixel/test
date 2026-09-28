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
            public static float AmbientK => On ? Pct(ProgressionPlugin.TestAmbient, 40) : 1f;
            public static float FlourishK => On ? Pct(ProgressionPlugin.TestFlourish, 65) : 1f;
            public static float BorderFade => On ? Pct(ProgressionPlugin.TestBorderFade, 70) : 0f;
            public static float CardRest => On ? Pct(ProgressionPlugin.TestCardRest, 90) : 1f;
            public static float CardLocked => On ? Pct(ProgressionPlugin.TestCardLocked, 72) : .85f;
            public static float SmallText => On ? (ProgressionPlugin.TestSmallText?.Value ?? 10.5f) : 0f;
            public static bool NeutralPips => On && (ProgressionPlugin.TestNeutralPips?.Value ?? true);
            public static bool QuietDuringFx => On && (ProgressionPlugin.TestQuietFx?.Value ?? true);
            public static HeroLine Subtitle => On ? (ProgressionPlugin.TestHeroSubtitle?.Value ?? HeroLine.Description) : HeroLine.Off;
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

        // ---------------------------------------------------------------- MW4 frames: strong in the middle, fading at the ends

        /// <summary>Frames drawn as four fading edges: the frame graphic itself stays (hover hits it) but is invisible.</summary>
        private static readonly Dictionary<Graphic, Image[]> _edges = new Dictionary<Graphic, Image[]>();
        private static Sprite _fadeV, _fadeH;
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
        }

        /// <summary>Gives a frame graphic fading edges (thickness px) — only with Polish on and Border Fade above 0.</summary>
        private static void FadeEdges(Graphic frame, float thickness)
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
            var edges = new[]
            {
                Edge("EdgeL", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(thickness, 0), _fadeV),
                Edge("EdgeR", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-thickness, 0), Vector2.zero, _fadeV),
                Edge("EdgeT", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -thickness), Vector2.zero, _fadeH),
                Edge("EdgeB", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, thickness), _fadeH),
            };
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

        private static void ForgetEdges() { if (_edges.Count > 0) { var dead = new List<Graphic>(); foreach (var kv in _edges) if (kv.Key == null) dead.Add(kv.Key); foreach (var d in dead) _edges.Remove(d); } }

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
