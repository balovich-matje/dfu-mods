// Idle Adventurer - a Daggerfall Unity mod.
// Entry point, save file and the hub screen. The simulation itself is in IdleSim.cs.

using System;
using System.IO;
using UnityEngine;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility;

namespace IdleAdventurer
{
    // Reads and writes the mode's own save file. Normal Daggerfall saves are never touched.
    public static class IdleStore
    {
        public static IdleWorld W;

        public static string FilePath
        {
            get { return Path.Combine(Application.persistentDataPath, "IdleAdventurer.json"); }
        }

        public static void Load()
        {
            IdleSave s = null;
            try
            {
                if (File.Exists(FilePath))
                    s = JsonUtility.FromJson<IdleSave>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                Debug.LogError("[Idle] Could not read the save, starting fresh: " + ex.Message);
            }

            W = null;
            if (IdleSim.Usable(s))
            {
                try
                {
                    W = IdleSim.FromSave(s);
                }
                catch (Exception ex)
                {
                    Debug.LogError("[Idle] Could not restore the hero's items, starting fresh: " + ex.Message);
                }
            }
            if (W == null)
                NewHero();
        }

        public static void NewHero()
        {
            NewHero(IdleData.ClassWarrior);
        }

        public static void NewHero(int heroClass)
        {
            int opened = (W != null) ? W.s.opened : 0;
            W = IdleSim.NewHero((int)(DateTime.UtcNow.Ticks & 0x7fffffff), "Adventurer", heroClass);
            W.s.opened = opened;
        }

        // A hero of one of the game's classes (ClassCareers 0-17).
        public static void NewCareer(int career)
        {
            int opened = (W != null) ? W.s.opened : 0;
            W = IdleSim.NewCareer((int)(DateTime.UtcNow.Ticks & 0x7fffffff), "Adventurer", career);
            W.s.opened = opened;
        }

        public static void Save()
        {
            if (W == null)
                return;
            try
            {
                IdleSim.ToSave(W);
                W.s.lastSeenUtcTicks = DateTime.UtcNow.Ticks;
                File.WriteAllText(FilePath, JsonUtility.ToJson(W.s, true));
            }
            catch (Exception ex)
            {
                Debug.LogError("[Idle] Could not write the save: " + ex.Message);
            }
        }
    }

    public class IdleEntry : MonoBehaviour
    {
        static Mod mod;
        bool registered;

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;
            GameObject go = new GameObject(mod.Title);
            go.AddComponent<IdleEntry>();
            mod.IsReady = true;
            Debug.Log("[Idle] Ready.");
        }

