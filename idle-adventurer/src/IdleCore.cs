// Idle Adventurer - a Daggerfall Unity mod.
// Milestone 0 spike: a main-menu button, an empty hub, one animated sprite pair, one generated item and a save file.

using System;
using System.IO;
using UnityEngine;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility;

namespace IdleAdventurer
{
    // The mode's own save file. Flat on purpose: JsonUtility drops nested classes from runtime-compiled mods.
    [Serializable]
    public class IdleSave
    {
        public int version = 1;
        public string heroName = "Adventurer";
        public int opened;
        public long lastSeenUtcTicks;
        // Items are stored the way DFU stores them, as a serialised ItemData_v1 array.
        public string itemsBlob;
    }

    public static class IdleStore
    {
        public static IdleSave S;

        public static string FilePath
        {
            get { return Path.Combine(Application.persistentDataPath, "IdleAdventurer.json"); }
        }

        public static void Load()
        {
            S = null;
            try
            {
                if (File.Exists(FilePath))
                    S = JsonUtility.FromJson<IdleSave>(File.ReadAllText(FilePath));
            }
            catch (Exception ex)
            {
                Debug.LogError("[Idle] Could not read the save, starting fresh: " + ex.Message);
            }
            if (S == null)
                S = new IdleSave();
        }

