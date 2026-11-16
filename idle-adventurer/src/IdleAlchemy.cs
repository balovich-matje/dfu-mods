// Idle Adventurer - the alchemist (poison recipes and the vial bought before each run) and the calling picker for a
// new hero.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
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
                        + " Critical strike and backstabbing raise the chance; coated hits train backstabbing.",
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
    // A new hero picks one of the game's 18 classes. They come in the game's own three groups; the group decides how
    // the hero plays, the class file gives attributes, skills, health, magicka and what it may not use.
    public class IdleCallingWindow : DaggerfallPopupWindow
    {
        const int BtnPick = 1, BtnBack = 2;
        static readonly int[] Groups = { IdleData.ClassMage, IdleData.ClassRogue, IdleData.ClassWarrior };
        static readonly string[] GroupNames = { "Mages", "Thieves", "Warriors" };

        CallingCanvas canvas;
        int choice;
        bool replaces;
        int shownFor = -1;
        string[] details = new string[0];

        public IdleCallingWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous, bool replacesHero)
            : base(uiManager, previous)
        {
            replaces = replacesHero;
            IdleWorld w = IdleStore.W;
            choice = (w != null && w.s.career >= 0) ? w.s.career : 16;     // the game's Warrior
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new CallingCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        void Pick(int career)
        {
            IdleStore.NewCareer(career);
            IdleStore.W.s.classChosen = 1;
            IdlePlaces.Name(IdleStore.W);
            IdleStore.W.record = true;
            IdleStore.W.Emit(IdleData.EvInfo, "A new " + IdleSim.CallingName(IdleStore.W.s).ToLower() + " arrives in town");
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
            int click = canvas.Buttons.Clicked();
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            // Six rows in each of three columns: up and down within a group, left and right across.
            int column = choice / 6, row = choice % 6;
            if (Input.GetKeyDown(KeyCode.DownArrow)) row = (row + 1) % 6;
            if (Input.GetKeyDown(KeyCode.UpArrow)) row = (row + 5) % 6;
            if (Input.GetKeyDown(KeyCode.RightArrow)) column = (column + 1) % 3;
            if (Input.GetKeyDown(KeyCode.LeftArrow)) column = (column + 2) % 3;
            choice = column * 6 + row;
            int at = canvas.RowAt(IdleUi.Mouse);
            if (Input.GetMouseButtonDown(0) && at >= 0)
            {
                choice = at;
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || click == BtnPick)
                Pick(choice);
        }

        // What the panel says about a class: built once per class shown.
        string[] Details(int career)
        {
            if (shownFor == career)
                return details;
            shownFor = career;
            List<string> lines = new List<string>();
            DFCareer c = IdleSim.Career(career);
            if (c == null)
            {
                details = new string[] { "The game's class file could not be read." };
                return details;
            }
            int calling = IdleData.CareerCalling(career);
            lines.Add("Plays as " + (calling == IdleData.ClassMage ? "a mage" : calling == IdleData.ClassRogue ? "a thief" : "a warrior") + ": " + IdleData.ClassText[calling]);
            lines.Add("Strength " + c.Strength + ", intelligence " + c.Intelligence + ", willpower " + c.Willpower + ", agility " + c.Agility
                + ", endurance " + c.Endurance + ", personality " + c.Personality + ", speed " + c.Speed + ", luck " + c.Luck);
            lines.Add("Primary skills (30): " + Skills(c.PrimarySkill1, c.PrimarySkill2, c.PrimarySkill3));
            lines.Add("Major skills (22): " + Skills(c.MajorSkill1, c.MajorSkill2, c.MajorSkill3));
            lines.Add("Minor skills (14): " + Skills(c.MinorSkill1, c.MinorSkill2, c.MinorSkill3) + ", " + Skills(c.MinorSkill4, c.MinorSkill5, c.MinorSkill6));

            IdleWorld preview = IdleSim.NewCareer(1, "Preview", career);
            lines.Add("Health " + preview.s.maxHealth + ", up to " + IdleSim.HitPointsPerLevel(preview.s) + " more a level (the game's " + c.HitPointsPerLevel
                + ")    Magicka " + IdleSim.MaxMagicka(preview.s) + " (" + IdleSim.MagickaPercent(preview.s) + "% of usual)    Poison lands " + IdleSim.PoisonChance(preview.s) + "% of coated hits");

            List<string> banned = new List<string>();
            string[] weaponNames = { "short blades", "long blades", "hand-to-hand", "axes", "blunt weapons", "bows" };
            for (int i = 0; i < weaponNames.Length; i++)
                if (c.IsProficiencyForbidden((DFCareer.ProficiencyFlags)(1 << i)))
                    banned.Add(weaponNames[i]);
            string[] armorNames = { "leather armor", "chain armor", "plate armor" };
            for (int i = 0; i < armorNames.Length; i++)
                if (c.IsArmorForbidden((DFCareer.ArmorFlags)(1 << i)))
                    banned.Add(armorNames[i]);
            string[] shieldNames = { "bucklers", "round shields", "kite shields", "tower shields" };
            for (int i = 0; i < shieldNames.Length; i++)
                if (c.IsShieldForbidden((DFCareer.ShieldFlags)(1 << i)))
                    banned.Add(shieldNames[i]);
            for (int i = 0; i < IdleData.Metals; i++)
                if (c.IsMaterialForbidden((DFCareer.MaterialFlags)(1 << i)))
                    banned.Add(IdleData.MetalNames[i].ToLower());
            lines.Add("May not use: " + (banned.Count > 0 ? string.Join(", ", banned.ToArray()) : "nothing is forbidden"));

            string gear = "";
            for (int i = 0; i < preview.gear.Length; i++)
                if (preview.gear[i] != null)
                    gear += (gear.Length > 0 ? ", " : "") + IdleSim.Name(preview.gear[i]);
            lines.Add("Starts with: " + gear + ", " + preview.s.gold + " gold");
            details = lines.ToArray();
            return details;
        }

        static string Skills(DFCareer.Skills a, DFCareer.Skills b, DFCareer.Skills c)
        {
            return Spaced(a) + ", " + Spaced(b) + ", " + Spaced(c);
        }

        // "CriticalStrike" as "critical strike".
        static string Spaced(DFCareer.Skills skill)
        {
            string name = skill.ToString();
            string o = "";
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]))
                    o += " ";
                o += char.ToLower(name[i]);
            }
            return o;
        }

        class CallingCanvas : BaseScreenComponent
        {
            public IdleCallingWindow Owner;
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
                if (Owner == null)
                    return;
                float W = Screen.width;
                float H = Screen.height;
                IdleUi.Box(new Rect(0, 0, W, H), Color.black);
                float width = Mathf.Round(Mathf.Min(W * 0.8f, H * 1.5f));
                float height = Mathf.Round(H * 0.86f);
                Rect r = new Rect(Mathf.Round((W - width) / 2f), Mathf.Round((H - height) / 2f), width, height);
                IdleUi.Frame(r, new Color(0.06f, 0.055f, 0.05f, 1f));
                float line = Mathf.Round(H * 0.034f);
                float sc = IdleUi.Scale(line);
                float pad = Mathf.Round(H * 0.025f);
                float x = r.x + pad;
                float inner = width - 2f * pad;
                float y = r.y + pad;

                IdleUi.Text(Owner.replaces ? "A new hero: choose a class (this replaces the current hero)" : "Choose your hero's class", x, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.8f;

                // The three groups, side by side.
                float gap = Mathf.Round(line * 0.6f);
                float colW = Mathf.Floor((inner - 2f * gap) / 3f);
                float rowH = Mathf.Round(line * 1.1f);
                rows.Clear();
                for (int g = 0; g < 3; g++)
                {
                    float cx = x + g * (colW + gap);
                    IdleUi.Text(GroupNames[g], cx, y, sc * 1.1f, IdleUi.Muted);
                    for (int k = 0; k < 6; k++)
                    {
                        int career = g * 6 + k;
                        Rect row = new Rect(cx, y + line * 1.3f + k * rowH, colW, rowH - 2f);
                        rows.Add(row);
                        bool on = Owner.choice == career;
                        bool hover = row.Contains(IdleUi.Mouse);
                        IdleUi.Box(row, on ? new Color(0.36f, 0.29f, 0.09f, 1f) : hover ? new Color(0.16f, 0.13f, 0.07f, 1f) : new Color(0.09f, 0.08f, 0.065f, 1f));
                        DFCareer c = IdleSim.Career(career);
                        string name = (c != null) ? c.Name : ((ClassCareers)career).ToString();
                        IdleUi.Text(name, row.x + line * 0.4f, row.y + (rowH - line) / 2f + line * 0.1f, sc, on ? IdleUi.Gold : IdleUi.White);
                    }
                }
                y += line * 1.3f + 6 * rowH + line * 0.8f;

                // The chosen class.
                DFCareer chosen = IdleSim.Career(Owner.choice);
                IdleUi.Text((chosen != null) ? chosen.Name : "", x, y, sc * 1.25f, IdleUi.Gold);
                y += line * 1.4f;
                string[] lines = Owner.Details(Owner.choice);
                float bottom = r.yMax - pad - line * 1.4f;
                for (int i = 0; i < lines.Length && y < bottom - line; i++)
                    y = Wrap(lines[i], x, y, inner, sc * 0.9f, line * 0.95f, (i == 0) ? IdleUi.White : IdleUi.Muted);

                float tall = Mathf.Round(line * 1.25f);
                float by = r.yMax - pad - tall;
                Buttons.Begin();
                float bx = x;
                bx += Buttons.Add(BtnPick, "Start as " + ((chosen != null) ? chosen.Name : "this class") + " (Enter)", bx, by, tall, false) + gap;
                bx += Buttons.Add(BtnBack, Owner.replaces ? "Keep the current hero (Esc)" : "Back (Esc)", bx, by, tall, false) + gap;
                IdleUi.Text("Arrows to choose; click a class to see it.", bx + gap, by + (tall - line) / 2f + line * 0.1f, sc * 0.85f, IdleUi.Muted);
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
