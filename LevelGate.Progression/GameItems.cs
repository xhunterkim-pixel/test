using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using BepInEx;
using UnityEngine;

namespace LevelGate.Progression
{
    /// <summary>
    /// Real game items for the tiles: an Item made by the game's own ItemFactory (a weapon's
    /// default preset when there is one), its icon from ItemViewFactory.LoadItemIcon (the same
    /// picture as the stash / handbook), and Inspect through ItemUiContext. All looked up by
    /// name; what was found (or not) is logged once so the next build can be exact.
    /// </summary>
    internal static class GameItems
    {
        private static bool _init;
        private static Type _itemType;
        private static object _factory;
        private static MethodInfo _create, _preset, _loadIcon;
        private static readonly Dictionary<string, object> _items = new Dictionary<string, object>();
        private static readonly Dictionary<Type, Func<object, Sprite>> _spriteGetters = new Dictionary<Type, Func<object, Sprite>>();
        private static int _made, _failed;
        private static readonly Dictionary<Texture2D, Sprite> _texSprites = new Dictionary<Texture2D, Sprite>();

        /// <summary>1.0.5: the item class lookup ahead of the first open (main-menu warm-up).</summary>
        public static void Warm() => Init();

        private static void Init()
        {
            if (_init) return;
            _init = true;
            var t0 = Time.realtimeSinceStartup;
            try
            {
                _itemType = AccessTools.TypeByName("EFT.InventoryLogic.Item");
                var factoryType = AccessTools.TypeByName("EFT.ItemFactory");
                _factory = SingletonOf(factoryType);
                L.Info($"items: Item type {(_itemType == null ? "MISSING" : "ok")}, ItemFactory {(factoryType == null ? "type missing" : _factory == null ? "not created yet" : "ok")}");
                if (factoryType != null)
                {
                    foreach (var m in factoryType.GetMethods(Refl.All).Where(m => m.Name.Contains("Create") || m.Name.Contains("Preset")))
                        L.Debug($"  ItemFactory.{m.Name}({Sig(m)}) -> {m.ReturnType.Name}");
                    _create = factoryType.GetMethods(Refl.All)
                        .Where(m => m.Name == "CreateItem" && !m.IsStatic && !m.ContainsGenericParameters && m.GetParameters().Length >= 2 && IsId(m.GetParameters()[0].ParameterType) && IsId(m.GetParameters()[1].ParameterType))
                        .OrderBy(m => m.GetParameters().Length).FirstOrDefault();
                    _preset = factoryType.GetMethods(Refl.All)
                        .FirstOrDefault(m => m.Name.Contains("Preset") && !m.ContainsGenericParameters && m.GetParameters().Length == 1 && IsId(m.GetParameters()[0].ParameterType) && _itemType != null && _itemType.IsAssignableFrom(m.ReturnType));
                    _fill = factoryType.GetMethods(Refl.All)
                        .FirstOrDefault(m => m.Name == "CreateAndFillItem" && !m.ContainsGenericParameters && m.GetParameters().Length == 1 && IsId(m.GetParameters()[0].ParameterType));
                    L.Info($"items: create with {(_create == null ? "NOTHING (no icons)" : "ItemFactory." + _create.Name + "(" + Sig(_create) + ")")}, weapon presets with {(_preset == null ? "nothing (bare receivers)" : "ItemFactory." + _preset.Name + "(" + Sig(_preset) + ")")}");
                }

                FindIconCandidates();

                var ui = AccessTools.TypeByName("EFT.UI.ItemUiContext");
                if (ui != null)
                    foreach (var m in ui.GetMethods(Refl.All).Where(m => m.Name.Contains("Inspect") || m.Name.Contains("ContextMenu") || m.Name == "ShowTooltip"))
                        L.Debug($"  ItemUiContext.{m.Name}({Sig(m)})");
                else L.Info("items: EFT.UI.ItemUiContext not found — no inspect");
            }
            catch (Exception e) { L.Error("looking up the game's item classes", e); }
            L.Debug($"item class lookup took {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
        }

        // ---------------------------------------------------------------- icons

        private static readonly List<MethodInfo> _iconCandidates = new List<MethodInfo>();
        private static bool _iconPicked;

        /// <summary>Every method that takes just an Item and gives back something icon-like (name has "Icon", returns a
        /// Sprite / Texture or an object holding one). Tried in order on the first item until one gives a picture.</summary>
        private static void FindIconCandidates()
        {
            if (_itemType == null) return;
            var t0 = Time.realtimeSinceStartup;
            foreach (var t in AccessTools.GetTypesFromAssembly(_itemType.Assembly))
            {
                MethodInfo[] ms;
                try { ms = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); } catch { continue; }
                foreach (var m in ms)
                {
                    if (m.ContainsGenericParameters || m.ReturnType == typeof(void) || m.Name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var ps = m.GetParameters();
                    if (ps.Length < 1 || !ps[0].ParameterType.IsAssignableFrom(_itemType) || ps.Skip(1).Any(p => !p.HasDefaultValue && p.ParameterType != typeof(bool))) continue;
                    if (!IconLike(m.ReturnType)) continue;
                    _iconCandidates.Add(m);
                }
            }
            // the old name first, then short names (the plain loader rather than helpers around it)
            _iconCandidates.Sort((a, b) => (a.Name == "LoadItemIcon" ? 0 : 1).CompareTo(b.Name == "LoadItemIcon" ? 0 : 1) != 0
                ? (a.Name == "LoadItemIcon" ? 0 : 1).CompareTo(b.Name == "LoadItemIcon" ? 0 : 1) : a.Name.Length.CompareTo(b.Name.Length));
            L.Info($"items: {_iconCandidates.Count} possible icon loader(s) found in {(Time.realtimeSinceStartup - t0) * 1000:0} ms: " +
                string.Join(" | ", _iconCandidates.Take(12).Select(m => $"{m.DeclaringType.FullName}.{m.Name}({Sig(m)}) -> {m.ReturnType.Name}").ToArray()));
        }

        private static bool IconLike(Type t)
        {
            if (typeof(Sprite).IsAssignableFrom(t) || typeof(Texture).IsAssignableFrom(t)) return true;
            if (t.IsPrimitive || t == typeof(string)) return false;
            return t.GetProperties(Refl.All).Any(p => p.PropertyType == typeof(Sprite)) || t.GetFields(Refl.All).Any(f => f.FieldType == typeof(Sprite));
        }

        /// <summary>scale: the game's scaleFactor (1 = stash size; 2–3 = a sharper render for big pictures).</summary>
        private static object CallIcon(MethodInfo m, object item, int scale = 1, bool? forced = null)
        {
            var ps = m.GetParameters();
            var args = new object[ps.Length];
            args[0] = item;
            for (int i = 1; i < ps.Length; i++)
                args[i] = ps[i].ParameterType == typeof(int) && ps[i].Name.IndexOf("scale", StringComparison.OrdinalIgnoreCase) >= 0 ? scale
                    // a bigger picture has to be drawn again, not taken from the stash-size cache
                    : ps[i].ParameterType == typeof(bool) && ps[i].Name.IndexOf("forced", StringComparison.OrdinalIgnoreCase) >= 0 ? (object)(forced ?? scale >= 2)
                    : ps[i].HasDefaultValue ? ps[i].DefaultValue : ps[i].ParameterType == typeof(int) ? (object)1 : false;
            return m.Invoke(null, args);
        }

        private static string Sig(MethodInfo m) => string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray());
        private static bool IsId(Type t) => t == typeof(string) || t.Name == "MongoID";

        private static object SingletonOf(Type t)
        {
            if (t == null) return null;
            try
            {
                var st = AccessTools.TypeByName("Comfort.Common.Singleton`1")?.MakeGenericType(t);
                return st?.GetProperty("Instance", Refl.All)?.GetValue(null, null);
            }
            catch (Exception e) { L.Debug($"Singleton<{t.Name}>: {e.GetBaseException().Message}"); return null; }
        }

