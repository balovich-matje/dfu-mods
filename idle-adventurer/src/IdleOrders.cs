// Idle Adventurer - the orders screen: the hero's fight rules, when to come home, how many potions to carry.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace IdleAdventurer
{
    public class IdleOrdersWindow : DaggerfallPopupWindow
    {
        const int BtnBack = 1, BtnReset = 2, BtnLess = 3, BtnMore = 4;
        const int RowRetreat = IdleData.RuleSlots;
        const int RowPotions = IdleData.RuleSlots + 1;
        const int Rows = IdleData.RuleSlots + 2;
        static readonly int[] retreatSteps = { 0, 10, 20, 25, 30, 35, 40, 50, 60, 70 };

        OrdersCanvas canvas;
        int row;
        int column;             // 0 the condition, 1 the action

        public IdleOrdersWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new OrdersCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        // Moves the selected setting one step forward or back.
        void Step(IdleSave s, int by)
        {
            if (row < IdleData.RuleSlots)
            {
                if (column == 0)
                {
                    int at = Mathf.Max(0, Array.IndexOf(IdleData.IfOrder, s.ruleIf[row]));
                    s.ruleIf[row] = IdleData.IfOrder[(at + by + IdleData.IfOrder.Length) % IdleData.IfOrder.Length];
                }
                else
                    s.ruleDo[row] = (s.ruleDo[row] + by + IdleData.DoKinds) % IdleData.DoKinds;
            }
            else if (row == RowRetreat)
            {
                int at = 0;
                for (int i = 0; i < retreatSteps.Length; i++)
                    if (retreatSteps[i] <= s.retreatPercent)
                        at = i;
                s.retreatPercent = retreatSteps[Mathf.Clamp(at + by, 0, retreatSteps.Length - 1)];
            }
            else if (row == RowPotions)
            {
                s.potionsToCarry = Mathf.Clamp(s.potionsToCarry + by, 0, 20);
            }
            IdleStore.Save();
        }

        public override void Update()
        {
            base.Update();
            IdleWorld w = IdleStore.W;
            if (canvas == null || w == null)
                return;
            canvas.Size = new Vector2(Screen.width, Screen.height);
            IdleSave s = w.s;

            int click = canvas.Buttons.Clicked();
            if (click >= 0)
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            if (click < 0 && Input.GetMouseButtonDown(0))
            {
                int cell = canvas.CellAt(IdleUi.Mouse);
                if (cell >= 0)
                {
                    // A click on a rule's part selects it and moves it on one step.
                    row = cell / 2;
                    column = cell % 2;
                    if (row < IdleData.RuleSlots)
                        Step(s, 1);
                }
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
                row = Mathf.Min(Rows - 1, row + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow))
                row = Mathf.Max(0, row - 1);
            bool rule = row < IdleData.RuleSlots;
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                if (rule)
                    column = 0;
                else
                    Step(s, -1);
            }
            if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                if (rule)
                    column = 1;
                else
                    Step(s, 1);
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Equals)
                || Input.GetKeyDown(KeyCode.KeypadPlus) || click == BtnMore)
                Step(s, 1);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || Input.GetKeyDown(KeyCode.Backspace) || click == BtnLess)
                Step(s, -1);
            if (Input.GetKeyDown(KeyCode.R) || click == BtnReset)
            {
                s.ruleIf = (int[])IdleData.DefaultIf.Clone();
                s.ruleDo = (int[])IdleData.DefaultDo.Clone();
                s.retreatPercent = 35;
                s.potionsToCarry = 3;
                IdleStore.Save();
            }
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            IdleClock.Tick();
        }

        class OrdersCanvas : BaseScreenComponent
        {
            public IdleOrdersWindow Owner;
            public readonly IdleButtons Buttons = new IdleButtons();
            readonly List<Rect> cells = new List<Rect>();
            readonly List<int> cellIds = new List<int>();

            public int CellAt(Vector2 mouse)
            {
                for (int i = 0; i < cells.Count; i++)
                    if (cells[i].Contains(mouse))
                        return cellIds[i];
                return -1;
            }

            // One clickable value box; selected boxes are lit.
            void Cell(int id, string text, Rect r, float sc, bool selected, Color c)
            {
                cells.Add(r);
                cellIds.Add(id);
                Color fill = selected ? new Color(0.72f, 0.58f, 0.18f, 0.32f) : r.Contains(IdleUi.Mouse) ? new Color(1f, 1f, 1f, 0.09f) : new Color(1f, 1f, 1f, 0.04f);
                IdleUi.Box(r, fill);
                if (selected)
                    IdleUi.Box(new Rect(r.x, r.yMax - 2f, r.width, 2f), IdleUi.Line);
                IdleUi.Text(text, r.x + r.height * 0.35f, r.y + (r.height - r.height * 0.7f) / 2f, sc, c);
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
                float listW = Mathf.Round(width * 0.62f);
                float x = r.x + pad;
                float cardX = x + listW + pad;
                float cardW = r.xMax - pad - cardX;

                cells.Clear();
                cellIds.Clear();
                float y = top;
                IdleUi.Text("Orders", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.6f;
                IdleUi.Text("On each of the hero's turns in a fight, the first rule that applies is followed.", x, y, sc * 0.85f, IdleUi.Muted);
                y += line * 1.4f;

                float rowTall = Mathf.Round(line * 1.35f);
                float numW = line * 1.6f;
                float ifW = Mathf.Round((listW - numW) * 0.56f);
                float doW = listW - numW - ifW - line * 0.4f;
                for (int i = 0; i < IdleData.RuleSlots; i++)
                {
                    bool off = s.ruleIf[i] == IdleData.IfOff;
                    IdleUi.Text((i + 1) + ".", x, y + (rowTall - line * 0.74f) / 2f, sc, off ? IdleUi.Muted : IdleUi.White);
                    Cell(i * 2, "If " + IdleData.IfNames[s.ruleIf[i]], new Rect(x + numW, y, ifW, rowTall - 4f), sc * 0.9f,
                        Owner.row == i && Owner.column == 0, off ? IdleUi.Muted : IdleUi.White);
                    Cell(i * 2 + 1, off ? "" : "then " + IdleData.DoNames[s.ruleDo[i]], new Rect(x + numW + ifW + line * 0.4f, y, doW, rowTall - 4f), sc * 0.9f,
                        Owner.row == i && Owner.column == 1, IdleUi.Gold);
                    y += rowTall;
                }
                IdleUi.Text("If no rule applies: attack.", x + numW, y + line * 0.2f, sc * 0.85f, IdleUi.Muted);
                y += line * 1.8f;

                // The two numbers. Left and right arrows (or the - and + buttons) change the selected one.
                IdleUi.Text("Between fights", x, y, sc * 1.05f, IdleUi.Gold);
                y += line * 1.3f;
                string retreat = (s.retreatPercent == 0) ? "Never go home early" : "Go home below " + s.retreatPercent + "% health, after a potion if there is one";
                Cell(RowRetreat * 2, retreat, new Rect(x, y, listW, rowTall - 4f), sc * 0.9f, Owner.row == RowRetreat, IdleUi.White);
                y += rowTall;
                Cell(RowPotions * 2, "Carry " + s.potionsToCarry + " healing potions (bought in town for " + IdleData.PotionPrice(s.level) + " gold each)",
                    new Rect(x, y, listW, rowTall - 4f), sc * 0.9f, Owner.row == RowPotions, IdleUi.White);
                y += rowTall;

                // ---- what the actions do
                Rect card = new Rect(cardX, top, cardW, bottom - line * 0.5f - top);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cw = card.width - line;
                float cy = card.y + line * 0.4f;
                IdleUi.Text("What the actions do", cx, cy, sc * 1.05f, IdleUi.Gold);
                cy += line * 1.4f;
                string[] help = {
                    "Attack: an ordinary swing.",
                    "Drink a healing potion: heals " + (s.maxHealth * 3 / 10 + 10) + " (30% and 10). Skipped when none are left or health is full.",
                    "Flee: try to run home. Speed and luck decide; a failed try costs the turn.",
                    "Power attack: 20 less to hit, half again the damage.",
                    "Guard: no attack, and the enemy's next attack is 40 less likely to land.",
                    "Spells (L in the hub to buy them): a damage spell picks the strongest known one this enemy does not resist. A spell is skipped when it is not known, magicka is short, or it would do nothing.",
                    "",
                    "Champions have one trait, bosses two:",
                    "Swift attacks more often. Armoured is harder to hit. Regenerating heals as it fights. Venomous and Enraged hit harder. Vampiric heals from its hits.",
                    "",
                    "Potions: " + s.potions + " carried now. Magicka " + s.magicka + "/" + IdleSim.MaxMagicka(s) + ". You have " + s.gold + " gold.",
                };
                for (int i = 0; i < help.Length; i++)
                    cy = Wrap(help[i], cx, cy, cw, sc * 0.85f, line * 0.9f, (i >= 7) ? IdleUi.Muted : IdleUi.White);

                float gap = Mathf.Round(line * 0.3f);
                float bx = x;
                Buttons.Begin();
                bx += Buttons.Add(BtnLess, "Back one (Backspace)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnMore, "Next (Enter)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnReset, "Default orders (R)", bx, bottom, tall, false) + gap;
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
