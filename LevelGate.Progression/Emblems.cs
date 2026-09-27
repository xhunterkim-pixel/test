using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// Animated rank emblems. The pictures are sprite sheets in the plugin's emblems folder, listed lowest rank first in
    /// emblems.txt (file, frames, columns, frame size, ms per frame). The levels are shared out evenly between them, and
    /// the top level always gets the last (final) emblem. A sheet is only loaded when an emblem from it is first shown.
    /// </summary>
    internal static class Emblems
    {
        private sealed class Sheet
        {
            public string File;
            public int Frames, Columns, Size, Ms, From, To;
            public Sprite[] Sprites;
            public bool Failed;
        }

        private static List<Sheet> _sheets;

        private static string Folder => Path.Combine(Path.GetDirectoryName(typeof(Emblems).Assembly.Location) ?? "", "emblems");

        private static List<Sheet> Sheets()
        {
            if (_sheets != null) return _sheets;
            _sheets = new List<Sheet>();
            try
            {
                var list = Path.Combine(Folder, "emblems.txt");
                if (!File.Exists(list)) { L.Info("emblems: no " + list + " — the diamond badges are used"); return _sheets; }
                foreach (var line in File.ReadAllLines(list))
                {
                    var p = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length < 5 || p[0].StartsWith("#")) continue;
                    if (int.TryParse(p[1], out int frames) && int.TryParse(p[2], out int cols) && int.TryParse(p[3], out int size) && int.TryParse(p[4], out int ms))
                    {
                        var sheet = new Sheet { File = p[0], Frames = frames, Columns = Math.Max(1, cols), Size = size, Ms = Math.Max(10, ms) };
                        // optional: the levels this emblem is for
                        if (p.Length >= 7 && int.TryParse(p[5], out int from) && int.TryParse(p[6], out int to)) { sheet.From = from; sheet.To = to; }
                        _sheets.Add(sheet);
                    }
                    else L.Warn("emblems: can't read line: " + line);
                }
                L.Info($"emblems: {_sheets.Count} listed in {list}");
            }
            catch (Exception e) { L.Error("reading emblems.txt", e); }
            return _sheets;
        }

        public static bool Available => Sheets().Count > 0;

        /// <summary>Which emblem a level gets: the one listed for its levels (or the closest lower one if its own is
        /// missing); without level ranges in emblems.txt the top level gets the last and the rest are spread evenly.</summary>
        public static int IndexOf(int level)
        {
            var sheets = Sheets();
            if (sheets.Any(x => x.From > 0))
            {
                int best = -1;
                for (int i = 0; i < sheets.Count; i++)
                {
                    if (sheets[i].From <= 0) continue;
                    if (level >= sheets[i].From && level <= sheets[i].To) return i;
                    if (sheets[i].From <= level && (best < 0 || sheets[i].From > sheets[best].From)) best = i;
                }
                return best < 0 ? 0 : best;
            }
            int n = sheets.Count, max = Math.Max(2, ProgData.MaxLevel);
            if (n <= 1) return 0;
            if (level >= max) return n - 1;
            return Mathf.Clamp((level - 1) * (n - 1) / (max - 1), 0, n - 2);
        }

        private static Sheet Load(int index)
        {
            var sheets = Sheets();
            if (index < 0 || index >= sheets.Count) return null;
            var s = sheets[index];
            if (s.Sprites != null || s.Failed) return s.Failed ? null : s;
            var t0 = Time.realtimeSinceStartup;
            L.Step("emblem load " + s.File);
            try
            {
                var path = Path.Combine(Folder, s.File);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (!File.Exists(path) || !ImageConversion.LoadImage(tex, File.ReadAllBytes(path), true)) { s.Failed = true; L.Warn("emblems: can't load " + path); return null; }
                var sprites = new Sprite[s.Frames];
                for (int i = 0; i < s.Frames; i++)
                {
                    int x = i % s.Columns * s.Size, row = i / s.Columns;
                    sprites[i] = Sprite.Create(tex, new Rect(x, tex.height - (row + 1) * s.Size, s.Size, s.Size), new Vector2(.5f, .5f));
                }
                s.Sprites = sprites;
                L.Debug($"emblems: loaded {s.File} ({tex.width}x{tex.height}, {s.Frames} frames) in {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
                return s;
            }
            catch (Exception e) { s.Failed = true; L.Error("loading emblem " + s.File, e); return null; }
        }

        /// <summary>Loads a level's emblem sheet now (the loading screen does this, so no emblem pops in afterwards).</summary>
        public static void Preload(int level) { if (Available) Load(IndexOf(level)); }

        /// <summary>Shows the level's emblem on this image and keeps it playing. False if there's no emblem for it.</summary>
        public static bool Show(Image img, int level)
        {
            var s = Load(IndexOf(level));
            var player = img.GetComponent<EmblemPlayer>() ?? img.gameObject.AddComponent<EmblemPlayer>();
            player.Sprites = s?.Sprites;
            player.Ms = s?.Ms ?? 33;
            img.enabled = s != null;
            if (s != null) img.sprite = s.Sprites[0];
            return s != null;
        }
    }

    /// <summary>Plays an emblem's frames on its Image (all emblems in step, off the real clock).</summary>
    internal sealed class EmblemPlayer : MonoBehaviour
    {
        public Sprite[] Sprites;
        public int Ms = 33;
        private Image _img;
        private int _shown = -1;

        private void Update()
        {
            if (Sprites == null || Sprites.Length == 0) return;
            if (_img == null) _img = GetComponent<Image>();
            // performance mode: the emblem stands still on its first frame
            int f = ProgressionPlugin.Low ? 0 : (int)(Time.unscaledTime * 1000f / Ms) % Sprites.Length;
            if (f == _shown && _img.sprite == Sprites[f]) return;
            _shown = f;
            _img.sprite = Sprites[f];
        }
    }
}
