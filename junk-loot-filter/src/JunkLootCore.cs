// Junk & Loot Filter - a Daggerfall Unity mod.
// Core: mod entry point, settings storage and item filtering rules.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;

namespace JunkLootFilter
{
    // Saved with each game: the UIDs of the items the player has locked.
    [FullSerializer.fsObject("v1")]
    public class JunkLootSaveData
    {
        public List<ulong> LockedItems;
    }

    public class JunkLootEntry : MonoBehaviour, IHasModSaveData
    {
        static Mod mod;

        Type IHasModSaveData.SaveDataType { get { return typeof(JunkLootSaveData); } }

        object IHasModSaveData.NewSaveData()
        {
            JunkLootSaveData data = new JunkLootSaveData();
            data.LockedItems = new List<ulong>();
            return data;
        }

        object IHasModSaveData.GetSaveData()
        {
            JunkLootSaveData data = new JunkLootSaveData();
            data.LockedItems = new List<ulong>(JunkStore.Locked);
            return data;
        }

        void IHasModSaveData.RestoreSaveData(object saveData)
        {
            JunkStore.Locked.Clear();
            JunkLootSaveData data = saveData as JunkLootSaveData;
            if (data != null && data.LockedItems != null)
            {
                for (int i = 0; i < data.LockedItems.Count; i++)
                    JunkStore.Locked.Add(data.LockedItems[i]);
            }
        }

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;
            GameObject go = new GameObject(mod.Title);
            go.AddComponent<JunkLootEntry>();
        }

        void Awake()
        {
            JunkStore.Load();
            gameObject.AddComponent<WindowScaler>();
            mod.SaveDataInterface = this;
            mod.IsReady = true;
        }