        /// <summary>A string id as the parameter type wants it (string, or the game's MongoID).</summary>
        private static object Id(Type want, string hex)
        {
            if (want == typeof(string)) return hex;
            var ctor = want.GetConstructor(new[] { typeof(string) });
            if (ctor != null) return ctor.Invoke(new object[] { hex });
            var op = want.GetMethods(BindingFlags.Static | BindingFlags.Public).FirstOrDefault(m => (m.Name == "op_Implicit" || m.Name == "op_Explicit") && m.ReturnType == want && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            if (op != null) return op.Invoke(null, new object[] { hex });
            throw new InvalidOperationException("can't make a " + want.Name + " from a string");
        }

        private static string NewHex()
        {
            var b = Guid.NewGuid().ToByteArray();
            return string.Concat(b.Take(12).Select(x => x.ToString("x2")).ToArray());
        }

        /// <summary>A game item of this template (made once, then reused), or null.</summary>
        public static object ItemOf(string tpl)
        {
            Init();
            if (_items.TryGetValue(tpl, out var have)) return have;
            object item = null;
            if (_factory == null) _factory = SingletonOf(AccessTools.TypeByName("EFT.ItemFactory"));
            if (_factory != null)
            {
                try
                {
                    if (_preset != null && ProgData.GroupOf(tpl) == "Weapons")
                        item = _preset.Invoke(_factory, new[] { Id(_preset.GetParameters()[0].ParameterType, tpl) });
                }
                catch (Exception e) { L.Debug($"preset of {tpl}: {e.GetBaseException().Message}"); }
                try
                {
                    if (item == null && _create != null)
                    {
                        var ps = _create.GetParameters();
                        var args = new object[ps.Length];
                        args[0] = Id(ps[0].ParameterType, NewHex());
                        args[1] = Id(ps[1].ParameterType, tpl);
                        for (int i = 2; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                        item = _create.Invoke(_factory, args);
                    }
                }
                catch (Exception e)
                {
                    if (_failed++ < 5) L.Warn($"couldn't make item {tpl}: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
                }
            }
            if (item != null && _made++ == 0) L.Info($"items: first game item made ({item.GetType().Name} for {tpl})");
            _items[tpl] = item;
            return item;
        }

        /// <summary>The game's icon object for an item (its sprite may arrive a few frames later), or null.</summary>
        public static object IconOf(object item, int scale = 1)
        {
            if (item == null) return null;
            if (_loadIcon != null)
            {
                try
                {
                    // already being drawn at this size (a card and the pre-loader asking for the same picture): share that request
                    // instead of having the game draw it again — 0.9.70 logs showed ~11% of all draws were such repeats
                    string key = scale != 1 ? Refl.Get(item, "TemplateId")?.ToString() + "@" + scale : null;
                    if (key != null && _inflight.TryGetValue(key, out var inf) && inf != null && _pending.ContainsKey(inf)) return inf;
                    if (scale != 1) { _scaled.Add(item); L.Debug($"icon: asking the game for {Refl.Get(item, "TemplateId")} ({ProgData.GroupOf(Refl.Get(item, "TemplateId")?.ToString() ?? "")}) at {scale}x"); }
                    L.Step($"icon: {Refl.Get(item, "TemplateId") ?? item.GetType().Name} at {scale}x");
                    var before = scale != 1 ? SpriteOf(CallIcon(_loadIcon, item, 1, false)) : null; // what the stash has now
                    var icon = CallIcon(_loadIcon, item, scale);
                    if (scale != 1 && icon != null)
                    {
                        // what a render at this scale should measure on its long side (the game draws 64 px per cell at 1x)
                        var (w, h) = CellsOf(item);
                        float expect = PxPerCell * Mathf.Max(w, h) * scale;
                        _pending[icon] = (before, Time.realtimeSinceStartup + 6f, item, scale, expect);
                        _inflight[key] = icon;
                    }
                    return icon;
                }
                catch (Exception e) { L.ErrorOnce("icon loader", e); return null; }
            }
            if (_iconPicked) return null;
            // first item: try the candidates until one gives back an icon object
            _iconPicked = true;
            foreach (var m in _iconCandidates)
            {
                try
                {
                    L.Step($"icon: trying {m.DeclaringType.Name}.{m.Name} on {Refl.Get(item, "TemplateId") ?? item.GetType().Name}");
                    var icon = CallIcon(m, item);
                    if (icon == null) { L.Debug($"icon loader {m.DeclaringType.Name}.{m.Name}: returned nothing"); continue; }
                    _loadIcon = m;
                    L.Info($"items: icons from {m.DeclaringType.FullName}.{m.Name}({Sig(m)}) -> {icon.GetType().FullName}");
                    L.Debug("  icon object members: " + string.Join(", ", icon.GetType().GetProperties(Refl.All).Select(p => p.Name + ":" + p.PropertyType.Name)
                        .Concat(icon.GetType().GetFields(Refl.All).Select(f => f.Name + ":" + f.FieldType.Name)).Take(40).ToArray()));
                    // the probe asked at stash size; a bigger request goes through the normal path (else the session's
                    // first picture stayed a stretched 64 px icon)
                    return scale != 1 ? IconOf(item, scale) : icon;
                }
                catch (Exception e) { L.Debug($"icon loader {m.DeclaringType.Name}.{m.Name} failed: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}"); }
            }
            L.Warn($"items: none of the {_iconCandidates.Count} icon loaders worked — tiles show names. The list is above.");
            return null;
        }

        // The game keeps ONE cached icon per item look (shared with the stash): a bigger render made here replaces
        // the stash's picture too. Every item drawn bigger is remembered and drawn again at stash size on close.
        private static readonly HashSet<object> _scaled = new HashSet<object>();

        /// <summary>Puts the game's cached icons back to stash size (call when the screen closes).</summary>
        public static void RestoreIcons()
        {
            if (_loadIcon == null || _scaled.Count == 0) return;
            var items = _scaled.ToList();
            int n = 0, fine = 0;
            var t0 = DateTime.Now;
            foreach (var item in items)
            {
                // 1.0.1: only the ones whose stash icon is still the big render. Redrawing every item ever shown big (300+, on
                // every close, growing all session) made the stash and traders reload their icons (the user's 1.0.0 log)
                if (_pending.Values.Any(p => ReferenceEquals(p.Item, item))) continue; // still arriving: handled when it lands
                if (!StillBig(item)) { _scaled.Remove(item); fine++; continue; }
                L.Step($"icon: back to 1x {Refl.Get(item, "TemplateId") ?? item.GetType().Name}");
                try { CallIcon(_loadIcon, item, 1, true); n++; _scaled.Remove(item); }
                catch (Exception e) { L.ErrorOnce("restoring an icon to stash size", e); }
            }
            L.Info($"items: {n} icon(s) drawn again at stash size, {fine} already fine ({(DateTime.Now - t0).TotalMilliseconds:0} ms)");
        }

        /// <summary>Is the game's cached (stash) icon of this item still a big render? Reads what is cached, draws nothing new.</summary>
        private static bool StillBig(object item)
        {
            try
            {
                var sp = SpriteOf(CallIcon(_loadIcon, item, 1, false));
                if (sp == null) return false;
                var (w, h) = CellsOf(item);
                float expect = PxPerCell * Mathf.Max(w, h);
                return Mathf.Max(sp.rect.width, sp.rect.height) > expect * 1.3f;
            }
            catch { return true; }
        }

        // A bigger render goes into the game's one shared icon cache (the stash uses it too). So as soon as it arrives it is
        // copied into a picture of our own, and the game's icon is drawn again at stash size right away.
        private static readonly Dictionary<object, (Sprite Before, float Deadline, object Item, int Scale, float Expect)> _pending = new Dictionary<object, (Sprite, float, object, int, float)>();

        /// <summary>The game's icon size per inventory cell at scale 1 (measured: a 1×1 item is 64×64, 6x gives 384×384).</summary>
        public const float PxPerCell = 64;

        /// <summary>An item's size in cells (from its template), at least 1×1.</summary>
        public static (int W, int H) CellsOf(object item)
        {
            try
            {
                // the whole item as the stash shows it: a weapon preset with its stock, barrel… (its template alone is just the
                // receiver: an SA58 read as 2×1 instead of 5×2 — wrong size shown, and big renders asked for at the wrong scale)
                var m = item == null ? null : AccessTools.Method(item.GetType(), "CalculateCellSize", Type.EmptyTypes);
                if (m != null)
                {
                    var size = m.Invoke(item, null);
                    if (Refl.Get(size, "X") is int x && Refl.Get(size, "Y") is int y && x > 0 && y > 0) return (x, y);
                }
            }
            catch (Exception e) { L.ErrorOnce("item cell size", e); }
            try
            {
                var t = Refl.Get(item, "Template");
                int w = Refl.Get(t, "Width") is int a ? a : 1, h = Refl.Get(t, "Height") is int b ? b : 1;
                return (Mathf.Max(1, w), Mathf.Max(1, h));
            }
            catch { return (1, 1); }
        }

        /// <summary>The whole item's weight (a weapon preset with its parts), or null.</summary>
        public static float? WeightOf(object item)
        {
            try
            {
                var m = item == null ? null : AccessTools.Method(item.GetType(), "GetSingleItemTotalWeight", Type.EmptyTypes);
                if (m != null && m.Invoke(item, null) is float kg && kg > 0) return kg;
                if (Refl.Get(item, "TotalWeight") is float tw && tw > 0) return tw;
            }
            catch (Exception e) { L.ErrorOnce("item weight", e); }
            return null;
        }

        /// <summary>The render scale that gives about targetPx on the item's long side (so a 1×1 box of ammo gets as many
        /// real pixels as a rifle), clamped to 2–6x and to 2048 px.</summary>
        /// <summary>Card pictures: stash size when that's already big enough (rifles, backpacks), 2–3x for small items.</summary>
        public static int CardScale(string tpl, float targetPx)
        {
            var (w, h) = CellsOf(ItemOf(tpl));
            return Mathf.Clamp(Mathf.CeilToInt(targetPx / (PxPerCell * Mathf.Max(w, h))), 1, 3);
        }

        public static int ScaleFor(string tpl, float targetPx)
        {
            var (w, h) = CellsOf(ItemOf(tpl));
            float basePx = PxPerCell * Mathf.Max(w, h);
            int scale = Mathf.CeilToInt(targetPx / basePx);
            scale = Mathf.Min(scale, Mathf.FloorToInt(2048f / basePx));
            return Mathf.Clamp(scale, 2, 6); // 8x was the heaviest request (one took 426 ms and still came back small)
        }
        private static readonly Dictionary<string, Sprite> _copies = new Dictionary<string, Sprite>();
        private static readonly Queue<string> _copyOrder = new Queue<string>();
        private static readonly Dictionary<string, object> _inflight = new Dictionary<string, object>();
        private static readonly Dictionary<object, (Sprite Copy, float At)> _madeFrom = new Dictionary<object, (Sprite, float)>();

        /// <summary>Our own copy of a bigger render of this item, if we made one already.</summary>
        public static Sprite CopyOf(string tpl, int scale) => _copies.TryGetValue(tpl + "@" + scale, out var sp) && sp != null && sp.texture != null ? sp : null;

        /// <summary>The picture to show for an icon: normal ones as they are; bigger ones copied (then the game's is put back).</summary>
        /// <summary>done = false: a stand-in (the stash-size picture) while the bigger one is still being drawn — show it and ask again.</summary>
        public static Sprite TakeSprite(object icon, string tpl, out bool done)
        {
            done = false;
            var sp = SpriteOf(icon);
            if (sp == null) return null;
            if (icon == null || !_pending.TryGetValue(icon, out var p))
            {
                BloomColor(sp); // its glow colour read now (in the background), ready before it's picked
                // a shared request another asker already finished: the copy made from it (the game's own is back at stash size)
                if (icon != null && _madeFrom.TryGetValue(icon, out var mf) && mf.Copy != null && mf.Copy.texture != null && Time.realtimeSinceStartup - mf.At < 10f) { done = true; return mf.Copy; }
                done = true; return sp;
            }
            // only the render we asked for counts: at least ~70% of the expected size. The first picture to arrive is often
            // the game's stash-size one (64 px a cell) — taking that one made the big preview a stretched thumbnail.
            float longSide = Mathf.Max(sp.rect.width, sp.rect.height);
            bool bigEnough = sp != p.Before && longSide >= p.Expect * .7f;
            if (!bigEnough && Time.realtimeSinceStartup < p.Deadline) return sp; // stand-in, not done
            _pending.Remove(icon);
            done = true;
            if (!bigEnough)
            {
                // gave up waiting: show what there is, but don't keep it (next time asks again), and put the stash's back
                L.Debug($"icon: {tpl} came back {sp.rect.width:0}x{sp.rect.height:0}, expected ~{p.Expect:0} px at {p.Scale}x — shown, not kept");
                try { CallIcon(_loadIcon, p.Item, 1, true); } catch (Exception e) { L.ErrorOnce("putting an icon back to stash size", e); }
                return sp;
            }
            var copy = Copy(sp);
            if (copy != null)
            {
                _madeFrom[icon] = (copy, Time.realtimeSinceStartup);
                if (_madeFrom.Count > 300) foreach (var k in _madeFrom.Where(x => Time.realtimeSinceStartup - x.Value.At > 10f).Select(x => x.Key).ToList()) _madeFrom.Remove(k);
            }
            try { CallIcon(_loadIcon, p.Item, 1, true); } catch (Exception e) { L.ErrorOnce("putting an icon back to stash size", e); }
            if (copy == null) return sp;
            var key = tpl + "@" + p.Scale;
            // drawn twice (asked for again before the first came back): keep the first, drop the new one. 0.9.66 and older
            // queued the key twice, and the first entry's turn to go deleted the newer copy — a white box where it showed.
            if (_copies.TryGetValue(key, out var had) && had != null && had.texture != null)
            {
                UnityEngine.Object.Destroy(copy.texture);
                _madeFrom[icon] = (had, Time.realtimeSinceStartup);
                L.Debug($"icon: {tpl} at {p.Scale}x was already kept — using that one");
                return had;
            }
            _copies[key] = copy;
            _copyOrder.Enqueue(key);
            Trim(key);
            L.Debug($"icon: kept a {copy.rect.width:0}x{copy.rect.height:0} copy of {tpl} and put the game's back to stash size");
            BloomColor(copy); // its glow colour read now (in the background; the loading screen does most), ready before it's picked
            return copy;
        }

        /// <summary>Set by the screen: every picture it shows right now (those are never deleted).</summary>
        internal static Func<HashSet<Sprite>> Shown;
        /// <summary>Set by the screen: take these pictures off every image (they're about to be deleted).</summary>
        internal static Action<HashSet<Sprite>> Dropping;

        /// <summary>
        /// Keeps at most ~150 copies (the pages around the current one are kept ready too): the oldest go first, but never
        /// one that's on screen (a deleted texture draws as a white box) — those wait for the next round.
        /// </summary>
        private static void Trim(string keep)
        {
            if (_copyOrder.Count <= 150) return;
            HashSet<Sprite> shown = null;
            try { shown = Shown?.Invoke(); } catch (Exception e) { L.ErrorOnce("pictures on screen", e); }
            var back = new List<string>();
            int guard = _copyOrder.Count;
            while (_copyOrder.Count > 150 && guard-- > 0)
            {
                var old = _copyOrder.Dequeue();
                if (!_copies.TryGetValue(old, out var o) || o == null) { _copies.Remove(old); continue; }
                if (old == keep || (shown != null && shown.Contains(o))) { back.Add(old); continue; }
                UnityEngine.Object.Destroy(o.texture);
                _copies.Remove(old);
            }
            foreach (var k in back) _copyOrder.Enqueue(k);
        }

        private static readonly Dictionary<Sprite, Color> _bloomColors = new Dictionary<Sprite, Color>();

        /// <summary>
        /// The colour an item's picture glows in: its picture shrunk to 16x16 on the GPU and read back WITHOUT waiting
        /// (AsyncGPUReadback: 0.9.71–0.9.74 read it back at once, which stalled the frame — most of the "Feature" slow
        /// frames in the 0.9.74 log). Opaque, colourful pixels count most (a red item blooms red), then brightened for
        /// light; mostly grey: a neutral warm light. Null until the answer is in (asked again next time), cached per picture.
        /// </summary>
        public static Color? BloomColor(Sprite sp)
        {
            if (sp == null || sp.texture == null) return null;
            if (_bloomColors.TryGetValue(sp, out var have)) return have;
            if (_bloomAsked.Contains(sp)) return null; // on its way
            try
            {
                const int n = 16;
                var src = sp.texture; var r = sp.textureRect;
                var rt = RenderTexture.GetTemporary(n, n, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt, new Vector2(r.width / src.width, r.height / src.height), new Vector2(r.x / src.width, r.y / src.height));
                _bloomAsked.Add(sp);
                UnityEngine.Rendering.AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                {
                    try
                    {
                        _bloomAsked.Remove(sp);
                        if (req.hasError || sp == null) return;
                        var data = req.GetData<Color32>();
                        if (_bloomColors.Count > 600) _bloomColors.Clear();
                        _bloomColors[sp] = ColorFrom(data);
                    }
                    catch (Exception e) { L.ErrorOnce("item bloom colour (read)", e); }
                    finally { RenderTexture.ReleaseTemporary(rt); }
                });
                return null;
            }
            catch (Exception e) { L.ErrorOnce("item bloom colour", e); return null; }
        }

