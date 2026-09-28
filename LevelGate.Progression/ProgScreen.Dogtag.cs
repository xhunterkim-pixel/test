using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The Dogtag rank-up, the way the game itself would show it: Tarkov's item inspect window — "Dogtag USEC / BEAR",
    /// the breadcrumb, the picture on the inspect window's diagonal-striped background, then the property grid (NICKNAME,
    /// FACTION, LEVEL, RANK, DATE, STATUS) and a line of description. The tag is your faction's: BEAR — an oval stamped tag
    /// with vertical lettering and a rule (like "ВС РОССИИ / P-048119"); USEC — a rounded tag with lines of small engraved
    /// text. Dull, worn steel on a small bead chain looping like the photos. On the emblem sound's peak the RANK row
    /// changes, the new rank emblem appears in the picture's corner, and a glint crosses the tag.
    /// </summary>
    internal static partial class ProgScreen
    {
        private const float WinW = 560, WinH = 610, PicH = 330;
        private static RectTransform _dtWin, _dtTag, _dtEmblemBox;
        private static Image _dtGlint;
        private static Component _dtTitle, _dtRankValue, _dtDesc;
        private static readonly List<Component> _dtEngrave = new List<Component>();
        private static bool _dtBear;
        private static Sprite _dtPlateBear, _dtPlateUsec, _dtBead, _dtStripes;

        private static (string Nick, string Side) Me2()
        {
            var info = Refl.Get(ProgData.Profile(), "Info");
            string nick = Refl.Get(info, "Nickname")?.ToString() ?? "OPERATOR";
            string side = Refl.Get(info, "Side")?.ToString() ?? "Usec";
            return (nick, side.ToUpperInvariant().Contains("BEAR") ? "BEAR" : "USEC");
        }

        /// <summary>Tarkov's inspect background: dark, with wide soft diagonal stripes. Tiles.</summary>
        private static Sprite InspectStripes()
        {
            if (_dtStripes != null) return _dtStripes;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = ((x + y) % 32) / 32f;                      // 45° bands, 32 px period
                    float a = Mathf.Clamp01(1 - Mathf.Abs(d - .5f) * 4); // soft stripe
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return _dtStripes = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }

        /// <summary>
        /// A tag plate: dull, worn steel (a soft diagonal sheen, grime, fine scratches, a slight bevel) with its hole.
        /// oval: BEAR's stadium tag (fully round ends); else USEC's rounded rectangle. 180x300, own colours.
        /// </summary>
        private static Sprite TagPlateOf(bool oval)
        {
            if (oval && _dtPlateBear != null) return _dtPlateBear;
            if (!oval && _dtPlateUsec != null) return _dtPlateUsec;
            const int w = 180, h = 300, ss = 2;
            float r = oval ? w / 2f : 30;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "LevelGate tag" };
            var px = new Color32[w * h];
            var rnd = new System.Random(oval ? 11 : 13);
            var scratch = new float[w * h];
            for (int k = 0; k < 70; k++)
            {
                float x0 = (float)rnd.NextDouble() * w, y0 = (float)rnd.NextDouble() * h, ang = (float)(rnd.NextDouble() * Mathf.PI);
                float len = 5 + (float)rnd.NextDouble() * 35, s = (float)(rnd.NextDouble() < .5 ? .05 : -.04) * (.3f + .7f * (float)rnd.NextDouble());
                for (float d = 0; d < len; d += .7f)
                {
                    int x = (int)(x0 + Mathf.Cos(ang) * d), y = (int)(y0 + Mathf.Sin(ang) * d);
                    if (x >= 0 && x < w && y >= 0 && y < h) scratch[y * w + x] += s;
                }
            }
            float Dist(float x, float y)
            {
                float qx = Mathf.Abs(x - w / 2f) - (w / 2f - r), qy = Mathf.Abs(y - h / 2f) - (h / 2f - r);
                return Mathf.Sqrt(Mathf.Max(qx, 0) * Mathf.Max(qx, 0) + Mathf.Max(qy, 0) * Mathf.Max(qy, 0)) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            }
            float hx = w / 2f, hy = h - (oval ? 34 : 22), hr = 8;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float cover = 0;
                    for (int j = 0; j < ss; j++) for (int i = 0; i < ss; i++)
                    {
                        float fx = x + (i + .5f) / ss, fy = y + (j + .5f) / ss;
                        if (Dist(fx, fy) <= 0 && (fx - hx) * (fx - hx) + (fy - hy) * (fy - hy) > hr * hr) cover += 1f / (ss * ss);
                    }
                    if (cover <= 0) continue;
                    float d = -Dist(x + .5f, y + .5f);
                    // dull steel: a soft diagonal sheen, broad grime, fine scratches, a slight bevel at the edge
                    float u = (x / (float)w + (1 - y / (float)h)) * .5f;
                    float sheen = .1f * Mathf.Exp(-Mathf.Pow((u - .42f) / .16f, 2));
                    float v = .58f + sheen + (Mathf.PerlinNoise(x * .03f + (oval ? 7 : 0), y * .03f) - .5f) * .14f + (Mathf.PerlinNoise(x * .2f, y * .2f) - .5f) * .04f + scratch[y * w + x];
                    v *= 1 - .2f * (1 - Mathf.Clamp01(d / 26f)); // darker toward the edges, like the photos
                    if (d < 3) v *= d < 1.2f ? .6f : 1.12f;
                    float dh = Mathf.Sqrt((x + .5f - hx) * (x + .5f - hx) + (y + .5f - hy) * (y + .5f - hy)) - hr;
                    if (dh < 2.5f) v *= .7f;
                    v = Mathf.Clamp01(v);
                    px[y * w + x] = new Color32((byte)(255 * v * .94f), (byte)(255 * v * .96f), (byte)(255 * v), (byte)(255 * cover));
                }
            tex.SetPixels32(px); tex.Apply(true, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
            if (oval) _dtPlateBear = sp; else _dtPlateUsec = sp;
            return sp;
        }

        /// <summary>A small steel bead (ball chain). 10x10.</summary>
        private static Sprite Bead()
        {
            if (_dtBead != null) return _dtBead;
            const int n = 10;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1, rr = dx * dx + dy * dy;
                    if (rr > 1) continue;
                    float hl = Mathf.Clamp01(1 - ((dx + .3f) * (dx + .3f) + (dy - .3f) * (dy - .3f)) * 2.2f);
                    float v = Mathf.Clamp01(.28f + .35f * Mathf.Sqrt(1 - rr) + .45f * hl * hl);
                    px[y * n + x] = new Color32((byte)(255 * v * .95f), (byte)(255 * v * .97f), (byte)(255 * v), (byte)(255 * Mathf.Clamp01((1 - rr) * 5)));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return _dtBead = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
        }

        /// <summary>Engraved text: dark letters with a light lower edge (pressed into the metal).</summary>
        private static void Engrave(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, string text, float size, float spacing, TextAnchor align)
        {
            var hi = Ui.Label(Ui.Rect(parent, "Hi", aMin, aMax, oMin + new Vector2(.8f, -.8f), oMax + new Vector2(.8f, -.8f)), "Text", text, size, new Color(1, 1, 1, .32f), align, true, spacing);
            var lo = Ui.Label(Ui.Rect(parent, "Lo", aMin, aMax, oMin, oMax), "Text", text, size, new Color(.12f, .13f, .14f, .8f), align, true, spacing);
            _dtEngrave.Add(hi); _dtEngrave.Add(lo);
        }

        private static void BuildDogtag(RankView v, RectTransform root)
        {
            var (nick, side) = Me2();
            _dtBear = side == "BEAR";
            // the window
            var win = Ui.Box(root, "InspectWindow", new Vector2(.5f, .5f), Vector2.zero, new Vector2(WinW, WinH));
            _dtWin = win; v.Body = win;
            Ui.Img(win, Ui.Hex("#0d1011", .98f));
            Ui.Outline(win, Ui.Hex("#3a4245"));
            var head = Ui.Rect(win, "Head", new Vector2(0, 1), Vector2.one, new Vector2(1, -30), new Vector2(-1, -1));
            Ui.Img(head, Ui.Hex("#1b2022"));
            _dtTitle = Ui.Label(Ui.Rect(head, "Title", Vector2.zero, Vector2.one, new Vector2(12, 0), new Vector2(-12, 0)), "Text", $"Dogtag {side}", 15, Ui.Hex("#e1e4e5"), TextAnchor.MiddleLeft, false, .5f);
            Ui.Label(Ui.Rect(win, "Crumb", new Vector2(0, 1), Vector2.one, new Vector2(12, -52), new Vector2(-12, -32)), "Text", "Progression  ›  Rank", 12, Ui.Hex("#7d8588"), TextAnchor.MiddleLeft, false, .5f);
            Ui.Label(Ui.Rect(win, "Weight", new Vector2(0, 1), Vector2.one, new Vector2(12, -52), new Vector2(-12, -32)), "Text", "0.010 kg", 12, Ui.Hex("#aab2b5"), TextAnchor.MiddleRight, false, .5f);
            // the picture: the inspect window's striped background
            var pic = Ui.Rect(win, "Picture", new Vector2(0, 1), Vector2.one, new Vector2(1, -54 - PicH), new Vector2(-1, -54));
            Ui.Img(pic, Ui.Hex("#15191a"));
            var st = Ui.Img(Ui.Fill(pic, "Stripes"), new Color(1, 1, 1, .035f), InspectStripes()); st.type = Image.Type.Tiled;
            pic.gameObject.AddComponent<RectMask2D>();
            // the tag, a little turned, on its chain
            var tag = Ui.Box(pic, "Tag", new Vector2(.5f, .47f), Vector2.zero, new Vector2(150, 250));
            tag.localEulerAngles = new Vector3(0, 0, _dtBear ? -22 : -14);
            _dtTag = tag;
            var holePos = new Vector2(0, 125 - (_dtBear ? 28 : 18));
            var pts = new List<Vector2> { holePos, holePos + new Vector2(-20, 40), holePos + new Vector2(-95, 80), holePos + new Vector2(-150, 40), holePos + new Vector2(-120, -20),
                                          holePos + new Vector2(-60, 10), holePos + new Vector2(-40, 90), holePos + new Vector2(-80, 170), holePos + new Vector2(-60, 260) };
            float acc = 0; Vector2 last = pts[0]; var bead = Bead();
            for (int seg = 0; seg < pts.Count - 1; seg++)
            {
                Vector2 p0 = pts[Mathf.Max(0, seg - 1)], p1 = pts[seg], p2 = pts[seg + 1], p3 = pts[Mathf.Min(pts.Count - 1, seg + 2)];
                for (float u = 0; u < 1; u += .02f)
                {
                    float u2 = u * u, u3 = u2 * u;
                    var p = .5f * (2 * p1 + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3);
                    acc += (p - last).magnitude; last = p;
                    if (acc < 4.2f) continue;
                    acc = 0;
                    var b = Ui.Img(Ui.Box(tag, "Bead", new Vector2(.5f, .5f), p, new Vector2(4.4f, 4.4f)), Color.white, bead);
                    b.transform.SetAsFirstSibling();
                }
            }
            Ui.Img(Ui.Box(tag, "Shadow", new Vector2(.5f, .5f), new Vector2(8, -10), new Vector2(190, 290)), new Color(0, 0, 0, .5f), Ui.Radial());
            Ui.Img(Ui.Fill(tag, "Plate"), Color.white, TagPlateOf(_dtBear)).preserveAspect = true;
            // the stamping
            if (_dtBear)
            {
                // like "ВС РОССИИ / P-048119": vertical lettering either side of a rule
                var col = Ui.Rect(tag, "Col", new Vector2(.5f, .45f), new Vector2(.5f, .45f), new Vector2(-110, -26), new Vector2(110, 26));
                col.localEulerAngles = new Vector3(0, 0, -90);
                Engrave(col, new Vector2(0, .5f), new Vector2(1, 1), Vector2.zero, Vector2.zero, "ВС РОССИИ", 17, 3, TextAnchor.MiddleCenter);
                Engrave(col, new Vector2(0, 0), new Vector2(1, .5f), Vector2.zero, Vector2.zero, $"{nick.ToUpperInvariant()}", 15, 2, TextAnchor.MiddleCenter);
                var rule = Ui.Rect(tag, "Rule", new Vector2(.5f, .1f), new Vector2(.5f, .78f), new Vector2(-.6f, 0), new Vector2(.6f, 0));
                Ui.Img(rule, new Color(.12f, .13f, .14f, .7f));
            }
            else
            {
                // US style: lines of small engraved text
                var lines = new[] { nick.ToUpperInvariant(), "USEC PMC", "ID " + Mathf.Abs((nick + "usec").GetHashCode() % 1000000).ToString("000000"), "O POS", "NO PREF" };
                for (int i = 0; i < lines.Length; i++)
                    Engrave(tag, new Vector2(.14f, .72f - i * .1f), new Vector2(.94f, .8f - i * .1f), Vector2.zero, Vector2.zero, lines[i], 12, 1.5f, TextAnchor.MiddleLeft);
            }
            // the glint: a soft band crossing the tag once, only on its shape
            var gm = Ui.Fill(tag, "GlintMask");
            Ui.Img(gm, Color.white, TagPlateOf(_dtBear)).preserveAspect = true;
            gm.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            _dtGlint = Ui.Img(Ui.Rect(gm, "Glint", new Vector2(0, 0), new Vector2(0, 1), new Vector2(-30, -40), new Vector2(30, 40)), new Color(1, 1, 1, 0), Ui.Radial());
            _dtGlint.rectTransform.localEulerAngles = new Vector3(0, 0, -20);
            // the new rank's emblem, top-right of the picture (like an item's badge)
            var eb = Ui.Rect(pic, "Emblem", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-104, -104), new Vector2(-12, -12));
            _dtEmblemBox = eb;
            Ui.Brackets(eb, 0, 10, new Color(1, 1, 1, .5f));
            v.Emblem = Ui.Img(Ui.Fill(eb, "Img", 6), Color.white); v.Emblem.preserveAspect = true;
            v.Flash = Ui.Img(Ui.Box(eb, "Flash", new Vector2(.5f, .5f), Vector2.zero, new Vector2(140, 140)), new Color(1, 1, 1, 0), Ui.Radial());
            // the property grid, like the game's inspect rows (two per line)
            var grid = Ui.Rect(win, "Rows", new Vector2(0, 0), new Vector2(1, 0), new Vector2(10, 70), new Vector2(-10, 70 + 3 * 30));
            string date = System.DateTime.Now.ToString("dd.MM.yyyy HH:mm");
            var rows = new[] { ("NICKNAME", nick), ("FACTION", side), ("LEVEL", "—"), ("RANK", "—"), ("DATE", date), ("STATUS", "Promoted") };
            for (int i = 0; i < rows.Length; i++)
            {
                int r = i / 2, c = i % 2;
                var row = Ui.Rect(grid, "Row", new Vector2(c * .5f, 1), new Vector2(c * .5f + .5f, 1), new Vector2(c == 0 ? 0 : 4, -(r + 1) * 30 + 2), new Vector2(c == 0 ? -4 : 0, -r * 30));
                Ui.Img(row, Ui.Hex("#1a1f21", .95f));
                Ui.Label(Ui.Rect(row, "K", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0)), "Text", rows[i].Item1, 11, Ui.Hex("#8e979a"), TextAnchor.MiddleLeft, true, 1);
                var val = Ui.Label(Ui.Rect(row, "V", Vector2.zero, Vector2.one, new Vector2(10, 0), new Vector2(-10, 0)), "Text", rows[i].Item2, 13, Ui.Hex("#e1e4e5"), TextAnchor.MiddleRight, false, .5f);
                if (rows[i].Item1 == "RANK") _dtRankValue = val;
                if (rows[i].Item1 == "LEVEL") v.Line = val;
            }
            _dtDesc = Ui.Label(Ui.Rect(win, "Desc", new Vector2(0, 0), new Vector2(1, 0), new Vector2(14, 12), new Vector2(-14, 64)), "Text", "", 12, Ui.Hex("#b9c0c3"), TextAnchor.UpperLeft, false, .3f);
            Refl.Set(_dtDesc, "enableWordWrapping", true);
        }

        private static int _dtForLevel = -1;
        private static bool _dtReal;

        private static void DrawDogtag(RankView v, int level, float t, float dur, float after, bool still, string name)
        {
            var (nick, side) = Me2();
            // on the peak: your real dogtag in the game's own inspect window (the real tag, the real rows); the drawn
            // window below is only the fallback if the game won't make the item
            if (_dtForLevel != level) { _dtForLevel = level; _dtReal = false; if (!still) _dtWin.gameObject.SetActive(false); }
            if ((after >= 0 || still) && !_dtReal && !_dtWin.gameObject.activeSelf)
            {
                _dtReal = GameItems.InspectDogtag(side == "BEAR", nick, level, TierOf(level).Name);
                if (!_dtReal) { _dtWin.gameObject.SetActive(true); L.Info("dogtag: showing the drawn window instead"); }
            }
            if (_dtReal || !_dtWin.gameObject.activeSelf)
            {
                v.Veil.color = new Color(0, 0, 0, .4f); // behind the game's window
                return;
            }
            v.Veil.color = new Color(0, 0, 0, .55f);
            // the window opens like the game's: a quick fade and settle
            float o = still ? 1 : Motion.Eval(Motion.Ease.OutCubic, t / .2f);
            _dtWin.localScale = Vector3.one * Mathf.Lerp(.97f, 1, o);
            Ui.SetText(v.Line, level.ToString());
            string rank = after >= 0 || still ? TierOf(level).Name : TierOf(Mathf.Max(1, level - 1)).Name;
            Ui.SetText(_dtRankValue, rank);
            Ui.SetColor(_dtRankValue, after >= 0 && after < .6f && !still ? Color.Lerp(Ui.Hex("#e0c24a"), Ui.Hex("#e1e4e5"), after / .6f) : Ui.Hex("#e1e4e5"));
            Ui.SetText(_dtDesc, $"Military dogtag, reissued on promotion to {TierOf(level).Name}. It belongs to operator {nick}.");
            // the tag hangs still, then shifts a little on the peak (as if picked up)
            float swing = still || after < 0 ? 0 : 3 * Mathf.Exp(-after * 3) * Mathf.Sin(after * 9);
            _dtTag.localEulerAngles = new Vector3(0, 0, (_dtBear ? -22 : -14) + swing);
            // the emblem appears on the peak
            float es = still || after < 0 ? 0 : Motion.Eval(Motion.Ease.OutBack, after / .3f);
            _dtEmblemBox.localScale = new Vector3(es, es, 1);
            // the glint crosses the tag once, just after
            float g = still ? -1 : (after - .1f) / .7f;
            if (g >= 0 && g <= 1)
            {
                var r = _dtGlint.rectTransform;
                float x = Mathf.Lerp(-.3f, 1.3f, Motion.Eval(Motion.Ease.InOutSine, g));
                r.anchorMin = new Vector2(x, 0); r.anchorMax = new Vector2(x, 1);
                _dtGlint.color = new Color(1, 1, 1, .4f * Motion.Eval(Motion.Ease.Pulse, g));
            }
            else _dtGlint.color = new Color(1, 1, 1, 0);
        }
    }
}
