using System.Collections.Generic;
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

        private sealed class Fx { public RectTransform Holder; public Image Glitch, Shine; public float GlitchAt = -10, ShineAt = -10; }
        private static readonly Dictionary<RectTransform, Fx> _fx = new Dictionary<RectTransform, Fx>();
        private static readonly List<RectTransform> _fxDead = new List<RectTransform>();

        private static Fx FxOf(RectTransform host)
        {
            if (_fx.TryGetValue(host, out var f) && f.Holder != null) return f;
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
        private static float GlitchK => (ProgressionPlugin.TestGlitch?.Value ?? 50) / 100f;

        private static void PlayGlitch(RectTransform host)
        {
            if (host == null || Calm || Ui.DetailK <= 0 || GlitchK <= 0) return;
            var f = FxOf(host); f.GlitchAt = Time.unscaledTime; f.Glitch.enabled = true;
        }

        private static void PlayShine(RectTransform host)
        {
            if (host == null || Calm || Ui.DetailK <= 0) return;
            var f = FxOf(host); f.ShineAt = Time.unscaledTime; f.Shine.enabled = true;
        }

        private static void TickFx()
        {
            if (_fx.Count == 0) return;
            float now = Time.unscaledTime;
            foreach (var kv in _fx)
            {
                var f = kv.Value;
                if (kv.Key == null || f.Holder == null) { _fxDead.Add(kv.Key); continue; }
                if (f.Glitch.enabled)
                {
                    float t = (now - f.GlitchAt) / .28f;
                    if (t >= 1) f.Glitch.enabled = false;
                    else
                    {
                        // slides across, flickering, and fades
                        float fl = (Mathf.Sin(now * 70f) > -.2f ? 1f : .35f);
                        f.Glitch.color = new Color(1, 1, 1, .55f * GlitchK * (1 - t) * fl);
                        f.Glitch.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-18, 22, t), (t < .5f ? 0 : 2));
                    }
                }
                if (f.Shine.enabled)
                {
                    float t = (now - f.ShineAt) / .4f;
                    if (t >= 1) f.Shine.enabled = false;
                    else
                    {
                        var r = f.Shine.rectTransform;
                        float x = Mathf.Lerp(-.2f, 1.2f, EaseOutCubic(t));
                        r.anchorMin = new Vector2(x, 0); r.anchorMax = new Vector2(x, 1);
                        f.Shine.color = new Color(1, 1, 1, .22f * Mathf.Sin(t * Mathf.PI));
                    }
                }
            }
            foreach (var d in _fxDead) _fx.Remove(d);
            _fxDead.Clear();
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

        private static Image _revealMask, _revealLines;
        private static float _revealAt = -10;
        private static bool _revealWanted;
        private static int _revealVariant;
        private static float _revealNextSwap;
        private static readonly System.Random _revealRng = new System.Random();
        private static float RevealTime => Mathf.Max(.1f, ProgressionPlugin.TestRevealTime?.Value ?? .6f);   // F12 > CURRENTLY TESTING
        private static float RevealRandom => Mathf.Clamp01((ProgressionPlugin.TestRevealRandom?.Value ?? 60) / 100f);

        private static void BuildReveal()
        {
            if (_featPic == null) return;
            // a copy of the picture as a mask (same sprite, same fit): the lines show only on the item's own shape
            var host = Ui.Fill(_featPic.rectTransform, "Reveal");
            _revealMask = Ui.Img(host, Color.white);
            _revealMask.preserveAspect = true; _revealMask.raycastTarget = false;
            host.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _revealLines = Ui.Img(Ui.Rect(host, "Lines", new Vector2(0, 0), new Vector2(1, 1), Vector2.zero, Vector2.zero), new Color(1, 1, 1, 0), Ui.RevealStreaks());
            _revealLines.raycastTarget = false;
            host.gameObject.SetActive(false);
        }

        /// <summary>The picture just appeared for a newly picked item: play the load-in (once per pick).</summary>
        private static void PictureShown()
        {
            if (!_revealWanted) return;
            _revealWanted = false;
            if (Calm || _revealMask == null) { EndReveal(); return; }
            _revealAt = Time.unscaledTime;
            _revealMask.transform.gameObject.SetActive(true);
            // the mask is a copy of the picture: always the current sprite, always on (0.9.70: clearing pictures on a
            // Performance Mode switch emptied it, and an empty mask let the lines cover the whole box as a flat band)
            _revealMask.sprite = _featPic.sprite; _revealMask.enabled = true;
            _revealVariant = _revealRng.Next(4); _revealNextSwap = 0;
            SetRevealLines();
            _featPic.type = Image.Type.Filled;
            _featPic.fillMethod = Image.FillMethod.Vertical;
            _featPic.fillOrigin = (int)Image.OriginVertical.Top;
            _featPic.fillAmount = 0;
        }

        private static void EndReveal()
        {
            _revealAt = -10;
            if (_featPic != null) { _featPic.fillAmount = 1; _featPic.type = Image.Type.Simple; }
            if (_revealMask != null) _revealMask.transform.gameObject.SetActive(false);
        }

        private static void SetRevealLines()
        {
            _revealLines.sprite = Ui.RevealStreaks(_revealVariant);
            float rk = RevealRandom;
            var lr = _revealLines.rectTransform;
            float shift = (float)(_revealRng.NextDouble() * 2 - 1) * 40 * rk;
            lr.offsetMin = new Vector2(-40 * rk + shift, lr.offsetMin.y); lr.offsetMax = new Vector2(40 * rk + shift, lr.offsetMax.y);
            lr.localScale = new Vector3(rk > 0 && _revealRng.NextDouble() < .5 ? -1 : 1, 1, 1);
        }

        private static void TickReveal()
        {
            if (_revealAt < 0 || _featPic == null) return;
            float t = (Time.unscaledTime - _revealAt) / RevealTime;
            if (t >= 1 || !_featPic.enabled) { EndReveal(); return; }
            float r = EaseOutCubic(t);
            _featPic.fillAmount = r;
            if (_revealMask.sprite != _featPic.sprite) _revealMask.sprite = _featPic.sprite; // the sharper render may arrive mid-way
            if (!_revealMask.enabled) _revealMask.enabled = true;
            if (_revealMask.sprite == null) { EndReveal(); return; } // no shape to draw the lines on: never a flat band
            // randomness: the lines swap pattern, shift and flip while it plays (0 = one fixed pattern)
            float rk = RevealRandom;
            if (rk > 0 && Time.unscaledTime >= _revealNextSwap)
            {
                _revealNextSwap = Time.unscaledTime + Mathf.Lerp(.25f, .03f, rk);
                _revealVariant = _revealRng.Next(4);
                SetRevealLines();
            }
            // the lines hang from the front down over the part still coming, in the item's rank colour
            var lr = _revealLines.rectTransform;
            float front = 1 - r;
            lr.anchorMin = new Vector2(0, Mathf.Max(0, front - .6f)); lr.anchorMax = new Vector2(1, front);
            var light = Ui.Hex(TierOf(Mathf.Max(1, _featLevel)).Light);
            _revealLines.color = new Color(light.r, light.g, light.b, .9f * Mathf.Clamp01((1 - t) / .25f));
        }
    }
}