        private static readonly HashSet<Sprite> _bloomAsked = new HashSet<Sprite>();

        private static Color ColorFrom(Unity.Collections.NativeArray<Color32> px)
        {
            float wr = 0, wg = 0, wb = 0, wsum = 0, satSum = 0, aSum = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float a = c.a / 255f; if (a < .2f) continue;
                Color.RGBToHSV(new Color32(c.r, c.g, c.b, 255), out _, out float sat, out float val);
                float w = a * (.15f + sat * sat * 3f) * (.3f + val);
                wr += c.r / 255f * w; wg += c.g / 255f * w; wb += c.b / 255f * w; wsum += w; satSum += sat * a; aSum += a;
            }
            if (wsum <= 0 || aSum <= 0) return new Color(1f, .93f, .82f);
            var avg = new Color(wr / wsum, wg / wsum, wb / wsum);
            Color.RGBToHSV(avg, out float h, out float s2, out float v);
            float colourful = satSum / aSum;
            // 35% less saturated than 0.9.71–0.9.78 (asked for: the item colours were too strong behind the pictures)
            return colourful < .12f ? new Color(1f, .93f, .82f) : Color.HSVToRGB(h, Mathf.Clamp01((s2 * 1.3f + .1f) * .65f), Mathf.Max(v, .85f));
        }

