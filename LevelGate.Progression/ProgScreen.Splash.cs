using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The new-rank splash (MW4's level-up screen), played by the XP animation's rank beat, timed to the emblem sound:
    /// build-up (the screen darkens, light streaks and specks rise, the old emblem gathers) → the peak (a flash, the new
    /// emblem pops in with rays, the rank's colour floods the glow) → hold (a perspective grid floor, a NEW RANK band with
    /// scanlines, the rank name and level, log micro text) → fade out. Its own canvas over everything; built on first use.
    /// Reduce Motion / Performance Mode: a still version (no streaks, specks, flash or pop). A skip hides it at once.
    /// </summary>
    internal static partial class ProgScreen
    {
        private static RectTransform _splash, _splashRoot;
        private static Image _spVeil, _spGlow, _spFlash, _spEmblem, _spFloor, _spBand, _spBandScan, _spBandStripes;
        private static RectTransform _spFrame;
        private static CanvasGroup _spFrameGroup;
        private static Image _spWaves;
        private static Component _spSide;
        private static Component _spNew, _spName, _spLevel, _spLog;
        private const int SpStreaks = 72, SpSpecks = 48;
        private static readonly RectTransform[] _spStreak = new RectTransform[SpStreaks];
        private static readonly Image[] _spStreakImg = new Image[SpStreaks];
        private static readonly float[] _spStreakX = new float[SpStreaks], _spStreakV = new float[SpStreaks], _spStreakA = new float[SpStreaks];
        private static readonly RectTransform[] _spSpeck = new RectTransform[SpSpecks];
        private static readonly Image[] _spSpeckImg = new Image[SpSpecks];
        private static readonly Vector2[] _spSpeckP = new Vector2[SpSpecks];
        private static readonly float[] _spSpeckV = new float[SpSpecks], _spSpeckA = new float[SpSpecks];
        private static int _splashFrame = -10, _splashLevel;
        // the tactical HUD: sync bar, status lines, crosshair + coordinates, typed name, ticking data, ruler, scanlines
        private static CanvasGroup _spHud;
        private static RectTransform _spSyncFill;
        private static Component _spSyncPct, _spStatus, _spTgt;
        private static string _spLogBase = "", _spNameText = "";
        private static float _spLogAt;
        private static readonly System.Random _spRng = new System.Random();
        private static bool _splashPopped;

        private static void BuildSplash()
        {
            if (_splash != null || _splashRoot == null) return;
            var layer = Ui.Rect(_splashRoot, "RankSplash", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(layer);
            _splash = layer;
            _spVeil = Ui.Img(layer, new Color(0, 0, 0, 0));
            _spVeil.raycastTarget = false;
            _spGlow = Ui.Img(Ui.Box(layer, "Glow", new Vector2(.5f, .58f), Vector2.zero, new Vector2(1500, 1000)), new Color(1, 1, 1, 0), Ui.Radial());
            Glow(_spGlow);
            // MW 10: a dotted wave surface behind the emblem
            _spWaves = Ui.Img(Ui.Box(layer, "Waves", new Vector2(.5f, .42f), Vector2.zero, new Vector2(1500, 560)), new Color(1, 1, 1, 0), DotWaves());
            // the floor: a perspective grid fading into the distance
            _spFloor = Ui.Img(Ui.Rect(layer, "Floor", new Vector2(.08f, 0), new Vector2(.92f, .34f), Vector2.zero, Vector2.zero), new Color(1, 1, 1, 0), PerspectiveGrid());
            // light streaks (thin vertical lines rising) and specks
            var rnd = new System.Random(19);
            for (int i = 0; i < SpStreaks; i++)
            {
                float h = 200 + (float)rnd.NextDouble() * 600; // MW4's curtain: many thin lines of light, some very long
                var r = Ui.Rect(layer, "Streak", new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(i % 7 == 0 ? 2 : 1, h));
                r.pivot = new Vector2(.5f, 0);
                _spStreak[i] = r;
                // mostly around the emblem, a few further out
                _spStreakX[i] = .5f + (float)(rnd.NextDouble() * 2 - 1) * (i % 3 == 0 ? .45f : .2f);
                _spStreakV[i] = 30 + (float)rnd.NextDouble() * 90;
                _spStreakA[i] = .15f + (float)rnd.NextDouble() * .45f;
                _spStreakImg[i] = Ui.Img(r, new Color(1, 1, 1, 0), Ui.VerticalFade());
                r.anchoredPosition = new Vector2(0, (float)rnd.NextDouble() * -300);
            }
            for (int i = 0; i < SpSpecks; i++)
            {
                var r = Ui.Rect(layer, "Speck", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(i % 5 == 0 ? 3 : 2, i % 5 == 0 ? 3 : 2));
                _spSpeck[i] = r;
                _spSpeckP[i] = new Vector2(.5f + (float)(rnd.NextDouble() * 2 - 1) * .35f, (float)rnd.NextDouble());
                _spSpeckV[i] = .03f + (float)rnd.NextDouble() * .09f;
                _spSpeckA[i] = .3f + (float)rnd.NextDouble() * .6f;
                _spSpeckImg[i] = Ui.Img(r, new Color(1, 1, 1, 0));
            }
            // tactical framing (no rays): viewfinder brackets around the emblem that close in at the peak
            _spFrame = Ui.Box(layer, "Frame", new Vector2(.5f, .6f), Vector2.zero, new Vector2(340, 340));
            _spFrameGroup = _spFrame.gameObject.AddComponent<CanvasGroup>();
            Ui.Brackets(_spFrame, 0, 34, new Color(1, 1, 1, .9f));
            Ui.Label(Ui.Rect(_spFrame, "Tag", new Vector2(0, 1), new Vector2(1, 1), new Vector2(4, 4), new Vector2(0, 18)), "Text", "RANK_UP  //  PROMOTION", 9, new Color(1, 1, 1, .7f), TextAnchor.LowerLeft, false, 2);
            // the rank's name, big and vertical down the right side (MW4's neon glyphs)
            var side = Ui.Rect(layer, "Side", new Vector2(.86f, .55f), new Vector2(.86f, .55f), new Vector2(-300, -40), new Vector2(300, 40));
            side.localEulerAngles = new Vector3(0, 0, -90);
            _spSide = Ui.Label(side, "Text", "", 64, Color.white, TextAnchor.MiddleCenter, true, 10);
            _spEmblem = Ui.Img(Ui.Box(layer, "Emblem", new Vector2(.5f, .6f), Vector2.zero, new Vector2(300, 300)), new Color(1, 1, 1, 0));
            _spEmblem.preserveAspect = true;
            // the band: NEW RANK over a strip of scanlines, the rank's name big, the level under it
            var band = Ui.Rect(layer, "Band", new Vector2(0, .22f), new Vector2(1, .22f), new Vector2(0, -38), new Vector2(0, 38));
            _spBand = Ui.Img(band, new Color(1, 1, 1, 0), Ui.HorizontalFade());
            _spBandScan = Ui.Img(Ui.Fill(band, "Scan"), new Color(1, 1, 1, 0), Ui.Scanlines());
            Ui.Crisp(_spBandScan); // 1.0.17: no moiré bands
            _spBandScan.type = Image.Type.Tiled;
            _spBandStripes = Ui.Img(Ui.Fill(band, "Stripes"), new Color(1, 1, 1, 0), Ui.VStripes());
            _spBandStripes.type = Image.Type.Tiled;
            _spName = Ui.Label(Ui.Fill(band, "Name"), "Text", "", 44, Color.white, TextAnchor.MiddleCenter, true, 6);
            _spNew = Ui.Label(Ui.Rect(layer, "New", new Vector2(0, .22f), new Vector2(1, .22f), new Vector2(0, 40), new Vector2(0, 62)), "Text", "NEW RANK", 15, Color.white, TextAnchor.MiddleCenter, true, 8);
            _spLevel = Ui.Label(Ui.Rect(layer, "Level", new Vector2(0, .22f), new Vector2(1, .22f), new Vector2(0, -74), new Vector2(0, -44)), "Text", "", 16, Color.white, TextAnchor.MiddleCenter, false, 4);
            // log micro text, top-left (MW's LOG_ANALYSIS column)
            _spLog = Ui.Label(Ui.Rect(layer, "Log", new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -260), new Vector2(360, -40)), "Text", "", 9, Color.white, TextAnchor.UpperLeft, false, 1.5f);
            BuildSplashHud(layer);
            _spFlash = Ui.Img(Ui.Fill(layer, "Flash"), new Color(1, 1, 1, 0)); // its own object (0.9.69 put it on the veil's: one image per object — it crashed and stalled the animation)
            foreach (var g in layer.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            layer.gameObject.SetActive(false);
        }

        private static void BuildSplashHud(RectTransform layer)
        {
            var hud = Ui.Fill(layer, "Hud");
            _spHud = hud.gameObject.AddComponent<CanvasGroup>();
            var tint = new Color(.85f, .9f, .92f, 1);
            // faint scanlines over everything
            var scan = Ui.Img(Ui.Fill(hud, "Scan"), new Color(1, 1, 1, .05f), Ui.Scanlines()); scan.type = Image.Type.Tiled;
            Ui.Crisp(scan); // 1.0.17: no moiré bands
            // crosshair lines through the emblem out to the edges, a gap around it
            var c = new Vector2(.5f, .6f);
            foreach (var (aMin, aMax, oMin, oMax) in new[] {
                (new Vector2(0, c.y), new Vector2(c.x, c.y), new Vector2(0, 0), new Vector2(-230, 1)),
                (new Vector2(c.x, c.y), new Vector2(1, c.y), new Vector2(230, 0), new Vector2(0, 1)),
                (new Vector2(c.x, 0), new Vector2(c.x, c.y), new Vector2(0, 0), new Vector2(1, -230)),
                (new Vector2(c.x, c.y), new Vector2(c.x, 1), new Vector2(0, 230), new Vector2(1, 0)) })
                Ui.Img(Ui.Rect(hud, "Cross", aMin, aMax, oMin, oMax), new Color(1, 1, 1, .16f));
            _spTgt = Ui.Label(Ui.Rect(hud, "Tgt", c, c, new Vector2(186, 178), new Vector2(520, 194)), "Text", "", 9, tint, TextAnchor.MiddleLeft, false, 2);
            // top-right: RANK_SYNC bar filling to the peak, status lines under it
            var sync = Ui.Rect(hud, "Sync", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-340, -92), new Vector2(-40, -60));
            Ui.Label(Ui.Rect(sync, "Label", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -12), Vector2.zero), "Text", "RANK_SYNC", 9, tint, TextAnchor.MiddleLeft, true, 2);
            _spSyncPct = Ui.Label(Ui.Rect(sync, "Pct", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -12), Vector2.zero), "Text", "0%", 9, tint, TextAnchor.MiddleRight, false, 2);
            var track = Ui.Rect(sync, "Track", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2), new Vector2(0, 8));
            Ui.Img(track, new Color(1, 1, 1, .12f));
            _spSyncFill = Ui.Rect(track, "Fill", Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero);
            Ui.Img(_spSyncFill, new Color(1, 1, 1, .85f));
            for (int k = 1; k < 10; k++) { float x = k / 10f; Ui.Img(Ui.Rect(track, "Tick", new Vector2(x, 0), new Vector2(x, 1), new Vector2(-.5f, 0), new Vector2(.5f, 0)), new Color(0, 0, 0, .6f)); }
            _spStatus = Ui.Label(Ui.Rect(hud, "Status", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-340, -150), new Vector2(-40, -98)), "Text", "", 9, tint, TextAnchor.UpperLeft, false, 2);
            // a ruler under the band
            var ruler = Ui.Img(Ui.Rect(hud, "Ruler", new Vector2(.1f, .22f), new Vector2(.9f, .22f), new Vector2(0, -44), new Vector2(0, -35)), new Color(1, 1, 1, .35f), Ui.Ruler());
            ruler.type = Image.Type.Tiled;
        }

        /// <summary>One frame of the splash: t seconds into the rank beat (dur long), the peak at `peak`.</summary>
        private static void RankSplash(int level, float t, float dur, float peak)
        {
            // never lets a problem here hold up the animation (0.9.69: it stopped on the rank beat until clicked)
            try { DrawSplash(level, t, dur, peak); }
            catch (System.Exception e) { L.ErrorOnce("new-rank splash", e); if (_splash != null) _splash.gameObject.SetActive(false); }
        }

        private static void DrawSplash(int level, float t, float dur, float peak)
        {
            BuildSplash();
            if (_splash == null) return;
            if (!_splash.gameObject.activeSelf || _splashLevel != level)
            {
                _splash.gameObject.SetActive(true);
                _splash.SetAsLastSibling();
                _splashLevel = level; _splashPopped = false;
                var tier = TierOf(level);
                _spNameText = tier.Name.ToUpperInvariant();
                Ui.SetText(_spName, "");
                Ui.SetText(_spSide, tier.Name.ToUpperInvariant());
                Ui.SetText(_spLevel, $"LEVEL  {level}");
                _spLogBase = $"LOG_ANALYSIS\nRANK_ID  {System.Array.IndexOf(Tiers, tier) + 1:00}\nLV_FROM  {tier.From:000}\nLV_NOW   {level:000}\nSTATE    PROMOTED\n\nSYS_ONLINE // SYNC OK\n";
                Ui.SetText(_spLog, _spLogBase);
                Ui.SetText(_spStatus, $"PROMOTION  //  AUTHORIZED\nCLEARANCE  LV {level:000}\nRANK  {tier.Name.ToUpperInvariant()}");
                Emblems.Show(_spEmblem, level - 1); // the old one gathers first; the new one swaps in at the peak
            }
            _splashFrame = Time.frameCount;
            bool still = Calm;
            var light = Ui.Hex(TierOf(level).Light);
            var rim = Ui.Hex(TierOf(level).Rim);
            float fadeIn = Mathf.Clamp01(t / .35f), fadeOut = Mathf.Clamp01((dur - t) / .45f);
            float vis = fadeIn * fadeOut;
            float after = t - peak; // < 0: build-up
            if (!_splashPopped && (after >= 0 || still)) { _splashPopped = true; Emblems.Show(_spEmblem, level); }
            _spVeil.color = new Color(.02f, .03f, .03f, .82f * vis);
            // the glow: faint and warm in the build-up, the rank's colour after the peak
            float gk = after < 0 ? .06f + .06f * Mathf.Clamp01(t / Mathf.Max(.01f, peak)) : .2f * Mathf.Exp(-after * 1.2f) + .08f;
            var gc = Color.Lerp(new Color(1f, .75f, .45f), light, after < 0 ? 0 : 1); gc.a = gk * vis; _spGlow.color = gc;
            var fc = Color.Lerp(rim, light, .5f); fc.a = (after < 0 ? .25f * Mathf.Clamp01(t / .8f) : .5f) * vis; _spFloor.color = fc;
            if (_spWaves != null) { var wc = light; wc.a = Mw(10) ? .16f * vis : 0; _spWaves.color = wc; _spWaves.rectTransform.localScale = new Vector3(1, 1 + .1f * Motion.Wave(3f), 1); }
            // the emblem: gathers (shrinks a little, dims) → pops in (overshoot) → settles, slowly breathing
            float s;
            if (still) s = 1;
            else if (after < 0) s = 1 - .12f * Mathf.Clamp01(t / Mathf.Max(.01f, peak));
            else if (after < .45f) { float u = after / .45f; s = .7f + .45f * Mathf.Sin(u * Mathf.PI * .5f) + .1f * Mathf.Sin(u * Mathf.PI); }
            else s = 1.05f + .02f * Mathf.Sin(after * 2f);
            _spEmblem.rectTransform.localScale = new Vector3(s, s, 1);
            _spEmblem.color = new Color(1, 1, 1, (after < 0 ? .65f : 1f) * vis);
            // the brackets: wide and faint in the build-up, snapping in around the emblem at the peak
            float fs = still ? 1 : after < 0 ? 1.35f : Mathf.Lerp(1.35f, 1f, EaseOutCubic(after / .3f));
            _spFrame.localScale = new Vector3(fs, fs, 1);
            _spFrameGroup.alpha = (after < 0 ? .3f : 1f) * vis;
            Ui.SetColor(_spSide, new Color(light.r, light.g, light.b, (still ? .5f : .55f * Mathf.Clamp01(after / .5f)) * vis));
            // the flash at the peak
            _spFlash.color = new Color(1, .98f, .92f, still || after < 0 ? 0 : .55f * Mathf.Clamp01(1 - after / .35f));
            // the band and its text arrive after the peak (NEW RANK first, the name wipes in)
            float ta = still ? 1 : Mathf.Clamp01((after - .1f) / .35f);
            var bc = light; bc.a = .16f * ta * vis; _spBand.color = bc;
            _spBandScan.color = new Color(light.r, light.g, light.b, .12f * ta * vis);
            _spBandStripes.color = new Color(light.r, light.g, light.b, .1f * ta * vis);
            ((RectTransform)_spBand.transform).localScale = new Vector3(1, still ? 1 : Mathf.Lerp(.2f, 1, EaseOutCubic(ta)), 1);
            Ui.SetColor(_spNew, new Color(light.r, light.g, light.b, ta * vis));
            Ui.SetColor(_spName, new Color(1, 1, 1, ta * vis));
            Ui.SetColor(_spLevel, new Color(.85f, .88f, .9f, Mathf.Clamp01((after - .35f) / .3f) * vis * (still ? 0 : 1) + (still ? vis : 0)));
            Ui.SetColor(_spLog, new Color(light.r, light.g, light.b, .45f * vis));
            // the HUD: sync bar fills to the peak; the name types in after it; the data column ticks
            _spHud.alpha = vis;
            float sync = still ? 1 : Mathf.Clamp01(t / Mathf.Max(.01f, peak));
            _spSyncFill.anchorMax = new Vector2(sync, 1);
            Ui.SetText(_spSyncPct, $"{Mathf.RoundToInt(sync * 100)}%");
            Ui.SetText(_spTgt, after < 0 ? $"TGT  {.5f + Mathf.Sin(t * 9) * .02f:0.000} / {.6f + Mathf.Cos(t * 7) * .02f:0.000}  //  ACQUIRING" : "TGT  0.500 / 0.600  //  LOCKED");
            int n = still ? _spNameText.Length : Mathf.Clamp(Mathf.FloorToInt(_spNameText.Length * Mathf.Clamp01((after - .1f) / .5f)), 0, _spNameText.Length);
            Ui.SetText(_spName, _spNameText.Substring(0, n) + (n < _spNameText.Length && after >= 0 && (Time.unscaledTime * 8 % 2) < 1 ? "_" : ""));
            if (!still && Time.unscaledTime >= _spLogAt)
            {
                _spLogAt = Time.unscaledTime + .07f;
                var sb = new System.Text.StringBuilder(_spLogBase);
                for (int k = 0; k < 7; k++)
                    sb.Append($"\n0x{_spRng.Next(0x1000, 0xFFFF):X4}  {_spRng.Next(256):X2} {_spRng.Next(256):X2} {_spRng.Next(256):X2} {_spRng.Next(256):X2}");
                Ui.SetText(_spLog, sb.ToString());
            }
            // streaks rise and flicker; specks drift up (build-up and hold alike)
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            float sw = _splash.rect.width, sh = _splash.rect.height;
            for (int i = 0; i < SpStreaks; i++)
            {
                var r = _spStreak[i];
                if (!still) { var p = r.anchoredPosition; p.y += _spStreakV[i] * dt * (after < 0 ? 1 : 2.2f); if (p.y > sh) p.y = -r.sizeDelta.y; r.anchoredPosition = new Vector2(_spStreakX[i] * sw, p.y); }
                else r.anchoredPosition = new Vector2(_spStreakX[i] * sw, sh * .15f);
                float fl = still ? .6f : .6f + .4f * Mathf.Sin(Time.unscaledTime * (7 + i % 5) + i);
                var c = Color.Lerp(new Color(1f, .85f, .6f), light, after < 0 ? .2f : .8f);
                c.a = _spStreakA[i] * fl * vis * (after < 0 ? .5f : 1f) * (still ? .5f : 1f);
                _spStreakImg[i].color = c;
            }
            for (int i = 0; i < SpSpecks; i++)
            {
                if (!still)
                {
                    var p = _spSpeckP[i]; p.y += _spSpeckV[i] * dt * (after < 0 ? .6f : 1.4f); p.x += Mathf.Sin(Time.unscaledTime + i) * .002f * dt * 60;
                    if (p.y > 1) p.y = 0; _spSpeckP[i] = p;
                }
                _spSpeck[i].anchoredPosition = new Vector2(_spSpeckP[i].x * sw, _spSpeckP[i].y * sh);
                var c = light; c.a = still ? 0 : _spSpeckA[i] * vis * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 3 + i));
                _spSpeckImg[i].color = c;
            }
        }

        /// <summary>Hides the splash when the rank beat didn't draw it this frame (it ended, or was skipped).</summary>
        private static void TickSplash()
        {
            if (_splash == null || !_splash.gameObject.activeSelf) return;
            if (Time.frameCount - _splashFrame > 1) _splash.gameObject.SetActive(false);
        }

        private static float EaseOutCubic(float x) => 1 - Mathf.Pow(1 - Mathf.Clamp01(x), 3);

        private static Sprite _grid;

        /// <summary>A floor grid in perspective (lines to a vanishing point, rows closing up toward the horizon), white.</summary>
        private static Sprite PerspectiveGrid()
        {
            if (_grid != null) return _grid;
            const int w = 1024, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LevelGate grid" };
            var px = new float[w * h];
            void Plot(int x, int y, float a) { if (x >= 0 && x < w && y >= 0 && y < h) px[y * w + x] = Mathf.Max(px[y * w + x], a); }
            // lines from the bottom edge to the vanishing point above the top
            float vx = w / 2f, vy = h * 1.6f;
            for (int k = -16; k <= 16; k++)
            {
                float bx = vx + k * 72;
                for (int y = 0; y < h; y++)
                {
                    float u = y / vy;
                    float x = bx + (vx - bx) * u;
                    Plot(Mathf.RoundToInt(x), y, 1);
                }
            }
            // rows: evenly spaced on the floor, so closer together toward the horizon
            for (int i = 1; i < 40; i++)
            {
                float d = i * .35f;
                int y = Mathf.RoundToInt(h * (1 - 1 / (1 + d * .45f)) * 1.25f);
                if (y >= h) break;
                for (int x = 0; x < w; x++) Plot(x, y, 1);
            }
            var cols = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // fades toward the horizon and the sides
                    float fy = 1 - y / (float)h, fx = 1 - Mathf.Abs(x / (float)w * 2 - 1);
                    float a = px[y * w + x] * fy * fy * Mathf.Clamp01(fx * 1.6f);
                    cols[y * w + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(a)));
                }
            tex.SetPixels32(cols);
            tex.Apply(false, true);
            return _grid = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

    }
}
