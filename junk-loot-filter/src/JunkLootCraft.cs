// Junk & Loot Filter - a Daggerfall Unity mod.
// Modern spell crafting windows: the spell maker, the effect picker and the effect editor, plus the small widgets they share.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace JunkLootFilter
{
    // ---------------------------------------------------------------------------------------
    // Sizes, colours and small helpers for the crafting windows.
    // ---------------------------------------------------------------------------------------

    public class Metrics
    {
        public float W;
        public float H;
        public float Pad;
        public float RowH;
        public float Ts;
        public float Fs;
        public float B;

        public static Metrics Measure()
        {
            Metrics m = new Metrics();
            m.W = Screen.width;
            m.H = Screen.height;
            m.Pad = Mathf.Round(Mathf.Clamp(m.H * 0.012f, 6f, 16f));
            m.RowH = Mathf.Round(Mathf.Clamp(m.H * 0.036f, 20f, 40f));
            m.Ts = m.RowH * 0.74f / Skin.GlyphH;
            m.Fs = Mathf.Max(1, Mathf.RoundToInt(m.H / 700f));
            m.B = m.Fs * 2f + 2f;
            return m;
        }
    }

    public static class Ui
    {
        public static bool Shared
        {
            get { return JunkStore.S != null && JunkStore.S.uiStyle == 1; }
        }

        public static FramedPanel Frame(Panel parent)
        {
            FramedPanel f = new FramedPanel();
            f.AutoSize = AutoSizeModes.None;
            f.BackgroundColor = Skin.PanelFill;
            parent.Components.Add(f);
            return f;
        }

        // A dark veil over everything below this window (the popup base resets its own dim colour on every draw).
        public static Panel Veil(Panel parent, float alpha)
        {
            Panel p = new Panel();
            p.AutoSize = AutoSizeModes.None;
            p.BackgroundColor = new Color(0f, 0f, 0f, alpha);
            parent.Components.Add(p);
            return p;
        }

        public static void Place(BaseScreenComponent c, float x, float y, float w, float h)
        {
            c.Position = new Vector2(x, y);
            c.Size = new Vector2(w, h);
        }

        // Applies the chosen frame style: separate frames, or one translucent container behind them all.
        public static void Style(FramedPanel backdrop, FramedPanel[] frames, Rect union, Metrics m)
        {
            bool shared = Shared;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i].BackgroundColor = shared ? Skin.SharedFill : Skin.PanelFill;
                frames[i].FrameScale = m.Fs;
            }
            backdrop.FrameScale = m.Fs;
            backdrop.Enabled = shared;
            float p = shared ? m.Pad : 0f;
            Place(backdrop, union.x - p, union.y - p, union.width + 2f * p, union.height + 2f * p);
        }

        public static Button MakeButton(Panel parent, string text, Action action)
        {
            Button b = DaggerfallUI.AddButton(new Rect(0, 0, 10, 10), parent);
            b.BackgroundColor = Skin.ButtonFill;
            b.Outline.Enabled = true;
            b.Outline.Color = Skin.ButtonLine;
            b.Label.Text = text;
            b.Label.TextColor = Skin.Gold;
            b.Label.ShadowColor = Skin.Shadow;
            b.Label.HorizontalAlignment = HorizontalAlignment.Center;
            b.Label.VerticalAlignment = VerticalAlignment.Middle;
            b.OnMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
                action();
            };
            Button self = b;
            b.OnMouseEnter += delegate (BaseScreenComponent s) { self.BackgroundColor = Skin.ButtonHover; };
            b.OnMouseLeave += delegate (BaseScreenComponent s) { self.BackgroundColor = Skin.ButtonFill; };
            return b;
        }

        public static void SizeButton(Button b, float x, float y, float w, float h, float ts)
        {
            b.Position = new Vector2(x, y);
            b.Size = new Vector2(w, h);
            b.Label.TextScale = ts;
        }

        public static float TextButtonWidth(Button b, float ts, float pad)
        {
            return Mathf.Round(Skin.Width(b.Label.Text, ts) + pad * 3f);
        }

        public static Color SchoolColor(DFCareer.MagicSkills school)
        {
            switch (school)
            {
                case DFCareer.MagicSkills.Destruction: return new Color(0.91f, 0.36f, 0.30f, 1f);
                case DFCareer.MagicSkills.Restoration: return new Color(0.44f, 0.83f, 0.44f, 1f);
                case DFCareer.MagicSkills.Alteration: return new Color(0.35f, 0.66f, 0.90f, 1f);
                case DFCareer.MagicSkills.Illusion: return new Color(0.71f, 0.49f, 0.86f, 1f);
                case DFCareer.MagicSkills.Mysticism: return new Color(0.31f, 0.82f, 0.77f, 1f);
                case DFCareer.MagicSkills.Thaumaturgy: return new Color(0.94f, 0.64f, 0.23f, 1f);
            }
            return Skin.Muted;
        }

        public static string SchoolName(DFCareer.MagicSkills school)
        {
            return school == DFCareer.MagicSkills.None ? "Other" : school.ToString();
        }

        public static Color ElementColor(ElementTypes element)
        {
            switch (element)
            {
                case ElementTypes.Fire: return new Color(1f, 0.55f, 0.25f, 1f);
                case ElementTypes.Cold: return new Color(0.50f, 0.82f, 1f, 1f);
                case ElementTypes.Poison: return new Color(0.50f, 0.85f, 0.35f, 1f);
                case ElementTypes.Shock: return new Color(1f, 0.88f, 0.30f, 1f);
            }
            return new Color(0.75f, 0.55f, 1f, 1f);
        }

        // Plain text of a DFU text-token description.
        public static string TokensToText(TextFile.Token[] tokens)
        {
            if (tokens == null)
                return string.Empty;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < tokens.Length; i++)
            {
                TextFile.Token t = tokens[i];
                if (t.formatting == TextFile.Formatting.Text || t.formatting == TextFile.Formatting.TextHighlight)
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '\n' && sb[sb.Length - 1] != ' ')
                        sb.Append(' ');
                    sb.Append(t.text);
                }
                else if (t.formatting == TextFile.Formatting.NewLine || t.formatting == TextFile.Formatting.JustifyCenter || t.formatting == TextFile.Formatting.JustifyLeft)
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
                        sb.Append('\n');
                }
            }
            return sb.ToString().Trim();
        }

        // Splits text into lines no wider than maxW.
        public static List<string> Wrap(string text, float maxW, float scale)
        {
            List<string> lines = new List<string>();
            string[] paragraphs = (text ?? string.Empty).Split('\n');
            for (int p = 0; p < paragraphs.Length; p++)
            {
                string[] words = paragraphs[p].Split(' ');
                string line = string.Empty;
                for (int i = 0; i < words.Length; i++)
                {
                    string candidate = (line.Length == 0) ? words[i] : line + " " + words[i];
                    if (line.Length > 0 && Skin.Width(candidate, scale) > maxW)
                    {
                        lines.Add(line);
                        line = words[i];
                    }
                    else
                    {
                        line = candidate;
                    }
                }
                lines.Add(line);
            }
            return lines;
        }
    }

    // ---------------------------------------------------------------------------------------
    // Wrapped text that asks a callback for its content.
    // ---------------------------------------------------------------------------------------

    public class TextBlock : BaseScreenComponent
    {
        public Func<string> Provider;
        public Color Color = Skin.White;
        public float TextScale = 1.5f;
        public bool Center;

        string cachedText;
        float cachedWidth;
        float cachedScale;
        List<string> lines = new List<string>();

        public TextBlock()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled || Provider == null)
                return;
            Rect r = Rectangle;
            string text = Provider();
            if (text != cachedText || r.width != cachedWidth || TextScale != cachedScale)
            {
                cachedText = text;
                cachedWidth = r.width;
                cachedScale = TextScale;
                lines = Ui.Wrap(text, r.width, TextScale);
            }
            float lh = Mathf.Round(Skin.Height(TextScale) * 1.35f);
            for (int i = 0; i < lines.Count; i++)
            {
                float y = r.y + i * lh;
                if (y + lh > r.yMax + 1f)
                    break;
                float x = r.x;
                if (Center)
                    x = r.x + (r.width - Skin.Width(lines[i], TextScale)) / 2f;
                Skin.TextInRow(lines[i], x, y, lh, TextScale, Color);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // A number box with - and + at its ends. It edits one of DFU's own spinners, so every
    // rule the stock editor has (ranges, min/max pairs, cost updates) still applies.
    // ---------------------------------------------------------------------------------------

    public class NumField : BaseScreenComponent
    {
        public UpDownSpinner Spinner;
        public float TextScale = 1.5f;

        int heldDir;
        float nextStep;

        public NumField(UpDownSpinner spinner)
        {
            Spinner = spinner;
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            OnMouseDown += delegate (BaseScreenComponent s, Vector2 p)
            {
                int d = DirAtMouse();
                if (d != 0 && Spinner.Enabled)
                {
                    Step(d);
                    heldDir = d;
                    nextStep = Time.unscaledTime + 0.4f;
                }
            };
            OnMouseScrollUp += delegate (BaseScreenComponent s) { if (Spinner.Enabled) Step(1); };
            OnMouseScrollDown += delegate (BaseScreenComponent s) { if (Spinner.Enabled) Step(-1); };
        }

        void Step(int d)
        {
            Spinner.Value = Spinner.Value + d;
        }

        int DirAtMouse()
        {
            Vector2 m = Skin.MouseLocal(this);
            if (m.x < Size.x * 0.28f)
                return -1;
            if (m.x > Size.x * 0.72f)
                return 1;
            return 0;
        }

        public override void Update()
        {
            base.Update();
            if (heldDir != 0)
            {
                if (!InputManager.Instance.GetMouseButton(0) || !MouseOverComponent || DirAtMouse() != heldDir)
                {
                    heldDir = 0;
                }
                else if (Time.unscaledTime >= nextStep)
                {
                    Step(heldDir);
                    nextStep = Time.unscaledTime + 0.07f;
                }
            }
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled)
                return;
            Rect r = Rectangle;
            bool on = Spinner.Enabled;
            float a = on ? 1f : 0.3f;
            Skin.Fill(r, new Color(1f, 1f, 1f, 0.06f * a));
            Color line = new Color(0.43f, 0.35f, 0.12f, a);
            Skin.Fill(new Rect(r.x, r.y, r.width, 1f), line);
            Skin.Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), line);
            Skin.Fill(new Rect(r.x, r.y, 1f, r.height), line);
            Skin.Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), line);
            Color arrow = new Color(0.72f, 0.58f, 0.18f, a);
            if (on && MouseOverComponent)
            {
                int d = DirAtMouse();
                if (d < 0)
                    Skin.Fill(new Rect(r.x + 1f, r.y + 1f, r.width * 0.28f, r.height - 2f), Skin.Hover);
                else if (d > 0)
                    Skin.Fill(new Rect(r.xMax - r.width * 0.28f - 1f, r.y + 1f, r.width * 0.28f, r.height - 2f), Skin.Hover);
            }
            Skin.TextInRow("-", r.x + r.width * 0.08f, r.y, r.height, TextScale, arrow);
            Skin.TextRight("+", r.xMax - r.width * 0.08f, r.y, r.height, TextScale, arrow);
            string v = on ? Spinner.Value.ToString() : "-";
            float w = Skin.Width(v, TextScale);
            Skin.TextInRow(v, r.x + (r.width - w) / 2f, r.y, r.height, TextScale, new Color(0.94f, 0.93f, 0.9f, a));
        }
    }

    // ---------------------------------------------------------------------------------------
    // A scrolling list of rows with a few text columns. Used for the picker windows and the enchantment lists.
    // ---------------------------------------------------------------------------------------

    public class TableRow
    {
        public string[] Cells;
        public Color Color = Skin.White;
        public Color[] CellColors;
        public object Tag;
        public DaggerfallUnityItem Item;
        public bool Checked;
        public bool Dim;

        public TableRow(object tag, Color color, params string[] cells)
        {
            Tag = tag;
            Color = color;
            Cells = cells;
        }
    }

    public class RowTable : BaseScreenComponent
    {
        public string[] Headers;
        public float[] Weights;
        public bool[] Right;
        public float RowH = 24f;
        public float TextScale = 1.5f;
        public string EmptyText = "Nothing here.";
        public bool ShowChecks;
        public bool ShowIcons;
        public List<TableRow> Rows = new List<TableRow>();
        public Action<TableRow> OnLeft;
        public Action<TableRow> OnRightClick;
        public Action<int> OnHeader;
        public int SortColumn = -1;
        public bool SortDescending;
        float[] colX = new float[1];

        int scroll;
        int hover = -1;

        public TableRow Hovered
        {
            get { return (hover >= 0 && hover < Rows.Count) ? Rows[hover] : null; }
        }

        float HeaderH
        {
            get { return (Headers != null) ? Mathf.Round(RowH * 0.95f) : 0f; }
        }

        int VisibleRows
        {
            get { return Mathf.Max(1, Mathf.FloorToInt((Size.y - HeaderH) / RowH)); }
        }

        public RowTable()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            OnMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                if (PopupMenu.Blocking)
                    return;
                Vector2 m = Skin.MouseLocal(this);
                if (Headers != null && m.y < HeaderH)
                {
                    float mx = m.x + Rectangle.x;
                    for (int i = 0; i + 1 < colX.Length; i++)
                    {
                        if (mx >= colX[i] - 2f && mx < colX[i + 1] && OnHeader != null)
                        {
                            OnHeader(i);
                            return;
                        }
                    }
                    return;
                }
                TableRow row = Hovered;
                if (row != null && OnLeft != null)
                    OnLeft(row);
            };
            OnRightMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                TableRow row = Hovered;
                if (row != null && OnRightClick != null)
                    OnRightClick(row);
            };
            OnMouseScrollUp += delegate (BaseScreenComponent s) { ScrollBy(-2); };
            OnMouseScrollDown += delegate (BaseScreenComponent s) { ScrollBy(2); };
        }

        public void SetRows(List<TableRow> rows)
        {
            Rows = rows ?? new List<TableRow>();
            scroll = Mathf.Clamp(scroll, 0, Mathf.Max(0, Rows.Count - VisibleRows));
        }

        public void ResetScroll()
        {
            scroll = 0;
        }

        void ScrollBy(int rows)
        {
            scroll = Mathf.Clamp(scroll + rows, 0, Mathf.Max(0, Rows.Count - VisibleRows));
        }

        public override void Update()
        {
            base.Update();
            hover = -1;
            if (MouseOverComponent && !PopupMenu.Blocking)
            {
                Vector2 m = Skin.MouseLocal(this);
                if (m.y >= HeaderH)
                {
                    int idx = scroll + (int)((m.y - HeaderH) / RowH);
                    if (idx >= 0 && idx < Rows.Count && idx < scroll + VisibleRows)
                        hover = idx;
                }
            }
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled)
                return;
            Rect r = Rectangle;
            float pad = Mathf.Round(RowH * 0.22f);
            float barW = Mathf.Max(4f, Mathf.Round(RowH * 0.2f));
            float iconW = ShowIcons ? Mathf.Round(RowH * 1.25f) + pad : 0f;
            float checkW = (ShowChecks ? Mathf.Round(RowH * 0.9f) : 0f) + iconW;
            float inner = r.width - barW - 2f * pad - checkW;
            int n = (Weights != null) ? Weights.Length : 1;
            float[] x = new float[n + 1];
            float acc = r.x + pad + checkW;
            for (int i = 0; i < n; i++)
            {
                x[i] = acc;
                acc += inner * ((Weights != null) ? Weights[i] : 1f);
            }
            x[n] = acc;
            colX = x;

            float hh = HeaderH;
            if (Headers != null)
            {
                Color head = new Color(0.72f, 0.66f, 0.45f, 1f);
                for (int i = 0; i < n && i < Headers.Length; i++)
                    DrawCell(Headers[i] + ((SortColumn == i) ? (SortDescending ? " v" : " ^") : string.Empty), x, i, r.y, hh, head);
                Skin.Fill(new Rect(r.x + pad, r.y + hh - 1f, r.width - 2f * pad - barW, 1f), Skin.Line);
            }

            if (Rows.Count == 0)
            {
                Skin.TextInRow(EmptyText, r.x + pad, r.y + hh, RowH * 1.2f, TextScale, Skin.Muted);
                return;
            }

            int visible = VisibleRows;
            for (int i = 0; i < visible; i++)
            {
                int idx = scroll + i;
                if (idx >= Rows.Count)
                    break;
                TableRow row = Rows[idx];
                float y = r.y + hh + i * RowH;
                Rect rect = new Rect(r.x + 1f, y, r.width - 2f - barW, RowH);
                if (idx == hover && !row.Dim)
                    Skin.Fill(rect, Skin.Hover);
                else if (row.Checked)
                    Skin.Fill(rect, new Color(0.44f, 0.83f, 0.44f, 0.14f));
                else if ((idx & 1) == 1)
                    Skin.Fill(rect, Skin.Zebra);
                if (ShowChecks && row.Checked)
                    Skin.TextInRow("x", r.x + pad, y, RowH, TextScale, new Color(0.44f, 0.83f, 0.44f, 1f));
                if (ShowIcons && row.Item != null)
                    Skin.DrawItemIcon(new Rect(r.x + pad, y + 1f, iconW - pad, RowH - 2f), row.Item);
                for (int c = 0; c < n && c < row.Cells.Length; c++)
                {
                    Color col = (row.CellColors != null && c < row.CellColors.Length) ? row.CellColors[c] : row.Color;
                    if (row.Dim)
                        col = Skin.Muted;
                    DrawCell(row.Cells[c], x, c, y, RowH, col);
                }
            }

            if (Rows.Count > visible)
            {
                float trackH = visible * RowH;
                float trackY = r.y + hh;
                float thumbH = Mathf.Max(RowH, trackH * visible / Rows.Count);
                float thumbY = trackY + (trackH - thumbH) * scroll / Mathf.Max(1, Rows.Count - visible);
                Skin.Fill(new Rect(r.xMax - barW - 1f, trackY, barW, trackH), new Color(1f, 1f, 1f, 0.06f));
                Skin.Fill(new Rect(r.xMax - barW - 1f, thumbY, barW, thumbH), new Color(0.72f, 0.58f, 0.18f, 0.9f));
            }
        }

        void DrawCell(string text, float[] x, int col, float rowTop, float rowH, Color color)
        {
            float w = x[col + 1] - x[col] - 4f;
            string fit = Skin.Fit(text, w, TextScale);
            if (Right != null && col < Right.Length && Right[col])
                Skin.TextRight(fit, x[col] + w, rowTop, rowH, TextScale, color);
            else
                Skin.TextInRow(fit, x[col], rowTop, rowH, TextScale, color);
        }
    }

    // ---------------------------------------------------------------------------------------
    // A row of choices that wraps onto more lines when it has to. Used for the spell element and target.
    // ---------------------------------------------------------------------------------------

    public class ChipRow : BaseScreenComponent
    {
        public string[] Labels;
        public Color[] Colors;
        public Func<int, bool> Allowed;
        public Func<int> Selected;
        public Action<int> OnPick;
        public float TextScale = 1.5f;

        public ChipRow()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            OnMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                int i = IndexAt(Skin.MouseScreen());
                if (i >= 0 && (Allowed == null || Allowed(i)) && OnPick != null)
                    OnPick(i);
            };
        }

        Rect ChipRect(int i, Rect r)
        {
            float pad = Mathf.Round(Skin.Height(TextScale) * 0.5f);
            float h = Mathf.Round(Skin.Height(TextScale) * 1.7f);
            float gap = Mathf.Round(pad * 0.5f);
            float x = r.x;
            float y = r.y;
            for (int k = 0; k <= i; k++)
            {
                float w = Mathf.Round(Skin.Width(Labels[k], TextScale) + pad * 2f);
                if (x > r.x && x + w > r.xMax)
                {
                    x = r.x;
                    y += h + gap;
                }
                if (k == i)
                    return new Rect(x, y, w, h);
                x += w + gap;
            }
            return new Rect(r.x, r.y, 0, 0);
        }

        int IndexAt(Vector2 m)
        {
            if (Labels == null)
                return -1;
            Rect r = Rectangle;
            for (int i = 0; i < Labels.Length; i++)
            {
                if (ChipRect(i, r).Contains(m))
                    return i;
            }
            return -1;
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled || Labels == null)
                return;
            Rect r = Rectangle;
            int sel = (Selected != null) ? Selected() : -1;
            Vector2 m = Skin.MouseScreen();
            for (int i = 0; i < Labels.Length; i++)
            {
                Rect c = ChipRect(i, r);
                bool allowed = (Allowed == null || Allowed(i));
                Color accent = (Colors != null && i < Colors.Length) ? Colors[i] : Skin.Gold;
                if (!allowed)
                    accent = Skin.Muted;
                bool on = (i == sel);
                if (on)
                    Skin.Fill(c, new Color(accent.r, accent.g, accent.b, 0.25f));
                else if (allowed && c.Contains(m))
                    Skin.Fill(c, Skin.Hover);
                Color line = on ? accent : new Color(accent.r, accent.g, accent.b, 0.45f);
                Skin.Fill(new Rect(c.x, c.y, c.width, 1f), line);
                Skin.Fill(new Rect(c.x, c.yMax - 1f, c.width, 1f), line);
                Skin.Fill(new Rect(c.x, c.y, 1f, c.height), line);
                Skin.Fill(new Rect(c.xMax - 1f, c.y, 1f, c.height), line);
                Skin.TextInRow(Labels[i], c.x + (c.width - Skin.Width(Labels[i], TextScale)) / 2f, c.y, c.height, TextScale,
                    allowed ? (on ? accent : Skin.White) : new Color(Skin.Muted.r, Skin.Muted.g, Skin.Muted.b, 0.6f));
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // The effect cards on the spell maker: up to three, each with its school colour, name, cost and a remove mark.
    // ---------------------------------------------------------------------------------------

    public class EffectCards : BaseScreenComponent
    {
        public int Count = 3;
        public Func<int, string> Name;
        public Func<int, string> Detail;
        public Func<int, Color> SchoolColor;
        public Action<int> OnEdit;
        public Action<int> OnRemove;
        public float TextScale = 1.5f;
        public float CardH = 60f;
        public float Gap = 8f;

        public EffectCards()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            OnMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                Vector2 m = Skin.MouseScreen();
                Rect r = Rectangle;
                for (int i = 0; i < Count; i++)
                {
                    Rect c = Card(i, r);
                    if (!c.Contains(m) || string.IsNullOrEmpty(Name(i)))
                        continue;
                    Rect x = RemoveRect(c);
                    if (x.Contains(m))
                    {
                        if (OnRemove != null)
                            OnRemove(i);
                    }
                    else if (OnEdit != null)
                    {
                        OnEdit(i);
                    }
                    return;
                }
            };
        }

        Rect Card(int i, Rect r)
        {
            return new Rect(r.x, r.y + i * (CardH + Gap), r.width, CardH);
        }

        Rect RemoveRect(Rect c)
        {
            float s = Mathf.Round(c.height * 0.5f);
            return new Rect(c.xMax - s - 8f, c.y + (c.height - s) / 2f, s, s);
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled)
                return;
            Rect r = Rectangle;
            Vector2 m = Skin.MouseScreen();
            for (int i = 0; i < Count; i++)
            {
                Rect c = Card(i, r);
                string name = Name(i);
                bool filled = !string.IsNullOrEmpty(name);
                Color line = filled ? new Color(0.43f, 0.35f, 0.12f, 1f) : new Color(1f, 1f, 1f, 0.12f);
                Skin.Fill(c, (filled && c.Contains(m) && MouseOverComponent) ? Skin.Hover : Skin.Zebra);
                Skin.Fill(new Rect(c.x, c.y, c.width, 1f), line);
                Skin.Fill(new Rect(c.x, c.yMax - 1f, c.width, 1f), line);
                Skin.Fill(new Rect(c.x, c.y, 1f, c.height), line);
                Skin.Fill(new Rect(c.xMax - 1f, c.y, 1f, c.height), line);
                if (!filled)
                {
                    Skin.TextInRow("Empty effect slot", c.x + 16f, c.y, c.height, TextScale, Skin.Muted);
                    continue;
                }
                Skin.Fill(new Rect(c.x, c.y, 5f, c.height), SchoolColor(i));
                Rect x = RemoveRect(c);
                float tx = c.x + 18f;
                float tw = x.x - tx - 10f;
                Skin.TextInRow(Skin.Fit(name, tw, TextScale), tx, c.y, c.height * 0.55f, TextScale, Skin.Gold);
                Skin.TextInRow(Skin.Fit(Detail(i), tw, TextScale * 0.85f), tx, c.y + c.height * 0.45f, c.height * 0.5f, TextScale * 0.85f, Skin.Muted);
                bool overX = x.Contains(m) && MouseOverComponent;
                Skin.TextInRow("x", x.x + (x.width - Skin.Width("x", TextScale)) / 2f, x.y, x.height, TextScale,
                    overX ? new Color(1f, 0.45f, 0.4f, 1f) : Skin.Muted);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // The spell icon with arrows on either side.
    // ---------------------------------------------------------------------------------------

    public class IconBox : BaseScreenComponent
    {
        public Func<Texture2D> Icon;
        public Action OnPrev;
        public Action OnNext;
        public Action OnPick;
        public float TextScale = 1.5f;

        public IconBox()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
            OnMouseClick += delegate (BaseScreenComponent s, Vector2 p)
            {
                Vector2 m = Skin.MouseLocal(this);
                if (m.x < Size.x * 0.25f)
                {
                    if (OnPrev != null)
                        OnPrev();
                }
                else if (m.x > Size.x * 0.75f)
                {
                    if (OnNext != null)
                        OnNext();
                }
                else if (OnPick != null)
                {
                    OnPick();
                }
            };
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled)
                return;
            Rect r = Rectangle;
            float s = Mathf.Min(r.height, r.width * 0.5f);
            Rect box = new Rect(r.x + (r.width - s) / 2f, r.y + (r.height - s) / 2f, s, s);
            Skin.Fill(box, Skin.Zebra);
            Color line = (MouseOverComponent && box.Contains(Skin.MouseScreen())) ? Skin.Gold : new Color(0.43f, 0.35f, 0.12f, 1f);
            Skin.Fill(new Rect(box.x, box.y, box.width, 1f), line);
            Skin.Fill(new Rect(box.x, box.yMax - 1f, box.width, 1f), line);
            Skin.Fill(new Rect(box.x, box.y, 1f, box.height), line);
            Skin.Fill(new Rect(box.xMax - 1f, box.y, 1f, box.height), line);
            Texture2D tex = (Icon != null) ? Icon() : null;
            if (tex != null)
                DaggerfallUI.DrawTexture(new Rect(box.x + 3f, box.y + 3f, box.width - 6f, box.height - 6f), tex, ScaleMode.ScaleToFit, true);
            Skin.TextInRow("<", r.x + r.width * 0.06f, r.y, r.height, TextScale, Skin.Gold);
            Skin.TextRight(">", r.xMax - r.width * 0.06f, r.y, r.height, TextScale, Skin.Gold);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Spell maker: the window of the Mages Guild spellmaking service.
    // ---------------------------------------------------------------------------------------

    public class ModernSpellMakerWindow : DaggerfallSpellMakerWindow
    {
        static readonly TargetTypes[] TargetOrder =
        {
            TargetTypes.CasterOnly, TargetTypes.ByTouch, TargetTypes.SingleTargetAtRange, TargetTypes.AreaAroundCaster, TargetTypes.AreaAtRange
        };

        static readonly string[] TargetNames = { "Caster", "Touch", "At range", "Area: caster", "Area: range" };

        static readonly ElementTypes[] ElementOrder =
        {
            ElementTypes.Fire, ElementTypes.Cold, ElementTypes.Poison, ElementTypes.Shock, ElementTypes.Magic
        };

        static readonly string[] ElementNames = { "Fire", "Cold", "Poison", "Shock", "Magic" };

        FramedPanel backdrop;
        FramedPanel effectsFrame;
        FramedPanel costFrame;
        FramedPanel detailFrame;
        FramedPanel bottomFrame;
        EffectCards cards;
        Button addBtn;
        Button buyBtn;
        Button newBtn;
        Button exitBtn;
        TextBlock costText;
        Caption nameCaption;
        Caption nameLabel;
        Caption elementLabel;
        Caption targetLabel;
        Caption effectsTitle;
        ChipRow elementRow;
        ChipRow targetRow;
        IconBox iconBox;
        float lastW;
        float lastH;
        bool lastShared;

        public ModernSpellMakerWindow(IUserInterfaceManager uiManager, DaggerfallBaseWindow previous = null)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            base.Setup();

            NativePanel.BackgroundTexture = null;
            NativePanel.BackgroundColor = Color.clear;
            NativePanel.Components.Clear();
            ParentPanel.Components.Remove(NativePanel);

            backdrop = Ui.Frame(ParentPanel);
            backdrop.BackgroundColor = Skin.BackdropFill;
            effectsFrame = Ui.Frame(ParentPanel);
            costFrame = Ui.Frame(ParentPanel);
            detailFrame = Ui.Frame(ParentPanel);
            bottomFrame = Ui.Frame(ParentPanel);

            effectsTitle = new Caption();
            effectsTitle.Provider = delegate () { return "Spell effects"; };
            effectsFrame.Components.Add(effectsTitle);

            cards = new EffectCards();
            cards.Name = EffectName;
            cards.Detail = EffectDetail;
            cards.SchoolColor = EffectColor;
            cards.OnEdit = delegate (int slot) { editOrDeleteSlot = slot; EditButton_OnMouseClick(null, Vector2.zero); };
            cards.OnRemove = delegate (int slot) { editOrDeleteSlot = slot; DeleteButton_OnMouseClick(null, Vector2.zero); };
            effectsFrame.Components.Add(cards);

            addBtn = Ui.MakeButton(effectsFrame, "Add effect", delegate () { AddEffectButton_OnMouseClick(null, Vector2.zero); });
            addBtn.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.SpellMakerAddEffect);

            costText = new TextBlock();
            costText.Provider = CostText;
            costFrame.Components.Add(costText);

            nameLabel = new Caption();
            nameLabel.Provider = delegate () { return "Spell name"; };
            nameLabel.ColorProvider = delegate () { return Skin.Muted; };
            detailFrame.Components.Add(nameLabel);
            nameCaption = new Caption();
            nameCaption.Provider = delegate () { return string.IsNullOrEmpty(spellNameLabel.Text) ? "Click to name the spell" : spellNameLabel.Text; };
            nameCaption.ColorProvider = delegate () { return string.IsNullOrEmpty(spellNameLabel.Text) ? Skin.Muted : Skin.Gold; };
            detailFrame.Components.Add(nameCaption);
            nameCaption.OnMouseClick += delegate (BaseScreenComponent s, Vector2 p) { NameSpellButton_OnMouseClick(null, Vector2.zero); };

            elementLabel = new Caption();
            elementLabel.Provider = delegate () { return "Element"; };
            elementLabel.ColorProvider = delegate () { return Skin.Muted; };
            detailFrame.Components.Add(elementLabel);
            elementRow = new ChipRow();
            elementRow.Labels = ElementNames;
            elementRow.Colors = new Color[] { Ui.ElementColor(ElementTypes.Fire), Ui.ElementColor(ElementTypes.Cold), Ui.ElementColor(ElementTypes.Poison), Ui.ElementColor(ElementTypes.Shock), Ui.ElementColor(ElementTypes.Magic) };
            elementRow.Allowed = delegate (int i) { return (allowedElements & ElementOrder[i]) != ElementTypes.None; };
            elementRow.Selected = delegate () { return Array.IndexOf(ElementOrder, selectedElement); };
            elementRow.OnPick = delegate (int i) { SetSpellElement(ElementOrder[i]); DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick); };
            detailFrame.Components.Add(elementRow);

            targetLabel = new Caption();
            targetLabel.Provider = delegate () { return "Cast type"; };
            targetLabel.ColorProvider = delegate () { return Skin.Muted; };
            detailFrame.Components.Add(targetLabel);
            targetRow = new ChipRow();
            targetRow.Labels = TargetNames;
            targetRow.Allowed = delegate (int i) { return (allowedTargets & TargetOrder[i]) != TargetTypes.None; };
            targetRow.Selected = delegate () { return Array.IndexOf(TargetOrder, selectedTarget); };
            targetRow.OnPick = delegate (int i) { SetSpellTarget(TargetOrder[i]); DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick); };
            detailFrame.Components.Add(targetRow);

            iconBox = new IconBox();
            iconBox.Icon = delegate () { return DaggerfallUI.Instance.SpellIconCollection.GetSpellIcon(selectedIcon); };
            iconBox.OnPrev = delegate () { PreviousIconButton_OnMouseClick(null, Vector2.zero); };
            iconBox.OnNext = StepIconForward;
            iconBox.OnPick = delegate () { SelectIconButton_OnMouseClick(null, Vector2.zero); };
            detailFrame.Components.Add(iconBox);

            buyBtn = Ui.MakeButton(bottomFrame, "Buy", delegate () { BuyButton_OnMouseClick(null, Vector2.zero); });
            buyBtn.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.SpellMakerBuySpell);
            newBtn = Ui.MakeButton(bottomFrame, "New", delegate () { NewSpellButton_OnMouseClick(null, Vector2.zero); });
            newBtn.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.SpellMakerNewSpell);
            exitBtn = Ui.MakeButton(bottomFrame, "Exit", delegate () { CloseWindow(); });
            exitBtn.Hotkey = DaggerfallShortcut.GetBinding(DaggerfallShortcut.Buttons.SpellMakerExit);

            ParentPanel.Components.Add(NativePanel);
            lastW = -1f;
        }

        void StepIconForward()
        {
            int index = selectedIcon.index + 1;
            if (index >= DaggerfallUI.Instance.SpellIconCollection.GetIconCount(selectedIcon.key))
                index = 0;
            selectedIcon.index = index;
            SetIcon(selectedIcon);
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
        }

        // Pick an effect from the school cards instead of DFU's two list boxes.
        protected override void AddEffectButton_OnMouseClick(BaseScreenComponent sender, Vector2 position)
        {
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);

            if (GetFirstFreeEffectSlotIndex() == -1)
            {
                DaggerfallMessageBox mb = new DaggerfallMessageBox(
                    uiManager,
                    DaggerfallMessageBox.CommonMessageBoxButtons.Nothing,
                    DaggerfallUnity.Instance.TextProvider.GetRSCTokens(1707),
                    this);
                mb.ClickAnywhereToClose = true;
                mb.Show();
                return;
            }

            ModernEffectPickerWindow picker = new ModernEffectPickerWindow(uiManager, this);
            picker.OnPicked = delegate (IEntityEffect effect) { AddAndEditSlot(effect); };
            uiManager.PushWindow(picker);
        }

        string EffectName(int slot)
        {
            if (slot >= EffectEntries.Length || string.IsNullOrEmpty(EffectEntries[slot].Key))
                return string.Empty;
            IEntityEffect e = GameManager.Instance.EntityEffectBroker.GetEffectTemplate(EffectEntries[slot].Key);
            return (e != null) ? e.DisplayName : EffectEntries[slot].Key;
        }

        string EffectDetail(int slot)
        {
            if (string.IsNullOrEmpty(EffectEntries[slot].Key))
                return string.Empty;
            IEntityEffect e = GameManager.Instance.EntityEffectBroker.GetEffectTemplate(EffectEntries[slot].Key);
            string school = (e != null) ? Ui.SchoolName(e.Properties.MagicSkill) : string.Empty;
            var cost = FormulaHelper.CalculateEffectCosts(EffectEntries[slot]);
            return school + "  -  " + cost.spellPointCost + " magicka  -  click to edit";
        }

        Color EffectColor(int slot)
        {
            IEntityEffect e = GameManager.Instance.EntityEffectBroker.GetEffectTemplate(EffectEntries[slot].Key);
            return (e != null) ? Ui.SchoolColor(e.Properties.MagicSkill) : Skin.Muted;
        }

        string CostText()
        {
            PlayerEntity p = GameManager.Instance.PlayerEntity;
            return "Gold cost: " + TotalGoldCost + "\nGold held: " + p.GetGoldAmount() + "\nMagic cost: " + TotalSpellPointCost + "\nMax SP: " + p.MaxMagicka;
        }

        public override void Update()
        {
            base.Update();
            if (backdrop == null)
                return;
            Layout();
        }

        void Layout()
        {
            Metrics m = Metrics.Measure();
            bool shared = Ui.Shared;
            if (m.W == lastW && m.H == lastH && shared == lastShared)
                return;
            lastW = m.W;
            lastH = m.H;
            lastShared = shared;

            float inset = shared ? m.Pad : 0f;
            float w = Mathf.Round(Mathf.Min(m.W * 0.62f, m.H * 1.45f)) - 2f * inset;
            float h = Mathf.Round(m.H * 0.64f) - 2f * inset;
            float x0 = Mathf.Round((m.W - w) / 2f);
            float y0 = Mathf.Round((m.H - h) / 2f);
            float botH = Mathf.Round(m.RowH * 1.5f);
            float topH = h - botH - m.Pad;
            float leftW = Mathf.Round((w - m.Pad) * 0.56f);
            float rightW = w - m.Pad - leftW;
            float costH = Mathf.Round(m.RowH * 5.3f);
            float b = m.B;

            Ui.Place(effectsFrame, x0, y0, leftW, topH);
            Ui.Place(costFrame, x0 + leftW + m.Pad, y0, rightW, costH);
            Ui.Place(detailFrame, x0 + leftW + m.Pad, y0 + costH + m.Pad, rightW, topH - costH - m.Pad);
            Ui.Place(bottomFrame, x0, y0 + topH + m.Pad, w, botH);
            Ui.Style(backdrop, new FramedPanel[] { effectsFrame, costFrame, detailFrame, bottomFrame }, new Rect(x0, y0, w, h), m);

            effectsTitle.TextScale = m.Ts;
            Ui.Place(effectsTitle, b + m.Pad, b, leftW, m.RowH);
            cards.TextScale = m.Ts;
            cards.CardH = Mathf.Round(m.RowH * 1.9f);
            cards.Gap = m.Pad;
            Ui.Place(cards, b + m.Pad, b + m.RowH + m.Pad * 0.5f, leftW - 2f * b - 2f * m.Pad, 3f * cards.CardH + 2f * cards.Gap);
            float btnH = Mathf.Round(m.RowH * 1.2f);
            Button add = addBtn;
            float aw = Ui.TextButtonWidth(add, m.Ts, m.Pad);
            Ui.SizeButton(add, b + m.Pad, topH - b - m.Pad - btnH, aw, btnH, m.Ts);

            costText.TextScale = m.Ts;
            Ui.Place(costText, b + m.Pad, b + m.Pad * 0.5f, rightW - 2f * b - 2f * m.Pad, costH - 2f * b);

            float dx = b + m.Pad;
            float dw = rightW - 2f * dx;
            float y = b + m.Pad * 0.5f;
            nameLabel.TextScale = m.Ts * 0.85f;
            Ui.Place(nameLabel, dx, y, dw, m.RowH * 0.8f);
            y += m.RowH * 0.8f;
            nameCaption.TextScale = m.Ts;
            Ui.Place(nameCaption, dx, y, dw, m.RowH);
            y += m.RowH + m.Pad;
            elementLabel.TextScale = m.Ts * 0.85f;
            Ui.Place(elementLabel, dx, y, dw, m.RowH * 0.8f);
            y += m.RowH * 0.8f;
            elementRow.TextScale = m.Ts * 0.9f;
            Ui.Place(elementRow, dx, y, dw, m.RowH * 2.2f);
            y += m.RowH * 1.3f + m.Pad;
            targetLabel.TextScale = m.Ts * 0.85f;
            Ui.Place(targetLabel, dx, y, dw, m.RowH * 0.8f);
            y += m.RowH * 0.8f;
            targetRow.TextScale = m.Ts * 0.9f;
            Ui.Place(targetRow, dx, y, dw, m.RowH * 2.6f);
            float iconH = Mathf.Round(m.RowH * 1.9f);
            iconBox.TextScale = m.Ts;
            Ui.Place(iconBox, rightW - b - m.Pad - iconH * 2.2f, b + m.Pad * 0.5f, iconH * 2.2f, iconH);

            float bh = botH - 2f * b - 2f;
            float by = Mathf.Round((botH - bh) / 2f);
            float bx = m.Pad;
            bx += Ui.TextButtonWidth(buyBtn, m.Ts, m.Pad) + m.Pad;
            Ui.SizeButton(buyBtn, m.Pad, by, bx - m.Pad * 2f, bh, m.Ts);
            float nw = Ui.TextButtonWidth(newBtn, m.Ts, m.Pad);
            Ui.SizeButton(newBtn, bx, by, nw, bh, m.Ts);
            float ew = Ui.TextButtonWidth(exitBtn, m.Ts, m.Pad);
            Ui.SizeButton(exitBtn, w - m.Pad - ew, by, ew, bh, m.Ts);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Add spell effect: every effect on offer, in one card per school of magic.
    // ---------------------------------------------------------------------------------------

    public class ModernEffectPickerWindow : DaggerfallPopupWindow
    {
        static readonly DFCareer.MagicSkills[] Schools =
        {
            DFCareer.MagicSkills.Alteration, DFCareer.MagicSkills.Destruction, DFCareer.MagicSkills.Illusion,
            DFCareer.MagicSkills.Mysticism, DFCareer.MagicSkills.Restoration, DFCareer.MagicSkills.Thaumaturgy
        };

        public Action<IEntityEffect> OnPicked;

        Panel veil;
        FramedPanel backdrop;
        FramedPanel[] heads = new FramedPanel[6];
        FramedPanel[] bodies = new FramedPanel[6];
        Caption[] titles = new Caption[6];
        RowTable[] tables = new RowTable[6];
        FramedPanel bottomFrame;
        Button cancelBtn;
        Caption hint;
        float lastW;
        float lastH;
        bool lastShared;

        public ModernEffectPickerWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            ParentPanel.Components.Remove(NativePanel);

            // Dark enough to hide the spell maker behind, so the cards read cleanly.
            veil = Ui.Veil(ParentPanel, 0.94f);
            backdrop = Ui.Frame(ParentPanel);
            backdrop.BackgroundColor = Skin.BackdropFill;
            bottomFrame = Ui.Frame(ParentPanel);

            // Collect every effect the spell maker offers, by school.
            List<IEntityEffect> all = new List<IEntityEffect>();
            string[] groups = GameManager.Instance.EntityEffectBroker.GetGroupNames(true, MagicCraftingStations.SpellMaker);
            for (int g = 0; g < groups.Length; g++)
                all.AddRange(GameManager.Instance.EntityEffectBroker.GetEffectTemplates(groups[g], MagicCraftingStations.SpellMaker));
            all.Sort(delegate (IEntityEffect a, IEntityEffect b) { return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); });

            for (int i = 0; i < 6; i++)
            {
                DFCareer.MagicSkills school = Schools[i];
                Color color = Ui.SchoolColor(school);
                heads[i] = Ui.Frame(ParentPanel);
                bodies[i] = Ui.Frame(ParentPanel);

                titles[i] = new Caption();
                string name = Ui.SchoolName(school);
                titles[i].Provider = delegate () { return name; };
                Color c = color;
                titles[i].ColorProvider = delegate () { return c; };
                titles[i].Align = 1;
                heads[i].Components.Add(titles[i]);

                tables[i] = new RowTable();
                tables[i].Weights = new float[] { 1f };
                tables[i].EmptyText = "No effects.";
                List<TableRow> rows = new List<TableRow>();
                for (int k = 0; k < all.Count; k++)
                {
                    if (all[k].Properties.MagicSkill == school)
                        rows.Add(new TableRow(all[k], Skin.White, all[k].DisplayName));
                }
                tables[i].SetRows(rows);
                tables[i].OnLeft = Picked;
                bodies[i].Components.Add(tables[i]);
            }

            hint = new Caption();
            hint.Provider = delegate () { return "Click an effect to add it to the spell."; };
            hint.ColorProvider = delegate () { return Skin.Muted; };
            bottomFrame.Components.Add(hint);
            cancelBtn = Ui.MakeButton(bottomFrame, "Cancel", delegate () { CloseWindow(); });

            ParentPanel.Components.Add(NativePanel);
            lastW = -1f;
        }

        void Picked(TableRow row)
        {
            IEntityEffect effect = row.Tag as IEntityEffect;
            DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            CloseWindow();
            if (effect != null && OnPicked != null)
                OnPicked(effect);
        }

        public override void Update()
        {
            base.Update();
            if (backdrop != null)
                Layout();
        }

        void Layout()
        {
            Metrics m = Metrics.Measure();
            bool shared = Ui.Shared;
            if (m.W == lastW && m.H == lastH && shared == lastShared)
                return;
            lastW = m.W;
            lastH = m.H;
            lastShared = shared;
            Ui.Place(veil, 0, 0, m.W, m.H);

            float inset = shared ? m.Pad : 0f;
            float w = Mathf.Round(Mathf.Min(m.W * 0.84f, m.H * 1.9f)) - 2f * inset;
            float h = Mathf.Round(m.H * 0.80f) - 2f * inset;
            float x0 = Mathf.Round((m.W - w) / 2f);
            float y0 = Mathf.Round((m.H - h) / 2f);
            float botH = Mathf.Round(m.RowH * 1.5f);
            float gridH = h - botH - m.Pad;
            float cw = Mathf.Floor((w - 2f * m.Pad) / 3f);
            float headH = Mathf.Round(m.RowH * 1.3f);
            float ch = Mathf.Floor((gridH - m.Pad) / 2f);
            float b = m.B;

            List<FramedPanel> frames = new List<FramedPanel>();
            for (int i = 0; i < 6; i++)
            {
                float cx = x0 + (i % 3) * (cw + m.Pad);
                float cy = y0 + (i / 3) * (ch + m.Pad);
                Ui.Place(heads[i], cx, cy, cw, headH);
                Ui.Place(bodies[i], cx, cy + headH + Mathf.Round(m.Pad * 0.5f), cw, ch - headH - Mathf.Round(m.Pad * 0.5f));
                titles[i].TextScale = m.Ts * 1.1f;
                Ui.Place(titles[i], 0, 0, cw, headH);
                tables[i].RowH = Mathf.Round(m.RowH * 0.95f);
                tables[i].TextScale = m.Ts * 0.95f;
                Ui.Place(tables[i], b, b, cw - 2f * b, ch - headH - Mathf.Round(m.Pad * 0.5f) - 2f * b);
                frames.Add(heads[i]);
                frames.Add(bodies[i]);
            }
            Ui.Place(bottomFrame, x0, y0 + gridH + m.Pad, w, botH);
            frames.Add(bottomFrame);
            Ui.Style(backdrop, frames.ToArray(), new Rect(x0, y0, w, h), m);

            hint.TextScale = m.Ts * 0.9f;
            Ui.Place(hint, m.Pad + b, 0, w * 0.6f, botH);
            float bh = botH - 2f * b - 2f;
            float cwb = Ui.TextButtonWidth(cancelBtn, m.Ts, m.Pad);
            Ui.SizeButton(cancelBtn, w - m.Pad - cwb, Mathf.Round((botH - bh) / 2f), cwb, bh, m.Ts);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Spell effect editor: duration, chance and magnitude of one effect.
    // ---------------------------------------------------------------------------------------

    public class ModernEffectEditorWindow : DaggerfallEffectSettingsEditorWindow
    {
        Panel veil;
        FramedPanel backdrop;
        FramedPanel titleFrame;
        FramedPanel gridFrame;
        FramedPanel bottomFrame;
        Caption title;
        TextBlock description;
        TextBlock costText;
        Button doneBtn;
        Caption[] headers = new Caption[5];
        Caption[] rowLabels = new Caption[3];
        Caption[] plus = new Caption[3];
        Caption[] per = new Caption[3];
        Caption[] results = new Caption[3];
        NumField[] fields = new NumField[11];
        string descText = string.Empty;
        float lastW;
        float lastH;
        bool lastShared;
        IEntityEffect lastTemplate;

        public ModernEffectEditorWindow(IUserInterfaceManager uiManager, DaggerfallBaseWindow previous = null)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            base.Setup();

            NativePanel.BackgroundTexture = null;
            NativePanel.BackgroundColor = Color.clear;
            NativePanel.Components.Clear();
            ParentPanel.Components.Remove(NativePanel);

            veil = Ui.Veil(ParentPanel, 0.92f);
            backdrop = Ui.Frame(ParentPanel);
            backdrop.BackgroundColor = Skin.BackdropFill;
            titleFrame = Ui.Frame(ParentPanel);
            gridFrame = Ui.Frame(ParentPanel);
            bottomFrame = Ui.Frame(ParentPanel);

            title = new Caption();
            title.Provider = delegate () { return (EffectTemplate != null) ? EffectTemplate.DisplayName : string.Empty; };
            titleFrame.Components.Add(title);
            description = new TextBlock();
            description.Provider = delegate () { return descText; };
            titleFrame.Components.Add(description);

            string[] heads = { "", "Base", "Bonus", "Per levels", "At your level" };
            for (int i = 0; i < 5; i++)
            {
                headers[i] = new Caption();
                string t = heads[i];
                headers[i].Provider = delegate () { return t; };
                headers[i].ColorProvider = delegate () { return Skin.Muted; };
                gridFrame.Components.Add(headers[i]);
            }
            string[] labels = { "Duration", "Chance", "Magnitude" };
            for (int i = 0; i < 3; i++)
            {
                rowLabels[i] = new Caption();
                string t = labels[i];
                rowLabels[i].Provider = delegate () { return t; };
                gridFrame.Components.Add(rowLabels[i]);
                plus[i] = new Caption();
                plus[i].Provider = delegate () { return "+"; };
                plus[i].ColorProvider = delegate () { return Skin.Muted; };
                plus[i].Align = 1;
                gridFrame.Components.Add(plus[i]);
                per[i] = new Caption();
                per[i].Provider = delegate () { return "per"; };
                per[i].ColorProvider = delegate () { return Skin.Muted; };
                per[i].Align = 1;
                gridFrame.Components.Add(per[i]);
                results[i] = new Caption();
                int row = i;
                results[i].Provider = delegate () { return ResultText(row); };
                results[i].ColorProvider = delegate () { return RowSupported(row) ? new Color(0.44f, 0.83f, 0.44f, 1f) : Skin.Muted; };
                gridFrame.Components.Add(results[i]);
            }

            UpDownSpinner[] spinners =
            {
                durationBaseSpinner, durationPlusSpinner, durationPerLevelSpinner,
                chanceBaseSpinner, chancePlusSpinner, chancePerLevelSpinner,
                magnitudeBaseMinSpinner, magnitudeBaseMaxSpinner, magnitudePlusMinSpinner, magnitudePlusMaxSpinner, magnitudePerLevelSpinner
            };
            for (int i = 0; i < spinners.Length; i++)
            {
                fields[i] = new NumField(spinners[i]);
                gridFrame.Components.Add(fields[i]);
            }

            costText = new TextBlock();
            costText.Provider = CostText;
            bottomFrame.Components.Add(costText);
            doneBtn = Ui.MakeButton(bottomFrame, "Done", delegate () { CloseWindow(); });

            ParentPanel.Components.Add(NativePanel);
            lastW = -1f;
            RefreshDescription();
        }

        public override void OnPush()
        {
            base.OnPush();
            RefreshDescription();
            lastW = -1f;
        }

        void RefreshDescription()
        {
            if (EffectTemplate != null)
                descText = Ui.TokensToText(EffectTemplate.SpellMakerDescription);
            lastTemplate = EffectTemplate;
        }

        bool RowSupported(int row)
        {
            if (EffectTemplate == null)
                return false;
            if (row == 0)
                return EffectTemplate.Properties.SupportDuration;
            if (row == 1)
                return EffectTemplate.Properties.SupportChance;
            return EffectTemplate.Properties.SupportMagnitude;
        }

        static int PerLevel(UpDownSpinner s)
        {
            return Mathf.Max(1, s.Value);
        }

        string ResultText(int row)
        {
            if (!RowSupported(row))
                return "not used";
            int level = GameManager.Instance.PlayerEntity.Level;
            if (row == 0)
                return (durationBaseSpinner.Value + durationPlusSpinner.Value * (level / PerLevel(durationPerLevelSpinner))).ToString();
            if (row == 1)
                return (chanceBaseSpinner.Value + chancePlusSpinner.Value * (level / PerLevel(chancePerLevelSpinner))).ToString() + " %";
            int steps = level / PerLevel(magnitudePerLevelSpinner);
            int lo = magnitudeBaseMinSpinner.Value + magnitudePlusMinSpinner.Value * steps;
            int hi = magnitudeBaseMaxSpinner.Value + magnitudePlusMaxSpinner.Value * steps;
            return lo + " - " + hi;
        }

        string CostText()
        {
            if (EffectTemplate == null)
                return string.Empty;
            var cost = FormulaHelper.CalculateEffectCosts(EffectEntry);
            return "This effect costs " + cost.spellPointCost + " magicka at level " + GameManager.Instance.PlayerEntity.Level;
        }

        public override void Update()
        {
            base.Update();
            if (backdrop == null)
                return;
            if (EffectTemplate != lastTemplate)
                RefreshDescription();
            Layout();
        }

        void Layout()
        {
            Metrics m = Metrics.Measure();
            bool shared = Ui.Shared;
            if (m.W == lastW && m.H == lastH && shared == lastShared)
                return;
            lastW = m.W;
            lastH = m.H;
            lastShared = shared;
            Ui.Place(veil, 0, 0, m.W, m.H);

            float inset = shared ? m.Pad : 0f;
            float w = Mathf.Round(Mathf.Min(m.W * 0.66f, m.H * 1.6f)) - 2f * inset;
            float titleH = Mathf.Round(m.RowH * 5.2f);
            float gridH = Mathf.Round(m.RowH * 5.6f);
            float botH = Mathf.Round(m.RowH * 1.5f);
            float h = titleH + gridH + botH + 2f * m.Pad;
            float x0 = Mathf.Round((m.W - w) / 2f);
            float y0 = Mathf.Round((m.H - h) / 2f);
            float b = m.B;

            Ui.Place(titleFrame, x0, y0, w, titleH);
            Ui.Place(gridFrame, x0, y0 + titleH + m.Pad, w, gridH);
            Ui.Place(bottomFrame, x0, y0 + titleH + gridH + 2f * m.Pad, w, botH);
            Ui.Style(backdrop, new FramedPanel[] { titleFrame, gridFrame, bottomFrame }, new Rect(x0, y0, w, h), m);

            title.TextScale = m.Ts * 1.15f;
            Ui.Place(title, b + m.Pad, b, w - 2f * (b + m.Pad), m.RowH * 1.2f);
            description.TextScale = m.Ts * 0.9f;
            Ui.Place(description, b + m.Pad, b + m.RowH * 1.3f, w - 2f * (b + m.Pad), titleH - m.RowH * 1.3f - b);

            // Columns: label, base, bonus, per levels, result. A pair of fields (magnitude) fits in each of base and bonus.
            float inner = w - 2f * (b + m.Pad);
            float gx = b + m.Pad;
            float sp = m.Pad * 0.5f;
            float gap = m.Pad * 0.4f;
            float c0 = Mathf.Round(inner * 0.15f);
            float linkW = Mathf.Round(m.Pad * 2.6f);
            float fw = Mathf.Round(Mathf.Min(m.RowH * 3.2f, (inner * 0.84f - c0 - 2f * gap - 6f * sp - 2f * linkW) / 5f));
            float cPair = 2f * fw + gap + 2f * sp;
            float cPer = fw + 2f * sp;
            float x1 = gx + c0;
            float x2 = x1 + cPair + linkW;
            float x3 = x2 + cPair + linkW;
            float x4 = x3 + cPer;
            float rowH = Mathf.Round(m.RowH * 1.45f);
            float fh = Mathf.Round(m.RowH * 1.1f);
            float hy = b;

            float[] xs = { gx, x1, x2, x3, x4 };
            for (int i = 0; i < 5; i++)
            {
                headers[i].TextScale = m.Ts * 0.85f;
                Ui.Place(headers[i], xs[i] + ((i > 0 && i < 4) ? sp : 0f), hy, inner, m.RowH * 0.9f);
            }
            for (int r = 0; r < 3; r++)
            {
                float y = hy + m.RowH * 0.95f + r * rowH + sp;
                rowLabels[r].TextScale = m.Ts;
                Ui.Place(rowLabels[r], gx, y, c0, fh);
                results[r].TextScale = m.Ts;
                Ui.Place(results[r], x4 + sp, y, inner - (x4 - gx), fh);
                plus[r].TextScale = m.Ts;
                Ui.Place(plus[r], x1 + cPair, y, linkW, fh);
                per[r].TextScale = m.Ts * 0.85f;
                Ui.Place(per[r], x2 + cPair, y, linkW, fh);
                float y2 = y;
                if (r < 2)
                {
                    Place(fields[r * 3], x1 + sp, y2, fw, fh, m);
                    Place(fields[r * 3 + 1], x2 + sp, y2, fw, fh, m);
                    Place(fields[r * 3 + 2], x3 + sp, y2, fw, fh, m);
                }
                else
                {
                    Place(fields[6], x1 + sp, y2, fw, fh, m);
                    Place(fields[7], x1 + sp + fw + gap, y2, fw, fh, m);
                    Place(fields[8], x2 + sp, y2, fw, fh, m);
                    Place(fields[9], x2 + sp + fw + gap, y2, fw, fh, m);
                    Place(fields[10], x3 + sp, y2, fw, fh, m);
                }
            }

            costText.TextScale = m.Ts * 0.95f;
            Ui.Place(costText, b + m.Pad, Mathf.Round((botH - Skin.Height(m.Ts)) / 2f), w * 0.65f, botH);
            float bh = botH - 2f * b - 2f;
            float dw = Ui.TextButtonWidth(doneBtn, m.Ts, m.Pad);
            Ui.SizeButton(doneBtn, w - m.Pad - dw, Mathf.Round((botH - bh) / 2f), dw, bh, m.Ts);
        }

        static void Place(NumField f, float x, float y, float w, float h, Metrics m)
        {
            f.TextScale = m.Ts;
            f.Position = new Vector2(x, y);
            f.Size = new Vector2(w, h);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Label and value pairs, one per line.
    // ---------------------------------------------------------------------------------------

    public class KeyValueList : BaseScreenComponent
    {
        public Func<List<string[]>> Provider;
        public float RowH = 24f;
        public float TextScale = 1.5f;

        public KeyValueList()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled || Provider == null)
                return;
            Rect r = Rectangle;
            List<string[]> rows = Provider();
            for (int i = 0; i < rows.Count; i++)
            {
                float y = r.y + i * RowH;
                if (i > 0)
                    Skin.Fill(new Rect(r.x, y, r.width, 1f), Skin.Line);
                Skin.TextInRow(rows[i][0], r.x, y, RowH, TextScale, Skin.Muted);
                Color c = Skin.White;
                if (rows[i].Length > 2 && rows[i][2] == "warn")
                    c = new Color(1f, 0.45f, 0.4f, 1f);
                else if (rows[i].Length > 2 && rows[i][2] == "gold")
                    c = Skin.Gold;
                Skin.TextRight(rows[i][1], r.xMax, y, RowH, TextScale, c);
            }
        }
    }

    // A horizontal bar of up to three segments out of a capacity.
    public class Meter : BaseScreenComponent
    {
        public Func<float> Used;
        public Func<float> Added;
        public Func<float> Capacity;

        public Meter()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled)
                return;
            Rect r = Rectangle;
            Skin.Fill(r, new Color(1f, 1f, 1f, 0.10f));
            float cap = Mathf.Max(1f, Capacity());
            float used = Mathf.Clamp(Used(), 0f, cap);
            Skin.Fill(new Rect(r.x, r.y, r.width * used / cap, r.height), new Color(0.72f, 0.58f, 0.18f, 1f));
            if (Added != null)
            {
                float add = Added();
                float from = Mathf.Clamp(used, 0f, cap);
                float to = Mathf.Clamp(used + add, 0f, cap);
                if (to < from)
                {
                    float t = from;
                    from = to;
                    to = t;
                }
                Skin.Fill(new Rect(r.x + r.width * from / cap, r.y, r.width * (to - from) / cap, r.height), new Color(0.44f, 0.83f, 0.44f, 1f));
            }
        }
    }

    // An item's icon in a box.
    public class ItemIconBox : BaseScreenComponent
    {
        public Func<DaggerfallUnityItem> Item;

        public ItemIconBox()
        {
            AutoSize = AutoSizeModes.None;
            BackgroundColor = Color.clear;
        }

        public override void Draw()
        {
            base.Draw();
            if (!Enabled)
                return;
            Rect r = Rectangle;
            Skin.Fill(r, Skin.Zebra);
            Color line = new Color(0.43f, 0.35f, 0.12f, 1f);
            Skin.Fill(new Rect(r.x, r.y, r.width, 1f), line);
            Skin.Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), line);
            Skin.Fill(new Rect(r.x, r.y, 1f, r.height), line);
            Skin.Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), line);
            DaggerfallUnityItem item = (Item != null) ? Item() : null;
            if (item != null)
            {
                ImageData img = DaggerfallUnity.Instance.ItemHelper.GetInventoryImage(item);
                if (img.texture != null)
                    DaggerfallUI.DrawTexture(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), img.texture, ScaleMode.ScaleToFit, true);
            }
        }
    }
}