        private static Sprite Copy(Sprite sp)
        {
            RenderTexture rt = null, prev = RenderTexture.active;
            try
            {
                var src = sp.texture;
                var r = sp.textureRect;
                rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                // whole pixels only (the game's sprite rects are fractional, e.g. 293.85 wide)
                int x = Mathf.RoundToInt(r.x), y = Mathf.RoundToInt(r.y);
                int w = Mathf.Clamp(Mathf.FloorToInt(r.width), 1, src.width - x), h = Mathf.Clamp(Mathf.FloorToInt(r.height), 1, src.height - y);
                // with mipmaps + trilinear: a big render shown smaller (a rifle in a card) stays clean instead of shimmering
                // mip bias -0.5: shown a little smaller than drawn (fit to the stage), it stays crisp instead of blending in the
                // half-size copy
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, mipMapBias = -.5f };
                dst.ReadPixels(new Rect(x, y, w, h), 0, 0);
                dst.Apply(true, true); // mipmaps built, no CPU copy kept
                return Sprite.Create(dst, new Rect(0, 0, w, h), new Vector2(.5f, .5f), sp.pixelsPerUnit);
            }
            catch (Exception e) { L.ErrorOnce("copying an icon", e); return null; }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        // "Fix stash icons": every item on the level list drawn again at stash size (forced), a few per frame, to clear
        // big icons an older build left in the game's icon cache.
        private static readonly Queue<string> _repair = new Queue<string>();
        public static int RepairLeft => _repair.Count;
        public static int RepairTotal;
        private static bool _repairPaused;

        private static readonly HashSet<string> _repairQueued = new HashSet<string>();
        private static bool _announce;
        private static int _announcedAt;

        /// <summary>Queues items to be drawn again at stash size. announce: progress + "done" shown on screen (Toast).</summary>
        public static void RepairAll(IEnumerable<string> tpls, bool announce = false)
        {
            if (_repair.Count == 0) { RepairTotal = 0; _repairQueued.Clear(); }
            int added = 0;
            foreach (var t in tpls) if (t != null && _repairQueued.Add(t)) { _repair.Enqueue(t); added++; }
            RepairTotal += added;
            _announce |= announce;
            L.Info($"items: redrawing {added} icons at stash size ({_repair.Count} queued)");
            if (_announce) Toast.Show(_repair.Count == 0 ? "Item icons refreshed" : $"Refreshing item icons…  0 / {RepairTotal}");
        }

        /// <summary>Items this session drew bigger than stash size (their stash icons are the ones worth redrawing).</summary>
        public static List<string> ScaledTpls() => _scaled.Select(i => Refl.Get(i, "TemplateId")?.ToString()).Where(t => t != null).Distinct().ToList();

        /// <summary>Forgets every bigger picture we kept, so the screen asks the game for fresh ones (new graphics quality).</summary>
        public static int ClearCopies()
        {
            int n = 0;
            // the screen lets go of them first: an image left pointing at a deleted texture shows whatever the GPU puts
            // there next (another item, the emblem sheet, a glow) — the "wrong pictures" of 0.9.66
            var all = new HashSet<Sprite>(_copies.Values.Where(x => x != null));
            try { Dropping?.Invoke(all); } catch (Exception e) { L.ErrorOnce("letting go of pictures", e); }
            foreach (var sp in all) { UnityEngine.Object.Destroy(sp.texture); n++; }
            _copies.Clear(); _copyOrder.Clear(); _pending.Clear(); _inflight.Clear(); _madeFrom.Clear();
            L.Info($"items: {n} kept picture(s) cleared");
            return n;
        }

        public static void RepairTick()
        {
            if (_repair.Count == 0 || _loadIcon == null) return;
            // only on the main menu itself with nothing else going on: forcing icon redraws while the stash / traders /
            // flea draw their own (and screens switch fast) crashed the game once
            if (!MenuHook.QuietMenu())
            {
                if (!_repairPaused) { _repairPaused = true; L.Info($"items: icon redraw paused ({_repair.Count} left) — continues on the main menu"); }
                if (_announce && !ProgScreen.IsOpen) Toast.Show($"Refreshing item icons: paused ({RepairTotal - _repair.Count} / {RepairTotal}) — continues on the main menu", 1f);
                return;
            }
            if (_repairPaused) { _repairPaused = false; L.Info($"items: icon redraw continues ({_repair.Count} left)"); }
            int budget = 2; // a couple per frame
            while (_repair.Count > 0 && budget-- > 0)
            {
                var tpl = _repair.Dequeue();
                var item = ItemOf(tpl);
                if (item == null) continue;
                try { CallIcon(_loadIcon, item, 1, true); } catch (Exception e) { L.ErrorOnce("redrawing an icon", e); }
            }
            int done = RepairTotal - _repair.Count;
            if (_repair.Count > 0 && _repair.Count % 100 == 0) L.Debug($"items: icon redraw {done} / {RepairTotal}");
            if (_announce && _repair.Count > 0 && done - _announcedAt >= 10) { _announcedAt = done; Toast.Show($"Refreshing item icons…  {done} / {RepairTotal}", 2f); }
            if (_repair.Count == 0)
            {
                L.Info($"items: {RepairTotal} icons redrawn at stash size");
                if (_announce) Toast.Show($"Item icons refreshed  ✓  ({RepairTotal})", 4f);
                _announce = false; _announcedAt = 0; _repairQueued.Clear();
            }
        }

        public static Sprite SpriteOf(object icon)
        {
            if (icon == null) return null;
            if (icon is Sprite s) return s;
            if (icon is Texture2D tex) return _texSprites.TryGetValue(tex, out var ts) ? ts : _texSprites[tex] = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(.5f, .5f));
            var t = icon.GetType();
            if (!_spriteGetters.TryGetValue(t, out var get))
            {
                var p = t.GetProperties(Refl.All).FirstOrDefault(x => x.PropertyType == typeof(Sprite) && x.GetIndexParameters().Length == 0);
                var f = p == null ? t.GetFields(Refl.All).FirstOrDefault(x => x.FieldType == typeof(Sprite)) : null;
                get = p != null ? (Func<object, Sprite>)(o => p.GetValue(o, null) as Sprite) : f != null ? o => f.GetValue(o) as Sprite : (Func<object, Sprite>)(o => null);
                _spriteGetters[t] = get;
                L.Info($"items: icon picture read from {t.Name}.{p?.Name ?? f?.Name ?? "NOTHING (no Sprite member)"}");
            }
            try { return get(icon); } catch { return null; }
        }

