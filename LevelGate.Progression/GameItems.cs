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
            int n = 0;
            var t0 = DateTime.Now;
            foreach (var item in items)
            {
                L.Step($"icon: back to 1x {Refl.Get(item, "TemplateId") ?? item.GetType().Name}");
                try { CallIcon(_loadIcon, item, 1, true); n++; }
                catch (Exception e) { L.ErrorOnce("restoring an icon to stash size", e); }
            }
            // kept (not cleared): every item ever drawn bigger is put back on each close and on the second pass after it
            L.Info($"items: {n} icon(s) drawn again at stash size ({(DateTime.Now - t0).TotalMilliseconds:0} ms)");
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
                var t = Refl.Get(item, "Template");
                int w = Refl.Get(t, "Width") is int a ? a : 1, h = Refl.Get(t, "Height") is int b ? b : 1;
                return (Mathf.Max(1, w), Mathf.Max(1, h));
            }
            catch { return (1, 1); }
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

        /// <summary>Our own copy of a bigger render of this item, if we made one already.</summary>
        public static Sprite CopyOf(string tpl, int scale) => _copies.TryGetValue(tpl + "@" + scale, out var sp) && sp != null ? sp : null;

        /// <summary>The picture to show for an icon: normal ones as they are; bigger ones copied (then the game's is put back).</summary>
        /// <summary>done = false: a stand-in (the stash-size picture) while the bigger one is still being drawn — show it and ask again.</summary>
        public static Sprite TakeSprite(object icon, string tpl, out bool done)
        {
            done = false;
            var sp = SpriteOf(icon);
            if (sp == null) return null;
            if (icon == null || !_pending.TryGetValue(icon, out var p)) { done = true; return sp; }
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
            try { CallIcon(_loadIcon, p.Item, 1, true); } catch (Exception e) { L.ErrorOnce("putting an icon back to stash size", e); }
            if (copy == null) return sp;
            var key = tpl + "@" + p.Scale;
            _copies[key] = copy;
            _copyOrder.Enqueue(key);
            while (_copyOrder.Count > 90) // the pages around the current one are kept ready too
            {
                var old = _copyOrder.Dequeue();
                if (_copies.TryGetValue(old, out var o) && o != null && old != key) { UnityEngine.Object.Destroy(o.texture); _copies.Remove(old); }
            }
            L.Debug($"icon: kept a {copy.rect.width:0}x{copy.rect.height:0} copy of {tpl} and put the game's back to stash size");
            return copy;
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
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
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

        public static void RepairAll(IEnumerable<string> tpls)
        {
            foreach (var t in tpls) _repair.Enqueue(t);
            RepairTotal = _repair.Count;
            L.Info($"items: redrawing {RepairTotal} icons at stash size");
        }

        public static void RepairTick()
        {
            if (_repair.Count == 0 || _loadIcon == null) return;
            if (MenuHook.Blocked()) return; // never while deploying / in a raid: the game needs its render time
            var t0 = Time.realtimeSinceStartup;
            while (_repair.Count > 0 && Time.realtimeSinceStartup - t0 < .012f)
            {
                var tpl = _repair.Dequeue();
                var item = ItemOf(tpl);
                if (item == null) continue;
                try { CallIcon(_loadIcon, item, 1, true); } catch (Exception e) { L.ErrorOnce("redrawing an icon", e); }
            }
            if (_repair.Count == 0) L.Info($"items: {RepairTotal} icons redrawn at stash size");
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

        /// <summary>An ItemContext for a loose item: any concrete class of that kind with a constructor taking the item
        /// (other arguments: first enum value, false, null). What was tried is logged.</summary>
        private static object ContextFor(Type want, object item)
        {
            if (_ctxCtors == null)
            {
                _ctxCtors = new List<ConstructorInfo>();
                foreach (var t in AccessTools.GetTypesFromAssembly(want.Assembly))
                {
                    if (t.IsAbstract || t.ContainsGenericParameters || !want.IsAssignableFrom(t)) continue;
                    foreach (var c in t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (c.GetParameters().Any(p => p.ParameterType.IsAssignableFrom(_itemType))) _ctxCtors.Add(c);
                }
                _ctxCtors = _ctxCtors.OrderBy(c => c.GetParameters().Length).ToList();
                L.Info($"inspect: {_ctxCtors.Count} way(s) to make a {want.Name}: " + string.Join(" | ", _ctxCtors.Take(10).Select(c => $"{c.DeclaringType.Name}({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name).ToArray())})").ToArray()));
            }
            foreach (var c in _ctxCtors)
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
        public static List<(string Label, string Value)> Facts(string tpl)
        {
            var list = new List<(string, string)>();
            try
            {
                var item = ItemOf(tpl);
                var t = Refl.Get(item, "Template");
                if (t == null) return list;
                object Num(params string[] names) { foreach (var n in names) { var v = Refl.Get(t, n); if (v != null && !(v is string s && s.Length == 0)) return v; } return null; }
                var w = Num("Width"); var h = Num("Height");
                if (w != null && h != null) list.Add(("Size", $"{w} × {h}"));
                if (Num("Weight") is float kg) list.Add(("Weight", $"{kg:0.###} kg"));
                if (Num("Damage") is int dmg && dmg > 0) list.Add(("Damage", dmg.ToString()));
                if (Num("PenetrationPower") is int pen && pen > 0) list.Add(("Penetration", pen.ToString()));
                if (Num("ArmorClass", "armorClass") is int ac && ac > 0) list.Add(("Armor class", ac.ToString()));
                if (Num("ammoCaliber", "AmmoCaliber", "Caliber", "caliber") is string cal && cal.Length > 0) list.Add(("Caliber", CaliberName(cal)));
                if (Num("MaxHpResource") is int hp && hp > 0) list.Add(("Resource", hp.ToString()));
                // weapons: the template's own handling numbers (base weapon, before mods)
                double? D(object v) { try { return v == null || v is string || v is bool ? (double?)null : Convert.ToDouble(v); } catch { return null; } }
                if (D(Num("bFirerate")) is double rpm && rpm > 0) list.Add(("Fire rate", $"{rpm:0}<size=60%> rpm</size>"));
                if (D(Num("Ergonomics")) is double ergo && ergo > 0) list.Add(("Ergonomics", $"{ergo:0}"));
                if (D(Num("RecoilForceUp")) is double rec && rec > 0) list.Add(("Recoil", $"{rec:0}"));
                if (D(Num("bEffDist")) is double eff && eff > 0) list.Add(("Eff. range", $"{eff:0} m"));
            }
            catch (Exception e) { L.ErrorOnce("item facts", e); }
            return list;
        }

        /// <summary>"Caliber366TKM" → the game's own name if it has one, else a readable form: ".366 TKM", "5.56x45 NATO", "9x19 PARA".</summary>
        public static string CaliberName(string raw)
        {
            var loc = ProgData.Localize(raw);
            if (!string.IsNullOrEmpty(loc) && loc != raw && !loc.StartsWith("Caliber")) return loc;
            var s = raw.Replace("Caliber", "");
            // calibers whose first number is a decimal the id drops the dot from (12.7, 9.3, 6.8, 8.6, 5.7, 4.6)
            foreach (var (id, real) in new[] { ("127x", "12.7x"), ("93x", "9.3x"), ("68x", "6.8x"), ("86x", "8.6x"), ("57x", "5.7x"), ("46x", "4.6x") })
                if (s.StartsWith(id)) { var rest = s.Substring(id.Length); var mm = System.Text.RegularExpressions.Regex.Match(rest, @"^(\d+)(.*)$"); return mm.Success ? $"{real}{mm.Groups[1]} {mm.Groups[2]}".Trim() : real + rest; }
            var m = System.Text.RegularExpressions.Regex.Match(s, @"^(\d)(\d{2})x(\d+)(.*)$");                   // 556x45NATO → 5.56x45 NATO
            if (m.Success) return $"{m.Groups[1]}.{m.Groups[2]}x{m.Groups[3]} {m.Groups[4]}".Trim();
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
            var uiType = AccessTools.TypeByName("EFT.UI.ItemUiContext");
            var ui = uiType?.GetProperty("Instance", Refl.All)?.GetValue(null, null) ?? SingletonOf(uiType) ?? (uiType != null ? UnityEngine.Object.FindObjectOfType(uiType) : null);
            if (ui == null) { L.Warn("inspect: ItemUiContext not found"); return; }
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
                    return;
                }
                catch (Exception e) { L.Debug($"inspect with {m.Name}({Sig(m)}) failed: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}"); }
            }
            L.Warn($"inspect {tpl}: nothing worked (tried {_inspect.Count} method(s); the item context attempts are logged above).");
        }
    }
}
