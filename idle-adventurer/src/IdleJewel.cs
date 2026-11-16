// Idle Adventurer - the jeweller: sockets for worn gear, gems in and out of them, and gem upgrades.

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
    public class IdleJewelWindow : DaggerfallPopupWindow
    {
        const int BtnSocket = 1, BtnSet = 2, BtnUpgrade = 3, BtnRemove = 4, BtnBack = 5, BtnBuy = 6;

        JewelCanvas canvas;
        int row;
        int socket;
        string note = "";

        public IdleJewelWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new JewelCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        DaggerfallUnityItem Selected(IdleWorld w)
        {
            return w.gear[IdleSmithWindow.SlotOfRow(row)];
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
                int at = canvas.SocketAt(IdleUi.Mouse);
                if (at >= 0)
                    socket = at;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
                row = Mathf.Min(IdleData.SlotCount - 1, row + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow))
                row = Mathf.Max(0, row - 1);
            DaggerfallUnityItem item = Selected(w);
            int sockets = IdleSim.Sockets(item);
            if (Input.GetKeyDown(KeyCode.RightArrow))
                socket++;
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                socket--;
            socket = Mathf.Clamp(socket, 0, Mathf.Max(0, sockets - 1));

            if (Input.GetKeyDown(KeyCode.A) || click == BtnSocket)
            {
                string why = IdleSim.CannotAddSocket(s, item);
                if (why != null)
                    note = why;
                else
                {
                    IdleSim.AddSocket(w, item);
                    socket = IdleSim.Sockets(item) - 1;
                    Done("Added a socket.");
                }
            }
            if (Input.GetKeyDown(KeyCode.G) || Input.GetKeyDown(KeyCode.Return) || click == BtnSet)
            {
                int code = (item != null) ? IdleSim.BestPouchGem(s, item) : -1;
                if (item == null || sockets == 0)
                    note = "Nothing to set a gem in: add a socket first.";
                else if (IdleSim.GemIn(item, socket) >= 0)
                    note = "That socket holds a gem already; take it out first (X).";
                else if (code < 0)
                    note = "No gems in the pouch. Veins in caves, champions and bosses give them.";
                else
                {
                    IdleSim.SetGem(w, item, socket, code);
                    Done("Set a " + IdleSim.GemName(code).ToLower() + ".");
                }
            }
            if (Input.GetKeyDown(KeyCode.U) || click == BtnUpgrade)
            {
                int code = IdleSim.GemIn(item, socket);
                string why = IdleSim.CannotUpgradeGem(s, code);
                if (why != null)
                    note = why;
                else
                {
                    IdleSim.UpgradeGem(w, item, socket);
                    Done("Now a " + IdleSim.GemName(code + 1).ToLower() + ".");
                }
            }
            if (Input.GetKeyDown(KeyCode.X) || click == BtnRemove)
            {
                int code = IdleSim.GemIn(item, socket);
                if (code < 0)
                    note = "The socket is empty.";
                else if (!IdleSim.RemoveGem(w, item, socket))
                    note = "Not enough gold: taking it out costs " + IdleData.GemRemoveGold(code % 16) + ".";
                else
                    Done("The " + IdleSim.GemName(code).ToLower() + " is back in the pouch.");
            }
            int slotHere = IdleSmithWindow.SlotOfRow(row);
            if ((Input.GetKeyDown(KeyCode.P) || click == BtnBuy) && slotHere >= IdleData.SlotAmulet)
            {
                string why = IdleSim.CannotBuyTrinket(w, slotHere);
                if (why != null)
                    note = why;
                else
                {
                    bool worn = w.gear[slotHere] == null;
                    IdleSim.BuyTrinket(w, slotHere);
                    Done(worn ? "Bought a plain " + IdleData.SlotNames[slotHere].ToLower() + "; the hero wears it." : "Bought a plain " + IdleData.SlotNames[slotHere].ToLower() + "; it is in the stash.");
                }
            }
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            IdleClock.Tick();
        }

        class JewelCanvas : BaseScreenComponent
        {
            public IdleJewelWindow Owner;
            public readonly IdleButtons Buttons = new IdleButtons();
            readonly List<Rect> rows = new List<Rect>();
            readonly List<Rect> socketRects = new List<Rect>();

            public int RowAt(Vector2 mouse)
            {
                for (int i = 0; i < rows.Count; i++)
                    if (rows[i].Contains(mouse))
                        return i;
                return -1;
            }

            public int SocketAt(Vector2 mouse)
            {
                for (int i = 0; i < socketRects.Count; i++)
                    if (socketRects[i].Contains(mouse))
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
                IdleUi.Text("Jeweller", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.6f;
                IdleUi.Text("Gold " + s.gold + "    Spent here " + s.jewelGold + "    Gems in the pouch " + IdleSim.GemCount(s), x, y, sc, IdleUi.White);
                y += line * 1.4f;

                rows.Clear();
                float rowTall = Mathf.Round(line * 1.15f);
                for (int i = 0; i < IdleData.SlotCount; i++)
                {
                    int slot = IdleSmithWindow.SlotOfRow(i);
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
                    // Sockets as small squares at the right: gold when filled.
                    int most = IdleSim.MaxSockets(item);
                    float box = Mathf.Round(cell.height * 0.45f);
                    float bx = cell.xMax - line * 0.4f - most * (box + 4f);
                    for (int k = 0; k < most; k++)
                    {
                        Rect b = new Rect(bx + k * (box + 4f), cell.y + (cell.height - box) / 2f, box, box);
                        if (k >= IdleSim.Sockets(item))
                            IdleUi.Box(b, new Color(1f, 1f, 1f, 0.06f));
                        else if (IdleSim.GemIn(item, k) >= 0)
                            IdleUi.Box(b, IdleUi.Gold);
                        else
                            IdleUi.Frame(b, new Color(0f, 0f, 0f, 0.4f));
                    }
                    string name = IdleSim.Name(item);
                    while (name.Length > 4 && cell.x + line * 0.4f + IdleUi.Width(name, sc * 0.9f) > bx - line * 0.5f)
                        name = name.Substring(0, name.Length - 4) + "..";
                    IdleUi.Text(name, cell.x + line * 0.4f, ty, sc * 0.9f, IdleUi.EnchantColor(IdleSim.Enchants(item)));
                    y += rowTall;
                }

                y += line * 0.4f;
                string pouch = "";
                for (int g = 0; g < IdleData.GemKinds; g++)
                {
                    int n = 0, best = 0;
                    for (int level = 1; level <= IdleData.GemTop; level++)
                        if (s.gems[g * 16 + level] > 0)
                        {
                            n += s.gems[g * 16 + level];
                            best = level;
                        }
                    if (n > 0)
                        pouch += (pouch.Length > 0 ? ", " : "") + IdleData.GemNames[g] + " " + n + (n > 0 ? " (best " + best + ")" : "");
                }
                y = Wrap(pouch.Length > 0 ? "Pouch: " + pouch : "Pouch: no gems. Veins in caves, champions and bosses give them.", x, y, listW, sc * 0.85f, line * 0.9f, IdleUi.Muted);
                y += line * 0.3f;
                if (Owner.note.Length > 0)
                    Wrap(Owner.note, x, y, listW, sc * 0.9f, line * 0.95f, IdleUi.Gold);

                // ---- the selected piece
                socketRects.Clear();
                DaggerfallUnityItem sel = w.gear[IdleSmithWindow.SlotOfRow(Mathf.Clamp(Owner.row, 0, IdleData.SlotCount - 1))];
                Rect card = new Rect(cardX, top, cardW, bottom - line * 0.5f - top);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cw = card.width - line;
                float cy = card.y + line * 0.4f;
                if (sel == null)
                {
                    int slotSel = IdleSmithWindow.SlotOfRow(Mathf.Clamp(Owner.row, 0, IdleData.SlotCount - 1));
                    IdleUi.Text("Nothing worn here.", cx, cy, sc, IdleUi.Muted);
                    if (slotSel >= IdleData.SlotAmulet)
                    {
                        cy += line * 1.4f;
                        string what = IdleData.SlotNames[slotSel].ToLower();
                        cy = Wrap("Amulets, rings and robes give no armor and are not reinforced, but take sockets and any enchantment but armor, so gems and souls pay off without plate. Every class may wear them.",
                            cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Muted);
                        cy += line * 0.4f;
                        Wrap("Buy a plain " + what + " (P): " + IdleData.TrinketPrice[slotSel - IdleData.SlotAmulet] + " gold. Champions and bosses sometimes drop enchanted ones.",
                            cx, cy, cw, sc * 0.85f, line * 0.9f, (IdleSim.CannotBuyTrinket(w, slotSel) == null) ? IdleUi.White : IdleUi.Bad);
                    }
                }
                else
                {
                    bool weapon = IdleSim.GemsAsWeapon(sel);
                    cy = Wrap(IdleSim.Name(sel), cx, cy, cw, sc * 1.1f, line * 1.2f, IdleUi.Gold);
                    cy += line * 0.4f;
                    int count = IdleSim.Sockets(sel);
                    if (count == 0)
                        cy = Wrap("No sockets yet.", cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Muted);
                    for (int k = 0; k < count; k++)
                    {
                        Rect cell = new Rect(cx - line * 0.2f, cy, cw + line * 0.4f, line * 1.15f);
                        socketRects.Add(cell);
                        if (Owner.socket == k)
                            IdleUi.Box(cell, new Color(0.72f, 0.58f, 0.18f, 0.32f));
                        int code = IdleSim.GemIn(sel, k);
                        string text = (code < 0) ? "Socket " + (k + 1) + ": empty" : "Socket " + (k + 1) + ": " + IdleSim.GemName(code) + ", " + IdleSim.GemEffect(code, weapon);
                        IdleUi.Text(text, cx, cy + line * 0.2f, sc * 0.85f, (code < 0) ? IdleUi.Muted : IdleUi.Good);
                        cy += line * 1.2f;
                    }
                    cy += line * 0.4f;

                    int chosen = IdleSim.GemIn(sel, Owner.socket);
                    if (chosen >= 0)
                    {
                        string upWhy = IdleSim.CannotUpgradeGem(s, chosen);
                        if (chosen % 16 < IdleData.GemTop)
                        {
                            cy = Wrap("Upgrade (U): to " + IdleSim.GemName(chosen + 1).ToLower() + ", " + IdleSim.GemEffect(chosen + 1, weapon) + ", for "
                                + IdleData.GemUpgradeGold(chosen % 16) + " gold" + ((chosen % 16 >= IdleData.GemSecondFrom) ? " and another " + IdleData.GemNames[chosen / 16].ToLower() : "") + ".",
                                cx, cy, cw, sc * 0.85f, line * 0.9f, (upWhy == null) ? IdleUi.White : IdleUi.Bad);
                            if (upWhy != null && !upWhy.StartsWith("Not enough"))
                                cy = Wrap(upWhy, cx, cy, cw, sc * 0.8f, line * 0.85f, IdleUi.Muted);
                        }
                        cy = Wrap("Take out (X): " + IdleData.GemRemoveGold(chosen % 16) + " gold; the gem goes back to the pouch.", cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Muted);
                    }
                    else if (count > 0)
                    {
                        int best = IdleSim.BestPouchGem(s, sel);
                        cy = Wrap((best >= 0) ? "Set a gem (G or Enter): the best in the pouch is a " + IdleSim.GemName(best).ToLower() + ", " + IdleSim.GemEffect(best, weapon) + "."
                            : "No gem in the pouch to set.", cx, cy, cw, sc * 0.85f, line * 0.9f, (best >= 0) ? IdleUi.White : IdleUi.Muted);
                    }
                    cy += line * 0.3f;
                    string sockWhy = IdleSim.CannotAddSocket(s, sel);
                    if (count < IdleSim.MaxSockets(sel))
                        cy = Wrap("Add a socket (A): " + IdleSim.SocketCost(sel) + " gold. This piece takes up to " + IdleSim.MaxSockets(sel) + ".", cx, cy, cw, sc * 0.85f, line * 0.9f,
                            (sockWhy == null) ? IdleUi.White : IdleUi.Bad);
                    cy += line * 0.5f;

                    // What each gem does in this piece.
                    IdleUi.Text(weapon ? ((sel.ItemGroup == ItemGroups.Weapons) ? "Gems in a weapon, per level:" : "Gems in an amulet, ring or robe, per level:") : "Gems in armor, per level:", cx, cy, sc * 0.8f, IdleUi.Muted);
                    cy += line * 0.9f;
                    for (int g = 0; g < IdleData.GemKinds; g++)
                    {
                        IdleUi.Text(IdleData.GemNames[g] + ": " + IdleSim.GemEffect(g * 16 + 1, weapon) + " at 1, " + IdleSim.GemEffect(g * 16 + IdleData.GemTop, weapon) + " at 10",
                            cx, cy, sc * 0.75f, IdleUi.Muted);
                        cy += line * 0.8f;
                    }
                }

                float gap = Mathf.Round(line * 0.3f);
                float bx2 = x;
                Buttons.Begin();
                bx2 += Buttons.Add(BtnSocket, "Add socket (A)", bx2, bottom, tall, false) + gap;
                bx2 += Buttons.Add(BtnSet, "Set gem (G)", bx2, bottom, tall, false) + gap;
                bx2 += Buttons.Add(BtnUpgrade, "Upgrade (U)", bx2, bottom, tall, false) + gap;
                bx2 += Buttons.Add(BtnRemove, "Take out (X)", bx2, bottom, tall, false) + gap;
                if (IdleSmithWindow.SlotOfRow(Mathf.Clamp(Owner.row, 0, IdleData.SlotCount - 1)) >= IdleData.SlotAmulet)
                    bx2 += Buttons.Add(BtnBuy, "Buy plain (P)", bx2, bottom, tall, false) + gap;
                Buttons.Add(BtnBack, "Back (Esc)", bx2, bottom, tall, false);
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