        // ---------------------------------------------------------------- inspect

        private static List<MethodInfo> _inspect;
        private static List<ConstructorInfo> _ctxCtors;

        private static readonly object _ctxLock = new object();

        /// <summary>Every constructor of a concrete ItemContext-like class that takes the item. Searching the game's
        /// assembly takes ~0.8 s, so <see cref="WarmInspect"/> does it on a worker thread when the screen opens
        /// (0.9.94 log: an 848 ms hitch on the first right-click).</summary>
        private static List<ConstructorInfo> CtxCtors(Type want)
        {
            lock (_ctxLock)
            {
                if (_ctxCtors != null) return _ctxCtors;
                var t0 = DateTime.UtcNow;
                var list = new List<ConstructorInfo>();
                foreach (var t in AccessTools.GetTypesFromAssembly(want.Assembly))
                {
                    if (t.IsAbstract || t.ContainsGenericParameters || !want.IsAssignableFrom(t)) continue;
                    foreach (var c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (c.GetParameters().Any(p => p.ParameterType.IsAssignableFrom(_itemType))) list.Add(c);
                }
                _ctxCtors = list.OrderBy(c => c.GetParameters().Length).ToList();
                L.Info($"inspect: {_ctxCtors.Count} way(s) to make a {want.Name} (looked up in {(DateTime.UtcNow - t0).TotalMilliseconds:0} ms): " + string.Join(" | ", _ctxCtors.Take(10).Select(c => $"{c.DeclaringType.Name}({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray())})").ToArray()));
                return _ctxCtors;
            }
        }

        private static bool _warmStarted;
        /// <summary>Finds the inspect method and the ItemContext constructors on a worker thread, so the first
        /// right-click / Inspect doesn't freeze the game. Safe to call every time the screen opens.</summary>
        public static void WarmInspect()
        {
            if (_warmStarted) return;
            _warmStarted = true;
            try
            {
                Init();
                if (_itemType == null) return;
                var uiType = AccessTools.TypeByName("EFT.UI.ItemUiContext");
                var want = uiType?.GetMethods(Refl.All).Where(m => m.Name == "Inspect" && !m.ContainsGenericParameters && m.GetParameters().Length >= 1)
                    .Select(m => m.GetParameters()[0].ParameterType).FirstOrDefault(pt => !pt.IsInstanceOfType(null) && !pt.IsAssignableFrom(_itemType));
                if (want == null) return;
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { CtxCtors(want); } catch (Exception e) { L.Debug($"inspect: warm-up failed ({e.GetBaseException().Message}); will look up on first inspect"); }
                });
            }
            catch (Exception e) { L.Debug($"inspect: warm-up skipped ({e.GetBaseException().Message})"); }
        }

        /// <summary>An ItemContext for a loose item: any concrete class of that kind with a constructor taking the item
        /// (other arguments: first enum value, false, null). What was tried is logged.</summary>
        private static object ContextFor(Type want, object item)
        {
            var ctors = CtxCtors(want);
            foreach (var c in ctors)
            {
                var ps = c.GetParameters();
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                {
                    var pt = ps[i].ParameterType;
                    if (pt.IsAssignableFrom(_itemType)) args[i] = item;
                    else if (pt.IsEnum)
                    {
                        // a view type that fits a read-only look at the item
                        var names = Enum.GetNames(pt);
                        var pick = names.FirstOrDefault(n => n.IndexOf("Handbook", StringComparison.OrdinalIgnoreCase) >= 0)
                            ?? names.FirstOrDefault(n => n.IndexOf("Inspect", StringComparison.OrdinalIgnoreCase) >= 0) ?? names.FirstOrDefault();
                        args[i] = pick != null ? Enum.Parse(pt, pick) : Activator.CreateInstance(pt);
                    }
                    else args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : pt.IsValueType ? Activator.CreateInstance(pt) : null;
                }
                try
                {
                    var ctx = c.Invoke(args);
                    L.Debug($"inspect: made {c.DeclaringType.Name} ({string.Join(", ", args.Select(a => a?.ToString() ?? "null").ToArray())})");
                    return ctx;
                }
                catch (Exception e) { L.Debug($"inspect: {c.DeclaringType.Name} ctor failed: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}"); }
            }
            return null;
        }

        /// <summary>Opens the game's item inspect window. Tries every ItemUiContext.Inspect* that can take just the item.</summary>
        /// <summary>A few facts from the item's template (size, weight, damage…) for the details panel.</summary>
        // 1.0.0: an item's facts / attributes never change: worked out once per item (they were redone, by reflection, on every
        // pick — the "slow frame … after: Feature" hitches). Callers get a copy (they edit their list).
        private static readonly Dictionary<string, List<(string Label, string Value)>> _factsCache = new Dictionary<string, List<(string, string)>>();
        private static readonly Dictionary<string, List<(string Name, string Value, object Id)>> _attrCache = new Dictionary<string, List<(string, string, object)>>();

        public static List<(string Label, string Value)> Facts(string tpl)
        {
            if (tpl == null) return new List<(string, string)>();
            if (!_factsCache.TryGetValue(tpl, out var have)) _factsCache[tpl] = have = FactsOf(tpl);
            return new List<(string, string)>(have);
        }

        public static List<(string Name, string Value, object Id)> GameAttributes(string tpl)
        {
            if (tpl == null) return new List<(string, string, object)>();
            if (!_attrCache.TryGetValue(tpl, out var have)) _attrCache[tpl] = have = GameAttributesOf(tpl);
            return new List<(string, string, object)>(have);
        }

