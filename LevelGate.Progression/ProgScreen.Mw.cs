using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The ten MW4 features being tried (F12 > CURRENTLY TESTING > MW 1 … MW 10, each on its own switch, every change
    /// logged). All timing goes through <see cref="Motion"/>.
    ///  1 additive glow · 2 card flood · 3 level numbers glow · 4 XP counter on the light wall · 5 reactive waveform ·
    ///  6 screen flashes · 7 title glitch · 8 row pips (built with the tiles) · 9 locked hologram · 10 wave surfaces
    /// </summary>
    internal static partial class ProgScreen
    {
        private static bool Mw(int n) => ProgressionPlugin.Mw(n);

        /// <summary>A switch changed in F12.</summary>
        public static void MwChanged(int n)
        {
            if (n == 1) ApplyAdditive();
            if ((n == 8 || n == 9) && IsOpen) Refresh(); // tiles carry these: rebuild the list
            if (n == 9 && _cards != null) foreach (var c in _cards) c?.Mark(_level, Me);
        }

        // ---------------------------------------------------------------- 1 additive glow

        private static readonly List<Image> _glows = new List<Image>();
        private static Material _addMat;
        private static bool _addTried;

        /// <summary>
        /// An additive material from a shader the game already has (particles' additive ones take vertex colour and a texture,
        /// like UI images). Found once; the log lists what was found. Null: none — the glows stay as they are.
        /// </summary>
        private static Material AddMat()
        {
            if (_addTried) return _addMat;
            _addTried = true;
            string[] names = { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive", "Legacy Shaders/Particles/Additive (Soft)", "Particles/Additive (Soft)" };
            Shader sh = null;
            foreach (var n in names) { sh = Shader.Find(n); if (sh != null) break; }
            if (sh == null)
            {
                var all = Resources.FindObjectsOfTypeAll<Shader>().Where(x => x != null && x.name.IndexOf("Additive", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                L.Info("MW01 additive glow: loaded additive shaders: " + (all.Count == 0 ? "none" : string.Join(", ", all.Select(x => x.name).Take(20).ToArray())));
                sh = all.FirstOrDefault(x => x.name.IndexOf("Particle", System.StringComparison.OrdinalIgnoreCase) >= 0) ?? all.FirstOrDefault();
            }
            if (sh == null) { L.Info("MW01 additive glow: no additive shader in the game — the glows stay see-through"); return null; }
            _addMat = new Material(sh) { name = "LevelGate additive" };
            if (_addMat.HasProperty("_TintColor")) _addMat.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f)); // particle shaders double it
            L.Info($"MW01 additive glow: using the game's '{sh.name}'");
            return _addMat;
        }

        /// <summary>
        /// Registers a glow: it becomes additive while MW 1 is on. Only for glows that are never inside a clipped area
        /// (a scroll list, a RectMask2D / Mask): the game's additive particle shader ignores UI clipping, so a clipped
        /// glow would draw outside its panel.
        /// </summary>
        private static void Glow(Image img)
        {
            if (img == null) return;
            _glows.Add(img);
            if (Mw(1)) { var m = AddMat(); if (m != null) img.material = m; }
        }

        private static void ApplyAdditive()
        {
            _glows.RemoveAll(g => g == null);
            var m = Mw(1) ? AddMat() : null;
            foreach (var g in _glows) g.material = m; // null: back to the default UI material
        }

        // ---------------------------------------------------------------- 2 card flood

        /// <summary>Levels unlocked during this visit's level-up animation: their cards stay lit until the screen closes.</summary>
        private static readonly HashSet<int> _flooded = new HashSet<int>();

        // ---------------------------------------------------------------- 4 + 5 the light wall's counter and bars

        private static Component _wallXp;
        private static RectTransform _wallXpRt;
        private static Image _wallXpBack; // 0.9.95: a soft dark backing, so it reads over card names
        private const int Bars = 56;
        private static readonly RectTransform[] _bar = new RectTransform[Bars];
        private static readonly Image[] _barImg = new Image[Bars];
        private static string _gainShown = "";

        private static void BuildWallExtras(RectTransform layer)
        {
            // 5: bars around the wall, mirrored about the rail, jumping like audio as it passes
            for (int i = 0; i < Bars; i++)
            {
                var r = Ui.Rect(layer, "Bar", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(i % 4 == 0 ? 2 : 1, 4));
                r.pivot = new Vector2(.5f, .5f);
                _bar[i] = r;
                _barImg[i] = Ui.Img(r, new Color(1f, .93f, .82f, 0));
                _barImg[i].raycastTarget = false;
            }
            // 4: the running +XP, riding just right of the wall above the rail, with a crosshair tick
            _wallXpRt = Ui.Rect(layer, "Xp", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(240, 30));
            _wallXpRt.pivot = new Vector2(0, .5f);
            _wallXpBack = Ui.Img(Ui.Box(_wallXpRt, "Back", new Vector2(0, .5f), new Vector2(92, 0), new Vector2(240, 64)), new Color(0, 0, 0, 0), Ui.Radial());
            _wallXpBack.raycastTarget = false;
            Ui.Img(Ui.Rect(_wallXpRt, "TickH", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(-14, 0), new Vector2(-2, 1)), new Color(1, 1, 1, .9f)).raycastTarget = false;
            Ui.Img(Ui.Rect(_wallXpRt, "TickV", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(-8.5f, -6), new Vector2(-7.5f, 6)), new Color(1, 1, 1, .9f)).raycastTarget = false;
            _wallXp = Ui.Label(Ui.Fill(_wallXpRt, "Text"), "Text", "", 24, Color.white, TextAnchor.MiddleLeft, true, 1);
            _wallXpRt.gameObject.AddComponent<CanvasGroup>().alpha = 0;
        }

        /// <summary>Each frame from TickWall: wall at x (0–1 of the layer), a = its strength, w/h the layer's size.</summary>
        private static void TickWallExtras(float x, float a, float w, float h, bool moving)
        {
            const float railY = 24;
            float wx = x * w;
            // 5: bars
            bool bars = Mw(5) && Ui.DetailK > 0;
            float idle = bars && !moving && !Calm && x > .002f && x < .998f ? .25f : 0;
            float strength = bars ? Mathf.Max(a, idle) : 0;
            for (int i = 0; i < Bars; i++)
            {
                if (strength <= .001f) { if (_barImg[i].enabled) _barImg[i].enabled = false; continue; }
                if (!_barImg[i].enabled) _barImg[i].enabled = true;
                float dx = -220 + i * 5.2f;                         // mostly behind the wall (where it has been)
                float prox = Mathf.Exp(-Mathf.Pow(dx / 110f, 2));
                float t = Motion.Now;
                float n = Mathf.Abs(Mathf.Sin(t * 9.1f + i * 1.73f) * Mathf.Sin(t * 5.3f + i * .61f));
                float hgt = 3 + 54 * n * prox * strength;
                _bar[i].sizeDelta = new Vector2(_bar[i].sizeDelta.x, hgt);
                _bar[i].anchoredPosition = new Vector2(wx + dx, railY);
                _barImg[i].color = new Color(1f, .93f, .82f, Mathf.Clamp01((.25f + .6f * prox) * strength));
            }
            // 4: +XP on the wall
            if (_wallXpRt != null)
            {
                var cg = _wallXpRt.GetComponent<CanvasGroup>();
                float xa = Mw(4) ? Mathf.Clamp01(a) : 0;
                cg.alpha = xa;
                if (xa > .001f)
                {
                    _wallXpRt.anchoredPosition = new Vector2(Mathf.Min(wx + 18, w - 240), railY + Polish.SweepXpHeight); // 0.9.94: 44
                    if (_wallXpBack != null) _wallXpBack.color = new Color(0, 0, 0, .75f * Polish.SweepXpBacking);
                    Ui.SetText(_wallXp, _gainShown);
                }
            }
        }

        // ---------------------------------------------------------------- 6 screen flashes

        private static Image _screenFlash;

        private static void BuildScreenFlash(RectTransform root)
        {
            var layer = Ui.Fill(root, "ScreenFlash");
            SubCanvas(layer);
            _screenFlash = Ui.Img(layer, new Color(1, 1, 1, 0));
            _screenFlash.raycastTarget = false;
            layer.GetComponent<GraphicRaycaster>().enabled = false;
        }

        /// <summary>A quick full-screen wash: straight to `peak`, then fading out over `dur`.</summary>
        private static void ScreenFlash(Color c, float peak, float dur)
        {
            if (!Mw(6) || Motion.Still || _screenFlash == null) return;
            _screenFlash.transform.parent.SetAsLastSibling();
            Motion.Reset(_screenFlash, "flash");
            Motion.To(_screenFlash, "flash", peak, 0, dur, Motion.Ease.OutCubic, v => _screenFlash.color = new Color(c.r, c.g, c.b, v));
        }

        // ---------------------------------------------------------------- 7 title glitch

        private static readonly Dictionary<Component, Component[]> _ghosts = new Dictionary<Component, Component[]>();
        private static readonly System.Random _glitchRng = new System.Random();

        private static Component[] GhostsOf(Component title)
        {
            if (_ghosts.TryGetValue(title, out var g) && g[0] != null) return g;
            g = new Component[2];
            for (int i = 0; i < 2; i++)
            {
                var rt = Ui.Fill(title.transform, i == 0 ? "GhostR" : "GhostC");
                var le = rt.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;
                float size = Refl.Get(title, "fontSize") is float f ? f : 20;
                g[i] = Ui.AddText(rt.gameObject, "", size, Color.white, TextAnchor.MiddleLeft);
                foreach (var prop in new[] { "alignment", "fontStyle", "characterSpacing", "enableWordWrapping", "overflowMode", "font" })
                    try { Refl.Set(g[i], prop, Refl.Get(title, prop)); } catch { }
                g[i].gameObject.SetActive(false);
            }
            _ghosts[title] = g;
            return g;
        }

        /// <summary>The title just changed: for a moment it smears sideways in red and cyan copies, then settles.</summary>
        private static void TitleGlitch(Component title)
        {
            if (!Mw(7) || Motion.Still || title == null || !title.gameObject.activeInHierarchy || _keepPickTpl != null || Polish.FlourishK < .05f) return;
            float fk = Mathf.Min(1f, Polish.FlourishK);
            var g = GhostsOf(title);
            string text = Refl.Get(title, "text") as string ?? "";
            foreach (var x in g) { Ui.SetText(x, text); x.gameObject.SetActive(true); }
            Motion.Reset(title, "glitch");
            Motion.To(title, "glitch", 1, 0, Motion.D(Motion.Base) * 1.4f, Motion.Ease.OutCubic, v =>
            {
                for (int i = 0; i < 2; i++)
                {
                    float jitter = (float)(_glitchRng.NextDouble() * 2 - 1);
                    // half of 0.9.74's (asked for after testing): smaller offsets, fainter copies
                    ((RectTransform)g[i].transform).anchoredPosition = new Vector2((i == 0 ? -1 : 1) * (2 + 5 * jitter) * v * fk, (_glitchRng.NextDouble() < .3 ? 1 : 0) * v * fk);
                    Ui.SetColor(g[i], i == 0 ? new Color(1f, .25f, .3f, .275f * v * fk) : new Color(.3f, .9f, 1f, .275f * v * fk));
                }
            }, 0, () => { foreach (var x in g) if (x != null) x.gameObject.SetActive(false); });
        }

        // ---------------------------------------------------------------- 10 wave surfaces

        private static RectTransform _wavesL, _wavesR;
        private static Sprite _dotWaves;

        /// <summary>Dotted wave lines receding in perspective, stronger to the right (mirror it for the left corner). White.</summary>
        private static Sprite DotWaves()
        {
            if (_dotWaves != null) return _dotWaves;
            const int w = 512, h = 220;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LevelGate dotwaves" };
            var px = new Color32[w * h];
            const int lines = 18;
            for (int j = 0; j < lines; j++)
            {
                float depth = j / (float)(lines - 1);                       // 0 near (bottom) … 1 far (top)
                float baseY = h * (.06f + .8f * Mathf.Pow(depth, .8f));
                float step = Mathf.Lerp(3, 6, depth);                        // dots closer together up close
                for (float x = 0; x < w; x += step)
                {
                    float u = x / w;
                    float amp = (8 + 46 * Mathf.Pow(u, 1.6f)) * (1 - depth * .6f);
                    float y = baseY + amp * Mathf.Sin(x * .018f + j * .42f) * Mathf.Sin(x * .006f + j * .2f + 1);
                    float a = (1 - depth * .75f) * Mathf.Pow(u, .7f);
                    int xi = (int)x, yi = Mathf.RoundToInt(y);
                    int size = depth < .4f ? 2 : 1;
                    for (int dy = 0; dy < size; dy++) for (int dx = 0; dx < size; dx++)
                    {
                        int X = xi + dx, Y = yi + dy;
                        if (X < 0 || X >= w || Y < 0 || Y >= h) continue;
                        px[Y * w + X] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(a)));
                    }
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _dotWaves = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
        }

        private static void BuildWaves(RectTransform root)
        {
            var holder = Ui.Fill(root, "Waves");
            Ui.OwnCanvas(holder);
            _wavesR = Ui.Rect(holder, "R", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-980, 0), new Vector2(0, 420));
            _wavesL = Ui.Rect(holder, "L", new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(980, 420));
            _wavesL.localScale = new Vector3(-1, 1, 1);
            foreach (var r in new[] { _wavesR, _wavesL }) { var i = Ui.Img(r, new Color(1, 1, 1, .09f), DotWaves()); i.raycastTarget = false; }
            _wavesL.pivot = _wavesR.pivot = new Vector2(.5f, 0);
        }

        private static void TickWaves()
        {
            if (_wavesR == null) return;
            bool on = Mw(10) && Ui.DetailK > 0;
            var holder = _wavesR.parent.gameObject;
            if (holder.activeSelf != on) holder.SetActive(on);
            if (!on || Motion.Still) return;
            // a slow swell, both corners on the shared clock (the right one half a beat later)
            float sR = 1 + .07f * Motion.Wave(9f), sL = 1 + .07f * Motion.Wave(9f, .5f);
            _wavesR.localScale = new Vector3(1, sR, 1);
            _wavesL.localScale = new Vector3(-1, sL, 1);
            _wavesR.anchoredPosition = new Vector2(-490 + 14 * Motion.Wave(23f), 0);
            _wavesL.anchoredPosition = new Vector2(490 + 14 * Motion.Wave(23f, .3f), 0);
        }
    }
}
