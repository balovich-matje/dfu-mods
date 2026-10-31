// Junk & Loot Filter - a Daggerfall Unity mod.
// Inventory (quick loot, junk tagging, item locks) and trade (quick sell) windows.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace JunkLootFilter
{
    // ---------------------------------------------------------------------------------------
    // Pixel helpers. Coordinates are top-left based like DFU's UI rects; Unity stores rows bottom-up.
    // ---------------------------------------------------------------------------------------

    public class PixelBuffer
    {
        public Color32[] px;
        public int w;
        public int h;

        public PixelBuffer(Texture2D tex)
        {
            w = tex.width;
            h = tex.height;
            px = tex.GetPixels32();
        }

        PixelBuffer()
        {
        }

        public PixelBuffer Clone()
        {
            PixelBuffer c = new PixelBuffer();
            c.w = w;
            c.h = h;
            c.px = (Color32[])px.Clone();
            return c;
        }

        int Index(int x, int y)
        {
            return (h - 1 - y) * w + x;
        }

        public Color32 Get(int x, int y)
        {
            return px[Index(x, y)];
        }

        public void Set(int x, int y, Color32 c)
        {
            px[Index(x, y)] = c;
        }

        public Texture2D ToTexture()
        {
            Texture2D t = new Texture2D(w, h, TextureFormat.ARGB32, false);
            t.SetPixels32(px);
            t.Apply(false);
            t.filterMode = DaggerfallUI.Instance.GlobalFilterMode;
            return t;
        }

        public Texture2D Sub(int x, int y, int sw, int sh)
        {
            Texture2D t = new Texture2D(sw, sh, TextureFormat.ARGB32, false);
            Color32[] outPx = new Color32[sw * sh];
            for (int yy = 0; yy < sh; yy++)
                for (int xx = 0; xx < sw; xx++)
                    outPx[(sh - 1 - yy) * sw + xx] = Get(x + xx, y + yy);
            t.SetPixels32(outPx);
            t.Apply(false);
            t.filterMode = DaggerfallUI.Instance.GlobalFilterMode;
            return t;
        }
    }

    public static class JunkUi
    {
        static readonly Color GoldText = new Color(0.97f, 0.9f, 0.1f, 1f);
        static readonly Color PlateBackground = new Color(0.07f, 0.06f, 0.04f, 0.85f);
        static readonly Color PlateOutline = new Color(0.8f, 0.68f, 0.2f, 1f);

        static Texture2D[] junkOverlay;
        static Texture2D[] lockOverlay;

        // The button slabs in the stock art are 33x16 px: the 31x14 click rect plus a 1 px frame.
        public const int SlabWidth = 33;
        public const int SlabHeight = 16;

        // Replaces the lettering of a slab with plain slab texture, so a label can be drawn instead.
        public static void WipeSlabText(PixelBuffer b, int rectX, int rectY)
        {
            for (int y = 2; y <= 11; y++)
            {
                int srcY = rectY + ((y % 2 == 0) ? 1 : 12);
                for (int x = 2; x <= 28; x++)
                    b.Set(rectX + x, rectY + y, b.Get(rectX + x, srcY));
            }
        }

        // Copies the whole 33x16 slab around a button rect.
        public static void PasteSlab(PixelBuffer src, PixelBuffer dst, int srcRectX, int srcRectY, int dstRectX, int dstRectY)
        {
            for (int y = -1; y <= SlabHeight - 2; y++)
                for (int x = -1; x <= SlabWidth - 2; x++)
                    dst.Set(dstRectX + x, dstRectY + y, src.Get(srcRectX + x, srcRectY + y));
        }

        public static void StyleSlabLabel(Button b, string text)
        {
            b.Label.Text = text;
            b.Label.TextScale = 1.5f;
            b.Label.TextColor = GoldText;
            b.Label.ShadowColor = new Color(0.1f, 0.08f, 0f, 1f);
            b.Label.ShadowPosition = new Vector2(0.5f, 0.5f);
            b.Label.HorizontalAlignment = HorizontalAlignment.Center;
            b.Label.VerticalAlignment = VerticalAlignment.Middle;
        }

        public static Button SlabButton(Panel parent, Rect rect, Texture2D background, string text)
        {
            Button b = DaggerfallUI.AddButton(rect, parent);
            b.BackgroundTexture = background;
            StyleSlabLabel(b, text);
            return b;
        }

        public static Button MakePlate(Panel parent, Rect rect, string text)
        {
            Button b = DaggerfallUI.AddTextButton(rect, text, parent);
            b.BackgroundColor = PlateBackground;
            b.Outline.Color = PlateOutline;
            b.Label.TextColor = GoldText;
            b.Label.VerticalAlignment = VerticalAlignment.Middle;
            return b;
        }

        // Sets the hotkey from the settings and puts it in the tooltip.
        public static void BindKey(Button b, int action, string tip, ToolTip toolTip)
        {
            if (b == null)
                return;
            b.Hotkey = JunkStore.Hotkey(action);
            b.ToolTip = toolTip;
            string key = JunkStore.KeyName(action);
            b.ToolTipText = (key == "none") ? tip : tip + " [" + key + "]";
        }

        // Dimming veil over the whole item cell with a small gold coin in the top right corner.
        public static Texture2D[] JunkOverlay()
        {
            if (junkOverlay != null && junkOverlay[0] != null)
                return junkOverlay;

            const int w = 25;
            const int h = 19;
            PixelBuffer b = new PixelBuffer(new Texture2D(w, h, TextureFormat.ARGB32, false));
            Color32 veil = new Color32(80, 80, 80, 175);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    b.Set(x, y, veil);

            string[] coin =
            {
                "..ooo..",
                ".oYYYo.",
                "oYLLYYo",
                "oYLYYYo",
                "oYYYYYo",
                ".oYYYo.",
                "..ooo.."
            };
            Stamp(b, coin, w - 8, 1, new Color32(70, 45, 0, 255), new Color32(240, 190, 20, 255), new Color32(255, 240, 140, 255));

            junkOverlay = new Texture2D[] { b.ToTexture() };
            return junkOverlay;
        }

        // A blue frame around the item cell and a small padlock in the top right corner.
        public static Texture2D[] LockOverlay()
        {
            if (lockOverlay != null && lockOverlay[0] != null)
                return lockOverlay;

            const int w = 25;
            const int h = 19;
            PixelBuffer b = new PixelBuffer(new Texture2D(w, h, TextureFormat.ARGB32, false));
            Color32 clear = new Color32(0, 0, 0, 0);
            Color32 frame = new Color32(110, 160, 235, 255);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool edge = (x == 0 || y == 0 || x == w - 1 || y == h - 1);
                    b.Set(x, y, edge ? frame : clear);
                }
            }

            string[] padlock =
            {
                "..ooo..",
                ".o...o.",
                ".o...o.",
                "ooooooo",
                "oYYYYYo",
                "oYYoYYo",
                "ooooooo"
            };
            Stamp(b, padlock, w - 9, 2, new Color32(20, 30, 60, 255), new Color32(170, 195, 245, 255), new Color32(255, 255, 255, 255));

            lockOverlay = new Texture2D[] { b.ToTexture() };
            return lockOverlay;
        }

        // 'o' = outline, 'Y' = fill, 'L' = highlight.
        static void Stamp(PixelBuffer b, string[] art, int x0, int y0, Color32 outline, Color32 fill, Color32 light)
        {
            for (int cy = 0; cy < art.Length; cy++)
            {
                for (int cx = 0; cx < art[cy].Length; cx++)
                {
                    char c = art[cy][cx];
                    if (c == 'o')
                        b.Set(x0 + cx, y0 + cy, outline);
                    else if (c == 'Y')
                        b.Set(x0 + cx, y0 + cy, fill);
                    else if (c == 'L')
                        b.Set(x0 + cx, y0 + cy, light);
                }
            }
        }

        // Locked items show the padlock; otherwise junk shows the coin.
        public static Texture2D[] OverlayFor(DaggerfallUnityItem item)
        {
            if (JunkStore.IsLocked(item))
                return LockOverlay();
            if (JunkStore.IsJunk(item))
                return JunkOverlay();
            return null;
        }

        public static HotkeySequence Key(KeyCode code)
        {
            return new HotkeySequence(code, HotkeySequence.KeyModifiers.None);
        }

        public static void Hud(string message)
        {
            DaggerfallUI.AddHUDText(message);
        }

        // Removes DFU's own name tooltip from everything below this component.
        public static void StripToolTips(BaseScreenComponent root)
        {
            root.ToolTip = null;
            Panel p = root as Panel;
            if (p == null)
                return;
            for (int i = 0; i < p.Components.Count; i++)
                StripToolTips(p.Components[i]);
        }

        // True while the mouse is over a populated item cell anywhere below this component.
        public static bool AnyCellHovered(BaseScreenComponent root)
        {
            Panel p = root as Panel;
            if (p == null)
                return false;
            for (int i = 0; i < p.Components.Count; i++)
            {
                BaseScreenComponent c = p.Components[i];
                Button b = c as Button;
                if (b != null)
                {
                    if (b.MouseOverComponent && !string.IsNullOrEmpty(b.ToolTipText))
                        return true;
                }
                else if (AnyCellHovered(c))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsHoveringItem(ItemListScroller local, ItemListScroller remote, PaperDoll doll, Button[] accessories, Button gold)
        {
            if (local != null && AnyCellHovered(local))
                return true;
            if (remote != null && AnyCellHovered(remote))
                return true;
            if (doll != null && doll.MouseOverComponent && !string.IsNullOrEmpty(doll.ToolTipText))
                return true;
            if (accessories != null)
            {
                for (int i = 0; i < accessories.Length; i++)
                {
                    Button a = accessories[i];
                    if (a != null && a.MouseOverComponent && !string.IsNullOrEmpty(a.ToolTipText))
                        return true;
                }
            }
            return gold != null && gold.MouseOverComponent;
        }
    }

    // ---------------------------------------------------------------------------------------
    // DFU's item info panel normally sits in the middle button column. Here it follows the cursor instead,
    // also shows magic powers and recipe ingredients, and compares the item with what is equipped.
    // ---------------------------------------------------------------------------------------

    public class InfoTip
    {
        static readonly Color Better = new Color(0.45f, 1f, 0.45f, 1f);
        static readonly Color Worse = new Color(1f, 0.45f, 0.4f, 1f);
        static readonly Color Same = new Color(0.95f, 0.95f, 0.9f, 1f);
        static readonly Color Muted = new Color(0.7f, 0.7f, 0.65f, 1f);

        const int MaxLines = 8;
        // The hover keeps the on-screen size it had before the windows were shrunk, then grows by this much.
        const float FontBoost = 1.3f;

        Panel nativePanel;
        Panel panel;
        MultiFormatTextLabel label;
        TextLabel[] lines = new TextLabel[MaxLines];
        FramedPanel frame;
        int lineCount;
        float appliedScale = 1f;
        TextFile.Token[] lastTokens;

        public bool Attached
        {
            get { return panel != null; }
        }

        public void Attach(Panel nativePanel, Panel infoPanel, MultiFormatTextLabel infoLabel)
        {
            if (infoPanel == null || infoLabel == null)
                return;

            this.nativePanel = nativePanel;
            panel = infoPanel;
            label = infoLabel;

            panel.BackgroundTexture = null;
            panel.BackgroundColor = new Color(0.06f, 0.05f, 0.04f, 0.94f);
            panel.Outline.Enabled = false;
            label.VerticalAlignment = VerticalAlignment.None;
            label.Position = new Vector2(3, 2);
            label.MaxTextWidth = 62;
            label.TextScale = 0.5f;

            for (int i = 0; i < MaxLines; i++)
            {
                lines[i] = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, string.Empty, panel);
                lines[i].TextScale = 0.5f;
                lines[i].ShadowPosition = new Vector2(0.5f, 0.5f);
                lines[i].ShadowColor = new Color(0f, 0f, 0f, 0.8f);
                lines[i].Enabled = false;
            }

            // DFU's compass border, drawn last so it sits over the text margins.
            frame = new FramedPanel();
            frame.AutoSize = AutoSizeModes.None;
            frame.BackgroundColor = Color.clear;
            frame.Position = Vector2.zero;
            panel.Components.Add(frame);

            // Re-add as the last child so it draws above the item lists.
            nativePanel.Components.Remove(panel);
            nativePanel.Components.Add(panel);
            panel.Enabled = false;
        }

        // The item text DFU shows in its info popup, plus what DFU only shows after a second click:
        // magic powers, or a potion recipe's ingredients.
        public static TextFile.Token[] BuildTokens(DaggerfallUnityItem item)
        {
            TextFile.Token[] tokens = ItemHelper.GetItemInfo(item, DaggerfallUnity.Instance.TextProvider);
            MacroHelper.ExpandMacros(ref tokens, item);

            List<TextFile.Token> all = new List<TextFile.Token>(tokens);
            if (item.IsPotionRecipe)
            {
                all.AddRange(item.GetMacroDataSource().PotionRecipeIngredients(TextFile.Formatting.JustifyCenter));
            }
            else if (item.legacyMagic != null)
            {
                TextFile.Token[] magic = DaggerfallUnity.Instance.TextProvider.GetRSCTokens(1016);
                MacroHelper.ExpandMacros(ref magic, item);
                all.AddRange(magic);
            }
            if (item.maxCondition > 0)
                InlineCondition(all, item);
            return all.ToArray();
        }

        // Replaces the "Condition: ..." row with a percentage after the item name: "Daedric Tanto (85%)".
        static void InlineCondition(List<TextFile.Token> all, DaggerfallUnityItem item)
        {
            for (int i = 0; i < all.Count; i++)
            {
                TextFile.Token t = all[i];
                if (t.formatting != TextFile.Formatting.Text || t.text == null || !t.text.TrimStart().StartsWith("Condition"))
                    continue;

                int end = i;
                while (end + 1 < all.Count && (all[end + 1].formatting == TextFile.Formatting.Text
                    || all[end + 1].formatting == TextFile.Formatting.TextHighlight
                    || all[end + 1].formatting == TextFile.Formatting.PositionPrefix))
                    end++;
                // Every row ends with a justify token (centre, for this text).
                if (end + 1 < all.Count && (all[end + 1].formatting == TextFile.Formatting.NewLine
                    || all[end + 1].formatting == TextFile.Formatting.JustifyLeft || all[end + 1].formatting == TextFile.Formatting.JustifyCenter))
                    end++;
                all.RemoveRange(i, end - i + 1);

                for (int j = 0; j < i && j < all.Count; j++)
                {
                    if (all[j].formatting == TextFile.Formatting.Text)
                    {
                        TextFile.Token name = all[j];
                        name.text = name.text.TrimEnd() + " (" + item.ConditionPercentage + "%)";
                        all[j] = name;
                        break;
                    }
                }
                return;
            }
        }

        // The tokens currently shown, so the text can be laid out again when the scale changes.
        public void Remember(TextFile.Token[] tokens)
        {
            lastTokens = tokens;
            FitWidth(tokens);
        }

        // The hover is only as wide as its text; long text wraps at the old fixed width.
        void FitWidth(TextFile.Token[] tokens)
        {
            if (label == null || tokens == null)
                return;
            float scale = Mathf.Max(0.05f, label.TextScale);
            float cap = 62f * appliedScale;
            float widest = 0f;
            float x = 0f;
            int tab = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                TextFile.Token t = tokens[i];
                switch (t.formatting)
                {
                    case TextFile.Formatting.Text:
                    case TextFile.Formatting.TextHighlight:
                        if (!string.IsNullOrEmpty(t.text))
                        {
                            float w = label.Font.CalculateTextWidth(t.text, Vector2.one);
                            widest = Mathf.Max(widest, x + w);
                            x += w;
                        }
                        break;
                    case TextFile.Formatting.PositionPrefix:
                        x = ((t.x != 0) ? t.x : (++tab) * 35f) / scale;
                        break;
                    case TextFile.Formatting.NewLine:
                    case TextFile.Formatting.JustifyLeft:
                    case TextFile.Formatting.JustifyCenter:
                    case TextFile.Formatting.Nothing:
                        x = 0f;
                        tab = 0;
                        break;
                }
            }
            label.MaxTextWidth = Mathf.RoundToInt(Mathf.Clamp(Mathf.Ceil(widest) + 4f, 16f, cap));
        }

        // Counteracts the window shrinking (windowFactor) so the hover stays the same size on screen.
        void ApplyScale(float m)
        {
            appliedScale = m;
            label.TextScale = 0.5f * m;
            label.Position = new Vector2(3f * m, 2f * m);
            for (int i = 0; i < MaxLines; i++)
            {
                if (lines[i] != null)
                    lines[i].TextScale = 0.5f * m;
            }
            if (lastTokens != null)
            {
                FitWidth(lastTokens);
                label.SetText(lastTokens);
            }
        }

        public void ClearCompare()
        {
            lineCount = 0;
            for (int i = 0; i < MaxLines; i++)
            {
                if (lines[i] != null)
                    lines[i].Enabled = false;
            }
        }

        void AddLine(string text, Color color)
        {
            if (lineCount >= MaxLines)
                return;
            lines[lineCount].Text = text;
            lines[lineCount].TextColor = color;
            lines[lineCount].Enabled = true;
            lineCount++;
        }

        // Adds "Name value (+delta)" in green when the delta is an improvement, red when it is worse.
        void AddDelta(string name, string value, string deltaText, int direction)
        {
            Color c = direction > 0 ? Better : (direction < 0 ? Worse : Same);
            AddLine(name + " " + value + " (" + deltaText + ")", c);
        }

        static string Signed(float v)
        {
            return v.ToString("+0.##;-0.##;0");
        }

        // Fills the comparison lines: the hovered item against whatever it would replace.
        public void SetItem(DaggerfallUnityItem item, PlayerEntity player)
        {
            ClearCompare();
            if (!Attached || item == null || player == null || item.IsEquipped)
                return;

            ItemEquipTable table = player.ItemEquipTable;
            EquipSlots slot = table.GetEquipSlot(item);
            if (slot == EquipSlots.None)
                return;
            DaggerfallUnityItem eq = table.GetItem(slot);
            if (eq == null && item.ItemGroup == ItemGroups.Weapons && item.TemplateIndex != (int)Weapons.Arrow)
            {
                // A one-handed weapon would go in the empty hand, so compare with the weapon in the other one.
                eq = table.GetItem(EquipSlots.RightHand);
                if (eq == null || eq.ItemGroup != ItemGroups.Weapons)
                    eq = table.GetItem(EquipSlots.LeftHand);
                if (eq != null && eq.ItemGroup != ItemGroups.Weapons)
                    eq = null;
            }
            if (eq == null || eq == item)
                return;

            string eqName = eq.LongName ?? string.Empty;
            if (eqName.Length > 24)
                eqName = eqName.Substring(0, 22) + "..";
            AddLine("vs " + eqName, Muted);

            if (item.ItemGroup == ItemGroups.Weapons && eq.ItemGroup == ItemGroups.Weapons && item.TemplateIndex != (int)Weapons.Arrow)
            {
                int nMin = item.GetBaseDamageMin() + item.GetWeaponMaterialModifier();
                int nMax = item.GetBaseDamageMax() + item.GetWeaponMaterialModifier();
                int eMin = eq.GetBaseDamageMin() + eq.GetWeaponMaterialModifier();
                int eMax = eq.GetBaseDamageMax() + eq.GetWeaponMaterialModifier();
                int dir = ((nMin + nMax) > (eMin + eMax)) ? 1 : (((nMin + nMax) < (eMin + eMax)) ? -1 : 0);
                AddDelta("Damage", nMin + "-" + nMax, Signed(nMin - eMin) + " / " + Signed(nMax - eMax), dir);
            }

            if (item.ItemGroup == ItemGroups.Armor && eq.ItemGroup == ItemGroups.Armor)
            {
                int n = item.GetMaterialArmorValue();
                int e = eq.GetMaterialArmorValue();
                AddDelta("Armor", n.ToString("+0;-0;0"), Signed(n - e), n > e ? 1 : (n < e ? -1 : 0));
            }

            float wd = item.weightInKg - eq.weightInKg;
            if (Mathf.Abs(wd) >= 0.005f)
                AddDelta("Weight", item.weightInKg.ToString("0.##") + " kg", Signed(wd), wd < 0 ? 1 : -1);

            if (item.IsEnchanted && !eq.IsEnchanted)
                AddLine("Gains magic powers", Better);
            else if (!item.IsEnchanted && eq.IsEnchanted)
                AddLine("Loses magic powers", Worse);
        }

        public void Place(bool show, float windowFactor)
        {
            if (panel == null)
                return;

            float m = FontBoost / Mathf.Max(0.4f, windowFactor);
            if (Mathf.Abs(m - appliedScale) > 0.01f)
                ApplyScale(m);

            Vector2 mouse = nativePanel.ScaledMousePosition;
            if (!show || mouse.x < 0 || mouse.y < 0 || label.Size.y < 1)
            {
                panel.Enabled = false;
                return;
            }

            float width = label.Size.x;
            float y = (2 + 3) * m + label.Size.y;
            for (int i = 0; i < lineCount; i++)
            {
                lines[i].Position = new Vector2(3f * m, y);
                y += lines[i].TextHeight + m;
                width = Mathf.Max(width, lines[i].TextWidth);
            }
            width += 8f * m;
            float height = y + 2f * m;

            float x = mouse.x + 8;
            float py = mouse.y + 10;
            if (x + width > 318)
                x = mouse.x - width - 4;
            if (py + height > 198)
                py = 198 - height;
            if (x < 2)
                x = 2;

            panel.Size = new Vector2(width, height);
            panel.Position = new Vector2(x, py);
            frame.Size = new Vector2(width, height);
            frame.FrameScale = Mathf.Max(1f, Mathf.Round(nativePanel.LocalScale.x * 0.5f));
            panel.Enabled = true;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Inventory window: junk and lock modes, quick loot, filter settings
    // ---------------------------------------------------------------------------------------

    public class JunkInventoryWindow : DaggerfallInventoryWindow
    {
        // Stock button rect tops, in column order: wagon, info, equip, remove, use, gold.
        static readonly int[] StockY = { 14, 36, 58, 80, 103, 126 };
        const int ColumnX = 226;
        const int FirstY = 13;
        const int Pitch = 18;
        const int RecessBottom = 176;

        const int ModeNone = 0;
        const int ModeJunk = 1;
        const int ModeLock = 2;

        Rect junkButtonRect = new Rect(ColumnX, 0, 31, 14);
        Rect lockButtonRect = new Rect(ColumnX, 0, 31, 14);
        Rect filterButtonRect = new Rect(ColumnX, 0, 31, 14);
        Button junkButton;
        Button lockButton;
        Button filterButton;
        Button lootPlate;
        Texture2D blankNotSelected;
        Texture2D blankSelected;
        bool layoutOk;
        int extraMode;
        bool changingModeInternally;
        readonly InfoTip tip = new InfoTip();

        public JunkInventoryWindow(IUserInterfaceManager uiManager, DaggerfallBaseWindow previous = null)
            : base(uiManager, previous)
        {
        }

        #region Layout

        protected override void LoadTextures()
        {
            base.LoadTextures();

            layoutOk = false;
            try
            {
                if (baseTexture.width != 320 || baseTexture.height != 200 || goldTexture.width != 320 || goldTexture.height != 200)
                    return;

                PixelBuffer orig = new PixelBuffer(baseTexture);
                PixelBuffer gold = new PixelBuffer(goldTexture);

                // Blank slabs: the INFO slab with its lettering wiped, in normal and gold versions.
                PixelBuffer blankBase = orig.Clone();
                JunkUi.WipeSlabText(blankBase, ColumnX, StockY[1]);
                PixelBuffer blankGold = gold.Clone();
                JunkUi.WipeSlabText(blankGold, ColumnX, StockY[1]);

                PixelBuffer b = orig.Clone();

                // The info panel no longer lives under the column, so the recessed column can grow down to
                // y = RecessBottom. Rows 29..34 sit between two stock slabs and are plain stone.
                for (int y = 11; y < RecessBottom; y++)
                    for (int x = 223; x <= 259; x++)
                        b.Set(x, y, orig.Get(x, 29 + ((y - 11) % 6)));
                for (int y = 143; y < RecessBottom; y++)
                {
                    int srcY = 120 + ((y - 143) % 12);
                    b.Set(222, y, orig.Get(222, srcY));
                    b.Set(260, y, orig.Get(260, srcY));
                }
                for (int x = 222; x <= 260; x++)
                    b.Set(x, RecessBottom, orig.Get(x, 143));

                // Slot order: wagon, info, equip, JUNK, LOCK, remove, use, gold, FILTER.
                // >= 0: a stock slab; -1/-2/-3: blank slabs for junk, lock and filter.
                int[] slotSource = { 0, 1, 2, -1, -2, 3, 4, 5, -3 };
                Rect[] rects = new Rect[6];
                for (int slot = 0; slot < slotSource.Length; slot++)
                {
                    int y = FirstY + slot * Pitch;
                    int src = slotSource[slot];
                    Rect rect = new Rect(ColumnX, y, 31, 14);
                    if (src < 0)
                    {
                        JunkUi.PasteSlab(blankBase, b, ColumnX, StockY[1], ColumnX, y);
                        if (src == -1)
                            junkButtonRect = rect;
                        else if (src == -2)
                            lockButtonRect = rect;
                        else
                            filterButtonRect = rect;
                    }
                    else
                    {
                        JunkUi.PasteSlab(orig, b, ColumnX, StockY[src], ColumnX, y);
                        rects[src] = rect;
                    }
                }

                blankNotSelected = blankBase.Sub(ColumnX, StockY[1], 31, 14);
                blankSelected = blankGold.Sub(ColumnX, StockY[1], 31, 14);

                // Buttons are created from these rects in SetupActionButtons().
                wagonButtonRect = rects[0];
                infoButtonRect = rects[1];
                equipButtonRect = rects[2];
                removeButtonRect = rects[3];
                useButtonRect = rects[4];
                goldButtonRect = rects[5];

                baseTexture = b.ToTexture();
                layoutOk = true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[JunkLoot] Could not re-layout the inventory buttons: " + ex);
            }
        }

        protected override void SetupActionButtons()
        {
            base.SetupActionButtons();

            if (layoutOk)
            {
                junkButton = JunkUi.SlabButton(NativePanel, junkButtonRect, blankNotSelected, "JUNK");
                lockButton = JunkUi.SlabButton(NativePanel, lockButtonRect, blankNotSelected, "LOCK");
                filterButton = JunkUi.SlabButton(NativePanel, filterButtonRect, blankNotSelected, "FILTER");
            }
            else
            {
                // Fallback when the stock art is replaced by a texture pack: plates along the backpack icon.
                junkButton = JunkUi.MakePlate(localTargetIconPanel, new Rect(1, 23, 17, 10), "JUNK");
                lockButton = JunkUi.MakePlate(localTargetIconPanel, new Rect(19, 23, 17, 10), "LOCK");
                filterButton = JunkUi.MakePlate(localTargetIconPanel, new Rect(37, 23, 17, 10), "FILTER");
            }

            junkButton.OnMouseClick += delegate (BaseScreenComponent sender, Vector2 position) { ToggleExtraMode(ModeJunk); };
            junkButton.OnRightMouseClick += OpenSettings_OnClick;
            lockButton.OnMouseClick += delegate (BaseScreenComponent sender, Vector2 position) { ToggleExtraMode(ModeLock); };
            lockButton.OnRightMouseClick += OpenSettings_OnClick;
            filterButton.OnMouseClick += OpenSettings_OnClick;
        }

        protected override void Setup()
        {
            base.Setup();

            lootPlate = JunkUi.MakePlate(remoteTargetIconPanel, new Rect(1, 23, 53, 10), "QUICK LOOT");
            lootPlate.OnMouseClick += LootPlate_OnMouseClick;
            lootPlate.OnRightMouseClick += OpenSettings_OnClick;
            lootPlate.Enabled = false;

            ApplyHotkeys();

            localItemListScroller.ForegroundAnimationHandler = ForegroundHandler;
            remoteItemListScroller.ForegroundAnimationHandler = ForegroundHandler;
            localItemListScroller.Items = localItemsFiltered;
            remoteItemListScroller.Items = remoteItemsFiltered;

            tip.Attach(NativePanel, itemInfoPanel, itemInfoPanelLabel);
            if (tip.Attached)
            {
                // Our info panel replaces DFU's name tooltips on items.
                JunkUi.StripToolTips(localItemListScroller);
                JunkUi.StripToolTips(remoteItemListScroller);
                paperDoll.ToolTip = null;
                for (int i = 0; i < accessoryButtons.Length; i++)
                {
                    if (accessoryButtons[i] != null)
                        accessoryButtons[i].ToolTip = null;
                }
            }
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

        protected override void GoldButton_OnMouseEnter(BaseScreenComponent sender)
        {
            tip.ClearCompare();
            base.GoldButton_OnMouseEnter(sender);
        }

        void ApplyHotkeys()
        {
            JunkUi.BindKey(junkButton, JunkStore.KeyJunk, "Junk mode: click items to tag them as always-sell. Right-click: settings.", defaultToolTip);
            JunkUi.BindKey(lockButton, JunkStore.KeyLock, "Lock mode: click items to lock them so they are never sold. Right-click: settings.", defaultToolTip);
            JunkUi.BindKey(filterButton, JunkStore.KeyFilter, "Loot filter, sell filter, junk list and key settings.", defaultToolTip);
            JunkUi.BindKey(lootPlate, JunkStore.KeyLoot, "Loot everything the loot filter allows, then close. Right-click: settings.", defaultToolTip);
        }

        Texture2D[] ForegroundHandler(DaggerfallUnityItem item)
        {
            Texture2D[] overlay = JunkUi.OverlayFor(item);
            if (overlay != null)
                return overlay;
            return item.IsEnchanted ? magicAnimation.animatedTextures : null;
        }

        public override void OnPush()
        {
            base.OnPush();
            extraMode = ModeNone;
            UpdateModeButtons();
            CorpseCleaner.OpenLoot = lootTarget;
        }

        public override void OnPop()
        {
            base.OnPop();
            CorpseCleaner.OpenLoot = null;
        }

        public override void Update()
        {
            base.Update();
            if (lootPlate != null)
                lootPlate.Enabled = CanQuickLoot();
            if (itemInfoPanel != null)
            {
                bool hover = JunkUi.IsHoveringItem(localItemListScroller, remoteItemListScroller, paperDoll, accessoryButtons, goldButton);
                tip.Place(hover, WindowScaler.FactorFor(this));
            }
        }

        #endregion

        #region Junk and lock modes

        void UpdateModeButtons()
        {
            if (!layoutOk)
                return;
            if (junkButton != null)
                junkButton.BackgroundTexture = (extraMode == ModeJunk) ? blankSelected : blankNotSelected;
            if (lockButton != null)
                lockButton.BackgroundTexture = (extraMode == ModeLock) ? blankSelected : blankNotSelected;
        }

        protected override void SelectActionMode(ActionModes mode)
        {
            base.SelectActionMode(mode);
            if (!changingModeInternally)
                extraMode = ModeNone;
            UpdateModeButtons();
        }

        void ToggleExtraMode(int mode)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            if (extraMode == mode)
            {
                extraMode = ModeNone;
                SelectActionMode(lootTarget != null ? ActionModes.Remove : ActionModes.Equip);
                return;
            }

            changingModeInternally = true;
            SelectActionMode(ActionModes.Select);
            changingModeInternally = false;
            extraMode = mode;
            UpdateModeButtons();
            JunkUi.Hud(mode == ModeJunk ? "Junk mode: click items to tag or untag them." : "Lock mode: click items to lock or unlock them.");
        }

        protected override void LocalItemListScroller_OnItemLeftClick(DaggerfallUnityItem item)
        {
            if (extraMode != ModeNone)
                ApplyExtraMode(item);
            else
                base.LocalItemListScroller_OnItemLeftClick(item);
        }

        protected override void RemoteItemListScroller_OnItemLeftClick(DaggerfallUnityItem item)
        {
            if (extraMode != ModeNone)
                ApplyExtraMode(item);
            else
                base.RemoteItemListScroller_OnItemLeftClick(item);
        }

        void ApplyExtraMode(DaggerfallUnityItem item)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            JunkUi.Hud(extraMode == ModeJunk ? JunkStore.ToggleJunk(item) : JunkStore.ToggleLock(item));
            localItemListScroller.Items = localItemsFiltered;
            remoteItemListScroller.Items = remoteItemsFiltered;
        }

        #endregion

        #region Quick loot

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

        #endregion

        #region Settings

        void OpenSettings_OnClick(BaseScreenComponent sender, Vector2 position)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            uiManager.PushWindow(new FilterSettingsWindow(uiManager, this, delegate ()
            {
                ApplyHotkeys();
                localItemListScroller.Items = localItemsFiltered;
                remoteItemListScroller.Items = remoteItemsFiltered;
            }));
        }

        #endregion
    }

    // ---------------------------------------------------------------------------------------
    // Trade window: junk and lock modes, quick sell and junk pre-selection
    // ---------------------------------------------------------------------------------------

    public class JunkTradeWindow : DaggerfallTradeWindow
    {
        const int ModeNone = 0;
        const int ModeJunk = 1;
        const int ModeLock = 2;

        Button junkButton;
        Button lockButton;
        Button quickSellButton;
        Button filterKeyButton;
        Button stockSelectButton;
        Texture2D blankNotSelected;
        Texture2D blankSelected;
        Texture2D selectNotSelectedCopy;
        bool slabs;
        int extraMode;
        bool changingModeInternally;
        readonly InfoTip tip = new InfoTip();

        // No default parameter values here: the runtime compiler can't emit enum defaults, and DFU always passes all four.
        public JunkTradeWindow(IUserInterfaceManager uiManager, DaggerfallBaseWindow previous, WindowModes windowMode, IGuild guild)
            : base(uiManager, previous, windowMode, guild)
        {
        }

        bool SellMode
        {
            get { return WindowMode == WindowModes.Sell || WindowMode == WindowModes.SellMagic; }
        }

        protected override void Setup()
        {
            base.Setup();

            if (SellMode)
            {
                try
                {
                    AddSellButtons();
                }
                catch (Exception ex)
                {
                    Debug.LogError("[JunkLoot] Could not add the sell buttons: " + ex);
                }
            }

            localItemListScroller.ForegroundAnimationHandler = ForegroundHandler;
            localItemListScroller.Items = localItemsFiltered;

            tip.Attach(NativePanel, itemInfoPanel, itemInfoPanelLabel);
            if (tip.Attached)
            {
                JunkUi.StripToolTips(localItemListScroller);
                JunkUi.StripToolTips(remoteItemListScroller);
                paperDoll.ToolTip = null;
                for (int i = 0; i < accessoryButtons.Length; i++)
                {
                    if (accessoryButtons[i] != null)
                        accessoryButtons[i].ToolTip = null;
                }
            }
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

        Texture2D[] ForegroundHandler(DaggerfallUnityItem item)
        {
            Texture2D[] overlay = JunkUi.OverlayFor(item);
            if (overlay != null)
                return overlay;
            return item.IsEnchanted ? magicAnimation.animatedTextures : null;
        }

        // The panel holding the left button column is private in DFU 1.1.1, so find it by its stock rect.
        Panel FindActionButtonsPanel()
        {
            Vector2 pos = new Vector2(actionButtonsPanelRect.x, actionButtonsPanelRect.y);
            Vector2 size = new Vector2(actionButtonsPanelRect.width, actionButtonsPanelRect.height);
            for (int i = 0; i < NativePanel.Components.Count; i++)
            {
                Panel p = NativePanel.Components[i] as Panel;
                if (p != null && !(p is Button) && p.Position == pos && p.Size == size)
                    return p;
            }
            return null;
        }

        static Button FindButtonAt(Panel panel, float x, float y)
        {
            for (int i = 0; i < panel.Components.Count; i++)
            {
                Button b = panel.Components[i] as Button;
                if (b != null && b.Position.x == x && b.Position.y == y)
                    return b;
            }
            return null;
        }

        void AddSellButtons()
        {
            Panel actionPanel = FindActionButtonsPanel();
            if (actionPanel == null)
            {
                Debug.LogError("[JunkLoot] Could not find the vendor button column.");
                return;
            }

            // The left column is 39x190; with the info panel floating, the gap between SELECT and SELL is free.
            Rect junkRect = new Rect(4, 68, 31, 14);
            Rect lockRect = new Rect(4, 86, 31, 14);
            Rect sellRect = new Rect(4, 104, 31, 14);

            Texture2D tex = actionPanel.BackgroundTexture;
            slabs = false;
            if (tex != null && tex.width == 39 && tex.height == 190)
            {
                PixelBuffer b = new PixelBuffer(tex);
                selectNotSelectedCopy = b.Sub(4, 48, 31, 14);
                PixelBuffer blank = b.Clone();
                JunkUi.WipeSlabText(blank, 4, 26);
                JunkUi.PasteSlab(blank, b, 4, 26, 4, 68);
                JunkUi.PasteSlab(blank, b, 4, 26, 4, 86);
                JunkUi.PasteSlab(blank, b, 4, 26, 4, 104);
                blankNotSelected = blank.Sub(4, 26, 31, 14);

                Texture2D goldTex = ImageReader.GetTexture("INVE11I0.IMG");
                if (goldTex != null && goldTex.width == 39 && goldTex.height == 190)
                {
                    PixelBuffer g = new PixelBuffer(goldTex);
                    JunkUi.WipeSlabText(g, 4, 26);
                    blankSelected = g.Sub(4, 26, 31, 14);
                }
                else
                {
                    blankSelected = blankNotSelected;
                }

                actionPanel.BackgroundTexture = b.ToTexture();
                slabs = true;
                stockSelectButton = FindButtonAt(actionPanel, 4, 48);
            }

            if (slabs)
            {
                junkButton = JunkUi.SlabButton(actionPanel, junkRect, blankNotSelected, "JUNK");
                lockButton = JunkUi.SlabButton(actionPanel, lockRect, blankNotSelected, "LOCK");
                quickSellButton = JunkUi.SlabButton(actionPanel, sellRect, blankNotSelected, "Q.SELL");
            }
            else
            {
                junkButton = JunkUi.MakePlate(actionPanel, junkRect, "JUNK");
                lockButton = JunkUi.MakePlate(actionPanel, lockRect, "LOCK");
                quickSellButton = JunkUi.MakePlate(actionPanel, sellRect, "Q.SELL");
            }

            junkButton.OnMouseClick += delegate (BaseScreenComponent sender, Vector2 position) { ToggleExtraMode(ModeJunk); };
            junkButton.OnRightMouseClick += FilterButton_OnMouseClick;
            lockButton.OnMouseClick += delegate (BaseScreenComponent sender, Vector2 position) { ToggleExtraMode(ModeLock); };
            lockButton.OnRightMouseClick += FilterButton_OnMouseClick;
            quickSellButton.OnMouseClick += QuickSellButton_OnMouseClick;
            quickSellButton.OnRightMouseClick += FilterButton_OnMouseClick;

            // Invisible button that only carries the filter settings hotkey.
            filterKeyButton = DaggerfallUI.AddButton(new Rect(0, 0, 1, 1), actionPanel);
            filterKeyButton.OnMouseClick += FilterButton_OnMouseClick;

            ApplyHotkeys();
        }

        void ApplyHotkeys()
        {
            JunkUi.BindKey(junkButton, JunkStore.KeyJunk, "Junk mode: click items to tag them as always-sell. Right-click: settings.", defaultToolTip);
            JunkUi.BindKey(lockButton, JunkStore.KeyLock, "Lock mode: click items to lock them so they are never sold. Right-click: settings.", defaultToolTip);
            JunkUi.BindKey(quickSellButton, JunkStore.KeySell, "Move everything the sell filter and junk list allow to the basket, then confirm. Right-click: settings.", defaultToolTip);
            if (filterKeyButton != null)
                filterKeyButton.Hotkey = JunkStore.Hotkey(JunkStore.KeyFilter);
        }

        void UpdateModeButtons()
        {
            if (slabs)
            {
                junkButton.BackgroundTexture = (extraMode == ModeJunk) ? blankSelected : blankNotSelected;
                lockButton.BackgroundTexture = (extraMode == ModeLock) ? blankSelected : blankNotSelected;
                // While a mode is on, the stock SELECT button must not look selected as well.
                if (extraMode != ModeNone && stockSelectButton != null && selectNotSelectedCopy != null)
                    stockSelectButton.BackgroundTexture = selectNotSelectedCopy;
            }
        }

        protected override void SelectActionMode(ActionModes mode)
        {
            base.SelectActionMode(mode);
            if (!changingModeInternally)
                extraMode = ModeNone;
            if (junkButton != null)
                UpdateModeButtons();
        }

        void ToggleExtraMode(int mode)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            changingModeInternally = true;
            SelectActionMode(ActionModes.Select);
            changingModeInternally = false;
            extraMode = (extraMode == mode) ? ModeNone : mode;
            UpdateModeButtons();
            if (extraMode == ModeJunk)
                JunkUi.Hud("Junk mode: click items to tag or untag them.");
            else if (extraMode == ModeLock)
                JunkUi.Hud("Lock mode: click items to lock or unlock them.");
        }

        protected override void LocalItemListScroller_OnItemLeftClick(DaggerfallUnityItem item)
        {
            if (extraMode != ModeNone)
            {
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
                JunkUi.Hud(extraMode == ModeJunk ? JunkStore.ToggleJunk(item) : JunkStore.ToggleLock(item));
                localItemListScroller.Items = localItemsFiltered;
            }
            else
            {
                base.LocalItemListScroller_OnItemLeftClick(item);
            }
        }

        // Locked items can't be moved into the basket by hand either.
        protected override void LocalItemListScroller_OnItemClick(DaggerfallUnityItem item, ActionModes actionMode)
        {
            if (SellMode && JunkStore.IsLocked(item) && (actionMode == ActionModes.Select || actionMode == ActionModes.Remove))
            {
                JunkUi.Hud("Locked: " + item.LongName);
                return;
            }
            base.LocalItemListScroller_OnItemClick(item, actionMode);
        }

        public override void Update()
        {
            base.Update();
            if (itemInfoPanel != null)
            {
                bool hover = JunkUi.IsHoveringItem(localItemListScroller, remoteItemListScroller, paperDoll, accessoryButtons, null);
                tip.Place(hover, WindowScaler.FactorFor(this));
            }
        }

        public override void OnPush()
        {
            base.OnPush();
            if (SellMode && JunkStore.S.preselectJunkOnSell)
                PreselectJunk();
        }

        bool AcceptedHere(DaggerfallUnityItem item)
        {
            if (WindowMode == WindowModes.SellMagic)
                return item.IsEnchanted;
            return ItemTypesAccepted.Contains(item.ItemGroup);
        }

        // Items that may be sold automatically: not equipped, accepted by this shop, and not locked.
        List<DaggerfallUnityItem> SnapshotLocalItems()
        {
            List<DaggerfallUnityItem> list = new List<DaggerfallUnityItem>();
            if (localItems == null)
                return list;
            for (int i = 0; i < localItems.Count; i++)
            {
                DaggerfallUnityItem item = localItems.GetItem(i);
                if (item != null && !item.IsEquipped && !JunkStore.IsLocked(item) && AcceptedHere(item))
                    list.Add(item);
            }
            return list;
        }

        void PreselectJunk()
        {
            if (localItems == null || remoteItems == null)
                return;

            int moved = 0;
            List<DaggerfallUnityItem> items = SnapshotLocalItems();
            for (int i = 0; i < items.Count; i++)
            {
                if (JunkStore.IsJunk(items[i]))
                {
                    remoteItems.Transfer(items[i], localItems);
                    moved++;
                }
            }

            if (moved > 0)
            {
                Refresh(false);
                JunkUi.Hud(moved + " junk item(s) added to the sale.");
            }
        }

        void DoQuickSell()
        {
            if (!SellMode || localItems == null || remoteItems == null)
                return;

            FilterProfile profile = JunkStore.S.sell;
            int moved = 0;
            List<DaggerfallUnityItem> items = SnapshotLocalItems();
            for (int i = 0; i < items.Count; i++)
            {
                if (JunkStore.IsJunk(items[i]) || ItemRules.PassesSell(items[i], profile))
                {
                    remoteItems.Transfer(items[i], localItems);
                    moved++;
                }
            }

            Refresh(false);
            if (moved == 0)
            {
                JunkUi.Hud("Nothing to sell with the current sell filter.");
                return;
            }
            ShowTradePopup();
        }

        void QuickSellButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            DoQuickSell();
        }

        void FilterButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            uiManager.PushWindow(new FilterSettingsWindow(uiManager, this, delegate ()
            {
                ApplyHotkeys();
                localItemListScroller.Items = localItemsFiltered;
            }));
        }
    }
}