        private static List<(string Label, string Value)> FactsOf(string tpl)
        {
            var list = new List<(string, string)>();
            try
            {
                var item = ItemOf(tpl);
                var t = Refl.Get(item, "Template");
                if (t == null) return list;
                object Num(params string[] names) { foreach (var n in names) { var v = Refl.Get(t, n); if (v != null && !(v is string s && s.Length == 0)) return v; } return null; }
                var (cw, ch) = CellsOf(item); // the whole item (weapons: the preset, not just its receiver)
                list.Add(("Size", $"{cw} × {ch}"));
                if ((WeightOf(item) ?? (Num("Weight") as float?)) is float kg) list.Add(("Weight", $"{kg:0.###}<size=65%> kg</size>"));
                if (Num("Damage") is int dmg && dmg > 0) list.Add(("Damage", dmg.ToString()));
                if (Num("PenetrationPower") is int pen && pen > 0) list.Add(("Penetration", pen.ToString()));
                // armor class: the item's own, else the best of its plates (armored rigs / armor carry their protection in plate
                // slots; LevelGate sets the carriers' own class to 0 — read only, LevelGate is untouched)
                int acOwn = ClassOf(t);
                string grp = ProgData.GroupOf(tpl);
                bool armored = grp == "Armor" || grp == "Rigs" || grp == "Headwear"; // helmets: their armor parts
                // the game data on disk first (LevelGate sets the class to 0 in what the server sends; the file is untouched)
                int acDisk = armored ? OriginalArmor.Get(tpl) : 0;
                int acBest = acDisk > 0 ? acDisk : acOwn > 0 ? acOwn : armored && !OriginalArmor.Ready ? PlateClass(tpl, item) : 0;
                if (acBest > 0) list.Add(("Armor class", acBest.ToString()));
                if (Num("ammoCaliber", "AmmoCaliber", "Caliber", "caliber") is string cal && cal.Length > 0) list.Add(("Caliber", CaliberName(cal)));
                if (Num("MaxHpResource") is int hp && hp > 0) list.Add(("Resource", hp.ToString()));
                // food & drink: what it gives (the template's health effects: Energy / Hydration)
                foreach (var (key, label) in new[] { ("Energy", "Energy"), ("Hydration", "Hydration") })
                    if (HealthEffect(t, key) is double fx && Math.Abs(fx) >= .5) list.Add((label, $"{fx:+0;-0}"));
                // weapons: the template's own handling numbers (base weapon, before mods)
                double? D(object v) { try { return v == null || v is string || v is bool ? (double?)null : Convert.ToDouble(v); } catch { return null; } }
                // bolt-actions: "30 rpm" read like a bug — the action instead
                bool bolt = Num("BoltAction") is bool ba && ba;
                // semi-autos (a Desert Eagle's template rate is low) read "Semi-auto", not "Bolt action": only the game's own flag says bolt
                if (D(Num("bFirerate")) is double rpm && rpm > 0)
                    list.Add(("Fire rate", bolt ? "Bolt<size=60%> action</size>" : rpm < 100 ? "Semi-auto" : $"{rpm:0}<size=60%> rpm</size>"));
                if (D(Num("Ergonomics")) is double ergo && ergo > 0) list.Add(("Ergonomics", $"{ergo:0}"));
                if (D(Num("RecoilForceUp")) is double rec && rec > 0) list.Add(("Recoil", $"{rec:0}"));
                if (D(Num("bEffDist")) is double eff && eff > 0) list.Add(("Eff. range", $"{eff:0}<size=65%> m</size>"));
                // gear (only what the template has, not zero): armor, rigs, backpacks, headwear
                double durNow = D(Num("MaxDurability", "Durability")) ?? 0;
                if (durNow <= 0 && armored) durNow = OriginalArmor.Durability(tpl); // helmets / carriers: from their armor parts on disk
                if (durNow > 0 && !(Num("bFirerate") != null)) list.Add(("Durability", $"{durNow:0}"));
                if (Num("ArmorMaterial") is object mat && mat.ToString() is string ms && ms.Length > 0 && ms != "None") list.Add(("Material", Spaced(ms)));
                int cap = Capacity(t);
                if (cap <= 0) // rigs / backpacks whose grids we can't count: the game's own CONTAINER SIZE row (what its inspect shows)
                    foreach (var (_, value, id) in GameAttributes(tpl))
                        if (id?.ToString() == "ContainerSize" && int.TryParse(System.Text.RegularExpressions.Regex.Match(value ?? "", @"\d+").Value, out int gc) && gc > 0) { cap = gc; break; }
                if (cap > 0) list.Add(("Container size", cap.ToString())); // how many cells it holds, as the game's inspect says it
                // penalties coloured like the game's inspect: red when it costs you, blue when it helps
                string Tone(double v, string text) => $"<color={(v < 0 ? "#e0473a" : "#54c1ff")}>{text}</color>";
                if (D(Num("speedPenaltyPercent")) is double sp && Math.Abs(sp) >= .05) list.Add(("Movement", Tone(sp, $"{sp:+0.#;-0.#}<size=65%>%</size>")));
                if (D(Num("mousePenalty")) is double mp && Math.Abs(mp) >= .05) list.Add(("Turning", Tone(mp, $"{mp:+0.#;-0.#}<size=65%>%</size>")));
                if (D(Num("weaponErgonomicPenalty")) is double ep && Math.Abs(ep) >= .05) list.Add(("Ergo penalty", Tone(ep, $"{ep:+0.#;-0.#}")));
            }
            catch (Exception e) { L.ErrorOnce("item facts", e); }
            return list;
        }

        /// <summary>A weapon's kind from its template's weapClass ("assaultRifle" → "Assault rifle"), for modded guns the handbook
        /// doesn't list; null if it has none.</summary>
        public static string WeaponClassOf(string tpl)
        {
            try
            {
                var c = Refl.Get(Refl.Get(ItemOf(tpl), "Template"), "weapClass")?.ToString();
                switch ((c ?? "").ToLowerInvariant())
                {
                    case "assaultrifle": return "Assault rifle";
                    case "assaultcarbine": return "Assault carbine";
                    case "smg": return "Submachine gun";
                    case "pistol": return "Pistol";
                    case "marksmanrifle": return "Marksman rifle";
                    case "sniperrifle": return "Sniper rifle";
                    case "shotgun": return "Shotgun";
                    case "machinegun": return "Machine gun";
                    case "grenadelauncher": return "Grenade launcher";
                    case "specialweapon": return "Special weapon";
                    default: return null;
                }
            }
            catch { return null; }
        }

        private static readonly HashSet<string> _attrDumped = new HashSet<string>();

