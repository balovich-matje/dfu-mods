// Idle Adventurer - the forge: the materials pouch, the smelter's queue and the pickaxe.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace IdleAdventurer
{
    public class IdleForgeWindow : DaggerfallPopupWindow
    {
        const int BtnSmelt = 1, BtnSmeltAll = 2, BtnPick = 3, BtnRush = 4, BtnBack = 5;

        ForgeCanvas canvas;
        int row;
        string note = "";

        public IdleForgeWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new ForgeCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        string CannotSmelt(IdleSave s, int metal)
        {
            if (IdleSim.SmeltRecipe(metal).Length == 0)
                return "Nothing smelts into that.";
            if (!IdleSim.CanSmelt(s, metal))
                return "One " + IdleData.MetalNames[metal].ToLower() + " ingot takes " + IdleSim.SmeltRecipe(metal) + " and " + IdleData.SmeltFee(metal) + " gold.";
            return null;
        }

        void Smelt(IdleWorld w, bool all)
        {
            IdleSave s = w.s;
            string why = CannotSmelt(s, row);
            if (why != null)
            {
                note = why;
                return;
            }
            int n = 0;
            do
            {
                if (!IdleSim.QueueSmelt(s, row))
                    break;
                n++;
            } while (all);
            note = n + " " + IdleData.MetalNames[row].ToLower() + " ingot(s) put in the smelter.";
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
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
                row = Mathf.Min(IdleData.Metals - 1, row + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow))
                row = Mathf.Max(0, row - 1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || click == BtnSmelt)
                Smelt(w, false);
            if (Input.GetKeyDown(KeyCode.A) || click == BtnSmeltAll)
                Smelt(w, true);
            if (Input.GetKeyDown(KeyCode.P) || click == BtnPick)
            {
                string why = IdleSim.CannotMakePick(s, row);
                if (why != null)
                    note = why;
                else
                {
                    IdleSim.MakePick(s, row);
                    note = "Now carrying a " + IdleSim.PickName(s).ToLower() + ".";
                    DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                    IdleStore.Save();
                }
            }
            if (Input.GetKeyDown(KeyCode.U) || click == BtnRush)
            {
                if (IdleSim.Queued(s) == 0)
                    note = "The smelter is idle.";
                else if (IdleSim.RushCost(s) > s.gold)
                    note = "Not enough gold: rushing costs " + IdleSim.RushCost(s) + ".";
                else
                {
                    note = "Paid " + IdleSim.RushCost(s) + " gold to finish the queue now.";
                    IdleSim.Rush(w);
                    DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                    IdleStore.Save();
                }
            }
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            IdleClock.Tick();
        }

        static string Time(int seconds)
        {
            if (seconds >= 3600)
                return (seconds / 3600) + " h " + (seconds % 3600 / 60) + " min";
            if (seconds >= 60)
                return (seconds / 60) + " min " + (seconds % 60) + " s";
            return seconds + " s";
        }

        class ForgeCanvas : BaseScreenComponent
        {
            public IdleForgeWindow Owner;
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
                float listW = Mathf.Round(width * 0.55f);
                float x = r.x + pad;
                float cardX = x + listW + pad;
                float cardW = r.xMax - pad - cardX;

                float y = top;
                IdleUi.Text("Forge", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.6f;
                IdleUi.Text("Gold " + s.gold + "    Coal " + s.coal + "    Daedra hearts " + s.hearts, x, y, sc, IdleUi.White);
                y += line * 1.1f;
                string pick = (s.pick <= 0) ? "No pickaxe: veins in caves are left alone."
                    : "Pickaxe: " + IdleData.MetalNames[s.pick - 1].ToLower() + ", mines up to " + IdleData.OreNames[NextOre(IdleSim.PickReach(s))].ToLower()
                      + ((s.pick == IdleData.Metals) ? ", never breaks" : ", " + s.pickUses + " vein(s) before it breaks");
                IdleUi.Text(pick, x, y, sc * 0.85f, (s.pick <= 0) ? IdleUi.Bad : IdleUi.Muted);
                y += line * 1.3f;

                // Column heads.
                float colOre = x + listW * 0.42f, colIngot = x + listW * 0.62f, colQueue = x + listW * 0.80f;
                IdleUi.Text("Metal", x + line * 0.4f, y, sc * 0.8f, IdleUi.Muted);
                IdleUi.Text("Ore", colOre, y, sc * 0.8f, IdleUi.Muted);
                IdleUi.Text("Ingots", colIngot, y, sc * 0.8f, IdleUi.Muted);
                IdleUi.Text("Smelting", colQueue, y, sc * 0.8f, IdleUi.Muted);
                y += line;

                rows.Clear();
                float rowTall = Mathf.Round(line * 1.2f);
                for (int m = 0; m < IdleData.Metals; m++)
                {
                    Rect cell = new Rect(x, y, listW, rowTall - 3f);
                    rows.Add(cell);
                    bool selected = Owner.row == m;
                    Color fill = selected ? new Color(0.72f, 0.58f, 0.18f, 0.32f) : cell.Contains(IdleUi.Mouse) ? new Color(1f, 1f, 1f, 0.09f) : new Color(1f, 1f, 1f, 0.04f);
                    IdleUi.Box(cell, fill);
                    if (selected)
                        IdleUi.Box(new Rect(cell.x, cell.yMax - 2f, cell.width, 2f), IdleUi.Line);
                    float ty = cell.y + (cell.height - line * 0.7f) / 2f;
                    IdleUi.Text(IdleData.MetalNames[m], cell.x + line * 0.4f, ty, sc * 0.9f, IdleUi.White);
                    string oreText = (IdleData.OreNames[m].Length > 0) ? s.ore[m].ToString() : "-";
                    IdleUi.Text(oreText, colOre, ty, sc * 0.9f, (s.ore[m] > 0) ? IdleUi.White : IdleUi.Muted);
                    IdleUi.Text(s.ingots[m].ToString(), colIngot, ty, sc * 0.9f, (s.ingots[m] > 0) ? IdleUi.Gold : IdleUi.Muted);
                    string queue = (s.smeltQueue[m] > 0) ? s.smeltQueue[m] + ((s.smeltMetal == m + 1) ? " (now)" : "") : "-";
                    IdleUi.Text(queue, colQueue, ty, sc * 0.9f, (s.smeltQueue[m] > 0) ? IdleUi.Good : IdleUi.Muted);
                    y += rowTall;
                }

                y += line * 0.4f;
                int queued = IdleSim.Queued(s);
                string smelter = (queued == 0) ? "The smelter is idle." : "Smelter: " + queued + " ingot(s) queued, done in " + Time(IdleSim.SmeltSecondsLeft(s))
                    + ". Rushing costs " + IdleSim.RushCost(s) + " gold.";
                IdleUi.Text(smelter, x, y, sc * 0.85f, (queued == 0) ? IdleUi.Muted : IdleUi.Good);
                y += line * 1.2f;
                if (Owner.note.Length > 0)
                    Wrap(Owner.note, x, y, listW, sc * 0.9f, line * 0.95f, IdleUi.Gold);

                // ---- the selected metal
                int k = Mathf.Clamp(Owner.row, 0, IdleData.Metals - 1);
                Rect card = new Rect(cardX, top, cardW, bottom - line * 0.5f - top);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cw = card.width - line;
                float cy = card.y + line * 0.4f;
                IdleUi.Text(IdleData.MetalNames[k], cx, cy, sc * 1.2f, IdleUi.Gold);
                cy += line * 1.5f;

                string recipe = IdleSim.SmeltRecipe(k);
                cy = Wrap("Smelting (Enter one, A all): " + recipe + " and " + IdleData.SmeltFee(k) + " gold make one ingot in " + Time(IdleData.SmeltSeconds(k))
                    + ". The smelter keeps working while the hero is away and while the game is closed.", cx, cy, cw, sc * 0.85f, line * 0.9f,
                    IdleSim.CanSmelt(s, k) ? IdleUi.White : IdleUi.Muted);
                cy += line * 0.4f;
                string where;
                if (k == 1)
                    where = "Iron ore and coal come from veins in mines and caves.";
                else if (k == IdleData.Metals - 1)
                    where = "Ebony ore shows from floor " + IdleData.OreFloor[6] + " of caves (earlier in volcanic caves); daedra drop their hearts.";
                else
                {
                    int index = Array.IndexOf(IdleData.OreMetals, k);
                    where = IdleData.OreNames[k] + " shows from floor " + IdleData.OreFloor[index] + " of natural caves; volcanic caves bring it 6 floors sooner, mines 3 floors later but richer.";
                }
                cy = Wrap(where, cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Muted);
                if (k < IdleData.SoldIngots)
                    cy = Wrap("The smith also sells these ingots for " + IdleData.IngotPrice[k] + " gold each.", cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Muted);
                cy = Wrap("Selling " + IdleData.MetalNames[k].ToLower() + " gear sometimes leaves an ingot (" + IdleData.SalvageChance + "%).", cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Muted);
                cy += line * 0.6f;

                IdleUi.Text(IdleData.MetalNames[k] + " pickaxe (P)", cx, cy, sc * 0.95f, IdleUi.Gold);
                cy += line * 1.1f;
                string pickWhy = IdleSim.CannotMakePick(s, k);
                string pickText = "Mines up to " + IdleData.OreNames[NextOre(Mathf.Min(IdleData.Metals - 1, k + 1))].ToLower()
                    + ((k == IdleData.Metals - 1) ? " and never breaks. " : " and lasts " + IdleData.PickUses(k) + " veins. ")
                    + ((k == 0) ? "Bought for " + IdleData.PickPrice + " gold, or forged from " + IdleData.PickIngots + " ingots and " + IdleData.PickGold(0) + " gold."
                        : "Forged from " + IdleData.PickIngots + " ingots and " + IdleData.PickGold(k) + " gold.");
                cy = Wrap(pickText, cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.White);
                if (pickWhy != null)
                    cy = Wrap(pickWhy, cx, cy, cw, sc * 0.85f, line * 0.9f, IdleUi.Bad);
                cy += line * 0.6f;
                Wrap(s.noAutoSmith ? "Auto-smith is off (Smith screen, A)." : "Auto-smith is on: coming home, the hero smelts all the ore, forges a better pickaxe when the ingots are there and buys an iron one when it broke.",
                    cx, cy, cw, sc * 0.8f, line * 0.85f, IdleUi.Muted);

                float gap = Mathf.Round(line * 0.3f);
                float bx = x;
                Buttons.Begin();
                bx += Buttons.Add(BtnSmelt, "Smelt one (Enter)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnSmeltAll, "Smelt all (A)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnPick, "Pickaxe (P)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnRush, "Rush (U)", bx, bottom, tall, false) + gap;
                Buttons.Add(BtnBack, "Back (Esc)", bx, bottom, tall, false);
            }

            // The ore a pickaxe's reach stops at, as an ore name (steel and daedric have none of their own).
            static int NextOre(int metal)
            {
                for (int m = metal; m >= 0; m--)
                    if (IdleData.OreNames[m].Length > 0)
                        return m;
                return 0;
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
