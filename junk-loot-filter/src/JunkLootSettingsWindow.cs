// Junk & Loot Filter - a Daggerfall Unity mod.
// Settings window: loot filter, sell filter and junk list.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace JunkLootFilter
{
    public class FilterSettingsWindow : DaggerfallPopupWindow
    {
        // Plain ints instead of enums: enums declared in runtime-compiled mods trip DFU's mod loader scan.
        const int TabLoot = 0;
        const int TabSell = 1;
        const int TabJunk = 2;
        const int TabKeys = 3;
        const int TabMisc = 4;
        const int PageMain = 0;
        const int PageWhiteList = 1;
        const int PageBlackList = 2;

        const int VisibleRows = 9;
        const int RowHeight = 10;

        static readonly Color PanelColor = new Color(0.09f, 0.08f, 0.07f, 1f);
        static readonly Color EdgeColor = new Color(0.75f, 0.62f, 0.2f, 1f);
        static readonly Color ButtonColor = new Color(0.17f, 0.15f, 0.12f, 1f);
        static readonly Color ActiveColor = new Color(0.42f, 0.33f, 0.06f, 1f);
        static readonly Color RowSelectedColor = new Color(0.35f, 0.28f, 0.08f, 1f);
        static readonly Color HintColor = new Color(0.7f, 0.7f, 0.65f, 1f);

        readonly Action onClosed;
        int tab = TabLoot;
        int page = PageMain;
        Panel root;
        Panel content;
        Button[] tabButtons = new Button[5];
        int keySelected;
        int capturing = -1;
        float ignoreUntil;
        static KeyCode[] allKeys;
        int scroll;
        int selected = -1;

        public FilterSettingsWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous, Action onClosed)
            : base(uiManager, previous)
        {
            this.onClosed = onClosed;
        }

        protected override void Setup()
        {
            base.Setup();

            root = DaggerfallUI.AddPanel(new Rect(10, 8, 300, 184), NativePanel);
            root.BackgroundColor = PanelColor;
            DaggerfallUI.AddOutline(new Rect(0, 0, 300, 184), EdgeColor, root);

            DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, new Vector2(8, 4), "JUNK & LOOT FILTER", root).TextColor = EdgeColor;

            string[] tabNames = { "LOOT", "SELL", "JUNK", "KEYS", "MISC" };
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                tabButtons[i] = DaggerfallUI.AddTextButton(new Rect(8 + i * 46, 18, 44, 12), tabNames[i], root);
                tabButtons[i].Hotkey = JunkUi.Key(KeyCode.Alpha1 + i);
                tabButtons[i].OnMouseClick += delegate (BaseScreenComponent sender, Vector2 position)
                {
                    if (capturing >= 0 || Time.unscaledTime < ignoreUntil)
                        return;
                    tab = index;
                    page = PageMain;
                    scroll = 0;
                    selected = -1;
                    Rebuild();
                };
            }

            Button done = DaggerfallUI.AddTextButton(new Rect(244, 18, 48, 12), "DONE", root);
            done.OnMouseClick += delegate (BaseScreenComponent sender, Vector2 position)
            {
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
                CloseWindow();
            };

            content = DaggerfallUI.AddPanel(new Rect(0, 34, 300, 150), root);
            Rebuild();
        }

        public override void OnPop()
        {
            base.OnPop();
            JunkStore.Save();
            JunkStore.InvalidateJunk();
            if (onClosed != null)
                onClosed();
        }

        // -----------------------------------------------------------------------------------

        FilterProfile CurrentProfile
        {
            get { return tab == TabLoot ? JunkStore.S.loot : JunkStore.S.sell; }
        }

        void Rebuild()
        {
            for (int i = 0; i < 5; i++)
                tabButtons[i].BackgroundColor = (i == tab) ? ActiveColor : ButtonColor;

            content.Components.Clear();
            if (tab == TabMisc)
                BuildMisc();
            else if (tab == TabKeys)
                BuildKeys();
            else if (tab == TabJunk)
                BuildList(JunkStore.S.junk, "JUNK PATTERNS",
                    "Case-insensitive regular expressions matched against the item name.|Tag items with the JUNK button (K) in the inventory.", true);
            else if (page == PageWhiteList)
                BuildList(CurrentProfile.whitelist, tab == TabLoot ? "LOOT WHITE LIST" : "SELL WHITE LIST",
                    tab == TabLoot ? "Names containing these words are always looted, whatever the toggles say."
                                    : "Names containing these words are always sold (magic items excluded).", false);
            else if (page == PageBlackList)
                BuildList(CurrentProfile.blacklist, tab == TabLoot ? "LOOT BLACK LIST" : "SELL BLACK LIST",
                    tab == TabLoot ? "Names containing these words are never looted (the white list wins)."
                                    : "Names containing these words are never sold (this list always wins).", false);
            else
                BuildMain();
        }

        TextLabel Label(Panel parent, float x, float y, string text, Color color)
        {
            TextLabel l = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, new Vector2(x, y), text, parent);
            l.TextColor = color;
            return l;
        }

        Checkbox AddToggle(float x, float y, string text, bool value, Action<bool> onChange)
        {
            Checkbox cb = DaggerfallUI.AddCheckbox(new Vector2(x, y), value, content);
            cb.Label.Text = text;
            cb.Label.TextColor = DaggerfallUI.DaggerfallDefaultTextColor;
            cb.OnToggleState += delegate ()
            {
                onChange(cb.IsChecked);
                JunkStore.Save();
            };
            return cb;
        }

        void BuildMain()
        {
            FilterProfile p = CurrentProfile;
            bool loot = tab == TabLoot;

            Label(content, 8, 2, loot ? "QUICK LOOT TAKES:" : "QUICK SELL SELLS:", EdgeColor);

            Label(content, 8, 14, "CATEGORIES", HintColor);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                AddToggle(8, 25 + i * 12, ItemRules.CategoryNames[i], p.categories[i], delegate (bool v) { p.categories[index] = v; });
            }

            Label(content, 150, 14, "MATERIALS", HintColor);
            for (int m = 0; m < 12; m++)
            {
                int index = m;
                int col = m / 6;
                int row = m % 6;
                AddToggle(150 + col * 74, 25 + row * 12, ItemRules.MaterialNames[m], p.materials[m], delegate (bool v) { p.materials[index] = v; });
            }

            Button allCats = DaggerfallUI.AddTextButton(new Rect(8, 75, 40, 11), "ALL", content);
            allCats.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { p.SetAllCategories(true); JunkStore.Save(); Rebuild(); };
            Button noCats = DaggerfallUI.AddTextButton(new Rect(52, 75, 40, 11), "NONE", content);
            noCats.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { p.SetAllCategories(false); JunkStore.Save(); Rebuild(); };

            if (loot)
                AddToggle(8, 92, "Ignore materials for magic items", JunkStore.S.lootMagicAnyMaterial, delegate (bool v) { JunkStore.S.lootMagicAnyMaterial = v; });

            Button allMats = DaggerfallUI.AddTextButton(new Rect(150, 98, 40, 11), "ALL", content);
            allMats.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { p.SetAllMaterials(true); JunkStore.Save(); Rebuild(); };
            Button noMats = DaggerfallUI.AddTextButton(new Rect(194, 98, 40, 11), "NONE", content);
            noMats.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { p.SetAllMaterials(false); JunkStore.Save(); Rebuild(); };

            Button white = DaggerfallUI.AddTextButton(new Rect(8, 116, 136, 12), "WHITE LIST (" + p.whitelist.Count + ")", content);
            white.Hotkey = JunkUi.Key(KeyCode.W);
            white.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { page = PageWhiteList; scroll = 0; selected = -1; Rebuild(); };
            Button black = DaggerfallUI.AddTextButton(new Rect(150, 116, 136, 12), "BLACK LIST (" + p.blacklist.Count + ")", content);
            black.Hotkey = JunkUi.Key(KeyCode.B);
            black.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { page = PageBlackList; scroll = 0; selected = -1; Rebuild(); };

            Label(content, 8, 133, loot ? "Weapons and armor also need their material ticked. Gold and quest items are always taken."
                                        : "Magic items follow only the Magic toggle. Junk-tagged items are always included.", HintColor);
        }

        void BuildList(List<string> list, string title, string hint, bool isJunk)
        {
            Label(content, 8, 2, title, EdgeColor);
            string[] hintLines = hint.Split('|');
            for (int h = 0; h < hintLines.Length; h++)
                Label(content, 8, 12 + h * 10, hintLines[h], HintColor);

            Panel rows = DaggerfallUI.AddPanel(new Rect(8, 34, 268, VisibleRows * RowHeight + 2), content);
            rows.BackgroundColor = new Color(0.03f, 0.03f, 0.03f, 1f);
            DaggerfallUI.AddOutline(new Rect(0, 0, 268, VisibleRows * RowHeight + 2), new Color(0.35f, 0.3f, 0.15f, 1f), rows);
            rows.OnMouseScrollUp += delegate (BaseScreenComponent s) { ScrollList(list, -1); };
            rows.OnMouseScrollDown += delegate (BaseScreenComponent s) { ScrollList(list, 1); };

            scroll = Mathf.Clamp(scroll, 0, Math.Max(0, list.Count - VisibleRows));
            for (int r = 0; r < VisibleRows; r++)
            {
                int index = scroll + r;
                if (index >= list.Count)
                    break;
                Button row = DaggerfallUI.AddButton(new Rect(1, 1 + r * RowHeight, 266, RowHeight), rows);
                row.Label.Text = list[index];
                row.Label.TextColor = DaggerfallUI.DaggerfallDefaultTextColor;
                row.Label.Position = new Vector2(2, 1);
                row.Label.HorizontalAlignment = HorizontalAlignment.None;
                if (index == selected)
                    row.BackgroundColor = RowSelectedColor;
                row.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { selected = index; Rebuild(); };
            }

            Button up = DaggerfallUI.AddTextButton(new Rect(280, 34, 12, 12), "^", content);
            up.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { ScrollList(list, -1); };
            Button down = DaggerfallUI.AddTextButton(new Rect(280, 34 + VisibleRows * RowHeight - 10, 12, 12), "v", content);
            down.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { ScrollList(list, 1); };

            int y = 34 + VisibleRows * RowHeight + 6;
            Button add = DaggerfallUI.AddTextButton(new Rect(8, y, 60, 12), "ADD", content);
            add.Hotkey = JunkUi.Key(KeyCode.A);
            add.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { AskForEntry(list, isJunk); };
            Button remove = DaggerfallUI.AddTextButton(new Rect(74, y, 60, 12), "REMOVE", content);
            remove.Hotkey = JunkUi.Key(KeyCode.R);
            remove.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos)
            {
                if (selected >= 0 && selected < list.Count)
                {
                    list.RemoveAt(selected);
                    selected = -1;
                    JunkStore.Save();
                    JunkStore.InvalidateJunk();
                    Rebuild();
                }
            };

            if (isJunk)
            {
                Checkbox cb = DaggerfallUI.AddCheckbox(new Vector2(150, y + 1), JunkStore.S.preselectJunkOnSell, content);
                cb.Label.Text = "Add junk to sales automatically";
                cb.Label.TextColor = DaggerfallUI.DaggerfallDefaultTextColor;
                cb.OnToggleState += delegate ()
                {
                    JunkStore.S.preselectJunkOnSell = cb.IsChecked;
                    JunkStore.Save();
                };
            }
            else
            {
                Button back = DaggerfallUI.AddTextButton(new Rect(140, y, 60, 12), "BACK", content);
                back.Hotkey = JunkUi.Key(KeyCode.B);
                back.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { page = PageMain; scroll = 0; selected = -1; Rebuild(); };
            }
        }

        // ---- KEYS tab ----

        void BuildKeys()
        {
            Label(content, 8, 2, "KEY BINDINGS", EdgeColor);
            Label(content, 8, 12, "Click a row (or use Up/Down and Enter), then press the new key.", HintColor);
            Label(content, 8, 22, "Escape cancels, Backspace unbinds. Keys DFU already uses in these windows will clash.", HintColor);

            for (int i = 0; i < JunkStore.KeyCount; i++)
            {
                int index = i;
                Label(content, 14, 35 + i * 13, JunkStore.KeyActionNames[i], DaggerfallUI.DaggerfallDefaultTextColor);
                string text = (capturing == i) ? "press a key..." : JunkStore.KeyName(i);
                Button row = DaggerfallUI.AddTextButton(new Rect(150, 33 + i * 13, 100, 12), text, content);
                if (i == keySelected)
                    row.BackgroundColor = RowSelectedColor;
                row.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos)
                {
                    if (capturing >= 0 || Time.unscaledTime < ignoreUntil)
                        return;
                    keySelected = index;
                    StartCapture(index);
                };
            }

            Button reset = DaggerfallUI.AddTextButton(new Rect(8, 33 + JunkStore.KeyCount * 13 + 3, 120, 12), "RESET KEYS", content);
            reset.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos)
            {
                if (capturing >= 0)
                    return;
                JunkStore.S.keys = JunkStore.DefaultKeys();
                JunkStore.Save();
                Rebuild();
            };
        }

        // ---- MISC page ----

        void BuildMisc()
        {
            Label(content, 8, 2, "OTHER SETTINGS", EdgeColor);

            AddToggle(8, 14, "Skyrim-style vitals bars", JunkStore.S.skyrimBars, delegate (bool v) { JunkStore.S.skyrimBars = v; });
            Label(content, 22, 24, "Magicka, health and stamina along the bottom, with numbers.", HintColor);

            AddToggle(8, 34, "Effect names and borders on the HUD", JunkStore.S.effectLabels, delegate (bool v) { JunkStore.S.effectLabels = v; });
            Label(content, 22, 44, "Top left; name under each icon, green border for buffs, red for debuffs.", HintColor);

            AddToggle(8, 54, "Wide compass", JunkStore.S.skyrimCompass, delegate (bool v) { JunkStore.S.skyrimCompass = v; });
            Label(content, 22, 64, "A stretched compass at the top centre of the screen.", HintColor);

            AddToggle(8, 74, "Fading interaction icon", JunkStore.S.fadeModeIcon, delegate (bool v) { JunkStore.S.fadeModeIcon = v; });
            Label(content, 22, 84, "Bottom left; fades out 5 seconds after the mode is switched.", HintColor);

            AddToggle(8, 94, "Corpse clean up", JunkStore.S.corpseCleanup, delegate (bool v) { JunkStore.S.corpseCleanup = v; });
            Label(content, 22, 104, "Empty corpses fade away in 5 seconds; after a quick loot that left items, in 10.", HintColor);

            Label(content, 8, 118, "WINDOW SIZE", EdgeColor);
            Label(content, 8, 127, "Shrinks and centres the game's windows.", HintColor);
            Button smaller = DaggerfallUI.AddTextButton(new Rect(8, 137, 22, 12), "-", content);
            smaller.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { ChangeWindowScale(-0.05f); };
            Label(content, 40, 139, Mathf.RoundToInt(JunkStore.S.windowScale * 100f) + "%", DaggerfallUI.DaggerfallDefaultTextColor);
            Button bigger = DaggerfallUI.AddTextButton(new Rect(74, 137, 22, 12), "+", content);
            bigger.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { ChangeWindowScale(0.05f); };

            Label(content, 150, 118, "UI STYLE", EdgeColor);
            Label(content, 150, 127, "Modern (table layout) is planned.", HintColor);
            Button classic = DaggerfallUI.AddTextButton(new Rect(150, 137, 52, 12), "CLASSIC", content);
            classic.BackgroundColor = (JunkStore.S.uiStyle == 0) ? ActiveColor : ButtonColor;
            classic.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { SetUiStyle(0); };
            Button modern = DaggerfallUI.AddTextButton(new Rect(206, 137, 52, 12), "MODERN", content);
            modern.BackgroundColor = (JunkStore.S.uiStyle == 1) ? ActiveColor : ButtonColor;
            modern.Label.TextColor = HintColor;
            modern.OnMouseClick += delegate (BaseScreenComponent s, Vector2 pos) { JunkUi.Hud("The modern style is not available yet."); };
        }

        void SetUiStyle(int style)
        {
            JunkStore.S.uiStyle = style;
            JunkStore.Save();
            Rebuild();
        }

        void ChangeWindowScale(float delta)
        {
            JunkStore.S.windowScale = Mathf.Clamp(Mathf.Round((JunkStore.S.windowScale + delta) * 20f) / 20f, 0.4f, 1f);
            JunkStore.Save();
            Rebuild();
        }

        void StartCapture(int action)
        {
            capturing = action;
            Rebuild();
        }

        void FinishCapture()
        {
            capturing = -1;
            ignoreUntil = Time.unscaledTime + 0.3f;
            JunkStore.Save();
            Rebuild();
        }

        static bool IsBindable(KeyCode k)
        {
            string name = k.ToString();
            if (name.StartsWith("Mouse") || name.StartsWith("Joystick") || name.StartsWith("Shift") || name.StartsWith("Control")
                || name.StartsWith("Alt") || name.StartsWith("Command") || name.StartsWith("Left") && name.EndsWith("Apple")
                || name.StartsWith("Right") && name.EndsWith("Apple") || name.StartsWith("Numlock") || name.StartsWith("Capslock")
                || k == KeyCode.None || k == KeyCode.Return || k == KeyCode.Escape)
                return false;
            return true;
        }

        void HandleKeyInput()
        {
            if (capturing >= 0)
            {
                if (allKeys == null)
                    allKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));
                for (int n = 0; n < allKeys.Length; n++)
                {
                    KeyCode k = allKeys[n];
                    if (!Input.GetKeyDown(k))
                        continue;

                    if (k == KeyCode.Escape)
                    {
                        FinishCapture();
                    }
                    else if (k == KeyCode.Backspace || k == KeyCode.Delete)
                    {
                        JunkStore.S.keys[capturing] = 0;
                        FinishCapture();
                    }
                    else if (IsBindable(k))
                    {
                        int clash = -1;
                        for (int j = 0; j < JunkStore.KeyCount; j++)
                        {
                            if (j != capturing && JunkStore.S.keys[j] == (int)k)
                                clash = j;
                        }
                        if (clash >= 0)
                        {
                            DaggerfallUI.MessageBox("That key is already used for: " + JunkStore.KeyActionNames[clash]);
                            FinishCapture();
                        }
                        else
                        {
                            JunkStore.S.keys[capturing] = (int)k;
                            FinishCapture();
                        }
                    }
                    else
                    {
                        continue;
                    }
                    break;
                }
            }
            else if (tab == TabMisc && Time.unscaledTime >= ignoreUntil)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow))
                    ChangeWindowScale(-0.05f);
                else if (Input.GetKeyDown(KeyCode.RightArrow))
                    ChangeWindowScale(0.05f);
            }
            else if (tab == TabKeys && Time.unscaledTime >= ignoreUntil)
            {
                if (Input.GetKeyDown(KeyCode.UpArrow) && keySelected > 0)
                {
                    keySelected--;
                    Rebuild();
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow) && keySelected < JunkStore.KeyCount - 1)
                {
                    keySelected++;
                    Rebuild();
                }
                else if (Input.GetKeyDown(KeyCode.Return))
                {
                    StartCapture(keySelected);
                }

            }
        }

        public override void Update()
        {
            HandleKeyInput();
            // Escape must cancel a capture, not close the window (and not the moment after it either).
            AllowCancel = capturing < 0 && Time.unscaledTime >= ignoreUntil;
            base.Update();
        }

        void ScrollList(List<string> list, int delta)
        {
            int max = Math.Max(0, list.Count - VisibleRows);
            int next = Mathf.Clamp(scroll + delta, 0, max);
            if (next != scroll)
            {
                scroll = next;
                Rebuild();
            }
        }

        void AskForEntry(List<string> list, bool isJunk)
        {
            DaggerfallInputMessageBox box = new DaggerfallInputMessageBox(uiManager, this);
            box.SetTextBoxLabel(isJunk ? "Junk pattern (regex):" : "Item name contains:");
            box.TextBox.Text = string.Empty;
            box.OnGotUserInput += delegate (DaggerfallInputMessageBox sender, string input)
            {
                input = (input ?? string.Empty).Trim();
                if (input.Length == 0)
                    return;
                if (isJunk)
                {
                    try
                    {
                        new Regex(input);
                    }
                    catch (Exception)
                    {
                        DaggerfallUI.MessageBox("That is not a valid regular expression.");
                        return;
                    }
                }
                list.Add(input);
                JunkStore.Save();
                JunkStore.InvalidateJunk();
                scroll = Math.Max(0, list.Count - VisibleRows);
                Rebuild();
            };
            box.Show();
        }
    }
}
