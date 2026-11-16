// Idle Adventurer - town services: the temple's blessing bought before each run, the hero's house, and the trainer.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace IdleAdventurer
{
    public class IdleTownWindow : DaggerfallPopupWindow
    {
        const int BtnUse = 1, BtnBack = 2;
        const int RowHouse = IdleData.BlessingKinds;
        const int FirstSkill = RowHouse + 1;
        static int Rows { get { return FirstSkill + IdleData.TrainSkills.Length; } }

        // "LongBlade" as "Long blade".
        public static string Spaced(int skill)
        {
            string name = ((DFCareer.Skills)skill).ToString();
            string spaced = name.Substring(0, 1);
            for (int i = 1; i < name.Length; i++)
                spaced += char.IsUpper(name[i]) ? " " + char.ToLower(name[i]) : name[i].ToString();
            return spaced;
        }

        TownCanvas canvas;
        int row;
        string note = "";

        public IdleTownWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new TownCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        void Use(IdleWorld w)
        {
            IdleSave s = w.s;
            if (row < RowHouse)
            {
                s.blessing = (s.blessing == row + 1) ? 0 : row + 1;
                note = (s.blessing == 0) ? "No blessing before runs." : "The hero asks for " + IdleData.BlessingNames[row] + " before each run.";
                return;
            }
            if (row == RowHouse)
            {
                string why = IdleSim.CannotBuyHouse(s);
                if (why != null)
                    note = why;
                else
                {
                    IdleSim.BuyHouse(s);
                    note = "The hero moves into " + IdleData.HouseNames[s.house] + ".";
                    DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                    IdleStore.Save();
                }
                return;
            }
            int skill = IdleData.TrainSkills[row - FirstSkill];
            string no = IdleSim.CannotTrain(s, skill);
            if (no != null)
                note = no;
            else
            {
                IdleSim.Train(s, skill);
                note = Spaced(skill) + " is now " + s.skills[skill] + ".";
                DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                IdleStore.Save();
            }
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
                row = Mathf.Min(Rows - 1, row + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow))
                row = Mathf.Max(0, row - 1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || click == BtnUse)
                Use(w);
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            IdleClock.Tick();
        }

        class TownCanvas : BaseScreenComponent
        {
            public IdleTownWindow Owner;
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

                float line = Mathf.Round(H * 0.03f);
                float sc = IdleUi.Scale(line);
                float pad = Mathf.Round(H * 0.025f);
                float tall = Mathf.Round(line * 1.3f);
                float top = r.y + pad;
                float bottom = r.yMax - pad - tall;
                float listW = Mathf.Round(width * 0.55f);
                float x = r.x + pad;
                float cardX = x + listW + pad;
                float cardW = r.xMax - pad - cardX;

                float y = top;
                IdleUi.Text("Town", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.6f;
                IdleUi.Text("Gold " + s.gold + "    Temple " + s.templeGold + "    Trainer " + s.trainerGold + "    House " + s.houseGold, x, y, sc * 0.95f, IdleUi.White);
                y += line * 1.4f;

                rows.Clear();
                float rowTall = Mathf.Round(line * 1.2f);
                for (int i = 0; i < Rows; i++)
                {
                    if (i == 0 || i == RowHouse || i == FirstSkill)
                    {
                        string head = (i == 0) ? "Temple blessing, before each run" : (i == RowHouse) ? "House" : "Trainer (up to " + IdleData.TrainCap(s.level) + " at level " + s.level + ")";
                        IdleUi.Text(head, x, y + line * 0.1f, sc * 0.8f, IdleUi.Muted);
                        y += line * 0.95f;
                    }
                    Rect cell = new Rect(x, y, listW, rowTall - 3f);
                    rows.Add(cell);
                    bool selected = Owner.row == i;
                    Color fill = selected ? new Color(0.72f, 0.58f, 0.18f, 0.32f) : cell.Contains(IdleUi.Mouse) ? new Color(1f, 1f, 1f, 0.09f) : new Color(1f, 1f, 1f, 0.04f);
                    IdleUi.Box(cell, fill);
                    if (selected)
                        IdleUi.Box(new Rect(cell.x, cell.yMax - 2f, cell.width, 2f), IdleUi.Line);
                    float ty = cell.y + (cell.height - line * 0.7f) / 2f;
                    string left, right;
                    Color rightColor = IdleUi.Muted;
                    if (i < RowHouse)
                    {
                        left = IdleData.BlessingNames[i] + ": " + string.Format(IdleData.BlessingText[i], IdleData.BlessingValue(i, s.level));
                        right = (s.blessing == i + 1) ? "chosen, " + IdleData.BlessingPrice(s.level) + " a run" : "";
                        rightColor = IdleUi.Good;
                    }
                    else if (i == RowHouse)
                    {
                        left = "Living in " + IdleData.HouseNames[s.house];
                        right = (s.house >= IdleData.HouseTop) ? "the grandest" : "next: " + IdleData.HousePrice[s.house + 1];
                        rightColor = (s.house < IdleData.HouseTop && s.gold >= IdleData.HousePrice[s.house + 1]) ? IdleUi.Gold : IdleUi.Muted;
                    }
                    else
                    {
                        int skill = IdleData.TrainSkills[i - FirstSkill];
                        left = Spaced(skill) + " " + s.skills[skill] + (IdleSim.Uses(w, skill) ? "" : "  (not used)");
                        right = (s.skills[skill] >= IdleData.TrainCap(s.level)) ? "at the cap" : IdleData.TrainGold(s.skills[skill]) + " gold";
                        rightColor = (IdleSim.CannotTrain(s, skill) == null) ? IdleUi.Gold : IdleUi.Muted;
                    }
                    IdleUi.Text(left, cell.x + line * 0.4f, ty, sc * 0.9f, IdleUi.White);
                    if (right.Length > 0)
                        IdleUi.Text(right, cell.xMax - IdleUi.Width(right, sc * 0.85f) - line * 0.4f, ty, sc * 0.85f, rightColor);
                    y += rowTall;
                }

                // ---- the card
                int k = Mathf.Clamp(Owner.row, 0, Rows - 1);
                Rect card = new Rect(cardX, top, cardW, bottom - line * 0.5f - top);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cw = card.width - line;
                float cy = card.y + line * 0.4f;
                string[] info;
                if (k < RowHouse)
                    info = new string[] {
                        "Blessing of " + IdleData.BlessingNames[k],
                        string.Format(IdleData.BlessingText[k], IdleData.BlessingValue(k, s.level)) + " for one run, at level " + s.level + ".",
                        "Enter makes it the blessing the hero buys before each run (" + IdleData.BlessingPrice(s.level) + " gold now, more every level); Enter again stops it. One blessing at a time.",
                        (s.blessed > 0) ? "On the hero now: " + IdleData.BlessingNames[s.blessed - 1] + "." : "No blessing on the hero right now." };
                else if (k == RowHouse)
                    info = new string[] {
                        "House",
                        "Living in " + IdleData.HouseNames[s.house] + ": time away counts up to " + (IdleData.OfflineCap(s.house) / 3600) + " hours, and the home furnace smelts "
                            + (s.house > 0 ? (100 * 4 / (4 + s.house)) + "% of the usual time." : "at the usual speed."),
                        (s.house >= IdleData.HouseTop) ? "Nothing grander for sale." : "Next, " + IdleData.HouseNames[s.house + 1] + " for " + IdleData.HousePrice[s.house + 1]
                            + " gold: " + (IdleData.OfflineCap(s.house + 1) / 3600) + " hours away and a faster furnace. Enter buys it.",
                        "Under auto-smith the hero moves up once the next house costs at most half the gold." };
                else
                {
                    int skill = IdleData.TrainSkills[k - FirstSkill];
                    info = new string[] {
                        Spaced(skill),
                        "Now " + s.skills[skill] + ". The next point costs " + IdleData.TrainGold(s.skills[skill]) + " gold (Enter); the price grows with the square of the skill.",
                        "The trainer teaches up to 30 + 3 per level (" + IdleData.TrainCap(s.level) + " now). Trained points do not count toward the hero's level.",
                        IdleSim.Uses(w, skill) ? "Under auto-smith the hero trains the skills in use with up to a quarter of the gold." : "The hero does not use this skill now; auto-smith leaves it alone." };
                }
                IdleUi.Text(info[0], cx, cy, sc * 1.2f, IdleUi.Gold);
                cy += line * 1.5f;
                for (int i = 1; i < info.Length; i++)
                    cy = Wrap(info[i], cx, cy, cw, sc * 0.85f, line * 0.95f, (i == info.Length - 1) ? IdleUi.Muted : IdleUi.White);
                if (Owner.note.Length > 0)
                    Wrap(Owner.note, cx, cy + line * 0.6f, cw, sc * 0.9f, line * 0.95f, IdleUi.Gold);

                float gap = Mathf.Round(line * 0.3f);
                float bx = x;
                Buttons.Begin();
                bx += Buttons.Add(BtnUse, (k < RowHouse) ? "Choose (Enter)" : (k == RowHouse) ? "Buy (Enter)" : "Train (Enter)", bx, bottom, tall, false) + gap;
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
