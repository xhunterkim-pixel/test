using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// The new-rank moment in five styles (F12 > CURRENTLY TESTING > Rank Up Style), all driven by the rank beat's time
    /// (t seconds of dur, the emblem sound's peak at `peak`), so they stay in time with the sound:
    /// - Full     — 0.9.69–0.9.74's full-screen splash (ProgScreen.Splash);
    /// - Banner   — a dark tactical strip wipes across mid-screen: the emblem in a bracketed box, the rank's data beside it;
    /// - Dossier  — a Tarkov-style personnel document: photo box, typed fields, a red PROMOTED stamp slammed on the peak;
    /// - Terminal — a comms readout typing line by line, the emblem coming online on the peak;
    /// - Dogtag   — a worn metal tag dropping in on its bead chain, swinging to rest, the rank stamped on it.
    /// Each is built on first use; a skip hides it at once (as the Full one).
    /// </summary>
    internal static partial class ProgScreen
    {
        public enum RankUpStyle { Banner, Dossier, Terminal, Dogtag, Full }

        private static RankUpStyle RankStyle => ProgressionPlugin.TestRankStyle?.Value ?? RankUpStyle.Dogtag;

        private sealed class RankView
        {
            public RectTransform Root; public CanvasGroup Group; public Image Veil, Emblem, Flash, Stamp;
            public RectTransform Body, Tag; public Component Title, Name, Line, Log; public string[] Lines = new string[0];
            public int Level = -1; public bool Swapped;
        }

        private static readonly RankView[] _rankViews = new RankView[4];
        private static int _rankFrame = -10;

        /// <summary>Called by the rank beat each frame (instead of RankSplash when the style isn't Full).</summary>
        private static void RankMoment(int level, float t, float dur, float peak)
        {
            if (RankStyle == RankUpStyle.Full) { RankSplash(level, t, dur, peak); return; }
            try { DrawRank(level, t, dur, peak); }
            catch (System.Exception e) { L.ErrorOnce("new-rank moment", e); HideRankViews(); }
        }

        private static void HideRankViews() { _dtForLevel = -1; FadeGameWindow(1); foreach (var v in _rankViews) if (v?.Root != null && v.Root.gameObject.activeSelf) v.Root.gameObject.SetActive(false); }

        private static void TickRankViews()
        {
            if (Time.frameCount - _rankFrame > 1) HideRankViews();
        }

        private static void DrawRank(int level, float t, float dur, float peak)
        {
            int s = (int)RankStyle;
            var v = _rankViews[s];
            if (v == null || v.Root == null) v = _rankViews[s] = BuildRank(RankStyle);
            if (v == null) return;
            foreach (var o in _rankViews) if (o != null && o != v && o.Root != null && o.Root.gameObject.activeSelf) o.Root.gameObject.SetActive(false);
            var tier = TierOf(level);
            var light = Ui.Hex(tier.Light);
            string name = tier.Name.ToUpperInvariant();
            if (!v.Root.gameObject.activeSelf || v.Level != level)
            {
                v.Root.gameObject.SetActive(true); v.Root.SetAsLastSibling();
                v.Level = level; v.Swapped = false;
                Emblems.Show(v.Emblem, level - 1);
                v.Lines = new[]
                {
                    "> RANK UPDATE RECEIVED",
                    "> VERIFYING SERVICE RECORD ... OK",
                    $"> LEVEL {level:000}  //  CLEARANCE GRANTED",
                    $"> NEW RANK: {name}",
                };
            }
            _rankFrame = Time.frameCount;
            bool still = Motion.Still;
            float after = t - peak;
            if (!v.Swapped && (after >= 0 || still)) { v.Swapped = true; Emblems.Show(v.Emblem, level); }
            float fadeIn = Mathf.Clamp01(t / .3f), fadeOut = Mathf.Clamp01((dur - t) / .4f);
            float vis = still ? Mathf.Min(1, fadeOut * 2) : fadeIn * fadeOut;
            v.Group.alpha = vis;
            // the flash: only inside the style's own emblem box, never the whole screen
            if (v.Flash != null) v.Flash.color = new Color(1, .98f, .92f, still || after < 0 ? 0 : .6f * Mathf.Clamp01(1 - after / .3f));
            switch (RankStyle)
            {
                case RankUpStyle.Banner:
                {
                    v.Veil.color = new Color(0, 0, 0, .3f);
                    // wipes in from the left, out to the right
                    float inW = still ? 1 : Motion.Eval(Motion.Ease.OutExpo, t / .45f);
                    float outW = still ? 0 : Motion.Eval(Motion.Ease.InCubic, Mathf.Clamp01((t - (dur - .4f)) / .4f));
                    v.Body.anchorMin = new Vector2(outW, v.Body.anchorMin.y); v.Body.anchorMax = new Vector2(Mathf.Max(outW, inW), v.Body.anchorMax.y);
                    Ui.SetText(v.Name, Typed(name, after, still));
                    Ui.SetColor(v.Title, new Color(light.r, light.g, light.b, 1));
                    Ui.SetText(v.Line, after < 0 ? "RANK DATA  ·  SYNCING" : $"LEVEL {level}  ·  CLEARANCE GRANTED  ·  {System.DateTime.Now:dd.MM.yyyy}");
                    v.Emblem.rectTransform.localScale = Vector3.one * (still || after < 0 ? .9f : Mathf.Lerp(1.15f, 1f, Motion.Eval(Motion.Ease.OutCubic, after / .3f)));
                    break;
                }
                case RankUpStyle.Dossier:
                {
                    v.Veil.color = new Color(0, 0, 0, .55f);
                    // slides up a little and settles, slightly off square like a paper put down
                    float u = still ? 1 : Motion.Eval(Motion.Ease.OutCubic, t / .5f);
                    v.Body.anchoredPosition = new Vector2(0, Mathf.Lerp(-40, 0, u));
                    Ui.SetText(v.Log, TypedLines(new[] { $"RANK        {name}", $"LEVEL       {level:000}", $"CLEARANCE   LV {level:000}", $"DATE        {System.DateTime.Now:dd.MM.yyyy}" }, t / Mathf.Max(.1f, peak), still));
                    // the stamp: slams down on the peak (big → size, a jolt), stays
                    if (v.Stamp != null)
                    {
                        float sa = still ? 1 : Mathf.Clamp01(after / .08f);
                        float sc = still ? 1 : after < 0 ? 1.8f : Mathf.Lerp(1.8f, 1f, Motion.Eval(Motion.Ease.OutQuint, after / .14f));
                        v.Tag.localScale = new Vector3(sc, sc, 1);
                        v.Tag.gameObject.SetActive(after >= 0 || still);
                        foreach (var g in v.Tag.GetComponentsInChildren<Graphic>()) { var c = g.color; c.a = .85f * sa; g.color = c; }
                        float jolt = still || after < 0 || after > .2f ? 0 : 4 * Mathf.Sin(after * 90) * (1 - after / .2f);
                        v.Body.anchoredPosition += new Vector2(jolt, -jolt * .5f);
                    }
                    break;
                }
                case RankUpStyle.Terminal:
                {
                    v.Veil.color = new Color(0, 0, 0, .35f);
                    Ui.SetText(v.Log, TypedLines(v.Lines, t / Mathf.Max(.1f, peak) * 1.05f, still) + ((Time.unscaledTime * 3 % 2) < 1 ? "_" : ""));
                    Ui.SetColor(v.Log, new Color(light.r, light.g, light.b, .95f));
                    // the emblem comes online on the peak: flickers on
                    float on = still ? 1 : after < 0 ? 0 : Mathf.Clamp01(after / .25f) * (after < .25f ? (Mathf.Sin(after * 90) > 0 ? 1 : .3f) : 1);
                    v.Emblem.color = new Color(1, 1, 1, on);
                    break;
                }
                case RankUpStyle.Dogtag:
                    DrawDogtag(v, level, t, dur, after, still, name); // ProgScreen.Dogtag
                    break;
            }
        }

        private static string Typed(string s, float after, bool still)
        {
            if (still) return s;
            int n = Mathf.Clamp(Mathf.FloorToInt(s.Length * Mathf.Clamp01((after - .05f) / .45f)), 0, s.Length);
            return s.Substring(0, n) + (n < s.Length && after >= 0 && (Time.unscaledTime * 8 % 2) < 1 ? "_" : "");
        }

        /// <summary>Lines typed one after another as `u` goes 0 → 1 (still: all at once).</summary>
        private static string TypedLines(string[] lines, float u, bool still)
        {
            if (still) return string.Join("\n", lines);
            int total = 0; foreach (var l in lines) total += l.Length;
            int n = Mathf.FloorToInt(total * Mathf.Clamp01(u));
            var sb = new System.Text.StringBuilder();
            foreach (var l in lines)
            {
                if (n <= 0) break;
                int k = Mathf.Min(n, l.Length);
                sb.Append(l, 0, k).Append('\n');
                n -= k;
            }
            return sb.ToString().TrimEnd('\n');
        }

        private static RankView BuildRank(RankUpStyle style)
        {
            if (_splashRoot == null) return null;
            var v = new RankView();
            var root = Ui.Rect(_splashRoot, "RankUp_" + style, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SubCanvas(root);
            v.Root = root;
            v.Group = root.gameObject.AddComponent<CanvasGroup>();
            v.Veil = Ui.Img(Ui.Fill(root, "Veil"), new Color(0, 0, 0, 0));
            var ink = Ui.Hex("#d5d9d6");
            switch (style)
            {
                case RankUpStyle.Banner:
                {
                    var band = Ui.Rect(root, "Band", new Vector2(0, .44f), new Vector2(1, .44f), new Vector2(0, -70), new Vector2(0, 70));
                    v.Body = band;
                    band.gameObject.AddComponent<RectMask2D>();
                    Ui.Img(band, Ui.Hex("#0a0d0f", .93f));
                    Ui.Img(Ui.Rect(band, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero), new Color(1, 1, 1, .35f));
                    Ui.Img(Ui.Rect(band, "Bottom", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)), new Color(1, 1, 1, .2f));
                    var scan = Ui.Img(Ui.Fill(band, "Scan"), new Color(1, 1, 1, .04f), Ui.Scanlines()); scan.type = Image.Type.Tiled;
                    var box = Ui.Rect(band, "EmblemBox", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-330, -54), new Vector2(-222, 54));
                    Ui.Img(box, Ui.Hex("#11161a", .9f));
                    Ui.Brackets(box, 0, 14, new Color(1, 1, 1, .8f));
                    v.Emblem = Ui.Img(Ui.Fill(box, "Emblem", 8), Color.white); v.Emblem.preserveAspect = true;
                    v.Flash = Ui.Img(Ui.Fill(box, "Flash"), new Color(1, 1, 1, 0));
                    v.Title = Ui.Label(Ui.Rect(band, "Title", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-200, 22), new Vector2(400, 42)), "Text", "RANK PROMOTION", 12, Color.white, TextAnchor.MiddleLeft, true, 4);
                    v.Name = Ui.Label(Ui.Rect(band, "Name", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-200, -18), new Vector2(500, 22)), "Text", "", 38, Color.white, TextAnchor.MiddleLeft, true, 5);
                    v.Line = Ui.Label(Ui.Rect(band, "Line", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-200, -44), new Vector2(500, -24)), "Text", "", 11, Ui.Hex("#9aa3a6"), TextAnchor.MiddleLeft, false, 2);
                    var ruler = Ui.Img(Ui.Rect(band, "Ruler", new Vector2(.62f, 0), new Vector2(.95f, 0), new Vector2(0, 8), new Vector2(0, 17)), new Color(1, 1, 1, .3f), Ui.Ruler()); ruler.type = Image.Type.Tiled;
                    break;
                }
                case RankUpStyle.Dossier:
                {
                    var doc = Ui.Box(root, "Doc", new Vector2(.5f, .52f), Vector2.zero, new Vector2(620, 330));
                    v.Body = doc;
                    doc.localEulerAngles = new Vector3(0, 0, -1.2f);
                    Ui.Img(doc, Ui.Hex("#1a1d1c", .97f));
                    var grain = Ui.Img(Ui.Fill(doc, "Grain"), new Color(1, 1, 1, .05f), Ui.Grain()); grain.type = Image.Type.Tiled;
                    Ui.Grit(doc, 3, .05f);
                    Ui.Label(Ui.Rect(doc, "Head", new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -44), new Vector2(-24, -16)), "Text", "PERSONNEL FILE  //  SERVICE RECORD", 13, ink, TextAnchor.MiddleLeft, true, 3);
                    Ui.Img(Ui.Rect(doc, "Rule", new Vector2(0, 1), new Vector2(1, 1), new Vector2(24, -50), new Vector2(-24, -49)), new Color(1, 1, 1, .25f));
                    var photo = Ui.Rect(doc, "Photo", new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -250), new Vector2(204, -70));
                    Ui.Img(photo, Ui.Hex("#0d100f"));
                    Ui.Brackets(photo, 0, 16, new Color(1, 1, 1, .6f));
                    v.Emblem = Ui.Img(Ui.Fill(photo, "Emblem", 14), Color.white); v.Emblem.preserveAspect = true;
                    v.Flash = Ui.Img(Ui.Fill(photo, "Flash"), new Color(1, 1, 1, 0));
                    v.Log = Ui.Label(Ui.Rect(doc, "Fields", new Vector2(0, 1), new Vector2(1, 1), new Vector2(230, -250), new Vector2(-24, -70)), "Text", "", 15, ink, TextAnchor.UpperLeft, false, 2);
                    Refl.Set(v.Log, "lineSpacing", 18f);
                    // the stamp: a red rubber stamp, rotated, over the fields
                    var stamp = Ui.Box(doc, "Stamp", new Vector2(.7f, .3f), Vector2.zero, new Vector2(250, 70));
                    stamp.localEulerAngles = new Vector3(0, 0, 11);
                    v.Tag = stamp;
                    var red = Ui.Hex("#b3302e");
                    foreach (var (a, b, c, d) in new[] { (Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 3)), (new Vector2(0, 1), Vector2.one, new Vector2(0, -3), Vector2.zero),
                                                         (Vector2.zero, new Vector2(0, 1), Vector2.zero, new Vector2(3, 0)), (new Vector2(1, 0), Vector2.one, new Vector2(-3, 0), Vector2.zero) })
                        Ui.Img(Ui.Rect(stamp, "Edge", a, b, c, d), red);
                    v.Stamp = Ui.Img(Ui.Fill(stamp, "Wear"), new Color(0, 0, 0, 0));
                    Ui.Label(Ui.Fill(stamp, "Text"), "Text", "PROMOTED", 36, red, TextAnchor.MiddleCenter, true, 6);
                    stamp.gameObject.SetActive(false);
                    break;
                }
                case RankUpStyle.Terminal:
                {
                    var box = Ui.Rect(root, "Terminal", new Vector2(.5f, .18f), new Vector2(.5f, .18f), new Vector2(-360, -80), new Vector2(360, 110));
                    v.Body = box;
                    Ui.Img(box, Ui.Hex("#070a0b", .94f));
                    Ui.Img(Ui.Rect(box, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, -1), Vector2.zero), new Color(1, 1, 1, .3f));
                    Ui.Label(Ui.Rect(box, "Head", new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -26), new Vector2(-16, -6)), "Text", "COMMS  //  RANK_SERVICE  //  CH 04", 10, Ui.Hex("#7d8588"), TextAnchor.MiddleLeft, false, 2);
                    var scan = Ui.Img(Ui.Fill(box, "Scan"), new Color(1, 1, 1, .05f), Ui.Scanlines()); scan.type = Image.Type.Tiled;
                    v.Log = Ui.Label(Ui.Rect(box, "Log", new Vector2(0, 0), new Vector2(1, 1), new Vector2(16, 14), new Vector2(-190, -32)), "Text", "", 15, Color.white, TextAnchor.UpperLeft, false, 1.5f);
                    Refl.Set(v.Log, "lineSpacing", 14f);
                    var eb = Ui.Rect(box, "EmblemBox", new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-176, -70), new Vector2(-16, 60));
                    Ui.Brackets(eb, 0, 12, new Color(1, 1, 1, .5f));
                    v.Emblem = Ui.Img(Ui.Fill(eb, "Emblem", 10), Color.white); v.Emblem.preserveAspect = true;
                    v.Flash = Ui.Img(Ui.Fill(eb, "Flash"), new Color(1, 1, 1, 0));
                    break;
                }
                case RankUpStyle.Dogtag:
                    BuildDogtag(v, root); // ProgScreen.Dogtag
                    break;
            }
            foreach (var g in root.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            root.gameObject.SetActive(false);
            return v;
        }
    }
}
