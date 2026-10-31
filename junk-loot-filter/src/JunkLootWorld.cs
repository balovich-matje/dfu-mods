// Junk & Loot Filter - a Daggerfall Unity mod.
// In-world parts: Skyrim-style HUD (vitals bars, compass, effect icons, interaction icon) and corpse clean-up.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace JunkLootFilter
{
    // ---------------------------------------------------------------------------------------
    // DFU's own compass border (COMPBOX.IMG) drawn around a panel; the corners stay crisp however the panel is stretched.
    // ---------------------------------------------------------------------------------------

    public class FramedPanel : Panel
    {
        const float TexW = 69f;
        const float TexH = 17f;
        const float Border = 2f;

        static Texture2D frameTexture;
        static bool frameTried;

        // Size of one texture pixel on screen.
        public float FrameScale = 2f;

        static Rect Src(float x0, float y0, float x1, float y1)
        {
            return new Rect(x0 / TexW, 1f - y1 / TexH, (x1 - x0) / TexW, (y1 - y0) / TexH);
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled)
                return;
            if (!frameTried)
            {
                frameTried = true;
                frameTexture = DaggerfallUI.GetTextureFromImg("COMPBOX.IMG");
            }
            if (frameTexture == null)
                return;

            Rect r = Rectangle;
            float t = Mathf.Round(Border * FrameScale);
            float x0 = Mathf.Round(r.x);
            float y0 = Mathf.Round(r.y);
            float x1 = Mathf.Round(r.xMax);
            float y1 = Mathf.Round(r.yMax);
            Piece(x0, y0, t, t, Src(0, 0, 2, 2));
            Piece(x1 - t, y0, t, t, Src(67, 0, 69, 2));
            Piece(x0, y1 - t, t, t, Src(0, 15, 2, 17));
            Piece(x1 - t, y1 - t, t, t, Src(67, 15, 69, 17));
            Piece(x0 + t, y0, x1 - x0 - 2f * t, t, Src(34, 0, 58, 2));
            Piece(x0 + t, y1 - t, x1 - x0 - 2f * t, t, Src(34, 15, 58, 17));
            Piece(x0, y0 + t, t, y1 - y0 - 2f * t, Src(0, 5, 2, 12));
            Piece(x1 - t, y0 + t, t, y1 - y0 - 2f * t, Src(67, 5, 69, 12));
        }

        void Piece(float x, float y, float w, float h, Rect uv)
        {
            if (w > 0f && h > 0f)
                DaggerfallUI.DrawTextureWithTexCoords(new Rect(x, y, w, h), frameTexture, uv, true);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Vitals bars: magicka on the left, health in the centre, stamina on the right, along the bottom.
    // ---------------------------------------------------------------------------------------

    public class HudBars : Panel
    {
        class Bar
        {
            public FramedPanel frame;
            public Panel fill;
            public TextLabel text;
            public float shown = 1f;
            public string lastText = string.Empty;
            public float lastScale;
        }

        static readonly Color FrameColor = new Color(0.03f, 0.03f, 0.03f, 0.72f);
        static readonly Color RedBar = new Color(0.72f, 0.10f, 0.10f, 1f);
        static readonly Color GreenBar = new Color(0.22f, 0.60f, 0.20f, 1f);
        static readonly Color BlueBar = new Color(0.18f, 0.32f, 0.82f, 1f);

        Bar health = new Bar();
        Bar fatigue = new Bar();
        Bar magicka = new Bar();

        public static float BarHeight(float screenH)
        {
            return Mathf.Clamp(screenH * 0.03f, 14f, 44f);
        }

        public static float FrameScaleFor(float barH)
        {
            return Mathf.Max(1f, Mathf.Round(barH / 17f));
        }

        public HudBars()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            Build(magicka);
            Build(health);
            Build(fatigue);
        }

        void Build(Bar b)
        {
            b.frame = new FramedPanel();
            b.frame.AutoSize = AutoSizeModes.None;
            b.frame.BackgroundColor = FrameColor;

            b.fill = new Panel();
            b.fill.AutoSize = AutoSizeModes.None;
            b.frame.Components.Add(b.fill);

            b.text = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, string.Empty, b.frame);
            b.text.HorizontalAlignment = HorizontalAlignment.Center;
            b.text.VerticalAlignment = VerticalAlignment.Middle;
            b.text.TextColor = new Color(0.97f, 0.95f, 0.88f, 1f);
            b.text.ShadowPosition = new Vector2(1f, 1f);
            b.text.ShadowColor = new Color(0f, 0f, 0f, 0.95f);

            Components.Add(b.frame);
        }

        void Place(Bar b, float x, float y, float w, float h, int cur, int max, int anchor, Color color)
        {
            float fs = FrameScaleFor(h);
            b.frame.FrameScale = fs;
            b.frame.Position = new Vector2(x, y);
            b.frame.Size = new Vector2(w, h);

            float target = (max > 0) ? Mathf.Clamp01(cur / (float)max) : 0f;
            b.shown = Mathf.MoveTowards(b.shown, target, Time.unscaledDeltaTime * 0.9f);

            // anchor: 0 shrinks towards the middle, 1 anchors on the right (left bar), -1 on the left (right bar).
            float inset = 2f * fs + 1f;
            float inner = w - 2f * inset;
            float fillW = inner * b.shown;
            float fillX = inset;
            if (anchor == 0)
                fillX += (inner - fillW) * 0.5f;
            else if (anchor > 0)
                fillX += inner - fillW;
            b.fill.Position = new Vector2(fillX, inset);
            b.fill.Size = new Vector2(fillW, h - 2f * inset);
            b.fill.BackgroundColor = color;

            string t = cur + " / " + max;
            if (t != b.lastText)
            {
                b.lastText = t;
                b.text.Text = t;
            }
            float scale = (h * 0.7f) / Mathf.Max(1, DaggerfallUI.DefaultFont.GlyphHeight);
            if (Mathf.Abs(scale - b.lastScale) > 0.01f)
            {
                b.lastScale = scale;
                b.text.TextScale = scale;
            }
        }

        public override void Update()
        {
            PlayerEntity p = GameManager.Instance.PlayerEntity;
            if (p == null)
                return;

            float w = Screen.width;
            float h = Screen.height;
            Position = Vector2.zero;
            Size = new Vector2(w, h);

            float barH = BarHeight(h);
            float y = h - h * 0.035f - barH;
            float barW = w * 0.21f;
            float gap = w * 0.06f;
            float x0 = (w - 3f * barW - 2f * gap) * 0.5f;

            // Health is always red and stamina green, whatever DFU's swap-colours option says.
            Place(magicka, x0, y, barW, barH, p.CurrentMagicka, p.MaxMagicka, 1, BlueBar);
            Place(health, x0 + barW + gap, y, barW, barH, p.CurrentHealth, p.MaxHealth, 0, RedBar);
            Place(fatigue, x0 + 2f * (barW + gap), y, barW, barH,
                p.CurrentFatigue / DaggerfallEntity.FatigueMultiplier, p.MaxFatigue / DaggerfallEntity.FatigueMultiplier,
                -1, GreenBar);

            base.Update();
        }
    }

    // ---------------------------------------------------------------------------------------
    // A wide compass for the top centre of the screen: letters and ticks scrolling past a fixed centre mark.
    // ---------------------------------------------------------------------------------------

    public class WideCompass : FramedPanel
    {
        const float Span = 120f;        // degrees visible across the bar
        static readonly string[] Names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        static readonly Color Ink = new Color(0.97f, 0.93f, 0.78f, 1f);
        static readonly Color TickColor = new Color(0.78f, 0.66f, 0.30f, 1f);

        public HUDCompass source;

        readonly TextLabel[] letters = new TextLabel[8];
        readonly Panel[] ticks = new Panel[24];
        readonly Panel[] needles = new Panel[2];
        readonly Panel[] marks = new Panel[16];
        readonly System.Reflection.FieldInfo detectors;
        Texture2D markerTexture;

        public WideCompass()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = new Color(0.03f, 0.03f, 0.03f, 0.72f);

            for (int i = 0; i < letters.Length; i++)
            {
                TextLabel l = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, Names[i], this);
                l.ShadowPosition = new Vector2(1f, 1f);
                l.ShadowColor = new Color(0f, 0f, 0f, 0.95f);
                l.TextColor = Ink;
                letters[i] = l;
            }
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = new Panel();
                ticks[i].AutoSize = AutoSizeModes.None;
                Components.Add(ticks[i]);
            }
            for (int i = 0; i < needles.Length; i++)
            {
                needles[i] = new Panel();
                needles[i].AutoSize = AutoSizeModes.None;
                needles[i].BackgroundColor = new Color(0.95f, 0.85f, 0.35f, 1f);
                Components.Add(needles[i]);
            }

            markerTexture = Resources.Load<Texture2D>("DetectMarker");
            for (int i = 0; i < marks.Length; i++)
            {
                marks[i] = new Panel();
                marks[i].AutoSize = AutoSizeModes.None;
                marks[i].BackgroundTexture = markerTexture;
                marks[i].Enabled = false;
                Components.Add(marks[i]);
            }
            detectors = typeof(HUDCompass).GetField("registeredDetectors",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

        public override void Update()
        {
            float w = Screen.width;
            float h = Screen.height;
            float barH = HudBars.BarHeight(h);
            float fs = HudBars.FrameScaleFor(barH);
            float cw = Mathf.Clamp(w * 0.32f, 300f, 800f);
            float ch = barH * 1.15f;
            float inset = 2f * fs + 1f;

            FrameScale = fs;
            Position = new Vector2(Mathf.Round((w - cw) * 0.5f), Mathf.Max(8f, h * 0.025f));
            Size = new Vector2(cw, ch);

            Camera cam = (source != null) ? source.CompassCamera : null;
            if (cam == null)
                cam = Camera.main;
            float heading = (cam != null) ? cam.transform.eulerAngles.y : 0f;

            float cx = cw * 0.5f;
            float ppd = (cw - 2f * inset) / Span;
            float half = Span * 0.5f;
            float tickW = Mathf.Max(1f, Mathf.Round(fs));
            float tickH = ch * 0.30f;
            float letterPx = ch * 1.0f;

            for (int i = 0; i < 24; i++)
            {
                float dx = Mathf.DeltaAngle(heading, i * 15f);
                float fade = Mathf.Clamp01((half - Mathf.Abs(dx)) / 12f);
                float x = cx + dx * ppd;
                bool named = (i % 3 == 0);

                Panel tick = ticks[i];
                tick.Enabled = !named && fade > 0.02f;
                if (tick.Enabled)
                {
                    tick.Size = new Vector2(tickW, tickH);
                    tick.Position = new Vector2(Mathf.Round(x - tickW * 0.5f), Mathf.Round((ch - tickH) * 0.5f));
                    Color c = TickColor;
                    c.a = fade * 0.85f;
                    tick.BackgroundColor = c;
                }

                if (named)
                {
                    TextLabel l = letters[i / 3];
                    l.Enabled = fade > 0.02f;
                    if (l.Enabled)
                    {
                        bool cardinal = (i % 6 == 0);
                        l.TextScale = (cardinal ? letterPx : letterPx * 0.75f) / Mathf.Max(1, DaggerfallUI.DefaultFont.GlyphHeight);
                        Color c = Ink;
                        c.a = fade;
                        l.TextColor = c;
                        Color s = new Color(0f, 0f, 0f, 0.95f * fade);
                        l.ShadowColor = s;
                        l.Position = new Vector2(Mathf.Round(x - l.TextWidth * 0.5f), Mathf.Round((ch - l.TextHeight) * 0.5f));
                    }
                }
            }

            float nw = Mathf.Max(2f, Mathf.Round(fs));
            needles[0].Size = new Vector2(nw, ch * 0.22f);
            needles[0].Position = new Vector2(Mathf.Round(cx - nw * 0.5f), inset);
            needles[1].Size = new Vector2(nw, ch * 0.22f);
            needles[1].Position = new Vector2(Mathf.Round(cx - nw * 0.5f), ch - inset - ch * 0.22f);

            UpdateMarks(cam, cx, ppd, ch);
            base.Update();
        }

        // Markers from Detect spells, which DFU's own compass would have drawn above its box.
        void UpdateMarks(Camera cam, float cx, float ppd, float ch)
        {
            int n = 0;
            System.Collections.IList list = (detectors != null && source != null) ? detectors.GetValue(source) as System.Collections.IList : null;
            if (list != null && cam != null && markerTexture != null && GameManager.Instance.PlayerObject != null)
            {
                Vector3 p = GameManager.Instance.PlayerObject.transform.position;
                Vector3 f = cam.transform.forward;
                f.y = 0f;
                float size = ch * 0.6f;
                for (int i = 0; i < list.Count && n < marks.Length; i++)
                {
                    DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects.DetectEffect d =
                        list[i] as DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects.DetectEffect;
                    if (d == null || d.HasEnded || d.DetectedObjects == null)
                        continue;
                    for (int j = 0; j < d.DetectedObjects.Count && n < marks.Length; j++)
                    {
                        GameObject go = d.DetectedObjects[j].gameObject;
                        if (go == null)
                            continue;
                        Vector3 dir = go.transform.position - p;
                        dir.y = 0f;
                        float angle = Vector3.SignedAngle(f, dir, Vector3.up);
                        if (Mathf.Abs(angle) > Span * 0.5f)
                            continue;
                        Panel m = marks[n++];
                        m.Enabled = true;
                        m.Size = new Vector2(size, size);
                        m.Position = new Vector2(Mathf.Round(cx + angle * ppd - size * 0.5f), ch + 2f);
                    }
                }
            }
            for (int i = n; i < marks.Length; i++)
                marks[i].Enabled = false;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Draws DFU's interaction-mode icon with an adjustable opacity.
    // ---------------------------------------------------------------------------------------

    public class FadingIcon : BaseScreenComponent
    {
        public Texture2D Texture;
        public Rect Area;
        public float Alpha = 1f;

        public override void Draw()
        {
            if (!Enabled || Texture == null || Alpha <= 0.01f)
                return;
            DaggerfallUI.DrawTexture(Area, Texture, ScaleMode.StretchToFill, true, new Color(1f, 1f, 1f, Alpha));
        }
    }

    // ---------------------------------------------------------------------------------------
    // Keeps the extra HUD pieces attached, hides DFU's own versions, and decorates the active effect icons.
    // ---------------------------------------------------------------------------------------

    public class HudTweaks : MonoBehaviour
    {
        const int IconsPerRow = 8;
        const float ModeShownSeconds = 5f;
        const float ModeFadeSeconds = 5f;

        static readonly Color BuffBorder = new Color(0.35f, 0.85f, 0.35f, 0.85f);
        static readonly Color DebuffBorder = new Color(0.90f, 0.25f, 0.22f, 0.85f);

        HudBars bars;
        DaggerfallHUD barsHud;
        WideCompass compass;
        DaggerfallHUD compassHud;
        FadingIcon modeIcon;
        DaggerfallHUD modeHud;
        int lastMode = -1;
        float modeSwitched;
        bool spellsMoved;
        readonly Dictionary<Panel, TextLabel> labels = new Dictionary<Panel, TextLabel>();
        readonly Dictionary<string, bool> harmfulByName = new Dictionary<string, bool>();

        void OnEnable()
        {
            SaveLoadManager.OnLoad += OnGameLoaded;
            StartGameBehaviour.OnNewGame += ResetModeTimer;
        }

        void OnDisable()
        {
            SaveLoadManager.OnLoad -= OnGameLoaded;
            StartGameBehaviour.OnNewGame -= ResetModeTimer;
        }

        void OnGameLoaded(SaveData_v1 saveData)
        {
            ResetModeTimer();
        }

        // The mode icon starts its visible period again when a game starts or a save is loaded.
        void ResetModeTimer()
        {
            modeSwitched = Time.unscaledTime;
        }

        void LateUpdate()
        {
            if (JunkStore.S == null || DaggerfallUI.Instance == null)
                return;
            DaggerfallHUD hud = DaggerfallUI.Instance.DaggerfallHUD;
            if (hud == null || GameManager.Instance.PlayerEntity == null)
                return;

            UpdateBars(hud);
            UpdateCompass(hud);
            UpdateModeIcon(hud);
            UpdateEffectIcons(hud);
        }

        // -- bars ----------------------------------------------------------------------------

        void UpdateBars(DaggerfallHUD hud)
        {
            bool want = JunkStore.S.skyrimBars;
            HUDVitals vitals = hud.HUDVitals;

            // DFU's own bars are parked off screen rather than disabled, because DFU re-enables them every frame.
            Vector2 away = new Vector2(-10000f, -10000f);
            Vector2 tiny = Vector2.one;
            vitals.CustomHealthBarPosition = want ? away : (Vector2?)null;
            vitals.CustomFatigueBarPosition = want ? away : (Vector2?)null;
            vitals.CustomMagickaBarPosition = want ? away : (Vector2?)null;
            vitals.CustomHealthBarSize = want ? tiny : (Vector2?)null;
            vitals.CustomFatigueBarSize = want ? tiny : (Vector2?)null;
            vitals.CustomMagickaBarSize = want ? tiny : (Vector2?)null;

            if (bars == null || barsHud != hud)
            {
                if (bars != null && barsHud != null)
                    barsHud.ParentPanel.Components.Remove(bars);
                bars = new HudBars();
                hud.ParentPanel.Components.Add(bars);
                barsHud = hud;
            }
            // Same visibility rules as DFU's bars (hidden with the HUD, or when the large HUD is shown).
            bars.Enabled = want && vitals.Enabled;
        }

        // -- compass -------------------------------------------------------------------------

        void UpdateCompass(DaggerfallHUD hud)
        {
            HUDCompass vanilla = hud.HUDCompass;
            if (compass == null || compassHud != hud)
            {
                if (compass != null && compassHud != null)
                    compassHud.ParentPanel.Components.Remove(compass);
                compass = new WideCompass();
                compass.source = vanilla;
                hud.ParentPanel.Components.Add(compass);
                compassHud = hud;
            }

            if (!JunkStore.S.skyrimCompass)
            {
                compass.Enabled = false;
                return;
            }
            // DFU sets its compass' visibility every frame (hidden with the HUD or by the large HUD); follow it.
            compass.Enabled = vanilla.Enabled;
            vanilla.Enabled = false;
        }

        // -- interaction mode icon -----------------------------------------------------------

        void UpdateModeIcon(DaggerfallHUD hud)
        {
            if (modeIcon == null || modeHud != hud)
            {
                if (modeIcon != null && modeHud != null)
                    modeHud.ParentPanel.Components.Remove(modeIcon);
                modeIcon = new FadingIcon();
                hud.ParentPanel.Components.Add(modeIcon);
                modeHud = hud;
            }

            HUDInteractionModeIcon icon = null;
            Panel parent = hud.ParentPanel;
            for (int i = 0; i < parent.Components.Count; i++)
            {
                icon = parent.Components[i] as HUDInteractionModeIcon;
                if (icon != null)
                    break;
            }

            int mode = (int)GameManager.Instance.PlayerActivate.CurrentMode;
            if (mode != lastMode)
            {
                lastMode = mode;
                modeSwitched = Time.unscaledTime;
            }

            // The crosshair-style icons never set a background texture; leave those alone.
            if (!JunkStore.S.fadeModeIcon || icon == null || !icon.Enabled || icon.BackgroundTexture == null)
            {
                modeIcon.Enabled = false;
                return;
            }

            float h = Screen.height;
            Rect r = icon.Rectangle;
            float pad = Mathf.Max(8f, h * 0.025f);
            modeIcon.Texture = icon.BackgroundTexture;
            modeIcon.Area = new Rect(pad, h - h * 0.035f - r.height, r.width, r.height);
            float age = Time.unscaledTime - modeSwitched;
            modeIcon.Alpha = 1f - Mathf.Clamp01((age - ModeShownSeconds) / ModeFadeSeconds);
            modeIcon.Enabled = true;
            icon.Enabled = false;
        }

        // -- effect icons --------------------------------------------------------------------

        static bool HarmfulKey(string k)
        {
            if (string.IsNullOrEmpty(k))
                return false;
            if (k.StartsWith("Damage-") || k.StartsWith("ContinuousDamage-") || k.StartsWith("Drain-") || k.StartsWith("Transfer-")
                || k.StartsWith("Weakness") || k.StartsWith("Disease") || k.StartsWith("Poison"))
                return true;
            return k == "Paralyze" || k == "Silence" || k == "Disintegrate" || k == "ItemDeteriorates" || k == "UserTakesDamage"
                || k == "BadRepWith" || k == "BadReactionsFrom" || k == "LowDamageVs" || k == "WeakensArmor" || k == "ExtraWeight";
        }

        void CollectHarmful()
        {
            harmfulByName.Clear();
            LiveEffectBundle[] bundles = GameManager.Instance.PlayerEffectManager.EffectBundles;
            if (bundles == null)
                return;
            for (int i = 0; i < bundles.Length; i++)
            {
                LiveEffectBundle b = bundles[i];
                if (b == null || b.liveEffects == null || b.name == null)
                    continue;
                bool harmful = false;
                for (int j = 0; j < b.liveEffects.Count; j++)
                {
                    if (HarmfulKey(b.liveEffects[j].Key))
                        harmful = true;
                }
                string name = b.name.TrimStart('!');
                bool old;
                harmfulByName[name] = harmful || (harmfulByName.TryGetValue(name, out old) && old);
            }
        }

        TextLabel LabelFor(Panel icon)
        {
            TextLabel l;
            if (!labels.TryGetValue(icon, out l))
            {
                l = DaggerfallUI.AddTextLabel(DaggerfallUI.DefaultFont, Vector2.zero, string.Empty, icon);
                l.HorizontalAlignment = HorizontalAlignment.Center;
                l.ShadowPosition = new Vector2(1.5f, 1.5f);
                l.ShadowColor = new Color(0f, 0f, 0f, 1f);
                l.TextColor = new Color(0.97f, 0.95f, 0.88f, 1f);
                labels[icon] = l;
            }
            return l;
        }

        // DFU keeps the effect icons in a panel inside its centred 4:3 area. Moving that panel to the full-screen
        // layer lets the icons sit in the corner of the screen, laid out in screen pixels.
        void MoveSpells(DaggerfallHUD hud, bool toScreen)
        {
            HUDActiveSpells spells = hud.ActiveSpells;
            if (toScreen && !spellsMoved)
            {
                hud.NativePanel.Components.Remove(spells);
                hud.ParentPanel.Components.Add(spells);
                spellsMoved = true;
            }
            else if (!toScreen && spellsMoved)
            {
                hud.ParentPanel.Components.Remove(spells);
                hud.NativePanel.Components.Add(spells);
                spells.HorizontalAlignment = HorizontalAlignment.Center;
                spells.Size = hud.NativePanel.Size;
                spells.UpdateIcons();
                spellsMoved = false;
            }
            if (toScreen)
            {
                spells.HorizontalAlignment = HorizontalAlignment.None;
                spells.Position = Vector2.zero;
                spells.Size = new Vector2(Screen.width, Screen.height);
            }
        }

        void UpdateEffectIcons(DaggerfallHUD hud)
        {
            HUDActiveSpells spells = hud.ActiveSpells;
            if (spells == null)
                return;

            bool on = JunkStore.S.effectLabels;
            MoveSpells(hud, on);
            if (on)
                CollectHarmful();

            int buffs = 0;
            int debuffs = 0;
            // Buffs first, then debuffs below them. DFU keeps icons in a pool of panels, filled from the start.
            List<Panel> active = new List<Panel>();
            for (int i = 0; i < spells.Components.Count; i++)
            {
                Panel icon = spells.Components[i] as Panel;
                if (icon != null && icon.BackgroundTexture != null && !string.IsNullOrEmpty(icon.ToolTipText))
                    active.Add(icon);
                else if (icon != null)
                    Decorate(icon, false, false, string.Empty, 0f, 0);
            }
            if (!on)
            {
                for (int i = 0; i < active.Count; i++)
                    Decorate(active[i], false, false, string.Empty, 0f, 0);
                return;
            }

            float h = Screen.height;
            float pad = Mathf.Max(8f, h * 0.025f);
            float iconPx = Mathf.Clamp(h * 0.034f, 20f, 40f);
            float textPx = Mathf.Clamp(iconPx * 0.42f, 9f, 18f);
            float gap = iconPx * 0.3f;
            float rowH = iconPx + textPx + 10f;
            int thickness = Mathf.Clamp(Mathf.RoundToInt(h / 500f), 1, 4);

            int buffCount = 0;
            for (int i = 0; i < active.Count; i++)
            {
                bool bad;
                if (!harmfulByName.TryGetValue(active[i].ToolTipText, out bad))
                    bad = false;
                if (!bad)
                    buffCount++;
            }
            int buffRows = (buffCount + IconsPerRow - 1) / IconsPerRow;
            float[] cursorX = new float[2];

            for (int i = 0; i < active.Count; i++)
            {
                Panel icon = active[i];
                bool bad;
                if (!harmfulByName.TryGetValue(icon.ToolTipText, out bad))
                    bad = false;

                int slot = bad ? debuffs++ : buffs++;
                int col = slot % IconsPerRow;
                if (col == 0)
                    cursorX[bad ? 1 : 0] = pad;
                icon.Size = new Vector2(iconPx, iconPx);
                Decorate(icon, true, bad, icon.ToolTipText, textPx, thickness);

                // Each icon takes the room its name needs, so the spacing stays even whatever the names are.
                float textW = labels[icon].TextWidth;
                float slotW = Mathf.Max(iconPx, textW);
                float rowStart = pad + (bad ? buffRows * rowH : 0f);
                icon.Position = new Vector2(Mathf.Round(cursorX[bad ? 1 : 0] + (slotW - iconPx) * 0.5f),
                    Mathf.Round(rowStart + (slot / IconsPerRow) * rowH));
                cursorX[bad ? 1 : 0] += slotW + gap;
            }
        }

        void Decorate(Panel icon, bool show, bool bad, string name, float textPx, int thickness)
        {
            icon.Outline.Enabled = show;
            if (show)
            {
                icon.Outline.Color = bad ? DebuffBorder : BuffBorder;
                icon.Outline.Thickness = thickness;
            }

            TextLabel label;
            if (!show)
            {
                if (labels.TryGetValue(icon, out label) && label.Text.Length > 0)
                    label.Text = string.Empty;
                return;
            }

            label = LabelFor(icon);
            string shown = name.Length > 16 ? name.Substring(0, 15) + "." : name;
            if (label.Text != shown)
                label.Text = shown;
            label.TextScale = textPx / Mathf.Max(1, DaggerfallUI.DefaultFont.GlyphHeight);
            label.Position = new Vector2(0f, icon.Size.y + 2f);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Corpse clean-up: empty corpses fade out and disappear after 5 seconds; corpses left with items after
    // a quick loot do the same after 10 seconds.
    // ---------------------------------------------------------------------------------------

    public class CorpseCleaner : MonoBehaviour
    {
        const float EmptyDelay = 5f;
        const float LeftoverDelay = 10f;

        class Fade
        {
            public float start;
            public float end;
            public MeshRenderer renderer;
            public Material original;
            public Material blended;
        }

        // The corpse whose inventory is open right now; it is left alone until the window closes.
        public static DaggerfallLoot OpenLoot;

        static readonly Dictionary<DaggerfallLoot, float> leftover = new Dictionary<DaggerfallLoot, float>();
        readonly Dictionary<DaggerfallLoot, Fade> fades = new Dictionary<DaggerfallLoot, Fade>();
        float nextScan;

        // Called by quick loot when it leaves items behind and closes the window.
        public static void MarkLeftover(DaggerfallLoot loot)
        {
            if (loot != null)
                leftover[loot] = Time.time;
        }

        void Update()
        {
            if (JunkStore.S == null)
                return;

            if (!JunkStore.S.corpseCleanup)
            {
                CancelAll();
                return;
            }

            if (Time.time >= nextScan)
            {
                nextScan = Time.time + 0.5f;
                Scan();
            }

            Advance();
        }

        void Scan()
        {
            DaggerfallLoot[] all = FindObjectsOfType<DaggerfallLoot>();
            for (int i = 0; i < all.Length; i++)
            {
                DaggerfallLoot loot = all[i];
                if (loot == null || loot.ContainerType != LootContainerTypes.CorpseMarker || loot.corpseQuestUID != 0 || loot.playerOwned)
                    continue;

                if (loot == OpenLoot)
                {
                    Cancel(loot);
                    leftover.Remove(loot);
                    continue;
                }
                if (fades.ContainsKey(loot))
                    continue;

                float start = 0f;
                float delay = 0f;
                if (leftover.TryGetValue(loot, out start))
                    delay = LeftoverDelay;
                else if (loot.Items.Count == 0)
                {
                    start = Time.time;
                    delay = EmptyDelay;
                }
                if (delay <= 0f)
                    continue;

                Fade f = new Fade();
                f.start = start;
                f.end = start + delay;
                f.renderer = loot.GetComponentInChildren<MeshRenderer>();
                fades[loot] = f;
            }
        }

        void Advance()
        {
            List<DaggerfallLoot> done = null;
            foreach (KeyValuePair<DaggerfallLoot, Fade> pair in fades)
            {
                DaggerfallLoot loot = pair.Key;
                Fade f = pair.Value;
                if (loot == null)
                {
                    if (done == null)
                        done = new List<DaggerfallLoot>();
                    done.Add(loot);
                    continue;
                }

                float t = (Time.time - f.start) / (f.end - f.start);
                if (t >= 1f)
                {
                    leftover.Remove(loot);
                    Destroy(loot.gameObject);
                    if (done == null)
                        done = new List<DaggerfallLoot>();
                    done.Add(loot);
                    continue;
                }
                SetAlpha(f, 1f - Mathf.Clamp01(t));
            }

            if (done != null)
            {
                for (int i = 0; i < done.Count; i++)
                    fades.Remove(done[i]);
            }
        }

        // DFU's billboard shader only cuts out pixels, so a blendable lit material takes over while fading.
        static void SetAlpha(Fade f, float alpha)
        {
            if (f.renderer == null)
                return;
            if (f.blended == null)
            {
                Shader shader = Shader.Find("Standard");
                Material src = f.renderer.sharedMaterial;
                if (shader == null || src == null)
                    return;
                f.original = src;
                Material m = new Material(shader);
                m.mainTexture = src.mainTexture;
                m.mainTextureScale = src.mainTextureScale;
                m.mainTextureOffset = src.mainTextureOffset;
                m.SetFloat("_Mode", 2f);
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
                m.SetFloat("_Glossiness", 0f);
                m.SetFloat("_Metallic", 0f);
                f.blended = m;
                f.renderer.sharedMaterial = m;
                f.renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            f.blended.color = new Color(1f, 1f, 1f, alpha);
        }

        void Cancel(DaggerfallLoot loot)
        {
            Fade f;
            if (!fades.TryGetValue(loot, out f))
                return;
            if (f.renderer != null && f.original != null)
            {
                f.renderer.sharedMaterial = f.original;
                f.renderer.shadowCastingMode = ShadowCastingMode.On;
            }
            fades.Remove(loot);
        }

        void CancelAll()
        {
            if (fades.Count == 0)
                return;
            List<DaggerfallLoot> keys = new List<DaggerfallLoot>(fades.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] != null)
                    Cancel(keys[i]);
                else
                    fades.Remove(keys[i]);
            }
        }
    }

}
