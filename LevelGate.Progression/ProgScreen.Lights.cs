using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The lights that move a little (F12 > Look &amp; Graphics > Detail Animation): the red glow in the top-right drifts, the
    /// header's rank emblem glow breathes, the picked card's coloured bloom and dotted light drift, the dotted floor light
    /// under the big picture wanders, and the XP bar carries a tracer (sparks flowing into its bright leading edge, like
    /// MW's XP track). Every moving piece sits on its own canvas, so only it redraws. Detail Animation 0, Reduce Motion or
    /// Performance Mode: all of it stands still (the tracer is hidden); UI Detailing 0: none of it shows.
    /// </summary>
    internal static partial class ProgScreen
    {
        private static float _lightPhase;

        /// <summary>How much the lights move, 0–1 (0: still).</summary>
        private static float MotionK => Calm || Ui.DetailK <= 0 ? 0f : Mathf.Clamp01((ProgressionPlugin.DetailAnimation?.Value ?? 100) / 100f);

        private static void TickLights()
        {
            float k = MotionK;
            TickTracer(k);
            TickMotes(k);
            TickWave(k);
            TickWall();
            TickSplash();
            if (k <= 0) return;
            _lightPhase += Mathf.Min(Time.unscaledDeltaTime, .1f);
            float t = _lightPhase;
            // the red glow top-right: a slow wander (the background's light isn't painted on)
            if (_bloom.Count > 1 && _bloom[1] != null)
                _bloom[1].rectTransform.anchoredPosition = new Vector2(-90 * k * (.5f + .5f * Mathf.Sin(t * .11f)), 70 * k * Mathf.Sin(t * .07f + 1));
            if (_headBadge != null) _headBadge.Breathe(k * Mathf.Sin(t * 1.1f));
            if (_featLight != null)
                _featLight.rectTransform.anchoredPosition = new Vector2(70 * k * Mathf.Sin(t * .17f), 5 * k * Mathf.Sin(t * .29f));
            foreach (var c in _cards) c?.TickLight(t, k);
        }

        // ---------------------------------------------------------------- the light wall (MW4's level track)

        private static RectTransform _wallLayer;
        private static Image _wallCore, _wallGlow, _wallWash, _wallFloor;
        private static float _wallA;

        /// <summary>
        /// While the XP animation moves your place along the rail: a tall wall of light rides the front across the card row,
        /// everything already reached is washed in its light (strongest at the wall, fading back to the left), and a pool of
        /// dotted light sits where it meets the rail. On its own canvas, over the cards; fades out when the fill stops.
        /// </summary>
        private static void BuildLightWall(RectTransform bottom)
        {
            var layer = Ui.Rect(bottom, "LightWall", new Vector2(0, 0), new Vector2(1, 1), new Vector2(Margin - Gutter / 2, 50), new Vector2(-Margin + Gutter / 2, -S3));
            Ui.OwnCanvas(layer);
            layer.gameObject.AddComponent<RectMask2D>();
            _wallLayer = layer;
            _wallWash = Ui.Img(Ui.Rect(layer, "Wash", Vector2.zero, new Vector2(0, 1), Vector2.zero, Vector2.zero), new Color(1f, .93f, .8f, 0), Ui.HorizontalFade());
            _wallGlow = Ui.Img(Ui.Rect(layer, "Glow", Vector2.zero, new Vector2(0, 1), new Vector2(-45, -30), new Vector2(45, 30)), new Color(1f, .9f, .75f, 0), Ui.Radial());
            _wallCore = Ui.Img(Ui.Rect(layer, "Core", Vector2.zero, new Vector2(0, 1), new Vector2(-1.5f, 0), new Vector2(1.5f, 0)), new Color(1f, .97f, .92f, 0), Ui.VerticalFade());
            _wallCore.rectTransform.localScale = new Vector3(1, -1, 1); // brightest down at the rail, fading up
            _wallFloor = Ui.Img(Ui.Box(layer, "Floor", new Vector2(0, 0), new Vector2(0, 21), new Vector2(300, 90)), new Color(1f, .9f, .75f, 0), Ui.HalftoneGlow());
            foreach (var i in new[] { _wallWash, _wallGlow, _wallCore, _wallFloor }) i.raycastTarget = false;
            layer.gameObject.SetActive(false);
        }

        private static void TickWall()
        {
            if (_wallLayer == null) return;
            bool moving = !Calm && XpAnimating && Mathf.Abs(_railShown - _railTarget) > .001f;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            _wallA = Mathf.MoveTowards(_wallA, moving ? 1f : 0f, dt * (moving ? 5f : 1.6f));
            bool on = _wallA > .001f && _railShown > 0;
            if (_wallLayer.gameObject.activeSelf != on) _wallLayer.gameObject.SetActive(on);
            if (!on) return;
            float x = Mathf.Clamp01(_railShown);
            float flick = .9f + .1f * Mathf.Sin(Time.unscaledTime * 31f);
            _wallWash.rectTransform.anchorMax = new Vector2(x, 1);
            var at = new Vector2(x, 0);
            var g = _wallGlow.rectTransform; g.anchorMin = at; g.anchorMax = new Vector2(x, 1);
            var c = _wallCore.rectTransform; c.anchorMin = at; c.anchorMax = new Vector2(x, 1);
            var f = _wallFloor.rectTransform; f.anchorMin = f.anchorMax = at; f.anchoredPosition = new Vector2(0, 21);
            float a = _wallA;
            _wallWash.color = new Color(1f, .93f, .8f, .14f * a);
            _wallGlow.color = new Color(1f, .9f, .75f, .3f * a * flick);
            _wallCore.color = new Color(1f, .97f, .92f, .95f * a * flick);
            _wallFloor.color = new Color(1f, .9f, .75f, .35f * a);
            // the waveform's glowing part flares with it
            if (_waveHot != null) { var wc = _waveHot.color; wc.a = Mathf.Clamp01(.3f * Ui.DetailK + .45f * a * flick); _waveHot.color = wc; }
        }

        // ---------------------------------------------------------------- the rail's waveform

        /// <summary>The glowing part of the rail's waveform rides the fill's front (your place); the faint band drifts left.</summary>
        private static void TickWave(float k)
        {
            if (_waveHot == null) return;
            float x = _railShown;
            bool on = x > .002f && x < .998f;
            if (_waveHot.enabled != on) _waveHot.enabled = on;
            if (on)
            {
                var hr = _waveHot.rectTransform;
                if (Mathf.Abs(hr.anchorMin.x - x) > .0001f) { hr.anchorMin = hr.anchorMax = new Vector2(x, .5f); hr.anchoredPosition = Vector2.zero; }
            }
            if (k <= 0 || _waveBase == null) return;
            var br = _waveBase.rectTransform;
            br.anchoredPosition = new Vector2(-((_lightPhase * 14f * k) % 384f), 0);
        }

        // ---------------------------------------------------------------- pictures whose texture is gone

        private static float _sweepAt;

        /// <summary>
        /// Twice a second while open: an image whose picture was deleted underneath it (the game redraws its own icons —
        /// High redraws the stash's weapons after the screen closes — and a tile at stash size shows the game's icon itself)
        /// draws whatever the GPU puts in that memory next: another item, the emblem sheet, a white glow. Such pictures are
        /// taken off and the screen asks for them again.
        /// </summary>
        private static void SweepPictures()
        {
            if (Time.unscaledTime < _sweepAt || _canvas == null) return;
            _sweepAt = Time.unscaledTime + .5f;
            int n = 0;
            foreach (var img in _canvas.GetComponentsInChildren<Image>(true))
            {
                var sp = img.sprite;
                if (ReferenceEquals(sp, null)) continue;
                if (sp != null && sp.texture != null) continue;
                img.sprite = null; img.enabled = false; n++;
            }
            if (n == 0) return;
            L.Info($"pictures: {n} had lost their texture (the game redrew them) — asked for again");
            ForgetPictures();
            Refresh();
        }

        // ---------------------------------------------------------------- specks of light over the background

        private const int Motes = 26;
        private static RectTransform _motes;
        private static readonly RectTransform[] _mote = new RectTransform[Motes];
        private static readonly Image[] _moteImg = new Image[Motes];
        private static readonly Vector2[] _moteP = new Vector2[Motes], _moteV = new Vector2[Motes];
        private static readonly float[] _moteA = new float[Motes], _moteF = new float[Motes];

        /// <summary>Behind the panels, over the pattern: a few specks (1–3 px, warm) that drift up slowly and twinkle.</summary>
        private static void BuildMotes(RectTransform root)
        {
            _motes = Ui.Fill(root, "Motes");
            Ui.OwnCanvas(_motes);
            var rng = new System.Random(11);
            for (int i = 0; i < Motes; i++)
            {
                float size = i % 7 == 0 ? 3 : i % 3 == 0 ? 2 : 1.5f;
                var r = Ui.Rect(_motes, "Mote", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(size, size));
                _mote[i] = r;
                // most near the red glow's side (the light source), some anywhere
                _moteP[i] = new Vector2((float)(i % 3 == 0 ? rng.NextDouble() : .45 + rng.NextDouble() * .55), (float)rng.NextDouble());
                _moteV[i] = new Vector2((float)(rng.NextDouble() * 2 - 1) * .004f, .006f + (float)rng.NextDouble() * .012f);
                _moteA[i] = .12f + (float)rng.NextDouble() * .3f;
                _moteF[i] = (float)rng.NextDouble() * 6.28f;
                _moteImg[i] = Ui.Img(r, new Color(1, .82f, .62f, 0));
                _moteImg[i].raycastTarget = false;
            }
        }

        private static void TickMotes(float k)
        {
            if (_motes == null) return;
            bool on = k > 0;
            if (_motes.gameObject.activeSelf != on) _motes.gameObject.SetActive(on);
            if (!on) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f) * k;
            var size = _motes.rect.size;
            float d = Ui.DetailK;
            for (int i = 0; i < Motes; i++)
            {
                var p = _moteP[i] + _moteV[i] * dt;
                if (p.y > 1.02f) { p.y = -.02f; } // back in at the bottom
                if (p.x < -.02f) p.x = 1.02f; else if (p.x > 1.02f) p.x = -.02f;
                _moteP[i] = p;
                _mote[i].anchoredPosition = new Vector2(p.x * size.x, p.y * size.y);
                // twinkle, and fade out toward the top and bottom edges
                float edge = Mathf.Clamp01(Mathf.Min(p.y, 1 - p.y) * 8);
                float a = _moteA[i] * d * edge * (.55f + .45f * Mathf.Sin(_lightPhase * 1.7f + _moteF[i]));
                var c = _moteImg[i].color; c.a = Mathf.Clamp01(a); _moteImg[i].color = c;
            }
        }

        // ---------------------------------------------------------------- the XP bar's tracer

        private const int Sparks = 18;
        private static RectTransform _tracer;
        private static Image _flare, _flareCore;
        private static readonly RectTransform[] _spark = new RectTransform[Sparks];
        private static readonly Image[] _sparkImg = new Image[Sparks];
        private static readonly float[] _sparkX = new float[Sparks], _sparkY = new float[Sparks], _sparkV = new float[Sparks], _sparkA = new float[Sparks];
        private static readonly System.Random _sparkRng = new System.Random(7);
        private const float TrailLen = 230;

        /// <summary>Built on first use, on the bar's fill (so it follows the fill as it grows), clipped to the filled part.</summary>
        private static bool BuildTracer()
        {
            if (_tracer != null) return true;
            if (_xpFill == null) return false;
            // the clip: the filled part, a little taller than the bar (sparks scatter around the line) and past its edge
            var clip = Ui.Rect(_xpFill, "Tracer", Vector2.zero, Vector2.one, new Vector2(0, -9), new Vector2(14, 9));
            Ui.OwnCanvas(clip);
            clip.gameObject.AddComponent<RectMask2D>();
            _tracer = clip;
            for (int i = 0; i < Sparks; i++)
            {
                var r = Ui.Rect(clip, "Spark", new Vector2(1, .5f), new Vector2(1, .5f), Vector2.zero, Vector2.zero);
                bool dash = i % 5 == 0; // a few short dashes among the dots (MW's track has little arrows in it)
                r.sizeDelta = dash ? new Vector2(4, 1) : new Vector2(i % 3 == 0 ? 2 : 1, i % 3 == 0 ? 2 : 1);
                _spark[i] = r;
                _sparkImg[i] = Ui.Img(r, new Color(1, .95f, .88f, 0));
                _sparkImg[i].raycastTarget = false;
                Respawn(i, true);
            }
            // the leading edge: a thin bright vertical line with a soft glow on it
            _flare = Ui.Img(Ui.Box(clip, "Flare", new Vector2(1, .5f), new Vector2(-14, 0), new Vector2(34, 30)), new Color(1, .93f, .85f, 0), Ui.Radial());
            _flareCore = Ui.Img(Ui.Box(clip, "FlareLine", new Vector2(1, .5f), new Vector2(-14, 0), new Vector2(2, 18)), new Color(1, .97f, .92f, 0));
            _flare.raycastTarget = _flareCore.raycastTarget = false;
            return true;
        }

        private static void Respawn(int i, bool anywhere)
        {
            double r() => _sparkRng.NextDouble();
            _sparkX[i] = -(float)(anywhere ? r() * TrailLen : TrailLen * (.6f + .4f * r())); // from the tail toward the edge
            // mostly close to the line, a few further out
            float spread = r() < .7 ? 3.5f : 7.5f;
            _sparkY[i] = (float)(r() * 2 - 1) * spread;
            _sparkV[i] = 40 + (float)r() * 70;
            _sparkA[i] = .35f + (float)r() * .55f;
        }

        private static void TickTracer(float k)
        {
            bool on = k > 0 && _xpFill != null && _xpFill.anchorMax.x > .01f;
            if (!on) { if (_tracer != null && _tracer.gameObject.activeSelf) _tracer.gameObject.SetActive(false); return; }
            if (!BuildTracer()) return;
            if (!_tracer.gameObject.activeSelf) _tracer.gameObject.SetActive(true);
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            float d = Ui.DetailK;
            for (int i = 0; i < Sparks; i++)
            {
                _sparkX[i] += _sparkV[i] * dt * (.4f + .6f * k);
                if (_sparkX[i] > -14) Respawn(i, false);
                // brightest near the edge, fading out along the tail; each flickers a little
                float along = 1 - Mathf.Clamp01(-_sparkX[i] / TrailLen);
                float a = _sparkA[i] * along * along * d * (.75f + .25f * Mathf.Sin((_lightPhase + i) * 9f));
                _spark[i].anchoredPosition = new Vector2(_sparkX[i], _sparkY[i]);
                var c = _sparkImg[i].color; c.a = Mathf.Clamp01(a); _sparkImg[i].color = c;
            }
            float pulse = .8f + .2f * Mathf.Sin(_lightPhase * 5.3f);
            _flare.color = new Color(1, .93f, .85f, .35f * d * pulse);
            _flareCore.color = new Color(1, .97f, .92f, .85f * d * pulse);
        }
    }
}
