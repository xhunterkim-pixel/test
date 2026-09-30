using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace LevelGate.Progression
{
    /// <summary>
    /// Similar items grouped inside their category (F12 > Group Similar Items): by kind — balaclavas, beanies, berets,
    /// bandanas, shemaghs, neoprene masks, caps, hats, glasses, goggles, masks, headsets, armbands, scarves, gloves — or,
    /// failing that, variants of one item ("Titan Aramid (MultiCam / OD Green / Rudiarius)"). A group needs 3 or more.
    /// Looks: Stack — one tile with cards stacked behind it and a +N box that opens the group; Folder — a 2x2 of the
    /// first pictures and the +N box; Rows — everything shown, each group under its own small label. Opening / closing a
    /// group keeps the picked item and the scroll position.
    /// </summary>
    internal static partial class ProgScreen
    {
        public enum GroupLook { Stack, Folder, Rows, Off }

        private static GroupLook Grouping => ProgressionPlugin.GroupStyle?.Value ?? GroupLook.Stack;

        private static readonly HashSet<string> _openGroups = new HashSet<string>();
        private static string _keepPickTpl;
        private static float _keepScrollY = -1;

        // kinds, most specific first (neoprene / balaclava before mask)
        private static readonly (string Word, string Label)[] Kinds =
        {
            ("balaclava", "Balaclavas"), ("neoprene", "Neoprene masks"), ("shemagh", "Shemaghs"), ("bandana", "Bandanas"), ("beanie", "Beanies"),
            ("beret", "Berets"), ("cap", "Caps"), ("hat", "Hats"), ("goggles", "Goggles"), ("glasses", "Glasses"), ("mask", "Masks"),
            ("headset", "Headsets"), ("armband", "Armbands"), ("scarf", "Scarves"), ("gloves", "Gloves"),
        };

        private static (string Key, string Label) SimilarKey(ProgItem it)
        {
            string name = (it.Name ?? "").ToLowerInvariant();
            foreach (var (word, label) in Kinds)
                if (Regex.IsMatch(name, @"\b" + word + @"s?\b")) return ("k:" + word, label);
            // variants of one item: the name without what's in brackets
            string baseName = Regex.Replace(it.Name ?? "", @"\s*\(.*?\)", "").Trim();
            return ("b:" + baseName.ToLowerInvariant(), baseName);
        }

        /// <summary>The list in order, split into groups (3+ similar) and single items.</summary>
        private static List<(string Key, string Label, List<ProgItem> Items)> Similar(List<ProgItem> list)
        {
            var byKey = new Dictionary<string, (string Label, List<ProgItem> Items)>();
            var order = new List<string>();
            foreach (var it in list)
            {
                var (k, label) = SimilarKey(it);
                if (!byKey.TryGetValue(k, out var e)) { e = (label, new List<ProgItem>()); byKey[k] = e; order.Add(k); }
                e.Items.Add(it);
            }
            var outList = new List<(string, string, List<ProgItem>)>();
            foreach (var k in order)
            {
                var e = byKey[k];
                if (e.Items.Count >= 3) outList.Add((k, e.Label, e.Items));
                else foreach (var it in e.Items) outList.Add((null, null, new List<ProgItem> { it }));
            }
            return outList;
        }

        /// <summary>A category's tiles, grouped per the F12 look (small side-by-side slots are never grouped).</summary>
        private static void AddGrouped(RectTransform body, RectTransform grid, string catKey, List<ProgItem> list, bool narrow, int cols, float cell, Action<RectTransform, ProgItem> addTile)
        {
            var look = Grouping;
            if (look == GroupLook.Off || narrow) { foreach (var it in list) addTile(grid, it); return; }
            var parts = Similar(list);
            if (look == GroupLook.Rows)
            {
                // singles first in the category's own grid, then each group under a label in a grid of its own
                foreach (var p in parts.Where(p => p.Key == null)) addTile(grid, p.Items[0]);
                if (!parts.Any(p => p.Key == null)) grid.gameObject.SetActive(false);
                foreach (var p in parts.Where(p => p.Key != null))
                {
                    var lab = Ui.Rect(body, "GroupLabel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    lab.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;
                    var l = Ui.Label(Ui.Rect(lab, "Text", Vector2.zero, Vector2.one, new Vector2(2, 0), Vector2.zero), "Text", $"{p.Label.ToUpperInvariant()}   <color=#6a7376>{p.Items.Count}</color>", 10, Ui.Hex("#9aa3a6"), TextAnchor.LowerLeft, true, 2);
                    ((Graphic)l).raycastTarget = false;
                    Ui.Img(Ui.Rect(lab, "Rule", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1)), new Color(1, 1, 1, .1f)).raycastTarget = false;
                    var g2 = Ui.Rect(body, "Grid", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                    var gl = g2.gameObject.AddComponent<GridLayoutGroup>();
                    var src = grid.GetComponent<GridLayoutGroup>();
                    gl.cellSize = src.cellSize; gl.spacing = src.spacing; gl.startCorner = src.startCorner; gl.constraint = src.constraint; gl.constraintCount = src.constraintCount;
                    foreach (var it in p.Items) addTile(g2, it);
                }
                return;
            }
            foreach (var p in parts)
            {
                if (p.Key == null) { addTile(grid, p.Items[0]); continue; }
                string gk = catKey + "/" + p.Key;
                bool open = _openGroups.Contains(gk);
                if (open)
                {
                    foreach (var it in p.Items) addTile(grid, it);
                    if (_tileViews.TryGetValue(p.Items[0].Tpl, out var first)) CountBox(first.Rt, "−", gk, $"{p.Label} · close");
                    continue;
                }
                // closed: one tile stands for the group — the picked item if it's in it, else the first
                var rep = p.Items.FirstOrDefault(i => i.Tpl == _featTpl) ?? p.Items[0];
                addTile(grid, rep);
                _closedGroups[rep.Tpl] = p.Items; // 1.0.1: clicking it clears NEW on the whole group
                _groupMembers[gk] = p.Items;
                if (!_tileViews.TryGetValue(rep.Tpl, out var v)) continue;
                if (look == GroupLook.Stack) StackEdges(v.Rt);
                else FolderPictures(v, p.Items.Where(i => i != rep).Take(3).ToList());
                CountBox(v.Rt, $"+{p.Items.Count - 1}", gk, $"{p.Label} · {p.Items.Count}");
                var cap = Ui.Label(Ui.Rect(v.Rt, "GroupName", new Vector2(0, 0), new Vector2(1, 0), new Vector2(8, 22), new Vector2(-40, 34)), "Text", p.Label.ToUpperInvariant(), 9, Ui.Hex("#9aa3a6"), TextAnchor.MiddleLeft, true, 1.5f, true);
                ((Graphic)cap).raycastTarget = false;
            }
        }

        /// <summary>Stack: two cards' edges peeking out behind the tile, bottom-right (drawn outside it, so they never cover it).
        /// 1.0.16: 2.5 / 5 px out (7 px reached past the category box's 6 px padding onto its border on the bottom row).</summary>
        private static void StackEdges(RectTransform rt)
        {
            for (int k = 2; k >= 1; k--)
            {
                float o = 2.5f * k;
                var layer = Ui.Rect(rt, "Stack" + k, Vector2.zero, Vector2.one, new Vector2(o, -o), new Vector2(o, -o));
                layer.SetAsFirstSibling();
                var c = Ui.Hex("#5a6468", k == 1 ? .8f : .45f);
                // only the right and bottom edges (outside the tile) show: the others fall under its face
                Ui.Img(Ui.Rect(layer, "R", new Vector2(1, 0), Vector2.one, new Vector2(-1, 0), Vector2.zero), c).raycastTarget = false;
                Ui.Img(Ui.Rect(layer, "B", Vector2.zero, new Vector2(1, 0), Vector2.zero, new Vector2(0, 1)), c).raycastTarget = false;
            }
        }

        /// <summary>Folder: the tile's picture moves to the top-left quarter, the next three fill the others.</summary>
        private static void FolderPictures(TileView v, List<ProgItem> more)
        {
            var pr = v.Pic.rectTransform;
            pr.anchorMin = new Vector2(.04f, .5f); pr.anchorMax = new Vector2(.48f, .96f);
            var slots = new[] { (new Vector2(.52f, .5f), new Vector2(.96f, .96f)), (new Vector2(.04f, .04f), new Vector2(.48f, .48f)), (new Vector2(.52f, .04f), new Vector2(.96f, .48f)) };
            var thumb = (RectTransform)pr.parent;
            for (int i = 0; i < more.Count && i < 3; i++)
            {
                var img = Ui.Img(Ui.Rect(thumb, "Mini", slots[i].Item1, slots[i].Item2, Vector2.zero, Vector2.zero), new Color(1, 1, 1, .85f));
                img.preserveAspect = true; img.enabled = false; img.raycastTarget = false;
                RequestIcon(_icons, more[i].Tpl, 1, img, null);
            }
        }

        /// <summary>The +N (or −) box bottom-right: opens / closes the group (keeping the pick and the scroll).</summary>
        private static readonly Dictionary<string, List<ProgItem>> _groupMembers = new Dictionary<string, List<ProgItem>>();

        private static void CountBox(RectTransform rt, string text, string groupKey, string tip)
        {
            var box = Ui.Rect(rt, "Count", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-38, 5), new Vector2(-5, 21));
            var face = Ui.Img(box, Ui.Hex("#0b0e10", .9f), null, true);
            Ui.Outline(box, Ui.Hex("#aab2b5", .8f));
            ((Graphic)Ui.Label(Ui.Fill(box, "Text"), "Text", text, 11, Ui.Hex("#e4e7e8"), TextAnchor.MiddleCenter, true, 1)).raycastTarget = false;
            var b = box.gameObject.AddComponent<Button>();
            b.targetGraphic = face; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(() =>
            {
                Sounds.Click();
                if (!_openGroups.Remove(groupKey)) _openGroups.Add(groupKey);
                // 1.0.1: opening a group counts as seeing it: NEW goes from all of its items
                if (_groupMembers.TryGetValue(groupKey, out var members)) foreach (var m in members) NewTags.ClearItem(m.Tpl, m.Level);
                // just this category's tiles are rebuilt (the pick and the scroll stay as they are)
                string cat = groupKey.Substring(0, Math.Max(0, groupKey.IndexOf('/')));
                if (_regroup.TryGetValue(cat, out var rebuild))
                {
                    float t0 = Time.realtimeSinceStartup;
                    rebuild();
                    UpdateSelection();
                    L.Debug($"group {groupKey} {(_openGroups.Contains(groupKey) ? "opened" : "closed")} in {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
                    return;
                }
                _keepPickTpl = _featTpl; _keepScrollY = _content != null ? _content.anchoredPosition.y : -1;
                try { ShowLevel(_level, true); }
                finally { _keepPickTpl = null; _keepScrollY = -1; }
            });
            HoverHook.Add(box, on => ShowTip(on ? box : null, tip));
        }
    }
}
