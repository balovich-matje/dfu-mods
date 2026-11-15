// Idle Adventurer - the hero screen: attributes, skills, worn gear and the stash, with equipping and selling.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace IdleAdventurer
{
    public class IdleHeroWindow : DaggerfallPopupWindow
    {
        const int BtnAct = 1, BtnSell = 2, BtnAuto = 3, BtnBack = 4, BtnShop = 5, BtnPack = 6;

        static readonly string[] statNames = { "Strength", "Intelligence", "Willpower", "Agility", "Endurance", "Personality", "Speed", "Luck" };
        static readonly int[] shownSkills = {
            (int)DFCareer.Skills.LongBlade, (int)DFCareer.Skills.ShortBlade, (int)DFCareer.Skills.Axe, (int)DFCareer.Skills.BluntWeapon,
            (int)DFCareer.Skills.HandToHand, (int)DFCareer.Skills.CriticalStrike, (int)DFCareer.Skills.Dodging, (int)DFCareer.Skills.Medical };
        static readonly string[] skillNames = { "Long blade", "Short blade", "Axe", "Blunt weapon", "Hand to hand", "Critical strike", "Dodging", "Medical" };

        HeroCanvas canvas;
        int cursor = IdleData.SlotWeapon;       // 0-8 worn slots, 9 and up the stash
        string note = "";
        bool shopping;                          // the right-hand list shows the shop instead of the stash

        List<DaggerfallUnityItem> Side(IdleWorld w)
        {
            return shopping ? w.shop : w.stash;
        }

        public IdleHeroWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new HeroCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        DaggerfallUnityItem At(IdleWorld w, int cursor)
        {
            if (cursor < IdleData.SlotCount)
                return w.gear[cursor];
            int i = cursor - IdleData.SlotCount;
            List<DaggerfallUnityItem> side = Side(w);
            return (i < side.Count) ? side[i] : null;
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
                int row = canvas.RowAt(IdleUi.Mouse);
                if (row >= 0)
                    cursor = row;
            }

            if (Input.GetKeyDown(KeyCode.S) || click == BtnShop)
            {
                shopping = !shopping;
                note = "";
                if (cursor >= IdleData.SlotCount)
                    cursor = IdleData.SlotCount;
            }
            if (Input.GetKeyDown(KeyCode.P) || click == BtnPack)
            {
                if (s.packLevel >= IdleData.PackLevels)
                    note = "The pack cannot get any bigger";
                else if (IdleSim.BuyPack(w))
                {
                    note = "Bigger pack bought: the hero now carries " + (int)IdleSim.Capacity(s) + " kg";
                    DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                    IdleStore.Save();
                }
                else
                    note = "A bigger pack costs " + IdleData.PackCost(s.packLevel) + " gold";
            }
            List<DaggerfallUnityItem> side = Side(w);

            // Arrow keys: up and down inside a list, left and right between worn gear and the stash.
            bool inStash = cursor >= IdleData.SlotCount;
            if (Input.GetKeyDown(KeyCode.DownArrow))
                cursor++;
            if (Input.GetKeyDown(KeyCode.UpArrow))
                cursor--;
            if (Input.GetKeyDown(KeyCode.RightArrow) && !inStash && side.Count > 0)
                cursor = IdleData.SlotCount + Mathf.Min(cursor, side.Count - 1);
            else if (Input.GetKeyDown(KeyCode.LeftArrow) && inStash)
                cursor = Mathf.Min(cursor - IdleData.SlotCount, IdleData.SlotCount - 1);
            else if (inStash)
                cursor = Mathf.Clamp(cursor, IdleData.SlotCount, IdleData.SlotCount + Mathf.Max(0, side.Count - 1));
            else
                cursor = Mathf.Clamp(cursor, 0, IdleData.SlotCount - 1);
            if (cursor >= IdleData.SlotCount && side.Count == 0)
                cursor = IdleData.SlotCount - 1;
            inStash = cursor >= IdleData.SlotCount;

            DaggerfallUnityItem item = At(w, cursor);
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || click == BtnAct) && item != null)
            {
                if (inStash && shopping)
                {
                    if (w.stash.Count >= IdleData.StashSize)
                        note = "The stash is full. Sell something first.";
                    else if (IdleSim.Buy(w, cursor - IdleData.SlotCount))
                    {
                        note = "Bought " + item.LongName + ". It is in the stash.";
                        DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                    }
                    else
                        note = "Not enough gold: " + item.LongName + " costs " + IdleSim.ShopPrice(item);
                }
                else if (inStash)
                {
                    if (IdleSim.EquipFromStash(w, cursor - IdleData.SlotCount))
                    {
                        note = "Now using " + item.LongName;
                        DaggerfallUI.Instance.PlayOneShot(SoundClips.EquipLeather);
                    }
                }
                else if (w.stash.Count >= IdleData.StashSize)
                {
                    note = "The stash is full. Sell something first.";
                }
                else if (IdleSim.Unequip(w, cursor))
                {
                    note = item.LongName + " put in the stash";
                }
                IdleStore.Save();
            }
            if ((Input.GetKeyDown(KeyCode.X) || click == BtnSell) && item != null && inStash && !shopping)
            {
                int gold = IdleSim.SellFromStash(w, cursor - IdleData.SlotCount);
                note = "Sold " + item.LongName + " for " + gold + " gold";
                DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);
                IdleStore.Save();
            }
            if (Input.GetKeyDown(KeyCode.A) || click == BtnAuto)
                s.autoEquip = (s.autoEquip == 0) ? 1 : 0;
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }

            IdleClock.Tick();
        }

        // ------------------------------------------------------------------ item numbers

        static int ArmorOf(DaggerfallUnityItem item, int slot)
        {
            return (slot == IdleData.SlotShield) ? item.GetShieldArmorValue() : item.GetMaterialArmorValue();
        }

        // The short number shown at the end of a row: damage for a weapon, armor for anything else.
        static string Short(DaggerfallUnityItem item)
        {
            int slot = IdleSim.SlotFor(item);
            if (slot == IdleData.SlotWeapon)
            {
                int mod = item.GetWeaponMaterialModifier();
                return (item.GetBaseDamageMin() + mod) + "-" + (item.GetBaseDamageMax() + mod);
            }
            if (slot >= 0)
                return Signed(ArmorOf(item, slot));
            return "";
        }

        static string Signed(int value)
        {
            return (value >= 0 ? "+" : "") + value;
        }

        static int Condition(DaggerfallUnityItem item)
        {
            return (item.maxCondition > 0) ? Mathf.Clamp(item.currentCondition * 100 / item.maxCondition, 0, 100) : 100;
        }

        class HeroCanvas : BaseScreenComponent
        {
            public IdleHeroWindow Owner;
            public readonly IdleButtons Buttons = new IdleButtons();
            readonly List<Rect> rowPlaces = new List<Rect>();
            readonly List<int> rowIds = new List<int>();
            bool iconsBroken;

            public int RowAt(Vector2 mouse)
            {
                for (int i = 0; i < rowPlaces.Count; i++)
                    if (rowPlaces[i].Contains(mouse))
                        return rowIds[i];
                return -1;
            }

            // The game keeps its own cache of these pictures.
            Texture2D Icon(DaggerfallUnityItem item)
            {
                if (iconsBroken)
                    return null;
                try
                {
                    return DaggerfallUnity.Instance.ItemHelper.GetInventoryImage(item).texture;
                }
                catch (Exception ex)
                {
                    iconsBroken = true;
                    Debug.LogError("[Idle] Item pictures are not available: " + ex.Message);
                    return null;
                }
            }

            // One item line: picture, name, condition and its damage or armor number.
            void Row(int id, DaggerfallUnityItem item, string label, Rect r, float sc, bool selected, bool better)
            {
                rowPlaces.Add(r);
                rowIds.Add(id);
                if (selected)
                {
                    IdleUi.Box(r, new Color(0.72f, 0.58f, 0.18f, 0.28f));
                    IdleUi.Box(new Rect(r.x, r.y, 3f, r.height), IdleUi.Line);
                }
                else if (r.Contains(IdleUi.Mouse))
                {
                    IdleUi.Box(r, new Color(1f, 1f, 1f, 0.06f));
                }

                float textY = r.y + (r.height - r.height * 0.74f) / 2f;
                float x = r.x + r.height * 0.3f;
                if (label != null)
                {
                    IdleUi.Text(label, x, textY, sc * 0.85f, IdleUi.Muted);
                    x += r.height * 3.3f;
                }
                if (item == null)
                {
                    IdleUi.Text("nothing", x + r.height * 1.3f, textY, sc, new Color(0.45f, 0.43f, 0.4f, 1f));
                    return;
                }

                Texture2D icon = Icon(item);
                if (icon != null && icon.height > 0)
                {
                    float box = r.height - 4f;
                    float k = Mathf.Min(box / icon.width, box / icon.height);
                    DaggerfallUI.DrawTexture(new Rect(Mathf.Round(x + (box - icon.width * k) / 2f), Mathf.Round(r.y + 2f + (box - icon.height * k) / 2f),
                        Mathf.Round(icon.width * k), Mathf.Round(icon.height * k)), icon, ScaleMode.StretchToFill, true, Color.white);
                }
                x += r.height * 1.3f;

                string number = Short(item);
                float numberX = r.xMax - r.height * 0.3f - IdleUi.Width(number, sc);
                int condition = Condition(item);
                string worn = condition + "%";
                float wornX = r.xMax - r.height * 2.6f - IdleUi.Width(worn, sc * 0.85f);

                // Long names are cut to fit.
                string name = item.LongName;
                while (name.Length > 4 && x + IdleUi.Width(name, sc) > wornX - r.height * 0.3f)
                    name = name.Substring(0, name.Length - 4) + "..";
                IdleUi.Text(name, x, textY, sc, better ? IdleUi.Good : IdleUi.White);
                IdleUi.Text(worn, wornX, textY, sc * 0.85f, (condition < 30) ? IdleUi.Bad : IdleUi.Muted);
                IdleUi.Text(number, numberX, textY, sc, IdleUi.Gold);
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

                float col1 = r.x + pad;
                float col1W = Mathf.Round(width * 0.24f);
                float col2 = col1 + col1W + pad;
                float col2W = Mathf.Round(width * 0.36f);
                float col3 = col2 + col2W + pad;
                float col3W = r.xMax - pad - col3;

                // ---- the hero
                float y = top;
                IdleUi.Text(s.heroName, col1, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.5f;
                IdleUi.Text("Level " + s.level + "    " + IdleSim.PhaseName(s), col1, y, sc * 0.9f, IdleUi.Muted);
                y += line * 1.1f;
                IdleUi.Text("Health " + s.health + "/" + s.maxHealth + "   Magicka " + s.magicka + "/" + IdleSim.MaxMagicka(s), col1, y, sc * 0.9f, IdleUi.White);
                y += line;
                IdleUi.Bar(new Rect(col1, y, col1W, Mathf.Round(line * 0.3f)), (float)s.health / Mathf.Max(1, s.maxHealth), IdleUi.Bad);
                y += line * 0.7f;
                IdleUi.Text("Gold " + s.gold, col1, y, sc, IdleUi.Gold);
                y += line * 1.5f;

                for (int i = 0; i < statNames.Length; i++)
                {
                    IdleUi.Text(statNames[i], col1, y, sc * 0.9f, IdleUi.Muted);
                    string v = s.stats[i].ToString();
                    IdleUi.Text(v, col1 + col1W - IdleUi.Width(v, sc * 0.9f), y, sc * 0.9f, IdleUi.White);
                    y += line * 0.92f;
                }
                y += line * 0.5f;
                for (int i = 0; i < shownSkills.Length; i++)
                {
                    IdleUi.Text(skillNames[i], col1, y, sc * 0.9f, IdleUi.Muted);
                    string v = s.skills[shownSkills[i]].ToString();
                    IdleUi.Text(v, col1 + col1W - IdleUi.Width(v, sc * 0.9f), y, sc * 0.9f, IdleUi.White);
                    y += line * 0.92f;
                }
                y += line * 0.5f;
                int armor = 0;
                for (int part = 0; part < 7; part++)
                    armor += (100 - IdleSim.ArmorAt(w, part)) / 5;
                IdleUi.Text("Pack " + (int)IdleSim.CarriedWeight(w) + " of " + (int)IdleSim.Capacity(s) + " kg", col1, y, sc * 0.9f, IdleUi.White);
                y += line * 0.92f;
                IdleUi.Text("Armor " + armor + "    Attack every " + (IdleData.AttackInterval(s.stats[IdleData.StatSpeed]) / 10f).ToString("0.0") + " s",
                    col1, y, sc * 0.9f, IdleUi.White);

                // ---- worn gear
                rowPlaces.Clear();
                rowIds.Clear();
                int cursor = Owner.cursor;
                y = top;
                IdleUi.Text("Worn", col2, y, sc * 1.1f, IdleUi.Gold);
                y += line * 1.4f;
                float rowTall = Mathf.Round(line * 1.15f);
                for (int slot = 0; slot < IdleData.SlotCount; slot++)
                {
                    Row(slot, w.gear[slot], IdleData.SlotNames[slot], new Rect(col2, y, col2W, rowTall), sc, cursor == slot, false);
                    y += rowTall;
                }
                y += line * 0.6f;

                // ---- the selected item
                DaggerfallUnityItem item = Owner.At(w, cursor);
                bool shopping = Owner.shopping;
                List<DaggerfallUnityItem> side = Owner.Side(w);
                Rect card = new Rect(col2, y, col2W, bottom - line * 0.5f - y);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cy = card.y + line * 0.4f;
                if (item == null)
                {
                    IdleUi.Text("Nothing here.", cx, cy, sc, IdleUi.Muted);
                }
                else
                {
                    int slot = IdleSim.SlotFor(item);
                    bool inStash = cursor >= IdleData.SlotCount;
                    DaggerfallUnityItem other = (inStash && slot >= 0) ? w.gear[slot] : null;
                    IdleUi.Text(item.LongName, cx, cy, sc * 1.05f, IdleUi.Gold);
                    cy += line * 1.3f;
                    if (slot == IdleData.SlotWeapon)
                    {
                        string text = "Damage " + Short(item) + (other != null ? "   (worn " + Short(other) + ")" : "");
                        IdleUi.Text(text, cx, cy, sc, IdleUi.White);
                        cy += line;
                        int skill = item.GetWeaponSkillIDAsShort();
                        string skillName = ((DFCareer.Skills)skill).ToString();
                        for (int i = 0; i < shownSkills.Length; i++)
                            if (shownSkills[i] == skill)
                                skillName = skillNames[i];
                        int value = (skill >= 0 && skill < s.skills.Length) ? s.skills[skill] : 0;
                        IdleUi.Text("Skill: " + skillName + " " + value + "    To hit " + Signed(item.GetWeaponMaterialModifier() * 10), cx, cy, sc, IdleUi.White);
                        cy += line;
                        if (IdleSim.IsBow(item))
                        {
                            IdleUi.Text("The hero cannot use bows yet", cx, cy, sc, IdleUi.Bad);
                            cy += line;
                        }
                        else if (ItemEquipTable.GetItemHands(item) == ItemHands.Both)
                        {
                            IdleUi.Text("Two-handed: no shield", cx, cy, sc, IdleUi.Blue);
                            cy += line;
                        }
                    }
                    else if (slot >= 0)
                    {
                        string text = "Armor +" + ArmorOf(item, slot) + (other != null ? "   (worn +" + ArmorOf(other, slot) + ")" : "");
                        IdleUi.Text(text, cx, cy, sc, IdleUi.White);
                        cy += line;
                    }
                    IdleUi.Text("Condition " + Condition(item) + "%    Weight " + item.weightInKg.ToString("0.#") + " kg    "
                        + ((inStash && shopping) ? "Costs " + IdleSim.ShopPrice(item) : "Sells for " + IdleSim.SalePrice(item)),
                        cx, cy, sc * 0.9f, IdleUi.Muted);
                    cy += line;
                    if (inStash && slot >= 0 && IdleSim.Works(item) && IdleSim.IsUpgrade(w, item, slot))
                        IdleUi.Text("Better than what is worn", cx, cy, sc, IdleUi.Good);
                }

                // ---- the stash, or the shop
                y = top;
                IdleUi.Text(shopping ? "Shop" : "Stash " + w.stash.Count + "/" + IdleData.StashSize, col3, y, sc * 1.1f, IdleUi.Gold);
                y += line * 1.4f;
                float stashTall = Mathf.Floor(Mathf.Min(rowTall, (bottom - line * 0.5f - y) / IdleData.StashSize));
                float stashScale = IdleUi.Scale(stashTall / 1.15f);
                if (side.Count == 0)
                    IdleUi.Text(shopping ? "Sold out. New stock arrives when the hero comes home." : "Spare weapons and armor the hero brings back are kept here.",
                        col3, y, sc * 0.85f, IdleUi.Muted);
                for (int i = 0; i < side.Count; i++)
                {
                    DaggerfallUnityItem spare = side[i];
                    int slot = IdleSim.SlotFor(spare);
                    bool better = slot >= 0 && IdleSim.Works(spare) && IdleSim.IsUpgrade(w, spare, slot);
                    Rect place = new Rect(col3, y, col3W, stashTall);
                    Row(IdleData.SlotCount + i, spare, null, place, stashScale, cursor == IdleData.SlotCount + i, better);
                    if (shopping)
                    {
                        // The price, between the name and the condition.
                        string price = IdleSim.ShopPrice(spare) + " g";
                        IdleUi.Text(price, place.xMax - place.height * 5.2f - IdleUi.Width(price, stashScale * 0.85f), place.y + (place.height - place.height * 0.74f) / 2f,
                            stashScale * 0.85f, (IdleSim.ShopPrice(spare) <= s.gold) ? IdleUi.Gold : IdleUi.Bad);
                    }
                    y += stashTall;
                }
                if (shopping && side.Count > 0)
                    IdleUi.Text("New stock every time the hero comes home.", col3, y + line * 0.5f, sc * 0.85f, IdleUi.Muted);

                // ---- buttons and the last message
                float gap = Mathf.Round(line * 0.3f);
                float bx = col1;
                Buttons.Begin();
                bx += Buttons.Add(BtnAct, (cursor < IdleData.SlotCount) ? "Take off (Enter)" : shopping ? "Buy (Enter)" : "Equip (Enter)", bx, bottom, tall, false) + gap;
                if (!shopping)
                    bx += Buttons.Add(BtnSell, "Sell (X)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnShop, shopping ? "Stash (S)" : "Shop (S)", bx, bottom, tall, false) + gap;
                if (s.packLevel < IdleData.PackLevels)
                    bx += Buttons.Add(BtnPack, "Bigger pack " + IdleData.PackCost(s.packLevel) + " g (P)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnAuto, "Auto-equip (A)", bx, bottom, tall, s.autoEquip != 0) + gap;
                bx += Buttons.Add(BtnBack, "Back (Esc)", bx, bottom, tall, false) + gap * 3f;
                IdleUi.Text(Owner.note, bx, bottom + (tall - line * 0.74f) / 2f, sc, IdleUi.White);
            }
        }
    }
}
