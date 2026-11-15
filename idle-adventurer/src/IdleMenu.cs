// Idle Adventurer - a main menu for any screen shape: the game's own night scene filling the screen, and a column of
// New game, Load game, Idle Adventurer, Settings and Exit. It replaces the stock start window and keeps its type, so
// anything in the game that looks for the start window still finds it.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace IdleAdventurer
{
    public class IdleStartWindow : DaggerfallStartWindow
    {
        public const int ItemNew = 0, ItemLoad = 1, ItemIdle = 2, ItemSettings = 3, ItemExit = 4;
        public static readonly string[] Labels = { "New Game", "Load Game", "Idle Adventurer", "Settings", "Exit" };
        public static readonly string[] Keys = { "N", "L", "I", "O", "E" };

        MenuCanvas canvas;
        public int Selected = ItemLoad;
        Vector2 lastMouse;

        public IdleStartWindow(IUserInterfaceManager uiManager)
            : base(uiManager)
        {
        }

        protected override void Setup()
        {
            // The stock setup (scroll picture and its buttons) is left out on purpose.
            ParentPanel.BackgroundColor = Color.black;
            NativePanel.BackgroundColor = Color.clear;
            canvas = new MenuCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public void Choose(int item)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            switch (item)
            {
                case ItemNew:
                    uiManager.PushWindow(UIWindowFactory.GetInstance(UIWindowType.StartNewGameWizard, uiManager));
                    break;
                case ItemLoad:
                    uiManager.PushWindow(UIWindowFactory.GetInstanceWithArgs(UIWindowType.UnitySaveGame,
                        new object[] { uiManager, DaggerfallUnitySaveGameWindow.Modes.LoadGame, null, true }));
                    break;
                case ItemIdle:
                    uiManager.PushWindow(new IdleHubWindow(uiManager, this));
                    break;
                case ItemSettings:
                {
                    // The game's settings window has no backdrop of its own; this menu is drawn behind it.
                    AdvancedSettingsWindow settings = new AdvancedSettingsWindow(uiManager);
                    settings.PreviousWindow = this;
                    uiManager.PushWindow(settings);
                    break;
                }
                case ItemExit:
                    Application.Quit();
                    break;
            }
        }

        public override void Update()
        {
            base.Update();
            if (canvas == null)
                return;
            canvas.Size = new Vector2(Screen.width, Screen.height);

            // The mouse picks a row only when it moves, so the arrow keys are not fought.
            Vector2 mouse = IdleUi.Mouse;
            int hover = canvas.ItemAt(mouse);
            if (hover >= 0 && (mouse - lastMouse).sqrMagnitude > 1f)
                Selected = hover;
            lastMouse = mouse;
            if (Input.GetMouseButtonDown(0) && hover >= 0)
            {
                Selected = hover;
                Choose(hover);
                return;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
                Selected = (Selected + 1) % Labels.Length;
            if (Input.GetKeyDown(KeyCode.UpArrow))
                Selected = (Selected + Labels.Length - 1) % Labels.Length;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                Choose(Selected);
            else if (Input.GetKeyDown(KeyCode.N) || Input.GetKeyDown(KeyCode.S))
                Choose(ItemNew);
            else if (Input.GetKeyDown(KeyCode.L))
                Choose(ItemLoad);
            else if (Input.GetKeyDown(KeyCode.I))
                Choose(ItemIdle);
            else if (Input.GetKeyDown(KeyCode.O))
                Choose(ItemSettings);
            else if (Input.GetKeyDown(KeyCode.E))
                Choose(ItemExit);
        }

        class MenuCanvas : BaseScreenComponent
        {
            public IdleStartWindow Owner;
            readonly List<Rect> items = new List<Rect>();
            Texture2D picture;
            bool tried;

            public int ItemAt(Vector2 mouse)
            {
                for (int i = 0; i < items.Count; i++)
                    if (items[i].Contains(mouse))
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

                // The game's night scene (from character creation), scaled to fill the width; on wide screens a few rows
                // are cut from the top and bottom instead of leaving bars at the sides.
                if (!tried)
                {
                    tried = true;
                    try
                    {
                        picture = DaggerfallUI.GetTextureFromImg("CHGN00I0.IMG");
                        if (picture != null)
                            picture.filterMode = FilterMode.Point;
                    }
                    catch (Exception)
                    {
                        picture = null;
                    }
                }
                if (picture != null)
                {
                    float scale = Mathf.Max(W / picture.width, H / picture.height);
                    float pw = picture.width * scale;
                    float ph = picture.height * scale;
                    GUI.DrawTexture(new Rect((W - pw) / 2f, (H - ph) / 2f, pw, ph), picture, ScaleMode.StretchToFill, false);
                }

                // A dark band on the left for the menu, fading out to the right.
                float band = Mathf.Round(Mathf.Clamp(W * 0.36f, H * 0.62f, W * 0.6f));
                IdleUi.Box(new Rect(0, 0, band, H), new Color(0f, 0f, 0f, 0.72f));
                int steps = 24;
                float fade = band * 0.5f;
                for (int i = 0; i < steps; i++)
                    IdleUi.Box(new Rect(band + fade * i / steps, 0, fade / steps + 1f, H), new Color(0f, 0f, 0f, 0.72f * (1f - (i + 1f) / steps)));

                float line = Mathf.Round(H * 0.04f);
                float sc = IdleUi.Scale(line);
                float x = Mathf.Round(Mathf.Max(H * 0.08f, W * 0.05f));
                float rowTall = Mathf.Round(line * 1.55f);
                float rowW = band - x - H * 0.05f;
                float total = line * 4.2f + rowTall * Labels.Length;
                float y = Mathf.Round((H - total) / 2f);

                IdleUi.Text("Daggerfall", x, y, sc * 2.4f, IdleUi.Gold);
                y += line * 2.3f;
                IdleUi.Text("Unity", x + line * 0.1f, y, sc * 1.1f, IdleUi.Muted);
                y += line * 1.2f;
                IdleUi.Box(new Rect(x, y, rowW * 0.55f, 2f), IdleUi.Line);
                y += line * 0.7f;

                items.Clear();
                for (int i = 0; i < Labels.Length; i++)
                {
                    Rect r = new Rect(x - line * 0.5f, y, rowW + line * 0.5f, rowTall - 6f);
                    items.Add(r);
                    bool on = Owner.Selected == i;
                    if (on)
                    {
                        IdleUi.Box(r, new Color(0.72f, 0.58f, 0.18f, 0.22f));
                        IdleUi.Box(new Rect(r.x, r.y, 3f, r.height), IdleUi.Line);
                    }
                    float ty = r.y + (r.height - line * 0.74f) / 2f;
                    IdleUi.Text(Labels[i], x, ty, sc * 1.05f, on ? IdleUi.Gold : IdleUi.White);
                    string key = Keys[i];
                    IdleUi.Text(key, r.xMax - IdleUi.Width(key, sc * 0.85f) - line * 0.5f, ty + line * 0.08f, sc * 0.85f, IdleUi.Muted);
                    y += rowTall;
                }

                string version = "Daggerfall Unity " + VersionInfo.DaggerfallUnityVersion;
                IdleUi.Text(version, x, H - line * 1.6f, sc * 0.75f, IdleUi.Muted);

                // Dimmed behind another window (the game's settings draw this menu as their backdrop).
                if (DaggerfallUI.UIManager.TopWindow != Owner)
                    IdleUi.Box(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.75f));
            }
        }
    }
}