        /// <summary>
        /// The item's own inspect rows, as the game's inspect window lists them (grenades: EXPLOSION DELAY, FRAGMENTS COUNT…;
        /// meds / stims: USE TIME, SKILL "ATTENTION"  Dur. 240sec (+30), HANDS TREMOR…): name, value and the attribute id (for
        /// its icon). Read from item.Attributes; the first item of each category is dumped to the log (verbose).
        /// </summary>
        private static List<(string Name, string Value, object Id)> GameAttributesOf(string tpl)
        {
            var list = new List<(string, string, object)>();
            try
            {
                var item = ItemOf(tpl);
                if (!(Refl.Get(item, "Attributes") is System.Collections.IEnumerable attrs)) return list;
                string grp = ProgData.GroupOf(tpl);
                bool dump = L.Verbose && _attrDumped.Add(grp ?? "");
                foreach (var a in attrs)
                {
                    if (a == null) continue;
                    var id = Refl.Get(a, "Id");
                    string raw = Text(Refl.Get(a, "DisplayName")) ?? Text(Refl.Get(a, "Name")) ?? id?.ToString();
                    string name = raw;
                    var loc = string.IsNullOrEmpty(raw) ? null : ProgData.Localize(raw);
                    if (!string.IsNullOrEmpty(loc) && loc != raw) name = loc;
                    string value = Text(Refl.Get(a, "StringValue")) ?? Text(Refl.Get(a, "FullStringValue"));
                    if (string.IsNullOrEmpty(value) && Refl.Get(a, "Base") is Delegate bf)
                        try { var b = bf.DynamicInvoke(); if (b is float f) value = f.ToString("0.##"); } catch { }
                    if (dump) L.Debug($"attributes of {tpl} ({grp}): id {id} ({id?.GetType().Name}), name '{raw}' → '{name}', value '{value}'");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value) || id?.ToString() == "Weight" || id?.ToString() == "Undefined") continue;
                    list.Add((name.Trim(), value.Trim(), id));
                }
            }
            catch (Exception e) { L.ErrorOnce("item attributes", e); }
            return list;
        }

        /// <summary>A string, or what a no-argument string function returns.</summary>
        private static string Text(object o)
        {
            if (o is string s) return s;
            if (o is Func<string> f) { try { return f(); } catch { return null; } }
            if (o is Delegate d && d.Method.ReturnType == typeof(string) && d.Method.GetParameters().Length == 0) { try { return d.DynamicInvoke() as string; } catch { return null; } }
            return null;
        }

        /// <summary>"Caliber366TKM" → the game's own name if it has one, else a readable form: ".366 TKM", "5.56x45 NATO", "9x19 PARA".</summary>
        /// <summary>The highest armor class among an item's parts (its default plates), 0 if none: from its preset when it has one.</summary>
        private static readonly Dictionary<string, int> _plateClass = new Dictionary<string, int>();

        private static int PlateClass(string tpl, object item)
        {
            if (_plateClass.TryGetValue(tpl, out int known)) return known; // a preset is made once per item, not per click
            int r = PlateClassOf(tpl, item);
            _plateClass[tpl] = r;
            return r;
        }

        private static MethodInfo _fill;
        private static bool _armorLogged;

        /// <summary>An armor class from a template: its ArmorClass / armorClass (int, or a number as text), 0 if none.</summary>
        private static int ClassOf(object template)
        {
            var v = Refl.Get(template, "ArmorClass") ?? Refl.Get(template, "armorClass");
            if (v is int i) return i;
            return v != null && int.TryParse(v.ToString(), out int p) ? p : 0;
        }

        /// <summary>
        /// The best armor class among an item's default plates / armor parts. The item is made filled
        /// (ItemFactory.CreateAndFillItem: plates inserted, like the game's own), else from its preset, else as it is; and
        /// as a last try, the default plate ids in its slots' filters. What was found is logged once, for the first one.
        /// </summary>
        private static int PlateClassOf(string tpl, object item)
        {
            var notes = new List<string>();
            int best = 0;
            try
            {
                var sources = new List<(string How, object Item)>();
                if (_fill != null && _factory != null)
                    try { sources.Add(("filled", _fill.Invoke(_factory, new[] { Id(_fill.GetParameters()[0].ParameterType, tpl) }))); } catch (Exception e) { notes.Add("filled: " + e.GetBaseException().Message); }
                if (_preset != null && _factory != null)
                    try { sources.Add(("preset", _preset.Invoke(_factory, new[] { Id(_preset.GetParameters()[0].ParameterType, tpl) }))); } catch { }
                sources.Add(("bare", item));
                foreach (var (how, source) in sources)
                {
                    if (source == null) { notes.Add(how + ": none"); continue; }
                    var all = AccessTools.Method(source.GetType(), "GetAllItems", Type.EmptyTypes)?.Invoke(source, null) as System.Collections.IEnumerable;
                    int parts = 0;
                    if (all != null)
                        foreach (var part in all)
                        {
                            if (part == null || ReferenceEquals(part, source)) continue;
                            parts++;
                            best = Math.Max(best, ClassOf(Refl.Get(part, "Template")));
                        }
                    notes.Add($"{how}: {parts} part(s), best class {best}");
                    if (best > 0) break;
                }
                // the default plates named in the slots' filters ("Plate": id)
                if (best == 0 && Refl.Get(Refl.Get(item, "Template"), "Slots") is System.Collections.IEnumerable slots)
                {
                    int plates = 0;
                    foreach (var slot in slots)
                    {
                        var props = Refl.Get(slot, "Props") ?? Refl.Get(slot, "_props") ?? slot;
                        if (!(Refl.Get(props, "filters") is System.Collections.IEnumerable filters)) continue;
                        foreach (var f in filters)
                        {
                            var plate = Refl.Get(f, "Plate")?.ToString();
                            if (string.IsNullOrEmpty(plate) || plate == "0") continue;
                            plates++;
                            best = Math.Max(best, ClassOf(Refl.Get(ItemOf(plate), "Template")));
                        }
                    }
                    notes.Add($"slot filters: {plates} default plate(s), best class {best}");
                }
            }
            catch (Exception e) { notes.Add("error: " + e.GetBaseException().Message); }
            if (!_armorLogged)
            {
                _armorLogged = true;
                var t = Refl.Get(item, "Template");
                var members = t == null ? "" : string.Join(", ", t.GetType().GetFields(Refl.All).Select(f => f.Name).Concat(t.GetType().GetProperties(Refl.All).Select(p => p.Name))
                    .Where(n => n.IndexOf("armor", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("class", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0).Distinct().Take(30).ToArray());
                L.Info($"armor class of {tpl}: {best} ({string.Join("; ", notes.ToArray())}); template members: {members}");
            }
            return best;
        }

        /// <summary>A food / drink's effect on Energy or Hydration (its template's effects_health), or null.</summary>
        private static double? HealthEffect(object template, string key)
        {
            try
            {
                var effects = Refl.Get(template, "effects_health") ?? Refl.Get(template, "HealthEffects") ?? Refl.Get(template, "EffectsHealth");
                if (!(effects is System.Collections.IEnumerable list)) return null;
                foreach (var e in list)
                {
                    var k = Refl.Get(e, "Key")?.ToString() ?? Refl.Get(e, "Type")?.ToString();
                    if (k == null || k.IndexOf(key, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var v = Refl.Get(e, "Value");
                    var n = Refl.Get(v, "Value") ?? Refl.Get(v, "value") ?? v;
                    return Convert.ToDouble(n);
                }
            }
            catch { }
            return null;
        }

        /// <summary>"ArmoredSteel" → "Armored steel", "UHMWPE" stays.</summary>
        private static string Spaced(string s)
        {
            if (s == "Titan") return "Titanium";
            var r = System.Text.RegularExpressions.Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
            return r.Length > 1 && r != r.ToUpperInvariant() ? char.ToUpperInvariant(r[0]) + r.Substring(1).ToLowerInvariant() : r;
        }

        /// <summary>Cells in a container's grids (rigs, backpacks), from its template; 0 if it has none.</summary>
        private static int Capacity(object template)
        {
            try
            {
                if (!(Refl.Get(template, "Grids") is System.Collections.IEnumerable grids)) return 0;
                int n = 0;
                foreach (var g in grids)
                {
                    var props = Refl.Get(g, "Props") ?? Refl.Get(g, "_props") ?? g;
                    object h = Refl.Get(props, "cellsH") ?? Refl.Get(g, "cellsH"), v = Refl.Get(props, "cellsV") ?? Refl.Get(g, "cellsV");
                    if (h != null && v != null) n += Convert.ToInt32(h) * Convert.ToInt32(v);
                }
                return n;
            }
            catch { return 0; }
        }

        public static string CaliberName(string raw)
        {
            var loc = ProgData.Localize(raw);
            if (!string.IsNullOrEmpty(loc) && loc != raw && !loc.StartsWith("Caliber")) return loc;
            var s = raw.Replace("Caliber", "");
            // calibers whose first number is a decimal the id drops the dot from (12.7, 9.3, 6.8, 8.6, 5.7, 4.6)
            foreach (var (id, real) in new[] { ("127x", "12.7x"), ("93x", "9.3x"), ("68x", "6.8x"), ("86x", "8.6x"), ("57x", "5.7x"), ("46x", "4.6x") })
                if (s.StartsWith(id)) { var rest = s.Substring(id.Length); var mm = System.Text.RegularExpressions.Regex.Match(rest, @"^(\d+)(.*)$"); return mm.Success ? $"{real}{mm.Groups[1]} {mm.Groups[2]}".Trim() : real + rest; }
            var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d)(\d{2})x(\d+)(.*)$");                   // 556x45NATO → 5.56x45 NATO
            // a one-letter suffix stays attached, like the game's own names: 762x54R → 7.62x54R (not "7.62x54 R")
            if (m.Success) return $"{m.Groups[1]}.{m.Groups[2]}x{m.Groups[3]}{(m.Groups[4].Length == 1 ? "" : " ")}{m.Groups[4]}".Trim();
            m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d{3})([A-Za-z].*)$");                            // 366TKM → .366 TKM
            if (m.Success) return $".{m.Groups[1]} {m.Groups[2]}";
            m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d+x\d+)(.*)$");                                // 9x19PARA → 9x19 PARA
            if (m.Success) return $"{m.Groups[1]} {m.Groups[2]}".Trim();
            return s;
        }

        public static void Inspect(string tpl)
        {
            var item = ItemOf(tpl);
            if (item == null) { L.Warn($"inspect {tpl}: no game item"); return; }
            InspectObject(item, tpl);
        }

        /// <summary>
        /// The rank-up dogtag: a fresh game dogtag of your faction (USEC 59f32c3b86f77472a31742f0 / BEAR
        /// 59f32bb586f774757e1e8442) with your nickname, level, the date and the new rank written into its dogtag data, opened
        /// in the game's own inspect window (the real tag, the real rows). False: it couldn't be made (the caller falls back).
        /// </summary>
        public static bool InspectDogtag(bool bear, string nickname, int level, string rank)
        {
            try
            {
                Init();
                if (_factory == null) _factory = SingletonOf(AccessTools.TypeByName("EFT.ItemFactory"));
                if (_factory == null || _create == null) { L.Info("dogtag: no item factory"); return false; }
                string tpl = bear ? "59f32bb586f774757e1e8442" : "59f32c3b86f77472a31742f0";
                var ps = _create.GetParameters();
                var args = new object[ps.Length];
                args[0] = Id(ps[0].ParameterType, NewHex());
                args[1] = Id(ps[1].ParameterType, tpl);
                for (int i = 2; i < ps.Length; i++) args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
                var item = _create.Invoke(_factory, args);
                if (item == null) { L.Info("dogtag: the game made no item"); return false; }
                // its dogtag data: the item's Dogtag, or the component in its Components that is one
                object tag = Refl.Get(item, "Dogtag");
                if (tag == null && Refl.Get(item, "Components") is System.Collections.IEnumerable comps)
                    foreach (var c in comps) if (c != null && c.GetType().Name.IndexOf("Dogtag", StringComparison.OrdinalIgnoreCase) >= 0) { tag = c; break; }
                if (tag == null) { L.Info($"dogtag: no dogtag data on {item.GetType().Name}"); return false; }
                var profile = ProgData.Profile();
                var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Nickname"] = nickname, ["Side"] = bear ? "Bear" : "Usec", ["Level"] = level, ["Time"] = DateTime.Now,
                    // the game shows STATUS as Status + KillerName ("Killed by " + name): "Promoted to Drifter"
                    ["Status"] = "Promoted to ", ["KillerName"] = rank, ["WeaponName"] = "Service record",
                    ["ProfileId"] = ProgData.ProfileId() ?? "", ["AccountId"] = Refl.Get(profile, "AccountId")?.ToString() ?? "",
                };
                var set = new List<string>();
                foreach (var m in tag.GetType().GetMembers(Refl.All))
                {
                    if (!values.TryGetValue(m.Name, out var val)) continue;
                    Type t = m is FieldInfo f ? f.FieldType : m is PropertyInfo p && p.CanWrite ? p.PropertyType : null;
                    if (t == null) continue;
                    try
                    {
                        object v = t.IsEnum ? Enum.Parse(t, val.ToString(), true) : t == typeof(string) ? val.ToString() : Convert.ChangeType(val, t);
                        if (m is FieldInfo fi) fi.SetValue(tag, v); else ((PropertyInfo)m).SetValue(tag, v, null);
                        set.Add(m.Name);
                    }
                    catch (Exception e) { L.Debug($"dogtag: {m.Name} not set: {e.GetBaseException().Message}"); }
                }
                L.Info($"dogtag: {(bear ? "BEAR" : "USEC")} tag for {nickname}, level {level} ({rank}) — set {string.Join(", ", set.ToArray())}");
                // the window the game opens for it: the inspect panel that wasn't open before (so the rank-up can fade it in)
                var before = new HashSet<int>(OpenInspectPanels().Select(c => c.GetInstanceID()));
                if (!InspectObject(item, tpl)) return false;
                _dogtagPanel = OpenInspectPanels().Where(c => !before.Contains(c.GetInstanceID())).LastOrDefault();
                _hiddenRows.Clear(); _rowsTried = 0;
                DogtagWindow = _dogtagPanel != null ? _dogtagPanel.transform as RectTransform : null;
                if (DogtagWindow != null) { var cg = DogtagWindow.GetComponent<CanvasGroup>() ?? DogtagWindow.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0; }
                L.Info(DogtagWindow != null ? $"dogtag: fading in {DogtagWindow.name}" : "dogtag: the game's window wasn't found (no fade-in)");
                return true;
            }
            catch (Exception e) { L.Info("dogtag: " + e.GetBaseException().Message); return false; }
        }

        /// <summary>The game's inspect window the rank-up opened (null once it's faded in).</summary>
        public static RectTransform DogtagWindow;
        private static Component _dogtagPanel;
        private static readonly List<GameObject> _hiddenRows = new List<GameObject>();
        private static int _rowsTried;

        /// <summary>
        /// The rows a promotion has no use for (DEATH TIME, WEAPON) hidden in the rank-up's window. Its rows are filled in a
        /// frame or two after it opens, so this is called every frame for a while; given back before the window closes.
        /// </summary>
        public static void HideDogtagRows()
        {
            if (_dogtagPanel == null || _hiddenRows.Count >= 2 || _rowsTried > 90) return;
            _rowsTried++;
            try
            {
                foreach (var c in _dogtagPanel.GetComponentsInChildren<Component>(false))
                {
                    if (c == null) continue;
                    var tn = c.GetType().Name;
                    if (tn.IndexOf("TextMeshPro", StringComparison.Ordinal) < 0 && !(c is UnityEngine.UI.Text)) continue;
                    string txt = (Refl.Get(c, "text") as string ?? "").Trim().TrimEnd(':').Trim();
                    if (!txt.Equals("death time", StringComparison.OrdinalIgnoreCase) && !txt.Equals("weapon", StringComparison.OrdinalIgnoreCase)) continue;
                    // up to the row: the child of the list (a grid / vertical layout), not the row's own horizontal layout
                    var row = c.transform;
                    for (int d = 0; d < 6 && row.parent != null && row.parent != _dogtagPanel.transform; d++)
                    {
                        if (row.parent.GetComponent<UnityEngine.UI.GridLayoutGroup>() != null || row.parent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() != null) break;
                        row = row.parent;
                    }
                    if (!row.gameObject.activeSelf || _hiddenRows.Contains(row.gameObject)) continue;
                    row.gameObject.SetActive(false);
                    _hiddenRows.Add(row.gameObject);
                    L.Info($"dogtag: hid the {txt.ToUpperInvariant()} row ({row.name} under {row.parent?.name})");
                }
            }
            catch (Exception e) { L.Debug("dogtag rows: " + e.GetBaseException().Message); _rowsTried = 999; }
        }

        /// <summary>Closes the rank-up's window (once the moment is over), giving back the rows it hid.</summary>
        public static void CloseDogtag()
        {
            var p = _dogtagPanel;
            _dogtagPanel = null; DogtagWindow = null;
            foreach (var g in _hiddenRows) if (g != null) g.SetActive(true);
            _hiddenRows.Clear();
            if (p == null || !p.gameObject.activeInHierarchy) return;
            var cg = p.GetComponent<CanvasGroup>(); if (cg != null) cg.alpha = 1;
            foreach (var c in p.GetComponents<Component>())
            {
                if (c == null) continue;
                var m = c.GetType().GetMethod("Close", Refl.All, null, Type.EmptyTypes, null);
                if (m == null) continue;
                try { m.Invoke(c, null); L.Info($"dogtag: closed the window ({c.GetType().Name}.Close)"); return; }
                catch (Exception e) { L.Debug($"dogtag: {c.GetType().Name}.Close failed: {e.GetBaseException().Message}"); }
            }
            L.Info("dogtag: no Close on the window — left open");
        }

        private static Type _inspectPanel;
        private static IEnumerable<Component> OpenInspectPanels()
        {
            try
            {
                _inspectPanel ??= AccessTools.TypeByName("EFT.UI.ItemSpecificationPanel");
                if (_inspectPanel == null) return Enumerable.Empty<Component>();
                return UnityEngine.Object.FindObjectsOfType(_inspectPanel).OfType<Component>().Where(c => c != null && c.gameObject.activeInHierarchy).ToList();
            }
            catch { return Enumerable.Empty<Component>(); }
        }

        private static bool InspectObject(object item, string tpl)
        {
            var uiType = AccessTools.TypeByName("EFT.UI.ItemUiContext");
            var ui = uiType?.GetProperty("Instance", Refl.All)?.GetValue(null, null) ?? SingletonOf(uiType) ?? (uiType != null ? UnityEngine.Object.FindObjectOfType(uiType) : null);
            if (ui == null) { L.Warn("inspect: ItemUiContext not found"); return false; }
            _inspect ??= uiType.GetMethods(Refl.All).Where(m => (m.Name == "Inspect" || m.Name == "ShowContextMenu") && !m.ContainsGenericParameters && m.GetParameters().Length >= 1)
                .OrderBy(m => m.Name == "Inspect" ? 0 : 1).ToList();
            foreach (var m in _inspect)
            {
                var ps = m.GetParameters();
                // the game wants an ItemContext (the item plus where it is shown), not the item
                object ctx = ps[0].ParameterType.IsInstanceOfType(item) ? item : ContextFor(ps[0].ParameterType, item);
                if (ctx == null) continue;
                var args = new object[ps.Length];
                args[0] = ctx;
                for (int i = 1; i < ps.Length; i++)
                    args[i] = ps[i].ParameterType == typeof(Vector2) ? (object)(Vector2)UnityInput.Current.mousePosition
                        : ps[i].HasDefaultValue ? ps[i].DefaultValue : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                try
                {
                    m.Invoke(m.IsStatic ? null : ui, args);
                    L.Info($"inspect {tpl}: opened with ItemUiContext.{m.Name}({Sig(m)}) using {ctx.GetType().Name}");
                    return true;
                }
                catch (Exception e) { L.Debug($"inspect with {m.Name}({Sig(m)}) failed: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}"); }
            }
            L.Warn($"inspect {tpl}: nothing worked (tried {_inspect.Count} method(s); the item context attempts are logged above).");
            return false;
        }
    }
}