        public static void Save()
        {
            try
            {
                S.lastSeenUtcTicks = DateTime.UtcNow.Ticks;
                File.WriteAllText(FilePath, JsonUtility.ToJson(S, true));
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
    // The hub. For now it only proves the pieces work with no game loaded.
    // ---------------------------------------------------------------------------------------

    public class IdleHubWindow : DaggerfallPopupWindow
    {
        static readonly Color Fill = new Color(0.06f, 0.055f, 0.05f, 0.94f);
        static readonly Color Line = new Color(0.72f, 0.58f, 0.18f, 1f);
        static readonly Color White = new Color(0.94f, 0.93f, 0.9f, 1f);
        static readonly Color Muted = new Color(0.62f, 0.6f, 0.55f, 1f);
        static readonly Color Good = new Color(0.44f, 0.83f, 0.44f, 1f);
        static readonly Color Bad = new Color(1f, 0.45f, 0.4f, 1f);

        HubCanvas canvas;

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
            canvas.RunChecks();
        }

        public override void Update()
        {
            base.Update();
            if (canvas != null)
                canvas.Size = new Vector2(Screen.width, Screen.height);
        }

        public override void OnPop()
        {
            base.OnPop();
            IdleStore.Save();
        }

        class HubCanvas : BaseScreenComponent
        {
            string[] lines = new string[0];
            bool[] ok = new bool[0];
            Texture2D[] heroFrames;
            Texture2D[] enemyFrames;
            DaggerfallUnityItem item;
            Texture2D itemIcon;

            static Texture2D[] Frames(int archive, int record)
            {
                ImageData data = ImageReader.GetImageData(TextureFile.IndexToFileName(archive), record, 0, true, true, true);
                if (data.animatedTextures != null && data.animatedTextures.Length > 0)
                    return data.animatedTextures;
                return (data.texture != null) ? new Texture2D[] { data.texture } : null;
            }

            public void RunChecks()
            {
                System.Collections.Generic.List<string> text = new System.Collections.Generic.List<string>();
                System.Collections.Generic.List<bool> good = new System.Collections.Generic.List<bool>();

                // 1. Save file round trip.
                try
                {
                    IdleStore.Load();
                    IdleStore.S.opened++;
                    DateTime last = new DateTime(IdleStore.S.lastSeenUtcTicks, DateTimeKind.Utc);
                    string away = (IdleStore.S.lastSeenUtcTicks == 0) ? "first visit" : "away " + (int)(DateTime.UtcNow - last).TotalSeconds + " s";
                    IdleStore.Save();
                    text.Add("Save file: opened " + IdleStore.S.opened + " time(s), " + away);
                    good.Add(true);
                }
                catch (Exception ex)
                {
                    text.Add("Save file: " + ex.Message);
                    good.Add(false);
                }

                // 2. Sprites: a barbarian seen from behind, attacking, and a skeletal warrior facing us.
                try
                {
                    MobileEnemy hero;
                    MobileEnemy enemy;
                    EnemyBasics.GetEnemy(MobileTypes.Barbarian, out hero);
                    EnemyBasics.GetEnemy(MobileTypes.SkeletalWarrior, out enemy);
                    heroFrames = Frames(hero.MaleTexture, 9);
                    enemyFrames = Frames(enemy.MaleTexture, 5);
                    bool fine = heroFrames != null && enemyFrames != null;
                    text.Add("Sprites: hero " + (heroFrames != null ? heroFrames.Length : 0) + " frames, enemy " + (enemyFrames != null ? enemyFrames.Length : 0) + " frames");
                    good.Add(fine);
                }
                catch (Exception ex)
                {
                    text.Add("Sprites: " + ex.Message);
                    good.Add(false);
                }

                // 3. An item made with no game loaded, saved and read back the way DFU saves items.
                try
                {
                    item = ItemBuilder.CreateRandomWeapon(8);
                    ItemCollection bag = new ItemCollection();
                    bag.AddItem(item);
                    IdleStore.S.itemsBlob = SaveLoadManager.Serialize(typeof(ItemData_v1[]), bag.SerializeItems(), false);
                    IdleStore.Save();
                    ItemData_v1[] back = (ItemData_v1[])SaveLoadManager.Deserialize(typeof(ItemData_v1[]), IdleStore.S.itemsBlob);
                    ItemCollection bag2 = new ItemCollection();
                    bag2.DeserializeItems(back);
                    DaggerfallUnityItem again = bag2.GetItem(0);
                    itemIcon = DaggerfallUnity.Instance.ItemHelper.GetInventoryImage(again).texture;
                    text.Add("Item: " + again.LongName + ", " + again.GetBaseDamageMin() + "-" + again.GetBaseDamageMax() + " damage, read back from the save");
                    good.Add(again.LongName == item.LongName);
                }
                catch (Exception ex)
                {
                    text.Add("Item: " + ex.Message);
                    good.Add(false);
                }

                lines = text.ToArray();
                ok = good.ToArray();
                for (int i = 0; i < lines.Length; i++)
                    Debug.Log("[Idle] " + (ok[i] ? "OK  " : "FAIL ") + lines[i]);
            }

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

            static void Sprite(Texture2D[] frames, Rect area, float fps)
            {
                if (frames == null || frames.Length == 0)
                    return;
                Texture2D t = frames[(int)(Time.unscaledTime * fps) % frames.Length];
                if (t != null)
                    DaggerfallUI.DrawTexture(area, t, ScaleMode.ScaleToFit, true);
            }

            public override void Draw()
            {
                base.Draw();
                float W = Screen.width;
                float H = Screen.height;
                Box(new Rect(0, 0, W, H), new Color(0f, 0f, 0f, 0.8f));

                float w = Mathf.Round(Mathf.Min(W * 0.6f, H * 1.3f));
                float h = Mathf.Round(H * 0.7f);
                Rect r = new Rect(Mathf.Round((W - w) / 2f), Mathf.Round((H - h) / 2f), w, h);
                Frame(r);

                float s = H * 0.036f * 0.74f / DaggerfallUI.DefaultFont.GlyphHeight;
                float line = Mathf.Round(H * 0.045f);
                float pad = Mathf.Round(H * 0.025f);
                Text("Idle Adventurer", r.x + pad, r.y + pad, s * 1.4f, DaggerfallUI.DaggerfallDefaultTextColor);
                Text("Proof of concept. Press Escape to go back.", r.x + pad, r.y + pad + line * 1.3f, s * 0.9f, Muted);

                // Battle scene stand-in: enemy in the middle facing us, hero in the foreground from behind.
                Rect scene = new Rect(r.x + pad, r.y + pad + line * 2.6f, w - 2f * pad, h * 0.5f);
                Box(scene, new Color(1f, 1f, 1f, 0.05f));
                float eh = scene.height * 0.72f;
                Sprite(enemyFrames, new Rect(scene.x + scene.width * 0.5f - eh * 0.4f, scene.y + scene.height * 0.06f, eh * 0.8f, eh), 6f);
                float hh = scene.height * 0.8f;
                Sprite(heroFrames, new Rect(scene.x + scene.width * 0.22f - hh * 0.4f, scene.yMax - hh, hh * 0.8f, hh), 6f);
                if (itemIcon != null)
                    DaggerfallUI.DrawTexture(new Rect(scene.xMax - scene.height * 0.4f, scene.y + scene.height * 0.3f, scene.height * 0.35f, scene.height * 0.3f), itemIcon, ScaleMode.ScaleToFit, true);

                float y = scene.yMax + pad;
                for (int i = 0; i < lines.Length; i++)
                {
                    Text(ok[i] ? "OK" : "FAIL", r.x + pad, y, s, ok[i] ? Good : Bad);
                    Text(lines[i], r.x + pad + line * 2.2f, y, s, White);
                    y += line;
                }
            }
        }
    }
}
