using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LevelGate.Progression
{
    /// <summary>
    /// The game's own stat icons (the ones next to CALIBER, EFFECTIVE DISTANCE, FIRE RATE… in the inspect window):
    /// StaticIcons.GetAttributeIcon(EItemAttributeId), found by reflection. Read only; null when the game doesn't have one
    /// (the stat then just shows without an icon). What was found is logged once.
    /// </summary>
    internal static class StatIcons
    {
        private static bool _tried;
        private static MethodInfo _get;
        private static object _inst;
        private static Type _enum;
        private static string[] _names = new string[0];
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();

        // our labels → the game's attribute ids, most likely first
        private static readonly Dictionary<string, string[]> Ids = new Dictionary<string, string[]>
        {
            { "Damage", new[] { "MaxAmmoDamage", "Damage" } },
            { "Penetration", new[] { "AmmoPenetrationPower", "PenetrationPower", "Penetration" } },
            { "Armor class", new[] { "ArmorClass" } },
            { "Size", new[] { "Size", "ContainerSize" } },
            { "Resource", new[] { "MaxResource", "Resource", "MaxHpResource", "HpResource", "Usages" } },
            { "Energy", new[] { "EnergyChange", "Energy", "FoodResource" } },
            { "Hydration", new[] { "HydrationChange", "Hydration", "FoodResource" } },
            { "Fire rate", new[] { "FireRate", "Firerate" } },
            { "Ergonomics", new[] { "Ergonomics" } },
            { "Recoil", new[] { "RecoilUp", "VerticalRecoil", "Recoil" } },
            { "Eff. range", new[] { "EffectiveDist", "EffectiveDistance", "EffDist" } },
            { "Weight", new[] { "Weight" } },
            { "Caliber", new[] { "Caliber", "AmmoCaliber" } },
            { "Durability", new[] { "Durability", "MaxDurability" } },
            { "Material", new[] { "ArmorMaterial", "Material" } },
            { "Capacity", new[] { "ContainerSize", "Capacity", "GridSize" } },
            { "Movement", new[] { "ChangeMovementSpeed", "MovementSpeed", "SpeedPenalty" } },
            { "Turning", new[] { "ChangeTurningSpeed", "TurningSpeed", "MouseSensitivity", "MousePenalty" } },
            { "Ergo penalty", new[] { "ChangeWeaponErgonomics", "Ergonomics" } },
        };

        public static Sprite Of(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            if (_cache.TryGetValue(label, out var s)) return s;
            Find();
            s = null;
            string used = null;
            if (_get != null && _enum != null && Ids.TryGetValue(label, out var wanted))
                foreach (var w in wanted)
                {
                    var name = _names.FirstOrDefault(n => string.Equals(n, w, StringComparison.OrdinalIgnoreCase));
                    if (name == null) continue;
                    try { s = _get.Invoke(_get.IsStatic ? null : _inst, new[] { Enum.Parse(_enum, name) }) as Sprite; }
                    catch (Exception e) { L.Debug($"stat icon {label}: {e.GetBaseException().Message}"); }
                    if (s != null) { used = name; break; }
                }
            _cache[label] = s;
            L.Debug($"stat icon '{label}': {(s != null ? $"the game's {used} ({s.name})" : "none")}");
            return s;
        }

        private static void Find()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                var types = asm?.GetTypes() ?? new Type[0];
                _enum = types.FirstOrDefault(t => t.IsEnum && t.Name == "EItemAttributeId");
                var icons = types.FirstOrDefault(t => t.Name == "StaticIcons");
                if (_enum != null) _names = Enum.GetNames(_enum);
                if (icons != null)
                {
                    _get = icons.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "GetAttributeIcon" && m.GetParameters().Length == 1 && m.ReturnType == typeof(Sprite));
                    if (_get != null && !_get.IsStatic)
                    {
                        // the game's instance: EFTHardSettings.Instance.StaticIcons, else a loaded asset of that type
                        var hard = types.FirstOrDefault(t => t.Name == "EFTHardSettings");
                        object hs = null;
                        try { hs = (object)hard?.GetProperty("Instance", Refl.All)?.GetValue(null, null) ?? hard?.GetField("Instance", Refl.All)?.GetValue(null); } catch { }
                        if (hs != null)
                            foreach (var f in hs.GetType().GetFields(Refl.All))
                                if (icons.IsAssignableFrom(f.FieldType)) { _inst = f.GetValue(hs); if (_inst != null) break; }
                        if (_inst == null && typeof(UnityEngine.Object).IsAssignableFrom(icons))
                            _inst = Resources.FindObjectsOfTypeAll(icons).FirstOrDefault();
                    }
                }
                L.Info($"stat icons: {(_get == null ? "StaticIcons.GetAttributeIcon not found" : _get.IsStatic ? "found (static)" : _inst != null ? "found" : "found, but no StaticIcons instance")}; " +
                       $"{_names.Length} attribute ids{(L.Verbose && _names.Length > 0 ? ": " + string.Join(", ", _names) : "")}");
                if (_get != null && !_get.IsStatic && _inst == null) _get = null;
            }
            catch (Exception e) { L.Info("stat icons: lookup failed: " + e.GetBaseException().Message); _get = null; }
        }
    }
}
