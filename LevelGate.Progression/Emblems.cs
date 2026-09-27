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
            public int Frames, Columns, Size, Ms;
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
                        _sheets.Add(new Sheet { File = p[0], Frames = frames, Columns = Math.Max(1, cols), Size = size, Ms = Math.Max(10, ms) });
                    else L.Warn("emblems: can't read line: " + line);
                }
                L.Info($"emblems: {_sheets.Count} listed in {list}");
            }
            catch (Exception e) { L.Error("reading emblems.txt", e); }
            return _sheets;
        }

        public static bool Available => Sheets().Count > 0;

        /// <summary>Which emblem a level gets: the top level the last one, the rest spread evenly over the others.</summary>
        public static int IndexOf(int level)
        {
            int n = Sheets().Count, max = Math.Max(2, ProgData.MaxLevel);
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
            int f = (int)(Time.unscaledTime * 1000f / Ms) % Sprites.Length;
            if (f == _shown && _img.sprite == Sprites[f]) return;
            _shown = f;
            _img.sprite = Sprites[f];
        }
    }
}
