using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
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

                var viewFactory = AccessTools.TypeByName("EFT.UI.DragAndDrop.ItemViewFactory");
                _loadIcon = viewFactory?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(m => m.Name == "LoadItemIcon" && m.GetParameters().Length == 1);
                if (_loadIcon == null && _itemType != null)
                {
                    L.Debug("ItemViewFactory.LoadItemIcon not found by name — searching the game's classes");
                    foreach (var t in AccessTools.GetTypesFromAssembly(_itemType.Assembly))
                    {
                        _loadIcon = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            .FirstOrDefault(m => m.Name == "LoadItemIcon" && m.GetParameters().Length == 1);
                        if (_loadIcon != null) break;
                    }
                }
                L.Info($"items: icons from {(_loadIcon == null ? "NOTHING" : _loadIcon.DeclaringType.FullName + "." + _loadIcon.Name + "(" + Sig(_loadIcon) + ") -> " + _loadIcon.ReturnType.FullName)}");
                if (_loadIcon != null)
                    L.Debug("  icon object members: " + string.Join(", ", _loadIcon.ReturnType.GetProperties(Refl.All).Select(p => p.Name + ":" + p.PropertyType.Name)
                        .Concat(_loadIcon.ReturnType.GetFields(Refl.All).Select(f => f.Name + ":" + f.FieldType.Name)).ToArray()));

                var ui = AccessTools.TypeByName("EFT.UI.ItemUiContext");
                if (ui != null)
                    foreach (var m in ui.GetMethods(Refl.All).Where(m => m.Name.Contains("Inspect") || m.Name.Contains("ContextMenu") || m.Name == "ShowTooltip"))
                        L.Debug($"  ItemUiContext.{m.Name}({Sig(m)})");
                else L.Info("items: EFT.UI.ItemUiContext not found — no inspect");
            }
            catch (Exception e) { L.Error("looking up the game's item classes", e); }
            L.Debug($"item class lookup took {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
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
        public static object IconOf(object item)
        {
            if (item == null || _loadIcon == null) return null;
            try { return _loadIcon.Invoke(null, new[] { item }); }
            catch (Exception e) { L.ErrorOnce("LoadItemIcon", e); return null; }
        }

        public static Sprite SpriteOf(object icon)
        {
            if (icon == null) return null;
            if (icon is Sprite s) return s;
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

        /// <summary>Opens the game's item inspect window. Tries every ItemUiContext.Inspect* that can take just the item.</summary>
        public static void Inspect(string tpl)
        {
            var item = ItemOf(tpl);
            if (item == null) { L.Warn($"inspect {tpl}: no game item"); return; }
            var uiType = AccessTools.TypeByName("EFT.UI.ItemUiContext");
            var ui = uiType?.GetProperty("Instance", Refl.All)?.GetValue(null, null) ?? SingletonOf(uiType) ?? (uiType != null ? UnityEngine.Object.FindObjectOfType(uiType) : null);
            if (ui == null) { L.Warn("inspect: ItemUiContext not found"); return; }
            _inspect ??= uiType.GetMethods(Refl.All).Where(m => m.Name.Contains("Inspect") && !m.ContainsGenericParameters && m.GetParameters().Length >= 1).OrderBy(m => m.GetParameters().Length).ToList();
            foreach (var m in _inspect)
            {
                var ps = m.GetParameters();
                int itemAt = Array.FindIndex(ps, p => p.ParameterType.IsInstanceOfType(item));
                if (itemAt < 0) continue;
                var args = new object[ps.Length];
                for (int i = 0; i < ps.Length; i++)
                    args[i] = i == itemAt ? item : ps[i].HasDefaultValue ? ps[i].DefaultValue : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
                try
                {
                    m.Invoke(m.IsStatic ? null : ui, args);
                    L.Info($"inspect {tpl}: opened with ItemUiContext.{m.Name}({Sig(m)})");
                    return;
                }
                catch (Exception e) { L.Debug($"inspect with {m.Name}({Sig(m)}) failed: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}"); }
            }
            L.Warn($"inspect {tpl}: none of the {_inspect.Count} ItemUiContext.Inspect* methods took just an item (their signatures are in the log above with VerboseLog on).");
        }
    }
}
