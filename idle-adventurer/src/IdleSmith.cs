// Idle Adventurer - the smith: reinforce worn gear (+1 to +25) and reforge it into the next metal.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace IdleAdventurer
{
    public class IdleSmithWindow : DaggerfallPopupWindow
    {
        const int BtnReinforce = 1, BtnReforge = 2, BtnAuto = 3, BtnBack = 4;

        SmithCanvas canvas;
        int row;
        string note = "";

        public IdleSmithWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new SmithCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        // Gear rows, weapon first.
        public static int SlotOfRow(int r)
        {
            return (r == 0) ? IdleData.SlotWeapon : r - 1;
        }

        void Reinforce(IdleWorld w)
        {
            DaggerfallUnityItem item = w.gear[SlotOfRow(row)];
            string why = IdleSim.CannotReinforce(w.s, item);
            if (why != null)
            {
                note = why;
                return;
            }
            IdleSim.Reinforce(w, item);
            note = "Now " + IdleSim.Name(item) + ".";
            DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
            IdleStore.Save();
        }

        void Reforge(IdleWorld w)
        {
            DaggerfallUnityItem item = w.gear[SlotOfRow(row)];
            string why = IdleSim.CannotReforge(w.s, item);
            if (why != null)
            {
                note = why;
                return;
            }
            DaggerfallUnityItem made = IdleSim.Reforge(w, item);
            note = (made != null) ? "Reforged into " + IdleSim.Name(made) + "." : "The smith could not reforge that.";
            if (made != null)
                DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
            IdleStore.Save();
        }

        public override void Update()
        {
            base.Update();
            IdleWorld w = IdleStore.W;
            if (canvas == null || w == null)
                return;
            canvas.Size = new Vector2(Screen.width, Screen.height);

            int click = canvas.Buttons.Clicked();
            if (click >= 0)
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            if (click < 0 && Input.GetMouseButtonDown(0))
            {
                int cell = canvas.RowAt(IdleUi.Mouse);
                if (cell >= 0)
                    row = cell;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
                row = Mathf.Min(IdleData.SlotCount - 1, row + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow))
                row = Mathf.Max(0, row - 1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.R) || click == BtnReinforce)
                Reinforce(w);
            if (Input.GetKeyDown(KeyCode.F) || click == BtnReforge)
                Reforge(w);
            if (Input.GetKeyDown(KeyCode.A) || click == BtnAuto)
            {
                w.s.noAutoSmith = !w.s.noAutoSmith;
                note = w.s.noAutoSmith ? "The smith now waits to be told." : "Coming home, the hero spends up to half the gold at the smith.";
            }
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            IdleClock.Tick();
        }

        class SmithCanvas : BaseScreenComponent
        {
            public IdleSmithWindow Owner;
            public readonly IdleButtons Buttons = new IdleButtons();
            readonly List<Rect> rows = new List<Rect>();

            public int RowAt(Vector2 mouse)
            {
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Contains(mouse))
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
                float listW = Mathf.Round(width * 0.58f);
                float x = r.x + pad;
                float cardX = x + listW + pad;
                float cardW = r.xMax - pad - cardX;

                float y = top;
                IdleUi.Text("Smith", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.6f;
                IdleUi.Text("Gold " + s.gold + "    Spent here " + s.smithGold, x, y, sc, IdleUi.White);
                y += line * 1.4f;

                rows.Clear();
                float rowTall = Mathf.Round(line * 1.3f);
                for (int i = 0; i < IdleData.SlotCount; i++)
                {
                    int slot = SlotOfRow(i);
                    DaggerfallUnityItem item = w.gear[slot];
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
                        IdleUi.Text(IdleData.SlotNames[slot] + ": nothing", cell.x + line * 0.4f, ty, sc * 0.9f, IdleUi.Muted);
                        y += rowTall;
                        continue;
                    }
                    string right;
                    Color rightColor;
                    if (IdleSim.Level(item) < IdleSim.LevelCap(item))
                    {
                        int cost = IdleSim.StepCost(s, item);
                        right = (cost >= IdleData.Unbuyable) ? "needs ingots" : cost + " gold";
                        rightColor = (cost <= s.gold) ? IdleUi.Gold : IdleUi.Bad;
                    }
                    else if (IdleSim.CannotReforge(s, item) == null || IdleSim.CannotReforge(s, item).StartsWith("Not enough") || IdleSim.CannotReforge(s, item).StartsWith("Needs"))
                    {
                        int cost = IdleSim.ReforgeCost(s, item);
                        right = (cost >= IdleData.Unbuyable) ? "reforge: needs ingots" : "reforge " + cost;
                        rightColor = (cost <= s.gold) ? IdleUi.Gold : IdleUi.Bad;
                    }
                    else
                    {
                        right = "at its top";
                        rightColor = IdleUi.Muted;
                    }
                    float rightX = cell.xMax - IdleUi.Width(right, sc * 0.85f) - line * 0.4f;
                    string name = IdleSim.Name(item);
                    while (name.Length > 4 && cell.x + line * 0.4f + IdleUi.Width(name, sc * 0.9f) > rightX - line)
                        name = name.Substring(0, name.Length - 4) + "..";
                    IdleUi.Text(name, cell.x + line * 0.4f, ty, sc * 0.9f, IdleUi.EnchantColor(IdleSim.Enchants(item)));
                    IdleUi.Text(right, rightX, ty, sc * 0.85f, rightColor);
                    y += rowTall;
                }

                y += line * 0.4f;
                string pouch = "Ingots:";
                bool any = false;
                for (int m = 0; m < IdleData.Metals; m++)
                    if (s.ingots[m] > 0)
                    {
                        pouch += " " + IdleData.MetalNames[m] + " " + s.ingots[m];
                        any = true;
                    }
                IdleUi.Text(any ? pouch : "Ingots: none in the pouch. The smith sells iron and steel; the forge (B in the hub) makes the rest.", x, y, sc * 0.85f, IdleUi.Muted);
                y += line * 1.2f;
                if (Owner.note.Length > 0)
                    Wrap(Owner.note, x, y, listW, sc * 0.9f, line * 0.95f, IdleUi.Gold);

                // ---- the selected piece
                DaggerfallUnityItem sel = w.gear[SlotOfRow(Mathf.Clamp(Owner.row, 0, IdleData.SlotCount - 1))];
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
                    cy += line * 0.3f;
                    cy = Wrap(IdleSim.LevelLine(sel), cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.White);
                    cy += line * 0.5f;

                    int level = IdleSim.Level(sel);
                    bool weapon = sel.ItemGroup == ItemGroups.Weapons;
                    int metal = IdleSim.Metal(sel);
                    if (level < IdleSim.LevelCap(sel))
                    {
                        IdleUi.Text("Reinforce to +" + (level + 1) + " (R or Enter)", cx, cy, sc * 0.95f, IdleUi.Gold);
                        cy += line * 1.1f;
                        string gain = weapon ? (IdleData.LevelDamagePercent + "% more base damage")
                            : ((level + 1) % 2 == 0) ? "+1 armor" : "no armor yet; the next level adds 1";
                        cy = Wrap(gain + ".", cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.White);
                        int ingots = IdleSim.StepIngots(sel);
                        int bill = IdleSim.IngotBill(s, metal, ingots);
                        if (bill >= IdleData.Unbuyable)
                            cy = Wrap(IdleSim.NeedIngots(s, metal, ingots), cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Bad);
                        else
                            cy = Wrap(IdleSim.StepGold(sel) + " gold and " + ingots + " " + IdleData.MetalNames[metal] + " ingot(s)"
                            + ((bill > 0) ? ", bought for " + bill : ", from the pouch") + ". Total " + IdleSim.StepCost(s, sel) + ".",
                            cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.White);
                    }
                    else
                    {
                        cy = Wrap("At its top: " + IdleSim.MetalName(sel) + " goes no higher than +" + IdleSim.LevelCap(sel) + ".", cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.White);
                    }
                    cy += line * 0.5f;

                    string reforge = IdleSim.CannotReforge(s, sel);
                    if (reforge != null && reforge.StartsWith("Needs"))
                    {
                        IdleUi.Text("Reforge into " + IdleSim.NextMetalName(sel) + " (F)", cx, cy, sc * 0.95f, IdleUi.Gold);
                        cy += line * 1.1f;
                        cy = Wrap(reforge, cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Bad);
                    }
                    else if (reforge == null || reforge.StartsWith("Not enough"))
                    {
                        IdleUi.Text("Reforge into " + IdleSim.NextMetalName(sel) + " (F)", cx, cy, sc * 0.95f, IdleUi.Gold);
                        cy += line * 1.1f;
                        cy = Wrap("Keeps its level and enchantments and raises its top. " + IdleSim.ReforgeGold(sel) + " gold and "
                            + IdleSim.ReforgeIngots(sel) + " ingot(s). Total " + IdleSim.ReforgeCost(s, sel) + ".", cx, cy, cw, sc * 0.85f, line * 0.9f,
                            (reforge == null) ? IdleUi.White : IdleUi.Bad);
                    }
                    else
                    {
                        cy = Wrap(reforge, cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Muted);
                    }
                    cy += line * 0.7f;
                    Wrap(s.noAutoSmith ? "Auto-smith is off." : "Auto-smith is on: coming home, the hero pays for the cheapest next step again and again while it costs at most half the gold, reforging pieces at their top.",
                        cx, cy, cw, sc * 0.8f, line * 0.85f, IdleUi.Muted);
                }

                float gap = Mathf.Round(line * 0.3f);
                float bx = x;
                Buttons.Begin();
                bx += Buttons.Add(BtnReinforce, "Reinforce (R)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnReforge, "Reforge (F)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnAuto, "Auto-smith (A)", bx, bottom, tall, !s.noAutoSmith) + gap;
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