        // The stock main menu is swapped for ours (IdleMenu.cs) whenever it comes up. Registering with the window
        // factory covers menus made after this mod loads; the swap covers one that was already made.
        void Update()
        {
            if (DaggerfallUI.Instance == null || DaggerfallUI.UIManager == null)
                return;
            if (!registered)
            {
                registered = true;
                try
                {
                    UIWindowFactory.RegisterCustomUIWindow(UIWindowType.Start, typeof(IdleStartWindow));
                }
                catch (Exception ex)
                {
                    Debug.Log("[Idle] Could not register the main menu: " + ex.Message);
                }
            }
            IUserInterfaceWindow top = DaggerfallUI.UIManager.TopWindow;
            if (top == null || top.GetType() != typeof(DaggerfallStartWindow))
                return;
            DaggerfallUI.UIManager.PopWindow();
            DaggerfallUI.UIManager.PushWindow(new IdleStartWindow(DaggerfallUI.UIManager));
        }
    }

    // Runs the simulation in real time for whichever screen is open.
    public static class IdleClock
    {
        public static int Speed = 1;
        static float carry;
        static float sinceSave;

        public static void NextSpeed()
        {
            Speed = (Speed == 1) ? 4 : (Speed == 4) ? 16 : 1;
        }

        public static void Tick()
        {
            IdleWorld w = IdleStore.W;
            if (w == null)
                return;
            carry += Mathf.Min(0.25f, Time.unscaledDeltaTime) * IdleData.TicksPerSecond * Speed;
            int ticks = (int)carry;
            if (ticks > 0)
            {
                carry -= ticks;
                IdleSim.Advance(w, ticks);
            }
            sinceSave += Time.unscaledDeltaTime;
            if (sinceSave > 30f)
            {
                sinceSave = 0f;
                IdleStore.Save();
            }
        }
    }

    // Drawing helpers shared by the mode's screens. Everything is in screen pixels.
    public static class IdleUi
    {
        public static readonly Color Fill = new Color(0.06f, 0.055f, 0.05f, 0.985f);
        public static readonly Color Glass = new Color(0.04f, 0.035f, 0.03f, 0.72f);
        public static readonly Color Line = new Color(0.72f, 0.58f, 0.18f, 1f);
        public static readonly Color White = new Color(0.94f, 0.93f, 0.9f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.6f, 0.55f, 1f);
        public static readonly Color Good = new Color(0.44f, 0.83f, 0.44f, 1f);
        public static readonly Color Bad = new Color(1f, 0.45f, 0.4f, 1f);
        public static readonly Color Blue = new Color(0.55f, 0.75f, 1f, 1f);

        public static Color Gold
        {
            get { return DaggerfallUI.DaggerfallDefaultTextColor; }
        }

        // Text scale for a line of the given pixel height.
        public static float Scale(float lineHeight)
        {
            return lineHeight * 0.74f / DaggerfallUI.DefaultFont.GlyphHeight;
        }

        public static float Width(string s, float scale)
        {
            return DaggerfallUI.DefaultFont.CalculateTextWidth(s, Vector2.one) * scale;
        }

        public static void Box(Rect r, Color c)
        {
            DaggerfallUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, c);
        }

        public static void Frame(Rect r, Color fill)
        {
            Box(r, fill);
            Box(new Rect(r.x, r.y, r.width, 2f), Line);
            Box(new Rect(r.x, r.yMax - 2f, r.width, 2f), Line);
            Box(new Rect(r.x, r.y, 2f, r.height), Line);
            Box(new Rect(r.xMax - 2f, r.y, 2f, r.height), Line);
        }

        public static void Text(string s, float x, float y, float scale, Color c)
        {
            float o = Mathf.Max(1f, Mathf.Round(scale * 0.5f));
            DaggerfallUI.DefaultFont.DrawText(s, new Vector2(Mathf.Round(x), Mathf.Round(y)), new Vector2(scale, scale), c, Color.black, new Vector2(o, o));
        }

        public static void Centred(string s, float centreX, float y, float scale, Color c)
        {
            Text(s, centreX - Width(s, scale) / 2f, y, scale, c);
        }

        public static void Bar(Rect r, float part, Color c)
        {
            Box(r, new Color(0f, 0f, 0f, 0.55f));
            Box(r, new Color(1f, 1f, 1f, 0.1f));
            Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(part), r.height), c);
        }

        public static Vector2 Mouse
        {
            get { return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y); }
        }

        public static Color EventColor(int type)
        {
            switch (type)
            {
                case IdleData.EvHeroHit:
                case IdleData.EvKill:
                case IdleData.EvLevel:
                case IdleData.EvSkill:
                case IdleData.EvPotion:
                case IdleData.EvCastSelf:
                    return Good;
                case IdleData.EvCast:
                    return Blue;
                case IdleData.EvEnemyHit:
                case IdleData.EvEnemyCast:
                case IdleData.EvDied:
                    return Bad;
                case IdleData.EvLoot:
                    return Blue;
                case IdleData.EvMine:
                    return Gold;
                case IdleData.EvHeroMiss:
                case IdleData.EvEnemyMiss:
                    return Muted;
                case IdleData.EvFight:
                case IdleData.EvFloor:
                case IdleData.EvRetreat:
                case IdleData.EvHome:
                    return Gold;
            }
            return White;
        }

        // Colour of an item's name by its number of enchantments.
        public static Color EnchantColor(int enchants)
        {
            switch (enchants)
            {
                case 1: return Blue;
                case 2: return new Color(1f, 0.86f, 0.3f, 1f);
                case 3: return new Color(1f, 0.55f, 0.15f, 1f);
            }
            return White;
        }

        public static Color TraitColor(int traits)
        {
            if ((traits & IdleData.TraitBoss) != 0) return new Color(1f, 0.55f, 0.25f, 1f);
            if ((traits & IdleData.TraitSwift) != 0) return new Color(0.75f, 1f, 0.75f, 1f);
            if ((traits & IdleData.TraitArmoured) != 0) return new Color(0.75f, 0.8f, 1f, 1f);
            if ((traits & IdleData.TraitRegenerating) != 0) return new Color(0.7f, 1f, 0.9f, 1f);
            if ((traits & IdleData.TraitVenomous) != 0) return new Color(0.7f, 1f, 0.45f, 1f);
            if ((traits & IdleData.TraitVampiric) != 0) return new Color(1f, 0.6f, 0.75f, 1f);
            if ((traits & IdleData.TraitEnraged) != 0) return new Color(1f, 0.6f, 0.55f, 1f);
            return Color.white;
        }
    }

    // A row of clickable buttons. They are drawn (and their places remembered) during Draw, and the window asks in
    // Update which one was clicked.
    public class IdleButtons
    {
        readonly System.Collections.Generic.List<Rect> places = new System.Collections.Generic.List<Rect>();
        readonly System.Collections.Generic.List<int> ids = new System.Collections.Generic.List<int>();

        public void Begin()
        {
            places.Clear();
            ids.Clear();
        }

        // How wide a button would be.
        public static float Measure(string label, float height)
        {
            return Mathf.Round(IdleUi.Width(label, IdleUi.Scale(height * 0.62f)) + height * 0.9f);
        }

        // Lays a group of buttons out in rows no wider than the width, the last row ending at bottom, and returns
        // the top of the group.
        public float Flow(int[] ids, string[] labels, bool[] lit, float x, float width, float bottom, float height, float gap)
        {
            System.Collections.Generic.List<int> starts = new System.Collections.Generic.List<int>();
            float used = 0f;
            for (int i = 0; i < ids.Length; i++)
            {
                float w = Measure(labels[i], height);
                if (i == 0 || used + gap + w > width)
                {
                    starts.Add(i);
                    used = w;
                }
                else
                {
                    used += gap + w;
                }
            }
            float top = bottom - (starts.Count - 1) * (height + gap);
            for (int r = 0; r < starts.Count; r++)
            {
                int end = (r + 1 < starts.Count) ? starts[r + 1] : ids.Length;
                float bx = x;
                for (int i = starts[r]; i < end; i++)
                    bx += Add(ids[i], labels[i], bx, top + r * (height + gap), height, lit[i]) + gap;
            }
            return top;
        }

        // Draws a button at x, y and returns its width.
        public float Add(int id, string label, float x, float y, float height, bool lit)
        {
            float scale = IdleUi.Scale(height * 0.62f);
            float width = Mathf.Round(IdleUi.Width(label, scale) + height * 0.9f);
            Rect r = new Rect(Mathf.Round(x), Mathf.Round(y), width, Mathf.Round(height));
            bool hover = r.Contains(IdleUi.Mouse);
            IdleUi.Frame(r, hover ? new Color(0.2f, 0.16f, 0.08f, 0.95f) : new Color(0.09f, 0.08f, 0.065f, 0.92f));
            IdleUi.Centred(label, r.x + r.width / 2f, r.y + (r.height - height * 0.62f) / 2f, scale, lit ? IdleUi.Good : IdleUi.Gold);
            places.Add(r);
            ids.Add(id);
            return width;
        }

        public int Clicked()
        {
            if (!Input.GetMouseButtonDown(0))
                return -1;
            Vector2 m = IdleUi.Mouse;
            for (int i = 0; i < places.Count; i++)
                if (places[i].Contains(m))
                    return ids[i];
            return -1;
        }
    }

    // ---------------------------------------------------------------------------------------
    // The hub. Milestone 1: a text view of the simulation, driven by keys.
    // ---------------------------------------------------------------------------------------

    public class IdleHubWindow : DaggerfallPopupWindow
    {
        static readonly Color Fill = new Color(0.06f, 0.055f, 0.05f, 0.985f);
        static readonly Color Line = new Color(0.72f, 0.58f, 0.18f, 1f);
        static readonly Color White = new Color(0.94f, 0.93f, 0.9f, 1f);
        static readonly Color Muted = new Color(0.62f, 0.6f, 0.55f, 1f);
        static readonly Color Good = new Color(0.44f, 0.83f, 0.44f, 1f);
        static readonly Color Bad = new Color(1f, 0.45f, 0.4f, 1f);
        static readonly Color Blue = new Color(0.55f, 0.75f, 1f, 1f);

        HubCanvas canvas;
        bool askedCalling;

        public IdleHubWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new HubCanvas();
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
        }

        public override void OnPush()
        {
            base.OnPush();
            IdleStore.Load();
            IdleWorld w = IdleStore.W;
            w.s.opened++;
            w.record = true;
            IdlePlaces.Name(w);
            if (w.shop.Count == 0 && string.IsNullOrEmpty(w.s.shopBlob))
                IdleSim.Restock(w);
            CatchUp(w);
            IdleStore.Save();
        }

        // Time spent away from the game is simulated in one go, from the system clock.
        void CatchUp(IdleWorld w)
        {
            IdleSave s = w.s;
            if (s.lastSeenUtcTicks <= 0 || (s.phase == IdleData.PhaseTown && s.autoRepeat == 0))
                return;
            double away = (DateTime.UtcNow - new DateTime(s.lastSeenUtcTicks, DateTimeKind.Utc)).TotalSeconds;
            long seconds = (long)Math.Max(0, Math.Min(away, IdleData.OfflineCap(w.s.house)));
            if (seconds < 2)
                return;
            int runs = s.expeditions;
            int deaths = s.deaths;
            int kills = s.kills;
            long earned = s.goldEarned;
            w.record = false;
            IdleSim.Advance(w, seconds * IdleData.TicksPerSecond);
            w.record = true;
            w.Emit(IdleData.EvHome, "While you were away (" + Clock(seconds) + "): " + (s.expeditions - runs) + " expeditions, "
                + (s.kills - kills) + " kills, " + (s.goldEarned - earned) + " gold earned, " + (s.deaths - deaths) + " deaths");
        }

        static string Clock(long seconds)
        {
            if (seconds >= 3600)
                return (seconds / 3600) + " h " + (seconds % 3600 / 60) + " min";
            if (seconds >= 60)
                return (seconds / 60) + " min " + (seconds % 60) + " s";
            return seconds + " s";
        }

        public override void Update()
        {
            base.Update();
            if (canvas == null || IdleStore.W == null)
                return;
            canvas.Size = new Vector2(Screen.width, Screen.height);
            IdleWorld w = IdleStore.W;
            IdleSave s = w.s;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int click = canvas.Buttons.Clicked();
            if (click >= 0)
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);

            if (Input.GetKeyDown(KeyCode.G) || click == HubCanvas.BtnGo)
            {
                IdleSim.StartExpedition(w);
                OpenView();
                return;
            }
            if (Input.GetKeyDown(KeyCode.V) || click == HubCanvas.BtnView)
            {
                OpenView();
                return;
            }
            if (Input.GetKeyDown(KeyCode.M) || click == HubCanvas.BtnMap)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleMapWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.O) || click == HubCanvas.BtnOrders)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleOrdersWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.L) || click == HubCanvas.BtnSpells)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleSpellsWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (!askedCalling)
            {
                // A brand-new hero first picks a calling.
                askedCalling = true;
                IdleSave fresh = IdleStore.W.s;
                if (fresh.expeditions == 0 && fresh.classChosen == 0 && fresh.phase == IdleData.PhaseTown)
                {
                    DaggerfallUI.UIManager.PushWindow(new IdleCallingWindow(DaggerfallUI.UIManager, this, false));
                    return;
                }
            }
            if (Input.GetKeyDown(KeyCode.H) || click == HubCanvas.BtnTown)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleTownWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.A) || click == HubCanvas.BtnAlchemy)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleAlchemyWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.E) || click == HubCanvas.BtnEnchant)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleEnchantWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.J) || click == HubCanvas.BtnJewel)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleJewelWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.B) || click == HubCanvas.BtnForge)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleForgeWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.S) || click == HubCanvas.BtnSmith)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleSmithWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.K) || click == HubCanvas.BtnSkip)
                s.skipCleared = (s.skipCleared == 0) ? 1 : 0;
            if (Input.GetKeyDown(KeyCode.C) || click == HubCanvas.BtnHero)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleHeroWindow(DaggerfallUI.UIManager, this));
                return;
            }
            if (Input.GetKeyDown(KeyCode.F) || click == HubCanvas.BtnSpeed)
                IdleClock.NextSpeed();
            if (Input.GetKeyDown(KeyCode.R) || click == HubCanvas.BtnRepeat)
                s.autoRepeat = (s.autoRepeat == 0) ? 1 : 0;
            if (Input.GetKeyDown(KeyCode.D) || click == HubCanvas.BtnDeeper)
                s.autoDeeper = (s.autoDeeper == 0) ? 1 : 0;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus) || click == HubCanvas.BtnMore)
                s.targetFloor++;
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus) || click == HubCanvas.BtnLess)
                s.targetFloor = Mathf.Max(1, s.targetFloor - 1);
            if (Input.GetKeyDown(KeyCode.T) || click == HubCanvas.BtnTest)
                canvas.Report = IdleTest.Run();
            if (click == HubCanvas.BtnBack)
            {
                CloseWindow();
                return;
            }
            if (Input.GetKeyDown(KeyCode.N) && shift)
            {
                DaggerfallUI.UIManager.PushWindow(new IdleCallingWindow(DaggerfallUI.UIManager, this, true));
                return;
            }

            IdleClock.Tick();
        }

        void OpenView()
        {
            IdleStore.Save();
            DaggerfallUI.UIManager.PushWindow(new IdleViewWindow(DaggerfallUI.UIManager, this));
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        class HubCanvas : BaseScreenComponent
        {
            public const int BtnGo = 1, BtnView = 2, BtnRepeat = 3, BtnDeeper = 4, BtnLess = 5, BtnMore = 6, BtnSpeed = 7, BtnTest = 8, BtnBack = 9, BtnHero = 10, BtnMap = 11, BtnSkip = 12, BtnOrders = 13, BtnSpells = 14, BtnSmith = 15, BtnForge = 16, BtnJewel = 17, BtnEnchant = 18, BtnAlchemy = 19, BtnTown = 20;
            public string[] Report;
            public readonly IdleButtons Buttons = new IdleButtons();

            static void Box(Rect r, Color c)
            {
                DaggerfallUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, c);
            }

            static void Frame(Rect r)
            {
                Box(r, Fill);
                Box(new Rect(r.x, r.y, r.width, 2f), Line);
                Box(new Rect(r.x, r.yMax - 2f, r.width, 2f), Line);
                Box(new Rect(r.x, r.y, 2f, r.height), Line);
                Box(new Rect(r.xMax - 2f, r.y, 2f, r.height), Line);
            }

            static void Text(string s, float x, float y, float scale, Color c)
            {
                float o = Mathf.Max(1f, Mathf.Round(scale * 0.5f));
                DaggerfallUI.DefaultFont.DrawText(s, new Vector2(x, y), new Vector2(scale, scale), c, Color.black, new Vector2(o, o));
            }

            // Draws text wrapped to a width and returns the y below it.
            static float Wrapped(string s, float x, float y, float width, float scale, float line, Color c)
            {
                string[] words = s.Split(' ');
                string row = "";
                for (int i = 0; i < words.Length; i++)
                {
                    string next = (row.Length == 0) ? words[i] : row + " " + words[i];
                    if (row.Length > 0 && DaggerfallUI.DefaultFont.CalculateTextWidth(next, Vector2.one) * scale > width)
                    {
                        Text(row, x, y, scale, c);
                        y += line;
                        row = words[i];
                    }
                    else
                    {
                        row = next;
                    }
                }
                if (row.Length > 0)
                {
                    Text(row, x, y, scale, c);
                    y += line;
                }
                return y;
            }

            static void Bar(Rect r, float part, Color c)
            {
                Box(r, new Color(1f, 1f, 1f, 0.08f));
                Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(part), r.height), c);
            }

            static Color EventColor(int type)
            {
                switch (type)
                {
                    case IdleData.EvHeroHit:
                    case IdleData.EvKill:
                    case IdleData.EvLevel:
                    case IdleData.EvSkill:
                    case IdleData.EvPotion:
                    case IdleData.EvCastSelf:
                        return Good;
                    case IdleData.EvCast:
                        return Blue;
                    case IdleData.EvEnemyHit:
                    case IdleData.EvEnemyCast:
                    case IdleData.EvDied:
                        return Bad;
                    case IdleData.EvLoot:
                        return Blue;
                    case IdleData.EvMine:
                        return IdleUi.Gold;
                    case IdleData.EvHeroMiss:
                    case IdleData.EvEnemyMiss:
                        return Muted;
                    case IdleData.EvFight:
                    case IdleData.EvFloor:
                    case IdleData.EvRetreat:
                    case IdleData.EvHome:
                        return DaggerfallUI.DaggerfallDefaultTextColor;
                }
                return White;
            }

            public override void Draw()
            {
                base.Draw();
                IdleWorld w = IdleStore.W;
                if (w == null)
                    return;
                IdleSave s = w.s;
                float W = Screen.width;
                float H = Screen.height;
                Box(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.8f));

                float width = Mathf.Round(Mathf.Min(W * 0.8f, H * 1.5f));
                float height = Mathf.Round(H * 0.86f);
                Rect r = new Rect(Mathf.Round((W - width) / 2f), Mathf.Round((H - height) / 2f), width, height);
                Frame(r);

                float sc = H * 0.03f * 0.74f / DaggerfallUI.DefaultFont.GlyphHeight;
                float line = Mathf.Round(H * 0.034f);
                float pad = Mathf.Round(H * 0.025f);
                float x = r.x + pad;
                float inner = width - 2f * pad;
                float y = r.y + pad;

                Text("Idle Adventurer", x, y, sc * 1.4f, DaggerfallUI.DaggerfallDefaultTextColor);
                y += line * 1.5f;

                Text(s.heroName + ", " + IdleSim.CallingName(s).ToLower() + ", level " + s.level + "    Gold " + s.gold + "    Health " + s.health + "/" + s.maxHealth + "    Magicka " + s.magicka + "/" + IdleSim.MaxMagicka(s)
                    + "    Potions " + s.potions + "/" + s.potionsToCarry, x, y, sc, White);
                y += line;
                Bar(new Rect(x, y, inner * 0.4f, Mathf.Round(line * 0.3f)), (float)s.health / Mathf.Max(1, s.maxHealth), Bad);
                Bar(new Rect(x + inner * 0.42f, y, inner * 0.2f, Mathf.Round(line * 0.3f)), (float)s.magicka / Mathf.Max(1, IdleSim.MaxMagicka(s)), Blue);
                y += line * 0.6f;

                string gear = "";
                for (int i = 0; i < w.gear.Length; i++)
                    if (w.gear[i] != null)
                        gear += (gear.Length > 0 ? ", " : "") + IdleSim.Name(w.gear[i]);
                y = Wrapped("Gear: " + gear + ". Stash: " + w.stash.Count + " spare.", x, y, inner, sc * 0.9f, line * 0.9f, Muted);

                string doing = IdleSim.PhaseName(s);
                if (s.phase == IdleData.PhaseFight && s.enemyId >= 0)
                    doing += ": " + IdleSim.Foe(s, IdleSim.Enemy(s.enemyId)) + " " + Mathf.Max(0, s.enemyHealth) + "/" + s.enemyMaxHealth;
                else if (s.phase != IdleData.PhaseTown)
                    doing += " (" + (s.phaseLeft / IdleData.TicksPerSecond + 1) + " s)";
                Text(doing, x, y, sc, DaggerfallUI.DaggerfallDefaultTextColor);
                y += line;
                if (s.phase == IdleData.PhaseFight)
                {
                    Bar(new Rect(x, y, inner * 0.4f, Mathf.Round(line * 0.3f)), (float)s.enemyHealth / Mathf.Max(1, s.enemyMaxHealth), Line);
                }
                y += line * 0.6f;

                string where = "Destination: " + IdleSim.Destination(s) + " (" + IdleData.DungeonNames[s.dungeonType] + ", " + s.travelSeconds
                    + " s away, cleared to floor " + s.clearedFloors[s.dungeonType] + ")";
                if (s.nextType >= 0)
                    where += "    Next: " + s.nextName;
                Text(where, x, y, sc * 0.9f, White);
                y += line;
                Text("Orders: target floor " + s.targetFloor + ", retreat below " + s.retreatPercent + "% health, auto-repeat "
                    + (s.autoRepeat != 0 ? "on" : "off") + ", go deeper after a clear " + (s.autoDeeper != 0 ? "on" : "off")
                    + ", skip cleared floors " + (s.skipCleared != 0 ? "on" : "off") + ", speed " + IdleClock.Speed + "x",
                    x, y, sc * 0.9f, Muted);
                y += line;
                Text("Runs " + s.expeditions + " (cleared " + s.clears + ", retreated " + s.retreats + ", died " + s.deaths + ")    Kills " + s.kills
                    + "    Deepest floor " + s.deepestFloor, x, y, sc * 0.9f, Muted);
                y += line * 1.3f;

                // Three groups of buttons, each wrapped to the panel's width: what to do, the town's shops, and the
                // standing orders.
                float tall = Mathf.Round(line * 1.25f);
                float bottom = r.yMax - pad - tall;
                float gap = Mathf.Round(line * 0.3f);
                Buttons.Begin();
                bool home = s.phase == IdleData.PhaseTown;
                bottom = Buttons.Flow(
                    home ? new int[] { BtnGo, BtnView, BtnHero, BtnMap, BtnOrders, BtnSpells, BtnSpeed, BtnTest, BtnBack }
                         : new int[] { BtnGo, BtnHero, BtnMap, BtnOrders, BtnSpells, BtnSpeed, BtnTest, BtnBack },
                    home ? new string[] { "Send out (G)", "View (V)", "Hero and shop (C)", "Destination (M)", "Orders (O)", "Spells (L)", "Speed " + IdleClock.Speed + "x (F)", "Test (T)", "Back (Esc)" }
                         : new string[] { "Watch (V)", "Hero and shop (C)", "Destination (M)", "Orders (O)", "Spells (L)", "Speed " + IdleClock.Speed + "x (F)", "Test (T)", "Back (Esc)" },
                    new bool[9], x, inner, bottom, tall, gap) - tall - gap;
                bottom = Buttons.Flow(new int[] { BtnSmith, BtnForge, BtnJewel, BtnEnchant, BtnAlchemy, BtnTown },
                    new string[] { "Smith (S)", "Forge (B)", "Jeweller (J)", "Enchanter (E)", "Alchemist (A)", "Town (H)" },
                    new bool[6], x, inner, bottom, tall, gap) - tall - gap;
                bottom = Buttons.Flow(new int[] { BtnRepeat, BtnDeeper, BtnSkip, BtnLess, BtnMore },
                    new string[] { "Auto-repeat (R)", "Go deeper (D)", "Skip cleared floors (K)", "Floor -", "Floor +" },
                    new bool[] { s.autoRepeat != 0, s.autoDeeper != 0, s.skipCleared != 0, false, false }, x, inner, bottom, tall, gap);
                bottom -= line * 0.4f;

                if (Report != null)
                {
                    for (int i = 0; i < Report.Length && y < bottom - line; i++)
                        y = Wrapped(Report[i], x, y, inner, sc * 0.85f, line * 0.85f, White);
                    y += line * 0.4f;
                }

                // The log: newest at the bottom.
                int rows = (int)((bottom - line * 0.3f - y) / line);
                int first = Mathf.Max(0, w.events.Count - rows);
                for (int i = first; i < w.events.Count; i++)
                {
                    Text(w.events[i].text, x, y, sc, EventColor(w.events[i].type));
                    y += line;
                }
            }
        }
    }

    // Balance and repeatability checks, run from the hub with T. They use throwaway heroes, not the save.
    public static class IdleTest
    {
        public static string[] Run()
        {
            System.Collections.Generic.List<string> lines = new System.Collections.Generic.List<string>();
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                string a = IdleSim.Summary(IdleSim.Simulate(12345, 30, true));
                string b = IdleSim.Summary(IdleSim.Simulate(12345, 30, true));
                lines.Add((a == b ? "OK" : "FAIL") + "  Same seed twice, 30 runs: " + (a == b ? "identical" : "DIFFERENT"));
                lines.Add("      " + a);
                if (a != b)
                    lines.Add("      " + b);

                // Time away is replayed in one step; it has to give the same result as watching it tick by tick.
                IdleWorld big = IdleSim.NewHero(4242, "Test");
                IdleWorld small = IdleSim.NewHero(4242, "Test");
                big.s.autoRepeat = 1;
                small.s.autoRepeat = 1;
                IdleSim.Advance(big, 36000);
                for (int i = 0; i < 36000 / 3; i++)
                    IdleSim.Advance(small, 3);
                string one1 = IdleSim.Summary(big);
                string one2 = IdleSim.Summary(small);
                lines.Add((one1 == one2 ? "OK" : "FAIL") + "  An hour in one step and in 0.3 s steps: " + (one1 == one2 ? "identical" : "DIFFERENT"));
                if (one1 != one2)
                {
                    lines.Add("      " + one1);
                    lines.Add("      " + one2);
                    // Debug: replay both with events and show where they part.
                    IdleWorld bigR = IdleSim.NewHero(4242, "Test");
                    IdleWorld smallR = IdleSim.NewHero(4242, "Test");
                    bigR.s.autoRepeat = 1;
                    smallR.s.autoRepeat = 1;
                    bigR.record = true;
                    smallR.record = true;
                    bigR.keepEvents = 100000;
                    smallR.keepEvents = 100000;
                    System.Collections.Generic.List<string> ea = new System.Collections.Generic.List<string>();
                    System.Collections.Generic.List<string> eb = new System.Collections.Generic.List<string>();
                    IdleSim.Advance(bigR, 36000);
                    for (int i = 0; i < bigR.events.Count; i++) ea.Add(bigR.events[i].text);
                    for (int i = 0; i < 36000 / 3; i++)
                    {
                        int before = smallR.events.Count;
                        IdleSim.Advance(smallR, 3);
                        for (int j = before; j < smallR.events.Count; j++) eb.Add(smallR.events[j].text);
                    }
                    lines.Add("      events " + ea.Count + " vs " + eb.Count + " (one-step list may be capped)");
                    int cut = 0;
                    for (int i = 0; i < ea.Count && i + cut < eb.Count; i++)
                        if (ea[i] != eb[i + cut])
                        {
                            for (int k = Math.Max(0, i - 4); k <= i + 1 && k < ea.Count && k + cut < eb.Count; k++)
                                lines.Add("      " + k + ": " + ea[k] + "  ||  " + eb[k + cut]);
                            break;
                        }
                }

                // Every dungeon type: 20 runs each with a fresh hero.
                for (int type = 0; type < IdleData.DungeonTypes; type++)
                {
                    IdleWorld d = IdleSim.NewHero(999 + type, "Test");
                    d.s.dungeonType = type;
                    d.s.autoRepeat = 1;
                    d.stopAfter = 20;
                    for (int guard = 0; guard < 400 && d.s.expeditions < 20; guard++)
                        IdleSim.Advance(d, 36000);
                    lines.Add(IdleData.DungeonNames[type] + ", 20 runs: level " + d.s.level + ", cleared to floor " + d.s.clearedFloors[type] + ", died "
                        + d.s.deaths + ", gold " + d.s.gold + ", " + (d.s.clock / 600) + " min");
                }

                int clearedFirst = 0;
                int diedFirst = 0;
                const int heroes = 40;
                for (int seed = 1; seed <= heroes; seed++)
                {
                    IdleWorld one = IdleSim.Simulate(seed * 7919, 1, false);
                    clearedFirst += one.s.clears;
                    diedFirst += one.s.deaths;
                }
                lines.Add("New hero, first run, floor 1 only: " + clearedFirst + " of " + heroes + " cleared it, " + diedFirst + " died, "
                    + (heroes - clearedFirst - diedFirst) + " retreated");

                // The first ten runs of 40 heroes, pushing deeper after each clear.
                int deaths = 0, levels = 0, floors = 0, gold = 0;
                long ticks = 0;
                for (int seed = 1; seed <= heroes; seed++)
                {
                    IdleWorld ten = IdleSim.Simulate(seed * 104729, 10, true);
                    deaths += ten.s.deaths;
                    levels += ten.s.level;
                    floors += ten.s.deepestFloor;
                    gold += ten.s.gold;
                    ticks += ten.s.clock;
                }
                lines.Add("First 10 runs, average of " + heroes + " heroes: " + (deaths / (float)heroes).ToString("0.0") + " deaths, level "
                    + (levels / (float)heroes).ToString("0.0") + ", deepest floor " + (floors / (float)heroes).ToString("0.0") + ", gold " + (gold / heroes)
                    + ", " + (ticks / heroes / 600) + " min");

                // The same for the other callings.
                for (int cls = 1; cls < IdleData.Classes; cls++)
                {
                    int cd = 0, cl = 0, cf = 0;
                    for (int seed = 1; seed <= heroes; seed++)
                    {
                        IdleWorld ten = IdleSim.Simulate(seed * 104729, 10, true, -1, cls);
                        cd += ten.s.deaths;
                        cl += ten.s.level;
                        cf += ten.s.deepestFloor;
                    }
                    lines.Add("Same as a " + IdleData.ClassNames[cls].ToLower() + ": " + (cd / (float)heroes).ToString("0.0") + " deaths, level "
                        + (cl / (float)heroes).ToString("0.0") + ", deepest floor " + (cf / (float)heroes).ToString("0.0"));
                }

                // The same 40 heroes with every order switched off but "always attack", for comparison.
                int bareDeaths = 0, bareLevels = 0, bareFloors = 0;
                for (int seed = 1; seed <= heroes; seed++)
                {
                    IdleWorld bare = IdleSim.NewHero(seed * 104729, "Test");
                    bare.s.autoRepeat = 1;
                    bare.s.potionsToCarry = 0;
                    bare.s.potions = 0;
                    for (int i = 0; i < IdleData.RuleSlots; i++)
                        bare.s.ruleIf[i] = IdleData.IfOff;
                    bare.stopAfter = 10;
                    for (int guard = 0; guard < 400 && bare.s.expeditions < 10; guard++)
                        IdleSim.Advance(bare, 36000);
                    bareDeaths += bare.s.deaths;
                    bareLevels += bare.s.level;
                    bareFloors += bare.s.deepestFloor;
                }
                lines.Add("Same, no potions and no orders: " + (bareDeaths / (float)heroes).ToString("0.0") + " deaths, level "
                    + (bareLevels / (float)heroes).ToString("0.0") + ", deepest floor " + (bareFloors / (float)heroes).ToString("0.0"));

                // The same 40 heroes knowing every spell from the start, with the default orders.
                int mageDeaths = 0, mageLevels = 0, mageFloors = 0, mageCasts = 0, enemyCasts = 0;
                for (int seed = 1; seed <= heroes; seed++)
                {
                    IdleWorld mage = IdleSim.NewHero(seed * 104729, "Test");
                    mage.s.autoRepeat = 1;
                    for (int i = 0; i < IdleData.SpellKinds; i++)
                        mage.s.spellsKnown[i] = 1;
                    mage.stopAfter = 10;
                    for (int guard = 0; guard < 400 && mage.s.expeditions < 10; guard++)
                        IdleSim.Advance(mage, 36000);
                    mageDeaths += mage.s.deaths;
                    mageLevels += mage.s.level;
                    mageFloors += mage.s.deepestFloor;
                    mageCasts += mage.s.spellsCast;
                    enemyCasts += mage.s.enemySpellsCast;
                }
                lines.Add("Same, knowing every spell: " + (mageDeaths / (float)heroes).ToString("0.0") + " deaths, level "
                    + (mageLevels / (float)heroes).ToString("0.0") + ", deepest floor " + (mageFloors / (float)heroes).ToString("0.0")
                    + ", " + (mageCasts / heroes) + " spells cast, " + (enemyCasts / heroes) + " enemy spells");

                int[] marks = { 10, 50, 200 };
                for (int i = 0; i < marks.Length; i++)
                    lines.Add("After " + marks[i] + " runs: " + IdleSim.Summary(IdleSim.Simulate(777, marks[i], true)));
                // The game's 18 classes: the first ten runs of 20 heroes each, and 100 runs of one hero.
                for (int career = 0; career < IdleData.Careers; career++)
                {
                    int kd = 0, kf = 0;
                    for (int seed = 1; seed <= 20; seed++)
                    {
                        IdleWorld ten = IdleSim.Simulate(seed * 104729, 10, true, -1, 0, career);
                        kd += ten.s.deaths;
                        kf += ten.s.deepestFloor;
                    }
                    IdleWorld hundred = IdleSim.Simulate(777, 100, true, -1, 0, career);
                    lines.Add(IdleSim.CallingName(hundred.s) + ": first 10 runs " + (kd / 20f).ToString("0.0") + " deaths, floor " + (kf / 20f).ToString("0.0")
                        + "; after 100 runs level " + hundred.s.level + ", deepest " + hundred.s.deepestFloor + ", died " + hundred.s.deaths + ", gold " + hundred.s.gold
                        + ", health " + hundred.s.maxHealth + ", magicka " + IdleSim.MaxMagicka(hundred.s) + ", poison hits " + hundred.s.poisonHits);
                }
                lines.Add("After 200 runs as a mage: " + IdleSim.Summary(IdleSim.Simulate(777, 200, true, -1, IdleData.ClassMage)));
                lines.Add("After 200 runs as a rogue: " + IdleSim.Summary(IdleSim.Simulate(777, 200, true, -1, IdleData.ClassRogue)));
                lines.Add("After 200 runs in natural caves: " + IdleSim.Summary(IdleSim.Simulate(777, 200, true, IdleData.DungeonNaturalCave)));
                lines.Add("After 200 runs in volcanic caves: " + IdleSim.Summary(IdleSim.Simulate(777, 200, true, IdleData.DungeonVolcanicCaves)));
            }
            catch (Exception ex)
            {
                lines.Add("FAIL  " + ex);
            }
            lines.Add("Test took " + watch.ElapsedMilliseconds + " ms");
            for (int i = 0; i < lines.Count; i++)
                Debug.Log("[Idle] TEST " + lines[i]);
            return lines.ToArray();
        }
    }
}