        void Start()
        {
            UIWindowFactory.RegisterCustomUIWindow(UIWindowType.Inventory, typeof(JunkInventoryWindow));
            UIWindowFactory.RegisterCustomUIWindow(UIWindowType.Trade, typeof(JunkTradeWindow));
            Debug.Log("[JunkLoot] Windows registered.");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Window scaling: DFU draws every window on a 320x200 canvas that is stretched to fit the screen.
    // Capping that stretch to a fraction of the fit makes the windows smaller and centred, and DFU
    // takes care of the layout and the mouse mapping.
    // ---------------------------------------------------------------------------------------

    public class WindowScaler : MonoBehaviour
    {
        // Windows built around full-screen art, maps, books and video stay full size.
        static readonly string[] Skip =
        {
            "DaggerfallStartWindow", "DaggerfallUnitySetupGameWizard", "DaggerfallStartNewGameWizard",
            "DaggerfallVidPlayerWindow", "DemoFLCWindow", "DemoClassQuestionsWindow", "DaggerfallLoadClassicGameWindow",
            "DaggerfallAutomapWindow", "DaggerfallExteriorAutomapWindow", "DaggerfallTravelMapWindow",
            "DaggerfallBookReaderWindow", "DaggerfallCourtWindow", "DaggerfallDaedraSummonedWindow"
        };

        // Small dialogs shrink only half as much, or their text becomes too small to read comfortably.
        static readonly string[] SmallDialogs =
        {
            "DaggerfallMessageBox", "DaggerfallInputMessageBox", "DaggerfallListPickerWindow", "DaggerfallPauseOptionsWindow",
            "DaggerfallTeleportPopUp", "DaggerfallTravelPopUp", "DaggerfallGuildServicePopupWindow",
            "DaggerfallMerchantServicePopupWindow", "DaggerfallMerchantRepairPopupWindow", "DaggerfallQuestPopupWindow",
            "DaggerfallWitchesCovenPopupWindow", "DaggerfallBankPurchasePopUp", "DaggerfallRestWindow", "ColorPicker",
            "FilterSettingsWindow"
        };

        static bool IsSmallDialog(object window)
        {
            // Match on the class and its base classes, so subclasses from other mods are covered too.
            for (System.Type t = window.GetType(); t != null; t = t.BaseType)
            {
                for (int i = 0; i < SmallDialogs.Length; i++)
                {
                    if (t.Name == SmallDialogs[i])
                        return true;
                }
            }
            return false;
        }

        static bool Skipped(object window)
        {
            string name = window.GetType().Name;
            if (name.StartsWith("CreateChar"))
                return true;
            for (int i = 0; i < Skip.Length; i++)
            {
                if (name == Skip[i])
                    return true;
            }
            return false;
        }

        // Before the UI updates and again just before it draws, so a window opened this frame is never
        // drawn full size for even one frame.
        void Update()
        {
            Apply();
        }

        void LateUpdate()
        {
            Apply();
        }

        static void Apply()
        {
            if (JunkStore.S == null || DaggerfallUI.Instance == null)
                return;
            IUserInterfaceManager manager = DaggerfallUI.UIManager;
            if (manager == null)
                return;

            float factor = Mathf.Clamp(JunkStore.S.windowScale, 0.4f, 1f);
            IUserInterfaceWindow window = manager.TopWindow;
            int guard = 0;
            while (window != null && guard++ < 8)
            {
                DaggerfallBaseWindow baseWindow = window as DaggerfallBaseWindow;
                if (baseWindow != null && !Skipped(baseWindow))
                {
                    Panel native = baseWindow.NativePanel;
                    float fit = Mathf.Min(Screen.width / native.Size.x, Screen.height / native.Size.y);
                    float f = IsSmallDialog(baseWindow) ? 1f - (1f - factor) * 0.5f : factor;
                    native.MaxAutoScale = (f >= 0.999f) ? 0f : fit * f;
                }

                // Pop-ups draw the window they sit on top of, so that one needs the same size.
                DaggerfallPopupWindow popup = window as DaggerfallPopupWindow;
                window = (popup != null) ? popup.PreviousWindow : null;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------------------------------

    // One set of filter toggles and lists. Not serialized itself: JsonUtility drops nested classes from
    // runtime-compiled mods, so JunkSettings stores the arrays and lists directly and these profiles
    // just point at them.
    public class FilterProfile
    {
        // 0 Weapons & Armor, 1 Magic items, 2 Clothing & Misc, 3 Ingredients
        public bool[] categories;
        // Indexes into ItemRules.MaterialNames
        public bool[] materials;
        public List<string> whitelist;
        public List<string> blacklist;

        public FilterProfile(bool[] categories, bool[] materials, List<string> whitelist, List<string> blacklist)
        {
            this.categories = categories;
            this.materials = materials;
            this.whitelist = whitelist;
            this.blacklist = blacklist;
        }

        public void SetAllCategories(bool value)
        {
            for (int i = 0; i < categories.Length; i++)
                categories[i] = value;
        }

        public void SetAllMaterials(bool value)
        {
            for (int i = 0; i < materials.Length; i++)
                materials[i] = value;
        }
    }

    [Serializable]
    public class JunkSettings
    {
        public bool[] lootCategories;
        public bool[] lootMaterials;
        public List<string> lootWhite;
        public List<string> lootBlack;

        public bool[] sellCategories;
        public bool[] sellMaterials;
        public List<string> sellWhite;
        public List<string> sellBlack;

        // Case-insensitive regular expressions matched against the item name.
        public List<string> junk;
        // Move junk-tagged items into the basket whenever a sell window opens.
        public bool preselectJunkOnSell = true;
        // KeyCode values, indexed by JunkStore.Key* below. 0 means unbound.
        public int[] keys;
        // Size of DFU's windows as a fraction of the full-screen size (1 = classic).
        public float windowScale;

        // Views onto the fields above, built by JunkStore.Load().
        [NonSerialized] public FilterProfile loot;
        [NonSerialized] public FilterProfile sell;
    }

    public static class JunkStore
    {
        public const int KeyJunk = 0;
        public const int KeyLock = 1;
        public const int KeyFilter = 2;
        public const int KeyLoot = 3;
        public const int KeySell = 4;
        public const int KeyCount = 5;

        public static readonly string[] KeyActionNames =
        {
            "Junk mode", "Lock mode", "Filter settings", "Quick loot", "Quick sell"
        };

        public static int[] DefaultKeys()
        {
            return new int[] { (int)KeyCode.K, (int)KeyCode.O, (int)KeyCode.F, (int)KeyCode.L, (int)KeyCode.Q };
        }

        public static JunkSettings S;

        // Items the player locked in this game; they are never sold.
        public static readonly HashSet<ulong> Locked = new HashSet<ulong>();

        public static HotkeySequence Hotkey(int action)
        {
            int code = (S != null && S.keys != null && action < S.keys.Length) ? S.keys[action] : 0;
            if (code == 0)
                return HotkeySequence.None;
            return new HotkeySequence((KeyCode)code, HotkeySequence.KeyModifiers.None);
        }

        public static string KeyName(int action)
        {
            int code = S.keys[action];
            if (code == 0)
                return "none";
            string name = ((KeyCode)code).ToString();
            if (name.StartsWith("Alpha"))
                name = name.Substring(5);
            return name;
        }

        public static bool IsLocked(DaggerfallUnityItem item)
        {
            return item != null && Locked.Contains(item.UID);
        }

        public static string ToggleLock(DaggerfallUnityItem item)
        {
            if (item == null || item.IsQuestItem)
                return "This item can't be locked.";
            if (Locked.Remove(item.UID))
                return "Unlocked: " + item.LongName;
            Locked.Add(item.UID);
            return "Locked (won't be sold): " + item.LongName;
        }

        static string FilePath
        {
            get { return Path.Combine(Application.persistentDataPath, "JunkLootFilter.json"); }
        }

        static List<Regex> junkRegex;

        public static void Load()
        {
            S = null;
            try
            {
                if (File.Exists(FilePath))
                    S = JsonUtility.FromJson<JunkSettings>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                Debug.LogError("[JunkLoot] Failed to read settings, using defaults: " + ex.Message);
            }

            if (S == null)
                S = new JunkSettings();

            // Fill anything missing or of the wrong size (first run, or a file from an older version).
            if (S.lootCategories == null || S.lootCategories.Length != 4)
                S.lootCategories = Filled(4, true);
            if (S.lootMaterials == null || S.lootMaterials.Length != 12)
                S.lootMaterials = Filled(12, true);
            if (S.sellCategories == null || S.sellCategories.Length != 4)
                S.sellCategories = Filled(4, false);
            if (S.sellMaterials == null || S.sellMaterials.Length != 12)
                S.sellMaterials = Filled(12, true);
            if (S.lootWhite == null)
                S.lootWhite = new List<string>();
            if (S.lootBlack == null)
                S.lootBlack = new List<string>();
            if (S.sellWhite == null)
                S.sellWhite = new List<string>();
            if (S.sellBlack == null)
                S.sellBlack = new List<string>();
            if (S.junk == null)
                S.junk = new List<string>();
            if (S.keys == null || S.keys.Length != KeyCount)
                S.keys = DefaultKeys();
            if (S.windowScale < 0.4f || S.windowScale > 1f)
                S.windowScale = 0.7f;

            S.loot = new FilterProfile(S.lootCategories, S.lootMaterials, S.lootWhite, S.lootBlack);
            S.sell = new FilterProfile(S.sellCategories, S.sellMaterials, S.sellWhite, S.sellBlack);
            junkRegex = null;
        }

        static bool[] Filled(int length, bool value)
        {
            bool[] a = new bool[length];
            for (int i = 0; i < length; i++)
                a[i] = value;
            return a;
        }

        public static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(S, true));
            }
            catch (Exception ex)
            {
                Debug.LogError("[JunkLoot] Failed to save settings: " + ex.Message);
            }
        }

        public static void InvalidateJunk()
        {
            junkRegex = null;
        }

        static List<Regex> JunkRegexes()
        {
            if (junkRegex != null)
                return junkRegex;

            junkRegex = new List<Regex>();
            for (int i = 0; i < S.junk.Count; i++)
            {
                try
                {
                    junkRegex.Add(new Regex(S.junk[i], RegexOptions.IgnoreCase));
                }
                catch (Exception)
                {
                    // Invalid pattern typed by the user: ignore it rather than breaking the inventory.
                    junkRegex.Add(null);
                }
            }
            return junkRegex;
        }

        public static bool IsJunk(DaggerfallUnityItem item)
        {
            if (item == null || S == null || S.junk.Count == 0 || !ItemRules.CanBeJunk(item) || IsLocked(item))
                return false;

            string name = item.LongName;
            List<Regex> list = JunkRegexes();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].IsMatch(name))
                    return true;
            }
            return false;
        }

        // Tags the item as junk, or removes every junk pattern that matches it.
        // Returns a short message for the HUD.
        public static string ToggleJunk(DaggerfallUnityItem item)
        {
            if (!ItemRules.CanBeJunk(item))
                return "This item can't be tagged as junk.";
            if (IsLocked(item))
                return "Locked items can't be junk: unlock it first.";

            string name = item.LongName;
            if (IsJunk(item))
            {
                List<Regex> list = JunkRegexes();
                for (int i = S.junk.Count - 1; i >= 0; i--)
                {
                    if (i < list.Count && list[i] != null && list[i].IsMatch(name))
                        S.junk.RemoveAt(i);
                }
                InvalidateJunk();
                Save();
                return "Junk tag removed: " + name;
            }

            S.junk.Add("^" + Regex.Escape(name) + "$");
            InvalidateJunk();
            Save();
            return "Tagged as junk: " + name;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Item rules
    // ---------------------------------------------------------------------------------------

    public static class ItemRules
    {
        public static readonly string[] CategoryNames =
        {
            "Weapons & Armor", "Magic items", "Clothing & Misc", "Ingredients"
        };

        public static readonly string[] MaterialNames =
        {
            "Leather", "Chain", "Iron", "Steel", "Silver", "Elven",
            "Dwarven", "Mithril", "Adamantium", "Ebony", "Orcish", "Daedric"
        };

        // Same split as the four inventory tabs.
        public static int Category(DaggerfallUnityItem item)
        {
            bool weaponOrArmor = (item.ItemGroup == ItemGroups.Weapons || item.ItemGroup == ItemGroups.Armor);
            if (item.IsEnchanted || item.IsOfTemplate((int)MiscItems.Spellbook))
                return 1;
            if (weaponOrArmor)
                return 0;
            if (item.IsIngredient)
                return 3;
            return 2;
        }

        // Index into MaterialNames, or -1 when the item has no material (anything but weapons and armor).
        public static int Material(DaggerfallUnityItem item)
        {
            int v = item.NativeMaterialValue;
            if (item.ItemGroup == ItemGroups.Weapons)
            {
                if (v < 0 || v > 9)
                    return -1;
                return v + 2;
            }
            if (item.ItemGroup == ItemGroups.Armor)
            {
                int high = v >> 8;
                if (high == 0)
                    return 0;
                if (high == 1)
                    return 1;
                int low = v & 0xFF;
                if (low > 9)
                    return -1;
                return low + 2;
            }
            return -1;
        }

        static bool MatchAny(List<string> terms, string name)
        {
            for (int i = 0; i < terms.Count; i++)
            {
                string t = terms[i];
                if (string.IsNullOrEmpty(t))
                    continue;
                t = t.Trim();
                if (t.Length > 0 && name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        public static bool IsGold(DaggerfallUnityItem item)
        {
            return item.IsOfTemplate(ItemGroups.Currency, (int)Currency.Gold_pieces);
        }

        // Items the mod will never sell, or tag as junk.
        public static bool CanBeJunk(DaggerfallUnityItem item)
        {
            if (item == null || item.IsQuestItem || item.IsSummoned)
                return false;
            if (item.IsEnchanted || item.IsOfTemplate((int)MiscItems.Spellbook))
                return false;
            if (item.ItemGroup == ItemGroups.Transportation || item.ItemGroup == ItemGroups.Currency)
                return false;
            if (item.IsOfTemplate(ItemGroups.MiscItems, (int)MiscItems.Map))
                return false;
            return true;
        }

        // Quick loot: a white list entry always loots, then the black list, then the toggles.
        public static bool PassesLoot(DaggerfallUnityItem item, FilterProfile p)
        {
            string name = item.LongName ?? string.Empty;
            if (MatchAny(p.whitelist, name))
                return true;
            if (MatchAny(p.blacklist, name))
                return false;
            return PassesToggles(item, p);
        }

        // Quick sell: black list always wins here (never sell), and magic items obey only the Magic toggle.
        public static bool PassesSell(DaggerfallUnityItem item, FilterProfile p)
        {
            if (item == null || item.IsQuestItem || item.IsSummoned)
                return false;
            if (item.ItemGroup == ItemGroups.Transportation || item.ItemGroup == ItemGroups.Currency)
                return false;
            if (item.IsOfTemplate(ItemGroups.MiscItems, (int)MiscItems.Map))
                return false;

            string name = item.LongName ?? string.Empty;
            if (MatchAny(p.blacklist, name))
                return false;
            if (Category(item) == 1)
                return p.categories[1];
            if (MatchAny(p.whitelist, name))
                return true;
            return PassesToggles(item, p);
        }

        static bool PassesToggles(DaggerfallUnityItem item, FilterProfile p)
        {
            if (!p.categories[Category(item)])
                return false;
            int m = Material(item);
            if (m >= 0 && !p.materials[m])
                return false;
            return true;
        }
    }
}
