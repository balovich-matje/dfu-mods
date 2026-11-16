// Idle Adventurer - the alchemist (poison recipes and the vial bought before each run) and the calling picker for a
// new hero.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace IdleAdventurer
{
    public class IdleAlchemyWindow : DaggerfallPopupWindow
    {
        const int BtnLearn = 1, BtnCoat = 2, BtnBack = 3;

        AlchemyCanvas canvas;
        int row;
        string note = "";

        public IdleAlchemyWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new AlchemyCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
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
                row = Mathf.Min(IdleData.PoisonKinds - 1, row + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow))
                row = Mathf.Max(0, row - 1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || click == BtnLearn)
            {
                string why = IdleSim.CannotLearnPoison(s, row);
                if (why != null)
                    note = why;
                else
                {
                    IdleSim.LearnPoison(s, row);
                    note = "Learned " + IdleData.PoisonNames[row] + ".";
                    DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                    IdleStore.Save();
                }
            }
            if (Input.GetKeyDown(KeyCode.C) || click == BtnCoat)
            {
                s.noPoison = !s.noPoison;
                note = s.noPoison ? "The weapon goes out clean." : "The hero buys a vial of the best known poison before each run.";
            }
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            IdleClock.Tick();
        }

        class AlchemyCanvas : BaseScreenComponent
        {
            public IdleAlchemyWindow Owner;
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
                IdleUi.Text("Alchemist", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.6f;
                IdleUi.Text("Gold " + s.gold + "    Spent here " + s.alchemyGold + "    Poisoned hits " + s.poisonHits, x, y, sc, IdleUi.White);
                y += line * 1.1f;
                string coat = s.noPoison ? "Coating is off." : (s.poisonsKnown == 0) ? "No recipe known yet."
                    : "Each run the hero buys a vial of " + IdleData.PoisonNames[s.poisonsKnown - 1] + " (or the best the purse allows).";
                IdleUi.Text(coat, x, y, sc * 0.85f, IdleUi.Muted);
                y += line * 1.3f;

                rows.Clear();
                float rowTall = Mathf.Round(line * 1.3f);
                for (int i = 0; i < IdleData.PoisonKinds; i++)
                {
                    Rect cell = new Rect(x, y, listW, rowTall - 4f);
                    rows.Add(cell);
                    bool selected = Owner.row == i;
                    Color fill = selected ? new Color(0.72f, 0.58f, 0.18f, 0.32f) : cell.Contains(IdleUi.Mouse) ? new Color(1f, 1f, 1f, 0.09f) : new Color(1f, 1f, 1f, 0.04f);
                    IdleUi.Box(cell, fill);
                    if (selected)
                        IdleUi.Box(new Rect(cell.x, cell.yMax - 2f, cell.width, 2f), IdleUi.Line);
                    float ty = cell.y + (cell.height - line * 0.7f) / 2f;
                    bool known = i < s.poisonsKnown;
                    IdleUi.Text(IdleData.PoisonNames[i], cell.x + line * 0.4f, ty, sc * 0.95f, known ? IdleUi.White : IdleUi.Muted);
                    IdleUi.Text("+" + IdleSim.PoisonHit(s, i) + " a hit", cell.x + listW * 0.38f, ty, sc * 0.85f, IdleUi.Muted);
                    IdleUi.Text(IdleData.PoisonVial[i] + " a vial", cell.x + listW * 0.58f, ty, sc * 0.85f, IdleUi.Muted);
                    string right = known ? "known" : IdleData.PoisonRecipe[i] + " gold";
                    IdleUi.Text(right, cell.xMax - IdleUi.Width(right, sc * 0.9f) - line * 0.4f, ty, sc * 0.9f,
                        known ? IdleUi.Good : (i == s.poisonsKnown && s.gold >= IdleData.PoisonRecipe[i]) ? IdleUi.Gold : IdleUi.Bad);
                    y += rowTall;
                }
                y += line * 0.4f;
                if (Owner.note.Length > 0)
                    IdleUi.Text(Owner.note, x, y, sc * 0.9f, IdleUi.Gold);

                int k = Mathf.Clamp(Owner.row, 0, IdleData.PoisonKinds - 1);
                Rect card = new Rect(cardX, top, cardW, bottom - line * 0.5f - top);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cw = card.width - line;
                float cy = card.y + line * 0.4f;
                IdleUi.Text(IdleData.PoisonNames[k], cx, cy, sc * 1.2f, IdleUi.Gold);
                cy += line * 1.5f;
                string[] info = {
                    "On a coated weapon, " + IdleSim.PoisonChance(s) + "% of hits add " + IdleSim.PoisonHit(s, k) + " poison damage on this floor (5% more every floor down)."
                        + (s.heroClass == IdleData.ClassRogue ? " Rogues land it far more often." : " Rogues land it far more often: " + IdleData.RoguePoisonChance + "%."),
                    "A vial lasts one run and costs " + IdleData.PoisonVial[k] + " gold. The recipe costs " + IdleData.PoisonRecipe[k] + " once; recipes are learned in order (Enter).",
                    "",
                    "Skeletons, zombies, ghosts, mummies, wraiths, vampires, liches, gargoyles, atronachs and daedra shrug poison off.",
                    "Auto-smith lets a rogue learn the next recipe once it is well within means. C turns coating off and on.",
                };
                for (int i = 0; i < info.Length; i++)
                    cy = Wrap(info[i], cx, cy, cw, sc * 0.85f, line * 0.9f, (i >= 2) ? IdleUi.Muted : IdleUi.White);

                float gap = Mathf.Round(line * 0.3f);
                float bx = x;
                Buttons.Begin();
                bx += Buttons.Add(BtnLearn, "Learn (Enter)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnCoat, "Coat weapon (C)", bx, bottom, tall, !s.noPoison) + gap;
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

    // Picking a calling starts a new hero. Shown for a brand-new hero, and from Shift+N in the hub.
    public class IdleCallingWindow : DaggerfallPopupWindow
    {
        CallingCanvas canvas;
        int choice;
        bool replaces;

        public IdleCallingWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous, bool replacesHero)
            : base(uiManager, previous)
        {
            replaces = replacesHero;
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new CallingCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        void Pick(int heroClass)
        {
            IdleStore.NewHero(heroClass);
            IdleStore.W.s.classChosen = 1;
            IdlePlaces.Name(IdleStore.W);
            IdleStore.W.record = true;
            IdleStore.W.Emit(IdleData.EvInfo, "A new " + IdleData.ClassNames[heroClass].ToLower() + " arrives in town");
            IdleStore.Save();
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            CloseWindow();
        }

        public override void Update()
        {
            base.Update();
            if (canvas == null)
                return;
            canvas.Size = new Vector2(Screen.width, Screen.height);
            if (Input.GetKeyDown(KeyCode.RightArrow))
                choice = Mathf.Min(IdleData.Classes - 1, choice + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                choice = Mathf.Max(0, choice - 1);
            if (Input.GetKeyDown(KeyCode.Alpha1)) choice = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) choice = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3)) choice = 2;
            int hover = canvas.CardAt(IdleUi.Mouse);
            if (Input.GetMouseButtonDown(0) && hover >= 0)
            {
                Pick(hover);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                Pick(choice);
        }

        class CallingCanvas : BaseScreenComponent
        {
            public IdleCallingWindow Owner;
            readonly List<Rect> cards = new List<Rect>();

            public int CardAt(Vector2 mouse)
            {
                for (int i = 0; i < cards.Count; i++)
                    if (cards[i].Contains(mouse))
                        return i;
                return -1;
            }

            public override void Draw()
            {
                base.Draw();
                if (Owner == null)
                    return;
                float W = Screen.width;
                float H = Screen.height;
                IdleUi.Box(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.92f));
                float line = Mathf.Round(H * 0.034f);
                float sc = IdleUi.Scale(line);
                float cardW = Mathf.Round(Mathf.Min(W * 0.28f, H * 0.5f));
                float cardH = Mathf.Round(H * 0.42f);
                float gap = Mathf.Round(line);
                float total = cardW * IdleData.Classes + gap * (IdleData.Classes - 1);
                float x = Mathf.Round((W - total) / 2f);
                float y = Mathf.Round((H - cardH) / 2f);
                string title = Owner.replaces ? "A new hero: choose a calling (this replaces the current hero)" : "Choose your hero's calling";
                IdleUi.Text(title, Mathf.Round((W - IdleUi.Width(title, sc * 1.2f)) / 2f), y - line * 2.4f, sc * 1.2f, IdleUi.Gold);
                cards.Clear();
                for (int i = 0; i < IdleData.Classes; i++)
                {
                    Rect c = new Rect(x + i * (cardW + gap), y, cardW, cardH);
                    cards.Add(c);
                    bool on = Owner.choice == i || c.Contains(IdleUi.Mouse);
                    IdleUi.Frame(c, on ? new Color(0.72f, 0.58f, 0.18f, 0.25f) : IdleUi.Fill);
                    IdleUi.Text((i + 1) + "  " + IdleData.ClassNames[i], c.x + line * 0.6f, c.y + line * 0.6f, sc * 1.25f, on ? IdleUi.Gold : IdleUi.White);
                    Wrap(IdleData.ClassText[i], c.x + line * 0.6f, c.y + line * 2.4f, cardW - line * 1.2f, sc * 0.9f, line * 1.0f, IdleUi.White);
                }
                string help = "Arrows or 1-3, Enter to pick; or click a card. Esc keeps the current hero.";
                IdleUi.Text(help, Mathf.Round((W - IdleUi.Width(help, sc * 0.8f)) / 2f), y + cardH + line * 1.2f, sc * 0.8f, IdleUi.Muted);
            }

            static float Wrap(string s, float x, float y, float width, float scale, float lineHeight, Color c)
            {
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
