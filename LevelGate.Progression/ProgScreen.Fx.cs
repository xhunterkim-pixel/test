using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// MW's small state animations, all short and cheap:
    /// - loading: three small squares blinking in turn where a picture is still being drawn (tiles, cards, the big picture);
    /// - hover: a burst of horizontal glitch streaks sliding across the tile / card (0.28 s);
    /// - selected: a single soft shine sweeping across it when it becomes the picked one (0.4 s);
    /// - the big picture's load-in: it appears from the top down, the part still coming shown as lines of light in the
    ///   rank's colour hanging from the edge (uneven, the longest ones breaking up), only on the item's own shape.
    /// Reduce Motion / Performance Mode: no glitch, shine or load-in (the loading squares stay: they say something).
    /// </summary>
    internal static partial class ProgScreen
    {
        // ---------------------------------------------------------------- loading squares

        private static readonly List<Image[]> _loaders = new List<Image[]>();
        private static float _loaderAt;
        private static int _loaderStep;

        /// <summary>Three small squares inside a picture's placeholder (they go when the placeholder does).</summary>
        private static void AddLoader(Component placeholder, float y, float dot = 4)
        {
            if (placeholder == null) return;
            var row = Ui.Rect(placeholder.transform, "Loading", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-dot * 2.5f, y - dot / 2), new Vector2(dot * 2.5f, y + dot / 2));
            var dots = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                float x = i * dot * 2;
                dots[i] = Ui.Img(Ui.Rect(row, "Dot", new Vector2(0, 0), new Vector2(0, 1), new Vector2(x, 0), new Vector2(x + dot, 0)), new Color(1, 1, 1, .15f));
                dots[i].raycastTarget = false;
            }
            _loaders.Add(dots);
        }

        private static void TickLoaders()
        {
            if (Time.unscaledTime < _loaderAt) return;
            _loaderAt = Time.unscaledTime + .14f; // ~7 steps a second: only the few that show redraw
            _loaderStep++;
            _loaders.RemoveAll(d => d[0] == null);
            foreach (var d in _loaders)
            {
                if (!d[0].gameObject.activeInHierarchy) continue;
                int on = _loaderStep % 4; // 0,1,2 lit in turn, then a beat of all dim
                for (int i = 0; i < 3; i++) d[i].color = new Color(.85f, .88f, .9f, i == on ? .75f : .15f);
            }
        }

        // ---------------------------------------------------------------- hover glitch, selection shine

        private sealed class Fx { public RectTransform Holder; public Image Glitch, Shine; }
        private static readonly Dictionary<RectTransform, Fx> _fx = new Dictionary<RectTransform, Fx>();

        private static Fx FxOf(RectTransform host)
        {
            if (_fx.TryGetValue(host, out var f) && f.Holder != null) return f;
            if (_fx.Count > 400) foreach (var k in _fx.Keys.Where(k => k == null || _fx[k].Holder == null).ToList()) _fx.Remove(k);
            f = new Fx();
            f.Holder = Ui.Fill(host, "Fx");
            f.Holder.gameObject.AddComponent<RectMask2D>(); // the streaks stay inside the tile
            f.Glitch = Ui.Img(Ui.Rect(f.Holder, "Glitch", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, 0), Ui.HStreaks());
            f.Shine = Ui.Img(Ui.Rect(f.Holder, "Shine", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-60, 0), new Vector2(60, 0)), new Color(1, 1, 1, 0), Ui.Radial());
            f.Glitch.raycastTarget = f.Shine.raycastTarget = false;
            f.Glitch.enabled = f.Shine.enabled = false;
            _fx[host] = f;
            return f;
        }

        /// <summary>F12 > CURRENTLY TESTING > Hover Glitch, as a multiple of 0.9.70's strength (default 50%).</summary>
        private static float GlitchK => (ProgressionPlugin.TestGlitch?.Value ?? 23) / 100f;

        /// <summary>Hover: glitch streaks slide across and fade (Motion: Fast × 1.8, linear, stepped flicker on the shared clock).</summary>
        private static void PlayGlitch(RectTransform host)
        {
            if (host == null || Motion.Still || Ui.DetailK <= 0 || GlitchK <= 0) return;
            var f = FxOf(host); var g = f.Glitch;
            g.enabled = true;
            Motion.Reset(g, "glitch");
            Motion.To(g, "glitch", 0, 1, Motion.D(Motion.Fast) * 1.8f, Motion.Ease.Linear, t =>
            {
                float fl = Motion.Flicker(.65f, g.GetInstanceID());
                g.color = new Color(1, 1, 1, .55f * GlitchK * (1 - t) * fl);
                g.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-18, 22, t), t < .5f ? 0 : 2);
            }, 0, () => { if (g != null) g.enabled = false; });
        }

        /// <summary>Picked: one soft shine sweeps across (Motion: Slow, OutCubic travel, pulse brightness).</summary>
        private static void PlayShine(RectTransform host)
        {
            float strength = (ProgressionPlugin.TestShine?.Value ?? 285) / 100f, width = (ProgressionPlugin.TestShineWidth?.Value ?? 157) / 100f;
            if (host == null || Motion.Still || Ui.DetailK <= 0 || strength <= 0) return;
            var f = FxOf(host); var sh = f.Shine;
            sh.enabled = true;
            sh.rectTransform.offsetMin = new Vector2(-60 * width, 0); sh.rectTransform.offsetMax = new Vector2(60 * width, 0); // F12 width
            Motion.Reset(sh, "shine");
            Motion.To(sh, "shine", 0, 1, Motion.D(Motion.Slow), Motion.Ease.Linear, t =>
            {
                var r = sh.rectTransform;
                float x = Mathf.Lerp(-.2f, 1.2f, Motion.Eval(Motion.Ease.OutCubic, t));
                r.anchorMin = new Vector2(x, 0); r.anchorMax = new Vector2(x, 1);
                sh.color = new Color(1, 1, 1, Mathf.Clamp01(.22f * strength * Motion.Eval(Motion.Ease.Pulse, t)));
            }, 0, () => { if (sh != null) sh.enabled = false; });
        }

        private static void TickFx() { } // (all on Motion now)

        // ---------------------------------------------------------------- selection border, section light-up

        /// <summary>
        /// A selection border. Picked: draws in from a little outside (Motion: Base, OutCubic); unpicked: lets go (Fast,
        /// InCubic); while shown its broken bottom crawls on the shared clock.
        /// </summary>
        private sealed class SelFrame
        {
            public Image Img;
            private bool _on;
            public bool On
            {
                set
                {
                    if (_on == value || Img == null) return;
                    _on = value;
                    var img = Img;
                    Motion.To(img, "sel", 0, value ? 1 : 0, Motion.D(value ? Motion.Base : Motion.Fast), value ? Motion.Ease.OutCubic : Motion.Ease.InCubic, a =>
                    {
                        bool show = a > .001f && Ui.DetailK > 0;
                        if (img.enabled != show) img.enabled = show;
                        img.color = new Color(1, 1, 1, .85f * a);
                        float sc = 1 + .035f * (1 - a); // draws in from a little outside
                        img.rectTransform.localScale = new Vector3(sc, sc, 1);
                    });
                }
                get => _on;
            }
        }
        private static readonly List<SelFrame> _selFrames = new List<SelFrame>();
        private static float _selSwapAt;
        private static int _selVariant;

        private static SelFrame MakeSelFrame(RectTransform around, float outset)
        {
            var img = Ui.Img(Ui.Rect(around, "SelFrame", Vector2.zero, Vector2.one, new Vector2(-outset, -outset), new Vector2(outset, outset)), new Color(1, 1, 1, 0), Ui.DashFrame(0));
            img.type = Image.Type.Tiled; img.raycastTarget = false; img.enabled = false;
            var f = new SelFrame { Img = img };
            _selFrames.Add(f);
            return f;
        }

        // the section holding the picked item lights up from the left in the theme colour (the viewed rank's)
        private static readonly Dictionary<string, (Image Head, Image Box)> _sectionLit = new Dictionary<string, (Image, Image)>();
        private static readonly Dictionary<string, float> _sectionA = new Dictionary<string, float>();
        private static string _litSection;

        private static void TickSelection()
        {
            // the broken bottoms crawl: one shared step for every border on screen
            if (!Motion.Still && Time.unscaledTime >= _selSwapAt)
            {
                _selSwapAt = Time.unscaledTime + Motion.Micro * 1.4f;
                _selVariant++;
                _selFrames.RemoveAll(f => f.Img == null);
                foreach (var f in _selFrames) if (f.Img.enabled) f.Img.sprite = Ui.DashFrame(_selVariant);
            }
            // the section light: moves when the pick moves to another section (Motion: Slow in, Base out)
            string lit = _featTpl != null ? ProgData.GroupOf(_featTpl) : null;
            if (lit == _litSection) return;
            _litSection = lit;
            var theme = Ui.Hex(TierOf(Mathf.Max(1, _level)).Light);
            foreach (var kv in _sectionLit.ToList())
            {
                if (kv.Value.Head == null) { _sectionLit.Remove(kv.Key); continue; }
                var (head, box) = kv.Value;
                bool on = kv.Key == lit;
                Motion.To(head, "lit", 0, on ? 1 : 0, Motion.D(on ? Motion.Slow : Motion.Base), on ? Motion.Ease.OutCubic : Motion.Ease.InCubic, e =>
                {
                    head.color = new Color(theme.r, theme.g, theme.b, .32f * e);
                    box.color = new Color(theme.r, theme.g, theme.b, .07f * e);
                });
            }
        }

        // ---------------------------------------------------------------- item bloom

        private static float _bloomAt;

        private static float BloomOpacity => Ui.DetailK * (ProgressionPlugin.TestBloomOpacity?.Value ?? 100) / 100f;
        private static float BloomSize => (ProgressionPlugin.TestBloomSize?.Value ?? 100) / 100f;

        /// <summary>
        /// Five times a second: the glow behind the big picture, the picked tile and the picked card takes the colours of
        /// the item it's behind (a red item blooms red). Only writes when something changed.
        /// </summary>
        private static void TickBloom()
        {
            if (Time.unscaledTime < _bloomAt) return;
            _bloomAt = Time.unscaledTime + .2f;
            float op = BloomOpacity, sz = BloomSize;
            if (_heroBloom != null)
            {
                var src = _featPic != null && _featPic.enabled ? _featPic.sprite : null;
                var c = src != null ? GameItems.BloomColor(src) : null;
                var col = c ?? new Color(1, 1, 1, 0);
                col.a = c.HasValue ? Mathf.Clamp01(.28f * op) : 0;
                if (_heroBloom.color != col) _heroBloom.color = col;
                var want = new Vector2(760, 440) * sz;
                if (_heroBloom.rectTransform.sizeDelta != want) _heroBloom.rectTransform.sizeDelta = want;
            }
            foreach (var v in _tileOrder)
            {
                if (v?.Bloom == null) continue;
                bool sel = v.Item.Tpl == _featTpl && v.Pic.enabled && op > 0;
                if (v.Bloom.enabled != sel) v.Bloom.enabled = sel;
                if (!sel) continue;
                var c = GameItems.BloomColor(v.Pic.sprite);
                if (!c.HasValue) continue;
                var col = c.Value; col.a = Mathf.Clamp01(.3f * op);
                if (v.Bloom.color != col) v.Bloom.color = col;
                var want = new Vector2(150, 110) * sz;
                if (v.Bloom.rectTransform.sizeDelta != want) v.Bloom.rectTransform.sizeDelta = want;
            }
            foreach (var card in _cards)
            {
                if (card == null || !card.Picked) continue;
                var sp = card.MainPicture;
                card.ItemBloom(sp != null ? GameItems.BloomColor(sp) : null, Mathf.Clamp01(.14f * op), sz);
            }
        }

        // ---------------------------------------------------------------- the big picture's load-in

        /// <summary>
        /// A newly picked item appears from the top down in 20 vertical strips, each starting at its own moment (a ragged
        /// front, not one line), the part still coming covered in light in its rank's colour — only on the item's own shape.
        /// The light (F12 Picture Load-In Style): Dot Columns / Dot Cloud / Lines, breathing (each dot pulses its size and
        /// brightness, drawn as a few frames and cycled). Time and Randomness from F12; Reduce Motion: none.
        /// </summary>
        private const int Strips = 48, DotFrames = 6; // 48: a dense ragged front (20 read as ~10 lines)
        private static RectTransform _revealHost;
        private static readonly Image[] _stripPic = new Image[Strips], _stripHot = new Image[Strips], _stripMask = new Image[Strips], _stripLight = new Image[Strips];
        private static readonly float[] _stripDelay = new float[Strips];
        private static float _revealAt = -10;
        private static bool _revealWanted;
        private static float _revealFrameAt;
        private static int _revealFrame;
        private static readonly System.Random _revealRng = new System.Random();
        private static readonly Dictionary<int, Sprite[]> _dotFrames = new Dictionary<int, Sprite[]>();
        private static float RevealTime => Mathf.Max(.1f, ProgressionPlugin.TestRevealTime?.Value ?? .2f) / Motion.Speed;
        private static float RevealRandom => Mathf.Clamp01((ProgressionPlugin.TestRevealRandom?.Value ?? 100) / 100f);
        private static int RevealStyle => (int)(ProgressionPlugin.TestRevealStyle?.Value ?? LoadInStyle.Lines);

        private static void BuildReveal()
        {
            if (_featPic == null) return;
            _revealHost = Ui.Fill(_featPic.rectTransform, "Reveal");
            for (int i = 0; i < Strips; i++)
            {
                // a strip of the picture's width: a full-size copy clipped to it, and the light on the item's shape
                var strip = Ui.Rect(_revealHost, "Strip", new Vector2(i / (float)Strips, 0), new Vector2((i + 1) / (float)Strips, 1), Vector2.zero, Vector2.zero);
                strip.gameObject.AddComponent<RectMask2D>();
                var full = new Vector2(-i, 0); var fullMax = new Vector2(Strips - i, 1); // the whole picture, in this strip's units
                _stripPic[i] = Ui.Img(Ui.Rect(strip, "Pic", full, fullMax, Vector2.zero, Vector2.zero), Color.white);
                _stripPic[i].preserveAspect = true;
                _stripPic[i].type = Image.Type.Filled; _stripPic[i].fillMethod = Image.FillMethod.Vertical; _stripPic[i].fillOrigin = (int)Image.OriginVertical.Top;
                // arriving bright: a white copy of the picture over it, fading as the strip settles
                _stripHot[i] = Ui.Img(Ui.Rect(strip, "Hot", full, fullMax, Vector2.zero, Vector2.zero), new Color(1, 1, 1, 0));
                _stripHot[i].preserveAspect = true;
                _stripHot[i].type = Image.Type.Filled; _stripHot[i].fillMethod = Image.FillMethod.Vertical; _stripHot[i].fillOrigin = (int)Image.OriginVertical.Top;
                var shape = Ui.Rect(strip, "Shape", full, fullMax, Vector2.zero, Vector2.zero);
                _stripMask[i] = Ui.Img(shape, Color.white);
                _stripMask[i].preserveAspect = true;
                shape.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                _stripLight[i] = Ui.Img(Ui.Rect(shape, "Light", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero), new Color(1, 1, 1, 0));
                _stripLight[i].type = Image.Type.Tiled;
                foreach (var g in new Graphic[] { _stripPic[i], _stripHot[i], _stripMask[i], _stripLight[i] }) g.raycastTarget = false;
            }
            _revealHost.gameObject.SetActive(false);
        }

        /// <summary>The picture just appeared for a newly picked item: play the load-in (once per pick).</summary>
        private static void PictureShown()
        {
            if (!_revealWanted) return;
            _revealWanted = false;
            if (Motion.Still || _revealHost == null || _featPic.sprite == null) { EndReveal(); return; }
            _revealAt = Motion.Now;
            float rk = RevealRandom;
            for (int i = 0; i < Strips; i++)
            {
                // ragged front: each strip waits a little (neighbours roughly agree, plus noise)
                float ridge = .5f + .5f * Mathf.Sin(i * .31f + (float)_revealRng.NextDouble() * 6.28f);
                _stripDelay[i] = rk * .45f * Mathf.Clamp01(.6f * ridge + .4f * (float)_revealRng.NextDouble());
            }
            _revealHost.gameObject.SetActive(true);
            _featPic.canvasRenderer.SetAlpha(0); // the strips draw it while it comes in
            SyncStrips();
        }

        private static void SyncStrips()
        {
            var sp = _featPic.sprite;
            for (int i = 0; i < Strips; i++)
            {
                if (_stripPic[i].sprite != sp) { _stripPic[i].sprite = sp; _stripMask[i].sprite = sp; _stripHot[i].sprite = sp; }
                if (!_stripMask[i].enabled) _stripMask[i].enabled = true; // (picture clearing can switch it off: never a flat band)
                if (!_stripPic[i].enabled) _stripPic[i].enabled = true;
            }
        }

        private static void EndReveal()
        {
            _revealAt = -10;
            if (_featPic != null) _featPic.canvasRenderer.SetAlpha(1);
            if (_revealHost != null) _revealHost.gameObject.SetActive(false);
        }

        private static void TickReveal()
        {
            if (_revealAt < 0 || _featPic == null) return;
            float dur = RevealTime;
            float t = (Motion.Now - _revealAt) / dur;
            if (t >= 1.45f || !_featPic.enabled || _featPic.sprite == null) { EndReveal(); return; }
            SyncStrips();
            // breathing: the light's frames cycle (each dot pulses its size and brightness)
            if (Motion.Now >= _revealFrameAt) { _revealFrameAt = Motion.Now + Motion.Micro * .9f; _revealFrame = (_revealFrame + 1) % DotFrames; }
            var frames = DotFramesOf(RevealStyle);
            var light = Ui.Hex(TierOf(Mathf.Max(1, _featLevel)).Light);
            for (int i = 0; i < Strips; i++)
            {
                // each strip: its own start, the same pace
                float u = Mathf.Clamp01(t - _stripDelay[i]); // t runs past 1 so the latest strips finish too
                float r = Motion.Eval(Motion.Ease.OutCubic, u);
                _stripPic[i].fillAmount = r;
                _stripHot[i].fillAmount = r;
                _stripHot[i].color = new Color(1, 1, 1, .5f * Mathf.Pow(1 - u, 1.4f)); // bright as it arrives, settling to the picture's own colours
                var lr = _stripLight[i].rectTransform;
                float front = 1 - r;
                lr.anchorMin = new Vector2(0, Mathf.Max(0, front - .55f)); lr.anchorMax = new Vector2(1, front);
                var fr = frames[(_revealFrame + i) % DotFrames]; // neighbouring strips out of step: it shimmers, not blinks
                if (_stripLight[i].sprite != fr) _stripLight[i].sprite = fr;
                _stripLight[i].color = new Color(light.r, light.g, light.b, .95f * Mathf.Clamp01((1 - u) / .3f));
            }
        }

        public enum LoadInStyle { DotColumns, DotCloud, Lines }

        /// <summary>The load-in light's breathing frames for a style (built once, 128x384, white, tiles sideways).</summary>
        private static Sprite[] DotFramesOf(int style)
        {
            if (_dotFrames.TryGetValue(style, out var have)) return have;
            const int w = 128, h = 384;
            var rnd = new System.Random(97 + style * 31);
            // the dots: position, size, brightness, breathing phase — shared by all frames
            var dots = new List<(int X, int Y, float S, float A, float P)>();
            if (style == (int)LoadInStyle.Lines) { }
            else if (style == (int)LoadInStyle.DotColumns)
            {
                for (int x = 1; x < w; x += 3)
                {
                    int len = (int)(h * Mathf.Pow((float)rnd.NextDouble(), 1.4f) * (.35f + .65f * (.5f + .5f * Mathf.Sin(x * .09f + 1))));
                    for (int d = 0; d < len; d += 3 + rnd.Next(2))
                    {
                        if (rnd.NextDouble() < .18 * d / (float)Mathf.Max(1, len)) continue; // longer columns break up toward their ends
                        dots.Add((x + (rnd.NextDouble() < .15 ? 1 : 0), h - 1 - d, 1 + (float)rnd.NextDouble() * .9f, (1 - d / (float)Mathf.Max(1, len)) * (.5f + .5f * (float)rnd.NextDouble()), (float)rnd.NextDouble() * 6.28f));
                    }
                }
            }
            else
            {
                for (int k = 0; k < 900; k++)
                {
                    float depth = Mathf.Pow((float)rnd.NextDouble(), 1.8f);     // most near the front, thinning out below
                    dots.Add((rnd.Next(w), h - 1 - (int)(depth * h), 1 + (float)rnd.NextDouble() * 1.4f, (1 - depth) * (.4f + .6f * (float)rnd.NextDouble()), (float)rnd.NextDouble() * 6.28f));
                }
            }
            var frames = new Sprite[DotFrames];
            for (int f = 0; f < DotFrames; f++)
            {
                if (style == (int)LoadInStyle.Lines) { frames[f] = Ui.RevealStreaks(f % 4); continue; }
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "LevelGate loadin" };
                var px = new Color32[w * h];
                float ph = f / (float)DotFrames * 6.28f;
                foreach (var d in dots)
                {
                    float breath = .5f + .5f * Mathf.Sin(ph + d.P);                 // this dot's pulse
                    float size = d.S * (.55f + .6f * breath);
                    float a = d.A * (.45f + .55f * breath);
                    int r = Mathf.CeilToInt(size);
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int X = d.X + dx, Y = d.Y + dy;
                            if (X < 0 || X >= w || Y < 0 || Y >= h) continue;
                            float cover = Mathf.Clamp01(size - Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) + .5f); // soft square dots
                            if (cover <= 0) continue;
                            byte v = (byte)(255 * Mathf.Clamp01(a * cover));
                            if (v > px[Y * w + X].a) px[Y * w + X] = new Color32(255, 255, 255, v);
                        }
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                frames[f] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            }
            _dotFrames[style] = frames;
            return frames;
        }
    }
}
