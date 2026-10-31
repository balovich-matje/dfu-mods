// Junk & Loot Filter - a Daggerfall Unity mod.
// In-world parts: Skyrim-style vitals bars, effect names and borders on the HUD, and corpse clean-up.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace JunkLootFilter
{
    // ---------------------------------------------------------------------------------------
    // Vitals bars: magicka on the left, health in the centre, stamina on the right, along the bottom.
    // ---------------------------------------------------------------------------------------

    public class HudBars : Panel
    {
        class Bar
        {
            public Panel frame;
            public Panel fill;
            public TextLabel text;
            public float shown = 1f;
            public string lastText = string.Empty;
            public float lastScale;
        }

        static readonly Color FrameColor = new Color(0.03f, 0.03f, 0.03f, 0.72f);
        static readonly Color EdgeColor = new Color(0.62f, 0.56f, 0.42f, 0.95f);
        static readonly Color RedBar = new Color(0.72f, 0.10f, 0.10f, 1f);
        static readonly Color GreenBar = new Color(0.22f, 0.60f, 0.20f, 1f);
        static readonly Color BlueBar = new Color(0.18f, 0.32f, 0.82f, 1f);

        Bar health = new Bar();
        Bar fatigue = new Bar();
        Bar magicka = new Bar();

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
            b.frame = new Panel();
            b.frame.AutoSize = AutoSizeModes.None;
            b.frame.BackgroundColor = FrameColor;
            b.frame.Outline.Enabled = true;
            b.frame.Outline.Color = EdgeColor;

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
            b.frame.Position = new Vector2(x, y);
            b.frame.Size = new Vector2(w, h);

            float target = (max > 0) ? Mathf.Clamp01(cur / (float)max) : 0f;
            b.shown = Mathf.MoveTowards(b.shown, target, Time.unscaledDeltaTime * 0.9f);

            // anchor: 0 shrinks towards the middle, 1 anchors on the right (left bar), -1 on the left (right bar).
            float inner = w - 4f;
            float fillW = inner * b.shown;
            float fillX = 2f;
            if (anchor == 0)
                fillX += (inner - fillW) * 0.5f;
            else if (anchor > 0)
                fillX += inner - fillW;
            b.fill.Position = new Vector2(fillX, 2f);
            b.fill.Size = new Vector2(fillW, h - 4f);
            b.fill.BackgroundColor = color;

            string t = cur + " / " + max;
            if (t != b.lastText)
            {
                b.lastText = t;
                b.text.Text = t;
            }
            float scale = (h * 0.8f) / Mathf.Max(1, DaggerfallUI.DefaultFont.GlyphHeight);
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

            float barH = Mathf.Clamp(h * 0.03f, 14f, 44f);
            float y = h - h * 0.035f - barH;
            float sideW = w * 0.22f;
            float midW = w * 0.30f;
            float gap = w * 0.015f;
            float midX = (w - midW) * 0.5f;

            bool swap = DaggerfallUnity.Settings.SwapHealthAndFatigueColors;
            Place(health, midX, y, midW, barH, p.CurrentHealth, p.MaxHealth, 0, swap ? GreenBar : RedBar);
            Place(magicka, midX - gap - sideW, y, sideW, barH, p.CurrentMagicka, p.MaxMagicka, 1, BlueBar);
            Place(fatigue, midX + midW + gap, y, sideW, barH,
                p.CurrentFatigue / DaggerfallEntity.FatigueMultiplier, p.MaxFatigue / DaggerfallEntity.FatigueMultiplier,
                -1, swap ? RedBar : GreenBar);

            base.Update();
        }
    }

    // ---------------------------------------------------------------------------------------
    // Keeps the bars attached to the HUD, hides DFU's own bars, and decorates the active effect icons.
    // ---------------------------------------------------------------------------------------

    public class HudTweaks : MonoBehaviour
    {
        const int IconsPerRow = 6;
        const float SlotWidth = 46f;
        const float RowHeight = 32f;
        const float IconSize = 18f;

        static readonly Color BuffBorder = new Color(0.35f, 0.85f, 0.35f, 0.85f);
        static readonly Color DebuffBorder = new Color(0.90f, 0.25f, 0.22f, 0.85f);

        HudBars bars;
        DaggerfallHUD barsHud;
        readonly Dictionary<Panel, TextLabel> labels = new Dictionary<Panel, TextLabel>();
        readonly Dictionary<string, bool> harmfulByName = new Dictionary<string, bool>();

        void LateUpdate()
        {
            if (JunkStore.S == null || DaggerfallUI.Instance == null)
                return;
            DaggerfallHUD hud = DaggerfallUI.Instance.DaggerfallHUD;
            if (hud == null || GameManager.Instance.PlayerEntity == null)
                return;

            UpdateBars(hud);
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
                l.TextScale = 0.55f;
                l.HorizontalAlignment = HorizontalAlignment.Center;
                l.ShadowPosition = new Vector2(0.5f, 0.5f);
                l.ShadowColor = new Color(0f, 0f, 0f, 0.95f);
                l.TextColor = new Color(0.97f, 0.95f, 0.88f, 1f);
                labels[icon] = l;
            }
            return l;
        }

        void UpdateEffectIcons(DaggerfallHUD hud)
        {
            HUDActiveSpells spells = hud.ActiveSpells;
            if (spells == null)
                return;

            bool on = JunkStore.S.effectLabels;
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
                    Decorate(icon, false, false, string.Empty);
            }
            if (!on)
            {
                for (int i = 0; i < active.Count; i++)
                    Decorate(active[i], false, false, string.Empty);
                return;
            }

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

            for (int i = 0; i < active.Count; i++)
            {
                Panel icon = active[i];
                bool bad;
                if (!harmfulByName.TryGetValue(icon.ToolTipText, out bad))
                    bad = false;

                int slot = bad ? debuffs++ : buffs++;
                float rowStart = bad ? 8f + buffRows * RowHeight + (buffRows > 0 ? 6f : 0f) : 8f;
                icon.Size = new Vector2(IconSize, IconSize);
                icon.Position = new Vector2(8f + (slot % IconsPerRow) * SlotWidth, rowStart + (slot / IconsPerRow) * RowHeight);
                Decorate(icon, true, bad, icon.ToolTipText);
            }
        }

        void Decorate(Panel icon, bool show, bool bad, string name)
        {
            icon.Outline.Enabled = show;
            if (show)
            {
                icon.Outline.Color = bad ? DebuffBorder : BuffBorder;
                icon.Outline.Thickness = 2;
            }

            TextLabel label;
            if (!show)
            {
                if (labels.TryGetValue(icon, out label) && label.Text.Length > 0)
                    label.Text = string.Empty;
                return;
            }

            label = LabelFor(icon);
            string shown = name.Length > 15 ? name.Substring(0, 14) + "." : name;
            if (label.Text != shown)
                label.Text = shown;
            label.Position = new Vector2(0f, IconSize + 1f);
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
