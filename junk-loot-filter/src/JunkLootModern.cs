// Junk & Loot Filter - a Daggerfall Unity mod.
// The modern inventory: item table, category tabs, right-click menu and a side panel for cart, loot and ground.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace JunkLootFilter
{
    // ---------------------------------------------------------------------------------------
    // Colours and immediate-mode drawing helpers. Everything here works in screen pixels.
    // ---------------------------------------------------------------------------------------

    public static class Skin
    {
        public static readonly Color PanelFill = new Color(0.06f, 0.055f, 0.05f, 0.82f);
        public static readonly Color SharedFill = new Color(0.07f, 0.065f, 0.06f, 0.45f);
        public static readonly Color BackdropFill = new Color(0.04f, 0.035f, 0.03f, 0.72f);
        public static readonly Color Zebra = new Color(1f, 1f, 1f, 0.045f);
        public static readonly Color Hover = new Color(0.95f, 0.8f, 0.25f, 0.18f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.10f);
        public static readonly Color White = new Color(0.94f, 0.93f, 0.9f, 1f);
        public static readonly Color Magic = new Color(0.45f, 0.72f, 1f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.6f, 0.55f, 1f);
        public static readonly Color Quest = new Color(0.55f, 1f, 0.55f, 1f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.95f);
        public static readonly Color ButtonFill = new Color(0.09f, 0.08f, 0.065f, 0.92f);
        public static readonly Color ButtonHover = new Color(0.25f, 0.2f, 0.07f, 0.95f);
        public static readonly Color ButtonLine = new Color(0.72f, 0.58f, 0.18f, 1f);

        public static Color Gold
        {
            get { return DaggerfallUI.DaggerfallDefaultTextColor; }
        }

        public static float GlyphH
        {
            get { return DaggerfallUI.DefaultFont.GlyphHeight; }
        }

        public static void Fill(Rect r, Color c)
        {
            DaggerfallUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, c);
        }

        public static float Width(string s, float scale)
        {
            if (string.IsNullOrEmpty(s))
                return 0f;
            // CalculateTextWidth returns unscaled glyph units; the glyphs are drawn scale times as wide.
            return DaggerfallUI.DefaultFont.CalculateTextWidth(s, new Vector2(scale, scale)) * scale;
        }

        public static float Height(float scale)
        {
            return GlyphH * scale;
        }

        // Text with a black shadow, top left at x, y.
        public static void Text(string s, float x, float y, float scale, Color c)
        {
            if (string.IsNullOrEmpty(s))
                return;
            float o = Mathf.Max(1f, Mathf.Round(scale * 0.5f));
            DaggerfallUI.DefaultFont.DrawText(s, new Vector2(Mathf.Round(x), Mathf.Round(y)), new Vector2(scale, scale), c, Shadow, new Vector2(o, o));
        }

        // Vertically centred in a row of the given height.
        public static void TextInRow(string s, float x, float rowTop, float rowH, float scale, Color c)
        {
            Text(s, x, rowTop + (rowH - Height(scale)) / 2f, scale, c);
        }

        public static void TextRight(string s, float right, float rowTop, float rowH, float scale, Color c)
        {
            TextInRow(s, right - Width(s, scale), rowTop, rowH, scale, c);
        }

        public static string Fit(string s, float maxW, float scale)
        {
            if (string.IsNullOrEmpty(s) || maxW <= 0f)
                return string.Empty;
            if (Width(s, scale) <= maxW)
                return s;
            while (s.Length > 1 && Width(s + "..", scale) > maxW)
                s = s.Substring(0, s.Length - 1);
            return s + "..";
        }

        public static Vector2 MouseScreen()
        {
            return new Vector2(InputManager.Instance.MousePosition.x, Screen.height - InputManager.Instance.MousePosition.y);
        }

        public static Vector2 MouseLocal(BaseScreenComponent c)
        {
            Rect r = c.Rectangle;
            Vector2 m = MouseScreen();
            return new Vector2(m.x - r.xMin, m.y - r.yMin);
        }

        // Template name for gear, a plain word for everything else.
        public static string TypeName(DaggerfallUnityItem item)
        {
            switch (item.ItemGroup)
            {
                case ItemGroups.Weapons:
                case ItemGroups.Armor:
                case ItemGroups.MensClothing:
                case ItemGroups.WomensClothing:
                case ItemGroups.Jewellery:
                    return item.ItemTemplate.name ?? string.Empty;
                case ItemGroups.Drugs: return "Potion";
                case ItemGroups.Books: return "Book";
                case ItemGroups.Gems: return "Gem";
                case ItemGroups.ReligiousItems: return "Religious";
                case ItemGroups.Maps: return "Map";
                case ItemGroups.Paintings: return "Painting";
                case ItemGroups.Furniture: return "Furniture";
                case ItemGroups.Artifacts: return "Artifact";
                case ItemGroups.MagicItems: return "Magic";
                case ItemGroups.Deeds: return "Deed";
                case ItemGroups.Transportation: return "Transport";
                case ItemGroups.QuestItems: return "Quest";
                case ItemGroups.Currency: return "Gold";
                case ItemGroups.MiscItems: return "Misc";
                case ItemGroups.UselessItems1:
                case ItemGroups.UselessItems2: return "Trinket";
                case ItemGroups.PlantIngredients1:
                case ItemGroups.PlantIngredients2: return "Plant";
                case ItemGroups.CreatureIngredients1:
                case ItemGroups.CreatureIngredients2:
                case ItemGroups.CreatureIngredients3: return "Creature";
                case ItemGroups.MetalIngredients: return "Metal";
                case ItemGroups.MiscellaneousIngredients1:
                case ItemGroups.MiscellaneousIngredients2: return "Mineral";
            }
            return item.IsIngredient ? "Ingredient" : "Misc";
        }

        public static float StackWeight(DaggerfallUnityItem item)
        {
            return item.EffectiveUnitWeightInKg() * item.stackCount;
        }

        public static float StackValue(DaggerfallUnityItem item)
        {
            return (float)item.value * item.stackCount;
        }

        public static string ItemName(DaggerfallUnityItem item)
        {
            string name = item.LongName ?? string.Empty;
            if (item.stackCount > 1)
                name += " x" + item.stackCount;
            return name;
        }

        public static Color NameColor(DaggerfallUnityItem item)
        {
            if (item.IsQuestItem)
                return Quest;
            if (JunkStore.IsJunk(item))
                return Muted;
            if (item.IsEnchanted)
                return Magic;
            return White;
        }

        public static void DrawItemIcon(Rect cell, DaggerfallUnityItem item)
        {
            ImageData img = DaggerfallUnity.Instance.ItemHelper.GetInventoryImage(item);
            if (img.texture != null)
                DaggerfallUI.DrawTexture(cell, img.texture, ScaleMode.ScaleToFit, true);
            Texture2D[] overlay = JunkUi.OverlayFor(item);
            if (overlay != null && overlay[0] != null)
                DaggerfallUI.DrawTexture(cell, overlay[0], ScaleMode.StretchToFill, true);
        }
    }

    // ---------------------------------------------------------------------------------------
    // A scrolling table of items: icon, type, name, weight, price and gold per kg. Click the header to sort.
    // ---------------------------------------------------------------------------------------

    public class ItemTable : BaseScreenComponent
    {
        public bool Full = true;
        public float RowH = 24f;
        public float TextScale = 1.5f;
        public string EmptyText = "Nothing here.";

        public Action<DaggerfallUnityItem> OnLeft;
        public Action<DaggerfallUnityItem> OnMiddle;
        public Action<DaggerfallUnityItem, Vector2> OnRight;
        public Action<DaggerfallUnityItem> OnHover;

        readonly List<DaggerfallUnityItem> source = new List<DaggerfallUnityItem>();
        readonly List<DaggerfallUnityItem> view = new List<DaggerfallUnityItem>();
        int scroll;
        int hover = -1;
        int sortCol = -1;
        bool sortDesc;
        DaggerfallUnityItem lastHover;

        // Column geometry in screen pixels, set by ComputeColumns().
        float iconX, iconW, typeX, nameX, nameW, wtRight, priceRight, gpwRight, numW, barW, pad;
        float[] edges = new float[6];

        public DaggerfallUnityItem Hovered
        {
            get { return (hover >= 0 && hover < view.Count) ? view[hover] : null; }
        }

        float HeaderH
        {
            get { return Mathf.Round(RowH * 0.95f); }
        }

        int VisibleRows
        {
            get { return Mathf.Max(1, Mathf.FloorToInt((Size.y - HeaderH) / RowH)); }
        }

        public ItemTable()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            OnMouseClick += delegate (BaseScreenComponent s, Vector2 p) { Click(0); };
            OnRightMouseClick += delegate (BaseScreenComponent s, Vector2 p) { Click(1); };
            OnMiddleMouseClick += delegate (BaseScreenComponent s, Vector2 p) { Click(2); };
            OnMouseScrollUp += delegate (BaseScreenComponent s) { ScrollBy(-2); };
            OnMouseScrollDown += delegate (BaseScreenComponent s) { ScrollBy(2); };
        }

        public void SetItems(List<DaggerfallUnityItem> items)
        {
            source.Clear();
            if (items != null)
                source.AddRange(items);
            Rebuild();
        }

        public void ResetScroll()
        {
            scroll = 0;
        }

        void Rebuild()
        {
            view.Clear();
            view.AddRange(source);
            if (sortCol >= 0)
                view.Sort(Compare);
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, view.Count - VisibleRows));
        }

        int Compare(DaggerfallUnityItem a, DaggerfallUnityItem b)
        {
            int c = 0;
            switch (sortCol)
            {
                case 1: c = string.Compare(Skin.TypeName(a), Skin.TypeName(b), StringComparison.OrdinalIgnoreCase); break;
                case 2: c = string.Compare(a.LongName, b.LongName, StringComparison.OrdinalIgnoreCase); break;
                case 3: c = Skin.StackWeight(a).CompareTo(Skin.StackWeight(b)); break;
                case 4: c = Skin.StackValue(a).CompareTo(Skin.StackValue(b)); break;
                case 5: c = Gpw(a).CompareTo(Gpw(b)); break;
            }
            if (sortDesc)
                c = -c;
            if (c == 0)
                c = a.UID.CompareTo(b.UID);
            return c;
        }

        static float Gpw(DaggerfallUnityItem item)
        {
            float w = Skin.StackWeight(item);
            return (w > 0.0001f) ? Skin.StackValue(item) / w : 0f;
        }

        void ScrollBy(int rows)
        {
            if (PopupMenu.Blocking)
                return;
            scroll = Mathf.Clamp(scroll + rows, 0, Mathf.Max(0, view.Count - VisibleRows));
        }

        void ComputeColumns(Rect r)
        {
            pad = Mathf.Round(RowH * 0.22f);
            barW = Mathf.Max(4f, Mathf.Round(RowH * 0.2f));
            float w = r.width - barW;
            iconW = Mathf.Round(RowH * 1.25f);
            iconX = r.x + pad;
            if (Full)
            {
                float typeW = Mathf.Round(w * 0.15f);
                numW = Mathf.Round(w * 0.085f);
                float priceW = Mathf.Round(w * 0.105f);
                float gpwW = Mathf.Round(w * 0.105f);
                typeX = iconX + iconW + pad;
                nameX = typeX + typeW + pad;
                gpwRight = r.x + w - pad;
                priceRight = gpwRight - gpwW;
                wtRight = priceRight - priceW;
                nameW = wtRight - numW - nameX - pad;
                edges[0] = typeX - pad / 2f;
                edges[1] = nameX - pad / 2f;
                edges[2] = wtRight - numW - pad / 2f;
                edges[3] = priceRight - priceW - pad / 2f;
                edges[4] = gpwRight - gpwW - pad / 2f;
                edges[5] = r.x + w;
            }
            else
            {
                numW = Mathf.Round(w * 0.2f);
                nameX = iconX + iconW + pad;
                wtRight = r.x + w - pad;
                nameW = wtRight - numW - nameX - pad;
                typeX = nameX;
                priceRight = gpwRight = wtRight;
                edges[0] = edges[1] = nameX - pad / 2f;
                edges[2] = wtRight - numW - pad / 2f;
                edges[3] = edges[4] = edges[5] = r.x + w;
            }
        }

        // Which sortable column the x position falls in: 1 type, 2 name, 3 weight, 4 price, 5 gold per kg; 0 for none.
        int ColumnAt(float x)
        {
            if (Full)
            {
                if (x >= edges[4] && x < edges[5]) return 5;
                if (x >= edges[3] && x < edges[4]) return 4;
                if (x >= edges[2] && x < edges[3]) return 3;
                if (x >= edges[1] && x < edges[2]) return 2;
                if (x >= edges[0] && x < edges[1]) return 1;
                return 0;
            }
            if (x >= edges[2]) return 3;
            if (x >= edges[1]) return 2;
            return 0;
        }

        void Click(int button)
        {
            if (PopupMenu.Blocking)
                return;
            Rect r = Rectangle;
            ComputeColumns(r);
            Vector2 m = Skin.MouseLocal(this);
            if (m.y < HeaderH)
            {
                if (button == 0)
                {
                    int col = ColumnAt(m.x + r.x);
                    if (col > 0)
                    {
                        if (sortCol == col)
                        {
                            if (sortDesc)
                                sortCol = -1;
                            sortDesc = !sortDesc;
                        }
                        else
                        {
                            sortCol = col;
                            sortDesc = false;
                        }
                        Rebuild();
                    }
                }
                return;
            }
            DaggerfallUnityItem item = Hovered;
            if (item == null)
                return;
            if (button == 0 && OnLeft != null)
                OnLeft(item);
            else if (button == 1 && OnRight != null)
                OnRight(item, Skin.MouseScreen());
            else if (button == 2 && OnMiddle != null)
                OnMiddle(item);
        }

        public override void Update()
        {
            base.Update();
            hover = -1;
            if (MouseOverComponent && !PopupMenu.Blocking)
            {
                Vector2 m = Skin.MouseLocal(this);
                if (m.y >= HeaderH)
                {
                    int idx = scroll + (int)((m.y - HeaderH) / RowH);
                    if (idx >= 0 && idx < view.Count && idx < scroll + VisibleRows)
                        hover = idx;
                }
            }
            DaggerfallUnityItem h = Hovered;
            if (h != lastHover)
            {
                lastHover = h;
                if (OnHover != null)
                    OnHover(h);
            }
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled)
                return;

            Rect r = Rectangle;
            ComputeColumns(r);
            float s = TextScale;
            Color head = new Color(0.72f, 0.66f, 0.45f, 1f);

            // Header
            float hh = HeaderH;
            if (Full)
            {
                Skin.TextInRow(HeaderLabel("Type", 1), typeX, r.y, hh, s, head);
                Skin.TextInRow(HeaderLabel("Name", 2), nameX, r.y, hh, s, head);
                Skin.TextRight(HeaderLabel("Wt", 3), wtRight, r.y, hh, s, head);
                Skin.TextRight(HeaderLabel("Price", 4), priceRight, r.y, hh, s, head);
                Skin.TextRight(HeaderLabel("G/kg", 5), gpwRight, r.y, hh, s, head);
            }
            else
            {
                Skin.TextInRow(HeaderLabel("Name", 2), nameX, r.y, hh, s, head);
                Skin.TextRight(HeaderLabel("Wt", 3), wtRight, r.y, hh, s, head);
            }
            Skin.Fill(new Rect(r.x + pad, r.y + hh - 1f, r.width - 2f * pad - barW, 1f), Skin.Line);

            if (view.Count == 0)
            {
                Skin.TextInRow(EmptyText, nameX, r.y + hh, RowH * 1.2f, s, Skin.Muted);
                return;
            }

            int visible = VisibleRows;
            for (int i = 0; i < visible; i++)
            {
                int idx = scroll + i;
                if (idx >= view.Count)
                    break;
                DaggerfallUnityItem it = view[idx];
                float y = r.y + hh + i * RowH;
                Rect row = new Rect(r.x + 1f, y, r.width - 2f - barW, RowH);
                if (idx == hover)
                    Skin.Fill(row, Skin.Hover);
                else if ((idx & 1) == 1)
                    Skin.Fill(row, Skin.Zebra);

                Skin.DrawItemIcon(new Rect(iconX, y + 1f, iconW, RowH - 2f), it);

                Color nc = Skin.NameColor(it);
                Color vc = (nc == Skin.White) ? new Color(0.85f, 0.84f, 0.8f, 1f) : nc;
                if (Full)
                    Skin.TextInRow(Skin.Fit(Skin.TypeName(it), nameX - typeX - pad * 1.5f, s), typeX, y, RowH, s, vc);
                Skin.TextInRow(Skin.Fit(Skin.ItemName(it), nameW, s), nameX, y, RowH, s, nc);
                float wt = Skin.StackWeight(it);
                Skin.TextRight(wt.ToString("0.0"), wtRight, y, RowH, s, vc);
                if (Full)
                {
                    Skin.TextRight(((int)Skin.StackValue(it)).ToString(), priceRight, y, RowH, s, vc);
                    Skin.TextRight(((int)Gpw(it)).ToString(), gpwRight, y, RowH, s, vc);
                }
            }

            // Scroll bar
            if (view.Count > visible)
            {
                float trackH = visible * RowH;
                float trackY = r.y + hh;
                float thumbH = Mathf.Max(RowH, trackH * visible / view.Count);
                float thumbY = trackY + (trackH - thumbH) * scroll / Mathf.Max(1, view.Count - visible);
                Skin.Fill(new Rect(r.xMax - barW - 1f, trackY, barW, trackH), new Color(1f, 1f, 1f, 0.06f));
                Skin.Fill(new Rect(r.xMax - barW - 1f, thumbY, barW, thumbH), new Color(0.72f, 0.58f, 0.18f, 0.9f));
            }
        }

        string HeaderLabel(string text, int col)
        {
            if (sortCol != col)
                return text;
            return text + (sortDesc ? " v" : " ^");
        }
    }

    // ---------------------------------------------------------------------------------------
    // Equipped slots: a strip of icon cells (trinkets) or a list of rows (gear).
    // ---------------------------------------------------------------------------------------

    public class SlotView : BaseScreenComponent
    {
        public EquipSlots[] Slots;
        public string[] Labels;
        public bool Strip;
        public float RowH = 24f;
        public float TextScale = 1.5f;

        public Action<DaggerfallUnityItem> OnLeft;
        public Action<DaggerfallUnityItem, Vector2> OnRight;
        public Action<DaggerfallUnityItem> OnHover;

        int hover = -1;
        DaggerfallUnityItem lastHover;

        public DaggerfallUnityItem Hovered
        {
            get { return ItemAt(hover); }
        }

        public SlotView()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            OnMouseClick += delegate (BaseScreenComponent s, Vector2 p) { Click(0); };
            OnRightMouseClick += delegate (BaseScreenComponent s, Vector2 p) { Click(1); };
        }

        DaggerfallUnityItem ItemAt(int i)
        {
            if (i < 0 || Slots == null || i >= Slots.Length)
                return null;
            return GameManager.Instance.PlayerEntity.ItemEquipTable.GetItem(Slots[i]);
        }

        Rect CellRect(int i, Rect r)
        {
            int n = Slots.Length;
            if (Strip)
            {
                float gap = Mathf.Max(2f, Mathf.Round(r.height * 0.1f));
                float cell = Mathf.Min(r.height - 2f * gap, Mathf.Floor((r.width - (n + 1) * gap) / n));
                float total = n * cell + (n - 1) * gap;
                float x0 = r.x + Mathf.Round((r.width - total) / 2f);
                return new Rect(x0 + i * (cell + gap), r.y + Mathf.Round((r.height - cell) / 2f), cell, cell);
            }
            return new Rect(r.x, r.y + i * RowH, r.width, RowH);
        }

        int IndexAt(Vector2 m)
        {
            if (Slots == null)
                return -1;
            Rect r = Rectangle;
            for (int i = 0; i < Slots.Length; i++)
            {
                if (CellRect(i, r).Contains(m))
                    return i;
            }
            return -1;
        }

        void Click(int button)
        {
            if (PopupMenu.Blocking)
                return;
            DaggerfallUnityItem item = ItemAt(IndexAt(Skin.MouseScreen()));
            if (item == null)
                return;
            if (button == 0 && OnLeft != null)
                OnLeft(item);
            else if (button == 1 && OnRight != null)
                OnRight(item, Skin.MouseScreen());
        }

        public override void Update()
        {
            base.Update();
            hover = -1;
            if (MouseOverComponent && !PopupMenu.Blocking)
                hover = IndexAt(Skin.MouseScreen());
            DaggerfallUnityItem h = Hovered;
            if (h != lastHover)
            {
                lastHover = h;
                if (OnHover != null)
                    OnHover(h);
            }
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled || Slots == null)
                return;
            Rect r = Rectangle;
            for (int i = 0; i < Slots.Length; i++)
            {
                Rect c = CellRect(i, r);
                DaggerfallUnityItem item = ItemAt(i);
                if (Strip)
                {
                    Skin.Fill(c, (i == hover && item != null) ? Skin.Hover : Skin.Zebra);
                    Skin.Fill(new Rect(c.x, c.y, c.width, 1f), Skin.Line);
                    Skin.Fill(new Rect(c.x, c.yMax - 1f, c.width, 1f), Skin.Line);
                    Skin.Fill(new Rect(c.x, c.y, 1f, c.height), Skin.Line);
                    Skin.Fill(new Rect(c.xMax - 1f, c.y, 1f, c.height), Skin.Line);
                    if (item != null)
                        Skin.DrawItemIcon(new Rect(c.x + 2f, c.y + 2f, c.width - 4f, c.height - 4f), item);
                }
                else
                {
                    if (i == hover && item != null)
                        Skin.Fill(c, Skin.Hover);
                    else if ((i & 1) == 1)
                        Skin.Fill(c, Skin.Zebra);
                    float pad = Mathf.Round(RowH * 0.22f);
                    float iconW = Mathf.Round(RowH * 1.25f);
                    if (item != null)
                        Skin.DrawItemIcon(new Rect(c.x + pad, c.y + 1f, iconW, RowH - 2f), item);
                    float tx = c.x + pad + iconW + pad;
                    float tw = c.xMax - tx - pad;
                    if (item != null)
                        Skin.TextInRow(Skin.Fit(item.LongName, tw, TextScale), tx, c.y, RowH, TextScale, item.IsEnchanted ? Skin.Magic : Skin.White);
                    else
                        Skin.TextInRow(Skin.Fit(Labels[i], tw, TextScale), tx, c.y, RowH, TextScale, Skin.Muted);
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Category tabs along the top of the item table.
    // ---------------------------------------------------------------------------------------

    public class TabBar : BaseScreenComponent
    {
        public string[] Names;
        public int[] Counts;
        public int Active;
        public float TextScale = 1.6f;
        public Action<int> OnSelect;

        public TabBar()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            OnMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                if (PopupMenu.Blocking || Names == null)
                    return;
                Vector2 m = Skin.MouseLocal(this);
                int i = Mathf.Clamp((int)(m.x / (Size.x / Names.Length)), 0, Names.Length - 1);
                if (OnSelect != null)
                    OnSelect(i);
            };
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled || Names == null)
                return;
            Rect r = Rectangle;
            float w = r.width / Names.Length;
            Vector2 m = Skin.MouseScreen();
            for (int i = 0; i < Names.Length; i++)
            {
                Rect cell = new Rect(r.x + i * w, r.y, w, r.height);
                bool on = (i == Active);
                string label = Names[i];
                string count = (Counts != null && i < Counts.Length && Counts[i] > 0) ? " " + Counts[i] : string.Empty;
                float tw = Skin.Width(label, TextScale);
                float cw = Skin.Width(count, TextScale * 0.8f);
                float x = cell.x + (cell.width - tw - cw) / 2f;
                Color gold = Skin.Gold;
                if (!on)
                    gold = new Color(gold.r, gold.g, gold.b, MouseOverComponent && cell.Contains(m) && !PopupMenu.Blocking ? 0.95f : 0.62f);
                Skin.TextInRow(label, x, cell.y, cell.height, TextScale, gold);
                if (count.Length > 0)
                    Skin.TextInRow(count, x + tw, cell.y + 1f, cell.height, TextScale * 0.8f, Skin.Muted);
                if (on)
                    Skin.Fill(new Rect(cell.x + cell.width * 0.12f, cell.yMax - Mathf.Max(2f, r.height * 0.12f), cell.width * 0.76f, Mathf.Max(2f, r.height * 0.06f)), Skin.Gold);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // One line of text that asks a callback for its content every frame.
    // ---------------------------------------------------------------------------------------

    public class Caption : BaseScreenComponent
    {
        public Func<string> Provider;
        public Func<Color> ColorProvider;
        public int Align;
        public float TextScale = 1.5f;

        public Caption()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled || Provider == null)
                return;
            Rect r = Rectangle;
            string text = Skin.Fit(Provider(), r.width, TextScale);
            Color c = (ColorProvider != null) ? ColorProvider() : Skin.Gold;
            float w = Skin.Width(text, TextScale);
            float x = r.x;
            if (Align == 1)
                x = r.x + (r.width - w) / 2f;
            else if (Align == 2)
                x = r.xMax - w;
            Skin.TextInRow(text, x, r.y, r.height, TextScale, c);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Right-click menu. Drawn on top of everything; while it is open (and for a frame after) the item lists ignore
    // the mouse, so the click that picks an entry never also lands on the row below it.
    // ---------------------------------------------------------------------------------------

    public class MenuEntry
    {
        public string Label;
        public string Hint;
        public Action Do;
        public bool Separator;
        public bool Dim;

        public MenuEntry(string label, Action action)
        {
            Label = label;
            Do = action;
        }

        public MenuEntry(string label, string hint, Action action)
        {
            Label = label;
            Hint = hint;
            Do = action;
        }

        public static MenuEntry Line()
        {
            MenuEntry e = new MenuEntry(string.Empty, null);
            e.Separator = true;
            return e;
        }
    }

    public class PopupMenu : BaseScreenComponent
    {
        static PopupMenu active;
        static int lastCloseFrame = -100;

        public float RowH = 24f;
        public float TextScale = 1.5f;

        List<MenuEntry> entries = new List<MenuEntry>();
        Rect area;
        bool open;
        int openFrame;
        int hover = -1;

        public static bool Blocking
        {
            get { return (active != null && active.open) || Time.frameCount - lastCloseFrame <= 1; }
        }

        public bool IsOpen
        {
            get { return open; }
        }

        public PopupMenu()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            Size = new Vector2(1, 1);
        }

        public void Open(Vector2 at, List<MenuEntry> list)
        {
            if (list == null || list.Count == 0)
                return;
            entries = list;
            float pad = Mathf.Round(RowH * 0.5f);
            float w = 0f;
            float h = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                MenuEntry e = entries[i];
                if (e.Separator)
                {
                    h += Mathf.Round(RowH * 0.35f);
                    continue;
                }
                float lw = Skin.Width(e.Label, TextScale) + (string.IsNullOrEmpty(e.Hint) ? 0f : Skin.Width(e.Hint, TextScale * 0.85f) + pad);
                w = Mathf.Max(w, lw);
                h += RowH;
            }
            w += pad * 2f;
            h += 4f;
            float x = Mathf.Min(at.x + 4f, Screen.width - w - 4f);
            float y = Mathf.Min(at.y + 4f, Screen.height - h - 4f);
            area = new Rect(Mathf.Max(4f, x), Mathf.Max(4f, y), w, h);
            open = true;
            active = this;
            openFrame = Time.frameCount;
            hover = -1;
        }

        public void Close()
        {
            if (!open)
                return;
            open = false;
            lastCloseFrame = Time.frameCount;
            if (active == this)
                active = null;
        }

        // The window is going away: forget the menu without the click-guard frame.
        public void Reset()
        {
            open = false;
            if (active == this)
                active = null;
        }

        int IndexAt(Vector2 m)
        {
            if (!area.Contains(m))
                return -1;
            float y = area.y + 2f;
            for (int i = 0; i < entries.Count; i++)
            {
                float h = entries[i].Separator ? Mathf.Round(RowH * 0.35f) : RowH;
                if (m.y >= y && m.y < y + h)
                    return entries[i].Separator ? -1 : i;
                y += h;
            }
            return -1;
        }

        public override void Update()
        {
            base.Update();
            if (!open || Time.frameCount == openFrame)
                return;

            Vector2 m = Skin.MouseScreen();
            hover = IndexAt(m);

            InputManager input = InputManager.Instance;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }
            bool up = input.GetMouseButtonUp(0) || input.GetMouseButtonUp(1);
            if (!up)
                return;

            MenuEntry chosen = (hover >= 0) ? entries[hover] : null;
            Close();
            if (chosen != null && !chosen.Dim && chosen.Do != null)
            {
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
                chosen.Do();
            }
        }

        public override void Draw()
        {
            base.Draw();
            if (!open)
                return;

            Skin.Fill(new Rect(area.x - 2f, area.y - 2f, area.width + 4f, area.height + 4f), new Color(0f, 0f, 0f, 0.35f));
            Skin.Fill(area, new Color(0.05f, 0.045f, 0.04f, 0.97f));
            Color edge = Skin.ButtonLine;
            Skin.Fill(new Rect(area.x, area.y, area.width, 2f), edge);
            Skin.Fill(new Rect(area.x, area.yMax - 2f, area.width, 2f), edge);
            Skin.Fill(new Rect(area.x, area.y, 2f, area.height), edge);
            Skin.Fill(new Rect(area.xMax - 2f, area.y, 2f, area.height), edge);

            float pad = Mathf.Round(RowH * 0.5f);
            float y = area.y + 2f;
            for (int i = 0; i < entries.Count; i++)
            {
                MenuEntry e = entries[i];
                if (e.Separator)
                {
                    float sh = Mathf.Round(RowH * 0.35f);
                    Skin.Fill(new Rect(area.x + pad * 0.5f, y + sh / 2f, area.width - pad, 1f), Skin.Line);
                    y += sh;
                    continue;
                }
                if (i == hover && !e.Dim)
                    Skin.Fill(new Rect(area.x + 2f, y, area.width - 4f, RowH), Skin.Hover);
                Color c = e.Dim ? Skin.Muted : ((i == hover) ? Skin.Gold : Skin.White);
                Skin.TextInRow(e.Label, area.x + pad, y, RowH, TextScale, c);
                if (!string.IsNullOrEmpty(e.Hint))
                    Skin.TextRight(e.Hint, area.xMax - pad, y, RowH, TextScale * 0.85f, Skin.Muted);
                y += RowH;
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // The inventory window.
    // ---------------------------------------------------------------------------------------

    public class ModernInventoryWindow : DaggerfallInventoryWindow
    {
        static readonly EquipSlots[] GearSlots =
        {
            EquipSlots.RightHand, EquipSlots.LeftHand, EquipSlots.Head, EquipSlots.RightArm, EquipSlots.LeftArm,
            EquipSlots.ChestArmor, EquipSlots.ChestClothes, EquipSlots.Gloves, EquipSlots.LegsArmor, EquipSlots.LegsClothes,
            EquipSlots.Feet, EquipSlots.Cloak1, EquipSlots.Cloak2
        };

        static readonly string[] GearLabels =
        {
            "Right hand", "Left hand", "Helm", "Right pauldron", "Left pauldron",
            "Cuirass", "Shirt", "Gauntlets", "Greaves", "Pants",
            "Boots", "Cloak", "Cloak"
        };

        static readonly EquipSlots[] TrinketSlots =
        {
            EquipSlots.Amulet0, EquipSlots.Amulet1, EquipSlots.Bracelet0, EquipSlots.Bracelet1, EquipSlots.Ring0, EquipSlots.Ring1,
            EquipSlots.Bracer0, EquipSlots.Bracer1, EquipSlots.Mark0, EquipSlots.Mark1, EquipSlots.Crystal0, EquipSlots.Crystal1
        };

        int tab;

        FramedPanel backdrop;
        FramedPanel trinketFrame;
        FramedPanel tabFrame;
        FramedPanel gearFrame;
        FramedPanel paperFrame;
        FramedPanel tableFrame;
        FramedPanel bottomFrame;
        FramedPanel sideHeadFrame;
        FramedPanel sideFrame;
        FramedPanel sideBottomFrame;
        Panel paperHost;

        SlotView trinkets;
        SlotView gear;
        TabBar tabBar;
        ItemTable table;
        ItemTable sideTable;
        PopupMenu menu;
        Caption footerLeft;
        Caption footerRight;
        Caption sideTitle;
        Caption sideWeight;

        Button wagonBtn;
        Button goldBtn;
        Button setupBtn;
        Button exitBtn;
        Button stackBtn;
        Button lootBtn;

        readonly InfoTip tip = new InfoTip();
        DaggerfallUnityItem hoverItem;
        DaggerfallUnityItem shownItem;
        bool layoutDirty = true;
        float lastW, lastH;
        bool lastShared, lastSide, lastWagon, lastQuick;

        public ModernInventoryWindow(IUserInterfaceManager uiManager, DaggerfallBaseWindow previous = null)
            : base(uiManager, previous)
        {
        }

        // ---- Setup ----

        protected override void Setup()
        {
            base.Setup();

            // Everything the stock window built stays alive for its logic, but only our own controls are shown.
            NativePanel.BackgroundTexture = null;
            NativePanel.Components.Clear();
            ParentPanel.Components.Remove(NativePanel);

            if (itemInfoPanel == null)
            {
                itemInfoPanel = DaggerfallUI.AddPanel(itemInfoPanelRect, NativePanel);
                SetupItemInfoPanel();
            }
            else
            {
                NativePanel.Components.Add(itemInfoPanel);
            }
            paperDoll.ToolTip = null;

            backdrop = NewFrame(Skin.BackdropFill);
            trinketFrame = NewFrame(Skin.PanelFill);
            tabFrame = NewFrame(Skin.PanelFill);
            gearFrame = NewFrame(Skin.PanelFill);
            paperFrame = NewFrame(Skin.PanelFill);
            tableFrame = NewFrame(Skin.PanelFill);
            bottomFrame = NewFrame(Skin.PanelFill);
            sideHeadFrame = NewFrame(Skin.PanelFill);
            sideFrame = NewFrame(Skin.PanelFill);
            sideBottomFrame = NewFrame(Skin.PanelFill);

            trinkets = new SlotView();
            trinkets.Slots = TrinketSlots;
            trinkets.Strip = true;
            trinkets.OnLeft = delegate (DaggerfallUnityItem item) { UnequipItem(item); };
            trinkets.OnRight = delegate (DaggerfallUnityItem item, Vector2 at) { menu.Open(at, EquippedEntries(item)); };
            trinketFrame.Components.Add(trinkets);

            gear = new SlotView();
            gear.Slots = GearSlots;
            gear.Labels = GearLabels;
            gear.OnLeft = delegate (DaggerfallUnityItem item) { UnequipItem(item); };
            gear.OnRight = delegate (DaggerfallUnityItem item, Vector2 at) { menu.Open(at, EquippedEntries(item)); };
            gearFrame.Components.Add(gear);

            tabBar = new TabBar();
            tabBar.Names = ItemRules.TabNames;
            tabBar.OnSelect = delegate (int i) { SetTab(i); DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick); };
            tabFrame.Components.Add(tabBar);

            paperHost = new Panel();
            paperHost.AutoSize = AutoSizeModes.None;
            paperHost.BackgroundColor = Color.clear;
            paperFrame.Components.Add(paperHost);
            paperDoll.AutoSize = AutoSizeModes.ScaleToFit;
            paperDoll.HorizontalAlignment = HorizontalAlignment.Center;
            paperDoll.VerticalAlignment = VerticalAlignment.Middle;
            paperHost.Components.Add(paperDoll);

            table = new ItemTable();
            table.Full = true;
            table.EmptyText = "Nothing in this category.";
            table.OnLeft = OnLocalLeft;
            table.OnMiddle = delegate (DaggerfallUnityItem item) { NextVariant(item); };
            table.OnRight = delegate (DaggerfallUnityItem item, Vector2 at) { menu.Open(at, LocalEntries(item)); };
            tableFrame.Components.Add(table);

            sideTable = new ItemTable();
            sideTable.Full = false;
            sideTable.EmptyText = "Empty.";
            sideTable.OnLeft = delegate (DaggerfallUnityItem item) { Take(item, false); };
            sideTable.OnMiddle = delegate (DaggerfallUnityItem item) { NextVariant(item); };
            sideTable.OnRight = delegate (DaggerfallUnityItem item, Vector2 at) { menu.Open(at, RemoteEntries(item)); };
            sideFrame.Components.Add(sideTable);

            footerLeft = new Caption();
            footerLeft.Provider = delegate () { return "Carry " + CarryText(); };
            footerLeft.ColorProvider = delegate () { return GetCarriedWeight() > PlayerEntity.MaxEncumbrance ? new Color(1f, 0.45f, 0.4f, 1f) : Skin.White; };
            tableFrame.Components.Add(footerLeft);
            footerRight = new Caption();
            footerRight.Align = 2;
            footerRight.Provider = delegate () { return "Gold " + PlayerEntity.GoldPieces.ToString("N0"); };
            footerRight.ColorProvider = delegate () { return Skin.Gold; };
            tableFrame.Components.Add(footerRight);

            sideTitle = new Caption();
            sideTitle.Provider = delegate () { return SideName(); };
            sideHeadFrame.Components.Add(sideTitle);
            sideWeight = new Caption();
            sideWeight.Align = 2;
            sideWeight.Provider = delegate () { return SideWeightText(); };
            sideWeight.ColorProvider = delegate () { return Skin.White; };
            sideHeadFrame.Components.Add(sideWeight);

            wagonBtn = NewButton("Wagon", bottomFrame, delegate () { CallStock("WagonButton_OnMouseClick"); });
            wagonBtn.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.InventoryWagon);
            goldBtn = NewButton("Gold", bottomFrame, delegate () { CallStock("GoldButton_OnMouseClick"); });
            goldBtn.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.InventoryGold);
            setupBtn = NewButton("Setup", bottomFrame, OpenSettings);
            exitBtn = NewButton("Exit", bottomFrame, delegate () { ExitButton_OnMouseClick(null, Vector2.zero); });
            stackBtn = NewButton("Quick stack", sideBottomFrame, DoQuickStack);
            lootBtn = NewButton("Quick loot", sideBottomFrame, DoQuickLoot);

            // Escape (or whatever the inventory exit shortcut is) closes the window.
            Button exitKey = DaggerfallUI.AddButton(new Rect(0, 0, 1, 1), ParentPanel);
            exitKey.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.InventoryExit);
            exitKey.OnKeyboardEvent += ExitButton_OnKeyboardEvent;

            for (int i = 0; i < ItemRules.TabCount; i++)
            {
                int index = i;
                Button key = DaggerfallUI.AddButton(new Rect(0, 0, 1, 1), ParentPanel);
                key.Hotkey = JunkUi.Key(KeyCode.Alpha1 + i);
                key.OnMouseClick += delegate (BaseScreenComponent s, Vector2 p) { SetTab(index); };
            }

            HotkeyCarrier(JunkStore.KeyJunk, delegate () { ToggleOnHovered(true); });
            HotkeyCarrier(JunkStore.KeyLock, delegate () { ToggleOnHovered(false); });
            HotkeyCarrier(JunkStore.KeyFilter, OpenSettings);
            HotkeyCarrier(JunkStore.KeyLoot, delegate () { if (CanQuickLoot()) DoQuickLoot(); });
            HotkeyCarrier(JunkStore.KeyStack, delegate () { if (usingWagon) DoQuickStack(); });

            menu = new PopupMenu();
            ParentPanel.Components.Add(menu);

            ParentPanel.Components.Add(NativePanel);

            tip.Attach(NativePanel, itemInfoPanel, itemInfoPanelLabel);

            SetTab(0);
            layoutDirty = true;
            Layout();
            RefreshModern();
        }

        FramedPanel NewFrame(Color fill)
        {
            FramedPanel f = new FramedPanel();
            f.AutoSize = AutoSizeModes.None;
            f.BackgroundColor = fill;
            ParentPanel.Components.Add(f);
            return f;
        }

        Button NewButton(string text, Panel parent, Action action)
        {
            Button b = DaggerfallUI.AddButton(new Rect(0, 0, 10, 10), parent);
            b.BackgroundColor = Skin.ButtonFill;
            b.Outline.Enabled = true;
            b.Outline.Color = Skin.ButtonLine;
            b.Label.Text = text;
            b.Label.TextColor = Skin.Gold;
            b.Label.ShadowColor = Skin.Shadow;
            b.Label.HorizontalAlignment = HorizontalAlignment.Center;
            b.Label.VerticalAlignment = VerticalAlignment.Middle;
            b.OnMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                if (PopupMenu.Blocking)
                    return;
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
                action();
            };
            Button self = b;
            b.OnMouseEnter += delegate (BaseScreenComponent s) { self.BackgroundColor = Skin.ButtonHover; };
            b.OnMouseLeave += delegate (BaseScreenComponent s) { self.BackgroundColor = Skin.ButtonFill; };
            return b;
        }

        void HotkeyCarrier(int action, Action what)
        {
            Button b = DaggerfallUI.AddButton(new Rect(0, 0, 1, 1), ParentPanel);
            b.Hotkey = JunkStore.Hotkey(action);
            b.OnMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                if (!PopupMenu.Blocking)
                    what();
            };
        }

        void CallStock(string method)
        {
            MethodInfo mi = typeof(DaggerfallInventoryWindow).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            if (mi != null)
                mi.Invoke(this, new object[] { null, Vector2.zero });
        }

        // ---- Layout ----

        bool SideOpen()
        {
            return remoteTargetType != RemoteTargetTypes.Dropped || (remoteItems != null && remoteItems.Count > 0);
        }

        void Layout()
        {
            float W = Screen.width;
            float H = Screen.height;
            bool shared = JunkStore.S.uiStyle == 1;
            bool side = SideOpen();
            bool quickLoot = CanQuickLoot();
            if (!layoutDirty && W == lastW && H == lastH && shared == lastShared && side == lastSide && usingWagon == lastWagon && quickLoot == lastQuick)
                return;
            layoutDirty = false;
            lastW = W; lastH = H; lastShared = shared; lastSide = side; lastWagon = usingWagon; lastQuick = quickLoot;

            float pad = Mathf.Round(Mathf.Clamp(H * 0.012f, 6f, 16f));
            float rowH = Mathf.Round(Mathf.Clamp(H * 0.036f, 20f, 40f));
            float ts = rowH * 0.74f / Skin.GlyphH;
            float fs = Mathf.Max(1, Mathf.RoundToInt(H / 700f));
            float b = fs * 2f + 2f;

            float mainH = Mathf.Round(H * 0.86f);
            float mainW = Mathf.Round(Mathf.Min(W * 0.60f, mainH * 1.9f));
            float mainX = Mathf.Round((W - mainW) / 2f);
            float mainY = Mathf.Round((H - mainH) / 2f);
            float inset = shared ? pad : 0f;
            float ix = mainX + inset;
            float iy = mainY + inset;
            float iw = mainW - 2f * inset;
            float ih = mainH - 2f * inset;

            float gearW = Mathf.Round(iw * 0.19f);
            float paperW = Mathf.Round(iw * 0.19f);
            float tableW = iw - gearW - paperW - 2f * pad;
            float trinketW = gearW + pad + paperW;
            float topH = Mathf.Max(Mathf.Round(rowH * 1.25f), Mathf.Floor((trinketW - 2f * pad) / 12f) + Mathf.Round(pad * 1.4f));
            float botH = Mathf.Round(rowH * 1.45f);
            float midH = ih - topH - botH - 2f * pad;
            float midY = iy + topH + pad;
            float botY = midY + midH + pad;
            float tableX = ix + gearW + pad + paperW + pad;

            menu.RowH = Mathf.Round(rowH * 0.9f);
            menu.TextScale = ts * 0.95f;

            Color fill = shared ? Skin.SharedFill : Skin.PanelFill;
            FramedPanel[] frames = { trinketFrame, tabFrame, gearFrame, paperFrame, tableFrame, bottomFrame, sideHeadFrame, sideFrame, sideBottomFrame };
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i].BackgroundColor = fill;
                frames[i].FrameScale = fs;
            }
            backdrop.FrameScale = fs;
            backdrop.Enabled = shared;

            Place(trinketFrame, ix, iy, trinketW, topH);
            Place(tabFrame, tableX, iy, tableW, topH);
            Place(gearFrame, ix, midY, gearW, midH);
            Place(paperFrame, ix + gearW + pad, midY, paperW, midH);
            Place(tableFrame, tableX, midY, tableW, midH);
            Place(bottomFrame, ix, botY, iw, botH);

            Fill(trinkets, trinketW, topH, 0f);
            trinkets.TextScale = ts;
            Fill(tabBar, tableW, topH, 0f);
            tabBar.TextScale = ts * 1.05f;
            gear.RowH = Mathf.Round(rowH * 0.92f);
            gear.TextScale = ts * 0.92f;
            gear.Position = new Vector2(b, b);
            gear.Size = new Vector2(gearW - 2f * b, midH - 2f * b);

            paperHost.Position = new Vector2(b + pad * 0.5f, b + pad * 0.5f);
            paperHost.Size = new Vector2(paperW - 2f * b - pad, midH - 2f * b - pad);

            float footH = Mathf.Round(rowH * 1.15f);
            table.RowH = rowH;
            table.TextScale = ts;
            table.Position = new Vector2(b, b);
            table.Size = new Vector2(tableW - 2f * b, midH - 2f * b - footH);
            footerLeft.TextScale = ts;
            footerLeft.Position = new Vector2(b + pad, midH - b - footH);
            footerLeft.Size = new Vector2((tableW - 2f * b) * 0.5f, footH);
            footerRight.TextScale = ts;
            footerRight.Position = new Vector2(b + (tableW - 2f * b) * 0.5f - pad, midH - b - footH);
            footerRight.Size = new Vector2((tableW - 2f * b) * 0.5f, footH);

            // Bottom bar: Wagon and Gold on the left, Setup and Exit on the right.
            float btnH = botH - 2f * b - 2f;
            float btnY = Mathf.Round((botH - btnH) / 2f);
            float x = pad;
            x = PlaceButton(wagonBtn, x, btnY, btnH, ts, pad) + pad;
            PlaceButton(goldBtn, x, btnY, btnH, ts, pad);
            float rx = iw - pad;
            rx = PlaceButtonRight(exitBtn, rx, btnY, btnH, ts, pad) - pad;
            PlaceButtonRight(setupBtn, rx, btnY, btnH, ts, pad);

            // Side panel to the right of the main window.
            float sideX = shared ? mainX + mainW : mainX + mainW + pad;
            float sideW = Mathf.Round(Mathf.Min(W * 0.18f, W - sideX - pad - (shared ? pad : 0f)));
            sideW = Mathf.Max(sideW, rowH * 6f);
            sideHeadFrame.Enabled = side;
            sideFrame.Enabled = side;
            sideBottomFrame.Enabled = side && (usingWagon || quickLoot);
            Place(sideHeadFrame, sideX, iy, sideW, topH);
            Place(sideFrame, sideX, midY, sideW, midH);
            Place(sideBottomFrame, sideX, botY, sideW, botH);
            sideTitle.TextScale = ts;
            sideTitle.Position = new Vector2(b + pad, 0f);
            sideTitle.Size = new Vector2(sideW * 0.5f, topH);
            sideWeight.TextScale = ts * 0.9f;
            sideWeight.Position = new Vector2(sideW * 0.45f, 0f);
            sideWeight.Size = new Vector2(sideW * 0.55f - b - pad, topH);
            sideTable.RowH = rowH;
            sideTable.TextScale = ts;
            sideTable.Position = new Vector2(b, b);
            sideTable.Size = new Vector2(sideW - 2f * b, midH - 2f * b);

            stackBtn.Enabled = usingWagon;
            lootBtn.Enabled = !usingWagon && quickLoot;
            Button quick = usingWagon ? stackBtn : lootBtn;
            quick.Position = new Vector2(pad, btnY);
            quick.Size = new Vector2(sideW - 2f * pad, btnH);
            quick.Label.TextScale = ts;

            float backR = side ? sideX + sideW + (shared ? pad : 0f) : mainX + mainW;
            Place(backdrop, mainX, mainY, backR - mainX, mainH);
            RefreshButtonHints();
        }

        static void Place(FramedPanel f, float x, float y, float w, float h)
        {
            f.Position = new Vector2(x, y);
            f.Size = new Vector2(w, h);
        }

        static void Fill(BaseScreenComponent c, float w, float h, float inset)
        {
            c.Position = new Vector2(inset, inset);
            c.Size = new Vector2(w - 2f * inset, h - 2f * inset);
        }

        float PlaceButton(Button btn, float x, float y, float h, float ts, float pad)
        {
            btn.Label.TextScale = ts;
            float w = Mathf.Round(Skin.Width(btn.Label.Text, ts) + pad * 3f);
            btn.Position = new Vector2(x, y);
            btn.Size = new Vector2(w, h);
            return x + w;
        }

        float PlaceButtonRight(Button btn, float right, float y, float h, float ts, float pad)
        {
            btn.Label.TextScale = ts;
            float w = Mathf.Round(Skin.Width(btn.Label.Text, ts) + pad * 3f);
            btn.Position = new Vector2(right - w, y);
            btn.Size = new Vector2(w, h);
            return right - w;
        }

        void RefreshButtonHints()
        {
            JunkUi.BindKey(setupBtn, JunkStore.KeyFilter, "Mod settings: loot and sell filters, junk list, keys, HUD and window options.", defaultToolTip);
            JunkUi.BindKey(stackBtn, JunkStore.KeyStack, "Move every unlocked item on this tab into the cart.", defaultToolTip);
            JunkUi.BindKey(lootBtn, JunkStore.KeyLoot, "Take everything the loot filter allows, then close.", defaultToolTip);
            wagonBtn.ToolTip = defaultToolTip;
            wagonBtn.ToolTipText = "Show or hide the cart.";
            goldBtn.ToolTip = defaultToolTip;
            goldBtn.ToolTipText = "Drop gold.";
        }

        // ---- Stock window hooks ----

        protected override void FilterLocalItems()
        {
            localItemsFiltered.Clear();
            if (localItems == null)
                return;
            for (int i = 0; i < localItems.Count; i++)
            {
                DaggerfallUnityItem item = localItems.GetItem(i);
                if (!item.IsEquipped && ItemRules.Tab(item) == tab)
                    localItemsFiltered.Add(item);
            }
        }

        protected override void SelectTabPage(TabPages tabPage)
        {
            selectedTabPage = tabPage;
            SetTab(0);
        }

        protected override void SetRemoteItemsAnimation()
        {
            // Shop shelf coins are part of the classic art; the table has no use for them.
        }

        public override void Refresh(bool refreshPaperDoll = true)
        {
            if (!IsSetup)
                return;
            base.Refresh(refreshPaperDoll);
            RefreshModern();
        }

        void RefreshModern()
        {
            if (table == null)
                return;
            table.SetItems(localItemsFiltered);
            sideTable.SetItems(remoteItemsFiltered);
            int[] counts = new int[ItemRules.TabCount];
            if (localItems != null)
            {
                for (int i = 0; i < localItems.Count; i++)
                {
                    DaggerfallUnityItem item = localItems.GetItem(i);
                    if (!item.IsEquipped)
                        counts[ItemRules.Tab(item)]++;
                }
            }
            tabBar.Counts = counts;
        }

        void SetTab(int index)
        {
            tab = Mathf.Clamp(index, 0, ItemRules.TabCount - 1);
            if (tabBar != null)
                tabBar.Active = tab;
            if (table != null)
                table.ResetScroll();
            FilterLocalItems();
            if (table != null)
                RefreshModern();
            if (itemInfoPanelLabel != null)
                itemInfoPanelLabel.SetText(new TextFile.Token[0]);
        }

        public override void OnPush()
        {
            base.OnPush();
            if (menu != null)
                menu.Reset();
            CorpseCleaner.OpenLoot = lootTarget;
            layoutDirty = true;
        }

        public override void OnPop()
        {
            base.OnPop();
            if (menu != null)
                menu.Reset();
            CorpseCleaner.OpenLoot = null;
        }

        public override void Update()
        {
            base.Update();
            if (table == null)
                return;

            Layout();

            DaggerfallUnityItem h = null;
            if (!PopupMenu.Blocking)
            {
                h = table.Hovered ?? sideTable.Hovered ?? gear.Hovered ?? trinkets.Hovered;
            }
            if (h != shownItem)
            {
                shownItem = h;
                if (h != null)
                    UpdateItemInfoPanel(h);
            }
            hoverItem = h;
            bool paperHover = !PopupMenu.Blocking && paperDoll.MouseOverComponent && !string.IsNullOrEmpty(paperDoll.ToolTipText);
            tip.Place(h != null || paperHover, 1f);
        }

        protected override void UpdateItemInfoPanel(DaggerfallUnityItem item)
        {
            if (!tip.Attached || item == null || item.ItemGroup == ItemGroups.Paintings)
            {
                tip.ClearCompare();
                if (item != null)
                    base.UpdateItemInfoPanel(item);
                else if (itemInfoPanelLabel != null)
                    itemInfoPanelLabel.SetText(new TextFile.Token[0]);
                return;
            }

            TextFile.Token[] tokens = InfoTip.BuildTokens(item);
            tip.Remember(tokens);
            UpdateItemInfoPanel(tokens);
            tip.SetItem(item, PlayerEntity);
        }

        // Clicking the paper doll always takes the piece off, whatever mode the stock window is in.
        protected override void PaperDoll_OnMouseClick(BaseScreenComponent sender, Vector2 position, ActionModes actionMode)
        {
            base.PaperDoll_OnMouseClick(sender, position, ActionModes.Equip);
        }

        protected override void PaperDoll_OnRightMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            byte value = paperDoll.GetEquipIndex((int)position.x, (int)position.y);
            if (value == 0xff)
                return;
            DaggerfallUnityItem item = PlayerEntity.ItemEquipTable.GetItem((EquipSlots)value);
            if (item != null)
                menu.Open(Skin.MouseScreen(), EquippedEntries(item));
        }

        // ---- Item actions and menus ----

        bool CanMoveAcross()
        {
            return remoteItems != null && !chooseOne;
        }

        bool Wearable(DaggerfallUnityItem item)
        {
            return PlayerEntity.ItemEquipTable.GetEquipSlot(item) != EquipSlots.None;
        }

        void OnLocalLeft(DaggerfallUnityItem item)
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift && CanMoveAcross())
                LocalItemListScroller_OnItemClick(item, ActionModes.Remove);
            else
                LocalItemListScroller_OnItemClick(item, (Wearable(item) || item.IsLightSource) ? ActionModes.Equip : ActionModes.Use);
        }

        void Take(DaggerfallUnityItem item, bool equip)
        {
            RemoteItemListScroller_OnItemClick(item, equip ? ActionModes.Equip : ActionModes.Remove);
        }

        string MoveLabel()
        {
            if (usingWagon)
                return "Move to cart";
            if (remoteTargetType == RemoteTargetTypes.Loot)
                return "Put in " + SideName().ToLower();
            return "Drop";
        }

        string KeyHint(int action)
        {
            string name = JunkStore.KeyName(action);
            return (name == "none") ? null : name;
        }

        List<MenuEntry> LocalEntries(DaggerfallUnityItem item)
        {
            List<MenuEntry> list = new List<MenuEntry>();
            if (item.IsLightSource)
                list.Add(new MenuEntry("Light", delegate () { LocalItemListScroller_OnItemClick(item, ActionModes.Equip); }));
            else if (Wearable(item))
                list.Add(new MenuEntry("Equip", delegate () { LocalItemListScroller_OnItemClick(item, ActionModes.Equip); }));
            else
                list.Add(new MenuEntry("Use", delegate () { LocalItemListScroller_OnItemClick(item, ActionModes.Use); }));
            list.Add(new MenuEntry("Info", delegate () { ShowInfoPopup(item); }));
            if (CanMoveAcross())
                list.Add(new MenuEntry(MoveLabel(), "shift+click", delegate () { LocalItemListScroller_OnItemClick(item, ActionModes.Remove); }));
            list.Add(MenuEntry.Line());
            list.Add(new MenuEntry(JunkStore.IsLocked(item) ? "Unlock" : "Lock", KeyHint(JunkStore.KeyLock),
                delegate () { JunkUi.Hud(JunkStore.ToggleLock(item)); RefreshModern(); }));
            if (ItemRules.CanBeJunk(item))
                list.Add(new MenuEntry(JunkStore.IsJunk(item) ? "Remove junk tag" : "Tag as junk", KeyHint(JunkStore.KeyJunk),
                    delegate () { JunkUi.Hud(JunkStore.ToggleJunk(item)); RefreshModern(); }));
            return list;
        }

        List<MenuEntry> RemoteEntries(DaggerfallUnityItem item)
        {
            List<MenuEntry> list = new List<MenuEntry>();
            list.Add(new MenuEntry("Take", delegate () { Take(item, false); }));
            if (Wearable(item))
                list.Add(new MenuEntry("Take and equip", delegate () { Take(item, true); }));
            list.Add(new MenuEntry("Info", delegate () { ShowInfoPopup(item); }));
            return list;
        }

        List<MenuEntry> EquippedEntries(DaggerfallUnityItem item)
        {
            List<MenuEntry> list = new List<MenuEntry>();
            list.Add(new MenuEntry("Unequip", delegate () { UnequipItem(item); }));
            list.Add(new MenuEntry("Info", delegate () { ShowInfoPopup(item); }));
            return list;
        }

        // The lock and junk keys act on whatever item the mouse is over.
        void ToggleOnHovered(bool junk)
        {
            DaggerfallUnityItem item = hoverItem;
            if (item == null)
            {
                JunkUi.Hud("Point at an item first.");
                return;
            }
            if (junk)
                JunkUi.Hud(JunkStore.ToggleJunk(item));
            else
                JunkUi.Hud(JunkStore.ToggleLock(item));
            RefreshModern();
        }

        // ---- Texts ----

        string CarryText()
        {
            float w = GetCarriedWeight();
            return string.Format(w % 1 == 0 ? "{0:F0} / {1} kg" : "{0:F1} / {1} kg", w, PlayerEntity.MaxEncumbrance);
        }

        string SideName()
        {
            if (usingWagon)
                return "Cart";
            if (remoteTargetType == RemoteTargetTypes.Loot && lootTarget != null)
            {
                switch (lootTarget.ContainerType)
                {
                    case LootContainerTypes.CorpseMarker: return "Corpse";
                    case LootContainerTypes.RandomTreasure: return "Treasure";
                    case LootContainerTypes.HouseContainers: return "Container";
                    default: return lootTarget.playerOwned ? "Container" : "Pile";
                }
            }
            if (remoteTargetType == RemoteTargetTypes.Merchant)
                return "Shelf";
            return "Ground";
        }

        string SideWeightText()
        {
            if (usingWagon)
            {
                float w = PlayerEntity.WagonWeight;
                return string.Format(w % 1 == 0 ? "{0:F0} / {1} kg" : "{0:F1} / {1} kg", w, ItemHelper.WagonKgLimit);
            }
            if (remoteItems == null)
                return string.Empty;
            float rw = remoteItems.GetWeight();
            return string.Format(rw % 1 == 0 ? "{0:F0} kg" : "{0:F1} kg", rw);
        }

        void OpenSettings()
        {
            uiManager.PushWindow(new FilterSettingsWindow(uiManager, this, delegate ()
            {
                layoutDirty = true;
                Refresh(false);
            }));
        }

        // ---- Quick loot and quick stack ----

        bool CanQuickLoot()
        {
            if (lootTarget == null || remoteTargetType != RemoteTargetTypes.Loot)
                return false;
            if (lootTarget.playerOwned || lootTarget.houseOwned || shopShelfStealing || chooseOne)
                return false;
            return lootTarget.ContainerType == LootContainerTypes.CorpseMarker
                || lootTarget.ContainerType == LootContainerTypes.RandomTreasure;
        }

        void LootPlate_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            DoQuickLoot();
        }

        int HowManyCanCarry(DaggerfallUnityItem item)
        {
            float unit = item.EffectiveUnitWeightInKg();
            int unitGp = Mathf.RoundToInt(unit * 400f);
            if (unitGp <= 0)
                return item.stackCount;
            int capacityGp = Mathf.RoundToInt(PlayerEntity.MaxEncumbrance * 400f);
            int loadGp = Mathf.RoundToInt(PlayerEntity.CarriedWeight * 400f);
            return Math.Min(item.stackCount, (capacityGp - loadGp) / unitGp);
        }

        void DoQuickLoot()
        {
            if (!CanQuickLoot())
                return;

            FilterProfile profile = JunkStore.S.loot;
            ItemCollection mine = PlayerEntity.Items;

            List<DaggerfallUnityItem> snapshot = new List<DaggerfallUnityItem>();
            for (int i = 0; i < remoteItems.Count; i++)
                snapshot.Add(remoteItems.GetItem(i));

            int taken = 0;
            int filteredOut = 0;
            int tooHeavy = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                DaggerfallUnityItem item = snapshot[i];
                if (item == null)
                    continue;

                bool gold = ItemRules.IsGold(item);
                bool must = gold || item.IsQuestItem;
                if (!must && (item.IsSummoned || !ItemRules.PassesLoot(item, profile)))
                {
                    filteredOut++;
                    continue;
                }

                if (gold)
                {
                    DoTransferItem(item, remoteItems, mine, false);
                    taken++;
                    continue;
                }

                // Quest items and maps need DFU's own bookkeeping.
                if (item.IsQuestItem || item.IsOfTemplate(ItemGroups.MiscItems, (int)MiscItems.Map))
                {
                    TransferItem(item, remoteItems, mine);
                    taken++;
                    continue;
                }

                int can = HowManyCanCarry(item);
                if (can <= 0)
                {
                    tooHeavy++;
                    continue;
                }

                DaggerfallUnityItem toMove = item;
                if (can < item.stackCount && item.IsAStack())
                {
                    toMove = remoteItems.SplitStack(item, can);
                    if (toMove == null)
                    {
                        tooHeavy++;
                        continue;
                    }
                    tooHeavy++;
                }
                DoTransferItem(toMove, remoteItems, mine, false);
                taken++;
            }

            if (tooHeavy > 0)
            {
                JunkUi.Hud("You can't carry everything: " + tooHeavy + " item(s) left behind.");
                return;
            }

            if (filteredOut > 0)
            {
                JunkUi.Hud("Looted " + taken + " item(s), left " + filteredOut + " filtered out.");
                CorpseCleaner.MarkLeftover(lootTarget);
            }
            else
            {
                JunkUi.Hud("Looted " + taken + " item(s).");
            }
            CloseWindow();
        }

        // Moves everything on the open tab that isn't locked from the backpack into the cart.
        void DoQuickStack()
        {
            if (!usingWagon || remoteItems == null || remoteTargetType != RemoteTargetTypes.Wagon)
                return;

            List<DaggerfallUnityItem> snapshot = new List<DaggerfallUnityItem>(localItemsFiltered);
            int moved = 0;
            int kept = 0;
            int noRoom = 0;

            for (int i = 0; i < snapshot.Count; i++)
            {
                DaggerfallUnityItem item = snapshot[i];
                if (item == null || item.IsEquipped)
                    continue;

                if (JunkStore.IsLocked(item))
                {
                    kept++;
                    continue;
                }

                // Same things the stock transfer refuses or handles specially.
                if (item.IsQuestItem || item.IsSummoned || item.ItemGroup == ItemGroups.Transportation
                    || item.IsOfTemplate(ItemGroups.MiscItems, (int)MiscItems.Map))
                {
                    kept++;
                    continue;
                }

                int can = WagonRoomFor(item);
                if (can <= 0)
                {
                    noRoom++;
                    continue;
                }

                DaggerfallUnityItem toMove = item;
                if (can < item.stackCount && item.IsAStack())
                {
                    toMove = localItems.SplitStack(item, can);
                    if (toMove == null)
                    {
                        noRoom++;
                        continue;
                    }
                    noRoom++;
                }
                else if (can < item.stackCount)
                {
                    noRoom++;
                    continue;
                }

                if (toMove.IsLightSource && PlayerEntity.LightSource == toMove)
                    PlayerEntity.LightSource = null;
                remoteItems.Transfer(toMove, localItems);
                moved++;
            }

            Refresh(false);
            localItemListScroller.Items = localItemsFiltered;
            remoteItemListScroller.Items = remoteItemsFiltered;
            if (moved > 0)
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);

            string msg = "Stacked " + moved + " item(s) into the cart";
            if (kept > 0)
                msg += ", kept " + kept + " locked";
            if (noRoom > 0)
                msg += ", cart full for " + noRoom;
            JunkUi.Hud(msg + ".");
        }

        int WagonRoomFor(DaggerfallUnityItem item)
        {
            float unit = item.EffectiveUnitWeightInKg();
            int unitGp = Mathf.RoundToInt(unit * 400f);
            if (unitGp <= 0)
                return item.stackCount;
            int capacityGp = Mathf.RoundToInt(ItemHelper.WagonKgLimit * 400f);
            int loadGp = Mathf.RoundToInt(remoteItems.GetWeight() * 400f);
            return Math.Min(item.stackCount, (capacityGp - loadGp) / unitGp);
        }
    }
}
