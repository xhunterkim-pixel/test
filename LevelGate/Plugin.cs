using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

// ---------------------------------------------------------------------------
// LevelGate — restrict equipping/using specific items until the player PMC
// reaches a configured level. Only ever checks the human player's own PMC;
// AI (bots, scav AI, other simulated PMCs) are never touched.
//
// Every class/method/field name referenced below was verified by directly
// reading the metadata tables of the user's own Assembly-CSharp.dll (SPT
// 4.1.6) — not guessed. See README.md for how each one was found.
// ---------------------------------------------------------------------------

namespace LevelGate
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class LevelGatePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.yourname.levelgate";
        public const string PluginName = "LevelGate";
        public const string PluginVersion = "1.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> ToggleMenuKey;
        internal static LevelGateConfig Data = new LevelGateConfig();

        private static string ConfigFolder =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "config");

        private static string ConfigFile => Path.Combine(ConfigFolder, "level_requirements.json");

        private bool _menuOpen;
        private Rect _menuRect = new Rect(60, 60, 420, 480);
        private string _newItemId = "";
        private string _newItemLevel = "";
        private Vector2 _scroll;
        private Vector2 _unresolvedScroll;
        private readonly List<string> _unresolvedIds = new List<string>();

        private void Awake()
        {
            Log = Logger;
            ToggleMenuKey = Config.Bind(
                "General",
                "ToggleMenuKey",
                new KeyboardShortcut(KeyCode.F9),
                "Key to open/close the LevelGate item editor.");

            LoadConfig();

            var harmony = new Harmony(PluginGuid);
            PatchAllIndividually(harmony);
            Patch_BlockOperationInRaid.Apply(harmony);
            LevelGatePatches.PatchAmmoLoad(harmony);
            LevelGateReloadPatches.ApplyAll(harmony);
            DiagnosticLogging.ApplyAll(harmony);

            Log.LogInfo("LevelGate loaded, " + Data.Items.Count + " restricted item(s).");
        }

        // harmony.PatchAll(assembly) processes every [HarmonyPatch]-attributed
        // class in one call — and if even ONE of them has a bad signature
        // (a parameter type Harmony can't bind to a real overload), the
        // WHOLE call throws and aborts, silently skipping every class that
        // would have been processed after the broken one. That's exactly
        // what caused the background-color and context-menu patches to
        // stop working despite having nothing wrong with them themselves —
        // an unrelated Proceed() signature mismatch elsewhere took them
        // down too. Patching each class individually, each in its own
        // try/catch, means a single broken attribute only costs that one
        // patch instead of an unpredictable subset of everything after it.
        private void PatchAllIndividually(Harmony harmony)
        {
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;

                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Log.LogError($"LevelGate: failed to patch {type.Name}, that specific fix will not work. " + e);
                }
            }
        }

        private void Update()
        {
            if (ToggleMenuKey.Value.IsDown())
            {
                _menuOpen = !_menuOpen;
            }
        }

        private void OnGUI()
        {
            if (_menuOpen)
            {
                _menuRect = GUI.Window(GetInstanceID(), _menuRect, DrawMenu, "LevelGate — item level requirements");
            }

            LevelLimiterContextMenu.DrawPickerIfOpen();
        }

        private void DrawMenu(int id)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            GUILayout.BeginVertical();

            GUILayout.Label("Add / update an entry:");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item TplId:", GUILayout.Width(70));
            _newItemId = GUILayout.TextField(_newItemId, GUILayout.Width(220));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Level:", GUILayout.Width(70));
            _newItemLevel = GUILayout.TextField(_newItemLevel, GUILayout.Width(60));
            if (GUILayout.Button("Add / Update"))
            {
                if (!string.IsNullOrWhiteSpace(_newItemId) && int.TryParse(_newItemLevel, out int lvl))
                {
                    Data.Items[_newItemId.Trim()] = lvl;
                    SaveConfig();
                    _newItemId = "";
                    _newItemLevel = "";
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.Label("Current entries (" + Data.Items.Count + "):");

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(300));
            string toRemove = null;
            foreach (var kvp in Data.Items.ToList())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(kvp.Key, GUILayout.Width(230));
                GUILayout.Label("Lvl " + kvp.Value, GUILayout.Width(60));
                if (GUILayout.Button("X", GUILayout.Width(25)))
                {
                    toRemove = kvp.Key;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            if (toRemove != null)
            {
                Data.Items.Remove(toRemove);
                SaveConfig();
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Unresolved IDs ({_unresolvedIds.Count}):", GUILayout.Width(150));
            if (GUILayout.Button("Scan", GUILayout.Width(70)))
            {
                ScanForUnresolvedIds();
            }
            GUILayout.EndHorizontal();

            if (_unresolvedIds.Count > 0)
            {
                _unresolvedScroll = GUILayout.BeginScrollView(_unresolvedScroll, GUILayout.Height(100));
                string toRemoveUnresolved = null;
                foreach (var unresolvedId in _unresolvedIds)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(unresolvedId, GUILayout.Width(260));
                    if (GUILayout.Button("X", GUILayout.Width(25)))
                    {
                        toRemoveUnresolved = unresolvedId;
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();

                if (toRemoveUnresolved != null)
                {
                    Data.Items.Remove(toRemoveUnresolved);
                    _unresolvedIds.Remove(toRemoveUnresolved);
                    SaveConfig();
                }
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Reload from disk"))
            {
                LoadConfig();
            }
            if (GUILayout.Button("Close"))
            {
                _menuOpen = false;
            }

            GUILayout.EndVertical();
        }

        // Uses the same Localized() fallback behavior discovered earlier
        // (an unregistered key comes back unchanged) to detect config
        // entries whose TplId doesn't correspond to any real item —
        // e.g. a typo, or an ID from a different SPT/game version. Run
        // manually (not every frame) since it does one lookup per config
        // entry and this list can run into the hundreds.
        private void ScanForUnresolvedIds()
        {
            _unresolvedIds.Clear();
            foreach (var templateId in Data.Items.Keys)
            {
                try
                {
                    string key = templateId + " Name";
                    string resolved = EFT.LocalizationExtensions.Localized(key, "");
                    if (resolved == key)
                    {
                        _unresolvedIds.Add(templateId);
                    }
                }
                catch (Exception e)
                {
                    Log.LogError($"LevelGate: failed to check id {templateId} during unresolved-id scan. " + e);
                }
            }
        }

        internal static void LoadConfig()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);
                if (!File.Exists(ConfigFile))
                {
                    Data = new LevelGateConfig();
                    SaveConfig();
                    return;
                }

                var json = File.ReadAllText(ConfigFile);
                var wrapper = JsonConvert.DeserializeObject<ConfigFileShape>(json);
                Data = new LevelGateConfig
                {
                    Items = wrapper?.items ?? new Dictionary<string, int>()
                };
            }
            catch (Exception e)
            {
                Log?.LogError("LevelGate: failed to load config, using empty set. " + e);
                Data = new LevelGateConfig();
            }
        }

        internal static void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(ConfigFolder);
                var wrapper = new ConfigFileShape { items = Data.Items };
                File.WriteAllText(ConfigFile, JsonConvert.SerializeObject(wrapper, Formatting.Indented));
            }
            catch (Exception e)
            {
                Log?.LogError("LevelGate: failed to save config. " + e);
            }
        }

        private class ConfigFileShape
        {
            public Dictionary<string, int> items { get; set; } = new Dictionary<string, int>();
        }
    }

    internal class LevelGateConfig
    {
        public Dictionary<string, int> Items = new Dictionary<string, int>();
    }

    // -----------------------------------------------------------------
    // Shared restriction check used by every patch below.
    // -----------------------------------------------------------------
    internal static class LevelGateCheck
    {
        // The "wearing/holding" equipment slots — moving a gated item here
        // counts as "equipping" it. In everyday terms: FirstPrimaryWeapon/
        // SecondPrimaryWeapon/Holster = weapons, TacticalVest = rig,
        // ArmorVest = body armor, Headwear = helmet, FaceCover = face
        // cover, Eyewear = glasses, Earpiece = headset, Backpack = backpack.
        // Pockets/SecuredContainer/Scabbard/Dogtag/ArmBand are deliberately
        // excluded: those are just storage, not equipping, so restricted
        // items can still be carried around unused (e.g. in a backpack you
        // aren't restricted from, or in your pockets).
        public static readonly HashSet<string> EquipSlotIds = new HashSet<string>
        {
            "FirstPrimaryWeapon", "SecondPrimaryWeapon", "Holster",
            "TacticalVest", "ArmorVest", "Eyewear", "FaceCover",
            "Headwear", "Earpiece", "Backpack"
        };

        /// <summary>
        /// True if this item is restricted AND the given player is your own
        /// human PMC AND that player's level is below the requirement.
        /// Bots / other simulated players always return false (never blocked).
        /// </summary>
        public static bool IsBlocked(EFT.Player player, string itemTplId, out int requiredLevel)
        {
            requiredLevel = 0;

            if (player == null) return false;
            if (!player.IsYourPlayer) return false;   // not the local human
            if (player.IsAI) return false;            // never touch bots

            if (itemTplId == null) return false;
            if (!LevelGatePlugin.Data.Items.TryGetValue(itemTplId, out requiredLevel)) return false;

            int currentLevel = player.Profile.Info.Level;
            return currentLevel < requiredLevel;
        }

        public static EFT.Player GetMainPlayer()
        {
            return Comfort.Common.Singleton<EFT.GameWorld>.Instance?.MainPlayer;
        }

        // -----------------------------------------------------------------
        // Ownership checks. IsBlocked() always compares against the MAIN
        // player's level, so any hook that also runs for bots (every bot has
        // its own PlayerInventoryController / FirearmController, and the
        // in-raid CanExecute patch fires for all of them) must first confirm
        // the call belongs to the local player — otherwise bots get blocked
        // from reloading/looting gated items based on YOUR level. Resolved
        // by reflection because the member names aren't compile-verified;
        // if nothing resolves, returns true (the previous behavior).
        // -----------------------------------------------------------------
        private static readonly MemberGetter PlayerInventoryControllerGetter =
            new MemberGetter("InventoryController", "_inventoryController");

        public static bool IsOwnInventoryController(object controller)
        {
            try
            {
                if (controller == null) return true;
                var player = GetMainPlayer();
                if (player == null) return true;

                var own = PlayerInventoryControllerGetter.Get(player);
                if (own != null && ReferenceEquals(own, controller)) return true;

                // Not the same object (or not resolvable): ItemController.ID
                // is the owning profile's ID.
                if (ReflectionUtil.GetMember(controller, "ID") is string id && !string.IsNullOrEmpty(id))
                    return id == player.ProfileId;

                // Main player's controller resolved and this isn't it -> a
                // bot. Nothing resolved at all -> keep the old behavior.
                return own == null;
            }
            catch
            {
                return true;
            }
        }

        public static bool IsOwnHandsController(object handsController)
        {
            try
            {
                if (handsController == null) return true;
                var player = GetMainPlayer();
                if (player == null) return true;

                if (ReflectionUtil.GetMember(handsController, "_player") is EFT.Player owner)
                    return ReferenceEquals(owner, player);

                // Could not resolve the owner: only the local player ever
                // gets a ClientFirearmController, so treat that as ours and
                // anything else (bot controllers) as not ours.
                return handsController is EFT.ClientFirearmController;
            }
            catch
            {
                return handsController is EFT.ClientFirearmController;
            }
        }

        // -----------------------------------------------------------------
        // True if putting a round at "to" loads it into a gun: a chamber
        // (slot on a Weapon), a box/internal/tube magazine or revolver
        // cylinder (a StackSlot/Slot/grid whose parent is a Magazine), or
        // the weapon itself. This is what the equip-slot check in
        // Evaluate() never covered: every in-raid reload/chamber/top-up
        // moves the round with a Move/Split/Transfer operation whose
        // destination is one of these, not one of the EquipSlotIds.
        // Ammo boxes are also Magazine-derived in EFT but are just storage,
        // so they're excluded.
        // -----------------------------------------------------------------
        public static bool IsLoadIntoWeaponOrMagazine(EFT.InventoryLogic.ItemAddress to)
        {
            var parent = GetContainerParentItem(to);
            if (parent == null) return false;
            if (parent is EFT.InventoryLogic.Weapon) return true;
            if (parent is EFT.InventoryLogic.Magazine)
                return parent.GetType().Name.IndexOf("AmmoBox", StringComparison.OrdinalIgnoreCase) < 0;
            return false;
        }

        public static EFT.InventoryLogic.Item GetContainerParentItem(EFT.InventoryLogic.ItemAddress address)
        {
            try
            {
                if (address == null) return null;
                if (address is EFT.InventoryLogic.GridItemAddress gridAddr)
                    return gridAddr.Grid?.ParentItem;

                // SlotItemAddress (.Slot), StackSlotItemAddress (.StackSlot)
                // and the common ItemAddress.Container all expose the owning
                // item as ParentItem.
                var container = ReflectionUtil.GetMember(address, "Container")
                                ?? ReflectionUtil.GetMember(address, "Slot")
                                ?? ReflectionUtil.GetMember(address, "StackSlot");
                return ReflectionUtil.GetMember(container, "ParentItem") as EFT.InventoryLogic.Item;
            }
            catch
            {
                return null;
            }
        }

        private static readonly MethodInfo ConsoleLogMethod = ResolveConsoleLogMethod();

        private static MethodInfo ResolveConsoleLogMethod()
        {
            try
            {
                var type = AccessTools.TypeByName("EFT.UI.ConsoleScreen");
                return type == null ? null : AccessTools.Method(type, "Log", new[] { typeof(string) });
            }
            catch
            {
                return null;
            }
        }

        public static void Notify(int requiredLevel)
        {
            string message = $"LevelGate: requires level {requiredLevel} to use this item.";
            try
            {
                if (ConsoleLogMethod != null)
                {
                    ConsoleLogMethod.Invoke(null, new object[] { message });
                    return;
                }
            }
            catch
            {
                // fall through to the plugin log below
            }

            LevelGatePlugin.Log.LogInfo(message);
        }
    }

    // Cached, exception-safe "read a property or field by name" helpers for
    // members whose exact names couldn't be verified at compile time.
    internal static class ReflectionUtil
    {
        private static readonly Dictionary<(Type, string), Func<object, object>> _cache =
            new Dictionary<(Type, string), Func<object, object>>();

        public static object GetMember(object instance, string name)
        {
            if (instance == null) return null;
            try
            {
                var key = (instance.GetType(), name);
                if (!_cache.TryGetValue(key, out var getter))
                {
                    getter = null;
                    var prop = AccessTools.Property(key.Item1, name);
                    if (prop != null && prop.GetIndexParameters().Length == 0 && prop.GetGetMethod(true) != null)
                    {
                        getter = o => prop.GetValue(o, null);
                    }
                    else
                    {
                        var field = AccessTools.Field(key.Item1, name);
                        if (field != null) getter = o => field.GetValue(o);
                    }
                    _cache[key] = getter;
                }
                return getter?.Invoke(instance);
            }
            catch
            {
                return null;
            }
        }
    }

    internal sealed class MemberGetter
    {
        private readonly string[] _names;
        public MemberGetter(params string[] names) { _names = names; }

        public object Get(object instance)
        {
            foreach (var name in _names)
            {
                var value = ReflectionUtil.GetMember(instance, name);
                if (value != null) return value;
            }
            return null;
        }
    }

    // Pulls every Ammo out of a method argument: the Ammo itself, a list of
    // rounds, or an "ammo pack" object that holds such a list in a field.
    // Doesn't look inside other Items (a magazine/weapon argument is not
    // the rounds being loaded).
    internal static class AmmoCollector
    {
        public static void Collect(object obj, List<EFT.InventoryLogic.Ammo> into, int depth)
        {
            if (obj == null || depth > 1) return;

            if (obj is EFT.InventoryLogic.Ammo ammo) { into.Add(ammo); return; }
            if (obj is EFT.InventoryLogic.Item) return;
            if (obj is string || obj is Delegate || obj is UnityEngine.Object || obj is EFT.InventoryLogic.ItemAddress) return;

            if (obj is System.Collections.IEnumerable enumerable)
            {
                foreach (var element in enumerable)
                {
                    if (element is EFT.InventoryLogic.Ammo a) into.Add(a);
                }
                return;
            }

            var type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || depth >= 1) return;

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object value;
                    try { value = f.GetValue(obj); }
                    catch { continue; }
                    Collect(value, into, depth + 1);
                }
            }
        }
    }

    // Completes a skipped method's Comfort.Common.Callback with a failure,
    // so whatever started the reload isn't left waiting for it. The failed
    // result type is resolved by reflection; if it can't be built, the
    // callback is left alone (the same as before).
    internal static class CallbackUtil
    {
        private static bool _resolved;
        private static ConstructorInfo _failedCtor;

        public static void TryFail(Comfort.Common.Callback callback, string message)
        {
            if (callback == null) return;
            try
            {
                if (!_resolved)
                {
                    _resolved = true;
                    var type = AccessTools.TypeByName("Comfort.Common.FailedResult");
                    _failedCtor = type?.GetConstructors()
                        .FirstOrDefault(c =>
                        {
                            var ps = c.GetParameters();
                            return ps.Length > 0 && ps[0].ParameterType == typeof(string);
                        });
                    if (_failedCtor == null)
                        LevelGatePlugin.Log.LogWarning("LevelGate: could not resolve Comfort.Common.FailedResult — blocked reload callbacks won't be completed.");
                }
                if (_failedCtor == null) return;

                var ps2 = _failedCtor.GetParameters();
                var ctorArgs = new object[ps2.Length];
                ctorArgs[0] = message;
                for (int i = 1; i < ps2.Length; i++)
                {
                    var p = ps2[i];
                    ctorArgs[i] = p.HasDefaultValue ? p.DefaultValue
                        : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null;
                }

                callback.DynamicInvoke(_failedCtor.Invoke(ctorArgs));
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate CallbackUtil error: " + e);
            }
        }
    }

    // -----------------------------------------------------------------
    // PATCHES 1-4 — block equipping, throwing, eating/drinking, and using
    // a med below the required level.
    //
    // This needs TWO separate hooks, because SPT has (at least) two
    // distinct execution paths for inventory actions:
    //
    //   A. Drag/drop or double-click moves (including looting an item off
    //      the ground/a corpse in a raid) run through
    //      EFT.InventoryLogic.ItemController.CanExecute(AbstractOperation),
    //      called at the top of Execute(). Verified via IL: neither
    //      ClientPlayerInventoryController nor its base classes
    //      (PlayerOwnerInventoryController, BackEndInventoryController)
    //      override CanExecute, so patching the base method catches the
    //      real gameplay call. In-raid loot pickup specifically uses
    //      TransferOperation rather than MoveOperation, so instead of
    //      matching individual concrete types we check the common
    //      IOneItemOperation/ITwoItemOperation interfaces both implement.
    //
    //   B. Context-menu-triggered actions (right-click "Eat"/"Drink"/
    //      "Apply") go through ItemController.ExecutePossibleAction
    //      instead, which never calls CanExecute at all — traced via IL,
    //      its own body calls EFT.InventoryLogic.ItemContext
    //      .IsOperationAllowed(IOperationResult) as its gate. ItemContext
    //      has a public "Item" field, so we read that directly rather
    //      than trying to unpack the IOperationResult parameter.
    //
    // (Two earlier approaches were tried and abandoned for hook A — kept
    // here as history in case a future SPT update breaks the current one:
    //   1. Skipping MoveOperation.ExecuteInternal directly — that runs
    //      *after* the drop is accepted and also drives the hands/weapon
    //      switch animation, so skipping it left that state machine stuck
    //      (the "hands busy" bug).
    //   2. Patching Slot.CheckCompatibility — traced its actual callers in
    //      the IL and found it is only used by Slot.CanReplace,
    //      Slot.CheckConditions, and the "auto-equip a better item you
    //      just picked up" feature — none of which run on the normal
    //      drag/drop or double-click equip path, so it silently never
    //      fired for a real equip action.)
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.InventoryLogic.ItemController), "CanExecute", new[] { typeof(EFT.InventoryLogic.Operations.AbstractOperation) })]
    internal static class Patch_BlockOperation
    {
        // A Prefix that skips the original method entirely (rather than a
        // Postfix that lets it run and overwrites the result afterward) —
        // see the note above the class for why this matters: the original
        // CanExecute likely engages a "pending hands change" flag as a side
        // effect of computing true, expecting the equip to actually follow
        // through. A Postfix can't undo that side effect after the fact,
        // which is what caused the "hands busy" stuck state. Skipping the
        // method outright means that side effect never happens.
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.InventoryLogic.ItemController __instance, EFT.InventoryLogic.Operations.AbstractOperation operation, ref bool __result)
        {
            // Bots' controllers inherit this method too — never judge them
            // by the main player's level.
            if (!LevelGateCheck.IsOwnInventoryController(__instance)) return true;

            bool allowed = true;
            Evaluate(operation, ref allowed, "Patch_BlockOperation", startingResult: true);
            if (!allowed)
            {
                __result = false;
                return false; // skip original
            }
            return true; // let the real logic decide
        }

        // Shared by both the base ItemController.CanExecute patch above (used by
        // PlayerOwnerInventoryController / ClientPlayerInventoryController, i.e.
        // the stash/hideout screens) and the PlayerInventoryController.CanExecute
        // patch below (the actual in-raid controller, which overrides CanExecute
        // with its own implementation — confirmed via IL that neither
        // ExecutePossibleAction nor ThrowItem are involved in that override, only
        // CanExecute itself, so patching both bases covers every context).
        //
        // "result" starts as whatever the caller passes via startingResult, and
        // is only ever set to false (never back to true) — callers use it purely
        // to detect "was this blocked".
        //
        // NOTE: EFT.InventoryLogic.Operations.ThrowOperation is NOT the grenade
        // cook-and-throw mechanic — that's handled entirely outside the
        // inventory-operation system, by separate hand-controller classes
        // (HighThrowOperation / LowThrowOperation / QuickGrenadeThrowOperation)
        // that never go through ItemController.CanExecute at all. ThrowOperation
        // here is actually the generic "drop this item into the raid world"
        // action, so it must never be blocked — that's the one thing a
        // restricted item should always be allowed to do, to get rid of it.
        internal static void Evaluate(EFT.InventoryLogic.Operations.AbstractOperation operation, ref bool result, string patchName, bool startingResult)
        {
            result = startingResult;
            try
            {
                DiagnosticLogging.LogCall($"{patchName} operationType={operation?.GetType().Name ?? "NULL"}");

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return;

                // Eating/drinking or using a med: item held in a private field.
                EFT.InventoryLogic.Item consumable = null;
                if (operation is EFT.InventoryLogic.Operations.EatOperation eatOp)
                    consumable = ConsumableFieldCache.EatItemField?.GetValue(eatOp) as EFT.InventoryLogic.Item;
                else if (operation is EFT.InventoryLogic.Operations.HealOperation healOp)
                    consumable = ConsumableFieldCache.HealItemField?.GetValue(healOp) as EFT.InventoryLogic.Item;

                if (consumable != null)
                {
                    if (LevelGateCheck.IsBlocked(player, consumable.TemplateId, out int reqConsume))
                    {
                        LevelGateCheck.Notify(reqConsume);
                        result = false;
                    }
                    return;
                }

                // Equipping — covers MoveOperation (stash drag/drop) and
                // TransferOperation (in-raid loot pickup), and any future
                // operation exposing the same one/two-item interfaces.
                //
                // Loading ammo — a round moved/split/transferred into a
                // chamber, magazine, shotgun tube, internal magazine or
                // cylinder. This was the missing case behind "M4A1 manually
                // chambers after a weapon swap", "Mosin still loads gated
                // ammo" and "MP-153 still loads gated slugs": in raid, every
                // one of those ends in a Move/Split/Transfer operation that
                // reaches this method, but the destination is a chamber /
                // magazine slot rather than one of the EquipSlotIds, so the
                // equip check alone let it straight through.
                if (operation is EFT.InventoryLogic.Operations.IOneItemOperation oneItem &&
                    (TryBlockEquip(oneItem.Item1, oneItem.To1, player, out int req1) ||
                     TryBlockAmmoLoad(oneItem.Item1, oneItem.To1, player, patchName, operation, out req1)))
                {
                    LevelGateCheck.Notify(req1);
                    result = false;
                    return;
                }

                if (operation is EFT.InventoryLogic.Operations.ITwoItemOperation twoItem &&
                    (TryBlockEquip(twoItem.Item2, twoItem.To2, player, out int req2) ||
                     TryBlockAmmoLoad(twoItem.Item2, twoItem.To2, player, patchName, operation, out req2)))
                {
                    LevelGateCheck.Notify(req2);
                    result = false;
                    return;
                }

                // Hotbar/quickslot assignment — confirmed via diagnostic
                // logging (which dumped its actual fields) that this is a
                // real, firing operation: _item is the item being bound,
                // _index is which hotbar slot (Item4, Item5, etc.).
                // Blocking this means a restricted item can never be
                // assigned to a hotbar slot in the first place, which
                // sidesteps needing to catch every possible "use" trigger
                // after the fact.
                if (operation is EFT.InventoryLogic.Operations.BindItemOperation bindOp)
                {
                    var boundItem = BindItemFieldCache.ItemField?.GetValue(bindOp) as EFT.InventoryLogic.Item;
                    if (boundItem != null && LevelGateCheck.IsBlocked(player, boundItem.TemplateId, out int reqBind))
                    {
                        LevelGateCheck.Notify(reqBind);
                        result = false;
                    }
                    return;
                }

                // Anything else (e.g. LoadMagOperation, confirmed via
                // diagnostic logging to pass through here unblocked): these
                // don't implement IOneItemOperation/ITwoItemOperation
                // themselves. LoadMagOperation's own constructor signature
                // (ushort, ItemController, AbstractOperation) shows it wraps
                // an INNER operation — likely the real MoveOperation doing
                // the actual placement — stored in a field inherited from a
                // generic base class that proved too difficult to resolve
                // from raw IL alone, so it's found via reflection at
                // runtime instead. If unwrapping succeeds, recursively
                // evaluate the real inner operation the same way.
                if (!(operation is EFT.InventoryLogic.Operations.IOneItemOperation) && !(operation is EFT.InventoryLogic.Operations.ITwoItemOperation))
                {
                    var innerOp = OperationUnwrapper.TryGetInnerOperation(operation);
                    if (innerOp != null && !ReferenceEquals(innerOp, operation))
                    {
                        Evaluate(innerOp, ref result, patchName + ">" + operation.GetType().Name, result);
                        return;
                    }

                    // Truly unhandled and not unwrappable — log its fields
                    // once so a future round can see exactly what data it
                    // actually carries, instead of guessing again.
                    OperationUnwrapper.LogUnknownOperationFields(operation);
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError($"LevelGate {patchName} error: " + e);
                result = false; // never let a bug here accidentally permit a restricted action
            }
        }

        private static bool TryBlockEquip(
            EFT.InventoryLogic.Item item,
            EFT.InventoryLogic.ItemAddress to,
            EFT.Player player,
            out int required)
        {
            required = 0;
            if (item == null) return false;
            if (!(to is EFT.InventoryLogic.SlotItemAddress slotAddress)) return false;
            if (!LevelGateCheck.EquipSlotIds.Contains(slotAddress.Slot.ID)) return false;
            return LevelGateCheck.IsBlocked(player, item.TemplateId, out required);
        }

        private static bool TryBlockAmmoLoad(
            EFT.InventoryLogic.Item item,
            EFT.InventoryLogic.ItemAddress to,
            EFT.Player player,
            string patchName,
            EFT.InventoryLogic.Operations.AbstractOperation operation,
            out int required)
        {
            required = 0;
            if (!(item is EFT.InventoryLogic.Ammo)) return false;

            bool intoGun = LevelGateCheck.IsLoadIntoWeaponOrMagazine(to);
            var parent = LevelGateCheck.GetContainerParentItem(to);
            DiagnosticLogging.LogCall(
                $"{patchName} AMMO op={operation.GetType().Name} ammo={item.TemplateId} " +
                $"toType={to?.GetType().Name ?? "null"} toParent={parent?.GetType().Name ?? "null"} intoGun={intoGun}");

            if (!intoGun) return false;
            return LevelGateCheck.IsBlocked(player, item.TemplateId, out required);
        }
    }

    // General-purpose fallback for any AbstractOperation subclass that
    // doesn't implement IOneItemOperation/ITwoItemOperation and isn't
    // Eat/HealOperation — either unwraps it if it's a thin wrapper around
    // another operation (like LoadMagOperation), or logs its full field
    // layout via reflection so future investigation doesn't need another
    // round of raw IL analysis.
    internal static class OperationUnwrapper
    {
        private static readonly Dictionary<Type, FieldInfo> _innerOpFieldCache = new Dictionary<Type, FieldInfo>();

        public static EFT.InventoryLogic.Operations.AbstractOperation TryGetInnerOperation(
            EFT.InventoryLogic.Operations.AbstractOperation operation)
        {
            try
            {
                var type = operation.GetType();
                if (!_innerOpFieldCache.TryGetValue(type, out var field))
                {
                    field = null;
                    var t = type;
                    while (t != null && field == null)
                    {
                        foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                        {
                            if (typeof(EFT.InventoryLogic.Operations.AbstractOperation).IsAssignableFrom(f.FieldType))
                            {
                                field = f;
                                break;
                            }
                        }
                        t = t.BaseType;
                    }
                    _innerOpFieldCache[type] = field;
                }

                return field?.GetValue(operation) as EFT.InventoryLogic.Operations.AbstractOperation;
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate OperationUnwrapper error: " + e);
                return null;
            }
        }

        public static void LogUnknownOperationFields(EFT.InventoryLogic.Operations.AbstractOperation operation)
        {
            try
            {
                var type = operation.GetType();
                var sb = new System.Text.StringBuilder();
                sb.Append("Unhandled operation type ").Append(type.FullName).Append(" fields: ");
                var t = type;
                while (t != null)
                {
                    foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        object val;
                        try { val = f.GetValue(operation); }
                        catch { val = "<error reading value>"; }
                        sb.Append(f.Name).Append('(').Append(f.FieldType.Name).Append(")=").Append(val).Append("; ");
                    }
                    t = t.BaseType;
                }
                DiagnosticLogging.LogCall(sb.ToString());
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate LogUnknownOperationFields error: " + e);
            }
        }
    }

    // -----------------------------------------------------------------
    // PATCH — the actual in-raid controller.
    //
    // EFT.Player has a nested class "PlayerInventoryController" (a
    // different class from ClientPlayerInventoryController/
    // PlayerOwnerInventoryController used by the stash/hideout screens),
    // and it OVERRIDES CanExecute(AbstractOperation) with its own
    // implementation. That override is confirmed via IL to exist
    // (declared directly in PlayerInventoryController's own method list),
    // which is exactly why the ItemController.CanExecute patch above
    // — while correct for the stash/hideout — never fired for real
    // in-raid actions: virtual dispatch calls this override instead.
    //
    // PlayerInventoryController is nested with no fixed enclosing-type
    // path safe to hardcode (same situation as LoadMagazineProcess), so
    // it's located at runtime by simple type name and patched manually.
    // -----------------------------------------------------------------
    internal static class Patch_BlockOperationInRaid
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                var type = AccessTools.AllTypes()
                    .FirstOrDefault(t => t.Name == "PlayerInventoryController");

                if (type == null)
                {
                    LevelGatePlugin.Log.LogWarning(
                        "LevelGate: could not find PlayerInventoryController — in-raid equip/throw restriction not applied.");
                    return;
                }

                var method = AccessTools.Method(type, "CanExecute", new[] { typeof(EFT.InventoryLogic.Operations.AbstractOperation) });
                if (method == null)
                {
                    LevelGatePlugin.Log.LogWarning(
                        "LevelGate: found PlayerInventoryController but not its CanExecute(AbstractOperation) override — in-raid equip/throw restriction not applied.");
                    return;
                }

                var prefixMethod = typeof(Patch_BlockOperationInRaid).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(method, prefix: new HarmonyMethod(prefixMethod) { priority = Priority.First });
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to patch PlayerInventoryController.CanExecute. " + e);
            }
        }

        // "__0" is Harmony's positional-parameter convention — used instead of a
        // named parameter here since we don't have compile-time certainty of the
        // override's exact parameter name.
        private static bool Prefix(object __instance, EFT.InventoryLogic.Operations.AbstractOperation __0, ref bool __result)
        {
            // Every bot has its own PlayerInventoryController and reloads
            // through this same override — only ever evaluate the local
            // player's own controller.
            if (!LevelGateCheck.IsOwnInventoryController(__instance)) return true;

            bool allowed = true;
            Patch_BlockOperation.Evaluate(__0, ref allowed, "Patch_BlockOperationInRaid", startingResult: true);
            if (!allowed)
            {
                __result = false;
                return false; // skip original — see comment on Patch_BlockOperation.Prefix for why
            }
            return true;
        }
    }

    // Context-menu-triggered actions (right-click "Eat"/"Drink"/"Apply") don't
    // go through ItemController.CanExecute at all — see the comment above.
    [HarmonyPatch(typeof(EFT.InventoryLogic.ItemContext), "IsOperationAllowed")]
    internal static class Patch_BlockContextAction
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(EFT.InventoryLogic.ItemContext __instance, ref bool __result)
        {
            try
            {
                if (!__result) return;

                var item = __instance.Item;
                if (item == null) return;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return;

                if (LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);
                    __result = false;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_BlockContextAction error: " + e);
            }
        }
    }

    // -----------------------------------------------------------------
    // The most reliable eat/drink/med hook: EatOperation.ExecuteInternal
    // and HealOperation.ExecuteInternal directly. Both are void-returning
    // (confirmed via IL — not async, unlike Move/Throw's Task<IResult>),
    // so skipping them outright is safe: there's no pending Task to
    // satisfy and no equivalent of the equip/weapon-switch animation
    // state machine to leave stuck.
    //
    // This exists because ItemContext.IsOperationAllowed and
    // ItemController.CanExecute both proved unreliable for this
    // specifically: ExecutePossibleAction's two trailing bool parameters
    // strongly suggest one execution branch is a preview/"simulate" check
    // and the real click-to-eat path may skip re-validating through
    // IsOperationAllowed entirely. ExecuteInternal is the one place any
    // eat/drink/heal action has to pass through no matter how it was
    // triggered (double-click, context-menu button, or a hotbar quick-use
    // bind), so it doesn't depend on figuring out every possible entry
    // point.
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.InventoryLogic.Operations.EatOperation), "ExecuteInternal")]
    internal static class Patch_BlockEatDirect
    {
        static bool Prefix(EFT.InventoryLogic.Operations.EatOperation __instance)
        {
            return !ConsumableBlockShared.TryBlockConsumable(ConsumableFieldCache.EatItemField, __instance, "Patch_BlockEatDirect");
        }
    }

    [HarmonyPatch(typeof(EFT.InventoryLogic.Operations.HealOperation), "ExecuteInternal")]
    internal static class Patch_BlockHealDirect
    {
        static bool Prefix(EFT.InventoryLogic.Operations.HealOperation __instance)
        {
            return !ConsumableBlockShared.TryBlockConsumable(ConsumableFieldCache.HealItemField, __instance, "Patch_BlockHealDirect");
        }
    }

    internal static class ConsumableBlockShared
    {
        /// <summary>Returns true if the operation was blocked (and Prefix should skip original).</summary>
        public static bool TryBlockConsumable(FieldInfo itemField, object operationInstance, string patchName)
        {
            try
            {
                var item = itemField?.GetValue(operationInstance) as EFT.InventoryLogic.Item;
                DiagnosticLogging.LogCall($"{patchName} item={(item == null ? "NULL" : item.TemplateId)}");
                if (item == null) return false;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return false;

                if (LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);
                    return true;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError($"LevelGate {patchName} error: " + e);
            }

            return false;
        }
    }

    internal static class BindItemFieldCache
    {
        public static readonly FieldInfo ItemField =
            ResolveAndLog(typeof(EFT.InventoryLogic.Operations.BindItemOperation), "_item", "BindItemOperation");

        private static FieldInfo ResolveAndLog(Type type, string fieldName, string label)
        {
            var field = AccessTools.Field(type, fieldName);
            if (field == null)
            {
                LevelGatePlugin.Log.LogWarning(
                    $"LevelGate: could not find field '{fieldName}' on {label} — hotbar-bind blocking will not work.");
            }
            return field;
        }
    }

    internal static class ConsumableFieldCache
    {
        public static readonly FieldInfo EatItemField = ResolveAndLog(
            typeof(EFT.InventoryLogic.Operations.EatOperation), "_item", "EatOperation");

        public static readonly FieldInfo HealItemField = ResolveAndLog(
            typeof(EFT.InventoryLogic.Operations.HealOperation), "_item", "HealOperation");

        private static FieldInfo ResolveAndLog(Type type, string fieldName, string label)
        {
            var field = AccessTools.Field(type, fieldName);
            if (field == null)
            {
                LevelGatePlugin.Log.LogWarning(
                    $"LevelGate: could not find field '{fieldName}' on {label} — eat/heal blocking via ExecuteInternal will not work for this type.");
            }
            return field;
        }
    }

    // -----------------------------------------------------------------
    // PATCH 6 — block throwing a gated grenade.
    //
    // First attempt patched CanThrow() — it turned out to be dead code:
    // confirmed via IL that it has ZERO callers anywhere in the assembly
    // (not even through the shared IHandsController interface it
    // implements, which is ALSO never called directly — grenade input is
    // evidently wired through Unity's input-binding system rather than
    // plain method calls, invisible to static IL tracing). A diagnostic
    // stack-trace logger confirmed this empirically too: it never fired
    // once during real testing.
    //
    // The real, verified entry point is
    // EFT.ClientGrenadeHandsController.Cook() — "pull the pin" — which has
    // 4 real callers in the IL. Blocking here means the grenade never
    // even gets armed, so the throw sequence never has a chance to start.
    // ClientGrenadeHandsController is the actual controller used for the
    // local player and is a normal, non-nested class — no reflection
    // needed. The item is read via the inherited public Item property.
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.ClientGrenadeHandsController), "Cook")]
    internal static class Patch_BlockGrenadeCook
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.ClientGrenadeHandsController __instance)
        {
            try
            {
                var item = __instance.Item;
                if (item == null)
                {
                    DiagnosticLogging.LogCall("ClientGrenadeHandsController.Cook (no Item)");
                    return true;
                }

                var player = LevelGateCheck.GetMainPlayer();
                DiagnosticLogging.LogCall($"ClientGrenadeHandsController.Cook item={item.TemplateId} player={(player == null ? "NULL" : "found")}");
                if (player == null) return true;

                if (LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);
                    return false; // skip Cook entirely — the grenade never gets armed
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_BlockGrenadeCook error: " + e);
            }

            return true;
        }
    }

    // -----------------------------------------------------------------
    // PATCH 7 — the real, foundational hook for food/drink/meds/stims:
    // EFT.HealthSystem.*.ApplyItem(Item, ..., float) -> bool.
    //
    // EatOperation and HealOperation (patched earlier) turned out
    // insufficient: tracing a third-party mod (a "make loose/degraded food
    // and meds usable" patch) showed it calls
    // player.HealthController.ApplyItem(item, ...) DIRECTLY, completely
    // bypassing EatOperation/HealOperation/CanExecute/IsOperationAllowed —
    // every hook this plugin had. ApplyItem is the one place that
    // actually applies an item's effect to the player's health system, so
    // it's called by every path (vanilla or modded) that makes an item
    // do anything — a more fundamental choke point than any of the
    // operation classes, the same role Magazine.ApplyWithoutRestrictions
    // played for ammo.
    //
    // There are two overloads (single body part / list of body parts) and
    // three classes that each declare their own implementation
    // (PlayerHealthController, ClientPlayerHealthController,
    // OfflineHealthController — SPT being offline-only makes the last one
    // likely the one that actually matters, but all three are patched for
    // safety, the same lesson learned from CanExecute/LoadMagazine). All
    // three are normal, non-nested classes in EFT.HealthSystem, so no
    // reflection is needed here — plain [HarmonyPatch] attributes work.
    // -----------------------------------------------------------------
    [HarmonyPatch]
    internal static class Patch_BlockApplyItem
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var types = new[]
            {
                typeof(EFT.HealthSystem.PlayerHealthController),
                typeof(EFT.HealthSystem.ClientPlayerHealthController),
                typeof(EFT.HealthSystem.OfflineHealthController),
            };

            var singleBodyPartSig = new[] { typeof(EFT.InventoryLogic.Item), typeof(EBodyPart), typeof(float) };
            var listBodyPartSig = new[] { typeof(EFT.InventoryLogic.Item), typeof(EFT.NetworkPackets.OneAndList<EBodyPart>), typeof(float) };

            foreach (var type in types)
            {
                var m1 = AccessTools.Method(type, "ApplyItem", singleBodyPartSig);
                if (m1 != null) yield return m1;
                else LevelGatePlugin.Log.LogWarning($"LevelGate: could not find {type.Name}.ApplyItem(Item, EBodyPart, float).");

                var m2 = AccessTools.Method(type, "ApplyItem", listBodyPartSig);
                if (m2 != null) yield return m2;
                else LevelGatePlugin.Log.LogWarning($"LevelGate: could not find {type.Name}.ApplyItem(Item, OneAndList<EBodyPart>, float).");
            }
        }

        [HarmonyPriority(Priority.First)]
        static bool Prefix(object[] __args, ref bool __result)
        {
            try
            {
                var item = __args.OfType<EFT.InventoryLogic.Item>().FirstOrDefault();
                if (item == null)
                {
                    DiagnosticLogging.LogCall("ApplyItem (no Item arg found)");
                    return true;
                }

                var player = LevelGateCheck.GetMainPlayer();
                bool inConfig = LevelGatePlugin.Data.Items.TryGetValue(item.TemplateId, out int configuredLevel);
                int currentLevel = player?.Profile?.Info?.Level ?? -1;
                DiagnosticLogging.LogCall(
                    $"ApplyItem item={item.TemplateId} player={(player == null ? "NULL" : "found")} " +
                    $"inConfig={inConfig} configuredLevel={configuredLevel} currentLevel={currentLevel}");
                if (player == null) return true;

                if (LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);
                    __result = false;
                    return false; // skip original — nothing gets applied
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_BlockApplyItem error: " + e);
            }

            return true;
        }
    }

    // -----------------------------------------------------------------
    // PATCH 8 — manual chamber-loading (e.g. tube-fed shotguns like the
    // MP-153, loaded one shell at a time with R).
    //
    // This turned out to be a THIRD distinct validation path, separate
    // from both the AbstractOperation/CanExecute system and the
    // LoadMagazine/LoadWeaponWithAmmo methods patched above. Tracing
    // FirearmHandsInputTranslator.LoadAmmoToChamber's IL showed it calls
    // the static EFT.InventoryLogic.ItemManipulator.Move(...) helper to
    // compute a MoveResult, then checks MoveResult.CanExecute(ItemController)
    // — a bool method taking the controller as its only argument, with the
    // actual item/destination read from the MoveResult instance's own
    // public Item/To properties. MoveResult is a normal, non-nested class,
    // so no reflection is needed.
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.InventoryLogic.MoveResult), "CanExecute")]
    internal static class Patch_BlockMoveResult
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.InventoryLogic.MoveResult __instance, object[] __args, ref bool __result)
        {
            try
            {
                // CanExecute(ItemController) — skip bots' controllers.
                if (__args != null && __args.Length > 0 && !LevelGateCheck.IsOwnInventoryController(__args[0]))
                    return true;

                var item = __instance.Item;
                if (item == null)
                {
                    DiagnosticLogging.LogCall("MoveResult.CanExecute (no Item)");
                    return true;
                }

                string toType = __instance.To?.GetType().Name ?? "null";
                DiagnosticLogging.LogCall($"MoveResult.CanExecute item={item.TemplateId} isAmmo={item is EFT.InventoryLogic.Ammo} toType={toType}");

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return true;

                // A chamber/cylinder destination for ammo is a
                // SlotItemAddress (not one of the top-level wearable slot
                // IDs in EquipSlotIds, just some other named slot) — that
                // part was already correct. But a TUBE magazine (shotguns
                // like the MP-153) turned out to use a GridItemAddress
                // instead, confirmed via diagnostic logging showing
                // "toType=ProtectedGridItemAddress" for ammo during a real
                // reload — makes sense, since a tube holds multiple shells
                // in a row, modeled as a small grid rather than one named
                // slot. Moving ammo to the stash/backpack ALSO uses a
                // GridItemAddress, so address type alone can't tell them
                // apart; GridItemAddress.Grid.ParentItem can, though.
                //
                // First attempt only checked for ParentItem being a
                // Magazine — that covers a detachable box magazine, but a
                // TRUE internal/integral magazine (a shotgun tube, or a
                // bolt-action's built-in magazine, confirmed still
                // bypassing the restriction with that narrower check) has
                // no separate Magazine item at all — the grid's parent is
                // the Weapon itself. Checking for either covers both.
                //
                // Box-magazine cartridges are a StackSlot
                // (StackSlotItemAddress), which neither check above
                // matched, so the ammo case now uses the shared
                // IsLoadIntoWeaponOrMagazine() rule (slot, stack slot or
                // grid whose parent is a Weapon or Magazine) — the same one
                // the in-raid CanExecute patch uses.
                bool shouldCheck;
                if (item is EFT.InventoryLogic.Ammo)
                {
                    shouldCheck = LevelGateCheck.IsLoadIntoWeaponOrMagazine(__instance.To);
                }
                else
                {
                    shouldCheck = __instance.To is EFT.InventoryLogic.SlotItemAddress slotAddress
                        && LevelGateCheck.EquipSlotIds.Contains(slotAddress.Slot.ID);
                }

                if (shouldCheck && LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);
                    __result = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_BlockMoveResult error: " + e);
            }

            return true;
        }
    }


    // -----------------------------------------------------------------
    // PATCH 9 — rename restricted items in the UI to show they're locked
    // and at what level they unlock.
    //
    // First attempt patched Item.Name / Item.ShortName directly and
    // prepended the label to the returned string — this broke display
    // entirely. Turns out those properties don't return display text at
    // all: they return a lookup KEY (literally "<TemplateId> Name", the
    // same string used as the key in en.json), which something else then
    // resolves via EFT.LocalizationExtensions.Localized(key, ...) to get
    // the actual text shown on screen. Modifying the key broke the
    // lookup, so the UI fell back to showing the raw, unresolved key
    // (visible in testing as literal text like
    // "5580223e4bdc2d1c128b457f Name" appearing in-game).
    //
    // The fix: patch Localized(string, string) and Localized(string,
    // EStringCase) — the actual resolvers — and parse the KEY (which is
    // stable and always ends in " Name" or " ShortName") to find the
    // TemplateId, then prepend the label to the RESOLVED text instead.
    // Since our modified output no longer matches any registered key, if
    // anything tries to re-localize it further, Localized()'s own
    // no-match fallback (return the input unchanged, the same behavior
    // that surfaced the raw key in the original bug) leaves it alone.
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.LocalizationExtensions), "Localized", new[] { typeof(string), typeof(string) })]
    internal static class Patch_RenameLockedItem_LocalizedA
    {
        // Harmony binds by parameter NAME to the original method — the real
        // signature is Localized(string id, string prefix), so this MUST be
        // named "id" (not "key" as an earlier version had it, which crashed
        // Harmony's patching at startup with "Parameter 'key' not found").
        static void Postfix(string id, ref string __result)
        {
            ItemRenameShared.ApplyLockedLabelByKey(id, ref __result);
        }
    }

    [HarmonyPatch(typeof(EFT.LocalizationExtensions), "Localized", new[] { typeof(string), typeof(EFT.EStringCase) })]
    internal static class Patch_RenameLockedItem_LocalizedB
    {
        static void Postfix(string id, ref string __result)
        {
            ItemRenameShared.ApplyLockedLabelByKey(id, ref __result);
        }
    }

    internal static class ItemRenameShared
    {
        // Keys look like "<TemplateId> Name" or "<TemplateId> ShortName" —
        // the exact format used in en.json. Only these two suffixes are
        // touched; "Description" and anything else is left alone.
        // ShortName gets a bare "[LOCKED]" (it's shown in tight spaces like
        // grid cells); the full Name gets the level detail too.
        private static readonly (string Suffix, bool ShowLevel)[] NameSuffixes =
        {
            (" ShortName", false),
            (" Name", true),
        };

        public static void ApplyLockedLabelByKey(string key, ref string result)
        {
            try
            {
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(result)) return;

                string templateId = null;
                bool showLevel = false;
                foreach (var (suffix, level) in NameSuffixes)
                {
                    if (key.EndsWith(suffix, StringComparison.Ordinal))
                    {
                        templateId = key.Substring(0, key.Length - suffix.Length);
                        showLevel = level;
                        break;
                    }
                }
                if (templateId == null) return;

                // Not in the config at all — nothing to label either way.
                if (!LevelGatePlugin.Data.Items.TryGetValue(templateId, out int required)) return;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return;

                if (LevelGateCheck.IsBlocked(player, templateId, out required))
                {
                    result = showLevel
                        ? $"[LOCKED - Lvl {required}] {result}"
                        : "[LOCKED]";
                }
                else
                {
                    // In the config, but the level requirement is now met —
                    // label it UNLOCKED instead of silently going back to
                    // the plain original name, so it's clear at a glance
                    // this item is still tracked by the level limiter.
                    result = showLevel
                        ? $"[UNLOCKED - Lvl {required}] {result}"
                        : "[UNLOCKED]";
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate ItemRenameShared error: " + e);
            }
        }
    }

    // -----------------------------------------------------------------
    // PATCH 15 — the definitive fix for grenades (and a strong additional
    // layer for meds/food/hotbar quick-use): EFT.Player.Proceed.
    //
    // Found by decoding a real, working mod (FastGrenadeThrow.dll) that
    // programmatically triggers a grenade throw — it calls exactly this
    // method. Proceed turns out to be a single, central dispatch gateway
    // with a separate overload for every kind of "put this item into
    // active use" action in the whole game: equipping a weapon, throwing
    // a grenade (both the normal hold-to-cook throw AND the G-key quick
    // throw have their own overloads), using a med, eating/drinking, and
    // hotbar quick-use — each confirmed via IL to have real, non-zero
    // call counts. This is a far more fundamental hook than anything else
    // tried for grenades (CanThrow, Cook, IHandsController.CanExecute,
    // BindItemOperation — all either dead code or not applicable), and
    // gives meds/food/hotbar a second layer beneath the operation-level
    // and brute-force-neutralization patches already in place.
    //
    // All overloads are void, so skipping them outright (Prefix returns
    // false) is safe — no return value to fake.
    // -----------------------------------------------------------------
    internal static class ProceedBlockShared
    {
        public static bool ShouldBlock(EFT.InventoryLogic.Item item)
        {
            try
            {
                if (item == null) return false;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return false;

                if (LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);
                    return true;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate ProceedBlockShared error: " + e);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(EFT.Player), "Proceed", new[] { typeof(EFT.InventoryLogic.ThrowWeap), typeof(Comfort.Common.Callback<EFT.IGrenadeController>), typeof(bool) })]
    internal static class Patch_BlockProceedGrenade
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.InventoryLogic.ThrowWeap throwWeap) => !ProceedBlockShared.ShouldBlock(throwWeap);
    }

    [HarmonyPatch(typeof(EFT.Player), "Proceed", new[] { typeof(EFT.InventoryLogic.ThrowWeap), typeof(Comfort.Common.Callback<EFT.IQuickGrenadeThrowController>), typeof(bool) })]
    internal static class Patch_BlockProceedQuickGrenade
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.InventoryLogic.ThrowWeap throwWeap) => !ProceedBlockShared.ShouldBlock(throwWeap);
    }

    [HarmonyPatch(typeof(EFT.Player), "Proceed", new[] { typeof(EFT.InventoryLogic.Meds), typeof(EFT.NetworkPackets.OneAndList<EBodyPart>), typeof(Comfort.Common.Callback<EFT.IMedsController>), typeof(int), typeof(bool) })]
    internal static class Patch_BlockProceedMeds
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.InventoryLogic.Meds meds) => !ProceedBlockShared.ShouldBlock(meds);
    }

    [HarmonyPatch(typeof(EFT.Player), "Proceed", new[] { typeof(EFT.InventoryLogic.FoodDrink), typeof(float), typeof(Comfort.Common.Callback<EFT.IMedsController>), typeof(int), typeof(bool) })]
    internal static class Patch_BlockProceedFoodDrink
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.InventoryLogic.FoodDrink foodDrink) => !ProceedBlockShared.ShouldBlock(foodDrink);
    }

    [HarmonyPatch(typeof(EFT.Player), "Proceed", new[] { typeof(EFT.InventoryLogic.Item), typeof(Comfort.Common.Callback<EFT.IQuickUseItem>), typeof(bool) })]
    internal static class Patch_BlockProceedQuickUse
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.InventoryLogic.Item item) => !ProceedBlockShared.ShouldBlock(item);
    }


    // -----------------------------------------------------------------
    // PATCH 14 — block firing when the chambered/next round is a
    // restricted ammo type (covers a gun picked up already loaded with
    // gated ammo, where the weapon itself is fine but its ammo isn't).
    //
    // EFT.ClientFirearmController — the real local-player firing
    // controller, a normal accessible class overriding the virtual
    // CanPressTrigger() from the base (nested) FirearmController — has a
    // confirmed-real, non-zero-caller CanPressTrigger() gate, and the base
    // class holds a "_preallocatedAmmoList" field (a List<Ammo>) with the
    // same pre-fetch-before-use pattern already verified working for the
    // shotgun chamber-load fix. This is more speculative than most of this
    // plugin — the firing/ballistics system is frame-critical, real-time
    // code this project hasn't touched before, so this is a best-effort
    // attempt taken on at the user's explicit request to accept more risk,
    // not a fully verified fix the way the equip/ammo-loading patches are.
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.ClientFirearmController), "CanPressTrigger")]
    internal static class Patch_BlockFireRestrictedAmmo
    {
        private static FieldInfo _ammoListField;

        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.ClientFirearmController __instance, ref bool __result)
        {
            try
            {
                if (!LevelGateCheck.IsOwnHandsController(__instance)) return true;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return true;

                // Prefer the round actually sitting in the chamber(s):
                // _preallocatedAmmoList is a reusable buffer refilled while
                // firing, so at prefix time it holds the PREVIOUS shot's
                // rounds (the same stale-buffer problem LoadAmmoToChamber
                // had) — it let the first gated shot through and, once it
                // held a gated round, kept blocking even after reloading
                // with allowed ammo. Only fall back to the buffer if the
                // chambers can't be read.
                System.Collections.IEnumerable rounds = ChamberedRounds(__instance);
                if (rounds == null)
                {
                    if (_ammoListField == null)
                    {
                        _ammoListField = AccessTools.Field(typeof(EFT.ClientFirearmController), "_preallocatedAmmoList");
                    }
                    rounds = _ammoListField?.GetValue(__instance) as System.Collections.IEnumerable;
                }
                if (rounds == null) return true;

                foreach (var obj in rounds)
                {
                    if (obj is EFT.InventoryLogic.Ammo ammo &&
                        LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                    {
                        LevelGateCheck.Notify(required);
                        __result = false;
                        return false; // skip original — trigger can't be pressed
                    }
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_BlockFireRestrictedAmmo error: " + e);
            }

            return true;
        }

        // Weapon.Chambers (Slot[]) -> each Slot.ContainedItem. Returns null
        // if any of those members can't be resolved on this game version.
        private static System.Collections.IEnumerable ChamberedRounds(EFT.ClientFirearmController controller)
        {
            var weapon = ReflectionUtil.GetMember(controller, "Item") as EFT.InventoryLogic.Weapon;
            if (!(ReflectionUtil.GetMember(weapon, "Chambers") is System.Collections.IEnumerable chambers)) return null;

            var rounds = new List<EFT.InventoryLogic.Ammo>();
            foreach (var slot in chambers)
            {
                if (ReflectionUtil.GetMember(slot, "ContainedItem") is EFT.InventoryLogic.Ammo ammo) rounds.Add(ammo);
            }
            return rounds;
        }
    }


    // -----------------------------------------------------------------
    // PATCH 13 — an in-game right-click context menu action to add/remove
    // an item from the level limiter, with a level picker (1-60), instead
    // of requiring the F9 menu or hand-editing the config file. See the
    // comment on the Postfix below for exactly which method and mechanism
    // this uses — verified against QuickSellFlea.dll, a mod confirmed to
    // add its own custom context-menu action ("Sell to Flea") the same
    // way, so this coexists with other mods' own additions rather than
    // replacing or conflicting with them.
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.UI.ItemUiContext), "GetItemContextInteractions")]
    internal static class Patch_AddLevelLimitContextMenu
    {
        // ItemUiContext.GetItemContextInteractions is the real, actively
        // used (9 confirmed real callers vs. 1 for the base class this
        // project tried first, which caused the build errors) builder for
        // an item's stash right-click menu — confirmed by checking
        // QuickSellFlea.dll, which patches this exact method for its own
        // "Sell to Flea" action. Its return type,
        // ContextInteractions<EItemInfoButton>, has a public
        // "_dynamicInteractions" dictionary (Dictionary<string,
        // DynamicContextInteraction>) specifically for adding custom
        // actions alongside the fixed EItemInfoButton enum-driven ones —
        // exactly the mechanism a mod like QuickSellFlea or UseLooseLoot
        // uses to inject something not in that enum.
        [HarmonyPriority(Priority.Last)]
        static void Postfix(
            EFT.InventoryLogic.ItemContext itemContext,
            ref EFT.UI.ContextInteractions<EFT.InventoryLogic.EItemInfoButton> __result)
        {
            try
            {
                var item = itemContext?.Item;
                DiagnosticLogging.LogCall($"AddLevelLimitContextMenu item={(item == null ? "NULL" : item.TemplateId)} result={(__result == null ? "NULL" : "found")}");
                if (item == null || __result == null) return;

                string templateId = item.TemplateId;
                bool inConfig = LevelGatePlugin.Data.Items.ContainsKey(templateId);
                string key = "LevelGate_" + templateId;
                string label = inConfig ? "Level Limit Remove" : "Level Limit";

                System.Action callback = () =>
                {
                    if (inConfig)
                    {
                        LevelLimiterContextMenu.RemoveLimit(templateId);
                    }
                    else
                    {
                        LevelLimiterContextMenu.OpenPicker(templateId, item.ShortName);
                    }
                };

                __result._dynamicInteractions[key] =
                    new EFT.UI.DynamicContextInteraction(key, label, callback, null);
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_AddLevelLimitContextMenu error: " + e);
            }
        }
    }

    internal static class LevelLimiterContextMenu
    {
        private static string _pendingTemplateId;
        private static string _pendingDisplayName;
        private static int _pendingLevel = 1;
        private static Rect _windowRect = new Rect(0, 0, 320, 170);
        private static bool _rectInitialized;

        public static void OpenPicker(string templateId, string displayName)
        {
            _pendingTemplateId = templateId;
            _pendingDisplayName = displayName;
            _pendingLevel = 1;
        }

        public static void RemoveLimit(string templateId)
        {
            try
            {
                LevelGatePlugin.Data.Items.Remove(templateId);
                LevelGatePlugin.SaveConfig();
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate RemoveLimit error: " + e);
            }
        }

        public static void DrawPickerIfOpen()
        {
            if (_pendingTemplateId == null) return;

            if (!_rectInitialized)
            {
                _windowRect.x = (Screen.width - _windowRect.width) / 2f;
                _windowRect.y = (Screen.height - _windowRect.height) / 2f;
                _rectInitialized = true;
            }

            _windowRect = GUI.Window(
                "LevelLimiterContextMenu".GetHashCode(),
                _windowRect,
                DrawWindow,
                "Set level limit");
        }

        private static void DrawWindow(int id)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            GUILayout.BeginVertical();
            GUILayout.Label($"Item: {_pendingDisplayName}");
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Required level:", GUILayout.Width(100));
            GUILayout.Label(_pendingLevel.ToString(), GUILayout.Width(30));
            GUILayout.EndHorizontal();

            _pendingLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(_pendingLevel, 1, 60));

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm"))
            {
                try
                {
                    LevelGatePlugin.Data.Items[_pendingTemplateId] = _pendingLevel;
                    LevelGatePlugin.SaveConfig();
                }
                catch (Exception e)
                {
                    LevelGatePlugin.Log.LogError("LevelGate: failed to save level limit from context menu. " + e);
                }
                _pendingTemplateId = null;
                _rectInitialized = false;
            }
            if (GUILayout.Button("Cancel"))
            {
                _pendingTemplateId = null;
                _rectInitialized = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }


    // -----------------------------------------------------------------
    // PATCH 11 — turn restricted items' inventory-grid background color
    // red, so they stand out visually in addition to the name label.
    //
    // EFT.InventoryLogic.Item.BackgroundColor returns a JsonType.TaxonomyColor
    // enum (the same category-coloring system that makes ammo green, meds
    // blue, keys black, etc. in the grid) — declared once on the base Item
    // class like Name/ShortName, so patching it here covers every item
    // type uniformly.
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.InventoryLogic.Item), "get_BackgroundColor")]
    internal static class Patch_RedBackgroundLockedItem
    {
        static void Postfix(EFT.InventoryLogic.Item __instance, ref JsonType.TaxonomyColor __result)
        {
            try
            {
                if (__instance == null) return;

                bool inConfig = LevelGatePlugin.Data.Items.ContainsKey(__instance.TemplateId);
                if (!inConfig) return;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return;

                bool blocked = LevelGateCheck.IsBlocked(player, __instance.TemplateId, out _);
                __result = blocked ? JsonType.TaxonomyColor.red : JsonType.TaxonomyColor.green;

                // BackgroundColor is queried constantly for every item shown
                // anywhere in the UI (grid, tooltip, hover, etc.), which
                // makes this a convenient, always-firing hook to also keep
                // the brute-force neutralization below in sync with the
                // current block state.
                ItemNeutralizer.Sync(__instance, blocked);
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_RedBackgroundLockedItem error: " + e);
            }
        }
    }

    // -----------------------------------------------------------------
    // PATCH 12 — brute-force neutralization at the item's own data, for
    // ammo/meds/food specifically, per explicit request: rather than keep
    // chasing every possible entry point that can trigger these items
    // (many of which turned out to be wired through Unity's input system
    // and invisible to static analysis — see PATCH 6/10's history), make
    // the game's OWN native systems refuse to use them, the same way they
    // already refuse real caliber mismatches or empty consumables.
    //
    // - Ammo: EFT.InventoryLogic.AmmoTemplate.Caliber is a public STRING
    //   FIELD (not a template asset — an actual game object field), shared
    //   by every instance of that ammo type. Setting it to a value that
    //   matches no real weapon/magazine caliber makes every native
    //   compatibility check reject it uniformly — manual reload, hotbar,
    //   magazine fill, drag-and-drop — without needing to find each one.
    //
    // - Meds: EFT.InventoryLogic.MedKitComponent.HpResource and
    //   FoodDrink: EFT.InventoryLogic.FoodDrinkComponent.HpPercent are
    //   public float fields tracking the REMAINING amount on that specific
    //   item INSTANCE (not the shared template) — zeroing them makes the
    //   game think the item is already empty, the same ordinary state a
    //   legitimately-used-up medkit or empty water bottle is already in
    //   (so it doesn't rely on any untested code path).
    //
    // All three cache the original value the first time an item is locked
    // and restore it once the player reaches the required level, so nothing
    // is lost — an item just becomes usable again exactly as it was.
    //
    // Grenades are NOT included here: their equivalent lever (an enum,
    // ThrowWeapTemplate.ThrowType) risks undefined behavior if game code
    // switches on it expecting only real values, unlike a string caliber
    // mismatch or an empty resource, which are both routine game states.
    // Grenades still rely on the Cook() patch instead (PATCH 6).
    // -----------------------------------------------------------------
    internal static class ItemNeutralizer
    {
        private const string LockedCaliberMarker = "LevelGateLocked_Incompatible";

        private static readonly Dictionary<string, string> _originalCalibers = new Dictionary<string, string>();
        private static readonly Dictionary<object, float> _originalMedResource = new Dictionary<object, float>();
        private static readonly Dictionary<object, float> _originalFoodResource = new Dictionary<object, float>();

        public static void Sync(EFT.InventoryLogic.Item item, bool shouldBeLocked)
        {
            try
            {
                if (item is EFT.InventoryLogic.Ammo ammo)
                {
                    SyncAmmo(ammo, shouldBeLocked);
                }
                else if (item is EFT.InventoryLogic.Meds meds)
                {
                    SyncMeds(meds, shouldBeLocked);
                }
                else if (item is EFT.InventoryLogic.FoodDrink food)
                {
                    SyncFood(food, shouldBeLocked);
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate ItemNeutralizer error: " + e);
            }
        }

        private static void SyncAmmo(EFT.InventoryLogic.Ammo ammo, bool locked)
        {
            var template = ammo.AmmoTemplate;
            if (template == null) return;

            if (locked)
            {
                if (!_originalCalibers.ContainsKey(ammo.TemplateId))
                {
                    _originalCalibers[ammo.TemplateId] = template.Caliber;
                }
                if (template.Caliber != LockedCaliberMarker)
                {
                    template.Caliber = LockedCaliberMarker;
                }
            }
            else if (template.Caliber == LockedCaliberMarker
                     && _originalCalibers.TryGetValue(ammo.TemplateId, out var original))
            {
                template.Caliber = original;
            }
        }

        private static void SyncMeds(EFT.InventoryLogic.Meds meds, bool locked)
        {
            var component = meds.MedKitComponent;
            if (component == null) return;

            if (locked)
            {
                if (!_originalMedResource.ContainsKey(component))
                {
                    _originalMedResource[component] = component.HpResource;
                }
                component.HpResource = 0f;
            }
            else if (_originalMedResource.TryGetValue(component, out var original))
            {
                component.HpResource = original;
                _originalMedResource.Remove(component);
            }
        }

        private static void SyncFood(EFT.InventoryLogic.FoodDrink food, bool locked)
        {
            var component = food.FoodDrinkComponent;
            if (component == null) return;

            if (locked)
            {
                if (!_originalFoodResource.ContainsKey(component))
                {
                    _originalFoodResource[component] = component.HpPercent;
                }
                component.HpPercent = 0f;
            }
            else if (_originalFoodResource.TryGetValue(component, out var original))
            {
                component.HpPercent = original;
                _originalFoodResource.Remove(component);
            }
        }
    }


    // -----------------------------------------------------------------
    // PATCH 10 — DIAGNOSTIC ONLY. Logs a full stack trace to
    // LogOutput.log the moment each of these methods runs, to find the
    // real entry point for grenade-throwing, hotbar quick-use, and manual
    // shotgun chambering — all three of which have shown zero direct IL
    // callers anywhere in the assembly (see the comments on PATCH 6 and
    // the hotbar investigation), meaning they're almost certainly invoked
    // through Unity's input-binding system rather than a plain method
    // call. A runtime stack trace captures that regardless of how the
    // call arrived, which static IL analysis cannot.
    //
    // This patch does not block anything — it only observes and logs.
    // Once the real call chain is visible in the log, this whole section
    // should be removed (or at least silenced) since it's noisy and not
    // needed for the plugin's actual function.
    // -----------------------------------------------------------------
    internal static class DiagnosticLogging
    {
        private const int MaxLogsPerLabel = 40; // raised from 5 per request — still capped so a per-frame method can't flood the log

        private static readonly Dictionary<string, int> _countsByLabel = new Dictionary<string, int>();

        public static void LogCall(string label)
        {
            try
            {
                int count = _countsByLabel.TryGetValue(label, out var c) ? c : 0;
                if (count >= MaxLogsPerLabel) return;
                _countsByLabel[label] = count + 1;

                LevelGatePlugin.Log.LogInfo($"LevelGate DIAGNOSTIC [{label}] call #{count + 1}:\n{Environment.StackTrace}");
            }
            catch
            {
                // never let diagnostic logging itself break anything
            }
        }

        public static void ApplyAll(Harmony harmony)
        {
            // These are the hotbar/quick-use controllers — EFT.Player has
            // them nested (confirmed via a HarmonyX error naming
            // "EFT.Player+QuickUseItemController" directly). Previously
            // diagnostic-only; now blocking for real, using the corrected
            // signatures (Execute takes TWO params — IInventoryOperation
            // and a Comfort.Common.Callback — not one, which is why the
            // original attempt failed to bind at all).
            PatchHotbarMethod(harmony, "QuickUseItemController", "CanExecute",
                new[] { typeof(EFT.InventoryLogic.Operations.IInventoryOperation) }, isCanExecute: true);
            PatchHotbarMethod(harmony, "QuickUseItemController", "Execute",
                new[] { typeof(EFT.InventoryLogic.Operations.IInventoryOperation), typeof(Comfort.Common.Callback) }, isCanExecute: false);
            PatchHotbarMethod(harmony, "UsableItemController", "CanExecute",
                new[] { typeof(EFT.InventoryLogic.Operations.IInventoryOperation) }, isCanExecute: true);
            PatchHotbarMethod(harmony, "UsableItemController", "Execute",
                new[] { typeof(EFT.InventoryLogic.Operations.IInventoryOperation), typeof(Comfort.Common.Callback) }, isCanExecute: false);
        }

        private static void PatchHotbarMethod(Harmony harmony, string typeName, string methodName, Type[] paramTypes, bool isCanExecute)
        {
            try
            {
                var type = AccessTools.AllTypes().FirstOrDefault(t => t.Name == typeName);
                if (type == null)
                {
                    LevelGatePlugin.Log.LogWarning($"LevelGate: could not find type {typeName} for hotbar blocking.");
                    return;
                }

                var method = AccessTools.Method(type, methodName, paramTypes);
                if (method == null)
                {
                    LevelGatePlugin.Log.LogWarning($"LevelGate: could not find {typeName}.{methodName}({string.Join(",", paramTypes.Select(t => t.Name))}) for hotbar blocking.");
                    return;
                }

                var prefixName = isCanExecute ? nameof(HotbarCanExecutePrefix) : nameof(HotbarExecutePrefix);
                var prefixMethod = typeof(DiagnosticLogging).GetMethod(prefixName, BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(method, prefix: new HarmonyMethod(prefixMethod) { priority = Priority.First });
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError($"LevelGate: failed to patch {typeName}.{methodName} for hotbar blocking. " + e);
            }
        }

        // Both CanExecute and Execute take the operation as their first
        // argument, typed as the IInventoryOperation interface — but every
        // real operation instance is actually an AbstractOperation, which
        // is exactly what Patch_BlockOperation.Evaluate already knows how
        // to check (including unwrapping wrapper operations like
        // LoadMagOperation). Reusing it here means hotbar quick-use gets
        // the exact same restriction logic as the main equip/use path,
        // with no duplicated logic to keep in sync.
        private static bool HotbarCanExecutePrefix(object[] __args, ref bool __result)
        {
            try
            {
                LogCall("HotbarCanExecute reached");
                var operation = __args.OfType<EFT.InventoryLogic.Operations.AbstractOperation>().FirstOrDefault();
                if (operation == null) return true;

                bool allowed = true;
                Patch_BlockOperation.Evaluate(operation, ref allowed, "HotbarCanExecute", startingResult: true);
                if (!allowed)
                {
                    __result = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate HotbarCanExecutePrefix error: " + e);
            }

            return true;
        }

        private static bool HotbarExecutePrefix(object[] __args)
        {
            try
            {
                LogCall("HotbarExecute reached");
                var operation = __args.OfType<EFT.InventoryLogic.Operations.AbstractOperation>().FirstOrDefault();
                if (operation == null) return true;

                bool allowed = true;
                Patch_BlockOperation.Evaluate(operation, ref allowed, "HotbarExecute", startingResult: true);
                if (!allowed)
                {
                    return false; // skip Execute entirely — nothing happens
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate HotbarExecutePrefix error: " + e);
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(EFT.ClientGrenadeHandsController), "HighThrow")]
    internal static class Patch_DiagnosticGrenadeHighThrow
    {
        static void Prefix() => DiagnosticLogging.LogCall("ClientGrenadeHandsController.HighThrow");
    }

    [HarmonyPatch(typeof(EFT.ClientGrenadeHandsController), "PullRingForHighThrow")]
    internal static class Patch_DiagnosticGrenadePullRing
    {
        static void Prefix() => DiagnosticLogging.LogCall("ClientGrenadeHandsController.PullRingForHighThrow");
    }

    // DIAGNOSTIC ONLY now (was a blocking prefix). The prefix read
    // "_preAllocatedAmmoList", but a pre-allocated list is a reusable
    // buffer that LoadAmmoToChamber itself clears and refills DURING the
    // call — so at prefix time it held the PREVIOUS call's rounds, not the
    // ones about to be chambered. That matches the M4A1 symptom: right
    // after swapping from the Mosin the buffer was empty, so the first R
    // always got through ("works once per swap"); after that it held M855
    // from the last attempt, so later presses were blocked — and once
    // blocked, the buffer never refreshed, so it would also keep blocking
    // non-gated ammo until the next swap. The actual chambering move is
    // now refused at the inventory operation (Evaluate -> TryBlockAmmoLoad)
    // and at the FirearmController reload entry. This postfix just logs
    // what the method really picked, after it has filled the buffer.
    //
    // (The class also had the [HarmonyPatch] attribute twice — harmless,
    // but removed.)
    [HarmonyPatch(typeof(EFT.FirearmHandsInputTranslator), "LoadAmmoToChamber")]
    internal static class Patch_DiagnosticLoadAmmoToChamber
    {
        private static FieldInfo _ammoListField;

        static void Postfix(EFT.FirearmHandsInputTranslator __instance, bool __result)
        {
            try
            {
                if (_ammoListField == null)
                {
                    _ammoListField = AccessTools.Field(typeof(EFT.FirearmHandsInputTranslator), "_preAllocatedAmmoList");
                }

                var list = _ammoListField?.GetValue(__instance) as System.Collections.IEnumerable;
                var ids = list == null
                    ? "no list"
                    : string.Join(",", list.OfType<EFT.InventoryLogic.Ammo>().Select(a => a.TemplateId).Distinct());
                DiagnosticLogging.LogCall($"LoadAmmoToChamber result={__result} ammo=[{ids}]");
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_DiagnosticLoadAmmoToChamber error: " + e);
            }
        }
    }

    // Now an ACTUAL blocking patch (previously diagnostic-only), per
    // explicit request to take on more risk. The return type
    // (Diz.LanguageExtensions.OperationResult<...>) is defined in a DLL we
    // don't have, so it can't be referenced at compile time — worked
    // around using System.Runtime.Serialization.FormatterServices to build
    // an UNINITIALIZED instance of whatever the real return type actually
    // is (found via reflection once, at patch time), skipping its
    // constructor entirely. This is explicitly higher-risk than the rest
    // of this plugin: an uninitialized object has every field at
    // default(T), so if the caller does anything beyond a simple
    // null/success check on it, this could misbehave in ways we can't
    // predict without the real type's source. Tried because every safer
    // alternative for ammo found so far proved insufficient on their own.
    [HarmonyPatch(typeof(EFT.InventoryLogic.Magazine), "ApplyWithoutRestrictions")]
    internal static class Patch_BlockApplyWithoutRestrictions
    {
        private static Type _cachedReturnType;

        [HarmonyPriority(Priority.First)]
        static bool Prefix(object[] __args, ref object __result)
        {
            try
            {
                var ammo = __args.OfType<EFT.InventoryLogic.Ammo>().FirstOrDefault();
                if (ammo == null) return true;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return true;

                if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);

                    if (_cachedReturnType == null)
                    {
                        var method = AccessTools.Method(typeof(EFT.InventoryLogic.Magazine), "ApplyWithoutRestrictions");
                        _cachedReturnType = method?.ReturnType;
                    }

                    if (_cachedReturnType != null)
                    {
                        try
                        {
                            __result = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(_cachedReturnType);
                        }
                        catch (Exception ex)
                        {
                            LevelGatePlugin.Log.LogError("LevelGate: could not build uninitialized OperationResult, letting original run instead. " + ex);
                            return true;
                        }
                    }

                    return false; // skip original — nothing gets inserted
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_BlockApplyWithoutRestrictions error: " + e);
            }

            return true;
        }
    }


    // -----------------------------------------------------------------
    // PATCH 16 — internal/integral magazine reload (bolt-actions like the
    // Mosin) and single-barrel weapon reload (semi-auto/pump shotguns
    // like the MP-153) specifically. Both turned out to use yet more
    // distinct classes from everything patched so far — confirmed via a
    // user-provided mod (Continuous Load Ammo) whose own reload flow
    // calls the already-patched ItemController.LoadMagazine with the
    // exact 4-parameter signature we handle, yet diagnostic logging
    // showed zero activity for real Mosin/MP-153 reloads — meaning that
    // mod's replacement flow (and by extension our existing patches)
    // simply doesn't apply to internal/integral magazines at all.
    //
    // ReloadSingleBarrelResult.Run(IDatabaseIdGenerator, ItemController,
    // Weapon, Ammo, ItemAddress) is a STATIC factory method with 1 real
    // caller, taking the Ammo directly as a parameter — likely the
    // MP-153/semi-auto shotgun case. Its return type
    // (Diz.LanguageExtensions.Option<ReloadSingleBarrelResult>) is from
    // the same external, unreferenceable DLL as Magazine
    // .ApplyWithoutRestrictions, so the same higher-risk
    // FormatterServices.GetUninitializedObject workaround is used here.
    //
    // ReloadInternalMagOperation / ReloadInternalMagWithOpenBoltOperation
    // (bolt-action-style reload) don't have an equivalent factory method —
    // instead they have an OnAddAmmoInChamber() method (no parameters,
    // reading state from private fields _ammoToChamber / _ammoFromChamber)
    // that has ZERO callers found anywhere via direct IL calls OR delegate
    // creation (ldftn/ldvirtftn) — strongly suggesting it's invoked via
    // Unity's animation-event system (a clip calling a method by name via
    // reflection), which is invisible to static IL analysis but still
    // gets intercepted correctly by a Harmony patch on the method itself,
    // regardless of how it's invoked at runtime.
    //
    // UPDATE: the logs confirmed the animation-event theory (the stack runs
    // AnimationEventsEmitter -> FirearmController.method_34 ->
    // OnAddAmmoInChamber), which is also why blocking it never worked: by
    // then the round has already been moved. OnAddAmmoInChamber is now
    // diagnostic only; the blocking moved to the reload entry points
    // (PatchFirearmReloadEntries) and the inventory operation check.
    //
    // All three classes are nested (no fixed namespace), so found via
    // reflection at runtime rather than compile-time attributes.
    // -----------------------------------------------------------------
    internal static class LevelGateReloadPatches
    {
        public static void ApplyAll(Harmony harmony)
        {
            PatchOnAddAmmoInChamber(harmony, "ReloadInternalMagOperation");
            PatchOnAddAmmoInChamber(harmony, "ReloadInternalMagWithOpenBoltOperation");
            PatchSingleBarrelRun(harmony);
            PatchFirearmReloadEntries(harmony);
        }

        private static void PatchOnAddAmmoInChamber(Harmony harmony, string typeName)
        {
            try
            {
                var type = AccessTools.AllTypes().FirstOrDefault(t => t.Name == typeName);
                if (type == null)
                {
                    LevelGatePlugin.Log.LogWarning($"LevelGate: could not find type {typeName} for internal-mag reload blocking.");
                    return;
                }

                var method = AccessTools.Method(type, "OnAddAmmoInChamber", Type.EmptyTypes);
                if (method == null)
                {
                    LevelGatePlugin.Log.LogWarning($"LevelGate: could not find {typeName}.OnAddAmmoInChamber() for internal-mag reload blocking.");
                    return;
                }

                harmony.Patch(method, prefix: new HarmonyMethod(typeof(LevelGateReloadPatches), nameof(OnAddAmmoInChamberPrefix)) { priority = Priority.First });
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError($"LevelGate: failed to patch {typeName}.OnAddAmmoInChamber. " + e);
            }
        }

        // DIAGNOSTIC ONLY now — this used to skip the method when the round
        // was gated, but the logs showed that was wrong on both counts:
        //  - OnAddAmmoInChamber is an animation EVENT (called from
        //    AnimationEventsEmitter -> FirearmController.method_34), fired
        //    after the inventory transaction that actually moves the round
        //    has already run. Skipping it didn't stop the load; it only left
        //    the reload operation waiting for an event that never came
        //    (HandsAreNotBusy logged "Cleared 2 stuck inventory operations"
        //    right after the Mosin block).
        //  - _ammoFromChamber on the open-bolt operation (M4A1 bolt locked
        //    back / Mosin bolt open) is null whenever the chamber was empty,
        //    which is exactly the manual-chamber case — so it logged
        //    item=NULL and let every one of those through.
        // The real block is now on the inventory operation itself
        // (Patch_BlockOperation.Evaluate -> TryBlockAmmoLoad) and on the
        // FirearmController reload entry points (PatchFirearmReloadEntries).
        // This just logs every Item-typed field so the next log shows what
        // each reload operation actually carries.
        private static void OnAddAmmoInChamberPrefix(object __instance, System.Reflection.MethodBase __originalMethod)
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                var t = __instance?.GetType();
                while (t != null && t != typeof(object))
                {
                    foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(EFT.InventoryLogic.Item).IsAssignableFrom(f.FieldType)) continue;
                        var value = f.GetValue(__instance) as EFT.InventoryLogic.Item;
                        sb.Append(f.Name).Append('=').Append(value == null ? "NULL" : value.TemplateId).Append(' ');
                    }
                    t = t.BaseType;
                }
                DiagnosticLogging.LogCall($"OnAddAmmoInChamber type={__originalMethod?.DeclaringType?.Name} {sb}");
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate OnAddAmmoInChamberPrefix error: " + e);
            }
        }

        // -----------------------------------------------------------------
        // Reload ENTRY points on the hands controller. Every R-key reload
        // that pushes loose rounds into the gun — internal magazine (Mosin),
        // tube (MP-153), single round into an empty chamber with the bolt
        // locked back (M4A1 manual chamber), revolver cylinder, grenade
        // launcher, break-action barrels — is started by one of these
        // Player.FirearmController methods, handed the rounds to load (an
        // ammo pack). Refusing here stops the reload before any animation
        // or reload operation starts, so nothing is left half-finished.
        //
        // The methods are looked up by name on FirearmController and every
        // subclass that declares its own override (ClientFirearmController
        // etc.), since patching only the base misses overrides. Each patched
        // method is logged at startup so the log shows what was actually
        // hooked. Box-magazine swaps (ReloadMag) are deliberately not
        // included: they don't move loose rounds, and loading gated rounds
        // into a magazine is already blocked by the operation-level check.
        // -----------------------------------------------------------------
        private static readonly HashSet<string> ReloadEntryNames = new HashSet<string>
        {
            "ReloadWithAmmo", "ReloadCylinderMagazine", "ReloadGrenadeLauncher", "ReloadBarrels"
        };

        private static void PatchFirearmReloadEntries(Harmony harmony)
        {
            try
            {
                Type baseType = typeof(EFT.ClientFirearmController);
                while (baseType != null && baseType.Name != "FirearmController") baseType = baseType.BaseType;
                if (baseType == null)
                {
                    LevelGatePlugin.Log.LogWarning("LevelGate: could not find Player.FirearmController — reload entry blocking not applied.");
                    return;
                }

                var voidPrefix = new HarmonyMethod(typeof(LevelGateReloadPatches), nameof(ReloadEntryPrefixVoid)) { priority = Priority.First };
                var boolPrefix = new HarmonyMethod(typeof(LevelGateReloadPatches), nameof(ReloadEntryPrefixBool)) { priority = Priority.First };
                int patched = 0;

                foreach (var type in AccessTools.AllTypes().Where(t => baseType.IsAssignableFrom(t)))
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (!ReloadEntryNames.Contains(method.Name) || method.IsAbstract) continue;

                        try
                        {
                            if (method.ReturnType == typeof(void))
                                harmony.Patch(method, prefix: voidPrefix);
                            else if (method.ReturnType == typeof(bool))
                                harmony.Patch(method, prefix: boolPrefix);
                            else
                            {
                                LevelGatePlugin.Log.LogWarning($"LevelGate: skipping {type.Name}.{method.Name} (unexpected return type {method.ReturnType.Name}).");
                                continue;
                            }

                            patched++;
                            LevelGatePlugin.Log.LogInfo($"LevelGate: reload entry hooked: {type.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name))})");
                        }
                        catch (Exception e)
                        {
                            LevelGatePlugin.Log.LogError($"LevelGate: failed to patch {type.Name}.{method.Name}. " + e);
                        }
                    }
                }

                if (patched == 0)
                    LevelGatePlugin.Log.LogWarning("LevelGate: no FirearmController reload entry methods found — reload entry blocking not applied.");
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to patch FirearmController reload entries. " + e);
            }
        }

        private static bool ReloadEntryPrefixVoid(object __instance, object[] __args, System.Reflection.MethodBase __originalMethod)
        {
            return !ShouldBlockReloadEntry(__instance, __args, __originalMethod);
        }

        private static bool ReloadEntryPrefixBool(object __instance, object[] __args, System.Reflection.MethodBase __originalMethod, ref bool __result)
        {
            if (!ShouldBlockReloadEntry(__instance, __args, __originalMethod)) return true;
            __result = false;
            return false;
        }

        private static bool ShouldBlockReloadEntry(object instance, object[] args, System.Reflection.MethodBase original)
        {
            try
            {
                if (!LevelGateCheck.IsOwnHandsController(instance)) return false;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null || args == null) return false;

                var ammoList = new List<EFT.InventoryLogic.Ammo>();
                foreach (var arg in args) AmmoCollector.Collect(arg, ammoList, 0);

                DiagnosticLogging.LogCall(
                    $"ReloadEntry {original?.DeclaringType?.Name}.{original?.Name} " +
                    $"ammo=[{string.Join(",", ammoList.Select(a => a.TemplateId).Distinct())}]");

                foreach (var ammo in ammoList)
                {
                    if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                    {
                        LevelGateCheck.Notify(required);
                        CallbackUtil.TryFail(args.OfType<Comfort.Common.Callback>().FirstOrDefault(),
                            $"LevelGate: requires level {required}");
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate ShouldBlockReloadEntry error: " + e);
            }

            return false;
        }

        private static Type _singleBarrelReturnType;

        private static void PatchSingleBarrelRun(Harmony harmony)
        {
            try
            {
                var type = AccessTools.AllTypes().FirstOrDefault(t => t.Name == "ReloadSingleBarrelResult");
                if (type == null)
                {
                    LevelGatePlugin.Log.LogWarning("LevelGate: could not find ReloadSingleBarrelResult for single-barrel reload blocking.");
                    return;
                }

                var method = AccessTools.Method(type, "Run");
                if (method == null)
                {
                    LevelGatePlugin.Log.LogWarning("LevelGate: could not find ReloadSingleBarrelResult.Run for single-barrel reload blocking.");
                    return;
                }

                _singleBarrelReturnType = method.ReturnType;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(LevelGateReloadPatches), nameof(SingleBarrelRunPrefix)) { priority = Priority.First });
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to patch ReloadSingleBarrelResult.Run. " + e);
            }
        }

        private static bool SingleBarrelRunPrefix(object[] __args, ref object __result)
        {
            try
            {
                var ammo = __args.OfType<EFT.InventoryLogic.Ammo>().FirstOrDefault();
                DiagnosticLogging.LogCall($"SingleBarrelRunPrefix ammo={(ammo == null ? "NULL" : ammo.TemplateId)}");
                if (ammo == null) return true;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return true;

                if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);

                    if (_singleBarrelReturnType != null)
                    {
                        try
                        {
                            __result = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(_singleBarrelReturnType);
                        }
                        catch (Exception ex)
                        {
                            LevelGatePlugin.Log.LogError("LevelGate: could not build uninitialized Option<ReloadSingleBarrelResult>, letting original run instead. " + ex);
                            return true;
                        }
                    }

                    return false;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate SingleBarrelRunPrefix error: " + e);
            }

            return true;
        }
    }


    // chamber, or multi-barrel weapon.
    //
    // -----------------------------------------------------------------
    // PATCH 5 — block loading a gated ammo type into a magazine, weapon
    // chamber, or multi-barrel weapon.
    //
    // The original hook here was LoadMagazineProcess.TryProceedForItem —
    // kept below as a defense-in-depth backup, but it turned out
    // insufficient with certain ammo/reload-related mods installed
    // (e.g. Continuous Load Ammo, which changes how rounds get inserted).
    // Tracing actual IL callers found the real, mod-agnostic choke point:
    // EFT.InventoryLogic.Magazine.ApplyWithoutRestrictions is what
    // actually inserts a round, and it's called from several different
    // higher-level paths (Magazine.ApplyItem, Weapon.Apply,
    // ItemController.LoadMagazine, plus the async internals of
    // LoadMagazineProcess) — meaning ApplyWithoutRestrictions itself would
    // be the most fundamental hook, but its return type
    // (Diz.LanguageExtensions.OperationResult) is defined in a DLL we
    // don't have, so we can't safely fabricate a substitute return value
    // for it.
    //
    // Instead we hook one level up, at LoadMagazine / LoadWeaponWithAmmo /
    // LoadMultiBarrelWeapon — these take the Ammo item directly as a
    // parameter and return the same Task<Comfort.Common.IResult> pattern
    // already confirmed safe for MoveOperation/ThrowOperation, so we can
    // fabricate SuccessfulResult.New the same way.
    //
    // Just like CanExecute, PlayerInventoryController (the real in-raid
    // controller) overrides all three of these with its own
    // implementation, so both the base ItemController methods (stash) and
    // PlayerInventoryController's overrides (raid) need patching.
    // -----------------------------------------------------------------
    internal static class LevelGatePatches
    {
        public static void PatchAmmoLoad(Harmony harmony)
        {
            // Base ItemController methods (stash/hideout context).
            PatchAmmoMethod(harmony, typeof(EFT.InventoryLogic.ItemController), "LoadMagazine",
                new[] { typeof(EFT.InventoryLogic.Ammo), typeof(EFT.InventoryLogic.Magazine), typeof(int), typeof(bool) });
            PatchAmmoMethod(harmony, typeof(EFT.InventoryLogic.ItemController), "LoadWeaponWithAmmo",
                new[] { typeof(EFT.InventoryLogic.Weapon), typeof(EFT.InventoryLogic.Ammo), typeof(int) });
            PatchAmmoMethod(harmony, typeof(EFT.InventoryLogic.ItemController), "LoadMultiBarrelWeapon",
                new[] { typeof(EFT.InventoryLogic.Weapon), typeof(EFT.InventoryLogic.Ammo), typeof(int) });

            // PlayerInventoryController's own overrides (real in-raid controller).
            var inRaidType = AccessTools.AllTypes().FirstOrDefault(t => t.Name == "PlayerInventoryController");
            if (inRaidType == null)
            {
                LevelGatePlugin.Log.LogWarning(
                    "LevelGate: could not find PlayerInventoryController — in-raid ammo-loading restriction not applied.");
            }
            else
            {
                PatchAmmoMethod(harmony, inRaidType, "LoadMagazine",
                    new[] { typeof(EFT.InventoryLogic.Ammo), typeof(EFT.InventoryLogic.Magazine), typeof(int), typeof(bool) });
                PatchAmmoMethod(harmony, inRaidType, "LoadWeaponWithAmmo",
                    new[] { typeof(EFT.InventoryLogic.Weapon), typeof(EFT.InventoryLogic.Ammo), typeof(int) });
                PatchAmmoMethod(harmony, inRaidType, "LoadMultiBarrelWeapon",
                    new[] { typeof(EFT.InventoryLogic.Weapon), typeof(EFT.InventoryLogic.Ammo), typeof(int) });
            }

            // Backup: the original, narrower hook, kept in case it catches
            // something the above don't in some other mod configuration.
            PatchLoadMagazineProcessBackup(harmony);
        }

        private static void PatchAmmoMethod(Harmony harmony, Type declaringType, string methodName, Type[] paramTypes)
        {
            try
            {
                var method = AccessTools.Method(declaringType, methodName, paramTypes);
                if (method == null)
                {
                    LevelGatePlugin.Log.LogWarning(
                        $"LevelGate: could not find {declaringType.Name}.{methodName}({string.Join(",", paramTypes.Select(t => t.Name))}) — that ammo-loading path is not restricted.");
                    return;
                }

                var prefixMethod = typeof(LevelGatePatches).GetMethod(nameof(AmmoLoadPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(method, prefix: new HarmonyMethod(prefixMethod));
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError($"LevelGate: failed to patch {declaringType.Name}.{methodName}. " + e);
            }
        }

        // Shared prefix for LoadMagazine/LoadWeaponWithAmmo/LoadMultiBarrelWeapon.
        // All three take the Ammo item as their second parameter (LoadMagazine:
        // ammo, magazine, count[, bool]; LoadWeaponWithAmmo/LoadMultiBarrelWeapon:
        // weapon, ammo, count) and return Task<Comfort.Common.IResult> — same
        // pattern as MoveOperation/ThrowOperation, so the same safe substitute
        // (a completed Task wrapping SuccessfulResult.New) applies.
        private static bool AmmoLoadPrefix(object[] __args, ref System.Threading.Tasks.Task<Comfort.Common.IResult> __result)
        {
            try
            {
                var ammo = __args.OfType<EFT.InventoryLogic.Ammo>().FirstOrDefault();
                if (ammo == null)
                {
                    DiagnosticLogging.LogCall("AmmoLoadPrefix (no Ammo arg found)");
                    return true;
                }

                var player = LevelGateCheck.GetMainPlayer();
                DiagnosticLogging.LogCall($"AmmoLoadPrefix ammo={ammo.TemplateId} player={(player == null ? "NULL" : "found")}");
                if (player == null) return true;

                if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);
                    __result = System.Threading.Tasks.Task.FromResult<Comfort.Common.IResult>(Comfort.Common.SuccessfulResult.New);
                    return false;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate AmmoLoadPrefix error: " + e);
            }

            return true;
        }

        private static void PatchLoadMagazineProcessBackup(Harmony harmony)
        {
            try
            {
                var type = AccessTools.AllTypes()
                    .FirstOrDefault(t => t.Name == "LoadMagazineProcess");

                if (type == null)
                {
                    LevelGatePlugin.Log.LogWarning(
                        "LevelGate: could not find LoadMagazineProcess — backup ammo-loading restriction not applied (this is fine if the main LoadMagazine patches above succeeded).");
                    return;
                }

                var method = AccessTools.Method(type, "TryProceedForItem", new[] { typeof(EFT.InventoryLogic.Item) });
                if (method == null)
                {
                    LevelGatePlugin.Log.LogWarning(
                        "LevelGate: found LoadMagazineProcess but not TryProceedForItem(Item) — backup ammo-loading restriction not applied.");
                    return;
                }

                var prefixMethod = typeof(LevelGatePatches).GetMethod(nameof(TryProceedPrefix), BindingFlags.Static | BindingFlags.NonPublic);
                harmony.Patch(method, prefix: new HarmonyMethod(prefixMethod));
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to patch backup ammo loading. " + e);
            }
        }

        private static bool TryProceedPrefix(EFT.InventoryLogic.Item item)
        {
            try
            {
                if (item == null) return true;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return true;

                if (LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required);
                    return false;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate TryProceedPrefix error: " + e);
            }

            return true;
        }
    }
}
