// Junk & Loot Filter - a Daggerfall Unity mod.
// Modern item enchanting: the item maker window and the picker for powers and side effects.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace JunkLootFilter
{
    // ---------------------------------------------------------------------------------------
    // Item maker: pick an item from a table, give it powers and side effects, pay and enchant.
    // Enchanted items are never listed: DFU only lets you enchant an item once.
    // ---------------------------------------------------------------------------------------

    public class ModernItemMakerWindow : DaggerfallItemMakerWindow
    {
        const int MaxSlots = 10;
        static readonly int[] MinPointSteps = { 0, 50, 100, 150, 200, 300, 400, 600, 800 };

        Panel veil;
        FramedPanel backdrop;
        FramedPanel itemsFrame;
        FramedPanel powersFrame;
        FramedPanel sidesFrame;
        FramedPanel addFrame;
        FramedPanel infoFrame;
        FramedPanel statsFrame;
        FramedPanel buttonsFrame;
        Caption powersTitle;
        Caption sidesTitle;
        Caption infoName;
        Caption infoHint;
        RowTable itemTable;
        RowTable powersTable;
        RowTable sidesTable;
        ItemIconBox iconBox;
        KeyValueList stats;
        Meter meter;
        Button nameFilterBtn;
        Button materialFilterBtn;
        Button pointsFilterBtn;
        Button addPowersBtn;
        Button addSidesBtn;
        Button enchantBtn;
        Button exitBtn;

        PlayerEntity player;
        DaggerfallUnityItem selectedItem;
        string itemName = string.Empty;
        string nameQuery = string.Empty;
        int materialFilter = -1;
        int pointsStep;
        int sortColumn = -1;
        bool sortDesc;

        List<EnchantmentSettings> powers = new List<EnchantmentSettings>();
        List<EnchantmentSettings> sideEffects = new List<EnchantmentSettings>();
        Dictionary<string, IEntityEffect> powerTemplates = new Dictionary<string, IEntityEffect>();
        Dictionary<string, IEntityEffect> sideTemplates = new Dictionary<string, IEntityEffect>();
        List<EnchantmentSettings> powerEnchantments = new List<EnchantmentSettings>();
        List<EnchantmentSettings> sideEnchantments = new List<EnchantmentSettings>();

        float lastW;
        float lastH;
        bool lastShared;

        public ModernItemMakerWindow(IUserInterfaceManager uiManager, DaggerfallBaseWindow previous = null)
            : base(uiManager, previous)
        {
        }

        // ---- Setup ----

        protected override void Setup()
        {
            ParentPanel.BackgroundColor = ScreenDimColor;
            NativePanel.BackgroundColor = Color.clear;
            ParentPanel.Components.Remove(NativePanel);

            veil = Ui.Veil(ParentPanel, 0.55f);
            backdrop = Ui.Frame(ParentPanel);
            backdrop.BackgroundColor = Skin.BackdropFill;
            itemsFrame = Ui.Frame(ParentPanel);
            powersFrame = Ui.Frame(ParentPanel);
            sidesFrame = Ui.Frame(ParentPanel);
            addFrame = Ui.Frame(ParentPanel);
            infoFrame = Ui.Frame(ParentPanel);
            statsFrame = Ui.Frame(ParentPanel);
            buttonsFrame = Ui.Frame(ParentPanel);

            nameFilterBtn = Ui.MakeButton(itemsFrame, "Name: any", delegate () { AskNameQuery(); });
            materialFilterBtn = Ui.MakeButton(itemsFrame, "Material: any", delegate () { CycleMaterial(); });
            pointsFilterBtn = Ui.MakeButton(itemsFrame, "Min points: 0", delegate () { CyclePoints(); });

            itemTable = new RowTable();
            itemTable.Headers = new string[] { "Name", "Material", "Points" };
            itemTable.Weights = new float[] { 0.5f, 0.3f, 0.2f };
            itemTable.Right = new bool[] { false, false, true };
            itemTable.ShowIcons = true;
            itemTable.EmptyText = "No items you can enchant.";
            itemTable.OnLeft = delegate (TableRow row) { SelectItem(row.Item); };
            itemTable.OnHeader = SortBy;
            itemsFrame.Components.Add(itemTable);

            powersTitle = new Caption();
            powersTitle.Provider = delegate () { return "Powers"; };
            powersFrame.Components.Add(powersTitle);
            powersTable = new RowTable();
            powersTable.Weights = new float[] { 0.78f, 0.22f };
            powersTable.Right = new bool[] { false, true };
            powersTable.EmptyText = "No powers yet.";
            powersTable.OnLeft = delegate (TableRow row) { RemoveEnchantment(row, true); };
            powersFrame.Components.Add(powersTable);

            sidesTitle = new Caption();
            sidesTitle.Provider = delegate () { return "Side effects"; };
            sidesFrame.Components.Add(sidesTitle);
            sidesTable = new RowTable();
            sidesTable.Weights = new float[] { 0.78f, 0.22f };
            sidesTable.Right = new bool[] { false, true };
            sidesTable.EmptyText = "No side effects. They give points back.";
            sidesTable.OnLeft = delegate (TableRow row) { RemoveEnchantment(row, false); };
            sidesFrame.Components.Add(sidesTable);

            addPowersBtn = Ui.MakeButton(addFrame, "Add powers", delegate () { OpenPicker(true); });
            addSidesBtn = Ui.MakeButton(addFrame, "Add side effects", delegate () { OpenPicker(false); });

            iconBox = new ItemIconBox();
            iconBox.Item = delegate () { return selectedItem; };
            infoFrame.Components.Add(iconBox);
            infoName = new Caption();
            infoName.Provider = delegate () { return selectedItem == null ? "Pick an item" : (string.IsNullOrEmpty(itemName) ? selectedItem.shortName : itemName); };
            infoName.ColorProvider = delegate () { return selectedItem == null ? Skin.Muted : Skin.Gold; };
            infoName.OnMouseClick += delegate (BaseScreenComponent s, Vector2 p) { AskItemName(); };
            infoFrame.Components.Add(infoName);
            infoHint = new Caption();
            infoHint.Provider = delegate () { return selectedItem == null ? string.Empty : "Click the name to rename"; };
            infoHint.ColorProvider = delegate () { return Skin.Muted; };
            infoFrame.Components.Add(infoHint);

            stats = new KeyValueList();
            stats.Provider = StatRows;
            statsFrame.Components.Add(stats);
            meter = new Meter();
            meter.Used = delegate () { return UsedPoints(); };
            meter.Capacity = delegate () { return Capacity(); };
            statsFrame.Components.Add(meter);

            enchantBtn = Ui.MakeButton(buttonsFrame, "Enchant", delegate () { Enchant(); });
            exitBtn = Ui.MakeButton(buttonsFrame, "Exit", delegate () { CloseWindow(); });

            ParentPanel.Components.Add(NativePanel);
            lastW = -1f;
            EnumerateEnchantments2();
            RefreshAll();
        }

        public override void OnPush()
        {
            if (!IsSetup)
                return;
            selectedItem = null;
            itemName = string.Empty;
            powers.Clear();
            sideEffects.Clear();
            EnumerateEnchantments2();
            RefreshAll();
        }

        public override void OnPop()
        {
        }

        PlayerEntity Player
        {
            get { return (player != null) ? player : player = GameManager.Instance.PlayerEntity; }
        }

        // ---- Enchantment data (the same split the classic window makes) ----

        void EnumerateEnchantments2()
        {
            powerTemplates.Clear();
            sideTemplates.Clear();
            powerEnchantments.Clear();
            sideEnchantments.Clear();

            List<IEntityEffect> effectTemplates = GameManager.Instance.EntityEffectBroker.GetEnchantmentEffectTemplates();
            foreach (IEntityEffect effect in effectTemplates)
            {
                EnchantmentSettings[] enchantments = effect.GetEnchantmentSettings();
                if (enchantments == null)
                    continue;
                foreach (EnchantmentSettings enchantment in enchantments)
                {
                    if (enchantment.EnchantCost > 0)
                    {
                        if (!powerTemplates.ContainsKey(effect.Key))
                            powerTemplates.Add(effect.Key, effect);
                        powerEnchantments.Add(enchantment);
                    }
                    else
                    {
                        if (!sideTemplates.ContainsKey(effect.Key))
                            sideTemplates.Add(effect.Key, effect);
                        sideEnchantments.Add(enchantment);
                    }
                }
            }
        }

        static bool ContainsKey(List<EnchantmentSettings> list, string key)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].EffectKey == key)
                    return true;
            }
            return false;
        }

        // Everything that could be added right now, for the picker.
        public List<EnchantmentSettings> Candidates(bool forPowers)
        {
            List<EnchantmentSettings> result = new List<EnchantmentSettings>();
            if (selectedItem == null)
                return result;
            EnchantmentSettings[] currentPowers = powers.ToArray();
            EnchantmentSettings[] currentSides = sideEffects.ToArray();
            List<EnchantmentSettings> here = forPowers ? powers : sideEffects;
            Dictionary<string, IEntityEffect> templates = forPowers ? powerTemplates : sideTemplates;
            List<EnchantmentSettings> all = forPowers ? powerEnchantments : sideEnchantments;

            foreach (IEntityEffect effect in templates.Values)
            {
                if (!effect.HasItemMakerFlags(ItemMakerFlags.AllowMultiplePrimaryInstances) && ContainsKey(here, effect.Key))
                    continue;
                if (effect.HasItemMakerFlags(ItemMakerFlags.WeaponOnly) && selectedItem.ItemGroup != ItemGroups.Weapons)
                    continue;
                if (effect.IsEnchantmentExclusiveTo(currentPowers) || effect.IsEnchantmentExclusiveTo(currentSides))
                    continue;

                for (int i = 0; i < all.Count; i++)
                {
                    EnchantmentSettings e = all[i];
                    if (e.EffectKey != effect.Key)
                        continue;
                    if (!effect.HasItemMakerFlags(ItemMakerFlags.AllowMultipleSecondaryInstances) && here.Contains(e))
                        continue;
                    EnchantmentParam comparer = new EnchantmentParam() { ClassicParam = e.ClassicParam, CustomParam = e.CustomParam };
                    if (effect.IsEnchantmentExclusiveTo(currentPowers, comparer) || effect.IsEnchantmentExclusiveTo(currentSides, comparer))
                        continue;
                    result.Add(e);
                }
            }
            result.Sort(delegate (EnchantmentSettings a, EnchantmentSettings b) { return string.Compare(DisplayName(a), DisplayName(b), StringComparison.OrdinalIgnoreCase); });
            return result;
        }

        public static string DisplayName(EnchantmentSettings e)
        {
            string name = e.PrimaryDisplayName ?? string.Empty;
            if (!string.IsNullOrEmpty(e.SecondaryDisplayName))
                name += (name.EndsWith(":") ? " " : ": ") + e.SecondaryDisplayName;
            return name;
        }

        // The enchantments an enchantment brings along, sorted into powers and side effects.
        public void ForcedFor(EnchantmentSettings parent, out EnchantmentSettings[] forcedPowers, out EnchantmentSettings[] forcedSides)
        {
            List<EnchantmentSettings> fp = new List<EnchantmentSettings>();
            List<EnchantmentSettings> fs = new List<EnchantmentSettings>();
            IEntityEffect effect = GameManager.Instance.EntityEffectBroker.GetEffectTemplate(parent.EffectKey);
            EnchantmentParam param = new EnchantmentParam() { ClassicParam = parent.ClassicParam, CustomParam = parent.CustomParam };
            ForcedEnchantmentSet? set = (effect != null) ? effect.GetForcedEnchantments(param) : null;
            if (set != null)
            {
                foreach (ForcedEnchantment forced in set.Value.forcedEffects)
                {
                    IEntityEffect fe = GameManager.Instance.EntityEffectBroker.GetEffectTemplate(forced.key);
                    if (fe == null)
                        continue;
                    EnchantmentSettings? settings = fe.GetEnchantmentSettings(forced.param);
                    if (settings == null)
                        continue;
                    EnchantmentSettings f = settings.Value;
                    f.ParentEnchantment = parent.GetHashCode();
                    if (f.EnchantCost > 0)
                        fp.Add(f);
                    else
                        fs.Add(f);
                }
            }
            forcedPowers = fp.ToArray();
            forcedSides = fs.ToArray();
        }

        public int SlotsUsed
        {
            get { return powers.Count + sideEffects.Count; }
        }

        public int SlotsMax
        {
            get { return MaxSlots; }
        }

        public int UsedPoints()
        {
            int cost = 0;
            for (int i = 0; i < powers.Count; i++)
                if (powers[i].ParentEnchantment == 0)
                    cost += powers[i].EnchantCost;
            for (int i = 0; i < sideEffects.Count; i++)
                if (sideEffects[i].ParentEnchantment == 0)
                    cost += sideEffects[i].EnchantCost;
            return cost;
        }

        public int Capacity()
        {
            return (selectedItem != null) ? selectedItem.GetEnchantmentPower() : 0;
        }

        int GoldCost()
        {
            int cost = 0;
            for (int i = 0; i < powers.Count; i++)
                cost += powers[i].EnchantCost;
            return cost * 10;
        }

        // Adds one enchantment with whatever it forces along; false when the item has no room.
        public bool TryAdd(EnchantmentSettings e, bool asPower)
        {
            EnchantmentSettings[] fp;
            EnchantmentSettings[] fs;
            ForcedFor(e, out fp, out fs);
            if (SlotsUsed + fp.Length + fs.Length + 1 > MaxSlots)
            {
                DaggerfallUI.MessageBox("There is no room in the item for these enchantments.");
                return false;
            }
            (asPower ? powers : sideEffects).Add(e);
            powers.AddRange(fp);
            sideEffects.AddRange(fs);
            return true;
        }

        void RemoveEnchantment(TableRow row, bool fromPowers)
        {
            if (!(row.Tag is EnchantmentSettings))
                return;
            EnchantmentSettings e = (EnchantmentSettings)row.Tag;
            if (e.ParentEnchantment != 0)
            {
                DaggerfallUI.MessageBox("That enchantment comes with another one. Remove the other one first.");
                return;
            }
            int hash = e.GetHashCode();
            List<EnchantmentSettings> list = fromPowers ? powers : sideEffects;
            list.Remove(e);
            powers.RemoveAll(delegate (EnchantmentSettings x) { return x.ParentEnchantment == hash; });
            sideEffects.RemoveAll(delegate (EnchantmentSettings x) { return x.ParentEnchantment == hash; });
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            RefreshLists();
        }

        // ---- Items ----

        static bool CanEnchant(DaggerfallUnityItem item)
        {
            if (item.IsEnchanted || item.IsPotion)
                return false;
            switch (item.ItemGroup)
            {
                case ItemGroups.Weapons:
                    return !item.IsOfTemplate(ItemGroups.Weapons, (int)Weapons.Arrow);
                case ItemGroups.Armor:
                case ItemGroups.MensClothing:
                case ItemGroups.WomensClothing:
                case ItemGroups.Jewellery:
                    return true;
                case ItemGroups.Gems:
                    return item.IsIngredient;
            }
            return false;
        }

        string MaterialOf(DaggerfallUnityItem item)
        {
            int m = ItemRules.Material(item);
            return (m >= 0) ? ItemRules.MaterialNames[m] : "-";
        }

        void RefreshAll()
        {
            RefreshItems();
            RefreshLists();
        }

        void RefreshItems()
        {
            if (itemTable == null)
                return;
            List<DaggerfallUnityItem> items = new List<DaggerfallUnityItem>();
            ItemCollection all = Player.Items;
            int minPoints = MinPointSteps[pointsStep];
            for (int i = 0; i < all.Count; i++)
            {
                DaggerfallUnityItem item = all.GetItem(i);
                if (item == selectedItem || !CanEnchant(item))
                    continue;
                if (nameQuery.Length > 0 && (item.LongName ?? string.Empty).IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (materialFilter >= 0 && ItemRules.Material(item) != materialFilter)
                    continue;
                if (item.GetEnchantmentPower() < minPoints)
                    continue;
                items.Add(item);
            }
            if (sortColumn >= 0)
                items.Sort(delegate (DaggerfallUnityItem a, DaggerfallUnityItem b)
                {
                    int c = 0;
                    if (sortColumn == 0)
                        c = string.Compare(a.LongName, b.LongName, StringComparison.OrdinalIgnoreCase);
                    else if (sortColumn == 1)
                        c = string.Compare(MaterialOf(a), MaterialOf(b), StringComparison.OrdinalIgnoreCase);
                    else
                        c = a.GetEnchantmentPower().CompareTo(b.GetEnchantmentPower());
                    if (sortDesc)
                        c = -c;
                    return (c != 0) ? c : a.UID.CompareTo(b.UID);
                });
            List<TableRow> rows = new List<TableRow>();
            for (int i = 0; i < items.Count; i++)
            {
                TableRow row = new TableRow(items[i], Skin.White, Skin.ItemName(items[i]), MaterialOf(items[i]), items[i].GetEnchantmentPower().ToString());
                row.Item = items[i];
                rows.Add(row);
            }
            itemTable.SetRows(rows);
            itemTable.SortColumn = sortColumn;
            itemTable.SortDescending = sortDesc;

            nameFilterBtn.Label.Text = nameQuery.Length > 0 ? "Name: " + nameQuery : "Name: any";
            materialFilterBtn.Label.Text = "Material: " + (materialFilter >= 0 ? ItemRules.MaterialNames[materialFilter] : "any");
            pointsFilterBtn.Label.Text = "Min points: " + minPoints;
            lastW = -1f;
        }

        void RefreshLists()
        {
            if (powersTable == null)
                return;
            powersTable.SetRows(ToRows(powers, true));
            sidesTable.SetRows(ToRows(sideEffects, false));
        }

        List<TableRow> ToRows(List<EnchantmentSettings> list, bool power)
        {
            List<TableRow> rows = new List<TableRow>();
            for (int i = 0; i < list.Count; i++)
            {
                EnchantmentSettings e = list[i];
                string cost = (e.ParentEnchantment != 0) ? "free" : (power ? "+" + e.EnchantCost : e.EnchantCost.ToString());
                TableRow row = new TableRow(e, power ? new Color(1f, 0.55f, 0.48f, 1f) : new Color(0.44f, 0.83f, 0.44f, 1f), DisplayName(e), cost);
                row.CellColors = new Color[] { Skin.White, row.Color };
                row.Dim = false;
                rows.Add(row);
            }
            return rows;
        }

        void SelectItem(DaggerfallUnityItem item)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            selectedItem = item;
            itemName = (item != null) ? item.shortName : string.Empty;
            powers.Clear();
            sideEffects.Clear();
            RefreshAll();
        }

        void SortBy(int col)
        {
            if (sortColumn == col)
            {
                if (sortDesc)
                    sortColumn = -1;
                sortDesc = !sortDesc;
            }
            else
            {
                sortColumn = col;
                sortDesc = false;
            }
            RefreshItems();
        }

        void AskNameQuery()
        {
            DaggerfallInputMessageBox mb = new DaggerfallInputMessageBox(uiManager, this);
            mb.TextBox.Text = nameQuery;
            mb.SetTextBoxLabel("Show items whose name contains: ");
            mb.OnGotUserInput += delegate (DaggerfallInputMessageBox sender, string input)
            {
                nameQuery = (input ?? string.Empty).Trim();
                RefreshItems();
            };
            mb.Show();
        }

        void CycleMaterial()
        {
            materialFilter++;
            if (materialFilter >= ItemRules.MaterialNames.Length)
                materialFilter = -1;
            RefreshItems();
        }

        void CyclePoints()
        {
            pointsStep = (pointsStep + 1) % MinPointSteps.Length;
            RefreshItems();
        }

        void AskItemName()
        {
            if (selectedItem == null)
                return;
            DaggerfallInputMessageBox mb = new DaggerfallInputMessageBox(uiManager, this);
            mb.TextBox.Text = itemName;
            mb.SetTextBoxLabel("Enter a new name for the item: ");
            mb.OnGotUserInput += delegate (DaggerfallInputMessageBox sender, string input) { itemName = input; };
            mb.Show();
        }

        // ---- Picker and enchanting ----

        void OpenPicker(bool forPowers)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            if (selectedItem == null)
            {
                DaggerfallUI.MessageBox("An item must be selected to be enchanted.");
                return;
            }
            if (SlotsUsed >= MaxSlots)
            {
                DaggerfallUI.MessageBox(forPowers ? "You cannot enchant this item with any more powers." : "No further side-effects may be enchanted in this item.");
                return;
            }
            ModernEnchantPickerWindow picker = new ModernEnchantPickerWindow(uiManager, this, this, forPowers);
            picker.OnDone = delegate (List<EnchantmentSettings> picked)
            {
                for (int i = 0; i < picked.Count; i++)
                {
                    if (!TryAdd(picked[i], forPowers))
                        break;
                }
                RefreshLists();
            };
            uiManager.PushWindow(picker);
        }

        void Enchant()
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            if (selectedItem == null)
            {
                DaggerfallUI.MessageBox(1653);
                return;
            }
            if (powers.Count == 0 && sideEffects.Count == 0)
            {
                DaggerfallUI.MessageBox("You have not prepared enchantments for this item.");
                return;
            }
            int goldCost = GoldCost();
            if (Player.GetGoldAmount() < goldCost)
            {
                DaggerfallUI.MessageBox(1650);
                return;
            }
            if (Capacity() < UsedPoints())
            {
                DaggerfallUI.MessageBox(1651);
                return;
            }

            Player.DeductGoldAmount(goldCost);
            DaggerfallUI.MessageBox(1652);

            // Only one item of a stack is enchanted.
            if (selectedItem.IsAStack())
                selectedItem = Player.Items.SplitStack(selectedItem, 1);

            List<EnchantmentSettings> combined = new List<EnchantmentSettings>();
            combined.AddRange(powers);
            combined.AddRange(sideEffects);
            selectedItem.SetEnchantments(combined.ToArray(), Player);
            selectedItem.RenameItem(itemName);
            DaggerfallUI.Instance.PlayOneShot(SoundClips.MakeItem);

            selectedItem = null;
            itemName = string.Empty;
            powers.Clear();
            sideEffects.Clear();
            RefreshAll();
        }

        List<string[]> StatRows()
        {
            List<string[]> rows = new List<string[]>();
            if (selectedItem == null)
            {
                rows.Add(new string[] { "Points", "-" });
                rows.Add(new string[] { "Slots", "0 / " + MaxSlots });
                rows.Add(new string[] { "Gold cost", "-" });
            }
            else
            {
                int used = UsedPoints();
                int cap = Capacity();
                rows.Add(new string[] { "Points", used + " / " + cap, used > cap ? "warn" : string.Empty });
                rows.Add(new string[] { "Slots", SlotsUsed + " / " + MaxSlots, SlotsUsed >= MaxSlots ? "warn" : string.Empty });
                int gold = GoldCost();
                rows.Add(new string[] { "Gold cost", gold.ToString("N0"), Player.GetGoldAmount() < gold ? "warn" : string.Empty });
            }
            rows.Add(new string[] { "Gold held", Player.GetGoldAmount().ToString("N0"), "gold" });
            return rows;
        }

        // ---- Layout ----

        public override void Update()
        {
            base.Update();
            if (backdrop == null)
                return;
            Layout();
        }

        void Layout()
        {
            Metrics m = Metrics.Measure();
            bool shared = Ui.Shared;
            if (m.W == lastW && m.H == lastH && shared == lastShared)
                return;
            lastW = m.W;
            lastH = m.H;
            lastShared = shared;
            Ui.Place(veil, 0, 0, m.W, m.H);

            float inset = shared ? m.Pad : 0f;
            float w = Mathf.Round(Mathf.Min(m.W * 0.86f, m.H * 1.95f)) - 2f * inset;
            float h = Mathf.Round(m.H * 0.82f) - 2f * inset;
            float x0 = Mathf.Round((m.W - w) / 2f);
            float y0 = Mathf.Round((m.H - h) / 2f);
            float b = m.B;
            float pad = m.Pad;

            float leftW = Mathf.Round((w - 2f * pad) * 0.42f);
            float rightW = Mathf.Round((w - 2f * pad) * 0.24f);
            float midW = w - 2f * pad - leftW - rightW;
            float midX = x0 + leftW + pad;
            float rightX = midX + midW + pad;
            float btnBarH = Mathf.Round(m.RowH * 1.55f);

            // Left: item table with its filters.
            Ui.Place(itemsFrame, x0, y0, leftW, h);
            float fy = b + pad * 0.5f;
            float fh = Mathf.Round(m.RowH * 1.2f);
            float fw = Mathf.Floor((leftW - 2f * b - 2f * pad - 2f * pad * 0.5f) / 3f);
            Ui.SizeButton(nameFilterBtn, b + pad, fy, fw, fh, m.Ts * 0.85f);
            Ui.SizeButton(materialFilterBtn, b + pad + fw + pad * 0.5f, fy, fw, fh, m.Ts * 0.85f);
            Ui.SizeButton(pointsFilterBtn, b + pad + 2f * (fw + pad * 0.5f), fy, fw, fh, m.Ts * 0.85f);
            itemTable.RowH = Mathf.Round(m.RowH * 1.05f);
            itemTable.TextScale = m.Ts;
            Ui.Place(itemTable, b, fy + fh + pad * 0.5f, leftW - 2f * b, h - fy - fh - pad * 0.5f - b);

            // Middle: powers, side effects and the buttons that add them.
            float listH = Mathf.Floor((h - btnBarH - 2f * pad) / 2f);
            Ui.Place(powersFrame, midX, y0, midW, listH);
            Ui.Place(sidesFrame, midX, y0 + listH + pad, midW, listH);
            Ui.Place(addFrame, midX, y0 + 2f * (listH + pad), midW, btnBarH);
            float th = Mathf.Round(m.RowH * 1.2f);
            powersTitle.TextScale = m.Ts;
            Ui.Place(powersTitle, b + pad, b, midW, th);
            sidesTitle.TextScale = m.Ts;
            Ui.Place(sidesTitle, b + pad, b, midW, th);
            powersTable.RowH = sidesTable.RowH = Mathf.Round(m.RowH * 1.0f);
            powersTable.TextScale = sidesTable.TextScale = m.Ts;
            Ui.Place(powersTable, b, b + th, midW - 2f * b, listH - th - 2f * b);
            Ui.Place(sidesTable, b, b + th, midW - 2f * b, listH - th - 2f * b);
            float bh = btnBarH - 2f * b - 2f;
            float by = Mathf.Round((btnBarH - bh) / 2f);
            float aw = (midW - 3f * pad) / 2f;
            Ui.SizeButton(addPowersBtn, pad, by, Mathf.Round(aw), bh, m.Ts);
            Ui.SizeButton(addSidesBtn, pad * 2f + Mathf.Round(aw), by, Mathf.Round(aw), bh, m.Ts);

            // Right: the chosen item, the numbers and the buttons.
            float infoH = Mathf.Round(m.RowH * 3.4f);
            float statsH = Mathf.Round(m.RowH * 6.2f);
            Ui.Place(infoFrame, rightX, y0, rightW, infoH);
            Ui.Place(statsFrame, rightX, y0 + infoH + pad, rightW, statsH);
            Ui.Place(buttonsFrame, rightX, y0 + infoH + statsH + 2f * pad, rightW, btnBarH);
            float ic = infoH - 2f * b - pad;
            Ui.Place(iconBox, b + pad * 0.5f, b + pad * 0.5f, ic * 1.25f, ic);
            infoName.TextScale = m.Ts;
            Ui.Place(infoName, b + pad + ic * 1.25f + pad * 0.5f, b + pad * 0.5f, rightW - ic * 1.25f - 2f * b - 2f * pad, m.RowH * 1.3f);
            infoHint.TextScale = m.Ts * 0.8f;
            Ui.Place(infoHint, b + pad + ic * 1.25f + pad * 0.5f, b + pad * 0.5f + m.RowH * 1.4f, rightW - ic * 1.25f - 2f * b - 2f * pad, m.RowH);
            stats.RowH = Mathf.Round(m.RowH * 1.1f);
            stats.TextScale = m.Ts;
            Ui.Place(stats, b + pad, b + pad * 0.5f, rightW - 2f * (b + pad), stats.RowH * 4f);
            Ui.Place(meter, b + pad, b + pad * 0.5f + stats.RowH * 4f + pad * 0.5f, rightW - 2f * (b + pad), Mathf.Round(m.RowH * 0.45f));
            float rw = rightW - 2f * pad;
            Ui.SizeButton(enchantBtn, pad, by, Mathf.Round(rw * 0.55f - pad / 2f), bh, m.Ts);
            Ui.SizeButton(exitBtn, pad * 2f + Mathf.Round(rw * 0.55f - pad / 2f), by, Mathf.Round(rw * 0.45f - pad / 2f), bh, m.Ts);

            Ui.Style(backdrop, new FramedPanel[] { itemsFrame, powersFrame, sidesFrame, addFrame, infoFrame, statsFrame, buttonsFrame }, new Rect(x0, y0, w, h), m);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Add powers / add side effects: every effect that fits, with the points it adds or gives back.
    // ---------------------------------------------------------------------------------------

    public class ModernEnchantPickerWindow : DaggerfallPopupWindow
    {
        public Action<List<EnchantmentSettings>> OnDone;

        readonly ModernItemMakerWindow maker;
        readonly bool forPowers;
        List<EnchantmentSettings> candidates;
        List<EnchantmentSettings> picked = new List<EnchantmentSettings>();
        string query = string.Empty;

        Panel veil;
        FramedPanel backdrop;
        FramedPanel listFrame;
        FramedPanel sideFrame;
        FramedPanel previewFrame;
        FramedPanel buttonsFrame;
        Button searchBtn;
        RowTable table;
        TextBlock description;
        KeyValueList preview;
        Meter meter;
        Button addBtn;
        Button cancelBtn;
        float lastW;
        float lastH;
        bool lastShared;

        public ModernEnchantPickerWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous, ModernItemMakerWindow maker, bool forPowers)
            : base(uiManager, previous)
        {
            this.maker = maker;
            this.forPowers = forPowers;
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            ParentPanel.Components.Remove(NativePanel);

            veil = Ui.Veil(ParentPanel, 0.94f);
            backdrop = Ui.Frame(ParentPanel);
            backdrop.BackgroundColor = Skin.BackdropFill;
            listFrame = Ui.Frame(ParentPanel);
            sideFrame = Ui.Frame(ParentPanel);
            previewFrame = Ui.Frame(ParentPanel);
            buttonsFrame = Ui.Frame(ParentPanel);

            candidates = maker.Candidates(forPowers);

            searchBtn = Ui.MakeButton(listFrame, "Search: any", delegate () { AskQuery(); });
            table = new RowTable();
            table.Headers = new string[] { "Effect", "Points" };
            table.Weights = new float[] { 0.8f, 0.2f };
            table.Right = new bool[] { false, true };
            table.ShowChecks = true;
            table.EmptyText = "Nothing fits this item.";
            table.OnLeft = Toggle;
            listFrame.Components.Add(table);

            description = new TextBlock();
            description.Provider = DescriptionText;
            description.Color = Skin.White;
            sideFrame.Components.Add(description);

            preview = new KeyValueList();
            preview.Provider = PreviewRows;
            previewFrame.Components.Add(preview);
            meter = new Meter();
            meter.Used = delegate () { return maker.UsedPoints(); };
            meter.Added = delegate () { return PickedPoints(); };
            meter.Capacity = delegate () { return maker.Capacity(); };
            previewFrame.Components.Add(meter);

            addBtn = Ui.MakeButton(buttonsFrame, "Add", delegate () { Commit(); });
            cancelBtn = Ui.MakeButton(buttonsFrame, "Cancel", delegate () { CloseWindow(); });

            ParentPanel.Components.Add(NativePanel);
            lastW = -1f;
            RefreshRows();
        }

        void RefreshRows()
        {
            List<TableRow> rows = new List<TableRow>();
            for (int i = 0; i < candidates.Count; i++)
            {
                EnchantmentSettings e = candidates[i];
                string name = ModernItemMakerWindow.DisplayName(e);
                if (query.Length > 0 && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                string cost = forPowers ? "+" + e.EnchantCost : e.EnchantCost.ToString();
                TableRow row = new TableRow(e, Skin.White, name, cost);
                row.CellColors = new Color[] { Skin.White, forPowers ? new Color(1f, 0.55f, 0.48f, 1f) : new Color(0.44f, 0.83f, 0.44f, 1f) };
                row.Checked = picked.Contains(e);
                rows.Add(row);
            }
            table.SetRows(rows);
            if (searchBtn != null)
                searchBtn.Label.Text = query.Length > 0 ? "Search: " + query : "Search: any";
        }

        void Toggle(TableRow row)
        {
            if (!(row.Tag is EnchantmentSettings))
                return;
            EnchantmentSettings e = (EnchantmentSettings)row.Tag;
            if (picked.Contains(e))
                picked.Remove(e);
            else
                picked.Add(e);
            row.Checked = picked.Contains(e);
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
        }

        void Commit()
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            List<EnchantmentSettings> chosen = new List<EnchantmentSettings>(picked);
            CloseWindow();
            if (chosen.Count > 0 && OnDone != null)
                OnDone(chosen);
        }

        void AskQuery()
        {
            DaggerfallInputMessageBox mb = new DaggerfallInputMessageBox(uiManager, this);
            mb.TextBox.Text = query;
            mb.SetTextBoxLabel("Show effects whose name contains: ");
            mb.OnGotUserInput += delegate (DaggerfallInputMessageBox sender, string input)
            {
                query = (input ?? string.Empty).Trim();
                RefreshRows();
            };
            mb.Show();
        }

        int PickedPoints()
        {
            int sum = 0;
            for (int i = 0; i < picked.Count; i++)
                sum += picked[i].EnchantCost;
            return sum;
        }

        int PickedSlots()
        {
            int slots = 0;
            for (int i = 0; i < picked.Count; i++)
            {
                EnchantmentSettings[] fp;
                EnchantmentSettings[] fs;
                maker.ForcedFor(picked[i], out fp, out fs);
                slots += 1 + fp.Length + fs.Length;
            }
            return slots;
        }

        string DescriptionText()
        {
            TableRow row = table.Hovered;
            if (row == null || !(row.Tag is EnchantmentSettings))
                return forPowers ? "Powers use up the item's enchantment points and cost gold.\nPoint at an effect to see what comes with it." : "Side effects give enchantment points back.\nPoint at an effect to see what comes with it.";
            EnchantmentSettings e = (EnchantmentSettings)row.Tag;
            string text = ModernItemMakerWindow.DisplayName(e) + "\n" + (forPowers ? "Uses " : "Gives back ") + Mathf.Abs(e.EnchantCost) + " points.";
            EnchantmentSettings[] fp;
            EnchantmentSettings[] fs;
            maker.ForcedFor(e, out fp, out fs);
            for (int i = 0; i < fp.Length; i++)
                text += "\nAlso adds: " + ModernItemMakerWindow.DisplayName(fp[i]);
            for (int i = 0; i < fs.Length; i++)
                text += "\nAlso adds: " + ModernItemMakerWindow.DisplayName(fs[i]);
            return text;
        }

        List<string[]> PreviewRows()
        {
            List<string[]> rows = new List<string[]>();
            int after = maker.UsedPoints() + PickedPoints();
            int cap = maker.Capacity();
            int slotsAfter = maker.SlotsUsed + PickedSlots();
            rows.Add(new string[] { "Points", after + " / " + cap, after > cap ? "warn" : string.Empty });
            rows.Add(new string[] { "Slots", slotsAfter + " / " + maker.SlotsMax, slotsAfter > maker.SlotsMax ? "warn" : string.Empty });
            return rows;
        }

        public override void Update()
        {
            base.Update();
            if (backdrop != null)
                Layout();
        }

        void Layout()
        {
            Metrics m = Metrics.Measure();
            bool shared = Ui.Shared;
            if (m.W == lastW && m.H == lastH && shared == lastShared)
                return;
            lastW = m.W;
            lastH = m.H;
            lastShared = shared;
            Ui.Place(veil, 0, 0, m.W, m.H);

            float inset = shared ? m.Pad : 0f;
            float w = Mathf.Round(Mathf.Min(m.W * 0.74f, m.H * 1.6f)) - 2f * inset;
            float h = Mathf.Round(m.H * 0.78f) - 2f * inset;
            float x0 = Mathf.Round((m.W - w) / 2f);
            float y0 = Mathf.Round((m.H - h) / 2f);
            float b = m.B;
            float pad = m.Pad;

            float sideW = Mathf.Round(Mathf.Max(m.RowH * 8f, (w - pad) * 0.30f));
            float listW = w - pad - sideW;
            float btnH = Mathf.Round(m.RowH * 1.55f);
            float prevH = Mathf.Round(m.RowH * 4.2f);
            float descH = h - prevH - btnH - 2f * pad;

            Ui.Place(listFrame, x0, y0, listW, h);
            Ui.Place(sideFrame, x0 + listW + pad, y0, sideW, descH);
            Ui.Place(previewFrame, x0 + listW + pad, y0 + descH + pad, sideW, prevH);
            Ui.Place(buttonsFrame, x0 + listW + pad, y0 + descH + prevH + 2f * pad, sideW, btnH);

            float fh = Mathf.Round(m.RowH * 1.2f);
            Ui.SizeButton(searchBtn, b + pad, b + pad * 0.5f, Mathf.Round(Mathf.Min(listW * 0.5f, m.RowH * 10f)), fh, m.Ts * 0.85f);
            table.RowH = Mathf.Round(m.RowH * 1.0f);
            table.TextScale = m.Ts;
            Ui.Place(table, b, b + pad * 0.5f + fh + pad * 0.5f, listW - 2f * b, h - 2f * b - fh - pad);
            description.TextScale = m.Ts * 0.9f;
            Ui.Place(description, b + pad, b + pad * 0.5f, sideW - 2f * (b + pad), descH - 2f * b);
            preview.RowH = Mathf.Round(m.RowH * 1.1f);
            preview.TextScale = m.Ts;
            Ui.Place(preview, b + pad, b + pad * 0.5f, sideW - 2f * (b + pad), preview.RowH * 2f);
            Ui.Place(meter, b + pad, b + pad * 0.5f + preview.RowH * 2f + pad * 0.5f, sideW - 2f * (b + pad), Mathf.Round(m.RowH * 0.45f));

            float bh = btnH - 2f * b - 2f;
            float by = Mathf.Round((btnH - bh) / 2f);
            float bw = Mathf.Round((sideW - 3f * pad) / 2f);
            Ui.SizeButton(addBtn, pad, by, bw, bh, m.Ts);
            Ui.SizeButton(cancelBtn, 2f * pad + bw, by, bw, bh, m.Ts);

            Ui.Style(backdrop, new FramedPanel[] { listFrame, sideFrame, previewFrame, buttonsFrame }, new Rect(x0, y0, w, h), m);
        }
    }
}
