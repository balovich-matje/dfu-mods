// Idle Adventurer - the spellbook: the spells the hero knows and the ones the Mages Guild sells.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace IdleAdventurer
{
    public class IdleSpellsWindow : DaggerfallPopupWindow
    {
        const int BtnLearn = 1, BtnBack = 2, BtnRank = 3;

        SpellsCanvas canvas;
        int row;
        string note = "";

        public IdleSpellsWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new SpellsCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        void Learn(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.spellsKnown[row] != 0)
                note = IdleData.SpellNames[row] + " is already known.";
            else if (IdleSim.LearnSpell(w, row))
            {
                note = "Learned " + IdleData.SpellNames[row] + ".";
                DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                IdleStore.Save();
            }
            else
                note = "Not enough gold: " + IdleData.SpellNames[row] + " costs " + IdleData.SpellPrice[row] + ".";
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
                row = Mathf.Min(IdleData.SpellKinds - 1, row + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow))
                row = Mathf.Max(0, row - 1);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || click == BtnLearn)
                Learn(w);
            if (Input.GetKeyDown(KeyCode.U) || click == BtnRank)
            {
                string why = IdleSim.CannotRankUp(w.s, row);
                if (why != null)
                    note = why;
                else
                {
                    IdleSim.RankUp(w.s, row);
                    note = IdleData.SpellNames[row] + " is now rank " + IdleSim.Roman(IdleSim.Rank(w.s, row) + 1) + ".";
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

        class SpellsCanvas : BaseScreenComponent
        {
            public IdleSpellsWindow Owner;
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
                IdleUi.Text("Spells", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.6f;
                int most = IdleSim.MaxMagicka(s);
                IdleUi.Text("Magicka " + s.magicka + "/" + most + "    Gold " + s.gold, x, y, sc, IdleUi.White);
                y += line;
                IdleUi.Box(new Rect(x, y, listW * 0.5f, Mathf.Round(line * 0.3f)), new Color(1f, 1f, 1f, 0.08f));
                IdleUi.Box(new Rect(x, y, listW * 0.5f * Mathf.Clamp01(s.magicka / (float)Mathf.Max(1, most)), Mathf.Round(line * 0.3f)), IdleUi.Blue);
                y += line * 0.9f;

                rows.Clear();
                float rowTall = Mathf.Round(line * 1.3f);
                for (int i = 0; i < IdleData.SpellKinds; i++)
                {
                    Rect cell = new Rect(x, y, listW, rowTall - 4f);
                    rows.Add(cell);
                    bool selected = Owner.row == i;
                    Color fill = selected ? new Color(0.72f, 0.58f, 0.18f, 0.32f) : cell.Contains(IdleUi.Mouse) ? new Color(1f, 1f, 1f, 0.09f) : new Color(1f, 1f, 1f, 0.04f);
                    IdleUi.Box(cell, fill);
                    if (selected)
                        IdleUi.Box(new Rect(cell.x, cell.yMax - 2f, cell.width, 2f), IdleUi.Line);
                    bool known = s.spellsKnown[i] != 0;
                    float ty = cell.y + (cell.height - line * 0.7f) / 2f;
                    IdleUi.Text(IdleData.SpellNames[i] + (known ? " " + IdleSim.Roman(IdleSim.Rank(s, i) + 1) : ""), cell.x + line * 0.4f, ty, sc * 0.95f, known ? IdleUi.White : IdleUi.Muted);
                    string school = ((DFCareer.Skills)IdleData.SpellSchool[i]).ToString();
                    IdleUi.Text(school, cell.x + listW * 0.38f, ty, sc * 0.85f, IdleUi.Muted);
                    string right = known ? "known" : IdleData.SpellPrice[i] + " gold";
                    IdleUi.Text(right, cell.xMax - IdleUi.Width(right, sc * 0.9f) - line * 0.4f, ty, sc * 0.9f,
                        known ? IdleUi.Good : (s.gold >= IdleData.SpellPrice[i]) ? IdleUi.Gold : IdleUi.Bad);
                    y += rowTall;
                }
                y += line * 0.3f;
                if (Owner.note.Length > 0)
                    IdleUi.Text(Owner.note, x, y, sc * 0.9f, IdleUi.Gold);

                // ---- the selected spell
                int k = Mathf.Clamp(Owner.row, 0, IdleData.SpellKinds - 1);
                Rect card = new Rect(cardX, top, cardW, bottom - line * 0.5f - top);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cw = card.width - line;
                float cy = card.y + line * 0.4f;
                IdleUi.Text(IdleData.SpellNames[k], cx, cy, sc * 1.2f, IdleUi.Gold);
                cy += line * 1.5f;
                int skill = s.skills[IdleData.SpellSchool[k]];
                string[] info = {
                    "At level " + s.level + ": " + IdleSim.SpellText(s, k) + ".",
                    "Costs " + IdleSim.Cost(s, k) + " magicka now. " + ((DFCareer.Skills)IdleData.SpellSchool[k]) + " is " + skill
                        + "; every cast trains it, and a higher skill makes the spell cheaper.",
                    (s.spellsKnown[k] == 0) ? "Sold by the Mages Guild for " + IdleData.SpellPrice[k] + " gold."
                        : (IdleSim.Rank(s, k) + 1 >= IdleData.RankTop) ? "Known, rank X: as strong as it gets."
                        : "Known, rank " + IdleSim.Roman(IdleSim.Rank(s, k) + 1) + ". Rank " + IdleSim.Roman(IdleSim.Rank(s, k) + 2) + " (U): " + IdleData.RankGold(k, IdleSim.Rank(s, k) + 1)
                          + " gold, +15% strength and 4% cheaper. Auto-smith also pays for ranks of spells the orders use.",
                    "",
                    "Use spells through the orders (O in the hub): cast a damage spell, cast Heal, Shield, Paralysis or Spell Shield. Between fights the hero casts Heal before drinking potions.",
                    "",
                    "Magicka is full when the hero sets out and comes back while walking between fights: 1.5% a second, more with Willpower. Intelligence and level raise the most.",
                    "Some enemies cast too: imps, orc shamans, wraiths, daedra, vampires, liches and enemy mages. Their spells skip armour; Willpower and Spell Shield resist them.",
                };
                for (int i = 0; i < info.Length; i++)
                    cy = Wrap(info[i], cx, cy, cw, sc * 0.85f, line * 0.9f, (i >= 3) ? IdleUi.Muted : IdleUi.White);

                float gap = Mathf.Round(line * 0.3f);
                float bx = x;
                Buttons.Begin();
                bx += Buttons.Add(BtnLearn, "Learn (Enter)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnRank, "Rank up (U)", bx, bottom, tall, false) + gap;
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
