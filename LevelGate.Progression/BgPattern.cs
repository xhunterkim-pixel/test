using System;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// Drives the line patterns of F12 > Graphics > Pattern (Streaks / Contours / Topo) and their slow motion
    /// (Graphics > PatternMotion). The pattern is worked out on a worker thread (PatternCore) and only the finished
    /// picture is handed to the screen, about 10 times a second. It only runs from ProgScreen.Tick while the screen
    /// is open: closed (menu, hideout, raid) it does nothing at all, and entering a raid lets its memory go.
    /// </summary>
    internal static class BgPattern
    {
        private const int W = 1280, H = 720;

        private static Image _img;
        private static string _want;           // the pattern asked for (null = the dots, nothing to do here)
        private static PatternCore _core;      // built field of _want (main thread's copy)
        // the last few worked-out patterns (~7 MB each), so Random / switching back shows one again at once
        private static readonly System.Collections.Generic.List<PatternCore> _kept = new System.Collections.Generic.List<PatternCore>();
        private const int Keep = 3;
        private static Texture2D _tex;
        private static Sprite _sprite;
        private static string _shown;          // the pattern the texture holds now
        private static byte[] _front = new byte[0], _back = new byte[0];
        private static float _t, _nextAt;

        // the one job in flight
        private static volatile bool _busy, _done;
        private static PatternCore _jobCore;
        private static string _jobError;
        private static long _buildMs = -1;
        private static int _gen, _jobGen;

        /// <summary>Show this pattern on the image (from ApplyLook). Null: back to the dots, the lines are let go.</summary>
        public static void Use(Image img, string kind)
        {
            _img = img;
            if (kind == null) { Forget(); return; }
            if (kind != _want) { _want = kind; _core = null; _gen++; _t = 0; _nextAt = 0; }
            Attach();
        }

        private static void Attach()
        {
            if (_img == null) return;
            bool ready = _tex != null && _shown == _want;
            if (ready && !_img.enabled)
            {
                // fades in over 0.3 s when it's ready (it popped in)
                _img.canvasRenderer.SetAlpha(0);
                _img.CrossFadeAlpha(1, .3f, true);
            }
            if (ready)
            {
                if (_img.sprite != _sprite) _img.sprite = _sprite;
                _img.type = Image.Type.Simple; _img.preserveAspect = false;
            }
            _img.enabled = ready; // nothing (rather than a blank quad) until the first picture is in
        }

        /// <summary>Every frame while the screen is open.</summary>
        public static void Tick()
        {
            if (_want == null) return;
            float motion = ProgressionPlugin.PatternMotion?.Value ?? 1f;
            _t += Mathf.Min(Time.unscaledDeltaTime, .1f) * motion * Speed(_want);
            if (_busy) return;
            if (_done)
            {
                _done = false;
                if (_jobError != null) { L.Info("background pattern: " + _jobError); _jobError = null; _want = null; return; }
                if (_buildMs >= 0) { L.Debug($"background pattern '{_want}' worked out in {_buildMs} ms (worker thread)"); _buildMs = -1; }
                if (_jobGen == _gen)
                {
                    _core = _jobCore;
                    _kept.Remove(_core); _kept.Insert(0, _core);
                    while (_kept.Count > Keep) _kept.RemoveAt(_kept.Count - 1);
                    Upload();
                }
            }
            if (_core == null)
            {
                // worked out before (kept): straight to drawing it; else build first (usually during the loading screen)
                var kept = _kept.Find(c => c.Kind == _want);
                if (kept != null) L.Debug($"background pattern '{_want}': kept from before");
                Start(kept, _t);
                return;
            }
            if (motion <= 0 || Time.unscaledTime < _nextAt || ProgScreen.Dragging) return; // holds still while you drag the cards
            _nextAt = Time.unscaledTime + (ProgressionPlugin.Low ? .2f : .1f) / Mathf.Clamp(motion, 1f, 2f);
            Start(_core, _t);
        }

        /// <summary>How fast the pattern moves at PatternMotion 1: line spacings per second (lines), or time for the streaks.</summary>
        private static float Speed(string kind) => kind == "Streaks" ? 1f : kind == "Topo" ? .06f : kind == "Marble" ? .05f
            : kind == "Pixels" ? .07f : kind == "Terrain" ? .25f : .08f;

        private static void Start(PatternCore core, float t)
        {
            if (_back.Length != W * H * 4) _back = new byte[W * H * 4];
            _busy = true;
            int gen = _gen; string kind = _want; var buf = _back;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var c = core;
                    if (c == null)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        c = new PatternCore(kind, W, H);
                        c.Build();
                        _buildMs = sw.ElapsedMilliseconds; // logged on the main thread (the log file isn't thread-safe)
                    }
                    c.Render(t, buf);
                    _jobCore = c; _jobGen = gen;
                }
                catch (Exception e) { _jobError = e.GetType().Name + ": " + e.Message; }
                _done = true; _busy = false;
            });
        }

        private static void Upload()
        {
            if (_tex == null)
            {
                _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "LevelGate pattern" };
                _sprite = Sprite.Create(_tex, new Rect(0, 0, W, H), new Vector2(.5f, .5f));
            }
            _tex.LoadRawTextureData(_back);
            _tex.Apply(false, false);
            var swap = _front; _front = _back; _back = swap; // the worker writes the other one next time
            _shown = _core.Kind;
            Attach();
        }

        private static void Forget()
        {
            _want = null; _core = null; _gen++;
            if (_img != null && _img.sprite == _sprite && _sprite != null) _img.sprite = null;
        }

        /// <summary>Let the memory go (going into a raid). The next open works the pattern out again.</summary>
        public static void Release()
        {
            string keep = _want;
            _core = null; _gen++; _kept.Clear();
            if (_img != null) _img.enabled = false;
            if (_sprite != null) UnityEngine.Object.Destroy(_sprite);
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _sprite = null; _tex = null; _shown = null;
            _front = new byte[0]; _back = new byte[0];
            _want = keep; // still the chosen pattern: rebuilt on the next open
        }
    }
}
