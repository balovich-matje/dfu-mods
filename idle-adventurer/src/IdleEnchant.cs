// Idle Adventurer - the enchanter: filled soul gems and gold buy a new enchantment for worn gear, or roll one anew.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace IdleAdventurer
{
    public class IdleEnchantWindow : DaggerfallPopupWindow
    {
        const int BtnAdd = 1, BtnReroll = 2, BtnLess = 3, BtnMore = 4, BtnBack = 5;

        EnchantCanvas canvas;
        int row;
        int slot;
        string note = "";

        public IdleEnchantWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new EnchantCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        void Done(string text)
        {
            note = text;
            DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
            IdleStore.Save();
        }

        public override void Update()
        {
            base.Update();
            IdleWorld w = IdleStore.W;
            if (canvas == null || w == null)
                return;
            IdleSave s = w.s;
            canvas.Size = new Vector2(Screen.width, Screen.height);

            int click = canvas.Buttons.Clicked();
            if (click >= 0)
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            if (click < 0 && Input.GetMouseButtonDown(0))
            {
                int cell = canvas.RowAt(IdleUi.Mouse);
                if (cell >= 0)
                    row = cell;
                int at = canvas.SlotAt(IdleUi.Mouse);
                if (at >= 0)
                    slot = at;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
                row = Mathf.Min(IdleData.SlotCount - 1, row + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow))
                row = Mathf.Max(0, row - 1);
            if (Input.GetKeyDown(KeyCode.RightArrow))
                slot = Mathf.Min(IdleData.MaxEnchants - 1, slot + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                slot = Mathf.Max(0, slot - 1);
            DaggerfallUnityItem item = w.gear[IdleSmithWindow.SlotOfRow(row)];

            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.Return) || click == BtnAdd)
            {
                string why = IdleSim.CannotEnchant(s, item);
                if (why != null)
                    note = why;
                else
                {
                    IdleSim.Enchant(w, item);
                    Done("Now " + IdleSim.Name(item) + ".");
                }
            }
            if (Input.GetKeyDown(KeyCode.R) || click == BtnReroll)
            {
                string why = IdleSim.CannotReroll(s, item, slot);
                if (why != null)
                    note = why;
                else
                {
                    IdleSim.Reroll(w, item, slot);
                    Done("Rolled anew: " + string.Format(IdleData.AffixText[IdleSim.EnchantKind(item, slot)], IdleSim.EnchantValue(item, slot)) + ".");
                }
            }
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || click == BtnMore)
                s.soulGemsToCarry = Mathf.Min(20, s.soulGemsToCarry + 1);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || click == BtnLess)
                s.soulGemsToCarry = Mathf.Max(0, s.soulGemsToCarry - 1);
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            IdleClock.Tick();
        }

        class EnchantCanvas : BaseScreenComponent
        {
            public IdleEnchantWindow Owner;
            public readonly IdleButtons Buttons = new IdleButtons();
            readonly List<Rect> rows = new List<Rect>();
            readonly List<Rect> slotRects = new List<Rect>();

            public int RowAt(Vector2 mouse)
            {
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Contains(mouse))
                        return i;
                return -1;
            }

            public int SlotAt(Vector2 mouse)
            {
                for (int i = 0; i < slotRects.Count; i++)
                    if (slotRects[i].Contains(mouse))
                        return i;
                return -1;
            }

            public override void Draw()
            {
                base.Draw();
                IdleWorld w = IdleStore.W;
                if (w == null || Owner == null)
                    return;
                IdleSave s = w.s;
                float W = Screen.width;
                float H = Screen.height;
                IdleUi.Box(new Rect(0, 0, W, H), Color.black);

                float width = Mathf.Round(Mathf.Min(W * 0.92f, H * 1.7f));
                float height = Mathf.Round(H * 0.9f);
                Rect r = new Rect(Mathf.Round((W - width) / 2f), Mathf.Round((H - height) / 2f), width, height);
                IdleUi.Frame(r, IdleUi.Fill);

                float line = Mathf.Round(H * 0.032f);
                float sc = IdleUi.Scale(line);
                float pad = Mathf.Round(H * 0.025f);
                float tall = Mathf.Round(line * 1.25f);
                float top = r.y + pad;
                float bottom = r.yMax - pad - tall;
                float listW = Mathf.Round(width * 0.52f);
                float x = r.x + pad;
                float cardX = x + listW + pad;
                float cardW = r.xMax - pad - cardX;

                float y = top;
                IdleUi.Text("Enchanter", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.6f;
                IdleUi.Text("Gold " + s.gold + "    Spent here " + s.enchantGold, x, y, sc, IdleUi.White);
                y += line * 1.1f;
                string souls = "";
                for (int t = IdleData.SoulTiers - 1; t >= 0; t--)
                    if (s.souls[t] > 0)
                        souls += (souls.Length > 0 ? ", " : "") + IdleData.SoulNames[t] + " " + s.souls[t];
                IdleUi.Text("Filled soul gems: " + (souls.Length > 0 ? souls : "none") + "    Empty: " + s.soulGemsEmpty + ", carrying " + s.soulGemsToCarry + " a run (+/-)",
                    x, y, sc * 0.85f, IdleUi.Muted);
                y += line * 1.3f;

                rows.Clear();
                float rowTall = Mathf.Round(line * 1.3f);
                for (int i = 0; i < IdleData.SlotCount; i++)
                {
                    int gearSlot = IdleSmithWindow.SlotOfRow(i);
                    DaggerfallUnityItem item = w.gear[gearSlot];
                    Rect cell = new Rect(x, y, listW, rowTall - 4f);
                    rows.Add(cell);
                    bool selected = Owner.row == i;
                    Color fill = selected ? new Color(0.72f, 0.58f, 0.18f, 0.32f) : cell.Contains(IdleUi.Mouse) ? new Color(1f, 1f, 1f, 0.09f) : new Color(1f, 1f, 1f, 0.04f);
                    IdleUi.Box(cell, fill);
                    if (selected)
                        IdleUi.Box(new Rect(cell.x, cell.yMax - 2f, cell.width, 2f), IdleUi.Line);
                    float ty = cell.y + (cell.height - line * 0.7f) / 2f;
                    if (item == null)
                    {
                        IdleUi.Text(IdleData.SlotNames[gearSlot] + ": nothing", cell.x + line * 0.4f, ty, sc * 0.9f, IdleUi.Muted);
                        y += rowTall;
                        continue;
                    }
                    string right = IdleSim.Enchants(item) + "/" + IdleData.MaxEnchants;
                    float rightX = cell.xMax - IdleUi.Width(right, sc * 0.85f) - line * 0.4f;
                    string name = IdleSim.Name(item);
                    while (name.Length > 4 && cell.x + line * 0.4f + IdleUi.Width(name, sc * 0.9f) > rightX - line)
                        name = name.Substring(0, name.Length - 4) + "..";
                    IdleUi.Text(name, cell.x + line * 0.4f, ty, sc * 0.9f, IdleUi.EnchantColor(IdleSim.Enchants(item)));
                    IdleUi.Text(right, rightX, ty, sc * 0.85f, IdleUi.Muted);
                    y += rowTall;
                }
                y += line * 0.4f;
                if (Owner.note.Length > 0)
                    Wrap(Owner.note, x, y, listW, sc * 0.9f, line * 0.95f, IdleUi.Gold);

                // ---- the selected piece
                slotRects.Clear();
                DaggerfallUnityItem sel = w.gear[IdleSmithWindow.SlotOfRow(Mathf.Clamp(Owner.row, 0, IdleData.SlotCount - 1))];
                Rect card = new Rect(cardX, top, cardW, bottom - line * 0.5f - top);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cw = card.width - line;
                float cy = card.y + line * 0.4f;
                if (sel == null)
                {
                    IdleUi.Text("Nothing worn here.", cx, cy, sc, IdleUi.Muted);
                }
                else
                {
                    cy = Wrap(IdleSim.Name(sel), cx, cy, cw, sc * 1.1f, line * 1.2f, IdleUi.Gold);
                    cy += line * 0.4f;
                    for (int k = 0; k < IdleData.MaxEnchants; k++)
                    {
                        Rect cell = new Rect(cx - line * 0.2f, cy, cw + line * 0.4f, line * 1.15f);
                        slotRects.Add(cell);
                        if (Owner.slot == k)
                            IdleUi.Box(cell, new Color(0.72f, 0.58f, 0.18f, 0.32f));
                        int kind = IdleSim.EnchantKind(sel, k);
                        string text = (kind < 0) ? "Enchantment " + (k + 1) + ": free"
                            : "Enchantment " + (k + 1) + ": " + IdleData.AffixWords[kind] + ", " + string.Format(IdleData.AffixText[kind], IdleSim.EnchantValue(sel, k));
                        IdleUi.Text(text, cx, cy + line * 0.2f, sc * 0.85f, (kind < 0) ? IdleUi.Muted : IdleUi.EnchantColor(IdleSim.Enchants(sel)));
                        cy += line * 1.2f;
                    }
                    cy += line * 0.4f;

                    int tier = IdleSim.BestSoul(s);
                    string soul = (tier < 0) ? "no filled soul gem" : "a " + IdleData.SoulNames[tier].ToLower() + " soul (" + IdleData.SoulPower[tier] + "% strength)";
                    if (IdleSim.Enchants(sel) < IdleData.MaxEnchants)
                    {
                        string why = IdleSim.CannotEnchant(s, sel);
                        cy = Wrap("Enchant (A or Enter): a new enchantment the piece does not have yet, using " + soul
                            + ((tier >= 0) ? " and " + IdleData.EnchantGold(IdleSim.Enchants(sel), tier) + " gold." : "."),
                            cx, cy, cw, sc * 0.85f, line * 0.9f, (why == null) ? IdleUi.White : IdleUi.Bad);
                    }
                    if (IdleSim.EnchantKind(sel, Owner.slot) >= 0)
                    {
                        string why = IdleSim.CannotReroll(s, sel, Owner.slot);
                        cy = Wrap("Roll anew (R): replaces enchantment " + (Owner.slot + 1) + " with a random other one, using " + soul
                            + ((tier >= 0) ? " and " + IdleData.RerollGold(tier) + " gold." : "."),
                            cx, cy, cw, sc * 0.85f, line * 0.9f, (why == null) ? IdleUi.White : IdleUi.Bad);
                    }
                    cy += line * 0.5f;
                    cy = Wrap("Strength grows with the deepest floor reached; the soul scales it: petty 70% to grand 165%. Souls come from creatures, by their level: under 5 petty, then every 5 levels a tier up to grand at 20.",
                        cx, cy, cw, sc * 0.8f, line * 0.85f, IdleUi.Muted);
                    cy += line * 0.3f;
                    Wrap(s.noAutoSmith ? "Auto-smith is off: nothing is enchanted on its own." : "Auto-smith is on: coming home, the hero enchants free slots with the best soul while it costs at most half the gold. Rolling anew is up to you.",
                        cx, cy, cw, sc * 0.8f, line * 0.85f, IdleUi.Muted);
                }

                float gap = Mathf.Round(line * 0.3f);
                float bx = x;
                Buttons.Begin();
                bx += Buttons.Add(BtnAdd, "Enchant (A)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnReroll, "Roll anew (R)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnLess, "Soul gems -", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnMore, "Soul gems +", bx, bottom, tall, false) + gap;
                Buttons.Add(BtnBack, "Back (Esc)", bx, bottom, tall, false);
            }

            static float Wrap(string s, float x, float y, float width, float scale, float lineHeight, Color c)
            {
                if (s.Length == 0)
                    return y + lineHeight * 0.6f;
                string[] words = s.Split(' ');
                string row = "";
                for (int i = 0; i < words.Length; i++)
                {
                    string next = (row.Length == 0) ? words[i] : row + " " + words[i];
                    if (row.Length > 0 && IdleUi.Width(next, scale) > width)
                    {
                        IdleUi.Text(row, x, y, scale, c);
                        y += lineHeight;
                        row = words[i];
                    }
                    else
                    {
                        row = next;
                    }
                }
                if (row.Length > 0)
                {
                    IdleUi.Text(row, x, y, scale, c);
                    y += lineHeight;
                }
                return y;
            }
        }
    }
}
