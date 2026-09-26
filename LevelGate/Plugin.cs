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
        public const string PluginVersion = "1.5.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> ToggleMenuKey;
        internal static ConfigEntry<string> LockedLabel;
        internal static ConfigEntry<string> SemiLockedLabel;
        internal static ConfigEntry<bool> LabelBrackets;
        internal static ConfigEntry<bool> LabelShowLevel;
        internal static ConfigEntry<bool> TooltipLayout;
        internal static ConfigEntry<string> LockedColor;
        internal static ConfigEntry<string> UnlockedColor;
        internal static ConfigEntry<string> SemiLockedColor;
        internal static ConfigEntry<bool> StripesEnabled;
        internal static ConfigEntry<string> StripeStrength;
        internal static ConfigEntry<bool> StripesGameStyle;

        // Diagonal-stripe look on LOCKED / UNLOCKED / SEMI LOCKED item backgrounds.
        internal static readonly string[] StripeStrengthChoices = { "Subtle", "Medium", "Strong" };

        internal static LevelGateConfig Data = new LevelGateConfig();

        private static string ConfigFolder =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "config");

        private static string ConfigFile => Path.Combine(ConfigFolder, "level_requirements.json");

        private bool _menuOpen;
        private Rect _menuRect = new Rect(60, 60, 440, 720);
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

            // Diagonal stripes over the colored background of LOCKED /
            // UNLOCKED / SEMI LOCKED items (behind the item picture).
            StripesEnabled = Config.Bind("Stripes", "Enabled", true,
                "Draw diagonal stripes on the background of LOCKED, UNLOCKED and SEMI LOCKED items.");
            StripeStrength = Config.Bind("Stripes", "Strength", "Medium",
                new ConfigDescription("How dark the stripes are.", new AcceptableValueList<string>(StripeStrengthChoices)));
            StripesGameStyle = Config.Bind("Stripes", "UseGameStripes", true,
                "Use the game's own striped background (the one on items you lock in the stash). Off = LevelGate's drawn stripes.");

            // Label text and background colors for the three states. Also
            // editable live from the F9 window. Colors are the game's own
            // item background colors (JsonType.TaxonomyColor) — the grid
            // can only show those, not arbitrary RGB.
            LockedLabel = Config.Bind("Labels", "LockedText", "LOCKED",
                "Label for items above your level. Short name shows [LOCKED], full name [LOCKED - Lvl X] Name.");
            SemiLockedLabel = Config.Bind("Labels", "SemiLockedText", "SEMI LOCKED",
                "Label for magazines holding rounds above your level.");
            LabelBrackets = Config.Bind("Labels", "UseBrackets", true,
                "Wrap the label in [ ] (e.g. turn off for just  X  or  ✓).");
            LabelShowLevel = Config.Bind("Labels", "ShowLevelInFullName", true,
                "Add ' - Lvl X' to the label in the full item name.");
            TooltipLayout = Config.Bind("Labels", "TooltipTwoLines", true,
                "Hover tooltip: 'LOCKED - Bandages' / 'Unlocks At Level X' (unlocked items: just the name / 'Unlocked At Level X'; level in yellow), above other mods' lines (e.g. Show Me The Money prices).");

            var colorNames = Enum.GetNames(typeof(JsonType.TaxonomyColor));
            string Pick(string preferred, string fallback) =>
                colorNames.Contains(preferred) ? preferred : colorNames.Contains(fallback) ? fallback : colorNames[0];
            LockedColor = Config.Bind("Colors", "LockedBackground", Pick("red", "red"),
                new ConfigDescription("Background color of LOCKED items.", new AcceptableValueList<string>(colorNames)));
            UnlockedColor = Config.Bind("Colors", "UnlockedBackground", Pick("green", "green"),
                new ConfigDescription("Background color of UNLOCKED items.", new AcceptableValueList<string>(colorNames)));
            SemiLockedColor = Config.Bind("Colors", "SemiLockedBackground", Pick("orange", "yellow"),
                new ConfigDescription("Background color of SEMI LOCKED magazines.", new AcceptableValueList<string>(colorNames)));

            LoadConfig();

            var harmony = new Harmony(PluginGuid);
            PatchAllIndividually(harmony);
            Patch_BlockOperationInRaid.Apply(harmony);
            LevelGatePatches.PatchAmmoLoad(harmony);
            LevelGateReloadPatches.ApplyAll(harmony);
            OnScreenNotifier.Resolve();
            FireGate.PatchTriggerPress(harmony);
            MagazineSemiLock.PatchNames(harmony);
            GearSlotGate.Apply(harmony);
            StripeOverlay.Apply(harmony);
            TooltipLayoutPatch.Apply(harmony);
            StripesEnabled.SettingChanged += (_, __) => StripeOverlay.RefreshAll();
            StripeStrength.SettingChanged += (_, __) => StripeOverlay.RefreshAll();
            StripesGameStyle.SettingChanged += (_, __) => StripeOverlay.RefreshAll();
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

            StripeOverlay.WatchLevel();
        }

        private void OnGUI()
        {
            if (_menuOpen)
            {
                _menuRect = GUI.Window(GetInstanceID(), _menuRect, DrawMenu, "LevelGate — item level requirements");
            }

            LevelLimiterContextMenu.DrawPickerIfOpen();
            OnScreenNotifier.DrawFallback();
        }

        private void DrawMenu(int id)
        {
            GUI.DragWindow(new Rect(0, 0, 10000, 20));

            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Stripes:", GUILayout.Width(90));
            if (GUILayout.Button(StripesEnabled.Value ? "ON" : "OFF", GUILayout.Width(60)))
            {
                StripesEnabled.Value = !StripesEnabled.Value;
            }
            if (GUILayout.Button(StripesGameStyle.Value ? "Game style" : "LevelGate style", GUILayout.Width(110)))
            {
                StripesGameStyle.Value = !StripesGameStyle.Value;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("LG strength:", GUILayout.Width(90));
            foreach (var choice in StripeStrengthChoices)
            {
                bool selected = StripeStrength.Value == choice;
                if (GUILayout.Button(selected ? "> " + choice + " <" : choice, GUILayout.Width(85)))
                {
                    StripeStrength.Value = choice;
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("Labels & colors (applied live; reopen the inventory to refresh names):");
            DrawLabelRow("Locked:", LockedLabel, LockedColor);
            DrawLabelRow("Unlocked:", null, UnlockedColor);
            DrawLabelRow("Semi locked:", SemiLockedLabel, SemiLockedColor);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Brackets [ ]: " + (LabelBrackets.Value ? "ON" : "OFF"), GUILayout.Width(130)))
                LabelBrackets.Value = !LabelBrackets.Value;
            if (GUILayout.Button("Show level: " + (LabelShowLevel.Value ? "ON" : "OFF"), GUILayout.Width(120)))
                LabelShowLevel.Value = !LabelShowLevel.Value;
            if (GUILayout.Button("Tooltip 2 lines: " + (TooltipLayout.Value ? "ON" : "OFF"), GUILayout.Width(140)))
                TooltipLayout.Value = !TooltipLayout.Value;
            if (GUILayout.Button("Defaults", GUILayout.Width(80)))
            {
                foreach (var entry in new ConfigEntryBase[] { LockedLabel, SemiLockedLabel, LabelBrackets, LabelShowLevel, LockedColor, UnlockedColor, SemiLockedColor })
                    entry.BoxedValue = entry.DefaultValue;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
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

        // One row: label text field + a color button that cycles through the
        // game's item background colors.
        private static void DrawLabelRow(string title, ConfigEntry<string> label, ConfigEntry<string> color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, GUILayout.Width(80));
            if (label != null)
            {
                string text = GUILayout.TextField(label.Value ?? "", 24, GUILayout.Width(150));
                if (text != label.Value) label.Value = text;
            }
            else GUILayout.Label("(no label — just the name)", GUILayout.Width(150));

            if (GUILayout.Button(color.Value, GUILayout.Width(120)))
            {
                var names = Enum.GetNames(typeof(JsonType.TaxonomyColor));
                int i = Array.IndexOf(names, color.Value);
                color.Value = names[(i + 1) % names.Length];
            }
            GUILayout.EndHorizontal();
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

            if (itemTplId == null) return false;
            if (!LevelGatePlugin.Data.Items.TryGetValue(itemTplId, out requiredLevel)) return false;

            int currentLevel;
            if (player != null)
            {
                if (!player.IsYourPlayer) return false;   // not the local human
                if (player.IsAI) return false;            // never touch bots
                currentLevel = player.Profile.Info.Level;
            }
            else
            {
                // No in-raid player: main menu / stash / traders. There is no
                // GameWorld there, which is why labels and colors used to
                // only show up after visiting the hideout or a raid. Use the
                // logged-in PMC profile's level instead.
                int? level = OutOfRaidProfile.GetLevel();
                if (level == null) return false;
                currentLevel = level.Value;
            }
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

        // True if this item is in the local player's own inventory (or its
        // owner can't be determined). Item.Owner is the IItemOwner holding
        // it — for anything you carry, that's your inventory controller; for
        // a bot's gear, the bot's controller.
        public static bool IsOwnItem(EFT.InventoryLogic.Item item)
        {
            try
            {
                if (item == null) return true;
                var owner = ReflectionUtil.GetMember(item, "Owner");
                if (owner == null) return true;
                return IsOwnInventoryController(owner);
            }
            catch
            {
                return true;
            }
        }

        // Ownership for hooks that only get items as arguments: judged by
        // the gun/magazine being loaded (you may be loading rounds looted
        // from a body into your own magazine), else by the round itself.
        public static bool IsOwnAction(object[] args)
        {
            if (args == null) return true;
            var container = args.OfType<EFT.InventoryLogic.Item>().FirstOrDefault(i => !(i is EFT.InventoryLogic.Ammo));
            if (container != null) return IsOwnItem(container);
            var ammo = args.OfType<EFT.InventoryLogic.Ammo>().FirstOrDefault();
            return IsOwnItem(ammo);
        }

        // The local player's Equipment item (the parent of the gear slots):
        // from the in-raid player, else the out-of-raid PMC profile.
        public static object GetLocalEquipment()
        {
            try
            {
                var player = GetMainPlayer();
                object profile = player != null ? (object)player.Profile : OutOfRaidProfile.Get();
                return ReflectionUtil.GetMember(ReflectionUtil.GetMember(profile, "Inventory"), "Equipment");
            }
            catch
            {
                return null;
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

        // True if the round already sits in the same gun/magazine it's being
        // "moved" into — shuffling or splitting it out of there (an unload's
        // transfer/merge can report the magazine as its address) is not
        // loading anything.
        private static readonly MemberGetter ItemAddressGetter = new MemberGetter("CurrentAddress", "Parent");

        public static bool IsAlreadyInside(EFT.InventoryLogic.Item item, EFT.InventoryLogic.ItemAddress to)
        {
            try
            {
                var current = ItemAddressGetter.Get(item) as EFT.InventoryLogic.ItemAddress;
                if (current == null) return false;
                var from = GetContainerParentItem(current);
                var dest = GetContainerParentItem(to);
                return from != null && ReferenceEquals(from, dest);
            }
            catch
            {
                return false;
            }
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

        public static void Notify(int requiredLevel, EFT.InventoryLogic.Item item = null, bool onScreen = false)
        {
            // Only the four deliberate player actions pass onScreen: true —
            // packing a magazine (LoadMagazine), manually chambering /
            // single-round loading (the Split into a chamber, and the R-key
            // reload entries), and pressing M1 (SetTriggerPressed). Every
            // other check (drag hover via MoveResult.CanExecute, the game
            // polling CanPressTrigger, per-round insert helpers) still
            // blocks, but silently — those were the "random" popups.
            // Equip blocks get a message too: out of raid the game's own text
            // for a refused equip is just "hands are busy", which doesn't
            // say why.
            if (onScreen && item != null)
                OnScreenNotifier.Show(item is EFT.InventoryLogic.Ammo
                    ? $"Ammo Level Too High (requires level {requiredLevel})"
                    : $"Item Level Too High (requires level {requiredLevel})");

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

    // Tarkov's own bottom-right notification popup — the same box the game
    // uses for "Can't execute ..." inventory errors. In older EFT this lived
    // on a static class called NotificationManagerClass, but SPT 4.1.6
    // doesn't have a type by that name (the log showed "Could not find type
    // named NotificationManagerClass"), so it's now found by METHOD name:
    // every type in Assembly-CSharp is scanned once at startup for a static
    // DisplayWarningNotification / DisplayMessageNotification /
    // DisplayNotification(string, ...optional). Whatever it finds is logged.
    //
    // If nothing matches (or the call throws), LevelGate draws its own
    // lookalike box in the bottom-right corner instead, so the message is
    // always shown. Each distinct message is throttled, because the fire
    // check runs every frame the trigger is held.
    internal static class OnScreenNotifier
    {
        // Not a cooldown: every separate press shows its own message. This
        // only swallows the duplicate when ONE press passes through two
        // hooked layers (e.g. a base method and its override) in the same
        // instant.
        private const float MinSecondsBetweenRepeats = 0.15f;
        private const float FallbackSeconds = 4f;

        private static MethodInfo _method;
        private static readonly Dictionary<string, float> _lastShown = new Dictionary<string, float>();

        private static string _fallbackText;
        private static float _fallbackUntil;
        private static GUIStyle _fallbackStyle;
        private static Texture2D _fallbackBackground;

        private static readonly string[] CandidateNames =
            { "DisplayWarningNotification", "DisplayMessageNotification", "DisplayNotification" };

        public static void Resolve()
        {
            try
            {
                var candidates = new List<MethodInfo>();
                Type[] types;
                try { types = typeof(EFT.Player).Assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly); }
                    catch { continue; }

                    foreach (var m in methods)
                    {
                        if (Array.IndexOf(CandidateNames, m.Name) < 0 || m.IsGenericMethodDefinition) continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 0 || ps[0].ParameterType != typeof(string)) continue;
                        if (!ps.Skip(1).All(p => p.IsOptional)) continue;
                        candidates.Add(m);
                    }
                }

                // Prefer the warning style (matches the "Can't execute" box),
                // then the plain message, then the generic one.
                _method = candidates
                    .OrderBy(m => Array.IndexOf(CandidateNames, m.Name))
                    .ThenBy(m => m.GetParameters().Length)
                    .FirstOrDefault();

                if (_method != null)
                    LevelGatePlugin.Log.LogInfo($"LevelGate: on-screen messages use {_method.DeclaringType?.FullName}.{_method.Name}");
                else
                    LevelGatePlugin.Log.LogWarning("LevelGate: no game notification method found — on-screen messages use LevelGate's own box instead.");
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate OnScreenNotifier.Resolve error: " + e);
            }
        }

        public static void Show(string message)
        {
            float now = Time.realtimeSinceStartup;
            if (_lastShown.TryGetValue(message, out var last) && now - last < MinSecondsBetweenRepeats) return;
            _lastShown[message] = now;

            if (_method != null)
            {
                try
                {
                    var ps = _method.GetParameters();
                    var args = new object[ps.Length];
                    args[0] = message;
                    for (int i = 1; i < ps.Length; i++)
                    {
                        var p = ps[i];
                        object value = p.HasDefaultValue ? p.DefaultValue : null;
                        if (value == null || value == DBNull.Value)
                            value = p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) == null
                                ? Activator.CreateInstance(p.ParameterType)
                                : null;
                        else if (p.ParameterType.IsEnum && value.GetType() != p.ParameterType)
                            value = Enum.ToObject(p.ParameterType, value);
                        args[i] = value;
                    }
                    _method.Invoke(null, args);
                    return;
                }
                catch (Exception e)
                {
                    LevelGatePlugin.Log.LogError($"LevelGate: {_method.Name} failed, switching to LevelGate's own box. " + e);
                    _method = null;
                }
            }

            _fallbackText = message;
            _fallbackUntil = now + FallbackSeconds;
        }

        // Called from LevelGatePlugin.OnGUI.
        public static void DrawFallback()
        {
            if (_fallbackText == null || Time.realtimeSinceStartup > _fallbackUntil) return;

            if (_fallbackStyle == null)
            {
                _fallbackBackground = new Texture2D(1, 1);
                _fallbackBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.8f));
                _fallbackBackground.Apply();

                _fallbackStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 16,
                    wordWrap = true,
                    padding = new RectOffset(14, 14, 10, 10)
                };
                _fallbackStyle.normal.background = _fallbackBackground;
                _fallbackStyle.normal.textColor = Color.white;
            }

            const float width = 420f;
            var content = new GUIContent("(!)  " + _fallbackText);
            float height = _fallbackStyle.CalcHeight(content, width);
            var rect = new Rect(Screen.width - width - 24f, Screen.height - height - 90f, width, height);
            GUI.Box(rect, content, _fallbackStyle);
        }
    }

    // -----------------------------------------------------------------
    // Gear slots refuse gated items when asked "can you take this?".
    //
    // Picking up loose loot (InteractionContextHelper -> PickUpState, per
    // the log) lets the game choose where the item goes, and it prefers an
    // EMPTY matching gear slot — a second weapon slot, headset, helmet,
    // armor, face cover, eyewear. For a gated item that slot was chosen,
    // then the equip block refused it, and the game reported "No space"
    // instead of trying your backpack. Answering "no" from the slot's own
    // compatibility check makes the game skip it and fall through to the
    // backpack/rig/pockets, exactly as if the slot were occupied — and a
    // drag onto that slot now simply shows it as not accepting the item.
    //
    // Only the local player's own gear slots (EquipSlotIds on YOUR
    // equipment) are affected, never an item already sitting in the slot,
    // and never bots or containers. The Slot type and its CanAccept /
    // CheckCompatibility(Item) -> bool methods are found by reflection and
    // each patched method is logged at startup.
    // -----------------------------------------------------------------
    internal static class GearSlotGate
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                // Take the type straight from the expression "address.Slot"
                // (already used elsewhere in this plugin), so it resolves at
                // compile time whether Slot is a field or a property — the
                // reflection lookup of a *property* named Slot failed on SPT
                // 4.1.6 ("could not resolve the Slot type").
                var slotType = TypeOf((EFT.InventoryLogic.SlotItemAddress a) => a.Slot);
                if (slotType == null)
                {
                    LevelGatePlugin.Log.LogWarning("LevelGate: could not resolve the Slot type — loose-loot auto-equip fix not applied.");
                    return;
                }

                var postfix = new HarmonyMethod(typeof(GearSlotGate), nameof(Postfix));
                int patched = 0;
                for (var t = slotType; t != null && t != typeof(object); t = t.BaseType)
                {
                    foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (m.Name != "CanAccept" && m.Name != "CheckCompatibility") continue;
                        if (m.ReturnType != typeof(bool) || m.IsAbstract) continue;
                        var ps = m.GetParameters();
                        if (ps.Length < 1 || ps[0].ParameterType != typeof(EFT.InventoryLogic.Item)) continue;
                        try
                        {
                            harmony.Patch(m, postfix: postfix);
                            patched++;
                            LevelGatePlugin.Log.LogInfo($"LevelGate: gear slot check hooked: {t.FullName}.{m.Name}({string.Join(",", ps.Select(p => p.ParameterType.Name))})");
                        }
                        catch (Exception e)
                        {
                            LevelGatePlugin.Log.LogError($"LevelGate: failed to patch {t.Name}.{m.Name}. " + e);
                        }
                    }
                }
                if (patched == 0)
                    LevelGatePlugin.Log.LogWarning("LevelGate: no Slot.CanAccept/CheckCompatibility(Item) found — loose-loot auto-equip fix not applied.");
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to apply gear slot gate. " + e);
            }
        }

        private static Type TypeOf<TAddress, TSlot>(Func<TAddress, TSlot> member) => typeof(TSlot);

        private static void Postfix(object __instance, object[] __args, ref bool __result)
        {
            try
            {
                if (!__result || __args == null || __args.Length == 0) return;
                if (!(__args[0] is EFT.InventoryLogic.Item item)) return;

                if (!(ReflectionUtil.GetMember(__instance, "ID") is string slotId) ||
                    !LevelGateCheck.EquipSlotIds.Contains(slotId)) return;

                // Already in this slot (e.g. equipped before the gate was set)
                // — never make the game think it's invalid there.
                if (ReferenceEquals(ReflectionUtil.GetMember(__instance, "ContainedItem"), item)) return;

                var equipment = LevelGateCheck.GetLocalEquipment();
                if (equipment == null || !ReferenceEquals(ReflectionUtil.GetMember(__instance, "ParentItem"), equipment)) return;

                if (LevelGateCheck.IsBlocked(LevelGateCheck.GetMainPlayer(), item.TemplateId, out _))
                {
                    DiagnosticLogging.LogCall($"GearSlotGate refused slot={slotId} item={item.TemplateId}");
                    __result = false;
                }
            }
            catch
            {
                // never break slot checks
            }
        }
    }

    internal enum ItemState { None, Locked, Unlocked, SemiLocked }

    // -----------------------------------------------------------------
    // Diagonal stripes over the colored background of LOCKED / UNLOCKED /
    // SEMI LOCKED items (the "striped cell" look).
    //
    // Event-driven, no scanning: a postfix on the item cell's own repaint
    // methods (ItemView.UpdateColor & co, found by name at startup) sets the
    // stripes whenever the game repaints a cell. The screen is only searched
    // once when you change a Stripes setting or your level changes. If no
    // repaint method is found, stripes stay off (logged, with the method
    // names to fix it) — never a periodic search, which cost FPS.
    //
    // By default the stripes are the GAME'S OWN striped background — the
    // layer it shows on items you pin / lock in the stash
    // (GridItemView._pinBackground): it's simply switched on for our items.
    // The item itself is NOT pinned/locked, sorting works as normal.
    // "LevelGate style" (or a cell without that layer) uses LevelGate's own
    // drawn stripes instead, BEHIND the item
    // picture: the stripes are a tiled, see-through image added as a child
    // of the cell's background image, so they paint right after the
    // background and before the item art, name and counters.
    //
    // The background image is found by the ItemView's field name first
    // (ColorPanel / Background...), else as the first image in the cell that
    // covers the whole cell. If neither is found the cell simply gets no
    // stripes (logged once) — nothing else changes.
    // -----------------------------------------------------------------
    internal static class StripeOverlay
    {
        // Methods an item cell calls when it (re)draws its background / item.
        private static readonly string[] RepaintMethods = { "UpdateColor", "UpdateInfo", "UpdatePinLockState", "SetPinLockState", "UpdatePinLock", "OnRefreshItem", "UpdateItemValue" };
        private static Type _itemViewType;
        private static bool _hooked;
        private static int? _lastLevel;
        private static float _nextLevelCheck;

        public static void Apply(Harmony harmony)
        {
            try
            {
                _itemViewType = typeof(EFT.Player).Assembly.GetType("EFT.UI.DragAndDrop.ItemView", false);
                if (_itemViewType == null)
                {
                    LevelGatePlugin.Log.LogWarning("LevelGate: EFT.UI.DragAndDrop.ItemView not found — stripes are off.");
                    return;
                }
                var postfix = new HarmonyMethod(typeof(StripeOverlay).GetMethod(nameof(AfterRepaint), BindingFlags.Static | BindingFlags.NonPublic));
                var hooked = new List<string>();
                var types = _itemViewType.Assembly.GetTypes().Where(t => _itemViewType.IsAssignableFrom(t));
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                foreach (var type in types)
                {
                    foreach (var method in type.GetMethods(flags))
                    {
                        if (!RepaintMethods.Contains(method.Name) || method.IsAbstract || method.ContainsGenericParameters) continue;
                        try
                        {
                            harmony.Patch(method, postfix: postfix);
                            hooked.Add(type.Name + "." + method.Name);
                        }
                        catch (Exception e)
                        {
                            LevelGatePlugin.Log.LogWarning($"LevelGate: couldn't hook {type.Name}.{method.Name} for stripes: {e.Message}");
                        }
                    }
                }
                _hooked = hooked.Count > 0;
                if (_hooked)
                    LevelGatePlugin.Log.LogInfo("LevelGate: stripes hooked: " + string.Join(", ", hooked));
                else
                {
                    var names = _itemViewType.GetMethods(flags).Select(m => m.Name).Distinct().OrderBy(n => n);
                    LevelGatePlugin.Log.LogWarning("LevelGate: no item cell repaint method found — stripes are off. ItemView methods: " + string.Join(", ", names));
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: stripe hooks failed, stripes are off. " + e);
            }
        }

        // After the game repaints a cell: stripes on / off for its item.
        private static void AfterRepaint(object __instance)
        {
            try
            {
                if (__instance is Component view && view != null) Refresh(view);
            }
            catch (Exception e)
            {
                if (!_repaintErrorLogged)
                {
                    _repaintErrorLogged = true;
                    LevelGatePlugin.Log.LogError("LevelGate stripes error: " + e);
                }
            }
        }
        private static bool _repaintErrorLogged;

        private static void Refresh(Component view)
        {
            bool on = LevelGatePlugin.StripesEnabled?.Value ?? true;
            var item = on ? ReflectionUtil.GetMember(view, "Item") as EFT.InventoryLogic.Item : null;
            Set(view, on ? StateOf(LevelGateCheck.GetMainPlayer(), item) : ItemState.None);
        }

        /// <summary>One pass over the cells on screen — only after a setting / level change.</summary>
        public static void RefreshAll()
        {
            if (!_hooked || _itemViewType == null) return;
            try
            {
                foreach (var obj in UnityEngine.Object.FindObjectsOfType(_itemViewType))
                    if (obj is Component view) Refresh(view);
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate stripes refresh error: " + e);
            }
        }

        /// <summary>Cheap: the profile level is cached; a level-up redraws the stripes once (items may unlock).</summary>
        public static void WatchLevel()
        {
            if (!_hooked) return;
            float now = Time.realtimeSinceStartup;
            if (now < _nextLevelCheck) return;
            _nextLevelCheck = now + 2f;
            var level = OutOfRaidProfile.GetLevel();
            if (level == null || level == _lastLevel) return;
            bool changed = _lastLevel != null;
            _lastLevel = level;
            if (changed) RefreshAll();
        }

        /// <summary>Same rule as the background colors (Patch_RedBackgroundLockedItem).</summary>
        private static ItemState StateOf(EFT.Player player, EFT.InventoryLogic.Item item)
        {
            if (item == null) return ItemState.None;
            bool inConfig = LevelGatePlugin.Data.Items.ContainsKey(item.TemplateId);
            if (inConfig && LevelGateCheck.IsBlocked(player, item.TemplateId, out _)) return ItemState.Locked;
            if (MagazineSemiLock.GatedAmmoLevel(item) > 0) return ItemState.SemiLocked;
            return inConfig ? ItemState.Unlocked : ItemState.None;
        }

        private const string ChildName = "LevelGateStripes";
        private const int Tile = 32;       // texture size (px); the pattern repeats every Tile/2
        private const int LineWidth = 5;

        private static readonly string[] BackgroundFields = { "ColorPanel", "_colorPanel", "Background", "_background", "BackgroundImage", "_backgroundImage" };
        private static readonly Dictionary<int, GameObject> _byView = new Dictionary<int, GameObject>();
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private static bool _noBackgroundLogged, _foundLogged;
        private static int _cleanup;

        // The cell's built-in striped layer (GridItemView._pinBackground, "PinnedBackground"),
        // turned on by us — remembered so it can be handed back to the game's own pin state.
        private static readonly HashSet<int> _shownByUs = new HashSet<int>();

        private static GameObject BuiltInStripes(Component view)
        {
            var v = ReflectionUtil.GetMember(view, "_pinBackground") ?? ReflectionUtil.GetMember(view, "PinnedBackground");
            return v as GameObject ?? (v as Component)?.gameObject;
        }

        /// <summary>Whether the game itself wants the layer (the player pinned / locked this item).</summary>
        private static bool GamePinned(Component view)
        {
            var item = ReflectionUtil.GetMember(view, "Item");
            var state = ReflectionUtil.GetMember(item, "PinLockState")?.ToString();
            return state != null && state != "Free" && state != "None";
        }

        public static void Set(Component view, ItemState state)
        {
            if (LevelGatePlugin.StripesGameStyle?.Value ?? true)
            {
                var layer = BuiltInStripes(view);
                if (layer != null)
                {
                    HideOwn(view); // in case LevelGate style was on before
                    int id = layer.GetInstanceID();
                    if (state != ItemState.None)
                    {
                        if (!layer.activeSelf) { layer.SetActive(true); _shownByUs.Add(id); }
                    }
                    else if (_shownByUs.Remove(id))
                    {
                        layer.SetActive(GamePinned(view)); // give it back to the game
                    }
                    LogFound("the cell's built-in striped layer (_pinBackground)");
                    return;
                }
            }
            else
            {
                var layer = BuiltInStripes(view);
                if (layer != null && _shownByUs.Remove(layer.GetInstanceID())) layer.SetActive(GamePinned(view));
            }
            SetOwn(view, state);
        }

        private static void HideOwn(Component view)
        {
            if (_byView.TryGetValue(view.GetInstanceID(), out var own) && own != null && own.activeSelf) own.SetActive(false);
        }

        /// <summary>LevelGate style: our own drawn stripes, as a child of the cell background.</summary>
        private static void SetOwn(Component view, ItemState state)
        {
            int key = view.GetInstanceID();
            _byView.TryGetValue(key, out var stripes);
            if (stripes == null && _byView.ContainsKey(key)) _byView.Remove(key); // destroyed with its view

            if (state == ItemState.None)
            {
                if (stripes != null && stripes.activeSelf) stripes.SetActive(false);
                return;
            }

            if (stripes == null)
            {
                var background = FindBackground(view);
                if (background == null) return;

                stripes = new GameObject(ChildName, typeof(RectTransform));
                stripes.transform.SetParent(background.transform, false);
                var rect = (RectTransform)stripes.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;

                var image = stripes.AddComponent<UnityEngine.UI.Image>();
                image.type = UnityEngine.UI.Image.Type.Tiled;
                image.raycastTarget = false; // never steal clicks/drags from the item
                _byView[key] = stripes;

                if (++_cleanup % 200 == 0)
                    foreach (var dead in _byView.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList()) _byView.Remove(dead);
            }

            var img = stripes.GetComponent<UnityEngine.UI.Image>();
            var sprite = GetSprite(LevelGatePlugin.StripeStrength?.Value ?? "Medium");
            if (img.sprite != sprite) img.sprite = sprite;
            if (!stripes.activeSelf) stripes.SetActive(true);
            // Background is a separate image: last among ITS children (still below the item art).
            // Background is the cell itself: first child, so everything else draws over the stripes.
            if (stripes.transform.parent == view.transform) stripes.transform.SetAsFirstSibling();
            else stripes.transform.SetAsLastSibling();
        }

        /// <summary>The image that paints the cell's colored background.</summary>
        private static UnityEngine.UI.Graphic FindBackground(Component view)
        {
            foreach (var name in BackgroundFields)
            {
                if (ReflectionUtil.GetMember(view, name) is UnityEngine.UI.Graphic g && g != null && g.transform.IsChildOf(view.transform))
                {
                    LogFound("field " + name);
                    return g;
                }
            }

            // Fallback: the first image (in drawing order) that covers the whole cell.
            if (view.transform is RectTransform viewRect)
            {
                var size = viewRect.rect.size;
                foreach (var g in view.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (g.gameObject.name == ChildName || g.transform == view.transform) continue;
                    var r = ((RectTransform)g.transform).rect.size;
                    if (size.x > 1 && size.y > 1 && r.x >= size.x * 0.9f && r.y >= size.y * 0.9f)
                    {
                        LogFound("first full-size image '" + g.gameObject.name + "'");
                        return g;
                    }
                }
            }

            // Last resort: the cell paints its own background.
            if (view.GetComponent<UnityEngine.UI.Graphic>() is UnityEngine.UI.Graphic own && own != null)
            {
                LogFound("the cell's own image");
                return own;
            }

            if (!_noBackgroundLogged)
            {
                _noBackgroundLogged = true;
                LevelGatePlugin.Log.LogWarning("LevelGate: couldn't find the item cell background — stripes are skipped on this game version.");
            }
            return null;
        }

        private static void LogFound(string how)
        {
            if (_foundLogged) return;
            _foundLogged = true;
            LevelGatePlugin.Log.LogInfo("LevelGate: stripes drawn on the item background (" + how + ").");
        }

        /// <summary>A see-through tile with dark "/" stripes; strength = how dark.</summary>
        private static Sprite GetSprite(string strength)
        {
            if (_sprites.TryGetValue(strength, out var cached) && cached != null) return cached;
            float alpha = strength == "Subtle" ? 0.14f : strength == "Strong" ? 0.38f : 0.24f;

            var pixels = new Color32[Tile * Tile];
            int period = Tile / 2;
            for (int y = 0; y < Tile; y++)
            {
                for (int x = 0; x < Tile; x++)
                {
                    // distance along the "/" diagonal; soft 1px edges so it doesn't shimmer
                    int d = ((x - y) % period + period) % period;
                    float a = d < LineWidth ? alpha : (d == LineWidth ? alpha * 0.45f : (d == period - 1 ? alpha * 0.45f : 0f));
                    pixels[y * Tile + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            var texture = new Texture2D(Tile, Tile, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0, 0, Tile, Tile), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            _sprites[strength] = sprite;
            return sprite;
        }
    }

    // The logged-in PMC profile while there's no raid/hideout GameWorld
    // (main menu, stash, traders, flea). Found via the running
    // EFT.TarkovApplication -> Session -> Profile (the session's PMC
    // profile), all by reflection so a rename can't break the build; the
    // lookup is refreshed every couple of seconds so a level-up shows up.
    internal static class OutOfRaidProfile
    {
        private const float RefreshSeconds = 2f;

        private static bool _typeResolved;
        private static Type _appType;
        private static float _nextRefresh;
        private static object _cached;

        public static int? GetLevel()
        {
            var level = ReflectionUtil.GetMember(ReflectionUtil.GetMember(Get(), "Info"), "Level");
            return level is int n ? n : (int?)null;
        }

        public static object Get()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextRefresh) return _cached;
            _nextRefresh = now + RefreshSeconds;

            try
            {
                if (!_typeResolved)
                {
                    _typeResolved = true;
                    _appType = typeof(EFT.Player).Assembly.GetType("EFT.TarkovApplication", false);
                    if (_appType == null)
                        LevelGatePlugin.Log.LogWarning("LevelGate: EFT.TarkovApplication not found — out-of-raid labels/blocks will wait for a raid/hideout.");
                }
                if (_appType == null) return _cached = null;

                object app = null;
                var exist = _appType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Exist" && m.GetParameters().Length == 1 && m.GetParameters()[0].IsOut);
                if (exist != null)
                {
                    var args = new object[] { null };
                    exist.Invoke(null, args);
                    app = args[0];
                }
                if (app == null && typeof(UnityEngine.Object).IsAssignableFrom(_appType))
                    app = UnityEngine.Object.FindObjectOfType(_appType);

                var session = ReflectionUtil.GetMember(app, "Session");
                _cached = ReflectionUtil.GetMember(session, "Profile");
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate OutOfRaidProfile error: " + e);
                _cached = null;
            }
            return _cached;
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
                    // Looked up by hand rather than via AccessTools.Property/
                    // Field, which print a HarmonyX warning for every miss
                    // (the log showed "Could not find property ... _player").
                    getter = null;
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                    for (var t = key.Item1; t != null && getter == null; t = t.BaseType)
                    {
                        var prop = t.GetProperty(name, flags);
                        if (prop != null && prop.GetIndexParameters().Length == 0 && prop.GetGetMethod(true) != null)
                        {
                            getter = o => prop.GetValue(o, null);
                            break;
                        }
                        var field = t.GetField(name, flags);
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
    // rounds, or an "ammo pack" object holding them — searched a few levels
    // deep through fields, properties and collections, because the first
    // version (one level, fields only, and it stopped at the first
    // collection it met) read EFT.InventoryLogic.AmmoPack as empty
    // ("ReloadWithAmmo ammo=[]" in the log) while the Mosin/MP-153 reload
    // went ahead with gated rounds.
    //
    // Never descends into other Items (a magazine or weapon argument isn't
    // the rounds being loaded) or into controllers/inventories/players —
    // those would drag in every round you carry and block reloads that
    // aren't using gated ammo at all.
    internal static class AmmoCollector
    {
        private const int MaxDepth = 4;

        public static List<EFT.InventoryLogic.Ammo> CollectAll(IEnumerable<object> roots)
        {
            var into = new List<EFT.InventoryLogic.Ammo>();
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (var root in roots) Collect(root, into, visited, 0);
            return into;
        }

        private static void Collect(object obj, List<EFT.InventoryLogic.Ammo> into, HashSet<object> visited, int depth)
        {
            if (obj == null || depth > MaxDepth) return;

            if (obj is EFT.InventoryLogic.Ammo ammo)
            {
                if (!into.Contains(ammo)) into.Add(ammo);
                return;
            }
            if (!ShouldDescend(obj)) return;

            var type = obj.GetType();
            if (!type.IsValueType && !visited.Add(obj)) return;

            // Dictionary entries: System types are otherwise not walked.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Collect(type.GetProperty("Key")?.GetValue(obj, null), into, visited, depth + 1);
                Collect(type.GetProperty("Value")?.GetValue(obj, null), into, visited, depth + 1);
                return;
            }

            if (obj is System.Collections.IEnumerable enumerable)
            {
                int n = 0;
                foreach (var element in enumerable)
                {
                    if (++n > 512) break;
                    Collect(element, into, visited, depth + 1);
                }
            }

            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                if (t.Namespace != null && t.Namespace.StartsWith("System", StringComparison.Ordinal)) break;

                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object value;
                    try { value = f.GetValue(obj); }
                    catch { continue; }
                    Collect(value, into, visited, depth + 1);
                }

                foreach (var prop in t.GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (prop.GetIndexParameters().Length != 0 || prop.GetGetMethod(true) == null) continue;
                    // Only plain collection/ammo-typed properties: an arbitrary
                    // getter could do work or have side effects.
                    var pt = prop.PropertyType;
                    if (!typeof(EFT.InventoryLogic.Ammo).IsAssignableFrom(pt) &&
                        !typeof(System.Collections.IEnumerable).IsAssignableFrom(pt)) continue;
                    object value;
                    try { value = prop.GetValue(obj, null); }
                    catch { continue; }
                    Collect(value, into, visited, depth + 1);
                }
            }
        }

        private static bool ShouldDescend(object obj)
        {
            if (obj is EFT.InventoryLogic.Item) return false;
            if (obj is string || obj is Delegate || obj is UnityEngine.Object) return false;
            if (obj is EFT.InventoryLogic.ItemAddress || obj is EFT.Player) return false;

            var type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || type.IsPointer) return false;

            // Short type name only: the namespace "EFT.InventoryLogic" would
            // otherwise match "Inventory" and exclude AmmoPack itself.
            var name = type.Name;
            if (name.IndexOf("Controller", StringComparison.Ordinal) >= 0) return false;
            if (name.IndexOf("Inventory", StringComparison.Ordinal) >= 0) return false;
            if (name.IndexOf("Profile", StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        // Field/property layout of an object, for diagnostics.
        public static string Describe(object obj)
        {
            if (obj == null) return "null";
            var sb = new System.Text.StringBuilder(obj.GetType().FullName).Append(" { ");
            for (var t = obj.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    object value;
                    try { value = f.GetValue(obj); }
                    catch { value = "<error>"; }
                    sb.Append(f.Name).Append('(').Append(f.FieldType.Name).Append(")=").Append(Short(value)).Append("; ");
                }
            }
            return sb.Append('}').ToString();
        }

        private static string Short(object value)
        {
            if (value == null) return "null";
            if (value is EFT.InventoryLogic.Item item) return item.GetType().Name + ":" + item.TemplateId;
            if (value is System.Collections.ICollection c) return value.GetType().Name + "[" + c.Count + "]";
            return value.ToString();
        }
    }

    internal sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
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

            // Out of raid (stash/hideout) the controller is a
            // BackEndInventoryController, and this CanExecute runs INSIDE
            // its backend operation queue (ClientBackendSession
            // .ReadWaitingQueue), after the action was already accepted and
            // sent. Refusing there makes the queue retry the same operation
            // forever — the freeze with the looping loot sound when dropping
            // M882 into an MP-5 chamber in the stash. Out of raid, actions
            // are refused BEFORE they're queued instead
            // (Patch_BlockOperationResult / MoveResult / LoadMagazine /
            // ApplyItem), so this one must never block.
            if (IsBackendQueueController(__instance))
            {
                DiagnosticLogging.LogCall($"Patch_BlockOperation skipped (backend queue) op={operation?.GetType().Name ?? "NULL"}");
                return true;
            }

            bool allowed = true;
            Evaluate(operation, ref allowed, "Patch_BlockOperation", startingResult: true);
            if (!allowed)
            {
                __result = false;
                return false; // skip original
            }
            return true; // let the real logic decide
        }

        private static readonly Dictionary<Type, bool> _backendTypeCache = new Dictionary<Type, bool>();

        internal static bool IsBackendQueueController(object controller)
        {
            if (controller == null) return false;
            var type = controller.GetType();
            if (!_backendTypeCache.TryGetValue(type, out bool isBackend))
            {
                isBackend = false;
                for (var t = type; t != null; t = t.BaseType)
                {
                    if (t.Name == "BackEndInventoryController") { isBackend = true; break; }
                }
                _backendTypeCache[type] = isBackend;
            }
            return isBackend;
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

                // May be null out of raid (stash) — IsBlocked then uses the
                // PMC profile's level, so equipping in the stash is gated too.
                var player = LevelGateCheck.GetMainPlayer();

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
                    LevelGateCheck.Notify(req1, oneItem.Item1, onScreen: true);
                    result = false;
                    return;
                }

                if (operation is EFT.InventoryLogic.Operations.ITwoItemOperation twoItem &&
                    (TryBlockEquip(twoItem.Item2, twoItem.To2, player, out int req2) ||
                     TryBlockAmmoLoad(twoItem.Item2, twoItem.To2, player, patchName, operation, out req2)))
                {
                    LevelGateCheck.Notify(req2, twoItem.Item2, onScreen: true);
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
            // Unloading must always work — getting gated rounds OUT of a
            // magazine/gun is exactly what the player is supposed to do.
            // "Unload" in patchName means this is the inner operation of an
            // UnloadMagOperation (Evaluate appends the wrapper's type name
            // when it unwraps one), whose internal Transfer/Merge the game
            // reported as "Can't execute" in the user's screenshot.
            bool unloading = patchName.IndexOf("Unload", StringComparison.Ordinal) >= 0
                             || operation.GetType().Name.IndexOf("Unload", StringComparison.Ordinal) >= 0;
            bool alreadyInside = LevelGateCheck.IsAlreadyInside(item, to);
            DiagnosticLogging.LogCall(
                $"{patchName} AMMO op={operation.GetType().Name} ammo={item.TemplateId} " +
                $"toType={to?.GetType().Name ?? "null"} toParent={parent?.GetType().Name ?? "null"} " +
                $"intoGun={intoGun} unloading={unloading} alreadyInside={alreadyInside}");

            if (!intoGun || unloading || alreadyInside) return false;
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
        static bool Prefix(object __instance, object[] __args, ref bool __result)
        {
            try
            {
                var item = __args.OfType<EFT.InventoryLogic.Item>().FirstOrDefault();
                if (item == null)
                {
                    DiagnosticLogging.LogCall("ApplyItem (no Item arg found)");
                    return true;
                }

                // In raid every bot has a health controller too — only judge
                // the local player's.
                if (ReflectionUtil.GetMember(__instance, "Player") is EFT.Player owner &&
                    !ReferenceEquals(owner, LevelGateCheck.GetMainPlayer()))
                {
                    return true;
                }

                var player = LevelGateCheck.GetMainPlayer();
                bool inConfig = LevelGatePlugin.Data.Items.TryGetValue(item.TemplateId, out int configuredLevel);
                int currentLevel = player?.Profile?.Info?.Level ?? -1;
                DiagnosticLogging.LogCall(
                    $"ApplyItem item={item.TemplateId} player={(player == null ? "NULL" : "found")} " +
                    $"inConfig={inConfig} configuredLevel={configuredLevel} currentLevel={currentLevel}");
                // player == null: out of raid — the stash "Use" button runs
                // through OfflineHealthController.ApplyItem with no in-raid
                // player (how the MRE got through). IsBlocked then uses the
                // PMC profile's level.

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
    // -----------------------------------------------------------------
    // Pre-check for EVERY kind of operation result — split, merge,
    // transfer, swap, not just MoveResult. ItemController
    // .CanExecute(IOperationResult) is what the UI (GridView.AcceptOnce /
    // RunNetworkTransaction, per the logs) asks before running a drop or
    // a transaction. Only MoveResult was covered before, so splitting a few
    // gated rounds off a stack into a chamber showed green and went ahead
    // — out of raid straight into the backend queue, where the late refusal
    // looped forever. The item and destination are read by name
    // ("Item"/"To") so any result type exposing them is covered; types
    // that don't are logged once.
    // -----------------------------------------------------------------
    [HarmonyPatch(typeof(EFT.InventoryLogic.ItemController), "CanExecute", new[] { typeof(EFT.InventoryLogic.IOperationResult) })]
    internal static class Patch_BlockOperationResult
    {
        // For results without a "To" address: the item the round is being
        // put onto/into. A TargetItem that is itself a gun/magazine is the
        // destination; a TargetItem that is a stack of rounds means "the
        // container that stack sits in". Falls back to any Magazine/Weapon
        // the result carries (e.g. the magazine on a mag-loading result).
        private static EFT.InventoryLogic.Item DestinationFromTarget(object result, EFT.InventoryLogic.Item movingItem)
        {
            var target = ReflectionUtil.GetMember(result, "TargetItem") as EFT.InventoryLogic.Item
                         ?? ReflectionUtil.GetMember(result, "Target") as EFT.InventoryLogic.Item;
            if (target == null)
            {
                for (var t = result.GetType(); t != null && t != typeof(object) && target == null; t = t.BaseType)
                {
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(EFT.InventoryLogic.Magazine).IsAssignableFrom(f.FieldType) &&
                            !typeof(EFT.InventoryLogic.Weapon).IsAssignableFrom(f.FieldType)) continue;
                        target = f.GetValue(result) as EFT.InventoryLogic.Item;
                        if (target != null) break;
                    }
                    if (target != null) break;
                    foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (p.GetIndexParameters().Length != 0) continue;
                        if (!typeof(EFT.InventoryLogic.Magazine).IsAssignableFrom(p.PropertyType) &&
                            !typeof(EFT.InventoryLogic.Weapon).IsAssignableFrom(p.PropertyType)) continue;
                        try { target = p.GetValue(result, null) as EFT.InventoryLogic.Item; } catch { }
                        if (target != null) break;
                    }
                }
            }
            if (target == null || ReferenceEquals(target, movingItem)) return null;

            if (target is EFT.InventoryLogic.Magazine || target is EFT.InventoryLogic.Weapon)
                return target;

            var targetAddress = ReflectionUtil.GetMember(target, "CurrentAddress") as EFT.InventoryLogic.ItemAddress
                                ?? ReflectionUtil.GetMember(target, "Parent") as EFT.InventoryLogic.ItemAddress;
            return LevelGateCheck.GetContainerParentItem(targetAddress);
        }

        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.InventoryLogic.ItemController __instance, EFT.InventoryLogic.IOperationResult __0, ref bool __result)
        {
            try
            {
                if (__0 == null || !LevelGateCheck.IsOwnInventoryController(__instance)) return true;

                var item = ReflectionUtil.GetMember(__0, "Item") as EFT.InventoryLogic.Item;
                if (item == null)
                {
                    DiagnosticLogging.LogCall($"Patch_BlockOperationResult no Item on {__0.GetType().Name}: {AmmoCollector.Describe(__0)}");
                    return true;
                }

                var player = LevelGateCheck.GetMainPlayer(); // null out of raid -> profile level
                var to = ReflectionUtil.GetMember(__0, "To") as EFT.InventoryLogic.ItemAddress;
                string resultName = __0.GetType().Name;
                bool blocked = false;
                int required = 0;

                if (resultName.IndexOf("Bind", StringComparison.Ordinal) >= 0 &&
                    resultName.IndexOf("Unbind", StringComparison.Ordinal) < 0)
                {
                    // Quick-slot binding (BindResult: no destination, just
                    // the item and a slot index). Out of raid the in-queue
                    // BindItemOperation check can't block, so refuse here.
                    blocked = LevelGateCheck.IsBlocked(player, item.TemplateId, out required);
                }
                else if (item is EFT.InventoryLogic.Ammo)
                {
                    // Where the round ends up. Moves/splits have To; merges
                    // and transfers (stacking onto the rounds already inside
                    // a Mosin/MP-153 internal magazine or a box magazine)
                    // have a TargetItem instead; magazine-loading results
                    // carry the magazine itself.
                    var destParent = LevelGateCheck.GetContainerParentItem(to) ?? DestinationFromTarget(__0, item);
                    var fromParent = LevelGateCheck.GetContainerParentItem(
                        ReflectionUtil.GetMember(item, "CurrentAddress") as EFT.InventoryLogic.ItemAddress
                        ?? ReflectionUtil.GetMember(item, "Parent") as EFT.InventoryLogic.ItemAddress);

                    bool intoGun = destParent is EFT.InventoryLogic.Weapon ||
                                   (destParent is EFT.InventoryLogic.Magazine &&
                                    destParent.GetType().Name.IndexOf("AmmoBox", StringComparison.OrdinalIgnoreCase) < 0);

                    blocked = intoGun
                              && !ReferenceEquals(fromParent, destParent)   // not unloading/shuffling inside it
                              && LevelGateCheck.IsBlocked(player, item.TemplateId, out required);
                }
                else if (to != null)
                {
                    blocked = to is EFT.InventoryLogic.SlotItemAddress slotAddress
                              && LevelGateCheck.EquipSlotIds.Contains(slotAddress.Slot.ID)
                              && LevelGateCheck.IsBlocked(player, item.TemplateId, out required);
                }

                if (blocked)
                {
                    DiagnosticLogging.LogCall($"Patch_BlockOperationResult blocked {__0.GetType().Name} item={item.TemplateId}");
                    // Ammo into a gun/magazine is one of the deliberate
                    // actions that shows "Ammo Level Too High" (this may now
                    // be the first check a manual chamber load hits).
                    LevelGateCheck.Notify(required, item, onScreen: item is EFT.InventoryLogic.Ammo);
                    __result = false;
                    return false;
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate Patch_BlockOperationResult error: " + e);
            }
            return true;
        }
    }

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
                // player may be null out of raid -> IsBlocked uses the PMC profile level

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
                    shouldCheck = LevelGateCheck.IsLoadIntoWeaponOrMagazine(__instance.To)
                                  && !LevelGateCheck.IsAlreadyInside(item, __instance.To);
                }
                else
                {
                    shouldCheck = __instance.To is EFT.InventoryLogic.SlotItemAddress slotAddress
                        && LevelGateCheck.EquipSlotIds.Contains(slotAddress.Slot.ID);
                }

                if (shouldCheck && LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required, item);
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

    // -----------------------------------------------------------------
    // SEMI LOCKED magazines — a magazine you're allowed to use, but with
    // at least one gated round inside, is labelled "[SEMI LOCKED]" (short
    // name) / "[SEMI LOCKED - Lvl X] <name>" (full name, X = the highest
    // level its rounds need) with an ORANGE background — alongside the
    // existing [LOCKED]/red and [UNLOCKED]/green labels. Magazines only
    // (ammo boxes are Magazine-derived in EFT but excluded).
    //
    // The existing labels work per TEMPLATE (the localization key is
    // "<tplId> Name", with no item instance), but "has gated rounds
    // inside" is per INSTANCE. So Item.Name / Item.ShortName are patched
    // for magazines only: while one holds gated rounds, its key gets a
    // marker prefix ("\u0001LGSEMI:<level>:<tplId> ShortName"). When the UI
    // localizes that key, the Localized postfix recognises the marker,
    // resolves the real key itself and returns the SEMI LOCKED text — so
    // the key never reaches the UI unresolved (which is what broke the
    // first name-patching attempt described in PATCH 9).
    // -----------------------------------------------------------------
    internal static class MagazineSemiLock
    {
        private const string KeyMarker = "\u0001LGSEMI:";

        private static bool _orangeResolved;
        private static JsonType.TaxonomyColor _orange;

        // Resolved by name so it builds whether or not this game version's
        // TaxonomyColor has "orange" (falls back to yellow).
        public static JsonType.TaxonomyColor OrangeColor
        {
            get
            {
                if (!_orangeResolved)
                {
                    _orangeResolved = true;
                    if (!Enum.TryParse("orange", true, out _orange) &&
                        !Enum.TryParse("yellow", true, out _orange))
                    {
                        _orange = JsonType.TaxonomyColor.red;
                    }
                }
                return _orange;
            }
        }

        public static bool IsLabelledMagazine(EFT.InventoryLogic.Item item)
        {
            return item is EFT.InventoryLogic.Magazine
                   && item.GetType().Name.IndexOf("AmmoBox", StringComparison.OrdinalIgnoreCase) < 0;
        }

        // Highest level required by a gated round inside this magazine, or 0
        // if it isn't a (non-ammo-box) magazine or holds nothing gated.
        public static int GatedAmmoLevel(EFT.InventoryLogic.Item item)
        {
            try
            {
                if (!IsLabelledMagazine(item)) return 0;
                var player = LevelGateCheck.GetMainPlayer(); // null out of raid -> profile level

                var contents = FireGate.InvokeNoArgs(item, "GetAllItems") as System.Collections.IEnumerable
                               ?? ReflectionUtil.GetMember(ReflectionUtil.GetMember(item, "Cartridges"), "Items") as System.Collections.IEnumerable;
                if (contents == null) return 0;

                int max = 0;
                foreach (var obj in contents)
                {
                    if (obj is EFT.InventoryLogic.Ammo ammo &&
                        LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required) &&
                        required > max)
                    {
                        max = required;
                    }
                }
                return max;
            }
            catch
            {
                return 0;
            }
        }

        public static bool TryParseKey(string key, out int level, out string realKey)
        {
            level = 0;
            realKey = null;
            if (key == null || !key.StartsWith(KeyMarker, StringComparison.Ordinal)) return false;
            int sep = key.IndexOf(':', KeyMarker.Length);
            if (sep < 0 || !int.TryParse(key.Substring(KeyMarker.Length, sep - KeyMarker.Length), out level)) return false;
            realKey = key.Substring(sep + 1);
            return true;
        }

        public static void PatchNames(Harmony harmony)
        {
            try
            {
                var postfix = new HarmonyMethod(typeof(MagazineSemiLock), nameof(NamePostfix));
                var targets = new List<MethodInfo>();
                foreach (var name in new[] { "Name", "ShortName" })
                {
                    var baseGetter = AccessTools.PropertyGetter(typeof(EFT.InventoryLogic.Item), name);
                    if (baseGetter != null) targets.Add(baseGetter);

                    // Any magazine subclass that overrides the getter needs
                    // its own patch.
                    foreach (var t in AccessTools.AllTypes().Where(t => typeof(EFT.InventoryLogic.Magazine).IsAssignableFrom(t)))
                    {
                        var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        var getter = prop?.GetGetMethod(true);
                        if (getter != null && !getter.IsAbstract && !targets.Contains(getter)) targets.Add(getter);
                    }
                }

                foreach (var target in targets)
                {
                    harmony.Patch(target, postfix: postfix);
                    LevelGatePlugin.Log.LogInfo($"LevelGate: SEMI LOCKED label hooked: {target.DeclaringType?.FullName}.{target.Name}");
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to patch item names for SEMI LOCKED magazines. " + e);
            }
        }

        private static void NamePostfix(EFT.InventoryLogic.Item __instance, ref string __result)
        {
            try
            {
                if (string.IsNullOrEmpty(__result) || __result.StartsWith(KeyMarker, StringComparison.Ordinal)) return;
                if (!IsLabelledMagazine(__instance)) return;

                // A magazine that is itself LOCKED keeps its [LOCKED] label.
                var player = LevelGateCheck.GetMainPlayer(); // null out of raid -> profile level
                if (LevelGateCheck.IsBlocked(player, __instance.TemplateId, out _)) return;

                int level = GatedAmmoLevel(__instance);
                if (level > 0) __result = KeyMarker + level + ":" + __result;
            }
            catch
            {
                // never break item names
            }
        }
    }

    // Builds the label text and background colors from the user's settings
    // (F9 window / BepInEx config sections "Labels" and "Colors").
    internal static class LabelStyle
    {
        // An empty label leaves the game's own short name untouched.
        public static string Short(ConfigEntry<string> label, string original)
        {
            string tag = Wrap(Text(label));
            return string.IsNullOrEmpty(tag) ? original : tag;
        }

        public static string Full(ConfigEntry<string> label, int level, string name)
        {
            string tag = LevelGatePlugin.LabelShowLevel?.Value ?? true ? $"{Text(label)} - Lvl {level}" : Text(label);
            tag = Wrap(tag);
            return string.IsNullOrEmpty(tag) ? name : tag + " " + name;
        }

        private static string Text(ConfigEntry<string> label) => label?.Value ?? "";

        private static string Wrap(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return LevelGatePlugin.LabelBrackets?.Value ?? true ? "[" + text + "]" : text;
        }

        public static JsonType.TaxonomyColor LockedColor => Parse(LevelGatePlugin.LockedColor, JsonType.TaxonomyColor.red);
        public static JsonType.TaxonomyColor UnlockedColor => Parse(LevelGatePlugin.UnlockedColor, JsonType.TaxonomyColor.green);
        public static JsonType.TaxonomyColor SemiLockedColor => Parse(LevelGatePlugin.SemiLockedColor, MagazineSemiLock.OrangeColor);

        // BackgroundColor is queried constantly, so parsed values are cached
        // per name.
        private static readonly Dictionary<string, JsonType.TaxonomyColor> _parsed = new Dictionary<string, JsonType.TaxonomyColor>();

        private static JsonType.TaxonomyColor Parse(ConfigEntry<string> entry, JsonType.TaxonomyColor fallback)
        {
            string name = entry?.Value;
            if (string.IsNullOrEmpty(name)) return fallback;
            if (!_parsed.TryGetValue(name, out var color))
            {
                if (!Enum.TryParse(name, true, out color)) color = fallback;
                _parsed[name] = color;
            }
            return color;
        }
    }

    // -----------------------------------------------------------------
    // Hover tooltip layout. Just before the game shows a tooltip
    // (SimpleTooltip.Show(text) — the same call Show Me The Money appends its
    // prices to), this finds the item name line and rewrites it:
    //   locked:      LOCKED - Bandages      /  Unlocks At Level 2
    //   semi locked: SEMI LOCKED - PMAG     /  Rounds Unlock At Level 2
    //   unlocked:    Bandages               /  Unlocked At Level 1
    // (levels in yellow). The item is recognised from the name text itself
    // (every name LevelGate hands out is remembered), so it works the same in
    // the stash, the inventory, containers and trader screens — no hover
    // tracking needed. Runs last, so other mods' lines stay below, untouched.
    // -----------------------------------------------------------------
    internal static class TooltipLayoutPatch
    {
        private const string Yellow = "#FFD24A";
        private static string _hoveredTpl; // like Show Me The Money: GridItemView.OnPointerEnter / OnPointerExit
        private static readonly HashSet<string> SmallWords = new HashSet<string> { "of", "and", "the", "for", "with", "in", "on", "a", "an", "to", "or" };

        public static void Apply(Harmony harmony)
        {
            try
            {
                var tooltip = AccessTools.TypeByName("EFT.UI.SimpleTooltip");
                var show = tooltip == null ? null : AccessTools.GetDeclaredMethods(tooltip)
                    .FirstOrDefault(m => m.Name == "Show" && m.GetParameters().Length > 0 && m.GetParameters()[0].Name == "text" && m.GetParameters()[0].ParameterType == typeof(string));
                if (show == null)
                {
                    LevelGatePlugin.Log.LogWarning("LevelGate: SimpleTooltip.Show(text) not found — tooltip layout is off.");
                    return;
                }
                harmony.Patch(show, prefix: new HarmonyMethod(typeof(TooltipLayoutPatch), nameof(BeforeShow)) { priority = Priority.Last });

                // The item under the pointer, the way Show Me The Money does it: every grid cell
                // (stash, inventory, containers, trader screens, in raid) is a GridItemView.
                bool hover = false;
                var grid = AccessTools.TypeByName("EFT.UI.DragAndDrop.GridItemView");
                var enter = grid == null ? null : AccessTools.GetDeclaredMethods(grid).FirstOrDefault(m => m.Name == "OnPointerEnter" && m.GetParameters().Length == 1);
                var exit = grid == null ? null : AccessTools.GetDeclaredMethods(grid).FirstOrDefault(m => m.Name == "OnPointerExit" && m.GetParameters().Length == 1);
                if (enter != null) { harmony.Patch(enter, postfix: new HarmonyMethod(typeof(TooltipLayoutPatch), nameof(OnEnter))); hover = true; }
                if (exit != null) harmony.Patch(exit, postfix: new HarmonyMethod(typeof(TooltipLayoutPatch), nameof(OnExit)));
                LevelGatePlugin.Log.LogInfo($"LevelGate: tooltip layout hooked: {show.DeclaringType?.Name}.Show{(hover ? " + GridItemView hover" : "")}.");
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: tooltip layout hooks failed. " + e);
            }
        }

        private static void OnEnter(object __instance)
        {
            _hoveredTpl = (ReflectionUtil.GetMember(__instance, "Item") as EFT.InventoryLogic.Item)?.TemplateId;
        }

        private static void OnExit() { _hoveredTpl = null; }

        private static void BeforeShow(ref string text)
        {
            try
            {
                if (!(LevelGatePlugin.TooltipLayout?.Value ?? true) || string.IsNullOrEmpty(text)) return;

                // the name is the tooltip's first line (sometimes wrapped in <b>/<color> tags by the game or other mods)
                int lineEnd = text.IndexOf('\n');
                string firstLine = lineEnd < 0 ? text : text.Substring(0, lineEnd);
                string key = firstLine;
                int at = -1;
                if (!ItemRenameShared.ShownNames.TryGetValue(key, out var info))
                {
                    key = StripTags(firstLine).Trim();
                    if (!ItemRenameShared.ShownNames.TryGetValue(key, out info)) key = null;
                }
                if (key != null) at = text.IndexOf(key, StringComparison.Ordinal);
                if (at < 0 && _hoveredTpl != null)
                {
                    // name not on the first line as-is: look for any name of the hovered item in the text
                    key = null;
                    foreach (var kv in ItemRenameShared.ShownNames)
                        if (kv.Value.Tpl == _hoveredTpl && (key == null || kv.Key.Length > key.Length) && text.IndexOf(kv.Key, StringComparison.Ordinal) >= 0)
                        { key = kv.Key; info = kv.Value; }
                    if (key != null) at = text.IndexOf(key, StringComparison.Ordinal);
                }
                if (at < 0) return;

                string tpl = info.Tpl;
                if (!ItemRenameShared.PlainNames.TryGetValue(tpl, out var plain) || string.IsNullOrEmpty(plain)) return;
                string name = TitleCase(plain);

                string header, levelLine;
                if (LevelGatePlugin.Data.Items.TryGetValue(tpl, out int required) && LevelGateCheck.IsBlocked(LevelGateCheck.GetMainPlayer(), tpl, out required))
                {
                    string word = LevelGatePlugin.LockedLabel?.Value ?? "";
                    header = string.IsNullOrEmpty(word) ? name : $"{word} - {name}";
                    levelLine = $"Unlocks At <color={Yellow}>Level {required}</color>";
                }
                else if (info.SemiLevel > 0)
                {
                    string word = LevelGatePlugin.SemiLockedLabel?.Value ?? "";
                    header = string.IsNullOrEmpty(word) ? name : $"{word} - {name}";
                    levelLine = $"Rounds Unlock At <color={Yellow}>Level {info.SemiLevel}</color>";
                }
                else if (LevelGatePlugin.Data.Items.TryGetValue(tpl, out required))
                {
                    header = name;
                    levelLine = $"Unlocked At <color={Yellow}>Level {required}</color>";
                }
                else return;

                if (text.Contains(levelLine)) return; // already rewritten
                text = text.Substring(0, at) + header + "\n" + levelLine + text.Substring(at + key.Length);
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate tooltip layout error: " + e);
            }
        }

        private static string StripTags(string s)
        {
            if (s.IndexOf('<') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') inTag = true;
                else if (c == '>' && inTag) inTag = false;
                else if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>"Can of beef stew (Large)" -> "Can of Beef Stew (Large)"; numbers / codes stay as they are.</summary>
        private static string TitleCase(string name)
        {
            var words = name.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (w.Length == 0 || !char.IsLower(w[0])) continue;
                if (i > 0 && SmallWords.Contains(w)) continue;
                words[i] = char.ToUpperInvariant(w[0]) + w.Substring(1);
            }
            return string.Join(" ", words);
        }
    }

    internal static class ItemRenameShared
    {
        /// <summary>The game's own full name per template id, before LevelGate's label (for the tooltip layout).</summary>
        public static readonly Dictionary<string, string> PlainNames = new Dictionary<string, string>();

        /// <summary>Every full name LevelGate hands out for a limited item (labelled or plain) -> which item it is,
        /// so a tooltip can be recognised from its text alone.</summary>
        public static readonly Dictionary<string, (string Tpl, int SemiLevel)> ShownNames = new Dictionary<string, (string, int)>();

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

                if (MagazineSemiLock.TryParseKey(key, out int semiLevel, out string realKey))
                {
                    // Resolve the real key (this re-enters this postfix for
                    // it, which is fine — it's a normal "<tpl> Name" key).
                    string resolved = EFT.LocalizationExtensions.Localized(realKey, "");
                    bool shortName = realKey.EndsWith(" ShortName", StringComparison.Ordinal);
                    result = shortName
                        ? LabelStyle.Short(LevelGatePlugin.SemiLockedLabel, resolved)
                        : LabelStyle.Full(LevelGatePlugin.SemiLockedLabel, semiLevel, resolved);
                    if (!shortName && realKey.EndsWith(" Name", StringComparison.Ordinal))
                    {
                        string semiTpl = realKey.Substring(0, realKey.Length - " Name".Length);
                        if (!PlainNames.ContainsKey(semiTpl)) PlainNames[semiTpl] = resolved;
                        ShownNames[result] = (semiTpl, semiLevel);
                    }
                    return;
                }

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
                if (showLevel) PlainNames[templateId] = result;

                // Not in the config at all — nothing to label either way.
                if (!LevelGatePlugin.Data.Items.TryGetValue(templateId, out int required)) return;

                var player = LevelGateCheck.GetMainPlayer(); // null out of raid -> profile level

                if (LevelGateCheck.IsBlocked(player, templateId, out required))
                {
                    result = showLevel
                        ? LabelStyle.Full(LevelGatePlugin.LockedLabel, required, result)
                        : LabelStyle.Short(LevelGatePlugin.LockedLabel, result);
                    if (showLevel) ShownNames[result] = (templateId, 0);
                    return;
                }
                // Unlocked: the name stays the game's own (so the short name is still
                // readable in the grid); the green background shows it's tracked,
                // and the tooltip adds "Unlocked At Level X".
                if (showLevel) ShownNames[result] = (templateId, 0);
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
    //
    // UPDATE: now also checks every round in the current magazine /
    // internal magazine / tube, not just the chamber, and shows the
    // on-screen "Ammo Level Too High" message. That covers a gun picked up
    // (or loaded before the gate was set) with gated rounds already inside:
    // pressing M1 is refused until those rounds are ejected/unloaded.
    // The trigger press itself (FirearmController.SetTriggerPressed) is
    // hooked too, in case CanPressTrigger isn't consulted on every path.
    [HarmonyPatch(typeof(EFT.ClientFirearmController), "CanPressTrigger")]
    internal static class Patch_BlockFireRestrictedAmmo
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(EFT.ClientFirearmController __instance, ref bool __result)
        {
            // Polled by the game (not only on M1), so block silently here;
            // the message comes from the actual press in SetTriggerPressed.
            if (!FireGate.ShouldBlockFire(__instance, showMessage: false)) return true;
            __result = false;
            return false; // skip original — trigger can't be pressed
        }
    }

    internal static class FireGate
    {
        public static void PatchTriggerPress(Harmony harmony)
        {
            try
            {
                Type baseType = typeof(EFT.ClientFirearmController);
                while (baseType != null && baseType.Name != "FirearmController") baseType = baseType.BaseType;
                if (baseType == null) return;

                var prefix = new HarmonyMethod(typeof(FireGate), nameof(SetTriggerPressedPrefix)) { priority = Priority.First };
                foreach (var type in AccessTools.AllTypes().Where(t => baseType.IsAssignableFrom(t)))
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (method.Name != "SetTriggerPressed" || method.IsAbstract || method.ReturnType != typeof(void)) continue;
                        var ps = method.GetParameters();
                        if (ps.Length != 1 || ps[0].ParameterType != typeof(bool)) continue;
                        try
                        {
                            harmony.Patch(method, prefix: prefix);
                            LevelGatePlugin.Log.LogInfo($"LevelGate: trigger hooked: {type.FullName}.SetTriggerPressed(Boolean)");
                        }
                        catch (Exception e)
                        {
                            LevelGatePlugin.Log.LogError($"LevelGate: failed to patch {type.Name}.SetTriggerPressed. " + e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to patch SetTriggerPressed. " + e);
            }
        }

        // Only a PRESS is refused; releasing the trigger always goes through
        // so the weapon can never get stuck "held".
        private static bool SetTriggerPressedPrefix(object __instance, bool __0)
        {
            if (!__0) return true;
            return !ShouldBlockFire(__instance, showMessage: true);
        }

        public static bool ShouldBlockFire(object firearmController, bool showMessage)
        {
            try
            {
                if (!LevelGateCheck.IsOwnHandsController(firearmController)) return false;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return false;

                var weapon = ReflectionUtil.GetMember(firearmController, "Item") as EFT.InventoryLogic.Weapon;
                if (weapon == null) return false;

                // Second ownership check on the gun itself, so a bot's
                // controller can never be mistaken for yours.
                if (!LevelGateCheck.IsOwnItem(weapon)) return false;

                foreach (var ammo in LoadedRounds(weapon))
                {
                    if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                    {
                        DiagnosticLogging.LogCall($"FireGate blocked weapon={weapon.TemplateId} ammo={ammo.TemplateId}");
                        LevelGateCheck.Notify(required, ammo, onScreen: showMessage);
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate FireGate error: " + e);
            }
            return false;
        }

        // Chambered round(s) plus everything in the magazine the gun is
        // using: a box magazine, a Mosin-style internal magazine, or a
        // shotgun tube (all are Magazine items in the weapon's magazine
        // slot). Members resolved by reflection: Weapon.Chambers ->
        // Slot.ContainedItem, Weapon.GetCurrentMagazine(), and the
        // magazine's own contents via Item.GetAllItems() (falling back to
        // Cartridges.Items). A revolver cylinder / anything else is covered
        // by the chamber check plus the GetAllItems fallback on the weapon.
        public static List<EFT.InventoryLogic.Ammo> LoadedRounds(EFT.InventoryLogic.Weapon weapon)
        {
            var rounds = new List<EFT.InventoryLogic.Ammo>();

            if (ReflectionUtil.GetMember(weapon, "Chambers") is System.Collections.IEnumerable chambers)
            {
                foreach (var slot in chambers)
                {
                    if (ReflectionUtil.GetMember(slot, "ContainedItem") is EFT.InventoryLogic.Ammo chambered)
                        rounds.Add(chambered);
                }
            }

            var magazine = InvokeNoArgs(weapon, "GetCurrentMagazine") as EFT.InventoryLogic.Item;
            if (magazine != null)
            {
                var contents = InvokeNoArgs(magazine, "GetAllItems") as System.Collections.IEnumerable
                               ?? ReflectionUtil.GetMember(ReflectionUtil.GetMember(magazine, "Cartridges"), "Items") as System.Collections.IEnumerable;
                if (contents != null)
                {
                    foreach (var obj in contents)
                    {
                        if (obj is EFT.InventoryLogic.Ammo a && !rounds.Contains(a)) rounds.Add(a);
                    }
                }
            }
            else
            {
                // No detachable/internal magazine found (cylinder, break
                // action, or the method didn't resolve): fall back to every
                // round anywhere in the weapon.
                if (InvokeNoArgs(weapon, "GetAllItems") is System.Collections.IEnumerable all)
                {
                    foreach (var obj in all)
                    {
                        if (obj is EFT.InventoryLogic.Ammo a && !rounds.Contains(a)) rounds.Add(a);
                    }
                }
            }

            return rounds;
        }

        private static readonly Dictionary<(Type, string), MethodInfo> _methodCache = new Dictionary<(Type, string), MethodInfo>();

        internal static object InvokeNoArgs(object instance, string name)
        {
            if (instance == null) return null;
            try
            {
                var key = (instance.GetType(), name);
                if (!_methodCache.TryGetValue(key, out var method))
                {
                    method = null;
                    for (var t = key.Item1; t != null && method == null; t = t.BaseType)
                    {
                        method = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 0 && !m.IsGenericMethodDefinition);
                    }
                    _methodCache[key] = method;
                }
                return method?.Invoke(instance, null);
            }
            catch
            {
                return null;
            }
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
                if (!inConfig)
                {
                    // Removed from the limiter (F9 "X" / "Level Limit
                    // Remove") — still undo any earlier neutralization.
                    // This used to return straight away, which is why water
                    // stayed at 0/60 after being un-gated.
                    ItemNeutralizer.Sync(__instance, false);
                    if (MagazineSemiLock.GatedAmmoLevel(__instance) > 0)
                        __result = LabelStyle.SemiLockedColor;
                    return;
                }

                var player = LevelGateCheck.GetMainPlayer(); // null out of raid -> profile level

                // LOCKED (red) wins; a usable magazine holding gated rounds
                // is SEMI LOCKED (orange); otherwise UNLOCKED (green).
                bool blocked = LevelGateCheck.IsBlocked(player, __instance.TemplateId, out _);
                __result = blocked ? LabelStyle.LockedColor
                    : MagazineSemiLock.GatedAmmoLevel(__instance) > 0 ? LabelStyle.SemiLockedColor
                    : LabelStyle.UnlockedColor;

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
    // and restore it once the player reaches the required level OR the item
    // is removed from the limiter, so nothing is lost — an item just becomes
    // usable again exactly as it was. Med/food originals are saved to
    // config/neutralized_resources.json by item id so they survive restarts
    // and raids (the ammo caliber lives on the shared template, which the
    // game reloads from the server on every start, so it needs no file).
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

        // Original med/food resource per item INSTANCE id, saved to
        // config/neutralized_resources.json. It used to live only in memory
        // (keyed by the component object), but the zeroed value is saved to
        // your profile like any other item change — so after a restart, or
        // once the raid re-created the item, the original was gone and the
        // item stayed at 0/60 even after the level was met. Item ids survive
        // restarts, so keying the saved file by id makes the restore exact.
        private static Dictionary<string, float> _originalResources;

        private static string ResourceFile =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "config", "neutralized_resources.json");

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
                    var component = meds.MedKitComponent;
                    if (component == null) return;
                    SyncResource(item, shouldBeLocked,
                        () => component.HpResource,
                        v => component.HpResource = v,
                        component, "MaxHpResource", "MaxResource");
                }
                else if (item is EFT.InventoryLogic.FoodDrink food)
                {
                    var component = food.FoodDrinkComponent;
                    if (component == null) return;
                    SyncResource(item, shouldBeLocked,
                        () => component.HpPercent,
                        v => component.HpPercent = v,
                        component, "MaxResource", "MaxHpPercent");
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate ItemNeutralizer error: " + e);
            }
        }

        // The ammo caliber lock is RETIRED. It rewrote AmmoTemplate.Caliber,
        // which is shared by every copy of that ammo in the raid — so bots
        // carrying e.g. M855 couldn't reload it either (and it made R on the
        // M4A1 silently do nothing instead of showing the level message).
        // Every player load/reload/fire path is now blocked directly, and
        // only for your own gear, so this only undoes a caliber a previous
        // build may have changed in this session.
        private static void SyncAmmo(EFT.InventoryLogic.Ammo ammo, bool locked)
        {
            var template = ammo.AmmoTemplate;
            if (template == null) return;

            if (template.Caliber == LockedCaliberMarker
                && _originalCalibers.TryGetValue(ammo.TemplateId, out var original))
            {
                template.Caliber = original;
            }
        }

        private static void SyncResource(
            EFT.InventoryLogic.Item item,
            bool locked,
            Func<float> get,
            Action<float> set,
            object component,
            params string[] maxMemberNames)
        {
            var store = LoadStore();
            string id = item.Id;
            if (string.IsNullOrEmpty(id)) return;

            if (locked)
            {
                float current = get();
                if (!store.ContainsKey(id) && current > 0f)
                {
                    store[id] = current;
                    SaveStore();
                }
                if (current != 0f) set(0f);
                return;
            }

            if (store.TryGetValue(id, out var original))
            {
                set(original);
                store.Remove(id);
                SaveStore();
                return;
            }

            // Zeroed by an older build that only remembered the original in
            // memory, so there's nothing to restore from. An emptied
            // med/food item is normally destroyed by the game, so one still
            // sitting at 0 was almost certainly zeroed by LevelGate: give it
            // back its full amount rather than leave it unusable forever.
            if (get() <= 0f)
            {
                foreach (var name in maxMemberNames)
                {
                    var max = ReflectionUtil.GetMember(component, name);
                    if (max is float f && f > 0f) { set(f); return; }
                    if (max is int n && n > 0) { set(n); return; }
                }
            }
        }

        private static Dictionary<string, float> LoadStore()
        {
            if (_originalResources != null) return _originalResources;
            _originalResources = new Dictionary<string, float>();
            try
            {
                if (File.Exists(ResourceFile))
                {
                    _originalResources = JsonConvert.DeserializeObject<Dictionary<string, float>>(File.ReadAllText(ResourceFile))
                                         ?? new Dictionary<string, float>();
                }
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to read neutralized_resources.json. " + e);
            }
            return _originalResources;
        }

        private static void SaveStore()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ResourceFile));
                File.WriteAllText(ResourceFile, JsonConvert.SerializeObject(_originalResources, Formatting.Indented));
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate: failed to save neutralized_resources.json. " + e);
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

    // Remembers which FirearmHandsInputTranslator is in the middle of
    // ReloadWithAmmo(Weapon) — the R-key path that, per the log's stack
    // traces, calls FirearmController.ReloadWithAmmo(AmmoPack, Callback) for
    // the Mosin and MP-153. The reload-entry check reads the translator's
    // freshly filled ammo buffer while the call is still in progress.
    [HarmonyPatch(typeof(EFT.FirearmHandsInputTranslator), "ReloadWithAmmo", new[] { typeof(EFT.InventoryLogic.Weapon) })]
    internal static class TranslatorReloadTracker
    {
        private static EFT.FirearmHandsInputTranslator _current;
        private static FieldInfo _ammoListField;

        static void Prefix(EFT.FirearmHandsInputTranslator __instance) => _current = __instance;

        static Exception Finalizer(Exception __exception)
        {
            _current = null;
            return __exception;
        }

        public static List<EFT.InventoryLogic.Ammo> CurrentAmmo()
        {
            var result = new List<EFT.InventoryLogic.Ammo>();
            try
            {
                if (_current == null) return result;
                if (_ammoListField == null)
                    _ammoListField = AccessTools.Field(typeof(EFT.FirearmHandsInputTranslator), "_preAllocatedAmmoList");
                if (_ammoListField?.GetValue(_current) is System.Collections.IEnumerable list)
                    result.AddRange(list.OfType<EFT.InventoryLogic.Ammo>());
            }
            catch (Exception e)
            {
                LevelGatePlugin.Log.LogError("LevelGate TranslatorReloadTracker error: " + e);
            }
            return result;
        }
    }

    // Empties the translator's reusable ammo buffer at the start of the
    // R-key handler (TranslateCommand -> Reload, per the log's stacks), so
    // anything in it by the time FirearmController.ReloadWithAmmo runs was
    // put there by THIS key press — never leftovers from an earlier call
    // (e.g. the M4A1's M855 blocking a Mosin reload with allowed ammo). Done
    // at the top-level handler rather than inside ReloadWithAmmo(Weapon) in
    // case Reload() itself fills the buffer before handing off.
    [HarmonyPatch(typeof(EFT.FirearmHandsInputTranslator), "Reload", new Type[0])]
    internal static class Patch_ClearTranslatorAmmoBuffer
    {
        private static FieldInfo _ammoListField;

        [HarmonyPriority(Priority.First)]
        static void Prefix(EFT.FirearmHandsInputTranslator __instance)
        {
            try
            {
                if (_ammoListField == null)
                    _ammoListField = AccessTools.Field(typeof(EFT.FirearmHandsInputTranslator), "_preAllocatedAmmoList");
                (_ammoListField?.GetValue(__instance) as System.Collections.IList)?.Clear();
            }
            catch
            {
                // fallback only — never break the reload
            }
        }
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
        static bool Prefix(EFT.InventoryLogic.Magazine __instance, object[] __args, ref object __result)
        {
            try
            {
                var ammo = __args.OfType<EFT.InventoryLogic.Ammo>().FirstOrDefault();
                if (ammo == null) return true;

                // Bots refill their magazines through this too — only judge
                // magazines you own.
                if (!LevelGateCheck.IsOwnItem(__instance)) return true;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return true;

                if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required, ammo);

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

                var packAmmo = AmmoCollector.CollectAll(args);

                // Fallback: when this call comes from the R key, the input
                // translator has just filled its _preAllocatedAmmoList with
                // the rounds it found for THIS reload (it's mid-call, so the
                // buffer is fresh here — unlike the stale read the old
                // LoadAmmoToChamber prefix did).
                var translatorAmmo = TranslatorReloadTracker.CurrentAmmo();

                var ammoList = packAmmo.Count > 0 ? packAmmo : translatorAmmo;

                DiagnosticLogging.LogCall(
                    $"ReloadEntry {original?.DeclaringType?.Name}.{original?.Name} " +
                    $"pack=[{string.Join(",", packAmmo.Select(a => a.TemplateId).Distinct())}] " +
                    $"translator=[{string.Join(",", translatorAmmo.Select(a => a.TemplateId).Distinct())}]");
                if (packAmmo.Count == 0)
                {
                    foreach (var arg in args)
                    {
                        if (arg != null && arg.GetType().Name.IndexOf("AmmoPack", StringComparison.Ordinal) >= 0)
                            DiagnosticLogging.LogCall("AmmoPack layout: " + AmmoCollector.Describe(arg));
                    }
                }

                foreach (var ammo in ammoList)
                {
                    if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                    {
                        LevelGateCheck.Notify(required, ammo, onScreen: true);
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
                if (!LevelGateCheck.IsOwnAction(__args)) return true;

                var player = LevelGateCheck.GetMainPlayer();
                if (player == null) return true;

                if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required, ammo, onScreen: true);

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
        private static bool AmmoLoadPrefix(object __instance, object[] __args, ref System.Threading.Tasks.Task<Comfort.Common.IResult> __result)
        {
            try
            {
                if (!LevelGateCheck.IsOwnInventoryController(__instance)) return true;

                var ammo = __args.OfType<EFT.InventoryLogic.Ammo>().FirstOrDefault();
                if (ammo == null)
                {
                    DiagnosticLogging.LogCall("AmmoLoadPrefix (no Ammo arg found)");
                    return true;
                }

                var player = LevelGateCheck.GetMainPlayer();
                DiagnosticLogging.LogCall($"AmmoLoadPrefix ammo={ammo.TemplateId} player={(player == null ? "NULL" : "found")}");
                // player may be null out of raid -> IsBlocked uses the PMC profile level

                if (LevelGateCheck.IsBlocked(player, ammo.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required, ammo, onScreen: true);
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
                if (!LevelGateCheck.IsOwnItem(item)) return true;

                var player = LevelGateCheck.GetMainPlayer();
                // player may be null out of raid -> IsBlocked uses the PMC profile level

                if (LevelGateCheck.IsBlocked(player, item.TemplateId, out int required))
                {
                    LevelGateCheck.Notify(required, item);
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
