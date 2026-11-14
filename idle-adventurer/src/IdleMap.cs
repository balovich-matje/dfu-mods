// Idle Adventurer - destinations: one real dungeon of each type, the nearest to the hero's home town, and the
// screen for choosing between them.

using System;
using System.Collections.Generic;
using UnityEngine;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace IdleAdventurer
{
    public class IdlePlace
    {
        public int type;
        public string name;
        public string region;
        public int travelSeconds;
    }

    public static class IdlePlaces
    {
        const string HomeRegion = "Daggerfall";
        const string HomeTown = "Daggerfall";
        static IdlePlace[] places;

        // Gives a hero whose destination has no name yet (a new hero, an older save) the real dungeon of its type.
        public static void Name(IdleWorld w)
        {
            IdleSave s = w.s;
            if (!string.IsNullOrEmpty(s.destName))
                return;
            IdlePlace p = All()[Mathf.Clamp(s.dungeonType, 0, IdleData.DungeonTypes - 1)];
            s.destName = p.name;
            if (s.phase == IdleData.PhaseTown)
                s.travelSeconds = p.travelSeconds;
        }

        // One entry per dungeon type, in type order. Found once from the game's map data.
        public static IdlePlace[] All()
        {
            if (places != null)
                return places;
            IdlePlace[] found = new IdlePlace[IdleData.DungeonTypes];
            float[] best = new float[IdleData.DungeonTypes];
            try
            {
                MapsFile maps = DaggerfallUnity.Instance.ContentReader.MapFileReader;
                DFPosition home = new DFPosition(207, 213);
                int homeRegion = maps.GetRegionIndex(HomeRegion);
                if (homeRegion >= 0)
                {
                    DFRegion r = maps.GetRegion(homeRegion);
                    for (int i = 0; i < r.LocationCount; i++)
                        if (r.MapNames[i] == HomeTown)
                            home = MapsFile.LongitudeLatitudeToMapPixel(r.MapTable[i].Longitude, r.MapTable[i].Latitude);
                }

                for (int region = 0; region < maps.RegionCount; region++)
                {
                    DFRegion r = maps.GetRegion(region);
                    if (r.MapTable == null || r.MapNames == null)
                        continue;
                    for (int i = 0; i < r.LocationCount && i < r.MapTable.Length && i < r.MapNames.Length; i++)
                    {
                        int type = (int)r.MapTable[i].DungeonType;
                        if (type < 0 || type >= IdleData.DungeonTypes || string.IsNullOrEmpty(r.MapNames[i]))
                            continue;
                        DFPosition at = MapsFile.LongitudeLatitudeToMapPixel(r.MapTable[i].Longitude, r.MapTable[i].Latitude);
                        float far = Mathf.Sqrt((at.X - home.X) * (at.X - home.X) + (at.Y - home.Y) * (at.Y - home.Y));
                        if (found[type] != null && far >= best[type])
                            continue;
                        IdlePlace place = new IdlePlace();
                        place.type = type;
                        place.name = r.MapNames[i];
                        place.region = maps.GetRegionName(region);
                        place.travelSeconds = Mathf.Clamp(12 + (int)(Mathf.Sqrt(far) * 8f), 12, 90);
                        found[type] = place;
                        best[type] = far;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[Idle] MAP could not read the map: " + ex.Message);
            }

            // A type with no dungeon on the map still gets an entry, without a name.
            for (int type = 0; type < found.Length; type++)
            {
                if (found[type] != null)
                    continue;
                IdlePlace place = new IdlePlace();
                place.type = type;
                place.name = "";
                place.region = "";
                place.travelSeconds = IdleData.TravelSeconds;
                found[type] = place;
            }
            places = found;
            return places;
        }
    }

    public class IdleMapWindow : DaggerfallPopupWindow
    {
        const int BtnGo = 1, BtnBack = 2;
        static readonly string[] metals = { "iron", "steel", "silver", "elven", "dwarven", "mithril", "adamantium", "ebony", "orcish", "daedric" };

        MapCanvas canvas;
        int cursor;
        string note = "";

        public IdleMapWindow(IUserInterfaceManager uiManager, IUserInterfaceWindow previous)
            : base(uiManager, previous)
        {
        }

        protected override void Setup()
        {
            NativePanel.BackgroundColor = Color.clear;
            canvas = new MapCanvas();
            canvas.Owner = this;
            canvas.AutoSize = AutoSizeModes.None;
            ParentPanel.Components.Add(canvas);
            if (IdleStore.W != null)
                cursor = Mathf.Clamp(IdleStore.W.s.dungeonType, 0, IdleData.DungeonTypes - 1);
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
            canvas.Size = new Vector2(Screen.width, Screen.height);
            IdlePlace[] places = IdlePlaces.All();

            int click = canvas.Buttons.Clicked();
            if (click >= 0)
                DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);
            if (click < 0 && Input.GetMouseButtonDown(0))
            {
                int row = canvas.RowAt(IdleUi.Mouse);
                if (row >= 0)
                    cursor = row;
            }
            if (Input.GetKeyDown(KeyCode.DownArrow))
                cursor++;
            if (Input.GetKeyDown(KeyCode.UpArrow))
                cursor--;
            cursor = Mathf.Clamp(cursor, 0, places.Length - 1);

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || click == BtnGo)
            {
                IdlePlace p = places[cursor];
                IdleSim.SetDestination(w, p.type, p.name, p.travelSeconds);
                note = (w.s.nextType >= 0)
                    ? "The hero is away. " + Title(p) + " is where the next expedition goes."
                    : Title(p) + " is now the destination.";
                IdleStore.Save();
            }
            if (click == BtnBack)
            {
                CloseWindow();
                return;
            }
            IdleClock.Tick();
        }

        static string Title(IdlePlace p)
        {
            return string.IsNullOrEmpty(p.name) ? "The " + IdleData.DungeonNames[p.type].ToLower() : p.name;
        }

        class MapCanvas : BaseScreenComponent
        {
            public IdleMapWindow Owner;
            public readonly IdleButtons Buttons = new IdleButtons();
            readonly List<Rect> rowPlaces = new List<Rect>();

            public int RowAt(Vector2 mouse)
            {
                for (int i = 0; i < rowPlaces.Count; i++)
                    if (rowPlaces[i].Contains(mouse))
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
                IdlePlace[] places = IdlePlaces.All();
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
                float listW = Mathf.Round(width * 0.56f);
                float cardX = r.x + pad + listW + pad;
                float cardW = r.xMax - pad - cardX;

                // ---- the list
                float y = top;
                IdleUi.Text("Destination", r.x + pad, y, sc * 1.4f, IdleUi.Gold);
                y += line * 1.7f;
                float x = r.x + pad;
                float colType = x + listW * 0.47f;
                float colTime = x + listW * 0.78f;
                float colDeep = x + listW * 0.9f;
                IdleUi.Text("Dungeon", x + line * 0.4f, y, sc * 0.85f, IdleUi.Muted);
                IdleUi.Text("Kind", colType, y, sc * 0.85f, IdleUi.Muted);
                IdleUi.Text("Travel", colTime, y, sc * 0.85f, IdleUi.Muted);
                IdleUi.Text("Cleared", colDeep, y, sc * 0.85f, IdleUi.Muted);
                y += line * 1.1f;

                rowPlaces.Clear();
                float rowTall = Mathf.Floor(Mathf.Min(line * 1.15f, (bottom - line * 0.5f - y) / places.Length));
                float rsc = IdleUi.Scale(rowTall / 1.15f);
                for (int i = 0; i < places.Length; i++)
                {
                    IdlePlace p = places[i];
                    Rect row = new Rect(x, y, listW, rowTall);
                    rowPlaces.Add(row);
                    if (i == Owner.cursor)
                    {
                        IdleUi.Box(row, new Color(0.72f, 0.58f, 0.18f, 0.28f));
                        IdleUi.Box(new Rect(row.x, row.y, 3f, row.height), IdleUi.Line);
                    }
                    else if (row.Contains(IdleUi.Mouse))
                    {
                        IdleUi.Box(row, new Color(1f, 1f, 1f, 0.06f));
                    }
                    float ty = row.y + (row.height - row.height * 0.74f) / 2f;
                    bool current = p.type == s.dungeonType;
                    bool next = p.type == s.nextType;
                    string name = Title(p);
                    while (name.Length > 6 && x + line * 0.4f + IdleUi.Width(name, rsc) > colType - line * 0.3f)
                        name = name.Substring(0, name.Length - 4) + "..";
                    IdleUi.Text(name, x + line * 0.4f, ty, rsc, current ? IdleUi.Good : next ? IdleUi.Blue : IdleUi.White);
                    IdleUi.Text(IdleData.DungeonNames[p.type], colType, ty, rsc * 0.9f, IdleUi.Muted);
                    IdleUi.Text(p.travelSeconds + " s", colTime, ty, rsc * 0.9f, IdleUi.Muted);
                    int deep = s.clearedFloors[p.type];
                    IdleUi.Text((deep > 0) ? "floor " + deep : "-", colDeep, ty, rsc * 0.9f, (deep > 0) ? IdleUi.Gold : IdleUi.Muted);
                    y += rowTall;
                }

                // ---- what lives there
                IdlePlace sel = places[Mathf.Clamp(Owner.cursor, 0, places.Length - 1)];
                Rect card = new Rect(cardX, top, cardW, bottom - line * 0.5f - top);
                IdleUi.Box(card, new Color(1f, 1f, 1f, 0.03f));
                float cx = card.x + line * 0.5f;
                float cy = card.y + line * 0.4f;
                IdleUi.Text(Title(sel), cx, cy, sc * 1.1f, IdleUi.Gold);
                cy += line * 1.3f;
                IdleUi.Text(IdleData.DungeonNames[sel.type] + (string.IsNullOrEmpty(sel.region) ? "" : " in " + sel.region) + ", " + sel.travelSeconds + " s each way",
                    cx, cy, sc * 0.9f, IdleUi.Muted);
                cy += line * 1.4f;
                IdleUi.Text("Who lives there, shallow to deep", cx, cy, sc * 0.9f, IdleUi.White);
                cy += line * 1.1f;

                // The encounter table in order, each enemy once, with what it takes to hurt it.
                int heroMetal = -1;
                DaggerfallUnityItem weapon = w.gear[IdleData.SlotWeapon];
                if (weapon != null && IdleSim.Works(weapon) && !IdleSim.IsBow(weapon))
                    heroMetal = weapon.NativeMaterialValue;
                MobileTypes[] table = IdleSim.Table(sel.type);
                List<int> seen = new List<int>();
                for (int i = 0; i < table.Length && cy < card.yMax - line * 2.2f; i++)
                {
                    int id = (int)table[i];
                    if (seen.Contains(id))
                        continue;
                    seen.Add(id);
                    EnemyInfo e = IdleSim.Enemy(id);
                    int need = (int)e.def.MinMetalToHit;
                    string text = "Floor " + Mathf.Max(1, i) + "+   " + e.name + (e.isClass ? "" : "  (level " + e.def.Level + ")");
                    IdleUi.Text(text, cx, cy, sc * 0.85f, IdleUi.White);
                    if (need > 0 && need < metals.Length)
                    {
                        bool can = heroMetal >= need;
                        string warn = "needs " + metals[need] + " or better";
                        IdleUi.Text(warn, card.xMax - line * 0.5f - IdleUi.Width(warn, sc * 0.8f), cy, sc * 0.8f, can ? IdleUi.Muted : IdleUi.Bad);
                    }
                    cy += line * 0.92f;
                }
                IdleUi.Text("Red: the hero's weapon cannot hurt it, so it is fists only.", cx, card.yMax - line * 1.4f, sc * 0.8f, IdleUi.Muted);

                // ---- buttons
                float gap = Mathf.Round(line * 0.3f);
                float bx = r.x + pad;
                Buttons.Begin();
                bx += Buttons.Add(BtnGo, "Go here (Enter)", bx, bottom, tall, false) + gap;
                bx += Buttons.Add(BtnBack, "Back (Esc)", bx, bottom, tall, false) + gap * 3f;
                IdleUi.Text(Owner.note, bx, bottom + (tall - line * 0.74f) / 2f, sc, IdleUi.White);
            }
        }
    }
}
