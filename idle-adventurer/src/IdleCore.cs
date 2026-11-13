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
            int opened = (W != null) ? W.s.opened : 0;
            W = IdleSim.NewHero((int)(DateTime.UtcNow.Ticks & 0x7fffffff), "Adventurer");
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
        DaggerfallStartWindow injectedInto;

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;
            GameObject go = new GameObject(mod.Title);
            go.AddComponent<IdleEntry>();
            mod.IsReady = true;
            Debug.Log("[Idle] Ready.");
        }

        // The main menu is an ordinary window, so the mode's button is simply added to it when it is on top.
        void Update()
        {
            if (DaggerfallUI.Instance == null || DaggerfallUI.UIManager == null)
                return;
            DaggerfallStartWindow start = DaggerfallUI.UIManager.TopWindow as DaggerfallStartWindow;
            if (start == null || start == injectedInto || start.NativePanel == null)
                return;
            injectedInto = start;

            Button b = DaggerfallUI.AddTextButton(new Rect(104, 172, 112, 13), "IDLE ADVENTURER", start.NativePanel);
            b.BackgroundColor = new Color(0.09f, 0.08f, 0.065f, 0.92f);
            b.Outline.Color = new Color(0.72f, 0.58f, 0.18f, 1f);
            b.Label.TextColor = DaggerfallUI.DaggerfallDefaultTextColor;
            b.Hotkey = new HotkeySequence(KeyCode.I, HotkeySequence.KeyModifiers.None);
            b.OnMouseClick += delegate (BaseScreenComponent sender, Vector2 position)
            {
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
                DaggerfallUI.UIManager.PushWindow(new IdleHubWindow(DaggerfallUI.UIManager, injectedInto));
            };
        }
    }

    // ---------------------------------------------------------------------------------------
    // The hub. Milestone 1: a text view of the simulation, driven by keys.
    // ---------------------------------------------------------------------------------------

    public class IdleHubWindow : DaggerfallPopupWindow
    {
        static readonly Color Fill = new Color(0.06f, 0.055f, 0.05f, 0.94f);
        static readonly Color Line = new Color(0.72f, 0.58f, 0.18f, 1f);
        static readonly Color White = new Color(0.94f, 0.93f, 0.9f, 1f);
        static readonly Color Muted = new Color(0.62f, 0.6f, 0.55f, 1f);
        static readonly Color Good = new Color(0.44f, 0.83f, 0.44f, 1f);
        static readonly Color Bad = new Color(1f, 0.45f, 0.4f, 1f);
        static readonly Color Blue = new Color(0.55f, 0.75f, 1f, 1f);

        HubCanvas canvas;
        float carry;
        float sinceSave;
        int speed = 1;

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
            long seconds = (long)Math.Max(0, Math.Min(away, IdleData.OfflineCapSeconds));
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
            canvas.Speed = speed;
            IdleWorld w = IdleStore.W;
            IdleSave s = w.s;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (Input.GetKeyDown(KeyCode.G))
                IdleSim.StartExpedition(w);
            if (Input.GetKeyDown(KeyCode.F))
                speed = (speed == 1) ? 4 : (speed == 4) ? 20 : 1;
            if (Input.GetKeyDown(KeyCode.R))
                s.autoRepeat = (s.autoRepeat == 0) ? 1 : 0;
            if (Input.GetKeyDown(KeyCode.D))
                s.autoDeeper = (s.autoDeeper == 0) ? 1 : 0;
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
                s.targetFloor++;
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
                s.targetFloor = Mathf.Max(1, s.targetFloor - 1);
            if (Input.GetKeyDown(KeyCode.T))
                canvas.Report = IdleTest.Run();
            if (Input.GetKeyDown(KeyCode.N) && shift)
            {
                IdleStore.NewHero();
                IdleStore.W.record = true;
                IdleStore.W.Emit(IdleData.EvInfo, "A new adventurer arrives in town");
                IdleStore.Save();
                return;
            }

            carry += Time.unscaledDeltaTime * IdleData.TicksPerSecond * speed;
            int ticks = (int)carry;
            if (ticks > 0)
            {
                carry -= ticks;
                IdleSim.Advance(w, Mathf.Min(ticks, 6000));
            }

            sinceSave += Time.unscaledDeltaTime;
            if (sinceSave > 30f)
            {
                sinceSave = 0f;
                IdleStore.Save();
            }
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        class HubCanvas : BaseScreenComponent
        {
            public int Speed = 1;
            public string[] Report;

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
                        return Good;
                    case IdleData.EvEnemyHit:
                    case IdleData.EvDied:
                        return Bad;
                    case IdleData.EvLoot:
                        return Blue;
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

                Text(s.heroName + ", level " + s.level + "    Gold " + s.gold + "    Health " + s.health + "/" + s.maxHealth, x, y, sc, White);
                y += line;
                Bar(new Rect(x, y, inner * 0.4f, Mathf.Round(line * 0.3f)), (float)s.health / Mathf.Max(1, s.maxHealth), Bad);
                y += line * 0.6f;

                string gear = "";
                for (int i = 0; i < w.gear.Length; i++)
                    if (w.gear[i] != null)
                        gear += (gear.Length > 0 ? ", " : "") + w.gear[i].LongName;
                y = Wrapped("Gear: " + gear, x, y, inner, sc * 0.9f, line * 0.9f, Muted);

                string doing = IdleSim.PhaseName(s);
                if (s.phase == IdleData.PhaseFight && s.enemyId >= 0)
                    doing += ": " + IdleSim.Enemy(s.enemyId).name + " " + Mathf.Max(0, s.enemyHealth) + "/" + s.enemyMaxHealth;
                else if (s.phase != IdleData.PhaseTown)
                    doing += " (" + (s.phaseLeft / IdleData.TicksPerSecond + 1) + " s)";
                Text(doing, x, y, sc, DaggerfallUI.DaggerfallDefaultTextColor);
                y += line;
                if (s.phase == IdleData.PhaseFight)
                {
                    Bar(new Rect(x, y, inner * 0.4f, Mathf.Round(line * 0.3f)), (float)s.enemyHealth / Mathf.Max(1, s.enemyMaxHealth), Line);
                }
                y += line * 0.6f;

                Text("Orders: target floor " + s.targetFloor + ", retreat below " + s.retreatPercent + "% health, auto-repeat "
                    + (s.autoRepeat != 0 ? "on" : "off") + ", go deeper after a clear " + (s.autoDeeper != 0 ? "on" : "off") + ", speed " + Speed + "x",
                    x, y, sc * 0.9f, Muted);
                y += line;
                Text("Runs " + s.expeditions + " (cleared " + s.cleared + ", retreated " + s.retreats + ", died " + s.deaths + ")    Kills " + s.kills
                    + "    Deepest floor " + s.deepestFloor, x, y, sc * 0.9f, Muted);
                y += line * 1.3f;

                float bottom = r.yMax - pad - line;
                Text("G go    R auto-repeat    D go deeper    +/- target floor    F speed    T balance test    Shift+N new hero    Esc back",
                    x, bottom, sc * 0.85f, Muted);

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

                int clearedFirst = 0;
                int diedFirst = 0;
                const int heroes = 40;
                for (int seed = 1; seed <= heroes; seed++)
                {
                    IdleWorld one = IdleSim.Simulate(seed * 7919, 1, false);
                    clearedFirst += one.s.cleared;
                    diedFirst += one.s.deaths;
                }
                lines.Add("New hero, first run, floor 1 only: " + clearedFirst + " of " + heroes + " cleared it, " + diedFirst + " died, "
                    + (heroes - clearedFirst - diedFirst) + " retreated");

                int[] marks = { 10, 50, 200 };
                for (int i = 0; i < marks.Length; i++)
                    lines.Add("After " + marks[i] + " runs: " + IdleSim.Summary(IdleSim.Simulate(777, marks[i], true)));
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
